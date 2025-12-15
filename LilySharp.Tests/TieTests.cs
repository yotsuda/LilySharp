using Xunit;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Semantics;

namespace LilySharp.Tests;

public class TieTests
{
    private static NoteItem CreateNote(int staffPosition) 
        => new(staffPosition, Fraction.Quarter, 0, null, false, 0);
    
    [Fact]
    public void TieEngraver_CalculatesTieLayout()
    {
        // Arrange
        var startNote = CreateNote(0);
        var endNote = CreateNote(0);
        var tie = new TieItem(startNote, endNote, 0, curveUp: true, 0, 0, 0, 1);
        
        var engraver = new TieEngraver();
        
        // Act
        var layout = engraver.CalculateTieLayout(
            tie,
            startX: 50.0,
            startY: 100.0,
            endX: 150.0,
            endY: 100.0);
        
        // Assert
        Assert.NotNull(layout);
        Assert.True(layout.StartX < layout.EndX, "Start X should be less than End X");
        Assert.True(layout.Control1.Y < layout.StartY, "Control point should be above for curve up");
        Assert.True(layout.Control2.Y < layout.EndY, "Control point should be above for curve up");
    }
    
    [Fact]
    public void TieEngraver_CurveDownHasLowerControlPoints()
    {
        // Arrange
        var startNote = CreateNote(4);
        var endNote = CreateNote(4);
        var tie = new TieItem(startNote, endNote, 4, curveUp: false, 0, 0, 0, 1);
        
        var engraver = new TieEngraver();
        
        // Act
        var layout = engraver.CalculateTieLayout(
            tie,
            startX: 50.0,
            startY: 100.0,
            endX: 150.0,
            endY: 100.0);
        
        // Assert
        Assert.True(layout.Control1.Y > layout.StartY, "Control point should be below for curve down");
        Assert.True(layout.Control2.Y > layout.EndY, "Control point should be below for curve down");
    }
    
    [Fact]
    public void TieDetails_HasReasonableDefaults()
    {
        var details = TieDetails.Default;
        
        Assert.True(details.HeightLimit > 0);
        Assert.True(details.Ratio > 0 && details.Ratio < 1);
        Assert.True(details.MinLength > 0);
        Assert.True(details.XGap > 0);
    }
    
    [Fact]
    public void TieItem_StoresCorrectProperties()
    {
        var startNote = CreateNote(2);
        var endNote = CreateNote(2);
        
        var tie = new TieItem(
            startNote, endNote,
            staffPosition: 2,
            curveUp: true,
            startMeasureIndex: 0,
            endMeasureIndex: 1,
            startItemIndex: 3,
            endItemIndex: 0);
        
        Assert.Equal(2, tie.StaffPosition);
        Assert.True(tie.CurveUp);
        Assert.Equal(0, tie.StartMeasureIndex);
        Assert.Equal(1, tie.EndMeasureIndex);
        Assert.Equal(3, tie.StartItemIndex);
        Assert.Equal(0, tie.EndItemIndex);
    }
}
