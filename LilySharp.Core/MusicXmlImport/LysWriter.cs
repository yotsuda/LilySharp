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
/// reuse (<c>PartSectionRegrouper</c> preserves music text verbatim). Output
/// is <c>octave absolute</c> so the register is explicit and unambiguous.
/// </summary>
internal static class LysWriter
{
    public static string Write(ImportDocument doc, ImportReport report, bool relativeOctave = false)
    {
        var sb = new StringBuilder();

        // Lily# resets the relative-octave reference at each section, so relative works
        // for the by-section volta layout too — each section is its own stream.
        var firstMeasures = doc.Parts.Count > 0 ? doc.Parts[0].Measures : new List<ImportMeasure>();
        // Endings first (the richer shape), then a plain repeat. ⚠️ THE SECOND CALL IS NOT AN
        // OPTIMISATION: since 2026-08-31 a repeat barline may only be written in a `form`
        // (LYS1034), so an imported book whose repeat stayed in the music would not compile —
        // this writer would have been emitting `|:` into a section body.
        // Anything else is one flat section played once. Either is then cut again at the
        // rehearsal marks and at the bars that state a key, time or clef (CutPoints).
        // ⚠️ The flat section runs to the LONGEST part's end, not the first part's: a first
        // part with no bars at all (a round trip of inporder.lys) must not empty the others.
        int longest = doc.Parts.Count > 0 ? doc.Parts.Max(p => p.Measures.Count) : 0;
        var cuts = TryFactorVoltas(firstMeasures) ?? TryFactorPlainRepeats(firstMeasures, report)
            ?? new VoltaLayout([new VoltaSegment("A", 0, longest, EndBar: true)], HiddenLabel("A"));
        var systemStarts = SystemStarts(doc);
        var marks = CutPoints(RehearsalMarks(doc), doc, systemStarts);
        var header = HeaderState(doc, cuts.Segments.Select(s => s.Start).Concat(marks.Keys));
        var directives = doc.Parts.ToDictionary(p => p, p => Directives(doc, p, header, systemStarts, report));
        var layout = SplitAtMarks(cuts, CuttableMarks(marks, cuts, doc, report), doc);
        bool useRelative = relativeOctave;

        // ---- header ----
        // Absolute is the unambiguous default; relative octave (Lily#'s file default)
        // gives more compact, hand-written-style output when requested.
        sb.Append(useRelative ? "octave relative\n" : "octave absolute\n");
        if (!string.IsNullOrWhiteSpace(doc.Title))
            sb.Append("title \"").Append(EscapeString(doc.Title!)).Append("\"\n");
        if (!string.IsNullOrWhiteSpace(doc.Subtitle))
            sb.Append("subtitle \"").Append(EscapeString(doc.Subtitle!)).Append("\"\n");
        if (!string.IsNullOrWhiteSpace(doc.Composer))
            sb.Append("composer \"").Append(EscapeString(doc.Composer!)).Append("\"\n");
        if (!string.IsNullOrWhiteSpace(doc.Poet))
            sb.Append("poet \"").Append(EscapeString(doc.Poet!)).Append("\"\n");
        sb.Append('\n');

        WritePaper(sb, doc.Paper);

        if (doc.Tempo is int tempo)
            sb.Append("tempo ").Append(tempo).Append('\n');

        // Opening time/key/clef come from the first measure that declares them.
        var (firstTime, firstKey) = header;
        if (firstTime is { } t0)
            sb.Append("time ").Append(t0.Beats).Append('/').Append(t0.BeatType).Append('\n');
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
        WriteSections(sb, doc, layout, directives, report, useRelative);

        // ---- score: one staff per part; split staves regroup into a grand staff ----
        // The part carrying lyrics places them EXPLICITLY, by band order — one
        // `lyrics NAME` row per verse directly under the part's staff (score = a
        // vertical stack of bands: the binding is the track's `sings`, the row's
        // position is the placement). No auto-attach; an unreferenced block would
        // be a LYS4006 error.
        var scoreLyricPart = doc.Parts.FirstOrDefault(HasLyrics);
        sb.Append("score main \"imported\" {\n");
        // Each part's groups, outer first — the source's part-groups, then its own split into
        // a grand staff. A run of parts sharing a group's key is that group's block.
        var open = new List<string>();
        foreach (var part in doc.Parts)
        {
            var path = part.Groups.ToList();
            if (part.StaffGroup is { } split)
                path.Add((split, "grandStaff"));
            int keep = 0;
            while (keep < open.Count && keep < path.Count && open[keep] == path[keep].Key)
                keep++;
            while (open.Count > keep)
            {
                open.RemoveAt(open.Count - 1);
                sb.Append(new string(' ', 2 + 2 * open.Count)).Append("}\n");
            }
            for (int d = keep; d < path.Count; d++)
            {
                sb.Append(new string(' ', 2 + 2 * open.Count)).Append(path[d].Kind).Append(" {\n");
                open.Add(path[d].Key);
            }
            string indent = new(' ', 2 + 2 * open.Count);
            sb.Append(indent).Append("staff ").Append(part.SafeName).Append(StaffLabel(part))
                .Append(LyricRowLines(part, scoreLyricPart, indent)).Append('\n');
        }
        while (open.Count > 0)
        {
            open.RemoveAt(open.Count - 1);
            sb.Append(new string(' ', 2 + 2 * open.Count)).Append("}\n");
        }
        sb.Append("}\n");

        return sb.ToString();
    }

    /// <summary>The staff's printed label (<see cref="ImportPart.Label"/>), or nothing.</summary>
    private static string StaffLabel(ImportPart part)
        => part.Label is { } label ? " \"" + EscapeString(label) + "\"" : "";

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

