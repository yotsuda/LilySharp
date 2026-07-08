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

using System.Collections.Generic;
using System.Linq;
using System.Text;
using LilySharp.Core.Music;

namespace LilySharp.Core.MusicXmlImport;

/// <summary>
/// The bounded half of the importer: serializes an <see cref="ImportDocument"/> to
/// idiomatic Lily# source. It is written as the INVERSE of the pitch/duration/
/// annotation grammar — there is no general AST-to-<c>.lys</c> pretty-printer to
/// reuse (<c>PartSectionLayoutConverter</c> preserves music text verbatim). Output
/// is <c>octave absolute</c> so the register is explicit and unambiguous.
/// </summary>
internal static class LysWriter
{
    public static string Write(ImportDocument doc, ImportReport report, bool relativeOctave = false)
    {
        var sb = new StringBuilder();

        // Lily# resets the relative-octave reference at each section, so relative works
        // for the section-major volta layout too — each section is its own stream.
        var layout = TryFactorVoltas(doc.Parts.Count > 0 ? doc.Parts[0].Measures : new List<ImportMeasure>());
        bool useRelative = relativeOctave;

        // ---- header ----
        // Absolute is the unambiguous default; relative octave (Lily#'s file default)
        // gives more compact, hand-written-style output when requested.
        sb.Append(useRelative ? "octave relative\n" : "octave absolute\n");
        if (!string.IsNullOrWhiteSpace(doc.Title))
            sb.Append("title \"").Append(EscapeString(doc.Title!)).Append("\"\n");
        if (!string.IsNullOrWhiteSpace(doc.Composer))
            sb.Append("composer \"").Append(EscapeString(doc.Composer!)).Append("\"\n");
        sb.Append('\n');

        if (doc.Tempo is int tempo)
            sb.Append("tempo ").Append(tempo).Append('\n');

        // Opening time/key/clef come from the first measure that declares them.
        var firstTime = doc.Parts.SelectMany(p => p.Measures).Select(m => m.Time).FirstOrDefault(t => t != null);
        if (firstTime is { } t0)
            sb.Append("time ").Append(t0.Beats).Append('/').Append(t0.BeatType).Append('\n');
        var firstKey = doc.Parts.SelectMany(p => p.Measures).Select(m => m.Key).FirstOrDefault(k => k != null);
        if (firstKey is { } k0)
            sb.Append("key ").Append(KeyToLily(k0, report)).Append('\n');
        sb.Append('\n');

        // ---- part declarations ----
        foreach (var part in doc.Parts)
            sb.Append("part ").Append(part.SafeName)
              .Append(" { clef ").Append(part.Clef).Append(" }\n");
        sb.Append('\n');

        // ---- sections + structure ----
        // First/second endings factor into named sections + a volta structure;
        // anything else is one flat section played once.
        if (layout != null)
            WriteVoltaSections(sb, doc, layout, report, useRelative);
        else
            WriteFlatSection(sb, doc, report, useRelative);

        // ---- score: one staff per part; split staves regroup into a grand staff ----
        sb.Append("score \"imported\" {\n");
        for (int gi = 0; gi < doc.Parts.Count;)
        {
            var group = doc.Parts[gi].StaffGroup;
            if (group == null)
            {
                sb.Append("  staff ").Append(doc.Parts[gi++].SafeName).Append('\n');
                continue;
            }
            // A run of consecutive parts sharing a staff group = one grand staff.
            sb.Append("  grandStaff {\n");
            while (gi < doc.Parts.Count && doc.Parts[gi].StaffGroup == group)
                sb.Append("    staff ").Append(doc.Parts[gi++].SafeName).Append('\n');
            sb.Append("  }\n");
        }
        sb.Append("}\n");

        return sb.ToString();
    }

    // ---- sections ---------------------------------------------------------

