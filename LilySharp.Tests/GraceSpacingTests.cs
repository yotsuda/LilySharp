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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using Xunit;

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
public class GraceSpacingTests
{
    // Helper to create grace note info with duration
    private static GraceColumnInfo MakeGrace(Fraction duration, int staffPos = 0) =>
        new(staffPos, null, false, duration);

    [Fact]
    public void GraceSpacingParameters_Default_MatchesLilyPond()
    {
        var p = GraceSpacingParameters.Default;

        // LILYPOND-REF: define-grobs.scm:1585-1598
        Assert.Equal(0.8, p.SpacingIncrement);
        Assert.Equal(1.6, p.ShortestDurationSpace);
        Assert.Equal(0.125, p.BaseShortestDuration);
    }

    [Fact]
    public void CreateGraceSpring_TighterThanRegular()
    {
        var graceSpring = SpacingRules.CreateGraceSpring(Fraction.Eighth);
        var regularSpring = SpacingRules.CreateSpring(null, null, Fraction.Eighth);

        // Grace notes should have tighter spacing
        Assert.True(graceSpring.IdealDistance < regularSpring.IdealDistance,
            $"Grace ideal ({graceSpring.IdealDistance:F3}) should be < regular ({regularSpring.IdealDistance:F3})");
        Assert.True(graceSpring.MinDistance < regularSpring.MinDistance,
            $"Grace min ({graceSpring.MinDistance:F3}) should be < regular ({regularSpring.MinDistance:F3})");
    }

    [Fact]
    public void CreateGraceSpring_InverseStretchStrength_IsHalfIncrement()
    {
        // LILYPOND-REF: spacing-basic.cc:153
        var spring = SpacingRules.CreateGraceSpring(Fraction.Eighth);

        Assert.Equal(0.4, spring.InverseStretchStrength, 3);
    }

    [Fact]
    public void CreateGraceSpring_EighthNote_UsesGraceParameters()
    {
        var spring = SpacingRules.CreateGraceSpring(Fraction.Eighth);

        // ratio = 1.0, spaceFactor = 1.6 + 0 = 1.6
        // idealDistance = 1.6 * 0.8 = 1.28
        Assert.Equal(1.28, spring.IdealDistance, 2);
        Assert.Equal(0.8, spring.MinDistance, 3);
    }

    [Fact]
    public void CreateGraceSpring_SixteenthNote_ShorterThanEighth()
    {
        var eighthSpring = SpacingRules.CreateGraceSpring(Fraction.Eighth);
        var sixteenthSpring = SpacingRules.CreateGraceSpring(Fraction.Sixteenth);

        Assert.True(sixteenthSpring.IdealDistance < eighthSpring.IdealDistance,
            $"16th ({sixteenthSpring.IdealDistance:F3}) should be shorter than 8th ({eighthSpring.IdealDistance:F3})");
    }

    [Fact]
    public void AdjustSpringForGraceNotes_IncreasesMinDistance()
    {
        var spring = new Spring(3.0, 1.2, 1.8);

        var adjusted = SpacingRules.AdjustSpringForGraceNotes(spring, 2);

        double graceWidth = GraceNoteEngraver.GetGraceGroupWidth(2);
        Assert.True(adjusted.MinDistance >= spring.MinDistance + graceWidth - 0.01,
            $"Adjusted min ({adjusted.MinDistance:F3}) should include grace width ({graceWidth:F3})");
    }

    [Fact]
    public void AdjustSpringForGraceNotes_ZeroNotes_NoChange()
    {
        var spring = new Spring(3.0, 1.2, 1.8);

        var adjusted = SpacingRules.AdjustSpringForGraceNotes(spring, 0);

        Assert.Equal(spring.IdealDistance, adjusted.IdealDistance, 3);
        Assert.Equal(spring.MinDistance, adjusted.MinDistance, 3);
    }

    [Fact]
    public void GraceNoteEngraver_GetGraceGroupWidth_Consistent()
    {
        double width1 = GraceNoteEngraver.GetGraceGroupWidth(1);
        double width2 = GraceNoteEngraver.GetGraceGroupWidth(2);
        double width3 = GraceNoteEngraver.GetGraceGroupWidth(3);

        Assert.True(width1 > 0, "Single grace should have positive width");
        Assert.True(width2 > width1, "Two grace notes should be wider than one");
        Assert.True(width3 > width2, "Three grace notes should be wider than two");
    }

    // === H-4: Grace note spacing dynamics tests ===

    [Fact]
    public void CalculateGraceGroupShortestDuration_AllEighths_ReturnsEighth()
    {
        // LILYPOND-REF: lily/grace-spacing-engraver.cc — per-group common shortest duration
        var notes = ImmutableArray.Create(
            MakeGrace(Fraction.Eighth),
            MakeGrace(Fraction.Eighth),
            MakeGrace(Fraction.Eighth));

        double shortest = SpacingRules.CalculateGraceGroupShortestDuration(notes);

        Assert.Equal(0.125, shortest, 4);
    }