    // Every section the layout cut, each part's music for its measures, then the form that
    // plays them. A flat piece is one section A played once (`form main { ~A }`) — reached only
    // when the piece has NO repeat barline at all: a repeat is cut into sections and spelled in
    // the form (LYS1034, TryFactorPlainRepeats). With endings, the repeat + volta brackets live
    // in the form (Body played twice, End1 the first time, End2 the second).
    private static void WriteSections(
        StringBuilder sb, ImportDocument doc, VoltaLayout layout, Dictionary<ImportPart, PartDirectives> directives,
        ImportReport report, bool relative)
    {
        var lyricPart = doc.Parts.FirstOrDefault(HasLyrics);
        foreach (var seg in layout.Segments)
        {
            sb.Append("section ").Append(seg.Name).Append(" {\n");
            foreach (var part in doc.Parts)
            {
                sb.Append("  ").Append(part.SafeName).Append(" {\n");
                sb.Append("    ").Append(WriteMusicRange(part, seg, report, relative, directives[part])).Append('\n');
                sb.Append("  }\n");
            }
            // Lyrics for just this section's measures, so each ending sings its own text.
            if (lyricPart != null)
                foreach (var line in WriteLyrics(lyricPart, seg.Start, seg.End))
                    sb.Append("  ").Append(line).Append('\n');
            sb.Append("}\n\n");
        }
        // `~`: the sections are the importer's, not the source's — their labels are not
        // printed (see HiddenLabel).
        if (layout.Segments.Count == 1)
            sb.Append("form main { ").Append(layout.Structure).Append(" }\n\n");
        else
            sb.Append("form main {\n  ").Append(layout.Structure).Append("\n}\n\n");
    }

    /// <summary>A stretch of measures [Start, End) written as one section.</summary>
    /// <param name="EndBar">Whether the section closes on its last measure's own bar line. A
    /// section that ends at a repeat or an ending leaves that bar to the form.</param>
    private sealed record VoltaSegment(string Name, int Start, int End, bool EndBar = false);
    private sealed record VoltaLayout(IReadOnlyList<VoltaSegment> Segments, string Structure);

    /// <summary>
    /// Cuts every section of <paramref name="layout"/> again before each measure that carries a
    /// rehearsal mark, so the book's sections are the source's own: a section per mark, named
    /// after it where its text can name one, and the form plays them in order where it played
    /// the uncut section.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Until 2026-10-01 a piece with no repeat came back as ONE section holding every bar, the
    /// marks written inline (<c>@mark("A")</c> … <c>@mark("H")</c>) — a book nobody could
    /// rearrange or read by its form. The mark itself stays where the source put it, as
    /// <c>@mark</c> on its note: the section's label is still hidden (<see cref="HiddenLabel"/>),
    /// so the page prints exactly what it printed before.
    /// </para>
    /// <para>
    /// The cut is at the start of the mark's MEASURE, wherever in the bar the mark stands: a
    /// section boundary inside a bar would leave two short bars.
    /// </para>
    /// <para>
    /// Only the marks <see cref="CuttableMarks"/> lets through arrive here.
    /// </para>
    /// </remarks>
    private static VoltaLayout SplitAtMarks(VoltaLayout layout, SortedDictionary<int, string?> marks, ImportDocument doc)
    {
        if (marks.Count == 0)
            return layout;

        // Names already taken: the layout's own sections and the parts.
        var used = new HashSet<string>(layout.Segments.Select(s => s.Name), StringComparer.Ordinal);
        used.UnionWith(doc.Parts.Select(p => p.SafeName));
        string Unique(string name)
        {
            string candidate = name;
            for (int k = 2; !used.Add(candidate); k++)
                candidate = name + k;
            return candidate;
        }

        // ⚠️ A flat piece's one section is named A, which is also the commonest mark: once it
        // is cut, its unmarked opening bars are the piece's Intro, and A stays free for the mark.
        bool flat = layout.Segments.Count == 1 && layout.Segments[0].EndBar;
        if (flat)
            used.Remove(layout.Segments[0].Name);

        var segments = new List<VoltaSegment>();
        string structure = layout.Structure;
        foreach (var seg in layout.Segments)
        {
            var bounds = new List<int> { seg.Start };
            bounds.AddRange(marks.Keys.Where(i => i > seg.Start && i < seg.End));
            if (bounds.Count == 1)
            {
                segments.Add(seg);
                continue;
            }
            bounds.Add(seg.End);
            var names = new List<string>();
            // A cut no mark names (a key, time or clef statement, or a mark that cannot name a
            // section) continues the name before it: A, A2, A3; after mark B, B2. "Intro" only
            // where a mark follows — a piece cut at its key changes alone has no intro.
            bool marked = bounds.Any(b => marks.GetValueOrDefault(b) != null);
            string run = seg.Name;
            int count = 1;
            for (int k = 0; k + 1 < bounds.Count; k++)
            {
                string name;
                if (marks.GetValueOrDefault(bounds[k]) is { } label && SectionNameFor(label) is { } fromMark
                        && !used.Contains(fromMark))
                    (name, run, count) = (Unique(fromMark), fromMark, 1);
                else if (k == 0)
                    (name, run, count) = flat ? (Unique(marked ? "Intro" : seg.Name), marked ? "Intro" : seg.Name, 1) : (seg.Name, seg.Name, 1);
                else
                    name = Unique(run + (char.IsAsciiDigit(run[^1]) ? "_" : "") + ++count);
                names.Add(name);
                bool last = k + 2 == bounds.Count;
                segments.Add(new VoltaSegment(name, bounds[k], bounds[k + 1], last ? seg.EndBar : true));
            }
            structure = System.Text.RegularExpressions.Regex.Replace(structure,
                System.Text.RegularExpressions.Regex.Escape(HiddenLabel(seg.Name)) + "(?![A-Za-z0-9_])",
                string.Join(" ", names.Select(HiddenLabel)));
        }
        return new VoltaLayout(segments, structure);
    }

