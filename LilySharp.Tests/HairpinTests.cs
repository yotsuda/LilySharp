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
        // StartX: measure[0].X + item[0].X + item[0].Width + BoundPadding*0.5
        // = 0 + 1.0 + 2.0 + 0.5 = 3.5
        Assert.Equal(3.5, h.StartX, 2);
        // EndX: measure[2].X + item[0].X - BoundPadding*0.5
        // = 40 + 1.0 - 0.5 = 40.5
        Assert.Equal(40.5, h.EndX, 2);
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
    public void Calculate_MinimumLength_Enforced()
    {
        // LILYPOND-REF: scm/define-grobs.scm:1659 (minimum-length . 2.0)
        // Create hairpin within same measure with very close start/end
        var items = ImmutableArray.Create(
            new ItemLayout(0, 1.0, 0.5),
            new ItemLayout(1, 2.0, 0.5));
        var measures = ImmutableArray.Create(
            new MeasureLayout(0, 0, 10, items));
        var systems = ImmutableArray.Create(new SystemLayout(0, 10.0, 200.0, 5.0, measures));
        var hairpins = ImmutableArray.Create(new HairpinItem(
            HairpinDirection.Crescendo, 0, 0, 0, 1, 0));

        var result = HairpinEngraver.Calculate(hairpins, systems, measures);

        Assert.Single(result);
        double length = result[0].EndX - result[0].StartX;
        Assert.True(length >= 2.0, $"Hairpin length {length:F2} should be >= 2.0 (minimum-length)");
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
