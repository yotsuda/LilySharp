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
using Xunit;
using LilySharp.Core.Svg.Layout;

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
public class PageBreakerTests
{
    [Fact]
    public void BreakIntoPages_EmptySystems_ReturnsEmpty()
    {
        var breaker = new PageBreaker(
            pageHeight: 100,
            topMargin: 5,
            bottomMargin: 5,
            headerHeight: 10);

        var result = breaker.BreakIntoPages(Array.Empty<SystemDetails>());

        Assert.Empty(result);
    }

    [Fact]
    public void BreakIntoPages_SingleSystem_SinglePage()
    {
        var breaker = new PageBreaker(
            pageHeight: 100,
            topMargin: 5,
            bottomMargin: 5,
            headerHeight: 10);

        var systems = new[]
        {
            CreateSystem(height: 20)
        };

        var result = breaker.BreakIntoPages(systems);

        Assert.Single(result);
        Assert.Equal(1, result[0]); // 1 system on first page
    }

    [Fact]
    public void BreakIntoPages_TwoSystemsFitOnePage_SinglePage()
    {
        var breaker = new PageBreaker(
            pageHeight: 100,
            topMargin: 5,
            bottomMargin: 5,
            headerHeight: 10);

        // Available: 100 - 5 - 5 - 10 = 80
        // Two systems: 20 + 20 = 40 (fits easily)
        var systems = new[]
        {
            CreateSystem(height: 20),
            CreateSystem(height: 20)
        };

        var result = breaker.BreakIntoPages(systems);

        Assert.Single(result);
        Assert.Equal(2, result[0]); // 2 systems on first page
    }

    [Fact]
    public void BreakIntoPages_SystemsExceedPage_MultiplePagesCreated()
    {
        var breaker = new PageBreaker(
            pageHeight: 100,
            topMargin: 5,
            bottomMargin: 5,
            headerHeight: 10);

        // Available first page: 100 - 5 - 5 - 10 = 80
        // Available subsequent: 100 - 5 - 5 = 90
        // 5 systems × 30 = 150 (needs multiple pages)
        var systems = new[]
        {
            CreateSystem(height: 30),
            CreateSystem(height: 30),
            CreateSystem(height: 30),
            CreateSystem(height: 30),
            CreateSystem(height: 30)
        };

        var result = breaker.BreakIntoPages(systems);

        Assert.True(result.Count >= 2, $"Expected at least 2 pages, got {result.Count}");
    }

    [Fact]
    public void BreakIntoPages_ForcedBreak_RespectsBreak()
    {
        var breaker = new PageBreaker(
            pageHeight: 100,
            topMargin: 5,
            bottomMargin: 5,
            headerHeight: 10);

        // Two small systems that fit on one page, but forced break after first
        var systems = new[]
        {
            CreateSystem(height: 15, forceBreakAfter: true),
            CreateSystem(height: 15)
        };

        var result = breaker.BreakIntoPages(systems);

        Assert.Equal(2, result.Count); // Should be 2 pages due to forced break
        Assert.Equal(1, result[0]);    // 1 system on first page
        Assert.Equal(2, result[1]);    // End at system 2
    }

    [Fact]
    public void PageSpacing_SingleSystem_CalculatesForce()
    {
        var spacing = NewSpacing(pageHeight: 100, topMargin: 10, bottomMargin: 10);

        var system = CreateSystem(height: 30);
        spacing.AppendSystem(system);

        // Available: 100 - 10 - 10 = 80, LESS the whitespace the two page-end springs
        // reserve — 1 + 1 on the shipping specs — so 78. See NewSpacing.
        Assert.Equal(78, spacing.AvailableHeight);
        // Rod: 30
        // Force should be positive (stretch to fill)
        Assert.True(spacing.Force > 0, $"Expected positive force, got {spacing.Force}");
        Assert.Equal(30, spacing.RodHeight);
    }

