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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// <c>paper { breaksOnly }</c> (owner's decision 2026-10-05, LilySharp-Omr proposal C3): lines
/// and pages break only at the written <c>break</c> / <c>pageBreak</c>, so a score copied from
/// a page keeps the original's systems and pages.
/// </summary>
[Trait("Category", "Unit")]
public class BreaksOnlyTests
{
    // 24 bars of eighths — far more than one line holds — with a `break` after bar 4 and a
    // `pageBreak` after bar 10.
    private static string Book(string paper)
    {
        string bar = "c'8 d' e' f' g' a' b' c'' | ";
        string music = string.Concat(Enumerable.Repeat(bar, 4)) + "break "
            + string.Concat(Enumerable.Repeat(bar, 6)) + "pageBreak "
            + string.Concat(Enumerable.Repeat(bar, 14));
        return $$"""
            octave absolute
            time 4/4
            {{paper}}
            part m { }
            section A { m { {{music}} } }
            form main { A }
            score main { staff m }
            """;
    }

    /// <summary>Per page, per system: the first and last bar index.</summary>
    private static (int First, int Last)[][] Pages(string source)
    {
        var tree = SyntaxTree.Parse(source);
        var score = SvgGenerator.CollectScore(tree, Core.Svg.Collector.RenderSpecParser.FindFirst(tree));
        var layout = new LayoutEngine(score.Paper).Layout(score);
        return layout.Pages.Select(p => p.Systems
            .Select(s => (s.Measures[0].MeasureIndex, s.Measures[^1].MeasureIndex)).ToArray()).ToArray();
    }

    [Fact]
    public void LinesAndPages_BreakOnlyWhereTheMusicSays()
    {
        var pages = Pages(Book("paper { breaksOnly }"));
        Assert.Equal(2, pages.Length);
        Assert.Equal(new[] { (0, 3), (4, 9) }, pages[0]);
        Assert.Equal(new[] { (10, 23) }, pages[1]);
    }

    [Fact]
    public void WithoutIt_TheBreakerStillBreaksTheLongLines()
    {
        var systems = Pages(Book("")).SelectMany(p => p).ToArray();
        Assert.True(systems.Length > 3, $"{systems.Length} systems");
        Assert.Contains(systems, s => s.First == 4);    // the written breaks still hold
        Assert.Contains(systems, s => s.First == 10);
    }

    [Fact]
    public void IsABareFlag()
    {
        var diagnostics = SemanticValidation.Run(SyntaxTree.Parse(Book("paper { breaksOnly 1 }")));
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCodes.PaperEntryMissingValue);
        Assert.DoesNotContain(SemanticValidation.Run(SyntaxTree.Parse(Book("paper { breaksOnly }"))),
            d => d.Severity == DiagnosticSeverity.Error);
    }
}
