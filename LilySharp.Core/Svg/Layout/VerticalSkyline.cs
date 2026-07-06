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

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// A building in a vertical skyline - represents a region with X extent and sloped roof.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/skyline.cc:32-46, 98-176 Building struct
///
/// A building is defined by:
/// - X range: [XLeft, XRight]
/// - Roof line: y = Slope * x + YIntercept
///
/// The height at any x is: Height(x) = Slope * x + YIntercept
/// For horizontal buildings (most common), Slope = 0.
/// </remarks>
internal readonly struct Building
{
    public double XLeft { get; }
    public double XRight { get; }
    public double Slope { get; }
    public double YIntercept { get; }

    /// <summary>
    /// Creates a sloped building.
    /// </summary>
    /// <param name="xLeft">Left X coordinate</param>
    /// <param name="startHeight">Height at xLeft</param>
    /// <param name="endHeight">Height at xRight</param>
    /// <param name="xRight">Right X coordinate</param>
    /// <remarks>LILYPOND-REF: lily/skyline.cc:98-105</remarks>
    public Building(double xLeft, double startHeight, double endHeight, double xRight)
    {
        XLeft = xLeft;
        XRight = xRight;

        // LILYPOND-REF: lily/skyline.cc:116-138 precompute()
        if (double.IsInfinity(xLeft) || double.IsInfinity(xRight))
        {
            // Infinite buildings must have constant height
            Slope = 0;
            YIntercept = startHeight;
        }
        else if (Math.Abs(startHeight - endHeight) < 1e-10)
        {
            // Horizontal building
            Slope = 0;
            YIntercept = startHeight;
        }
        else
        {
            double length = xRight - xLeft;
            if (Math.Abs(length) < 1e-10)
            {
                Slope = 0;
                YIntercept = Math.Max(startHeight, endHeight);
            }
            else
            {
                Slope = (endHeight - startHeight) / length;

                // Too steep - treat as horizontal at max height
                if (Math.Abs(Slope) > 1e6)
                {
                    Slope = 0;
                    YIntercept = Math.Max(startHeight, endHeight);
                }
                else
                {
                    YIntercept = startHeight - Slope * xLeft;
                }
            }
        }
    }

    /// <summary>
    /// Creates a horizontal building (constant height).
    /// </summary>
    public Building(double xLeft, double xRight, double height)
        : this(xLeft, height, height, xRight)
    {
    }

    /// <summary>
    /// Creates a building from a bounding box.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/skyline.cc:107-113
    /// LilyPond convention: height = sky * y_coordinate
    /// - UP skyline (sky=-1): height = -yTop (negative for higher positions)
    /// - DOWN skyline (sky=+1): height = +yBottom (positive for lower positions)
    /// This convention makes distance calculation simple: dist = h1 + h2
    /// </remarks>
    public static Building FromBox(double xLeft, double xRight, double yBottom, double yTop, VerticalDirection direction)
    {
        // LilyPond: height = sky * b[other_axis][sky]
        // sky = -1 for UP, +1 for DOWN
        double height = direction == VerticalDirection.Up ? -yTop : yBottom;
        return new Building(xLeft, xRight, height);
    }

    /// <summary>
    /// Returns the height of the building at the given x coordinate.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/skyline.cc:140-144 Building::height()</remarks>
    public double Height(double x)
    {
        return double.IsInfinity(x) ? YIntercept : Slope * x + YIntercept;
    }

    /// <summary>
    /// Computes the x coordinate where this building intersects with another.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/skyline.cc:157-167 Building::intersection_x()</remarks>
    public double IntersectionX(Building other)
    {
        double slopeDelta = other.Slope - Slope;

        // If slopes are very close, avoid division by small number
        if (Math.Abs(slopeDelta) < 1e-4)
            return Math.Max(XLeft, other.XLeft);

        return (YIntercept - other.YIntercept) / slopeDelta;
    }

    /// <summary>
    /// Returns true if this building is above the other at the given x coordinate.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/skyline.cc:169-176 Building::above()</remarks>
    public bool Above(Building other, double x)
    {
        if (double.IsInfinity(YIntercept) || double.IsInfinity(other.YIntercept) || double.IsInfinity(x))
            return YIntercept > other.YIntercept;

        return (Slope - other.Slope) * x + YIntercept > other.YIntercept;
    }

    /// <summary>
    /// Creates a copy with modified X range.
    /// </summary>
    public Building WithXRange(double newLeft, double newRight)
    {
        // Recalculate heights at new boundaries
        double startHeight = Height(newLeft);
        double endHeight = Height(newRight);
        return new Building(newLeft, startHeight, endHeight, newRight);
    }

    public override string ToString()
        => $"Building[{XLeft:F2}, {XRight:F2}] y = {Slope:F4}x + {YIntercept:F2}";
}

