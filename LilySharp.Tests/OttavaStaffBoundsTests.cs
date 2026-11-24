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
/// An ottava bracket's two ends are the STAFF's columns, not the columns of the voice that
/// wrote the mark (session 1304): it begins at the first voice's column of its first moment
/// and ends after the last voice's column of its last, each where the note collision put it.
/// Until then the right end read the first voice's column, unshifted — 1.377 short where the
/// lower voice stands a second under the upper — and the left end the mark's own.
/// MEASURED (LilyPond 2.26.0, Lab sessions/p1304/ot o0-o10, dump.ily, raggedRight): the
/// bracket's X extent. Its right is 0.05 outside the hook's centre line, which is what
/// <c>EndX</c> is — the control o0 has the same 0.05.
/// </summary>
[Trait("Category", "Unit")]
public class OttavaStaffBoundsTests
{
    private static OttavaBracketLayout BracketOf(string bars)
    {
        var tree = SyntaxTree.Parse("octave absolute\ntime 4/4\npaper { raggedRight }\npart m { clef treble }\n"
            + "section A { m {\n" + bars + "\n} }\nform { A }\nscore { staff m }\n");
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        return Assert.Single(new LayoutEngine().Layout(SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree)))
            .OttavaBracketLayouts);
    }

    [Theory]
    // The control: two voices a third apart, nothing shifted.
    [InlineData("o0", "voice { d''2 e''2 } { b'2@8vb c''2@!8vb } |", 16.321, 23.424)]
    // The lower voice a second under the upper: the end is after ITS shifted head…
    [InlineData("o1", "voice { c''2 d''2 } { b'2@8vb c''2@!8vb } |", 16.321, 25.490)]
    // …whichever voice wrote the mark.
    [InlineData("o2", "voice { c''2@8va d''2@!8va } { b'2 c''2 } |", 16.321, 25.490)]
    // The last moment is the other voice's alone.
    [InlineData("o3", "voice { c''4 d'' e'' f'' } { b'2@8vb c''2@!8vb } |", 16.321, 28.450)]
    // Stopping inside the bar, on a shifted column.
    [InlineData("o4", "voice { c''4 b' e'' f'' } { b'4@8vb a'@!8vb g'2 } |", 16.321, 24.034)]
    // The last voice's quarter head, not the first voice's half.
    [InlineData("o5", "voice { c''2 d''2 } { b'4 a'@8vb g'@!8vb f' } |", 20.950, 26.707)]
    // Beginning where the mark's own column is the shifted one: at the first voice's.
    [InlineData("o6", "voice { c''4 b' e'' f'' } { b'4 a'@8vb g'@!8vb f' } |", 20.225, 26.759)]
    [InlineData("o7", "voice { c''2@8va d''4@!8va e'' } { b'2 c''2 } |", 16.321, 25.417)]
    // Crossed voices: the FIRST voice's heads are the shifted ones, and the bracket begins there.
    [InlineData("o8", "voice { b'2 c''2 } { c''2@8vb d''2@!8vb } |", 17.423, 23.975)]
    [InlineData("o9", "voice { b'2@8va c''2@!8va } { c''2 d''2 } |", 17.423, 23.975)]
    // The first voice rests at the last moment: the end is after the last voice's head.
    [InlineData("o10", "voice { c''2 r2 } { b'2@8vb c''2@!8vb } |", 16.321, 24.112)]
    public void TheBracket_BeginsAndEndsWhereLilyPondsDoes(string book, string bars, double left, double right)
    {
        var bracket = BracketOf(bars);
        Assert.True(System.Math.Abs(left - bracket.StartX) <= 0.005, $"{book} left: {bracket.StartX:F3}, LilyPond {left:F3}");
        Assert.True(System.Math.Abs(right - 0.05 - bracket.EndX) <= 0.005, $"{book} right: {bracket.EndX:F3}, LilyPond {right - 0.05:F3}");
    }

    private static ScoreLayout LayoutOf(string bars)
    {
        var tree = SyntaxTree.Parse("octave absolute\ntime 4/4\npaper { raggedRight }\npart m { clef treble }\n"
            + "section A { m {\n" + bars + "\n} }\nform { A }\nscore { staff m }\n");
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        return new LayoutEngine().Layout(SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree)));
    }

    /// <summary>
    /// A bar of multi-measure rest is no note column: a span stopping over it hooks after the
    /// last note before it, as one stopping on that note does, and draws no piece over the rest
    /// even when the rest opens a line. MEASURED (2.26.0, Lab sessions/p1378/m a1, a2, a5): the
    /// hook 0.6 right of the f'' over `R1` and `R1*2` alike; nothing on the rest's line.
    /// </summary>
    [Theory]
    [InlineData("c''4@8va d'' e'' f'' | R1@!8va | g''1 |")]
    [InlineData("c''4@8va d'' e'' f'' | R1*2@!8va | g''1 |")]
    public void AStopOverAMultiMeasureRest_HooksAfterTheLastNote(string bars)
    {
        var onTheNote = Assert.Single(LayoutOf("c''4@8va d'' e'' f''@!8va | R1 | g''1 |").OttavaBracketLayouts);
        var overTheRest = Assert.Single(LayoutOf(bars).OttavaBracketLayouts);
        Assert.Equal(onTheNote.EndX, overTheRest.EndX, 6);
        Assert.Equal(onTheNote.YUp, overTheRest.YUp, 6);
    }

    [Fact]
    public void AStopOverARestOpeningALine_DrawsNoPieceThere()
    {
        var layout = LayoutOf("c''1@8va | d''1 | break R1@!8va | g''1 |");
        Assert.Equal(2, layout.Systems.Length);
        var piece = Assert.Single(layout.OttavaBracketLayouts);
        Assert.Equal(0, piece.StartMeasureIndex);
        Assert.True(piece.EdgeHeight != 0, "the one piece hooks");
    }

    /// <summary>
    /// A broken end is the line's own edge, unshortened: the first piece runs to its line's last
    /// bar's right edge and the continuation starts at the next line's first bar. MEASURED (2.26.0,
    /// Lab sessions/p1378/m a3): the first piece ends on the line's last bar line's right edge
    /// (Lily# 0.6 short until session 1378); the continuation starts on the clef's right edge,
    /// 0.156 left of Lily#'s first bar X (until session 1378 it started 0.8 further left still).
    /// </summary>
    [Fact]
    public void ABrokenBracket_RunsToTheLinesEdges()
    {
        var layout = LayoutOf("c''1@8va | d''1 | break e''1 | f''1@!8va | g''1 |");
        var pieces = layout.OttavaBracketLayouts.OrderBy(p => p.StartMeasureIndex).ToList();
        Assert.Equal(2, pieces.Count);
        var first = layout.Systems[0].Measures[^1];
        Assert.Equal(first.X + first.Width, pieces[0].EndX, 6);
        Assert.Equal(layout.Systems[1].Measures[0].X, pieces[1].StartX, 6);
    }
}
