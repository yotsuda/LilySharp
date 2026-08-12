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
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Creates pages from systems using optimal page breaking.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/page-spacing.cc Page_spacer class
/// LILYPOND-REF: lily/page-layout-problem.cc vertical justification
/// LILYPOND-REF: ly/paper-defaults-init.ly:64-89 — default spacing specs
///
/// Loose line distribution (page-layout-problem.cc:1025-1054):
///   dynamics and figured bass heights are still ESTIMATED from the items and added to the
///   system extents in LayoutEngine.AugmentExtentsWithLooseLines() before page breaking.
///   ⚠️ LYRICS ARE NOT ESTIMATED ANY MORE (2026-07-27): a lyric block is reserved at its
///   ALIGNMENT MINIMUM by LayoutEngine.LyricReservationBelowSystem, which is AlignmentWalk
///   over the real syllable ink and the real staff skyline — LilyPond's own reservation
///   (:593-599 hands build_system_skyline the minimum translations). The estimate and the
///   drawn-distance reading that used to stand beside it are both gone.
/// build_system_skyline (page-layout-problem.cc:1070-1127):
///   per-system UP/DOWN skylines are passed to PositionSystemsOnPage for inter-system collision avoidance
/// IMPLEMENTED — fixed_force_solution for ragged-last (page-layout-problem.cc:1057-1061,
///   applied with the previous page's force as page-breaking.cc:570-573 does)
/// PARTIAL — footnote heights via SystemDetails.FootnoteHeight (page-layout-problem.cc:186-310)
/// IMPLEMENTED — in-note-system-padding (page-layout-problem.cc:483)
/// IMPLEMENTED — hara-kiri auto-hide empty staves (MultiStaffLayouter + LayoutEngine)
/// IMPLEMENTED — alignment-distances manual override (StaffSpacingParameters.ApplyOverrides)
/// ⚠️ NOT WIRED — MultiStaffLayouter.CalculatePureSystemHeight has no product caller. It is
///   reached only from SkylineStaffSpacingTests, and LilyPond's own pure path
///   (get_pure_minimum_translations, align-interface.cc:136-143) runs the SAME walk with
///   pure skylines, which Lily# does not have. The line above used to claim this was
///   implemented via AugmentExtentsWithLooseLines; it was not.
/// </remarks>
internal sealed class PageLayouter
{
    private readonly LayoutOptions _options;

    public PageLayouter(LayoutOptions options)
    {
        _options = options;
    }

    /// <summary>
    /// The inter-system skyline distance, memoized per facing pair by INSTANCE identity.
    /// </summary>
    /// <remarks>
    /// <c>Distance</c> is a pure walk over the two skylines' buildings (plus the constant
    /// horizontal padding), so the same two INSTANCES always yield the same number —
    /// reference identity is the whole soundness argument, exactly as in
    /// <see cref="SystemLayoutCache.GetOrComputePagingAugment"/>, whose cached pairs are
    /// what make the identities stable across keystrokes (an unchanged system's paging
    /// skyline comes back as the same object; an edited one arrives fresh and misses).
    /// MEASURED (session 142, Release, warm keystroke): the positioning pass was ~99%
    /// this walk — 85.4 ms over 98 pairs on perf-v2bow1k (bow-fattened skylines),
    /// 41.7 ms over 184 pairs on perf-plain1k — while the page-break DP itself cost
    /// 0.8–6.7 ms.
    /// <para>
    /// Keyed on the UP side via a <see cref="ConditionalWeakTable{TKey,TValue}"/> (the
    /// same static-CWT shape as <c>MultiStaffLayouter.StaffBeamGroupsOf</c>): one entry
    /// per up-skyline, holding the down-instance it was measured against. A given up
    /// skyline faces one predecessor per layout, so one slot suffices; a re-pairing
    /// (systems re-broken) just recomputes. The table never outlives its keys, and an
    /// entry's strong reference to its down-instance only ties two objects whose
    /// lifetimes the skyline cache already couples.
    /// </para>
    /// ⚠️ Skylines must stay immutable after construction for this to hold —
    /// <c>VerticalSkyline.Merge</c> merges INTO a fresh wrapper everywhere on the paging
    /// path, and <c>SystemLayoutCache</c>'s remarks pin the shared-instance contract.
    /// </remarks>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<
        VerticalSkyline, PairDistanceEntry> PairDistances = new();

    private sealed class PairDistanceEntry
    {
        public VerticalSkyline? Down;
        public double Value;
    }

    private static double InterSystemSkylineDistance(VerticalSkyline nextUp, VerticalSkyline prevDown)
    {
        var e = PairDistances.GetOrCreateValue(nextUp);
        if (!ReferenceEquals(e.Down, prevDown))
        {
            e.Value = nextUp.Distance(prevDown, EngravingDefaults.SystemSkylineHorizontalPadding);
            e.Down = prevDown;
        }
        return e.Value;
    }

