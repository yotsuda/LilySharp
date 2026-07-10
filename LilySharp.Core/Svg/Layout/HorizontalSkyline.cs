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
/// Direction for horizontal skylines.
/// </summary>
/// <remarks>
/// LILYPOND-REF: Uses sky = +1 for RIGHT, -1 for LEFT
/// </remarks>
public enum HorizontalDirection
{
    /// <summary>Rightward skyline (sky = +1): the rightmost X at each Y.</summary>
    Right = 1,
    /// <summary>Leftward skyline (sky = -1): the leftmost X at each Y.</summary>
    Left = -1
}

/// <summary>
/// A horizontal skyline - the outline of a set of buildings as seen from the right or left.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/include/skyline.hh:48-100 Skyline class
///
/// Used for note spacing collision detection.
/// - RIGHT skyline: rightmost X at each Y position
/// - LEFT skyline: leftmost X at each Y position
/// </remarks>
internal sealed class HorizontalSkyline
{
    private readonly List<SkylineBuilding> _buildings;
    private readonly HorizontalDirection _direction;

    private const double NegativeInfinity = double.NegativeInfinity;
    private const double PositiveInfinity = double.PositiveInfinity;

    public HorizontalSkyline(HorizontalDirection direction)
    {
        _buildings = new List<SkylineBuilding>();
        _direction = direction;
    }

    private HorizontalSkyline(List<SkylineBuilding> buildings, HorizontalDirection direction)
    {
        _buildings = buildings;
        _direction = direction;
    }

    public HorizontalDirection Direction => _direction;
    public bool IsEmpty => _buildings.Count == 0;
    public IReadOnlyList<SkylineBuilding> Buildings => _buildings;

    /// <summary>
    /// Creates a skyline from a single bounding box.
    /// </summary>
    public static HorizontalSkyline FromBox(double yBottom, double yTop, double xLeft, double xRight, HorizontalDirection direction)
    {
        var building = BoxBuilding(yBottom, yTop, xLeft, xRight, direction);
        var skyline = new HorizontalSkyline(direction);
        skyline._buildings.Add(building);
        return skyline;
    }

    /// <summary>
    /// Creates a skyline from multiple bounding boxes.
    /// </summary>
    public static HorizontalSkyline FromBoxes(IEnumerable<(double YBottom, double YTop, double XLeft, double XRight)> boxes, HorizontalDirection direction)
    {
        var skyline = new HorizontalSkyline(direction);
        foreach (var (yBottom, yTop, xLeft, xRight) in boxes)
        {
            var building = BoxBuilding(yBottom, yTop, xLeft, xRight, direction);
            skyline._buildings.Add(building);
        }
        return skyline;
    }

    /// <summary>
    /// Creates a skyline from a sloped region (e.g., beam edge).
    /// </summary>
    public static HorizontalSkyline FromSlope(double yBottom, double xBottom, double yTop, double xTop, HorizontalDirection direction)
    {
        int sky = (int)direction;
        return new HorizontalSkyline(new List<SkylineBuilding>
        {
            new SkylineBuilding(yBottom, sky * xBottom, sky * xTop, yTop)
        }, direction);
    }

    // LilyPond sign convention (skyline.cc:107-113): the stored value is sky*x.
    // RIGHT (sky=+1) stores +xRight; LEFT (sky=-1) stores -xLeft. The horizon
    // axis for a horizontal skyline is Y, so the box maps to [yBottom, yTop].
    private static SkylineBuilding BoxBuilding(double yBottom, double yTop, double xLeft, double xRight, HorizontalDirection direction)
    {
        double x = direction == HorizontalDirection.Right ? xRight : -xLeft;
        return new SkylineBuilding(yBottom, yTop, x);
    }

    /// <summary>
    /// Merges another skyline into this one.
    /// </summary>
    /// <remarks>
    /// Deliberately keeps the building LIST (concatenation), not the envelope.
    /// This is sound for every query this class offers: a building shadowed by
    /// a taller one can never win a max — <see cref="Distance"/> takes the max
    /// of building-pair sums and <see cref="X"/> takes the max over covering
    /// buildings, so both already compute envelope values. (LilyPond's
    /// internal_merge_skyline canonicalizes because its representation must
    /// cover the full axis; ours is sparse.) Cost: queries are O(n·m) instead
    /// of O(n+m) — fine at the per-item-pair sizes used in note spacing.
    /// </remarks>
    public void Merge(HorizontalSkyline other)
    {
        if (other._direction != _direction)
            throw new ArgumentException("Cannot merge skylines with different directions");

        _buildings.AddRange(other._buildings);
    }

    /// <summary>
    /// Distance to another skyline of the OPPOSITE direction (throws otherwise),
    /// in the LilyPond internal (sign*x) frame: larger = closer/overlapping.
    /// Returns <see cref="double.NegativeInfinity"/> if either side is empty.
    /// Shares the <see cref="SkylineMath.Distance"/> kernel with
    /// <see cref="VerticalSkyline"/> — the two are the same axis-transposed
    /// computation over <see cref="SkylineBuilding"/> horizon intervals.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/skyline.cc:617-649 internal_distance()</remarks>
    public double Distance(HorizontalSkyline other)
    {
        if (_direction == other._direction)
            throw new ArgumentException("Distance requires skylines with opposite directions");

        return SkylineMath.Distance(_buildings, other._buildings);
    }

    /// <summary>
    /// Returns the X position at a specific Y coordinate in real coordinates.
    /// Takes the OUTERMOST covering building (the envelope), since the
    /// building list may contain shadowed entries (see <see cref="Merge"/>).
    /// </summary>
    public double X(double y)
    {
        double best = NegativeInfinity; // stored frame: larger = outer for both directions
        foreach (var b in _buildings)
        {
            if (y >= b.Start && y <= b.End)
                best = Math.Max(best, b.ValueAt(y));
        }

        if (double.IsNegativeInfinity(best))
            return _direction == HorizontalDirection.Right ? NegativeInfinity : PositiveInfinity;

        int sky = (int)_direction;
        return sky * best;
    }
}
