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
/// A slide on a tab (evaluation 5, guitar r-04): the line runs between the two fret digits on
/// their string, tilted ±0.75 toward the target's pitch, with the TabStaff's 0.3 padding —
/// LilyPond 2.26.0's TabStaff glissando (engraver-init.ly:1286-1292, tablature.scm:131-140).
/// Until then the tab drew it at the pitch's STAFF position, so a slide on string 5 stood up
/// near strings 1-2.
/// </summary>
[Trait("Category", "Unit")]
public class GlissandoOnTabTests
{
    private const string Book =
        "octave absolute\npart gtr { instrument guitar }\n" +
        "section A { gtr { a8\\5@slide c'\\5 c'8\\3@slide e'\\3 e'8\\1@slide g'\\1 r4 | e'8\\3@slide c'\\3 r4 r2 | } }\n" +
        "form { ~A }\nscore { staff gtr  tab gtr }\n";

    private static ScoreLayout Render()
    {
        var tree = SyntaxTree.Parse(Book);
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        return new LayoutEngine().Layout(score);
    }

    // The draw reflects a glissando's Y-up against half the 5-line staff's height below the
    // staff's top line (SharedRenderer.StaffHeight 4), on a tab too: string s is there at
    // 2 - (s - 1) * the string space.
    private static double StringYUp(int s) => 2.0 - (s - 1) * EngravingDefaults.TabStringSpace(6);

    private static double Mid(GlissandoLayout g) => (g.StartYUp + g.EndYUp) / 2;

    [Fact]
    public void EachSlide_StandsOnItsString()
    {
        var tab = Render().GlissandoLayouts.Where(g => g.StaffIndex == 1).OrderBy(g => g.MeasureIndex).ThenBy(g => g.StartX).ToList();
        Assert.Equal(4, tab.Count);
        // The padding shortens the line alike at both ends, so its middle is the bounds' middle:
        // the string's line (the ±extra-dy/2 cancel).
        Assert.Equal(StringYUp(5), Mid(tab[0]), 6);
        Assert.Equal(StringYUp(3), Mid(tab[1]), 6);
        Assert.Equal(StringYUp(1), Mid(tab[2]), 6);
        Assert.Equal(StringYUp(3), Mid(tab[3]), 6);
    }

    [Fact]
    public void ASlide_TiltsTowardTheTargetsPitch_ByLessThanTheExtraDy()
    {
        var tab = Render().GlissandoLayouts.Where(g => g.StaffIndex == 1).OrderBy(g => g.MeasureIndex).ThenBy(g => g.StartX).ToList();
        double Rise(GlissandoLayout g) => g.EndYUp - g.StartYUp;
        // Up a string: the line rises; down (bar 2, e' -> c'): it falls. The padding takes its
        // share of the 0.75, so what is drawn is less.
        foreach (var up in tab.Take(3))
            Assert.InRange(Rise(up), 0.05, 0.75);
        Assert.InRange(Rise(tab[3]), -0.75, -0.05);
    }

    [Fact]
    public void TheStaffsSlide_StillReadsThePitch()
    {
        // The notation staff is untouched: a (staff position -1 under the guitar's treble_8)
        // to c' (+1) rises across the two positions.
        var staff = Render().GlissandoLayouts.Where(g => g.StaffIndex == 0).OrderBy(g => g.MeasureIndex).ThenBy(g => g.StartX).First();
        Assert.True(staff.StartYUp > -1.0 && staff.EndYUp < 1.0 && staff.EndYUp > staff.StartYUp);
    }
}
