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

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Represents a horizontal skyline - the outline of a set of rectangles
/// as seen from the left (LeftSkyline) or right (RightSkyline).
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/skyline.cc — Skyline class (simplified version)
///
/// A skyline is represented as a series of Y-intervals, each with an X boundary.
/// For a RightSkyline (looking from the right), X is the rightmost edge at each Y.
/// For a LeftSkyline (looking from the left), X is the leftmost edge at each Y.
///
/// This is a simplified version of LilyPond's Skyline that uses only horizontal
/// segments (no slopes). See HorizontalSkyline.cs and VerticalSkyline.cs for the
/// slope-intercept implementations closer to LilyPond's Building model.
///
/// NOTE: LilyPond uses a slope-intercept Building model (skyline.cc:32-46)
/// with O(n+m) plane-sweep merge and 45° sloped padding.
/// This class uses flat segments with O(n log n) sort-based merge — adequate for
/// rectangular bounding box collision within measures.
///
/// IMPLEMENTED — horizon_padding parameter (skyline.cc:530-554) on VerticalSkyline
/// IMPLEMENTED — padded() with 45° sloped edges (skyline.cc:558-615) on VerticalSkyline
/// This simplified Skyline class supports horizon_padding via Y-range extension.
/// </remarks>
internal sealed class Skyline
{
    /// <summary>
    /// A segment of the skyline: a Y-interval with an X boundary.
    /// </summary>
    public readonly record struct Segment(double YBottom, double YTop, double X);

    private readonly ImmutableArray<Segment> _segments;
    private readonly Direction _direction;

    public enum Direction { Left, Right }

    /// <summary>
    /// Creates an empty skyline.
    /// </summary>
    public Skyline(Direction direction)
    {
        _segments = ImmutableArray<Segment>.Empty;
        _direction = direction;
    }

    /// <summary>
    /// Creates a skyline from a list of segments.
    /// </summary>
    private Skyline(ImmutableArray<Segment> segments, Direction direction)
    {
        _segments = segments;
        _direction = direction;
    }

    /// <summary>
    /// Creates a skyline from a single bounding box.
    /// </summary>
    /// <param name="yBottom">Bottom Y coordinate</param>
    /// <param name="yTop">Top Y coordinate</param>
    /// <param name="xLeft">Left X coordinate</param>
    /// <param name="xRight">Right X coordinate</param>
    /// <param name="direction">Which side of the box to use</param>
    public static Skyline FromBox(double yBottom, double yTop, double xLeft, double xRight, Direction direction)
    {
        double x = direction == Direction.Right ? xRight : xLeft;
        var segment = new Segment(yBottom, yTop, x);
        return new Skyline(ImmutableArray.Create(segment), direction);
    }

    /// <summary>
    /// Creates a skyline from multiple bounding boxes (e.g., notehead + flag).
    /// </summary>
    public static Skyline FromBoxes(IEnumerable<(double YBottom, double YTop, double XLeft, double XRight)> boxes, Direction direction)
    {
        var segments = new List<Segment>();

        foreach (var (yBottom, yTop, xLeft, xRight) in boxes)
        {
            double x = direction == Direction.Right ? xRight : xLeft;
            segments.Add(new Segment(yBottom, yTop, x));
        }

        if (segments.Count == 0)
            return new Skyline(direction);

        // Merge overlapping segments
        var merged = MergeSegments(segments, direction);
        return new Skyline(merged.ToImmutableArray(), direction);
    }

    // LILYPOND-REF: lily/skyline.cc:529-533 Skyline::distance()
    /// <summary>
    /// Calculates the minimum distance between this skyline and another.
    /// This skyline should be a RightSkyline, other should be a LeftSkyline.
    /// </summary>
    /// <returns>
    /// The minimum horizontal distance. Positive means no overlap,
    /// negative means overlap (collision).
    /// </returns>
    public double Distance(Skyline other)
    {
        return Distance(other, 0.0);
    }

