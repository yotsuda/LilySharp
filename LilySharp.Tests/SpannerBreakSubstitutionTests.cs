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

using System.Collections.Immutable;
using LilySharp.Core.Svg.Layout;
using Xunit;

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
public class SpannerBreakSubstitutionTests
{
    private static SystemLayout SystemWithMeasures(int systemIndex, params int[] measureIndices)
    {
        var measures = measureIndices
            .Select(mi => new MeasureLayout(mi, x: 0, width: 10, items: ImmutableArray<ItemLayout>.Empty))
            .ToImmutableArray();
        return new SystemLayout(
            SystemIndex: systemIndex,
            Y: 0,
            Width: 100,
            PrefixWidth: 5,
            Measures: measures);
    }

    [Fact]
    public void BuildMeasureToSystemMap_EmptySystems_ReturnsEmptyMap()
    {
        var map = SpannerBreakSubstitution.BuildMeasureToSystemMap(ImmutableArray<SystemLayout>.Empty);
        Assert.Empty(map);
    }

    [Fact]
    public void BuildMeasureToSystemMap_MultipleSystems_MapsEveryMeasure()
    {
        var systems = ImmutableArray.Create(
            SystemWithMeasures(0, 0, 1, 2),
            SystemWithMeasures(1, 3, 4, 5),
            SystemWithMeasures(2, 6, 7));

        var map = SpannerBreakSubstitution.BuildMeasureToSystemMap(systems);

        Assert.Equal(8, map.Count);
        Assert.Equal(0, map[0]);
        Assert.Equal(0, map[2]);
        Assert.Equal(1, map[3]);
        Assert.Equal(1, map[5]);
        Assert.Equal(2, map[6]);
        Assert.Equal(2, map[7]);
    }

    [Fact]
    public void Split_SingleSystemSpanner_ReturnsOneSegmentMarkedFirstAndLast()
    {
        var systems = ImmutableArray.Create(SystemWithMeasures(0, 0, 1, 2, 3));
        var map = SpannerBreakSubstitution.BuildMeasureToSystemMap(systems);

        var segments = SpannerBreakSubstitution.Split(
            spannerStartMeasure: 1, spannerEndMeasure: 2, systems, map);

        Assert.Single(segments);
        var seg = segments[0];
        Assert.Equal(0, seg.SystemIndex);
        Assert.Equal(1, seg.StartMeasureIndex);
        Assert.Equal(2, seg.EndMeasureIndex);
        Assert.True(seg.IsFirst);
        Assert.True(seg.IsLast);
        Assert.False(seg.IsMiddle);
    }

    [Fact]
    public void Split_TwoSystemSpanner_ReturnsTwoSegmentsWithCorrectFlagsAndBounds()
    {
        var systems = ImmutableArray.Create(
            SystemWithMeasures(0, 0, 1, 2),
            SystemWithMeasures(1, 3, 4, 5));
        var map = SpannerBreakSubstitution.BuildMeasureToSystemMap(systems);

        var segments = SpannerBreakSubstitution.Split(
            spannerStartMeasure: 1, spannerEndMeasure: 4, systems, map);

        Assert.Equal(2, segments.Length);

        Assert.Equal(0, segments[0].SystemIndex);
        Assert.Equal(1, segments[0].StartMeasureIndex);
        Assert.Equal(2, segments[0].EndMeasureIndex);
        Assert.True(segments[0].IsFirst);
        Assert.False(segments[0].IsLast);
        Assert.False(segments[0].IsMiddle);

        Assert.Equal(1, segments[1].SystemIndex);
        Assert.Equal(3, segments[1].StartMeasureIndex);
        Assert.Equal(4, segments[1].EndMeasureIndex);
        Assert.False(segments[1].IsFirst);
        Assert.True(segments[1].IsLast);
        Assert.False(segments[1].IsMiddle);
    }

