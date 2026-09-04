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

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// The page-scored system-count loop: LilyPond's <c>Optimal_page_breaking::solve</c>, which
/// starts from the line DP's best count and tries fewer and more systems, pricing each count
/// by its lines' forces AND the pages they make, and engraves the cheapest.
/// </summary>
internal sealed partial class LayoutEngine
{
    /// <summary>
    /// LilyPond's <c>-ddebug-page-breaking-scoring</c>: when set, every count the loop
    /// prices is reported here ("trying N systems" / "best score for this sys-count"), so a
    /// Lily# book's scores can be laid beside LilyPond's for the same book. Null in
    /// production; a probe test sets it. Not read anywhere else.
    /// </summary>
    internal static Action<string>? DebugPageBreakingScoring { get; set; }

    /// <summary>
    /// The per-measure height estimate a candidate line is priced from before it is laid
    /// out — Lily#'s stand-in for LilyPond's pure heights.
    /// </summary>
    /// <param name="UpRest">Per measure, the ink above the body over that bar's X span
    /// (the REST bucket's share).</param>
    /// <param name="DownRest">Per measure, the ink below the body over that bar's X span.</param>
    /// <param name="Body">Per measure, the body height of the system it was placed in.</param>
    /// <param name="BeginUpAt">Per measure, the ink above the body a candidate line STARTING
    /// at that measure carries in its BEGIN bucket — its line-start prefix and whatever
    /// stands over it: the placed system's own begin bucket where the ideal breaking started
    /// a system there, the bare continuation prefix everywhere else (see the remarks of
    /// <see cref="EstimateMeasureHeights"/>).</param>
    /// <param name="BeginDownAt">Its ink below the body, likewise per line start.</param>
    /// <param name="Frame">Per measure, the breaker's refpoint frame of the system it was
    /// placed in (<c>LayoutEngine.BreakerFrame</c>): the outer staves' refpoints and the
    /// squeeze that takes the body back to its alignment minimum. Null where no placed
    /// system holds the bar.</param>
    private readonly record struct MeasureHeightEstimate(
        double[] UpRest, double[] DownRest, double[] Body, double[] BeginUpAt, double[] BeginDownAt,
        BreakerRefpointFrame?[] Frame);

