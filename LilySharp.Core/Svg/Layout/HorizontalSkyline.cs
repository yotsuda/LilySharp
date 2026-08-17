// Lily# - Music notation compiler
// Copyright (C) 2025-2026 Yoshifumi Tsuda
//
// Parts of this file are ported from LilyPond, the GNU music typesetter.
// The C# is a modified translation of the following, not a copy of it:
//   lily/skyline.cc
//     Copyright (C) 2006--2026 Joe Neeman <joeneeman@gmail.com>
// LilyPond is free software under the GNU General Public License version 3 or
// later; its notices are kept here as that licence requires. The full list is in
// LILYPOND-ATTRIBUTION.md. Lily# is an independent project, not affiliated with
// or endorsed by the LilyPond project.
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
    /// Creates a skyline from a flat list of sign-framed buildings, four doubles apiece:
    /// start (horizon low), startValue (sky*x there), endValue (sky*x at horizon high),
    /// end (horizon high). This is the form the baked accidental skylines
    /// (<see cref="GlyphMetrics.AccidentalSkylinePair"/>) are stored in.
    /// </summary>
    public static HorizontalSkyline FromSignedBuildings(HorizontalDirection direction, double[] quads)
    {
        var buildings = new List<SkylineBuilding>(quads.Length / 4);
        for (int i = 0; i + 3 < quads.Length; i += 4)
            buildings.Add(new SkylineBuilding(quads[i], quads[i + 1], quads[i + 2], quads[i + 3]));
        return new HorizontalSkyline(buildings, direction);
    }

    /// <summary>A deep copy (the building list is duplicated), so mutating operations
    /// (<see cref="Raise"/>/<see cref="Shift"/>/<see cref="Merge"/>) on the copy leave the
    /// original — e.g. a shared baked glyph skyline — untouched.</summary>
    public HorizontalSkyline Clone() => new HorizontalSkyline(new List<SkylineBuilding>(_buildings), _direction);

    /// <summary>Raises every building's roof by <paramref name="r"/> in real X (mutates).
    /// LILYPOND-REF: lily/skyline.cc:512 Skyline::raise (y_intercept_ += sky*r).</summary>
    public void Raise(double r)
    {
        int sky = (int)_direction;
        for (int i = 0; i < _buildings.Count; i++)
            _buildings[i] = _buildings[i].RaisedBy(sky * r);
    }

    /// <summary>Translates every building along the horizon (Y) by <paramref name="s"/> (mutates).
    /// LILYPOND-REF: lily/skyline.cc:519 Skyline::shift.</summary>
    public void Shift(double s)
    {
        for (int i = 0; i < _buildings.Count; i++)
            _buildings[i] = _buildings[i].ShiftedHorizon(s);
    }

    /// <summary>Uniformly scales every building about the origin by <paramref name="k"/>
    /// (mutates) — used to shrink a cue-sized accidental glyph before it is shifted onto
    /// its (unscaled) staff position.</summary>
    public void Scale(double k)
    {
        for (int i = 0; i < _buildings.Count; i++)
            _buildings[i] = _buildings[i].ScaledBy(k);
    }

    /// <summary>Maximum roof height (real X in the skyline's own sense: rightmost for a
    /// RIGHT skyline, leftmost for a LEFT one). NegativeInfinity if empty.
    /// LILYPOND-REF: lily/skyline.cc:668 Skyline::max_height.</summary>
    public double MaxHeight()
    {
        double ret = NegativeInfinity;
        foreach (var b in _buildings)
        {
            ret = Math.Max(ret, b.ValueAt(b.Start));
            ret = Math.Max(ret, b.ValueAt(b.End));
        }
        return (int)_direction * ret;
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
    /// a taller one can never win a max — <c>Distance</c> (both overloads) takes the max
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
    /// Distance to an opposite-facing skyline with a horizon padding: a grob is kept clear
    /// by <paramref name="horizonPadding"/> even from neighbours that are that far away
    /// ALONG the horizon (Y) rather than only where the Y ranges overlap. The padding falls
    /// off at 45° (skyline.cc:558-615 padded), so a distant-in-Y neighbour pushes less than
    /// a directly-facing one.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/skyline.cc:530-554 Skyline::distance(other, horizon_padding).
    /// LilyPond pads ONE side and reuses the other as-is; we pad <c>this</c>.</remarks>
    public double Distance(HorizontalSkyline other, double horizonPadding)
    {
        if (_direction == other._direction)
            throw new ArgumentException("Distance requires skylines with opposite directions");
        if (horizonPadding <= 0.0)
            return SkylineMath.Distance(_buildings, other._buildings);

        return SkylineMath.Distance(Padded(horizonPadding), other._buildings);
    }

    /// <summary>
    /// A COPY thickened along the horizon by <paramref name="horizonPadding"/> — the padded
    /// skyline itself, for callers that go on to READ it (<see cref="X"/>) rather than to
    /// measure a distance against it.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/skyline.cc:558-615 Skyline::padded (horizon_padding) — LilyPond returns a
    /// Skyline there too; <see cref="Distance(HorizontalSkyline, double)"/> only ever needed
    /// the building list, which is why the padding lived inside it until now.</remarks>
    public HorizontalSkyline PaddedCopy(double horizonPadding)
        => horizonPadding <= 0.0
            ? Clone()
            : new HorizontalSkyline(Padded(horizonPadding), _direction);

    /// <summary>
    /// Raises the whole skyline's floor to <paramref name="h"/> (real X): the outline no
    /// longer recedes past <paramref name="h"/> anywhere, including at horizon coordinates no
    /// building covers at all. Mutates.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/skyline.cc:719-725 Skyline::set_minimum_height — it merges
    /// a one-building skyline spanning the whole horizon at that height. Appending the same
    /// building to this lazy list is that merge: <see cref="X"/> and
    /// <see cref="SkylineMath.Distance"/> both take the maximum in the sign frame, so a
    /// building the merge would have shadowed cannot win either way.</remarks>
    public void SetMinimumHeight(double h)
    {
        int sky = (int)_direction;
        _buildings.Add(new SkylineBuilding(NegativeInfinity, sky * h, sky * h, PositiveInfinity));
    }

    /// <summary>
    /// This skyline's buildings plus the sloped-and-flat pad buildings that thicken it by
    /// <paramref name="horizonPadding"/> along the horizon. The lazy-list envelope makes
    /// concatenation sufficient (a shadowed pad building never wins the distance max), so —
    /// unlike LilyPond, which must canonicalise — no merge is needed.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/skyline.cc:558-615 Skyline::padded. Heights are in the
    /// sign frame (sky*x); subtracting the padding lowers the roof for both directions.</remarks>
    private List<SkylineBuilding> Padded(double horizonPadding)
    {
        double hp = horizonPadding;
        var pad = new List<SkylineBuilding>(_buildings);
        foreach (var b in _buildings)
        {
            if (!double.IsInfinity(b.Start))
            {
                double h = b.ValueAt(b.Start);
                if (!double.IsNegativeInfinity(h))
                {
                    pad.Add(new SkylineBuilding(b.Start - 2 * hp, h - hp, h, b.Start - hp));
                    pad.Add(new SkylineBuilding(b.Start - hp, h, h, b.Start));
                }
            }
            if (!double.IsInfinity(b.End))
            {
                double h = b.ValueAt(b.End);
                if (!double.IsNegativeInfinity(h))
                {
                    pad.Add(new SkylineBuilding(b.End, h, h, b.End + hp));
                    pad.Add(new SkylineBuilding(b.End + hp, h, h - hp, b.End + 2 * hp));
                }
            }
        }
        return pad;
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
