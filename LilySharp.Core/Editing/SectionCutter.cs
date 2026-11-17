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
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using LilySharp.Core.Syntax;
using static LilySharp.Core.Editing.SectionJoiner;

namespace LilySharp.Core.Editing;

/// <summary>
/// "Split Section Here": a section cut in two at a bar line — every part, chord row and lyrics track
/// at the same bar — the second half a new section declared right after it, and every form playing
/// the two where it played the one (owner's request 2026-11-17: Split Sections to Match a Part
/// follows a part that already cut; this cuts where the writer says).
/// </summary>
/// <remarks>
/// The inverse of <see cref="SectionJoiner"/>, with the same care: a section boundary RESETS what a
/// section starts from (MeasureCollector.ProcessSectionPrologue), so the new section is given back
/// what ran on at the cut — the section's header <c>time</c> / <c>key</c>; a meter, key or clef the
/// first half changed in a cell's music, restated at the head of that cell; the running note value,
/// on a first note that wrote none; and, under relative octaves, the octave marks that keep its first
/// pitch where it sounded. Refused with the reason: a section written by part, an inline repeat in
/// it, words with no bar lines to cut by, a name already taken, a form playing the section with
/// octave marks. Checked before it is offered, as the join is: it parses, no error is new, and under
/// every form every part sounds as before and the words fall on the same notes.
/// </remarks>
public static class SectionCutter
{
    public sealed record Result(string? NewText, string? Plan, string? Error);

    private static Result Fail(string error) => new(null, null, error);

    /// <summary>Where a caret cuts: the top-level section whose cell it is in, and how many of that
    /// cell's bars stand before it (bars whose bar line is at or before the caret). Null where it
    /// cuts nothing — outside a cell, before the first bar line, after the last.</summary>
    public static (string Section, int Bars)? PlaceAt(SyntaxTree tree, int caret)
    {
        foreach (var s in tree.GetRoot().ChildNodes().OfType<SectionDeclarationSyntax>())
        {
            if (caret < InkStart(s) || caret > InkEnd(s))
                continue;
            foreach (var cell in Cells(s))
            {
                var (from, to) = BodyRange(cell.Node);
                if (caret < from || caret > to)
                    continue;
                var ends = BarEnds(cell.Node);
                int k = ends.Count(e => e <= caret);
                return k > 0 && k < ends.Count ? (s.SectionName, k) : null;
            }
            return null;
        }
        return null;
    }

    /// <summary>The top-level section whose text holds <paramref name="offset"/>, or null — for saying why
    /// a caret there cuts nothing.</summary>
    public static string? SectionAt(SyntaxTree tree, int offset)
        => tree.GetRoot().ChildNodes().OfType<SectionDeclarationSyntax>()
            .FirstOrDefault(s => InkStart(s) <= offset && offset <= InkEnd(s))?.SectionName;

    /// <summary>A free name for the second half: <c>A2</c>, <c>A3</c>, … (an <c>A2</c> cut gives <c>A3</c>).</summary>
    public static string SuggestName(SyntaxTree tree, string section)
    {
        var taken = tree.GetRoot().DescendantNodes<SectionDeclarationSyntax>().Select(s => s.SectionName).ToHashSet(StringComparer.Ordinal);
        var m = Regex.Match(section, @"^(.*?)(\d+)$");
        string stem = m.Success ? m.Groups[1].Value : section;
        for (int n = m.Success ? int.Parse(m.Groups[2].Value) + 1 : 2; ; n++)
            if (!taken.Contains(stem + n))
                return stem + n;
    }