    /// <summary>
    /// Slices the ideal placement's paging silhouettes by bar, so that any candidate line —
    /// any run of bars — can be priced by the max over its bars without being laid out.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/constrained-breaking.cc:505-565 fill_line_details, which prices a
    /// candidate line by <c>System::begin_of_line_pure_height</c> and
    /// <c>rest_of_line_pure_height</c> over the line's column range — estimates built per
    /// column from the grobs' pure extents (lily/axis-group-interface.cc:359-474
    /// adjacent_pure_heights), never from a laid-out line. LilyPond's page-breaking cost
    /// stands on that: a count's lines are priced in O(columns), and only the chosen count
    /// is engraved.
    /// <para>
    /// ⚠️ Lily#-own deviation, declared: Lily# has no pure extent per grob at this seam. What
    /// it has is the paging silhouette of each system of the IDEAL breaking (the line DP's
    /// own choice, always placed first), and a bar's share of that silhouette is
    /// <see cref="VerticalSkyline.MaxHeightInRange"/> over the bar's X span — the begin
    /// bucket being the ink left of the first bar (<c>BuildLineShapes</c>' cut). So where
    /// LilyPond's estimate is a function of the columns alone, this one carries the ideal
    /// line's spacing and its neighbours' collisions. Both estimates differ from the
    /// engraved height; the page is still broken from the real, placed systems
    /// (CreatePages), exactly as before — only the COUNT is chosen from this.
    /// </para>
    /// <para>
    /// THE BEGIN BUCKET IS PER LINE START, as LilyPond's is per break rank
    /// (LILYPOND-REF: lily/axis-group-interface.cc:417-458 adjacent_pure_heights —
    /// <c>begin_line_heights[j]</c> unites, for a line that would start at break rank
    /// <c>ranks[j]</c>, the prebroken pieces there and every grob whose rank span starts at
    /// or before that column; lily/system.cc:906-928 part_of_line_pure_height reads the ONE
    /// entry at the candidate line's start rank). Lily#'s stand-in: a bar the ideal breaking
    /// started a system at takes that system's measured begin bucket; every other bar takes
    /// the BARE continuation prefix — the smallest begin bucket any continuation system
    /// showed (clef, key and the bar number over them; a mark or tempo standing over a
    /// line start is in that line's bucket only). What stands over such a bar mid-line
    /// (its mark box, a tempo) is in the bar's REST share already, so a line starting
    /// there is still priced for it, through max(begin, rest).
    /// ⚠️ It used to be ONE bucket for every line — the WIDEST any placed system showed —
    /// and the first system's is the widest by construction (the tempo and the opening mark
    /// stand over its prefix): every candidate line of "Le Freak" (staff + tab, 24 lines)
    /// was priced 7.301 above the body where the placed continuation lines are 2.311, six
    /// systems fitted a page where the placement then fitted eight, the ideal count needed
    /// a page more than the count below it, and the loop's "one page fewer and stretched"
    /// exit (optimal-page-breaking.cc:183-185) fired one count too early — LilyPond's
    /// 23-line breaking, whose line sum Lily# had already priced cheapest, was never tried
    /// (session 335, scratch/p335).
    /// </para>
    /// <para>
    /// What the silhouette cannot account for (whole-line bands, anything the extents were
    /// enriched with) is given to every bar and to the begin bucket alike, the same union
    /// rule <c>BuildLineShapes</c> applies; a system with no paging silhouette lends its
    /// scalar extents to each of its bars.
    /// </para>
    /// </remarks>
    private MeasureHeightEstimate EstimateMeasureHeights(
        SystemPass pass, int measureCount, double fallbackBody)
    {
        var upRest = new double[measureCount];
        var downRest = new double[measureCount];
        var body = new double[measureCount];
        Array.Fill(body, fallbackBody);
        var frame = new BreakerRefpointFrame?[measureCount];
        // The begin bucket per line start: NaN until a placed system starting there fills
        // it; the rest take the bare continuation prefix below.
        var beginUpAt = new double[measureCount];
        var beginDownAt = new double[measureCount];
        Array.Fill(beginUpAt, double.NaN);
        Array.Fill(beginDownAt, double.NaN);
        // The bare continuation prefix: the smallest begin bucket a continuation system
        // showed. A book of one system has no continuation and lends that system's.
        double bareUp = double.PositiveInfinity, bareDown = double.PositiveInfinity;
        double firstUp = 0, firstDown = 0;
        var skylines = pass.Prelim.PagingSkylines;

        for (int s = 0; s < pass.Systems.Count; s++)
        {
            var sys = pass.Systems[s];
            if (sys.Measures.IsDefaultOrEmpty || s >= pass.Extents.Count)
                continue;
            double h = s < pass.Heights.Count ? pass.Heights[s] : fallbackBody;
            var ext = pass.Extents[s];
            int count = sys.Measures.Length;
            var measureUp = new double[count];
            var measureDown = new double[count];
            double sysBeginUp = 0, sysBeginDown = 0, sysRestUp = 0, sysRestDown = 0;

            if (skylines != null && s < skylines.Count)
            {
                var (up, down) = skylines[s];
                // Where the line's first bar begins in the silhouette's own X frame; left of
                // it is the line-start prefix — BuildLineShapes' begin bucket.
                double xSplit = sys.Measures[0].X;
                if (!up.IsEmpty)
                    sysBeginUp = Math.Max(0, up.MaxHeightsSplitAt(xSplit).Left);
                if (!down.IsEmpty)
                    sysBeginDown = Math.Max(0, -down.MaxHeightsSplitAt(xSplit).Left - h);
                for (int k = 0; k < count; k++)
                {
                    // Bars tile the line: a bar's span runs to the next bar's start, and the
                    // last bar's to the end of the silhouette (the closing bar line, a
                    // volta hook, whatever trails the last note is that bar's).
                    double x0 = k == 0 ? xSplit : sys.Measures[k].X;
                    double x1 = k == count - 1
                        ? double.PositiveInfinity
                        : sys.Measures[k + 1].X;
                    measureUp[k] = up.IsEmpty ? 0 : Math.Max(0, up.MaxHeightInRange(x0, x1));
                    measureDown[k] = down.IsEmpty
                        ? 0
                        : Math.Max(0, -down.MaxHeightInRange(x0, x1) - h);
                    sysRestUp = Math.Max(sysRestUp, measureUp[k]);
                    sysRestDown = Math.Max(sysRestDown, measureDown[k]);
                }
            }

            // What the silhouette could not account for belongs to every bucket.
            double excessUp = Math.Max(0, ext.upExtent - Math.Max(sysBeginUp, sysRestUp));
            double excessDown = Math.Max(0, ext.downExtent - Math.Max(sysBeginDown, sysRestDown));
            double lineBeginUp = sysBeginUp + excessUp;
            double lineBeginDown = sysBeginDown + excessDown;
            int firstMeasure = sys.Measures[0].MeasureIndex;
            if (firstMeasure >= 0 && firstMeasure < measureCount)
            {
                beginUpAt[firstMeasure] = lineBeginUp;
                beginDownAt[firstMeasure] = lineBeginDown;
            }
            if (s == 0)
            {
                firstUp = lineBeginUp;
                firstDown = lineBeginDown;
            }
            else
            {
                bareUp = Math.Min(bareUp, lineBeginUp);
                bareDown = Math.Min(bareDown, lineBeginDown);
            }
            // The breaker's frame of the system this bar was placed in — the same
            // BreakerFrame the paging path hands the placed systems' details, so an
            // estimated line of one system's bars is priced in exactly that system's frame.
            var sysFrame = BreakerFrame(sys);
            for (int k = 0; k < count; k++)
            {
                int mi = sys.Measures[k].MeasureIndex;
                if (mi < 0 || mi >= measureCount)
                    continue;
                upRest[mi] = measureUp[k] + excessUp;
                downRest[mi] = measureDown[k] + excessDown;
                body[mi] = h;
                frame[mi] = sysFrame;
            }
        }
        if (double.IsPositiveInfinity(bareUp))
        {
            bareUp = firstUp;
            bareDown = firstDown;
        }
        for (int m = 0; m < measureCount; m++)
        {
            if (double.IsNaN(beginUpAt[m]))
                beginUpAt[m] = bareUp;
            if (double.IsNaN(beginDownAt[m]))
                beginDownAt[m] = bareDown;
        }
        return new MeasureHeightEstimate(upRest, downRest, body, beginUpAt, beginDownAt, frame);
    }

