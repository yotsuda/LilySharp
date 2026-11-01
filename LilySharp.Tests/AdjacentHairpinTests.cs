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
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Two hairpins back to back on one note — <c>d''8@!cresc@decresc</c> — meet with a gap and never
/// cross (owner's report 2026-11-01, <c>Downloads/Chop-28-4a-a4-2.lys</c> bar 12: the crescendo ran
/// to the note's right edge, through the decrescendo's open end). Each stops a third of the bound
/// padding short of the note column's centre.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/hairpin.cc:241-260 Hairpin::print — neighbor_found, not circled:
///   x_points[d] = e.center () - d * padding / 3. MEASURED (2.26.0, Lab sessions/p1036/chop/adj.svg):
///   <c>c'8\&lt; d'8 e'8\!\&gt; f'8 g'8\!</c> — the crescendo ends at 22.448, the decrescendo starts at
///   23.115: 0.667 apart, the column's centre between them.
/// </remarks>
[Trait("Category", "Unit")]
public class AdjacentHairpinTests
{
    private static string Book(string measures) =>
        "octave absolute\ntime 2/2\npart m { clef treble }\n" + $"section S {{ m {{ {measures} }} }}\n" + "form { S }\nscore { staff m }\n";

    private static (ScoreLayout Layout, MultiStaffScore Score) LayoutOf(string measures)
    {
        var tree = SyntaxTree.Parse(Book(measures));
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        return (new LayoutEngine().Layout(score), score);
    }

    [Fact]
    public void BackToBackOnOneNote_MeetWithAGap_EitherSideOfTheColumnsCentre()
    {
        var (layout, score) = LayoutOf("c'8@cresc d'8 e'8@!cresc@decresc f'8 g'8@!decresc a'8 b'8 c''8 |");
        var hairpins = HairpinEngraver.DetectHairpins(score.MusicMarks, score.Dynamics);
        var cresc = Assert.Single(hairpins, h => h.Direction == HairpinDirection.Crescendo);
        var decresc = Assert.Single(hairpins, h => h.Direction == HairpinDirection.Decrescendo);
        Assert.True(cresc.AdjacentAtEnd && decresc.AdjacentAtStart);
        Assert.False(cresc.AdjacentAtStart || decresc.AdjacentAtEnd || cresc.TipAdjacent || decresc.TipAdjacent);
        var drawn = layout.HairpinLayouts.OrderBy(h => h.StartX).ToList();
        Assert.Equal(2, drawn.Count);
        // The gap is two thirds of the bound padding (1.0), the e' column's centre in its middle:
        // the crescendo no longer runs to the column's right edge, past the decrescendo's start.
        Assert.Equal(2.0 / 3.0, drawn[1].StartX - drawn[0].EndX, 9);
        var (_, score2) = LayoutOf("c'8@cresc d'8 e'8@!cresc f'8 g'8 a'8 b'8 c''8 |");
        var alone = Assert.Single(new LayoutEngine().Layout(score2).HairpinLayouts);
        Assert.True(alone.EndX > drawn[0].EndX, $"alone {alone.EndX} adjacent {drawn[0].EndX}");
    }

    [Fact]
    public void OnDifferentNotes_TheyKeepTheirOwnBounds()
    {
        var (layout, _) = LayoutOf("c'8@cresc d'8 e'8@!cresc f'8@decresc g'8 a'8@!decresc b'8 c''8 |");
        var drawn = layout.HairpinLayouts.OrderBy(h => h.StartX).ToList();
        Assert.Equal(2, drawn.Count);
        // The crescendo runs to the e' column's right edge; the decrescendo opens at the f' column: more than a note apart.
        Assert.True(drawn[1].StartX - drawn[0].EndX > 1.0, $"{drawn[0].EndX} .. {drawn[1].StartX}");
    }

    [Fact]
    public void ANientePair_KeepsItsSharedCircle()
    {
        var (_, score) = LayoutOf("c'4@p@decresc d' e'@niente@cresc f' | g'1@f |");
        var hairpins = HairpinEngraver.DetectHairpins(score.MusicMarks, score.Dynamics);
        Assert.All(hairpins, h => Assert.True(h.TipAdjacent));
        Assert.True(hairpins.Single(h => h.Direction == HairpinDirection.Decrescendo).AdjacentAtEnd);
        Assert.True(hairpins.Single(h => h.Direction == HairpinDirection.Crescendo).AdjacentAtStart);
    }
}
