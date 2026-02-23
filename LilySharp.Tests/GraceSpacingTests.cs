using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Layout;
using Xunit;

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
public class GraceSpacingTests
{
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
}
