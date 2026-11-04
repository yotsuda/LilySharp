// Lily# - Music notation compiler
// Copyright (C) 2025-2026 Yoshifumi Tsuda
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using LilySharp.Core.Semantics;

namespace LilySharp.Core.MusicXmlImport;

/// <summary>
/// The dirty half of the importer: parses a MusicXML <c>score-partwise</c>
/// document (or an <c>.mxl</c> zip container) into the <see cref="ImportDocument"/>
/// IR. Divisions are normalized to fractions of a whole note; anything Tier 1 does
/// not yet cover (extra voices, mid-measure attribute churn) is warned, not
/// silently mangled.
/// </summary>
/// <remarks>
/// Element lookups go through <see cref="Local"/>/<see cref="Els"/> by LOCAL name,
/// so a file that namespaces its elements still reads.
/// </remarks>
internal static class MusicXmlReader
{
    /// <summary>Reads raw file bytes — an <c>.mxl</c> zip or a plain XML file —
    /// into the IR.</summary>
    public static ImportDocument ReadBytes(byte[] bytes, ImportReport report)
    {
        // .mxl is a ZIP; the real score is named by META-INF/container.xml.
        if (bytes.Length >= 2 && bytes[0] == 'P' && bytes[1] == 'K')
            return Read(UnzipMxl(bytes), report);
        // Strip a UTF-8 BOM if present so XDocument.Parse does not choke.
        int start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
        return Read(System.Text.Encoding.UTF8.GetString(bytes, start, bytes.Length - start), report);
    }

    /// <summary>Reads a MusicXML document string into the IR.</summary>
    public static ImportDocument Read(string xmlText, ImportReport report)
    {
        XDocument xml;
        try
        {
            xml = XDocument.Parse(xmlText);
        }
        catch (Exception ex)
        {
            throw new FormatException($"Not valid XML: {ex.Message}", ex);
        }

        var root = xml.Root
            ?? throw new FormatException("Empty MusicXML document.");
        if (root.Name.LocalName == "score-timewise")
            throw new FormatException("Timewise MusicXML is not supported; convert to score-partwise.");
        if (root.Name.LocalName != "score-partwise")
            throw new FormatException($"Not a MusicXML score (root element <{root.Name.LocalName}>).");

        var doc = new ImportDocument
        {
            Title = Local(Local(root, "work"), "work-title")?.Value.Trim()
                    ?? Local(root, "movement-title")?.Value.Trim(),
            Composer = Els(Local(root, "identification"), "creator")
                .FirstOrDefault(c => (string?)c.Attribute("type") == "composer")?.Value.Trim(),
            // "poet" is what Lily# (and MuseScore) write; "lyricist" is the schema's own word.
            Poet = Els(Local(root, "identification"), "creator")
                .FirstOrDefault(c => (string?)c.Attribute("type") is "poet" or "lyricist")?.Value.Trim(),
            Subtitle = Els(root, "credit")
                .FirstOrDefault(c => Local(c, "credit-type")?.Value.Trim() == "subtitle")
                is { } credit ? Local(credit, "credit-words")?.Value.Trim() : null,
        };
        doc.Paper = ReadPageLayout(root, report);

        // Part names come from the part-list; the <part> elements carry the music.
        var names = new Dictionary<string, string?>();
        var labels = new Dictionary<string, string?>();
        foreach (var sp in Els(Local(root, "part-list"), "score-part"))
        {
            var id = (string?)sp.Attribute("id");
            if (id == null)
                continue;
            var partName = Local(sp, "part-name");
            names[id] = partName?.Value.Trim();
            // The name the source PRINTS comes back as the staff's label (2026-09-30: every
            // label was lost, `staff rh "Piano"` coming back as a bare `staff pianoRH`).
            if ((string?)partName?.Attribute("print-object") != "no" && names[id] is { Length: > 0 } printed)
                labels[id] = printed;
        }
        var partGroups = ReadPartGroups(Local(root, "part-list"));

        var used = new HashSet<string>(StringComparer.Ordinal);
        int index = 0;
        foreach (var partEl in Els(root, "part"))
        {
            index++;
            var id = (string?)partEl.Attribute("id") ?? $"P{index}";
            var working = new ImportPart
            {
                Id = id,
                Name = names.GetValueOrDefault(id),
                Label = labels.GetValueOrDefault(id),
            };
            // One MusicXML part may yield several Lily# parts (one per staff).
            var read = ReadPart(partEl, working, doc, report);
            // The part's groups; a part that splits into its own grand staff keeps that one
            // rather than a brace around it.
            if (partGroups.TryGetValue(id, out var path))
                foreach (var p in read)
                    p.Groups.AddRange(read.Count > 1 ? path.Where(g => g.Kind != "grandStaff") : path);
            foreach (var p in read)
            {
                p.SafeName = SafeIdentifier(p.Name, index, used);
                doc.Parts.Add(p);
            }
        }
        // A group left with one staff (its other members split into a grand staff of their own,
        // or were never read) is no group.
        foreach (var lone in doc.Parts.SelectMany(p => p.Groups).GroupBy(g => g.Key)
                     .Where(g => g.Count() < 2).Select(g => g.Key).ToList())
            foreach (var p in doc.Parts)
                p.Groups.RemoveAll(g => g.Key == lone);

        // An empty score is a common surprise (e.g. a template exported with no
        // music): the output parses but renders nothing, so say so plainly rather
        // than leave the user wondering whether the importer swallowed the notes.
        int totalNotes = doc.Parts.Sum(p => p.Measures.Sum(
            m => m.VoiceItems.Values.Sum(list => list.Count(i => i is ImportNote note && !note.IsRest))));
        if (doc.Parts.Count == 0)
            report.Warn("No <part> elements found; the imported score is empty.");
        else if (totalNotes == 0)
            report.Warn("The MusicXML contains no notes; the imported score is empty.");
        return doc;
    }

    /// <summary>
    /// The <c>&lt;part-group&gt;</c>s each part stands in, by part id, outer first
    /// (<see cref="ImportPart.Groups"/>): a brace is a <c>grandStaff</c> — a grand staff whose
    /// staves are separate parts, as <c>MusicXmlExporter</c> writes one labelled apart and
    /// other programs a piano's two parts — a bracket (or square) a <c>staffGroup</c>, or a
    /// <c>choirStaff</c> when its bar lines stop at each staff. Until 2026-09-30 no part-group
    /// was read and every group came back as unrelated staves. A group with another symbol
    /// (line, none) is not read.
    /// </summary>
    private static Dictionary<string, List<(string Key, string Kind)>> ReadPartGroups(XElement? partList)
    {
        var result = new Dictionary<string, List<(string Key, string Kind)>>(StringComparer.Ordinal);
        if (partList == null)
            return result;
        var open = new List<(string Number, string Key, string? Kind)>();
        int serial = 0;
        foreach (var el in partList.Elements())
        {
            switch (el.Name.LocalName)
            {
                case "part-group":
                {
                    string number = (string?)el.Attribute("number") ?? "1";
                    open.RemoveAll(g => g.Number == number);
                    if ((string?)el.Attribute("type") != "start")
                        break;
                    string? kind = Local(el, "group-symbol")?.Value.Trim() switch
                    {
                        "brace" => "grandStaff",
                        "bracket" or "square" => Local(el, "group-barline")?.Value.Trim() == "no"
                            ? "choirStaff" : "staffGroup",
                        _ => null,
                    };
                    open.Add((number, $"\u0001group{++serial}", kind));
                    break;
                }
                case "score-part":
                    if ((string?)el.Attribute("id") is { } id)
                        result[id] = open.Where(g => g.Kind != null).Select(g => (g.Key, g.Kind!)).ToList();
                    break;
            }
        }
        return result;
    }

