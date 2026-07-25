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
    private readonly ImmutableArray<Spring> _springs;

    public SpringSolver(ImmutableArray<Spring> springs)
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
        if (_springs.Length == 0)
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
    /// Calculates the force penalty for line-breaking optimization.
    /// Compression (negative force) is penalized more heavily than stretching.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/simple-spacer.cc:309-321 Simple_spacer::force_penalty()
    /// For justified text: penalty = force - (force^4 * 2) if force &lt; 0
    /// For ragged right: penalty = max(0, idealLength - solvedLength)
    /// </remarks>
    public double ForcePenalty(double targetWidth, bool ragged = false)
    {
        var (force, _) = Solve(targetWidth, ragged);

        if (ragged)
        {
            // Ragged: penalize unused space
            return Math.Max(0, TotalLength(0) - targetWidth);
        }

        // Justified: convex compression penalty
        // LILYPOND-REF: simple-spacer.cc:316-320
        double f = force;
        return f - (f < 0 ? f * f * f * f * 2 : 0);
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

                if (idealLen < dist)
                {
                    // Guard the divide the same way the blocking-force branch guards
                    // invK below: a range of springs with zero total ideal length
                    // cannot be scaled up, and dist / 0 would poison every spring with
                    // NaN (0 * +Inf). Valid springs always have IdealDistance > 0, so
                    // this only trips on degenerate input; skip the rod rather than NaN.
                    if (idealLen <= 0)
                        continue;

                    // Need to scale up ideal distances
                    double factor = dist / idealLen;
                    for (int i = left; i < right; i++)
                    {
                        // LILYPOND-REF: lily/simple-spacer.cc:116-117
                        //   springs_[i].set_ideal_distance (… * factor) — the SETTER, which
                        //   leaves both strengths alone (lily/spring.cc:131-141).
                        result[i] = result[i].WithIdealDistance(result[i].IdealDistance * factor);
                    }
                    changed = true;
                }
                else
                {
                    // Calculate blocking force for this rod
                    double invK = 0;
                    for (int i = left; i < right; i++)
                        invK += result[i].InverseCompressStrength;

                    if (invK > 0)
                    {
                        double blockForce = (dist - idealLen) / invK;
                        for (int i = left; i < right; i++)
                        {
                            var s = result[i];
                            double newBlockForce = Math.Max(blockForce, s.BlockingForce);
                            if (newBlockForce > s.BlockingForce)
                            {
                                double newMin = Math.Max(s.MinDistance,
                                    s.IdealDistance + newBlockForce * s.InverseCompressStrength);
                                // LILYPOND-REF: lily/spring.cc:183-195
                                //   Spring::set_blocking_force sets min_distance_ = length (f)
                                //   and then only update_blocking_force (): the strengths are
                                //   what make the new blocking force come out as f, so
                                //   recomputing them here would undo the rod.
                                result[i] = s.WithMinDistance(newMin);
                                changed = true;
                            }
                        }
                    }
                }
            }

            if (!changed)
                break;
        }

        return result.ToImmutableArray();
    }
}