    [Fact]
    public void CalculateGraceGroupShortestDuration_MixedDurations_ReturnsShortest()
    {
        // LILYPOND-REF: lily/grace-spacing-engraver.cc — finds shortest in group
        var notes = ImmutableArray.Create(
            MakeGrace(Fraction.Eighth),
            MakeGrace(Fraction.Sixteenth),
            MakeGrace(Fraction.Eighth));

        double shortest = SpacingRules.CalculateGraceGroupShortestDuration(notes);

        Assert.Equal(0.0625, shortest, 4);  // 1/16
    }

    [Fact]
    public void CalculateGraceGroupShortestDuration_Empty_ReturnsDefault()
    {
        var notes = ImmutableArray<GraceColumnInfo>.Empty;

        double shortest = SpacingRules.CalculateGraceGroupShortestDuration(notes);

        Assert.Equal(GraceSpacingParameters.Default.BaseShortestDuration, shortest, 4);
    }

    [Fact]
    public void CreateGraceSpring_WithPerGroupBsd_AffectsSpacing()
    {
        // LILYPOND-REF: lily/grace-spacing-engraver.cc — per-group common shortest duration
        // When the base shortest duration is 1/16, eighth notes get more space
        // (because ratio = (1/8) / (1/16) = 2, spaceFactor = 1.6 + log2(2) = 2.6)
        var springDefaultBsd = SpacingRules.CreateGraceSpring(Fraction.Eighth);
        var springCustomBsd = SpacingRules.CreateGraceSpring(Fraction.Eighth, baseShortestDuration: 0.0625);

        Assert.True(springCustomBsd.IdealDistance > springDefaultBsd.IdealDistance,
            $"With 16th-based bsd, eighth spring ({springCustomBsd.IdealDistance:F3}) " +
            $"should be wider than with default bsd ({springDefaultBsd.IdealDistance:F3})");
    }

    [Fact]
    public void CalculateGraceGroupSpringWidth_SingleNote_MinimumWidth()
    {
        // LILYPOND-REF: lily/grace-spacing-engraver.cc:36-80
        var notes = ImmutableArray.Create(MakeGrace(Fraction.Eighth));

        double width = SpacingRules.CalculateGraceGroupSpringWidth(notes);

        // One gap — the grace to the main note — and it is an ordinary grace spring, not a
        // junction of its own. Its floor is merge_springs' headroom over the two columns'
        // facing skylines (lily/spring.cc:122), which a flagged grace makes the larger term.
        // LILYPOND-REF: lily/spacing-basic.cc:163-180 Spacing_spanner::note_spacing;
        //   MEASURED 1.938627 for a lone sixteenth grace (grace.column.single.to-main).
        Assert.True(width >= SpacingRules.SpringHeadroom,
            $"Single grace width ({width:F3}) should clear merge_springs' headroom");
    }

    [Fact]
    public void CalculateGraceGroupSpringWidth_MultipleNotes_Wider()
    {
        // LILYPOND-REF: lily/grace-spacing-engraver.cc:36-80
        var oneNote = ImmutableArray.Create(MakeGrace(Fraction.Eighth));
        var twoNotes = ImmutableArray.Create(
            MakeGrace(Fraction.Eighth),
            MakeGrace(Fraction.Eighth));
        var threeNotes = ImmutableArray.Create(
            MakeGrace(Fraction.Eighth),
            MakeGrace(Fraction.Eighth),
            MakeGrace(Fraction.Eighth));

        double w1 = SpacingRules.CalculateGraceGroupSpringWidth(oneNote);
        double w2 = SpacingRules.CalculateGraceGroupSpringWidth(twoNotes);
        double w3 = SpacingRules.CalculateGraceGroupSpringWidth(threeNotes);

        Assert.True(w2 > w1, $"Two ({w2:F3}) should be wider than one ({w1:F3})");
        Assert.True(w3 > w2, $"Three ({w3:F3}) should be wider than two ({w2:F3})");
    }

    [Fact]
    public void CalculateGraceGroupSpringWidth_MixedDurations_ShorterGetLessSpace()
    {
        // LILYPOND-REF: lily/grace-spacing-engraver.cc — per-group common shortest duration
        // When there are mixed durations, shorter notes get less space because
        // bsd = shortest in group, and ratio < 1 for shorter notes
        var mixedGroup = ImmutableArray.Create(
            MakeGrace(Fraction.Eighth),     // ratio = (1/8) / (1/16) = 2.0
            MakeGrace(Fraction.Sixteenth));  // ratio = (1/16) / (1/16) = 1.0

        var uniformGroup = ImmutableArray.Create(
            MakeGrace(Fraction.Eighth),     // ratio = 1.0
            MakeGrace(Fraction.Eighth));    // ratio = 1.0

        double wMixed = SpacingRules.CalculateGraceGroupSpringWidth(mixedGroup);
        double wUniform = SpacingRules.CalculateGraceGroupSpringWidth(uniformGroup);

        // Mixed group: eighth gets more space (bsd=1/16, ratio=2 → spaceFactor=2.6)
        // while sixteenth gets standard space (ratio=1 → spaceFactor=1.6)
        // Uniform group: both get standard space (ratio=1 → spaceFactor=1.6)
        Assert.True(wMixed > wUniform,
            $"Mixed duration group ({wMixed:F3}) should be wider than uniform ({wUniform:F3})");
    }