    // The single-section flat layout (no repeat endings): every part's full music in
    // section A, with inline repeat barlines, played once by structure { A }.
    private static void WriteFlatSection(StringBuilder sb, ImportDocument doc, ImportReport report, bool relative)
    {
        sb.Append("section A {\n");
        foreach (var part in doc.Parts)
        {
            sb.Append("  ").Append(part.SafeName).Append(" {\n");
            sb.Append("    ").Append(WriteMusic(part, report, relative)).Append('\n');
            sb.Append("  }\n");
        }
        // Section-level lyrics sing the first part carrying them.
        var lyricPart = doc.Parts.FirstOrDefault(HasLyrics);
        if (lyricPart != null)
            foreach (var line in WriteLyrics(lyricPart, 0, lyricPart.Measures.Count))
                sb.Append("  ").Append(line).Append('\n');
        sb.Append("}\n\n");
        sb.Append("structure { A }\n\n");
    }

    // A first/second-ending layout: the music splits into named sections and the
    // repeat + volta brackets live in the structure (Body played twice, End1 the
    // first time, End2 the second).
    private static void WriteVoltaSections(
        StringBuilder sb, ImportDocument doc, VoltaLayout layout, ImportReport report, bool relative)
    {
        var lyricPart = doc.Parts.FirstOrDefault(HasLyrics);
        foreach (var seg in layout.Segments)
        {
            sb.Append("section ").Append(seg.Name).Append(" {\n");
            foreach (var part in doc.Parts)
            {
                sb.Append("  ").Append(part.SafeName).Append(" {\n");
                sb.Append("    ").Append(WriteMusicRange(part, seg.Start, seg.End, report, relative)).Append('\n');
                sb.Append("  }\n");
            }
            // Lyrics for just this section's measures, so each ending sings its own text.
            if (lyricPart != null)
                foreach (var line in WriteLyrics(lyricPart, seg.Start, seg.End))
                    sb.Append("  ").Append(line).Append('\n');
            sb.Append("}\n\n");
        }
        sb.Append("structure {\n  ").Append(layout.Structure).Append("\n}\n\n");
    }

    private sealed record VoltaSegment(string Name, int Start, int End);
    private sealed record VoltaLayout(IReadOnlyList<VoltaSegment> Segments, string Structure);

    /// <summary>Recognizes the common <c>[Intro] |: Body [1. End1] :| [2. End2]
    /// [Coda]</c> shape from the measures' repeat and ending markers, returning the
    /// sections + structure; null (→ flat layout) when there are no endings or the
    /// shape is not one we factor.</summary>
    private static VoltaLayout? TryFactorVoltas(List<ImportMeasure> measures)
    {
        int n = measures.Count;
        if (n == 0 || !measures.Any(m => m.EndingStart != null))
            return null;

        int end1Start = IndexWhere(measures, 0, m => m.EndingStart == 1);
        int end2Start = IndexWhere(measures, 0, m => m.EndingStart == 2);
        if (end1Start < 0 || end2Start < 0 || end1Start >= end2Start)
            return null;
        int end1Stop = IndexWhere(measures, end1Start, m => m.EndingStop);
        int end2Stop = IndexWhere(measures, end2Start, m => m.EndingStop);
        if (end1Stop < 0 || end2Stop < 0 || end2Start != end1Stop + 1)
            return null;

        // The repeated body runs from the |: (or the top if none) to the first ending.
        int repeatFwd = IndexWhere(measures, 0, m => m.RepeatForward);
        if (repeatFwd < 0 || repeatFwd > end1Start)
            repeatFwd = 0;
        if (repeatFwd >= end1Start)
            return null; // empty body

        var segments = new List<VoltaSegment>();
        var structure = new StringBuilder();
        if (repeatFwd > 0)
        {
            segments.Add(new VoltaSegment("Intro", 0, repeatFwd));
            structure.Append("Intro ");
        }
        segments.Add(new VoltaSegment("Body", repeatFwd, end1Start));
        segments.Add(new VoltaSegment("End1", end1Start, end1Stop + 1));
        segments.Add(new VoltaSegment("End2", end2Start, end2Stop + 1));
        structure.Append("|: Body [1. End1] :| [2. End2]");
        if (end2Stop + 1 < n)
        {
            segments.Add(new VoltaSegment("Coda", end2Stop + 1, n));
            structure.Append(" Coda");
        }
        return new VoltaLayout(segments, structure.ToString());
    }

