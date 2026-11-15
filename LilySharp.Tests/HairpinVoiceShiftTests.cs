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
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A hairpin and a dynamic of a voice the note collision shifted stand on the SHIFTED column
/// (session 1262). The lower voice a second under the upper has its heads moved a head's width
/// right; until then its hairpin's ends and its dynamic text read the unshifted column, so the
/// wedge opened 1.377 left of its own note, the f stood under the other voice's note — and
/// there, in the way of that voice's neighbour stem, 0.74 lower, taking the wedge down with it.
/// MEASURED (LilyPond 2.26.0, Lab sessions/p1262/shift: a0–a5, dump.ily, raggedRight): every
/// number below is LilyPond's. LilyPond's wedge ends stand 0.05 outside Lily#'s on EVERY book,
/// the unshifted controls included (a0: 17.071 against 17.12), so the ends are held to 0.06.
/// </summary>
[Trait("Category", "Unit")]
public class HairpinVoiceShiftTests
{
    private static ScoreLayout LayoutOf(string bars)
    {
        var tree = SyntaxTree.Parse("octave absolute\ntime 4/4\npaper { raggedRight }\npart m { clef treble }\n"
            + "section A { m {\n" + bars + "\n} }\nform { A }\nscore { staff m }\n");
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        return new LayoutEngine().Layout(SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree)));
    }

    private const double EndTolerance = 0.06;

    /// <summary>The shifted voice's crescendo to its f: both ends, the f and their height.</summary>
    [Fact]
    public void AShiftedVoicesHairpinAndItsLevel_StandOnTheShiftedColumns()
    {
        var layout = LayoutOf("voice { c''2 d''2 } { b'2@cresc c''2@f } |");
        var hp = Assert.Single(layout.HairpinLayouts);
        var f = Assert.Single(layout.DynamicLayouts);
        Assert.Equal(18.448, hp.StartX, EndTolerance);
        Assert.Equal(22.569, hp.EndX, EndTolerance);
        Assert.Equal(24.151, f.X, 0.005);          // centred on the shifted head (23.462..24.840)
        Assert.Equal(-4.6124, f.YUp, 0.01);        // clear of the other voice's stem now
    }

    /// <summary>The control: a third apart nothing is shifted, and nothing moved.</summary>
    [Fact]
    public void WithNoCollision_NothingMoves()
    {
        var layout = LayoutOf("voice { d''2 e''2 } { b'2@cresc c''2@f } |");
        var hp = Assert.Single(layout.HairpinLayouts);
        var f = Assert.Single(layout.DynamicLayouts);
        Assert.Equal(17.071, hp.StartX, EndTolerance);
        Assert.Equal(20.503, hp.EndX, EndTolerance);
        Assert.Equal(22.085, f.X, 0.005);
        Assert.Equal(-4.6183, f.YUp, 0.01);
    }

    /// <summary>A written stop on a shifted note: the wedge runs to that note's right edge.</summary>
    [Fact]
    public void AStopOnAShiftedNote_EndsAtItsRightEdge()
    {
        var hp = Assert.Single(LayoutOf("voice { d''2 d''2 } { b'2@cresc c''2@!cresc } |").HairpinLayouts);
        Assert.Equal(17.071, hp.StartX, EndTolerance);
        Assert.Equal(24.201, hp.EndX, EndTolerance);
    }

    /// <summary>The UPPER voice's hairpin over a shifted lower voice stays on its own column.</summary>
    [Fact]
    public void TheUnshiftedVoicesHairpin_StaysOnItsOwnColumn()
    {
        var layout = LayoutOf("voice { c''2@cresc d''2@f } { b'2 c''2 } |");
        var hp = Assert.Single(layout.HairpinLayouts);
        Assert.Equal(17.071, hp.StartX, EndTolerance);
        Assert.Equal(21.192, hp.EndX, EndTolerance);
        Assert.Equal(22.774, Assert.Single(layout.DynamicLayouts).X, 0.005);
    }

    /// <summary>Whole notes: the decrescendo opens at the shifted whole head, and the p of the
    /// next bar is centred on its own shifted head.</summary>
    [Fact]
    public void AShiftedWholeNote_OpensTheWedgeAtItsOwnHead()
    {
        var layout = LayoutOf("voice { c''1 } { b'1@decresc } | voice { c''1 } { b'1@p } |");
        var hp = Assert.Single(layout.HairpinLayouts);
        Assert.Equal(19.033, hp.StartX, EndTolerance);
        Assert.Equal(23.402, hp.EndX, EndTolerance);
        Assert.Equal(29.195, Assert.Single(layout.DynamicLayouts).X, 0.005);
    }
}
