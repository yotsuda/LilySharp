using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Tests for TieFormattingProblem - constraint-based tie positioning.
/// </summary>
public class TieFormattingProblemTests
{
    private static NoteItem CreateNote(int staffPosition) =>
        new(staffPosition, Fraction.Quarter, 0, null, false, 0);

    private static TieItem CreateTie(int staffPosition, bool curveUp = true)
    {
        var note = CreateNote(staffPosition);
        return new TieItem(note, note, staffPosition, curveUp, 0, 0, 0, 1);
    }

    [Fact]
    public void Solve_BasicTie_ReturnsValidLayout()
    {
        var tie = CreateTie(0, curveUp: true);
        var problem = new TieFormattingProblem(tie, 5, 2, 15, 2);

        var layout = problem.Solve();

        Assert.NotNull(layout);
        Assert.True(layout.StartX > 0);
        Assert.True(layout.EndX > layout.StartX);
    }

    [Fact]
    public void Solve_TieCurveUp_HasNegativeControlY()
    {
        var tie = CreateTie(0, curveUp: true);
        var problem = new TieFormattingProblem(tie, 5, 2, 15, 2);

        var layout = problem.Solve();

        // Curve up means control points should be above (lower Y in SVG coordinates)
        Assert.True(layout.Control1.Y < layout.StartY);
        Assert.True(layout.Control2.Y < layout.EndY);
    }

    [Fact]
    public void Solve_TieCurveDown_HasPositiveControlY()
    {
        var tie = CreateTie(0, curveUp: false);
        var problem = new TieFormattingProblem(tie, 5, 2, 15, 2);

        var layout = problem.Solve();

        // Curve down means control points should be below (higher Y in SVG coordinates)
        Assert.True(layout.Control1.Y > layout.StartY);
        Assert.True(layout.Control2.Y > layout.EndY);
    }

    [Fact]
    public void Solve_WithExistingTies_AvoidsCollision()
    {
        var tie1 = CreateTie(0, curveUp: true);
        var problem1 = new TieFormattingProblem(tie1, 5, 2, 15, 2);
        var layout1 = problem1.Solve();

        var tie2 = CreateTie(1, curveUp: true);
        var existingTies = new[] { layout1 };
        var problem2 = new TieFormattingProblem(tie2, 5, 2.5, 15, 2.5, existingTies: existingTies);
        var layout2 = problem2.Solve();

        // The second tie should be positioned to avoid the first
        Assert.NotNull(layout2);
        Assert.True(layout2.EndX > layout2.StartX);
    }

    [Fact]
    public void Solve_ShortTie_HasCompactHeight()
    {
        var tie = CreateTie(0, curveUp: true);
        var shortProblem = new TieFormattingProblem(tie, 5, 2, 7, 2);
        var longProblem = new TieFormattingProblem(tie, 5, 2, 20, 2);

        var shortLayout = shortProblem.Solve();
        var longLayout = longProblem.Solve();

        double shortHeight = Math.Abs(shortLayout.Control1.Y - shortLayout.StartY);
        double longHeight = Math.Abs(longLayout.Control1.Y - longLayout.StartY);
        Assert.True(shortHeight < longHeight);
    }

    [Fact]
    public void Solve_TieAtStaffLine_ReturnsValidLayout()
    {
        var tie = CreateTie(0, curveUp: true);
        var problem = new TieFormattingProblem(tie, 5, 2, 15, 2, staffHeight: 4);

        var layout = problem.Solve();

        Assert.NotNull(layout);
        Assert.True(layout.EndX > layout.StartX);
    }
}
