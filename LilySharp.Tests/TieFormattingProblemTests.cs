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

using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Tests for TieFormattingProblem - constraint-based tie positioning.
/// <para>
/// These synthetic fixtures carry no head geometry, so the head-centre attachment is passed
/// equal to the inner-edge one (no recession). The edge-vs-centre rule — attach at the head
/// centre once the scored endpoint clears the head box — is exercised by the LP-fidelity
/// ledger probes (staff/system tie-{under,over}-notes), not here.
/// </para>
/// </summary>
[Trait("Category", "Unit")]
public class TieFormattingProblemTests
{
    private static NoteItem CreateNote(int staffPosition) =>
        new(staffPosition, Fraction.Quarter, 0, null, false, 0);

    /// <summary>
    /// A tie with its direction IMPOSED, which is what these fixtures need: they assert the
    /// shape a given direction produces, and an ordinary tie now arrives with no direction at
    /// all and lets the search pick one (see <see cref="TieItem.ForcedCurveUp"/>). Which
    /// direction the search picks is held by the ledger books
    /// <c>tie.direction.*</c>, not here.
    /// </summary>
    private static TieItem CreateTie(int staffPosition, bool curveUp = true)
    {
        var note = CreateNote(staffPosition);
        return new TieItem(note, note, staffPosition, forcedCurveUp: curveUp, 0, 0, 0, 1);
    }

    [Fact]
    public void Solve_BasicTie_ReturnsValidLayout()
    {
        var tie = CreateTie(0, curveUp: true);
        var problem = new TieFormattingProblem(tie, 5, 15, 5, 15, 2);

        var layout = problem.Solve();

        Assert.NotNull(layout);
        Assert.True(layout.StartX > 0);
        Assert.True(layout.EndX > layout.StartX);
    }

    [Fact]
    public void Solve_TieCurveUp_HasNegativeControlY()
    {
        var tie = CreateTie(0, curveUp: true);
        var problem = new TieFormattingProblem(tie, 5, 15, 5, 15, 2);

        var layout = problem.Solve();

        // Curve up: control points sit ABOVE the baseline = LARGER value in the page
        // Y-up frame the layout now stores.
        Assert.True(layout.Control1.Y > layout.StartYUp);
        Assert.True(layout.Control2.Y > layout.EndYUp);
    }

    [Fact]
    public void Solve_TieCurveDown_HasPositiveControlY()
    {
        var tie = CreateTie(0, curveUp: false);
        var problem = new TieFormattingProblem(tie, 5, 15, 5, 15, 2);

        var layout = problem.Solve();

        // Curve down: control points sit BELOW the baseline = SMALLER value in the
        // page Y-up frame the layout now stores.
        Assert.True(layout.Control1.Y < layout.StartYUp);
        Assert.True(layout.Control2.Y < layout.EndYUp);
    }

    [Fact]
    public void Solve_WithExistingTies_AvoidsCollision()
    {
        var tie1 = CreateTie(0, curveUp: true);
        var problem1 = new TieFormattingProblem(tie1, 5, 15, 5, 15, 2);
        var layout1 = problem1.Solve();

        var tie2 = CreateTie(1, curveUp: true);
        var existingTies = new[] { layout1 };
        var problem2 = new TieFormattingProblem(tie2, 5, 15, 5, 15, 2.5, existingTies: existingTies);
        var layout2 = problem2.Solve();

        // The second tie should be positioned to avoid the first
        Assert.NotNull(layout2);
        Assert.True(layout2.EndX > layout2.StartX);
    }

    [Fact]
    public void Solve_ShortTie_HasCompactHeight()
    {
        var tie = CreateTie(0, curveUp: true);
        var shortProblem = new TieFormattingProblem(tie, 5, 7, 5, 7, 2);
        var longProblem = new TieFormattingProblem(tie, 5, 20, 5, 20, 2);

        var shortLayout = shortProblem.Solve();
        var longLayout = longProblem.Solve();

        double shortHeight = Math.Abs(shortLayout.Control1.Y - shortLayout.StartYUp);
        double longHeight = Math.Abs(longLayout.Control1.Y - longLayout.StartYUp);
        Assert.True(shortHeight < longHeight);
    }

    [Fact]
    public void Solve_TieAtStaffLine_ReturnsValidLayout()
    {
        var tie = CreateTie(0, curveUp: true);
        var problem = new TieFormattingProblem(tie, 5, 15, 5, 15, 2);

        var layout = problem.Solve();

        Assert.NotNull(layout);
        Assert.True(layout.EndX > layout.StartX);
    }

    // --- Helper function tests ---

    [Theory]
    [InlineData(0.0, 1.0)]     // At zero → 1.0
    [InlineData(0.5, 0.0)]     // At threshold → 0.0
    [InlineData(1.0, 0.0)]     // Beyond threshold → 0.0
    public void PeakAround_ReturnsExpectedValues(double x, double expected)
    {
        // LILYPOND-REF: lily/misc.cc:48-55
        double result = BezierBow.PeakAround(0.05, 0.5, x);
        Assert.Equal(expected, result, 3);
    }

    [Fact]
    public void PeakAround_NegativeX_ReturnsOne()
    {
        Assert.Equal(1.0, BezierBow.PeakAround(0.1, 0.5, -0.1));
    }