    private static List<ImportPart> ReadPart(XElement partEl, ImportPart part, ImportDocument doc, ImportReport report)
    {
        int divisions = 1;              // ticks per quarter; may change mid-part
        string? clefSet = null;         // most recent clef, for the part header
        int? transposeSet = null;       // the part's <transpose>, in semitones
        int measureNo = 0;
        var staffClefs = new Dictionary<int, string>();   // staff number -> clef
        var clefsInForce = new Dictionary<int, string>(); // staff number -> the clef it stands in now
        int staffCount = 1;                               // the part's <staves>
        var voiceStaff = new Dictionary<int, int>();       // voice number -> its staff
        var staffCounts = new Dictionary<(int Voice, int Staff), int>();   // the notes it has on each
        var noteStaff = new Dictionary<ImportNote, int>();  // the staff each head is written on
        var noteAt = new Dictionary<ImportNote, (int Voice, int Measure, int Onset)>();
        var directed = new List<(ImportNote Host, string Mark, int Staff)>();  // marks a <staff> sent
        var slurEnds = new List<SlurEnd>();                 // every <slur> of the part, in stream order
        var tiedNotes = new List<(ImportNote Note, int Voice, int Measure, int Onset)>();
        var wedgeStops = new List<(int Measure, int At, int? Staff)>();
        var ottavaStops = new List<(string Name, int Measure, int At, int? Staff)>();
        void NoteDirected(ImportNote host, string mark, int? staff)
        {
            if (staff is { } s)
                directed.Add((host, mark, s));
        }
        // How the <pedal>s of the part want drawing, by how many ask (a Lily# part has one style).
        var pedalStyles = new Dictionary<string, int>();
        // A span's end (`!ottava`, `!sustain`) standing past a bar's last onset — where a
        // MusicXML stop usually stands, after the last note it covers — closes on the next
        // note of its staff, in the next bar: on the bar's last note it would end the line a
        // note early (the note carrying `@!ottava` is outside the line).
        var carriedEnds = new List<(string Mark, int? Staff)>();
        var lastNoteOfStaff = new Dictionary<int, ImportNote>();
        ImportNote? partLastNote = null;
        var pedals = new List<string>();                   // the pedals down, oldest first
        var ottava = new List<string>();                   // the octave line open, by the name it opened with
        // Grace notes wait for the next note, which may be the next bar's: those at a bar's end
        // were lost until 2026-11-04 (Lang op. 36/3, bars 45-46).
        var pendingGrace = new List<ImportGraceNote>();

        foreach (var measEl in Els(partEl, "measure"))
        {
            measureNo++;
            var measure = new ImportMeasure
            {
                Implicit = (string?)measEl.Attribute("implicit") == "yes",
                NewSystem = Els(measEl, "print").Any(p =>
                    (string?)p.Attribute("new-system") == "yes" || (string?)p.Attribute("new-page") == "yes"),
            };

            int lastVoice = 1;          // voice a dangling annotation attaches to
            // <harmony>/<figured-bass> precede their note in the stream; hold them
            // until the note arrives so they attach to it in the writer.
            var pendingAnnotations = new List<ImportItem>();
            // <direction> dynamics and <grace> notes also precede the note they mark. A
            // dynamic waits for the first note AT OR PAST its position (2026-09-29): a
            // direction may carry an <offset> from where it stands in the stream — the
            // exporter writes every direction at the bar's head with one — and until now a
            // dynamic on beat 4 came in on beat 1.
            // A direction of a multi-staff part marks a note of ITS staff (<staff>; none = any):
            // the lower hand's `@f` used to land on the upper hand's note at the same beat.
            var pendingDynamics = new List<(string Dynamic, int At, int? Staff)>();
            foreach (var (end, endStaff) in carriedEnds)
                pendingDynamics.Add((end, 0, endStaff));
            carriedEnds.Clear();
            int position = 0;           // divisions from the bar's head, along the stream
            var voiceAt = new Dictionary<int, int>();   // where each voice's written music ends
            var openTremolos = new Dictionary<int, (ImportNote First, int Ticks)>();   // per voice
            ImportNote? lastNote = null;
            // A direction on a staff with no note in this bar (both hands written on the other,
            // the mark set between the staves) marks the note at its moment on any staff; until
            // 2026-11-04 it went back to that staff's last note, in an earlier bar.
            var stavesWritten = measEl.Elements().Where(e => e.Name.LocalName == "note")
                .Select(e => e.Elements().FirstOrDefault(c => c.Name.LocalName == "staff") is { } s && int.TryParse(s.Value, out int n) ? n : 1)
                .ToHashSet();

            void AttachDue(ImportNote note, int staff)
            {
                bool Due((string Dynamic, int At, int? Staff) p)
                    => p.At <= position && (p.Staff == null || p.Staff == staff || !stavesWritten.Contains(p.Staff.Value));
                // Levels written at different moments, with no note of their staff until now, all
                // fall on this note, and a note prints one: the last is what stands when it
                // sounds. Two at ONE moment are left as written, for the check to name.
                var levels = pendingDynamics.Where(p => Due(p) && LysWriter.IsDynamic(p.Dynamic)).ToList();
                if (levels.Count > 1)
                {
                    int latest = levels.Max(p => p.At);
                    foreach (var passed in levels.Where(p => p.At < latest))
                    {
                        report.Warn(measureNo, $"the dynamic {passed.Dynamic} has no note of its staff before a later dynamic, so it is dropped.");
                        pendingDynamics.Remove(passed);
                    }
                    // Two at one moment that are one mark: sf and p set side by side is @sfp, and
                    // a note prints one dynamic.
                    var left = pendingDynamics.Where(p => Due(p) && LysWriter.IsDynamic(p.Dynamic)).ToList();
                    if (left.Count == 2 && left[1].Dynamic == "p" && left[0].Dynamic is "sf" or "f")
                    {
                        int first = pendingDynamics.IndexOf(left[0]);
                        pendingDynamics[first] = (left[0].Dynamic + "p", left[0].At, left[0].Staff);
                        pendingDynamics.Remove(left[1]);
                    }
                }
                int k = 0;
                for (int i = 0; i < pendingDynamics.Count; i++)
                    if (Due(pendingDynamics[i]))
                    {
                        // An octave line's end INCLUDES its note in Lily# (owner's decision
                        // 2026-10-28): the stop that stands before this note closes the line on
                        // the note before it, its staff's last one.
                        if (pendingDynamics[i].Dynamic.StartsWith(ClefMark, StringComparison.Ordinal))
                        {
                            note.ClefBefore = pendingDynamics[i].Dynamic[ClefMark.Length..];
                        }
                        else if (EndsWithItsNote(pendingDynamics[i].Dynamic)
                            && (lastNoteOfStaff.TryGetValue(staff, out var before) ? before : partLastNote) is { } last)
                        {
                            last.Articulations.Add(pendingDynamics[i].Dynamic);
                            NoteDirected(last, pendingDynamics[i].Dynamic, pendingDynamics[i].Staff);
                        }
                        else
                        {
                            note.Articulations.Insert(k++, pendingDynamics[i].Dynamic);
                            NoteDirected(note, pendingDynamics[i].Dynamic, pendingDynamics[i].Staff);
                        }
                    }
                pendingDynamics.RemoveAll(Due);
            }

            foreach (var el in measEl.Elements())
            {
                switch (el.Name.LocalName)
                {
                    case "attributes":
                        ReadAttributes(el, measure, ref divisions, ref clefSet, ref transposeSet,
                                       staffClefs, report, measureNo,
                                       midBar: position > 0 || measure.VoiceItems.Values.Any(items => items.Count > 0),
                                       clefsInForce, ref staffCount,
                                       (word, staff) => pendingDynamics.Add((ClefMark + word, position, staff)));
                        break;

                    case "direction":
                    {
                        if (ReadDirectionTempo(el) is int bpm)
                        {
                            doc.Tempo ??= bpm;
                            if (measureNo > 1 || measure.HasAnyItems)
                                measure.Tempo = bpm;
                        }
                        int at = position + (int.TryParse(Local(el, "offset")?.Value, out int off) ? off : 0);
                        int? dirStaff = int.TryParse(Local(el, "staff")?.Value, out int ds) ? ds : null;
                        foreach (var dyn in ReadDirectionDynamics(el))
                            pendingDynamics.Add((dyn, at, dirStaff));
                        if (Els(el, "direction-type").SelectMany(t => Els(t, "wedge"))
                                .Any(w => (string?)w.Attribute("type") == "stop"))
                            wedgeStops.Add((measureNo, at, dirStaff));
                        // The direction's text and its lines ride the same queue: they mark
                        // the note at their position exactly as a dynamic does.
                        foreach (var text in ReadDirectionTexts(el, report, measureNo))
                            pendingDynamics.Add((text, at, dirStaff));
                        foreach (var span in ReadDirectionSpans(el, pedals, ottava, report, measureNo))
                        {
                            pendingDynamics.Add((span, at, dirStaff));
                            if (EndsWithItsNote(span))
                                ottavaStops.Add((span[1..], measureNo, at, dirStaff));
                        }
                        // Only a <pedal> that says: `lysc xml` writes neither attribute, and a book's
                        // own default style has to come back as itself.
                        foreach (var pedal in el.Descendants().Where(d => d.Name.LocalName == "pedal"
                            && (string?)d.Attribute("type") is "start" or "sostenuto"
                            && (d.Attribute("line") != null || d.Attribute("sign") != null)))
                        {
                            string style = PedalStyleOf(pedal);
                            pedalStyles[style] = pedalStyles.GetValueOrDefault(style) + 1;
                        }
                        break;
                    }

                    case "harmony":
                        if (ReadHarmony(el) is { } h)
                            pendingAnnotations.Add(h);
                        break;

                    case "figured-bass":
                        if (ReadFiguredBass(el) is { } fb)
                            pendingAnnotations.Add(fb);
                        break;

                    case "backup":
                        // A rewind to overlay another voice; voices are bucketed by
                        // <voice>, so the cursor move is implicit — only the position moves.
                        position -= int.TryParse(Local(el, "duration")?.Value, out int back) ? back : 0;
                        break;

                    case "forward":
                    {
                        // A gap in the current voice — fill it with rest(s) so the
                        // following notes stay on the beat.
                        int fdur = int.TryParse(Local(el, "duration")?.Value, out int fd) ? fd : 0;
                        bool named = int.TryParse(Local(el, "voice")?.Value, out int fv);
                        // <voice> is optional here. With none, the gap is the last voice's only if
                        // the stream is still at that voice's end; after a <backup> it is only a
                        // cursor move, and whichever voice writes next fills up to it (below).
                        // Until 2026-11-04 such a gap went into the voice just finished, past its
                        // bar's end, and the voice it was for lost it: its notes came in early
                        // (Dichterliebe 1, bar 6: `<backup 16> <forward 8> <forward 4> F4 v2`).
                        if (!named && voiceAt.GetValueOrDefault(lastVoice, -1) != position)
                        {
                            position += fdur;
                            break;
                        }
                        int fvoice = named ? fv : lastVoice;
                        foreach (var (value, dots) in DecomposeDuration(fdur, divisions))
                            measure.Voice(fvoice).Add(new ImportNote { IsRest = true, NoteValue = value, Dots = dots });
                        position += fdur;
                        voiceAt[fvoice] = position;
                        break;
                    }

                    case "note":
                    {
                        int voice = int.TryParse(Local(el, "voice")?.Value, out int v) ? v : 1;
                        lastVoice = voice;
                        // Remember which staff each voice sits on; a part spanning
                        // several staves is split into a grand staff below.
                        int staff = int.TryParse(Local(el, "staff")?.Value, out int st) ? st : 1;
                        staffCounts.TryGetValue((voice, staff), out int seen);
                        staffCounts[(voice, staff)] = seen + 1;
                        voiceStaff.TryAdd(voice, staff);
                        // A grace note has no duration — accumulate it to attach to the
                        // next real note as a leading acciaccatura/grace block.
                        if (Local(el, "grace") != null)
                        {
                            if (ReadGraceNote(el) is { } g)
                            {
                                pendingGrace.Add(g);
                                if (g.SlurStart && !g.ChordWithPrev)
                                    slurEnds.Add(new SlurEnd(g, null, "1", true, voice, measureNo, position));
                            }
                            break;
                        }
                        var note = ReadNote(el, divisions, report, measureNo);
                        if (note == null)
                            break;
                        // A chord member sounds with its head, which has already moved the cursor.
                        int slurOnset = note.ChordWithPrev && lastNote != null && noteAt.TryGetValue(lastNote, out var head)
                            ? head.Onset : position;
                        // A slur on a chord's later head is the chord's: the writer spells a chord's
                        // slur once, from its first head, and an end left on another was lost.
                        var slurHost = note.ChordWithPrev && lastNote != null ? lastNote : note;
                        foreach (var s in Els(Local(el, "notations"), "slur"))
                            if ((string?)s.Attribute("type") is "start" or "stop")
                                slurEnds.Add(new SlurEnd(null, slurHost, (string?)s.Attribute("number") ?? "1",
                                    (string?)s.Attribute("type") == "start", voice, measureNo, slurOnset));
                        if (slurHost != note && note.PhrasingSlurPlacement != null)
                            slurHost.PhrasingSlurPlacement ??= note.PhrasingSlurPlacement;
                        if (note.TieStart || note.TieStop)
                            tiedNotes.Add((note, voice, measureNo, slurOnset));
                        var target = measure.Voice(voice);
                        // A chord member folds onto the previous note; harmony /
                        // figured-bass / dynamics / grace attach to the head note only.
                        if (!note.ChordWithPrev)
                        {
                            // A voice's note past that voice's end stands on its beat: the stream
                            // reached it by a voiceless <forward> or by a <backup> short of the
                            // bar's head (Dichterliebe 13 bar 30: E4 v2 on beat 2.5, at the bar's
                            // head until 2026-11-04), and the voice owes the gap either way.
                            int gap = position - voiceAt.GetValueOrDefault(voice);
                            int spelled = 0;
                            foreach (var (value, dots) in DecomposeDuration(gap, divisions))
                            {
                                target.Add(new ImportNote { IsRest = true, NoteValue = value, Dots = dots });
                                spelled += 4 * Math.Max(1, divisions) / value;
                            }
                            // A gap inside a tuplet (0.4 of a beat) has no plain note value.
                            if (gap > spelled)
                                report.Warn(measureNo, "a voice's gap has no plain note value, so its next note stands early.");
                            target.AddRange(pendingAnnotations);
                            pendingAnnotations.Clear();
                            AttachDue(note, staff);
                            note.LeadingGrace.AddRange(pendingGrace);
                            pendingGrace.Clear();
                            noteAt[note] = (voice, measureNo, position);
                            int noteTicks = int.TryParse(Local(el, "duration")?.Value, out int ndur) ? ndur : 0;
                            position += noteTicks;
                            voiceAt[voice] = position;
                            SettleTremoloPair(note, noteTicks, voice, divisions, openTremolos);
                            lastNote = note;
                            lastNoteOfStaff[staff] = note;
                            partLastNote = note;
                            noteStaff[note] = staff;
                        }
                        target.Add(note);
                        break;
                    }

                    case "barline":
                        ReadBarline(el, measure);
                        break;
                }
            }
            // Any annotation with no following note still gets recorded (the writer
            // drops a dangling @chord/@fig with a warning rather than mis-attaching).
            measure.Voice(lastVoice).AddRange(pendingAnnotations);
            // A dynamic past the bar's last onset (on its last note, at that note's end)
            // belongs to that note of its staff: it used to be dropped. A span's end goes on
            // to the next bar's note instead (carriedEnds).
            foreach (var (dyn, _, dynStaff) in pendingDynamics)
            {
                // A clef after the bar's last note stands at the next bar's first (and is drawn
                // before the bar line between them, as a clef opening a bar is).
                if ((dyn.StartsWith('!') && !EndsWithItsNote(dyn)) || dyn.StartsWith(ClefMark, StringComparison.Ordinal))
                    carriedEnds.Add((dyn, dynStaff));
                else if ((dynStaff is { } s && lastNoteOfStaff.TryGetValue(s, out var own) ? own : lastNote) is { } host)
                {
                    host.Articulations.Add(dyn);
                    NoteDirected(host, dyn, dynStaff);
                }
            }
            pendingDynamics.Clear();

            part.Measures.Add(measure);
        }
        if (pendingGrace.Count > 0)
            report.Warn(measureNo, "grace notes after the part's last note have no note to lead to, so they are dropped.");
        // An end after the part's last note has no next note to stand on: its staff's last
        // note carries it — Lily# requires the end, and a line one note short beats none.
        foreach (var (end, endStaff) in carriedEnds.Where(e => !e.Mark.StartsWith(ClefMark, StringComparison.Ordinal)))
            if ((endStaff is { } s && lastNoteOfStaff.TryGetValue(s, out var own) ? own
                    : partLastNote) is { } host)
            {
                host.Articulations.Add(end);
                NoteDirected(host, end, endStaff);
            }

        part.Clef = clefSet ?? "treble";
        part.TranspositionSemitones = transposeSet;
        part.PedalStyle = pedalStyles.Count == 0 ? null
            : pedalStyles.OrderByDescending(s => s.Value).ThenBy(s => s.Key, StringComparer.Ordinal).First().Key;

        // A part whose voices span more than one staff (a piano grand staff) splits
        // into one Lily# part per staff, grouped into a grandStaff by the score.
        // A voice sits on the staff most of its notes are written on (the first of equals): a
        // note written on the other one is a cross-staff note, and until 2026-11-04 whichever
        // staff a voice's LAST note named took the whole voice, so a left hand ending on a note
        // up in the right hand's staff left one staff, its clef mid-air.
        foreach (var voice in voiceStaff.Keys.ToList())
        {
            int best = voiceStaff[voice], most = staffCounts.GetValueOrDefault((voice, best));
            foreach (var ((v, s), count) in staffCounts)
                if (v == voice && (count > most))
                    (best, most) = (s, count);
            voiceStaff[voice] = best;
        }
        PairSlurs(slurEnds, report);
        PairTies(tiedNotes, noteAt, report);
        var staves = voiceStaff.Values.Distinct().OrderBy(s => s).ToList();
        // A TAB staff beside a notation staff holds the same notes again, with their strings
        // and frets (what MusicXmlExporter writes for `staff gt  tab gt`): its voices are a
        // copy, not more music, and are dropped rather than split into a part of their own.
        var tabStaves = staves.Where(s => staffClefs.GetValueOrDefault(s) == TabClef).ToList();
        if (tabStaves.Count > 0 && tabStaves.Count < staves.Count)
        {
            foreach (var measure in part.Measures)
                foreach (var (voice, staff) in voiceStaff)
                    if (tabStaves.Contains(staff))
                        measure.VoiceItems.Remove(voice);
            staves.RemoveAll(tabStaves.Contains);
        }
        if (staves.Count <= 1)
        {
            EndWedges(wedgeStops, noteAt, voiceStaff);
            EndOttavasInTheirVoice(ottavaStops, noteAt, voiceStaff, report);
            KeepUnreleasedUnaCordaAsWords(noteAt);
            ToPlainClefOctave(part.Measures, part.Clef);
            return new List<ImportPart> { part };
        }
        SendDirectedMarksHome(directed, noteAt, voiceStaff);
        EndWedges(wedgeStops, noteAt, voiceStaff);
        EndOttavasInTheirVoice(ottavaStops, noteAt, voiceStaff, report);
        KeepUnreleasedUnaCordaAsWords(noteAt);
        // `@cross` names "the other staff", which only two staves answer.
        if (staves.Count == 2)
            foreach (var measure in part.Measures)
                foreach (var (voice, items) in measure.VoiceItems)
                    foreach (var item in items)
                        if (item is ImportNote { IsRest: false } note
                            && noteStaff.TryGetValue(note, out int written)
                            && written != voiceStaff.GetValueOrDefault(voice, written)
                            && staves.Contains(written))
                            note.Articulations.Add("cross");
        return SplitByStaff(part, staves, voiceStaff, staffClefs, transposeSet);
    }