/// <summary>
/// Direction for vertical skylines.
/// </summary>
/// <remarks>
/// LILYPOND-REF: Uses sky = -1 for UP, +1 for DOWN
/// This matches LilyPond's convention where heights are stored as sky * y.
/// </remarks>
public enum VerticalDirection
{
    Up = -1,
    Down = 1
}

/// <summary>
/// A vertical skyline - the outline of a set of buildings as seen from above (UP) or below (DOWN).
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/include/skyline.hh:48-100 Skyline class
/// LILYPOND-REF: lily/skyline.cc:1-700 Skyline implementation
///
/// A skyline is a sequence of non-overlapping buildings covering [-∞, +∞].
/// For UP skylines, we keep the highest roof at each x.
/// For DOWN skylines, we keep the lowest floor at each x.
/// </remarks>
internal sealed class VerticalSkyline
{
    private readonly List<Building> _buildings;
    private readonly VerticalDirection _direction;

    private const double NegativeInfinity = double.NegativeInfinity;
    private const double PositiveInfinity = double.PositiveInfinity;

    public VerticalSkyline(VerticalDirection direction)
    {
        _buildings = new List<Building>();
        _direction = direction;
    }

    private VerticalSkyline(List<Building> buildings, VerticalDirection direction)
    {
        _buildings = buildings;
        _direction = direction;
    }

    public VerticalDirection Direction => _direction;
    public bool IsEmpty => _buildings.Count == 0;
    public IReadOnlyList<Building> Buildings => _buildings;

    /// <summary>
    /// Creates a skyline from a single bounding box.
    /// </summary>
    public static VerticalSkyline FromBox(double xLeft, double xRight, double yBottom, double yTop, VerticalDirection direction)
    {
        var building = Building.FromBox(xLeft, xRight, yBottom, yTop, direction);
        var skyline = new VerticalSkyline(direction);
        skyline.AddBuilding(building);
        return skyline;
    }

    /// <summary>
    /// Creates a skyline from a sloped region (e.g., beam).
    /// </summary>
    /// <remarks>
    /// Uses LilyPond sign convention: UP heights are negative, DOWN heights are positive.
    /// </remarks>
    public static VerticalSkyline FromSlope(double xLeft, double yLeft, double xRight, double yRight,
        double thickness, VerticalDirection direction)
    {
        if (direction == VerticalDirection.Up)
        {
            // UP skyline: top edge with negative heights (LilyPond convention)
            return new VerticalSkyline(new List<Building>
            {
                new Building(xLeft, -yLeft, -yRight, xRight)
            }, direction);
        }
        else
        {
            // DOWN skyline: bottom edge with positive heights (LilyPond convention)
            double bottomLeft = yLeft + thickness;
            double bottomRight = yRight + thickness;
            return new VerticalSkyline(new List<Building>
            {
                new Building(xLeft, bottomLeft, bottomRight, xRight)
            }, direction);
        }
    }

    /// <summary>
    /// Adds a building to the skyline, merging as necessary.
    /// </summary>
    private void AddBuilding(Building b)
    {
        if (_buildings.Count == 0)
        {
            // Initialize with empty regions on both sides
            if (b.XLeft > NegativeInfinity)
                _buildings.Add(new Building(NegativeInfinity, NegativeInfinity, NegativeInfinity, b.XLeft));
            _buildings.Add(b);
            if (b.XRight < PositiveInfinity)
                _buildings.Add(new Building(b.XRight, NegativeInfinity, NegativeInfinity, PositiveInfinity));
        }
        else
        {
            // Need to merge with existing
            var other = new VerticalSkyline(new List<Building> { b }, _direction);
            MergeInternal(other._buildings);
        }
    }

    /// <summary>
    /// Merges another skyline into this one.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/skyline.cc:178-260 internal_merge_skyline()</remarks>
    public void Merge(VerticalSkyline other)
    {
        if (other._direction != _direction)
            throw new ArgumentException("Cannot merge skylines with different directions");

        if (other.IsEmpty) return;

        if (IsEmpty)
        {
            _buildings.AddRange(other._buildings);
            return;
        }

        MergeInternal(other._buildings);
    }

