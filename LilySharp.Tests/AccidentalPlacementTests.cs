using Xunit;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using System.Collections.Immutable;

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
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
        // Staff positions 0 and 8 are far apart (4 staff spaces)
        // Glyph Y-extents don't overlap → placed at same X distance
        var notes = ImmutableArray.Create(
            new ChordNoteInfo(0, "sharp", false),
            new ChordNoteInfo(8, "flat", false)
        );

        var layouts = placement.CalculatePositions(notes);

        Assert.Equal(2, layouts.Length);
        // Non-overlapping Y extents → both at rightmost position (different widths only)
        double xDiff = Math.Abs(layouts[0].XOffset - layouts[1].XOffset);
        Assert.True(xDiff < 0.5, $"Far apart accidentals should be in same column, got xDiff={xDiff}");
    }

    [Fact]
    public void TwoAccidentals_Close_DifferentColumns()
    {
        var placement = new AccidentalPlacement();
        // Staff positions 0 and 2 are close (1 staff space apart)
        // Glyph Y-extents overlap → second must shift left
        var notes = ImmutableArray.Create(
            new ChordNoteInfo(0, "sharp", false),
            new ChordNoteInfo(2, "flat", false)
        );

        var layouts = placement.CalculatePositions(notes);

        Assert.Equal(2, layouts.Length);
        double xDiff = Math.Abs(layouts[0].XOffset - layouts[1].XOffset);
        Assert.True(xDiff > 0.3, $"Close accidentals should be in different columns, got xDiff={xDiff}");
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

    // --- New tests for LilyPond-faithful algorithm ---

    [Fact]
    public void Parameters_Default_MatchesLilyPond()
    {
        var p = AccidentalPlacementParameters.Default;

        // LILYPOND-REF: accidental-placement.cc:398,505
        Assert.Equal(0.2, p.Padding);
        // LILYPOND-REF: define-grobs.scm:84
        Assert.Equal(0.15, p.RightPadding);
        // LILYPOND-REF: accidental-placement.cc:413
        Assert.Equal(0.1, p.HorizonPadding);
    }

    [Fact]
    public void AlterationPriority_NaturalClosestToNote()
    {
        var placement = new AccidentalPlacement();
        // Natural and sharp at same Y-overlap distance → natural should be closer to notes
        var notes = ImmutableArray.Create(
            new ChordNoteInfo(0, "natural", false),
            new ChordNoteInfo(2, "sharp", false)
        );

        var layouts = placement.CalculatePositions(notes);

        Assert.Equal(2, layouts.Length);
        var naturalLayout = layouts.First(l => l.Accidental == "natural");
        var sharpLayout = layouts.First(l => l.Accidental == "sharp");
        // Natural should be closer to note (less negative XOffset)
        Assert.True(naturalLayout.XOffset > sharpLayout.XOffset,
            $"Natural ({naturalLayout.XOffset:F3}) should be closer to notes than sharp ({sharpLayout.XOffset:F3})");
    }

    [Fact]
    public void GlyphExtent_CollisionDetection_UsesActualHeight()
    {
        var placement = new AccidentalPlacement();
        // Double-sharp is very short (height ~1.0 ss), so it can share column with
        // accidentals that are further apart than a sharp could
        // Sharp at pos 0: Y extent [-1.392, 1.4] → total height 2.792
        // DoubleSharp at pos 4: Y extent [1.5, 2.508] → does NOT overlap with sharp's [−1.392, 1.4]
        var notes = ImmutableArray.Create(
            new ChordNoteInfo(0, "sharp", false),
            new ChordNoteInfo(4, "doubleSharp", false)
        );

        var layouts = placement.CalculatePositions(notes);
        Assert.Equal(2, layouts.Length);

        // Position 4 → Y center = 2.0, doubleSharp bottom = 2.0 + (-0.5) = 1.5
        // Sharp top = 0 + 1.4 = 1.4, with horizon_padding 0.1: 1.5 - 0.1 = 1.4
        // Marginal: 1.4 < 1.4 is false → no collision → same column
        var sharpLayout = layouts.First(l => l.Accidental == "sharp");
        var dsLayout = layouts.First(l => l.Accidental == "doubleSharp");
        double xDiff = Math.Abs(sharpLayout.XOffset - dsLayout.XOffset);
        Assert.True(xDiff < 0.5, $"DoubleSharp at pos 4 should not collide with sharp at pos 0, xDiff={xDiff}");
    }

    [Fact]
    public void ThreeAccidentals_StackedCorrectly()
    {
        var placement = new AccidentalPlacement();
        // Three accidentals very close together: all overlap in Y → stacked in 3 columns
        var notes = ImmutableArray.Create(
            new ChordNoteInfo(-2, "sharp", false),
            new ChordNoteInfo(0, "flat", false),
            new ChordNoteInfo(2, "natural", false)
        );

        var layouts = placement.CalculatePositions(notes);
        Assert.Equal(3, layouts.Length);

        // All three should have distinct X offsets
        var offsets = layouts.Select(l => l.XOffset).OrderByDescending(x => x).ToList();
        Assert.True(offsets[0] > offsets[1], "First column should be different from second");
        Assert.True(offsets[1] > offsets[2], "Second column should be different from third");
    }

    [Fact]
    public void IsCourtesy_DefaultFalse()
    {
        var placement = new AccidentalPlacement();
        var notes = ImmutableArray.Create(
            new ChordNoteInfo(0, "sharp", false)
        );

        var layouts = placement.CalculatePositions(notes);
        Assert.False(layouts[0].IsCourtesy);
    }

    [Fact]
    public void SinglePosition_XOffset_MatchesExpectedValue()
    {
        var placement = new AccidentalPlacement();
        var note = new NoteItem(0, Core.Semantics.Fraction.Quarter, 0, "sharp", false, 0);

        var layout = placement.CalculateSinglePosition(note);

        Assert.NotNull(layout);
        // Sharp width from GlyphMetrics: 0.996, RightPadding: 0.15
        double expected = -(GlyphMetrics.AccidentalSharp.Width + 0.15);
        Assert.Equal(expected, layout.Value.XOffset, 3);
    }

    [Theory]
    [InlineData("sharp", "flat", "natural", "doubleSharp", "doubleFlat")]
    public void AllAccidentalTypes_ProduceValidLayout(
        string a1, string a2, string a3, string a4, string a5)
    {
        var placement = new AccidentalPlacement();
        var notes = ImmutableArray.Create(
            new ChordNoteInfo(-4, a1, false),
            new ChordNoteInfo(-2, a2, false),
            new ChordNoteInfo(0, a3, false),
            new ChordNoteInfo(2, a4, false),
            new ChordNoteInfo(4, a5, false)
        );

        var layouts = placement.CalculatePositions(notes);

        Assert.Equal(5, layouts.Length);
        foreach (var layout in layouts)
        {
            Assert.True(layout.XOffset < 0, $"Accidental {layout.Accidental} at pos {layout.StaffPosition} should be left of note");
        }
    }
}