    /// <summary>
    /// The <see cref="SystemDetails"/> of one candidate breaking, priced from the estimate —
    /// LilyPond's <c>cache_line_details</c> for one configuration.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-breaking.cc:1045-1081 cache_line_details → line_details per
    /// chunk; the details themselves are fill_line_details' (constrained-breaking.cc:505-565):
    /// the shape from the two pure-height buckets, the page permission from the line's last
    /// column. Built through the same <see cref="PageLayouter.BuildSystemDetails"/> the
    /// paging path builds the placed systems' details with.
    /// </remarks>
    private List<SystemDetails> EstimatedSystemDetails(
        List<int> breaks, MeasureHeightEstimate estimate, ImmutableArray<Measure> measures)
    {
        var details = new List<SystemDetails>(breaks.Count);
        int start = 0;
        for (int i = 0; i < breaks.Count; i++)
        {
            int end = Math.Min(breaks[i], measures.Length);
            double restUp = 0, restDown = 0, body = 0;
            // The frame travels with the body it belongs to: the bar whose placed system
            // is the tallest lends the line its body AND its refpoints, so the two never
            // come from different systems.
            BreakerRefpointFrame? frame = null;
            for (int m = start; m < end; m++)
            {
                restUp = Math.Max(restUp, estimate.UpRest[m]);
                restDown = Math.Max(restDown, estimate.DownRest[m]);
                if (estimate.Body[m] > body)
                {
                    body = estimate.Body[m];
                    frame = estimate.Frame[m];
                }
            }
            if (body <= 0)
            {
                body = _options.StaffHeight;
                frame = null;
            }
            var permission = end > start
                ? measures[end - 1].EffectivePagePermission
                : BreakPermission.Allow;
            // The begin bucket of a line starting HERE (LilyPond's begin_line_heights at
            // this line's start rank), not one bucket for every line.
            double beginUp = start < estimate.BeginUpAt.Length ? estimate.BeginUpAt[start] : 0;
            double beginDown = start < estimate.BeginDownAt.Length ? estimate.BeginDownAt[start] : 0;
            details.Add(_pageLayouter.BuildSystemDetails(
                i, body,
                Math.Max(beginUp, restUp), Math.Max(beginDown, restDown),
                new LineShape(beginUp, beginDown, restUp, restDown),
                permission, frame));
            start = end;
        }
        return details;
    }