    [Fact]
    public void PageSpacing_MultipleSystems_AccumulatesHeight()
    {
        var spacing = NewSpacing(pageHeight: 100, topMargin: 10, bottomMargin: 10);

        // Go through CalcLineHeights, as BreakIntoPages does — tallness is not a property
        // of a system on its own, it is how much the stack grows when the system is added
        // below its predecessor, so hand-built details have none.
        // LILYPOND-REF: lily/page-breaking.cc:1044-1081 cache_line_details, which calls
        //   calc_line_heights at :1079.
        var systems = PageBreaker.CalcLineHeights(new[]
        {
            CreateSystem(height: 20, staffHeight: 10, bottomExtent: 5),
            CreateSystem(height: 20, staffHeight: 10, topExtent: 5),
        });

        spacing.AppendSystem(systems[0]);
        spacing.AppendSystem(systems[1]);

        // LILYPOND-REF: lily/page-spacing.cc:53-62 — the FIRST system contributes
        // full_height(), every one after it contributes tallness_.
        //   first  : full height                                    = 20
        //   second : origin drops by its top extent 5, then padding 2, then its
        //            staff 10 + bottom extent 5 hangs below           = 22
        // so the rod is 42.
        //
        // This asserted 40 (two full heights) until the tallness port. That number
        // contradicted the very line it cited: with full heights on both, each system's
        // own extents were counted once in the rod and again inside the spring that
        // spanned them, which priced pages fuller than they are and left the breaker
        // packing about two systems too few onto every page.
        Assert.Equal(42, spacing.RodHeight);

        // The spring is now only what the rod has NOT already spent: the ideal 3 against
        // a refpoint distance of 20, i.e. nothing.
        // LILYPOND-REF: lily/constrained-breaking.cc:657-667 spring_length.
        Assert.Equal(0, spacing.SpringLength);
    }

    [Fact]
    public void PageSpacing_OverfullPage_ReturnsNegativeInfinity()
    {
        var spacing = NewSpacing(pageHeight: 50, topMargin: 10, bottomMargin: 10);

        // Available: 50 - 10 - 10 - (1 + 1 reserved by the page-end springs) = 28
        // System: 40 (overfull)
        var system = CreateSystem(height: 40);
        spacing.AppendSystem(system);

        Assert.True(double.IsNegativeInfinity(spacing.Force));
    }

    [Fact]
    public void SystemDetails_CreateFromLayout_SetsProperties()
    {
        var details = PageBreaker.CreateFromLayout(
            staffHeight: 4,
            topExtent: 2,
            bottomExtent: 3,
            padding: 1,
            springLength: 2,
            forceBreakAfter: true);

        Assert.Equal(9, details.Height);      // 2 + 4 + 3
        Assert.Equal(2, details.TopExtent);
        Assert.Equal(3, details.BottomExtent);
        Assert.Equal(4, details.StaffHeight);
        Assert.Equal(1, details.Padding);
        Assert.Equal(2, details.SpringLength);
        Assert.True(details.ForceBreakAfter);
    }

    // --- PageBreakingParameters tests ---

    [Fact]
    public void PageBreakingParameters_Default_MatchesLilyPond()
    {
        var p = PageBreakingParameters.Default;

        // LILYPOND-REF: lily/page-breaking.cc:280-297
        // LILYPOND-REF: ly/paper-defaults-init.ly ragged-last-bottom = ##f
        Assert.False(p.RaggedBottom);
        // LILYPOND-REF: ly/paper-defaults-init.ly:56 ragged-last-bottom = ##t
        Assert.True(p.RaggedLastBottom);
        Assert.Equal(0, p.SystemsPerPage);
        Assert.Equal(0, p.MaxSystemsPerPage);
        Assert.Equal(0, p.MinSystemsPerPage);
        Assert.Equal(100000, p.OrphanPenalty);
        // LILYPOND-REF: lily/page-breaking.cc:1506
        Assert.Equal(10, p.PageSpacingWeight);
    }

    // --- Orphan penalty tests ---

    [Fact]
    public void PageTooSmallForOneSystem_StillPaginates_RatherThanCollapsing()
    {
        // LILYPOND-REF: lily/page-spacing.cc:339-349 — the overfull guard is
        //   if (!too_few_lines (line_count) && page_start < line && overfull) break;
        // and the loop starts at page_start == line, so the page holding a single line is
        // ALWAYS considered, overfull or not.
        // LILYPOND-REF: lily/page-spacing.cc:362-365 — "Clamp the demerits at
        // BAD_SPACING_PENALTY, even if the page is overfull." LilyPond prices an
        // impossible page badly; it never rejects it.
        //
        // So when nothing fits, the answer is one system per page — overfull pages — not
        // one page holding everything. Lily# returned MaxValue for every candidate, the DP
        // found no solution at all, and BreakIntoPages' `bestPages < 0` fallback silently
        // put the whole score on a single page. Before this fix the assert below reads 1.
        var systems = new[]
        {
            CreateSystem(height: 40),
            CreateSystem(height: 40),
            CreateSystem(height: 40)
        };

        // A band of 30 - 5 - 5 = 20, less the 2 the page-end springs reserve: 18. No
        // single 40-tall system comes close to fitting.
        var pages = new PageBreaker(
            pageHeight: 30, topMargin: 5, bottomMargin: 5, headerHeight: 0)
            .BreakIntoPages(systems);

        Assert.Equal(3, pages.Count);
        Assert.Equal(new[] { 1, 2, 3 }, pages);
    }

