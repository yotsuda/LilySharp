using Xunit;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using System.Collections.Immutable;

namespace LilySharp.Tests;

public class AccidentalPlacementTests
{
    [Fact]
    public void SingleAccidental_PositionedLeftOfNote()
    {
        var placement = new AccidentalPlacement();
        var notes = ImmutableArray.Create(
            new ChordNoteInfo(0, "sharp", false)
        );
        
        var layouts = placement.CalculatePositions(notes);
        
        Assert.Single(layouts);
        Assert.True(layouts[0].XOffset < 0, "Accidental should be left of note");
    }
    
    [Fact]
    public void TwoAccidentals_FarApart_SameColumn()
    {
        var placement = new AccidentalPlacement();
        // Staff positions 0 and 8 are far apart (8 positions = 4 lines)
        var notes = ImmutableArray.Create(
            new ChordNoteInfo(0, "sharp", false),
            new ChordNoteInfo(8, "flat", false)
        );
        
        var layouts = placement.CalculatePositions(notes);
        
        Assert.Equal(2, layouts.Length);
        // Both should have similar X offset (same column)
        // In staff spaces, width is ~0.5, so column difference is small
        double xDiff = Math.Abs(layouts[0].XOffset - layouts[1].XOffset);
        Assert.True(xDiff < 0.5, "Far apart accidentals should be in same column");
    }
    
    [Fact]
    public void TwoAccidentals_Close_DifferentColumns()
    {
        var placement = new AccidentalPlacement();
        // Staff positions 0 and 2 are close (within threshold of 6)
        var notes = ImmutableArray.Create(
            new ChordNoteInfo(0, "sharp", false),
            new ChordNoteInfo(2, "flat", false)
        );
        
        var layouts = placement.CalculatePositions(notes);
        
        Assert.Equal(2, layouts.Length);
        // Should have different X offsets (different columns)
        // In staff spaces, column separation is at least accidental width
        double xDiff = Math.Abs(layouts[0].XOffset - layouts[1].XOffset);
        Assert.True(xDiff > 0.3, "Close accidentals should be in different columns");
    }
    
    [Fact]
    public void NoAccidentals_ReturnsEmpty()
    {
        var placement = new AccidentalPlacement();
        var notes = ImmutableArray.Create(
            new ChordNoteInfo(0, null, false),
            new ChordNoteInfo(4, null, false)
        );
        
        var layouts = placement.CalculatePositions(notes);
        
        Assert.Empty(layouts);
    }
    
    [Fact]
    public void CalculateSinglePosition_ForNote()
    {
        var placement = new AccidentalPlacement();
        var note = new NoteItem(0, Core.Semantics.Fraction.Quarter, 0, "flat", false, 0);
        
        var layout = placement.CalculateSinglePosition(note);
        
        Assert.NotNull(layout);
        Assert.Equal(0, layout.Value.StaffPosition);
        Assert.Equal("flat", layout.Value.Accidental);
        Assert.True(layout.Value.XOffset < 0);
    }
    
    [Fact]
    public void CalculateSinglePosition_NoAccidental_ReturnsNull()
    {
        var placement = new AccidentalPlacement();
        var note = new NoteItem(0, Core.Semantics.Fraction.Quarter, 0, null, false, 0);
        
        var layout = placement.CalculateSinglePosition(note);
        
        Assert.Null(layout);
    }
}