    /// <summary>
    /// Moves the pitches of a staff whose clef carries an octave (<c>treble_8</c>) from where
    /// MusicXML reads them — under the clef, <c>&lt;clef-octave-change&gt;</c> included, so a
    /// <c>treble_8</c> staff's middle line is B3 — to where Lily# writes them: as on the plain
    /// clef, the clef word adding the octave (<c>b</c> on the middle line, sounding B3).
    /// </summary>
    /// <remarks>
    /// The inverse of <c>MusicXmlExporter.ApplyPartHeader</c> / <c>MusicXmlPart.PitchOctaveShift</c>,
    /// and with it <c>&lt;transpose&gt;</c> is the instrument's share alone, taken as it stands.
    /// Until 2026-10-05 both sides read a guitar as "written on the plain clef, the clef's octave
    /// in <c>&lt;transpose&gt;</c> too", which a reader that honours the clef drew an octave high
    /// (LilySharp-Omr feedback #17); a tenor part written the usual way (<c>treble_8</c>,
    /// sounding pitches, no <c>&lt;transpose&gt;</c>) came in an octave low.
    /// </remarks>
    private static void ToPlainClefOctave(IEnumerable<ImportMeasure> measures, string clefWord)
    {
        int octaves = Tablature.Tunings.ClefOctaveShift(PartHeaderDefaults.ParseClefWord(clefWord)) / 12;
        if (octaves == 0)
            return;
        foreach (var measure in measures)
            foreach (var items in measure.VoiceItems.Values)
                foreach (var item in items)
                    if (item is ImportNote note)
                    {
                        if (!note.IsRest)
                            note.Octave -= octaves;
                        foreach (var grace in note.LeadingGrace)
                            grace.Octave -= octaves;
                    }
    }

    /// <summary>
    /// A mark whose &lt;direction&gt; names a staff goes to a note of a voice that LIVES on that
    /// staff: a note merely written there, by a voice of the other staff, belongs to the other
    /// Lily# part once the staves split.
    /// </summary>
    /// <remarks>
    /// Left there, Dichterliebe 12's <c>@!sustain</c> on staff 2 landed on a right-hand voice's
    /// <c>@cross</c> note, so the right hand released a pedal it never pressed and the left hand
    /// never released its own (`lysc check`: "a pedal is never released"). The nearest onset in
    /// the bar; a bar with no such note keeps the mark where it is.
    /// </remarks>
    private static void SendDirectedMarksHome(List<(ImportNote Host, string Mark, int Staff)> directed,
        Dictionary<ImportNote, (int Voice, int Measure, int Onset)> noteAt, Dictionary<int, int> voiceStaff)
    {
        Dictionary<int, List<ImportNote>>? byMeasure = null;
        foreach (var (host, mark, staff) in directed)
        {
            if (!noteAt.TryGetValue(host, out var at) || voiceStaff.GetValueOrDefault(at.Voice, staff) == staff)
                continue;
            if (byMeasure == null)
            {
                byMeasure = new();
                foreach (var (note, place) in noteAt)
                {
                    if (!byMeasure.TryGetValue(place.Measure, out var list))
                        byMeasure[place.Measure] = list = new();
                    list.Add(note);
                }
            }
            ImportNote? home = null;
            int nearest = int.MaxValue;
            foreach (var candidate in byMeasure[at.Measure])
            {
                var place = noteAt[candidate];
                int distance = Math.Abs(place.Onset - at.Onset);
                if (voiceStaff.GetValueOrDefault(place.Voice, -1) == staff && distance < nearest)
                    (home, nearest) = (candidate, distance);
            }
            if (home != null && host.Articulations.Remove(mark))
            {
                // A release before what follows it on that note, as AttachDue orders them.
                if (mark.StartsWith('!'))
                    home.Articulations.Insert(0, mark);
                else
                    home.Articulations.Add(mark);
            }
        }
    }

    /// <summary>One end of a &lt;slur&gt;: on a grace note or on a note, with where it sounds.</summary>
    private sealed record SlurEnd(ImportGraceNote? Grace, ImportNote? Note, string Number, bool Start,
        int Voice, int Measure, int Onset);

    /// <summary>
    /// Keeps the slurs a Lily# voice can hold: those whose two ends are in one voice. The ends are
    /// paired by number along the music's time, not the stream's.
    /// </summary>
    /// <remarks>
    /// A MusicXML slur may run from one voice to another, even between the staves (Dichterliebe 1,
    /// bar 11: voice 2 on the lower staff to voice 1 on the upper), and the voice written later in
    /// the stream can hold the end that sounds first. A Lily# slur stays in its voice. Until
    /// 2026-11-04 each end was written where it stood, so the `(` went unclosed or met an unrelated
    /// `)` further on, and drew a wrong curve. Such a slur has no spelling, so it is dropped and
    /// reported.
    /// </remarks>
    private static void PairSlurs(List<SlurEnd> ends, ImportReport report)
    {
        foreach (var end in ends)
        {
            if (end.Grace != null)
                end.Grace.SlurStart = false;
            else if (end.Number == "1")
                (end.Note!.SlurStart, end.Note.SlurStop) = (false, false);
            else
                (end.Note!.PhrasingSlurStart, end.Note.PhrasingSlurStop) = (false, false);
        }
        var open = new Dictionary<string, List<SlurEnd>>();
        // A stop before a start at one moment: a note may end one slur and begin the next. A grace
        // note sounds before its main note, whose stop may be the end of the grace note's slur.
        foreach (var end in ends.OrderBy(e => e.Measure).ThenBy(e => e.Onset)
                     .ThenBy(e => e.Grace != null ? 0 : e.Start ? 2 : 1))
        {
            if (!open.TryGetValue(end.Number, out var waiting))
                open[end.Number] = waiting = new();
            if (end.Start)
            {
                waiting.Add(end);
                continue;
            }
            var mate = waiting.LastOrDefault(e => e.Voice == end.Voice) ?? waiting.FirstOrDefault();
            if (mate == null)
            {
                report.Warn(end.Measure, "a slur's end has no start, so it is dropped.");
                continue;
            }
            waiting.Remove(mate);
            if (mate.Voice != end.Voice)
            {
                report.Warn(mate.Measure, $"a slur runs from voice {mate.Voice} to voice {end.Voice} (bar {end.Measure}); "
                    + "a Lily# slur stays in its voice, so it is dropped.");
                continue;
            }
            if (mate.Grace != null)
                mate.Grace.SlurStart = true;
            else if (mate.Number == "1")
                mate.Note!.SlurStart = true;
            else
                mate.Note!.PhrasingSlurStart = true;
            if (end.Number == "1")
                end.Note!.SlurStop = true;
            else
                end.Note!.PhrasingSlurStop = true;
        }
        foreach (var unended in open.Values.SelectMany(w => w))
            report.Warn(unended.Measure, "a slur has no end, so it is dropped.");
    }

