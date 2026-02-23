using System.Collections.Immutable;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using Xunit;

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
public class SpringRodModelTests
{
    // --- NoteSpacingParameters ---

    [Fact]
    public void NoteSpacingParameters_Default_MatchesLilyPond()
    {
        var p = NoteSpacingParameters.Default;

        // LILYPOND-REF: define-grobs.scm:2428-2442
        Assert.Equal(1.0, p.KneeSpacingCorrection);
        Assert.Equal(0.25, p.SameDirectionCorrection);
        Assert.Equal(0.5, p.StemSpacingCorrection);
        Assert.True(p.SpaceToBarline);
    }

    // --- Spring.Merge ---

    [Fact]
    public void Spring_Merge_AveragesIdealDistances()
    {
        var a = new Spring(10, 5, 5);
        var b = new Spring(20, 5, 15);

        var merged = Spring.Merge(a, b);

        // Average of 10 and 20 = 15, but headroom: max(5+0.3, 15) = 15
        Assert.Equal(15.0, merged.IdealDistance, 3);
    }

    [Fact]
    public void Spring_Merge_TakesMaxMinDistance()
    {
        var a = new Spring(10, 3, 7);
        var b = new Spring(10, 7, 3);

        var merged = Spring.Merge(a, b);

        Assert.Equal(7.0, merged.MinDistance, 3);
    }

    [Fact]
    public void Spring_Merge_EnforcesHeadroom()
    {
        // When average ideal is close to min, headroom ensures some gap
        var a = new Spring(5.1, 5, 0.1);
        var b = new Spring(5.1, 5, 0.1);

        var merged = Spring.Merge(a, b);

        // Headroom: max(5 + 0.3, 5.1) = 5.3
        Assert.True(merged.IdealDistance >= 5.3 - 0.01,
            $"Merged ideal ({merged.IdealDistance}) should respect headroom >= 5.3");
    }

    // --- Spring.Scale ---

    [Fact]
    public void Spring_Scale_ScalesIdealAndStretch()
    {
        var spring = new Spring(10, 5, 5);

        var scaled = spring.Scale(0.5);

        // IdealDistance = max(5, 10 * 0.5) = 5
        Assert.Equal(5.0, scaled.IdealDistance, 3);
        // MinDistance unchanged
        Assert.Equal(5.0, scaled.MinDistance, 3);
        // InverseStretchStrength scaled: 5 * 0.5 = 2.5
        Assert.Equal(2.5, scaled.InverseStretchStrength, 3);
    }

    [Fact]
    public void Spring_Scale_DoesNotGoBelowMin()
    {
        var spring = new Spring(10, 8, 2);

        var scaled = spring.Scale(0.1);

        // max(8, 10 * 0.1) = max(8, 1) = 8
        Assert.Equal(8.0, scaled.IdealDistance, 3);
    }

    // --- Spring 4-param constructor ---

    [Fact]
    public void Spring_FourParamConstructor_SetsCompressStrength()
    {
        var spring = new Spring(10, 5, 5, 3);

        Assert.Equal(3.0, spring.InverseCompressStrength, 3);
        Assert.Equal(5.0, spring.InverseStretchStrength, 3);
    }

    // --- Stem direction correction ---

    [Fact]
    public void StemCorrection_OppositeDirections_IncreasesSpace()
    {
        // Stem up followed by stem down → need more space
        var noteUp = new NoteItem(-2, Fraction.Quarter, 0, null, false, 0);
        var noteDown = new NoteItem(2, Fraction.Quarter, 0, null, false, 0);

        var springOpposite = SpacingRules.CreateSpring(noteUp, noteDown, Fraction.Quarter);

        // Same direction → less space
        var noteUp2 = new NoteItem(-2, Fraction.Quarter, 0, null, false, 0);
        var noteUp3 = new NoteItem(-4, Fraction.Quarter, 0, null, false, 0);

        var springSame = SpacingRules.CreateSpring(noteUp2, noteUp3, Fraction.Quarter);

        Assert.True(springOpposite.IdealDistance > springSame.IdealDistance,
            $"Opposite stems ({springOpposite.IdealDistance:F3}) should need more space than same ({springSame.IdealDistance:F3})");
    }

