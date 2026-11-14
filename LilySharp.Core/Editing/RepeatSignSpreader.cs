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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Editing;

/// <summary>
/// Writes a section's repeat signs, volta endings and navigation marks in EVERY part that
/// writes the section — or takes them back to one — as edits for an editor's command (owner's
/// design 2026-11-14, docs/inline-repeat-design.md §8). A sign written in one part is the whole
/// score's, so the other parts need none; but a part read on its own then shows no repeat, and
/// the command makes the text say what the page draws.
/// </summary>
/// <remarks>
/// <para>
/// A sign goes where the other voice writes a plain <c>|</c> on the same bar line
/// (<see cref="VoiceBarLines"/>, the count LYS1070 compares signs by), and a <c>|:</c> that
/// opens the section is written at the head of a voice that opens with music. Where a voice
/// has no bar line of its own there — the bar is inside a phrase it plays or a <c>R1*4</c>, or
/// the voice is shorter — or writes another typed line (<c>||</c>, <c>|.</c>), nothing is
/// written and the place is named in <see cref="Result.Skipped"/>. Signs that DISAGREE between
/// voices (LYS1070) are left as they are: which one is meant is the writer's.
/// </para>
/// <para>
/// AN ENDING (<see cref="SectionSigns.Endings"/>) is written around the same bars: <c>[N.</c>
/// after the voice's bar line the ending opens on, and <c>]</c> before the one it closes on —
/// none where that line is a <c>:|</c>, which closes an ending by itself. Both lines must be
/// the voice's own and stand in one block; an ending that opens the section is not written
/// (there is no line to write it after).
/// A NAVIGATION MARK (<see cref="SectionSigns.Marks"/>) is written at the same bar line: a
/// text (<c>fine</c>, <c>to coda</c>, <c>dc</c>, <c>ds</c> …) before the line that closes its
/// bar, a sign (<c>segno</c>, <c>coda</c>) after the line its bar opens on — at the voice's
/// head for the section's first bar.
/// </para>
/// <para>
/// Everything written at one bar line is ONE edit (the line, what stands before it and what
/// stands after), so an editor applies them in no order of its own.
/// </para>
/// </remarks>
public static class RepeatSignSpreader
{
    /// <summary>Put <paramref name="NewText"/> in place of <c>[Start, End)</c>.</summary>
    public readonly record struct Edit(int Start, int End, string NewText);

    /// <summary>The edits, how many signs they write (or take away), and the places that could
    /// not be written, each as a sentence.</summary>
    public sealed record Result(IReadOnlyList<Edit> Edits, int Count, IReadOnlyList<string> Skipped);

    /// <summary>Every repeat sign, ending and navigation mark, written in every voice of its
    /// section that can take it.</summary>
    public static Result WriteInEveryPart(SyntaxTree tree) => Run(tree, spread: true);

    /// <summary>Every repeat sign, ending and navigation mark, kept in the first voice that
    /// writes it and taken out of the others (a plain <c>|</c> in a sign's place; a <c>|:</c>
    /// that opens the voice is deleted; an ending's music stays).</summary>
    public static Result KeepInOnePart(SyntaxTree tree) => Run(tree, spread: false);

    /// <summary>What is written at one bar line: the line itself (as it is, or the sign put in
    /// its place), with what is written before and after it.</summary>
    private sealed class AtLine(int start, int end, string text)
    {
        public readonly int Start = start, End = end;
        public string Text = text;
        public readonly List<string> Before = [], After = [];
    }

