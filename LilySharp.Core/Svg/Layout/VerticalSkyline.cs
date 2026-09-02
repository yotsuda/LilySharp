// Lily# - Music notation compiler
// Copyright (C) 2025-2026 Yoshifumi Tsuda
//
// Parts of this file are ported from LilyPond, the GNU music typesetter.
// The C# is a modified translation of the following, not a copy of it:
//   lily/skyline.cc
//     Copyright (C) 2006--2026 Joe Neeman <joeneeman@gmail.com>
//   lily/freetype.cc
//     Copyright (C) 2004--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>
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
/// Direction for vertical skylines.
/// </summary>
/// <remarks>
/// LILYPOND-REF: flower/include/direction.hh — UP = +1, DOWN = -1.
/// Matches LilyPond's Y-up convention where heights are stored as sky * y_up
/// (skyline.cc:107 <c>height = sky * b[other_axis][sky]</c>).
/// </remarks>
public enum VerticalDirection
{
    /// <summary>Upward skyline (sky = +1): heights stored as <c>+y_up</c>, keeping the highest roof (largest Y-up) at each X.</summary>
    Up = 1,
    /// <summary>Downward skyline (sky = -1): heights stored as <c>-y_up</c>, keeping the lowest floor (smallest Y-up) at each X.</summary>
    Down = -1
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
    private readonly List<SkylineBuilding> _buildings;
    private readonly VerticalDirection _direction;

    private const double NegativeInfinity = double.NegativeInfinity;
    private const double PositiveInfinity = double.PositiveInfinity;

    /// <summary>Two buildings are treated as parallel (no interior intersection
    /// to split at) when their slopes differ by less than this.</summary>
    private const double SlopeEqualityEpsilon = 1e-6;

    /// <summary>Adjacent result segments are fused when their shared boundary,
    /// slope, and value all coincide within this floating-point tolerance.</summary>
    private const double ContinuityEpsilon = 1e-10;

    public VerticalSkyline(VerticalDirection direction)
    {
        _buildings = new List<SkylineBuilding>();
        _direction = direction;
    }

    private VerticalSkyline(List<SkylineBuilding> buildings, VerticalDirection direction)
    {
        _buildings = buildings;
        _direction = direction;
    }

    /// <summary>
    /// Wraps buildings that are ALREADY a resolved skyline (sorted, non-overlapping) —
    /// a placement copy of a cached profile. The caller owns the invariant; nothing is
    /// re-resolved here. A uniform horizon shift / value raise preserves it, which is
    /// what lets <see cref="TextOutlineSkylines"/> resolve a string once and place it
    /// per grob without paying the merge again.
    /// </summary>
    internal static VerticalSkyline FromResolvedBuildings(
        VerticalDirection direction, IEnumerable<SkylineBuilding> resolved)
        => new(new List<SkylineBuilding>(resolved), direction);

    public VerticalDirection Direction => _direction;
    public bool IsEmpty => _buildings.Count == 0;
    public IReadOnlyList<SkylineBuilding> Buildings => _buildings;

    /// <summary>
    /// Creates a skyline from a single bounding box.
    /// </summary>
    public static VerticalSkyline FromBox(double xLeft, double xRight, double yBottom, double yTop, VerticalDirection direction)
    {
        // LILYPOND-REF: lily/skyline.cc:104-110 Building::Building(Box, axis, sky) —
        //   Real height = sky * b[other_axis (horizon_axis)][sky].
        // In the native Y-up frame (yBottom < yTop, up-positive), sky picks the box
        // edge on its own side: UP (sky=+1) stores +yTop; DOWN (sky=-1) stores
        // -yBottom. Read sign-for-sign against skyline.cc.
        double edge = direction == VerticalDirection.Up ? yTop : yBottom;
        double height = (int)direction * edge;
        var building = new SkylineBuilding(xLeft, xRight, height);
        var skyline = new VerticalSkyline(direction);
        skyline.AddBuilding(building);
        return skyline;
    }

    /// <summary>
    /// Creates a skyline from a sloped region (e.g., beam).
    /// </summary>
    /// <remarks>
    /// Native Y-up frame (matching <see cref="FromBox"/>): the stored internal
    /// height is sky * edge — UP (sky=+1) keeps the top edge <c>+y</c>, DOWN
    /// (sky=-1) keeps the bottom edge (top edge lowered by <paramref name="thickness"/>)
    /// as <c>-(y + thickness)</c>. Currently exercised only by unit tests (no
    /// production caller builds sloped vertical skylines).
    /// </remarks>
    public static VerticalSkyline FromSlope(double xLeft, double yLeft, double xRight, double yRight,
        double thickness, VerticalDirection direction)
    {
        if (direction == VerticalDirection.Up)
        {
            // UP skyline (sky=+1): top edge stored as +y_up.
            return new VerticalSkyline(new List<SkylineBuilding>
            {
                new SkylineBuilding(xLeft, yLeft, yRight, xRight)
            }, direction);
        }
        else
        {
            // DOWN skyline (sky=-1): bottom edge (y lowered by thickness) stored as -y_up.
            return new VerticalSkyline(new List<SkylineBuilding>
            {
                new SkylineBuilding(xLeft, -(yLeft + thickness), -(yRight + thickness), xRight)
            }, direction);
        }
    }

