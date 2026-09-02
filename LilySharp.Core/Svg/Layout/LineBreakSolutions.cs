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

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// The line-break DP's finished table, kept so that the WHOLE family of solutions — the
/// best breaking into exactly <c>k</c> lines, for every reachable <c>k</c> — can be read
/// after the run, not only the one the line DP itself prefers.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/constrained-breaking.cc:262-315 Constrained_breaking::line_details
/// (start, end, sys_count) — LilyPond's page breaker asks the line breaker for the
/// <c>sys_count</c>-line solution and its per-line details, one count at a time, while
/// choosing the system count by the PAGE's score (lily/optimal-page-breaking.cc:139-248).
/// This object is that reader: <see cref="For"/> is <c>line_details</c>, and
/// <see cref="IdealBreaks"/> is <c>best_solution</c> (:224-260), the count the page
/// breaker starts from (page-breaking.cc:1007-1036 set_to_ideal_line_configuration).
/// <para>
/// The arrays are the breaker's own (or the <see cref="LineBreakDpSession"/>'s, which the
/// breaker fills in place): row <c>j</c>, column <c>k</c> holds the least demerits for
/// measures <c>0..j-1</c> in exactly <c>k</c> lines, the predecessor break and that
/// solution's LAST line force (KnuthPlassBreaker.Solve). Nothing here writes them. ⚠️ A
/// session's next <c>Begin</c> resets rows in place, so an instance is only read within the
/// keystroke that built it or — the incremental driver's case — while the gate has proven
/// the springs unchanged, which is exactly when <c>Begin</c> is not called.
/// </para>
/// <para>
/// A breaker that fell back to the greedy walk, or a score broken without the DP at all,
/// yields a <see cref="Fixed"/> instance: its one solution is the ideal and
/// <see cref="HasAlternatives"/> is false, so the page-scored count loop has nothing to
/// choose among and leaves the breaking alone.
/// </para>
/// </remarks>
internal sealed class LineBreakSolutions
{
    private readonly MeasureSpringData[]? _springs;
    private readonly int _n;
    private readonly int _cols;
    private readonly double[]? _dp;
    private readonly int[]? _prev;
    private readonly double[]? _lineForce;

    /// <summary>The line DP's own choice — measure END indices, one per line, the last
    /// being the measure count. <c>KnuthPlassBreaker.CreateMeasureGroups</c> turns it into
    /// systems.</summary>
    public List<int> IdealBreaks { get; }

    /// <summary>How many lines <see cref="IdealBreaks"/> has.</summary>
    public int IdealLineCount => IdealBreaks.Count;

    /// <summary>The most lines the table can answer for: one per measure.
    /// LILYPOND-REF: lily/constrained-breaking.cc:339-345 max_system_count — the number of
    /// breakpoints in the range.</summary>
    public int MaxLineCount => _n;

    /// <summary>Whether <see cref="For"/> can answer at all (false for a <see cref="Fixed"/>
    /// instance).</summary>
    public bool HasAlternatives => _dp != null;

    internal LineBreakSolutions(
        MeasureSpringData[] springs, int n, int cols,
        double[] dp, int[] prev, double[] lineForce, List<int> idealBreaks)
    {
        _springs = springs;
        _n = n;
        _cols = cols;
        _dp = dp;
        _prev = prev;
        _lineForce = lineForce;
        IdealBreaks = idealBreaks;
    }

    private LineBreakSolutions(List<int> idealBreaks)
    {
        _n = idealBreaks.Count > 0 ? idealBreaks[^1] : 0;
        IdealBreaks = idealBreaks;
    }

    /// <summary>A breaking with no alternatives: the greedy fallback, or a score whose
    /// systems were not chosen by the DP.</summary>
    internal static LineBreakSolutions Fixed(List<int> idealBreaks) => new(idealBreaks);

    /// <summary>
    /// The best breaking into exactly <paramref name="lineCount"/> lines, with the two sums
    /// the page's score charges the lines for — or null when no such breaking exists.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/constrained-breaking.cc:262-315 line_details — the walk back
    /// through <c>prev_</c> from the end state at (end, sys_count − 1), collecting each
    /// line's stored <c>Line_details</c>; and lily/page-breaking.cc:1564-1569
    /// finalize_spacing_result, which sums those details' <c>force_ * force_</c> and
    /// <c>break_penalty_</c> — WITHOUT the (prev − force)² term the line DP itself
    /// minimised (constrained-breaking.cc:568-573 combine_demerits). That omission is the
    /// whole reason the page-scored count can differ from the line DP's: see
    /// LayoutEngine.ChooseSystemCount.
    /// <para>
    /// The force is the PENALISED force the DP stored (simple-spacer.cc:506-507 →
    /// Line_details::force_), so an unsettable single-measure line carries its −200000 here
    /// too and prices the count out, which is LilyPond's "inf" for a count its constraints
    /// cannot meet. ⚠️ LilyPond's line_details answers an UNREACHABLE count with a patched
    /// configuration (:277-291, the "cannot find line breaking that satisfies constraints"
    /// arm); Lily# answers null and the count loop treats it as infinitely bad, which is
    /// where the patched configuration's demerits land as well when the patch is a line that
    /// does not fit.
    /// </para>
    /// </remarks>
    public LineBreakCandidate? For(int lineCount)
    {
        if (_dp == null || lineCount < 1 || lineCount > _n)
            return null;
        if (_dp[_n * _cols + lineCount] >= KnuthPlassBreaker.Infinity)
            return null;

        var breaks = new List<int>(lineCount);
        double forceSquared = 0;
        double breakPenalty = 0;
        int cur = _n;
        for (int k = lineCount; k > 0; k--)
        {
            double f = _lineForce![cur * _cols + k];
            forceSquared += f * f;
            breaks.Add(cur);
            // The line DP charged the break penalty of the measure a line ends on, for
            // every line but the last (KnuthPlassBreaker.Solve, "1-2").
            if (cur < _n)
                breakPenalty += _springs![cur - 1].BreakPenalty;
            cur = _prev![cur * _cols + k];
            if (cur < 0)
                return null;
        }
        if (cur != 0)
            return null;
        breaks.Reverse();
        return new LineBreakCandidate(breaks, forceSquared, breakPenalty);
    }
}

/// <summary>One exactly-<c>k</c>-line breaking and what the page score charges its lines.</summary>
/// <param name="Breaks">Measure END indices, one per line.</param>
/// <param name="ForceSquaredSum">Σ force² over the lines (penalised forces).</param>
/// <param name="BreakPenaltySum">Σ break_penalty_ over the lines' end measures.</param>
internal readonly record struct LineBreakCandidate(
    List<int> Breaks, double ForceSquaredSum, double BreakPenaltySum);