    /// <summary>
    /// Creates pages using optimal page breaking algorithm with full skyline collision detection.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-spacing.cc Page_spacer class
    /// LILYPOND-REF: lily/page-layout-problem.cc:1070-1127 build_system_skyline
    /// Uses dynamic programming to find optimal page breaks,
    /// then applies force-based vertical spacing within each page.
    /// When per-system skylines are provided, inter-system distances use
    /// VerticalSkyline.Distance() for X-dependent collision avoidance
    /// instead of scalar extents.
    /// </remarks>
    /// <param name="systemAnchors">
    /// Per system, how far DOWN from its origin its first and its last SPACEABLE staff's
    /// REFERENCE POINTS sit — the frame every spring on this page is written in
    /// (<c>LayoutEngine.PageAnchorOffsets</c>, whose remarks carry the LilyPond references and
    /// the measurement). Absent, a nominal half staff stands in, which is right only for a
    /// one-staff system of five lines; the product path always supplies it.
    /// </param>
    public ImmutableArray<PageLayout> CreatePagesWithOptimalBreaking(
        ImmutableArray<SystemLayout> systems,
        double headerHeight,
        ImmutableArray<(double upExtent, double downExtent)> systemExtents,
        ImmutableArray<(VerticalSkyline up, VerticalSkyline down)>? systemSkylines = null,
        ImmutableArray<(double bandUp, double bandDown)>? systemBands = null,
        IReadOnlyList<double>? systemBodyHeights = null,
        ImmutableArray<(double toFirst, double toLast, double halfFirst, double halfLast)>?
            systemAnchors = null,
        ImmutableArray<LineShape?>? systemShapes = null)
    {
        if (systems.Length == 0)
        {
            return ImmutableArray<PageLayout>.Empty;
        }

        var vs = _options.VerticalSpacing;

        // THE REFPOINT FRAME, per system. Every distance below runs between staff REFERENCE
        // POINTS while Lily# stacks systems by their ORIGIN, and this pair is the conversion:
        // how far down the origin sits above the first spaceable staff's refpoint, and above
        // the last one's. It used to be a nominal `_options.StaffHeight / 2.0` written out at
        // each use, which is that distance only for a five-line staff (a six-string tab
        // staff's refpoint is 3.750000 below its top line, not 2.000000 — audit/lp-geometry
        // page.tab-only.first-staff-refpoint).
        // ⚠️ TWO PAIRS, and they are not interchangeable: ToFirst/ToLast run from the ORIGIN
        // and convert anything measured there (a skyline, a scalar extent); HalfFirst/HalfLast
        // are the outer staves' OWN half spans and convert anything measured from the STAFF (a
        // whole-line band). Using the first where the second belongs counts a leading chord
        // row twice.
        (double ToFirst, double ToLast, double HalfFirst, double HalfLast) Anchor(int i)
        {
            double nominal = _options.StaffHeight / 2.0;
            return systemAnchors is { } a && i >= 0 && i < a.Length
                ? a[i]
                : (nominal, nominal, nominal, nominal);
        }

        // Create SystemDetails for each system using per-system skyline extents
        // and context-dependent spacing specs
        var systemDetails = new List<SystemDetails>();
        for (int i = 0; i < systems.Length; i++)
        {
            // The system BODY height: a grand-staff/multi-staff system is far
            // taller than one staff — pricing it at StaffHeight (4) made the
            // page breaker cram a dozen grand-staff systems onto one page and
            // truncate the bottom.
            double staffHeight = systemBodyHeights != null && i < systemBodyHeights.Count
                ? systemBodyHeights[i]
                : _options.StaffHeight;
            double topExtent = systemExtents[i].upExtent;
            double bottomExtent = systemExtents[i].downExtent;

            // LILYPOND-REF: lily/page-layout-problem.cc:488-535
            // Select spacing spec based on pair context.
            // Title/markup distinction is handled via SystemDetails.IsTitle when
            // the caller provides it (future extension).
            // For now, determine spec from the pair relationship:
            VerticalSpacingSpec spec;
            if (i == 0)
            {
                // First system uses top-system spec (applied during positioning)
                spec = vs.SystemSystem;
            }
            else
            {
                spec = vs.SelectSpec(
                    isFirstOnPage: false,
                    prevIsTitle: false,
                    currentIsTitle: false,
                    currentIsNewScore: false);
            }

            // LILYPOND-REF: lily/include/constrained-breaking.hh:56-60
            // refpoint_extent_ (LilyPond reads the real grobs; see SystemDetails).
            double halfStaffNominal = _options.StaffHeight / 2.0;

            systemDetails.Add(new SystemDetails
            {
                // LILYPOND-REF: lily/constrained-breaking.cc:547 fill_line_details, the line
                // that builds Line_shape — the pair of pure heights the breaker prices this
                // line by. Absent, CalcLineHeights lends the whole-line extents to both.
                Shape = systemShapes is { } sh && i < sh.Length ? sh[i] : null,
                Height = topExtent + staffHeight + bottomExtent,
                TopExtent = topExtent,
                BottomExtent = bottomExtent,
                StaffHeight = staffHeight,
                Padding = spec.Padding,
                // LILYPOND-REF: constrained-breaking.hh:66,69 min_distance_ / space_ —
                // both are refpoint-to-refpoint and both go in RAW. Subtracting the
                // minimum from the ideal here is what Line_details::spring_length does
                // later, against the rod that tallness_ actually spends; doing it twice
                // is what made the breaker under-fill pages.
                MinDistance = spec.MinimumDistance,
                SpringLength = spec.BasicDistance,
                // ⚠️ LILYSHARP-OWN: THE BREAKER KEEPS THE NOMINAL PAIR, deliberately and under
                // protest — LilyPond reads the real grobs here too, so this is a Lily# value
                // and not a ported one, and it goes when a point measures a page COUNT over a
                // staff that is not four staff spaces tall. This
                // is LilyPond's refpoint_extent_ and the placement chain's own anchors are
                // now the placed ones, so the two models disagree here — but the breaker is a
                // SECOND implementation of the page's spring model (HANDOFF 5.2.1 (2)) and
                // moving it changes which systems land on which page: measured, taking the
                // placed pair re-broke book LYRHKG's pages and left its staves interleaved.
                // No corpus point measures a page COUNT over a staff that is not four staff
                // spaces tall, so there is nothing to justify that rebreak with yet.
                RefpointExtentUp = -halfStaffNominal,
                RefpointExtentDown = -(staffHeight - halfStaffNominal),
                // LILYPOND-REF: lily/constrained-breaking.cc:555 —
                //   out->inverse_hooke_ = out->full_height () + system_system_space_;
                // where system_system_space_ is system-system-spacing's BASIC-DISTANCE
                // (:426-430, with page-breaking-system-system-spacing allowed to override
                // it; Lily# models no such variable). full_height() is the line's own
                // extent-to-extent height, which is exactly SystemDetails.Height.
                //
                // ⚠️ The breaker does NOT use stretchability. This read
                // `max(0.1, Stretchability / 60)` — the same /60 invention cfdf85b4 struck
                // out of the placement chain, which survived here because the breaker is a
                // SECOND implementation of the page's spring model (HANDOFF 5.2.1 (2): a
                // duplicate is where a port lands only half the time). On the shipping
                // specs the two differ by a factor of ~19 (1.0 against 7.35 + 12), so the
                // force the breaker solved for was nothing like the one the chain then
                // solved, and the page count came from the wrong one.
                InverseHooke = topExtent + staffHeight + bottomExtent + vs.SystemSystem.BasicDistance,
            });
        }

        // Tallness is filled in by the breaker itself, as LilyPond does it
        // (page-breaking.cc:1037, at the end of cache_line_details).

        // Run page breaker
        var breaker = new PageBreaker(
            pageHeight: _options.PageHeight,
            topMargin: _options.MarginTop,
            bottomMargin: _options.MarginBottom,
            headerHeight: headerHeight,
            parameters: _options.PageBreaking,
            verticalSpacing: vs);

        var breakPoints = breaker.BreakIntoPages(systemDetails);

        // Create pages from break points with context-aware Y positioning
        var pages = new List<PageLayout>();
        int systemStart = 0;

        // LILYPOND-REF: lily/page-breaking.cc:643 — Page_breaking::make_pages opens with
        // `Real last_page_force = 0` and threads it through every draw_page call, so the
        // force a page solved to is what the NEXT page may be asked to reuse.
        double lastPageForce = 0;

        for (int pageIdx = 0; pageIdx < breakPoints.Count; pageIdx++)
        {
            int systemEnd = breakPoints[pageIdx];
            bool isFirstPage = pageIdx == 0;
            bool isLastPage = pageIdx == breakPoints.Count - 1;

            // The page's force used to be re-derived here from a PageSpacing rebuilt out of
            // the SCALAR system heights, and then thrown away by PASS 2 below, which solves
            // its own against the X-aware skyline gaps placement actually uses. Two answers
            // to one question, of which only the second reached the page.

            // Determine if this page uses ragged spacing
            // LILYPOND-REF: ly/paper-defaults-init.ly — ragged-bottom / ragged-last-bottom
            // 4 combinations: both false (justify all), last-only, all ragged, both true (≡ ragged-bottom)
            bool raggedAll = _options.PageBreaking.RaggedBottom;
            bool isRagged = raggedAll
                || (isLastPage && _options.PageBreaking.RaggedLastBottom);

            // LILYPOND-REF: lily/page-breaking.cc:565-575 draw_page
            //   bool rag = ragged () || (last && ragged_last ());
            //   ...
            //   else if (rag && !ragged ())
            //     // If we're ragged-last but not ragged, make the last page
            //     // have the same force as the previous page.
            //     config = layout.fixed_force_solution (last_page_force);
            //   else
            //     config = layout.solution (rag);
            //
            // fixed_force_solution takes a force ARGUMENT (page-layout-problem.cc:1057-1061
            // hands it straight to solve_rod_spring_problem); it is not a synonym for zero.
            // Lily# used to pin every ragged page to 0, which left the last page of a book
            // at its natural spacing while every page before it was justified — the one page
            // that looked different. Measured on 18 plain systems at A4: page 1 came out at
            // 12.450000 between systems and the last page at 12.000000.
            //
            // Only a book that is ONE page long still comes out natural, because
            // lastPageForce is then still its initial 0. LilyPond 2.24.4 measures 12.000000
            // there and 11.801982 on both pages of a two-page book
            // (audit/lp-geometry/probes/page-vertical.ly, books L and J).
            bool useFixedForce = isRagged && !raggedAll;

            // Position systems using context-aware spacing specs
            // LILYPOND-REF: lily/page-layout-problem.cc:1070-1127 build_system_skyline
            // When skylines are available, use Distance() for X-dependent collision detection
            var pageSystems = PositionSystemsOnPage(
                systems, systemExtents, systemDetails, systemStart, systemEnd,
                isFirstPage, headerHeight, isRagged, useFixedForce, lastPageForce,
                vs, systemSkylines, systemBands, Anchor, out double pageForce);

            // LILYPOND-REF: lily/page-breaking.cc:577-582 — the force is carried forward
            // after every page, so "the previous page" always means the immediately
            // preceding one rather than the first.
            lastPageForce = pageForce;

            pages.Add(new PageLayout(
                PageIndex: pageIdx,
                Width: _options.PageWidth,
                Height: _options.PageHeight,
                HeaderHeight: isFirstPage ? headerHeight : 0,
                Systems: pageSystems));

            systemStart = systemEnd;
        }

        return pages.ToImmutableArray();
    }

