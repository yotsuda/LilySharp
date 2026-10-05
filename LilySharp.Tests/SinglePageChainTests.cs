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


using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// One page through the page chain (session 829): a score that fits is cut to its music, one the
/// chain had to compress keeps the paper, and the chain's breaker — not a fit test of its own —
/// decides whether it fits at all.
/// </summary>
public sealed class SinglePageChainTests
{
    private static ScoreLayout LayoutOfFile(string relative)
    {
        string path = System.IO.Path.Combine(CollectResumeTests.FindRepoRoot(), relative);
        var tree = SyntaxTree.Parse(System.IO.File.ReadAllText(path));
        var spec = RenderSpecParser.FindFirst(tree);
        var score = SvgGenerator.CollectScore(tree, spec, LayoutOptions.Default);
        return new LayoutEngine(score.Paper).Layout(score);
    }

    /// <summary>
    /// Thirteen systems that a stack of their own spacing fitted 0.55 inside A4 — the single-page
    /// loop before session 829 drew them on one cropped page — go onto two pages, 12 and 1, as
    /// LilyPond 2.26.0 lays out the twin (Lab sessions/p829/sd40): the chain prices the
    /// last-bottom spring the loop never had.
    /// </summary>
    [Fact]
    public void AScoreThatFitsOnlyWithoutTheLastBottomSpacing_TakesTwoPages_AsLilyPonds()
    {
        var layout = LayoutOfFile("audit/lpreg/perf-sd40.lys");
        Assert.Equal(2, layout.Pages.Length);
        Assert.Equal([12, 1], layout.Pages.Select(p => p.Systems.Length));
    }

    /// <summary>
    /// A snippet is one page whatever the paper block says of pages — LilyPond's one-page
    /// breaking asks no breaker, so a systems-per-page cap has nothing to cap.
    /// </summary>
    [Fact]
    public void ASnippet_IsOnePage_UnderASystemsPerPageCap()
    {
        var tree = SyntaxTree.Parse("paper { systemsPerPage 1 }\ntime 4/4\npart m { clef treble\n  section A { "
            + string.Concat(Enumerable.Repeat("c'4 d' e' f' | ", 40)) + "} }\nform main { A }\nscore main { staff m }");
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree), LayoutOptions.Snippet);
        var layout = new LayoutEngine(score.Paper).Layout(score);
        Assert.True(layout.AllSystems.Length >= 2);
        Assert.Single(layout.Pages);
    }

    /// <summary>
    /// A lone page the chain had to compress keeps the paper (what the single-page loop handed
    /// the chain before the fold); one at rest is cut to its music.
    /// </summary>
    [Theory]
    [InlineData("LilySharp.Tests/Fixtures/test/feature-tour.lys", false)]
    [InlineData("LilySharp.Tests/Fixtures/test/lyrics.lys", true)]
    public void ALonePage_IsCutToItsMusic_OnlyAtRest(string book, bool cut)
    {
        var page = Assert.Single(LayoutOfFile(book).Pages);
        Assert.Equal(cut, page.Force >= 0);
        if (cut)
            Assert.True(page.Height < LayoutOptions.Default.PageHeight, $"cut to {page.Height}");
        else
            Assert.Equal(LayoutOptions.Default.PageHeight, page.Height, 9);
    }
}