    /// <summary>
    /// Keeps the ties a Lily# voice can hold, as <see cref="PairSlurs"/> keeps its slurs: the two
    /// ends are paired by sounding pitch along the music's time, and one that runs to another
    /// voice has no spelling, so it is dropped and reported. A `~` left there met a note of another
    /// pitch: nothing was drawn, and the check blamed the book (Dichterliebe 1, 19 of them). So too
    /// a tie over other notes of its voice, and one between two spellings of a pitch: a Lily# tie
    /// joins a note to the next one, of the same written pitch.
    /// </summary>
    private static void PairTies(List<(ImportNote Note, int Voice, int Measure, int Onset)> notes,
        Dictionary<ImportNote, (int Voice, int Measure, int Onset)> noteAt, ImportReport report)
    {
        static int Sounding(ImportNote n) => n.Octave * 12 + new[] { 0, 2, 4, 5, 7, 9, 11 }[n.Step] + n.Alter;
        var heads = noteAt.Values.GroupBy(h => h.Voice).ToDictionary(g => g.Key,
            g => g.Select(h => (h.Measure, h.Onset)).Distinct().OrderBy(h => h.Measure).ThenBy(h => h.Onset).ToList());
        bool Next(int voice, (int Measure, int Onset) from, (int Measure, int Onset) to)
        {
            var after = heads[voice].FirstOrDefault(h => h.Measure > from.Measure
                || (h.Measure == from.Measure && h.Onset > from.Onset));
            return after == to;
        }
        var open = new Dictionary<int, List<(ImportNote Note, int Voice, int Measure, int Onset)>>();
        foreach (var (note, voice, measure, onset) in notes.OrderBy(n => n.Measure).ThenBy(n => n.Onset))
        {
            if (!open.TryGetValue(Sounding(note), out var waiting))
                open[Sounding(note)] = waiting = new();
            // Its stop before its start: a note may end one tie and begin the next.
            if (note.TieStop)
            {
                int mate = waiting.FindLastIndex(w => w.Voice == voice);
                if (mate < 0)
                    mate = waiting.Count > 0 ? 0 : -1;
                if (mate < 0)
                    note.TieStop = false;
                else
                {
                    var start = waiting[mate];
                    waiting.RemoveAt(mate);
                    string? why = start.Voice != voice
                        ? $"runs from voice {start.Voice} to voice {voice} (bar {measure}); a Lily# tie stays in its voice"
                        : !Next(voice, (start.Measure, start.Onset), (measure, onset))
                            ? $"passes over other notes of its voice to bar {measure}; a Lily# tie joins a note to the next"
                            : start.Note.Step != note.Step || start.Note.Alter != note.Alter
                                ? $"joins two spellings of a pitch (bar {measure}); a Lily# tie joins one written pitch"
                                : null;
                    if (why != null)
                    {
                        start.Note.TieStart = false;
                        note.TieStop = false;
                        report.Warn(start.Measure, $"a tie {why}, so it is dropped.");
                    }
                }
            }
            if (note.TieStart)
                waiting.Add((note, voice, measure, onset));
        }
        foreach (var (note, _, measure, _) in open.Values.SelectMany(w => w))
        {
            note.TieStart = false;
            report.Warn(measure, "a tie has no end, so it is dropped.");
        }
    }

    /// <summary>
    /// A wedge's stop as <c>@!cresc</c> / <c>@!decresc</c>, on the last note of the wedge's voice
    /// before the stop: the end includes its note, up to that note's right edge, which is where
    /// the stop stands. Until 2026-11-04 the stop was not read and a hairpin ran on to the next
    /// dynamic, however far that was.
    /// </summary>
    /// <remarks>
    /// Nothing is written when the voice's next note carries a dynamic or a hairpin: that ends the
    /// wedge where it stops, and is how a hairpin ending on a dynamic is spelled.
    /// </remarks>
    private static void EndWedges(List<(int Measure, int At, int? Staff)> stops,
        Dictionary<ImportNote, (int Voice, int Measure, int Onset)> noteAt, Dictionary<int, int> voiceStaff)
    {
        if (stops.Count == 0)
            return;
        var byTime = noteAt.OrderBy(n => n.Value.Measure).ThenBy(n => n.Value.Onset).ToList();
        var ended = new HashSet<ImportNote>();
        foreach (var (measure, at, staff) in stops.OrderBy(s => s.Measure).ThenBy(s => s.At))
        {
            bool Before((int Voice, int Measure, int Onset) p) => p.Measure < measure || (p.Measure == measure && p.Onset < at);
            var opener = byTime.LastOrDefault(n => Before(n.Value) && !ended.Contains(n.Key)
                && n.Key.Articulations.Any(a => a is "cresc" or "decresc")
                && (staff == null || voiceStaff.GetValueOrDefault(n.Value.Voice, staff.Value) == staff)).Key;
            if (opener == null)
                continue;
            ended.Add(opener);
            var inVoice = byTime.Where(n => n.Value.Voice == noteAt[opener].Voice).Select(n => n.Key).ToList();
            int from = inVoice.IndexOf(opener);
            int last = from;
            while (last + 1 < inVoice.Count && Before(noteAt[inVoice[last + 1]]))
                last++;
            // A dynamic or a hairpin met on the way has already ended it.
            if (inVoice.Skip(from + 1).Take(last - from + 1).Any(n => n.Articulations.Any(EndsAHairpin)))
                continue;
            string name = opener.Articulations.Last(a => a is "cresc" or "decresc");
            // A wedge under one note is `@cresc@!cresc`, which spans that note's length.
            inVoice[last].Articulations.Add("!" + name);
        }
    }

    /// <summary>
    /// An octave line ends in the voice it opened in, on that voice's last note before the stop, as
    /// a wedge does (<see cref="EndWedges"/>). The stop's first home is its staff's last note in the
    /// stream, which may be another voice's and even sound before the line opens: a Lily# line ended
    /// there is never closed (Kinkel op. 7/1, Boulanger Clairières 2).
    /// </summary>
    private static void EndOttavasInTheirVoice(List<(string Name, int Measure, int At, int? Staff)> stops,
        Dictionary<ImportNote, (int Voice, int Measure, int Onset)> noteAt, Dictionary<int, int> voiceStaff,
        ImportReport report)
    {
        if (stops.Count == 0)
            return;
        foreach (var note in noteAt.Keys)
            note.Articulations.RemoveAll(EndsWithItsNote);
        var byTime = noteAt.OrderBy(n => n.Value.Measure).ThenBy(n => n.Value.Onset).ToList();
        var ended = new HashSet<ImportNote>();
        foreach (var (name, measure, at, staff) in stops.OrderBy(s => s.Measure).ThenBy(s => s.At))
        {
            bool Before((int Voice, int Measure, int Onset) p) => p.Measure < measure || (p.Measure == measure && p.Onset < at);
            var open = byTime.Where(n => Before(n.Value) && !ended.Contains(n.Key) && n.Key.Articulations.Contains(name)).ToList();
            // Each hand may have a line of its own open at once.
            var opener = open.LastOrDefault(n => staff == null || voiceStaff.GetValueOrDefault(n.Value.Voice) == staff).Key
                ?? open.LastOrDefault().Key;
            if (opener == null)
            {
                report.Warn(measure, "an octave line's end has no start, so it is dropped.");
                continue;
            }
            ended.Add(opener);
            int voice = noteAt[opener].Voice;
            byTime.Last(n => n.Value.Voice == voice && Before(n.Value)).Key.Articulations.Add("!" + name);
        }
    }

    /// <summary>A mark that ends the hairpin before it: a dynamic, or another hairpin.</summary>
    private static bool EndsAHairpin(string mark)
        => mark is "cresc" or "decresc" or "niente" || LysWriter.IsDynamic(mark);

    /// <summary>
    /// An "una corda" no "tre corde" follows stays the words on the page: <c>@unaCorda</c> opens a
    /// span that must be released, and many scores never print the release (Holmes, Melodies 16).
    /// </summary>
    private static void KeepUnreleasedUnaCordaAsWords(Dictionary<ImportNote, (int Voice, int Measure, int Onset)> noteAt)
    {
        ImportNote? down = null;
        void Words()
        {
            if (down != null)
                down.Articulations[down.Articulations.IndexOf("unaCorda")] = "text(\"una corda\")";
        }
        foreach (var note in noteAt.OrderBy(n => n.Value.Measure).ThenBy(n => n.Value.Onset).Select(n => n.Key))
            foreach (var mark in note.Articulations.ToList())
                if (mark == "unaCorda")
                {
                    Words();
                    down = note;
                }
                else if (mark is "treCorde")
                    down = null;
        Words();
    }

    /// <summary>
    /// The Lily# <c>pedal</c> style a &lt;pedal&gt; asks for. MusicXML: <c>sign</c> is yes by
    /// default where <c>line</c> is no, and no where <c>line</c> is yes.
    /// </summary>
    private static string PedalStyleOf(XElement pedal)
    {
        bool line = (string?)pedal.Attribute("line") == "yes";
        bool sign = (string?)pedal.Attribute("sign") is { } written ? written == "yes" : !line;
        return line ? (sign ? "mixed" : "bracket") : "text";
    }

    /// <summary>Splits a multi-staff part into one part per staff (grouped as a grand
    /// staff), each keeping only the voices that sit on it, with its own clef.</summary>
    private static List<ImportPart> SplitByStaff(
        ImportPart part, List<int> staves,
        Dictionary<int, int> voiceStaff, Dictionary<int, string> staffClefs,
        int? transposeTotal)
    {
        var result = new List<ImportPart>();
        foreach (int staff in staves)
        {
            string staffClef = staffClefs.TryGetValue(staff, out var clef) ? clef
                             : staff == staves[0] ? "treble" : "bass";
            var sub = new ImportPart
            {
                Id = $"{part.Id}s{staff}",
                Name = StaffPartName(part.Name, staff, staves),
                Label = staff == staves[0] ? part.Label : null,
                StaffGroup = part.Id,
                Clef = staffClef,
                // ⚠️ <transpose> is the PART's, so every staff of a split part keeps it —
                // the split used to drop it and a transposing grand staff came back at
                // written pitch. Each staff reads it against its OWN clef.
                TranspositionSemitones = transposeTotal,
                PedalStyle = part.PedalStyle,
            };
            foreach (var measure in part.Measures)
            {
                var m = new ImportMeasure
                {
                    Implicit = measure.Implicit,
                    NewSystem = measure.NewSystem,
                    Key = measure.Key,
                    Time = measure.Time,
                    Tempo = measure.Tempo,
                    RepeatForward = measure.RepeatForward,
                    BarlineRight = measure.BarlineRight,
                };
                foreach (var (voice, items) in measure.VoiceItems)
                    if (voiceStaff.GetValueOrDefault(voice, 1) == staff)
                        m.Voice(voice).AddRange(items);
                sub.Measures.Add(m);
            }
            ToPlainClefOctave(sub.Measures, staffClef);
            result.Add(sub);
        }
        return result;
    }

