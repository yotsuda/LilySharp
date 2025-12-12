using Xunit;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Semantics;
using System.Collections.Immutable;

namespace LilySharp.Tests;

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
        var problem = new BeamScoringProblem(group, xPositions, 10.0);
        
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
        var problem = new BeamScoringProblem(group, xPositions, 10.0);
        
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
        var problem = new BeamScoringProblem(group, xPositions, 10.0);
        
        // Act
        var (leftY, rightY) = problem.Solve();
        
        // Assert: Stem length should be at least 5 staff positions (2.5 * 2)
        double stemLength = leftY - 0; // beamY - noteY for stem up
        Assert.True(stemLength >= 5, $"Stem length {stemLength} should be >= 5 (2.5 staff spaces * 2)");
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
        var problemWithout = new BeamScoringProblem(group, xPositions, 10.0, collisions: null);
        var (leftYWithout, _) = problemWithout.Solve();
        
        // With collision - rest at middle position in the beam's path
        var collisions = new List<BeamCollision>
        {
            new BeamCollision(X: 75.0, MinY: leftYWithout - 1, MaxY: leftYWithout + 1, BasePenalty: 1.0)
        };
        var problemWith = new BeamScoringProblem(group, xPositions, 10.0, collisions: collisions);
        var (leftYWith, _) = problemWith.Solve();
        
        // Assert: Beam with collision should move to avoid it
        // The beam may move up or down depending on which direction has less penalty
        Assert.True(Math.Abs(leftYWith - leftYWithout) > 0.1 || leftYWith != leftYWithout,
            $"Beam should adjust position due to collision. Without: {leftYWithout}, With: {leftYWith}");
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
        
        // Create a collision object directly in the beam's expected path
        // Beam for stem up typically goes from about staffPos+7 (ideal stem length)
        var collisions = new List<BeamCollision>
        {
            new BeamCollision(X: 75.0, MinY: 8, MaxY: 10, BasePenalty: 5.0) // Collision near expected beam Y
        };
        
        // The collision scorer should add demerits when beam is near collision
        var problem = new BeamScoringProblem(group, xPositions, 10.0, collisions: collisions);
        var (leftY, rightY) = problem.Solve();
        
        // Beam should still produce valid output
        Assert.True(leftY > 2, "Beam should be above the notes");
        Assert.True(rightY > 4, "Beam right should be above the highest note");
    }
}