    /// <summary>
    /// The skyline of ONE GLYPH'S OUTLINE, placed: the baked sign-framed buildings of
    /// <paramref name="quads"/> read in the glyph's own frame, engraved at
    /// <paramref name="size"/> and moved to the glyph origin (<paramref name="x"/>,
    /// <paramref name="y"/>).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/stencil-integral.cc:534-563 <c>add_named_glyph_segments</c> —
    /// a glyph enters a skyline through <c>add_outline_to_skyline</c>
    /// (LILYPOND-REF: lily/freetype.cc:174-202 <c>Path_interpreter</c> is run over the glyph by
    /// <c>ly_FT_add_outline_to_skyline</c>), which
    /// decomposes the OUTLINE, not its bounding box. The baking is
    /// <c>audit/scripts/Extract-EmmentalerSkylines.py</c>; this method only places what it
    /// baked, so the arithmetic here is the <c>Transform</c> LilyPond passes to that call.
    /// <para>
    /// ⚠️ WHY A BOX IS NOT ENOUGH, since one was here until 2026-07-28: a skyline distance is
    /// a POINTWISE maximum of two profiles
    /// (LILYPOND-REF: lily/skyline.cc:618-645 <c>Skyline::internal_distance</c>, which
    /// sums <c>i-&gt;height(x) + j-&gt;height(x)</c>), so two glyphs
    /// whose extremes sit at different x bind LOWER than the sum of their maxima. Two facing
    /// G clefs do exactly that, by 0.105961 — MEASURED off LilyPond in
    /// <c>audit/lp-geometry/probes/skyline-binding.ly</c>, which reads
    /// <c>dist=7.210039</c> against <c>3.540000 + 3.776000 = 7.316000</c>. A boxed port
    /// reproduces <c>max_height</c> and therefore every reading that binds against ONE
    /// profile; it cannot reproduce a comparison of TWO.
    /// </para>
    /// <para>
    /// X AND Y BOTH SCALE, and only the ORIGIN does not: the quads are glyph-frame numbers, so
    /// <see cref="StaffSize"/> applies to them exactly as <see cref="StaffSize.Ink"/> applies
    /// to a box, while <paramref name="x"/> is a paper-column position shared with the staff
    /// this one decorates. That is the one bare number here, as
    /// <see cref="StaffSize"/>'s own remark requires.
    /// </para>
    /// <para>
    /// ⚠️ LILYSHARP-OWN, one deviation, declared rather than hidden: LilyPond flattens each
    /// cubic into <c>max(2, |end-start|/0.2)</c> segments measured in the TRANSFORMED frame
    /// (LILYPOND-REF: lily/freetype.cc:128-150 <c>Path_interpreter::curve3to</c> takes that
    /// length off <c>transform_</c>), so a magnified staff gets a different segment COUNT.
    /// A baked profile
    /// is the full-size one, re-scaled. It costs sub-quantisation accuracy on an ossia's clef
    /// and nothing at full size; it goes when the outline itself is carried into the layout
    /// rather than a flattening of it.
    /// </para>
    /// </remarks>
    public static VerticalSkyline FromGlyphOutline(
        VerticalDirection direction, double[] quads, StaffSize size, double x, double y)
    {
        var sky = new VerticalSkyline(direction);
        double scale = size.Magnification;
        // The stored value is sky*height, so translating by y adds sky*y and scaling
        // multiplies the stored number directly
        // (LILYPOND-REF: lily/skyline.cc:512 Skyline::raise adds sky * amount to y_intercept).
        double offset = (int)direction * y;
        for (int i = 0; i + 3 < quads.Length; i += 4)
        {
            sky._buildings.Add(new SkylineBuilding(
                x + scale * quads[i],
                offset + scale * quads[i + 1],
                offset + scale * quads[i + 2],
                x + scale * quads[i + 3]));
        }
        // The baked buildings are raw contour edges and overlap each other, exactly as
        // LilyPond's per-direction todo list does before it is resolved
        // (LILYPOND-REF: lily/include/lazy-skyline-pair.hh:109-122 Lazy_skyline_pair::merge).
        // Resolve once.
        // ⚠️ DO NOT FILTER THESE BY SIGN. `quads` is sorted by CONTOUR DIRECTION, not by
        // which half of the glyph an edge is in, so a DOWN list legitimately contains
        // buildings ABOVE the glyph's origin (the C clef's does) and an UP list ones below.
        // The resolve is what picks the right building at each x; dropping any would delete
        // real silhouette. See GlyphSkylinesGenerated.cs's header for the measurement.
        sky.EndBatch();
        return sky;
    }