    [Fact]
    public void Split_ThreeSystemSpanner_MarksMiddleSegment()
    {
        var systems = ImmutableArray.Create(
            SystemWithMeasures(0, 0, 1),
            SystemWithMeasures(1, 2, 3),
            SystemWithMeasures(2, 4, 5));
        var map = SpannerBreakSubstitution.BuildMeasureToSystemMap(systems);

        var segments = SpannerBreakSubstitution.Split(
            spannerStartMeasure: 1, spannerEndMeasure: 4, systems, map);

        Assert.Equal(3, segments.Length);

        Assert.True(segments[0].IsFirst);
        Assert.False(segments[0].IsLast);
        Assert.False(segments[0].IsMiddle);
        Assert.Equal(1, segments[0].StartMeasureIndex);
        Assert.Equal(1, segments[0].EndMeasureIndex);

        // Middle segment spans the full middle system.
        Assert.False(segments[1].IsFirst);
        Assert.False(segments[1].IsLast);
        Assert.True(segments[1].IsMiddle);
        Assert.Equal(2, segments[1].StartMeasureIndex);
        Assert.Equal(3, segments[1].EndMeasureIndex);

        Assert.False(segments[2].IsFirst);
        Assert.True(segments[2].IsLast);
        Assert.False(segments[2].IsMiddle);
        Assert.Equal(4, segments[2].StartMeasureIndex);
        Assert.Equal(4, segments[2].EndMeasureIndex);
    }

    [Fact]
    public void Split_StartEqualsEndMeasure_ReturnsSingleSegment()
    {
        var systems = ImmutableArray.Create(SystemWithMeasures(0, 0, 1, 2));
        var map = SpannerBreakSubstitution.BuildMeasureToSystemMap(systems);

        var segments = SpannerBreakSubstitution.Split(
            spannerStartMeasure: 1, spannerEndMeasure: 1, systems, map);

        Assert.Single(segments);
        Assert.Equal(1, segments[0].StartMeasureIndex);
        Assert.Equal(1, segments[0].EndMeasureIndex);
        Assert.True(segments[0].IsFirst);
        Assert.True(segments[0].IsLast);
    }

    [Fact]
    public void Split_StartMeasureMissingFromMap_ReturnsEmpty()
    {
        var systems = ImmutableArray.Create(SystemWithMeasures(0, 0, 1, 2));
        var map = SpannerBreakSubstitution.BuildMeasureToSystemMap(systems);

        var segments = SpannerBreakSubstitution.Split(
            spannerStartMeasure: 99, spannerEndMeasure: 1, systems, map);

        Assert.Empty(segments);
    }

    [Fact]
    public void Split_EndMeasureMissingFromMap_ReturnsEmpty()
    {
        var systems = ImmutableArray.Create(SystemWithMeasures(0, 0, 1, 2));
        var map = SpannerBreakSubstitution.BuildMeasureToSystemMap(systems);

        var segments = SpannerBreakSubstitution.Split(
            spannerStartMeasure: 0, spannerEndMeasure: 99, systems, map);

        Assert.Empty(segments);
    }

    [Fact]
    public void Split_EmptySystems_ReturnsEmpty()
    {
        var segments = SpannerBreakSubstitution.Split(
            spannerStartMeasure: 0,
            spannerEndMeasure: 1,
            ImmutableArray<SystemLayout>.Empty,
            new Dictionary<int, int>());

        Assert.Empty(segments);
    }

    [Fact]
    public void Split_SpannerEndsAtSystemBoundaryFirstMeasure_StillSplitsCorrectly()
    {
        // Spanner ending at the first measure of system 1 still produces 2 segments.
        var systems = ImmutableArray.Create(
            SystemWithMeasures(0, 0, 1, 2),
            SystemWithMeasures(1, 3, 4));
        var map = SpannerBreakSubstitution.BuildMeasureToSystemMap(systems);

        var segments = SpannerBreakSubstitution.Split(
            spannerStartMeasure: 0, spannerEndMeasure: 3, systems, map);

        Assert.Equal(2, segments.Length);
        Assert.Equal(0, segments[0].StartMeasureIndex);
        Assert.Equal(2, segments[0].EndMeasureIndex);
        Assert.Equal(3, segments[1].StartMeasureIndex);
        Assert.Equal(3, segments[1].EndMeasureIndex);
    }

    [Fact]
    public void Split_StartSysGreaterThanEndSys_ReturnsEmpty()
    {
        // Defensive: start measure mapped to later system than end measure (caller bug).
        var systems = ImmutableArray.Create(
            SystemWithMeasures(0, 0, 1),
            SystemWithMeasures(1, 2, 3));
        var map = SpannerBreakSubstitution.BuildMeasureToSystemMap(systems);

        var segments = SpannerBreakSubstitution.Split(
            spannerStartMeasure: 3, spannerEndMeasure: 0, systems, map);

        Assert.Empty(segments);
    }
}
