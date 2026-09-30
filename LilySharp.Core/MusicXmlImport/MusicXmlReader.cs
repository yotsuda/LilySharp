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
            foreach (var p in ReadPart(partEl, working, doc, report))
            {
                p.SafeName = SafeIdentifier(p.Name, index, used);
                doc.Parts.Add(p);
            }
        }

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

    private static List<ImportPart> ReadPart(XElement partEl, ImportPart part, ImportDocument doc, ImportReport report)
    {
        int divisions = 1;              // ticks per quarter; may change mid-part
        string? clefSet = null;         // most recent clef, for the part header
        int? transposeSet = null;       // the part's <transpose>, in semitones
        int measureNo = 0;
        var staffClefs = new Dictionary<int, string>();   // staff number -> clef
        var voiceStaff = new Dictionary<int, int>();       // voice number -> its staff
        // A span's end (`!ottava`, `!sustain`) standing past a bar's last onset — where a
        // MusicXML stop usually stands, after the last note it covers — closes on the next
        // note of its staff, in the next bar: on the bar's last note it would end the line a
        // note early (the note carrying `@!ottava` is outside the line).
        var carriedEnds = new List<(string Mark, int? Staff)>();
        var lastNoteOfStaff = new Dictionary<int, ImportNote>();
        ImportNote? partLastNote = null;
        var pedals = new List<string>();                   // the pedals down, oldest first

        foreach (var measEl in Els(partEl, "measure"))
        {
            measureNo++;
            var measure = new ImportMeasure
            {
                Implicit = (string?)measEl.Attribute("implicit") == "yes",
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
            var pendingGrace = new List<ImportGraceNote>();
            int position = 0;           // divisions from the bar's head, along the stream
            ImportNote? lastNote = null;

            void AttachDue(ImportNote note, int staff)
            {
                bool Due((string Dynamic, int At, int? Staff) p)
                    => p.At <= position && (p.Staff == null || p.Staff == staff);
                int k = 0;
                for (int i = 0; i < pendingDynamics.Count; i++)
                    if (Due(pendingDynamics[i]))
                        note.Articulations.Insert(k++, pendingDynamics[i].Dynamic);
                pendingDynamics.RemoveAll(Due);
            }

            foreach (var el in measEl.Elements())
            {
                switch (el.Name.LocalName)
                {
                    case "attributes":
                        ReadAttributes(el, measure, ref divisions, ref clefSet, ref transposeSet,
                                       staffClefs, report, measureNo);
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
                        // The direction's text and its lines ride the same queue: they mark
                        // the note at their position exactly as a dynamic does.
                        foreach (var text in ReadDirectionTexts(el, report, measureNo))
                            pendingDynamics.Add((text, at, dirStaff));
                        foreach (var span in ReadDirectionSpans(el, pedals, report, measureNo))
                            pendingDynamics.Add((span, at, dirStaff));
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
                        int fvoice = int.TryParse(Local(el, "voice")?.Value, out int fv) ? fv : lastVoice;
                        int fdur = int.TryParse(Local(el, "duration")?.Value, out int fd) ? fd : 0;
                        foreach (var (value, dots) in DecomposeDuration(fdur, divisions))
                            measure.Voice(fvoice).Add(new ImportNote { IsRest = true, NoteValue = value, Dots = dots });
                        position += fdur;
                        break;
                    }

                    case "note":
                    {
                        int voice = int.TryParse(Local(el, "voice")?.Value, out int v) ? v : 1;
                        lastVoice = voice;
                        // Remember which staff each voice sits on; a part spanning
                        // several staves is split into a grand staff below.
                        int staff = int.TryParse(Local(el, "staff")?.Value, out int st) ? st : 1;
                        voiceStaff[voice] = staff;
                        // A grace note has no duration — accumulate it to attach to the
                        // next real note as a leading acciaccatura/grace block.
                        if (Local(el, "grace") != null)
                        {
                            if (ReadGraceNote(el) is { } g)
                                pendingGrace.Add(g);
                            break;
                        }
                        var note = ReadNote(el, divisions, report, measureNo);
                        if (note == null)
                            break;
                        var target = measure.Voice(voice);
                        // A chord member folds onto the previous note; harmony /
                        // figured-bass / dynamics / grace attach to the head note only.
                        if (!note.ChordWithPrev)
                        {
                            target.AddRange(pendingAnnotations);
                            pendingAnnotations.Clear();
                            AttachDue(note, staff);
                            note.LeadingGrace.AddRange(pendingGrace);
                            pendingGrace.Clear();
                            position += int.TryParse(Local(el, "duration")?.Value, out int ndur) ? ndur : 0;
                            lastNote = note;
                            lastNoteOfStaff[staff] = note;
                            partLastNote = note;
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
                if (dyn.StartsWith('!'))
                    carriedEnds.Add((dyn, dynStaff));
                else if ((dynStaff is { } s && lastNoteOfStaff.TryGetValue(s, out var own) ? own : lastNote) is { } host)
                    host.Articulations.Add(dyn);
            }
            pendingDynamics.Clear();

            part.Measures.Add(measure);
        }
        // An end after the part's last note has no next note to stand on: its staff's last
        // note carries it — Lily# requires the end, and a line one note short beats none.
        foreach (var (end, endStaff) in carriedEnds)
            if ((endStaff is { } s && lastNoteOfStaff.TryGetValue(s, out var own) ? own
                    : partLastNote) is { } host)
                host.Articulations.Add(end);

        part.Clef = clefSet ?? "treble";
        part.TranspositionSemitones = TranspositionBeyondClef(transposeSet, part.Clef);

        // A part whose voices span more than one staff (a piano grand staff) splits
        // into one Lily# part per staff, grouped into a grandStaff by the score.
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
            return new List<ImportPart> { part };
        return SplitByStaff(part, staves, voiceStaff, staffClefs, transposeSet);
    }

    /// <summary>
    /// What the part's <c>transposition</c> property must state, given the document's whole
    /// <c>&lt;transpose&gt;</c> and the clef word being written for it: the remainder, since
    /// an octave clef word (<c>treble_8</c>) already carries its own share.
    /// </summary>
    /// <remarks>
    /// The two halves of the round trip are this and
    /// <c>MusicXmlExporter.ApplyPartHeader</c>, which writes clef octave + instrument as one
    /// number. Change either alone and a guitar moves an octave in one direction or two in
    /// the other. Null in, null out — a part with no <c>&lt;transpose&gt;</c> states nothing.
    /// </remarks>
    private static int? TranspositionBeyondClef(int? transposeTotal, string clefWord)
        => transposeTotal is { } total
            ? total - Tablature.Tunings.ClefOctaveShift(PartHeaderDefaults.ParseClefWord(clefWord))
            : null;

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
                // written pitch. Each staff subtracts its OWN clef's share.
                TranspositionSemitones = TranspositionBeyondClef(transposeTotal, staffClef),
            };
            foreach (var measure in part.Measures)
            {
                var m = new ImportMeasure
                {
                    Implicit = measure.Implicit,
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
        Dictionary<int, string> staffClefs, ImportReport report, int measureNo)
    {
        if (int.TryParse(Local(el, "divisions")?.Value, out int d) && d > 0)
            divisions = d;

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
            measure.Clef = name;        // mid-piece single-staff clef change
            clefSet ??= name;           // first clef becomes the part header clef
        }

        // <transpose> is the part's WHOLE written→sounding shift — the instrument's own AND
        // the octave a transposing clef carries, which is how every publisher writes a guitar
        // (<clef-octave-change> −1 and <transpose> −12 on the same part). ⚠️ It is kept RAW
        // here and the clef's share is subtracted once at the end of ReadPart, because the
        // clef WORD written out already carries it: taking this at face value spelled
        // `clef treble_8 transposition 8vb` and dropped a guitar two octaves.
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
                // print as a :value slash (1 beam = :8, 2 = :16, 3 = :32).
                if (o.Name.LocalName == "tremolo"
                    && (string?)o.Attribute("type") is null or "single"
                    && int.TryParse(o.Value, out int beams) && beams is >= 1 and <= 3)
                    note.TremoloMarks = 4 << beams;
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

    /// <summary>Tuplet bracket ends on a note or a rest; the ratio comes from
    /// &lt;time-modification&gt;.</summary>
    private static void ReadTuplets(XElement notations, XElement el, ImportNote note)
    {
        foreach (var tup in Els(notations, "tuplet"))
            switch ((string?)tup.Attribute("type"))
            {
                case "start": note.TupletStart = ReadTimeModification(el); break;
                case "stop": note.TupletStop = true; break;
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
    /// decresc). A wedge stop is not a mark.</summary>
    private static IEnumerable<string> ReadDirectionDynamics(XElement dir)
    {
        var dt = Local(dir, "direction-type");
        if (dt == null)
            yield break;
        foreach (var dyn in Els(dt, "dynamics"))
            foreach (var level in dyn.Elements())
                if (level.Name.LocalName != "other-dynamics")
                    yield return level.Name.LocalName;
        foreach (var wedge in Els(dt, "wedge"))
            switch ((string?)wedge.Attribute("type"))
            {
                case "crescendo": yield return "cresc"; break;
                case "diminuendo": yield return "decresc"; break;
            }
    }

    /// <summary>
    /// The lines a &lt;direction&gt; opens and closes: an &lt;octave-shift&gt; as
    /// <c>ottava</c> / <c>ottava(bassa)</c> / <c>quindicesima</c>(…) and its stop as
    /// <c>!ottava</c>; a &lt;pedal&gt; as <c>sustain</c> / <c>sostenuto</c> and its stop as
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
    private static IEnumerable<string> ReadDirectionSpans(XElement dir, List<string> pedals, ImportReport report, int measureNo)
    {
        foreach (var dt in Els(dir, "direction-type"))
        {
            foreach (var shift in Els(dt, "octave-shift"))
            {
                string type = (string?)shift.Attribute("type") ?? "";
                if (type == "stop")
                {
                    yield return "!ottava";
                    continue;
                }
                if (type is not ("up" or "down"))
                    continue;
                string? name = (string?)shift.Attribute("size") switch
                {
                    null or "8" => "ottava",
                    "15" => "quindicesima",
                    _ => null,
                };
                if (name == null)
                {
                    report.Warn(measureNo, $"An octave line of size {(string?)shift.Attribute("size")} has no Lily# spelling (8 or 15); it is dropped.");
                    continue;
                }
                yield return type == "up" ? name + "(bassa)" : name;
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
                    case "stop":
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
        foreach (int value in new[] { 1, 2, 4, 8, 16, 32 })
        {
            int ticks = wholeTicks / value;
            while (ticks > 0 && remaining >= ticks)
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
        // Join elision-separated texts (rare) into one syllable.
        var texts = Els(el, "text").Select(t => t.Value).ToList();
        if (texts.Count == 0)
            return null;
        string text = string.Join("", texts);
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

    // Lily# reserved words a part identifier must not collide with. Besides keywords,
    // this includes the single-letter tokens a short part name can lex as: pitch
    // letters (a-g), the rest r, and the dynamic marks (p, f, mf, …) — a part named
    // "P" or "F" in the source would otherwise fail to parse (found 'DynamicP').
    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        "part", "section", "score", "staff", "structure", "chords", "lyrics",
        "time", "key", "clef", "tempo", "octave", "title", "composer", "subtitle", "poet",
        "phrase", "drummap", "grace", "partial", "repeat", "tuplet",
        "acciaccatura", "appoggiatura",
        "a", "b", "c", "d", "e", "f", "g", "r",
        "ppp", "pp", "p", "mp", "mf", "ff", "fff", "sf", "sfz", "fp", "fz", "rfz", "rf", "sffz",
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

        if (cleaned.Length == 0 || Reserved.Contains(cleaned))
            cleaned = $"part{index}";

        string candidate = cleaned;
        int suffix = 2;
        while (!used.Add(candidate))
            candidate = cleaned + suffix++;
        return candidate;
    }

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
