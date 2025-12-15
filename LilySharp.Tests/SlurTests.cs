using Xunit;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Semantics;

namespace LilySharp.Tests;

public class SlurTests
{
    private static NoteItem CreateNote(int staffPosition) 
        => new(staffPosition, Fraction.Quarter, 0, null, false, 0);
    
    [Fact]
    public void SlurEngraver_CalculatesSlurLayout()
    {
        // Arrange: Slur from C4 to G4
        var startNote = CreateNote(0);
        var endNote = CreateNote(5);
        var slur = new SlurItem(startNote, endNote, 0, 5, curveUp: true, 0, 0, 0, 3);
        
        var engraver = new SlurEngraver();
        
        // Act
        var layout = engraver.CalculateSlurLayout(
            slur,
            startX: 50.0,
            startY: 100.0,
            endX: 200.0,
            endY: 75.0);
        
        // Assert
        Assert.NotNull(layout);
        Assert.True(layout.StartX < layout.EndX, "Start X should be less than End X");
        Assert.True(layout.Control1.Y < layout.StartY, "Control point should be above for curve up");
    }
    
    [Fact]
    public void SlurEngraver_CurveDownHasLowerControlPoints()
    {
        // Arrange
        var startNote = CreateNote(6);
        var endNote = CreateNote(4);
        var slur = new SlurItem(startNote, endNote, 6, 4, curveUp: false, 0, 0, 0, 2);
        
        var engraver = new SlurEngraver();
        
        // Act
        var layout = engraver.CalculateSlurLayout(
            slur,
            startX: 50.0,
            startY: 80.0,
            endX: 150.0,
            endY: 90.0);
        
        // Assert
        double midY = (layout.StartY + layout.EndY) / 2;
        Assert.True(layout.Control1.Y > midY, "Control point should be below midpoint for curve down");
    }
    
    [Fact]
    public void SlurScoreParameters_HasReasonableDefaults()
    {
        var params_ = SlurScoreParameters.Default;
        
        Assert.True(params_.HeightLimit > 0);
        Assert.True(params_.Ratio > 0 && params_.Ratio < 1);
        Assert.True(params_.MaxSlope > 0);
        Assert.True(params_.RegionSize > 0);
    }
    
    [Fact]
    public void SlurItem_StoresCorrectProperties()
    {
        var startNote = CreateNote(0);
        var endNote = CreateNote(7);
        
        var slur = new SlurItem(
            startNote, endNote,
            startStaffPosition: 0,
            endStaffPosition: 7,
            curveUp: false,
            startMeasureIndex: 0,
            endMeasureIndex: 1,
            startItemIndex: 0,
            endItemIndex: 2);
        
        Assert.Equal(0, slur.StartStaffPosition);
        Assert.Equal(7, slur.EndStaffPosition);
        Assert.False(slur.CurveUp);
        Assert.Equal(0, slur.StartMeasureIndex);
        Assert.Equal(1, slur.EndMeasureIndex);
    }
}
