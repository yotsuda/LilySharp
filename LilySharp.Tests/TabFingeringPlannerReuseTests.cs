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

using LilySharp.Core.Syntax;
using LilySharp.Core.Tablature;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The fingering planner keeps its trellis on the thread between plans (session 495). A plan
/// must not depend on what the thread planned before it: the same events plan the same
/// strings after a longer, different plan as before it.
/// </summary>
[Trait("Category", "Unit")]
public class TabFingeringPlannerReuseTests
{
    // Open strings and a free hand: the open-string arm (CollectOpenHands and its seen-hand
    // marks) is what a previous plan could leave something behind in.
    private static readonly TabEvent[] Phrase =
    [
        new(40, 0, 1.0, HandFree: true, SlurFromPrevious: false),
        new(47, 0, 0.25, HandFree: false, SlurFromPrevious: false),
        new(45, 0, 0.25, HandFree: false, SlurFromPrevious: false),
        new(52, 0, 0.25, HandFree: true, SlurFromPrevious: false),
        new(55, 0, 0.25, HandFree: false, SlurFromPrevious: false),
        new(59, 0, 0.5, HandFree: false, SlurFromPrevious: false),
        new(57, 0, 0.25, HandFree: false, SlurFromPrevious: true),
        new(64, 0, 0.25, HandFree: true, SlurFromPrevious: false),
    ];

    [Fact]
    public void APlan_IsTheSameAfterAnotherPlanOnTheSameThread()
    {
        var tuning = Tunings.GetTuning(TuningType.Guitar);
        int span = Tunings.HandSpanFor(TuningType.Guitar);

        var before = TabFingeringPlanner.Plan(Phrase, tuning, span);

        // A longer run high on the neck, with its own open strings, in between.
        var other = new TabEvent[40];
        for (int i = 0; i < other.Length; i++)
            other[i] = new(64 + (i * 5) % 17 - (i % 4 == 0 ? 24 : 0), 0, 0.25 + (i % 3) * 0.25,
                HandFree: i % 5 == 0, SlurFromPrevious: i % 7 == 3);
        TabFingeringPlanner.Plan(other, tuning, span);

        Assert.Equal(before, TabFingeringPlanner.Plan(Phrase, tuning, span));
    }
}
