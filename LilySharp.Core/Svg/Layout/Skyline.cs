using System.Collections.Immutable;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Represents a horizontal skyline - the outline of a set of rectangles
/// as seen from the left (LeftSkyline) or right (RightSkyline).
/// </summary>
/// <remarks>
/// A skyline is represented as a series of Y-intervals, each with an X boundary.
/// For a RightSkyline (looking from the right), X is the rightmost edge at each Y.
/// For a LeftSkyline (looking from the left), X is the leftmost edge at each Y.
/// 
/// This is a simplified version of Lilypond's Skyline that uses only horizontal
/// segments (no slopes). This is sufficient for LilySharp because:
/// 1. Bar lines are explicit, so collision detection is within measures only
/// 2. Rectangular approximation is accurate enough for most music elements
/// </remarks>
public sealed class Skyline
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
        if (_segments.Length == 0 || other._segments.Length == 0)
            return double.PositiveInfinity;
        
        double minDistance = double.PositiveInfinity;
        
        // For each segment in this skyline, find overlapping segments in other
        foreach (var seg1 in _segments)
        {
            foreach (var seg2 in other._segments)
            {
                // Check if Y intervals overlap
                double overlapBottom = Math.Max(seg1.YBottom, seg2.YBottom);
                double overlapTop = Math.Min(seg1.YTop, seg2.YTop);
                
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
    
    public bool IsEmpty => _segments.Length == 0;
    public ImmutableArray<Segment> Segments => _segments;
}
