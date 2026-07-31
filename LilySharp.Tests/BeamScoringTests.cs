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

using Xunit;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Semantics;
using System.Collections.Immutable;

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
public class BeamScoringTests
{
    private static NoteItem CreateNote(int staffPosition)
        => new(staffPosition, Fraction.Eighth, 0, null, false, 0);

    [Fact]
    public void BeamScoringProblem_SolvesForTwoNotes()
    {
        // Arrange: Two 8th notes ascending (staff pos 0 to 4)
        var members = ImmutableArray.Create(
            new BeamMember(CreateNote(0), 1, 0, 1, 0, 0),
            new BeamMember(CreateNote(4), 1, 1, 0, 4, 1)
        );
        var group = new BeamGroup(members, 0, 0, stemUp: true);

        var xPositions = new List<double> { 50.0, 100.0 };
        var problem = new BeamScoringProblem(group, xPositions);

        // Act
        var (leftY, rightY) = problem.Solve();

        // Assert: Beam should be above notes (higher staff position for stem up)
        Assert.True(leftY > 0, $"Left Y {leftY} should be positive for stem up (beam above notes)");
    }

    [Fact]
    public void BeamScoringProblem_StemDownBeamBelowNotes()
    {
        // Arrange: Two 8th notes, stem down
        var members = ImmutableArray.Create(
            new BeamMember(CreateNote(6), 1, 0, 1, 6, 0),
            new BeamMember(CreateNote(3), 1, 1, 0, 3, 1)
        );
        var group = new BeamGroup(members, 0, 0, stemUp: false);

        var xPositions = new List<double> { 50.0, 100.0 };
        var problem = new BeamScoringProblem(group, xPositions);

        // Act
        var (leftY, rightY) = problem.Solve();

        // Assert: Beam should be below notes (lower staff position for stem down)
        Assert.True(leftY < 3, $"Left Y {leftY} should be below the lowest note (3) for stem down");
    }

    [Fact]
    public void BeamScoringProblem_RespectsMinimumStemLength()
    {
        // Arrange: Notes with same pitch
        var members = ImmutableArray.Create(
            new BeamMember(CreateNote(0), 1, 0, 1, 0, 0),
            new BeamMember(CreateNote(0), 1, 1, 0, 0, 1)
        );
        var group = new BeamGroup(members, 0, 0, stemUp: true);

        var xPositions = new List<double> { 50.0, 100.0 };
        var problem = new BeamScoringProblem(group, xPositions);

        // Act
        var (leftY, rightY) = problem.Solve();

        // Assert: Stem length should be at least 5 staff positions (2.5 * 2)
        double stemLength = leftY - 0; // beamY - noteY for stem up
        Assert.True(stemLength >= 5, $"Stem length {stemLength} should be >= 5 (2.5 staff spaces * 2)");
    }

    [Fact]
    public void BeamScoringProblem_ForcedDirectionBeamIsShortened()
    {
        // A beam forced into its unnatural direction is pulled toward the staff by the
        // beam's 'shorten (Beam::calc_stem_shorten). Eight g's at staff position -9 (below
        // the middle line, so their natural stem is UP) forced DOWN: LilyPond quantises to
        // -6.81 ss (staff position -13.62); without the shortening Lily# would draw -7.5.
        // LILYPOND-REF: lily/beam.cc:1059-1090, lily/stem.cc:1245.
        var members = ImmutableArray.CreateBuilder<BeamMember>();
        var xs = new List<double>();
        for (int i = 0; i < 8; i++)
        {
            members.Add(new BeamMember(CreateNote(-9), 1, i == 0 ? 0 : 1, i == 7 ? 0 : 1, -9, i, memberStemUp: false));
            xs.Add(50.0 + i * 20.0);
        }
        var group = new BeamGroup(members.ToImmutable(), 0, 0, stemUp: false);
        var (leftY, _) = new BeamScoringProblem(group, xs).Solve();
        Assert.Equal(-13.62, leftY, 1);
    }

    [Fact]
    public void BeamConfiguration_CalculatesSlope()
    {
        var config = new BeamConfiguration(-3.5, -2.5);
        double xSpan = 50.0;

        double slope = config.GetSlope(xSpan);

        Assert.Equal(0.02, slope, 3);
    }

    [Fact]
    public void BeamConfiguration_CalculatesYAtPosition()
    {
        var config = new BeamConfiguration(-4.0, -3.0);
        double leftX = 50.0;
        double xSpan = 50.0;

        // At left edge
        double yAtLeft = config.GetYAt(50.0, leftX, xSpan);
        Assert.Equal(-4.0, yAtLeft, 3);

        // At right edge
        double yAtRight = config.GetYAt(100.0, leftX, xSpan);
        Assert.Equal(-3.0, yAtRight, 3);

        // At midpoint
        double yAtMid = config.GetYAt(75.0, leftX, xSpan);
        Assert.Equal(-3.5, yAtMid, 3);
    }

