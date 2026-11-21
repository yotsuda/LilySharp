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
using System.Globalization;
using System.Linq;
using System.Text;
using LilySharp.Core.Midi;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using static LilySharp.Core.Editing.SectionJoiner;

namespace LilySharp.Core.Editing;

/// <summary>
/// "Join Bars in Pairs" (Orecchiante's request 21, owner's decisions 2026-11-21,
/// docs/join-bars-design.md): the bars of a chords row the selection touches are joined two by
/// two from the first — <c>C | D |</c> → <c>C D</c> — each bar becoming half of the joined one,
/// for a chart a transcription read at half the bar's true length.
/// </summary>
/// <remarks>
/// <para>
/// The chords keep their place in time, halved: a bar's slots are read on the meter's grid
/// (<see cref="ChordNameCollector.SlotGroups"/>), the first bar's onsets land in the joined bar's
/// first half and the second's in its second half, and the joined bar takes the fewest slots
/// <see cref="ChordRhythm.SlotDurations"/> grids without a warning that hold every onset, the
/// rest filled with <c>.</c>. A <c>%</c> is opened to the bar it repeats; two in a pair stay one.
/// </para>
/// <para>
/// Refused, with the reason, rather than guessed: a second bar that opens silent after a first
/// that sounds a chord (the <c>.</c> would hold that chord on — owner's decision 3), a meter,
/// key or tempo that would stand inside the joined bar, a bar line in a pair that is not a plain
/// <c>|</c>, repeat signs, endings or navigation marks in the range, a pickup, a section with
/// anything else that counts bars (a part's music, words, another row), comments inside a pair.
/// A <c>break</c> that would stand inside the joined bar is dropped and said.
/// </para>
/// <para>
/// A tempo is halved where everything it reaches is joined (owner's decision 1): every tempo
/// mark when the whole chart is; else one at the head of a section's row — or at a bar of it —
/// when the bars from there to the section's end are all joined and every section played after
/// it opens with a tempo of its own (a form with repeat signs or endings is not read: nothing
/// is halved under it).
/// </para>
/// <para>
/// The rewrite is checked before it is offered: it parses, no error is new, and every note
/// plays in the bar it played in, the joined bars' notes at half their offset and length.
/// <see cref="Result.Played"/> names, for the file beside a chart (docs/recording-design.md),
/// the bar played first of every pair, each time the form plays it — counted from the score's
/// first bar played, a pickup being one.
/// </para>
/// </remarks>
public static class BarJoiner
{
    /// <summary>The rewritten text, a one-line account and, for the file beside a chart, the
    /// index of the first bar played of every pair joined (ascending) — or the reason nothing
    /// was done. <see cref="BarsPlayed"/> is how many bars the score played before (the file beside a
    /// chart lines up with it when it has as many).</summary>
    public sealed record Result(string? NewText, string? Plan, string? Error, IReadOnlyList<int>? Played, int BarsPlayed = 0);

    private static Result Fail(string error) => new(null, null, error, null);

    /// <summary>The menu's words.</summary>
    public const string Title = "Join Bars in Pairs";

    // ---- the bars of a row ------------------------------------------------------------------

    private sealed class Bar
    {
        public readonly List<SyntaxNode> Head = [];    // before its first slot: time / tempo / key / break / marks
        public readonly List<SyntaxNode> Slots = [];   // chords, rests, '.', '%'
        public readonly List<SyntaxNode> Tail = [];    // after its last slot, before its bar line
        public SyntaxNode? Line;                        // the bar line closing it (null: the row's end)
        public int Start;                               // where it begins in the source
        public int End;                                 // where it ends (its bar line's end)
        public (int Beats, int BeatType) Meter;
    }

    private sealed class Row
    {
        public required SyntaxNode Node;                 // the chords block or the track's inner section
        public required List<SyntaxNode> Items;
        public required SectionDeclarationSyntax? Section;   // the top-level section it is written in
        public List<Bar> Bars = [];
    }

