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
using System.Linq;
using LilySharp.Core.Svg.Layout;
using Xunit;
using Xunit.Abstractions;

namespace LilySharp.Tests.Svg;

/// <summary>
/// The dot placement Lily# ships must agree with the spelling it replaced, on every input
/// small enough to enumerate.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a reference oracle rather than expected values.</b> <see cref="DotConfiguration"/>
/// is a faithful port of LilyPond's <c>Dot_configuration</c>, and session 427 rewrote its
/// CONTAINER — a <c>SortedDictionary</c> and a LINQ sort became spans — to stop it allocating
/// 1,036 bytes to place one dot. A rewrite of a port cannot be checked against a handful of
/// hand-written cases: the interesting inputs are the ones where a dot lands on a line, is
/// displaced, and cascades into its neighbours, and those are exactly the ones nobody thinks
/// to write down. So the spelling that was there is kept HERE, verbatim, and the two are
/// compared over every input in a box — 100,000-odd of them. RULES §7.7: a reference oracle
/// deliberately kept as a test is not dead code, and §5.3: it must not be able to agree for
/// free, so this copy keeps the dictionary and the LINQ rather than sharing anything with the
/// implementation.
/// </para>
/// <para>
/// ⚠️ WHEN THE PRODUCT'S PLACEMENT IS MEANT TO CHANGE, this oracle is what has to change with
/// it, and deliberately: the LilyPond references live on the product, so a future session
/// porting more of <c>dot-configuration.cc</c> should edit both and say so. What it must never
/// do is delete this to make a red go away.
/// </para>
/// <para>
/// ⚠️ AND IT IS THE ONLY OBSERVER OF ONE OF THEM. Poisoned two ways: making the order sort
/// unstable reddens this and two behaviour tests as well, so ties were already watched — but
/// dropping the line that STOPS the cascade (<c>if (!Contains(dst, m, p)) offset = 0;</c>,
/// LP's <c>shifted()</c> walking on until it finds a free slot) reddens this test and NOTHING
/// else in 8,760. That rule had no observer before this file, the same way session 421 found
/// the resolved-profile merge arm had none.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class DotConfigurationSpellingTests
{
    private readonly ITestOutputHelper _output;

    public DotConfigurationSpellingTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void EveryEnumerableChord_PlacesItsDotsWhereTheOldSpellingDid()
    {
        int cases = 0, dots = 0;

        void Check(int[] positions, int[] directions)
        {
            var mine = DotConfiguration.Resolve(positions, directions);
            var theirs = ReferenceResolve(positions, directions);
            cases++;
            dots += positions.Length;
            if (!mine.SequenceEqual(theirs))
                Assert.Fail(
                    $"positions [{string.Join(",", positions)}] dirs [{string.Join(",", directions)}]"
                    + $" → [{string.Join(",", mine)}] but the old spelling said"
                    + $" [{string.Join(",", theirs)}]");

            // The no-directions overload is a third path through the same code (null vs an
            // all-zero list), so it gets the same comparison rather than being assumed.
            if (directions.All(d => d == 0))
                Assert.Equal(theirs, DotConfiguration.Resolve(positions));
        }

        // ONE dot, the case the corpus is made of — session 427 measured 99.84% of all calls.
        // Positions run well past the staff on both sides, so both on-line and in-space dots
        // are covered at every sign.
        for (int p = -12; p <= 12; p++)
            for (int d = -1; d <= 1; d++)
                Check([p], [d]);

        // TWO, where a displaced dot first has a neighbour to cascade into.
        for (int a = -6; a <= 6; a++)
            for (int b = -6; b <= 6; b++)
                for (int da = -1; da <= 1; da++)
                    for (int db = -1; db <= 1; db++)
                        Check([a, b], [da, db]);

        // THREE, where the cascade can run past one neighbour into the next.
        for (int a = -4; a <= 4; a++)
            for (int b = -4; b <= 4; b++)
                for (int c = -4; c <= 4; c++)
                    for (int da = -1; da <= 1; da++)
                        for (int db = -1; db <= 1; db++)
                            for (int dc = -1; dc <= 1; dc++)
                                Check([a, b, c], [da, db, dc]);

        // FOUR, the widest the owner's corpus reaches (n ≤ 4 in every one of 45,320 calls
        // measured). Directions are held at zero here — the three-dot sweep above already
        // crosses every direction pattern — so this stays a sweep of SHAPES.
        for (int a = -3; a <= 3; a++)
            for (int b = -3; b <= 3; b++)
                for (int c = -3; c <= 3; c++)
                    for (int e = -3; e <= 3; e++)
                        Check([a, b, c, e], [0, 0, 0, 0]);

        // Ties: two dots on ONE position. The sort has to be STABLE for the input indices to
        // land the way they did — LINQ's OrderBy is, and an unstable sort passes everything
        // above and fails here.
        for (int p = -4; p <= 4; p++)
        {
            Check([p, p], [0, 0]);
            Check([p, p, p], [0, 0, 0]);
            Check([p, p, p + 1], [0, 0, 0]);
            Check([p + 1, p, p], [0, 0, 0]);
        }

        // Degenerate shapes the product can hand it.
        Check([], []);
        Check([0], [0]);

        _output.WriteLine($"{cases:N0} chords, {dots:N0} dots, all placed identically");
        // A floor under the sweep itself: a loop bound edited down to make a red go away would
        // otherwise leave a green test comparing almost nothing. 23,718 the day it was written.
        Assert.True(cases >= 23_000, $"only {cases} cases were compared; the sweep shrank");
    }

    // ───────────────────────────────────────────────────────────────────────────────────────
    // THE OLD SPELLING, verbatim as of 7d23bb43 — SortedDictionary keyed by position, LINQ
    // for the order, a fresh configuration object per candidate shift. Kept as the oracle.
    // Do not "simplify" it and do not make it share anything with the product.
    // ───────────────────────────────────────────────────────────────────────────────────────

    private readonly struct RefEntry
    {
        public RefEntry(int originalPos, int inputIndex, int dir)
        {
            OriginalPos = originalPos;
            InputIndex = inputIndex;
            Dir = dir;
        }

        public int OriginalPos { get; }
        public int InputIndex { get; }
        public int Dir { get; }
    }

    private sealed class RefConfig
    {
        internal readonly SortedDictionary<int, RefEntry> Entries = new();

        internal int Badness()
        {
            int total = 0;
            foreach (var (p, ent) in Entries)
            {
                int demerit = 2 * (p - ent.OriginalPos) * (p - ent.OriginalPos);
                int moveDir = System.Math.Sign(p - ent.OriginalPos);
                if (ent.Dir != 0 && moveDir != ent.Dir)
                    demerit += 2;
                else if (moveDir != 1)
                    demerit += 1;
                total += demerit;
            }
            return total;
        }

        internal RefConfig Shifted(int k, int d)
        {
            var newCfg = new RefConfig();
            int offset = 0;

            void Process(int p, RefEntry ent)
            {
                if (p == k)
                {
                    p += IsOnLine(p) ? d : 2 * d;
                    offset = 2 * d;
                    newCfg.Entries[p] = ent;
                }
                else
                {
                    if (!newCfg.Entries.ContainsKey(p))
                        offset = 0;
                    newCfg.Entries[p + offset] = ent;
                }
            }

            if (d > 0)
            {
                foreach (var (p, ent) in Entries)
                    Process(p, ent);
            }
            else
            {
                foreach (var (p, ent) in Entries.Reverse())
                    Process(p, ent);
            }

            return newCfg;
        }

        internal void RemoveCollision(int p)
        {
            if (!Entries.ContainsKey(p))
                return;

            var up = Shifted(p, +1);
            var down = Shifted(p, -1);
            var best = up.Badness() < down.Badness() ? up : down;

            Entries.Clear();
            foreach (var (pos, ent) in best.Entries)
                Entries[pos] = ent;
        }
    }

    private static bool IsOnLine(int position) => position % 2 == 0;

    private static int[] ReferenceResolve(
        IReadOnlyList<int> notePositions, IReadOnlyList<int>? directions = null)
    {
        var cfg = new RefConfig();
        var order = Enumerable.Range(0, notePositions.Count)
            .OrderBy(i => notePositions[i])
            .ToArray();

        foreach (int i in order)
        {
            int p = notePositions[i];
            cfg.RemoveCollision(p);
            cfg.Entries[p] = new RefEntry(p, i, dir: directions?[i] ?? 0);
            if (IsOnLine(p))
                cfg.RemoveCollision(p);
        }

        var result = new int[notePositions.Count];
        foreach (var (pos, ent) in cfg.Entries)
            result[ent.InputIndex] = pos;
        return result;
    }
}
