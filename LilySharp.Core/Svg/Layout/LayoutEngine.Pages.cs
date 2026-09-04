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

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using LilySharp.Core.Rendering;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

internal sealed partial class LayoutEngine
{
    /// <summary>
    /// One system's below-system loose block in the two readings its two consumers need —
    /// see <see cref="LyricReservationBelowSystem"/>, which is the only producer.
    /// </summary>
    /// <param name="Minimum">Every line at its ALIGNMENT MINIMUM: what the page reserves,
    /// what the inter-system floor reads, and what LilyPond puts in its system skyline.</param>
    /// <param name="AtRest">Every line at its spring's FORCE-0 REST LENGTH
    /// <c>max(minimum, ideal)</c>: where the chain that draws the block comes to rest, and
    /// therefore what the CROP has to be sized from. The SAME INSTANCE as
    /// <paramref name="Minimum"/> wherever no spring's ideal rises above its floor, which
    /// is the common sung book.</param>
    internal readonly record struct LooseBlockProfiles(
        VerticalSkyline? Minimum, VerticalSkyline? AtRest);

    /// <summary>The below-system lyric band, through the per-system cache when the session
    /// has one — see <see cref="SystemLayoutCache.GetOrComputeLyricBand"/> for the key's
    /// coverage claim. Null cache (the full-render path) computes live, as everywhere.</summary>
    private static LooseBlockProfiles ComputeLyricBand(
        SystemLayoutCache? cache, int firstMeasureIndex, int measureCount, bool isFirstSystem,
        bool isLastSystem, double indent, double commonShortestDuration,
        Func<LooseBlockProfiles> compute)
        => cache == null
            ? compute()
            : cache.GetOrComputeLyricBand(firstMeasureIndex, measureCount, isFirstSystem,
                isLastSystem, indent, commonShortestDuration, compute);

    /// <summary>
    /// One (system, staff)'s inside-staff spanners out of the per-system lists the room
    /// produced, or an empty set when there are none for that index.
    /// </summary>
    /// <remarks>
    /// ⚠️ STATIC, BECAUSE TWO PASSES ASK. <c>AnnotationLayoutContext.SpannersOf</c> is the
    /// annotation pass's door and <see cref="BuildLooseChainEnds"/> runs BEFORE that context
    /// is built, so the page pass has to reach the same lists without it. Both go through
    /// here rather than each spelling the bounds check — five call sites now depend on the
    /// empty case meaning "no such ink", and that is one decision, not five.
    /// <para>
    /// ⚠️ TWO ABSENT CASES, AND ONLY ONE OF THEM IS REAL. A null <paramref name="bySystem"/>
    /// is the PRELIMINARY pass, which runs before the systems are placed and legitimately has
    /// no room to quote — the same real absence that makes
    /// <c>AnnotationLayoutContext.StaffSkylines</c> nullable. An OUT-OF-RANGE index is not:
    /// the room appends one entry per staff per system, so an index the callers can form is
    /// one this list has. MEASURED 2026-08-04, with the range branch replaced by a throw: the
    /// whole suite (4028 tests, every fixture book) passes without reaching it once.
    /// ⇒ ★ THE RANGE GUARD IS NOT LOAD-BEARING, and it is written down here because that is
    /// the difference between a guard and HANDOFF 7.7's "fallback で握りつぶす": if this ever
    /// returns empty for a range reason, that is a BUG in the indexing and not an absence —
    /// it would silently reserve nothing and leave the suite green, which is exactly how the
    /// defect this whole island closes survived. It is kept rather than thrown because the
    /// consequence of a throw in a per-keystroke preview is worse than an overlap; the
    /// measurement above is what stands in for the compiler.
    /// </para>
    /// </remarks>
    private static MultiStaffLayouter.StaffInsideSpanners SpannersAt(
        IReadOnlyList<List<MultiStaffLayouter.StaffInsideSpanners>>? bySystem,
        int systemIndex, int staffIndex)
        => bySystem != null
           && systemIndex >= 0 && systemIndex < bySystem.Count
           && staffIndex >= 0 && staffIndex < bySystem[systemIndex].Count
            ? bySystem[systemIndex][staffIndex]
            : default;

    /// <summary>
    /// One (system, staff)'s INSIDE-STAFF SKYLINE out of the per-system lists the room
    /// produced — LilyPond's one <c>inside_staff_skylines</c> per VerticalAxisGroup, which
    /// every consumer of a staff's silhouette reads instead of building its own.
    /// A COPY, because the consumers translate it into their own frame.
    /// </summary>
    /// <remarks>
    /// The same two-passes-ask shape as <see cref="SpannersAt"/>, and the same two absent
    /// cases: null is the preliminary pass (no room yet, and the caller falls back to
    /// building its own); an out-of-range index is a bug in the indexing, not an absence.
    /// LILYPOND-REF: lily/axis-group-interface.cc:914-935 inside_staff_skylines.
    /// </remarks>
    private static (VerticalSkyline Up, VerticalSkyline Down)? InsideAt(
        IReadOnlyList<List<(VerticalSkyline Up, VerticalSkyline Down)>>? bySystem,
        int systemIndex, int staffIndex)
    {
        if (bySystem == null
            || systemIndex < 0 || systemIndex >= bySystem.Count
            || staffIndex < 0 || staffIndex >= bySystem[systemIndex].Count)
            return null;
        var (up, down) = bySystem[systemIndex][staffIndex];
        return (SkylineBuilder.Copy(up), SkylineBuilder.Copy(down));
    }