    /// <summary>The rows of chords the file writes: each block's own items, or each of its inner
    /// sections'.</summary>
    private static List<Row> RowsOf(SyntaxNode root)
    {
        var rows = new List<Row>();
        foreach (var block in root.DescendantNodes<ChordPartBlockSyntax>())
        {
            if (block.HasSections)
            {
                foreach (var inner in block.Sections)
                    rows.Add(new Row
                    {
                        Node = inner,
                        Items = [.. ChordNameCollector.SectionItems(inner)],
                        Section = root.ChildNodes().OfType<SectionDeclarationSyntax>().FirstOrDefault(s => s.SectionName == inner.SectionName),
                    });
                continue;
            }
            SectionDeclarationSyntax? inside = null;
            for (var n = block.Parent; n != null && inside == null; n = n.Parent)
                inside = n as SectionDeclarationSyntax;
            rows.Add(new Row { Node = block, Items = [.. block.Items], Section = inside });
        }
        return rows;
    }

    private static bool IsSlot(SyntaxNode n) => n is ChordEntrySyntax or RestSyntax or ChordExtendSyntax or PercentMarkSyntax;

    /// <summary>The row's bars as its readers count them (<see cref="ChordRowBarLines"/>).</summary>
    private static List<Bar> BarsOf(Row row, (int, int) meter)
    {
        var bars = new List<Bar>();
        var bar = new Bar();
        bool holds = false;
        foreach (var item in row.Items)
        {
            if (item is SyntaxTokenNode)
                continue;
            bool closes = item switch
            {
                BarlineSyntax line => ChordRowBarLines.Closes(line, holds),
                VoltaEndSyntax end => ChordRowBarLines.EndCloses(end, holds),
                _ => false,
            };
            if (closes)
            {
                bar.Line = item;
                bars.Add(bar);
                bar = new Bar();
                holds = false;
                continue;
            }
            if (IsSlot(item))
            {
                bar.Slots.AddRange(bar.Tail);   // (nothing but a slot stands between two slots)
                bar.Tail.Clear();
                bar.Slots.Add(item);
                holds = true;
            }
            else if (holds)
                bar.Tail.Add(item);
            else
                bar.Head.Add(item);
        }
        if (bar.Slots.Count > 0)
            bars.Add(bar);
        // Where each begins and ends, and the meter it is read in.
        int prevEnd = row.Items.Count > 0 ? InkStart(row.Items[0]) : 0;
        foreach (var b in bars)
        {
            var first = b.Head.Concat(b.Slots).Concat(b.Tail).Cast<SyntaxNode?>().FirstOrDefault() ?? b.Line;
            b.Start = first != null ? InkStart(first) : prevEnd;
            b.End = b.Line != null ? InkEnd(b.Line) : InkEnd(b.Slots.Concat(b.Tail).Last());
            if (b.Head.OfType<TimeSignatureSyntax>().LastOrDefault() is { } t && !t.IsSenzaMisura && t.Denominator != null)
                meter = (t.Beats, int.Parse(t.Denominator.Text, CultureInfo.InvariantCulture));
            b.Meter = meter;
            prevEnd = b.End;
        }
        return bars;
    }

    private static (int, int) MeterOf(TimeSignatureSyntax? t)
        => t == null || t.IsSenzaMisura || t.Denominator == null || !int.TryParse(t.Denominator.Text, out int d) ? (4, 4) : (t.Beats, d);

    /// <summary>Whether a selection [<paramref name="start"/>, <paramref name="end"/>] touches two
    /// or more bars of a chords row — when the menu offers the join (worked out, and checked,
    /// when picked).</summary>
    public static bool Offers(SyntaxTree tree, int start, int end)
    {
        if (end <= start)
            return false;
        var root = tree.GetRoot();
        foreach (var row in RowsOf(root))
        {
            if (row.Items.Count == 0 || InkEnd(row.Items[^1]) < start || end < InkStart(row.Items[0]))
                continue;
            if (BarsOf(row, (4, 4)).Count(b => b.Start < end && start < b.End) >= 2)
                return true;
        }
        return false;
    }

    // ---- the join ---------------------------------------------------------------------------