    /// <summary>Cuts section <paramref name="section"/> after its first <paramref name="bars"/> bars; the
    /// rest becomes section <paramref name="newName"/>, declared right after it.</summary>
    public static Result Cut(string source, string section, int bars, string newName)
    {
        var tree = SyntaxTree.Parse(source);
        if (tree.HasErrors)
            return Fail("The file has syntax errors — no changes made.");
        var root = tree.GetRoot();
        if (!Regex.IsMatch(newName, @"^[A-Za-z_][A-Za-z0-9_\-]*$"))
            return Fail($"'{newName}' cannot be a section name (a letter, then letters, digits, '_' and '-').");
        if (root.DescendantNodes<SectionDeclarationSyntax>().Any(s => s.SectionName == newName))
            return Fail($"There is a section {newName} already.");
        var s = root.ChildNodes().OfType<SectionDeclarationSyntax>().FirstOrDefault(x => x.SectionName == section);
        if (s == null)
            return Fail($"There is no section {section} written as a section of the score.");
        foreach (var n in root.DescendantNodes<SectionDeclarationSyntax>())
            if (n.Parent is not CompilationUnitSyntax && n.SectionName == section)
                return Fail($"Section {section} is written inside a part or a track — split works on sections written as `section {section} {{ part {{ … }} }}`.");

        // ---- each cell cut at the bar ------------------------------------------------------
        var cells = Cells(s).ToList();
        if (cells.Count == 0)
            return Fail($"Section {section} has nothing to cut.");
        var edits = new List<Edit>();
        var seconds = new List<(string Head, string Text, SyntaxNode Cell, int ContentStart, int PrefixLength)>();
        string? tiedAcross = null;   // a cell tying a note over the cut — the likely reason, if the check fails
        foreach (var (node, label, kind) in cells)
        {
            if (node.DescendantNodes().OfType<SyntaxTokenNode>().Any(t => t.Kind is SyntaxKind.RepeatStartBar or SyntaxKind.RepeatBothBar))
                return Fail($"{label} in section {section} writes an inline repeat (`|:`) — cut the section where no repeat is written, or by hand.");
            var ends = BarEnds(node);
            var (from, to) = BodyRange(node);
            if (kind == CellKind.Lyrics && ends.Count == 0 && source[from..to].Trim().Length > 0)
                return Fail($"The words of {label} have no bar lines, so where they are cut cannot be told — mark their bars with `|` first.");
            if (ends.Count < bars)
                continue;   // a cell that ends before the cut stays whole in the first half
            int cut = ends[bars - 1];
            // A line break written after the bar line stays with the first half.
            var after = Regex.Match(source[cut..to], @"^\s*(break|pageBreak|noBreak)\b");
            if (after.Success)
                cut += after.Length;
            string rest = source[cut..to];
            if (rest.Trim().Length == 0)
                continue;
            // A bar repeat (`%`) opening the second half would repeat nothing there; a tie into it
            // would end at the section's end (MEASURED on My Life: b,1~ | b cut gave a half-length note).
            // A run of bar repeats cut in its middle: the second half opens with the bar written out —
            // the run's first bar, which every `%` of it repeats.
            string? writtenOut = null;
            int runStart = -1;
            if (Regex.IsMatch(rest, @"^\s*%"))
            {
                var run = node.DescendantNodes().OfType<PercentRepeatSyntax>().FirstOrDefault(p => InkStart(p) < cut && cut <= InkEnd(p));
                var firstBar = run == null ? null : Enumerable.Range(0, run.SlotCount).Select(run.GetChild).FirstOrDefault(c => c is BarlineSyntax);
                if (run == null || firstBar == null)
                    return Fail($"{label} repeats the bar before the cut (`%` right after it) — the new section would have no bar to repeat. Write that bar out, or cut elsewhere.");
                writtenOut = source[InkStart(run)..InkStart(firstBar)].Trim();
                runStart = InkStart(run);
            }
            if (kind == CellKind.Music && Regex.IsMatch(Regex.Replace(source[from..cut], @"//[^\n]*", ""), @"~\s*(\|\s*)?(break\s*)?$"))
                tiedAcross ??= label;
            int contentStart = cut + (rest.Length - rest.TrimStart().Length);
            // What the first half ran on with in this cell: its inline meter, key and clef, and the note value.
            string prefix = "";
            foreach (var kindOf in new Func<SyntaxNode, bool>[] { n => n is TimeSignatureSyntax, n => n is KeySignatureSyntax, n => n is ClefDeclarationSyntax })
                if (node.DescendantNodes().Where(n => kindOf(n) && InkStart(n) < cut).LastOrDefault() is { } directive)
                    prefix += source[InkStart(directive)..InkEnd(directive)] + " ";
            string text = source[contentStart..to].TrimEnd();
            if (writtenOut != null)
            {
                // (the `%` replaced by its bar; the bar's first pitch is the one to keep where it sounded)
                text = writtenOut + text[1..];
                seconds.Add((source[InkStart(node)..OpenBraceEnd(node)], prefix + text, node, runStart, prefix.Length));
            }
            else
            {
                if (kind == CellKind.Music && FirstLeafAfter(node, cut) is { } leaf && LengthGap(leaf) is int gap
                    && RunningLength(source, node, cut) is { } length && length != "4")
                    text = source[contentStart..gap] + length + source[gap..to].TrimEnd();
                seconds.Add((source[InkStart(node)..OpenBraceEnd(node)], prefix + text, node, contentStart, prefix.Length));
            }
            // The first half keeps what stood before the cut, and the space before its brace.
            edits.Add(new Edit(cut, to, TrailingSpace(source, from, to)));
        }
        if (seconds.Count == 0)
            return Fail($"Nothing of section {section} stands after bar {bars}.");

        // ---- the new section, after the first ---------------------------------------------------
        bool oneLine = !source[InkStart(s)..InkEnd(s)].Contains('\n');
        string indent = IndentOf(source, InkStart(cells[0].Node));
        var header = BodyItems(s).Where(n => n is TimeSignatureSyntax or KeySignatureSyntax).Select(n => source[InkStart(n)..InkEnd(n)]).ToList();
        var sb = new StringBuilder();
        sb.Append(oneLine ? "\n" : "\n\n").Append(IndentOf(source, InkStart(s))).Append("section ").Append(newName).Append(" {");
        foreach (var h in header)
            sb.Append(oneLine ? " " : "\n" + indent).Append(h);
        foreach (var (head, text, _, _, _) in seconds)
        {
            bool cellOneLine = !text.Contains('\n') && !text.Contains("//", StringComparison.Ordinal);
            sb.Append(oneLine ? " " : "\n" + indent).Append(head);
            sb.Append(cellOneLine ? " " + text + " }" : "\n" + indent + "  " + text + "\n" + indent + "}");
        }
        sb.Append(oneLine ? " }" : "\n" + IndentOf(source, InkStart(s)) + "}");
        edits.Add(new Edit(InkEnd(s), InkEnd(s), sb.ToString()));

        // ---- the forms: each play of the section plays the two ----------------------------------
        int plays = 0;
        var seen = new HashSet<int>();
        foreach (var form in root.DescendantNodes<FormDeclarationSyntax>())
            foreach (var r in form.DescendantNodes().Where(IsReference))
            {
                if (RefName(r) != section || !seen.Add(InkStart(r)))
                    continue;
                string written = source[InkStart(r)..InkEnd(r)];
                if (Regex.IsMatch(written, @"^~?[A-Za-z_][A-Za-z0-9_\-]*['’,]+"))
                    return Fail($"A form plays {section} as `{written}` — its octave marks would have to go on both halves; write them out first.");
                edits.Add(new Edit(InkEnd(r), InkEnd(r), " " + (written.StartsWith('~') ? "~" : "") + newName));
                plays++;
            }
        string candidate = Apply(source, edits);

        // ---- the relative frame: each second half's first pitch, where it sounded ------------------
        var pinned = new Dictionary<int, int>();
        var after2 = SyntaxTree.Parse(candidate);
        var made = after2.GetRoot().ChildNodes().OfType<SectionDeclarationSyntax>().FirstOrDefault(x => x.SectionName == newName);
        if (made != null)
        {
            var madeCells = Cells(made).ToList();
            for (int i = 0; i < seconds.Count && i < madeCells.Count; i++)
            {
                var (_, _, cell, contentStart, _) = seconds[i];
                var was = cell.DescendantNodes<PitchSyntax>().FirstOrDefault(p => p.Span.Start >= contentStart);
                var now = madeCells[i].Node.DescendantNodes<PitchSyntax>().FirstOrDefault();
                if (was != null && now != null)
                    pinned[now.Span.Start] = was.Span.Start;
            }
        }
        candidate = Repin(source, candidate, pinned);

        if (Verify(tree, candidate) is { } problem)
            return Fail(tiedAcross != null
                ? $"{tiedAcross} ties a note across bar line {bars}, and the tie would not carry into the new section — cut at another bar line. (What would change:\n{problem})"
                : $"Splitting {section} after bar {bars} would change the piece, so nothing was done:\n{problem}");
        return new Result(candidate,
            $"Split {section} after bar {bars}: the rest is section {newName}" + (plays > 0 ? $"; the forms play {section} {newName} where they played {section}." : "."),
            null);
    }