    /// <summary>
    /// One staff's UP half of the room's OWN per-staff skyline — the inside profile with this
    /// staff's placed outside-staff grobs merged onto it, which is what LilyPond's
    /// VerticalAxisGroup publishes as <c>vertical-skylines</c>.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/axis-group-interface.cc:860-985 skyline_spacing.
    /// <para>
    /// The same shape and the same two absent cases as <see cref="InsideAt"/>; only the UP
    /// half, because its readers are the ones spacing something ABOVE the staff, and copying
    /// the DOWN half for them would be a copy nobody looks at.
    /// </para>
    /// </remarks>
    private static VerticalSkyline? OutsideAt(
        IReadOnlyList<List<(VerticalSkyline Up, VerticalSkyline Down)>>? bySystem,
        int systemIndex, int staffIndex)
    {
        if (bySystem == null
            || systemIndex < 0 || systemIndex >= bySystem.Count
            || staffIndex < 0 || staffIndex >= bySystem[systemIndex].Count)
            return null;
        return SkylineBuilder.Copy(bySystem[systemIndex][staffIndex].Up);
    }

    // Route a system's PER-STAFF skylines through the session cache. They became a
    // per-system cost when the placement did (see the loop above); before that one list
    // served the whole score, so there was nothing worth memoising. On a fifty-system
    // score a one-note edit rebuilt all fifty without this. Null cache => direct compute,
    // byte-identical to the non-incremental path.
    private static MultiStaffLayouter.StaffSkylineSet ComputeStaffSkylines(
        SystemLayoutCache? cache, int firstMeasureIndex, int measureCount, bool isFirstSystem,
        bool isLastSystem, double indent, double commonShortestDuration,
        Func<MultiStaffLayouter.StaffSkylineSet> compute)
        => cache == null
            ? compute()
            : cache.GetOrComputeStaffSkylines(firstMeasureIndex, measureCount, isFirstSystem,
                isLastSystem, indent, commonShortestDuration, compute);

    // F3/S5-3c: route a system's skyline through the session cache (the dominant
    // per-system cost, esp. multi-staff). Keyed additionally on systemHeight.
    private static (VerticalSkyline up, VerticalSkyline down) ComputeSystemSkyline(
        SystemLayoutCache? cache, int firstMeasureIndex, int measureCount, bool isFirstSystem,
        bool isLastSystem, double indent, double commonShortestDuration, double systemHeight,
        Func<(VerticalSkyline up, VerticalSkyline down)> compute)
        => cache == null
            ? compute()
            : cache.GetOrComputeSkyline(firstMeasureIndex, measureCount, isFirstSystem, isLastSystem,
                indent, commonShortestDuration, systemHeight, compute);

    /// <summary>
    /// Splits every system's paging silhouette into the two buckets the page BREAKER
    /// prices lines by: the ink that is there because the line starts here, and the ink
    /// that is there anywhere along it.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/constrained-breaking.cc:512-547 fill_line_details, which fills
    /// Line_shape from <c>System::begin_of_line_pure_height</c> /
    /// <c>rest_of_line_pure_height</c>. See <see cref="LineShape"/> for what LilyPond's own
    /// dump says the two buckets hold, and PageBreaker.CalcLineHeights for the deviation:
    /// LilyPond partitions the GROBS by the column they hang off, and this partitions the
    /// SKYLINE by X at the line's first musical column, which is where that membership
    /// lands geometrically.
    /// <para>
    /// ⚠️ THE UNION IS PRESERVED, deliberately. The scalar extents carry terms the paging
    /// skylines do not (whole-line bands, and anything a caller enriched them with), so
    /// whatever the skyline cannot account for is given to BOTH buckets: this can only
    /// close a gap the skyline proves is X-disjoint, never open one. A system with no
    /// skyline, no measures or an empty silhouette gets no shape at all and is priced
    /// exactly as it was before the split existed.
    /// </para>
    /// </remarks>
    private static ImmutableArray<LineShape?>? BuildLineShapes(
        ImmutableArray<SystemLayout> systems,
        List<(VerticalSkyline up, VerticalSkyline down)>? perSystemSkylines,
        List<(double upExtent, double downExtent)> perSystemExtents,
        Func<int, double> sysHeight)
    {
        if (perSystemSkylines == null)
            return null;
        var shapes = ImmutableArray.CreateBuilder<LineShape?>(systems.Length);
        for (int i = 0; i < systems.Length; i++)
        {
            if (i >= perSystemSkylines.Count || i >= perSystemExtents.Count
                || systems[i].Measures.IsDefaultOrEmpty)
            {
                shapes.Add(null);
                continue;
            }
            // Where the line's first measure begins, in the skylines' own X frame. Left of
            // it is the line-start prefix — the clef/key/time and the bar number that sits
            // over them — and that is what hangs off the first breakable column, which is
            // LilyPond's begin bucket. ⚠️ NOT the first musical column's X: a grob ANCHORED
            // there is in LilyPond's rest bucket however far its ink spreads, and a figure
            // row is centred on that column, so splitting at the column itself puts half of
            // every figure into the begin bucket and the two buckets come out identical.
            double xSplit = systems[i].Measures[0].X;
            var (up, down) = perSystemSkylines[i];
            double h = sysHeight(i);
            var ext = perSystemExtents[i];

            // ONE walk per direction. max(begin, rest) is the whole skyline's own extent, so
            // the union below costs no further pass — see MaxHeightsSplitAt.
            var (upBegin, upRest) = up.IsEmpty ? (0.0, 0.0) : up.MaxHeightsSplitAt(xSplit);
            var (downBegin, downRest) = down.IsEmpty ? (0.0, 0.0) : down.MaxHeightsSplitAt(xSplit);
            double beginUp = up.IsEmpty ? 0 : Math.Max(0, upBegin);
            double restUp = up.IsEmpty ? 0 : Math.Max(0, upRest);
            double beginDown = down.IsEmpty ? 0 : Math.Max(0, -downBegin - h);
            double restDown = down.IsEmpty ? 0 : Math.Max(0, -downRest - h);

            // What the skyline could not account for belongs to both buckets.
            double excessUp = Math.Max(0, ext.upExtent - Math.Max(beginUp, restUp));
            double excessDown = Math.Max(0, ext.downExtent - Math.Max(beginDown, restDown));
            shapes.Add(new LineShape(
                beginUp + excessUp, beginDown + excessDown,
                restUp + excessUp, restDown + excessDown));
        }
        return shapes.MoveToImmutable();
    }

