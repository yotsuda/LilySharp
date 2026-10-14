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

using System.Linq;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// The percent repeat's spelling (docs/percent-repeat-design.md): LYS1047 for the old
/// <c>repeat percent</c> / <c>repeat unfold</c>, LYS1048 for a mark the parser could not group
/// with the bars it repeats, saying why. (Whether the bars are whole bars is a question of
/// time, answered by <see cref="MeasureValidator"/> — LYS1049.)
/// </summary>
internal sealed class PercentRepeatValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        foreach (var node in tree.GetRoot().DescendantNodes())
        {
            if (node is RepeatExpressionSyntax { RepeatType.Text: "percent" or "unfold" } old)
                ReportOldSpelling(old);
            else if (node is PercentMarkSyntax mark && node.Parent is not PercentRepeatSyntax)
                _diagnostics.Error(mark.Span, DiagnosticCodes.PercentMarkUngrouped, WhyUngrouped(mark));
        }
    }

    private void ReportOldSpelling(RepeatExpressionSyntax old)
    {
        var span = new TextSpan(old.RepeatKeyword.Span.Start, old.Count.Span.End - old.RepeatKeyword.Span.Start);
        _diagnostics.Error(span, DiagnosticCodes.RepeatPercentRemoved,
            old.RepeatType.Text == "percent"
                ? "'repeat percent' is gone: write the bars once, then one mark a repetition after them — "
                  + "'c4 d e f | % | % | % |' (one bar four times), 'a2 b | c d | %2 |' (two bars twice). "
                  + "Read here as its body once; the quick fix rewrites it."
                : "'repeat unfold' is gone: write the music out as many times as it is played. "
                  + "Read here as its body once; the quick fix writes it out.");
    }

    private static string WhyUngrouped(PercentMarkSyntax mark)
    {
        string text = mark.Mark.Text;
        int bars = mark.Bars;
        if (bars == 0)
            return $"'{text}' is no number of bars: write '%' for one bar, or '%N' with N from 1 to {PercentMarkSyntax.MaxBars}.";

        var siblings = mark.Parent!.ChildNodes().Where(n => n is not SyntaxTokenNode).ToList();
        int at = siblings.FindIndex(s => s.Position == mark.Position);
        int before = at - 1;
        while (before >= 0 && siblings[before] is BreakSyntax)
            before--;
        int after = at + 1;
        while (after < siblings.Count && siblings[after] is BreakSyntax)
            after++;
        bool barBefore = before < 0 || siblings[before] is BarlineSyntax;
        bool barAfter = after == siblings.Count || siblings[after] is BarlineSyntax;
        if (!barBefore || !barAfter)
            return $"A percent mark is the only thing in its bar: write it between two bar lines, '| {text} |'.";

        // Back over the bars before it, as the parser counts them (Parser.GroupPercentRepeats).
        int counted = 0;
        for (int k = before; k >= 0;)
        {
            int j = k - 1;
            bool holdsMark = false, music = false;
            while (j >= 0 && siblings[j] is not BarlineSyntax)
            {
                if (siblings[j] is PercentMarkSyntax or PercentRepeatSyntax)
                    holdsMark = true;
                else if (!Parser.Parser.IsBarHeadDirective(siblings[j].Green))
                    music = true;
                j--;
            }
            if (holdsMark)
                return counted == 0
                    ? $"'{text}' repeats {Bars(bars)} written before it, and the bar before it is a repetition: "
                      + "marks in a row repeat the same bars, so they are all of one length — write the bars out, or a mark of the length before."
                    : $"'{text}' repeats {Bars(bars)} written before it, and only {Bars(counted)} written before it come after a repetition: "
                      + "the bars a mark repeats are written bars.";
            if (!music)
                return $"'{text}' repeats {Bars(bars)} written before it, and the bar before it is empty.";
            counted++;
            if (counted == bars)
                return $"'{text}' could not be read with the bars before it.";
            k = j;
        }
        return $"'{text}' repeats {Bars(bars)} written before it, and only {Bars(counted)} are written here.";
    }

    private static string Bars(int n) => n == 1 ? "1 bar" : $"{n} bars";
}
