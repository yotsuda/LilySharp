// Lily# - Music notation compiler
// Copyright (C) 2025-2026 Yoshifumi Tsuda
//
// Parts of this file are ported from LilyPond, the GNU music typesetter.
// The C# is a modified translation of the following, not a copy of it:
//   lily/simple-spacer.cc
//     Copyright (C) 1999--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>
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

using System.Collections.Immutable;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Solves spring-based spacing to achieve a target width.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/simple-spacer.cc:1-300 Simple_spacer class
///
/// The solver finds a force that, when applied uniformly to all springs,
/// achieves the target width while respecting minimum distance constraints.
///
/// Algorithm (analytical solution, same as LilyPond):
/// 1. Calculate max_block_force (maximum blocking force among all springs)
/// 2. Calculate length at max_block_force
/// 3. If target > length: expand_line (simple linear calculation)
/// 4. If target &lt; length: compress_line (iterative blocking)
/// </remarks>
internal sealed class SpringSolver
{
    private readonly IReadOnlyList<Spring> _springs;

    public SpringSolver(ImmutableArray<Spring> springs)
        : this((IReadOnlyList<Spring>)springs)
    {
    }

    /// <summary>
    /// Over any spring list — the break gate hands a candidate LINE's springs (its measures'
    /// springs end to end, gathered per candidate into a reused buffer) to the same solver
    /// the system layout uses, so the two price a line with one spelling of range_solve.
    /// </summary>
    public SpringSolver(IReadOnlyList<Spring> springs)
    {
        _springs = springs;
    }

    // LILYPOND-REF: lily/simple-spacer.cc:159-162 Simple_spacer::configuration_length()
    /// <summary>
    /// Gets the total length of all springs at the given force.
    /// </summary>
    public double TotalLength(double force)
    {
        double total = 0;
        foreach (var spring in _springs)
        {
            total += spring.Length(force);
        }
        return total;
    }

    /// <summary>
    /// Gets the minimum possible total length (all springs compressed to MinDistance).
    /// </summary>
    public double MinTotalLength => TotalLength(double.NegativeInfinity);

    /// <summary>
    /// Gets the ideal total length (all springs at IdealDistance with zero force).
    /// </summary>
    public double IdealTotalLength => TotalLength(0);

    // LILYPOND-REF: lily/simple-spacer.cc:165-172 Simple_spacer::range_max_block_force()
    /// <summary>
    /// Gets the maximum blocking force among all springs.
    /// </summary>
    private double MaxBlockingForce()
    {
        double result = 0.0;
        foreach (var spring in _springs)
        {
            result = Math.Max(result, spring.BlockingForce);
        }
        return result;
    }

    // LILYPOND-REF: lily/simple-spacer.cc:175-205 Simple_spacer::solve() + range_solve()
    /// <summary>
    /// Finds the force needed to achieve the target total width.
    /// </summary>
    /// <param name="targetWidth">The desired total width</param>
    /// <param name="ragged">If true, negative force means the line doesn't fit</param>
    /// <returns>Solution containing force and whether the line fits</returns>
    public (double Force, bool Fits) Solve(double targetWidth, bool ragged = false)
    {
        if (_springs.Count == 0)
            return (0, true);

        double maxBlockForce = MaxBlockingForce();
        double maxBlockForceLen = TotalLength(maxBlockForce);

        double force;
        bool fits;

        if (maxBlockForceLen < targetWidth)
        {
            // Need to expand
            (force, fits) = ExpandLine(targetWidth, maxBlockForceLen, maxBlockForce);
        }
        else if (maxBlockForceLen > targetWidth)
        {
            // Need to compress
            (force, fits) = CompressLine(targetWidth, maxBlockForceLen, maxBlockForce);
        }
        else
        {
            force = maxBlockForce;
            fits = true;
        }

        // LILYPOND-REF: lily/simple-spacer.cc:201-202
        if (ragged && force < 0)
            fits = false;

        return (force, fits);
    }