    /// <summary>
    /// Core merge algorithm - merges another building list into this skyline.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/skyline.cc:178-260 internal_merge_skyline()
    ///
    /// Simplified approach: collect all buildings, sort by X, and rebuild
    /// keeping the "above" one at each point. This is O(n log n) instead of
    /// LilyPond's O(n) but simpler and correct.
    /// </remarks>
    private void MergeInternal(IReadOnlyList<Building> otherBuildings)
    {
        if (otherBuildings.Count == 0) return;
        if (_buildings.Count == 0)
        {
            _buildings.AddRange(otherBuildings);
            return;
        }

        // Collect all buildings and sort by left X
        var allBuildings = new List<Building>(_buildings);
        allBuildings.AddRange(otherBuildings);
        allBuildings.Sort((a, b) => a.XLeft.CompareTo(b.XLeft));

        // Rebuild skyline keeping highest at each point
        var result = new List<Building>();

        foreach (var building in allBuildings)
        {
            if (double.IsNegativeInfinity(building.Height(building.XLeft)) &&
                double.IsNegativeInfinity(building.Height(building.XRight)))
            {
                // Empty building, skip
                continue;
            }

            if (result.Count == 0)
            {
                result.Add(building);
                continue;
            }

            // Try to merge with last building
            var last = result[^1];

            if (building.XLeft >= last.XRight)
            {
                // No overlap, just add
                result.Add(building);
            }
            else
            {
                // Overlapping - need to merge
                MergeOverlapping(result, building);
            }
        }

        _buildings.Clear();
        _buildings.AddRange(result);
    }

    /// <summary>
    /// Merges a new building with the existing result, handling overlaps.
    /// </summary>
    private void MergeOverlapping(List<Building> result, Building newBuilding)
    {
        // Find all buildings that overlap with newBuilding
        int firstOverlap = -1;
        for (int i = result.Count - 1; i >= 0; i--)
        {
            if (result[i].XRight > newBuilding.XLeft)
                firstOverlap = i;
            else
                break;
        }

        if (firstOverlap < 0)
        {
            result.Add(newBuilding);
            return;
        }

        // Extract overlapping buildings
        var overlapping = new List<Building>();
        for (int i = firstOverlap; i < result.Count; i++)
            overlapping.Add(result[i]);
        result.RemoveRange(firstOverlap, result.Count - firstOverlap);

        // Merge newBuilding with overlapping buildings
        var merged = MergeBuildingSet(overlapping, newBuilding);
        result.AddRange(merged);
    }

    /// <summary>
    /// Merges a set of overlapping buildings with a new building.
    /// </summary>
    private List<Building> MergeBuildingSet(List<Building> existing, Building newBuilding)
    {
        var result = new List<Building>();

        // Collect all boundary points
        var boundaries = new SortedSet<double>();
        foreach (var b in existing)
        {
            if (!double.IsInfinity(b.XLeft)) boundaries.Add(b.XLeft);
            if (!double.IsInfinity(b.XRight)) boundaries.Add(b.XRight);
        }
        if (!double.IsInfinity(newBuilding.XLeft)) boundaries.Add(newBuilding.XLeft);
        if (!double.IsInfinity(newBuilding.XRight)) boundaries.Add(newBuilding.XRight);

        // Add intersection points
        foreach (var b in existing)
        {
            if (Math.Abs(b.Slope - newBuilding.Slope) > 1e-6)
            {
                double ix = b.IntersectionX(newBuilding);
                if (ix > Math.Max(b.XLeft, newBuilding.XLeft) &&
                    ix < Math.Min(b.XRight, newBuilding.XRight))
                {
                    boundaries.Add(ix);
                }
            }
        }

        var boundaryList = boundaries.ToList();

        // For each interval, find the "above" building
        for (int i = 0; i < boundaryList.Count - 1; i++)
        {
            double left = boundaryList[i];
            double right = boundaryList[i + 1];
            double mid = (left + right) / 2;

            // Find highest building at midpoint
            Building? best = null;
            double bestHeight = double.NegativeInfinity;

            foreach (var b in existing)
            {
                if (b.XLeft <= mid && mid <= b.XRight)
                {
                    double h = b.Height(mid);
                    if (IsBetterHeight(h, bestHeight))
                    {
                        best = b;
                        bestHeight = h;
                    }
                }
            }

            if (newBuilding.XLeft <= mid && mid <= newBuilding.XRight)
            {
                double h = newBuilding.Height(mid);
                if (IsBetterHeight(h, bestHeight))
                {
                    best = newBuilding;
                    bestHeight = h;
                }
            }

            if (best.HasValue)
            {
                var segment = best.Value.WithXRange(left, right);

                // Try to merge with previous segment
                if (result.Count > 0)
                {
                    var last = result[^1];
                    if (Math.Abs(last.XRight - segment.XLeft) < 1e-10 &&
                        Math.Abs(last.Slope - segment.Slope) < 1e-10 &&
                        Math.Abs(last.Height(last.XRight) - segment.Height(segment.XLeft)) < 1e-10)
                    {
                        // Continuous - merge
                        result[^1] = new Building(last.XLeft, last.Height(last.XLeft),
                            segment.Height(segment.XRight), segment.XRight);
                        continue;
                    }
                }

                result.Add(segment);
            }
        }

        return result;
    }