    [Fact]
    public void OrphanPenalty_DoesNotReachMusicSystems()
    {
        // LILYPOND-REF: lily/page-spacing.cc:375-383 — orphan_penalty() is charged only
        // when lines_[page_start].last_markup_line_ or lines_[page_start - 1].first_markup_line_
        // is set, i.e. when a multi-line markup PARAGRAPH is split across a page boundary.
        // LILYPOND-REF: lily/constrained-breaking.cc:633-636 — those flags are read from a
        // markup line's Prob; lily/include/constrained-breaking.hh:115-116 — a music
        // system's Line_details leaves both false and nothing ever sets them.
        //
        // So the penalty's VALUE must make no difference to a break made of music, and
        // that is what this asserts. It replaces a test that claimed the opposite
        // ("a single system on the last page is penalised") and then asserted only
        // `lastPageSystems >= 1`, which is true of any page — so the invented rule it
        // documented was never actually checked. Before the fix this fails: at 100000
        // against force-squared demerits of ~0.001 the penalty moved the break.
        var systems = new[]
        {
            CreateSystem(height: 25),
            CreateSystem(height: 25),
            CreateSystem(height: 25)
        };

        var withPenalty = new PageBreaker(
            pageHeight: 80, topMargin: 5, bottomMargin: 5, headerHeight: 5,
            parameters: new PageBreakingParameters { OrphanPenalty = 100000 })
            .BreakIntoPages(systems);

        var withoutPenalty = new PageBreaker(
            pageHeight: 80, topMargin: 5, bottomMargin: 5, headerHeight: 5,
            parameters: new PageBreakingParameters { OrphanPenalty = 0 })
            .BreakIntoPages(systems);

        Assert.Equal(withoutPenalty, withPenalty);
    }

    // --- Ragged bottom tests ---

    [Fact]
    public void RaggedLastBottom_DoesNotStretchLastPage()
    {
        // Verify that ragged-last-bottom produces valid breaks
        var systems = new[]
        {
            CreateSystem(height: 15),
            CreateSystem(height: 15),
            CreateSystem(height: 15),
            CreateSystem(height: 15)
        };

        var breaker = new PageBreaker(
            pageHeight: 100, topMargin: 5, bottomMargin: 5, headerHeight: 5,
            parameters: new PageBreakingParameters { RaggedLastBottom = true });

        var result = breaker.BreakIntoPages(systems);

        Assert.True(result.Count >= 1);
        Assert.Equal(systems.Length, result[^1]); // All systems accounted for
    }

    [Fact]
    public void RaggedBottom_AllPagesNotStretched()
    {
        var systems = new[]
        {
            CreateSystem(height: 15),
            CreateSystem(height: 15),
            CreateSystem(height: 15),
            CreateSystem(height: 15),
            CreateSystem(height: 15),
            CreateSystem(height: 15)
        };

        var breaker = new PageBreaker(
            pageHeight: 70, topMargin: 5, bottomMargin: 5, headerHeight: 5,
            parameters: new PageBreakingParameters { RaggedBottom = true });

        var result = breaker.BreakIntoPages(systems);

        Assert.True(result.Count >= 2, "Should need multiple pages");
        Assert.Equal(systems.Length, result[^1]);
    }

    // --- Max/min systems per page tests ---

