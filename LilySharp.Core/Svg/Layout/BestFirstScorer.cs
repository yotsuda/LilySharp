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

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// A candidate in a lazy best-first score search: it carries a running
/// <see cref="Demerits"/> total (lower is better) and reports when it has been
/// fully scored via <see cref="IsDone"/>.
/// </summary>
internal interface IScorableConfig
{
    /// <summary>Running demerit total; the priority-queue key (lower is better).</summary>
    double Demerits { get; }

    /// <summary>True once every scorer stage has been applied to this candidate.</summary>
    bool IsDone { get; }
}

/// <summary>
/// LilyPond's lazy best-first search over scored candidates, shared by beam and
/// slur quanting. Every candidate is enqueued by its (partial) demerit; the
/// current best is pulled repeatedly — if it is fully scored it wins, otherwise
/// its next scorer stage runs and it is re-enqueued at its updated demerit. The
/// laziness is the point: expensive scorers only run on candidates that stay
/// competitive, so most candidates are abandoned after the cheap early stages.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/beam-quanting.cc:1050-1083 Beam_scoring_problem::solve()
/// LILYPOND-REF: lily/slur-scoring.cc:438-459 get_best_curve()
/// </remarks>
internal static class BestFirstScorer
{
    /// <summary>
    /// Runs the search and returns the winning candidate (the first pulled from
    /// the queue that is <see cref="IScorableConfig.IsDone"/>).
    /// </summary>
    /// <param name="candidates">All seed candidates, already given their initial demerit.</param>
    /// <param name="advanceScorer">
    /// Applies the next scorer stage to a not-yet-done candidate, mutating its
    /// demerit and scorer progress in place. Must eventually make the candidate
    /// <see cref="IScorableConfig.IsDone"/>.
    /// </param>
    /// <remarks>
    /// Behaviour is identical to the hand-written loops it replaced: a
    /// <see cref="PriorityQueue{TElement,TPriority}"/> keyed by
    /// <see cref="IScorableConfig.Demerits"/>, enqueue-all, then
    /// dequeue → done? win : advance and re-enqueue. Candidates are mutated in
    /// place, so <typeparamref name="TConfig"/> must be a reference type.
    /// </remarks>
    public static TConfig Solve<TConfig>(IEnumerable<TConfig> candidates, Action<TConfig> advanceScorer)
        where TConfig : class, IScorableConfig
    {
        var queue = new PriorityQueue<TConfig, double>();
        foreach (var candidate in candidates)
            queue.Enqueue(candidate, candidate.Demerits);

        while (true)
        {
            var best = queue.Dequeue();
            if (best.IsDone)
                return best;

            advanceScorer(best);
            queue.Enqueue(best, best.Demerits);
        }
    }
}
