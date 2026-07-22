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
///   lyrics, dynamics, and figured bass heights are estimated and added to system extents
///   in LayoutEngine.AugmentExtentsWithLooseLines() before page breaking
/// build_system_skyline (page-layout-problem.cc:1070-1127):
///   per-system UP/DOWN skylines are passed to PositionSystemsOnPage for inter-system collision avoidance
/// IMPLEMENTED — fixed_force_solution for ragged-last (page-layout-problem.cc:1057-1061,
///   applied with the previous page's force as page-breaking.cc:570-573 does)
/// PARTIAL — footnote heights via SystemDetails.FootnoteHeight (page-layout-problem.cc:186-310)
/// IMPLEMENTED — in-note-system-padding (page-layout-problem.cc:483)
/// IMPLEMENTED — hara-kiri auto-hide empty staves (MultiStaffLayouter + LayoutEngine)
/// IMPLEMENTED — alignment-distances manual override (StaffSpacingParameters.ApplyOverrides)
/// IMPLEMENTED — pure height estimation for pre-breaking optimization
///   via LayoutEngine.AugmentExtentsWithLooseLines + MultiStaffLayouter.CalculatePureSystemHeight
/// </remarks>
internal sealed class PageLayouter
{
    private readonly LayoutOptions _options;

    public PageLayouter(LayoutOptions options)
    {
        _options = options;
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
    public ImmutableArray<PageLayout> CreatePagesWithOptimalBreaking(
        ImmutableArray<SystemLayout> systems,
        double headerHeight,
        ImmutableArray<(double upExtent, double downExtent)> systemExtents,
        ImmutableArray<(VerticalSkyline up, VerticalSkyline down)>? systemSkylines = null,
        ImmutableArray<(double bandUp, double bandDown)>? systemBands = null,
        IReadOnlyList<double>? systemBodyHeights = null)
    {
        if (systems.Length == 0)
        {
            return ImmutableArray<PageLayout>.Empty;
        }

        var vs = _options.VerticalSpacing;

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

            // The staff refpoint is the staff's CENTRE, and this system's origin is its
            // top staff's TOP LINE, so the first staff's refpoint sits half a single
            // staff below the origin and the last staff's half a single staff above the
            // body's bottom. LILYPOND-REF: lily/include/constrained-breaking.hh:56-60
            // refpoint_extent_ (LilyPond reads the real grobs; see SystemDetails).
            double halfStaff = _options.StaffHeight / 2.0;

            systemDetails.Add(new SystemDetails
            {
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
                RefpointExtentUp = -halfStaff,
                RefpointExtentDown = -(staffHeight - halfStaff),
                InverseHooke = Math.Max(0.1, spec.Stretchability > 0 ? spec.Stretchability / 60.0 : 0.1),
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
            parameters: _options.PageBreaking);

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
                vs, systemSkylines, systemBands, out double pageForce);

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
        out double pageForce)
    {
        var pageSystems = new List<SystemLayout>();

        // First system Y position
        var topSpec = vs.TopSystem;
        double firstY = isFirstPage
            ? _options.MarginTop + headerHeight + systemExtents[startIdx].upExtent + topSpec.Padding
            : _options.MarginTop + systemExtents[startIdx].upExtent + topSpec.Padding;

        // PASS 1 — the natural gap for each system pair, from the REAL
        // (X-aware skyline) distances placement uses.
        int count = endIdx - startIdx;
        var gapNatural = new double[Math.Max(0, count - 1)];
        var gapInvHooke = new double[Math.Max(0, count - 1)];
        for (int sysIdx = startIdx; sysIdx < endIdx; sysIdx++)
        {
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
                    double dist = nextUp.Distance(prevDown,
                        EngravingDefaults.SystemSkylineHorizontalPadding);
                    // Distance() returns negative infinity for empty skylines;
                    // fall back to scalar calculation in that case
                    if (double.IsNegativeInfinity(dist))
                    {
                        skylineDistance = _options.StaffHeight + d.BottomExtent
                            + systemExtents[sysIdx + 1].upExtent;
                    }
                    else
                    {
                        // Whole-line annotation bands (lyric lines below,
                        // chord-symbol rows above) lay out after the page Y is
                        // fixed and are absent from the skylines; they floor
                        // the distance (a band spans every X, so the X-disjoint
                        // argument for preferring Distance() does not apply).
                        if (systemBands is { } bands && sysIdx + 1 < bands.Length)
                        {
                            double bandDownPrev = bands[sysIdx].bandDown;
                            double bandUpNext = bands[sysIdx + 1].bandUp;
                            if (bandDownPrev > 0)
                                dist = Math.Max(dist, _options.StaffHeight + bandDownPrev
                                    + systemExtents[sysIdx + 1].upExtent);
                            if (bandUpNext > 0)
                                dist = Math.Max(dist, _options.StaffHeight
                                    + systemExtents[sysIdx].downExtent + bandUpNext);
                        }
                        skylineDistance = dist;
                    }
                }
                else
                {
                    skylineDistance = _options.StaffHeight + d.BottomExtent
                        + systemExtents[sysIdx + 1].upExtent;
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
                // the spec's padding, floored by the spec's minimum-distance.
                // (LP's in-note-system-padding folds into the skyline only when a
                // system carries an in-note stencil, which Lily# never renders, so
                // it can never contribute to a plain system-to-system spring.)
                double minDistance = Math.Max(spec.MinimumDistance, skylineDistance + padding);

                // Spring-based ideal (natural) distance
                double springDistance = Math.Max(basicDist, minDistance);

                gapNatural[sysIdx - startIdx] = Math.Max(springDistance, minDistance);
                gapInvHooke[sysIdx - startIdx] =
                    spec.Stretchability > 0 ? spec.Stretchability / 60.0 : 0.1;
            }
        }

        // PASS 2 — justification. The page breaker's force is solved on
        // SCALAR system heights, but placement packs tighter via the X-aware
        // skyline distances, so that force under-fills real pages. Re-solve
        // here against the actual gaps: stretch the springs (weighted by
        // their stretchability) until the last system's ink reaches the
        // bottom margin.
        //
        // A spring's length is its ideal plus force times its inverse Hooke
        // constant, so the force this page runs at is just the leftover space
        // divided by the total inverse Hooke — and stating it that way is what
        // lets the LAST page be given the force of the page before it rather
        // than a natural spacing of its own (see draw_page at the caller).
        // LILYPOND-REF: lily/page-layout-problem.cc solve() — springs are
        // solved against the real rods; ly/paper-defaults-init.ly
        // ragged-bottom = ##f, ragged-last-bottom = ##t.
        pageForce = 0;
        if (count > 1)
        {
            double naturalSum = 0, invSum = 0;
            for (int i = 0; i < gapNatural.Length; i++)
            {
                naturalSum += gapNatural[i];
                invSum += gapInvHooke[i];
            }

            if (useFixedForce)
            {
                // LILYPOND-REF: lily/page-layout-problem.cc:1057-1061
                // fixed_force_solution — solve at the force handed in, not at zero.
                pageForce = fixedForce;
            }
            else if (!isRagged && invSum > 0)
            {
                var last = systemDetails[endIdx - 1];
                double lastBottom = firstY + naturalSum + last.StaffHeight + last.BottomExtent;
                double leftover = _options.PageHeight - _options.MarginBottom - lastBottom;
                // Stretch only. A negative force would have to be solved against the rods
                // to keep systems from colliding, and this pass has none — the natural gaps
                // it starts from are already the rod-floored ones. Replacing the whole pass
                // with a two-sided spring solve was tried and moved neither a probe nor a
                // snapshot byte, because the breaker never hands this code a page it has to
                // compress; leaving the branch out keeps it from shipping unreached.
                pageForce = leftover > 0 ? leftover / invSum : 0;
            }

            if (pageForce != 0)
            {
                for (int i = 0; i < gapNatural.Length; i++)
                    gapNatural[i] += pageForce * gapInvHooke[i];
            }
        }

        double currentY = firstY;
        for (int sysIdx = startIdx; sysIdx < endIdx; sysIdx++)
        {
            // Stage-4 W2-core multi-page producer seam: currentY accumulates the
            // system top DOWNWARD (device) within the page; store it as page Y-up
            // (UP from the page bottom) against this page's fixed height, matching
            // the PageLayout.Height (= _options.PageHeight) built above.
            pageSystems.Add(allSystems[sysIdx] with { Y = _options.PageHeight - currentY });
            if (sysIdx < endIdx - 1)
                currentY += gapNatural[sysIdx - startIdx];
        }

        return pageSystems.ToImmutableArray();
    }
}
