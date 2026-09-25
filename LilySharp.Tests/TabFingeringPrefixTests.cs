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
using System.Linq;
using LilySharp.Core.Syntax;
using LilySharp.Core.Tablature;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A plan that shares its first events with the previous plan on the thread starts its solve
/// after them (the trellis' shared prefix, session 590). The answer must be the one a plan
/// with no history gives — including where a LATER event changes the strings of the shared
/// ones, which is why the walk back is redone — and the prefix must actually be taken.
/// </summary>
[Trait("Category", "Unit")]
public class TabFingeringPrefixTests
{
    private static TabEvent[] RandomEvents(Random rng, int count)
    {
        var events = new TabEvent[count];
        double[] times = [1.0 / 16, 1.0 / 8, 1.0 / 4, 1.0 / 2, 1.0];
        for (int i = 0; i < count; i++)
        {
            bool tied = i > 0 && rng.Next(20) == 0;
            events[i] = new TabEvent(
                Midi: tied ? events[i - 1].Midi : 28 + rng.Next(32),
                FixedString: rng.Next(8) == 0 ? 1 + rng.Next(4) : 0,
                TimeToMove: times[rng.Next(times.Length)],
                HandFree: rng.Next(6) == 0,
                SlurFromPrevious: rng.Next(10) == 0,
                TiedFromPrevious: tied);
        }
        return events;
    }

    [Fact]
    public void APlanSharingThePreviousPlansPrefix_AnswersAsAPlanWithNoHistory()
    {
        var tuning = Tunings.GetTuning(TuningType.Bass);
        int span = Tunings.HandSpanFor(TuningType.Bass);
        var rng = new Random(590);
        int resumed = 0, prefixMoved = 0, forced = 0;
        for (int trial = 0; trial < 300; trial++)
        {
            var a = RandomEvents(rng, 5 + rng.Next(60));
            int k = rng.Next(a.Length + 1);
            var b = a.Take(k).Concat(RandomEvents(rng, rng.Next(30))).ToArray();
            if (b.Length == 0)
                continue;
            // The first event after the shared ones, where a[k] allows it, becomes a pitch a[k]
            // could not be played as on any string: 28 sounds only on the E string (4), 66 only
            // on the G string (1). A plan that wrongly shared event k then answers k with one of
            // a[k]'s strings, which cannot be right — a random pitch too often lands on the same
            // string by chance (45 and 55 are both on the G string).
            if (k < a.Length && k < b.Length && (a[k].Midi < 43 || a[k].Midi > 52))
            {
                b[k] = b[k] with { Midi = a[k].Midi < 43 ? 66 : 28, FixedString = 0, TiedFromPrevious = false };
                if (k + 1 < b.Length && b[k + 1].TiedFromPrevious)
                    b[k + 1] = b[k + 1] with { Midi = b[k].Midi };
                forced++;
            }

            // No history: the plan before it has another hand span, so nothing is shared.
            TabFingeringPlanner.Plan(a, tuning, span + 1);
            var fresh = TabFingeringPlanner.Plan(b, tuning, span);

            // …and none for a either: a plan of a right after b would share b's prefix, and a
            // wrongly shared event there could carry b's own states back into the plan of b
            // below, where two wrongs make a right (a poison that shares one event too many
            // passed this net that way).
            TabFingeringPlanner.Plan(b, tuning, span + 1);
            var planA = TabFingeringPlanner.Plan(a, tuning, span);
            long before = TabFingeringPlanner.t_resumedEvents;
            var planB = TabFingeringPlanner.Plan(b, tuning, span);

            Assert.Equal(fresh, planB);
            if (TabFingeringPlanner.t_resumedEvents > before)
                resumed++;
            int shared = Math.Min(k, b.Length);
            if (!planA.Take(shared).SequenceEqual(planB.Take(shared)))
                prefixMoved++;
        }
        Assert.True(forced > 50, $"only {forced} trials forced a string change after the shared events");
        Assert.True(resumed > 150, $"only {resumed} plans took the shared prefix — the net is not on the path");
        Assert.True(prefixMoved > 0,
            "no later event ever changed a shared event's string — the net cannot see a walk back that is not redone");
    }

    [Fact]
    public void AnotherTuningOrHandSpan_SharesNothing()
    {
        var rng = new Random(5901);
        var events = RandomEvents(rng, 40);
        var bass = Tunings.GetTuning(TuningType.Bass);
        var guitar = Tunings.GetTuning(TuningType.Guitar);
        int span = Tunings.HandSpanFor(TuningType.Bass);

        TabFingeringPlanner.Plan(events, bass, span);
        long before = TabFingeringPlanner.t_resumedEvents;
        TabFingeringPlanner.Plan(events, guitar, span);
        TabFingeringPlanner.Plan(events, guitar, span + 1);
        Assert.Equal(before, TabFingeringPlanner.t_resumedEvents);

        // …and the same input again shares all of it.
        TabFingeringPlanner.Plan(events, guitar, span + 1);
        Assert.Equal(before + events.Length, TabFingeringPlanner.t_resumedEvents);
    }
}
