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
        };

        // Part names come from the part-list; the <part> elements carry the music.
        var names = new Dictionary<string, string?>();
        foreach (var sp in Els(Local(root, "part-list"), "score-part"))
        {
            var id = (string?)sp.Attribute("id");
            if (id != null)
                names[id] = Local(sp, "part-name")?.Value.Trim();
        }

        var used = new HashSet<string>(StringComparer.Ordinal);
        int index = 0;
        foreach (var partEl in Els(root, "part"))
        {
            index++;
            var id = (string?)partEl.Attribute("id") ?? $"P{index}";
            var part = new ImportPart
            {
                Id = id,
                Name = names.GetValueOrDefault(id),
            };
            part.SafeName = SafeIdentifier(part.Name, index, used);
            ReadPart(partEl, part, doc, report);
            doc.Parts.Add(part);
        }

        // An empty score is a common surprise (e.g. a template exported with no
        // music): the output parses but renders nothing, so say so plainly rather
        // than leave the user wondering whether the importer swallowed the notes.
        int totalNotes = doc.Parts.Sum(
            p => p.Measures.Sum(m => m.Items.Count(i => i is ImportNote note && !note.IsRest)));
        if (doc.Parts.Count == 0)
            report.Warn("No <part> elements found; the imported score is empty.");
        else if (totalNotes == 0)
            report.Warn("The MusicXML contains no notes; the imported score is empty.");
        return doc;
    }

    private static void ReadPart(XElement partEl, ImportPart part, ImportDocument doc, ImportReport report)
    {
        int divisions = 1;              // ticks per quarter; may change mid-part
        string? clefSet = null;         // most recent clef, for the part header
        int measureNo = 0;

        foreach (var measEl in Els(partEl, "measure"))
        {
            measureNo++;
            var measure = new ImportMeasure
            {
                Implicit = (string?)measEl.Attribute("implicit") == "yes",
            };

            int firstVoice = 0;         // Tier 1: keep only the first voice seen
            bool warnedVoices = false;
            // <harmony>/<figured-bass> precede their note in the stream; hold them
            // until the note arrives so they attach to it in the writer.
            var pendingAnnotations = new List<ImportItem>();
            // <direction> dynamics and <grace> notes also precede the note they mark.
            var pendingDynamics = new List<string>();
            var pendingGrace = new List<ImportGraceNote>();

            foreach (var el in measEl.Elements())
            {
                switch (el.Name.LocalName)
                {
                    case "attributes":
                        ReadAttributes(el, measure, ref divisions, ref clefSet, report, measureNo);
                        break;

                    case "direction":
                        if (ReadDirectionTempo(el) is int bpm)
                        {
                            doc.Tempo ??= bpm;
                            if (measureNo > 1 || measure.Items.Count > 0)
                                measure.Tempo = bpm;
                        }
                        pendingDynamics.AddRange(ReadDirectionDynamics(el));
                        break;

                    case "harmony":
                        if (ReadHarmony(el) is { } h)
                            pendingAnnotations.Add(h);
                        break;

                    case "figured-bass":
                        if (ReadFiguredBass(el) is { } fb)
                            pendingAnnotations.Add(fb);
                        break;

                    case "backup":
                    case "forward":
                        if (!warnedVoices)
                        {
                            report.Warn(measureNo,
                                "multiple voices/staves are not yet imported; kept the first voice only.");
                            warnedVoices = true;
                        }
                        // Tier 1: everything after a backup belongs to another voice.
                        goto endMeasure;

                    case "note":
                    {
                        int voice = int.TryParse(Local(el, "voice")?.Value, out int v) ? v : 1;
                        if (firstVoice == 0) firstVoice = voice;
                        if (voice != firstVoice)
                        {
                            if (!warnedVoices)
                            {
                                report.Warn(measureNo,
                                    "multiple voices are not yet imported; kept the first voice only.");
                                warnedVoices = true;
                            }
                            break;
                        }
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
                        // A chord member folds onto the previous note; harmony /
                        // figured-bass / dynamics / grace attach to the head note only.
                        if (!note.ChordWithPrev)
                        {
                            measure.Items.AddRange(pendingAnnotations);
                            pendingAnnotations.Clear();
                            note.Articulations.InsertRange(0, pendingDynamics);
                            pendingDynamics.Clear();
                            note.LeadingGrace.AddRange(pendingGrace);
                            pendingGrace.Clear();
                        }
                        measure.Items.Add(note);
                        break;
                    }

                    case "barline":
                        ReadBarline(el, measure);
                        break;
                }
            }
            endMeasure:
            // Any annotation with no following note still gets recorded (the writer
            // drops a dangling @chord/@fig with a warning rather than mis-attaching).
            measure.Items.AddRange(pendingAnnotations);

            part.Measures.Add(measure);
        }

        part.Clef = clefSet ?? "treble";
    }

    private static void ReadAttributes(
        XElement el, ImportMeasure measure, ref int divisions, ref string? clefSet,
        ImportReport report, int measureNo)
    {
        if (int.TryParse(Local(el, "divisions")?.Value, out int d) && d > 0)
            divisions = d;

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

        var clefEl = Local(el, "clef");
        if (clefEl != null)
        {
            string name = ClefName(clefEl, report, measureNo);
            measure.Clef = name;
            clefSet ??= name;   // first clef becomes the part header clef
        }
    }

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
        // Grace notes are handled by the caller (attached as leading grace); a cue
        // note is dropped for now.
        if (Local(el, "cue") != null)
        {
            report.Warn(measureNo, "cue note dropped.");
            return null;
        }

        var note = new ImportNote
        {
            ChordWithPrev = Local(el, "chord") != null,
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
        if (Local(el, "rest") != null)
        {
            note.IsRest = true;
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
            foreach (var s in Els(notations, "slur"))
                switch ((string?)s.Attribute("type"))
                {
                    case "start": note.SlurStart = true; break;
                    case "stop": note.SlurStop = true; break;
                }
            // Fermata is a direct <notations> child in real files, but the Lily#
            // exporter nests it under <articulations> (mapped below); handle both, once.
            if (Local(notations, "fermata") != null && !note.Articulations.Contains("fermata"))
                note.Articulations.Add("fermata");
            foreach (var a in Local(notations, "articulations")?.Elements() ?? Enumerable.Empty<XElement>())
                if (ArticulationMark(a.Name.LocalName) is { } mark && !note.Articulations.Contains(mark))
                    note.Articulations.Add(mark);
            foreach (var o in Local(notations, "ornaments")?.Elements() ?? Enumerable.Empty<XElement>())
                if (OrnamentMark(o.Name.LocalName) is { } mark && !note.Articulations.Contains(mark))
                    note.Articulations.Add(mark);
            // Tuplet bracket: the ratio comes from <time-modification>.
            foreach (var tup in Els(notations, "tuplet"))
                switch ((string?)tup.Attribute("type"))
                {
                    case "start": note.TupletStart = ReadTimeModification(el); break;
                    case "stop": note.TupletStop = true; break;
                }
        }

        foreach (var lyric in Els(el, "lyric"))
            if (ReadLyric(lyric) is { } imported)
                note.Lyrics.Add(imported);

        return note;
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
        if (int.TryParse(Local(el, "duration")?.Value, out int ticks) && ticks > 0 && divisions > 0)
        {
            var frac = new Fraction(ticks, divisions * 4); // fraction of a whole note
            foreach (int baseVal in new[] { 1, 2, 4, 8, 16, 32, 64 })
                for (int k = 0; k <= 2; k++)
                    if (Fraction.FromNoteValue(baseVal).Dotted(k) == frac)
                        return (baseVal, k);
        }
        return (4, dots);
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

        if (location == "left")
        {
            if ((string?)repeat?.Attribute("direction") == "forward")
                measure.RepeatForward = true;
            return;
        }

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
            return pm;
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

    // ---- helpers ----------------------------------------------------------

    // Lily# reserved words a part identifier must not collide with. Besides keywords,
    // this includes the single-letter tokens a short part name can lex as: pitch
    // letters (a-g), the rest r, and the dynamic marks (p, f, mf, …) — a part named
    // "P" or "F" in the source would otherwise fail to parse (found 'DynamicP').
    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        "part", "section", "score", "staff", "structure", "chords", "lyrics",
        "time", "key", "clef", "tempo", "octave", "title", "composer", "with",
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
