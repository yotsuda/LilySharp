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

using System.Threading.Tasks;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Collector;

/// <summary>
/// The voice walk as a coroutine (docs/moment-walk-design.md §3.1, stage ③): the walk below
/// <see cref="CollectMeasures"/> / <see cref="CollectMeasuresFromNode"/> is <c>async ValueTask</c>,
/// and every place a moment advances — the three <see cref="MeasureBuilder"/> funnels — first
/// asks the staff's clock to reach that moment. With one voice the clock is always there, the
/// walk never suspends, and the two entry points run it to completion synchronously: the
/// page, the MIDI, the MusicXML and the twin are byte-identical to the walk before it had a
/// clock, and no state machine is ever boxed (an <c>async ValueTask</c> allocates only on a
/// suspension).
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/global-context.cc:174-203 Global_context::iterate — the loop that
/// advances one moment at a time and processes every iterator up to it; the clock here is
/// the Lily# place that loop will stand in once the voices of a staff are scheduled together
/// (stage ⑤). Until then <see cref="VoiceClock.ReachAsync"/> is the completed task, and
/// <see cref="Run{T}"/> refuses a suspension out loud rather than blocking on it.
/// </remarks>
public sealed partial class MeasureCollector
{
    /// <summary>The staff's clock a voice walk waits on before it advances a moment.</summary>
    internal sealed class VoiceClock
    {
        /// <summary>How many times a walk asked to reach a moment (one per funnel call).</summary>
        public int Reaches { get; private set; }

        /// <summary>How many of those asks suspended the walk — 0 until stage ⑤ schedules
        /// several voices on one clock.</summary>
        public int Suspensions { get; private set; }

        /// <summary>
        /// Completes when the clock has reached the moment <paramref name="builder"/> stands
        /// at (<see cref="MeasureBuilder.CurrentMeasureIndex"/>, <see cref="MeasureBuilder.CurrentMoment"/>).
        /// One voice on the clock: already reached, always.
        /// </summary>
        public ValueTask ReachAsync(MeasureBuilder builder)
        {
            Reaches++;
            return default;
        }
    }

    private readonly VoiceClock _clock = new();

    /// <summary>The clock the walks of this collector wait on (counters for the nets and probes).</summary>
    internal VoiceClock Clock => _clock;

    /// <summary>
    /// Runs a walk to completion from a synchronous entry point. The walk below has one voice
    /// on the clock and never suspends; should it ever (a scheduler that is not yet there), this
    /// says so instead of blocking a thread on it.
    /// </summary>
    private static T Run<T>(ValueTask<T> walk)
    {
        if (!walk.IsCompleted)
            throw new InvalidOperationException(
                "The voice walk suspended on its clock with no scheduler to resume it (docs/moment-walk-design.md stage 3: one voice never suspends).");
        return walk.GetAwaiter().GetResult();
    }

    /// <summary><see cref="Run{T}"/> for the nets (VoiceWalkCoroutineTests), which hand it a
    /// task of their own making.</summary>
    internal static T RunForTests<T>(ValueTask<T> walk) => Run(walk);

    // The three funnels, gated. The walk calls these — never builder.Add* directly — so the
    // one place a moment advances is the one place the clock is asked (the calls are the
    // coroutine's yield points; a grep for `builder.Add` in the walk should find none).

    /// <summary>Reaches the builder's moment, then <see cref="MeasureBuilder.AddItem"/>.</summary>
    private async ValueTask AddItemAsync(MeasureBuilder builder, MusicItem item)
    {
        await _clock.ReachAsync(builder);
        builder.AddItem(item);
    }

    /// <summary>Reaches the builder's moment, then <see cref="MeasureBuilder.AddItemWithoutDuration"/>.</summary>
    private async ValueTask AddItemWithoutDurationAsync(MeasureBuilder builder, MusicItem item)
    {
        await _clock.ReachAsync(builder);
        builder.AddItemWithoutDuration(item);
    }

    /// <summary>Reaches the builder's moment, then <see cref="MeasureBuilder.AddDuration"/>.</summary>
    private async ValueTask AddDurationAsync(MeasureBuilder builder, Fraction duration, int sourcePosition)
    {
        await _clock.ReachAsync(builder);
        builder.AddDuration(duration, sourcePosition);
    }
}