    private static int IndexWhere(List<ImportMeasure> ms, int from, System.Func<ImportMeasure, bool> pred)
    {
        for (int i = from; i < ms.Count; i++)
            if (pred(ms[i]))
                return i;
        return -1;
    }

    // ---- music emission ---------------------------------------------------

    private static readonly List<ImportItem> EmptyItems = new();

    // One part's music over a measure range [start, end), voice-aware, joined by plain
    // barlines (repeat/volta bars come from the structure, not the notes). Each section
    // is its own relative-octave stream (Lily# resets relative per section).
    private static string WriteMusicRange(ImportPart part, int start, int end, ImportReport report, bool relative)
    {
        var voices = part.Measures.SelectMany(m => m.VoiceItems.Keys).Distinct().OrderBy(x => x).ToList();
        if (voices.Count <= 1)
            return WriteVoiceRange(part, voices.Count == 1 ? voices[0] : 1, start, end, report, Rel(relative));
        return string.Join(" ",
            voices.Select(v => "voice { " + WriteVoiceRange(part, v, start, end, report, Rel(relative)) + " }"));
    }

    private static string WriteVoiceRange(
        ImportPart part, int voice, int start, int end, ImportReport report, RelativeOctave? rel)
    {
        var sb = new StringBuilder();
        for (int i = start; i < end && i < part.Measures.Count; i++)
        {
            var items = part.Measures[i].VoiceItems.TryGetValue(voice, out var v) ? v : EmptyItems;
            sb.Append(WriteMeasureItems(items, report, rel)).Append(' ');
            if (i < end - 1)
                sb.Append("| ");
        }
        return sb.ToString().TrimEnd();
    }

    private static string WriteMusic(ImportPart part, ImportReport report, bool relative)
    {
        var voices = part.Measures
            .SelectMany(m => m.VoiceItems.Keys)
            .Distinct().OrderBy(n => n).ToList();
        if (voices.Count <= 1)
            return WriteVoiceStream(part, voices.Count == 1 ? voices[0] : 1, report, Rel(relative));

        // Several voices on one staff → parallel voice { } blocks. Ascending voice
        // order puts voice 1 (the upper part, stems up) first. Each voice is its own
        // relative-octave stream.
        return string.Join(" ",
            voices.Select(v => "voice { " + WriteVoiceStream(part, v, report, Rel(relative)) + " }"));
    }

    private static RelativeOctave? Rel(bool relative) => relative ? new RelativeOctave() : null;

    // One voice's measures assembled with the shared barlines between them.
    private static string WriteVoiceStream(ImportPart part, int voice, ImportReport report, RelativeOctave? rel)
    {
        var measures = part.Measures;
        var cells = measures
            .Select(m => WriteMeasureItems(
                m.VoiceItems.TryGetValue(voice, out var items) ? items : EmptyItems, report, rel))
            .ToList();

        var sb = new StringBuilder();
        if (measures.Count > 0 && measures[0].RepeatForward)
            sb.Append("|: ");

        for (int i = 0; i < measures.Count; i++)
        {
            sb.Append(cells[i]);
            sb.Append(' ');
            sb.Append(i < measures.Count - 1
                ? BarlineBetween(measures[i], measures[i + 1])
                : FinalBarline(measures[i]));
            if (i < measures.Count - 1)
                sb.Append(' ');
        }
        return sb.ToString();
    }