    /// <summary>A readable Lily# part name per staff of a split part: RH/LH for the
    /// common two-staff piano, else "… staff N".</summary>
    private static string StaffPartName(string? baseName, int staff, List<int> staves)
    {
        string b = string.IsNullOrWhiteSpace(baseName) ? "Part" : baseName!;
        if (staves.Count == 2)
            return b + (staff == staves[0] ? " RH" : " LH");
        return $"{b} staff {staff}";
    }

    private static void ReadAttributes(
        XElement el, ImportMeasure measure, ref int divisions, ref string? clefSet,
        ref int? transposeSet,
        Dictionary<int, string> staffClefs, ImportReport report, int measureNo,
        bool midBar, Dictionary<int, string> clefsInForce, ref int staffCount, Action<string, int?> clefAtItsNote)
    {
        if (int.TryParse(Local(el, "divisions")?.Value, out int d) && d > 0)
            divisions = d;
        if (int.TryParse(Local(el, "staves")?.Value, out int staves))
            staffCount = staves;

        // The exporter writes `R1*N` as N whole-measure rests with this on the first; the
        // writer folds them back into one `R…*N`.
        if (int.TryParse(Local(Local(el, "measure-style"), "multiple-rest")?.Value, out int bars) && bars > 1)
            measure.MultipleRest = bars;

        var keyEl = Local(el, "key");
        if (keyEl != null)
        {
            if (int.TryParse(Local(keyEl, "fifths")?.Value, out int fifths))
            {
                string mode = Local(keyEl, "mode")?.Value.Trim().ToLowerInvariant() ?? "major";
                measure.Key = new ImportKey(fifths, mode);
            }
            else
            {
                report.Warn(measureNo, "non-traditional key signature dropped.");
            }
        }

        var timeEl = Local(el, "time");
        if (timeEl != null)
        {
            if (Local(timeEl, "senza-misura") != null)
                report.Warn(measureNo, "senza-misura (unmeasured) time dropped.");
            else if (int.TryParse(Local(timeEl, "beats")?.Value, out int beats)
                     && int.TryParse(Local(timeEl, "beat-type")?.Value, out int beatType))
                measure.Time = new ImportTime(beats, beatType);
        }

        // A multi-staff part carries one <clef number="N"> per staff; a single-staff
        // part omits the number (staff 1). Record each so a split keeps its own clef.
        foreach (var clefEl in Els(el, "clef"))
        {
            int staff = int.TryParse((string?)clefEl.Attribute("number"), out int n) ? n : 1;
            // A TAB staff is remembered as one (ReadPart drops it when it is a notation
            // staff's copy); it names no clef of its own.
            if (string.Equals(Local(clefEl, "sign")?.Value.Trim(), "TAB", StringComparison.OrdinalIgnoreCase))
            {
                staffClefs.TryAdd(staff, TabClef);
                continue;
            }
            string name = ClefName(clefEl, report, measureNo);
            staffClefs.TryAdd(staff, name);
            bool isTheFirst = !clefsInForce.TryGetValue(staff, out var inForce);
            clefsInForce[staff] = name;
            // A later clef is a word in the music, and the music takes fewer than a part header.
            if (!isTheFirst && name != inForce && name is not ("treble" or "bass" or "alto" or "tenor" or "treble_8"))
            {
                report.Warn(measureNo, $"the clef {name} has no spelling inside music, so this change is dropped.");
                continue;
            }
            // Inside the bar, or on one staff of several: it stands before the note at its moment
            // and goes where that note goes when the staves split. Until 2026-11-04 a change inside
            // a bar was read at the bar's head, and a split staff's changes were lost.
            if (!isTheFirst && (midBar || staffCount > 1))
            {
                if (name != inForce)
                    clefAtItsNote(name, staffCount > 1 ? staff : null);
                continue;
            }
            measure.Clef = name;        // mid-piece single-staff clef change
            clefSet ??= name;           // first clef becomes the part header clef
        }

        // <transpose> is what the written pitch SOUNDS beyond the clef: the pitches are read
        // under their clef, <clef-octave-change> included (ToPlainClefOctave moves them to the
        // plain clef's octave the clef word adds back), so this is the instrument's own shift
        // and is taken as it stands.
        var transEl = Local(el, "transpose");
        if (transEl != null && transposeSet == null)
        {
            int chromatic = int.TryParse(Local(transEl, "chromatic")?.Value, out int c) ? c : 0;
            int octaveChange = int.TryParse(Local(transEl, "octave-change")?.Value, out int oc) ? oc : 0;
            int semis = chromatic + octaveChange * 12;
            if (semis != 0)
            {
                if (semis % 12 == 0)
                    transposeSet = semis;
                else
                    report.Warn(measureNo,
                        $"a transposing part ({semis} semitones) is imported at written pitch — "
                        + "Lily#'s `transposition` states whole octaves only.");
            }
        }
    }

    /// <summary>What <c>staffClefs</c> records for a TAB staff — not a clef word.</summary>
    private const string TabClef = "<tab>";

    /// <summary>How a clef change waits among the marks for the note at its moment.</summary>
    private const string ClefMark = "clef ";

    private static string ClefName(XElement clefEl, ImportReport report, int measureNo)
    {
        string sign = Local(clefEl, "sign")?.Value.Trim().ToUpperInvariant() ?? "G";
        int line = int.TryParse(Local(clefEl, "line")?.Value, out int l) ? l : -1;
        int oct = int.TryParse(Local(clefEl, "clef-octave-change")?.Value, out int o) ? o : 0;

        return (sign, line, oct) switch
        {
            ("G", 2, 0) => "treble",
            ("G", 2, -1) => "treble_8",
            ("G", 2, 1) => "treble^8",
            ("F", 4, 0) => "bass",
            ("F", 4, -1) => "bass_8",
            ("C", 3, _) => "alto",
            ("C", 4, _) => "tenor",
            ("C", 1, _) => "soprano",
            ("C", 2, _) => "mezzosoprano",
            ("C", 5, _) => "baritone",
            ("PERCUSSION", _, _) => "percussion",
            _ => Fallback(),
        };

        string Fallback()
        {
            report.Warn(measureNo, $"unsupported clef {sign}/{line} approximated as treble.");
            return "treble";
        }
    }

    private static ImportNote? ReadNote(XElement el, int divisions, ImportReport report, int measureNo)
    {
        // Grace notes are handled by the caller (attached as leading grace).
        // ⚠️ A <cue/> is NOT dropped any more. It used to be — "cue note dropped." — because
        // Lily# spelled a cue per note and had nowhere sensible to put it; now Lily# has the
        // region LilyPond has, and the serializer groups consecutive cue notes into one.
        var note = new ImportNote
        {
            ChordWithPrev = Local(el, "chord") != null,
            IsCue = Local(el, "cue") != null,
        };

        var (value, dots) = NoteValue(el, divisions);
        note.NoteValue = value;
        note.Dots = dots;

        if (Local(el, "unpitched") != null)
        {
            report.Warn(measureNo, "unpitched (percussion) note approximated as a rest.");
            note.IsRest = true;
            return note;
        }
        if (Local(el, "rest") is { } restEl)
        {
            note.IsRest = true;
            // A rest that is not printed holds its time and shows nothing: Lily#'s spacer
            // `s`, which is how the exporter writes one (MusicXmlNote.PrintObject).
            note.IsSpacer = (string?)el.Attribute("print-object") == "no";
            // A whole-measure rest is the bar's length whatever its <type> says — writers
            // commonly put "whole" on it in any meter — so its value comes from the duration.
            note.IsMeasureRest = (string?)restEl.Attribute("measure") == "yes";
            if (note.IsMeasureRest && ValueFromDuration(el, divisions) is { } bar)
                (note.NoteValue, note.Dots) = bar;
            // A rest carries post-events like a note: `r2@fermata`, `r4@staccato`, and a
            // tuplet may open or close on it. Until 2026-09-30 this returned before reading
            // <notations>, so all of them were dropped.
            if (Local(el, "notations") is { } restNotations)
            {
                ReadMarks(restNotations, note);
                ReadTuplets(restNotations, el, note);
            }
            return note;
        }

        var pitch = Local(el, "pitch");
        if (pitch == null)
        {
            report.Warn(measureNo, "note without pitch dropped.");
            return null;
        }
        string step = Local(pitch, "step")?.Value.Trim().ToUpperInvariant() ?? "C";
        note.Step = "CDEFGAB".IndexOf(step[0]);
        if (note.Step < 0) note.Step = 0;
        note.Alter = (int)Math.Round(ParseDouble(Local(pitch, "alter")?.Value));
        note.Octave = int.TryParse(Local(pitch, "octave")?.Value, out int oct) ? oct : 4;

        if (Math.Abs(note.Alter) > 2)
        {
            report.Warn(measureNo, "quarter-tone / extreme accidental approximated.");
            note.Alter = Math.Clamp(note.Alter, -2, 2);
        }

        // Ties: either the sounding <tie> or the notated <tied> marks a tie.
        foreach (var t in Els(el, "tie").Concat(
                     Els(Local(el, "notations"), "tied")))
        {
            switch ((string?)t.Attribute("type"))
            {
                case "start": note.TieStart = true; break;
                case "stop": note.TieStop = true; break;
            }
        }

        // Slurs, articulations and ornaments live under <notations>.
        var notations = Local(el, "notations");
        if (notations != null)
        {
            // A slur numbered 1 (or unnumbered) is a slur; any other number is a SECOND curve
            // over the same notes, which a voice can only hold as a phrasing slur — slurs do
            // not nest in LilyPond — and number 2 is what the exporter writes a phrasing slur as.
            // LILYPOND-REF: lily/slur-engraver.cc:213-231 can_create_slur — "already have %s".
            foreach (var s in Els(notations, "slur"))
            {
                bool second = (string?)s.Attribute("number") is { } num && num != "1";
                switch ((string?)s.Attribute("type"))
                {
                    case "start" when second:
                        note.PhrasingSlurStart = true;
                        note.PhrasingSlurPlacement = (string?)s.Attribute("placement");
                        break;
                    case "stop" when second: note.PhrasingSlurStop = true; break;
                    case "start": note.SlurStart = true; break;
                    case "stop": note.SlurStop = true; break;
                }
            }
            ReadMarks(notations, note);
            foreach (var o in Local(notations, "ornaments")?.Elements() ?? Enumerable.Empty<XElement>())
            {
                if (OrnamentMark(o.Name.LocalName) is { } mark && !note.Articulations.Contains(mark))
                    note.Articulations.Add(mark);
                // A single-note tremolo (<tremolo type="single">N</tremolo>): N beams
                // print as a :value slash (1 beam = :8, 2 = :16, 3 = :32), on top of the note's
                // own beams: one mark on an eighth is :16, where it read :8, shorter than nothing.
                if (o.Name.LocalName == "tremolo"
                    && (string?)o.Attribute("type") is null or "single"
                    && int.TryParse(o.Value, out int beams) && beams is >= 1 and <= 3)
                    note.TremoloMarks = Math.Max(4, note.NoteValue) << beams;
                if (o.Name.LocalName == "tremolo" && (string?)o.Attribute("type") == "start"
                    && int.TryParse(o.Value, out int pairBeams) && pairBeams is >= 1 and <= 4)
                    note.TremoloPairBeams = pairBeams;
                if (o.Name.LocalName == "tremolo" && (string?)o.Attribute("type") == "stop")
                    note.TremoloPairStop = true;
            }
            // A glissando / slide prints @glissando on the note it leaves from.
            if ((Els(notations, "glissando").Concat(Els(notations, "slide")))
                    .Any(g => (string?)g.Attribute("type") == "start")
                && !note.Articulations.Contains("glissando"))
                note.Articulations.Add("glissando");
            // An arpeggiated chord prints @arpeggio (on the head note).
            if (Local(notations, "arpeggiate") != null && !note.Articulations.Contains("arpeggio"))
                note.Articulations.Add("arpeggio");
            // <technical>: the string number (\N) and a numeric fingering (@finger(N)).
            // Until 2026-09-08 nothing under <technical> was read, so a \N did not round trip.
            if (Local(notations, "technical") is { } technical)
            {
                if (int.TryParse(Local(technical, "string")?.Value, out int str) && str > 0)
                    note.StringNumber = str;
                if (Local(technical, "fingering") is { } fingering)
                {
                    if (int.TryParse(fingering.Value, out int finger) && finger >= 0)
                        note.Fingering = finger;
                    else
                        report.Warn(measureNo, $"fingering '{fingering.Value.Trim()}' is not a number and is dropped.");
                }
            }
            ReadTuplets(notations, el, note);
        }

        foreach (var lyric in Els(el, "lyric"))
            if (ReadLyric(lyric) is { } imported)
                note.Lyrics.Add(imported);

        return note;
    }

