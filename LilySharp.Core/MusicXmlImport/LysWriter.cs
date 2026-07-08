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
    public static string Write(ImportDocument doc, ImportReport report)
    {
        var sb = new StringBuilder();

        // ---- header ----
        sb.Append("octave absolute\n");
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

        // ---- one section holding every part's music (flat structure) ----
        sb.Append("section A {\n");
        foreach (var part in doc.Parts)
        {
            sb.Append("  ").Append(part.SafeName).Append(" {\n");
            sb.Append("    ").Append(WriteMusic(part, report)).Append('\n');
            sb.Append("  }\n");
        }
        // Section-level lyrics sing the first part carrying them.
        var lyricPart = doc.Parts.FirstOrDefault(HasLyrics);
        if (lyricPart != null)
            foreach (var line in WriteLyrics(lyricPart))
                sb.Append("  ").Append(line).Append('\n');
        sb.Append("}\n\n");

        sb.Append("structure { A }\n\n");

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

    // ---- music emission ---------------------------------------------------

    private static readonly List<ImportItem> EmptyItems = new();

    private static string WriteMusic(ImportPart part, ImportReport report)
    {
        var voices = part.Measures
            .SelectMany(m => m.VoiceItems.Keys)
            .Distinct().OrderBy(n => n).ToList();
        if (voices.Count <= 1)
            return WriteVoiceStream(part, voices.Count == 1 ? voices[0] : 1, report);

        // Several voices on one staff → parallel voice { } blocks. Ascending voice
        // order puts voice 1 (the upper part, stems up) first.
        return string.Join(" ",
            voices.Select(v => "voice { " + WriteVoiceStream(part, v, report) + " }"));
    }

    // One voice's measures assembled with the shared barlines between them.
    private static string WriteVoiceStream(ImportPart part, int voice, ImportReport report)
    {
        var measures = part.Measures;
        var cells = measures
            .Select(m => WriteMeasureItems(
                m.VoiceItems.TryGetValue(voice, out var items) ? items : EmptyItems, report))
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

    private static string WriteMeasureItems(List<ImportItem> items, ImportReport report)
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

            string body = members.Count == 1
                ? Pitch(note)
                : "<" + string.Join(" ", members.Select(Pitch)) + ">";
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
            if (note.LeadingGrace.Count > 0)
                tokens.Add(GraceBlock(note.LeadingGrace));
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
    private static string GraceBlock(List<ImportGraceNote> grace)
    {
        string keyword = grace[0].Slash ? "acciaccatura" : "grace";
        var notes = string.Join(" ",
            grace.Select(g => PitchToken(g.Step, g.Alter, g.Octave) + Value(g.NoteValue, g.Dots)));
        return keyword + " { " + notes + " }";
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

    /// <summary>One <c>lyrics { ... }</c> block per verse present in the part.
    /// Syllables walk the singable notes (no rests, chord members or tie
    /// continuations), synced to the music by a <c>|</c> per measure.</summary>
    private static IEnumerable<string> WriteLyrics(ImportPart part)
    {
        var verses = part.Measures.SelectMany(m => m.PrimaryItems).OfType<ImportNote>()
            .SelectMany(n => n.Lyrics).Select(l => l.Verse).Distinct().OrderBy(v => v);

        foreach (int verse in verses)
        {
            var sb = new StringBuilder("lyrics { ");
            foreach (var measure in part.Measures)
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