    private static Result Run(SyntaxTree tree, bool spread)
    {
        var root = tree.GetRoot();
        string text = root.ToFullString();
        var phrases = SectionBarCounts.PhraseBodies(root);
        var voices = SectionBarCounts.SemanticVoices(root, phrases);
        var lines = voices.Select(v => VoiceBarLines.Of(v, phrases)).ToList();
        var edits = new List<Edit>();
        var skipped = new List<string>();
        var atLines = new SortedDictionary<int, AtLine>();
        var atHeads = new SortedDictionary<int, List<string>>();
        int count = 0;

        AtLine At(VoiceBarLines.Line line)
        {
            int start = line.Node.Span.Start;
            if (!atLines.TryGetValue(start, out var at))
                atLines[start] = at = new AtLine(start, line.End, text.Substring(start, line.End - start));
            return at;
        }
        void AtHead(int head, string piece)
        {
            if (!atHeads.TryGetValue(head, out var pieces))
                atHeads[head] = pieces = [];
            pieces.Add(piece);
        }

        foreach (var section in voices.Select(v => v.SectionName).Distinct())
        {
            var members = Enumerable.Range(0, voices.Count).Where(i => voices[i].SectionName == section).ToList();
            if (members.Count < 2)
                continue;
            // The signs of the section, by the bars before them: who writes which.
            var places = new SortedDictionary<int, List<(int Voice, VoiceBarLines.Line Line)>>();
            foreach (int i in members)
                foreach (var line in lines[i])
                    if (SyntaxFacts.IsRepeatBarlineKind(line.Node.BarToken.Kind))
                    {
                        if (!places.TryGetValue(line.Bars, out var at))
                            places[line.Bars] = at = [];
                        at.Add((i, line));
                    }

            foreach (var (bars, signs) in places)
            {
                string sign = Text(signs[0].Line.Node);
                if (signs.Any(s => Text(s.Line.Node) != sign))
                    continue; // LYS1070: the writer says which
                if (!spread)
                {
                    foreach (var (voice, line) in signs.Skip(1))
                    {
                        if (voice == signs[0].Voice)
                            continue;
                        if (OpensTheVoice(line, lines[voice]))
                            edits.Add(Deleted(line.Node.Span.Start, line.End, text));
                        else
                            At(line).Text = "|";
                        count++;
                    }
                    continue;
                }
                foreach (int i in members)
                {
                    if (signs.Any(s => s.Voice == i))
                        continue;
                    var own = lines[i].Where(l => l.Bars == bars).ToList();
                    if (own.Count > 0 && own.All(l => l.Node.BarToken.Kind == SyntaxKind.Bar))
                    {
                        At(own[0]).Text = sign;
                        count++;
                    }
                    else if (own.Count == 0 && bars == 0 && sign == "|:" && HeadOf(voices[i], lines[i], text) is int head)
                    {
                        AtHead(head, "|: ");
                        count++;
                    }
                    else
                        skipped.Add(own.Count > 0
                            ? $"{voices[i].Label}, section '{section}', after bar {bars}: it writes '{Text(own[0].Node)}' there"
                            : $"{voices[i].Label}, section '{section}', after bar {bars}: it writes no bar line of its own there");
                }
            }

            // The endings and the marks, once the section's lines are what they will be (an
            // ending is closed by a ':|' written here just above).
            var music = members.Where(i => !voices[i].IsLyrics).ToList();
            var written = SectionSigns.Of(section, voices, phrases);

            foreach (var place in written.Endings.GroupBy(e => e.From))
            {
                var first = place.First();
                var others = place.Where(e => !ReferenceEquals(e.Container, first.Container)).ToList();
                if (others.Any(e => !e.Node.Numbers.SequenceEqual(first.Node.Numbers) || e.To != first.To))
                    continue; // LYS1070
                if (!spread)
                {
                    foreach (var ending in others)
                        if (Brackets(ending, text) is { } taken)
                        {
                            edits.Add(taken.Open);
                            if (taken.Close is { } closing)
                                edits.Add(closing);
                            count++;
                        }
                    continue;
                }
                foreach (int i in music)
                {
                    if (place.Any(e => ReferenceEquals(e.Container, voices[i].Container)))
                        continue;
                    string where = $"{voices[i].Label}, section '{section}', the ending '[{first.Node.VoltaText}' after bar {first.From}";
                    var from = lines[i].Where(l => l.Bars == first.From).ToList();
                    var to = lines[i].Where(l => l.Bars == first.To).ToList();
                    if (first.From == 0)
                        skipped.Add($"{where}: it opens the section, and there is no bar line to write it after");
                    else if (from.Count == 0 || to.Count == 0)
                        skipped.Add($"{where}: it writes no bar line of its own where the ending {(from.Count == 0 ? "opens" : "closes")}");
                    else if (!ReferenceEquals(from[^1].Node.Parent, to[0].Node.Parent))
                        skipped.Add($"{where}: its two bar lines there stand in different blocks");
                    else
                    {
                        At(from[^1]).After.Add(" [" + first.Node.VoltaText);
                        var close = At(to[0]);
                        if (!close.Text.StartsWith(":|", System.StringComparison.Ordinal))
                            close.Before.Add("] ");
                        count++;
                    }
                }
            }

            foreach (var place in written.Marks.GroupBy(m => m.Bars))
            {
                var byVoice = place.GroupBy(m => m.Container, (IEqualityComparer<SyntaxNode>)ReferenceEqualityComparer.Instance)
                    .Select(g => g.ToList()).ToList();
                var first = byVoice[0];
                if (byVoice.Skip(1).Any(other => !other.Select(m => m.Node.MarkType).SequenceEqual(first.Select(m => m.Node.MarkType))))
                    continue; // LYS1070
                if (!spread)
                {
                    foreach (var mark in byVoice.Skip(1).SelectMany(marks => marks))
                    {
                        edits.Add(Deleted(mark.Node.Span.Start, mark.Node.Span.Start + mark.Node.Span.Length, text));
                        count++;
                    }
                    continue;
                }
                var texts = first.Where(m => !IsSign(m.Node)).Select(m => Words(m.Node)).ToList();
                var signs = first.Where(m => IsSign(m.Node)).Select(m => Words(m.Node)).ToList();
                foreach (int i in music)
                {
                    if (byVoice.Any(marks => ReferenceEquals(marks[0].Container, voices[i].Container)))
                        continue;
                    string where = $"{voices[i].Label}, section '{section}', {string.Join(" ", first.Select(m => "'" + Words(m.Node) + "'"))} after bar {place.Key}";
                    var own = lines[i].Where(l => l.Bars == place.Key).ToList();
                    if (own.Count > 0)
                    {
                        foreach (string words in texts)
                            At(own[0]).Before.Add(words + " ");
                        foreach (string words in signs)
                            At(own[^1]).After.Add(" " + words);
                        count += first.Count;
                    }
                    else if (place.Key == 0 && texts.Count == 0 && HeadOf(voices[i], lines[i], text) is int head)
                    {
                        foreach (string words in signs)
                            AtHead(head, words + " ");
                        count += first.Count;
                    }
                    else
                        skipped.Add($"{where}: it writes no bar line of its own there");
                }
            }
        }

        foreach (var at in atLines.Values)
        {
            string now = string.Concat(at.Before) + at.Text + string.Concat(at.After);
            if (now != text.Substring(at.Start, at.End - at.Start))
                edits.Add(new Edit(at.Start, at.End, now));
        }
        foreach (var (head, pieces) in atHeads)
            edits.Add(new Edit(head, head, string.Concat(pieces)));
        edits.Sort((a, b) => a.Start.CompareTo(b.Start));
        return new Result(edits, count, skipped);
    }

