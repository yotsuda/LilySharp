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

using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Every note of a chord carries ITS OWN pitch source offset (not the chord's '&lt;'),
/// so the interactive preview highlights/selects one chord note at a time and jumps the
/// caret to that exact pitch. The offset must NOT, however, enter the incremental
/// content key — otherwise a leading edit that shifts every offset would defeat
/// per-system reuse (the regression these tests guard).
/// </summary>
[Trait("Category", "Unit")]
public class ChordNoteSourcePositionTests
{
    private static ChordItem FirstChord(string source)
    {
        var score = new MeasureCollector().Collect(SyntaxTree.Parse(source));
        foreach (var item in score.Voice.Measures[0].Items)
            if (item is ChordItem chord)
                return chord;
        throw new Xunit.Sdk.XunitException("no chord item collected");
    }

    [Fact]
    public void EachMember_PointsAtItsOwnPitchToken()
    {
        //          0123456789
        var source = "{ <c e g>4 }";
        var chord = FirstChord(source);

        Assert.Equal(3, chord.Notes.Length);
        // Distinct, ascending offsets — one per written pitch, not the shared '<'.
        Assert.Equal(3, chord.Notes[0].SourcePosition);
        Assert.Equal(5, chord.Notes[1].SourcePosition);
        Assert.Equal(7, chord.Notes[2].SourcePosition);
        // And each really lands on that pitch letter in the source.
        Assert.Equal('c', source[chord.Notes[0].SourcePosition]);
        Assert.Equal('e', source[chord.Notes[1].SourcePosition]);
        Assert.Equal('g', source[chord.Notes[2].SourcePosition]);
    }

    [Fact]
    public void DegreeChordMembers_PointAtTheirOwnDegreeToken()
    {
        //          0         1
        //          0123456789012345
        var source = "{ <c 3 5>4 }";
        var chord = FirstChord(source);

        Assert.Equal(3, chord.Notes.Length);
        Assert.Equal('c', source[chord.Notes[0].SourcePosition]);
        Assert.Equal('3', source[chord.Notes[1].SourcePosition]);
        Assert.Equal('5', source[chord.Notes[2].SourcePosition]);
    }

    [Fact]
    public void SourcePosition_DoesNotEnterContentKey()
    {
        // A leading newline shifts EVERY chord member's offset by one. The
        // position-independent content key must be unchanged so the per-system
        // cache still reuses the measure (the offset is a highlight anchor, not content).
        var a = new MeasureCollector().Collect(SyntaxTree.Parse("{ <c e g>4 }"));
        var b = new MeasureCollector().Collect(SyntaxTree.Parse("\n{ <c e g>4 }"));

        var keyA = MeasureContentKey.Compute(a);
        var keyB = MeasureContentKey.Compute(b);

        Assert.Equal(keyA, keyB);
    }

    [Fact]
    public void DifferentChordPitches_StillDifferInContentKey()
    {
        // Guard the other direction: normalizing the offset out must not blind the
        // key to a real pitch change (that would be a false-reuse / soundness break).
        var a = new MeasureCollector().Collect(SyntaxTree.Parse("{ <c e g>4 }"));
        var b = new MeasureCollector().Collect(SyntaxTree.Parse("{ <c e a>4 }"));

        Assert.NotEqual(MeasureContentKey.Compute(a), MeasureContentKey.Compute(b));
    }
}