    /// <summary>
    /// Calculates the minimum distance with horizon_padding.
    /// Horizon padding extends each segment's Y range by the padding amount,
    /// so that nearby objects in the Y direction also affect the distance.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/skyline.cc:530-554 Skyline::distance(other, horizon_padding)
    /// In this simplified flat-segment model, horizon_padding is applied by
    /// extending Y ranges rather than creating 45° slopes.
    /// </remarks>
    public double Distance(Skyline other, double horizonPadding)
    {
        if (_segments.Length == 0 || other._segments.Length == 0)
            return double.PositiveInfinity;

        double minDistance = double.PositiveInfinity;

        // For each segment in this skyline, find overlapping segments in other
        foreach (var seg1 in _segments)
        {
            foreach (var seg2 in other._segments)
            {
                // Check if Y intervals overlap (with horizon padding)
                double overlapBottom = Math.Max(seg1.YBottom - horizonPadding, seg2.YBottom - horizonPadding);
                double overlapTop = Math.Min(seg1.YTop + horizonPadding, seg2.YTop + horizonPadding);

                if (overlapBottom < overlapTop)
                {
                    // Y intervals overlap, calculate X distance
                    // For Right-to-Left distance: other.X - this.X
                    double distance = seg2.X - seg1.X;
                    minDistance = Math.Min(minDistance, distance);
                }
            }
        }

        return minDistance;
    }

    /// <summary>
    /// Shifts the skyline horizontally.
    /// </summary>
    public Skyline Shift(double dx)
    {
        if (_segments.Length == 0 || dx == 0)
            return this;

        var shifted = _segments.Select(s => new Segment(s.YBottom, s.YTop, s.X + dx));
        return new Skyline(shifted.ToImmutableArray(), _direction);
    }

    /// <summary>
    /// Merges this skyline with another of the same direction.
    /// </summary>
    public Skyline Merge(Skyline other)
    {
        if (other._segments.Length == 0)
            return this;
        if (_segments.Length == 0)
            return other;

        var allSegments = _segments.Concat(other._segments).ToList();
        var merged = MergeSegments(allSegments, _direction);
        return new Skyline(merged.ToImmutableArray(), _direction);
    }

    /// <summary>
    /// Merges overlapping segments, keeping the outermost X for each Y interval.
    /// </summary>
    private static List<Segment> MergeSegments(List<Segment> segments, Direction direction)
    {
        if (segments.Count <= 1)
            return segments;

        // Sort by YBottom
        segments.Sort((a, b) => a.YBottom.CompareTo(b.YBottom));

        var result = new List<Segment>();
        var current = segments[0];

        for (int i = 1; i < segments.Count; i++)
        {
            var next = segments[i];

            if (next.YBottom <= current.YTop)
            {
                // Overlapping - merge
                double x = direction == Direction.Right
                    ? Math.Max(current.X, next.X)
                    : Math.Min(current.X, next.X);
                current = new Segment(
                    Math.Min(current.YBottom, next.YBottom),
                    Math.Max(current.YTop, next.YTop),
                    x);
            }
            else
            {
                // Non-overlapping - add current and start new
                result.Add(current);
                current = next;
            }
        }

        result.Add(current);
        return result;
    }

    /// <summary>
    /// Queries the extreme X value within a Y range.
    /// For LeftSkyline: returns the minimum (leftmost) X.
    /// For RightSkyline: returns the maximum (rightmost) X.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/skyline.cc — used in accidental-placement for collision queries
    /// </remarks>
    public double QueryXInRange(double yBottom, double yTop)
    {
        double result = _direction == Direction.Right
            ? double.NegativeInfinity
            : double.PositiveInfinity;

        foreach (var seg in _segments)
        {
            if (seg.YBottom < yTop && yBottom < seg.YTop)
            {
                result = _direction == Direction.Right
                    ? Math.Max(result, seg.X)
                    : Math.Min(result, seg.X);
            }
        }

        return result;
    }

    public bool IsEmpty => _segments.Length == 0;
    public ImmutableArray<Segment> Segments => _segments;
}
