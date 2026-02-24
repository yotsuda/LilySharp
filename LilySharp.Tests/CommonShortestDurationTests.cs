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
using LilySharp.Core.Syntax;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;

namespace LilySharp.Tests;

/// <summary>
/// Tests for common-shortest-duration calculation and its effect on spacing (H-1).
/// LILYPOND-REF: lily/spacing-determine-shortest-duration-op.cc
/// </summary>
[Trait("Category", "Unit")]
public class CommonShortestDurationTests
{
    [Fact]
    public void QuarterNoteOnlyScore_ShortestIsQuarter()
    {
        // LILYPOND-REF: lily/spacing-determine-shortest-duration-op.cc
        // A score with only quarter notes should have common shortest = 0.25
        var source = "c4 d e f |";
        var tree = SyntaxTree.Parse(source);
        var score = new MeasureCollector().Collect(tree);

        double shortest = SpacingRules.CalculateCommonShortestDuration(score);

        Assert.Equal(0.25, shortest, 4);
    }

    [Fact]
    public void MixedDurations_ShortestIsSmallest()
    {
        // Score with half, quarter, and eighth notes → shortest is eighth (0.125)
        var source = "c2 d4 e8 f |";
        var tree = SyntaxTree.Parse(source);
        var score = new MeasureCollector().Collect(tree);

        double shortest = SpacingRules.CalculateCommonShortestDuration(score);

        Assert.Equal(0.125, shortest, 4);
    }

    [Fact]
    public void SixteenthNotes_ShortestIsSixteenth()
    {
        var source = "c16 d e f g a b c' d' e' f' g' a' b' c'' d'' |";
        var tree = SyntaxTree.Parse(source);
        var score = new MeasureCollector().Collect(tree);

        double shortest = SpacingRules.CalculateCommonShortestDuration(score);

        Assert.Equal(0.0625, shortest, 4);
    }

    [Fact]
    public void DurationSpaceChangesWithBaseShortestDuration()
    {
        // LILYPOND-REF: lily/spacing-options.cc:68-104 get_duration_space()
        // With base=1/4 (quarter), a quarter note gets ratio=1 → spaceFactor = 2.0
        // With base=1/8 (eighth), a quarter note gets ratio=2 → spaceFactor = 2.0 + log2(2) = 3.0
        var quarter = new Fraction(1, 4);

        double spaceWithQuarterBase = SpacingRules.CalculateDurationSpace(quarter, 0.25);
        double spaceWithEighthBase = SpacingRules.CalculateDurationSpace(quarter, 0.125);

        // Quarter base: (2.0 + log2(1)) * 1.2 = 2.0 * 1.2 = 2.4
        Assert.Equal(2.4, spaceWithQuarterBase, 2);
        // Eighth base: (2.0 + log2(2)) * 1.2 = 3.0 * 1.2 = 3.6
        Assert.Equal(3.6, spaceWithEighthBase, 2);

        // Quarter base should produce tighter spacing
        Assert.True(spaceWithQuarterBase < spaceWithEighthBase,
            "Quarter-base spacing should be tighter than eighth-base");
    }

    [Fact]
    public void SpringCreationUsesBaseShortestDuration()
    {
        // Verify that CreateSpring uses the provided baseShortestDuration
        var quarter = new Fraction(1, 4);

        var springDefault = SpacingRules.CreateSpring(null, null, quarter);
        var springQuarterBase = SpacingRules.CreateSpring(null, null, quarter,
            baseShortestDuration: 0.25);

        // Default uses BaseShortestDuration = 0.125, so ideal is larger
        Assert.True(springDefault.IdealDistance > springQuarterBase.IdealDistance,
            $"Default base (ideal={springDefault.IdealDistance:F2}) should produce wider spacing " +
            $"than quarter base (ideal={springQuarterBase.IdealDistance:F2})");
    }

    [Fact]
    public void TimingSpringUsesBaseShortestDuration()
    {
        // Verify that CreateTimingSpring uses the provided baseShortestDuration
        var quarter = new Fraction(1, 4);

        var springDefault = SpacingRules.CreateTimingSpring(quarter);
        var springQuarterBase = SpacingRules.CreateTimingSpring(quarter,
            baseShortestDuration: 0.25);

        Assert.True(springDefault.IdealDistance > springQuarterBase.IdealDistance,
            $"Default timing spring (ideal={springDefault.IdealDistance:F2}) should be wider " +
            $"than quarter-base (ideal={springQuarterBase.IdealDistance:F2})");
    }

    [Fact]
    public void MeasureIdealWidthAffectedByBaseShortestDuration()
    {
        // A measure's ideal width should differ based on the score's common shortest duration
        var source = "c4 d e f |";
        var tree = SyntaxTree.Parse(source);
        var score = new MeasureCollector().Collect(tree);
        var measure = score.Voice.Measures[0];

        double widthDefault = SpacingRules.CalculateMeasureIdealWidth(measure);
        double widthQuarterBase = SpacingRules.CalculateMeasureIdealWidth(measure,
            baseShortestDuration: 0.25);

        // With base=1/4, quarter notes are the shortest, so spacing is tighter
        Assert.True(widthDefault > widthQuarterBase,
            $"Default width ({widthDefault:F2}) should be wider than quarter-base ({widthQuarterBase:F2})");
    }
}