    /// <summary>
    /// Returns true if height h1 is "better" than h2 for this skyline direction.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/skyline.cc:170-176 Building::above()
    /// With LilyPond sign convention (UP stores negative, DOWN stores positive),
    /// "better" is always the larger internal height for both directions:
    /// - UP: -10 > -20 means y=10 is "above" y=20 (closer to top)
    /// - DOWN: 70 > 50 means y=70 is "below" y=50 (closer to bottom)
    /// </remarks>
    private bool IsBetterHeight(double h1, double h2)
    {
        return h1 > h2;  // Same for both directions with LilyPond sign convention
    }

    /// <summary>
    /// Returns true if a is above b at x, considering skyline direction.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/skyline.cc:170-176 Building::above()
    /// With LilyPond sign convention, Building::Above() already works correctly
    /// for both directions - it compares internal heights directly.
    /// </remarks>
    private bool IsAbove(Building a, Building b, double x)
    {
        return a.Above(b, x);  // Same for both directions with LilyPond sign convention
    }

    /// <summary>
    /// Raises the skyline by the given amount. Only the intercept moves — a
    /// sloped building keeps its slope (the flat 3-arg ctor used here before
    /// flattened every sloped building to its intercept, so a raised skyline
    /// containing beam/merged-slope buildings came out with a corrupted roof).
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/skyline.cc Skyline::raise —
    /// <c>y_intercept_ += sky_ * amount</c>, slope untouched.</remarks>
    public void Raise(double amount)
    {
        double delta = (int)_direction * amount;
        for (int i = 0; i < _buildings.Count; i++)
        {
            var b = _buildings[i];
            _buildings[i] = b.Slope == 0
                ? new Building(b.XLeft, b.XRight, b.YIntercept + delta)
                : new Building(b.XLeft, b.Height(b.XLeft) + delta,
                               b.Height(b.XRight) + delta, b.XRight);
        }
    }

    /// <summary>
    /// Shifts the skyline horizontally.
    /// </summary>
    public void Shift(double amount)
    {
        for (int i = 0; i < _buildings.Count; i++)
        {
            var b = _buildings[i];
            // Need to recalculate YIntercept when shifting
            double newLeft = b.XLeft + amount;
            double newRight = b.XRight + amount;
            double heightAtNewLeft = b.Height(b.XLeft);  // Height stays same at equivalent position
            double heightAtNewRight = b.Height(b.XRight);
            _buildings[i] = new Building(newLeft, heightAtNewLeft, heightAtNewRight, newRight);
        }
    }

    /// <summary>
    /// Sets a minimum height for the skyline.
    /// </summary>
    public void SetMinimumHeight(double height)
    {
        double targetHeight = (int)_direction * height;

        if (_buildings.Count == 0)
        {
            _buildings.Add(new Building(NegativeInfinity, PositiveInfinity, targetHeight));
            return;
        }

        for (int i = 0; i < _buildings.Count; i++)
        {
            var b = _buildings[i];
            double leftHeight = b.Height(b.XLeft);
            double rightHeight = b.Height(b.XRight);

            if (_direction == VerticalDirection.Up)
            {
                leftHeight = Math.Max(leftHeight, targetHeight);
                rightHeight = Math.Max(rightHeight, targetHeight);
            }
            else
            {
                leftHeight = Math.Min(leftHeight, targetHeight);
                rightHeight = Math.Min(rightHeight, targetHeight);
            }

            _buildings[i] = new Building(b.XLeft, leftHeight, rightHeight, b.XRight);
        }
    }