    /// <summary>
    /// Per system, the page-break permission AFTER it: the permission the last measure of
    /// the system carries, after LilyPond's <c>min_permission</c> chain with the line's
    /// (<see cref="Measure.EffectivePagePermission"/>). Read off the PRIMARY staff's
    /// measures, the same staff <see cref="SystemBreaker"/> reads the line permissions
    /// from, so the two directives of one keyword (<c>pageBreak</c> forces both) come from
    /// one measure.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/constrained-breaking.cc:530-535 fill_line_details — a line's
    ///   page_permission_ is its last column's page-break-permission.
    /// </remarks>
    internal static ImmutableArray<BreakPermission> PagePermissionsAfterSystems(
        MultiStaffScore score, IReadOnlyList<SystemLayout> systems)
    {
        var measures = score.PrimaryContentStaff.PrimaryVoice.Measures;
        var result = ImmutableArray.CreateBuilder<BreakPermission>(systems.Count);
        foreach (var system in systems)
        {
            int last = system.Measures.IsDefaultOrEmpty ? -1 : system.Measures[^1].MeasureIndex;
            result.Add(last >= 0 && last < measures.Length
                ? measures[last].EffectivePagePermission
                : BreakPermission.Allow);
        }
        return result.MoveToImmutable();
    }

    private (ImmutableArray<PageLayout> pages, ImmutableArray<SystemLayout> systems) CreatePages(
        ImmutableArray<SystemLayout> systems, double headerHeight,
        List<(double upExtent, double downExtent)> perSystemExtents, double systemHeight,
        List<(VerticalSkyline up, VerticalSkyline down)>? perSystemSkylines = null,
        List<double>? perSystemHeights = null,
        List<double>? perSystemBandUps = null,
        List<double>? perSystemCropDown = null,
        ImmutableArray<BreakPermission>? perSystemPagePermissions = null)
    {
        // The down extent the CROP reads: the system's own, raised to clear its loose block
        // standing at REST rather than at its alignment minimum
        // (SystemPlacements.CropDown / LooseBlockProfiles). ⚠️ A MAX AGAINST THE LIVE
        // EXTENT, not a stored answer — the extent grows after the pass that produced the
        // block (the preliminary annotation pass folds in slurs, ties, dynamics and
        // scripts), so anything read from a snapshot of it would be that pass's own ink
        // silently thrown away. Without the list (the single-staff path, and every caller
        // that has no lyric block) this IS the extent, and by construction: no block, no
        // difference between what is reserved and what is drawn.
        double CropDown(int i) =>
            perSystemCropDown != null && i >= 0 && i < perSystemCropDown.Count
                ? Math.Max(perSystemExtents[i].downExtent, perSystemCropDown[i])
                : perSystemExtents[i].downExtent;
        // Whole-line CHORD-SYMBOL row band above a system. It lays out only after the page
        // Y is fixed, so it is absent from the skylines — the skyline distance must be
        // floored by it or adjacent systems overprint it (found by the Greensleeves
        // sample). Local annotations (dynamics, ties, …) are NOT banded: the X-aware
        // skyline distance is the better model for those.
        // ⚠️ THE LYRIC BAND BELOW IS NOT HERE ANY MORE (2026-08-20): its minimum profile is
        // IN the paging skylines (LyricReservationBelowSystem → AddLyricBand), so the
        // X-aware Distance() below reads it the way LilyPond's floor does
        // (page-layout-problem.cc:593-599 build_system_skyline's minimum translations,
        // :625-632 append_system's distance floor) — the scalar spread it under every X
        // (audit/lp-geometry lyrics.band-floor.*). The chord row keeps this shape until a
        // point measures it the same way.
        double BandUp(int i) =>
            perSystemBandUps == null || i >= perSystemBandUps.Count
                ? 0
                : perSystemBandUps[i];
        // Per-system body height, defaulting to the scalar systemHeight when the
        // caller has none (single-staff path, or no hara-kiri) — in that case every
        // entry equals systemHeight, so the result is byte-identical.
        double SysHeight(int i) =>
            perSystemHeights != null && i >= 0 && i < perSystemHeights.Count
                ? perSystemHeights[i]
                : systemHeight;
        // Origin to the staff the spring LEAVES this system from — its LAST spaceable staff.
        // LILYPOND-REF: lily/page-layout-problem.cc:936-939 distribute_loose_lines — its two
        // positions are `last_spaceable_line_translation` and `-solution_[spring_idx]`, so the
        // inter-system distance runs from `last_spaceable_line` to the next system's first,
        // and that line is a fact about the system's ALIGNMENT (:943-944 records it as the
        // walk passes each spaceable staff). Nothing there consults a spring.
        // ★ IT USED TO FORK ON THE SPRING LIST (2026-08-26): `StaffSprings.IsDefaultOrEmpty
        // ? ToFirst : ToLast`, on the account that "a system contributes one chain node per
        // staff SPRING, so one with no springs left never reached its last staff" — measured,
        // it said, on hara-kiri'd book LYRHKG.
        // ⚠️ THAT ACCOUNT NEVER DESCRIBED THE FORK IT GUARDED. Hara-kiri leaves ONE surviving
        // staff, and there the first spaceable staff IS the last, so ToFirst and ToLast are
        // the same number and the branch changes nothing. The branch could only ever differ
        // where a system had TWO spaceable staves and no spring — which is not a hara-kiri
        // state at all, but the state MultiStaffLayouter.StaffSprings' own decline used to
        // produce for `staff / chords / lyrics / staff`. So the fork silently answered in the
        // ORIGIN frame for exactly the books the decline broke, and its remark pointed at a
        // book that never exercised it.
        // ⇒ Now that every consecutive spaceable pair is sprung, `StaffSprings` is empty
        // exactly when the system has at most one spaceable staff, where the two are equal.
        // The fork is gone rather than corrected: a conditional that can only fire on a state
        // the port no longer produces is a trap, not a guard. The invariant it rested on is
        // ASSERTED — InterSystemFloorTests.EverySystemWithTwoSpaceableStaves_CarriesAStaffSpring.
        double OriginToChainEnd(int i) => PageAnchorOffsets(systems[i].StaffGroups).ToLast;
        // An empty score (no systems) has nothing to page; return empty rather than
        // indexing perSystemExtents[0] below.
        if (systems.IsDefaultOrEmpty || perSystemExtents.Count == 0)
            return (ImmutableArray<PageLayout>.Empty, ImmutableArray<SystemLayout>.Empty);

        // LILYPOND-REF: lily/page-layout-problem.cc:1070-1127 build_system_skyline
        // Pass per-system skylines for X-dependent inter-system collision detection
        (ImmutableArray<PageLayout>, ImmutableArray<SystemLayout>) OptimalPages()
        {
            var skylines = perSystemSkylines != null
                ? (ImmutableArray<(VerticalSkyline, VerticalSkyline)>?)perSystemSkylines.ToImmutableArray()
                : null;
            // The refpoint frame every page spring is written in, per system — see
            // PageAnchorOffsets. Computed here because the SELECTION it rests on is
            // ClassifySystem's, which needs to know which rows this port solves; the page
            // layouter is handed the answer for the same reason it is handed the body heights.
            var anchors = systems
                .Select(s => PageAnchorOffsets(s.StaffGroups))
                .ToImmutableArray();
            var shapes = BuildLineShapes(systems, perSystemSkylines, perSystemExtents, SysHeight);
            if (DebugPageBreakingScoring is { } debug)
            {
                // The placed systems' own details — what the page is really broken from —
                // beside the count loop's estimate of the same lines (ChooseSystemCount).
                var placedDetails = new List<SystemDetails>(systems.Length);
                for (int i = 0; i < systems.Length; i++)
                    placedDetails.Add(_pageLayouter.BuildSystemDetails(
                        i, SysHeight(i), perSystemExtents[i].upExtent, perSystemExtents[i].downExtent,
                        shapes is { } sh && i < sh.Length ? sh[i] : null,
                        perSystemPagePermissions is { } pp && i < pp.Length ? pp[i] : BreakPermission.Allow));
                var stacked = PageBreaker.CalcLineHeights(placedDetails);
                for (int i = 0; i < stacked.Count; i++)
                    debug($"  placed sys {i + 1}: {DescribeDetails(stacked[i])}");
            }
            var pages = _pageLayouter.CreatePagesWithOptimalBreaking(
                systems, headerHeight, perSystemExtents.ToImmutableArray(), skylines,
                perSystemBandUps?.ToImmutableArray(), perSystemHeights, anchors,
                shapes,
                perSystemPagePermissions);
            return (pages, pages.SelectMany(p => p.Systems).ToImmutableArray());
        }

        if (_options.UseOptimalPageBreaking && _options.PageHeight > 0)
            return OptimalPages();

        // A FORCED page break (`pageBreak`) after any system but the last is a page count
        // the single-page stack below cannot honour: it stacks everything on one page and
        // only overflows into the breaker. The breaker is the one reader of page
        // permissions (SystemDetails.PagePermission → PageBreaker.IsValidBreak), so the
        // book goes there whether or not it would have fit.
        if (_options.PageHeight > 0 && perSystemPagePermissions is { } permissions)
        {
            for (int i = 0; i + 1 < systems.Length && i < permissions.Length; i++)
                if (permissions[i] == BreakPermission.Force)
                    return OptimalPages();
        }

        // Recalculate Y positions using skyline extents to avoid overlaps
        var pageAnchor = PageAnchorOffsets(systems[0].StaffGroups);
        double skylineY = LayoutUtilities.CalculateFirstSystemY(
            _options.MarginTop, headerHeight, perSystemExtents[0].upExtent,
            pageAnchor.ToFirst, _options.VerticalSpacing.TopSystem);

        // ⚠️ THE SINGLE-PAGE STACK IS THROWN AWAY WHEN THE SCORE OVERFLOWS (the check below
        // hands the whole thing to OptimalPages), and it is not cheap to build: the loop
        // measures each adjacent pair with a horizon padding, and padding a SYSTEM skyline
        // copies the whole silhouette. So decide the overflow FIRST, from a bound that needs
        // no skyline at all.
        // The bound is sound because every increment the loop adds is
        // max(BasicDistance, max(MinimumDistance, …)) — never less than
        // max(BasicDistance, MinimumDistance) whatever the skylines say. If even that
        // minimal stacking does not fit, no skyline reading can make it fit.
        // ⚠️ ONE-SIDED ON PURPOSE: exceeding the bound proves overflow, but not exceeding it
        // proves nothing, so the loop still runs and the real check below still decides.
        // MEASURED (session 191, Release, keystroke allocation): the discarded loop was
        // 249 MB of perf-scripts1k's 337 MB keystroke and 285 MB of perf-fingstack1k's 525 MB
        // — every multi-page book paid for a single-page layout it could never use.
        if (_options.PageHeight > 0 && systems.Length > 1)
        {
            var floorSpec = _options.VerticalSpacing.SystemSystem;
            double floorGap = Math.Max(floorSpec.BasicDistance, floorSpec.MinimumDistance);
            double floorHeight = skylineY + (systems.Length - 1) * floorGap
                + SysHeight(systems.Length - 1)
                + CropDown(systems.Length - 1) + _options.MarginBottom;
            if (floorHeight > _options.PageHeight)
                return OptimalPages();
        }

        var updatedSystems = new List<SystemLayout>();
        for (int i = 0; i < systems.Length; i++)
        {
            updatedSystems.Add(systems[i] with { Y = skylineY });
            if (i < systems.Length - 1)
            {
                // LILYPOND-REF: ly/paper-defaults-init.ly:62-65 system-system-spacing —
                // the pair's padding is 1, its minimum-distance 8 and its basic-distance
                // 12, and page-layout-problem.cc:625-632 uses exactly those. This path
                // used to invent `SystemSpacing * 0.5` (= 4) instead, four times
                // LilyPond's padding, which made the skyline term bind on scores where
                // LilyPond's does not — it was invisible while the skylines were thin,
                // and surfaced the moment the clef joined them.
                var pairSpec = _options.VerticalSpacing.SystemSystem;

                // Reference-to-reference distance to the next system, through the ONE
                // home for the pair minimum — LayoutUtilities.InterSystemPairMinimum
                // (the spring chain's refpoint-frame composition; this path's old
                // origin-frame association was collapsed onto it after the 2026-08-27
                // corpus A/B measured the difference at zero everywhere). Its remarks
                // carry the shared frame prose and the divergence inventory; what
                // stays here is this path's own history and the arguments' whys.
                // ⚠️ UNTIL 2026-08-25 THIS PATH FLOORED IN THE ORIGIN FRAME, and the floor
                // therefore stopped flooring as soon as a system was taller than the numbers
                // themselves: with two staves SysHeight is 13.000000 and BasicDistance is
                // 12.000000, so `Math.Max(12, …)` could not bind and nothing stood under the
                // skyline term at all. A one-staff system hid it exactly — its body is
                // 4.000000 and 12.000000 - 4.000000 is the 8.000000 LilyPond draws — which is
                // why 572 books never moved. MEASURED on the reported book
                // (scratch/ベースタブLy/Untitled-6.lys, `staff melody` twice, user report
                // 2026-08-25): the first system pair read Distance() 15.045000 against a
                // scalar 20.205000 — the next system's rehearsal mark rises into the INDENT
                // column, where the first system has no staff and so no silhouette, which
                // LilyPond does too — and the gap collapsed to 3.050000, printing the mark's
                // box through the instrument name. The same A→B pair later in the same score
                // read 8.200000. LilyPond 2.26.0 answers 8.000000 for that pair and does so
                // at one, two and three staves alike (probes/system-indent-floor.ly).
                bool hasSkylines = perSystemSkylines != null
                    && i + 1 < perSystemSkylines.Count;
                // LILYPOND-REF: lily/page-layout-problem.cc:618-629 — measured
                // with the System grob's skyline-horizontal-padding (1.0), through
                // the pair memo the spring-chain path already stands on (finding
                // 4-6): the skyline instances are the cache's own, so an unchanged
                // pair replays its number instead of re-walking the buildings.
                double dist = hasSkylines
                    ? PageLayouter.InterSystemSkylineDistance(
                        perSystemSkylines![i + 1].up, perSystemSkylines[i].down)
                    : double.NegativeInfinity;
                var aNext = PageAnchorOffsets(systems[i + 1].StaffGroups);
                double originToLastHere = OriginToChainEnd(i);
                double toStaffFrame = aNext.ToFirst - originToLastHere;
                double staffToStaff = LayoutUtilities.InterSystemPairMinimum(
                    hasSkylines, dist,
                    prevBodyHeight: SysHeight(i),
                    prevDownExtent: perSystemExtents[i].downExtent,
                    nextUpExtent: perSystemExtents[i + 1].upExtent,
                    prevOriginToLast: originToLastHere,
                    nextToFirst: aNext.ToFirst,
                    nextHalfFirst: aNext.HalfFirst,
                    // The chord-row band above the NEXT system clears against this
                    // system's full extent (the band spans every X, so the X-disjoint
                    // argument for preferring Distance() does not apply to it).
                    // ⚠️ A BAND IS MEASURED FROM THE STAFF IT HANGS OFF (see
                    // PageAnchorOffsets' remark). The lyric band's mirror-image floor
                    // stood here until 2026-08-20; it is in the skylines now, so
                    // Distance() already prices it — see BandUp's remark.
                    bandUpNext: BandUp(i + 1),
                    // ⚠️ A SYSTEM WITH NO SPACEABLE STAFF HAS NO DOWN SILHOUETTE TO REFINE.
                    // BuildSystemSkylines seeds the down side from the BOTTOM STAFF'S INK,
                    // and a rows-only lead sheet (chords row + lyrics row, no staff) has
                    // none — its content is text the lyric and chord engravers draw. The
                    // lyric reservation does not cover it either: that profile is the rows
                    // hanging BELOW the last spaceable staff, and here there is no such
                    // staff for them to hang below. MEASURED on the reported book
                    // (scratch/ベースタブLy/Untitled-6.lys, user report session 240): the
                    // down skyline reached 1.900 under a body of 10.300, so Distance()
                    // answered 6.395 where the true origin-to-origin need was 14.900, the
                    // 12.000 basic distance won the max below, and the next system's
                    // section label and bar number printed 1.8 into the system above.
                    // ⇒ The SCALAR sum floors the answer for such a system: Distance() is
                    // a refinement of that sum and cannot refine what it cannot see.
                    // ⚠️ This CANNOT reach a book with a staff: the test is exactly
                    // PageAnchorOffsets' own fallback condition, whose remark already says
                    // the nominal anchor stands there only until a corpus point measures
                    // it. This is that point, for the silhouette half of the same hole.
                    scalarFloorForSpaceablelessPrev:
                        ClassifySystem(systems[i].StaffGroups).FirstSpaceable is null,
                    // Divergence ⑴: this path's empty-silhouette fallback converts
                    // with ToFirst (the extents are origin-measured); the chain's
                    // converts with HalfFirst — a different number for a row-led
                    // next system, unmeasured, so each keeps its own.
                    emptySilhouetteHalfFirstFallback: false);

                // LILYPOND-REF: lily/page-layout-problem.cc:625-632 + spring.cc:219-237 —
                // the ink is a FLOOR under the spring, and at force 0 (which is what an
                // unjustified single page runs at) the spring is
                // max(min_distance, ideal_distance). Same shape as PageLayouter's chain.
                double minDistance = Math.Max(
                    pairSpec.MinimumDistance, staffToStaff + pairSpec.Padding);
                skylineY += Math.Max(pairSpec.BasicDistance, minDistance) - toStaffFrame;
            }
        }
        // LILYSHARP-OWN, DECLARED: the CROP. LilyPond always engraves onto the paper; a
        // single Lily# page is sized to its content and only switches to the paper when the
        // content overflows (a deliberate choice — see the page.height note in
        // LpGeometryProbes, where it is a recorded -109.468268 that is not going to close).
        // ⚠️ IT READS `CropDown`, NOT THE DOWN EXTENT, and the difference is the whole of
        // this line's history. The extent reserves a below-system lyric block at its
        // ALIGNMENT MINIMUM (LyricReservationBelowSystem, which is what LilyPond reserves
        // too), while the chain that DRAWS it has been solved into the PAPER since session
        // 291 and comes to rest at the spring's ideal (BuildLooseChainEnds' page-edge
        // branch). Between 2026-08-29 and this line's fix the syllables sat up to
        // (ideal - floor) below the height computed here and the page's bottom white shrank
        // by exactly that: 1.130041 on the ledger's book TBL2, 0.139 on test/lyrics.
        // ⚠️ IT COULD NEVER CLIP, WHICH IS WHY IT WAS A CROP BUG AND NOT A LAYOUT ONE: the
        // growth is bounded by the first spring's ideal 5.5 less its own floor, and every
        // later spring in such a chain has basic-distance 0, so its ideal IS its minimum.
        // ⚠️ AND IT COULD NOT BE CLOSED BY RESERVING THE IDEAL IN THE EXTENT: that same
        // extent is the system's DOWN skyline for system-system spacing, where LilyPond's
        // reservation really is the minimum (page-layout-problem.cc:593-599 hands the
        // skyline builder the minimum translations). One quantity, two consumers that want
        // different numbers — so the producer answers both (LooseBlockProfiles) and only
        // this line reads the second.
        double totalHeight = skylineY + SysHeight(systems.Length - 1)
            + CropDown(systems.Length - 1) + _options.MarginBottom;

        // Auto-pagination: a score that FITS one page keeps this simple layout
        // (byte-identical to the historical single-page output); one that
        // overflows the paper height re-runs through the optimal page breaker
        // and splits across real pages, like LilyPond always does.
        if (_options.PageHeight > 0 && totalHeight > _options.PageHeight)
            return OptimalPages();

        // Stage-4 W2-core: the loop above accumulated each system's top DOWNWARD
        // (device) to size the page; store the final origins as page Y-up (UP from
        // the page bottom) by reflecting through the now-known totalHeight. This is
        // the single-page producer seam — after it, SystemLayout.Y is Y-up and the
        // renderer's YFlip is the only device conversion left.
        var systemsArray = updatedSystems
            .Select(s => s with { Y = totalHeight - s.Y })
            .ToImmutableArray();
        var page = new PageLayout(0, _options.PageWidth, totalHeight, headerHeight, systemsArray);
        return (ImmutableArray.Create(page), systemsArray);
    }

