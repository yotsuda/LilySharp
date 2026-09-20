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

    /// <summary>
    /// A resolved profile PLACED: the cached buildings of a glyph, a string or a wave, shifted
    /// by <paramref name="dx"/> along the horizon and raised by <paramref name="dy"/> in the
    /// caller's Y-up frame. Nothing is re-resolved — a placement is a shift plus a raise, both
    /// monotone, so it commutes with the resolve, which is what lets a profile be resolved once
    /// per (glyph, size) and placed per grob.
    /// </summary>
    /// <remarks>
    /// ⚠️ ONE SPELLING WHERE THERE WERE FOUR, and the fourth is what it cost.
    /// <c>TextOutlineSkylines</c>, <c>TrillWaveOutline</c> and <c>DynamicOutline</c> each
    /// carried a byte-identical private <c>PlaceResolved</c>, and <c>SkylineBuilder</c> a
    /// fourth copy inline; every one of them built an ARRAY of placed buildings that
    /// <see cref="FromResolvedBuildings"/> then copied again into the skyline's list — two
    /// full-size allocations for a placement that needs one. MEASURED (session 421, Release,
    /// the owner's corpus, eight forward keystrokes a book, allocated bytes): that second copy
    /// was 51,637,312 B over 40,372 placements, <b>0.33% of a keystroke</b>.
    /// <para>
    /// This is HANDOFF §5.2.1② — two of a quantity and one of them drifts — read in the
    /// direction the duplication actually ran: four hand-written placements against one
    /// operation the skyline can name.
    /// </para>
    /// </remarks>
    internal static VerticalSkyline FromPlacedProfile(
        VerticalDirection direction, IReadOnlyList<SkylineBuilding> resolved, double dx, double dy)
    {
        double raise = (int)direction * dy;
        var placed = new List<SkylineBuilding>(resolved.Count);
        for (int i = 0; i < resolved.Count; i++)
            placed.Add(resolved[i].ShiftedHorizon(dx).RaisedBy(raise));
        return new(placed, direction);
    }

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
    /// Seats the one building of a brand-new skyline, padding the horizon either side of it
    /// with the empty regions LilyPond's invariant asks for.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE SKYLINE IS EMPTY HERE BY CONSTRUCTION, and this used to carry a merge arm for
    /// when it was not: build a one-building skyline and <see cref="MergeInternal"/> it. That
    /// arm was unreachable — this is private, its one caller is <see cref="FromBox"/>, and
    /// FromBox makes the skyline two lines earlier. Session 421 counted it over the owner's
    /// corpus as well before removing it: 0 calls in 1,848 keystrokes.
    /// </remarks>
    private void AddBuilding(SkylineBuilding b)
    {
        // Initialize with empty regions on both sides
        if (b.Start > NegativeInfinity)
            _buildings.Add(new SkylineBuilding(NegativeInfinity, NegativeInfinity, NegativeInfinity, b.Start));
        _buildings.Add(b);
        if (b.End < PositiveInfinity)
            _buildings.Add(new SkylineBuilding(b.End, NegativeInfinity, NegativeInfinity, PositiveInfinity));
    }

    /// <summary>
    /// Merges ONE BOX into this skyline — the seed <c>Merge(FromBox(…))</c> delivers, without
    /// building a skyline to carry it.
    /// </summary>
    /// <remarks>
    /// THE SAME BUILDING, APPENDED AT THE SAME POINT IN THE SAME ORDER, and that is why this
    /// is not a second spelling of <see cref="FromBox"/>: the edge and the height are read off
    /// the identical expression, and a batched <see cref="Merge(VerticalSkyline)"/> already
    /// drops the two ±inf padders <see cref="AddBuilding"/> puts either side of the box (the
    /// filter is in that method, and its remark says the resolve would drop them anyway). So
    /// the list this leaves is the list the pair left, building for building.
    /// <para>
    /// ⚠️ WHAT IT SAVES IS THE CARRIER, NOT THE GEOMETRY. <c>FromBox</c> allocates a
    /// <see cref="VerticalSkyline"/>, its <see cref="List{T}"/> and the List's array to deliver
    /// one 32-byte struct into a batch that keeps one of the three buildings it is handed.
    /// MEASURED over the owner's corpus (231 books, eight keystrokes each, allocated bytes):
    /// a keystroke makes 454,829 batched merges of such a wrapper, 387,515 of them this exact
    /// shape — three buildings offered, one kept.
    /// </para>
    /// <para>
    /// ⚠️ THE DIRECTION IS THIS SKYLINE'S. The pair it replaces names a direction at the call
    /// site and <see cref="Merge(VerticalSkyline)"/> throws when the two disagree; here there
    /// is only one direction and nothing to disagree with. Every converted site passed its own
    /// skyline's direction, so what the throw guarded is gone rather than silenced.
    /// </para>
    /// <para>
    /// OUTSIDE A BATCH there is no saving to take: an unbatched merge resolves against what is
    /// already here, and the padders take part in that resolve. Those callers go the old way,
    /// through the one <c>FromBox</c> that has always served them.
    /// </para>
    /// </remarks>
    public void MergeBox(double xLeft, double xRight, double yBottom, double yTop)
    {
        var batch = _batch;
        if (batch is null)
        {
            Merge(FromBox(xLeft, xRight, yBottom, yTop, _direction));
            return;
        }
        // The identical two lines FromBox reads the box with. The sign convention has ONE
        // home and it is that method's citation; repeating the address here would give it a
        // second, and one of two addresses always rots (HANDOFF §7.6).
        double edge = _direction == VerticalDirection.Up ? yTop : yBottom;
        var building = new SkylineBuilding(xLeft, xRight, (int)_direction * edge);
        if (double.IsNegativeInfinity(building.ValueAt(building.Start))
            && double.IsNegativeInfinity(building.ValueAt(building.End)))
            return;
        batch.Add(building);
    }

    /// <summary>
    /// Merges ONE SLOPED REGION — a beam's outer edge — without building a skyline to carry
    /// it. <see cref="MergeBox"/>'s twin; see its remark for what is saved and why the result
    /// is the same list.
    /// </summary>
    /// <remarks>
    /// ⚠️ A SLOPE CARRIES NO PADDERS. <see cref="FromSlope"/> makes a skyline of exactly one
    /// building — no ±inf regions either side, unlike <see cref="FromBox"/> — so the batched
    /// merge it feeds already appends that one building untouched, and this appends the same
    /// one. What goes is the carrier, not a building.
    /// </remarks>
    public void MergeSlope(double xLeft, double yLeft, double xRight, double yRight,
        double thickness)
    {
        var batch = _batch;
        if (batch is null)
        {
            Merge(FromSlope(xLeft, yLeft, xRight, yRight, thickness, _direction));
            return;
        }
        // FromSlope's own two arms, sign for sign: UP keeps the top edge as +y_up, DOWN keeps
        // the bottom edge (lowered by the thickness) as -y_up.
        var building = _direction == VerticalDirection.Up
            ? new SkylineBuilding(xLeft, yLeft, yRight, xRight)
            : new SkylineBuilding(xLeft, -(yLeft + thickness), -(yRight + thickness), xRight);
        if (double.IsNegativeInfinity(building.ValueAt(building.Start))
            && double.IsNegativeInfinity(building.ValueAt(building.End)))
            return;
        batch.Add(building);
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
        if (_batch is { } batch)
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
                batch.Add(b);
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
        var batch = _batch;
        if (batch is null && !IsEmpty)
        {
            // EXACTLY the sequence MergeInternal would have been handed — this skyline's
            // buildings, then the placed ones — so the sort sees the same input in the same
            // order and the resolve the same output. The list `placed` used to be is the
            // walk's own input now, which is the one it always was.
            var input = RentMergeInput(_buildings.Count + resolved.Count);
            input.AddRange(_buildings);
            // Indexed, not walked, at both sites: `resolved` is an interface and foreach
            // boxes its enumerator on every merge (RULES §5.3, measured session 446).
            for (int i = 0; i < resolved.Count; i++)
                input.Add(resolved[i].ShiftedHorizon(dx).RaisedBy(raise));
            ResolveFrom(input);
            return;
        }
        // Batch (or empty): append the placed buildings straight in — the same filtering
        // Merge(VerticalSkyline) does, since a resolved profile carries no empty padders.
        var target = batch ?? _buildings;
        for (int i = 0; i < resolved.Count; i++)
            target.Add(resolved[i].ShiftedHorizon(dx).RaisedBy(raise));
    }

    /// <summary>
    /// The list an open BATCH accumulates into — non-null exactly while one is open, which is
    /// what "deferring the resolve" now means. Lent from a per-thread pool
    /// (<see cref="RentBatch"/>), so a batch appends into the capacity the last batch on this
    /// thread grew to instead of growing one of its own.
    /// </summary>
    /// <remarks>
    /// ⚠️ IT IS NOT <see cref="_buildings"/>, and that is the whole saving. A batch appends
    /// every seed of a staff or a system — hundreds of buildings — into a list that starts
    /// EMPTY, and <see cref="List{T}"/> doubles: delivering N buildings that way allocates and
    /// discards about 2N-4N slots of array before the resolve keeps a fraction of them.
    /// MEASURED (session 426, the owner's corpus, 231 books × eight forward keystrokes,
    /// allocated bytes, counted per call site): the four batches
    /// <see cref="SkylineBuilder.BuildSystemSkylines"/> and
    /// <c>SkylineBuilder.BuildStaffSkylines</c> open appended 897,518 buildings a sweep and
    /// paid 83,353,208 B of doubling for them — 2.90 slots per building, <b>0.556% of a
    /// keystroke</b>, which is more than the whole island that led here (0.353%). The one
    /// batch site that already counted its appends and reserved for them
    /// (<c>LyricEngraver</c>, <see cref="ReserveForBatch"/>, session 224) paid ZERO in the
    /// same run — the control was already in the tree.
    /// <para>
    /// ⚠️ A POOL ON THE THREAD, not a field on the skyline, for the reason
    /// <see cref="RentMergeInput"/>'s own remark gives: batching is a CONSTRUCTION idiom, one
    /// batch per skyline (session 421 measured <see cref="EndBatch"/> over 10,284 distinct
    /// skylines, a ratio of 1.00), so an instance buffer would be allocated by the one call
    /// that could have used it. And a STACK rather than one slot, because the UP and DOWN
    /// skylines of the same system are batched at the same time (measured high-water mark: 2).
    /// </para>
    /// <para>
    /// ⚠️ A BATCH THAT IS NEVER ENDED simply loses its buffer to the garbage collector and the
    /// next <see cref="BeginBatch"/> makes a new one. Nothing is corrupted, because the buffer
    /// is only reachable from the skyline that holds it.
    /// </para>
    /// </remarks>
    private List<SkylineBuilding>? _batch;

    /// <summary>Start deferring overlap resolution; every <c>Merge</c> (both overloads) just
    /// appends until <see cref="EndBatch"/>. The skyline must NOT be read between
    /// Begin/End (callers only merge boxes in during construction).</summary>
    public void BeginBatch()
    {
        var batch = RentBatch();
        // A BATCH THAT OPENS ON A NON-EMPTY SKYLINE resolves the buildings already here
        // together with the ones to come, in that order — so the buffer starts as those and
        // _buildings is the OUTPUT alone. The sequence the resolve's sort sees is byte for
        // byte the one it saw before: List.Sort is NOT stable, so where two buildings share a
        // Start their order is part of the answer, and this keeps it.
        if (_buildings.Count > 0)
        {
            batch.AddRange(_buildings);
            _buildings.Clear();
        }
        _batch = batch;
    }

    /// <summary>Takes a batch buffer off this thread's pool, EMPTY, or makes the thread's
    /// first.</summary>
    /// <remarks>
    /// ⚠️ THE EMPTYING IS LOAD-BEARING and it is HERE, once. A buffer handed to a batch still
    /// holding the last batch's buildings resolves them into this skyline — silhouette from
    /// grobs it never saw, exactly what <see cref="RentMergeInput"/>'s own remark describes, and
    /// <c>SkylineMergeTests.ASecondResolveOnTheSameThread_DoesNotInheritTheFirstsBuildings</c>
    /// is what catches it (verified by poison). <see cref="ReturnBatch"/> deliberately does NOT
    /// clear as well: a second clear is a second spelling of one guarantee (HANDOFF §5.2.1②),
    /// it was measured to make no test fail when removed, and a <c>SkylineBuilding</c> holds no
    /// references, so an unemptied buffer in the pool pins nothing.
    /// It is <see cref="List{T}.Clear"/> rather than a fresh list because the capacity is the
    /// whole point.
    /// </remarks>
    private static List<SkylineBuilding> RentBatch()
    {
        var pool = t_batchPool;
        if (pool is null || pool.Count == 0)
            return new List<SkylineBuilding>();
        var list = pool.Pop();
        list.Clear();
        return list;
    }

    /// <summary>Puts a finished batch's buffer back, with its capacity. See
    /// <see cref="RentBatch"/> for why it is not emptied here.</summary>
    private static void ReturnBatch(List<SkylineBuilding> batch) =>
        (t_batchPool ??= new()).Push(batch);

    // One buffer per batch open AT ONCE on this thread; the pool never holds more than the
    // deepest nesting reached. See _batch's remark for the measurement and for why the scope
    // is the thread.
    [ThreadStatic]
    private static Stack<List<SkylineBuilding>>? t_batchPool;

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
    internal void ReserveForBatch(int additional)
    {
        var target = _batch ?? _buildings;
        target.EnsureCapacity(target.Count + additional);
    }

    /// <summary>Resolve all buildings accumulated since <see cref="BeginBatch"/> in a
    /// single sort+rebuild, restoring the normal (fully resolved) invariant.</summary>
    /// <remarks>
    /// ⚠️ IT ALSO SERVES A CALLER THAT NEVER BEGAN A BATCH — <see cref="FromGlyphOutline"/>
    /// fills <see cref="_buildings"/> itself and asks for the one resolve. That arm is the
    /// <c>batch is null</c> one, and it is what it always was: a rented copy of those
    /// buildings, resolved back into them.
    /// </remarks>
    public void EndBatch()
    {
        var batch = _batch;
        _batch = null;
        if (batch is null)
        {
            if (_buildings.Count > 1)
            {
                var copy = RentMergeInput(_buildings.Count);
                copy.AddRange(_buildings);
                ResolveFrom(copy);
                CoalesceColinear();
            }
        }
        else if (batch.Count > 1)
        {
            // The batch's own buffer IS the resolve's input — the list the appends built, in
            // the order they built it. What went is the copy EndBatch used to make of it.
            // ⚠️ AND THE ROOM FOR THE RESULT IS NO LONGER RESERVED HERE. Session 426 reserved
            // batch.Count, because the resolve rebuilt INTO _buildings and a pooled batch
            // leaves that empty — the doubling the batch stopped paying came back on the way
            // out. But batch.Count is the BOUND, not the answer, and the two are far apart:
            // MEASURED (session 427) the resolve keeps 0.363 of what it is handed, so the
            // reservation was 2.8x the fill. A batched skyline is built once and then read, so
            // the resolve sizes the result at exactly what it comes to instead — the policy,
            // and the measurement behind it, are on sizeResultExactly.
            RebuildKeepingHighest(batch, sizeResultExactly: true);
            CoalesceColinear();
        }
        else if (batch.Count == 1)
        {
            // _buildings is empty here by construction (BeginBatch moved anything it held
            // into the buffer), so the one building resolves against nothing.
            _buildings.Add(batch[0]);
        }
        if (batch is not null)
            ReturnBatch(batch);
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
        var allBuildings = RentMergeInput(_buildings.Count + otherBuildings.Count);
        allBuildings.AddRange(_buildings);
        allBuildings.AddRange(otherBuildings);
        ResolveFrom(allBuildings);
    }

    /// <summary>
    /// Sorts the combined building list by X and rebuilds THIS skyline keeping the
    /// highest building at each point. Order-independent (max is commutative), so
    /// resolving all buildings at once equals merging them one at a time — the
    /// property the batch path relies on.
    /// </summary>
    /// <remarks>
    /// The walk's three scratch buffers are taken ONCE HERE and handed down, not allocated
    /// per overlapping building. They are rented from the thread and given back at the end of
    /// the walk (<see cref="t_resolveScratch"/>, which is where the measurement is), so two
    /// threads resolving two skylines still share nothing.
    /// <para>
    /// ⚠️ WHY THAT IS WORTH A PARAMETER. MEASURED (session 416, Release, the owner's corpus,
    /// eight forward keystrokes a book, allocated bytes): this walk ran 83,538 times a corpus
    /// and entered <see cref="MergeOverlapping"/> 1,691,308 times — twenty overlaps a walk —
    /// and each of those allocated three Lists that lived for the length of one overlap:
    /// <c>overlapping</c>, <c>boundaryList</c> and the merged result. 398 B an overlap,
    /// 673,848,928 B, <b>3.97% of a keystroke</b>, of which the system silhouette's own
    /// resolve (HANDOFF §1's island ⒳) was only 1.16% — the same three lists cost more in
    /// everyone else's merges than in the island that was named. The arithmetic was never
    /// the cost: the sort allocates nothing, and the inner loop reads 17.3
    /// (interval × building) pairs a call.
    /// </para>
    /// <para>
    /// ⚠️ AND IT IS NOT A QUADRATIC RESOLVE, which is what a reader expects of the shape.
    /// MEASURED the same run: the tail a new building drags back through the merge is 1.52
    /// buildings on average and never exceeded 18, over inputs of up to 596 buildings. The
    /// walk is linear; it was the churn.
    /// </para>
    /// </remarks>
    /// <param name="allBuildings">⚠️ MUST BE A LIST THIS SKYLINE DOES NOT OWN, never
    /// <see cref="_buildings"/> itself. Every call site hands over a buffer the thread lends —
    /// <see cref="RentMergeInput"/> or the batch's own — which is what saves a whole second
    /// full-size list on EVERY merge. MEASURED (session 191, Release, keystroke allocation):
    /// merging is the hot allocator of a script-dense page and the buffer was the larger half
    /// of it.
    /// <para>
    /// ⚠️ THE REASON CHANGED IN SESSION 427 AND THE RULE DID NOT. Until then this rebuilt IN
    /// PLACE — it cleared <see cref="_buildings"/> and used it as the result buffer — so an
    /// input that WAS <see cref="_buildings"/> would have been cleared mid-walk. Session 427
    /// took that arm off the batch path and session 428 off the last one, so no walk writes
    /// into the list it is rebuilding any more and that particular damage is gone; what
    /// remains is that <see cref="ResolveFrom"/> hands the input to the thread's pool
    /// afterwards, and a pool holding a live skyline's own list resolves the next walk's
    /// buildings straight into that skyline.
    /// </para></param>
    /// <param name="sizeResultExactly">
    /// Whether <see cref="_buildings"/> is allocated at exactly the count the walk comes to,
    /// or left to the ONE growth step <see cref="List{T}.AddRange"/> takes — which is the same
    /// array when the list is empty and twice what is there when it is not. ⚠️ The second is
    /// not spelled out anywhere in the code, and deliberately: an explicit
    /// <c>EnsureCapacity(R)</c> before the copy computes the identical number from the
    /// identical source one line earlier (HANDOFF §5.2.1②).
    /// <para>
    /// ⚠️ THIS IS A POLICY AND THE MEASUREMENT THAT DECIDES IT IS ALREADY IN THIS FILE, in
    /// <see cref="t_mergeInput"/>'s remark: session 421 counted <see cref="EndBatch"/> running
    /// 10,284 times over 10,284 DISTINCT skylines — a ratio of 1.00, because Begin/EndBatch is
    /// a CONSTRUCTION idiom, a skyline is batched once and then read — while
    /// <see cref="MergeInternal"/> reads 4.51 calls an instance.
    /// </para>
    /// <para>
    /// So the two want opposite arrays. A batch-built skyline has nothing to amortise: doubling
    /// only overshoots, and session 426's reservation of <c>batch.Count</c> overshot the other
    /// way — MEASURED (session 427, the owner's corpus, eight forward keystrokes a book) the
    /// resolve keeps 0.363 of what the batch appends, so the exit paid 0.206% of a keystroke to
    /// fill 0.072%. A skyline that is merged into again DOES have something to amortise, and
    /// exact-size is actively wrong for it: sized at R, the very next merge that adds one
    /// building has to allocate again. <c>SkylineMergeTests.AMergeIntoALargeSkyline_</c>
    /// <c>DoesNotCopyItToReadIt</c> is what says so — a first version of this change sized
    /// every walk exactly and that gate went red, because its warm merges each add a building
    /// to a 300-building skyline and each one then reallocated.
    /// </para>
    /// <para>
    /// ⚠️ AND THE OTHER ARM IS NOT "NO POLICY", which is what it was until session 428: the
    /// walk grew <see cref="_buildings"/> a rung at a time from whatever the Clear left, which
    /// for a skyline being merged into for the FIRST time is nothing, so it climbed
    /// 4-8-16-…-R and allocated about twice R on the way. MEASURED (session 428, the owner's
    /// corpus, 231 books × eight forward keystrokes, counted by construction, and it
    /// reproduces session 427's independently measured 29,912,072 B over 73,231 walks):
    /// <see cref="MergeInternal"/> and the placed-profile merge climbed 29,946,160 B over
    /// 73,254 walks, <b>0.201% of a keystroke</b> — and <b>92.4% of it was each skyline's
    /// FIRST merge</b> (26,439,832 B of 28,610,688 over 12,769 of 14,061 growing walks).
    /// <c>EnsureCapacity(R)</c> is both policies at once for exactly that reason, and the
    /// simulation priced all four candidates in one sweep: the rung climb 0.192%, reserving
    /// the input's count before the walk 0.159%, sizing every walk exactly <b>0.372%</b> —
    /// worse than doing nothing, because 17,474 walks that pay nothing today would then
    /// reallocate for the next merge — and this 0.113%.
    /// </para>
    /// </param>
    private void RebuildKeepingHighest(
        List<SkylineBuilding> allBuildings, bool sizeResultExactly = false)
    {
        // The contract in the param remark, said by the machine rather than by prose.
        System.Diagnostics.Debug.Assert(
            !ReferenceEquals(allBuildings, _buildings),
            "RebuildKeepingHighest's input is handed to the thread's pool afterwards; "
            + "it must not be this skyline's own list");

        allBuildings.Sort((a, b) => a.Start.CompareTo(b.Start));

        // THE WALK WRITES TO THE LENT BUFFER — never into the list it is rebuilding. What
        // that buys is R, and R is the only number that can size an array right; which of the
        // two ways it is then spent is the parameter's business.
        var result = RentResolveOutput();
        result.Clear();

        // Lazily, so a walk that never overlaps — a resolved profile merged into an empty
        // skyline, which is most of the placement copies — still rents nothing.
        ResolveScratch? scratch = null;

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
                MergeOverlapping(result, building, scratch ??= RentScratch());
            }
        }

        if (scratch is not null)
            ReturnScratch(scratch);

        // ⚠️ R IS KNOWN ONLY NOW, and that is the whole point. Clearing first means the sizing
        // has nothing to copy, and the AddRange below then takes ONE growth step — to R, or to
        // twice what this list already holds, whichever is larger. That is ACCUMULATION's
        // policy, and it is List's own: a skyline merged into for the first time is sized at
        // exactly the answer, and one merged into again keeps the headroom it amortises with.
        _buildings.Clear();
        if (sizeResultExactly)
        {
            // CONSTRUCTION overrides it: the array is the answer and nothing more. The
            // Capacity setter, because the growth AddRange would do is the doubling this is
            // here to retire.
            if (_buildings.Capacity < result.Count)
                _buildings.Capacity = result.Count;
        }
        _buildings.AddRange(result);
        ReturnResolveOutput(result);
    }

    /// <summary>
    /// The buffer a resolve walk WRITES — the result it is building, lent from one buffer the
    /// thread keeps between walks, so that the walk's own doubling is paid once per thread
    /// instead of once per walk.
    /// </summary>
    /// <remarks>
    /// ⚠️ WHY THE RESULT NEEDS A BUFFER OF ITS OWN, when session 191 deliberately had the walk
    /// rebuild in place. In place is free of copies but it forces the ARRAY to be chosen before
    /// the answer is known: either the walk grows _buildings by rungs (what session 426 found,
    /// and it is the larger half — MEASURED session 427: MergeInternal's exit grew 29,912,072 B
    /// over 14,310 walks, 0.200% of a keystroke, and nobody had priced it) or a caller reserves
    /// an upper bound (what session 426 did for EndBatch, 0.206%, of which only 0.072% was ever
    /// filled). Resolving into a lent buffer and copying R out costs one memmove of R buildings
    /// and lets _buildings be sized by the answer.
    /// <para>
    /// ⚠️ EVERY WALK, since session 428 — the batch arm was only the half that had been
    /// measured. 38.4 non-batch walks a keystroke now copy their result out, 1,604 buildings
    /// of memmove, and what that buys is on <c>RebuildKeepingHighest</c>'s
    /// <c>sizeResultExactly</c>. The buffer stays ONE list a thread whichever arm rents it:
    /// the walks do not nest.
    /// </para>
    /// <para>
    /// ⚠️ AND BOTH WALKS, since session 432. <see cref="SortAndResolve"/> went on building its
    /// result in a bare <c>new List</c>, and THAT WAS THE WHOLE OF WHAT IT ALLOCATED. MEASURED
    /// (session 432, Release, the owner's corpus, 231 books × eight forward keystrokes,
    /// allocated bytes, per region inside BOTH walks): it ran 7,185 times a sweep (3.9 a
    /// keystroke, 332,150 overlaps) and its body came to 18,912,792 B, <b>0.128% of a
    /// keystroke</b>, of which <b>100.0%</b> was that one list — the list object with its first
    /// four-building rung 1,322,040 B (715 a keystroke, 184 B a walk), the loop's own
    /// <c>Add</c> 5,695,472 B (3,082) and <see cref="MergeOverlapping"/>'s
    /// <c>result.AddRange</c> 11,895,280 B (6,437). The sort, the scratch, the tail lift,
    /// <see cref="MergeBuildingSet"/> and the write-back read <b>zero</b>, and so did
    /// <c>unattributed</c>. The same run carried its own control: the other walk's result,
    /// already lent, read zero on all three of those regions, and 100% of its 15,263 a
    /// keystroke was the exit copy into <see cref="_buildings"/>.
    /// </para>
    /// <para>
    /// ⚠️ THE THREE REGIONS ARE ONE LIST, AND READING THEM AS THREE THINGS IS WHAT MISPRICED IT.
    /// Session 431 re-ticketed this fix at 0.039% by taking only the loop's own <c>Add</c> to be
    /// "the resolved list"; <c>AddRange</c> grows the same list from the same rung. The A/B says
    /// the whole of it: 7,986,775 / 7,986,751 B a keystroke before and 7,976,522 / 7,976,517
    /// after, <b>-0.128%</b> — 10,234 B a keystroke, the region's measured total to the byte.
    /// The answer still goes back into <see cref="Padded"/>'s own buffer, which holds 4N and so
    /// never grows for it: that exit stayed zero.
    /// </para>
    /// <para>
    /// ⚠️ THE EMPTYING IS LOAD-BEARING, exactly as in <see cref="RentMergeInput"/>: the walk
    /// asks <c>result.Count == 0</c> to decide whether it is placing the first building, so a
    /// buffer still holding the last walk's result would merge that skyline's silhouette into
    /// this one. <c>SkylineMergeTests</c> pins it.
    /// <para>
    /// ⚠️ AND WHICH TEST PINS IT CHANGED WITH SESSION 432, WITHOUT A LINE OF TEST BEING WRITTEN.
    /// <see cref="RebuildKeepingHighest"/> clears the buffer again for itself, so dropping the
    /// <c>Clear</c> here leaves that walk correct and breaks only the other one. VERIFIED BY
    /// POISON, both sides of the change: before session 432 that poison left
    /// <c>SkylineMergeTests.ASecondPaddingOnTheSameThread_DoesNotInheritTheFirstsBuildings</c>
    /// GREEN; after it, that test is RED under the same poison and red RUN ALONE — an observer,
    /// not a victim of another test's pollution — while
    /// <c>ASecondWalkOnTheSameThread_DoesNotInheritTheFirstsScratch</c>,
    /// <c>Distance_BetweenFacingSystems_IsTheirInkAndNoMore</c> and
    /// <c>AMergeIntoALargeSkyline_DoesNotCopyItToReadIt</c> all stay green. So this change
    /// wanted a REMARK and not a net: a second test saying the same thing would only restate it
    /// (HANDOFF §5.4).
    /// </para>
    /// </para>
    /// <para>
    /// ⚠️ AND IT IS TAKEN OUT OF THE DRAWER, so a walk that re-entered would get a list of its
    /// own rather than the one being written. No product path nests today — the walk calls
    /// <see cref="MergeOverlapping"/>, which merges no skyline — and this is what keeps that
    /// from having to stay true.
    /// </para>
    /// </remarks>
    [ThreadStatic]
    private static List<SkylineBuilding>? t_resolveOutput;

    private static List<SkylineBuilding> RentResolveOutput()
    {
        var list = t_resolveOutput;
        if (list is null)
            return new List<SkylineBuilding>();
        t_resolveOutput = null;
        list.Clear();
        return list;
    }

    private static void ReturnResolveOutput(List<SkylineBuilding> list) =>
        t_resolveOutput = list;

    /// <summary>
    /// The scratch one resolve walk lends to every overlap it has to merge: the tail it
    /// lifts out of the result, the boundary points it cuts that tail at, and the segments
    /// it builds back. Cleared at each use, and lent from the thread
    /// (<see cref="RentScratch"/>) so the three lists keep the capacity the last walk grew
    /// them to.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE THREE ARE ALIVE AT THE SAME TIME AND MUST STAY DISTINCT:
    /// <see cref="Overlapping"/> is read as <c>existing</c> throughout
    /// <see cref="MergeBuildingSet"/>, which is writing <see cref="Boundaries"/> and
    /// <see cref="Merged"/> as it goes. Reusing one list for two of those roles is the one
    /// way this can be got wrong, and it does not throw — it silently drops silhouette.
    /// <para>
    /// ⚠️ AND NOTHING MAY OUTLIVE THE WALK. <see cref="MergeBuildingSet"/> RETURNS
    /// <see cref="Merged"/> rather than a copy, so its one caller has to consume it before
    /// the next overlap — it does, by <c>AddRange</c>-ing it into the result on the next
    /// line. A second caller that kept the reference would hold a list the walk goes on
    /// rewriting. <c>SkylineMergeTests</c>' two scratch nets pin the reuse: one compares the
    /// many-overlap walk against merging one at a time, the other against the arithmetic —
    /// a stale buffer that corrupted BOTH paths alike would pass the first and fail the
    /// second.
    /// </para>
    /// </remarks>
    private sealed class ResolveScratch
    {
        internal readonly List<SkylineBuilding> Overlapping = new();
        internal readonly List<double> Boundaries = new();
        internal readonly List<SkylineBuilding> Merged = new();
    }

    /// <summary>
    /// The thread's <see cref="ResolveScratch"/>, kept between walks — the third buffer in
    /// this file to be scoped to the thread rather than to the call, for the reason the other
    /// two give: the walk's own doubling is paid once per thread instead of once per walk.
    /// </summary>
    /// <remarks>
    /// ⚠️ THIS IS THE WHOLE OF THE WALK'S REMAINING ALLOCATION, and nothing else in it was.
    /// Session 416 hoisted these three lists out of the per-OVERLAP loop into one set per
    /// WALK; what a walk then paid was the set itself and the climb of three lists from EMPTY.
    /// MEASURED (session 429, Release, the owner's corpus, 231 books × eight forward
    /// keystrokes, allocated bytes, per region inside the walk): the walk allocated
    /// 40,172,952 B a sweep, <b>0.270% of a keystroke</b>, and the split came to
    /// <b>100.0%</b> of it — the <c>new ResolveScratch()</c> 10,444,936 B (26.0%, 136 B over
    /// 76,801 of 83,538 walks: the object and three empty Lists),
    /// <see cref="ResolveScratch.Overlapping"/> 11,673,752 B (29.1%),
    /// <see cref="ResolveScratch.Merged"/> 11,716,872 B (29.2%) and
    /// <see cref="ResolveScratch.Boundaries"/> 6,337,392 B (15.8%). The two lists that were
    /// ALREADY lent — the result buffer the walk writes and the input it reads — paid
    /// <b>zero</b> in the same run. So the island was not the arithmetic, the sort or the
    /// copies: it was three lists that began every walk at capacity 0.
    /// <para>
    /// ⚠️ THE CLEARING IS AT THE POINT OF USE, not here, and that is why this pool needs no
    /// emptying of its own — unlike <see cref="RentBatch"/> and <see cref="RentMergeInput"/>,
    /// where a stale buffer would be READ. All three lists are <c>Clear</c>ed by the code that
    /// fills them (<see cref="MergeOverlapping"/> for <see cref="ResolveScratch.Overlapping"/>,
    /// <see cref="MergeBuildingSet"/> for the other two) because they were already reused
    /// across the overlaps of ONE walk; reusing them across walks asks nothing new of them.
    /// <c>SkylineMergeTests.ASecondWalkOnTheSameThread_DoesNotInheritTheFirstsScratch</c> is
    /// what says so, and it is a NEW observer rather than a restatement: within a walk the
    /// tail in <see cref="ResolveScratch.Overlapping"/> is this skyline's own earlier
    /// buildings, so a missing clear cannot change a maximum and no net could see it; across
    /// walks it is another skyline's ink. Verified by poison both ways.
    /// </para>
    /// <para>
    /// ⚠️ RENTING TAKES IT OUT OF THE DRAWER, as with the other two: a walk that re-entered
    /// would get a scratch of its own rather than the one being written, and the scratch goes
    /// back only when the walk that took it is finished. The two walks that rent —
    /// <see cref="RebuildKeepingHighest"/> and <see cref="SortAndResolve"/> — run one after
    /// the other inside <see cref="Padded"/>, never nested, and this is what keeps that from
    /// having to stay true.
    /// </para>
    /// <para>
    /// ⚠️ WHAT IT RETAINS is three lists a thread at the high-water mark of that thread's
    /// widest overlap, and a <see cref="SkylineBuilding"/> holds no references, so an unemptied
    /// scratch in the drawer pins nothing — the same argument <see cref="ReturnBatch"/> makes.
    /// </para>
    /// </remarks>
    [ThreadStatic]
    private static ResolveScratch? t_resolveScratch;

    /// <summary>Takes the thread's scratch, or makes the thread's first.</summary>
    private static ResolveScratch RentScratch()
    {
        var scratch = t_resolveScratch;
        if (scratch is null)
            return new ResolveScratch();
        t_resolveScratch = null;
        return scratch;
    }

    /// <summary>Puts a finished walk's scratch back, with its three capacities.</summary>
    private static void ReturnScratch(ResolveScratch scratch) => t_resolveScratch = scratch;

    /// <summary>
    /// The list a resolve walk READS — the combined buildings it is about to sort — lent from
    /// one buffer the thread keeps between walks. Every such list was built, read once by
    /// <see cref="RebuildKeepingHighest"/> and dropped.
    /// </summary>
    /// <remarks>
    /// ⚠️ WHY IT IS NOT A FIELD ON THE SKYLINE, which is the cheaper thing to reach for.
    /// MEASURED (session 421, Release, the owner's corpus, eight forward keystrokes a book,
    /// allocated bytes): <see cref="EndBatch"/> ran 10,284 times over 10,284 DISTINCT
    /// skylines — a ratio of 1.00. Begin/EndBatch is a CONSTRUCTION idiom: a skyline is
    /// batched once and then read, so a buffer hanging off the instance would be allocated by
    /// the one call that could have used it and amortise nothing. <see cref="MergeInternal"/>
    /// reads 4.51 calls an instance, so a field would have served that half and not the other.
    /// One buffer a THREAD serves both: 29,312,416 B of EndBatch input copies and
    /// 291,436,360 B of MergeInternal's — together 2.06% of a keystroke.
    /// <para>
    /// ⚠️ THE THREAD IS THE SCOPE, not the process: two threads resolving two skylines share
    /// nothing, the same property session 416's per-walk <see cref="ResolveScratch"/> locals
    /// have. What it retains is one list a thread, grown to the largest walk that thread has
    /// seen — 596 buildings, 19 KB, over the whole corpus, measured the same run.
    /// </para>
    /// <para>
    /// ⚠️ RENTING TAKES THE LIST OUT OF THE DRAWER. <see cref="RentMergeInput"/> nulls the
    /// slot, so a walk that re-entered would get a list of its own rather than the one being
    /// read, and the buffer goes back only when the walk that took it is finished with it. No
    /// product path nests today — the walk calls <see cref="MergeOverlapping"/>, which merges
    /// no skyline — and this is what keeps that from having to stay true.
    /// </para>
    /// </remarks>
    [ThreadStatic]
    private static List<SkylineBuilding>? t_mergeInput;

    /// <summary>
    /// Takes the thread's merge-input buffer, EMPTY and sized for <paramref name="capacity"/>
    /// buildings.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE EMPTYING IS LOAD-BEARING, unlike the <see cref="ResolveScratch"/> clears session
    /// 416 measured: the walk reads the WHOLE list, so a buffer still holding the previous
    /// walk's buildings would resolve them into this skyline — silhouette out of nowhere, in
    /// a skyline that never saw those grobs. <c>SkylineMergeTests</c> pins it.
    /// </remarks>
    private static List<SkylineBuilding> RentMergeInput(int capacity)
    {
        var list = t_mergeInput;
        if (list is null)
            return new List<SkylineBuilding>(capacity);
        t_mergeInput = null;
        list.Clear();
        list.EnsureCapacity(capacity);
        return list;
    }

    /// <summary>
    /// Resolves <paramref name="input"/> into this skyline and puts the buffer back for the
    /// next walk on this thread. ⚠️ Nothing may hold <paramref name="input"/> afterwards.
    /// </summary>
    private void ResolveFrom(List<SkylineBuilding> input)
    {
        RebuildKeepingHighest(input);
        t_mergeInput = input;
    }

    /// <summary>
    /// Merges a new building with the existing result, handling overlaps.
    /// </summary>
    private void MergeOverlapping(
        List<SkylineBuilding> result, SkylineBuilding newBuilding, ResolveScratch scratch)
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
        var overlapping = scratch.Overlapping;
        overlapping.Clear();
        for (int i = firstOverlap; i < result.Count; i++)
            overlapping.Add(result[i]);
        result.RemoveRange(firstOverlap, result.Count - firstOverlap);

        // Merge newBuilding with overlapping buildings. `merged` IS scratch.Merged — consumed
        // on the next line, before the walk's next overlap rewrites it (ResolveScratch).
        var merged = MergeBuildingSet(overlapping, newBuilding, scratch);
        result.AddRange(merged);
    }

    /// <summary>
    /// Merges a set of overlapping buildings with a new building.
    /// </summary>
    private List<SkylineBuilding> MergeBuildingSet(
        List<SkylineBuilding> existing, SkylineBuilding newBuilding, ResolveScratch scratch)
    {
        var result = scratch.Merged;
        result.Clear();

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
        var boundaryList = scratch.Boundaries;
        boundaryList.Clear();
        boundaryList.EnsureCapacity(existing.Count * 2 + 4);
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
        if (_batch is not null)
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

        var padBuildings = RentPadding(_buildings.Count * 4);

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
        {
            ReturnPadding(padBuildings);
            return this;
        }

        // Resolve overlaps among the padding buildings, in the buffer they were built in.
        SortAndResolve(padBuildings);

        // Merge padding with original. The two lists go into the walk's input together, which
        // is what MergeInternal would have done with a COPY of this skyline as the result's
        // starting point: same buildings, same order, same resolve — and the answer's list is
        // then sized by the answer rather than by this skyline's count.
        var all = RentMergeInput(_buildings.Count + padBuildings.Count);
        all.AddRange(_buildings);
        all.AddRange(padBuildings);
        ReturnPadding(padBuildings);

        var result = new VerticalSkyline(_direction);
        result.ResolveFrom(all);
        return result;
    }

    /// <summary>
    /// The buffer <see cref="Padded"/> builds its padding buildings in — four per building of
    /// the skyline being padded — lent from the thread, like the other three buffers in this
    /// file and for the same reason: the list is written once, read once and dropped.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE EMPTYING IS LOAD-BEARING, as in <see cref="RentMergeInput"/> and unlike the
    /// <see cref="ResolveScratch"/> clears: the whole list is merged into the answer, so a
    /// buffer still holding the previous <see cref="Padded"/>'s buildings would raise this
    /// skyline's silhouette with another one's padding — ink from a grob that is not there.
    /// <c>SkylineMergeTests.ASecondPaddingOnTheSameThread_DoesNotInheritTheFirstsBuildings</c>
    /// is what says so, and it pads twice itself, so it says it whatever ran before it. The
    /// poison also reddens <c>Distance_BetweenFacingSystems_IsTheirInkAndNoMore</c> — but only
    /// in a run where something else padded first; alone, that one stays green under the same
    /// poison. A test downstream of the pollution is a victim of it, not an observer of it.
    /// <para>
    /// ⚠️ WHY THE SKYLINE THAT USED TO OWN IT IS GONE. MEASURED (session 430, Release, the
    /// owner's corpus, 231 books × eight forward keystrokes, allocated bytes, per region inside
    /// <see cref="Padded"/>): the call ran 7,185 times a sweep (3.9 a keystroke) and its own
    /// copying came to 56,812,640 B, <b>0.382% of a keystroke</b> — and the split was
    /// <b>66.5%</b> the intermediate skyline (37,785,800 B: a fresh <see cref="VerticalSkyline"/>
    /// whose list climbed 4-8-16-… as the padding buildings were <c>Add</c>ed into it ONE AT A
    /// TIME, having just been built in a list that was already the right size), 26.0% this
    /// buffer (14,750,136 B) and 7.5% the copy of the original the answer started from
    /// (4,276,704 B). The intermediate held nothing the padding list did not: it was a place to
    /// call <see cref="SortAndResolve"/> from, and that now takes the list.
    /// </para>
    /// <para>
    /// The measured saving is the whole of that plus a little more: A/B over the same corpus,
    /// 8,017,590 / 8,017,598 B a keystroke before and 7,986,759 / 7,986,771 after,
    /// <b>-0.385%</b>. The extra 162,776 B a sweep is the exit: the answer's list used to start
    /// as a copy of this skyline's N buildings and then climb to <c>max(2N, R)</c> when the
    /// resolve kept more than N, and starting it empty lands it on R. ⚠️ A model built from the
    /// AVERAGE N and R predicted 726,736 B for that term, 4.5× the truth — a max does not
    /// commute with an average, and the island itself (which does) came in exact.
    /// </para>
    /// <para>
    /// ⚠️ AND IT IS TAKEN OUT OF THE DRAWER, as with the others: <see cref="Padded"/> does not
    /// nest — the resolve and the merge it runs pad nothing — and this is what keeps that from
    /// having to stay true. What it retains is one list a thread at that thread's widest
    /// padding: 1,076 buildings, 34 KB, over the whole corpus, measured the same run.
    /// </para>
    /// </remarks>
    [ThreadStatic]
    private static List<SkylineBuilding>? t_padding;

    /// <summary>
    /// Takes the thread's padding buffer, EMPTY and sized for <paramref name="capacity"/>
    /// buildings.
    /// </summary>
    private static List<SkylineBuilding> RentPadding(int capacity)
    {
        var list = t_padding;
        if (list is null)
            return new List<SkylineBuilding>(capacity);
        t_padding = null;
        list.Clear();
        list.EnsureCapacity(capacity);
        return list;
    }

    /// <summary>Puts a finished padding buffer back, with its capacity.</summary>
    private static void ReturnPadding(List<SkylineBuilding> list) => t_padding = list;

    /// <summary>
    /// Sorts buildings and resolves overlaps to form a valid skyline.
    /// </summary>
    /// <remarks>
    /// Takes the list rather than this skyline's own, because its one caller
    /// (<see cref="Padded"/>) has the buildings in a lent buffer and used to build a whole
    /// <see cref="VerticalSkyline"/> around them to be able to call this — see
    /// <see cref="t_padding"/> for what that cost. It stays an instance method because the
    /// walk it runs is this class's (<see cref="MergeOverlapping"/>), and the skyline it is
    /// called on is the one being padded: the same direction the intermediate carried.
    /// <para>
    /// ⚠️ ITS RESULT IS THE THREAD'S BUFFER, since session 432, and that was the whole of what
    /// this walk allocated — 0.128% of a keystroke, measured region by region. See
    /// <see cref="t_resolveOutput"/> for the split, for the control that ran beside it and for
    /// the poison that says which test now watches the emptying.
    /// </para>
    /// </remarks>
    private void SortAndResolve(List<SkylineBuilding> buildings)
    {
        if (buildings.Count <= 1)
            return;

        buildings.Sort((a, b) => a.Start.CompareTo(b.Start));

        // THE WALK WRITES TO THE LENT BUFFER, the same one <see cref="RebuildKeepingHighest"/>
        // writes and for the reason <see cref="t_resolveOutput"/> gives. It arrives empty and
        // at the widest capacity this thread has needed, so the list that used to climb
        // 4-8-16-… once per call climbs once per thread.
        var resolved = RentResolveOutput();
        resolved.Add(buildings[0]);
        // One set of scratch buffers for this whole walk, rented as RebuildKeepingHighest
        // rents them and for the same measured reason (ResolveScratch): the padding of a
        // system profile
        // enters here with four buildings per original one, and every one of them overlaps
        // its neighbours by construction.
        ResolveScratch? scratch = null;

        for (int i = 1; i < buildings.Count; i++)
        {
            var b = buildings[i];
            if (b.Start >= resolved[^1].End)
            {
                resolved.Add(b);
            }
            else
            {
                MergeOverlapping(resolved, b, scratch ??= RentScratch());
            }
        }

        if (scratch is not null)
            ReturnScratch(scratch);

        // The answer goes back into the caller's own buffer, which already holds 4N and so
        // never grows for it, and the lent one goes back to the drawer with its capacity.
        buildings.Clear();
        buildings.AddRange(resolved);
        ReturnResolveOutput(resolved);
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