    /// <summary>
    /// Positions systems on a page using context-aware vertical spacing.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:488-535 spacing spec selection
    /// LILYPOND-REF: lily/page-layout-problem.cc:622-646 minimum distance from skylines
    /// LILYPOND-REF: lily/page-layout-problem.cc:1070-1127 build_system_skyline
    ///
    /// When per-system skylines are available, uses VerticalSkyline.Distance()
    /// for X-dependent inter-system collision detection. This gives more accurate
    /// minimum distances than scalar extents because it considers the full
    /// horizontal profile of each system.
    /// </remarks>
    private ImmutableArray<SystemLayout> PositionSystemsOnPage(
        ImmutableArray<SystemLayout> allSystems,
        ImmutableArray<(double upExtent, double downExtent)> systemExtents,
        List<SystemDetails> systemDetails,
        int startIdx, int endIdx,
        bool isFirstPage, double headerHeight,
        bool isRagged, bool useFixedForce, double fixedForce,
        VerticalSpacingParameters vs,
        ImmutableArray<(VerticalSkyline up, VerticalSkyline down)>? systemSkylines,
        ImmutableArray<(double bandUp, double bandDown)>? systemBands,
        Func<int, (double ToFirst, double ToLast, double HalfFirst, double HalfLast)> anchor,
        out double pageForce)
    {
        var pageSystems = new List<SystemLayout>();

        // THE CHAIN. LilyPond builds one Page_layout_problem per PAGE and pushes one
        // spring per boundary: top-system-spacing first (:511-518), then one spring per
        // system pair (the loop at :489-533), then last-bottom-spacing (:538-545). It
        // solves that whole chain at once, so every spring on the page carries the SAME
        // force — the top and bottom springs stretch with the middle.
        // LILYPOND-REF: lily/page-layout-problem.cc:406-545 Page_layout_problem::Page_layout_problem
        //
        // ⚠️ AND THE STAVES OF A SYSTEM ARE IN THAT CHAIN TOO. append_system pushes the
        // system's own spring and then one spring per spaceable staff PAIR (:651-720), so
        // "how far apart are two staves of this system" is solved by the page, at the same
        // force as "how far apart are two systems". Lily# used to draw every system at the
        // Align_interface minimum on every page and at every force; the ledger points
        // page.natural/page.stretched.staff-staff-inside are the pair that measured it.
        var springs = ImmutableArray.CreateBuilder<Spring>();

        // Spring 0 — down to the first system's staff refpoint. Only the first page
        // carries a header, and the header enters this spring's FLOOR, not the anchor.
        // ⚠️ HalfFirst, not ToFirst: the floor is built from an EXTENT, which is measured from
        // the staff. On a lead sheet the chord row is already inside that extent.
        springs.Add(LayoutUtilities.CreateTopSystemSpring(
            isFirstPage ? headerHeight : 0,
            systemExtents[startIdx].upExtent, anchor(startIdx).HalfFirst, vs.TopSystem));

        int count = endIdx - startIdx;
        // positions[FirstStaffPosition(local)] is that system's FIRST staff refpoint; its
        // k-th staff spring leads to the entry one further along. Recorded rather than
        // computed from sysIdx because the number of springs per system now varies.
        var firstStaffPosition = new int[count];
        // Two spans read off the LAID-OUT system — the frame its skylines were built in —
        // per system: how far below the system's ORIGIN its first spaceable staff's refpoint
        // sits, and how far below it the last one's does. They are the conversion the
        // neighbouring springs need, and they are what LilyPond means by first_spaceable_dy /
        // last_spaceable_dy (:1116, :1126).
        // ⚠️ NOT the sum of the springs' floors. Those are the ALIGNMENT minimums, which
        // sit below the drawn distance by exactly the basic-distance the spring supplies as
        // its ideal — the distinction that lets a page compress at all.
        var originToFirst = new double[count];
        var originToLast = new double[count];

        // How far a system's ink reaches below the staff its springs attach to — the term
        // both the scalar fallback and the page's closing spring are written from.
        // ⚠️ THE BODY, NOT THE HALF SPAN. Whatever hangs under the last spaceable staff —
        // a lyric row's band — is inside StaffHeight (the body Lily# reserved), not inside
        // BottomExtent, so the reach below the refpoint is the body less the origin's
        // distance to that refpoint. Reading it as HalfLast + BottomExtent drops the rows
        // and the next system lands on top of them (measured: LYRHKG's staves interleaved).
        double InkBelowLastRefpoint(SystemDetails d, int sysIdx)
            => d.StaffHeight - originToLast[sysIdx - startIdx] + d.BottomExtent;

        for (int sysIdx = startIdx; sysIdx < endIdx; sysIdx++)
        {
            int local = sysIdx - startIdx;
            firstStaffPosition[local] = springs.Count;
            var staffSprings = allSystems[sysIdx].StaffSprings;
            var sysAnchor = anchor(sysIdx);
            originToFirst[local] = sysAnchor.ToFirst;
            // ⚠️ LILYSHARP-OWN: THE CHAIN'S LAST NODE, WHICH IS NOT ALWAYS THE LAST STAFF.
            // LilyPond has no such case — append_system pushes a spring for every spaceable
            // staff PAIR and its chain therefore always reaches the last one
            // (page-layout-problem.cc:651-720) — so this says something about Lily#'s chain
            // and not about LilyPond's. It goes when a system's staff springs cover every
            // spaceable pair on every system, hara-kiri included. A system
            // contributes one node per staff SPRING; with no springs it contributes only the
            // one this loop just recorded, so the spring that leaves this system leaves from
            // its FIRST refpoint and the conversion below has to say so. Reading the last
            // staff's refpoint anyway subtracts a span the chain never added: measured on book
            // LYRHKG (whose hara-kiri'd system has no pair left to spring), the next system
            // rose 7.192800 and the staves interleaved.
            // This is what SprungStaffOffsets' `return (0, 0)` used to encode, kept as a
            // statement about the CHAIN rather than as a floor for existing output.
            originToLast[local] = staffSprings.IsDefaultOrEmpty
                ? sysAnchor.ToFirst
                : sysAnchor.ToLast;
            if (!staffSprings.IsDefaultOrEmpty)
            {
                foreach (var ss in staffSprings)
                {
                    // LILYPOND-REF: lily/page-layout-problem.cc:678-704 — the spec's spring
                    // (basic-distance / stretchability), floored by the minimum translation
                    // through ensure_min_distance.
                    springs.Add(LayoutUtilities.CreateSpring(ss.Spec, ss.MinimumDistance));
                }
            }

            if (sysIdx < endIdx - 1)
            {
                // Select spacing spec for this pair
                var spec = vs.SelectSpec(
                    isFirstOnPage: false, // Not first — we already placed first
                    prevIsTitle: systemDetails[sysIdx].IsTitle,
                    currentIsTitle: systemDetails[sysIdx + 1].IsTitle,
                    currentIsNewScore: false);

                var d = systemDetails[sysIdx];

                // LILYPOND-REF: lily/page-layout-problem.cc:1070-1127 build_system_skyline
                // Use full skyline Distance() when available for X-dependent collision detection;
                // fall back to scalar extents when skylines are not provided.
                double skylineDistance;
                if (systemSkylines.HasValue
                    && sysIdx < systemSkylines.Value.Length
                    && sysIdx + 1 < systemSkylines.Value.Length)
                {
                    var prevDown = systemSkylines.Value[sysIdx].down;
                    var nextUp = systemSkylines.Value[sysIdx + 1].up;
                    // LILYPOND-REF: lily/page-layout-problem.cc:618-629 — the
                    // inter-system distance is measured with the System grob's
                    // skyline-horizontal-padding, so nearly-X-adjacent facing
                    // ink still interacts through the 45° shoulders.
                    double dist = InterSystemSkylineDistance(nextUp, prevDown);
                    // Distance() returns negative infinity for empty skylines;
                    // fall back to scalar calculation in that case
                    if (double.IsNegativeInfinity(dist))
                    {
                        skylineDistance = InkBelowLastRefpoint(d, sysIdx)
                            + systemExtents[sysIdx + 1].upExtent + anchor(sysIdx + 1).HalfFirst;
                    }
                    else
                    {
                        // FRAME. Both skylines are built about their system's ORIGIN (its
                        // first staff's top line), so Distance() is origin-to-origin, while
                        // the spring being built runs from the PREVIOUS system's LAST
                        // spaceable staff to the next system's FIRST. LilyPond states the
                        // same conversion as a shift of the skylines themselves —
                        // LILYPOND-REF: lily/page-layout-problem.cc:1120-1126 leaves the up
                        // skyline relative to the top spaceable staff and the down skyline
                        // relative to the BOTTOM one, by first_spaceable_dy /
                        // last_spaceable_dy out of the same minimum translations this span
                        // is the sum of.
                        // ⚠️ NOT A LITERAL PORT, and the deviation is here rather than
                        // hidden: LilyPond performs the conversion by RAISING THE SKYLINES
                        // THEMSELVES inside build_system_skyline, so what leaves that
                        // function is already relative to the top/bottom spaceable staff.
                        // Lily# subtracts at the spring instead, which keeps
                        // SkylineBuilder in ONE frame for its other readers (LayoutEngine's
                        // up/down extents, the paging pass). Same number, different place.
                        // Moving it into the builder is a frame migration across those
                        // readers, not an edit to this line — HANDOFF 2D.
                        // (A leading loose line — a lead sheet's chord row — sits between
                        // the anchor and the first sprung staff, which is why the offset is
                        // measured from the anchor and not from the first spring.)
                        // ⚠️ It is also what makes the two branches agree: the scalar
                        // fallbacks are written in the SAME frame (InkBelowLastRefpoint plus
                        // the next system's own origin-to-refpoint), so a system whose outer
                        // staves are not four staff spaces tall cannot price its skyline gap
                        // and its fallback gap differently.
                        dist += anchor(sysIdx + 1).ToFirst - originToLast[local];

                        // Whole-line annotation bands (lyric lines below,
                        // chord-symbol rows above) lay out after the page Y is
                        // fixed and are absent from the skylines; they floor
                        // the distance (a band spans every X, so the X-disjoint
                        // argument for preferring Distance() does not apply).
                        if (systemBands is { } bands && sysIdx + 1 < bands.Length)
                        {
                            double bandDownPrev = bands[sysIdx].bandDown;
                            double bandUpNext = bands[sysIdx + 1].bandUp;
                            // A BAND IS MEASURED FROM THE STAFF IT HANGS OFF, so the term that
                            // reaches its refpoint is that staff's own half span; the OTHER
                            // side of each max is an extent measured from the origin and takes
                            // the origin distance. Reaching for ToFirst/ToLast on both sides
                            // counts a leading chord row twice.
                            if (bandDownPrev > 0)
                                dist = Math.Max(dist, anchor(sysIdx).HalfLast + bandDownPrev
                                    + systemExtents[sysIdx + 1].upExtent
                                    + anchor(sysIdx + 1).HalfFirst);
                            if (bandUpNext > 0)
                                dist = Math.Max(dist, InkBelowLastRefpoint(d, sysIdx)
                                    + anchor(sysIdx + 1).HalfFirst + bandUpNext);
                        }
                        skylineDistance = dist;
                    }
                }
                else
                {
                    skylineDistance = InkBelowLastRefpoint(d, sysIdx)
                        + systemExtents[sysIdx + 1].upExtent + anchor(sysIdx + 1).ToFirst;
                }

                // LILYPOND-REF: lily/include/constrained-breaking.hh tight_spacing_
                // In tight spacing mode, compress basic distance and padding
                double basicDist = spec.BasicDistance;
                double padding = spec.Padding;
                if (_options.PageBreaking.TightSpacing)
                {
                    double factor = _options.PageBreaking.TightSpacingFactor;
                    basicDist *= factor;
                    padding *= factor;
                }

                // LILYPOND-REF: lily/page-layout-problem.cc:625-632 append_system —
                // the inter-system minimum distance is the skyline distance plus
                // the spec's padding, and it reaches the spring as a FLOOR through
                // ensure_min_distance rather than as the distance itself.
                // (LP's in-note-system-padding folds into the skyline only when a
                // system carries an in-note stencil, which Lily# never renders, so
                // it can never contribute to a plain system-to-system spring.)
                springs.Add(LayoutUtilities.CreateSpring(
                    spec with { BasicDistance = basicDist, Padding = padding },
                    skylineDistance + padding));
            }
        }

        // The last spring — down from the last system's staff refpoint to the foot.
        // LILYPOND-REF: lily/page-layout-problem.cc:538-545 — last-bottom-spacing,
        // floored by `last_padding - bottom_skyline_.max_height () + footer_height_`.
        // bottom_skyline_ is the last system's DOWN skyline about its own refpoint, so
        // -max_height() is the ink hanging below that refpoint. Lily# has no footer.
        // Lily# used to have NO spring here at all: it stretched the inter-system gaps
        // until the last system's INK touched the bottom margin, which drops both this
        // spring's padding and its stretchability out of the force calculation.
        {
            var lastDetails = systemDetails[endIdx - 1];
            // Measured from the LAST SPACEABLE staff's refpoint, which is where this spring
            // attaches: StaffHeight is the whole body (the origin down to the last staff's
            // bottom line), so dropping the origin's distance to that staff's refpoint is
            // what hangs below it.
            double inkBelowLastRefpoint = InkBelowLastRefpoint(lastDetails, endIdx - 1);
            springs.Add(LayoutUtilities.CreateSpring(
                vs.LastBottom, vs.LastBottom.Padding + inkBelowLastRefpoint));
        }

        // LILYPOND-REF: lily/page-layout-problem.cc:471-476 — page_height_ deliberately
        // does NOT reserve the header: the top spring is anchored at the top of it.
        double pageHeight = _options.PageHeight - _options.MarginTop - _options.MarginBottom;
        var solver = new SpringSolver(springs.ToImmutable());

        // LILYPOND-REF: lily/page-layout-problem.cc:780-804 solve_rod_spring_problem
        ImmutableArray<double> positions;
        if (useFixedForce)
        {
            // fixed_force_solution (:1057-1061) — solve_rod_spring_problem (true, force).
            // The spacer is told it is NOT ragged, "otherwise it will refuse to stretch",
            // and the handed-in force is used only if the page still fits at it.
            var sol = solver.Solve(pageHeight, ragged: false);
            pageForce = solver.TotalLength(fixedForce) <= pageHeight ? fixedForce : sol.Force;
            positions = solver.GetPositions(pageForce);
        }
        else
        {
            var sol = solver.Solve(pageHeight, isRagged);
            pageForce = sol.Force;
            // LILYPOND-REF: lily/simple-spacer.cc:301-303 — a ragged configuration is laid
            // out at force 0 even when the solve reported a positive one.
            positions = solver.GetPositions(isRagged && pageForce > 0 ? 0.0 : pageForce);
        }

        for (int sysIdx = startIdx; sysIdx < endIdx; sysIdx++)
        {
            int local = sysIdx - startIdx;
            // positions[] is the running sum of the chain, measured DOWN from the top of
            // the printable area, and firstStaffPosition names the entry that is this
            // system's FIRST staff refpoint
            // (LILYPOND-REF: page-layout-problem.cc:896-901 — solution_[spring_idx] is the
            // first staff's position and the system origin is that plus min_offsets[0]).
            // Lily# stacks systems by their ORIGIN, which is originToFirst above the refpoint
            // the chain positions — half a staff on an ordinary system, more on a lead sheet
            // whose chord row comes first, and 3.750000 on a six-string tab staff.
            double refpoint = _options.MarginTop + positions[firstStaffPosition[local]];
            double origin = refpoint - originToFirst[local];

            var system = allSystems[sysIdx];
            // Stage-4 W2-core multi-page producer seam: origin is the system top measured
            // DOWNWARD (device) within the page; store it as page Y-up (UP from the page
            // bottom) against this page's fixed height, matching the PageLayout.Height
            // (= _options.PageHeight) built above.
            pageSystems.Add(system with
            {
                Y = _options.PageHeight - origin,
                StaffGroups = RespaceStaves(system, positions, firstStaffPosition[local]),
            });
        }

        return pageSystems.ToImmutableArray();
    }