    /// <summary>The first rehearsal mark of each measure, in any part, voice or chord, by
    /// measure index.</summary>
    private static SortedDictionary<int, string> RehearsalMarks(ImportDocument doc)
    {
        var marks = new SortedDictionary<int, string>();
        foreach (var part in doc.Parts)
            for (int i = 0; i < part.Measures.Count; i++)
                foreach (var note in part.Measures[i].VoiceItems.Values.SelectMany(v => v).OfType<ImportNote>())
                    foreach (var art in note.Articulations)
                        if (MarkLabel(art) is { } label)
                            marks.TryAdd(i, label);
        return marks;
    }

    /// <summary>
    /// The marks a section may start at: every one but those a slur or a hairpin runs through.
    /// The others stay inside the section before them, as <c>@mark</c>, and the report says so.
    /// </summary>
    /// <remarks>
    /// A section boundary resets the meter, the key and the clef (SYNTAX_REFERENCE "Across a
    /// section boundary"), so a section starting where the one in force is not the file's says
    /// it again (<see cref="Directives"/>). Since 2026-10-02 that restatement draws nothing — it
    /// states what the section before left, and a section head that does is no change
    /// (MeasureBuilder.SectionHead, LILYSHARP-OWN, owner's decision; the twin omits it too).
    /// Until then such a mark was refused — the restatement printed a second, identical
    /// signature (Lab sessions/p734/imp, round trip of section-meter-resets-to-global-meter.lys:
    /// a 4/4 printed again at its mark B) — and Bohemian Rhapsody's G and H stayed inside F.
    /// <para>
    /// ⚠️ NOR WHERE A SLUR, A PHRASING SLUR OR A HAIRPIN RUNS ON PAST THE NEXT SECTION: a span
    /// open at a section's end is carried into the next section only and must end there, or it
    /// is cut and reported (LYS4023). An imported hairpin has no end of its own (the reader
    /// keeps no wedge stop) — the next dynamic or hairpin ends it — so one opened before a mark
    /// and followed by no dynamic until the mark after it cannot be cut there (round trip of
    /// hairpin-in-a-repeated-section.lys). A tie may cross anything.
    /// </para>
    /// </remarks>
    private static SortedDictionary<int, string?> CuttableMarks(
        SortedDictionary<int, string?> marks, VoltaLayout layout, ImportDocument doc, ImportReport report)
    {
        var starts = layout.Segments.Select(s => s.Start).ToHashSet();
        // Every place a section may start, so a span carried over a cut must end before the next.
        var bounds = starts.Concat(marks.Keys).Append(int.MaxValue).Distinct().Order().ToList();
        var result = new SortedDictionary<int, string?>();
        foreach (var (i, label) in marks)
        {
            if (starts.Contains(i))
                result.Add(i, label);
            else if (!SpanRunsThrough(doc, i, bounds.First(b => b > i)))
                result.Add(i, label);
            // A statement left inside a section loses nothing: it is written where it stands.
            else if (label != null)
                report.Warn(i + 1, $"the rehearsal mark '{label}' does not start a section: a slur or "
                    + "hairpin open there would have to run on through the section after it.");
        }
        return result;
    }

    /// <summary>
    /// The places a section may start: the rehearsal marks (by their label), and every other bar
    /// past the first that states a key, time or clef again that its system's head does not
    /// explain (<see cref="Restatements"/>), with no label — and, in a piece with no rehearsal
    /// mark, every bar that changes one.
    /// </summary>
    /// <remarks>
    /// Owner's decision 2026-10-02. A <c>&lt;key&gt;</c> that changes nothing is in the file on
    /// purpose — MusicXML prints whatever <c>&lt;attributes&gt;</c> states (print-object defaults
    /// to yes); MuseScore, Finale and Sibelius write one only where it changes or where the user
    /// put one, and musicxml2ly turns every one into a <c>\key</c> that LilyPond draws. It used to
    /// be dropped here, so a <c>key!</c> exported to MusicXML came back without its <c>!</c>. The
    /// bar that states one is where a new section of the piece most often opens, so the book is
    /// cut there, and the statement is <c>key!</c> (<see cref="Directives"/>) — at a section's
    /// head, a plain one restating what the section before left would draw nothing
    /// (MeasureBuilder.SectionHead).
    /// <para>
    /// A change cuts only where no mark does: the marks are the score's own sections, and a
    /// change inside one is often a bar's detour (Bohemian Rhapsody's one-bar 6/4, 2/4 and 6/8
    /// would each have become a section of their own). Without marks, a change is the best
    /// guess at where a section opens.
    /// </para>
    /// </remarks>
    private static SortedDictionary<int, string?> CutPoints(
        SortedDictionary<int, string> marks, ImportDocument doc, HashSet<int> systemStarts)
    {
        var points = new SortedDictionary<int, string?>();
        foreach (var (i, label) in marks)
            points.Add(i, label);
        foreach (var part in doc.Parts)
        {
            var restated = Restatements(part, systemStarts);
            var (time, key, clef) = ((ImportTime?)null, (ImportKey?)null, (string?)null);
            for (int i = 0; i < part.Measures.Count; i++)
            {
                var m = part.Measures[i];
                bool changes = (time != null && m.Time is { } t && t != time)
                    || (key != null && m.Key is { } k && k != key)
                    || (part.StaffGroup == null && clef != null && m.Clef is { } c && c != clef);
                if (i > 0 && ((changes && marks.Count == 0) || restated[i] != default))
                    points.TryAdd(i, null);
                (time, key) = (m.Time ?? time, m.Key ?? key);
                clef = m.Clef ?? clef;
            }
        }
        return points;
    }

