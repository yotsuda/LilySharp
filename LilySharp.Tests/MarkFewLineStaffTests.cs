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
using LilySharp.Core.Svg.Layout;
using LilySharp.Tests.LpFidelity;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A boxed label and a metronome mark rest on the lines THEIR staff draws, and the above-staff
/// pass that then clears them of the staff's ink runs for every mark over the staff's outermost
/// DRAWN line — not over a five-line frame's top that an <c>as lines N</c> staff keeps. A
/// one-line staff prints no clef, so the pass seeds none.
/// </summary>
/// <remarks>
/// MEASURED, LilyPond 2.26.0 (Lab sessions/p878/probe/mark, the `as lines 1` and `as lines 5`
/// twins of <c>\tempo 4 = 120 \mark \box "A" c''4 d'' e'' f'' | g'4 a' b' c'' | \mark \box "B"
/// c''4 …</c>, read off the 300-dpi page against the staff's line; the one line's values are
/// what the collision pass leaves, as the quiet 0.85 floor over the line is under the bar
/// line's ±1.0 and the meter's C):
/// <list type="bullet">
/// <item>one line: the "= 120" baseline 2.05 over the line (the d''s head top 1.5 + 0.46 +
///   its 0.033), the "B" frame's bottom 1.5 (the bar line's 1.0 + 0.46), baseline 1.86;</item>
/// <item>five lines: 2.883 and 3.202 over the middle (the quiet 2.05 + 0.8 floors).</item>
/// </list>
/// Before 2026-10-10 the one-line tempo stood 4.26 over its line: the pass seeded a treble
/// clef the staff never prints (1.8 over the frame's top), and the tempo's own 2.883 base was
/// the five-line one; and a one-line mark under the frame's 2.0 was left out of the pass.
/// The "A" over the tempo is NOT pinned: LilyPond's box starts left of the staff's edge
/// (its break alignment on a clefless line) and clears only the tempo's note head, where Lily#'s
/// box stands over the tempo's stem and clears its whole height — an X question, open.
/// LILYPOND-REF: scm/define-grobs.scm:2889-2896 RehearsalMark side-position-interface::y-aligned-side,
///   padding 0.8; :2337-2360 MetronomeMark self-alignment-interface::self-aligned-on-breakable;
///   lily/side-position-interface.cc:433-453 Side_position_interface::aligned_side, the
///   staff-padding floor; lily/axis-group-interface.cc:937-950 Axis_group_interface::skyline_spacing;
///   ly/engraver-init.ly \context RhythmicStaff — \remove "Clef_engraver".
/// </remarks>
[Trait("Category", "Unit")]
public class MarkFewLineStaffTests
{
    private static RenderedGeometry Render(int lines) => RenderedGeometry.RenderProduct($$"""
        octave absolute
        time 4/4
        tempo 4 = 120
        part p { }
        section A { p { c'4 d' e' f' | g4 a b c' | } }
        section B { p { c'4 d' e' f' | g4 a b c' | } }
        form { A B }
        score { staff p as lines {{lines}} }
        """);

    private static double TopAboveMiddle(int lines) => EngravingDefaults.StaffLinePositions(lines)[^1] / 2.0;

    [Theory]
    [InlineData(1, 2.05)]
    [InlineData(5, 2.883)]
    public void AMetronomeMark_RestsOnItsOwnStaff(int lines, double baselineAboveMiddle)
    {
        var g = Render(lines);
        Assert.InRange(g.TextBaselineAboveStaffLine(TextRole.Tempo, 0, 0) + TopAboveMiddle(lines),
            baselineAboveMiddle - 0.05, baselineAboveMiddle + 0.05);
    }

    [Theory]
    [InlineData(1, 1.86)]
    [InlineData(5, 3.202)]
    public void ABoxedLabel_RestsOnItsOwnStaff(int lines, double baselineAboveMiddle)
    {
        var g = Render(lines);
        // The second label, "B" — the first stands over the tempo mark (see the remarks).
        Assert.InRange(g.TextBaselineAboveStaffLine(TextRole.Mark, 1, 0) + TopAboveMiddle(lines),
            baselineAboveMiddle - 0.05, baselineAboveMiddle + 0.05);
    }

    [Theory]
    [InlineData(5, 2.85)]
    [InlineData(4, 2.85)]
    [InlineData(2, 1.85)]
    [InlineData(1, 0.85)]
    public void TheLabelFrame_Pads08OverTheOutermostDrawnLine(int lines, double frameBottom)
        => Assert.Equal(frameBottom, MusicMarkEngraver.LabelFrameBottomAboveStaffMiddle(lines), 9);

    [Theory]
    [InlineData(5, 2.883)]
    [InlineData(1, 0.883)]
    public void TheMetronomeMark_Pads08OverTheOutermostDrawnLine(int lines, double baseline)
        => Assert.Equal(baseline, MetronomeMarkGeometry.QuietBaselineAboveMiddle(-0.033, lines), 9);
}