    // SprungStaffOffsets lived here: "how far below the anchor does the first (last) SPRUNG
    // staff sit", with the anchor at a nominal half staff and BOTH offsets 0 when a system
    // had no staff springs at all. Its own remark said that 0 "keeps one-staff scores and
    // lead sheets on the arithmetic they had before the springs existed" — which is the shape
    // HANDOFF 5.2 names: a branch that exists to leave existing output alone. It cost exactly
    // that, measured: a one-staff TAB page put its refpoint 1.750000 below LilyPond's
    // (audit/lp-geometry page.tab-only.first-staff-refpoint). The quantity is now
    // LayoutEngine.PageAnchorOffsets, computed for EVERY system from the same
    // ClassifySystem selection the loose chain uses, and handed in.

    /// <summary>
    /// Re-places a system's staves at the distances the PAGE solved for them.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:896-914 — <c>find_system_offsets</c> reads
    /// one solution entry per spaceable staff and translates that staff by it, so the
    /// staves of a system end up wherever the page's force put them rather than at the
    /// minimum <c>Align_interface</c> handed in.
    /// <para>
    /// Returns the system's own groups UNCHANGED when nothing moved, so a page at force 0 —
    /// every ragged page, and every score that fits one page — is byte-identical to before
    /// the springs existed. That is a consequence, not a construction: at force 0 a spring
    /// is <c>max(min_distance, ideal)</c> and the minimum handed in IS the laid-out
    /// distance, which is never below the spec's basic-distance.
    /// </para>
    /// <para>
    /// ⚠️ Staves with no spring — text rows and hidden staves (see
    /// <c>MultiStaffLayouter.StaffSprings</c>) — keep their offset from the spaceable staff
    /// above them THROUGH THIS PASS. LilyPond re-spaces its loose lines separately
    /// (<c>distribute_loose_lines</c>, :1025-1054), and since 2026-07-27 so does Lily# — but
    /// in the LYRIC chain (<c>LyricEngraver.DistributeLooseLines</c>) and applied by
    /// <c>LayoutEngine.ApplySolvedRowPositions</c>, i.e. AFTER this. So "no spring here" no
    /// longer means "never re-spaced"; it means this page pass is not what moves them.
    /// ⚠️ What genuinely still travels with its staff: a LYRICS row (no ink in any skyline,
    /// HANDOFF 1) and any row on a page's FIRST system. ⚠️ AN OSSIA NO LONGER DOES — since
    /// 2026-07-28 it carries a spring of its own and this pass moves it like any staff.
    /// </para>
    /// </remarks>
    private static ImmutableArray<StaffGroupLayout> RespaceStaves(
        SystemLayout system, ImmutableArray<double> positions, int firstStaffPosition)
    {
        var sprung = system.StaffSprings;
        if (sprung.IsDefaultOrEmpty || system.StaffGroups.IsDefaultOrEmpty)
            return system.StaffGroups;

        // Where each sprung staff was laid out, by global staff index.
        var laidOut = new Dictionary<int, double>();
        foreach (var group in system.StaffGroups)
            foreach (var staff in group.Staves)
                laidOut[staff.StaffIndex] = MultiStaffLayouter.StaffRefpoint(staff);

        // How far each sprung staff moved from there. Measured against the LAID-OUT
        // distance, not against the spring's floor: the floor is the alignment minimum and
        // sits a basic-distance below where the staves were actually drawn.
        var shift = new Dictionary<int, double>();
        double cumulativeSolved = 0;
        for (int k = 0; k < sprung.Length; k++)
        {
            if (!laidOut.TryGetValue(sprung[0].UpperStaffIndex, out double anchorY)
                || !laidOut.TryGetValue(sprung[k].LowerStaffIndex, out double lowerY))
                return system.StaffGroups;
            cumulativeSolved += positions[firstStaffPosition + k + 1]
                                - positions[firstStaffPosition + k];
            double cumulativeLaidOut = anchorY - lowerY;
            // Y-up: a staff pushed further DOWN the page has a SMALLER Y.
            shift[sprung[k].LowerStaffIndex] = -(cumulativeSolved - cumulativeLaidOut);
        }
        if (shift.Values.All(v => Math.Abs(v) < 1e-9))
            return system.StaffGroups;

        var groups = ImmutableArray.CreateBuilder<StaffGroupLayout>(system.StaffGroups.Length);
        // A staff with no spring of its own follows the last sprung staff above it.
        double running = 0;
        foreach (var group in system.StaffGroups)
        {
            var staves = ImmutableArray.CreateBuilder<StaffLayout>(group.Staves.Length);
            foreach (var staff in group.Staves)
            {
                if (shift.TryGetValue(staff.StaffIndex, out double own))
                    running = own;
                staves.Add(staff with { Y = staff.Y + running });
            }
            var moved = staves.ToImmutable();
            double top = moved[0].Y;
            double bottom = moved[^1].Y - moved[^1].Height;
            var delimiter = group.GrandStaffLayout is { } d
                ? d with { Staves = moved, BraceTop = top, BraceBottom = bottom }
                : null;
            groups.Add(group with
            {
                Staves = moved,
                Y = top,
                Height = top - bottom,
                GrandStaffLayout = delimiter,
            });
        }
        return groups.ToImmutable();
    }
}