    /// <summary>Joins in pairs the bars of every chords row [<paramref name="start"/>,
    /// <paramref name="end"/>] touches. <paramref name="scoreName"/> is the score the preview
    /// plays (null: the file's first), whose form <see cref="Result.Played"/> counts.</summary>
    public static Result Join(string source, int start, int end, string? scoreName = null)
    {
        if (end <= start)
            return Fail("Select the bars to join — two or more bars of a chords row.");
        var tree = SyntaxTree.Parse(source);
        if (tree.HasErrors)
            return Fail("The file has syntax errors — no changes made.");
        var root = tree.GetRoot();
        var rows = RowsOf(root);
        var scoreMeter = MeterOf(root.ChildNodes().OfType<TimeSignatureSyntax>().LastOrDefault());
        foreach (var row in rows)
        {
            var headerTime = row.Section == null ? null
                : BodyItems(row.Section).OfType<TimeSignatureSyntax>().LastOrDefault();
            row.Bars = BarsOf(row, headerTime != null ? MeterOf(headerTime) : scoreMeter);
        }

        // ---- the bars the selection touches, row by row ----------------------------------------
        var picked = new List<(Row Row, List<int> Bars)>();
        foreach (var row in rows)
        {
            var touched = Enumerable.Range(0, row.Bars.Count)
                .Where(i => row.Bars[i].Start < end && start < row.Bars[i].End).ToList();
            if (touched.Count >= 2)
                picked.Add((row, touched));
        }
        if (picked.Count == 0)
            return Fail("Select two or more bars of a chords row to join them in pairs.");

        var notes = new List<string>();
        var edits = new List<Edit>();
        var joinedBars = new HashSet<Bar>();
        foreach (var (row, touched) in picked)
        {
            string where = row.Section != null ? $"section {row.Section.SectionName}" : "the chords row";
            // Anything else that counts bars where the row is written would be left a bar off.
            if (row.Section != null)
            {
                foreach (var item in BodyItems(row.Section))
                    if (item is PartBlockSyntax or LyricsBlockSyntax || (item is ChordPartBlockSyntax other && other != row.Node))
                        return Fail($"{Cap(where)} writes `{Short(source, item)[..Math.Min(30, Short(source, item).Length)]}` beside the chords — its bars would no longer line up. Join Bars in Pairs works on a section that writes one chords row alone.");
                if (touched[0] == 0 && BodyItems(row.Section).Any(i => i is PartialDeclarationSyntax))
                    return Fail($"{Cap(where)} opens with a pickup — leave its first bar out of the range.");
            }
            else if (root.DescendantNodes().Any(n => n is PartBlockSyntax or LyricsBlockSyntax) || rows.Count(r => r.Section == null) > 1)
                return Fail("The file writes music or words beside this chords row — their bars would no longer line up. Join Bars in Pairs works on a chart of one chords row.");

            for (int t = 0; t < touched.Count; t++)
            {
                var bar = row.Bars[touched[t]];
                foreach (var item in bar.Head.Concat(bar.Slots).Concat(bar.Tail))
                    if (item is InlineVoltaSyntax or VoltaEndSyntax or NavigationMarkSyntax or BarlineSyntax)
                        return Fail($"`{Short(source, item)}` stands among the bars selected ({Where(source, item)}) — Join Bars in Pairs does not cross repeat signs, endings or navigation marks.");
                if (bar.Line is VoltaEndSyntax || (bar.Line is BarlineSyntax bl && bl.BarText.Contains(':') && t < touched.Count - 1))
                    return Fail($"`{Short(source, bar.Line!)}` stands among the bars selected ({Where(source, bar.Line!)}) — Join Bars in Pairs does not cross repeat signs, endings or navigation marks.");
            }
            // (A meter may change at a pair's first bar: the joined bar opens with it. At its second
            // bar it would stand inside the joined bar — refused with the pair below.)

            int pairs = touched.Count / 2;
            if (touched.Count % 2 == 1)
                notes.Add($"the bar on {Where(source, row.Bars[touched[^1]].Slots.FirstOrDefault() ?? row.Bars[touched[^1]].Line ?? row.Node)} was left as it is (an odd bar out)");
            string? previous = null;
            for (int p = 0; p < pairs; p++)
            {
                var a = row.Bars[touched[2 * p]];
                var b = row.Bars[touched[2 * p + 1]];
                if (b.Head.FirstOrDefault(h => h is TempoDeclarationSyntax or KeySignatureSyntax or TimeSignatureSyntax) is { } inside)
                    return Fail($"`{Short(source, inside)}` would stand inside the joined bar ({Where(source, inside)}) — move it, or start the range a bar later.");
                if (a.Line is BarlineSyntax { IsLeftOut: false } mid && mid.BarText != "|")
                    return Fail($"`{mid.BarText}` stands between two bars to be joined ({Where(source, mid)}) — Join Bars in Pairs removes plain bar lines only.");
                int spanStart = a.Slots.Count > 0 ? InkStart(a.Slots[0]) : InkStart(a.Line!);
                int spanEnd = b.Slots.Count > 0 ? InkEnd(b.Slots[^1]) : InkStart(b.Line ?? a.Line!);
                if (source[spanStart..spanEnd].Contains("//", StringComparison.Ordinal) || source[spanStart..spanEnd].Contains("/*", StringComparison.Ordinal))
                    return Fail($"A comment stands between two bars to be joined ({Where(source, a.Line!)}) — move it first.");
                foreach (var dropped in a.Tail.Concat(b.Head).OfType<BreakSyntax>())
                    notes.Add($"the `{Short(source, dropped)}` between two joined bars ({Where(source, dropped)}) was dropped");

                var slotsA = Resolved(row, touched[2 * p]);
                var slotsB = Resolved(row, touched[2 * p + 1]);
                if (slotsA == null || slotsB == null)
                    return Fail($"A `%` in the range repeats no bar of its row ({Where(source, a.Line ?? row.Node)}).");
                string? text = JoinedSlots(source, slotsA, slotsB, a.Meter, out string? why);
                if (text == null)
                    return Fail(why!.Replace("{where}", Where(source, b.Slots.FirstOrDefault() ?? b.Line ?? a.Line!)));
                // Two '%' in a pair whose joined bar is the pair before it again: one '%'.
                string written = text;
                if (p > 0 && a.Slots is [PercentMarkSyntax] && b.Slots is [PercentMarkSyntax] && text == previous)
                    text = "%";
                previous = written;
                edits.Add(new Edit(spanStart, spanEnd, text));
                // Text that ran onto the next line before the second bar: the joined bar keeps the
                // line, and the line break moves after it.
                if (b.Line != null && source[spanStart..spanEnd].Contains('\n'))
                {
                    int lineEnd = InkEnd(b.Line), ink = lineEnd;
                    while (ink < source.Length && source[ink] is ' ' or '\t') ink++;
                    if (ink < source.Length && source[ink] is not ('\r' or '\n'))
                    {
                        // The indent of the line the second bar stood on.
                        int lineStart = source.LastIndexOf('\n', spanEnd - 1) + 1, indentEnd = lineStart;
                        while (source[indentEnd] is ' ' or '\t') indentEnd++;
                        edits.Add(new Edit(lineEnd, ink, (source.Contains("\r\n") ? "\r\n" : "\n") + source[lineStart..indentEnd]));
                    }
                }
                joinedBars.Add(a);
                joinedBars.Add(b);
            }
        }

        // ---- the tempo marks whose reach is all joined ----------------------------------------
        var halved = TemposToHalve(root, rows, picked, joinedBars);
        foreach (var tempo in halved)
        {
            var (_, count, _, _) = tempo.ValuePositions;
            var token = tempo.Values.OfType<SyntaxTokenNode>().First(v => v.Span.Start == count);
            double bpm = double.Parse(token.Text, CultureInfo.InvariantCulture) / 2;
            edits.Add(new Edit(token.Span.Start, token.Span.Start + token.Span.Length, bpm.ToString("0.##", CultureInfo.InvariantCulture)));
        }
        string candidate = Apply(source, edits);

        // ---- where the pairs are played, and the check -----------------------------------------
        var score = ChooseScore(tree, scoreName);
        var before = new MidiExporter { Form = score?.Form, Score = score }.Export(tree);
        var played = PlayedPairs(before, picked);
        if (played == null)
            return Fail(score == null
                ? "The file has no score, so its chords row plays nowhere and the joined bars could not be checked — add a score that places the row (`score { chords … }`)."
                : "The bars selected sound no chord in the score played, so where they are played could not be read — nothing was done.");
        if (Verify(tree, candidate, scoreName, before, played) is { } problem)
            return Fail($"Joining these bars would change the piece, so nothing was done:\n{problem}");

        int joinedCount = picked.Sum(p => p.Bars.Count / 2);
        string plan = $"Joined {joinedCount * 2} bars into {joinedCount}";
        if (halved.Count > 0)
            notes.Add($"halved {(halved.Count == 1 ? "the tempo" : $"{halved.Count} tempo marks")} ({string.Join(", ", halved.Select(t => Short(source, t)))})");
        return new Result(candidate, plan + (notes.Count > 0 ? "; " + string.Join("; ", notes) : "") + ".", null, played, BarStarts(before).Count);
    }

