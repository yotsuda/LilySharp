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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Layout;

namespace LilySharp.Tests;

/// <summary>
/// Tests for strict-note-spacing mode (H-2).
/// LILYPOND-REF: lily/note-spacing.cc:229-264 strict_note_spacing
/// </summary>
[Trait("Category", "Unit")]
public class StrictNoteSpacingTests
{
    [Fact]
    public void DefaultParameters_StrictModeDisabled()
    {
        var p = NoteSpacingParameters.Default;
        Assert.False(p.StrictNoteSpacing);
    }

    [Fact]
    public void BaseNoteSpace_MatchesLilyPondDefault()
    {
        // LILYPOND-REF: scm/define-grobs.scm SpacingSpanner
        // BaseNoteSpace = ShortestDurationSpace * SpacingIncrement = 2.0 * 1.2 = 2.4
        var p = NoteSpacingParameters.Default;
        Assert.Equal(2.4, p.BaseNoteSpace, 2);
    }

    [Fact]
    public void StrictMode_EnforcesMinDistanceEqualToIdeal()
    {
        // LILYPOND-REF: lily/note-spacing.cc:229-264
        // In strict mode, minDistance should be at least idealDistance
        var strictParams = new NoteSpacingParameters { StrictNoteSpacing = true };
        var quarter = new Fraction(1, 4);

        var spring = SpacingRules.CreateSpring(null, null, quarter, noteParams: strictParams);

        // In strict mode, min >= ideal
        Assert.True(spring.MinDistance >= spring.IdealDistance,
            $"Strict mode: MinDistance ({spring.MinDistance:F2}) should be >= IdealDistance ({spring.IdealDistance:F2})");
    }

    [Fact]
    public void NormalMode_MinDistanceCanBeLessThanIdeal()
    {
        var normalParams = new NoteSpacingParameters { StrictNoteSpacing = false };
        var quarter = new Fraction(1, 4);

        var spring = SpacingRules.CreateSpring(null, null, quarter, noteParams: normalParams);

        // In normal mode, min < ideal (collision-based min is typically smaller than duration-based ideal)
        Assert.True(spring.MinDistance < spring.IdealDistance,
            $"Normal mode: MinDistance ({spring.MinDistance:F2}) should be < IdealDistance ({spring.IdealDistance:F2})");
    }

    [Fact]
    public void StrictTimingSpring_EnforcesMinDistanceEqualToIdeal()
    {
        // CreateTimingSpring with strict mode
        var strictParams = new NoteSpacingParameters { StrictNoteSpacing = true };
        var quarter = new Fraction(1, 4);

        var spring = SpacingRules.CreateTimingSpring(quarter, noteParams: strictParams);

        Assert.True(spring.MinDistance >= spring.IdealDistance,
            $"Strict timing spring: MinDistance ({spring.MinDistance:F2}) should be >= IdealDistance ({spring.IdealDistance:F2})");
    }

    [Fact]
    public void StrictMode_ShortDurationStillGetsMinimumSpace()
    {
        // Even very short durations should get at least SpacingIncrement in strict mode
        var strictParams = new NoteSpacingParameters { StrictNoteSpacing = true };
        var sixteenth = new Fraction(1, 16);

        var spring = SpacingRules.CreateTimingSpring(sixteenth, noteParams: strictParams);

        Assert.True(spring.MinDistance >= spring.IdealDistance,
            $"Strict mode 16th note: min ({spring.MinDistance:F2}) >= ideal ({spring.IdealDistance:F2})");
        Assert.True(spring.MinDistance >= EngravingDefaults.SpacingIncrement,
            $"Min ({spring.MinDistance:F2}) should be >= SpacingIncrement ({EngravingDefaults.SpacingIncrement})");
    }
}
