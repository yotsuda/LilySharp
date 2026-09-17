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
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The net for the gather seek (session 402, R13): a resumed collect enters a container's
/// green walk AT the recorded checkpoint's site by its slot path
/// (<see cref="SyntaxNode.TryGreenSiteAt"/> / <see cref="MusicSiteList.TrySeek"/>) instead
/// of gathering every site before it. Two differential nets and a counter:
/// the seeked walk must be the full walk's remainder, site by site, on every container of
/// every net book; a Δ=0 resume from every spread checkpoint must still equal the full
/// collect — and must actually have seeked wherever the checkpoint carries a path (the
/// counter, so the reuse cannot silently fall back to gathering everything).
/// </summary>
public class GatherSeekTests
{
    [Fact]
    public void TheSeekedWalk_IsTheFullWalksRemainder_OnEveryContainerOfEveryNetBook()
    {
        var rule = MeasureCollector.MusicSiteRule(includeParallel: true);
        var failures = new List<string>();
        int containers = 0, sitesChecked = 0;

        foreach (var path in AllBooks())
        {
            SyntaxTree tree;
            try
            {
                tree = SyntaxTree.Parse(File.ReadAllText(path));
            }
            catch
            {
                continue;
            }
            string book = Path.GetFileName(path);

            foreach (var container in GatherRoots(tree.GetRoot()))
            {
                var full = MeasureCollector.MusicSitesLazy(container, includeParallel: true).ToList();
                if (full.Count == 0)
                    continue;
                containers++;
                foreach (int i in Spread(full.Count))
                {
                    sitesChecked++;
                    string where = $"{book} {container.Kind}@{container.Position} site {i}/{full.Count}";
                    var slots = full[i].PathFrom(container);
                    if (slots == null)
                    {
                        failures.Add($"{where}: a site of the container's own walk has no path");
                        continue;
                    }
                    if (!container.TryGreenSiteAt(rule, slots, out var site, out var frames))
                    {
                        failures.Add($"{where}: the seek declined its own path [{string.Join(",", slots)}]");
                        continue;
                    }
                    if (!ReferenceEquals(site.Green, full[i].Green) || site.Position != full[i].Position)
                    {
                        failures.Add($"{where}: the seek landed on {site.Kind}@{site.Position}, the walk has {full[i].Kind}@{full[i].Position}");
                        continue;
                    }
                    if (!ReferenceEquals(site.Node, full[i].Node))
                    {
                        failures.Add($"{where}: the seeked site materializes another red");
                        continue;
                    }
                    var rest = SyntaxNode.GreenSitesLazyFrom(rule, frames).ToList();
                    if (rest.Count != full.Count - i - 1)
                    {
                        failures.Add($"{where}: the walk after the seek has {rest.Count} sites, the full walk {full.Count - i - 1}");
                        continue;
                    }
                    for (int k = 0; k < rest.Count; k++)
                    {
                        var expected = full[i + 1 + k];
                        if (!ReferenceEquals(rest[k].Green, expected.Green) || rest[k].Position != expected.Position
                            || (k < 3 && !ReferenceEquals(rest[k].Node, expected.Node)))
                        {
                            failures.Add($"{where}: site {i + 1 + k} after the seek is {rest[k].Kind}@{rest[k].Position}, the full walk has {expected.Kind}@{expected.Position}");
                            break;
                        }
                    }
                }
            }
        }

        Assert.True(failures.Count == 0,
            $"{failures.Count} seek mismatch(es):\n" + string.Join("\n", failures.Take(20)));
        Assert.True(containers >= 100 && sitesChecked >= 300,
            $"only {containers} containers / {sitesChecked} sites reached the comparison");
    }

    [Fact]
    public void ADriftedPath_IsDeclined_NotFollowed()
    {
        var tree = SyntaxTree.Parse("part v { clef treble }\nsection A {\n  v {\n    c4 d | e f |\n  }\n}\n");
        var block = tree.GetRoot().DescendantNodes<PartBlockSyntax>().Single();
        var rule = MeasureCollector.MusicSiteRule(includeParallel: true);
        var sites = MeasureCollector.MusicSitesLazy(block, includeParallel: true).ToList();
        Assert.Equal(6, sites.Count); // c d | e f |
        var first = sites[0].PathFrom(block)!;
        Assert.True(block.TryGreenSiteAt(rule, first, out var found, out _));
        Assert.Equal(sites[0].Position, found.Position);

        // an empty path, a slot past the end, a token slot, and a leaf the rule does not
        // collect (the note's pitch, one level below a site)
        Assert.False(block.TryGreenSiteAt(rule, [], out _, out _));
        Assert.False(block.TryGreenSiteAt(rule, [block.Green.SlotCount], out _, out _));
        int tokenSlot = Enumerable.Range(0, block.Green.SlotCount).First(s => block.Green.GetSlot(s)?.IsToken == true);
        Assert.False(block.TryGreenSiteAt(rule, [tokenSlot], out _, out _));
        Assert.False(block.TryGreenSiteAt(rule, [first[0], 0], out _, out _));
        // a preset site (a synthetic marker) has no path
        Assert.Null(new GreenSite(PhraseEndMarker.At(0)).PathFrom(block));
    }