    /// <summary>
    /// Finds the force needed to achieve the target total width.
    /// </summary>
    /// <param name="targetWidth">The desired total width</param>
    /// <returns>The force to apply to all springs</returns>
    public double SolveForWidth(double targetWidth)
    {
        return Solve(targetWidth).Force;
    }

    // LILYPOND-REF: lily/simple-spacer.cc:207-225 Simple_spacer::expand_line()
    /// <summary>
    /// Calculates force when expanding the line (target > max_block_force_len).
    /// </summary>
    private (double Force, bool Fits) ExpandLine(double targetLen, double maxBlockForceLen, double maxBlockForce)
    {
        // Sum of all inverse stretch strengths
        double invHooke = 0;
        foreach (var spring in _springs)
        {
            invHooke += spring.InverseStretchStrength;
        }

        // Avoid division by zero - if springs are infinitely stiff, report very large force
        if (invHooke == 0.0)
            invHooke = 1e-6;

        // Linear calculation: force = (targetLen - currentLen) / totalFlexibility + currentForce
        double force = (targetLen - maxBlockForceLen) / invHooke + maxBlockForce;
        return (force, true);
    }

    // LILYPOND-REF: lily/simple-spacer.cc:233-288 Simple_spacer::compress_line()
    /// <summary>
    /// Calculates force when compressing the line (target &lt; max_block_force_len).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/simple-spacer.cc:232-287 Simple_spacer::compress_line. LP applies
    /// NO force clamp — it returns the force that reaches the target (fits), or the last
    /// spring's blocking force with fits=false when the line cannot compress far enough.
    /// </remarks>
    private (double Force, bool Fits) CompressLine(double targetLen, double maxBlockForceLen, double maxBlockForce)
    {
        // Check whether we will actually be compressed (negative force) or just less stretched
        double neutralLength = TotalLength(0.0);
        bool compressed = (neutralLength > targetLen);

        double curForce = compressed ? 0.0 : maxBlockForce;
        double curLen = compressed ? neutralLength : maxBlockForceLen;

        // Sort springs by blocking force (descending)
        var sortedSprings = _springs.OrderByDescending(s => s.BlockingForce).ToList();

        // inv_hooke is the total flexibility of currently-active springs
        double invHooke = 0;
        int i = sortedSprings.Count;

        // Add springs that are already active (blocking_force < current_force)
        for (; i > 0 && sortedSprings[i - 1].BlockingForce < curForce; i--)
        {
            invHooke += compressed
                ? sortedSprings[i - 1].InverseCompressStrength
                : sortedSprings[i - 1].InverseStretchStrength;
        }

        // Process remaining springs in order
        for (; i < sortedSprings.Count; i++)
        {
            var sp = sortedSprings[i];

            if (double.IsPositiveInfinity(sp.BlockingForce))
                break;

            // Distance the line would shrink before this spring blocks
            double blockDist = (curForce - sp.BlockingForce) * invHooke;

            // Check if we reach target before this spring blocks
            if (curLen - blockDist < targetLen)
            {
                // LILYPOND-REF: lily/simple-spacer.cc:274-276 — reached the target; the
                // line fits. Return the force unclamped (LP has no compression limit).
                curForce += (targetLen - curLen) / invHooke;
                return (curForce, true);
            }

            // This spring blocks - update state
            curLen -= blockDist;
            invHooke -= compressed ? sp.InverseCompressStrength : sp.InverseStretchStrength;
            curForce = sp.BlockingForce;
        }

        // Couldn't fit: LP returns the last spring's blocking force with fits=false
        // (no clamp). LILYPOND-REF: lily/simple-spacer.cc:285-286.
        return (curForce, false);
    }

