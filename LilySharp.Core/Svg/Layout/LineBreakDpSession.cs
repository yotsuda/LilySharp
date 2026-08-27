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

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// The line-break DP's keystroke-crossing ROW-PREFIX resume (2026-08-26 review,
/// finding 4-5): the previous keystroke's whole DP table, kept so the next run
/// recomputes only the rows at and after the first changed spring instead of
/// refilling Θ(n²) per keystroke. Row j of the table is a pure function of
/// springs[0..j-1], the rows before it and the breaker's constants — verified
/// against the recurrence: the cumulative sums a row reads reach index j, the
/// cross-bar pair sum reaches pair (j-2, j-1), the line-start substitution reads
/// springs[i&lt;j], and every dp/prev/lineForce write of the outer loop's
/// iteration j lands in row j. So rows 0..c are BIT-identical to a fresh run's
/// whenever springs[0..c-1] match the stored vector and the constants match —
/// the same operations in the same order on the same values.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ ORTHOGONAL TO THE SESSION-191 VERDICT, deliberately (ARCHIVE 第191 —
/// checked before this was built): both rejected proposals changed the TABLE'S
/// SHAPE (column doubling regressed time +56% and alloc +111%; banding halved
/// alloc but cost time +27%, and preview TIME is the ruling priority). This
/// keeps the exact (n+1)² flat layout and the exact k-search — it only stops
/// REFILLING rows whose inputs did not change, which spends less of both time
/// and allocation. Do not re-litigate the shape.
/// </para>
/// <para>
/// SOUNDNESS: reuse is keyed on the stored spring vector (element equality —
/// the same comparison the F3 gate makes) and the constants (line width, both
/// prefix widths, looseness, ragged). A stale stored vector (the gate skipped
/// the DP for some keystrokes) only shrinks the reusable prefix, never serves a
/// wrong row. When n changes, rows 0..c are re-strided into the new (n+1)²
/// layout (a row's reachable states have k ≤ j, so the copy fits both strides).
/// The arrays are session-resident and MUTATED in place on the same-n path —
/// nothing else holds them (they never escape FindOptimalBreaks except into
/// this session) — the ~20 MB/keystroke re-allocation the review measured is
/// what this trades against one resident copy.
/// </para>
/// <para>
/// The home is <see cref="SystemLayoutCache"/> (one per incremental session,
/// shed whole on a font/paper change), so the CLI full path never constructs
/// one and override books — which run without a consulted cache today — stay
/// on the fresh fill, like every other per-system memo (3-2's second stage).
/// </para>
/// </remarks>
internal sealed class LineBreakDpSession
{
    // --- the key ---
    private double _lineWidth, _firstPrefixWidth, _continuationPrefixWidth, _looseness;
    private bool _raggedRight;
    private MeasureSpringData[]? _springs;

    // --- the table (flat (n+1) x (n+1), exactly FindOptimalBreaks' layout) ---
    private double[]? _dp;
    private int[]? _prev;
    private double[]? _lineForce;
    private int[]? _minLines;
    private int[]? _maxLines;

    /// <summary>Rows reused / rows recomputed over this session's lifetime
    /// (diagnostics / the liveness half of the nets).</summary>
    public (long Reused, long Recomputed) Stats { get; private set; }

    /// <summary>Hands the stored table to a run over <paramref name="n"/> springs,
    /// re-strided if n changed, and answers the first row index to compute
    /// (rows before it are the reused prefix). Answers 1 with fresh arrays when
    /// nothing is reusable. The out arrays are THIS session's — the caller
    /// mutates them and must finish with <see cref="Store"/>.</summary>
    public int Begin(
        MeasureSpringData[] springs, int n,
        double lineWidth, double firstPrefixWidth, double continuationPrefixWidth,
        double looseness, bool raggedRight,
        out double[] dp, out int[] prev, out double[] lineForce,
        out int[] minLines, out int[] maxLines)
    {
        int cols = n + 1;
        bool constantsMatch = _springs != null
            && _lineWidth == lineWidth
            && _firstPrefixWidth == firstPrefixWidth
            && _continuationPrefixWidth == continuationPrefixWidth
            && _looseness == looseness
            && _raggedRight == raggedRight;

        // c = count of leading springs equal to the stored vector; rows 0..c are
        // reusable (row j reads springs[0..j-1] only).
        int c = 0;
        if (constantsMatch)
        {
            var stored = _springs!;
            int limit = Math.Min(n, stored.Length);
            while (c < limit && springs[c].Equals(stored[c]))
                c++;
        }

        if (c > 0 && _springs!.Length == n)
        {
            // Same n: keep the arrays, reset only the rows to recompute.
            dp = _dp!;
            prev = _prev!;
            lineForce = _lineForce!;
            minLines = _minLines!;
            maxLines = _maxLines!;
            for (int j = c + 1; j <= n; j++)
            {
                Array.Fill(dp, KnuthPlassBreaker.Infinity, j * cols, cols);
                Array.Fill(prev, -1, j * cols, cols);
                minLines[j] = int.MaxValue;
                maxLines[j] = int.MinValue;
                // lineForce rows need no reset: every read is guarded by
                // dp[from] < Infinity, and every improving write refreshes it.
            }
            Stats = (Stats.Reused + c, Stats.Recomputed + (n - c));
            return c + 1;
        }

        var freshDp = new double[cols * cols];
        var freshPrev = new int[cols * cols];
        var freshForce = new double[cols * cols];
        var freshMin = new int[cols];
        var freshMax = new int[cols];
        Array.Fill(freshDp, KnuthPlassBreaker.Infinity);
        Array.Fill(freshPrev, -1);
        Array.Fill(freshMin, int.MaxValue);
        Array.Fill(freshMax, int.MinValue);
        freshDp[0] = 0;
        freshForce[0] = 0;
        freshMin[0] = 0;
        freshMax[0] = 0;

        if (c > 0)
        {
            // n changed: re-stride the reusable rows into the new layout. A row's
            // reachable states have k <= j <= c < min(n, old n), so k = 0..j fits
            // both strides.
            int oldCols = _springs!.Length + 1;
            for (int j = 0; j <= c; j++)
            {
                Array.Copy(_dp!, j * oldCols, freshDp, j * cols, j + 1);
                Array.Copy(_prev!, j * oldCols, freshPrev, j * cols, j + 1);
                Array.Copy(_lineForce!, j * oldCols, freshForce, j * cols, j + 1);
                freshMin[j] = _minLines![j];
                freshMax[j] = _maxLines![j];
            }
        }

        dp = freshDp;
        prev = freshPrev;
        lineForce = freshForce;
        minLines = freshMin;
        maxLines = freshMax;
        Stats = (Stats.Reused + c, Stats.Recomputed + (n - c));
        return c + 1;
    }

    /// <summary>Adopts the finished run as the next keystroke's baseline. The
    /// spring array is held by reference — the callers' vectors are immutable
    /// once built (the F3 gate already holds and compares them the same way).</summary>
    public void Store(
        MeasureSpringData[] springs,
        double lineWidth, double firstPrefixWidth, double continuationPrefixWidth,
        double looseness, bool raggedRight,
        double[] dp, int[] prev, double[] lineForce, int[] minLines, int[] maxLines)
    {
        _springs = springs;
        _lineWidth = lineWidth;
        _firstPrefixWidth = firstPrefixWidth;
        _continuationPrefixWidth = continuationPrefixWidth;
        _looseness = looseness;
        _raggedRight = raggedRight;
        _dp = dp;
        _prev = prev;
        _lineForce = lineForce;
        _minLines = minLines;
        _maxLines = maxLines;
    }
}