    // --- helpers ----------------------------------------------------------------------------------

    internal enum CellKind { Music, Chords, Lyrics }

    /// <summary>A top-level section's cells: its parts' music, chord rows and lyrics tracks.</summary>
    internal static IEnumerable<(SyntaxNode Node, string Label, CellKind Kind)> Cells(SectionDeclarationSyntax s)
    {
        foreach (var item in BodyItems(s))
            switch (item)
            {
                case PartBlockSyntax p: yield return (p, p.Name, CellKind.Music); break;
                case ChordPartBlockSyntax { PartName: { } row } c: yield return (c, "chords " + row, CellKind.Chords); break;
                case LyricsBlockSyntax l: yield return (l, "lyrics", CellKind.Lyrics); break;
            }
    }

    /// <summary>The offsets just after each bar line written at the cell's own level (every bar line
    /// closes a bar: an empty bar's too).</summary>
    internal static List<int> BarEnds(SyntaxNode cell)
    {
        // A lyrics cell's bars are its lyric measures, each closed by its own `|` (SameBarFinder.LyricLines):
        // no BarlineSyntax stands there. MEASURED (owner's report 2026-11-17, "03 Open Arms.lys", whose
        // words are `| | | …`): every cut was refused as "the words have no bar lines".
        if (cell is LyricsBlockSyntax)
            return [.. SameBarFinder.LyricLines(cell).Select(l => l.End)];
        var container = OpenBrace(cell)?.Parent ?? cell;
        var ends = new List<int>();
        // (a bar repeat's run holds its own bar lines — `d8 … | % | % | %` is ONE node — so its bars
        // are counted inside it: MEASURED on My Life's bass, whose bars were otherwise 2 for 8)
        void Collect(IEnumerable<SyntaxNode> items)
        {
            foreach (var item in items)
            {
                if (item is BarlineSyntax)
                    ends.Add(InkEnd(item));
                else if (item is PercentRepeatSyntax)
                    Collect(Enumerable.Range(0, item.SlotCount).Select(item.GetChild).Where(c => c != null)!);
            }
        }
        Collect(BodyItems(container));
        return ends;
    }