    private static string Cap(string s) => char.ToUpperInvariant(s[0]) + s[1..];

    private static RenderSpec? ChooseScore(SyntaxTree tree, string? scoreName)
    {
        try { return RenderSpecParser.Choose(RenderSpecParser.FindAll(tree), scoreName); }
        catch { return null; }
    }

    /// <summary>A bar's slots with a lone <c>%</c> opened to the bar it repeats; null when it
    /// repeats none.</summary>
    private static List<SyntaxNode>? Resolved(Row row, int index)
    {
        for (int i = index; i >= 0; i--)
            if (row.Bars[i].Slots is not [PercentMarkSyntax])
                return row.Bars[i].Slots;
        return null;
    }

    /// <summary>The joined bar's slots as text, or null with the reason (<c>{where}</c> for the
    /// caller to fill).</summary>
    private static string? JoinedSlots(string source, List<SyntaxNode> a, List<SyntaxNode> b, (int Beats, int BeatType) meter, out string? why)
    {
        why = null;
        var whole = new Fraction(meter.Beats, meter.BeatType);
        var half = whole * new Fraction(1, 2);
        var onsets = new List<(Fraction At, string Text)>();
        bool aSounds = false;
        foreach (var (slots, offset) in new[] { (a, Fraction.Zero), (b, half) })
        {
            if (slots.Count == 0)
                continue;
            var groups = ChordNameCollector.SlotGroups(slots, meter.Beats, meter.BeatType, out _);
            for (int g = 0; g < groups.Count; g++)
            {
                var (node, timing, _) = groups[g];
                if (node is ChordExtendSyntax)
                {
                    if (g == 0 && ReferenceEquals(slots, b) && aSounds)
                    {
                        why = "The second bar of a pair opens silent ({where}) after a bar that sounds a chord — joined, the '.' would hold that chord on. Write what the silence is (an `r`, or the chord), or start the range a bar later.";
                        return null;
                    }
                    continue;
                }
                onsets.Add((offset + timing * new Fraction(1, 2), source[InkStart(node)..InkEnd(node)]));
                if (ReferenceEquals(slots, a))
                    aSounds = true;
            }
        }
        if (b.Count == 0 && aSounds)
        {
            why = "The second bar of a pair is empty ({where}) after a bar that sounds a chord — joined, the first bar's chord would hold on through it. Write what the silence is (an `r`), or start the range a bar later.";
            return null;
        }
        // The second bar opening on the chord the first bar ends on, written alike (its `@or(…)`
        // and all): a chart that writes a chord a bar says "these bars are C", not "strike C
        // again" — joined, it is held (Orecchiante's request 22: `C | C |` -> `C`, not `C C`;
        // `A | A E/G# |` -> `A . . E/G#`). The check lets these strikes become holds (Verify).
        int second = onsets.FindIndex(o => o.At == half);
        if (second > 0 && aSounds && onsets[second - 1].Text == onsets[second].Text)
            onsets.RemoveAt(second);
        if (onsets.Count == 0)
            return a.Count == 0 && b.Count == 0 ? "" : ".";
        int beatCount = ChordRhythm.BeatsOf(meter.Beats, meter.BeatType).Count;
        for (int s = 1; s <= 16 * beatCount; s++)
        {
            var durations = ChordRhythm.SlotDurations(s, meter.Beats, meter.BeatType);
            if (durations == null)
                continue;
            var at = new Dictionary<Fraction, int>();
            var pos = Fraction.Zero;
            for (int i = 0; i < s; i++)
            {
                at[pos] = i;
                pos += durations.Value[i];
            }
            if (!onsets.All(o => at.ContainsKey(o.At)))
                continue;
            var cells = Enumerable.Repeat(".", s).ToArray();
            foreach (var (o, text) in onsets)
                cells[at[o]] = text;
            return string.Join(" ", cells);
        }
        why = "The chords of a pair ({where}) fall where no grid of the meter's beats holds them all — join those bars by hand.";
        return null;
    }

