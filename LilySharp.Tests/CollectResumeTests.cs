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
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The completeness net for the collect walk's checkpoint/resume substrate
/// (<see cref="CollectWalkProbe"/> — HANDOFF ▶ ⒭ ⑵'s first slice): a collect
/// RESUMED from any recorded checkpoint must be indistinguishable from a full
/// collect of the same document. This is what holds the checkpoint's state
/// inventory to completeness — a collector field that mutates across measures
/// but is missing from <c>WalkCheckpoint</c> shows up here as a model (or SVG)
/// difference on whichever fixture exercises it.
/// </summary>
/// <remarks>
/// Same-document only (Δ=0) by design — the skip/adopt/restore path itself.
/// The cross-edit (Δ≠0) prefix resume built on top of it (CollectResumePlanner)
/// has its own net, <see cref="CollectEditResumeTests"/>.
/// </remarks>
public class CollectResumeTests
{
    // ---------- the net ----------

    [Fact]
    public void ResumedCollect_MatchesFullCollect_OnEveryFixture()
    {
        var failures = new List<string>();
        int booksWithCheckpoints = 0, resumesRun = 0;

        foreach (var path in NetBooks())
        {
            int resumed = RunBook(path, failures, render: false);
            if (resumed < 0)
                continue; // book skipped (does not collect cleanly / uses `using`)
            if (resumed > 0)
                booksWithCheckpoints++;
            resumesRun += resumed;
        }

        Assert.True(failures.Count == 0,
            $"{failures.Count} resume mismatch(es):\n" + string.Join("\n", failures.Take(20)));
        // The net must actually bite: if eligibility silently tightened to the
        // point where nothing is checkpointed, this is the alarm — not a pass.
        Assert.True(booksWithCheckpoints >= 20,
            $"only {booksWithCheckpoints} books produced any checkpoint (resumes run: {resumesRun}) — eligibility collapsed?");
    }