    private static string WriteMeasureItems(List<ImportItem> items, ImportReport report, RelativeOctave? rel = null)
    {
        var tokens = new List<string>();
        string? pendingChord = null;
        string? pendingFig = null;

        for (int i = 0; i < items.Count;)
        {
            if (items[i] is ImportHarmony harmony)
            {
                pendingChord = ChordAnnotation(harmony, report);
                i++;
                continue;
            }
            if (items[i] is ImportFiguredBass figuredBass)
            {
                pendingFig = FigAnnotation(figuredBass, report);
                i++;
                continue;
            }

            var note = (ImportNote)items[i];
            if (note.IsRest)
            {
                tokens.Add("r" + Value(note.NoteValue, note.Dots));
                pendingChord = null; // a rest cannot carry a chord symbol
                pendingFig = null;   // ... nor figured bass
                i++;
                continue;
            }

            // Gather this note plus any following chord members.
            var members = new List<ImportNote> { note };
            int j = i + 1;
            while (j < items.Count && items[j] is ImportNote m && m.ChordWithPrev)
            {
                members.Add(m);
                j++;
            }

            // Grace notes precede the main note, so (in relative mode) they thread the
            // reference first — build the grace block before the main note's body.
            string? graceToken = note.LeadingGrace.Count > 0 ? GraceBlock(note.LeadingGrace, rel) : null;

            string body = members.Count == 1
                ? (rel != null ? rel.Note(note.Step, note.Alter, note.Octave) : Pitch(note))
                : (rel != null ? rel.Chord(members)
                               : "<" + string.Join(" ", members.Select(Pitch)) + ">");
            string token = body + Value(note.NoteValue, note.Dots);
            if (pendingChord != null)
            {
                token += pendingChord;
                pendingChord = null;
            }
            if (pendingFig != null)
            {
                token += pendingFig;
                pendingFig = null;
            }
            foreach (var art in note.Articulations)
                token += "@" + art;
            if (note.SlurStop)
                token += ")";
            if (note.SlurStart)
                token += "(";
            if (note.TieStart)
                token += "~";

            // Wrap a tuplet group: `tuplet A/N { … }` around the notes it spans.
            if (note.TupletStart is { } tr)
                tokens.Add($"tuplet {tr.Actual}/{tr.Normal} {{");
            // Leading grace notes hang before the main note (inside any tuplet wrap).
            if (graceToken != null)
                tokens.Add(graceToken);
            tokens.Add(token);
            if (note.TupletStop)
                tokens.Add("}");
            i = j;
        }

        return tokens.Count == 0 ? "r" + "1" : string.Join(" ", tokens);
    }

    // A Lily# absolute-octave pitch token: letter + accidental + octave marks.
    private static string Pitch(ImportNote note) => PitchToken(note.Step, note.Alter, note.Octave);

    private static string PitchToken(int step, int alter, int octave)
    {
        char letter = "cdefgab"[((step % 7) + 7) % 7];
        string acc = alter switch
        {
            2 => "isis",
            1 => "is",
            -1 => "es",
            -2 => "eses",
            _ => "",
        };
        int marks = octave - 4; // bare c = octave 4 (middle C) in Lily# absolute
        string octaveMarks = marks > 0 ? new string('\'', marks)
            : marks < 0 ? new string(',', -marks)
            : "";
        return letter + acc + octaveMarks;
    }

    /// <summary>A leading grace block: <c>acciaccatura { … }</c> (slashed) or
    /// <c>grace { … }</c>, from the notes written before the main note.</summary>
    private static string GraceBlock(List<ImportGraceNote> grace, RelativeOctave? rel)
    {
        string keyword = grace[0].Slash ? "acciaccatura" : "grace";
        var notes = string.Join(" ", grace.Select(g =>
            (rel != null ? rel.Note(g.Step, g.Alter, g.Octave) : PitchToken(g.Step, g.Alter, g.Octave))
            + Value(g.NoteValue, g.Dots)));
        return keyword + " { " + notes + " }";
    }

    /// <summary>Relative-octave spelling: each note sits in the octave nearest the
    /// previous note (interval ≤ a fourth); <c>'</c>/<c>,</c> shift by octaves. The
    /// stream starts nearest C4. Used only in relative-output mode; each independent
    /// music stream gets a fresh instance.</summary>
    private sealed class RelativeOctave
    {
        private int _ref = 4 * 7; // C4 as a diatonic number (octave 4, letter C = 0)

        /// <summary>Spell one note and advance the reference to it.</summary>
        public string Note(int step, int alter, int octave)
        {
            string token = Spell(_ref, step, alter, octave);
            _ref = octave * 7 + Mod7(step);
            return token;
        }

