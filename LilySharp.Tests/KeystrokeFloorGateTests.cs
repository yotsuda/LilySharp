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
using System.Diagnostics;
using System.IO;
using LilySharp.Core.Svg;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The one latency gate RULES §5.6 asks for: an order-of-magnitude ceiling on the
/// unchanged-tree keystroke floor of every book on the <see cref="PreviewUpdateBench"/> shelf.
/// </summary>
/// <remarks>
/// <para>
/// Latency is a property of the product, not an optimization (§5.6) — yet until this gate the
/// suite had a fidelity ledger that fails on a 1e-9 drift and NOTHING that fails on a 10x
/// keystroke regression. <see cref="PreviewUpdateBench"/> deliberately asserts nothing, and its
/// reason is honoured here: a percentage budget on a shared runner is a flaky test waiting to
/// happen, and an ignored red is worse than none. So this gate only ever fails BY AN ORDER OF
/// MAGNITUDE, and only in one direction — an improvement is never a failure, and a round that
/// merely jitters is absorbed twice over (the floor is the MINIMUM of the rounds, so a failure
/// means every single round was that slow; and the ceiling clears the slowest population below
/// by ~10x).
/// </para>
/// <para>
/// ⚠️ THE POPULATION THIS GATE PRICES (§5.5: every gate says what population it belongs to),
/// measured 2026-08-27 on the dev machine, Windows 11, Release:
/// <list type="bullet">
/// <item>bare `dotnet test`: worst shelf floor 19.2 ms (perf-fingbeam1k; the whole shelf sits
/// in 0.4–19.2 ms)</item>
/// <item>the same run under `--collect:"XPlat Code Coverage"` — which is what CI's test step
/// actually executes: worst floor 151.4 ms, a 7.9x slowdown from coverage instrumentation
/// alone. ⚠️ This is why the ceiling is NOT 5–10x of the bare number: a 200 ms gate would be
/// an always-red on CI while staying green on every dev box.</item>
/// </list>
/// The 1500 ms ceiling is ~10x the instrumented measurement, so a CI runner would additionally
/// have to be ~10x slower than this desktop before honest jitter could touch it; on the
/// instrumented CI leg it trips at roughly 4–8x of today's floor — the §5.6 design window —
/// and on a bare dev box only at ~75x, which still catches every regression this shelf has
/// ever actually seen (4189 ms and 6051 ms per keystroke, sessions 133–135).
/// </para>
/// <para>
/// ⚠️ Debug builds return without measuring: the JIT-unoptimized population was never priced,
/// the product ships Release, and CI runs a Release leg on both OSes — so the gate still
/// stands in every automatic path that matters. A missing book FAILS instead of skipping,
/// because a gate that quietly stops gating is the defect this file exists to close.
/// </para>
/// </remarks>
public class KeystrokeFloorGateTests
{
    /// <summary>One-sided, order-of-magnitude ceiling. See the class remarks before touching:
    /// the number is priced against the coverage-instrumented population, not the bare one.</summary>
    private const double CeilingMs = 1500.0;

    private const int Warmups = 2;
    private const int Rounds = 5;

    [Theory]
    [MemberData(nameof(PreviewUpdateBench.Books), MemberType = typeof(PreviewUpdateBench))]
    public void UnchangedKeystrokeFloorStaysUnderCeiling(string book)
    {
#if DEBUG
        // Not the measured population — see the class remarks. Release legs gate this book.
        _ = book;
#else
        var path = Path.Combine(CollectResumeTests.FindRepoRoot(), "audit", "lpreg", book);
        Assert.True(File.Exists(path), $"{book}: shelf book missing at {path} — the gate would " +
            "quietly stop gating the worst-case floor, so this is a failure, not a skip.");

        var tree = SyntaxTree.Parse(File.ReadAllText(path));
        var options = new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false };
        var compiler = new IncrementalCompiler(tree, options);

        for (int i = 0; i < Warmups; i++)
            compiler.RenderIncremental(tree);

        double floor = double.MaxValue;
        string? svg = null;
        for (int i = 0; i < Rounds; i++)
        {
            var sw = Stopwatch.StartNew();
            svg = compiler.RenderIncremental(tree);
            sw.Stop();
            floor = Math.Min(floor, sw.Elapsed.TotalMilliseconds);
        }

        // A keystroke that got fast by rendering nothing is not fast.
        Assert.False(string.IsNullOrEmpty(svg), $"{book}: RenderIncremental returned no SVG.");
        Assert.True(floor < CeilingMs,
            $"{book}: unchanged-tree keystroke floor {floor:F1} ms is over the {CeilingMs:F0} ms " +
            $"order-of-magnitude ceiling — every one of {Rounds} rounds was at least this slow. " +
            "This is not runner jitter; a keystroke cost has regressed by an order of magnitude " +
            "(RULES §5.6). Measure with PreviewUpdateBench A/B before touching the ceiling.");
#endif
    }
}
