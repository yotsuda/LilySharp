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
using System.IO;
using System.Linq;
using System.Text;
using LilySharp.Core.Syntax;
using LilySharp.Core.Tablature;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The bass fingering planner is FROZEN (owner's decision, 2026-10-25: "a user who fingered a
/// tab with care must not see it change under a new version"). This holds its every string over
/// a fixed set of generated bass lines, so any change to its costs or its code turns it red.
/// </summary>
/// <remarks>
/// The lines are generated, not taken from books — the owner's corpus is copyrighted music and
/// stays out of the repository — by a fixed generator: walks and leaps in the bass range,
/// eighths to whole notes, rests, ties and slurs, on four and five strings. A change that is
/// MEANT (approved by the owner, written in the changelog) regenerates the expected file with
/// LILYSHARP_UPDATE_BASS_GOLDEN=1.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class BassFingeringGoldenTests
{
    private const int Lines = 400;
    private const int NotesPerLine = 48;

    private static string GoldenPath() => Path.Combine(CollectResumeTests.FindRepoRoot(),
        "LilySharp.Tests", "Fixtures", "tabfingering", "bass-golden.txt");

    /// <summary>A xorshift generator: the same numbers on every runtime, unlike System.Random.</summary>
    private struct Xorshift(uint seed)
    {
        private uint _s = seed == 0 ? 1u : seed;
        public int Next(int max)
        {
            _s ^= _s << 13; _s ^= _s >> 17; _s ^= _s << 5;
            return (int)(_s % (uint)max);
        }
    }

    internal static (TabEvent[] Events, int[] Tuning) Line(int index)
    {
        var rng = new Xorshift((uint)(index * 2654435761u + 12345));
        int[] tuning = index % 3 == 2 ? Tunings.GetTuning(TuningType.Bass5) : Tunings.GetTuning(TuningType.Bass);
        int low = tuning[0], high = tuning[^1] + 14;
        int midi = low + 5 + rng.Next(14);
        double[] durations = [0.125, 0.125, 0.25, 0.25, 0.25, 0.5, 1.0, 1.0 / 16];
        var events = new List<TabEvent>();
        double sinceLast = 0.25;
        bool rest = false, slurOpen = false;
        int? previous = null;
        while (events.Count < NotesPerLine)
        {
            double d = durations[rng.Next(durations.Length)];
            if (rng.Next(10) == 0) { rest = true; sinceLast += d; continue; }
            bool tie = previous.HasValue && !rest && rng.Next(14) == 0;
            if (!tie)
            {
                int r = rng.Next(20);
                int step = r < 6 ? 0 : r < 13 ? rng.Next(5) - 2 : r < 17 ? (rng.Next(2) == 0 ? 5 : 7) * (rng.Next(2) == 0 ? 1 : -1) : rng.Next(2) == 0 ? 12 : -12;
                midi = Math.Clamp(midi + step, low, high);
            }
            bool slur = !tie && slurOpen && previous.HasValue;
            bool leapt = previous is { } p && Math.Abs(midi - p) >= Tunings.LeapResetInterval;
            events.Add(new TabEvent(midi, 0, events.Count == 0 ? 1.0 : sinceLast,
                HandFree: rest || leapt, SlurFromPrevious: slur && !leapt, TiedFromPrevious: tie));
            slurOpen = !slurOpen && rng.Next(6) == 0;
            previous = midi;
            sinceLast = d;
            rest = false;
        }
        return (events.ToArray(), tuning);
    }

    private static string Render()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < Lines; i++)
        {
            var (events, tuning) = Line(i);
            sb.Append(string.Concat(BassFingeringPlanner.Plan(events, tuning))).Append('\n');
        }
        return sb.ToString();
    }

    [Fact]
    public void ThePlannerPlaysTheFrozenFingering()
    {
        string actual = Render();
        if (Environment.GetEnvironmentVariable("LILYSHARP_UPDATE_BASS_GOLDEN") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(GoldenPath())!);
            File.WriteAllText(GoldenPath(), actual);
            return;
        }
        var expected = File.ReadAllText(GoldenPath()).Replace("\r\n", "\n").Split('\n');
        var got = actual.Split('\n');
        for (int i = 0; i < Lines; i++)
            Assert.True(expected[i] == got[i],
                $"line {i}: the frozen bass fingering changed\n  expected {expected[i]}\n  got      {got[i]}\n" +
                "A meant change needs the owner's approval, a changelog line, and LILYSHARP_UPDATE_BASS_GOLDEN=1.");
    }
}