    // LILYPOND-REF: lily/simple-spacer.cc:295-305 Simple_spacer::spring_positions()
    /// <summary>
    /// Gets the positions of all items given the solved force.
    /// </summary>
    /// <param name="force">The force from SolveForWidth</param>
    /// <param name="startX">The starting X position</param>
    /// <returns>Array of X positions for each item (N+1 positions for N springs)</returns>
    public ImmutableArray<double> GetPositions(double force, double startX = 0)
    {
        var positions = new List<double> { startX };
        double currentX = startX;

        foreach (var spring in _springs)
        {
            currentX += spring.Length(force);
            positions.Add(currentX);
        }

        return positions.ToImmutableArray();
    }

    /// <summary>
    /// The force-penalty FORMULA on an already-solved force — the ONE spelling of
    /// LilyPond's <c>force_penalty</c> (the breaker's DP and the instance overload
    /// below both read this; 2026-08-26, §5.2.1②).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/simple-spacer.cc:307-319 Simple_spacer::force_penalty() —
    /// ragged: <c>max (0.0, line_len - configuration_length (0.0))</c>, the whitespace
    /// left after the line's natural end; justified: <c>f - (f &lt; 0 ? f^4 * 2 : 0)</c>,
    /// the convex compression penalty.
    /// </remarks>
    public static double ForcePenaltyOf(
        double lineLength, double force, double naturalLength, bool ragged)
    {
        if (ragged)
            return Math.Max(0.0, lineLength - naturalLength);

        double f = force;
        return f - (f < 0 ? f * f * f * f * 2 : 0);
    }

    /// <summary>
    /// Solves for <paramref name="targetWidth"/> and applies
    /// <see cref="ForcePenaltyOf"/> to the result.
    /// </summary>
    /// <remarks>
    /// ⚠️ The RAGGED arm used to be spelled REVERSED here —
    /// <c>max(0, natural − target)</c> — against both LilyPond and the breaker's live
    /// copy, and its own test pinned the reversal while being NAMED
    /// "PenalizesUnusedSpace" (unused space is target − natural). Test-only readers,
    /// so no rendered output ever went through the wrong arm; corrected 2026-08-26
    /// when the two spellings were folded into <see cref="ForcePenaltyOf"/>.
    /// </remarks>
    public double ForcePenalty(double targetWidth, bool ragged = false)
    {
        var (force, _) = Solve(targetWidth, ragged);
        return ForcePenaltyOf(targetWidth, force, TotalLength(0), ragged);
    }

