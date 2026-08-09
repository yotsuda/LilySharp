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

    // --- knee_correction: LILYPOND-REF: lily/note-spacing.cc:117-137 knee_correction,
    //     forked to at :288-293 by stem_dir_correction ---
    //
    // KneeSpacingCorrection, asserted just above, had NO production reader at all until this
    // branch was ported — audit/property_coverage.csv classified it "Mention". These are its
    // observers, and they are written the way LilyPond's own books falsify the term
    // (audit/lp-geometry/probes/beam-column-spacing.ly, 2.26.0): book A's kneed bar carries
    // its last column gap 1.1742 wider than the two before it, and books E/F/G override
    // knee-spacing-correction to 0 / 0.5 / 2 and move the term in proportion, both signs.

    private static NoteItem BeamedEighth(int staffPosition, bool stemUp, int? beamId) =>
        new NoteItem(staffPosition, Fraction.Eighth, 0, null, false, 0)
        { StemUpOverride = stemUp, BeamId = beamId };

    [Theory]
    [InlineData(true, false, +1)]  // up -> down: the pair LilyPond pushes APART
    [InlineData(false, true, -1)]  // down -> up: pulled together by exactly as much
    public void OppositeStemsInOneBeam_EarnOneHeadWidthLessTheStem_SignedByTheRightStem(
        bool leftUp, bool rightUp, int sign)
    {
        // The term as LilyPond writes it: the right stem's support head extent[RIGHT], less
        // Stem::thickness (:131), times the property, signed by the RIGHT stem's direction.
        double expected = sign
            * (GlyphMetrics.NoteheadBlack.Right - LilySharp.Core.Svg.EngravingDefaults.StemThickness)
            * NoteSpacingParameters.Default.KneeSpacingCorrection;

        double corr = SpacingRules.CalculateStemCorrection(
            BeamedEighth(-6, leftUp, 1), BeamedEighth(6, rightUp, 1),
            NoteSpacingParameters.Default);

        Assert.Equal(expected, corr, 9);
        // …and that is LilyPond's measured 1.1742, which is NOT the head width 1.3042.
        Assert.Equal(1.1742, Math.Abs(corr), 4);
    }

    [Fact]
    public void OppositeStemsInDifferentBeams_TakeTheOverlapBranch_JustLikeUnbeamedOnes()
    {
        // LilyPond forks on `beams_drul[LEFT] == beams_drul[RIGHT]` — one BEAM, not merely
        // "both beamed". Two adjacent beams must therefore behave exactly like no beam.
        double twoBeams = SpacingRules.CalculateStemCorrection(
            BeamedEighth(-6, true, 1), BeamedEighth(6, false, 2),
            NoteSpacingParameters.Default);
        double unbeamed = SpacingRules.CalculateStemCorrection(
            BeamedEighth(-6, true, null), BeamedEighth(6, false, null),
            NoteSpacingParameters.Default);

        Assert.Equal(unbeamed, twoBeams, 9);
        // The overlap branch cannot reach the knee term: it is bounded by its own property.
        Assert.True(Math.Abs(twoBeams) <= NoteSpacingParameters.Default.StemSpacingCorrection);
    }

    [Fact]
    public void TheKneeCorrection_ScalesWithTheProperty_NotWithALiteral()
    {
        NoteItem left = BeamedEighth(-6, true, 1), right = BeamedEighth(6, false, 1);
        double Corr(double knee) => SpacingRules.CalculateStemCorrection(
            left, right, NoteSpacingParameters.Default with { KneeSpacingCorrection = knee });

        double full = Corr(1.0);
        // The shape of LilyPond's E / F / G books. A literal would ignore all three.
        Assert.Equal(0.0, Corr(0.0), 9);
        Assert.Equal(full * 0.5, Corr(0.5), 9);
        Assert.Equal(full * 2.0, Corr(2.0), 9);
    }

    // --- Spring.MergeSprings (lily/spring.cc:101-129 merge_springs) ---

    [Fact]
    public void Spring_MergeSprings_AveragesIdealDistances()
    {
        var a = new Spring(10, 5, 5);
        var b = new Spring(20, 5, 15);

        var merged = Spring.MergeSprings(new[] { a, b });

        // Average of 10 and 20 = 15, but headroom: max(5+0.3, 15) = 15
        Assert.Equal(15.0, merged.IdealDistance, 3);
    }

    [Fact]
    public void Spring_MergeSprings_TakesMaxMinDistance()
    {
        var a = new Spring(10, 3, 7);
        var b = new Spring(10, 7, 3);

        var merged = Spring.MergeSprings(new[] { a, b });

        Assert.Equal(7.0, merged.MinDistance, 3);
    }

    [Fact]
    public void Spring_MergeSprings_EnforcesHeadroom()
    {
        // When average ideal is close to min, headroom ensures some gap
        var a = new Spring(5.1, 5, 0.1);
        var b = new Spring(5.1, 5, 0.1);

        var merged = Spring.MergeSprings(new[] { a, b });

        // Headroom: max(5 + 0.3, 5.1) = 5.3
        Assert.True(merged.IdealDistance >= 5.3 - 0.01,
            $"Merged ideal ({merged.IdealDistance}) should respect headroom >= 5.3");
    }

    /// <summary>
    /// THREE wishes are averaged with equal weight. Folding a two-argument merge over them
    /// weights the first pair 1/4 : 1/4 : 1/2, which is what Lily# used to do — the reason
    /// merge_springs is ported as an N-ary function.
    /// </summary>
    [Fact]
    public void Spring_MergeSprings_WeighsEveryWishEqually()
    {
        var merged = Spring.MergeSprings(new[]
        {
            new Spring(3, 0, 3), new Spring(3, 0, 3), new Spring(9, 0, 9),
        });

        Assert.Equal(5.0, merged.IdealDistance, 6);          // (3+3+9)/3, not 4.5
        Assert.Equal(5.0, merged.InverseStretchStrength, 6);
    }

    /// <summary>
    /// ONE rigid wish makes the merged spring rigid: <c>avg_compress += 1 / 0</c> is
    /// <c>+inf</c> unconditionally (spring.cc:115), so <c>1 / avg_compress</c> is 0. This is
    /// the branch a notation+tab line start takes — the TAB clef's wish has nothing left to
    /// compress — and the one the old two-argument merge got wrong, returning
    /// <c>avgIdeal - maxMin</c> instead.
    /// </summary>
    [Fact]
    public void Spring_MergeSprings_OneRigidWishMakesTheMergeIncompressible()
    {
        var flexible = new Spring(10, 5, 0, 4);
        var rigid = new Spring(8, 5, 0, 0);

        var merged = Spring.MergeSprings(new[] { flexible, rigid });

        Assert.Equal(9.0, merged.IdealDistance, 6);
        Assert.Equal(0.0, merged.InverseCompressStrength, 6);
        Assert.Equal(9.0, merged.Length(-1.0), 6);   // cannot give way at all
    }

    // --- the SETTERS: replacing the ideal or the minimum must NOT restate the strengths ---
    //
    // LilyPond's Spring::set_ideal_distance / set_min_distance / ensure_min_distance assign
    // one field and call update_blocking_force (), and that is all: whatever
    // set_default_strength or set_inverse_*_strength last wrote stays
    // (lily/spring.cc:131-159). Every refinement a note spring gets after it is built runs
    // through those setters — the left head width and the stem correction land on
    // note-spacing.cc:113 base.set_ideal_distance, and the SKYLINE minimum on :83
    // base.set_min_distance — so the compressibility keeps the DURATION value
    // `fraction * (duration_space - increment)` that Spacing_spanner::note_spacing gave it
    // (spacing-basic.cc:151-157 with spring.cc:204-210). Rebuilding the spring through the
    // defaulting constructor instead makes it `ideal - min`, which is a different number and
    // is only visible on a COMPRESSED line.

    /// <summary>
    /// The quarter-to-quarter spring of ledger point
    /// <c>compressed.line-start.time-to-first-note</c> (probe score TSJ), and the number
    /// LilyPond itself compresses it by.
    /// </summary>
    /// <remarks>
    /// MEASURED, not inferred: <c>audit/lp-geometry/probes/compressed-line-force.ly</c>
    /// engraves that music twice, ragged (CLW) and justified (CLJ), so CLW - CLJ is
    /// <c>|force| * inverse_compress_strength</c> per spring and nothing else. LilyPond gives
    /// up 0.011749 on this spring at a solved force of -0.006918750, i.e. 1.698045 — which is
    /// <c>duration_space (quarter) - spacing-increment = 2.898045 - 1.2</c>, and NOT
    /// <c>ideal - min = 3.002245 - 1.604200 = 1.398045</c>.
    /// </remarks>
    [Fact]
    public void NoteSpring_KeepsItsDurationCompressibility_WhenIdealAndMinimumAreReplaced()
    {
        // Spring (fraction * len, fraction * increment) — spacing-basic.cc:157.
        var duration = new Spring(2.898045, 1.2, 1.698045);
        Assert.Equal(1.698045, duration.InverseCompressStrength, 6);

        // note-spacing.cc:77 then :113 — the left head width refines the IDEAL.
        var refined = duration.WithIdealDistance(3.002245);
        Assert.Equal(3.002245, refined.IdealDistance, 6);
        Assert.Equal(1.698045, refined.InverseCompressStrength, 6);

        // note-spacing.cc:82-83 — the skyline distance becomes the MINIMUM.
        var withSkyline = refined.EnsureMinDistance(1.604200);
        Assert.Equal(1.604200, withSkyline.MinDistance, 6);

        // LilyPond's number, and not the one a rebuild would produce.
        Assert.Equal(1.698045, withSkyline.InverseCompressStrength, 6);
        Assert.NotEqual(1.398045, withSkyline.InverseCompressStrength, 6);

        // update_blocking_force DOES follow the new pair. LILYPOND-REF: spring.cc:62-83.
        Assert.Equal((1.604200 - 3.002245) / 1.698045, withSkyline.BlockingForce, 6);

        // …and that is what the drawn length is: at LilyPond's solved force the spring gives
        // up 1.698045 * 0.006918750 = 0.011748349. The probe reports 0.011749 because it
        // subtracts two column positions LilyPond printed to six places, so five is the
        // precision that comparison actually carries.
        Assert.Equal(1.698045 * 0.006918750,
            refined.IdealDistance - withSkyline.Length(-0.006918750), 9);
        Assert.Equal(0.011749, refined.IdealDistance - withSkyline.Length(-0.006918750), 5);
    }

    /// <summary>
    /// <see cref="Spring.EnsureMinDistance"/> lowers nothing — <c>ensure_min_distance</c> is
    /// <c>set_min_distance (max (d, min_distance_))</c>. LILYPOND-REF: lily/spring.cc:155-159.
    /// </summary>
    [Fact]
    public void EnsureMinDistance_NeverLowersTheMinimum()
    {
        var spring = new Spring(3.0, 1.6, 1.7);
        Assert.Same(spring, spring.EnsureMinDistance(1.0));
        Assert.Equal(1.9, spring.EnsureMinDistance(1.9).MinDistance, 6);
    }

    /// <summary>
    /// A ROD raises a spring's minimum through <c>Spring::set_blocking_force</c>, which sets
    /// <c>min_distance_ = length (f)</c> and updates the blocking force — the strengths are
    /// what make the new blocking force come out as <c>f</c>, so restating them would undo
    /// the rod. LILYPOND-REF: lily/spring.cc:183-195; lily/simple-spacer.cc:124-126.
    /// </summary>
    [Fact]
    public void ApplyRods_RaisesTheMinimum_WithoutRestatingTheCompressibility()
    {
        // The four-argument constructor, because the compressibility here is the DURATION one
        // (1.7 = len - increment) and not ideal - min (1.5) — which is the whole point.
        var springs = ImmutableArray.Create(
            new Spring(3.0, 1.5, 1.7, 1.7),
            new Spring(3.0, 1.5, 1.7, 1.7));

        var rodded = SpringSolver.ApplyRods(
            springs, new (int Left, int Right, double Distance)[] { (0, 2, 5.0) });

        Assert.True(rodded[0].MinDistance > 1.5);
        foreach (var s in rodded)
            Assert.Equal(1.7, s.InverseCompressStrength, 6);
        // The rod is what it says: the range cannot compress below 5.0.
        Assert.Equal(5.0, rodded[0].Length(double.MinValue) + rodded[1].Length(double.MinValue), 6);
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
    public void ApplyRods_ExceedsIdeal_StretchesByBlockingForce()
    {
        var springs = ImmutableArray.Create(
            new Spring(10, 5, 5),
            new Spring(10, 5, 5)
        );

        // Rod distance 30 across springs 0-2: ideal is 10+10=20 and the springs CAN
        // stretch, so LilyPond raises a positive blocking force — it does NOT scale
        // the ideals (that fallback is only for a range with infinite stiffness).
        // rod_force = (30 - 20) / (5 + 5) = 1.0, each spring frozen at
        // length (1.0) = 10 + 1.0 * 5 = 15.
        // LILYPOND-REF: lily/simple-spacer.cc:89-127 add_rod calls set_blocking_force per spring.
        // LILYPOND-REF: lily/spring.cc:183-195 set_blocking_force — min_distance_ = length (f).
        var rods = new[] { (Left: 0, Right: 2, Distance: 30.0) };
        var result = SpringSolver.ApplyRods(springs, rods);

        Assert.Equal(10.0, result[0].IdealDistance, 6);
        Assert.Equal(10.0, result[1].IdealDistance, 6);
        Assert.Equal(15.0, result[0].Length(0), 6);
        Assert.Equal(15.0, result[1].Length(0), 6);
        Assert.Equal(1.0, result[0].BlockingForce, 6);
        // The range holds the rod distance at every force at or below the blocking force.
        Assert.Equal(30.0, result[0].Length(-2) + result[1].Length(-2), 6);
    }

    [Fact]
    public void ApplyRods_ExceedsIdeal_InextensibleRangeScalesUp()
    {
        // A range with NO stretchability cannot take a blocking force — this is the
        // isinf branch, the only one that scales the ideals.
        // LILYPOND-REF: lily/simple-spacer.cc:104-122 add_rod isinf branch — set_ideal_distance
        //   scales by dist / spring_dist.
        var springs = ImmutableArray.Create(
            new Spring(10, 5, 0),
            new Spring(10, 5, 0)
        );
        var rods = new[] { (Left: 0, Right: 2, Distance: 30.0) };
        var result = SpringSolver.ApplyRods(springs, rods);

        double totalIdeal = result[0].IdealDistance + result[1].IdealDistance;
        Assert.Equal(30.0, totalIdeal, 6);
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

    // --- CalculateDurationSpace with custom baseShortestDuration ---

    [Fact]
    public void CalculateDurationSpace_WithBaseShortestDuration_MatchesDefault()
    {
        // LILYPOND-REF: lily/spacing-options.cc:68-104
        // Calling with the default value should produce the same result
        var quarter = Fraction.Quarter;
        double defaultResult = SpacingRules.CalculateDurationSpace(quarter);
        double explicitResult = SpacingRules.CalculateDurationSpace(quarter, 0.1875);

        Assert.Equal(defaultResult, explicitResult, 6);
    }

    [Fact]
    public void CalculateDurationSpace_ShorterBase_GivesLessSpaceForShortNotes()
    {
        // LILYPOND-REF: lily/spacing-spanner.cc
        // When the score has 16th notes as shortest, the base is 1/16 = 0.0625.
        // A 16th note with base=1/16 gets ratio=1 (shortest_duration_space * increment).
        // A 16th note with base=1/8 gets ratio=0.5 (linear, less space).
        var sixteenth = Fraction.Sixteenth;

        double spaceWithEighthBase = SpacingRules.CalculateDurationSpace(sixteenth, 0.125);
        double spaceWithSixteenthBase = SpacingRules.CalculateDurationSpace(sixteenth, 0.0625);

        // With sixteenth base, the 16th note is the reference and gets more space
        Assert.True(spaceWithSixteenthBase > spaceWithEighthBase,
            $"16th note space with base=1/16 ({spaceWithSixteenthBase:F3}) should be > " +
            $"with base=1/8 ({spaceWithEighthBase:F3})");
    }

    [Fact]
    public void CalculateDurationSpace_QuarterBase_QuarterNoteGetsBaseSpace()
    {
        // LILYPOND-REF: lily/spacing-options.cc:68-104
        // When quarter note is the shortest, ratio = 0.25/0.25 = 1.0
        // space = (ShortestDurationSpace + log2(1)) * increment = 2.0 * 1.2 = 2.4
        var quarter = Fraction.Quarter;
        double space = SpacingRules.CalculateDurationSpace(quarter, 0.25);

        double expected = LilySharp.Core.Svg.EngravingDefaults.ShortestDurationSpace
                        * LilySharp.Core.Svg.EngravingDefaults.SpacingIncrement;
        Assert.Equal(expected, space, 6);
    }
}