    private static string Text(BarlineSyntax line)
        => line.BarToken.Text + (line.HasExplicitRepeatCount ? "*" + line.RepeatCount : "");

    /// <summary>A navigation mark as written, its words a space apart (<c>ds al coda</c>).</summary>
    internal static string Words(NavigationMarkSyntax mark)
        => string.Join(' ', Enumerable.Range(0, mark.SlotCount).Select(i => mark.GetChild(i)).OfType<SyntaxTokenNode>().Select(t => t.Text));

    /// <summary>A sign stands at the head of the bar ahead; the others are texts at the end of
    /// the bar behind (<see cref="SectionSigns"/>).</summary>
    private static bool IsSign(NavigationMarkSyntax mark) => mark.MarkType is NavigationMarkType.Segno or NavigationMarkType.Coda;

    /// <summary>A <c>|:</c> that closes nothing (it opens the voice): taken out, it goes with
    /// the space after it, since a <c>|</c> there would be an empty bar.</summary>
    private static bool OpensTheVoice(VoiceBarLines.Line line, List<VoiceBarLines.Line> all)
        => line.Bars == 0 && line.Node.BarToken.Kind == SyntaxKind.RepeatStartBar && ReferenceEquals(all[0].Node, line.Node);

    /// <summary><c>[start, end)</c> deleted, with the blanks after it.</summary>
    private static Edit Deleted(int start, int end, string text)
    {
        while (end < text.Length && text[end] is ' ' or '\t')
            end++;
        return new Edit(start, end, "");
    }