    /// <summary>The fermata and the &lt;articulations&gt; marks, for a note or a rest.</summary>
    private static void ReadMarks(XElement notations, ImportNote note)
    {
        // Fermata is a direct <notations> child in real files, but the Lily#
        // exporter nests it under <articulations> (mapped below); handle both, once.
        if (Local(notations, "fermata") != null && !note.Articulations.Contains("fermata"))
            note.Articulations.Add("fermata");
        foreach (var a in Local(notations, "articulations")?.Elements() ?? Enumerable.Empty<XElement>())
            if (ArticulationMark(a.Name.LocalName) is { } mark && !note.Articulations.Contains(mark))
                note.Articulations.Add(mark);
    }

    /// <summary>
    /// Settles a two-note tremolo on its closing note: MusicXML writes each note with half the
    /// sounding span and the WHOLE span's <c>&lt;type&gt;</c>, Lily# the alternation
    /// (<c>repeat tremolo N { a16 b16 }</c>), N pairs at the value the beams name.
    /// </summary>
    /// <remarks>
    /// Read as they stood the two notes were each the whole span long, and the bar overflowed
    /// (kinkel10-4: <c>c''2. fis'2.</c> in 6/8, 18 errors). A span the value does not divide into
    /// whole pairs keeps the notes as written, as does a start with no stop: the bar then reports
    /// itself rather than the import guessing a length.
    /// </remarks>
    private static void SettleTremoloPair(ImportNote note, int ticks, int voice, int divisions,
        Dictionary<int, (ImportNote First, int Ticks)> open)
    {
        if (note.TremoloPairBeams > 0)
        {
            open[voice] = (note, ticks);
            return;
        }
        if (!note.TremoloPairStop || !open.Remove(voice, out var first))
            return;
        int value = 4 << first.First.TremoloPairBeams;
        // Pairs = span / (2 / value of a whole), a whole being 4 × divisions ticks.
        int numerator = (first.Ticks + ticks) * value, denominator = 2 * 4 * Math.Max(1, divisions);
        if (numerator <= 0 || numerator % denominator != 0)
            return;
        first.First.TremoloPairRepeat = numerator / denominator;
        note.TremoloPairEnd = true;
        foreach (var pairNote in new[] { first.First, note })
        {
            pairNote.NoteValue = value;
            pairNote.Dots = 0;
        }
    }

    /// <summary>Tuplet bracket ends on a note or a rest; the ratio comes from
    /// &lt;time-modification&gt;.</summary>
    private static void ReadTuplets(XElement notations, XElement el, ImportNote note)
    {
        foreach (var tup in Els(notations, "tuplet"))
            switch ((string?)tup.Attribute("type"))
            {
                case "start":
                    note.TupletStart = ReadTimeModification(el);
                    note.TupletBare = (string?)tup.Attribute("bracket") == "no" && (string?)tup.Attribute("show-number") == "none";
                    break;
                // A bracket with no <time-modification> changes no durations, so it has no group to
                // open (its start reads null above) and none to close: closing one wrote a stray
                // `}` that unmade the whole file (kinkel19-1: every 16th group, 52 errors).
                case "stop": note.TupletStop = ReadTimeModification(el) != null; break;
            }
    }

    /// <summary>A grace &lt;note&gt; → an <see cref="ImportGraceNote"/> (pitch + written
    /// value + slash), or null for a rest/pitchless grace.</summary>
    private static ImportGraceNote? ReadGraceNote(XElement el)
    {
        var pitch = Local(el, "pitch");
        if (pitch == null)
            return null;
        string step = Local(pitch, "step")?.Value.Trim().ToUpperInvariant() ?? "C";
        int stepIdx = "CDEFGAB".IndexOf(step.Length > 0 ? step[0] : 'C');
        var (value, dots) = NoteValue(el, 1); // grace has no duration → use <type>
        return new ImportGraceNote
        {
            Step = stepIdx < 0 ? 0 : stepIdx,
            Alter = (int)Math.Round(ParseDouble(Local(pitch, "alter")?.Value)),
            Octave = int.TryParse(Local(pitch, "octave")?.Value, out int oct) ? oct : 4,
            NoteValue = value,
            Dots = dots,
            Slash = (string?)Local(el, "grace")?.Attribute("slash") == "yes",
            ChordWithPrev = Local(el, "chord") != null,
            // The slur a grace group carries into its main note (`grace { d16( } e4)`); the
            // stop is the main note's own <slur type="stop">. Not read until 2026-09-30.
            SlurStart = Els(Local(el, "notations"), "slur").Any(s =>
                (string?)s.Attribute("type") == "start"
                && (string?)s.Attribute("number") is null or "1"),
        };
    }

    /// <summary>Dynamic mark names from a &lt;direction&gt;: each &lt;dynamics&gt; level
    /// (f, ff, p, mf, …) and an OPENING hairpin wedge (crescendo → cresc, diminuendo →
    /// decresc). A wedge stop is read apart (<see cref="EndWedges"/>), but for a diminuendo's al
    /// niente stop, which is a mark.</summary>
    private static IEnumerable<string> ReadDirectionDynamics(XElement dir)
    {
        // Every <direction-type>: one direction may hold "cresc." and its wedge side by side, and
        // until 2026-11-04 only the first was read, so the wedge after a word was lost.
        foreach (var dt in Els(dir, "direction-type"))
        {
            foreach (var dyn in Els(dt, "dynamics"))
                foreach (var level in dyn.Elements())
                    if (level.Name.LocalName != "other-dynamics")
                        // <n/> is MusicXML's niente.
                        yield return level.Name.LocalName == "n" ? "niente" : level.Name.LocalName;
            foreach (var wedge in Els(dt, "wedge"))
            {
                // niente="yes" is the circle at the tip: a crescendo from nothing, or the stop of
                // a diminuendo to nothing — Lily#'s @niente at that end.
                bool niente = (string?)wedge.Attribute("niente") == "yes";
                switch ((string?)wedge.Attribute("type"))
                {
                    case "crescendo":
                        if (niente) yield return "niente";
                        yield return "cresc";
                        break;
                    case "diminuendo": yield return "decresc"; break;
                    case "stop":
                        if (niente) yield return "niente";
                        break;
                }
            }
        }
    }

    /// <summary>A span end that includes the note it is written on — an octave line's
    /// (<c>!ottava</c>, <c>!quindicesima</c>): it goes on the last note the line covers, not on
    /// the next one as a pedal's release does.</summary>
    private static bool EndsWithItsNote(string mark)
        => mark.StartsWith('!')
           && Svg.Model.MusicMarkItem.ParseSpanEndName(mark[1..]) == Svg.Model.MusicMarkType.OttavaStop;
    /// <summary>
    /// The lines a &lt;direction&gt; opens and closes: an &lt;octave-shift&gt; as
    /// <c>ottava</c> / <c>ottava(bassa)</c> / <c>quindicesima</c>(…) and its stop as
    /// the end of the line by the name it opened with (<c>!quindicesima</c>; LYS4032); a &lt;pedal&gt; as <c>sustain</c> / <c>sostenuto</c> and its stop as
    /// the end of the pedal that is down (MusicXML has one stop for both). Until 2026-09-30
    /// neither was read: every <c>@ottava</c> and <c>@sustain</c> was lost on import.
    /// </summary>
    /// <remarks>
    /// MusicXML's octave-shift names the way the notes are MOVED on the page: "down" is an
    /// 8va (the notes sound an octave above where they stand), "up" an 8vb — the spelling
    /// <c>MusicXmlExporter.ProcessDirectionName</c> writes. A pedal <c>change</c> is Lily#'s
    /// start again while the pedal is down (<c>g,4@sustain</c>). <paramref name="pedals"/>
    /// holds the part's pedals that are down, so a stop closes the latest one.
    /// </remarks>

    private static IEnumerable<string> ReadDirectionSpans(XElement dir, List<string> pedals, List<string> ottava, ImportReport report, int measureNo)
    {
        foreach (var dt in Els(dir, "direction-type"))
        {
            foreach (var shift in Els(dt, "octave-shift"))
            {
                string type = (string?)shift.Attribute("type") ?? "";
                if (type == "stop")
                {
                    // Closed by the name it opened with (owner's decision 2026-10-28, LYS4032).
                    yield return "!" + (ottava.Count > 0 ? ottava[^1] : "8va");
                    ottava.Clear();
                    continue;
                }
                if (type is not ("up" or "down"))
                    continue;
                // MusicXML's "up" moves the notes UP on the page: the music sounds lower, 8vb.
                string? name = ((string?)shift.Attribute("size"), type) switch
                {
                    (null or "8", "down") => "8va",
                    (null or "8", _) => "8vb",
                    ("15", "down") => "15ma",
                    ("15", _) => "15mb",
                    _ => null,
                };
                if (name == null)
                {
                    report.Warn(measureNo, $"An octave line of size {(string?)shift.Attribute("size")} has no Lily# spelling (8 or 15); it is dropped.");
                    continue;
                }
                ottava.Clear();
                ottava.Add(name);
                yield return name;
            }
            foreach (var pedal in Els(dt, "pedal"))
            {
                switch ((string?)pedal.Attribute("type"))
                {
                    case "start" or "change":
                        if (!pedals.Contains("sustain"))
                            pedals.Add("sustain");
                        yield return "sustain";
                        break;
                    case "sostenuto":
                        if (!pedals.Contains("sostenuto"))
                            pedals.Add("sostenuto");
                        yield return "sostenuto";
                        break;
                    // "discontinue" ends the line with no lift drawn; Lily# has one end for both.
                    // Until 2026-11-04 it was not read, and the pedal was never released.
                    case "stop" or "discontinue":
                        string down = pedals.Count > 0 ? pedals[^1] : "sustain";
                        pedals.Remove(down);
                        yield return "!" + down;
                        break;
                }
            }
        }
    }