    [Fact]
    public void CalculateGraceGroupSpringWidth_UniformDurations_SameRegardlessOfValue()
    {
        // LILYPOND-REF: lily/grace-spacing-engraver.cc — per-group common shortest duration
        // When all notes are the same duration, bsd = that duration,
        // so ratio is always 1.0 and spacing is identical
        var eighths = ImmutableArray.Create(
            MakeGrace(Fraction.Eighth),
            MakeGrace(Fraction.Eighth));
        var sixteenths = ImmutableArray.Create(
            MakeGrace(Fraction.Sixteenth),
            MakeGrace(Fraction.Sixteenth));

        double wEighths = SpacingRules.CalculateGraceGroupSpringWidth(eighths);
        double wSixteenths = SpacingRules.CalculateGraceGroupSpringWidth(sixteenths);

        Assert.Equal(wEighths, wSixteenths, 4);
    }

    [Fact]
    public void AdjustSpringForGraceNotes_WithGraceInfo_UsesSpringWidth()
    {
        // LILYPOND-REF: lily/grace-spacing-engraver.cc:36-80
        var spring = new Spring(3.0, 1.2, 1.8);
        var graceNotes = ImmutableArray.Create(
            MakeGrace(Fraction.Eighth),
            MakeGrace(Fraction.Eighth));

        var adjusted = SpacingRules.AdjustSpringForGraceNotes(spring, graceNotes);

        double springWidth = SpacingRules.CalculateGraceGroupSpringWidth(graceNotes);
        Assert.True(adjusted.MinDistance >= spring.MinDistance + springWidth - 0.01,
            $"Adjusted min ({adjusted.MinDistance:F3}) should include spring-based width ({springWidth:F3})");
    }

    [Fact]
    public void AdjustSpringForGraceNotes_WithGraceInfo_Empty_NoChange()
    {
        var spring = new Spring(3.0, 1.2, 1.8);
        var empty = ImmutableArray<GraceColumnInfo>.Empty;

        var adjusted = SpacingRules.AdjustSpringForGraceNotes(spring, empty);

        Assert.Equal(spring.IdealDistance, adjusted.IdealDistance, 3);
        Assert.Equal(spring.MinDistance, adjusted.MinDistance, 3);
    }

    [Fact]
    public void GraceNoteEngraver_GetGraceGroupWidth_WithNotes_UsesSpringBased()
    {
        // LILYPOND-REF: lily/grace-spacing-engraver.cc:36-80
        var notes = ImmutableArray.Create(
            MakeGrace(Fraction.Eighth),
            MakeGrace(Fraction.Eighth));

        double springWidth = GraceNoteEngraver.GetGraceGroupWidth(notes);
        double expectedWidth = SpacingRules.CalculateGraceGroupSpringWidth(notes);

        Assert.Equal(expectedWidth, springWidth, 4);
    }

    /// <summary>
    /// The last grace → main note gap is an ordinary grace spring, NOT a junction of its own
    /// — so it moves with the run's parameters like any other gap.
    /// </summary>
    /// <remarks>
    /// This replaces <c>GraceToMainRod_MatchesLilyPond</c>, which asserted that a 0.4 rod was
    /// LilyPond's. It is not: <c>delta_t.grace_part_</c> is non-zero for that pair too, so
    /// lily/spacing-basic.cc:163 takes the grace branch, and MEASURED (ledger
    /// grace.column.four-sixteenths.to-main) LilyPond gives the closing gap exactly what it
    /// gives the interior ones. The rule is asserted by PERTURBATION rather than by a value,
    /// per HANDOFF 5.4: widen the run's <c>spacing-increment</c> and the closing gap has to
    /// follow, which a constant junction would not.
    /// </remarks>
    [Fact]
    public void GraceToMainGap_IsAGraceSpring_NotAConstantJunction()
    {
        var notes = ImmutableArray.Create(MakeGrace(Fraction.Eighth), MakeGrace(Fraction.Eighth));

        var narrow = SpacingRules.GraceColumns(notes, mainItem: null);
        var wide = SpacingRules.GraceColumns(
            notes, mainItem: null, new GraceSpacingParameters { SpacingIncrement = 1.6 });

        // The closing gap IS the interior gap — same spring, same floor, same answer.
        Assert.Equal(narrow.Offsets[1] - narrow.Offsets[0], narrow.ToMain, 9);
        Assert.Equal(wide.Offsets[1] - wide.Offsets[0], wide.ToMain, 9);
        // And it follows the run's parameters. A constant junction would not move at all;
        // the default run sits on its skyline floor, so this also proves the spring can
        // lift the closing gap off that floor.
        Assert.True(wide.ToMain > narrow.ToMain + 0.4,
            $"closing gap {narrow.ToMain:F6} -> {wide.ToMain:F6} did not follow spacing-increment");
    }
}