    /// <summary>
    /// Applies multi-column rod constraints to a set of springs.
    /// A rod enforces a minimum total distance across a span of springs.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/simple-spacer.cc:92-128 Simple_spacer::add_rod()
    ///
    /// If the rod's distance exceeds what the springs can provide at maximum compression,
    /// the springs' ideal distances are scaled up proportionally.
    /// Otherwise, blocking forces are updated to enforce the constraint.
    /// </remarks>
    /// <summary>
    /// Applies multi-column rod constraints to a set of springs with blocking force propagation.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/simple-spacer.cc:92-128 Simple_spacer::add_rod()
    ///
    /// After processing all rods, re-checks satisfaction in a convergence loop.
    /// When a rod increases a spring's blocking force, overlapping rods that share
    /// those springs may become unsatisfied, requiring re-propagation.
    /// LILYPOND-REF: lily/simple-spacer.cc:92-128 — rod adding triggers cascade
    /// </remarks>
    public static ImmutableArray<Spring> ApplyRods(
        ImmutableArray<Spring> springs,
        IReadOnlyList<(int Left, int Right, double Distance)> rods)
    {
        if (rods.Count == 0)
            return springs;

        var result = springs.ToArray();

        // LILYPOND-REF: lily/simple-spacer.cc:92-128
        // Convergence loop: re-apply rods until no changes occur,
        // because updating blocking forces for one rod may invalidate others.
        const int maxIterations = 10;
        for (int iteration = 0; iteration < maxIterations; iteration++)
        {
            bool changed = false;

            foreach (var (left, right, dist) in rods)
            {
                if (left < 0 || right > result.Length || left >= right)
                    continue;

                // Check if rod is already satisfied at maximum compression
                double minLen = 0;
                for (int i = left; i < right; i++)
                    minLen += result[i].MinDistance;

                if (minLen >= dist)
                    continue; // Rod already satisfied

                // Calculate ideal length of springs in range
                double idealLen = 0;
                for (int i = left; i < right; i++)
                    idealLen += result[i].IdealDistance;

                // The rod's blocking force: the force at which the range spans exactly
                // dist — a STRETCH (positive) when the rod is longer than the range's
                // ideal, a compression (negative) otherwise. Which stiffness answers is
                // decided by that same comparison, so a rod longer than the ideals
                // STRETCHES the springs in proportion to their stretchability — it does
                // NOT scale the ideals up; that is the fallback below for a range with
                // no give at all. (Scaling here instead is what an over-long loose-column
                // rod used to do: it redistributed the two spanned springs proportionally
                // to their ideals and put the polyphony column 0.30 off LilyPond.)
                // LILYPOND-REF: lily/simple-spacer.cc:76-87 rod_force — range_stiffness
                //   (left, right, dist > ideal_length), infinite stiffness short-circuits;
                //   :147-156 range_stiffness picks stretch vs compress per that flag.
                // ⚠️ SIMPLIFICATION: LilyPond's rod_force runs range_solve, which walks
                // the range's EXISTING blocking forces piecewise; this closed form
                // assumes none are above the answer. The convergence loop below re-checks
                // satisfaction, which is exact whenever the rods land disjoint or nested.
                bool stretchRod = dist > idealLen;
                double invK = 0;
                for (int i = left; i < right; i++)
                    invK += stretchRod
                        ? result[i].InverseStretchStrength
                        : result[i].InverseCompressStrength;

                if (invK <= 0)
                {
                    // Nothing can move in the needed direction: fall back on scaling
                    // the ideals so the range still reaches the rod at force 0.
                    // Valid springs always have IdealDistance > 0; guard the divide so
                    // degenerate input skips the rod rather than poisoning it with NaN.
                    // (LilyPond's own zero-ideal arm sets every ideal to
                    // dist / (right - left) instead, :109-119 — unreachable here, and
                    // named rather than ported.)
                    // LILYPOND-REF: lily/simple-spacer.cc:104-122 add_rod isinf branch — set_ideal_distance
                    //   scales by dist / spring_dist and leaves both strengths alone.
                    if (idealLen < dist && idealLen > 0)
                    {
                        double factor = dist / idealLen;
                        for (int i = left; i < right; i++)
                            result[i] = result[i].WithIdealDistance(
                                result[i].IdealDistance * factor);
                        changed = true;
                    }
                    continue;
                }

                double blockForce = (dist - idealLen) / invK;
                for (int i = left; i < right; i++)
                {
                    var s = result[i];
                    double newBlockForce = Math.Max(blockForce, s.BlockingForce);
                    if (newBlockForce > s.BlockingForce)
                    {
                        // set_blocking_force: min_distance = length (f), whose inverse
                        // constant is the compress one for f < 0 and the stretch one for
                        // f >= 0; the Spring constructor then re-derives the blocking
                        // force from that min, landing back on f.
                        // LILYPOND-REF: lily/spring.cc:183-195 set_blocking_force —
                        //   min_distance_ = length (f); :218-237 length picks inv_k by
                        //   the force's sign.
                        double newMin = Math.Max(s.MinDistance,
                            s.IdealDistance + newBlockForce
                            * (newBlockForce < 0
                                ? s.InverseCompressStrength
                                : s.InverseStretchStrength));
                        result[i] = s.WithMinDistance(newMin);
                        changed = true;
                    }
                }
            }

            if (!changed)
                break;
        }

        return result.ToImmutableArray();
    }
}
