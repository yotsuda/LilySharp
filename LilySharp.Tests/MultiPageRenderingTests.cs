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

using System.Text.RegularExpressions;
using LilySharp.Core.Rendering;
using LilySharp.Core.Rendering.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Page-level overlays (ties, slurs, dynamics, …) must be drawn only on the
/// page that owns their measures. System Y coordinates are PAGE-LOCAL (each
/// page restarts at MarginTop), so before the page-membership filter every
/// page drew every other page's overlays at the wrong place — a two-page score
/// showed page 2's tie overprinted on page 1's music and vice versa.
/// </summary>
[Trait("Category", "Unit")]
public class MultiPageRenderingTests
{
    private const string Source = """
        time 4/4
        key c major
        part melody { clef treble }
        section Main { melody {
          c4 d e f | g4 a b c |
          break
          c4 d e f | g2~ g2 |
        } }
        structure { Main }
        score "x" { staff melody }
        """;

    private static string Render(LayoutOptions options, out ScoreLayout layout)
    {
        var score = new MeasureCollector().Collect(SyntaxTree.Parse(Source), "melody");
        var multi = MultiStaffScore.FromScore(score);
        layout = new LayoutEngine(options).Layout(multi);
        var doc = new SvgDocumentContext(new SvgDocumentOptions { EmbedFont = false });
        SharedRenderer.RenderTo(multi, layout, doc);
        doc.Dispose(); // finalizes pages; ToSvg requires a disposed document
        return doc.ToSvg();
    }

    [Fact]
    public void Overlays_AreDrawnOnlyOnTheirOwnPage()
    {
        // Reference: the same music on ONE page. The tie (the only Bézier
        // curve in this fixture) must appear exactly as many times in the
        // paged rendering as it does here — one extra occurrence means a page
        // is drawing another page's overlay.
        string single = Render(new LayoutOptions(), out var singleLayout);
        Assert.Single(singleLayout.Pages);
        int expectedCurves = Regex.Matches(single, "<path").Count;
        Assert.True(expectedCurves > 0, "fixture must contain the tie curve");

        string paged = Render(new LayoutOptions
        {
            PageHeight = 26,
            UseOptimalPageBreaking = true,
        }, out var pagedLayout);
        Assert.True(pagedLayout.Pages.Length >= 2,
            $"expected a multi-page layout, got {pagedLayout.Pages.Length} page(s)");

        int pagedCurves = Regex.Matches(paged, "<path").Count;
        Assert.Equal(expectedCurves, pagedCurves);
    }
}