    /// <summary>
    /// An ending taken out of its voice, its music left standing: one edit over the ending,
    /// from its <c>[N.</c> to its <c>]</c> (a row's is the <c>]</c> written after its chords,
    /// before the next ending or <c>:|</c>). Null when the brackets are not where they are
    /// looked for.
    /// </summary>
    internal static Edit? Unbracketed(SectionSigns.Ending ending, string text)
        => Brackets(ending, text) is not { } b ? null
            : b.Close is not { } close ? b.Open
            : new Edit(b.Open.Start, close.End, text.Substring(b.Open.End, close.Start - b.Open.End));

    /// <summary>The two brackets of an ending as deletions: its <c>[N.</c> with the blanks
    /// after it, and its <c>]</c> likewise when one is written.</summary>
    private static (Edit Open, Edit? Close)? Brackets(SectionSigns.Ending ending, string text)
    {
        var node = ending.Node;
        int start = node.Span.Start;
        string open = "[" + node.VoltaText;
        if (string.CompareOrdinal(text, start, open, 0, open.Length) != 0)
            return null;
        int bodyStart = start + open.Length;
        while (bodyStart < text.Length && text[bodyStart] is ' ' or '\t')
            bodyStart++;

        // Where the closing ']' stands, if one is written.
        SyntaxNode? close = null;
        int end = node.Span.Start + node.Span.Length;
        if (ending.Nested)
        {
            if (node.GetChild(node.SlotCount - 1) is SyntaxTokenNode { Text: "]" } bracket)
                close = bracket;
        }
        else if (node.Parent is { } row)
        {
            foreach (var sibling in row.ChildNodes())
            {
                if (sibling.Span.Start < end)
                    continue;
                if (sibling is VoltaEndSyntax)
                    close = sibling;
                if (sibling is VoltaEndSyntax or InlineVoltaSyntax
                    || sibling is BarlineSyntax { BarToken.Kind: SyntaxKind.RepeatEndBar or SyntaxKind.RepeatBothBar })
                    break;
            }
        }
        var opening = new Edit(start, bodyStart, "");
        if (close == null)
            return (opening, null);
        return (opening, Deleted(close.Span.Start, close.Span.Start + close.Span.Length, text));
    }

    /// <summary>Where a voice that opens with music begins — after its brace — or null when it
    /// opens with a bar line (an empty bar the sign would not stand before).</summary>
    private static int? HeadOf(SectionBarCounts.SemanticVoice voice, List<VoiceBarLines.Line> lines, string text)
    {
        if (lines.Count > 0 && lines[0].Bars == 0)
            return null;
        int open = text.IndexOf('{', voice.Container.Span.Start);
        if (open < 0 || open >= voice.Container.Span.Start + voice.Container.Span.Length)
            return null;
        int at = open + 1;
        while (at < text.Length && char.IsWhiteSpace(text[at]))
            at++;
        return at;
    }
}