    private static SyntaxNode? FirstLeafAfter(SyntaxNode cell, int at)
        => cell.DescendantNodes().FirstOrDefault(n => InkStart(n) >= at
            && (n is NoteSyntax or ChordSyntax || (n is RestSyntax r && !r.ToString().TrimStart().StartsWith('R'))));

    /// <summary>The note value written last before <paramref name="at"/> in the cell — what a note with
    /// none after it took — or null when none was (the 4 a section opens with).</summary>
    private static string? RunningLength(string source, SyntaxNode cell, int at)
    {
        DurationSyntax? last = null;
        foreach (var n in cell.DescendantNodes())
        {
            if (InkStart(n) >= at)
                break;
            var d = n switch { NoteSyntax x => x.Duration, RestSyntax x => x.Duration, ChordSyntax x => x.Duration, _ => null };
            if (d != null && !(n is RestSyntax r && r.ToString().TrimStart().StartsWith('R')))
                last = d;
        }
        return last == null ? null : source[InkStart(last)..InkEnd(last)];
    }

    /// <summary>The space a body ends with before its closing brace (a new line and indent, or a space).</summary>
    private static string TrailingSpace(string source, int from, int to)
    {
        int i = to;
        while (i > from && char.IsWhiteSpace(source[i - 1])) i--;
        return source[i..to].Length > 0 ? source[i..to] : " ";
    }
}
