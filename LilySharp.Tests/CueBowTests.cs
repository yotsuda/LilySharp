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
using LilySharp.Core.Rendering;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using LilySharp.Tests.LpFidelity;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A slur or tie on cue notes reads the CUE heads: LilyPond's CueVoice sets its heads at
/// font-size −4 (the thirteen design × 0.62996), and every head extent a bow reads is the head
/// grob's own (lily/slur-scoring.cc:556-562; lily/tie-formatting-problem.cc:119). Until session
/// 656 Lily#'s bows read the twenty's full-size head. LilyPond 2.26.0, Lab sessions/p656
/// (cue1, cuetie).
/// </summary>
[Trait("Category", "Unit")]
public sealed class CueBowTests
{
    private static RecordingDrawingContext Render(string music)
    {
        var tree = SyntaxTree.Parse($$"""
            octave absolute
            part m {
              section A { {{music}} }
            }
            form main { A }
            score main { staff m }
            """);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        var layout = new LayoutEngine(score.Paper).Layout(score);
        using var doc = new RecordingDocumentContext();
        SharedRenderer.RenderTo(score, layout, doc);
        return doc.Page;
    }

    private static double[] HeadXs(RecordingDrawingContext page) => page.Glyphs
        .Where(g => g.Glyph == EmmentalerGlyphs.NoteheadBlack)
        .Select(g => g.X).OrderBy(x => x).ToArray();

    [Fact]
    public void ACueSlur_HangsFromTheCueHeads()
    {
        var page = Render("c4 cue { e4( f) } g4 |");
        var slur = Assert.Single(page.Beziers);
        double middle = Assert.Single(TwinBeamSweep.StavesOf(page)).Middle;
        // Start: the cue head's centre 0.4077 + the tilt shift; 0.354 under e's centre (−2.5).
        Assert.Equal(0.4607, slur.P0.X - HeadXs(page)[1], 3);
        Assert.Equal(-2.854178, middle - slur.P0.Y, 0.001);
    }

    [Fact]
    public void ACueTie_LeavesTheCueHead()
    {
        var page = Render("c4 cue { e4~ e4 } g4 |");
        var tie = Assert.Single(page.Beziers);
        var heads = HeadXs(page);
        Assert.Equal(0.6077, tie.P0.X - heads[1], 3);
        Assert.Equal(0.2077, tie.P1.X - heads[2], 3);
    }
}