        /// <summary>Spell a chord: the first member is relative to the running
        /// reference, each later member relative to the PREVIOUS member; the reference
        /// then advances to the FIRST member (the note after a chord is reckoned from
        /// it). This matches how the collector reads relative chords.</summary>
        public string Chord(IReadOnlyList<ImportNote> members)
        {
            int first = members[0].Octave * 7 + Mod7(members[0].Step);
            int prev = _ref;
            var parts = new List<string>();
            foreach (var m in members)
            {
                parts.Add(Spell(prev, m.Step, m.Alter, m.Octave));
                prev = m.Octave * 7 + Mod7(m.Step);
            }
            _ref = first;
            return "<" + string.Join(" ", parts) + ">";
        }

        private static string Spell(int refDiatonic, int step, int alter, int octave)
        {
            int letter = Mod7(step);
            int def = (int)System.Math.Round((refDiatonic - letter) / 7.0, System.MidpointRounding.AwayFromZero);
            int marks = octave - def;
            string oct = marks > 0 ? new string('\'', marks) : marks < 0 ? new string(',', -marks) : "";
            return "cdefgab"[letter] + AlterSuffix(alter) + oct;
        }

        private static int Mod7(int step) => ((step % 7) + 7) % 7;
    }

    private static string Value(int noteValue, int dots)
        => noteValue.ToString() + new string('.', dots);

    // ---- barlines ---------------------------------------------------------

    private static string BarlineBetween(ImportMeasure cur, ImportMeasure next)
    {
        bool endRepeat = cur.BarlineRight == BarlineKind.RepeatEnd;
        bool startNext = next.RepeatForward;
        if (endRepeat && startNext) return ":|:";
        if (endRepeat) return ":|";
        if (startNext) return "|:";
        return cur.BarlineRight switch
        {
            BarlineKind.Final => "|.",
            BarlineKind.Double => "||",
            _ => "|",
        };
    }

    private static string FinalBarline(ImportMeasure cur) => cur.BarlineRight switch
    {
        BarlineKind.RepeatEnd => ":|",
        BarlineKind.Final => "|.",
        BarlineKind.Double => "||",
        _ => "|",
    };

    // ---- chord symbols (@chord) ------------------------------------------

    private static string ChordAnnotation(ImportHarmony h, ImportReport report)
    {
        string root = "cdefgab"[((h.RootStep % 7) + 7) % 7].ToString() + AlterSuffix(h.RootAlter);
        string? quality = KindToToken(h.Kind);
        if (quality == null)
        {
            // No clean colon-form target: fall back to the quoted free-text escape
            // hatch so nothing renders wrong.
            string text = ChordStructure.SpellPitch(h.RootStep, h.RootAlter)
                          + (h.KindText ?? h.Kind);
            report.Warn($"chord kind '{h.Kind}' has no Lily# quality; emitted as text \"{text}\".");
            return "@chord(\"" + EscapeString(text) + "\")";
        }

        var sb = new StringBuilder("@chord(");
        sb.Append(root);
        if (quality.Length > 0)
            sb.Append(':').Append(quality);
        if (h.BassStep is int bs)
            sb.Append('/').Append("cdefgab"[((bs % 7) + 7) % 7]).Append(AlterSuffix(h.BassAlter ?? 0));
        sb.Append(')');
        return sb.ToString();
    }

    // ---- figured bass (@fig) ---------------------------------------------

    private static string? FigAnnotation(ImportFiguredBass fig, ImportReport report)
    {
        var parts = new List<string>();
        foreach (var f in fig.Figures)
        {
            if (f.Held)
            {
                parts.Add("_");
                continue;
            }
            if (f.Number > 0)
            {
                parts.Add(f.Number.ToString());
                // Accidental rides as a suffix token after the figure (6 s = 6-sharp).
                string? acc = f.Alteration switch { 1 => "s", -1 => "f", 2 => "n", _ => null };
                if (acc != null)
                    parts.Add(acc);
            }
            else if (f.Alteration == 1)
            {
                parts.Add("#"); // bare sharp (raised third)
            }
            else
            {
                report.Warn("figured-bass accidental without a figure dropped.");
            }
        }
        return parts.Count > 0 ? "@fig(" + string.Join(" ", parts) + ")" : null;
    }