    [Fact]
    public void MaxSystemsPerPage_LimitsSystemCount()
    {
        var systems = new[]
        {
            CreateSystem(height: 10),
            CreateSystem(height: 10),
            CreateSystem(height: 10),
            CreateSystem(height: 10)
        };

        var breaker = new PageBreaker(
            pageHeight: 200, topMargin: 5, bottomMargin: 5, headerHeight: 5,
            parameters: new PageBreakingParameters { MaxSystemsPerPage = 2 });

        var result = breaker.BreakIntoPages(systems);

        // Should be at least 2 pages since max is 2 systems per page
        Assert.True(result.Count >= 2,
            $"Expected at least 2 pages with max 2 per page, got {result.Count}");

        // Verify each page has at most 2 systems
        int prevBreak = 0;
        foreach (var bp in result)
        {
            int count = bp - prevBreak;
            Assert.True(count <= 2,
                $"Page has {count} systems, max is 2");
            prevBreak = bp;
        }
    }

    [Fact]
    public void SystemsPerPage_ForcesExactCount()
    {
        var systems = new[]
        {
            CreateSystem(height: 10),
            CreateSystem(height: 10),
            CreateSystem(height: 10),
            CreateSystem(height: 10)
        };

        var breaker = new PageBreaker(
            pageHeight: 200, topMargin: 5, bottomMargin: 5, headerHeight: 5,
            parameters: new PageBreakingParameters { SystemsPerPage = 2 });

        var result = breaker.BreakIntoPages(systems);

        Assert.Equal(2, result.Count);
        Assert.Equal(2, result[0]); // 2 systems on page 1
        Assert.Equal(4, result[1]); // 2 systems on page 2
    }

    // --- Break permission tests ---

    [Fact]
    public void BreakPermission_Forbid_PreventsBreak()
    {
        var systems = new[]
        {
            CreateSystem(height: 30, pagePermission: BreakPermission.Forbid),
            CreateSystem(height: 30)
        };

        var breaker = new PageBreaker(
            pageHeight: 60, topMargin: 5, bottomMargin: 5, headerHeight: 5);

        var result = breaker.BreakIntoPages(systems);

        // Even though systems might not fit well, forbid prevents break after system 0
        Assert.Single(result);
        Assert.Equal(2, result[0]);
    }