    [Fact]
    public void BeamScoringProblem_AvoidsCollisionWithRest()
    {
        // Arrange: Two 8th notes with a rest in between that would collide
        var members = ImmutableArray.Create(
            new BeamMember(CreateNote(0), 1, 0, 1, 0, 0),
            new BeamMember(CreateNote(0), 1, 1, 0, 0, 2)
        );
        var group = new BeamGroup(members, 0, 0, stemUp: true);

        var xPositions = new List<double> { 50.0, 75.0, 100.0 };

        // Without collision - solve normally
        var problemWithout = new BeamScoringProblem(group, xPositions);
        var (leftYWithout, rightYWithout) = problemWithout.Solve();

        // With collision - place a large collision object directly at the beam position
        // Use high base penalty to ensure the collision scorer triggers movement.
        // ⚠️ Solve() answers in staff POSITIONS; BeamCollision speaks staff SPACES.
        var collisions = new List<BeamCollision>
        {
            new BeamCollision(X: 75.0 - 50.0, MinY: leftYWithout / 2 - 0.5, MaxY: leftYWithout / 2 + 0.5, BasePenalty: 5.0)
        };
        var problemWith = new BeamScoringProblem(group, xPositions, collisions: collisions);
        var (leftYWith, _) = problemWith.Solve();

        // Assert: Beam with collision should move to avoid it
        // The beam may move up or down depending on which direction has less penalty
        Assert.True(Math.Abs(leftYWith - leftYWithout) > 0.01,
            $"Beam should adjust position due to collision. Without: {leftYWithout}, With: {leftYWith}");
    }

    [Fact]
    public void BeamScoringProblem_CrossStaff_AppliesPenaltyMultiplier()
    {
        // LILYPOND-REF: lily/beam-quanting.cc — cross-staff 10× penalty multiplier
        // Cross-staff beams should have stricter stem length requirements,
        // resulting in beam positions that more closely match ideal stem lengths.
        var note0 = CreateNote(0);
        var note8 = CreateNote(8);

        // Non cross-staff beam (normal) — targetStaffIndex=-1 means same staff as voice
        var normalMembers = ImmutableArray.Create(
            new BeamMember(note0, 1, 0, 1, 0, 0, memberStemUp: true, targetStaffIndex: -1),
            new BeamMember(note8, 1, 1, 0, 8, 1, memberStemUp: true, targetStaffIndex: -1)
        );
        var normalGroup = new BeamGroup(normalMembers, 0, 0, stemUp: true);
        Assert.False(normalGroup.IsCrossStaff, "Normal beam should not be cross-staff");

        // Cross-staff beam (members on different staves)
        var crossMembers = ImmutableArray.Create(
            new BeamMember(note0, 1, 0, 1, 0, 0, memberStemUp: true, targetStaffIndex: 0),
            new BeamMember(note8, 1, 1, 0, 8, 1, memberStemUp: true, targetStaffIndex: 1)
        );
        var crossGroup = new BeamGroup(crossMembers, 0, 0, stemUp: true);
        Assert.True(crossGroup.IsCrossStaff, "Cross-staff beam should be detected");

        // Both should solve without error
        var xPositions = new List<double> { 50.0, 100.0 };
        var normalProblem = new BeamScoringProblem(normalGroup, xPositions);
        var crossProblem = new BeamScoringProblem(crossGroup, xPositions);

        var (normalLeftY, _) = normalProblem.Solve();
        var (crossLeftY, _) = crossProblem.Solve();

        // Both should produce valid beam positions above notes for stem-up
        Assert.True(normalLeftY > 0, "Normal beam should be above lowest note");
        Assert.True(crossLeftY > 0, "Cross-staff beam should be above lowest note");
    }

    [Fact]
    public void BeamScoringProblem_CollisionPenaltyIncreasesDemerits()
    {
        // Arrange
        var members = ImmutableArray.Create(
            new BeamMember(CreateNote(2), 1, 0, 1, 2, 0),
            new BeamMember(CreateNote(4), 1, 1, 0, 4, 1)
        );
        var group = new BeamGroup(members, 0, 0, stemUp: true);
        var xPositions = new List<double> { 50.0, 100.0 };

        // Create a collision object directly in the beam's expected path.
        // BeamCollision.X is relative to the beam's left stem (here: mid-span); its Y is
        // in staff SPACES (a stem-up beam over a note on the middle line lands near 4).
        var collisions = new List<BeamCollision>
        {
            new BeamCollision(X: 25.0, MinY: 4, MaxY: 5, BasePenalty: 5.0) // Collision near expected beam Y
        };

        // The collision scorer should add demerits when beam is near collision
        var problem = new BeamScoringProblem(group, xPositions, collisions: collisions);
        var (leftY, rightY) = problem.Solve();

        // Beam should still produce valid output
        Assert.True(leftY > 2, "Beam should be above the notes");
        Assert.True(rightY > 4, "Beam right should be above the highest note");
    }