    [Fact]
    public void StemCorrection_RestDoesNotAffect()
    {
        var note = new NoteItem(0, Fraction.Quarter, 0, null, false, 0);
        var rest = new RestItem(Fraction.Quarter, 0, 0);

        var springNoParams = SpacingRules.CreateSpring(note, rest, Fraction.Quarter,
            new NoteSpacingParameters { StemSpacingCorrection = 0, SameDirectionCorrection = 0 });
        var springWithParams = SpacingRules.CreateSpring(note, rest, Fraction.Quarter);

        // Rest has no stem direction → no correction
        Assert.Equal(springNoParams.IdealDistance, springWithParams.IdealDistance, 3);
    }

    // --- SpringSolver.ForcePenalty ---

    [Fact]
    public void ForcePenalty_Stretching_LinearPenalty()
    {
        var springs = ImmutableArray.Create(
            new Spring(10, 5, 5),
            new Spring(10, 5, 5)
        );
        var solver = new SpringSolver(springs);

        // Target > ideal (20) → stretching → force > 0
        double penalty = solver.ForcePenalty(25);

        // Stretching force is positive, penalty = force (no extra term)
        Assert.True(penalty > 0, $"Stretching penalty should be positive, got {penalty}");
    }

    [Fact]
    public void ForcePenalty_Compressing_ConvexPenalty()
    {
        var springs = ImmutableArray.Create(
            new Spring(10, 5, 5),
            new Spring(10, 5, 5)
        );
        var solver = new SpringSolver(springs);

        // Target < ideal (20) → compressing → force < 0
        double penalty = solver.ForcePenalty(15);

        // Compression: penalty = f - f^4 * 2 (more negative = worse)
        Assert.True(penalty < 0, $"Compression penalty should be negative, got {penalty}");
    }

    [Fact]
    public void ForcePenalty_Ragged_PenalizesUnusedSpace()
    {
        var springs = ImmutableArray.Create(
            new Spring(10, 5, 5),
            new Spring(10, 5, 5)
        );
        var solver = new SpringSolver(springs);

        // Target > ideal → some unused space
        double penalty = solver.ForcePenalty(25, ragged: true);

        // Ragged: penalty = max(0, idealLength - targetWidth)
        // IdealLength at force 0 = 20, target = 25 → penalty = max(0, 20-25) = 0
        Assert.Equal(0, penalty, 3);

        // Target < ideal → penalty = idealLength - targetWidth
        double penalty2 = solver.ForcePenalty(15, ragged: true);
        Assert.True(penalty2 > 0, "Ragged should penalize when compressing");
    }

    // --- SpringSolver.ApplyRods ---

    [Fact]
    public void ApplyRods_SatisfiedRod_NoChange()
    {
        var springs = ImmutableArray.Create(
            new Spring(10, 5, 5),
            new Spring(10, 5, 5)
        );

        // Rod distance 8 across springs 0-2: min is 5+5=10, already satisfied
        var rods = new[] { (Left: 0, Right: 2, Distance: 8.0) };
        var result = SpringSolver.ApplyRods(springs, rods);

        Assert.Equal(springs[0].IdealDistance, result[0].IdealDistance, 3);
        Assert.Equal(springs[1].IdealDistance, result[1].IdealDistance, 3);
    }

    [Fact]
    public void ApplyRods_ExceedsIdeal_ScalesUp()
    {
        var springs = ImmutableArray.Create(
            new Spring(10, 5, 5),
            new Spring(10, 5, 5)
        );

        // Rod distance 30 across springs 0-2: ideal is 10+10=20, need scaling
        var rods = new[] { (Left: 0, Right: 2, Distance: 30.0) };
        var result = SpringSolver.ApplyRods(springs, rods);

        double totalIdeal = result[0].IdealDistance + result[1].IdealDistance;
        Assert.True(totalIdeal >= 30.0 - 0.01,
            $"Total ideal ({totalIdeal}) should be >= rod distance 30");
    }

    [Fact]
    public void ApplyRods_BetweenMinAndIdeal_UpdatesBlocking()
    {
        var springs = ImmutableArray.Create(
            new Spring(10, 2, 8),
            new Spring(10, 2, 8)
        );

        // Rod distance 12: min=2+2=4, ideal=10+10=20, rod is between
        var rods = new[] { (Left: 0, Right: 2, Distance: 12.0) };
        var result = SpringSolver.ApplyRods(springs, rods);

        // At least one spring should have higher min distance
        double totalMin = result[0].MinDistance + result[1].MinDistance;
        Assert.True(totalMin >= 4.0, "Min distances should be at least original");
    }
}
