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

using LilySharp.Core.Rendering;
using LilySharp.Core.Svg;
using LilySharp.Tests.LpFidelity;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Every outside-staff grob stands off the lines ITS staff draws: the trill spanner and the
/// ottava bracket side off their staff's own extent (the minimum under their supports and the
/// staff-padding floor), a fermata and a text on a multi-measure rest side off the rest's
/// staff, and the collision pass that then stacks them starts from the outermost DRAWN line —
/// not from the five-line frame's top that an <c>as lines N</c> staff keeps.
/// </summary>
/// <remarks>
/// MEASURED, LilyPond 2.26.0 (Lab sessions/p877/probe/ext, the `as lines 1` twins under
/// <c>-dbackend=svg</c> with a grob dump; the grob's refpoint against the one line):
/// <list type="bullet">
/// <item><c>c'4\startTrillSpan d' e' f'\stopTrillSpan</c>, stems down: the line at 2.949
///   (the wave over the heads; on five lines the staff's 2.05 + 1.0 + 0.5 = 3.55 binds);
///   <c>g4\startTrillSpan a b c'\stopTrillSpan</c>, stems up: 4.0 on either staff;</item>
/// <item><c>\ottava #1 c''4 d'' e'' \ottava #0</c>: the bracket at 2.595, its "8va" baseline
///   0.76 below that (on five lines the floor 2.05 + 2.0 = 4.05 binds, baseline 3.29);</item>
/// <item><c>R1-\fermata</c>: the fermata's origin at 0.526 = 0.05 + its 0.075 + padding 0.4
///   (2.526 on five lines);</item>
/// <item><c>R1*2-\markup \italic "tacet"</c> under <c>\compressMMRests</c>: 2.943 = the
///   count's top (0.05 + 0.4 + 2.0) + 0.46 + the text's 0.033 (4.943 on five lines).</item>
/// </list>
/// Before 2026-10-10 the four read <c>DynamicEngraver.StaffExtent</c>, the five-line 2.05, and
/// the above-staff stacker's support started at the frame's top line, so on one line the trill
/// stood at 3.46 (2.0 + 0.46 + its 1.0 reach), the fermata at 2.07, the 8va at 3.29.
/// LILYPOND-REF: lily/side-position-interface.cc:323-330 set_minimum_height (staff_extents[dir]),
///   :433-453 the staff-padding floor; lily/axis-group-interface.cc:937-950 skyline_spacing
///   (the inside-staff skylines the pass starts from); lily/staff-symbol.cc:346-369
///   Staff_symbol::height.
/// </remarks>
[Trait("Category", "Unit")]
public class OutsideStaffFewLineStaffTests
{
    private static RenderedGeometry Render(int lines, string music) => RenderedGeometry.RenderProduct($$"""
        octave absolute
        time 4/4
        part p { }
        section A { p { {{music}} } }
        form { A }
        score { staff p as lines {{lines}} }
        """);

    private static double TopAboveMiddle(int lines) => EngravingDefaults.StaffLinePositions(lines)[^1] / 2.0;

    [Theory]
    [InlineData(1, 0, 2.949)]
    [InlineData(1, 1, 4.0)]
    [InlineData(5, 0, 3.55)]
    [InlineData(5, 1, 4.0)]
    public void ATrillSpanner_SidesOffItsOwnStaff(int lines, int index, double lineAboveMiddle)
    {
        var g = Render(lines, "c'4@startTrillSpan d' e' f'@stopTrillSpan | r1 | g4@startTrillSpan a b c'@stopTrillSpan |");
        double line = g.GlyphAboveStaffLine(EmmentalerGlyphs.OrnTrill, index, 0)
            + EngravingDefaults.TrillSpannerTextOffsetDown + TopAboveMiddle(lines);
        Assert.InRange(line, lineAboveMiddle - 0.02, lineAboveMiddle + 0.02);
    }

    [Theory]
    [InlineData(1, 1.835)]
    [InlineData(5, 3.29)]
    public void AnOttavaBracket_SidesOffItsOwnStaff(int lines, double baselineAboveMiddle)
    {
        var g = Render(lines, "c''4@ottava d'' e'' f''@!ottava |");
        Assert.InRange(g.TextBaselineAboveStaffLine(TextRole.Ottava, 0, 0) + TopAboveMiddle(lines),
            baselineAboveMiddle - 0.02, baselineAboveMiddle + 0.02);
    }

    [Theory]
    [InlineData(1, 0.526)]
    [InlineData(5, 2.526)]
    public void AFermataOnABarRest_SidesOffItsOwnStaff(int lines, double originAboveMiddle)
    {
        var g = Render(lines, "R@fermata |");
        Assert.InRange(g.GlyphAboveStaffLine(EmmentalerGlyphs.FermataAbove, 0, 0) + TopAboveMiddle(lines),
            originAboveMiddle - 0.02, originAboveMiddle + 0.02);
    }

    [Theory]
    [InlineData(1, 2.943)]
    [InlineData(5, 4.943)]
    public void ATextOnAMultiMeasureRest_ClearsTheCountOverItsOwnStaff(int lines, double baselineAboveMiddle)
    {
        var g = Render(lines, "R*2@text(\"tacet\") |");
        Assert.InRange(g.TextBaselineAboveStaffLine(TextRole.Text, 0, 0) + TopAboveMiddle(lines),
            baselineAboveMiddle - 0.02, baselineAboveMiddle + 0.02);
    }
}
