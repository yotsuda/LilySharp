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
    private static LooseBlockProfiles ComputeLyricBand<TState>(
        SystemLayoutCache? cache, int firstMeasureIndex, int measureCount, bool isFirstSystem,
        bool isLastSystem, double indent, double commonShortestDuration,
        TState state, Func<TState, LooseBlockProfiles> compute)
        => cache == null
            ? compute(state)
            : cache.GetOrComputeLyricBand(firstMeasureIndex, measureCount, isFirstSystem,
                isLastSystem, indent, commonShortestDuration, state, compute);

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
    /// THE STORED PAIR, READ-ONLY: a consumer that translates a side into its own frame
    /// copies THAT side as it raises it (<see cref="VerticalSkyline.RaisedCopy"/>), or merges
    /// it raised without a copy (<see cref="VerticalSkyline.MergeRaised"/>).
    /// </summary>
    /// <remarks>
    /// The same two-passes-ask shape as <see cref="SpannersAt"/>, and the same two absent
    /// cases: null is the preliminary pass (no room yet, and the caller falls back to
    /// building its own); an out-of-range index is a bug in the indexing, not an absence.
    /// LILYPOND-REF: lily/axis-group-interface.cc:914-935 inside_staff_skylines.
    /// <para>
    /// ⚠️ IT HANDED OUT A COPY OF BOTH SIDES UNTIL SESSION 518, and every consumer read ONE
    /// side: the above stacker copied the pair a second time on top and raised the up side,
    /// the figured-bass drop raised the down side, the chord row the up side. On the
    /// keystroke path that is the memoized stacker's whole live cost — 7,989 B a tracker
    /// build, 1.26% of a render (Lab sessions/p518/steps-head4.txt). Now nothing here is
    /// copied for a reader, and the writers of these tables are unchanged.
    /// </para>
    /// </remarks>
    private static (VerticalSkyline Up, VerticalSkyline Down)? InsideAt(
        IReadOnlyList<List<(VerticalSkyline Up, VerticalSkyline Down)>>? bySystem,
        int systemIndex, int staffIndex)
    {
        if (bySystem == null
            || systemIndex < 0 || systemIndex >= bySystem.Count
            || staffIndex < 0 || staffIndex >= bySystem[systemIndex].Count)
            return null;
        return bySystem[systemIndex][staffIndex];
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
    private static MultiStaffLayouter.StaffSkylineSet ComputeStaffSkylines<TState>(
        SystemLayoutCache? cache, int firstMeasureIndex, int measureCount, bool isFirstSystem,
        bool isLastSystem, double indent, double commonShortestDuration,
        TState state, Func<TState, MultiStaffLayouter.StaffSkylineSet> compute)
        => cache == null
            ? compute(state)
            : cache.GetOrComputeStaffSkylines(firstMeasureIndex, measureCount, isFirstSystem,
                isLastSystem, indent, commonShortestDuration, state, compute);

    // F3/S5-3c: route a system's skyline through the session cache (the dominant
    // per-system cost, esp. multi-staff). Keyed additionally on systemHeight.
    private static (VerticalSkyline up, VerticalSkyline down) ComputeSystemSkyline<TState>(
        SystemLayoutCache? cache, int firstMeasureIndex, int measureCount, bool isFirstSystem,
        bool isLastSystem, double indent, double commonShortestDuration, double systemHeight,
        TState state, Func<TState, (VerticalSkyline up, VerticalSkyline down)> compute)
        => cache == null
            ? compute(state)
            : cache.GetOrComputeSkyline(firstMeasureIndex, measureCount, isFirstSystem, isLastSystem,
                indent, commonShortestDuration, systemHeight, state, compute);

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
        Func<int, double> sysHeight,
        PreliminaryPass? breakerView = null)
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
            // The BREAKER's up half: above-staff marks at their pure heights, not stacked
            // (PreliminaryPass.BreakerUp) — these shapes are read by the breaker alone.
            if (breakerView is { } bv)
            {
                up = bv.BreakerUp(i, up);
                down = bv.BreakerDown(i, down);
                ext = (bv.BreakerUpExtent(i, ext.upExtent), bv.BreakerDownExtent(i, ext.downExtent, h));
            }

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
        MultiStaffScore score, ImmutableArray<SystemLayout> systems, HeaderBand? header,
        List<(double upExtent, double downExtent)> perSystemExtents, double systemHeight,
        List<(VerticalSkyline up, VerticalSkyline down)>? perSystemSkylines = null,
        List<double>? perSystemHeights = null,
        List<double>? perSystemBandUps = null,
        List<double>? perSystemCropDown = null,
        ImmutableArray<BreakPermission>? perSystemPagePermissions = null,
        PreliminaryPass? breakerView = null)
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
        // Per-system body height, defaulting to the scalar systemHeight when the
        // caller has none (single-staff path, or no hara-kiri) — in that case every
        // entry equals systemHeight, so the result is byte-identical.
        double SysHeight(int i) =>
            perSystemHeights != null && i >= 0 && i < perSystemHeights.Count
                ? perSystemHeights[i]
                : systemHeight;
        // An empty score (no systems) has nothing to page; return empty rather than
        // indexing perSystemExtents[0] below.
        if (systems.IsDefaultOrEmpty || perSystemExtents.Count == 0)
            return (ImmutableArray<PageLayout>.Empty, ImmutableArray<SystemLayout>.Empty);

        // LILYPOND-REF: lily/page-layout-problem.cc:1070-1127 build_system_skyline
        // Pass per-system skylines for X-dependent inter-system collision detection
        (ImmutableArray<PageLayout>, ImmutableArray<SystemLayout>) OptimalPages(
            PageLayouter? layouter = null, bool onePage = false)
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
            // ...and the BREAKER's frame, per system: the same anchors with the last one
            // taken back up to the pairs' alignment minimum (BreakerFrame).
            // With LilyPond's own origin for each line over it: its staves' pure top
            // (SystemDetails.AlignmentOriginUp).
            var frames = systems.Select((s, i) => BreakerFrame(s) with
            {
                PureTopUp = breakerView is { } bv && perSystemSkylines is { } ps && i < ps.Count
                    ? bv.BreakerPureTop(i, ps[i].up)
                    : 0,
            }).ToImmutableArray();
            var shapes = BuildLineShapes(systems, perSystemSkylines, perSystemExtents, SysHeight,
                breakerView);
            double BreakerUpExtent(int i) =>
                breakerView?.BreakerUpExtent(i, perSystemExtents[i].upExtent)
                ?? perSystemExtents[i].upExtent;
            double BreakerDownExtent(int i) =>
                breakerView?.BreakerDownExtent(i, perSystemExtents[i].downExtent, SysHeight(i))
                ?? perSystemExtents[i].downExtent;
            // The extents the breaker prices each line by (PreliminaryPass.BreakerUpExtent).
            double[]? breakerUpExtents = null, breakerDownExtents = null;
            if (breakerView is not null)
            {
                int n = Math.Min(systems.Length, perSystemExtents.Count);
                breakerUpExtents = new double[n];
                breakerDownExtents = new double[n];
                for (int i = 0; i < n; i++)
                {
                    breakerUpExtents[i] = BreakerUpExtent(i);
                    breakerDownExtents[i] = BreakerDownExtent(i);
                }
            }
            if (DebugPageBreakingScoring is { } debug)
            {
                // The placed systems' own details — what the page is really broken from —
                // beside the count loop's estimate of the same lines (ChooseSystemCount).
                var placedDetails = new List<SystemDetails>(systems.Length);
                for (int i = 0; i < systems.Length; i++)
                    placedDetails.Add(_pageLayouter.BuildSystemDetails(
                        SysHeight(i), BreakerUpExtent(i), BreakerDownExtent(i),
                        shapes is { } sh && i < sh.Length ? sh[i] : null,
                        perSystemPagePermissions is { } pp && i < pp.Length ? pp[i] : BreakPermission.Allow,
                        frames[i]));
                var stacked = PageBreaker.CalcLineHeights(placedDetails);
                for (int i = 0; i < stacked.Count; i++)
                    debug($"  placed sys {i + 1}: {DescribeDetails(stacked[i])}");
            }
            var pages = (layouter ?? _pageLayouter).CreatePagesWithOptimalBreaking(
                systems, header, perSystemExtents.ToImmutableArray(), skylines,
                perSystemBandUps?.ToImmutableArray(), perSystemHeights, anchors,
                shapes,
                // One page asks no breaker, so a snippet's pageBreak is a line break and a
                // systemsPerPage cap has nothing to cap (SnippetLayoutTests).
                perSystemPagePermissions,
                frames,
                onePage,
                score.TextMetrics,
                breakerUpExtents,
                breakerDownExtents);
            return (pages, pages.SelectMany(p => p.Systems).ToImmutableArray());
        }

        // LilyPond's one-page breaking (lily/one-page-breaking.cc:63-183 One_page_breaking::solve):
        // the paper made 1e6 tall for the solve, ragged, every system on the one page — the crop
        // below cuts it to the music.
        (ImmutableArray<PageLayout>, ImmutableArray<SystemLayout>) OnePage()
            => OptimalPages(new PageLayouter(_options with
            {
                PageHeight = 1e6,
                PageBreaking = _options.PageBreaking with { RaggedBottom = true },
            }), onePage: true);

        if (_options.UseOptimalPageBreaking && _options.PageHeight > 0)
            return OptimalPages();

        // ONE PAGE OR THE PAPER — and the page chain places the systems either way
        // (PageLayouter.CreatePagesWithOptimalBreaking).
        // ★ UNTIL SESSION 829 A SCORE THAT FIT ONE PAGE TOOK A SECOND ROUTE: a loop here stacked
        // its systems at force 0 itself, and only an overflow went to the chain — two
        // implementations of one page (HANDOFF §2 D). MEASURED before folding it (Lab
        // sessions/p829/chain, 998 books, each score the loop placed solved both ways): 682 of 682
        // one-page scores came out of the chain with every system where the loop had put it
        // (|d| < 1e-6), and the one difference was audit/lpreg/perf-sd40, which the loop fitted
        // 0.55 inside the paper while the chain's breaker, pricing the last-bottom spring the loop
        // never had, made two pages — as LilyPond 2.26.0 does (12 systems and 1). A snippet (no
        // paper height) is LilyPond's one-page breaking through the same chain (OnePage).
        var (pages, placed) = _options.PageHeight > 0 ? OptimalPages() : OnePage();

        // LILYSHARP-OWN, DECLARED: the CROP. LilyPond always engraves onto the paper; a lone Lily#
        // page the chain laid out at rest is cut to its content, and one it had to compress keeps
        // the paper, as before the fold (the loop handed exactly those to the chain). LilyPond's
        // one-page breaking cuts too, but under its last-bottom spacing
        // (lily/one-page-breaking.cc:125-176 One_page_breaking::solve); this cut is the margin's.
        // See the page.height note in LpGeometryProbes (−109.468268, not going to close).
        if (pages.Length != 1 || !(pages[0].Force >= 0))
            return (pages, placed);
        var onPaper = pages[0];
        int last = onPaper.Systems.Length - 1;
        // ⚠️ THE CUT READS `CropDown`, NOT THE DOWN EXTENT, and the difference is the whole of
        // this line's history. The extent reserves a below-system lyric block at its ALIGNMENT
        // MINIMUM (LyricReservationBelowSystem, which is what LilyPond reserves too), while the
        // chain that DRAWS it comes to rest at the spring's ideal (BuildLooseChainEnds'
        // page-edge branch). Between 2026-08-29 and the fix the syllables sat up to
        // (ideal − floor) below the cut and the page's bottom white shrank by exactly that:
        // 1.130041 on the ledger's book TBL2, 0.139 on test/lyrics. It could not be closed by
        // reserving the ideal in the extent: that extent is the system's DOWN skyline for
        // system-system spacing, where LilyPond's reservation really is the minimum
        // (page-layout-problem.cc:593-599) — so the producer answers both (LooseBlockProfiles)
        // and only this line reads the second.
        // The chain's Y is Y-up from the paper's bottom; the cut keeps the top, so each system
        // keeps its distance below it and the page ends under the last one.
        double totalHeight = onPaper.Height - onPaper.Systems[last].Y + SysHeight(last)
            + CropDown(last) + _options.MarginBottom;
        var systemsArray = onPaper.Systems
            .Select(s => s with { Y = totalHeight - (onPaper.Height - s.Y) })
            .ToImmutableArray();
        // The snippet page (LayoutOptions.CropWidth) is as wide as its widest system's
        // drawn staff — the final barline or the courtesy suffix past it, the ONE reading
        // the renderer ends the lines at — or its title / composer row when that is wider
        // (HeaderBand.Width: the title is centred on the page, the composer set against
        // the right margin, so either needs the page at least its own width), plus the
        // two margins; every other page is the paper's width.
        double pageWidth = _options.PageWidth;
        if (_options.CropWidth)
        {
            double widest = header?.Width ?? 0;
            foreach (var s in systemsArray)
            {
                var (_, notationRight, tabRight) = Rendering.SharedRenderer.StaffRightEdges(score, s);
                widest = Math.Max(widest, Math.Max(notationRight, tabRight));
            }
            pageWidth = _options.MarginLeft + widest + _options.MarginRight;
        }
        var page = onPaper with { Width = pageWidth, Height = totalHeight, Systems = systemsArray };
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

    /// <summary>True when a system has no spaceable staff — a rows-only lead sheet (chords and
    /// lyrics rows, no staff) — so it has no down silhouette for the next pair to read.</summary>
    /// <remarks>A system built without its groups (a unit test's) answers no: nothing says it
    /// lacks a staff, and its anchors are the nominal staff's (<see cref="PageAnchorOffsets"/>).</remarks>
    internal static bool HasNoSpaceableStaff(ImmutableArray<StaffGroupLayout> groups)
        => !groups.IsDefaultOrEmpty && ClassifySystem(groups).FirstSpaceable is null;

    /// <summary>Cuts one system's placed staves into that classification.</summary>
    private static SystemAlignment ClassifySystem(ImmutableArray<StaffGroupLayout> groups)
    {
        StaffLayout? first = null, last = null;
        // ⚠️ THE THREE BUILDERS WAIT FOR THEIR FIRST ELEMENT. `ImmutableArray.CreateBuilder<T>()`
        // is not `new List<T>()`: it lays out its first block — 88 B for a reference element —
        // before a single Add, and this method runs 103 times a keystroke over the reader's
        // corpus while all three stay EMPTY in every one of them (session 447: a system whose
        // staves are all spaceable classifies into nothing but `first` and `last`).
        ImmutableArray<StaffLayout>.Builder? leading = null;
        ImmutableArray<int>.Builder? trailing = null;
        // The rows that turned out to stand BETWEEN two spaceable staves, paired with the
        // staff they hang under. They are collected in `trailing` first and moved here the
        // moment a spaceable staff appears below them, because which of the two a row is
        // cannot be known until the walk reaches the next staff -- the same reason LilyPond
        // cuts its runs in one pass (page-layout-problem.cc:919-925).
        ImmutableArray<(int Anchor, int Row)>.Builder? between = null;
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
                    if (first is null)
                    {
                        (leading ??= ImmutableArray.CreateBuilder<StaffLayout>()).Add(st);
                        continue;
                    }
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
                    (trailing ??= ImmutableArray.CreateBuilder<int>()).Add(st.StaffIndex);
                    continue;
                }
                // A spaceable staff below a row means that row stood BETWEEN two of them.
                // ★ THAT USED TO BE A REASON TO DECLINE FOR THE WHOLE SYSTEM (2026-08-25).
                // It is the OTHER call of distribute_loose_lines -- the one handed two
                // spaceable positions of ONE system (page-layout-problem.cc:936-939) -- so the
                // run is a run like any other and the rows in it are its elements. They are
                // kept, keyed by the staff they hang under, and LyricEngraver walks them.
                if (trailing is { Count: > 0 })
                {
                    between ??= ImmutableArray.CreateBuilder<(int Anchor, int Row)>();
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
            first, last, leading?.ToImmutable() ?? [], trailing?.ToImmutable() ?? [],
            between?.ToImmutable() ?? []);
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

    /// <summary>
    /// The frame the page BREAKER prices one placed system in — <see cref="PageAnchorOffsets"/>'
    /// two refpoints with the last taken back up by <see cref="StaffSpringCompression"/>, so
    /// the pair stands at the alignment minimum LilyPond's Line_details read it at.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/constrained-breaking.cc:562 fill_line_details — refpoint_extent_ is
    /// <c>pure_refpoint_extent</c>, the outer spaceable staves at
    /// <c>get_pure_minimum_translations</c> (system.cc:864-890); the body the same details
    /// price (<c>full_height ()</c>) is at those translations too. Lily# lays a system out at
    /// its basic-distances first and springs the pairs on the page afterwards, so the placed
    /// system is the STRETCHED one and the breaker's is recovered by subtracting the squeeze.
    /// A system with one spaceable staff has no spring and no squeeze; a plain five-line
    /// staff's frame is the nominal (2, 2, 0) the breaker always priced.
    /// </remarks>
    private BreakerRefpointFrame BreakerFrame(SystemLayout system)
    {
        var a = PageAnchorOffsets(system.StaffGroups);
        double compression = StaffSpringCompression(system);
        return new BreakerRefpointFrame(a.ToFirst, a.ToLast - compression, compression);
    }

    /// <summary>
    /// How much taller a placed system's body is than its alignment minimum: over its staff
    /// springs, the drawn refpoint distance less the spring's floor, summed.
    /// </summary>
    /// <remarks>
    /// The placement puts a spaceable pair at <c>max (basic-distance, minimum)</c> (the
    /// alignment's answer at force 0 — MultiStaffLayouter's pair branch), and
    /// <see cref="StaffSpring.MinimumDistance"/> IS that alignment minimum
    /// (StaffSprings.AddSpring: "the alignment's own minimum translation for the pair"), so
    /// the difference per pair is exactly what LilyPond's pure translations leave out. On a
    /// staff-plus-tab system it is 9 − 8 = 1.0 (audit/lp-geometry
    /// page.staff-tab.compressed.staves-on-first-page's other term). Clamped at 0 per pair:
    /// a pair drawn on its floor contributes nothing.
    /// ★ THE FLOOR IS THE BREAKER'S, <see cref="StaffSpring.PureMinimumDistance"/> (session
    /// 856): a tie or a tuplet bracket that holds the drawn pair apart is in no pure height, so
    /// the line is priced without it.
    /// </remarks>
    private static double StaffSpringCompression(SystemLayout system)
    {
        if (system.StaffSprings.IsDefaultOrEmpty || system.StaffGroups.IsDefaultOrEmpty)
            return 0;
        var byIndex = RentStaffIndex();
        foreach (var group in system.StaffGroups)
            foreach (var staff in group.Staves)
                byIndex[staff.StaffIndex] = staff;
        double total = 0;
        foreach (var spring in system.StaffSprings)
        {
            if (!byIndex.TryGetValue(spring.UpperStaffIndex, out var upper)
                || !byIndex.TryGetValue(spring.LowerStaffIndex, out var lower))
                continue;
            double drawn = MultiStaffLayouter.StaffRefpoint(upper) - MultiStaffLayouter.StaffRefpoint(lower);
            total += Math.Max(0, drawn - spring.PureMinimumDistance);
        }
        GiveStaffIndex(byIndex);
        return total;
    }

    /// <summary>
    /// The staff-by-index map <see cref="StaffSpringCompression"/> reads its pairs out of, lent
    /// from one dictionary the thread keeps between systems.
    /// </summary>
    /// <remarks>
    /// MEASURED (session 457, Release, the reader's corpus, eight forward keystrokes a book):
    /// 35.69 systems a keystroke holding exactly 2.00 staves each (max 2), and all 65,948 of the
    /// dictionaries built were unreachable by the time the render that built them returned - a
    /// weak handle a build and a forced blocking gen2 collection at each render boundary. The
    /// dictionaries and their arrays were 4,853 + 2,855 = 7,708 B a keystroke, 0.19% of it. It
    /// is the one site in the census whose `waste` is ZERO and whose price is all `actual`: the
    /// map is never oversized, it is simply built 35.69 times to be read once each.
    /// <para>
    /// WHY IT IS SAFE TO PARK: the map is filled, read for the pairs of ONE system and never
    /// handed anywhere; the method returns a double.
    /// </para>
    /// <para>
    /// RENTING TAKES THE MAP OUT OF THE DRAWER - the idiom session 421 wrote for
    /// <c>VerticalSkyline</c>'s scratch buffers, and THE CLEARING IS ON GIVE (session 456), so
    /// a map parked with the staves of the system before it is observable.
    /// </para>
    /// <para>
    /// WHAT IT RETAINS is one dictionary a thread holding at most the staves of one system - and
    /// a parked map is EMPTY, so it pins no <see cref="StaffLayout"/>.
    /// </para>
    /// <para>
    /// THE CLEAR HAS NO OBSERVER HERE, and that was measured rather than assumed. Session 457's
    /// poison 3 parked the map DIRTY and predicted many red; the suite came back all 8,783 green.
    /// The reason is a postcondition: every key a spring asks for is written by the loop three
    /// lines above, because the springs and the groups come from the SAME system - so a stale
    /// entry can only sit at a key this call never asks about. A gate says so on both
    /// populations: with the continue below replaced by a throw, the suite is green (8,783) and
    /// all 231 books of the reader's corpus still render. The clear stays because it is what
    /// makes the buffer's contract true for a system that does NOT hold every staff; it is just
    /// not a shape this tree has yet.
    /// </para>
    /// </remarks>
    [ThreadStatic]
    private static Dictionary<int, StaffLayout>? t_staffByIndex;

    /// <summary>Takes the thread's staff-by-index map, or makes the thread's first.</summary>
    private static Dictionary<int, StaffLayout> RentStaffIndex()
    {
        var map = t_staffByIndex;
        if (map is null)
            return new Dictionary<int, StaffLayout>();
        t_staffByIndex = null;
        return map;
    }

    /// <summary>Puts a finished system's map back, emptied, with its capacity.</summary>
    private static void GiveStaffIndex(Dictionary<int, StaffLayout> map)
    {
        map.Clear();
        t_staffByIndex = map;
    }

}