    /// <summary>
    /// Chooses the system count by the page's score: the breaking to engrave instead of the
    /// line DP's own, or null when the line DP's count is the cheapest.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/optimal-page-breaking.cc:41-254 Optimal_page_breaking::solve,
    /// transcribed (the branches for a forced <c>page-count</c> and <c>systems-per-page</c>
    /// are not modelled — Lily# has neither variable at this seam):
    /// <list type="number">
    /// <item>:48-59 — start from the line DP's ideal configuration and count.</item>
    /// <item>:111-128 — price it on its best pages; <c>min_sys_count</c> is the ideal count
    /// less the last page's systems, less the second-last page's too when that page holds
    /// more than one, floored at 1.</item>
    /// <item>:139-190 — try FEWER systems, from the ideal count down to <c>min_sys_count</c>,
    /// keeping the cheapest; stop when a count already needs fewer pages and stretches them
    /// on average, or scores at or beyond BAD_SPACING_PENALTY.</item>
    /// <item>:192-248 — try MORE systems, from the ideal count + 1 up to the most lines the
    /// breaker can make, skipping any count whose <c>min_page_count</c> exceeds the ideal's
    /// page count (:207-211); stop at the first count that scores at or beyond
    /// BAD_SPACING_PENALTY.</item>
    /// </list>
    /// The score is <see cref="PageBreaker.Demerits"/> — Σ line force² + Σ line break
    /// penalty + page-spacing-weight × page demerits — on the estimated details of
    /// <see cref="EstimatedSystemDetails"/>. Every count, the ideal included, is priced from
    /// the SAME estimate, so the comparison is between counts and not between an estimate
    /// and a placement.
    /// <para>
    /// <c>system_count_status_</c> is always <c>SYSTEM_COUNT_OK</c> here: it is set by the
    /// <c>systems-per-page</c> spacer (page-breaking.cc:1461-1470) and by
    /// <c>line_count_status</c> under min/max-systems-per-page, neither of which Lily#'s
    /// breaker reports as a status (it prices them as penalties). The two early exits that
    /// read it therefore take their unconditional arm: :181 tests <c>!(TOO_MANY)</c>, :244
    /// <c>!(TOO_FEW)</c>.
    /// </para>
    /// <para>
    /// MEASURED on scratch/p321/fx/bis-v6-proper-rests-first.lys (session 322): LilyPond
    /// scores 4 systems 42.466 and 3 systems 38.781 (<c>-ddebug-page-breaking-scoring</c>)
    /// and engraves 2 | 4 | 2; the line DP alone had Lily# at 2 | 2+2 | 2. The family is
    /// HANDOFF §2 T7 B-eng, 69 of 286 real-corpus books.
    /// </para>
    /// </remarks>
    private List<int>? ChooseSystemCount(
        MultiStaffScore score, LineBreakSolutions lineBreaks, SystemPass ideal, HeaderBand? header)
    {
        var measures = score.PrimaryContentStaff.PrimaryVoice.Measures;
        if (measures.Length == 0 || ideal.Systems.Count == 0)
            return null;

        var estimate = EstimateMeasureHeights(ideal, measures.Length, _options.StaffHeight);
        var breaker = _pageLayouter.CreateBreaker();
        // The book title is the page's first LINE (paper-book.cc:570-580), priced by the
        // same DP as the systems; the page loop below sees it in front of every candidate.
        var title = header is null ? null : _pageLayouter.BuildTitleDetails(header);
        List<SystemDetails> WithTitle(List<SystemDetails> details)
        {
            if (title is not null)
                details.Insert(0, title);
            return details;
        }
        // The MUSIC systems a page holds: LilyPond's systems_per_page_ counts compressed
        // lines, and its title is compressed INTO the first system's line
        // (page-breaking.cc:155-190 compress_lines); Lily# keeps the title a line of its own,
        // so the first page's count is one more than its systems.
        int SystemsOn(PageBreakResult pages, int page) =>
            pages.SystemsPerPage[page] - (page == 0 && title is not null ? 1 : 0);

        // LILYPOND-REF: :156 space_systems_on_best_pages (i, first_page_num), then the
        // finalize_spacing_result inside it.
        (double demerits, PageBreakResult pages, List<int> breaks)? Score(int lineCount)
        {
            if (lineBreaks.For(lineCount) is not { } candidate)
                return null;
            var details = WithTitle(EstimatedSystemDetails(candidate.Breaks, estimate, measures));
            var pages = breaker.BreakIntoPagesScored(details);
            double demerits = breaker.Demerits(pages, candidate.ForceSquaredSum, candidate.BreakPenaltySum);
            return (demerits, pages, candidate.Breaks);
        }

        // LILYPOND-REF: :48-59, :111 — the ideal configuration on its best pages.
        int idealCount = lineBreaks.IdealLineCount;
        if (Score(idealCount) is not { } best)
            return null;
        var debug = DebugPageBreakingScoring;
        if (debug != null)
        {
            // What each candidate line is priced at — LilyPond's line_details per chunk,
            // laid beside the placed systems' own details (CreatePages prints those).
            debug("estimate: begin up/down per line start "
                + string.Join(" ", lineBreaks.IdealBreaks.Prepend(0).Take(lineBreaks.IdealBreaks.Count)
                    .Select(m => $"{m + 1}:{estimate.BeginUpAt[m]:F3}/{estimate.BeginDownAt[m]:F3}")));
            var idealDetails = PageBreaker.CalcLineHeights(
                WithTitle(EstimatedSystemDetails(lineBreaks.IdealBreaks, estimate, measures)));
            for (int i = 0; i < idealDetails.Count; i++)
                debug($"  est line {i + 1}: {DescribeDetails(idealDetails[i])}");
        }
        double bestDemerits = best.demerits;
        List<int> bestBreaks = best.breaks;
        bool changed = false;
        int pageCount = best.pages.PageCount;

        // LILYPOND-REF: :113-128 min_sys_count.
        int minCount;
        if (pageCount == 0)
        {
            minCount = 1;
        }
        else
        {
            minCount = idealCount - SystemsOn(best.pages, pageCount - 1);
            if (pageCount > 1 && SystemsOn(best.pages, pageCount - 2) > 1)
                minCount -= SystemsOn(best.pages, pageCount - 2);
            if (minCount > idealCount || minCount <= 0)
                minCount = 1;
        }

        // LILYPOND-REF: :139-190 — "try a smaller number of systems than the ideal number
        // for line breaking". The ideal count itself is the first iteration there too.
        debug?.Invoke($"ideal {idealCount} systems on {pageCount} page(s): {best.demerits:F6} "
            + $"(pages {string.Join(",", best.pages.SystemsPerPage)} forces "
            + $"{string.Join(",", best.pages.Forces.Select(f => f.ToString("F3")))}); min_sys_count {minCount}");
        for (int count = idealCount; count >= minCount; count--)
        {
            var cur = Score(count);
            double demerits = cur?.demerits ?? double.PositiveInfinity;
            debug?.Invoke($"trying {count} systems: {demerits:F6}"
                + (cur is { } d ? $" (pages {string.Join(",", d.pages.SystemsPerPage)} forces "
                    + $"{string.Join(",", d.pages.Forces.Select(f => f.ToString("F3")))}; lines {string.Join(",", LineSizes(d.breaks))})"
                    : ""));
            if (demerits < bestDemerits)
            {
                bestDemerits = demerits;
                bestBreaks = cur!.Value.breaks;
                changed = true;
            }
            // :181-189 — under !(best.system_count_status_ & SYSTEM_COUNT_TOO_MANY), which
            // always holds here (see the remarks).
            if (cur is { } c && c.pages.PageCount < pageCount && c.pages.AverageForce > 0)
                break;
            if (demerits >= PageBreaker.BadSpacingPenalty)
                break;
        }

        // LILYPOND-REF: :192-248 — "try a larger number of systems than the ideal line
        // breaking number. This is more or less C&P."
        int prevActualCount = 0;
        for (int count = idealCount + 1; count <= lineBreaks.MaxLineCount; count++)
        {
            double bestDemeritsForThisCount = double.PositiveInfinity;
            if (lineBreaks.For(count) is { } candidate)
            {
                var details = EstimatedSystemDetails(candidate.Breaks, estimate, measures);
                // :207-211 — a count that cannot keep the ideal page count is not priced.
                if (breaker.MinPageCount(details) <= pageCount)
                {
                    var pages = breaker.BreakIntoPagesScored(details);
                    double demerits = breaker.Demerits(
                        pages, candidate.ForceSquaredSum, candidate.BreakPenaltySum);
                    if (demerits < bestDemerits)
                    {
                        bestDemerits = demerits;
                        bestBreaks = candidate.Breaks;
                        changed = true;
                    }
                    bestDemeritsForThisCount = demerits;
                    debug?.Invoke($"trying {count} systems: {demerits:F6} (pages "
                        + $"{string.Join(",", pages.SystemsPerPage)} forces "
                        + $"{string.Join(",", pages.Forces.Select(f => f.ToString("F3")))}; lines "
                        + $"{string.Join(",", LineSizes(candidate.Breaks))})");
                }
                else
                {
                    debug?.Invoke($"trying {count} systems: skipped (min_page_count > {pageCount})");
                }
            }
            else
            {
                debug?.Invoke($"trying {count} systems: unreachable");
            }

            // :234-247 — stop on an infinitely bad count unless we have too few systems and
            // adding one still changes the count; the status is never TOO_FEW here, so the
            // first arm decides.
            int actualCount = bestBreaks.Count;
            const bool tooFewSystems = false;
            if (bestDemeritsForThisCount >= PageBreaker.BadSpacingPenalty
                && (!tooFewSystems || actualCount == prevActualCount))
                break;
            prevActualCount = actualCount;
        }

        debug?.Invoke($"chosen {bestBreaks.Count} systems: {bestDemerits:F6}"
            + (changed ? "" : " (the ideal)"));
        return changed ? bestBreaks : null;
    }

    /// <summary>One line's page-breaking details, for the scoring report: the three heights,
    /// the two silhouette buckets and the tallness the breaker stacks it at.</summary>
    internal static string DescribeDetails(SystemDetails d)
    {
        string shape = d.Shape is { } s
            ? $"begin {s.BeginUp:F3}/{s.BeginDown:F3} rest {s.RestUp:F3}/{s.RestDown:F3}"
            : "-";
        return $"top {d.TopExtent:F3} body {d.StaffHeight:F3} bottom {d.BottomExtent:F3} "
            + $"height {d.Height:F3} shape {shape} tallness {d.Tallness:F3} perm {d.PagePermission}";
    }

    /// <summary>Line sizes (bars per line) of a breaking, for the scoring report.</summary>
    private static IEnumerable<int> LineSizes(List<int> breaks)
    {
        int start = 0;
        foreach (int end in breaks)
        {
            yield return end - start;
            start = end;
        }
    }
}
