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
/// The ways out of LYS1070 — two voices writing different repeat signs on one bar line — as
/// edits an editor can offer (owner's request 2026-11-14: "the fix is plain, but the same bar
/// stands lines apart in another part and its bar line is hard to find"). Read off the same
/// signs the report was made from (<see cref="RepeatSignAgreementValidator.Signs"/>), so a fix
/// can only name the two signs the error named.
/// </summary>
public static class RepeatSignFixes
{
    /// <summary>One way out: its title, and the text to put in place of <c>[Start, End)</c>.</summary>
    public readonly record struct Fix(string Title, int Start, int End, string NewText);

    /// <summary>The fixes for the sign reported at <paramref name="reported"/> (the span of
    /// the diagnostic): write the other voice's sign here, write this one there, or write a
    /// plain <c>|</c> here and follow the other. Empty when no reported sign stands there.</summary>
    public static IReadOnlyList<Fix> For(SyntaxTree tree, TextSpan reported)
    {
        var signs = RepeatSignAgreementValidator.Signs(tree.GetRoot());
        int at = signs.FindIndex(s => s.Node.BarToken.Span.Start == reported.Start);
        if (at < 0)
            return ForEndingsAndMarks(tree.GetRoot(), reported);
        var here = signs[at];
        var first = signs.First(s => s.Section == here.Section && s.BarsBefore == here.BarsBefore);
        if (first.Text == here.Text || ReferenceEquals(first.Node, here.Node))
            return [];
        return
        [
            new Fix($"Write '{first.Text}' here, as {first.Voice} does", Start(here), End(here), first.Text),
            new Fix($"Write '{here.Text}' in {first.Voice} too", Start(first), End(first), here.Text),
            new Fix($"Write a plain '|' here - it follows the '{first.Text}' of {first.Voice}", Start(here), End(here), "|"),
        ];

        static int Start(RepeatSignAgreementValidator.Sign sign) => sign.Node.Span.Start;
        static int End(RepeatSignAgreementValidator.Sign sign) => sign.Node.Span.Start + sign.Node.Span.Length;
    }

    /// <summary>
    /// The same for an ending or a navigation mark the error stands on (the validator's
    /// <c>ReportEndingsAndMarks</c>, read off the same <see cref="SectionSigns"/>): an ending
    /// whose passes differ takes the other voice's, gives its own, or loses its brackets — its
    /// music stays, and it follows the other's; one that runs a different length only the last
    /// (which bar line it should close on is the writer's). A mark takes the other voice's, or
    /// goes. Voices that write several marks on the line are left to the writer.
    /// </summary>
    private static IReadOnlyList<Fix> ForEndingsAndMarks(SyntaxNode root, TextSpan reported)
    {
        string text = root.ToFullString();
        var phrases = SectionBarCounts.PhraseBodies(root);
        var voices = SectionBarCounts.SemanticVoices(root, phrases);
        var label = new Dictionary<SyntaxNode, string>(ReferenceEqualityComparer.Instance);
        foreach (var voice in voices)
            label.TryAdd(voice.Container, voice.Label);
        string Who(SyntaxNode container) => label.GetValueOrDefault(container, "another part");
        var fixes = new List<Fix>();

        foreach (string section in voices.Select(v => v.SectionName).Distinct(System.StringComparer.Ordinal))
        {
            var written = SectionSigns.Of(section, voices, phrases);
            foreach (var place in written.Endings.GroupBy(e => e.From))
            {
                var first = place.First();
                foreach (var here in place.Skip(1))
                {
                    if (here.Node.Span.Start != reported.Start || ReferenceEquals(here.Container, first.Container))
                        continue;
                    string mine = "[" + here.Node.VoltaText, theirs = "[" + first.Node.VoltaText;
                    if (mine != theirs && Opens(here, mine) && Opens(first, theirs))
                    {
                        fixes.Add(new Fix($"Write '{theirs}' here, as {Who(first.Container)} does", here.Node.Span.Start, here.Node.Span.Start + mine.Length, theirs));
                        fixes.Add(new Fix($"Write '{mine}' in {Who(first.Container)} too", first.Node.Span.Start, first.Node.Span.Start + theirs.Length, mine));
                    }
                    if (RepeatSignSpreader.Unbracketed(here, text) is { } taken)
                        fixes.Add(new Fix($"Leave the ending out here - it follows the '{theirs}' of {Who(first.Container)}", taken.Start, taken.End, taken.NewText));
                    return fixes;
                }
            }

            foreach (var place in written.Marks.GroupBy(m => m.Bars))
            {
                var byVoice = place.GroupBy(m => m.Container, (IEqualityComparer<SyntaxNode>)ReferenceEqualityComparer.Instance)
                    .Select(g => g.ToList()).ToList();
                var first = byVoice[0];
                foreach (var here in byVoice.Skip(1))
                {
                    if (here[0].Node.Span.Start != reported.Start || here.Count != 1)
                        continue;
                    int start = here[0].Node.Span.Start, end = start + here[0].Node.Span.Length;
                    string mine = RepeatSignSpreader.Words(here[0].Node);
                    if (first.Count == 1)
                    {
                        string theirs = RepeatSignSpreader.Words(first[0].Node);
                        int theirStart = first[0].Node.Span.Start;
                        fixes.Add(new Fix($"Write '{theirs}' here, as {Who(first[0].Container)} does", start, end, theirs));
                        fixes.Add(new Fix($"Write '{mine}' in {Who(first[0].Container)} too", theirStart, theirStart + first[0].Node.Span.Length, mine));
                    }
                    while (end < text.Length && text[end] is ' ' or '\t')
                        end++;
                    fixes.Add(new Fix($"Leave '{mine}' out here - it follows the mark of {Who(first[0].Container)}", start, end, ""));
                    return fixes;
                }
            }
        }
        return fixes;

        bool Opens(SectionSigns.Ending ending, string open)
            => string.CompareOrdinal(text, ending.Node.Span.Start, open, 0, open.Length) == 0;
    }
}