    /// <summary>The bars a <c>&lt;print new-system="yes"&gt;</c> or <c>new-page</c> opens, in any
    /// part.</summary>
    private static HashSet<int> SystemStarts(ImportDocument doc)
    {
        var starts = new HashSet<int>();
        foreach (var part in doc.Parts)
            for (int i = 0; i < part.Measures.Count; i++)
                if (part.Measures[i].NewSystem)
                    starts.Add(i);
        return starts;
    }

    /// <summary>Which of the time, key and clef a bar states again without changing it.</summary>
    private readonly record struct Restated(bool Time, bool Key, bool Clef);

    /// <summary>
    /// Per measure of <paramref name="part"/>, the time, key and clef it states again, unchanged,
    /// after an earlier bar of the part stated them — except on a bar that opens a system: a
    /// writer that copies what a system's head prints (an optical reader, typically) restates
    /// the clef and key on every line, and those are not the score's (owner's decision
    /// 2026-10-02 — a section per line would be absurd).
    /// </summary>
    private static Restated[] Restatements(ImportPart part, HashSet<int> systemStarts)
    {
        var result = new Restated[part.Measures.Count];
        var (time, key, clef) = ((ImportTime?)null, (ImportKey?)null, (string?)null);
        for (int i = 0; i < part.Measures.Count; i++)
        {
            var m = part.Measures[i];
            if (!systemStarts.Contains(i))
                result[i] = new Restated(
                    time != null && m.Time == time,
                    key != null && m.Key == key,
                    part.StaffGroup == null && clef != null && m.Clef == clef);
            (time, key) = (m.Time ?? time, m.Key ?? key);
            clef = m.Clef ?? clef;
        }
        return result;
    }

    /// <summary>
    /// Whether, in any part or voice, a slur, phrasing slur or hairpin open at the start of
    /// measure <paramref name="cut"/> is not ended before measure <paramref name="next"/> (or
    /// the end of the part) — the one span a cut there would carry into a section and not end.
    /// </summary>
    private static bool SpanRunsThrough(ImportDocument doc, int cut, int next)
    {
        foreach (var part in doc.Parts)
            foreach (int voice in part.Measures.SelectMany(m => m.VoiceItems.Keys).Distinct())
            {
                bool slur = false, phrasing = false, hairpin = false;
                bool carried = false; // past the cut: only the spans open there are followed
                for (int i = 0; i < Math.Min(next, part.Measures.Count); i++)
                {
                    if (i == cut)
                    {
                        if (!slur && !phrasing && !hairpin)
                            break;
                        carried = true;
                    }
                    if (!part.Measures[i].VoiceItems.TryGetValue(voice, out var items))
                        continue;
                    foreach (var note in items.OfType<ImportNote>().Where(n => !n.ChordWithPrev))
                    {
                        // Close before open, the order the writer spells them in.
                        if (note.SlurStop) slur = false;
                        if (note.SlurStart && !carried) slur = true;
                        if (note.PhrasingSlurStop) phrasing = false;
                        if (note.PhrasingSlurStart && !carried) phrasing = true;
                        foreach (var art in note.Articulations)
                        {
                            // A new hairpin ends the one before it, as a dynamic does.
                            if (art is "cresc" or "decresc")
                                hairpin = !carried;
                            else if (IsDynamic(art))
                                hairpin = false;
                        }
                    }
                }
                if (carried && (slur || phrasing || hairpin))
                    return true;
            }
        return false;
    }

    // A dynamic mark as the reader spells one: it ends a hairpin.
    private static bool IsDynamic(string articulation)
        => System.Text.RegularExpressions.Regex.IsMatch(articulation, "^(p+|f+|m[pf]|s?f+z?|sfp+|fp|rf+z?|fz|sp+|n)$");

    /// <summary>The text of a <c>mark("…")</c> articulation, or null for any other.</summary>
    private static string? MarkLabel(string articulation)
        => articulation.StartsWith("mark(\"", StringComparison.Ordinal) && articulation.EndsWith("\")", StringComparison.Ordinal)
            ? articulation[6..^2]
            : null;

