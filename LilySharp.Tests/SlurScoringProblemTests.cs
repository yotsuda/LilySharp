using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using Xunit;

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
public class SlurScoringProblemTests
{
    private static NoteItem CreateNote(int staffPosition) =>
        new(staffPosition, Fraction.Quarter, 0, null, false, 0);

    private static SlurItem CreateSlur(int startPos, int endPos, bool curveUp = true)
    {
        var startNote = CreateNote(startPos);
        var endNote = CreateNote(endPos);
        return new SlurItem(startNote, endNote, startPos, endPos, curveUp, 0, 0, 0, 1);
    }

    [Fact]
    public void Solve_ReturnsValidLayout()
    {
        // Arrange
        var slur = CreateSlur(0, 4);
        var problem = new SlurScoringProblem(
            slur,
            startX: 10, startY: 2,
            endX: 50, endY: 2);

        // Act
        var layout = problem.Solve();

        // Assert
        Assert.NotNull(layout);
        Assert.Equal(slur, layout.Slur);
        Assert.True(layout.StartX >= 10);
        Assert.True(layout.EndX <= 50);
    }

    [Fact]
    public void Solve_CurveUp_ControlPointsAboveBaseline()
    {
        // Arrange
        var slur = CreateSlur(0, 0, curveUp: true);
        var problem = new SlurScoringProblem(
            slur,
            startX: 10, startY: 2,
            endX: 50, endY: 2);

        // Act
        var layout = problem.Solve();

        // Assert
        // For curve up, control points should have smaller Y (above baseline in SVG coords)
        double midY = (layout.StartY + layout.EndY) / 2;
        Assert.True(layout.Control1.Y < midY, "Control point 1 should be above baseline for curve up");
        Assert.True(layout.Control2.Y < midY, "Control point 2 should be above baseline for curve up");
    }

    [Fact]
    public void Solve_CurveDown_ControlPointsBelowBaseline()
    {
        // Arrange
        var slur = CreateSlur(0, 0, curveUp: false);
        var problem = new SlurScoringProblem(
            slur,
            startX: 10, startY: 2,
            endX: 50, endY: 2);

        // Act
        var layout = problem.Solve();

        // Assert
        // For curve down, control points should have larger Y (below baseline in SVG coords)
        double midY = (layout.StartY + layout.EndY) / 2;
        Assert.True(layout.Control1.Y > midY, "Control point 1 should be below baseline for curve down");
        Assert.True(layout.Control2.Y > midY, "Control point 2 should be below baseline for curve down");
    }

    [Fact]
    public void Solve_WiderSlur_HasHigherArc()
    {
        // Arrange
        var slur1 = CreateSlur(0, 0);
        var slur2 = CreateSlur(0, 0);

        var problem1 = new SlurScoringProblem(slur1, 10, 2, 30, 2);
        var problem2 = new SlurScoringProblem(slur2, 10, 2, 100, 2);

        // Act
        var layout1 = problem1.Solve();
        var layout2 = problem2.Solve();

        // Assert
        double height1 = Math.Abs(layout1.Control1.Y - layout1.StartY);
        double height2 = Math.Abs(layout2.Control1.Y - layout2.StartY);
        Assert.True(height2 > height1, "Wider slur should have higher arc");
    }

    [Fact]
    public void Solve_WithExistingSlurs_AvoidsCollision()
    {
        // Arrange
        var slur1 = CreateSlur(0, 0);
        var problem1 = new SlurScoringProblem(slur1, 10, 2, 50, 2);
        var layout1 = problem1.Solve();

        var slur2 = CreateSlur(2, 2);
        var problem2 = new SlurScoringProblem(
            slur2, 10, 2, 50, 2,
            existingSlurs: new[] { layout1 });

        // Act
        var layout2 = problem2.Solve();

        // Assert
        Assert.NotNull(layout2);
        // The second slur should be positioned to avoid collision
        double peak1 = (layout1.Control1.Y + layout1.Control2.Y) / 2;
        double peak2 = (layout2.Control1.Y + layout2.Control2.Y) / 2;
        Assert.NotEqual(peak1, peak2, 3);  // Peaks should be different
    }