    /// <summary>
    /// Creates a padded copy of this skyline with 45° sloped edges.
    /// Each building gets flat+sloped extensions on both sides.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/skyline.cc:558-615 Skyline::padded()
    ///
    /// For each building, 4 padding buildings are created:
    /// - Left-outer: 45° slope from (x-2P, h-P) to (x-P, h)
    /// - Left-inner: flat from (x-P, h) to (x, h)
    /// - Right-inner: flat from (xR, h) to (xR+P, h)
    /// - Right-outer: 45° slope from (xR+P, h) to (xR+2P, h-P)
    /// These are merged with the original to form the padded skyline.
    /// </remarks>
    public VerticalSkyline Padded(double horizonPadding)
    {
        if (horizonPadding <= 0.0)
            return this;

        var padBuildings = new List<Building>(_buildings.Count * 4);

        foreach (var b in _buildings)
        {
            if (!double.IsInfinity(b.XLeft))
            {
                double height = b.Height(b.XLeft);
                if (!double.IsNegativeInfinity(height))
                {
                    // Left-outer: sloped 45° (slope = +1 in internal coordinates)
                    double start = b.XLeft - 2 * horizonPadding;
                    double end = b.XLeft - horizonPadding;
                    padBuildings.Add(new Building(start, height - horizonPadding, height, end));

                    // Left-inner: flat at same height
                    padBuildings.Add(new Building(end, height, height, b.XLeft));
                }
            }

            if (!double.IsInfinity(b.XRight))
            {
                double height = b.Height(b.XRight);
                if (!double.IsNegativeInfinity(height))
                {
                    // Right-inner: flat at same height
                    double start = b.XRight;
                    double end = start + horizonPadding;
                    padBuildings.Add(new Building(start, height, height, end));

                    // Right-outer: sloped 45° (slope = -1 in internal coordinates)
                    padBuildings.Add(new Building(end, height, height - horizonPadding, end + horizonPadding));
                }
            }
        }

        if (padBuildings.Count == 0)
            return this;

        // Build a skyline from padding buildings
        var padSkyline = new VerticalSkyline(_direction);
        foreach (var pb in padBuildings)
            padSkyline._buildings.Add(pb);

        // Resolve overlaps among padding buildings
        padSkyline.SortAndResolve();

        // Merge padding with original
        var result = new VerticalSkyline(new List<Building>(_buildings), _direction);
        result.MergeInternal(padSkyline._buildings);
        return result;
    }

    /// <summary>
    /// Sorts buildings and resolves overlaps to form a valid skyline.
    /// </summary>
    private void SortAndResolve()
    {
        if (_buildings.Count <= 1)
            return;

        _buildings.Sort((a, b) => a.XLeft.CompareTo(b.XLeft));

        var resolved = new List<Building>();
        resolved.Add(_buildings[0]);

        for (int i = 1; i < _buildings.Count; i++)
        {
            var b = _buildings[i];
            if (b.XLeft >= resolved[^1].XRight)
            {
                resolved.Add(b);
            }
            else
            {
                MergeOverlapping(resolved, b);
            }
        }

        _buildings.Clear();
        _buildings.AddRange(resolved);
    }

    /// <summary>
    /// Calculates the distance between this skyline and another of opposite direction,
    /// with horizon_padding applied to this skyline.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/skyline.cc:530-554 Skyline::distance(other, horizon_padding)
    /// Only this skyline is padded (not other). The comment in LilyPond states:
    /// "it is not necessary to build a padded version of other, because the same
    /// effect can be achieved just by doubling horizon_padding."
    /// </remarks>
    public double Distance(VerticalSkyline other, double horizonPadding)
    {
        if (horizonPadding <= 0.0)
            return Distance(other);

        var paddedThis = Padded(horizonPadding);
        return paddedThis.Distance(other);
    }

