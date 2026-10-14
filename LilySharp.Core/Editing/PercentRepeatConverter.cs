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
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Editing;

/// <summary>
/// Rewrites the old <c>repeat percent N { … }</c> and <c>repeat unfold N { … }</c>
/// (LYS1047) into what they are written as now (docs/percent-repeat-design.md): the bars
/// once and a mark a repetition after them — <c>repeat percent 4 { c4 d e f }</c> becomes
/// <c>c4 d e f | % | % | %</c> — and an unfold, or a percent repeat of anything but whole bars
/// (a beat, a pickup), its music written out. Nothing else in the text changes.
/// </summary>
/// <remarks>
/// A repeat is rewritten with marks only when the marks read back as the same bars: it opens
/// a bar (nothing timed before it in its bar), its bars are counted on the bar lines its body
/// writes, and the rewritten book raises no LYS1048 / LYS1049 the old one did not — the check
/// that the body is whole bars of the meter, which the text alone cannot answer. Otherwise
/// the music is written out, as many times as it was played. One repeat at a time, the
/// innermost first, so a repeat inside another is rewritten before the one around it.
/// </remarks>
public static class PercentRepeatConverter
{
    /// <summary>The rewritten text, how many repeats became marks and how many were written
    /// out.</summary>
    public sealed record Result(string NewText, int AsMarks, int WrittenOut);

    /// <summary>Rewrites every old repeat in <paramref name="source"/>.</summary>
    public static Result Convert(string source)
    {
        string text = source;
        int asMarks = 0, writtenOut = 0;
        int? flagged = null;
        while (FirstOldRepeat(SyntaxTree.Parse(text)) is { } repeat)
        {
            flagged ??= Flagged(text);
            var (next, marks, after) = RewriteOne(text, repeat, flagged.Value);
            text = next;
            flagged = after;
            if (marks) asMarks++; else writtenOut++;
        }
        return new Result(text, asMarks, writtenOut);
    }

    /// <summary>The whole text with the old repeat at <paramref name="position"/> rewritten
    /// (the quick fix), or null when no old repeat is written there.</summary>
    public static string? ConvertAt(string source, int position)
    {
        var tree = SyntaxTree.Parse(source);
        var repeat = OldRepeats(tree)
            .Where(r => r.Span.Start <= position && position <= r.Span.End)
            .OrderBy(r => r.Span.Length)
            .FirstOrDefault();
        return repeat == null ? null : RewriteOne(source, repeat, Flagged(source)).Text;
    }

    private static IEnumerable<RepeatExpressionSyntax> OldRepeats(SyntaxTree tree) =>
        tree.GetRoot().DescendantNodes().OfType<RepeatExpressionSyntax>()
            .Where(r => r.RepeatType.Text is "percent" or "unfold");

    // The first old repeat in the text that holds none.
    private static RepeatExpressionSyntax? FirstOldRepeat(SyntaxTree tree) =>
        OldRepeats(tree).FirstOrDefault(r => !r.Body.DescendantNodes().OfType<RepeatExpressionSyntax>()
            .Any(inner => inner.RepeatType.Text is "percent" or "unfold"));

    // The marks this text's book reports as repeating nothing or not whole bars.
    private static int Flagged(string text) =>
        SemanticValidation.Run(SyntaxTree.Parse(text)).Count(d =>
            d.Code is DiagnosticCodes.PercentMarkUngrouped or DiagnosticCodes.PercentBodyNotWholeBars);

    private static (string Text, bool AsMarks, int Flagged) RewriteOne(string text, RepeatExpressionSyntax repeat, int flagged)
    {
        if (repeat.RepeatType.Text == "percent" && AsMarks(text, repeat) is { } marks)
        {
            string candidate = Splice(text, repeat, marks);
            int after = Flagged(candidate);
            if (after <= flagged)
                return (candidate, true, after);
        }
        // Written out: the copies side by side, or with a bar line between them when the body
        // writes none at its end and that reads as whole bars — the one the book reports less
        // in (`repeat unfold 2 { c1 }` is `c1 | c1`, a beat `c8 d` twice is `c8 d c8 d`).
        string spaced = Splice(text, repeat, WrittenOut(text, repeat, " "));
        if (!repeat.Body.Items.Any() || repeat.Body.Items.Last() is BarlineSyntax)
            return (spaced, false, flagged);
        string barred = Splice(text, repeat, WrittenOut(text, repeat, " | "));
        return (Reported(barred) < Reported(spaced) ? barred : spaced, false, flagged);
    }

    // What a book reports at all, warnings included — the count the two spellings of a
    // written-out repeat are compared by.
    private static int Reported(string text) =>
        SemanticValidation.Run(SyntaxTree.Parse(text)).Count(d => d.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error);

    private static string Splice(string text, SyntaxNode node, string replacement) =>
        text[..node.Span.Start] + replacement + text[node.Span.End..];

    private static int CountOf(RepeatExpressionSyntax repeat) =>
        int.TryParse(repeat.Count.Text, out int n) && n > 0 ? n : 2;

    /// <summary>The bars once and a mark a repetition — null when the repeat does not open a
    /// bar, or writes nothing timed.</summary>
    private static string? AsMarks(string text, RepeatExpressionSyntax repeat)
    {
        int count = CountOf(repeat);
        if (count < 2 || repeat.Parent == null)
            return null;
        var siblings = repeat.Parent.ChildNodes().Where(n => n is not SyntaxTokenNode).ToList();
        int at = siblings.FindIndex(s => s.Position == repeat.Position);
        for (int k = at - 1; k >= 0 && siblings[k] is not BarlineSyntax; k--)
            if (!Parser.Parser.IsBarHeadDirective(siblings[k].Green))
                return null; // music before it in its bar: the bars would not be the body's

        var items = repeat.Body.Items.Where(i => i is not SyntaxTokenNode).ToList();
        int last = items.FindLastIndex(i => i is not BarlineSyntax && !Parser.Parser.IsBarHeadDirective(i.Green));
        if (last < 0)
            return null;
        int bars = 1 + items.Take(last).Count(i => i is BarlineSyntax);
        if (bars > PercentMarkSyntax.MaxBars)
            return null;

        var sb = new System.Text.StringBuilder();
        sb.Append(text, items[0].Span.Start, items[^1].Span.End - items[0].Span.Start);
        if (!items.Skip(last + 1).Any(i => i is BarlineSyntax))
            sb.Append(" |");
        string mark = bars == 1 ? "%" : "%" + bars;
        for (int m = 1; m < count; m++)
            sb.Append(m == 1 ? " " : " | ").Append(mark);

        // The last repetition's bar closes at the bar line after the repeat, or the music's end.
        int next = at + 1;
        while (next < siblings.Count && siblings[next] is BreakSyntax)
            next++;
        if (next < siblings.Count && siblings[next] is not BarlineSyntax)
            sb.Append(" |");
        return sb.ToString();
    }

    /// <summary>The body written out as many times as it is played.</summary>
    private static string WrittenOut(string text, RepeatExpressionSyntax repeat, string between)
    {
        int from = repeat.Body.OpenBrace.Span.End, to = repeat.Body.CloseBrace.Span.Start;
        string body = to > from ? text[from..to].Trim() : "";
        return string.Join(between, Enumerable.Repeat(body, CountOf(repeat)));
    }
}