    /// <summary>
    /// Adds a building to the skyline, merging as necessary.
    /// </summary>
    private void AddBuilding(SkylineBuilding b)
    {
        if (_buildings.Count == 0)
        {
            // Initialize with empty regions on both sides
            if (b.Start > NegativeInfinity)
                _buildings.Add(new SkylineBuilding(NegativeInfinity, NegativeInfinity, NegativeInfinity, b.Start));
            _buildings.Add(b);
            if (b.End < PositiveInfinity)
                _buildings.Add(new SkylineBuilding(b.End, NegativeInfinity, NegativeInfinity, PositiveInfinity));
        }
        else
        {
            // Need to merge with existing
            var other = new VerticalSkyline(new List<SkylineBuilding> { b }, _direction);
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

        // Batch mode (BeginBatch/EndBatch): only APPEND the other skyline's
        // buildings; overlap resolution is deferred to EndBatch. Because the
        // resolve keeps the HIGHEST building at each point (a commutative max),
        // resolving the whole set once is byte-identical to merging one at a
        // time — but O(K log K) instead of O(K^2). This is the fix for the
        // per-note skyline construction that dominated layout allocation.
        if (_deferResolve)
        {
            // Batch mode: append only the REAL buildings. FromBox wraps each box in
            // ±inf empty-region padders; EndBatch's resolve drops those anyway (the
            // -inf skip in RebuildKeepingHighest), so filtering them here keeps the
            // batch's sort+copy small. Identical result — the same drop, moved earlier.
            foreach (var b in other._buildings)
            {
                if (double.IsNegativeInfinity(b.ValueAt(b.Start))
                    && double.IsNegativeInfinity(b.ValueAt(b.End)))
                    continue;
                _buildings.Add(b);
            }
            return;
        }

        if (IsEmpty)
        {
            _buildings.AddRange(other._buildings);
            return;
        }

        MergeInternal(other._buildings);
    }

    /// <summary>
    /// Merges an ALREADY-RESOLVED building set — a cached glyph or text profile — shifted by
    /// <paramref name="dx"/> along the horizon and raised by <paramref name="dy"/> in the
    /// caller's Y-up frame.
    /// </summary>
    /// <remarks>
    /// The same result as building a placed <see cref="VerticalSkyline"/> and merging it, with
    /// one copy of the profile instead of two: a placement is a shift plus a raise, both
    /// monotone, so they commute with the resolve — which is what lets the profile be resolved
    /// once per (glyph, size) and placed per grob.
    /// <para>
    /// ⚠️ MEASURED, and it is why this exists: seeding accidentals from their real outline
    /// (about eight buildings a glyph against a box's one) cost +44% on a 320-accidental
    /// score, and every one of those buildings was copied twice before it reached the batch.
    /// </para>
    /// </remarks>
    public void Merge(IReadOnlyList<SkylineBuilding> resolved, double dx, double dy)
    {
        if (resolved.Count == 0) return;
        double raise = (int)_direction * dy;
        if (!_deferResolve && !IsEmpty)
        {
            var placed = new List<SkylineBuilding>(resolved.Count);
            foreach (var b in resolved)
                placed.Add(b.ShiftedHorizon(dx).RaisedBy(raise));
            MergeInternal(placed);
            return;
        }
        // Batch (or empty): append the placed buildings straight in — the same filtering
        // Merge(VerticalSkyline) does, since a resolved profile carries no empty padders.
        foreach (var b in resolved)
            _buildings.Add(b.ShiftedHorizon(dx).RaisedBy(raise));
    }

    private bool _deferResolve;

    /// <summary>Start deferring overlap resolution; every <c>Merge</c> (both overloads) just
    /// appends until <see cref="EndBatch"/>. The skyline must NOT be read between
    /// Begin/End (callers only merge boxes in during construction).</summary>
    public void BeginBatch() => _deferResolve = true;

    /// <summary>
    /// Reserves room for <paramref name="additional"/> more buildings before a batch of
    /// <c>Merge</c> appends. Capacity only — the arithmetic, the order and the resolved
    /// result are untouched.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE POINT IS THE DOUBLING STEP, NOT THE LAST FEW PERCENT. A sung line's verse
    /// skyline is ~50 outline buildings per syllable, so a 5-measure line (~950) sits
    /// just under <see cref="List{T}"/>'s 1024-capacity rung and a 6-measure line
    /// (~1,140) just over it — and crossing the rung DOUBLES the allocate-and-discard
    /// copies behind <c>Add</c> (cumulative 4+8+…+1024 ≈ 2k elements vs …+2048 ≈ 4k).
    /// MEASURED (session 224, perf-lyrplain1k keystroke): the verse-skyline appends read
    /// 52.2 MB at 5-measure lines and 84.9 MB at 6-measure lines for byte-identical merge
    /// input (15,984 calls / 1,518,480 buildings, counted) — the whole difference is this
    /// rung. Reserving the exact count makes the append cost the buildings themselves.
    /// ⚠️ Per-call <c>EnsureCapacity(count + next)</c> is NOT this fix: List's
    /// EnsureCapacity grows by doubling too, so incremental reservations replay the same
    /// rungs. Only a caller that can COUNT the batch up front can retire them.
    /// </remarks>
    internal void ReserveForBatch(int additional) =>
        _buildings.EnsureCapacity(_buildings.Count + additional);

    /// <summary>Resolve all buildings accumulated since <see cref="BeginBatch"/> in a
    /// single sort+rebuild, restoring the normal (fully resolved) invariant.</summary>
    public void EndBatch()
    {
        _deferResolve = false;
        if (_buildings.Count > 1)
        {
            RebuildKeepingHighest(new List<SkylineBuilding>(_buildings));
            CoalesceColinear();
        }
    }

    /// <summary>
    /// Merges adjacent buildings that are one straight line — same slope, same intercept,
    /// touching ends — into one. LOSSLESS by construction: a building's height at x is
    /// Slope·x + Intercept, so the merged building computes bit-identical values
    /// everywhere the two did. The mass case is a glyph outline's flat top, which the
    /// flattener splits at every control point: a verse line of fifty-building syllables
    /// walks and re-merges far fewer segments after this (the walk's MergeInternal is
    /// linear in building count per step, and the lyric chain steps once per line per
    /// system per pass).
    /// </summary>
    private void CoalesceColinear()
    {
        if (_buildings.Count < 2) return;
        int w = 0;
        for (int i = 1; i < _buildings.Count; i++)
        {
            var a = _buildings[w];
            var b = _buildings[i];
            if (a.End == b.Start && a.Slope == b.Slope && a.Intercept == b.Intercept)
                _buildings[w] = new SkylineBuilding(
                    a.Start, a.ValueAt(a.Start), b.ValueAt(b.End), b.End);
            else
                _buildings[++w] = b;
        }
        _buildings.RemoveRange(w + 1, _buildings.Count - w - 1);
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
    private void MergeInternal(IReadOnlyList<SkylineBuilding> otherBuildings)
    {
        if (otherBuildings.Count == 0) return;
        if (_buildings.Count == 0)
        {
            _buildings.AddRange(otherBuildings);
            return;
        }

        // Collect all buildings and resolve keeping the highest at each point.
        var allBuildings = new List<SkylineBuilding>(_buildings);
        allBuildings.AddRange(otherBuildings);
        RebuildKeepingHighest(allBuildings);
    }

    /// <summary>
    /// Sorts the combined building list by X and rebuilds THIS skyline keeping the
    /// highest building at each point. Order-independent (max is commutative), so
    /// resolving all buildings at once equals merging them one at a time — the
    /// property the batch path relies on.
    /// </summary>
    /// <param name="allBuildings">⚠️ MUST BE THE CALLER'S OWN LIST, never
    /// <see cref="_buildings"/> itself: this rebuilds in place, so it clears
    /// <see cref="_buildings"/> before the walk and uses it as the result buffer. Both
    /// call sites hand over a fresh <c>new List&lt;SkylineBuilding&gt;(_buildings)</c>,
    /// which is what makes that sound — and what saves a whole second full-size list plus
    /// its copy-back on EVERY merge. MEASURED (session 191, Release, keystroke allocation):
    /// merging is the hot allocator of a script-dense page and the buffer was the larger
    /// half of it.</param>
    private void RebuildKeepingHighest(List<SkylineBuilding> allBuildings)
    {
        // The contract in the param remark, said by the machine rather than by prose: a
        // third caller that handed over _buildings itself would have this method clear the
        // very list it is walking, and the damage — a silently truncated skyline — is the
        // kind that comes out as spacing, not as a throw.
        System.Diagnostics.Debug.Assert(
            !ReferenceEquals(allBuildings, _buildings),
            "RebuildKeepingHighest rebuilds into _buildings; its input must be a separate list");

        allBuildings.Sort((a, b) => a.Start.CompareTo(b.Start));

        // Rebuild skyline keeping highest at each point, straight into this skyline's own
        // list — allBuildings already holds everything that was in it.
        var result = _buildings;
        result.Clear();

        foreach (var building in allBuildings)
        {
            if (double.IsNegativeInfinity(building.ValueAt(building.Start)) &&
                double.IsNegativeInfinity(building.ValueAt(building.End)))
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

            if (building.Start >= last.End)
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
    }

    /// <summary>
    /// Merges a new building with the existing result, handling overlaps.
    /// </summary>
    private void MergeOverlapping(List<SkylineBuilding> result, SkylineBuilding newBuilding)
    {
        // Find all buildings that overlap with newBuilding
        int firstOverlap = -1;
        for (int i = result.Count - 1; i >= 0; i--)
        {
            if (result[i].End > newBuilding.Start)
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
        var overlapping = new List<SkylineBuilding>();
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
    private List<SkylineBuilding> MergeBuildingSet(List<SkylineBuilding> existing, SkylineBuilding newBuilding)
    {
        var result = new List<SkylineBuilding>();

        // Collect all boundary points. A plain List sorted in place replaces a
        // SortedSet (a red-black tree that allocates a node per insert) — this
        // primitive is the hottest allocator in skyline resolution, hit by both
        // system-skyline construction and Padded(). Duplicate boundaries are not
        // removed here; the interval loop skips zero-width spans below, which is
        // equivalent to the set's dedup.
        //
        // ⚠️ ±∞ ARE BOUNDARIES TOO, and leaving them out DELETED A REAL FLOOR. LilyPond's
        // invariant is that a skyline spans the whole horizon — its own file header says
        // "the start of the first building is at -infinity, the end of the last building is
        // at infinity" — and it keeps it by padding every gap with a -infinity building.
        // Lily# stores only the NON-EMPTY stretches, which is the same skyline written
        // shorter; but then a building that DOES reach ±∞ has to put that end into the
        // interval walk, or the walk covers only the finite hull and everything outside it
        // comes back empty. Merging one finite box over an (-∞ . +∞) floor used to erase the
        // floor either side of that box — and merging two unbounded buildings used to leave
        // NOTHING, there being no finite boundary to walk between.
        // LILYPOND-REF: lily/skyline.cc:259-282 empty_skyline / single_skyline — the two
        //   builders that keep that invariant (non_overlapping_skyline :284-326 fills the
        //   gaps the same way).
        bool reachesLeft = double.IsNegativeInfinity(newBuilding.Start);
        bool reachesRight = double.IsPositiveInfinity(newBuilding.End);
        var boundaryList = new List<double>(existing.Count * 2 + 4);
        foreach (var b in existing)
        {
            if (double.IsNegativeInfinity(b.Start)) reachesLeft = true;
            else boundaryList.Add(b.Start);
            if (double.IsPositiveInfinity(b.End)) reachesRight = true;
            else boundaryList.Add(b.End);
        }
        if (!double.IsInfinity(newBuilding.Start)) boundaryList.Add(newBuilding.Start);
        if (!double.IsInfinity(newBuilding.End)) boundaryList.Add(newBuilding.End);
        if (reachesLeft) boundaryList.Add(double.NegativeInfinity);
        if (reachesRight) boundaryList.Add(double.PositiveInfinity);

        // Add intersection points
        foreach (var b in existing)
        {
            if (Math.Abs(b.Slope - newBuilding.Slope) > SlopeEqualityEpsilon)
            {
                double ix = b.Intersection(newBuilding);
                if (ix > Math.Max(b.Start, newBuilding.Start) &&
                    ix < Math.Min(b.End, newBuilding.End))
                {
                    boundaryList.Add(ix);
                }
            }
        }

        boundaryList.Sort();

        // For each interval, find the "above" building
        for (int i = 0; i < boundaryList.Count - 1; i++)
        {
            double left = boundaryList[i];
            double right = boundaryList[i + 1];
            if (right <= left) continue; // zero-width span (duplicate boundary) — matches SortedSet dedup
            // A probe point INSIDE the interval. An unbounded end cannot be averaged
            // ((-∞ + ∞)/2 is NaN, and (-∞ + x)/2 is -∞, which no finite building contains),
            // so step one unit in from the finite side — the tail intervals are outside every
            // finite boundary by construction, so no finite building can be reached there and
            // the probe can only find the buildings that actually run to infinity.
            double mid = double.IsNegativeInfinity(left)
                ? (double.IsPositiveInfinity(right) ? 0.0 : right - 1.0)
                : double.IsPositiveInfinity(right) ? left + 1.0
                : (left + right) / 2;

            // Find highest building at midpoint
            SkylineBuilding? best = null;
            double bestHeight = double.NegativeInfinity;

            foreach (var b in existing)
            {
                if (b.Start <= mid && mid <= b.End)
                {
                    double h = b.ValueAt(mid);
                    if (IsBetterHeight(h, bestHeight))
                    {
                        best = b;
                        bestHeight = h;
                    }
                }
            }

            if (newBuilding.Start <= mid && mid <= newBuilding.End)
            {
                double h = newBuilding.ValueAt(mid);
                if (IsBetterHeight(h, bestHeight))
                {
                    best = newBuilding;
                    bestHeight = h;
                }
            }

            if (best.HasValue)
            {
                var segment = best.Value.WithRange(left, right);

                // Try to merge with previous segment
                if (result.Count > 0)
                {
                    var last = result[^1];
                    if (Math.Abs(last.End - segment.Start) < ContinuityEpsilon &&
                        Math.Abs(last.Slope - segment.Slope) < ContinuityEpsilon &&
                        Math.Abs(last.ValueAt(last.End) - segment.ValueAt(segment.Start)) < ContinuityEpsilon)
                    {
                        // Continuous - merge
                        result[^1] = new SkylineBuilding(last.Start, last.ValueAt(last.Start),
                            segment.ValueAt(segment.End), segment.End);
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
    /// LILYPOND-REF: lily/skyline.cc:166-173 Building::above()
    /// In the native Y-up frame (UP stores +y_up, DOWN stores -y_up), "better" is
    /// always the larger internal height for both directions:
    /// - UP: +20 > +10 means y_up=20 is "above" y_up=10 (closer to top)
    /// - DOWN: -50 > -70 means y_up=50 is "below" y_up=70 (closer to bottom)
    /// </remarks>
    private bool IsBetterHeight(double h1, double h2)
    {
        return h1 > h2;  // Same for both directions with LilyPond sign convention
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
                ? new SkylineBuilding(b.Start, b.End, b.Intercept + delta)
                : new SkylineBuilding(b.Start, b.ValueAt(b.Start) + delta,
                               b.ValueAt(b.End) + delta, b.End);
        }
    }

    /// <summary>
    /// Scales every height about Y = 0, leaving X alone — the skyline of the same ink
    /// engraved at a smaller size.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/lily-library.scm <c>magstep</c> — LilyPond has no operation here
    /// to port, because it never scales a skyline: a context engraved at <c>fontSize = -3</c>
    /// builds its stencils small in the first place, so its VerticalAxisGroup's
    /// <c>vertical-skylines</c> come out small with them
    /// (LILYPOND-REF: lily/axis-group-interface.cc:914-940 <c>skyline_spacing</c>, which
    /// merges the members' own stencils).
    /// <para>
    /// ⚠️ LILYSHARP-OWN: THIS EXISTS BECAUSE LILY# SCALES AT DRAW TIME. An ossia is drawn
    /// inside <c>SharedRenderer</c>'s <c>BeginGroup(0, .., OssiaScale, OssiaScale)</c> while
    /// its layout reads full-size <c>GlyphMetrics</c>, so the transform the renderer applies
    /// has to be applied to the reserved ink as well or the two disagree. It goes when the
    /// metrics are read per staff at that staff's own size.
    /// </para>
    /// <para>
    /// ⚠️ Y ONLY, AND THAT IS NOT THE WHOLE TRUTH — it is what the current frame can express.
    /// A magnified staff keeps its X POSITIONS (they are the score-wide paper columns, shared
    /// with the staff it decorates) but its glyphs are narrower as well as shorter: the
    /// renderer gets both, drawing the ink inside a uniform scale group and pre-dividing the
    /// positions with <c>UnscaledXDrawingContext</c>. This builder cannot do the same because
    /// ONE X number here carries two units at once — a position in the system's staff-spaces
    /// and a box width in the glyph's — so scaling X would move the positions too. The
    /// consequence, named rather than hidden: an ossia's glyph boxes are 1/0.7071 too WIDE in
    /// the skyline, which costs horizontal overlap in <c>Distance</c> (either overload) and
    /// nothing else.
    /// <see cref="Height"/>-based assertion
    /// <c>StaffSkylineFrameTests.AnOssiaStaffReservesItsOwnSize_InXAsWellAsY</c> pins it and
    /// is skipped until every width-bearing seed separates the two units in one move.
    /// </para>
    /// </remarks>
    public void Scale(double factor)
    {
        for (int i = 0; i < _buildings.Count; i++)
        {
            var b = _buildings[i];
            _buildings[i] = b.Slope == 0
                ? new SkylineBuilding(b.Start, b.End, b.Intercept * factor)
                : new SkylineBuilding(b.Start, b.ValueAt(b.Start) * factor,
                               b.ValueAt(b.End) * factor, b.End);
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
    /// <summary>
    /// A copy restricted to the horizon interval [<paramref name="xLeft"/>,
    /// <paramref name="xRight"/>]: ink outside the range is dropped, a building
    /// straddling a boundary is trimmed at it. Distances against the result read only
    /// the in-range ink — a gap is no constraint, exactly as an unbuilt stretch is.
    /// </summary>
    /// <remarks>
    /// Built for the pedal support set (PedalEngraver): LilyPond side-positions a pedal
    /// item against the note columns its spanner ACKNOWLEDGED, not against the whole
    /// staff profile, and the cheapest faithful spelling of "those columns' ink" is the
    /// staff's inside profile clipped to their allocated span. ⚠️ Requires a RESOLVED
    /// skyline (no open batch): the trim reads each building's roof as final.
    /// </remarks>
    public VerticalSkyline ClippedToRange(double xLeft, double xRight)
    {
        if (_deferResolve)
            throw new InvalidOperationException(
                "ClippedToRange on an open batch — EndBatch first, the roofs are not final.");
        var clipped = new List<SkylineBuilding>();
        foreach (var b in _buildings)
        {
            double s = Math.Max(b.Start, xLeft), e = Math.Min(b.End, xRight);
            if (s >= e)
                continue;
            clipped.Add(s <= b.Start && e >= b.End ? b : b.WithRange(s, e));
        }
        return new VerticalSkyline(clipped, _direction);
    }

    public VerticalSkyline Padded(double horizonPadding)
    {
        if (horizonPadding <= 0.0)
            return this;

        var padBuildings = new List<SkylineBuilding>(_buildings.Count * 4);

        foreach (var b in _buildings)
        {
            if (!double.IsInfinity(b.Start))
            {
                double height = b.ValueAt(b.Start);
                if (!double.IsNegativeInfinity(height))
                {
                    // Left-outer: sloped 45° (slope = +1 in internal coordinates)
                    double start = b.Start - 2 * horizonPadding;
                    double end = b.Start - horizonPadding;
                    padBuildings.Add(new SkylineBuilding(start, height - horizonPadding, height, end));

                    // Left-inner: flat at same height
                    padBuildings.Add(new SkylineBuilding(end, height, height, b.Start));
                }
            }

            if (!double.IsInfinity(b.End))
            {
                double height = b.ValueAt(b.End);
                if (!double.IsNegativeInfinity(height))
                {
                    // Right-inner: flat at same height
                    double start = b.End;
                    double end = start + horizonPadding;
                    padBuildings.Add(new SkylineBuilding(start, height, height, end));

                    // Right-outer: sloped 45° (slope = -1 in internal coordinates)
                    padBuildings.Add(new SkylineBuilding(end, height, height - horizonPadding, end + horizonPadding));
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
        var result = new VerticalSkyline(new List<SkylineBuilding>(_buildings), _direction);
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

        _buildings.Sort((a, b) => a.Start.CompareTo(b.Start));

        var resolved = new List<SkylineBuilding>();
        resolved.Add(_buildings[0]);

        for (int i = 1; i < _buildings.Count; i++)
        {
            var b = _buildings[i];
            if (b.Start >= resolved[^1].End)
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
    /// Distance to another skyline of the OPPOSITE direction (throws otherwise),
    /// in the LilyPond internal (sign*y) frame: larger = closer/overlapping.
    /// Returns <see cref="double.NegativeInfinity"/> if either side is empty.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/skyline.cc:529-533 Skyline::distance() — via the O(n+m)
    /// merge walk (<see cref="SkylineMath.DistanceResolved"/>): this class maintains
    /// the resolved invariant (sorted, non-overlapping; a batch may suspend it but the
    /// batch contract forbids reading until <see cref="EndBatch"/>).
    /// </remarks>
    public double Distance(VerticalSkyline other)
    {
        if (_direction == other._direction)
            throw new ArgumentException("Distance requires skylines with opposite directions");

        return SkylineMath.DistanceResolved(_buildings, other._buildings);
    }

    /// <summary>
    /// Returns the extreme height of this skyline in real Y coordinates.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/skyline.cc:667-680 Skyline::max_height()
    /// Returns sky_ * max(building heights), converting the internal representation
    /// back to real Y-up coordinates:
    /// - UP skyline: returns the largest Y-up (topmost point)
    /// - DOWN skyline: returns the smallest Y-up (bottommost point)
    /// </remarks>
    public double MaxHeight()
        // ONE walk, not two spellings of it: a cut at +∞ leaves every building on the left,
        // so MaxHeightsSplitAt's left half IS this. (It used to be a second copy of the same
        // loop, which is the duplication HANDOFF §5.2.1② names — two of a quantity and one
        // of them drifts.) The empty answer matches: with no buildings the left half comes
        // back as this direction's ∓∞ too.
        => MaxHeightsSplitAt(PositiveInfinity).Left;

    /// <summary>
    /// <see cref="MaxHeight"/> for the two halves this skyline is cut into at
    /// <paramref name="xSplit"/> — left of it and right of it — in real Y coordinates.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/skyline.cc:667-680 Skyline::max_height, taken over the two halves
    /// in one walk. LilyPond has no such call: it reaches the same partition by asking a
    /// SUBSET of the grobs for their pure height (lily/axis-group-interface.cc:359-474
    /// adjacent_pure_heights, which splits by which COLUMN a grob hangs off rather than by
    /// where its ink lands). Both name the same cut — the ink that is there only because the
    /// line starts here, against everything else — and the deviation is stated where this is
    /// consumed (PageBreaker.CalcLineHeights).
    /// <para>
    /// BOTH HALVES IN ONE PASS, deliberately: this runs per system on the paged path, which
    /// a preview re-enters on every edit to a score longer than one page. A building is a
    /// straight line, so its extreme over a half is at that half's ends and a building
    /// straddling the cut is counted in both — which also makes <c>max(left, right)</c> the
    /// whole skyline's <see cref="MaxHeight"/> exactly, so the caller needs no second walk
    /// for the union.
    /// </para>
    /// Either half is the empty answer (∓∞) when nothing lies on that side, exactly as
    /// <see cref="MaxHeight"/> is for an empty skyline.
    /// </remarks>
    public (double Left, double Right) MaxHeightsSplitAt(double xSplit)
    {
        int sky = (int)_direction;
        double left = NegativeInfinity;
        double right = NegativeInfinity;
        foreach (var b in _buildings)
        {
            if (b.Start < xSplit)
            {
                double r = Math.Min(b.End, xSplit);
                left = Max3(left, b.ValueAt(b.Start), b.ValueAt(r));
            }
            if (b.End > xSplit)
            {
                double l = Math.Max(b.Start, xSplit);
                right = Max3(right, b.ValueAt(l), b.ValueAt(b.End));
            }
        }
        double empty = _direction == VerticalDirection.Up ? NegativeInfinity : PositiveInfinity;
        return (double.IsNegativeInfinity(left) ? empty : sky * left,
                double.IsNegativeInfinity(right) ? empty : sky * right);

        // The ±∞ sentinels the resolved form carries must not become the answer.
        static double Max3(double acc, double a, double b)
        {
            if (!double.IsNegativeInfinity(a))
                acc = Math.Max(acc, a);
            if (!double.IsNegativeInfinity(b))
                acc = Math.Max(acc, b);
            return acc;
        }
    }

    /// <summary>
    /// <see cref="MaxHeight"/> over the X range [<paramref name="xLeft"/>, <paramref name="xRight"/>]
    /// alone, in real Y coordinates — the direction's empty answer (∓∞) when nothing lies in
    /// the range, exactly as <see cref="MaxHeightsSplitAt"/> answers for an empty half.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/axis-group-interface.cc:359-474 adjacent_pure_heights — LilyPond's
    /// page-breaking estimate of a line's height is built per COLUMN range from the grobs'
    /// pure extents, and a candidate line is priced by the max over its columns. Lily# has no
    /// pure extent per grob at that seam; it has the placed paging skyline, and a bar's share
    /// of it is this range walk. The estimate is consumed in LayoutEngine.EstimateMeasureHeights,
    /// where the deviation is stated. Same walk shape as MaxHeightsSplitAt (a building is a
    /// straight line, so its extreme over a range is at the range's ends), one pass.
    /// </remarks>
    public double MaxHeightInRange(double xLeft, double xRight)
    {
        int sky = (int)_direction;
        double max = NegativeInfinity;
        foreach (var b in _buildings)
        {
            double l = Math.Max(b.Start, xLeft);
            double r = Math.Min(b.End, xRight);
            if (l > r)
                continue;
            double a = b.ValueAt(l);
            double c = b.ValueAt(r);
            if (!double.IsNegativeInfinity(a))
                max = Math.Max(max, a);
            if (!double.IsNegativeInfinity(c))
                max = Math.Max(max, c);
        }
        double empty = _direction == VerticalDirection.Up ? NegativeInfinity : PositiveInfinity;
        return double.IsNegativeInfinity(max) ? empty : sky * max;
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
        int sky = (int)_direction; // UP=+1
        double max = 0;
        foreach (var b in _buildings)
        {
            double l = Math.Max(b.Start, xLeft);
            double r = Math.Min(b.End, xRight);
            if (l >= r)
                continue;
            // protrusion above Y=0 = realY (Y-up), realY = sky * internalHeight.
            double pL = sky * b.ValueAt(l);
            double pR = sky * b.ValueAt(r);
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
    /// Returns sky * building.height(x), converting internal to real Y-up coordinates.
    /// </remarks>
    public double Height(double x)
    {
        foreach (var b in _buildings)
        {
            if (x >= b.Start && x <= b.End)
            {
                int sky = (int)_direction;  // UP=+1, DOWN=-1
                return sky * b.ValueAt(x);
            }
        }

        return _direction == VerticalDirection.Up ? NegativeInfinity : PositiveInfinity;
    }
}