    /// <summary>
    /// Calculates the distance between this skyline and another of opposite direction.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/skyline.cc:529-533 Skyline::distance()</remarks>
    public double Distance(VerticalSkyline other)
    {
        if (_direction == other._direction)
            throw new ArgumentException("Distance requires skylines with opposite directions");

        if (IsEmpty || other.IsEmpty)
            return NegativeInfinity;

        double maxDistance = NegativeInfinity;

        foreach (var b1 in _buildings)
        {
            foreach (var b2 in other._buildings)
            {
                // Find X overlap
                double overlapLeft = Math.Max(b1.XLeft, b2.XLeft);
                double overlapRight = Math.Min(b1.XRight, b2.XRight);

                if (overlapLeft < overlapRight)
                {
                    // Each building's height is Slope*x + intercept, so the gap
                    // h1+h2 is linear over the overlap and its maximum is always at
                    // an endpoint. Sampling the midpoint or the b1/b2 intersection
                    // can never beat the ends, so we only evaluate the two ends.
                    // LILYPOND-REF: skyline.cc measures distance at the overlap ends.
                    double distLeft = b1.Height(overlapLeft) + b2.Height(overlapLeft);
                    double distRight = b1.Height(overlapRight) + b2.Height(overlapRight);
                    maxDistance = Math.Max(maxDistance, Math.Max(distLeft, distRight));
                }
            }
        }

        return maxDistance;
    }

    /// <summary>
    /// Returns the extreme height of this skyline in real Y coordinates.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/skyline.cc:667-680 Skyline::max_height()
    /// Returns sky_ * max(building heights), converting internal representation
    /// back to real coordinates:
    /// - UP skyline: returns the smallest Y (topmost point, negative internally)
    /// - DOWN skyline: returns the largest Y (bottommost point, positive internally)
    /// </remarks>
    public double MaxHeight()
    {
        if (IsEmpty)
            return _direction == VerticalDirection.Up ? PositiveInfinity : NegativeInfinity;

        // Find maximum internal height across all buildings
        double maxInternalHeight = NegativeInfinity;
        foreach (var b in _buildings)
        {
            double hLeft = b.Height(b.XLeft);
            double hRight = b.Height(b.XRight);

            if (!double.IsNegativeInfinity(hLeft))
                maxInternalHeight = Math.Max(maxInternalHeight, hLeft);
            if (!double.IsNegativeInfinity(hRight))
                maxInternalHeight = Math.Max(maxInternalHeight, hRight);
        }

        // Convert to real Y coordinate: sky * internal_height
        // UP (sky=-1): negative internal -> positive real Y (but we want smallest Y)
        // DOWN (sky=+1): positive internal -> positive real Y
        int sky = (int)_direction;  // UP=-1, DOWN=+1
        return sky * maxInternalHeight;
    }

    /// <summary>
    /// The maximum protrusion above Y=0 over the X range [<paramref name="xLeft"/>,
    /// <paramref name="xRight"/>] — i.e. how far the skyline's content rises above
    /// the staff top within that span (0 if nothing protrudes). Used to space a
    /// chord-name line above the notes its symbols horizontally overhang, rather
    /// than only at the symbols' anchor points. (Defined for UP skylines.)
    /// </summary>
    public double MaxProtrusionInRange(double xLeft, double xRight)
    {
        if (xRight <= xLeft || IsEmpty)
            return 0;
        int sky = (int)_direction; // UP=-1
        double max = 0;
        foreach (var b in _buildings)
        {
            double l = Math.Max(b.XLeft, xLeft);
            double r = Math.Min(b.XRight, xRight);
            if (l >= r)
                continue;
            // protrusion above Y=0 = -realY, realY = sky * internalHeight.
            double pL = -sky * b.Height(l);
            double pR = -sky * b.Height(r);
            if (!double.IsInfinity(pL)) max = Math.Max(max, pL);
            if (!double.IsInfinity(pR)) max = Math.Max(max, pR);
        }
        return Math.Max(0, max);
    }

    /// <summary>
    /// Returns the height at a specific x coordinate in real Y coordinates.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/skyline.cc:657-665 Skyline::height()
    /// Returns sky * building.height(x), converting internal to real coordinates.
    /// </remarks>
    public double Height(double x)
    {
        foreach (var b in _buildings)
        {
            if (x >= b.XLeft && x <= b.XRight)
            {
                int sky = (int)_direction;  // UP=-1, DOWN=+1
                return sky * b.Height(x);
            }
        }

        return _direction == VerticalDirection.Up ? PositiveInfinity : NegativeInfinity;
    }
}