    /// <summary>
    /// The text marks of a &lt;direction&gt;: its &lt;words&gt; as <c>text("…")</c> (<c>.up</c>
    /// when the direction is placed above — <c>@text</c> stands below by default), the una
    /// corda pair as their own names, and a &lt;rehearsal&gt; as <c>mark("…")</c>.
    /// </summary>
    /// <remarks>
    /// Until 2026-09-30 neither was read: <c>R1@text("tacet")</c>, <c>c4@mark("A")</c> and
    /// <c>@unaCorda</c> did not come back, and neither did any other program's "dolce" or
    /// "rit.". Several &lt;words&gt; in one &lt;direction-type&gt; are one text in several
    /// fonts, so they join. A word the source pairs with a jump (<c>&lt;sound dacapo&gt;</c>
    /// and its kin) is still imported as text — the jump itself is a <c>form</c> matter
    /// (<c>dc</c>, <c>ds</c>, <c>fine</c>, <c>coda</c>), and the report says so.
    /// </remarks>
    private static IEnumerable<string> ReadDirectionTexts(XElement dir, ImportReport report, int measureNo)
    {
        bool above = (string?)dir.Attribute("placement") == "above";
        var sound = Local(dir, "sound");
        bool jump = sound != null && new[] { "dacapo", "dalsegno", "fine", "tocoda" }
            .Any(a => sound.Attribute(a) != null);
        foreach (var dt in Els(dir, "direction-type"))
        {
            string text = string.Join(" ", string.Concat(Els(dt, "words").Select(w => w.Value))
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (text.Length > 0)
            {
                if (text == "una corda")
                    yield return "unaCorda";
                else if (text == "tre corde")
                    yield return "treCorde";
                else
                {
                    if (jump)
                        report.Warn(measureNo, $"'{text}' is a jump in the source; it is imported as text only — write the jump in the form (dc, ds, fine, coda).");
                    yield return "text(\"" + LysWriter.EscapeString(text) + "\")" + (above ? ".up" : "");
                }
            }
            foreach (var r in Els(dt, "rehearsal"))
                if (r.Value.Trim() is { Length: > 0 } label)
                    yield return "mark(\"" + LysWriter.EscapeString(label) + "\")";
        }
    }

    /// <summary>Greedily splits a tick duration into undotted rest note-values
    /// (largest first), e.g. a dotted-half gap → a half rest + a quarter rest. Used to
    /// fill a &lt;forward&gt; gap.</summary>
    private static IEnumerable<(int value, int dots)> DecomposeDuration(int duration, int divisions)
    {
        int wholeTicks = 4 * Math.Max(1, divisions);
        int remaining = duration;
        foreach (int value in new[] { 1, 2, 4, 8, 16, 32, 64, 128 })
        {
            // A value that is no whole number of ticks is not this gap's: at divisions 3 an eighth
            // is 1.5 ticks, and read as 1 a triplet's gap came out twice as long as it is.
            if (wholeTicks % value != 0)
                continue;
            int ticks = wholeTicks / value;
            while (remaining >= ticks)
            {
                yield return (value, 0);
                remaining -= ticks;
            }
        }
    }

    /// <summary>The tuplet ratio (actual, normal) from a note's
    /// &lt;time-modification&gt;, or null when absent/malformed.</summary>
    private static (int Actual, int Normal)? ReadTimeModification(XElement el)
    {
        var tm = Local(el, "time-modification");
        if (tm != null
            && int.TryParse(Local(tm, "actual-notes")?.Value, out int a) && a > 0
            && int.TryParse(Local(tm, "normal-notes")?.Value, out int n) && n > 0)
            return (a, n);
        return null;
    }

    /// <summary>A MusicXML &lt;articulations&gt; child to a Lily# mark, or null when
    /// unsupported (Tier 1 covers the common set).</summary>
    private static string? ArticulationMark(string name) => name switch
    {
        "staccato" => "staccato",
        "accent" => "accent",
        "tenuto" => "tenuto",
        "strong-accent" => "marcato",
        "staccatissimo" => "staccatissimo",
        "detached-legato" => "portato",
        "fermata" => "fermata",   // the Lily# exporter nests fermata here
        _ => null,
    };

    /// <summary>A MusicXML &lt;ornaments&gt; child to a Lily# mark, or null when
    /// unsupported.</summary>
    private static string? OrnamentMark(string name) => name switch
    {
        "trill-mark" => "trill",
        "mordent" => "mordent",
        "inverted-mordent" => "prall",
        "turn" => "turn",
        "inverted-turn" => "reverseTurn",
        _ => null,
    };

    /// <summary>The written note value (1/2/4/8/...) and dot count. Prefers the
    /// explicit <c>&lt;type&gt;</c>; falls back to decoding <c>duration/divisions</c>
    /// when the file omits a type.</summary>
    private static (int Value, int Dots) NoteValue(XElement el, int divisions)
    {
        int dots = Els(el, "dot").Count();
        string? type = Local(el, "type")?.Value.Trim().ToLowerInvariant();
        int fromType = type switch
        {
            "breve" or "double-whole" => 1, // no plain Lily# breve token in Tier 1
            "whole" => 1,
            "half" => 2,
            "quarter" => 4,
            "eighth" => 8,
            "16th" => 16,
            "32nd" => 32,
            "64th" => 64,
            "128th" => 128,
            _ => 0,
        };
        if (fromType > 0)
            return (fromType, dots);

        // No usable <type>: recover a value+dots from the sounding duration.
        return ValueFromDuration(el, divisions) ?? (4, dots);
    }

    /// <summary>The value+dots whose length is the element's &lt;duration&gt;, or null when
    /// none is (a 5/4 bar, a tuplet member).</summary>
    private static (int Value, int Dots)? ValueFromDuration(XElement el, int divisions)
    {
        if (int.TryParse(Local(el, "duration")?.Value, out int ticks) && ticks > 0 && divisions > 0)
        {
            var frac = new Fraction(ticks, divisions * 4); // fraction of a whole note
            foreach (int baseVal in new[] { 1, 2, 4, 8, 16, 32, 64 })
                for (int k = 0; k <= 2; k++)
                    if (Fraction.FromNoteValue(baseVal).Dotted(k) == frac)
                        return (baseVal, k);
        }
        return null;
    }

    private static ImportLyric? ReadLyric(XElement el)
    {
        // One syllable from all its texts, an elision standing as Lily#'s `~`: an <elision>, or the
        // private-use glyph MuseScore writes as a <text> of its own in a music font. Until 2026-11-04
        // both vanished, and "E‿o" came back "Eo".
        if (!Els(el, "text").Any())
            return null;
        var joined = new System.Text.StringBuilder();
        foreach (var part in el.Elements())
        {
            if (part.Name.LocalName == "elision")
                joined.Append('~');
            else if (part.Name.LocalName == "text")
                joined.Append(System.Text.RegularExpressions.Regex.Replace(part.Value, "[\uE000-\uF8FF]+", "~"));
        }
        string text = joined.ToString();
        if (string.IsNullOrWhiteSpace(text))
            return null;
        int verse = int.TryParse((string?)el.Attribute("number"), out int n) ? n : 1;
        string syllabic = Local(el, "syllabic")?.Value.Trim().ToLowerInvariant() ?? "single";
        bool extend = Local(el, "extend") != null;
        return new ImportLyric(verse, text, syllabic, extend);
    }

    private static ImportHarmony? ReadHarmony(XElement el)
    {
        var rootEl = Local(el, "root");
        if (rootEl == null)
            return null; // function-based harmony not supported
        string step = Local(rootEl, "root-step")?.Value.Trim().ToUpperInvariant() ?? "C";
        int stepIdx = "CDEFGAB".IndexOf(step.Length > 0 ? step[0] : 'C');
        if (stepIdx < 0)
            return null;

        var h = new ImportHarmony
        {
            RootStep = stepIdx,
            RootAlter = (int)Math.Round(ParseDouble(Local(rootEl, "root-alter")?.Value)),
        };

        var kindEl = Local(el, "kind");
        h.Kind = kindEl?.Value.Trim().ToLowerInvariant() ?? "major";
        h.KindText = (string?)kindEl?.Attribute("text");

        var bassEl = Local(el, "bass");
        if (bassEl != null)
        {
            string bstep = Local(bassEl, "bass-step")?.Value.Trim().ToUpperInvariant() ?? "C";
            int bidx = "CDEFGAB".IndexOf(bstep.Length > 0 ? bstep[0] : 'C');
            if (bidx >= 0)
            {
                h.BassStep = bidx;
                h.BassAlter = (int)Math.Round(ParseDouble(Local(bassEl, "bass-alter")?.Value));
            }
        }
        foreach (var degreeEl in Els(el, "degree"))
        {
            if (!int.TryParse(Local(degreeEl, "degree-value")?.Value.Trim(), out int value))
                continue;
            int alter = (int)Math.Round(ParseDouble(Local(degreeEl, "degree-alter")?.Value));
            string type = Local(degreeEl, "degree-type")?.Value.Trim().ToLowerInvariant() ?? "add";
            h.Degrees.Add(new ImportDegree(value, alter, type));
        }
        return h;
    }

    private static ImportFiguredBass? ReadFiguredBass(XElement el)
    {
        var fb = new ImportFiguredBass();
        foreach (var figEl in Els(el, "figure"))
        {
            if (Local(figEl, "extend") != null && Local(figEl, "figure-number") == null)
            {
                fb.Figures.Add(new ImportFigure(0, 0, Held: true));
                continue;
            }
            int number = int.TryParse(Local(figEl, "figure-number")?.Value, out int n) ? n : 0;
            // An accidental rides either the <suffix> (numbered figures) or the
            // <prefix> (a bare accidental); both encode the same alteration.
            int alter = AccidentalValue(Local(figEl, "suffix")?.Value)
                        ?? AccidentalValue(Local(figEl, "prefix")?.Value)
                        ?? 0;
            if (number == 0 && alter == 0)
                continue;
            fb.Figures.Add(new ImportFigure(number, alter, Held: false));
        }
        return fb.Figures.Count > 0 ? fb : null;
    }

    private static int? AccidentalValue(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        "sharp" => 1,
        "flat" => -1,
        "natural" => 2,
        "double-sharp" or "sharp-sharp" => 1,   // Tier 1 approximates doubles as single
        "flat-flat" => -1,
        _ => null,
    };

    private static void ReadBarline(XElement el, ImportMeasure measure)
    {
        string location = (string?)el.Attribute("location") ?? "right";
        var repeat = Local(el, "repeat");
        string? barStyle = Local(el, "bar-style")?.Value.Trim();
        var ending = Local(el, "ending");
        string? endingType = (string?)ending?.Attribute("type");

        if (location == "left")
        {
            if ((string?)repeat?.Attribute("direction") == "forward")
                measure.RepeatForward = true;
            // A volta bracket opens here (first ending = "1", second = "2", ...).
            if (endingType == "start"
                && int.TryParse((string?)ending?.Attribute("number"), out int num))
                measure.EndingStart = num;
            return;
        }

        // A volta bracket closes at this measure's right barline.
        if (endingType is "stop" or "discontinue")
            measure.EndingStop = true;

        if ((string?)repeat?.Attribute("direction") == "backward")
            measure.BarlineRight = BarlineKind.RepeatEnd;
        else
            measure.BarlineRight = barStyle switch
            {
                "light-light" => BarlineKind.Double,
                "light-heavy" => BarlineKind.Final,
                _ => measure.BarlineRight,
            };
    }

    private static int? ReadDirectionTempo(XElement el)
    {
        // Prefer the machine-readable <sound tempo="...">; fall back to the
        // printed metronome mark.
        if (int.TryParse((string?)Local(el, "sound")?.Attribute("tempo"), out int t) && t > 0)
            return t;
        var metro = Local(Local(el, "direction-type"), "metronome");
        if (metro != null && int.TryParse(Local(metro, "per-minute")?.Value, out int pm) && pm > 0)
        {
            // <sound tempo> is always quarter-note BPM; a metronome mark counts
            // <beat-unit> beats, so scale per-minute to quarter-BPM. A dotted
            // beat-unit (e.g. dotted-quarter = 90) lengthens the beat by 1.5 per dot.
            int beatUnitValue = (Local(metro, "beat-unit")?.Value.Trim().ToLowerInvariant()) switch
            {
                "breve" or "double-whole" => 1,
                "whole" => 1,
                "half" => 2,
                "quarter" => 4,
                "eighth" => 8,
                "16th" => 16,
                "32nd" => 32,
                "64th" => 64,
                "128th" => 128,
                _ => 4, // absent/unknown: treat as quarter (no scaling)
            };
            double dotFactor = 1.0;
            int beatDots = Els(metro, "beat-unit-dot").Count();
            for (int i = 0; i < beatDots; i++)
                dotFactor += 1.0 / (1 << (i + 1)); // +1/2, +1/4, ...
            int quarterBpm = (int)Math.Round(pm * (4.0 / beatUnitValue) * dotFactor);
            return quarterBpm > 0 ? quarterBpm : null;
        }
        return null;
    }

    // ---- .mxl container ---------------------------------------------------

    private static string UnzipMxl(byte[] bytes)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);