    // Inverse of the exporter's suffix -> kind map (MusicXmlExporter.BuildHarmony).
    private static string? KindToToken(string kind) => kind switch
    {
        "major" or "" => "",
        "minor" => "m",
        "dominant" => "7",
        "minor-seventh" => "m7",
        "major-seventh" => "maj7",
        "diminished" => "dim",
        "diminished-seventh" => "dim7",
        "augmented" => "aug",
        "suspended-fourth" => "sus4",
        "suspended-second" => "sus2",
        "major-sixth" => "6",
        "minor-sixth" => "m6",
        "dominant-ninth" => "9",
        "major-ninth" => "maj9",
        "minor-ninth" => "m9",
        "major-minor" => "mmaj7",
        "half-diminished" => "m7b5",
        _ => null,
    };

    private static string AlterSuffix(int alter) => alter switch
    {
        2 => "isis",
        1 => "is",
        -1 => "es",
        -2 => "eses",
        _ => "",
    };

    // ---- lyrics -----------------------------------------------------------

    private static bool HasLyrics(ImportPart part)
        => part.Measures.SelectMany(m => m.PrimaryItems)
            .OfType<ImportNote>().Any(n => n.Lyrics.Count > 0);

    /// <summary>One <c>lyrics { ... }</c> block per verse present in the part's
    /// measures [start, end). Syllables walk the singable notes (no rests, chord
    /// members or tie continuations), synced to the music by a <c>|</c> per measure.</summary>
    private static IEnumerable<string> WriteLyrics(ImportPart part, int start, int end)
    {
        var measures = part.Measures.Skip(start).Take(end - start).ToList();
        var verses = measures.SelectMany(m => m.PrimaryItems).OfType<ImportNote>()
            .SelectMany(n => n.Lyrics).Select(l => l.Verse).Distinct().OrderBy(v => v);

        foreach (int verse in verses)
        {
            var sb = new StringBuilder("lyrics { ");
            foreach (var measure in measures)
            {
                foreach (var note in measure.PrimaryItems.OfType<ImportNote>())
                {
                    if (note.IsRest || note.ChordWithPrev || note.TieStop)
                        continue;
                    var lyric = note.Lyrics.FirstOrDefault(l => l.Verse == verse);
                    if (lyric.Text == null)
                        continue;
                    bool hyphen = lyric.Syllabic is "begin" or "middle";
                    sb.Append(lyric.Text).Append(hyphen ? "- " : " ");
                }
                sb.Append("| ");
            }
            sb.Append('}');
            yield return sb.ToString();
        }
    }

    // ---- key spelling -----------------------------------------------------

    // Circle-of-fifths -> Dutch major tonic (index shifted so fifths 0 = "c").
    private static readonly string[] MajorTonics =
    {
        "ces", "ges", "des", "aes", "ees", "bes", "f", // -7..-1
        "c",                                            //  0
        "g", "d", "a", "e", "b", "fis", "cis",          //  1..7
    };

    private static string KeyToLily(ImportKey key, ImportReport report)
    {
        string mode = string.IsNullOrEmpty(key.Mode) ? "major" : key.Mode;
        // Undo the church-mode fifths offset to land back on a MAJOR tonic, then
        // spell that tonic and keep the mode word (matches KeySpelling.SharpsFor).
        int offset = mode switch
        {
            "lydian" => 1,
            "mixolydian" => -1,
            "dorian" => -2,
            "minor" or "aeolian" => -3,
            "phrygian" => -4,
            "locrian" => -5,
            _ => 0,
        };
        int majorFifths = key.Fifths - offset;
        if (majorFifths < -7 || majorFifths > 7)
        {
            report.Warn($"key with {key.Fifths} fifths ({mode}) is out of range; approximated as c {mode}.");
            majorFifths = 0;
        }
        return MajorTonics[majorFifths + 7] + " " + mode;
    }

    private static string EscapeString(string s)
        => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