    // ---- tempo ---------------------------------------------------------------------------------

    /// <summary>The tempo marks everything of whose reach is joined (owner's decision 1, the
    /// class remarks).</summary>
    private static List<TempoDeclarationSyntax> TemposToHalve(SyntaxNode root, List<Row> rows,
        List<(Row Row, List<int> Bars)> picked, HashSet<Bar> joined)
    {
        static bool HasBpm(TempoDeclarationSyntax t) => t.ValuePositions.Count != 0;
        var all = root.DescendantNodes<TempoDeclarationSyntax>().Where(HasBpm).ToList();
        // The whole chart joined: every bar of every row, and no music beside them.
        bool everything = rows.Count > 0 && rows.All(r => r.Bars.All(joined.Contains))
            && !root.DescendantNodes().Any(n => n is PartBlockSyntax or LyricsBlockSyntax);
        if (everything)
            return all;

        var result = new List<TempoDeclarationSyntax>();
        var next = SectionsPlayedNext(root);
        if (next == null)
            return result;
        foreach (var (row, _) in picked)
        {
            if (row.Section is not { } section)
                continue;
            // The bars from a bar to the row's end, all joined.
            int from = row.Bars.Count;
            while (from > 0 && joined.Contains(row.Bars[from - 1])) from--;
            if (from == row.Bars.Count)
                continue;
            // Every section played after this one opens with its own tempo.
            if (!next.TryGetValue(section.SectionName, out var after) || !after.All(n => OpensWithATempo(root, rows, n)))
                continue;
            var marks = new List<TempoDeclarationSyntax>();
            if (from == 0)
                marks.AddRange(BodyItems(section).OfType<TempoDeclarationSyntax>());
            for (int i = from; i < row.Bars.Count; i++)
                // A pair's first bar (the second's head refused one).
                marks.AddRange(row.Bars[i].Head.OfType<TempoDeclarationSyntax>());
            result.AddRange(marks.Where(HasBpm));
        }
        return result;
    }