    [Fact]
    public void AResumeFromEveryCheckpoint_Seeks_WhereTheCheckpointHasAPath_AndStillEqualsTheFullCollect()
    {
        var failures = new List<string>();
        int resumes = 0, seeked = 0, unpathed = 0;
        foreach (var path in AllBooks())
        {
            var text = File.ReadAllText(path);
            if (text.Contains("using \"", StringComparison.Ordinal))
                continue;
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
                continue;
            }
            string book = Path.GetFileName(path);
            foreach (var (ordinal, recording) in recorder.Recordings)
            {
                if (recording.IneligibleReason != null || recording.Checkpoints.Count == 0)
                    continue;
                foreach (int i in Spread(recording.Checkpoints.Count))
                {
                    var checkpoint = recording.Checkpoints[i];
                    if (checkpoint.MeasureCount == 0)
                        continue;
                    var resumer = CollectWalkProbe.Resumer();
                    var plan = new VoiceResumePlan { Checkpoint = checkpoint, Recording = recording, Source = source };
                    resumer.ResumePlans[ordinal] = plan;
                    string where = $"{book} walk#{ordinal} @measure {checkpoint.MeasureCount} (node {checkpoint.NodeIndex}, path {(checkpoint.GatherPath == null ? "-" : string.Join(",", checkpoint.GatherPath))})";
                    try
                    {
                        var collector = new MeasureCollector { ScoreTranspose = spec?.ScoreTranspose, WalkProbe = resumer };
                        var resumed = SvgGenerator.CollectScore(collector, tree, spec);
                        resumes++;
                        if (!plan.Consumed)
                        {
                            failures.Add($"{where}: the plan was never consumed");
                            continue;
                        }
                        int expectedSeeks = checkpoint.GatherPath != null ? 1 : 0;
                        if (resumer.GatherSeeks != expectedSeeks)
                            failures.Add($"{where}: {resumer.GatherSeeks} seeks, expected {expectedSeeks}");
                        if (checkpoint.GatherPath != null) seeked++; else unpathed++;
                        var diff = ModelDeepDiff.FirstDifference(full, resumed, "score");
                        if (diff != null)
                            failures.Add($"{where}: {diff}");
                    }
                    catch (Exception ex)
                    {
                        failures.Add($"{where}: threw {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }
        }
        Assert.True(failures.Count == 0,
            $"{failures.Count} failure(s):\n" + string.Join("\n", failures.Take(20)));
        Assert.True(seeked >= 50, $"only {seeked} of {resumes} resumes seeked ({unpathed} checkpoints carry no path) — did the recording stop writing paths?");
    }

    [Theory]
    [InlineData("perf-plain1k.lys", 9000)]
    [InlineData("perf-fingbeam1k.lys", 33000)]
    public void AResumeFromTheLastCheckpoint_GathersAlmostNothing_OnThePerfBooks(string name, int sitesInTheBook)
    {
        var path = Path.Combine(CollectResumeTests.FindRepoRoot(), "audit", "lpreg", name);
        if (!File.Exists(path))
            return;
        var tree = SyntaxTree.Parse(File.ReadAllText(path));
        var spec = RenderSpecParser.FindFirst(tree);
        var recorder = CollectWalkProbe.Recorder();
        var source = new MeasureCollector { WalkProbe = recorder, ScoreTranspose = spec?.ScoreTranspose };
        var full = SvgGenerator.CollectScore(source, tree, spec);
        var (ordinal, recording) = recorder.Recordings.Single(r => r.Value.Checkpoints.Count > 0);
        var last = recording.Checkpoints[^1];
        Assert.NotNull(last.GatherPath);

        var resumer = CollectWalkProbe.Resumer();
        resumer.ResumePlans[ordinal] = new VoiceResumePlan { Checkpoint = last, Recording = recording, Source = source };
        var resumed = SvgGenerator.CollectScore(
            new MeasureCollector { WalkProbe = resumer, ScoreTranspose = spec?.ScoreTranspose }, tree, spec);
        Assert.Null(ModelDeepDiff.FirstDifference(full, resumed, "score"));
        Assert.Equal(1, resumer.GatherSeeks);
        // The one bar after the last checkpoint, not the book: the gathered count is
        // the counter this change is about (RULES §7 9 — count, not time).
        Assert.True(resumer.GatherSitesMaterialized < sitesInTheBook / 100,
            $"{resumer.GatherSitesMaterialized} sites gathered for a resume at the last of {sitesInTheBook}");
    }

    private static IEnumerable<string> AllBooks()
    {
        foreach (var p in CollectResumeTests.NetBooks())
            yield return p;
        var perf = Path.Combine(CollectResumeTests.FindRepoRoot(), "audit", "lpreg");
        foreach (var name in new[] { "perf-plain1k.lys", "perf-fingbeam1k.lys", "perf-v2bow1k.lys" })
        {
            var p = Path.Combine(perf, name);
            if (File.Exists(p))
                yield return p;
        }
    }

    /// <summary>The containers the top-level walk gathers (ProcessMusicContainer): every
    /// part block and every section declaration (a part-major cell is one), plus the
    /// root (the section-less path).</summary>
    private static IEnumerable<SyntaxNode> GatherRoots(SyntaxNode root)
    {
        yield return root;
        foreach (var n in root.DescendantNodes())
            if (n is PartBlockSyntax or SectionDeclarationSyntax)
                yield return n;
    }

    /// <summary>The first, the middle, the last, and every 101st — enough to reach past
    /// a big book's opening without walking its remainder a thousand times.</summary>
    private static IEnumerable<int> Spread(int count)
    {
        var picks = new SortedSet<int> { 0, count / 2, count - 1 };
        for (int i = 101; i < count; i += 101)
            picks.Add(i);
        return picks;
    }
}
