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
using LilySharp.Core.Svg.Layout;

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
public class NoteCollisionTests
{
    [Fact]
    public void NoCollision_FarApartNotes()
    {
        var collision = new NoteCollision();
        var ups = new[] { 8 };    // High note
        var downs = new[] { 0 };  // Low note

        var result = collision.AnalyzeCollision(ups, downs, upNoteValue: 4, downNoteValue: 4, upDots: 0, downDots: 0);

        Assert.Equal(CollisionType.None, result.Type);
        Assert.Equal(0, result.UpStemXOffset);
        Assert.Equal(0, result.DownStemXOffset);
    }

    [Fact]
    public void MergeCollision_SamePositionSameNoteValue()
    {
        var collision = new NoteCollision();
        var ups = new[] { 4 };
        var downs = new[] { 4 };

        // Same position with same note value can be merged
        var result = collision.AnalyzeCollision(ups, downs, upNoteValue: 4, downNoteValue: 4, upDots: 0, downDots: 0);

        Assert.Equal(CollisionType.Merge, result.Type);
        Assert.True(result.ShouldMerge);
    }

    [Fact]
    public void FullCollision_SamePosition_DifferentDots()
    {
        var collision = new NoteCollision();
        var ups = new[] { 4 };
        var downs = new[] { 4 };

        // Same position but different dots - cannot merge by default
        var result = collision.AnalyzeCollision(ups, downs, upNoteValue: 4, downNoteValue: 4, upDots: 1, downDots: 0);

        Assert.Equal(CollisionType.Full, result.Type);
        Assert.False(result.ShouldMerge);
    }

    [Fact]
    public void FullCollision_SamePosition_DifferentNoteValues()
    {
        var collision = new NoteCollision();
        var ups = new[] { 4 };
        var downs = new[] { 4 };

        // Same position but different note values (half vs quarter) - cannot merge
        var result = collision.AnalyzeCollision(ups, downs, upNoteValue: 2, downNoteValue: 4, upDots: 0, downDots: 0);

        Assert.Equal(CollisionType.Full, result.Type);
        Assert.False(result.ShouldMerge);
    }

    [Fact]
    public void CloseHalfCollision_AdjacentPositions()
    {
        var collision = new NoteCollision();
        var ups = new[] { 5 };    // One position above
        var downs = new[] { 4 };  // One position below

        var result = collision.AnalyzeCollision(ups, downs, upNoteValue: 4, downNoteValue: 4, upDots: 0, downDots: 0);

        // Adjacent positions should cause collision
        Assert.True(result.Type == CollisionType.CloseHalf || result.Type == CollisionType.Full,
            $"Expected CloseHalf or Full, got {result.Type}");
    }

    [Fact]
    public void ChordCollision_MultipleNotes_WithOverlap()
    {
        var collision = new NoteCollision();
        var ups = new[] { 4, 6, 8 };     // Position 4 overlaps
        var downs = new[] { 0, 2, 4 };   // Position 4 overlaps

        var result = collision.AnalyzeCollision(ups, downs, upNoteValue: 4, downNoteValue: 4, upDots: 0, downDots: 0);

        // Has overlapping position - should trigger collision handling
        Assert.NotEqual(CollisionType.None, result.Type);
    }

    [Fact]
    public void ChordCollision_NoOverlap()
    {
        var collision = new NoteCollision();
        var ups = new[] { 6, 8, 10 };    // High chord
        var downs = new[] { 0, 2, 4 };   // Low chord, doesn't touch

        var result = collision.AnalyzeCollision(ups, downs, upNoteValue: 4, downNoteValue: 4, upDots: 0, downDots: 0);

        Assert.Equal(CollisionType.None, result.Type);
    }
}