    /// <summary>For each section, the sections played right after it — none at the end — under
    /// every form; null when a form writes repeat signs or endings (not read here).</summary>
    private static Dictionary<string, List<string>>? SectionsPlayedNext(SyntaxNode root)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        void Add(string a, string? b)
        {
            if (!map.TryGetValue(a, out var list))
                map[a] = list = [];
            if (b != null && !list.Contains(b))
                list.Add(b);
        }
        var forms = root.DescendantNodes<FormDeclarationSyntax>().Where(f => !f.IsReference).ToList();
        if (forms.Count == 0)
        {
            var declared = root.ChildNodes().OfType<SectionDeclarationSyntax>().Select(s => s.SectionName).ToList();
            for (int i = 0; i < declared.Count; i++)
                Add(declared[i], i + 1 < declared.Count ? declared[i + 1] : null);
            return map;
        }
        foreach (var form in forms)
        {
            if (form.DescendantNodes().Any(n => n is BarlineSyntax or InlineVoltaSyntax or VoltaEndSyntax or NavigationMarkSyntax)
                || form.DescendantNodes().OfType<SyntaxTokenNode>().Any(t => t.Text is "[" or "]" or "|:" or ":|"))
                return null;
            var refs = form.DescendantNodes().Where(IsReference).Select(RefName).ToList();
            for (int i = 0; i < refs.Count; i++)
                Add(refs[i], i + 1 < refs.Count ? refs[i + 1] : null);
        }
        return map;
    }

    private static bool OpensWithATempo(SyntaxNode root, List<Row> rows, string sectionName)
    {
        var section = root.ChildNodes().OfType<SectionDeclarationSyntax>().FirstOrDefault(s => s.SectionName == sectionName);
        if (section == null)
            return false;
        if (BodyItems(section).Any(i => i is TempoDeclarationSyntax))
            return true;
        return rows.Any(r => r.Section == section && r.Bars.Count > 0 && r.Bars[0].Head.Any(h => h is TempoDeclarationSyntax));
    }

    // ---- where the pairs are played ----------------------------------------------------------

    /// <summary>The bars the score plays, as their first ticks (a pickup being one: the first
    /// accent after tick 0 makes a bar of what is before it — recordingCore.ts' scoreBars).</summary>
    internal static List<int> BarStarts(MidiFile midi)
    {
        var beats = MetronomeBeats.Of(midi, midi.TicksPerQuarterNote, out int barEnd);
        var starts = beats.Where(b => b.Accent).Select(b => b.Tick).ToList();
        if (starts.Count == 0 || starts[0] > 0)
            starts.Insert(0, 0);
        // (a bar is one with time in it: none starts at the music's end)
        while (starts.Count > 1 && starts[^1] >= barEnd)
            starts.RemoveAt(starts.Count - 1);
        return starts;
    }

    private static int BarAt(List<int> starts, int tick)
    {
        int i = starts.BinarySearch(tick);
        return i >= 0 ? i : ~i - 1;
    }

    /// <summary>The first bar played of every pair, each time it is played, ascending — read
    /// off the notes of the range's chords (a bar played is where a note of it starts; the
    /// range plays its bars one after another each time, no repeat sign standing in it).</summary>
    private static List<int>? PlayedPairs(MidiFile midi, List<(Row Row, List<int> Bars)> picked)
    {
        var starts = BarStarts(midi);
        var bySource = new Dictionary<int, List<int>>();
        foreach (var n in midi.Tracks.SelectMany(t => t.Notes))
        {
            int at = n.MemberPos >= 0 ? n.MemberPos : n.SourcePos;
            if (at < 0)
                continue;
            if (!bySource.TryGetValue(at, out var list))
                bySource[at] = list = [];
            int bar = BarAt(starts, n.StartTick);
            if (!list.Contains(bar))
                list.Add(bar);
        }
        var result = new List<int>();
        foreach (var (row, touched) in picked)
        {
            // Each bar of the range with a chord of its own says where the range starts each time.
            HashSet<int>? firsts = null;
            for (int t = 0; t < touched.Count; t++)
            {
                var entries = row.Bars[touched[t]].Slots.OfType<ChordEntrySyntax>().ToList();
                if (entries.Count == 0)
                    continue;
                var here = new HashSet<int>();
                foreach (var e in entries)
                    foreach (int at in new[] { e.SourceStart, InkStart(e) })
                        if (bySource.TryGetValue(at, out var bars))
                            foreach (int bar in bars)
                                here.Add(bar - t);
                if (here.Count == 0)
                    continue;
                // A '%' of the row sounds this bar's chords again, d bars later: a start f is
                // the range's only when f + d is one too, for every such d.
                int self = touched[t];
                var repeats = Enumerable.Range(self + 1, row.Bars.Count - self - 1)
                    .TakeWhile(i => row.Bars[i].Slots is [PercentMarkSyntax])
                    .Select(i => i - self).ToList();
                if (repeats.Count > 0)
                    here = [.. here.Where(f => repeats.All(d => here.Contains(f + d)))];
                if (here.Count == 0)
                    continue;
                if (firsts == null)
                    firsts = here;
                else
                    firsts.IntersectWith(here);
            }
            if (firsts == null || firsts.Count == 0)
                return null;
            foreach (int f in firsts)
                for (int p = 0; p < touched.Count / 2; p++)
                    result.Add(f + 2 * p);
        }
        result.Sort();
        return result.Distinct().Count() == result.Count ? result : null;
    }

    // ---- the check ------------------------------------------------------------------------------

    /// <summary>Null when <paramref name="candidate"/> parses, adds no error, and plays every note
    /// of <paramref name="before"/> in the bar it played in — a joined pair's first bar at half its
    /// offset, the second after the first's half, each at half its length — and nothing else.</summary>
    private static string? Verify(SyntaxTree original, string candidate, string? scoreName, MidiFile before, List<int> played)
    {
        var after = SyntaxTree.Parse(candidate);
        if (after.HasErrors)
            return "• it would not parse.";
        var problems = new List<string>();
        var oldErrors = DiagnosticsOf(original).Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Code + d.Message).ToList();
        foreach (var d in DiagnosticsOf(after).Where(d => d.Severity == DiagnosticSeverity.Error))
            if (!oldErrors.Remove(d.Code + d.Message))
                problems.Add($"• a new error: {d.Code} {d.Message}");

        var score = ChooseScore(after, scoreName);
        var now = new MidiExporter { Form = score?.Form, Score = score }.Export(after);
        var startsBefore = BarStarts(before);
        var startsAfter = BarStarts(now);
        var pairFirst = played.ToHashSet();
        var expected = new List<(int Bar, double At, int Pitch, double Length, string Part)>();
        var atMiddle = new List<int>();   // the notes struck where a pair's second bar began
        foreach (var n in before.Tracks.SelectMany(t => t.Notes))
        {
            int bar = BarAt(startsBefore, n.StartTick);
            int newBar = bar - played.Count(p => p < bar);
            double offset = n.StartTick - startsBefore[bar], length = n.DurationTicks;
            if (pairFirst.Contains(bar))
                (offset, length) = (offset / 2, length / 2);
            else if (pairFirst.Contains(bar - 1))
            {
                if (offset == 0)
                    atMiddle.Add(expected.Count);
                int firstLength = startsBefore[bar] - startsBefore[bar - 1];
                (offset, length) = ((firstLength + offset) / 2, length / 2);
            }
            expected.Add((newBar, offset, n.Pitch, length, n.Part ?? ""));
        }
        var actual = now.Tracks.SelectMany(t => t.Notes)
            .Select(n => { int bar = BarAt(startsAfter, n.StartTick); return (Bar: bar, At: (double)(n.StartTick - startsAfter[bar]), n.Pitch, Length: (double)n.DurationTicks, Part: n.Part ?? ""); })
            .ToList();
        // A chord struck again at a pair's middle that the joined bar holds instead (the same
        // chord ending the first bar, JoinedSlots): the strike is not played, and the note before
        // it lasts through. Only there, only the same key of the same part, only when the note
        // before ends where the strike began.
        var gone = new HashSet<int>();
        foreach (int i in atMiddle)
        {
            var e = expected[i];
            if (actual.Any(a => a.Bar == e.Bar && Math.Abs(a.At - e.At) <= 1 && a.Pitch == e.Pitch && a.Part == e.Part))
                continue;
            int held = expected.FindIndex(p => p.Bar == e.Bar && p.Pitch == e.Pitch && p.Part == e.Part
                && p.At < e.At && Math.Abs(p.At + p.Length - e.At) <= 1);
            if (held < 0)
                continue;
            var p = expected[held];
            expected[held] = (p.Bar, p.At, p.Pitch, e.At + e.Length - p.At, p.Part);
            gone.Add(i);
        }
        if (gone.Count > 0)
            expected = [.. expected.Where((_, i) => !gone.Contains(i))];
        static List<(int, double, int, double, string)> Sorted(IEnumerable<(int Bar, double At, int Pitch, double Length, string Part)> x)
            => [.. x.OrderBy(e => e.Bar).ThenBy(e => e.At).ThenBy(e => e.Pitch).ThenBy(e => e.Part, StringComparer.Ordinal)];
        var x = Sorted(expected);
        var y = Sorted(actual);
        if (x.Count != y.Count)
            problems.Add($"• {x.Count} notes would become {y.Count}.");
        else
            for (int i = 0; i < x.Count; i++)
            {
                var (b1, a1, p1, l1, part1) = x[i];
                var (b2, a2, p2, l2, part2) = y[i];
                if (b1 != b2 || Math.Abs(a1 - a2) > 1 || p1 != p2 || Math.Abs(l1 - l2) > 1 || part1 != part2)
                {
                    problems.Add($"• {part1}: the note in bar {b1 + 1} at tick {a1} (key {p1}) would be in bar {b2 + 1} at tick {a2} (key {p2}).");
                    break;
                }
            }
        if (startsBefore.Count - played.Count != startsAfter.Count && problems.Count == 0)
            problems.Add($"• {startsBefore.Count} bars would become {startsAfter.Count}, not {startsBefore.Count - played.Count}.");
        return problems.Count == 0 ? null : string.Join("\n", problems.Take(6));
    }

    private static List<Diagnostic> DiagnosticsOf(SyntaxTree tree)
    {
        var list = tree.Diagnostics.ToList();
        try { list.AddRange(SemanticValidation.Run(tree)); }
        catch { /* a validator that throws is compared as silent on both sides */ }
        return list;
    }
}
