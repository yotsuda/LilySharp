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
    private static RecordingDrawingContext Render(string music, string header = "")
    {
        var tree = SyntaxTree.Parse($$"""
            octave absolute
            {{header}}
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

    /// <summary>
    /// An up slur leaving an up-stemmed cue note attaches 0.3 past the CUE stem, which stands
    /// on the cue head's own attachment (0.7504 from its left, not the twenty's 1.2392).
    /// LilyPond 2.26.0, Lab sessions/p691/cue (cue-slurs.lys, bar 1): the slur starts 0.7695
    /// right of the e's head and 1.4384 above the middle line. Until session 691 Lily# started
    /// it 0.49 further right.
    /// </summary>
    [Fact]
    public void ACueSlurLeavingACueStem_StandsOnTheCueStem()
    {
        var page = Render("""
            cue { e4( a4 d'4 c4) } |
            cue { f'4( e'4 c''4 b'4) } |
            cue { g8( a b c' d'4 e'4) } |
            cue { c''8( b' a' g' f'4) r4 } |
            cue { <c e g>4( <d f a>4) <e' g' b'>4( <d' f' a'>4) } |
            cue { e4( fis4 bes4 c'4) } |
            cue { c'16( e' g' c'' e''4) a'4( g'4) } |
            cue { b4( g'4 d'2) } |
            """, "time 4/4");
        var slur = page.Beziers.OrderBy(b => b.P0.X).First();
        double middle = Assert.Single(TwinBeamSweep.StavesOf(page)).Middle;
        // Within the twin net's 0.01: the cue columns stand ~0.1 apart from LilyPond's.
        Assert.Equal(0.7695, slur.P0.X - HeadXs(page)[0], 0.01);
        Assert.Equal(1.4384, middle - slur.P0.Y, 0.01);
    }

    /// <summary>
    /// A cue note's accidental is an extra object of the CUE font's size, placed against the
    /// cue head. LilyPond 2.26.0, Lab sessions/p691/cue (cue-acc.lys — half notes, so the
    /// cue columns space as LilyPond's do): each slur's first control point stands
    /// <c>h</c> above its start. With the twenty's accidentals Lily# drew these four
    /// 0.22–0.87 taller.
    /// </summary>
    [Fact]
    public void ACueSlurOverACueAccidental_ReadsTheCueAccidental()
    {
        var page = Render("""
            cue { e2( fis4 g4) } |
            cue { b2( fis'4 g'4) } |
            cue { g'2( bes'4 a'4) } |
            cue { c''2( fis''4 e''4) } |
            cue { a'2( cis''4 d''4) } |
            cue { e2( fis2) } |
            cue { d''2( bes'4 c''4) } |
            cue { f'1( | gis'1) } |
            """, "time 4/4");
        var slurs = page.Beziers.OrderBy(b => b.P0.X).ToArray();
        Assert.Equal(8, slurs.Length);
        double[] lpHeight = [1.5274, 1.4986, 1.7317, 1.4348];
        for (int i = 0; i < lpHeight.Length; i++)
            Assert.Equal(lpHeight[i], slurs[i + 1].P0.Y - slurs[i + 1].Centreline1.Y, 0.01);
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