    /// <summary>
    /// One system's alignment as the loose-line pass needs to see it: the two spaceable
    /// staves that bracket everything, the non-spaceable lines it OPENS with, and the ones
    /// that hang below its last staff.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:919-925 and :948-990 —
    /// <c>Page_layout_problem</c> walks the alignment IN ORDER and cuts the non-spaceable
    /// lines into runs between the spaceable ones. This is that walk's classification, and it
    /// is order-based for the same reason: LilyPond never compares two positions to decide
    /// what a line belongs to.
    /// </remarks>
    /// <param name="Trailing">
    /// The independent text ROWS standing below <paramref name="LastSpaceable"/> — lyrics
    /// and chords alike — in alignment order, the elements of this system's own block, which
    /// is the run the chain below it is solved from.
    /// </param>
    /// <remarks>
    /// ★ THERE IS NO LONGER AN `UnmodelledRow` FLAG (2026-08-26), and its removal is the
    /// finding rather than a tidy-up. It said "a text row this port does not place in a
    /// chain stands below a spaceable staff", and its own remark said it would go "when the
    /// last un-modelled arrangement does: a row between two staves and a chords row below
    /// one". Both are modelled now: a row between two staves became a run element on
    /// 2026-08-25, and a CHORDS row became one here, because
    /// <see cref="StaffAffinity.GetSpacingSpec"/> — a complete port of
    /// page-layout-problem.cc:1266-1342 that this chain simply was not calling — already
    /// knew every branch a DOWN-affinity line takes.
    /// <para>
    /// ⚠️ THE FLAG WAS NOT WHAT KEPT THE TWO SIDES AGREEING, which is what it claimed. Two
    /// of its three readers honoured it and <see cref="BuildBetweenRowStaves"/> did not, so
    /// on a book with a chords row AND a lyrics row between two staves the chain solved a
    /// run it had been told not to model — the lyric row landed where a run of one puts it
    /// and the two rows were drawn on one line. A flag that half the readers consult is
    /// worse than no flag: it reads like a guarantee while being an option.
    /// </para>
    /// <para>
    /// ★ AN OSSIA USED TO BE A THIRD REASON TO DECLINE and is not one since 2026-07-28: it is
    /// a spaceable staff, so it BRACKETS runs instead of being one. The flag that carried it
    /// (<c>HasOssia</c>) went the same way, with its three readers.
    /// </para>
    /// </remarks>
    private readonly record struct SystemAlignment(
        StaffLayout? FirstSpaceable,
        StaffLayout? LastSpaceable,
        ImmutableArray<StaffLayout> Leading,
        ImmutableArray<int> Trailing,
        ImmutableArray<(int Anchor, int Row)> Between);