    /// <summary>
    /// The section name a mark's text gives, or null when it cannot name one. Only a word
    /// opening with a capital: every keyword is lower case (SYNTAX_REFERENCE "Reserved Words"),
    /// save <c>R</c>, the bar rest. A text of digits alone ("12") names <c>M12</c>.
    /// </summary>
    private static string? SectionNameFor(string label)
    {
        if (label.Length > 0 && label.All(char.IsAsciiDigit))
            return "M" + label;
        if (label.Length == 0 || !char.IsAsciiLetterUpper(label[0]) || !label.All(char.IsAsciiLetterOrDigit) || label == "R")
            return null;
        return label;
    }

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
            structure.Append(HiddenLabel("Intro")).Append(' ');
        }
        segments.Add(new VoltaSegment("Body", repeatFwd, end1Start));
        segments.Add(new VoltaSegment("End1", end1Start, end1Stop + 1));
        segments.Add(new VoltaSegment("End2", end2Start, end2Stop + 1));
        structure.Append($"|: {HiddenLabel("Body")} [1. {HiddenLabel("End1")}] :| [2. {HiddenLabel("End2")}]");
        if (end2Stop + 1 < n)
        {
            segments.Add(new VoltaSegment("Coda", end2Stop + 1, n));
            structure.Append(' ').Append(HiddenLabel("Coda"));
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
    /// <c>form</c>, so the flat layout's output — one section holding the whole piece with
    /// its <c>|:</c> / <c>:|</c> / <c>:|:</c> in it — stopped
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

        // Returns the name as the FORM writes it (label hidden); the segment keeps the bare name.
        string Cut(int start, int end)
        {
            string name = "Sec" + (segments.Count + 1);
            segments.Add(new VoltaSegment(name, start, end));
            return HiddenLabel(name);
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

    /// <summary>
    /// A section of the importer's own making, as the form names it: with <c>~</c>, so its
    /// label does not print.
    /// </summary>
    /// <remarks>
    /// The names (<c>A</c>, <c>Intro</c>, <c>Body</c>, <c>End1</c>, <c>Sec1</c>…) are the
    /// importer's cuts, not labels the source prints: a label the source DOES print is its
    /// &lt;rehearsal&gt;, which arrives as <c>@mark</c>. Until 2026-09-30 the labels printed —
    /// every import opened with a boxed "A" — and once the rehearsals were read, a source's own
    /// mark on the first bar collided with that label (LYS4021, 270 times in 196 of 998 books).
    /// </remarks>
    private static string HiddenLabel(string name) => "~" + name;

    private static int IndexWhere(List<ImportMeasure> ms, int from, System.Func<ImportMeasure, bool> pred)
    {
        for (int i = from; i < ms.Count; i++)
            if (pred(ms[i]))
                return i;
        return -1;
    }

    // ---- music emission ---------------------------------------------------

    private static readonly List<ImportItem> EmptyItems = new();

    /// <summary>How many bars a line of the written music holds at most. A bar line other than
    /// a plain one ends the line early.</summary>
    private const int BarsPerLine = 4;

    // The indent of a section's music: inside `section X {` and `part {`.
    private const string MusicIndent = "    ";

    // One part's music over a section's measures, voice-aware. Repeat and volta bars come
    // from the form, not the notes. Each section is its own relative-octave stream (Lily#
    // resets relative per section).
    private static string WriteMusicRange(
        ImportPart part, VoltaSegment seg, ImportReport report, bool relative, PartDirectives directives)
    {
        var voices = part.Measures.SelectMany(m => m.VoiceItems.Keys).Distinct().OrderBy(x => x).ToList();
        if (voices.Count <= 1)
            return WriteVoiceRange(part, voices.Count == 1 ? voices[0] : 1, seg, report, Rel(relative),
                directives, "\n" + MusicIndent);
        // Several voices on one staff → one parallel span. Ascending voice order puts voice 1
        // (the upper part, stems up) first. Each voice is its own relative-octave stream; the
        // staff's changes ride the first one only.
        return VoiceSpan(voices.Select(v =>
            WriteVoiceRange(part, v, seg, report, Rel(relative), v == voices[0] ? directives : null,
                "\n" + MusicIndent + "  ")));
    }

    /// <summary>Wraps several simultaneous streams in one span. <c>voice</c> opens the span
    /// ONCE and each further voice is another block (repeating the keyword is LYS0019); a
    /// further block starts on a line of its own.</summary>
    private static string VoiceSpan(IEnumerable<string> bodies)
        => "voice " + string.Join("\n" + MusicIndent, bodies.Select(b => "{ " + b + " }"));

    /// <summary>
    /// One voice's measures of a section, with the source's bar lines between them, broken
    /// into lines of <see cref="BarsPerLine"/> bars and after any bar line that is not plain.
    /// </summary>
    /// <remarks>
    /// Until 2026-10-01 a part's whole piece was written on ONE line — hundreds of bars, which
    /// no editor shows and no reader can find a bar in. A bar rest folded over several bars
    /// (<c>R1*8</c>) counts as one bar of the line.
    /// </remarks>
    private static string WriteVoiceRange(
        ImportPart part, int voice, VoltaSegment seg, ImportReport report, RelativeOctave? rel,
        PartDirectives? directives, string newline)
    {
        var sb = new StringBuilder();
        int start = seg.Start, end = Math.Min(seg.End, part.Measures.Count);
        int onLine = 0;
        for (int i = start; i < end;)
        {
            int bars = MultiRestSpan(part.Measures, i, end, voice);
            var items = part.Measures[i].VoiceItems.TryGetValue(voice, out var v) ? v : EmptyItems;
            if (directives != null)
                sb.Append(i == start ? directives.Opening[i] : directives.Changes[i]);
            sb.Append(WriteMeasureItems(items, report, rel, bars));
            // The folded bars close at the LAST one's bar line.
            var last = part.Measures[i + bars - 1];
            i += bars;
            onLine++;
            if (i < end)
            {
                string bar = PlainOrDoubleBarline(last);
                sb.Append(' ').Append(bar);
                bool breakHere = onLine >= BarsPerLine || bar != "|";
                sb.Append(breakHere ? newline : " ");
                if (breakHere)
                    onLine = 0;
            }
            else if (seg.EndBar)
            {
                sb.Append(' ').Append(PlainOrDoubleBarline(last));
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// How many bars from <paramref name="i"/> fold into one <c>R…*N</c>: the measure's
    /// <c>multiple-rest</c> count, cut short where anything but another plain whole-measure
    /// rest stands in the way (a bar line other than plain, a repeat, an ending, an
    /// attribute change, a mark on a later bar) and at <paramref name="end"/>. 1 = no fold.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/parser.yy:3117-3120 MULTI_MEASURE_REST — <c>R1*4</c> is ONE event
    /// over four bars, while <c>R1 | R1 | R1 | R1</c> is four (the exporter writes the one as
    /// <c>multiple-rest 4</c> on the first of four bar rests); so only the mark folds, never
    /// a run of bar rests that merely happen to follow each other.
    /// </remarks>
    private static int MultiRestSpan(IReadOnlyList<ImportMeasure> measures, int i, int end, int voice)
    {
        if (measures[i].MultipleRest is not int bars || LoneBarRest(measures[i], voice) is not { } head)
            return 1;
        int k = 1;
        while (k < bars && i + k < end)
        {
            var prev = measures[i + k - 1];
            var m = measures[i + k];
            if (prev.BarlineRight != BarlineKind.Plain || prev.EndingStop
                || m.RepeatForward || m.EndingStart != null || m.MultipleRest != null
                || m.Key != null || m.Time != null || m.Clef != null || m.Tempo != null
                || LoneBarRest(m, voice) is not { } r
                || r.NoteValue != head.NoteValue || r.Dots != head.Dots || r.IsSpacer != head.IsSpacer
                || r.Articulations.Count > 0 || r.TupletStart != null)
                break;
            k++;
        }
        return k;
    }

    // The measure's one item when it is a whole-measure rest alone in the bar, else null.
    private static ImportNote? LoneBarRest(ImportMeasure m, int voice)
        => m.VoiceItems.Count == 1 && m.VoiceItems.TryGetValue(voice, out var items) && items.Count == 1
           && items[0] is ImportNote { IsRest: true, IsMeasureRest: true } rest
            ? rest
            : null;

    /// <summary>
    /// Per measure, the changes that open it — <c>time</c>, <c>key</c>, <c>clef</c>,
    /// <c>tempo</c> — as the directives written before its music ("" when none).
    /// </summary>
    /// <remarks>
    /// Until 2026-09-30 only the OPENING time and key reached the file (the header), so a
    /// piece that changed meter came back reading every later bar against the first
    /// signature (chord-tremolo.lys: its 3/4, 2/4 and 1/4 bars warned as short 4/4 ones), and
    /// a key, clef or tempo change was lost outright. A change is written where it differs
    /// from what is in force — the header's time/key/tempo and the part's clef at the start.
    /// A grand staff's clef is left alone: the reader keeps one clef per measure, which
    /// cannot say which staff changed.
    /// </remarks>
    // The time and key the piece opens in: the first a measure declares, in any part.
    private static (ImportTime? Time, ImportKey? Key) Opening(ImportDocument doc)
        => (doc.Parts.SelectMany(p => p.Measures).Select(m => m.Time).FirstOrDefault(t => t != null),
            doc.Parts.SelectMany(p => p.Measures).Select(m => m.Key).FirstOrDefault(k => k != null));

    /// <summary>
    /// The time and key the header states: the ones in force at the most places a section may
    /// start (the form's cuts and the rehearsal marks), the opening's where none is more
    /// common. ONE house, so the header and the directives (<see cref="Directives"/>) cannot
    /// disagree about what is in force.
    /// </summary>
    /// <remarks>
    /// A section that starts in anything else restates it — which draws nothing since 2026-10-02 (see
    /// <see cref="CuttableMarks"/>), but is a line of source; the header's value is the one no section has to restate. The opening
    /// section's own statement costs nothing: a <c>time</c> at the first moment REPLACES the
    /// initial signature (MeasureCollector.MusicWalk.cs, TimeSignatureSyntax). Until
    /// 2026-10-01 a piece was cut only at its repeats and the header was simply the opening.
    /// </remarks>
    private static (ImportTime? Time, ImportKey? Key) HeaderState(ImportDocument doc, IEnumerable<int> sectionStarts)
    {
        var (time, key) = Opening(doc);
        var starts = sectionStarts.Where(s => s > 0).ToHashSet();
        if (doc.Parts.Count == 0 || starts.Count == 0)
            return (time, key);
        var times = new List<ImportTime?>();
        var keys = new List<ImportKey?>();
        var (t, k) = (time, key);
        var measures = doc.Parts[0].Measures;
        // A bar that changes the value itself writes it anyway, so only the others vote.
        for (int i = 0; i < measures.Count; i++)
        {
            bool timeChanges = measures[i].Time is { } mt && mt != t;
            bool keyChanges = measures[i].Key is { } mk && mk != k;
            t = measures[i].Time ?? t;
            k = measures[i].Key ?? k;
            if (starts.Contains(i))
            {
                if (!timeChanges)
                    times.Add(t);
                if (!keyChanges)
                    keys.Add(k);
            }
        }
        return (MostCommon(times, time), MostCommon(keys, key));
    }

    // The value met most often, the fallback on a tie with it.
    private static T MostCommon<T>(List<T> values, T fallback)
    {
        int Count(T v) => values.Count(x => EqualityComparer<T>.Default.Equals(x, v));
        int best = Count(fallback);
        T result = fallback;
        foreach (var v in values.Distinct())
            if (Count(v) > best)
                (best, result) = (Count(v), v);
        return result;
    }

    /// <summary>A part's per-measure directives: <see cref="Changes"/> where the bar continues
    /// a section, <see cref="Opening"/> where it opens one.</summary>
    private sealed record PartDirectives(string[] Changes, string[] Opening);

    // ⚠️ OPENING ALSO RESTATES what a section boundary resets — the meter, the key and the
    // clef (SYNTAX_REFERENCE "Across a section boundary") — wherever the one in force is not
    // the file's: without it, a section cut after a key change would be read in the header's
    // key. The tempo is not reset and is not restated.
    // ⚠️ AND ONLY THERE: a `time` or `key` restating the file's value where the section before
    // also left it would still be a needless line of source (it draws nothing since 2026-10-02,
    // MeasureBuilder.SectionHead — but the source is the user's to read).
    // ⚠️ THE SOURCE'S OWN RESTATEMENT IS ANOTHER THING: a bar that states the key again without
    // changing it writes `key!` (CutPoints, owner's decision 2026-10-02), at a section's head
    // or not, and the plain restatement is not written beside it.
    private static PartDirectives Directives(
        ImportDocument doc, ImportPart part, (ImportTime? Time, ImportKey? Key) header,
        HashSet<int> systemStarts, ImportReport report)
    {
        var (time, key) = header;
        var restated = Restatements(part, systemStarts);
        string clef = part.Clef;
        int? tempo = doc.Tempo;
        // The directive that states each one in force (KeyToLily once per change, so its
        // warnings are not repeated per bar).
        string timeText = "", keyText = "", clefText = "";
        var changes = new string[part.Measures.Count];
        var opening = new string[part.Measures.Count];
        for (int i = 0; i < part.Measures.Count; i++)
        {
            var m = part.Measures[i];
            var sb = new StringBuilder();
            var open = new StringBuilder();
            // A change is written where the bar makes it; a section opening on the bar writes
            // the value in force wherever it is not the file's.
            void State(bool changed, bool notTheFiles, string text)
            {
                if (changed)
                    sb.Append(text);
                if (notTheFiles)
                    open.Append(text);
            }
            // A statement that changes nothing is forced (CutPoints), wherever it stands.
            void Force(string text)
            {
                text = text.Insert(text.IndexOf(' '), "!");
                sb.Append(text);
                open.Append(text);
            }
            bool timeChanged = false, keyChanged = false, clefChanged = false;
            if (m.Time is { } t && t != time)
            {
                (time, timeChanged) = (t, true);
                timeText = $"time {t.Beats}/{t.BeatType} ";
            }
            if (restated[i].Time)
                Force($"time {m.Time!.Value.Beats}/{m.Time.Value.BeatType} ");
            else
                State(timeChanged, time != header.Time, timeText);
            if (m.Key is { } k && k != key)
            {
                (key, keyChanged) = (k, true);
                keyText = "key " + KeyToLily(k, report) + " ";
            }
            if (restated[i].Key)
                Force(keyText != "" ? keyText : "key " + KeyToLily(m.Key!.Value, report) + " ");
            else
                State(keyChanged, key != header.Key, keyText);
            if (part.StaffGroup == null && m.Clef is { } c && c != clef)
            {
                (clef, clefChanged) = (c, true);
                clefText = "clef " + c + " ";
            }
            if (restated[i].Clef)
                Force("clef " + m.Clef + " ");
            else
                State(clefChanged, clef != part.Clef, clefText);
            // The tempo is not reset: written where it changes, whether a section opens or not.
            if (m.Tempo is int bpm && bpm != tempo)
            {
                tempo = bpm;
                State(true, true, $"tempo {bpm} ");
            }
            changes[i] = sb.ToString();
            opening[i] = open.ToString();
        }
        return new PartDirectives(changes, opening);
    }

    private static RelativeOctave? Rel(bool relative) => relative ? new RelativeOctave() : null;

    private static string WriteMeasureItems(
        List<ImportItem> items, ImportReport report, RelativeOctave? rel = null, int multiRest = 1)
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
                // A whole-measure rest is `R` (and `R…*N` when the bars fold — WriteVoiceRange);
                // its post-events follow as a note's do. Until 2026-09-30 a rest was written
                // bare: `R1*4@p`, `r2@fermata` and `r4@f` came back as plain rests, and a tuplet
                // opening or closing on a rest lost its brace.
                // A whole-measure rest is a bare `R` (owner's decision 2026-10-02, Music.BarRest):
                // it lasts its bar whatever the meter — `R1` read a 5/4 bar's rest as four
                // quarters, and no note value spells five.
                string rest = note.IsMeasureRest && !note.IsSpacer
                    ? "R" + (multiRest > 1 ? "*" + multiRest : "")
                    : (note.IsSpacer ? "s" : "r") + Value(note.NoteValue, note.Dots)
                      + (note.IsMeasureRest && multiRest > 1 ? "*" + multiRest : "");
                // A chord symbol stands on a rest's beat (owner's decision 2026-09-28: the
                // page draws `r1@chord(C)`); figured bass on a rest has no Lily# spelling.
                if (pendingChord != null)
                    rest += pendingChord;
                pendingChord = null;
                pendingFig = null;
                foreach (var art in note.Articulations)
                    rest += "@" + art;
                if (note.TupletStart is { } rt)
                {
                    tokens.Add($"tuplet {rt.Actual}/{rt.Normal} {{");
                    tupletDepth++;
                }
                tokens.Add(rest);
                if (note.TupletStop)
                {
                    tokens.Add("}");
                    tupletDepth = Math.Max(0, tupletDepth - 1);
                }
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
            string? graceToken = note.LeadingGrace.Count > 0 ? GraceBlock(note.LeadingGrace, rel, report) : null;

            // A chord member's string number and fingering are its own, so they are written
            // inside the brackets (<e\5 dis'\4>); a single note's follow its duration (c4\3),
            // the canonical post-event order (HANDOFF §3: `核 \N @… ] ) ( [ ~`).
            string body = members.Count == 1
                ? (rel != null ? rel.Note(note.Step, note.Alter, note.Octave) : Pitch(note))
                : (rel != null ? rel.Chord(members)
                               : "<" + string.Join(" ", members.Select(m => Pitch(m) + MemberMarks(m))) + ">");
            string token = body + Value(note.NoteValue, note.Dots);
            if (members.Count == 1)
                token += MemberMarks(note);
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
            // Close before open, the order both engines read them in.
            if (note.PhrasingSlurStop)
                token += "@!phrasingSlur";
            if (note.PhrasingSlurStart)
                token += "@phrasingSlur" + note.PhrasingSlurPlacement switch
                {
                    "above" => ".up",
                    "below" => ".down",
                    _ => "",
                };
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

    /// <summary>The marks a note carries on its own pitch: <c>\N</c> then <c>@finger(N)</c>.</summary>
    private static string MemberMarks(ImportNote note)
        => (note.StringNumber is { } s ? "\\" + s : "")
           + (note.Fingering is { } f ? "@finger(" + f + ")" : "");

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
    /// <remarks>
    /// The one slur a grace group carries is a <c>(</c> on its LAST note, closed on the main
    /// note — the only one the page engraves (LYS4020) — so only that one is written; a slur
    /// another program opens earlier in the group is reported and dropped.
    /// </remarks>
    private static string GraceBlock(List<ImportGraceNote> grace, RelativeOctave? rel, ImportReport report)
    {
        string keyword = grace[0].Slash ? "acciaccatura" : "grace";
        // A grace chord is its head note plus the <chord/> members after it — one column, the
        // way the main stream's chord is gathered (WriteMeasureItems). Until 2026-09-30 each
        // member came back as a grace note of its own, one after another.
        var columns = new List<List<ImportGraceNote>>();
        foreach (var g in grace)
        {
            if (g.ChordWithPrev && columns.Count > 0)
                columns[^1].Add(g);
            else
                columns.Add(new List<ImportGraceNote> { g });
        }
        if (columns.Take(columns.Count - 1).Any(c => c[0].SlurStart))
            report.Warn("a slur opening inside a grace group before its last note has no Lily# "
                + "spelling (the group's one slur opens on its last note) and is dropped.");
        var tokens = columns.Select((c, i) =>
        {
            var head = c[0];
            string body = c.Count == 1
                ? (rel != null ? rel.Note(head.Step, head.Alter, head.Octave) : PitchToken(head.Step, head.Alter, head.Octave))
                : rel != null
                    ? rel.Chord(c.Select(m => new ImportNote { Step = m.Step, Alter = m.Alter, Octave = m.Octave }).ToList())
                    : "<" + string.Join(" ", c.Select(m => PitchToken(m.Step, m.Alter, m.Octave))) + ">";
            return body + Value(head.NoteValue, head.Dots) + (i == columns.Count - 1 && head.SlurStart ? "(" : "");
        });
        return keyword + " { " + string.Join(" ", tokens) + " }";
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

        /// <summary>Spell a chord the way Lily# reads one: the root's bare LETTER, placed
        /// relative to the running reference, is the chord's ANCHOR; every member's own
        /// <c>'</c>/<c>,</c> marks — the root's included — are local to that member; every
        /// later member STACKS above the anchor (at or above its letter); and the reference
        /// then advances to the ANCHOR, not to the root's sounding octave.</summary>
        /// <remarks>
        /// The rule is the page's and the exporter's (<c>MusicXmlExporter.ResolveChordMemberPitch</c>,
        /// user decision 2026-09-27). ⚠️ Until 2026-09-30 this stacked the members above the
        /// root's SOUNDING octave and advanced to it, which agrees only while the root needs
        /// no marks: <c>c'''4 &lt;e' g'&gt;4</c> came back as <c>&lt;e,, g&gt;</c> — E5 and G7 —
        /// and every note after it two octaves high.
        /// </remarks>
        public string Chord(IReadOnlyList<ImportNote> members)
        {
            int rootStep = Mod7(members[0].Step);
            int anchorOctave = DefaultOctave(_ref, rootStep);
            var parts = new List<string>();
            for (int i = 0; i < members.Count; i++)
            {
                var m = members[i];
                int letter = Mod7(m.Step);
                int placed = i == 0 ? anchorOctave : anchorOctave + (letter >= rootStep ? 0 : 1);
                parts.Add(Format(letter, m.Alter, m.Octave - placed) + MemberMarks(m));
            }
            _ref = anchorOctave * 7 + rootStep;
            return "<" + string.Join(" ", parts) + ">";
        }

        // The octave a bare letter takes: the one nearest the reference.
        private static int DefaultOctave(int refDiatonic, int letter)
            => (int)System.Math.Round((refDiatonic - letter) / 7.0, System.MidpointRounding.AwayFromZero);

        private static string Spell(int refDiatonic, int step, int alter, int octave)
        {
            int letter = Mod7(step);
            return Format(letter, alter, octave - DefaultOctave(refDiatonic, letter));
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

    /// <summary>A measure's own right bar line as a section writes it. A repeat is the form's,
    /// so it reads as plain here.</summary>
    /// <remarks>
    /// Since LYS1034 (2026-08-31) a repeat barline is legal only in a <c>form</c>, and
    /// <see cref="TryFactorPlainRepeats"/> cuts a section at every one. The repeat spellings
    /// this used to write (<c>|:</c> <c>:|</c> <c>:|:</c>) left with the one-line flat writer,
    /// 2026-10-01.
    /// </remarks>
    private static string PlainOrDoubleBarline(ImportMeasure m) => m.BarlineRight switch
    {
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

    // ---- figured bass (@figuredBass) -------------------------------------

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
        return parts.Count > 0 ? "@figuredBass(" + string.Join(" ", parts) + ")" : null;
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

    // A value as the INSIDE of a regular literal — C#'s escapes (StringLiteral.Quote without
    // its quotes): the reader decodes exactly these.
    internal static string EscapeString(string s)
    {
        string quoted = Syntax.StringLiteral.Quote(s);
        return quoted[1..^1];
    }
}