    [Fact]
    public void BeamCollision_OnBeamPath_PushesBeamAway()
    {
        // Two level 8ths, stem up — baseline beam height first.
        var members = ImmutableArray.Create(
            new BeamMember(CreateNote(0), 1, 0, 1, 0, 0),
            new BeamMember(CreateNote(0), 1, 1, 0, 0, 1));
        var group = new BeamGroup(members, 0, 0, stemUp: true);
        var xPositions = new List<double> { 50.0, 100.0 };

        var (baseLeft, _) = new BeamScoringProblem(group, xPositions).Solve();

        // A fat obstacle straddling exactly the baseline beam height at mid-span
        // (X is relative to the left stem; Y is staff spaces, Solve() answers positions).
        var collisions = new List<BeamCollision>
        {
            new BeamCollision(X: 25.0, MinY: baseLeft / 2 - 1, MaxY: baseLeft / 2 + 1, BasePenalty: 4.0)
        };
        var (withLeft, _) =
            new BeamScoringProblem(group, xPositions, collisions: collisions).Solve();

        Assert.True(withLeft > baseLeft + 0.5,
            $"beam should quant up past the obstacle: with={withLeft} base={baseLeft}");
    }

    [Fact]
    public void BeamCollision_OutsideSpan_IsIgnored()
    {
        // X beyond the beam span (e.g. an absolute coordinate passed by
        // mistake) must not influence the result — the relative-X contract.
        // ⚠️ NOT a range check any more: there is no beam SEGMENT over that x, so the
        // beam's own y extent there is empty and the distance to it is infinite
        // (LILYPOND-REF: lily/beam-quanting.cc:186-209 add_collision).
        var members = ImmutableArray.Create(
            new BeamMember(CreateNote(0), 1, 0, 1, 0, 0),
            new BeamMember(CreateNote(0), 1, 1, 0, 0, 1));
        var group = new BeamGroup(members, 0, 0, stemUp: true);
        var xPositions = new List<double> { 50.0, 100.0 };

        var (baseLeft, baseRight) = new BeamScoringProblem(group, xPositions).Solve();

        var collisions = new List<BeamCollision>
        {
            new BeamCollision(X: 75.0, MinY: baseLeft / 2 - 1, MaxY: baseLeft / 2 + 1, BasePenalty: 4.0)
        };
        var (withLeft, withRight) =
            new BeamScoringProblem(group, xPositions, collisions: collisions).Solve();

        Assert.Equal(baseLeft, withLeft, 3);
        Assert.Equal(baseRight, withRight, 3);
    }

    /// <summary>
    /// The second beam of <c>c16[ c8]</c> is a STUB: it reaches 1.1 staff spaces to the
    /// right of the first stem and stops. An obstacle under the stub is under two beams;
    /// the same obstacle a little further right is under one, and the quanter must see the
    /// difference — it is the horizontal extent of the beam's segments that decides, not
    /// the neighbouring stems' beam counts.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/beam-quanting.cc:194-200 add_collision — beam_y_ collects
    ///   <c>vertical_count_ * beam_translation_</c> for every segment whose horizontal
    ///   extent CONTAINS x. The x is perturbed rather than the y, so a scorer that read a
    ///   per-stem beam count (the same number at every x between two stems) fails it.
    /// LILYPOND-REF: lily/beam.cc:604-624 calc_beam_segments — the stub's own length,
    ///   <c>beamlet-default-length</c> capped by <c>beamlet-max-length-proportion</c>.
    /// </remarks>
    [Fact]
    public void BeamletStub_ShieldsOnlyWithinItsOwnHorizontalExtent()
    {
        // c16[ c8]: the first stem carries two beams to the right, the second only one.
        var members = ImmutableArray.Create(
            new BeamMember(CreateNote(0), 2, 0, 2, 0, 0),
            new BeamMember(CreateNote(0), 1, 1, 0, 0, 1));
        var group = new BeamGroup(members, 0, 0, stemUp: true);
        var xPositions = new List<double> { 50.0, 100.0 };

        var (baseLeft, _) = new BeamScoringProblem(group, xPositions).Solve();

        // A thin obstacle exactly where the SECOND beam line runs (one beam translation
        // below the primary, for a stem-up beam), in staff spaces.
        double secondBeamY = baseLeft / 2 - EngravingDefaults.BeamTranslation;
        List<BeamCollision> At(double x) => new()
        {
            new BeamCollision(X: x, MinY: secondBeamY - 0.05, MaxY: secondBeamY + 0.05,
                              BasePenalty: 4.0)
        };

        // Under the stub (it runs 1.1 ss from the stem) — the beam has ink here.
        var (underStub, _) =
            new BeamScoringProblem(group, xPositions, collisions: At(0.5)).Solve();
        // Past its end, still between the stems — only the primary beam is overhead, and
        // that is more than COLLISION_PADDING away.
        var (pastStub, _) =
            new BeamScoringProblem(group, xPositions, collisions: At(2.0)).Solve();

        Assert.Equal(baseLeft, pastStub, 3);
        Assert.True(Math.Abs(underStub - baseLeft) > 0.01,
            $"an obstacle under the stub must move the beam: under={underStub} base={baseLeft}");
    }
}

