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

using System.Collections.Immutable;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
public class HairpinTests
{
    // --- HairpinLayout constants ---

    [Fact]
    public void HairpinLayout_HasExpectedLilyPondDefaults()
    {
        // LILYPOND-REF: scm/define-grobs.scm:1655 (height . 0.6666)
        // Opening = Height / 2 = 0.6666 / 2 = 0.3333
        // YUp is Y-up from the system top; device 5.2 below the top = -5.2 up.
        var layout = new HairpinLayout(0, 0, 10, -5.2, 0, 0.3333, HairpinDirection.Crescendo, 0);
        Assert.Equal(0.0, layout.StartOpening);   // Crescendo: point at start
        Assert.Equal(0.3333, layout.EndOpening);   // Crescendo: open at end
        Assert.Equal(-5.2, layout.YUp);
    }

    // --- DetectHairpins ---

    [Fact]
    public void DetectHairpins_NoCrescMarks_ReturnsEmpty()
    {
        var musicMarks = ImmutableArray<MusicMarkItem>.Empty;
        var dynamics = ImmutableArray<DynamicItem>.Empty;

        var result = HairpinEngraver.DetectHairpins(musicMarks, dynamics);

        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void DetectHairpins_CrescFollowedByDynamic_CreatesHairpin()
    {
        var musicMarks = ImmutableArray.Create(
            new MusicMarkItem(MusicMarkType.Cresc, 0, 0));
        var dynamics = ImmutableArray.Create(
            new DynamicItem(DynamicLevel.F, 2, 0, 0));

        var result = HairpinEngraver.DetectHairpins(musicMarks, dynamics);

        Assert.Single(result);
        Assert.Equal(HairpinDirection.Crescendo, result[0].Direction);
        Assert.Equal(0, result[0].StartMeasureIndex);
        Assert.Equal(2, result[0].EndMeasureIndex);
        Assert.Equal(0, result[0].EndItemIndex);
    }

    [Fact]
    public void DetectHairpins_DecrescFollowedByDynamic_CreatesDecrescendo()
    {
        var musicMarks = ImmutableArray.Create(
            new MusicMarkItem(MusicMarkType.Decresc, 1, 0));
        var dynamics = ImmutableArray.Create(
            new DynamicItem(DynamicLevel.P, 3, 0, 0));

        var result = HairpinEngraver.DetectHairpins(musicMarks, dynamics);

        Assert.Single(result);
        Assert.Equal(HairpinDirection.Decrescendo, result[0].Direction);
        Assert.Equal(1, result[0].StartMeasureIndex);
        Assert.Equal(3, result[0].EndMeasureIndex);
    }

    [Fact]
    public void DetectHairpins_DimTreatedAsDecrescendo()
    {
        var musicMarks = ImmutableArray.Create(
            new MusicMarkItem(MusicMarkType.Dim, 0, 0));
        var dynamics = ImmutableArray.Create(
            new DynamicItem(DynamicLevel.PP, 1, 0, 0));

        var result = HairpinEngraver.DetectHairpins(musicMarks, dynamics);

        Assert.Single(result);
        Assert.Equal(HairpinDirection.Decrescendo, result[0].Direction);
    }

    [Fact]
    public void DetectHairpins_NoDynamic_ExtendsToNextMeasure()
    {
        var musicMarks = ImmutableArray.Create(
            new MusicMarkItem(MusicMarkType.Cresc, 2, 0));
        var dynamics = ImmutableArray<DynamicItem>.Empty;

        var result = HairpinEngraver.DetectHairpins(musicMarks, dynamics);

        Assert.Single(result);
        Assert.Equal(2, result[0].StartMeasureIndex);
        Assert.Equal(3, result[0].EndMeasureIndex);
        Assert.Equal(0, result[0].EndItemIndex);
    }

    [Fact]
    public void DetectHairpins_TwoCrescMarks_CreatesTwoHairpins()
    {
        var musicMarks = ImmutableArray.Create(
            new MusicMarkItem(MusicMarkType.Cresc, 0, 0),
            new MusicMarkItem(MusicMarkType.Decresc, 2, 0));
        var dynamics = ImmutableArray.Create(
            new DynamicItem(DynamicLevel.FF, 4, 0, 0));

        var result = HairpinEngraver.DetectHairpins(musicMarks, dynamics);

        Assert.Equal(2, result.Length);
        Assert.Equal(HairpinDirection.Crescendo, result[0].Direction);
        Assert.Equal(HairpinDirection.Decrescendo, result[1].Direction);
    }

    // --- HairpinEngraver.Calculate ---

    private static ImmutableArray<MeasureLayout> CreateMeasureLayouts(int count, double measureWidth = 20.0)
    {
        var builder = ImmutableArray.CreateBuilder<MeasureLayout>(count);
        for (int i = 0; i < count; i++)
        {
            var items = ImmutableArray.Create(
                new ItemLayout(0, 1.0, 2.0),
                new ItemLayout(1, 5.0, 2.0),
                new ItemLayout(2, 9.0, 2.0));
            builder.Add(new MeasureLayout(i, i * measureWidth, measureWidth, items));
        }
        return builder.ToImmutable();
    }

    private static ImmutableArray<SystemLayout> CreateSingleSystem(int measureCount)
    {
        var measures = CreateMeasureLayouts(measureCount);
        return ImmutableArray.Create(new SystemLayout(0, 10.0, 200.0, 5.0, measures));
    }

    [Fact]
    public void Calculate_Crescendo_PositionsWedge()
    {
        var measures = CreateMeasureLayouts(4);
        var systems = CreateSingleSystem(4);
        var hairpins = ImmutableArray.Create(new HairpinItem(
            HairpinDirection.Crescendo, 0, 0, 2, 0, 42));

        var result = HairpinEngraver.Calculate(hairpins, systems, measures);

        Assert.Single(result);
        var h = result[0];
        Assert.Equal(HairpinDirection.Crescendo, h.Direction);
        Assert.Equal(0, h.StartMeasureIndex);
        Assert.Equal(42, h.SourcePosition);
        // StartX: the note column's LEFT edge, unpadded — measure[0].X + item[0].X
        // = 0 + 1.0 = 1.0 (LILYPOND-REF: lily/hairpin.cc:184-290 — x_points = e[LEFT]).
        Assert.Equal(1.0, h.StartX, 2);
        // EndX: the terminator sits on measure 2's START, so to-barline binds the
        // hairpin to the bar line before it — whose right edge is the measure's X —
        // minus the full bound-padding: 40 − 1.0 = 39.0.
        // LILYPOND-REF: lily/bar-engraver.cc:548-558 — set_bound (RIGHT, bar_)
        Assert.Equal(39.0, h.EndX, 2);
    }

    [Fact]
    public void Calculate_TextBounds_PadOffTheDynamicInk()
    {
        var measures = CreateMeasureLayouts(4);
        var systems = CreateSingleSystem(4);
        // A cresc whose start note carries a "p" and whose MID-MEASURE terminator
        // carries an "f": both bounds are the TEXT, padded by the full bound-padding
        // off its ink — not the note column.
        // LILYPOND-REF: lily/hairpin.cc:214-218 — Text_interface bound,
        //   x_points[d] = e[-d] − d·padding. Measured: probe-hairpin-bounds line 2
        //   (start = p-right + 1.0) and line 3 (end = f-left − 1.0).
        var hairpins = ImmutableArray.Create(new HairpinItem(
            HairpinDirection.Crescendo, 0, 0, 2, 1, 0));
        var dynamics = ImmutableArray.Create(
            new DynamicLayout(0, 0, 2.0, 0, "p", 0),
            new DynamicLayout(2, 1, 45.0, 0, "f", 0));

        var result = HairpinEngraver.Calculate(hairpins, systems, measures,
            dynamicLayouts: dynamics);

        var h = Assert.Single(result);
        double pw = DynamicOutline.AdvanceWidth("p")!.Value;
        double fw = DynamicOutline.AdvanceWidth("f")!.Value;
        Assert.Equal(2.0 + pw / 2.0 + 1.0, h.StartX, 6);
        Assert.Equal(45.0 - fw / 2.0 - 1.0, h.EndX, 6);
    }

    [Fact]
    public void Calculate_Decrescendo_PositionsWedge()
    {
        var measures = CreateMeasureLayouts(3);
        var systems = CreateSingleSystem(3);
        var hairpins = ImmutableArray.Create(new HairpinItem(
            HairpinDirection.Decrescendo, 0, 1, 1, 2, 0));

        var result = HairpinEngraver.Calculate(hairpins, systems, measures);

        Assert.Single(result);
        Assert.Equal(HairpinDirection.Decrescendo, result[0].Direction);
    }

    [Fact]
    public void Calculate_ShortSpan_DrawsAtItsBounds_NotStretchedToMinimumLength()
    {
        // minimum-length is a SPACING rod (it rides springs-and-rods and widens the
        // gap between the bound columns), never a draw-time stretch: the stencil is
        // drawn at whatever the bounds give. Measured: dynamics-line.ly's to-barline
        // wedge is 1.511 long (21.985 − 20.474) and LilyPond draws it as-is. The old
        // law stretched EndX to StartX + 2.0 here, which is a second spelling of a
        // rod the spacing side does not have yet.
        // LILYPOND-REF: lily/hairpin.cc:292-299 Hairpin::print — width = x_points[RIGHT]
        //   − x_points[LEFT]; only negative width clamps (to 0, with a warning)
        // LILYPOND-REF: scm/define-grobs.scm:1786-1788 Hairpin minimum-length 2.0 rides
        //   springs-and-rods (ly:spanner::set-spacing-rods)
        var items = ImmutableArray.Create(
            new ItemLayout(0, 1.0, 0.5),
            new ItemLayout(1, 2.0, 0.5));
        var measures = ImmutableArray.Create(
            new MeasureLayout(0, 0, 10, items));
        var systems = ImmutableArray.Create(new SystemLayout(0, 10.0, 200.0, 5.0, measures));
        var hairpins = ImmutableArray.Create(new HairpinItem(
            HairpinDirection.Crescendo, 0, 0, 0, 1, 0));

        var result = HairpinEngraver.Calculate(hairpins, systems, measures);

        var h = Assert.Single(result);
        // Left bound: the note column's left edge, unpadded (measure.X + item.X = 1.0).
        Assert.Equal(1.0, h.StartX, 6);
        // Right bound: mid-measure musical end (item.X − padding/2 = 1.5) — shorter
        // than minimum-length, and drawn that way.
        Assert.Equal(1.5, h.EndX, 6);
    }

    [Fact]
    public void Calculate_OutOfRangeMeasure_Skipped()
    {
        var measures = CreateMeasureLayouts(2);
        var systems = CreateSingleSystem(2);
        var hairpins = ImmutableArray.Create(new HairpinItem(
            HairpinDirection.Crescendo, 0, 0, 10, 0, 0)); // endMeasure=10 is out of range

        var result = HairpinEngraver.Calculate(hairpins, systems, measures);

        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void Calculate_EmptyHairpins_ReturnsEmpty()
    {
        var measures = CreateMeasureLayouts(2);
        var systems = CreateSingleSystem(2);

        var result = HairpinEngraver.Calculate(ImmutableArray<HairpinItem>.Empty, systems, measures);

        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void Calculate_Opening_MatchesLilyPondHeight()
    {
        // LILYPOND-REF: scm/define-grobs.scm:1655 (height . 0.6666)
        // height IS the half-opening, so the open end's half-mouth = height.
        var measures = CreateMeasureLayouts(3);
        var systems = CreateSingleSystem(3);
        var hairpins = ImmutableArray.Create(new HairpinItem(
            HairpinDirection.Crescendo, 0, 0, 2, 0, 0));

        var result = HairpinEngraver.Calculate(hairpins, systems, measures);

        Assert.Single(result);
        Assert.Equal(0.0, result[0].StartOpening, 4);  // Crescendo: point at start
        Assert.Equal(0.6666, result[0].EndOpening, 4);  // Full opening at end
    }

    /// <summary>
    /// The wedge's resting level is LilyPond's <c>aligned_side</c> for its
    /// DynamicLineSpanner, not a constant — and with nothing under the staff to support
    /// off, that is the staff's own ink plus the spanner's padding plus the wedge's own
    /// half height.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE ARITHMETIC IS WRITTEN OUT rather than asserted as 5.3666, because every term is
    /// a measured LilyPond one and the point of the test is which terms there are: staff
    /// ink 2.05 (the outermost line's centre plus half its thickness) + DynamicLineSpanner's
    /// <c>padding</c> 0.6 + the drawn wedge's half height (<c>height</c> 0.6666 plus half
    /// the rule's thickness), all below the staff MIDDLE, which is itself
    /// <see cref="EngravingDefaults.StaffMiddle"/> below the frame's origin at the system
    /// top. MEASURED in LilyPond 2.26.0 by dumping the spanner's own offset — the
    /// decomposition, the perturbation that found each term's owner, and the two
    /// compensating 0.05 errors it replaced are in HairpinEngraver's own remark and in
    /// audit/lp-geometry <c>hairpin.page.quiet.last-staff-to-foot</c>, which reads the same
    /// quantity through a page foot and lands at 0.
    /// </para>
    /// <para>
    /// ⚠️ WHAT BREAKS THIS: putting a constant back. Before 2026-07-31 the level was
    /// <c>BaseYUp = -5.2</c>, a LILYSHARP-OWN number 0.166600 shallower than LilyPond's,
    /// and no unit test could tell — the old assertion pinned the constant to itself.
    /// </para>
    /// </remarks>
    [Fact]
    public void Calculate_Y_IsAlignedSideOffTheStaff_NotAConstant()
    {
        var measures = CreateMeasureLayouts(3);
        var systems = CreateSingleSystem(3);
        var hairpins = ImmutableArray.Create(new HairpinItem(
            HairpinDirection.Crescendo, 0, 0, 2, 0, 0));

        // No voices: nothing hangs under the staff, so the staff's own extent is the
        // whole support — aligned_side's include_staff minimum.
        var result = HairpinEngraver.Calculate(hairpins, systems, measures);

        double staffInk = EngravingDefaults.StaffMiddle + EngravingDefaults.StaffLineThickness / 2;
        double wedgeHalf = 0.6666 + EngravingDefaults.StaffLineThickness / 2;
        double expected = -(EngravingDefaults.StaffMiddle + staffInk + 0.6 + wedgeHalf);

        Assert.Equal(expected, result[0].YUp, 9);
        Assert.Equal(-5.3666, result[0].YUp, 9);
    }

    // --- Broken hairpin (cross-system) tests ---

    private static (ImmutableArray<MeasureLayout> measures, ImmutableArray<SystemLayout> systems)
        CreateTwoSystemLayout()
    {
        // System 0: measures 0,1 (each 20 wide, starting at 0)
        // System 1: measures 2,3 (each 20 wide, starting at 0)
        var allMeasures = ImmutableArray.CreateBuilder<MeasureLayout>(4);
        for (int i = 0; i < 4; i++)
        {
            var items = ImmutableArray.Create(
                new ItemLayout(0, 1.0, 2.0),
                new ItemLayout(1, 5.0, 2.0));
            allMeasures.Add(new MeasureLayout(i, (i % 2) * 20.0, 20.0, items));
        }
        var measures = allMeasures.ToImmutable();
        var sys0Measures = ImmutableArray.Create(measures[0], measures[1]);
        var sys1Measures = ImmutableArray.Create(measures[2], measures[3]);
        var systems = ImmutableArray.Create(
            new SystemLayout(0, 10.0, 80.0, 5.0, sys0Measures),
            new SystemLayout(1, 30.0, 80.0, 5.0, sys1Measures));
        return (measures, systems);
    }

    [Fact]
    public void Calculate_BrokenCrescendo_ContinuedHasTwoThirdsOpening()
    {
        // LILYPOND-REF: lily/hairpin.cc:180-220 — continued = 2/3 height
        var (measures, systems) = CreateTwoSystemLayout();
        var hairpins = ImmutableArray.Create(new HairpinItem(
            HairpinDirection.Crescendo, 0, 0, 3, 0, 0));

        var result = HairpinEngraver.Calculate(hairpins, systems, measures);

        // Should produce 2 segments (one per system)
        Assert.Equal(2, result.Length);

        // First segment (continued): point at left, 2/3 opening at right
        double fullOpening = 0.6666;
        Assert.Equal(0.0, result[0].StartOpening, 4);
        Assert.Equal(fullOpening * 2.0 / 3.0, result[0].EndOpening, 4);

        // Second segment (continuing): 1/3 opening at left, full opening at right
        Assert.Equal(fullOpening * 1.0 / 3.0, result[1].StartOpening, 4);
        Assert.Equal(fullOpening, result[1].EndOpening, 4);
    }

    [Fact]
    public void Calculate_BrokenDecrescendo_MirrorsCrescendoFractions()
    {
        // LILYPOND-REF: lily/hairpin.cc:305-309 — decrescendo (SMALLER):
        //   starth = continuing ? 2h/3 : h ;  endh = continued ? h/3 : 0.
        // So the interior fractions are the MIRROR of the crescendo case: the
        // leftmost piece tapers full -> h/3, the rightmost piece 2h/3 -> point.
        var (measures, systems) = CreateTwoSystemLayout();
        var hairpins = ImmutableArray.Create(new HairpinItem(
            HairpinDirection.Decrescendo, 0, 0, 3, 0, 0));

        var result = HairpinEngraver.Calculate(hairpins, systems, measures);

        Assert.Equal(2, result.Length);

        double fullOpening = 0.6666;
        // First segment (leftmost): full opening at left, 1/3 at the break (right).
        Assert.Equal(fullOpening, result[0].StartOpening, 4);
        Assert.Equal(fullOpening * 1.0 / 3.0, result[0].EndOpening, 4);

        // Second segment (rightmost): 2/3 at the break (left), point at right.
        Assert.Equal(fullOpening * 2.0 / 3.0, result[1].StartOpening, 4);
        Assert.Equal(0.0, result[1].EndOpening, 4);
    }

    [Fact]
    public void Calculate_BrokenRightBound_BacksOffHalfBoundPaddingOnlyUnderASpanBar()
    {
        // A line-end (broken RIGHT) bound clears the span bar between its staff and
        // the staff below by bound-padding/2 = 0.5 — and pays NOTHING when that
        // neighbor is hara-kiri'd away (the span bar goes with it). Twin: regression
        // hairpin-span-bar.ly — line-end 101.93 = 102.43 − 0.5 with the span bar,
        // 102.43 exactly without (both LilyPond-matched to the digit).
        // LILYPOND-REF: lily/hairpin.cc:53-109 Hairpin::broken_bound_padding — bound-padding / 2.0 only when both staves share the line-end span bar
        // LILYPOND-REF: scm/define-grobs.scm:1780-1781 Hairpin — bound-padding 1.0 and the broken-bound-padding callback
        var (measures, systems) = CreateTwoSystemLayout();
        var hairpins = ImmutableArray.Create(new HairpinItem(
            HairpinDirection.Crescendo, 0, 0, 3, 0, 0));

        StaffGroupLayout Group(bool lowerHidden)
        {
            var staves = ImmutableArray.Create(
                new StaffLayout(0, ClefType.Treble, 0, 4.0),
                new StaffLayout(1, ClefType.Bass, -9.0, 4.0, IsHidden: lowerHidden));
            return StaffGroupLayout.CreateGrandStaff(
                staves, 0, 13.0, new GrandStaffLayout(staves, 0, 0, -13.0));
        }

        // Both staves stand on system 0: the piece's line-end bound backs off 0.5.
        var spanned = ImmutableArray.Create(
            systems[0] with { StaffGroups = ImmutableArray.Create(Group(lowerHidden: false)) },
            systems[1]);
        var withBar = HairpinEngraver.Calculate(hairpins, spanned, measures);
        Assert.Equal(2, withBar.Length);
        Assert.Equal(40.0 - 0.5, withBar[0].EndX, 4);

        // The lower staff is hidden on system 0: no span bar, the piece runs to the
        // line end untouched.
        var bare = ImmutableArray.Create(
            systems[0] with { StaffGroups = ImmutableArray.Create(Group(lowerHidden: true)) },
            systems[1]);
        var noBar = HairpinEngraver.Calculate(hairpins, bare, measures);
        Assert.Equal(40.0, noBar[0].EndX, 4);
    }

    [Fact]
    public void Calculate_SameSystem_NormalOpenings()
    {
        // Hairpin within one system should have normal openings
        var measures = CreateMeasureLayouts(4);
        var systems = CreateSingleSystem(4);
        var hairpins = ImmutableArray.Create(new HairpinItem(
            HairpinDirection.Crescendo, 0, 0, 2, 0, 0));

        var result = HairpinEngraver.Calculate(hairpins, systems, measures);

        Assert.Single(result);
        Assert.Equal(0.0, result[0].StartOpening, 4);
        Assert.Equal(0.6666, result[0].EndOpening, 4);
    }

    // --- HairpinDirection enum ---

    [Fact]
    public void HairpinDirection_HasCrescendoAndDecrescendo()
    {
        Assert.Equal(0, (int)HairpinDirection.Crescendo);
        Assert.Equal(1, (int)HairpinDirection.Decrescendo);
    }
}