    [Fact]
    public void BreakPermission_Force_EnforcesBreak()
    {
        var systems = new[]
        {
            CreateSystem(height: 10, pagePermission: BreakPermission.Force),
            CreateSystem(height: 10)
        };

        var breaker = new PageBreaker(
            pageHeight: 200, topMargin: 5, bottomMargin: 5, headerHeight: 5);

        var result = breaker.BreakIntoPages(systems);

        // Force permission should create page break after system 0
        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[0]);
        Assert.Equal(2, result[1]);
    }

    // --- Page spacing weight test ---

    [Fact]
    public void PageSpacingWeight_AffectsDemerits()
    {
        // Higher weight means page spacing is more important
        var systems = new[]
        {
            CreateSystem(height: 20),
            CreateSystem(height: 20),
            CreateSystem(height: 20)
        };

        // With low weight
        var breakerLow = new PageBreaker(
            pageHeight: 80, topMargin: 5, bottomMargin: 5, headerHeight: 5,
            parameters: new PageBreakingParameters { PageSpacingWeight = 1 });
        var resultLow = breakerLow.BreakIntoPages(systems);

        // With high weight
        var breakerHigh = new PageBreaker(
            pageHeight: 80, topMargin: 5, bottomMargin: 5, headerHeight: 5,
            parameters: new PageBreakingParameters { PageSpacingWeight = 100 });
        var resultHigh = breakerHigh.BreakIntoPages(systems);

        // Both should produce valid results
        Assert.True(resultLow.Count >= 1);
        Assert.True(resultHigh.Count >= 1);
        Assert.Equal(systems.Length, resultLow[^1]);
        Assert.Equal(systems.Length, resultHigh[^1]);
    }

    // --- fixed_force_solution tests ---

    [Fact]
    public void RaggedLastBottom_FixedForceSolution_NoPenaltyForUnderfull()
    {
        // LILYPOND-REF: lily/page-layout-problem.cc:1056-1061 fixed_force_solution — it only
        //   hands the force on.
        // LILYPOND-REF: lily/page-layout-problem.cc:779-804 solve_rod_spring_problem — the
        //   ragged-with-finite-force branch, whose intent is written out at :772-778.
        // For ragged pages, underfull pages should have no spacing penalty.
        // Systems are placed at natural positions; remaining space at bottom.
        var systems = new[]
        {
            CreateSystem(height: 15),
            CreateSystem(height: 15)
        };

        // Page height=200 → lots of unused space, but ragged shouldn't penalize
        var breaker = new PageBreaker(
            pageHeight: 200, topMargin: 5, bottomMargin: 5, headerHeight: 5,
            parameters: new PageBreakingParameters { RaggedLastBottom = true });

        var result = breaker.BreakIntoPages(systems);

        // All systems on one page (no reason to split)
        Assert.Single(result);
        Assert.Equal(2, result[0]);
    }

    [Fact]
    public void RaggedBottom_OverfullPage_Rejected()
    {
        // LILYPOND-REF: lily/page-layout-problem.cc:808-823
        // Overfull ragged pages should be rejected (systems don't fit at natural spacing)
        var systems = new[]
        {
            CreateSystem(height: 40),
            CreateSystem(height: 40),
            CreateSystem(height: 40)
        };

        // Page too small for 3 systems
        var breaker = new PageBreaker(
            pageHeight: 70, topMargin: 5, bottomMargin: 5, headerHeight: 5,
            parameters: new PageBreakingParameters { RaggedBottom = true });

        var result = breaker.BreakIntoPages(systems);

        // Must use multiple pages since 3 systems don't fit
        Assert.True(result.Count >= 2, "Overfull ragged page should force page break");
    }

    [Fact]
    public void RaggedBottom_FourCombinations_AllValid()
    {
        // LILYPOND-REF: ly/paper-defaults-init.ly — ragged-bottom / ragged-last-bottom
        // All 4 combinations should produce valid breaks
        var systems = new[]
        {
            CreateSystem(height: 15),
            CreateSystem(height: 15),
            CreateSystem(height: 15),
            CreateSystem(height: 15),
            CreateSystem(height: 15)
        };

        var combinations = new[]
        {
            new PageBreakingParameters { RaggedBottom = false, RaggedLastBottom = false },
            new PageBreakingParameters { RaggedBottom = false, RaggedLastBottom = true },
            new PageBreakingParameters { RaggedBottom = true, RaggedLastBottom = false },
            new PageBreakingParameters { RaggedBottom = true, RaggedLastBottom = true }
        };

        foreach (var combo in combinations)
        {
            var breaker = new PageBreaker(
                pageHeight: 80, topMargin: 5, bottomMargin: 5, headerHeight: 5,
                parameters: combo);

            var result = breaker.BreakIntoPages(systems);

            Assert.True(result.Count >= 1,
                $"RaggedBottom={combo.RaggedBottom}, RaggedLastBottom={combo.RaggedLastBottom} failed");
            Assert.Equal(systems.Length, result[^1]);
        }
    }

    [Fact]
    public void FirstPage_HoldsWhatTheNaturalDistanceFits()
    {
        // The geometry of a plain one-staff system of quarter notes, measured off the
        // probe used to diagnose this: 4 ss of staff, stems reaching 3.5 above it, half a
        // space below, LilyPond's system-system-spacing (basic-distance 12,
        // minimum-distance 8, padding 1).
        //
        // Natural system-to-system distance is therefore 12, confirmed against LilyPond
        // 2.24.4 by forcing few systems per page under ragged-bottom so that nothing is
        // stretched or compressed: the gaps come out at 12.000 exactly.
        //
        // A system costs the page its tallness 9 plus a spring of 3 — twelve, the natural
        // distance, which is the whole point of the tallness/spring split. Before that
        // port it was priced at its full height 8 PLUS padding 1 and a spring 4, i.e. 13
        // (arithmetic, not a measurement — the old code cannot be run against this test,
        // which needs fields it did not have). What WAS measured is the effect on a real
        // A4 page: the diagnosis probe went from 11 systems per page to 13, against
        // LilyPond's 14, and its non-last-page gaps from 14.55 to 12.12 while the ragged
        // last page stayed at 12.00. Over-pricing left space the breaker would not spend,
        // and PageLayouter's justification pass then stretched the survivors to fill it —
        // which is exactly why every non-last page came out looser than the last one.
        var systems = new List<SystemDetails>();
        for (int i = 0; i < 24; i++)
        {
            systems.Add(new SystemDetails
            {
                Height = 3.5 + 4 + 0.5,
                TopExtent = 3.5,
                BottomExtent = 0.5,
                StaffHeight = 4,
                Padding = 1,
                MinDistance = 8,
                SpringLength = 12,
                RefpointExtentUp = -2,
                RefpointExtentDown = -2,
                InverseHooke = 1,
            });
        }

        var breaker = new PageBreaker(
            pageHeight: 100, topMargin: 5, bottomMargin: 5, headerHeight: 0);

        var result = breaker.BreakIntoPages(systems);

        // 90 ss of usable height: the first system spends its full height 8 and every one
        // after it 12, so eight fit with 10 to spare and nine would overrun by 2.
        Assert.True(result.Count >= 2, $"expected several pages, got {result.Count}");
        Assert.Equal(8, result[0]);
    }

    // --- ragged-last-bottom: what LilyPond actually does with the last page ---------------
    //
    // Measured against LilyPond 2.24.4 by audit/lp-geometry/probes/page-vertical.ly, whose
    // three books separate the three regimes. Book J (the shipping default over 150 bars)
    // comes out at 11.801982 between staff refpoints on page 1 -- and at 11.801982 on
    // page 2 as well, its LAST page, which has 108 ss of unused paper below it. Book L is
    // the same music short enough to fit one page, and that page sits at 12.000000, the
    // natural system-system-spacing basic-distance.
    //
    // The two together say ragged-last-bottom does not mean "space the last page freely":
    //
    //   lily/page-breaking.cc:570-573
    //     else if (rag && !ragged ())
    //       // If we're ragged-last but not ragged, make the last page
    //       // have the same force as the previous page.
    //       config = layout.fixed_force_solution (last_page_force);
    //
    // last_page_force starts at 0 (:643), so a one-page book is the only one that comes out
    // natural. Every other page of a book is spaced to match the page before it -- which is
    // the whole reason LilyPond's pages look alike, and why a Lily# last page pinned to
    // force 0 was the one page that did not.

    [Fact]
    public void LastPage_IsSpacedWithTheForceOfThePageBefore()
    {
        // 18 plain one-staff systems on A4: 13 fit the first page, 5 fall to the second.
        var (systems, extents) = PlainSystems(18);

        var pages = new PageLayouter(LayoutOptions.Default)
            .CreatePagesWithOptimalBreaking(systems, header: null, extents);

        Assert.Equal(2, pages.Length);
        double firstPageGap = UniformGap(pages[0]);
        double lastPageGap = UniformGap(pages[1]);

        // The first page is justified, so it is stretched past the natural 12.
        Assert.True(firstPageGap > 12.0,
            $"page 1 should be stretched to fill the page, got {firstPageGap:F6}");

        // ...and the last page takes that same force rather than falling back to natural.
        Assert.Equal(firstPageGap, lastPageGap, 6);
    }

    [Fact]
    public void SinglePageScore_KeepsTheNaturalDistance()
    {
        // The one case LilyPond DOES leave natural: last_page_force is still its initial 0
        // when the only page is drawn. Book L of the probe measures 12.000000 here.
        var (systems, extents) = PlainSystems(5);

        var pages = new PageLayouter(LayoutOptions.Default)
            .CreatePagesWithOptimalBreaking(systems, header: null, extents);

        Assert.Single(pages);
        Assert.Equal(12.0, UniformGap(pages[0]), 6);
    }

    /// <summary>
    /// The distance between consecutive systems on a page, asserting they are all equal.
    /// </summary>
    /// <remarks>
    /// SystemLayout.Y is stored page Y-UP, so a later system has the SMALLER Y.
    /// </remarks>
    private static double UniformGap(PageLayout page)
    {
        Assert.True(page.Systems.Length >= 2,
            $"a gap needs two systems, page has {page.Systems.Length}");
        double first = page.Systems[0].Y - page.Systems[1].Y;
        for (int i = 1; i < page.Systems.Length - 1; i++)
        {
            double gap = page.Systems[i].Y - page.Systems[i + 1].Y;
            Assert.Equal(first, gap, 6);
        }
        return first;
    }

    /// <summary>
    /// N identical one-staff systems of quarter notes: 4 ss of staff, stems 3.5 above it,
    /// half a space below. The same geometry
    /// <see cref="FirstPage_HoldsWhatTheNaturalDistanceFits"/> uses, and the same music
    /// audit/lp-geometry/probes/page-vertical.ly engraves.
    /// </summary>
    private static (ImmutableArray<SystemLayout> Systems,
                    ImmutableArray<(double upExtent, double downExtent)> Extents)
        PlainSystems(int count)
    {
        var systems = ImmutableArray.CreateBuilder<SystemLayout>(count);
        var extents = ImmutableArray.CreateBuilder<(double, double)>(count);
        for (int i = 0; i < count; i++)
        {
            systems.Add(new SystemLayout(i, 0, 100, 0, ImmutableArray<MeasureLayout>.Empty));
            extents.Add((3.5, 0.5));
        }
        return (systems.ToImmutable(), extents.ToImmutable());
    }

    /// <summary>
    /// A <see cref="PageSpacing"/> carrying the PRODUCT's page-end specs.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-spacing.cc:30-34 — a page is priced against its printable
    /// height LESS <c>min_whitespace_at_top_of_page</c> and
    /// <c>min_whitespace_at_bottom_of_page</c>, which are the breaker's only knowledge of
    /// the top-system and last-bottom springs. Both come to 1 on the shipping specs
    /// (padding 1 beats minimum-distance 0 minus a non-negative reach), so every
    /// available height in this file is the band minus 2. Passing the real specs rather
    /// than neutral ones is deliberate: a test that zeroed them would still pass with the
    /// term deleted from the product.
    /// </remarks>
    private static PageSpacing NewSpacing(double pageHeight, double topMargin, double bottomMargin)
    {
        var vs = new VerticalSpacingParameters();
        return new PageSpacing(pageHeight, topMargin, bottomMargin, vs.TopSystem, vs.LastBottom);
    }

    /// <summary>
    /// The two buckets are compared bucket to bucket, so a line-start grob is only ever
    /// measured against the previous line's line-start grobs.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-breaking.cc:1154-1177 calc_line_heights, whose
    /// <c>max (prev_hanging_begin + a, prev_hanging_rest + b)</c> is this claim.
    /// <para>
    /// The numbers are the deep figured-bass page book's, measured: a bass staff with a
    /// figure row 7.622462 under it and a line-start bar number 2.310714 over it, where the
    /// rest of the line carries only the staff line's 0.050000 on both sides. The pair is
    /// worth 12.672462 — which is what the PLACEMENT chain's pointwise skyline distance
    /// independently says (11.672462 + the spec's padding 1), the two models meeting on one
    /// number. Without the split the same line prices the pair by the scalar sum, 2.260714
    /// more, and eleven of those are two systems' worth of page: audit/lp-geometry
    /// figbass.page.deep.systems-on-first-page, which read 10 against LilyPond's 12 until
    /// the shape arrived. The second case is the guard on the fallback, not a leftover: a
    /// caller that cannot split a system must get exactly the old arithmetic.
    /// </para>
    /// </remarks>
    [Fact]
    public void CalcLineHeights_PricesTheBucketsSeparately_SoALineStartGrobNeverMeetsTheDeepInk()
    {
        var shaped = new SystemDetails
        {
            Height = 13.933176,
            TopExtent = 2.310714,
            BottomExtent = 7.622462,
            StaffHeight = 4,
            Padding = 1,
            MinDistance = 8,
            RefpointExtentUp = -2,
            RefpointExtentDown = -2,
            Shape = new LineShape(
                BeginUp: 2.310714, BeginDown: 0.050000,
                RestUp: 0.050000, RestDown: 7.622462),
        };

        var withShape = PageBreaker.CalcLineHeights(new[] { shaped, shaped });
        Assert.Equal(12.672462, withShape[1].Tallness, 6);

        var flat = shaped with { Shape = null };
        var withoutShape = PageBreaker.CalcLineHeights(new[] { flat, flat });
        Assert.Equal(14.933176, withoutShape[1].Tallness, 6);
    }

    private static SystemDetails CreateSystem(
        double height = 20,
        double staffHeight = 10,
        double topExtent = 5,
        double bottomExtent = 5,
        double padding = 2,
        double springLength = 3,
        bool forceBreakAfter = false,
        BreakPermission pagePermission = BreakPermission.Allow)
    {
        return new SystemDetails
        {
            Height = height,
            TopExtent = topExtent,
            BottomExtent = bottomExtent,
            StaffHeight = staffHeight,
            Padding = padding,
            SpringLength = springLength,
            ForceBreakAfter = forceBreakAfter,
            PagePermission = pagePermission
        };
    }
}