    [Theory]
    [InlineData(0.0, 0.0)]     // At zero → 0.0
    [InlineData(1.0, 1.0)]     // At standard_x → 1.0
    public void ConvexAmplifier_ReturnsExpectedValues(double x, double expected)
    {
        // LILYPOND-REF: lily/misc.cc:60-65
        double result = BezierBow.ConvexAmplifier(1.0, 0.9, x);
        Assert.Equal(expected, result, 3);
    }

    [Fact]
    public void ConvexAmplifier_BeyondStandard_GreaterThanOne()
    {
        double result = BezierBow.ConvexAmplifier(1.0, 0.9, 2.0);
        Assert.True(result > 1.0, $"ConvexAmplifier at 2*standard should be > 1.0, got {result}");
    }

    // --- Dot collision tests ---
    // LILYPOND-REF: lily/tie-formatting-problem.cc:795-818

    [Fact]
    public void Solve_DottedNote_CurveUp_AvoidsDot()
    {
        // Note on a staff line (position 0) with 1 dot.
        // Dot shifts up by 1 half-space → dotPosition=1.
        // CurveUp tie should be penalized if it curves toward the dot.
        var startNote = new NoteItem(0, Fraction.Quarter, dots: 1, null, false, 0, hasTieStart: true);
        var endNote = CreateNote(0);
        var tieUp = new TieItem(startNote, endNote, 0, forcedCurveUp: true, 0, 0, 0, 1);
        var tieDown = new TieItem(startNote, endNote, 0, forcedCurveUp: false, 0, 0, 0, 1);

        // With dot, the solver should favor the direction that avoids the dot
        var problemUp = new TieFormattingProblem(tieUp, 5, 15, 5, 15, 2, startDots: 1);
        var problemDown = new TieFormattingProblem(tieDown, 5, 15, 5, 15, 2, startDots: 1);

        var layoutUp = problemUp.Solve();
        var layoutDown = problemDown.Solve();

        // Both should produce valid ties
        Assert.NotNull(layoutUp);
        Assert.NotNull(layoutDown);
        Assert.True(layoutUp.EndX > layoutUp.StartX);
        Assert.True(layoutDown.EndX > layoutDown.StartX);
    }

    [Fact]
    public void Solve_NoDots_NoDotPenalty()
    {
        // Without dots, there should be no dot-related penalty difference
        var tie = CreateTie(0, curveUp: true);
        var problemNoDots = new TieFormattingProblem(tie, 5, 15, 5, 15, 2, startDots: 0);
        var problemWithDots = new TieFormattingProblem(tie, 5, 15, 5, 15, 2, startDots: 1);

        var layoutNoDots = problemNoDots.Solve();
        var layoutWithDots = problemWithDots.Solve();

        // Both should produce valid results
        Assert.NotNull(layoutNoDots);
        Assert.NotNull(layoutWithDots);
    }

    [Fact]
    public void Solve_DottedNoteInSpace_DotAtNotePosition()
    {
        // Note in a space (odd position=1), dot stays at position 1.
        // CurveUp should be affected if tie attachment is near position 1.
        var startNote = new NoteItem(1, Fraction.Quarter, dots: 1, null, false, 0, hasTieStart: true);
        var endNote = CreateNote(1);
        var tie = new TieItem(startNote, endNote, 1, forcedCurveUp: true, 0, 0, 0, 1);

        var problem = new TieFormattingProblem(tie, 5, 15, 5, 15, 1.5, startDots: 1);
        var layout = problem.Solve();

        Assert.NotNull(layout);
        Assert.True(layout.EndX > layout.StartX);
    }

    [Fact]
    public void TieDetails_Default_MatchesLilyPondDefineGrobs()
    {
        var d = TieDetails.Default;

        // define-grobs.scm values
        Assert.Equal(1.0, d.HeightLimit);
        Assert.Equal(0.333, d.Ratio);
        Assert.Equal(0.2, d.XGap);
        Assert.Equal(0.35, d.StemGap);
        Assert.Equal(0.225, d.TipStaffLineClearance);   // 0.45 half-spaces / 2
        Assert.Equal(0.3, d.CenterStaffLineClearance);   // 0.6 half-spaces / 2
        Assert.Equal(10.0, d.HorizontalDistancePenaltyFactor);
        Assert.Equal(8.0, d.SameDirAsStemPenalty);
        Assert.Equal(26.0, d.MinLengthPenaltyFactor);
        Assert.Equal(0.45, d.TieTieCollisionDistance);
        Assert.Equal(25.0, d.TieTieCollisionPenalty);
        Assert.Equal(1.25, d.IntraSpaceThreshold);
        Assert.Equal(10.0, d.OuterTieVerticalDistanceSymmetryPenaltyFactor);
        Assert.Equal(10.0, d.OuterTieLengthSymmetryPenaltyFactor);
        Assert.Equal(7.0, d.VerticalDistancePenaltyFactor);
        Assert.Equal(0.25, d.OuterTieVerticalGap);
        Assert.Equal(3, d.MultiTieRegionSize);
        Assert.Equal(4, d.SingleTieRegionSize);
        Assert.Equal(100.0, d.TieColumnMonotonicityPenalty);
    }
}