    [Fact]
    public void ResumedCollect_RendersByteIdenticalSvg_OnSubset()
    {
        // Full pipeline double-check on a small subset: the deep model compare
        // above is the wide net; this pins layout+render byte identity too.
        var failures = new List<string>();
        int rendered = 0;
        foreach (var path in NetBooks())
        {
            int resumed = RunBook(path, failures, render: true);
            if (resumed > 0 && ++rendered >= 3)
                break;
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
        Assert.True(rendered >= 3, $"only {rendered} books reached the SVG comparison");
    }

    [Fact]
    public void ResumedCollect_MatchesFullCollect_OnPerfBooks()
    {
        // The books the memo is being built for (scratch/lpreg — skip when absent,
        // e.g. on a checkout without the perf corpus).
        var dir = Path.Combine(FindRepoRoot(), "scratch", "lpreg");
        var failures = new List<string>();
        int resumes = 0;
        foreach (var name in new[] { "perf-plain1k.lys", "perf-fingbeam1k.lys", "perf-v2bow1k.lys" })
        {
            var path = Path.Combine(dir, name);
            if (!File.Exists(path))
                continue;
            int resumed = RunBook(path, failures, render: false);
            Assert.True(resumed > 0, $"{name}: no checkpoint was recorded — the target regime fell out of eligibility");
            resumes += resumed;
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    // ---------- runner ----------

    /// <summary>Collects <paramref name="path"/> fully under a recorder, then
    /// re-collects resumed from a spread of its checkpoints and compares.
    /// Returns the number of resumes run, or -1 when the book is skipped.</summary>
    private static int RunBook(string path, List<string> failures, bool render)
    {
        var text = File.ReadAllText(path);
        if (text.Contains("using \"", StringComparison.Ordinal))
            return -1; // `using` books are expanded by the LSP before collect

        SyntaxTree tree;
        RenderSpec? spec;
        MultiStaffScore full;
        var recorder = CollectWalkProbe.Recorder();
        var source = new MeasureCollector { WalkProbe = recorder };
        try
        {
            tree = SyntaxTree.Parse(text);
            spec = RenderSpecParser.FindFirst(tree);
            source.ScoreTranspose = spec?.ScoreTranspose;
            full = SvgGenerator.CollectScore(source, tree, spec);
        }
        catch
        {
            return -1; // the net covers books that collect cleanly today
        }

        string? fullSvg = render ? Render(full) : null;

        string book = Path.GetFileName(path);
        int resumes = 0;
        foreach (var (ordinal, recording) in recorder.Recordings)
        {
            if (recording.IneligibleReason != null || recording.Checkpoints.Count == 0)
                continue;

            foreach (var checkpoint in Spread(recording.Checkpoints))
            {
                var resumer = CollectWalkProbe.Resumer();
                var plan = new VoiceResumePlan
                {
                    Checkpoint = checkpoint,
                    Recording = recording,
                    Source = source,
                };
                resumer.ResumePlans[ordinal] = plan;

                string where = $"{book} walk#{ordinal} ({recording.VoiceName ?? "-"}) " +
                    $"@measure {checkpoint.MeasureCount} (visit {checkpoint.SectionVisit}, " +
                    $"inv {checkpoint.Invocation}, node {checkpoint.NodeIndex})";
                try
                {
                    var collector = new MeasureCollector
                    {
                        ScoreTranspose = spec?.ScoreTranspose,
                        WalkProbe = resumer,
                    };
                    var resumedScore = SvgGenerator.CollectScore(collector, tree, spec);
                    resumes++;

                    if (!plan.Consumed)
                    {
                        failures.Add($"{where}: the plan was never consumed");
                        continue;
                    }
                    if (render)
                    {
                        var resumedSvg = Render(resumedScore);
                        if (resumedSvg != fullSvg)
                            failures.Add($"{where}: SVG differs ({FirstSvgDiff(fullSvg!, resumedSvg)})");
                    }
                    else
                    {
                        var diff = ModelDeepDiff.FirstDifference(full, resumedScore, "score");
                        if (diff != null)
                            failures.Add($"{where}: {diff}");
                    }
                }
                catch (Exception ex)
                {
                    failures.Add($"{where}: threw {ex.GetType().Name}: {ex.Message}");
                }
            }
        }
        return resumes;
    }

    /// <summary>Up to three checkpoints spread across the walk — the very first
    /// (empty prefix), the middle, and the last (maximal prefix).</summary>
    private static IEnumerable<WalkCheckpoint> Spread(List<WalkCheckpoint> checkpoints)
    {
        var picks = new[] { 0, checkpoints.Count / 2, checkpoints.Count - 1 }
            .Distinct();
        foreach (int i in picks)
            yield return checkpoints[i];
    }

    private static string Render(MultiStaffScore score)
        => SvgGenerator.RenderToSvg(score, new LayoutEngine().Layout(score), new SvgRenderOptions());

    private static string FirstSvgDiff(string a, string b)
    {
        int n = Math.Min(a.Length, b.Length);
        int i = 0;
        while (i < n && a[i] == b[i]) i++;
        var context = a.Substring(Math.Max(0, i - 40), Math.Min(80, a.Length - Math.Max(0, i - 40)));
        return $"lengths {a.Length}/{b.Length}, first diff at {i}: …{context}…";
    }

    // ---------- book discovery (shared with CollectEditResumeTests) ----------

    internal static IEnumerable<string> NetBooks()
    {
        var root = FindRepoRoot();
        var dirs = new[]
        {
            Path.Combine(root, "LilySharp.Tests", "Fixtures"),
            Path.Combine(root, "samples"),
        };
        foreach (var dir in dirs)
        {
            if (!Directory.Exists(dir))
                continue;
            foreach (var f in Directory.EnumerateFiles(dir, "*.lys", SearchOption.AllDirectories)
                         .OrderBy(f => f, StringComparer.Ordinal))
                yield return f;
        }
    }

    internal static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir, "LilySharp.Tests", "Fixtures")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException("Cannot find the repository root");
    }

    // Exact deep comparison: ModelDeepDiff.cs (shared with CollectEditResumeTests).
}
