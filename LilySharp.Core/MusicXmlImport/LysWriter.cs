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
        var firstMeasures = doc.Parts.Count > 0 ? doc.Parts[0].Measures : new List<ImportMeasure>();
        // Endings first (the richer shape), then a plain repeat. ⚠️ THE SECOND CALL IS NOT AN
        // OPTIMISATION: since 2026-08-31 a repeat barline may only be written in a `form`
        // (LYS1034), so an imported book whose repeat stayed in the music would not compile —
        // this writer would have been emitting `|:` into a section body.
        var layout = TryFactorVoltas(firstMeasures) ?? TryFactorPlainRepeats(firstMeasures, report);
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

        WritePaper(sb, doc.Paper);

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
        {
            sb.Append("part ").Append(part.SafeName)
              .Append(" { clef ").Append(part.Clef);
            // <transpose> comes back as `transposition`, the same knob that produced it.
            // Only whole octaves reach here (the reader warns about anything else), and the
            // clef's own octave has already been subtracted — see ImportPart.
            // ⚠️ TWO octaves are spelled `15mb`, not `8vb`: this used to write 8vb for any
            // multiple of 12 and quietly halved a doubly-transposing part on the way in.
            // Three or more has no marker at all, and is said out loud rather than rounded.
            if (part.TranspositionSemitones is { } semis && semis != 0)
            {
                string? marker = semis switch
                {
                    -12 => "8vb", 12 => "8va", -24 => "15mb", 24 => "15ma", _ => null,
                };
                if (marker != null)
                    sb.Append(" transposition ").Append(marker);
                else
                    report.Warn($"a part transposing {semis} semitones beyond its clef is "
                        + "imported at written pitch — Lily#'s `transposition` states one or "
                        + "two octaves.");
            }
            sb.Append(" }\n");
        }
        sb.Append('\n');

        // ---- sections + structure ----
        // First/second endings factor into named sections + a volta structure;
        // anything else is one flat section played once.
        if (layout != null)
            WriteVoltaSections(sb, doc, layout, report, useRelative);
        else
            WriteFlatSection(sb, doc, report, useRelative);

        // ---- score: one staff per part; split staves regroup into a grand staff ----
        // The part carrying lyrics places them EXPLICITLY, by band order — one
        // `lyrics NAME` row per verse directly under the part's staff (score = a
        // vertical stack of bands: the binding is the track's `sings`, the row's
        // position is the placement). No auto-attach; an unreferenced block would
        // be a LYS4006 error.
        var scoreLyricPart = doc.Parts.FirstOrDefault(HasLyrics);
        sb.Append("score main \"imported\" {\n");
        for (int gi = 0; gi < doc.Parts.Count;)
        {
            var group = doc.Parts[gi].StaffGroup;
            if (group == null)
            {
                sb.Append("  staff ").Append(doc.Parts[gi].SafeName)
                    .Append(LyricRowLines(doc.Parts[gi], scoreLyricPart, "  ")).Append('\n');
                gi++;
                continue;
            }
            // A run of consecutive parts sharing a staff group = one grand staff.
            sb.Append("  grandStaff {\n");
            while (gi < doc.Parts.Count && doc.Parts[gi].StaffGroup == group)
            {
                sb.Append("    staff ").Append(doc.Parts[gi].SafeName)
                    .Append(LyricRowLines(doc.Parts[gi], scoreLyricPart, "    ")).Append('\n');
                gi++;
            }
            sb.Append("  }\n");
        }
        sb.Append("}\n");

        return sb.ToString();
    }

    // ---- paper ------------------------------------------------------------

    /// <summary>
    /// Writes the page the source stated as a <c>paper { }</c> block — only the keys it
    /// stated, so everything else keeps the paper block's own (a4) defaults.
    /// </summary>
    /// <remarks>
    /// Values arrive in millimetres (the reader owns the tenths bridge) and are written
    /// to two decimals: one tenth is about 0.18mm at the common scaling, so 0.01mm
    /// out-resolves the source's own unit while trimming the noise a tenths-times-scale
    /// product carries (a 1190.55-tenths A4 width must read back as a width, not as a
    /// 15-digit decimal).
    /// </remarks>
    private static void WritePaper(StringBuilder sb, ImportPaper? paper)
    {
        if (paper == null)
            return;
        (string Key, double? Mm)[] entries =
        [
            ("paperWidth", paper.WidthMm), ("paperHeight", paper.HeightMm),
            ("leftMargin", paper.LeftMm), ("rightMargin", paper.RightMm),
            ("topMargin", paper.TopMm), ("bottomMargin", paper.BottomMm),
        ];
        if (!entries.Any(e => e.Mm != null))
            return;
        sb.Append("paper {\n");
        foreach (var (key, mm) in entries)
        {
            if (mm is { } value)
                sb.Append("  ").Append(key).Append(' ')
                  .Append(value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture))
                  .Append("mm\n");
        }
        sb.Append("}\n\n");
    }

    // ---- sections ---------------------------------------------------------

    // The single-section flat layout: every part's full music in section A, played once by
    // `form main { A }`. ⚠️ Reached only when the piece has NO repeat barline at all — a
    // repeat is cut into sections and spelled in the form (LYS1034, TryFactorPlainRepeats).
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
        sb.Append("form main { A }\n\n");
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
        sb.Append("form main {\n  ").Append(layout.Structure).Append("\n}\n\n");
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

    /// <summary>Factors plain repeats — <c>|: … :|</c> with no volta endings, in any number
    /// and including the back-to-back <c>:|:</c> — into named sections plus a form. Returns
    /// null when the measures hold no repeat barline at all, which is the one case that can
    /// still be written as one flat section.</summary>
    /// <remarks>
    /// <para>
    /// ⚠️ This exists because of LYS1034 (2026-08-31): a repeat barline is legal only inside a
    /// <c>form</c>, so <see cref="WriteFlatSection"/>'s output — one section holding the whole
    /// piece with <c>BarlineBetween</c>'s <c>|:</c> / <c>:|</c> / <c>:|:</c> in it — stopped
    /// being a book Lily# accepts. The endings case already factored (TryFactorVoltas); this
    /// is the same move for the case that did not.
    /// </para>
    /// <para>
    /// The cut is exactly at the repeat bars: a <c>|:</c> opens BEFORE its measure, a
    /// <c>:|</c> closes AFTER its measure, and <c>:|:</c> is both on adjacent measures. Every
    /// stretch between cuts becomes a section, and the form says the order — which is what
    /// the rule is for. Sections are named <c>Sec1</c>, <c>Sec2</c>, … rather than after
    /// their musical role: this shape has no Intro/Body/Coda to read off, and a generated
    /// name that pretends otherwise would be a guess in the output.
    /// </para>
    /// <para>
    /// ⚠️ A <c>|:</c> the source never closes is CLOSED at the end of the piece and reported.
    /// The alternative was to bail out to the flat layout, and that no longer exists as a
    /// legal option — silently dropping the repeat would be the other way to make the book
    /// compile, and it would change the music without saying so.
    /// </para>
    /// </remarks>
    private static VoltaLayout? TryFactorPlainRepeats(List<ImportMeasure> measures, ImportReport report)
    {
        int n = measures.Count;
        if (n == 0 || !measures.Any(m => m.RepeatForward || m.BarlineRight == BarlineKind.RepeatEnd))
            return null;

        var segments = new List<VoltaSegment>();
        var structure = new StringBuilder();
        int segStart = 0;
        bool openRepeat = false;

        string Cut(int start, int end)
        {
            string name = "Sec" + (segments.Count + 1);
            segments.Add(new VoltaSegment(name, start, end));
            return name;
        }

        for (int i = 0; i < n; i++)
        {
            if (measures[i].RepeatForward)
            {
                // A '|:' with music in front of it closes the stretch before it.
                if (i > segStart)
                {
                    if (structure.Length > 0) structure.Append(' ');
                    structure.Append(Cut(segStart, i));
                    segStart = i;
                }
                openRepeat = true;
            }
            if (measures[i].BarlineRight == BarlineKind.RepeatEnd)
            {
                string name = Cut(segStart, i + 1);
                if (structure.Length > 0) structure.Append(' ');
                // With no '|:' open this is the one-sided ':|' — repeat from the beginning of
                // the piece, which the form spells the same way the source did.
                structure.Append(openRepeat ? $"|: {name} :|" : $"{name} :|");
                openRepeat = false;
                segStart = i + 1;
            }
        }

        if (segStart < n)
        {
            string name = Cut(segStart, n);
            if (structure.Length > 0) structure.Append(' ');
            structure.Append(openRepeat ? $"|: {name} :|" : name);
            if (openRepeat)
                report.Warn("a repeat that the source opens and never closes is closed at the "
                    + "end of the piece — a repeat opens and closes in the form.");
        }
        else if (openRepeat)
        {
            // A '|:' on the very last barline, opening nothing. It has no body to repeat.
            report.Warn("a repeat opened on the last barline has nothing after it to repeat, "
                + "so it is dropped.");
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
        return VoiceSpan(voices.Select(v => WriteVoiceRange(part, v, start, end, report, Rel(relative))));
    }

    /// <summary>Wraps several simultaneous streams in one span. <c>voice</c> opens the span
    /// ONCE and each further voice is another block (repeating the keyword is LYS0019).</summary>
    private static string VoiceSpan(IEnumerable<string> bodies)
        => "voice " + string.Join(" ", bodies.Select(b => "{ " + b + " }"));

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

        // Several voices on one staff → one parallel span. Ascending voice order puts
        // voice 1 (the upper part, stems up) first. Each voice is its own
        // relative-octave stream.
        return VoiceSpan(voices.Select(v => WriteVoiceStream(part, v, report, Rel(relative))));
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

        // ⚠️ MusicXML MARKS EACH NOTE; Lily# (like LilyPond) has a REGION. One region per
        // maximal run of consecutive cue notes is the only grouping that can round-trip: a
        // region per note would forbid a beam inside a cue, because a cue region is a voice
        // of its own and a beam cannot cross it (MEASURED,
        // audit/lp-geometry/probes/cue-span.ly, book B-BEAM). A <cue/> used to be dropped
        // outright here — Lily# had nowhere to put it.
        bool inCue = false;
        int tupletDepth = 0;
        void OpenCueIfNeeded(ImportNote n)
        {
            if (n.IsCue == inCue)
                return;
            if (n.IsCue)
            {
                tokens.Add("cue {");
                inCue = true;
                return;
            }
            CloseCue();
        }
        void CloseCue()
        {
            if (!inCue)
                return;
            // Brackets may not cross. A tuplet that opened inside the run and has not closed
            // would be cut by the cue's brace, so say so rather than emit music that will not
            // parse; the notes stay, un-cued.
            if (tupletDepth > 0)
            {
                report.Warn(
                    "a cue run ends inside a tuplet; the cue braces would cross the tuplet's, "
                    + "so this run is written without 'cue { … }'.");
                tokens.RemoveAt(tokens.FindLastIndex(t => t == "cue {"));
            }
            else
            {
                tokens.Add("}");
            }
            inCue = false;
        }

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
            OpenCueIfNeeded(note);
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
            if (note.TremoloMarks > 0)
                token += ":" + note.TremoloMarks; // single-note tremolo slash (c2:8)
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
            {
                tokens.Add($"tuplet {tr.Actual}/{tr.Normal} {{");
                tupletDepth++;
            }
            // Leading grace notes hang before the main note (inside any tuplet wrap).
            if (graceToken != null)
                tokens.Add(graceToken);
            tokens.Add(token);
            if (note.TupletStop)
            {
                tokens.Add("}");
                tupletDepth = Math.Max(0, tupletDepth - 1);
            }
            i = j;
        }

        // A run that reaches the end of the measure closes here.
        CloseCue();

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

        /// <summary>Spell a chord: the first member (root) is relative to the
        /// running reference; every later member STACKS above the root (its octave
        /// mark is the offset from the nearest octave at or above the root — the
        /// same placement Lily# reads). The reference then advances to the root.
        /// Root-anchored, so the emitted marks round-trip regardless of member
        /// order and match <c>&lt;c 3 5&gt;</c>-style stacking.</summary>
        public string Chord(IReadOnlyList<ImportNote> members)
        {
            int rootStep = Mod7(members[0].Step);
            int rootOctave = members[0].Octave;
            var parts = new List<string>();
            for (int i = 0; i < members.Count; i++)
            {
                var m = members[i];
                if (i == 0)
                {
                    parts.Add(Spell(_ref, m.Step, m.Alter, m.Octave));
                }
                else
                {
                    int letter = Mod7(m.Step);
                    int stackedDefault = rootOctave + (letter >= rootStep ? 0 : 1);
                    parts.Add(Format(letter, m.Alter, m.Octave - stackedDefault));
                }
            }
            _ref = rootOctave * 7 + rootStep;
            return "<" + string.Join(" ", parts) + ">";
        }

        private static string Spell(int refDiatonic, int step, int alter, int octave)
        {
            int letter = Mod7(step);
            int def = (int)System.Math.Round((refDiatonic - letter) / 7.0, System.MidpointRounding.AwayFromZero);
            return Format(letter, alter, octave - def);
        }

        private static string Format(int letter, int alter, int marks)
        {
            string oct = marks > 0 ? new string('\'', marks) : marks < 0 ? new string(',', -marks) : "";
            return "cdefgab"[letter] + AlterSuffix(alter) + oct;
        }

        private static int Mod7(int step) => ((step % 7) + 7) % 7;
    }

    private static string Value(int noteValue, int dots)
        => noteValue.ToString() + new string('.', dots);

    // ---- barlines ---------------------------------------------------------

    /// <remarks>
    /// ⚠️ THE THREE REPEAT ARMS ARE NO LONGER REACHABLE, and they are left standing rather
    /// than deleted while the fact is fresh. Since LYS1034 (2026-08-31) a repeat barline is
    /// legal only in a <c>form</c>, and <see cref="TryFactorPlainRepeats"/> now cuts a section
    /// at every one of them — so the flat layout, which is the only caller of this, is only
    /// chosen for measures that hold none. If a fourth repeat spelling ever arrives, this is
    /// where the old answer is written down.
    /// </remarks>
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
        // The entry format is the SYMBOL as it prints (GRAMMAR_AUDIT 8.1):
        // uppercase root + '#'/'b' + bare quality — "Cm7", "F#m", "Bb7/D".
        string? root = SymbolPitch(h.RootStep, h.RootAlter);
        string? quality = KindToToken(h.Kind);
        string? bass = h.BassStep is int bs ? SymbolPitch(bs, h.BassAlter ?? 0) : "";
        if (root == null || quality == null || bass == null)
        {
            // No clean symbol target (an unknown kind, or a double accidental the
            // entry grammar does not spell): fall back to the quoted free-text
            // escape hatch so nothing renders wrong.
            string text = ChordStructure.SpellPitch(h.RootStep, h.RootAlter)
                          + (h.KindText ?? h.Kind);
            report.Warn($"chord '{text}' has no Lily# entry spelling; emitted as text.");
            return "@chord(\"" + EscapeString(text) + "\")";
        }

        var sb = new StringBuilder("@chord(");
        sb.Append(root).Append(quality);
        if (bass.Length > 0)
            sb.Append('/').Append(bass);
        sb.Append(')');
        return sb.ToString();
    }

    /// <summary>A symbol-entry pitch ("C", "F#", "Bb"), or null for an alteration
    /// the entry grammar cannot spell (double accidentals).</summary>
    private static string? SymbolPitch(int step, int alter)
    {
        if (alter is < -1 or > 1)
            return null;
        return "CDEFGAB"[((step % 7) + 7) % 7]
               + (alter switch { 1 => "#", -1 => "b", _ => "" });
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

    /// <summary>Every distinct lyric verse number in the part, ascending.</summary>
    private static IEnumerable<int> LyricVerses(ImportPart part)
        => part.Measures.SelectMany(m => m.PrimaryItems).OfType<ImportNote>()
            .SelectMany(n => n.Lyrics).Select(l => l.Verse).Distinct().OrderBy(v => v);

    /// <summary>The name a verse's lyric track is written under, so the score can
    /// place its row (<c>lyrics NAME</c> under the staff) — there is no auto-attach.
    /// Stable across sections (keyed on the verse number) so one row collects every
    /// section's cell for that verse.</summary>
    private static string LyricTrackName(int verse) => verse <= 1 ? "words" : "words" + verse;

    /// <summary>The <c>lyrics NAME</c> row lines a staff needs directly under it when
    /// its part carries the score's lyrics (each verse is its own row, stacking as
    /// verses in written order); empty for any other staff. Starts with a newline so
    /// it appends after the staff's own line at the given indent.</summary>
    private static string LyricRowLines(ImportPart part, ImportPart? lyricPart, string indent)
    {
        if (part != lyricPart || lyricPart == null)
            return "";
        var sb = new StringBuilder();
        foreach (int verse in LyricVerses(lyricPart))
            sb.Append('\n').Append(indent).Append("lyrics ").Append(LyricTrackName(verse));
        return sb.ToString();
    }

    /// <summary>One <c>lyrics NAME { ... }</c> block per verse present in the part's
    /// measures [start, end). Syllables walk the singable notes (no rests, chord
    /// members or tie continuations), synced to the music by a <c>|</c> per measure.</summary>
    private static IEnumerable<string> WriteLyrics(ImportPart part, int start, int end)
    {
        var measures = part.Measures.Skip(start).Take(end - start).ToList();
        var verses = measures.SelectMany(m => m.PrimaryItems).OfType<ImportNote>()
            .SelectMany(n => n.Lyrics).Select(l => l.Verse).Distinct().OrderBy(v => v);

        foreach (int verse in verses)
        {
            // The track sings the part whose notes carried the syllables — the
            // binding lives at the definition; the score row only places it.
            var sb = new StringBuilder("lyrics " + LyricTrackName(verse) + " sings " + part.SafeName + " { ");
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
