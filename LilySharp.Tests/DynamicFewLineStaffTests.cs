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
using LilySharp.Core.Svg.Layout;
using LilySharp.Tests.LpFidelity;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A dynamic sides off ITS staff's extent — the StaffSymbol grob's own Y extent, which
/// <c>aligned_side</c> puts under the supports as a minimum and floors the staff-padding on —
/// not a five-line staff's whatever the staff draws. On a one-line staff that extent is ±0.05,
/// so the stem and head alone hold the dynamic off, and a <c>f</c> under a stem-down note
/// hangs by its bottom-left curl beside the thin stem where a <c>p</c>'s full-height left
/// stroke stands on it.
/// </summary>
/// <remarks>
/// MEASURED, LilyPond 2.26.0 (Lab sessions/p876/probe/dyn, `as lines 1` and `as lines 5`
/// twins under <c>-dbackend=svg</c>, grob dump dump-dy1.log / dump-dy5.log; the DynamicText
/// origin against the staff middle, f extent (−0.692 . 1.896), p (−0.584 . 1.168), text at
/// −0.6 in the spanner, padding 0.6, staff-padding 0.1):
/// <list type="bullet">
/// <item>one line: f under b (stem to −3.333) −3.509, under d' −2.676, under f' −2.546 (the
///   staff's 0.05 binds: 1.296 + 0.05 + 0.6), under g (stem up; the head) −4.019; p under b
///   −5.056, under d' −4.223;</item>
/// <item>five lines: f −4.546 under b, d' and f' alike (the staff's 2.05 binds), p −5.056 /
///   −4.223 (its stroke reaches the stem) — unchanged.</item>
/// </list>
/// Before 2026-10-10 <c>DynamicEngraver</c>'s staff extent was the five-line 2.05 on every
/// staff, so the one-line f stood at −4.546 wherever the stem ended.
/// LILYPOND-REF: lily/side-position-interface.cc:323-330 set_minimum_height (staff_extents[dir]),
///   :433-453 the staff-padding floor; lily/staff-symbol.cc:346-369 Staff_symbol::height.
/// </remarks>
[Trait("Category", "Unit")]
public class DynamicFewLineStaffTests
{
    private static RenderedGeometry Render(int lines) => RenderedGeometry.RenderProduct($$"""
        octave absolute
        time 4/4
        part p { }
        section A { p { b4@f r d'@f r | b4@p r d'@p r | f'4@f r g@f r | } }
        form { A }
        score { staff p as lines {{lines}} }
        """);

    [Theory]
    [InlineData(1, 0, -3.509)]
    [InlineData(1, 1, -2.676)]
    [InlineData(1, 2, -5.056)]
    [InlineData(1, 3, -4.223)]
    [InlineData(1, 4, -2.546)]
    [InlineData(1, 5, -4.019)]
    [InlineData(5, 0, -4.546)]
    [InlineData(5, 1, -4.546)]
    [InlineData(5, 2, -5.056)]
    [InlineData(5, 3, -4.223)]
    [InlineData(5, 4, -4.546)]
    [InlineData(5, 5, -4.546)]
    public void ADynamic_SidesOffItsOwnStaffsExtent(int lines, int index, double aboveMiddle)
    {
        var g = Render(lines);
        // Read against the staff's TOP drawn line, then moved to the middle.
        double topAboveMiddle = EngravingDefaults.StaffLinePositions(lines)[^1] / 2.0;
        Assert.Equal(aboveMiddle, g.DynamicBaselineAboveStaffLine(index, 0) + topAboveMiddle, 2);
    }

    [Theory]
    [InlineData(5, 1.0, 2.05)]
    [InlineData(5, -1.0, 2.05)]
    [InlineData(4, 1.0, 2.05)]
    [InlineData(4, -1.0, 1.05)]
    [InlineData(2, 1.0, 1.05)]
    [InlineData(1, 1.0, 0.05)]
    [InlineData(1, -1.0, 0.05)]
    public void TheStaffExtent_IsTheOutermostDrawnLinesInkOnThatSide(int lines, double dir, double extent)
    {
        Assert.Equal(extent, DynamicEngraver.StaffExtentToward(dir, lines), 9);
    }
}