    /// <summary>Cuts one system's placed staves into that classification.</summary>
    private static SystemAlignment ClassifySystem(ImmutableArray<StaffGroupLayout> groups)
    {
        StaffLayout? first = null, last = null;
        var leading = ImmutableArray.CreateBuilder<StaffLayout>();
        var trailing = ImmutableArray.CreateBuilder<int>();
        // The rows that turned out to stand BETWEEN two spaceable staves, paired with the
        // staff they hang under. They are collected in `trailing` first and moved here the
        // moment a spaceable staff appears below them, because which of the two a row is
        // cannot be known until the walk reaches the next staff -- the same reason LilyPond
        // cuts its runs in one pass (page-layout-problem.cc:919-925).
        var between = ImmutableArray.CreateBuilder<(int Anchor, int Row)>();
        int anchor = -1;

        foreach (var group in groups)
        {
            if (group.Staves.IsDefaultOrEmpty) continue;
            foreach (var st in group.Staves)
            {
                // Hara-kiri leaves a hidden staff at the current Y with zero height, so it
                // neither draws nor takes room — LilyPond's filter_dead_elements (:589).
                if (st.IsHidden) continue;
                // LILYPOND-REF: lily/page-layout-problem.cc:1173-1177 Page_layout_problem::is_spaceable
                // — a line is spaceable exactly when it declares no `staff-affinity`, and that
                // ONE property is the whole question. Nothing there reads a magnification (a
                // small staff is a staff) or a kind of context.
                // ⚠️ IT USED TO BE ASKED AS A TYPE ENUMERATION — the score's set of text-row
                // indices, handed in — which is the same answer by a different route and only
                // for as long as the two lists agree. An ossia is what they disagreed about:
                // excluding it put an ossia that LEADS a system outside the page's chain
                // entirely, the anchor fell through to the staff the ossia decorates, and the
                // ossia was drawn ABOVE the page's head, 2.123312 into the top margin
                // (audit/lp-geometry page.ossia-pair.compressed.first-staff-refpoint, book OSSK).
                if (!StaffAffinity.IsSpaceable(st.StaffAffinity))
                {
                    if (first is null) { leading.Add(st); continue; }
                    // ★ EVERY NON-SPACEABLE LINE IS AN ELEMENT OF ITS RUN (2026-08-26), which
                    // is what page-layout-problem.cc:919-925 and :948-990 collect: the walk
                    // pushes a line onto `loose_lines` because it is not spaceable, and asks
                    // nothing else about it. It USED TO ASK — a line that was not a LYRICS row
                    // set `unmodelled` and was dropped from the run — and dropping it did not
                    // stop the run being solved, because BuildBetweenRowStaves never read the
                    // flag. So a book written `staff / chords / lyrics / staff` had its lyric
                    // row solved as the ONLY occupant of a run that had two, and the two rows
                    // were engraved on one line (user report 2026-08-26; the same book the
                    // remarks on ComputeBetweenStavesEnd and BuildBetweenRowStaves name).
                    // ⚠️ WHAT MAKES THIS SAFE IS NOT THIS LINE, it is that the gap SPECS are
                    // now per-line — LyricEngraver.BuildChainPrefix asks
                    // StaffAffinity.GetSpacingSpec for each pair — so a DOWN-affinity line in
                    // the run takes its own branches (:1284-1294 and :1313-1337) instead of
                    // the Lyrics numbers a score-wide spec would have handed it.
                    trailing.Add(st.StaffIndex);
                    continue;
                }
                // A spaceable staff below a row means that row stood BETWEEN two of them.
                // ★ THAT USED TO BE A REASON TO DECLINE FOR THE WHOLE SYSTEM (2026-08-25).
                // It is the OTHER call of distribute_loose_lines -- the one handed two
                // spaceable positions of ONE system (page-layout-problem.cc:936-939) -- so the
                // run is a run like any other and the rows in it are its elements. They are
                // kept, keyed by the staff they hang under, and LyricEngraver walks them.
                if (trailing.Count > 0)
                {
                    foreach (int row in trailing)
                        between.Add((anchor, row));
                    trailing.Clear();
                }
                anchor = st.StaffIndex;
                double down = -st.Y;
                if (first is null || down < -first.Y) first = st;
                if (last is null || down > -last.Y) last = st;
            }
        }

        return new SystemAlignment(
            first, last, leading.ToImmutable(), trailing.ToImmutable(),
            between.ToImmutable());
    }

