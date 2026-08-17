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

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;

namespace LilySharp.Benchmarks;

/// <summary>
/// The line-break DP alone — <c>KnuthPlassBreaker.BreakIntoLines(measures, springData)</c>,
/// i.e. <c>FindOptimalBreaks</c> plus the regrouping, with the spring vector precomputed
/// exactly as the incremental compiler hands it over.
/// </summary>
/// <remarks>
/// ⚠️ WHY THIS EXISTS: to make a TIME question answerable that the repo's usual instruments
/// cannot answer. Keystroke time on these machines swings 16% between runs of the same
/// build (HANDOFF RULES §5.3), so a change worth a few per cent of one stage is invisible
/// there; allocation is deterministic but says nothing about a change that TRADES
/// allocation for time. This DP is the standing example: it allocates <c>dp</c>,
/// <c>prev</c> and <c>lineForce</c> at (n+1)² each — 20.0 MB on a 1000-bar book, measured
/// as 19.3 MB and 29% of perf-plain1k's whole keystroke — and <c>dp[j,k]</c> is meaningless
/// for k &gt; j, so half of each array is never touched. Making them jagged halves the
/// allocation AND the Array.Fill, at the price of one more indirection in the inner loop.
/// That is a trade, and this benchmark is what decides it.
/// <para>
/// ⚠️ READ THE ERROR COLUMN BEFORE THE MEAN. A benchmark that cannot resolve the size of
/// the difference in question has not answered it — the number to check first is whether
/// the confidence interval is narrower than the effect being argued about.
/// </para>
/// <para>
/// ⚠️ NOT A SECOND HOME FOR KEYSTROKE TIME. <c>EditKeystrokeBench</c> (in LilySharp.Tests)
/// still owns "what does one edit cost end to end", and <c>audit/LilySharp.Probe -- alloc</c>
/// owns allocation. This is one FUNCTION under a harness that can see per-cent differences,
/// which neither of those can.
/// </para>
/// <para>
/// Run: <c>dotnet run -c Release --project LilySharp.Benchmarks -- --filter '*LineBreakDp*'</c>
/// </para>
/// </remarks>
[Config(typeof(Config))]
[MemoryDiagnoser]
public class LineBreakDpBenchmark
{
    /// <summary>
    /// ⚠️ IN-PROCESS ON PURPOSE, AND IT IS THE MACHINE, NOT THE BENCHMARK. BenchmarkDotNet's
    /// default toolchain compiles a job assembly and launches it; on this machine the
    /// application-control policy BLOCKS loading it —
    /// <c>System.IO.FileLoadException … 0x800711C7</c> — and every benchmark reports NA with
    /// BenchmarkDotNet's own guess ("might be caused by antivirus software") as the only
    /// clue. The in-process emit toolchain runs the benchmark in the host process and never
    /// writes an assembly to load, so it is unaffected.
    /// <para>
    /// ⚠️ The sibling benchmarks in this project still use <c>[SimpleJob]</c> and will hit
    /// the same wall on a machine with this policy. They were not changed here because none
    /// of them was run for this work — but that is where to start if one reports NA.
    /// </para>
    /// </summary>
    private sealed class Config : ManualConfig
    {
        public Config() => AddJob(Job.Default.WithToolchain(InProcessEmitToolchain.Instance));
    }

    /// <summary>Bar counts, so the DP's shape is visible rather than assumed: the state
    /// table is (break index × line count), so both the allocation and the walk are
    /// quadratic, and a run that does not show ~4× per doubling is measuring something
    /// else.</summary>
    [Params(250, 500, 1000)]
    public int Bars;

    private IReadOnlyList<Measure> _measures = null!;
    private MeasureSpringData[] _springs = null!;
    private KnuthPlassBreaker _breaker = null!;

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir, "LilySharp.Core")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException("repo root (no ancestor holds LilySharp.Core)");
    }

    /// <summary>The plain 1000-bar book truncated to <see cref="Bars"/> bars — the same
    /// source the alloc probe prices, so the two instruments talk about one book.</summary>
    [GlobalSetup]
    public void Setup()
    {
        var path = Path.Combine(FindRepoRoot(), "audit", "lpreg", "perf-plain1k.lys");
        var text = File.ReadAllText(path).Replace("\r\n", "\n");
        var tree = SyntaxTree.Parse(text);
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));

        double shortest = SpacingRules.CalculateCommonShortestDuration(score);
        var all = SystemBreaker.ComputeMultiStaffSpringData(score, shortest);
        var measures = score.PrimaryContentStaff.PrimaryVoice.Measures;

        int n = Math.Min(Bars, measures.Length);
        _measures = measures.Take(n).ToList();
        _springs = all.Take(n).ToArray();
        // The paper the corpus is engraved on; the exact width only has to be the SAME
        // between the two sides of any A/B, and a realistic one keeps the reachable
        // line-count band realistic too.
        _breaker = new KnuthPlassBreaker(
            lineWidth: 180.0, firstPrefixWidth: 10.0, continuationPrefixWidth: 6.0);
    }

    /// <summary>The line count is returned so the DP cannot be optimised away, and so a
    /// run that silently stops breaking (every bar its own line, or one line) is visible
    /// in the output rather than hidden in a time.</summary>
    [Benchmark(Description = "BreakIntoLines — the DP with springs precomputed")]
    public int Break() => _breaker.BreakIntoLines(_measures, _springs).Count;

    /// <summary>
    /// ⚠️ THE PREMISE, PRINTED — a timing run cannot show it and a degenerate one still
    /// produces a plausible curve. If the DP put every bar on its own line (or all of them
    /// on one) the state band would be trivial and the times below would be measuring the
    /// wrong shape entirely, which is exactly the trap HANDOFF §5.3 keeps recording under
    /// "the bench book is not the book you think it is".
    /// </summary>
    [GlobalCleanup]
    public void ReportShape()
    {
        var lines = _breaker.BreakIntoLines(_measures, _springs);
        Console.WriteLine($"  [shape] bars={_measures.Count} lines={lines.Count} "
            + $"bars/line={(double)_measures.Count / Math.Max(1, lines.Count):F2}");
    }
}