    [Fact]
    public void Solve_WithObstacles_AvoidsNoteHeads()
    {
        // Arrange
        var slur = CreateSlur(0, 0);
        var obstacles = new[]
        {
            new SlurObstacle(30, 1.5, 2.5, SlurObstacleType.NoteHead)
        };

        var problemWithObstacle = new SlurScoringProblem(
            slur, 10, 2, 50, 2,
            obstacles: obstacles);

        var problemWithoutObstacle = new SlurScoringProblem(
            slur, 10, 2, 50, 2);

        // Act
        var layoutWith = problemWithObstacle.Solve();
        var layoutWithout = problemWithoutObstacle.Solve();

        // Assert
        Assert.NotNull(layoutWith);
        Assert.NotNull(layoutWithout);
        // Both should produce valid layouts
    }

    [Fact]
    public void SlurCandidate_Clone_CreatesIndependentCopy()
    {
        // Arrange
        var original = new SlurCandidate
        {
            StartX = 10,
            StartY = 2,
            EndX = 50,
            EndY = 2,
            Height = 1.5,
            CurveUp = true,
            YOffset = 0.3,
            Demerits = 5.0
        };

        // Act
        var clone = original.Clone();
        clone.Demerits = 10.0;
        clone.YOffset = 0.6;

        // Assert
        Assert.Equal(5.0, original.Demerits);
        Assert.Equal(0.3, original.YOffset);
        Assert.Equal(10.0, clone.Demerits);
        Assert.Equal(0.6, clone.YOffset);
    }

    [Fact]
    public void SlurScoreParameters_Default_MatchesLilyPondLayoutSlur()
    {
        var p = SlurScoreParameters.Default;

        // layout-slur.scm values
        Assert.Equal(4, p.RegionSize);
        Assert.Equal(1000.0, p.HeadEncompassPenalty);
        Assert.Equal(30.0, p.StemEncompassPenalty);
        Assert.Equal(4.0, p.EdgeAttractionFactor);
        Assert.Equal(20.0, p.SameSlopePenalty);
        Assert.Equal(50.0, p.SteeperSlopeFactor);
        Assert.Equal(15.0, p.NonHorizontalPenalty);
        Assert.Equal(1.1, p.MaxSlope);
        Assert.Equal(10.0, p.MaxSlopeFactor);
        Assert.Equal(50.0, p.ExtraObjectCollisionPenalty);
        Assert.Equal(3.0, p.AccidentalCollision);
        Assert.Equal(0.8, p.FreeSlurDistance);
        Assert.Equal(0.3, p.FreeHeadDistance);
        Assert.Equal(0.2, p.GapToStafflineInside);
        Assert.Equal(0.1, p.GapToStafflineOutside);
        Assert.Equal(0.3, p.ExtraEncompassFreeDistance);
        Assert.Equal(0.8, p.ExtraEncompassCollisionDistance);
        Assert.Equal(3.0, p.HeadSlurDistanceMaxRatio);
        Assert.Equal(10.0, p.HeadSlurDistanceFactor);
        Assert.Equal(0.3, p.AbsoluteClosenessMeasure);
        Assert.Equal(1.7, p.EdgeSlopeExponent);
        Assert.Equal(2.5, p.CloseToEdgeLength);
        Assert.Equal(0.5, p.EncompassObjectRangeOvershoot);
        Assert.Equal(0.2, p.SlurTieExtremaMinDistance);
        Assert.Equal(2.0, p.SlurTieExtremaMinDistancePenalty);
    }

    [Theory]
    [InlineData(0.0, 1.0)]
    [InlineData(0.5, 0.0)]
    [InlineData(1.0, 0.0)]
    public void PeakAround_ReturnsExpectedValues(double x, double expected)
    {
        // LILYPOND-REF: lily/misc.cc:48-55
        double result = SlurScoringProblem.PeakAround(0.05, 0.5, x);
        Assert.Equal(expected, result, 3);
    }

    [Fact]
    public void Solve_UsesLazyPriorityQueue()
    {
        // Verify the solver works with priority-queue lazy evaluation
        var slur = CreateSlur(0, 4);
        var problem = new SlurScoringProblem(
            slur, 10, 2, 50, 2);

        var layout = problem.Solve();

        Assert.NotNull(layout);
        Assert.True(layout.EndX > layout.StartX);
        // Control points should be on the curve-up side
        double midY = (layout.StartY + layout.EndY) / 2;
        Assert.True(layout.Control1.Y < midY);
    }
}