    /// <summary>
    /// How far DOWN from a system's ORIGIN its first and its last SPACEABLE staff's
    /// REFERENCE POINTS sit — the two anchors every page spring is written against.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:896-901 — <c>solution_[spring_idx]</c> is the
    /// first spaceable staff's position and the system's origin is that plus
    /// <c>min_offsets[0]</c>; :1116 and :1126 are the same conversion at the other end
    /// (<c>last_spaceable_dy</c>). Every page distance LilyPond writes — top-system-spacing to
    /// the first one, system-system-spacing between them, last-bottom-spacing under the last —
    /// runs between reference points, while Lily# stacks systems by their ORIGIN (the first
    /// element's top line). This is that conversion, and it is one function because it was
    /// three: <c>_options.StaffHeight / 2.0</c> stood in for it in
    /// <see cref="Layout(LilySharp.Core.Svg.Model.MultiStaffScore,
    /// LineBreakSolutions, SystemLayoutCache, MeasureSpringData[],
    /// System.Nullable{double})"/>,
    /// in <see cref="CreatePages"/> and in <c>PageLayouter</c>, and which
    /// of the three was live depended on the paper regime (HANDOFF 5.2.1 (2)).
    /// <para>
    /// ⚠️ A NOMINAL HALF STAFF IS NOT THIS QUANTITY. A staff's refpoint is the middle of its
    /// OWN line span, so it is 2.000000 below the top line only for a five-line staff: a
    /// six-string tab staff's is 3.750000 below (its lines span (6-1) × 1.5). MEASURED against
    /// LilyPond, which puts the first staff of a tab page exactly where it puts the first staff
    /// of a notation page — audit/lp-geometry <c>page.tab-only.first-staff-refpoint</c> against
    /// its control <c>page.tab-control.first-staff-refpoint</c>, both 11.690551.
    /// </para>
    /// <para>
    /// ⚠️ THE SELECTION IS <see cref="ClassifySystem"/>'s, not "the outer layouts": a hidden
    /// (hara-kiri'd) staff and a text row are both there in the array and neither is what a
    /// page spring attaches to. MEASURED both ways — taking the outer layouts regresses
    /// <c>hara-kiri.wide-ink.lone-staff-to-next-system</c> by 2.000000 (it picks the hidden
    /// staff) and four <c>lyrics.hara-kiri.grouper.*</c> entries with it.
    /// </para>
    /// <para>
    /// ⚠️ LILYSHARP-OWN: THE FALLBACK. A system with no spaceable staff at all — a chords-only
    /// lead sheet — keeps the nominal half staff, because LilyPond's anchor there is a
    /// ChordNames group's own reference point (its baseline) and no corpus point measures a
    /// page anchor over a staffless system. It goes when such a point exists.
    /// </para>
    /// </remarks>
    /// <param name="groups">One system's placed staff groups.</param>
    /// <returns>
    /// <c>ToFirst</c>/<c>ToLast</c>: origin to that staff's refpoint. <c>HalfFirst</c>/
    /// <c>HalfLast</c>: that staff's OWN half span — the distance from its own top (bottom)
    /// line to its refpoint, which is NOT the same number as soon as a loose line stands
    /// between the origin and the staff.
    /// ⚠️ LILYSHARP-OWN: THE SECOND PAIR HAS NO LILYPOND COUNTERPART, and it exists because a
    /// Lily#-only quantity does. LilyPond has one frame — <c>min_offsets</c> off the system's
    /// own reference point, every element in it (page-layout-problem.cc:896-901) — and no
    /// "band": a loose line is IN the skyline it is spaced against. ★ THE LYRIC BAND BELOW
    /// became such an element on 2026-08-20 (its minimum profile rides the paging skylines —
    /// <c>LyricReservationBelowSystem</c>), so the DOWN half of this pair lost its band
    /// consumer; what keeps the pair alive is the CHORD-ROW band above, still estimated
    /// outside the skyline and measured from the staff. It goes when that one is an element
    /// too.
    /// ⚠️ Quantities floored by a whole-line BAND need the
    /// half span, because a band is already measured from the staff it hangs off; quantities
    /// floored by a skyline or a scalar extent need the origin distance, because those are
    /// measured from the origin. Mixing them double-counts the band — MEASURED, it put
    /// <c>lyrics.chord-row.between-systems.system-gap</c> 1.883400 over LilyPond's 12.000000.
    /// </returns>
    /// <summary>
    /// How far a system's ORIGIN stands above its first SPACEABLE staff's TOP LINE — the
    /// rows the alignment stacked over that staff, and exactly 0 for every system whose
    /// topmost element IS the staff.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:1120-1122 <c>build_system_skyline</c>'s
    /// closing <c>up->raise (-first_spaceable_dy)</c>. LilyPond re-anchors the SKYLINE once and every consumer
    /// then reads one frame; Lily# keeps its silhouette in the origin frame, so the same
    /// raise has to be made by each consumer that mixes an origin-framed quantity with a
    /// STAFF-framed one. This is that raise, named once.
    /// <para>
    /// ⚠️ IT IS THE DIFFERENCE, NOT <c>ToFirst</c>. A quantity already measured from the
    /// staff (a mark's protrusion, <see cref="EstimateAboveStaffExtents"/>'s constants)
    /// needs only the ROWS added; a quantity measured from the origin (a skyline extent)
    /// needs the whole <c>ToFirst</c>. Handing either the other one is the double count
    /// <c>PageAnchorOffsets</c>' own remark measured at 1.883400 over LilyPond.
    /// </para>
    /// </remarks>
    private double RowsAboveFirstStaff(ImmutableArray<StaffGroupLayout> groups)
    {
        var a = PageAnchorOffsets(groups);
        return a.ToFirst - a.HalfFirst;
    }

    private (double ToFirst, double ToLast, double HalfFirst, double HalfLast) PageAnchorOffsets(
        ImmutableArray<StaffGroupLayout> groups)
    {
        double nominal = _options.StaffHeight / 2.0;
        if (groups.IsDefaultOrEmpty)
            return (nominal, nominal, nominal, nominal);
        var alignment = ClassifySystem(groups);
        return alignment.FirstSpaceable is { } first && alignment.LastSpaceable is { } last
            ? (-MultiStaffLayouter.StaffRefpoint(first), -MultiStaffLayouter.StaffRefpoint(last),
               first.Height / 2.0, last.Height / 2.0)
            : (nominal, nominal, nominal, nominal);
    }

}