        // META-INF/container.xml names the real rootfile.
        string? rootPath = null;
        var container = zip.GetEntry("META-INF/container.xml");
        if (container != null)
        {
            using var cs = container.Open();
            var cdoc = XDocument.Load(cs);
            rootPath = Els(Local(cdoc.Root, "rootfiles"), "rootfile")
                .Select(r => (string?)r.Attribute("full-path"))
                .FirstOrDefault(p => !string.IsNullOrEmpty(p));
        }

        var entry = (rootPath != null ? zip.GetEntry(rootPath) : null)
            // Fall back to the first non-META .xml/.musicxml entry.
            ?? zip.Entries.FirstOrDefault(e =>
                !e.FullName.StartsWith("META-INF/", StringComparison.Ordinal)
                && (e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
                    || e.FullName.EndsWith(".musicxml", StringComparison.OrdinalIgnoreCase)))
            ?? throw new FormatException("No score file found inside the .mxl archive.");

        using var es = entry.Open();
        using var reader = new StreamReader(es);
        return reader.ReadToEnd();
    }

    // ---- page layout ------------------------------------------------------

    /// <summary>
    /// Reads <c>&lt;defaults&gt;&lt;page-layout&gt;</c> into millimetres, or null when the
    /// document states no page. Only the keys the source states are filled — an absent
    /// margin stays null and the paper block's own default covers it.
    /// </summary>
    /// <remarks>
    /// <c>&lt;scaling&gt;</c> is the ONLY bridge from tenths to a physical unit, so a
    /// page-layout without a usable scaling is dropped with a warning rather than
    /// converted by a guessed scale — the report's contract is "never emitted wrong".
    /// <para>
    /// The same scaling states the STAFF size (40 tenths = one staff height), which
    /// Lily# deliberately does not have as a knob (GRAMMAR 2.5: the staff space is the
    /// unit itself). A source staff of a DIFFERENT size is said out loud, because the
    /// page is kept while the music on it is not the source's size — the one honest
    /// combination is page-as-stated plus a warning. "Different" is read at whole-point
    /// resolution: Lily#'s staff is 20 TeX points = 19.93 DTP points and the common
    /// MusicXML default (7.05556mm per 40 tenths) is 20.00 DTP points — the 0.4% gap is
    /// the TeX-vs-DTP point, a spelling difference, not a size choice, and both round
    /// to 20pt; any deliberate size choice sits whole points away.
    /// </para>
    /// </remarks>
    private static ImportPaper? ReadPageLayout(XElement root, ImportReport report)
    {
        var defaults = Local(root, "defaults");
        var pageLayout = Local(defaults, "page-layout");
        if (pageLayout == null)
            return null;

        var scaling = Local(defaults, "scaling");
        double millimeters = ParseDouble(Local(scaling, "millimeters")?.Value);
        double tenths = ParseDouble(Local(scaling, "tenths")?.Value);
        if (millimeters <= 0 || tenths <= 0)
        {
            report.Warn("the <page-layout> is stated in tenths but the document has no "
                + "usable <scaling>, so the page dimensions cannot be converted and are dropped.");
            return null;
        }
        double mmPerTenth = millimeters / tenths;

        double staffPt = mmPerTenth * 40.0 * 72.0 / 25.4;
        if (Math.Round(staffPt) != 20)
            report.Warn($"the source staff is about {Math.Round(staffPt)}pt; Lily# engraves "
                + "a fixed 20pt staff, so the music will fill the stated page differently.");

        var paper = new ImportPaper();
        if (Local(pageLayout, "page-width") is { } w)
            paper.WidthMm = ParseDouble(w.Value) * mmPerTenth;
        if (Local(pageLayout, "page-height") is { } h)
            paper.HeightMm = ParseDouble(h.Value) * mmPerTenth;

        // <page-margins> comes as one set for both parities, or an odd/even pair.
        // Lily#'s margins are per-document, so the odd (first-page) set wins and a
        // DIFFERING even set is reported, not silently averaged or dropped.
        var marginSets = Els(pageLayout, "page-margins").ToList();
        var odd = marginSets.FirstOrDefault(m => ((string?)m.Attribute("type") ?? "both") != "even");
        var even = marginSets.FirstOrDefault(m => (string?)m.Attribute("type") == "even");
        var margins = odd ?? even;
        if (margins != null)
        {
            paper.LeftMm = MarginMm(margins, "left-margin", mmPerTenth);
            paper.RightMm = MarginMm(margins, "right-margin", mmPerTenth);
            paper.TopMm = MarginMm(margins, "top-margin", mmPerTenth);
            paper.BottomMm = MarginMm(margins, "bottom-margin", mmPerTenth);

            if (odd != null && even != null && !SameMargins(odd, even))
                report.Warn("the even-page margins differ from the odd-page ones; Lily# "
                    + "margins are per-document, so the odd-page set is used.");
            // A lone type="odd" set means even pages MIRROR it (left and right swap) —
            // the identity when the sides are equal, unrepresentable when they are not.
            else if (even == null && (string?)odd?.Attribute("type") == "odd"
                     && paper.LeftMm != paper.RightMm)
                report.Warn("the margins are stated for odd pages only (even pages mirror "
                    + "them); Lily# margins are per-document, so every page gets the odd-page set.");
        }
        return paper;
    }

    private static double? MarginMm(XElement margins, string name, double mmPerTenth)
        => Local(margins, name) is { } el ? ParseDouble(el.Value) * mmPerTenth : null;

    private static bool SameMargins(XElement a, XElement b)
        => new[] { "left-margin", "right-margin", "top-margin", "bottom-margin" }
            .All(n => ParseDouble(Local(a, n)?.Value) == ParseDouble(Local(b, n)?.Value));

    // ---- helpers ----------------------------------------------------------

    // Words a GENERATED part identifier avoids. The parser's own rule is the probe below
    // (LexesAsOneName); this table is the importer's taste on top of it: since 2026-10-03
    // a part may be named almost any word (a pitch letter, the rest letters, a dynamic —
    // SyntaxFacts.IsPartNameToken), but `part s` or `part f` at the head of a generated
    // book reads as a spacer or a note to anyone who opens it, so a source part named "S",
    // "F" or "P" takes its index name (part3) instead. Before that date these words also
    // failed to parse ("found 'DynamicP'"), which is how the table began.
    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        "part", "section", "score", "staff", "structure", "chords", "lyrics",
        "time", "key", "clef", "tempo", "octave", "title", "composer", "subtitle", "poet",
        "phrase", "drummap", "grace", "partial", "repeat", "tuplet",
        "acciaccatura", "appoggiatura", "afterGrace",
        "a", "b", "c", "d", "e", "f", "g", "r", "s",
        "ppp", "pp", "p", "mp", "mf", "ff", "fff", "sf", "sfz", "fp", "sfp", "fz", "rfz", "rf", "sffz",
    };

    /// <summary>Turns a MusicXML part name into a valid, unique Lily# identifier
    /// (letters/digits, starting with a letter), falling back to <c>part{index}</c>.</summary>
    private static string SafeIdentifier(string? name, int index, HashSet<string> used)
    {
        string cleaned = new string((name ?? "")
            .Where(char.IsLetterOrDigit).ToArray());
        if (cleaned.Length > 0 && char.IsLetter(cleaned[0]))
            cleaned = char.ToLowerInvariant(cleaned[0]) + cleaned.Substring(1);
        else
            cleaned = "";

        if (cleaned.Length == 0 || Reserved.Contains(cleaned) || !LexesAsOneName(cleaned))
            cleaned = $"part{index}";

        string candidate = cleaned;
        int suffix = 2;
        while (!used.Add(candidate))
            candidate = cleaned + suffix++;
        return candidate;
    }

    /// <summary>Whether <paramref name="candidate"/> works as a part's name everywhere the
    /// import writes one — asked of the parser itself, with a book that uses it in each place.
    /// The table above could not keep up with the language: a part named "S" came back as
    /// <c>part s</c>, a spacer, and the whole imported book failed to parse (2026-09-30).
    /// The lexer alone is too strict — a clef word (<c>bass</c>, <c>soprano</c>) is a keyword
    /// to it and still a good part name.</summary>
    private static bool LexesAsOneName(string candidate)
        => !Syntax.SyntaxTree.Parse($"part {candidate} {{ }}\nsection A {{ {candidate} {{ c1 | }} }}\n"
                + $"form {{ ~A }}\nscore {{ staff {candidate} }}\n")
            .Diagnostics.Any(d => d.Severity == Syntax.DiagnosticSeverity.Error);

    private static double ParseDouble(string? s)
        => double.TryParse(s, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double d) ? d : 0;

    /// <summary>First child element with the given LOCAL name (namespace-agnostic).</summary>
    private static XElement? Local(XElement? parent, string localName)
        => parent?.Elements().FirstOrDefault(e => e.Name.LocalName == localName);

    /// <summary>All child elements with the given LOCAL name (namespace-agnostic).</summary>
    private static IEnumerable<XElement> Els(XElement? parent, string localName)
        => parent?.Elements().Where(e => e.Name.LocalName == localName) ?? Enumerable.Empty<XElement>();
}
