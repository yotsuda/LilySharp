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
using System.IO;
using System.Linq;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The drift net for the green-tree music gather
/// (<see cref="MeasureCollector.MusicSites"/> — HANDOFF's retired ▶ ⒭ (the incremental workstream, NOT §2 F ⒭) ⑵′): on every
/// fixture and sample book, the finder must yield EXACTLY the node instances,
/// in exactly the order, that the old red spelling produced —
/// <c>DescendantNodes()</c> filtered by the ancestor guard
/// (<see cref="MeasureCollector.IsInsideProcessedContainer"/> /
/// <see cref="MeasureCollector.IsInsideProcessedContainerExceptParallel"/>,
/// kept internal as this net's reference oracle) and the candidate type test.
/// This is what pins the finder's KIND lists
/// (IsMusicCandidateKind / IsProcessedContainerKind) to the TYPE lists the
/// collector consumes (<see cref="MeasureCollector.IsCollectableMusicNode"/> /
/// <see cref="MeasureCollector.IsProcessedContainer"/>): a kind missing from
/// the candidate list drops its nodes here as a sequence mismatch, a container
/// kind missing lets inner nodes leak in as extras.
/// </summary>
/// <remarks>
/// Reference identity, not value equality: both walks materialize red nodes
/// through the same parent-cached <c>GetChild</c>, so the SAME tree must hand
/// both spellings the same instances. Containers compared are every shape the
/// production call sites walk: the root (root path / ProcessMusicContainer on
/// part blocks and sections) in both modes, and every parallel voice block in
/// per-voice mode (GatherVoiceMusicNodes / CollectMeasuresFromNode).
/// </remarks>
public class MusicSitesEquivalenceTests
{
    [Fact]
    public void MusicSites_MatchesOldRedWalk_OnEveryFixture()
    {
        var failures = new List<string>();
        int books = 0, containers = 0;

        foreach (var path in CollectResumeTests.NetBooks())
        {
            var text = File.ReadAllText(path);
            SyntaxTree tree;
            try
            {
                tree = SyntaxTree.Parse(text);
            }
            catch
            {
                continue; // the net covers books that parse today
            }
            books++;
            string book = Path.GetFileName(path);

            foreach (var (container, label) in ContainersOf(tree.GetRoot()))
            {
                containers++;
                Compare(container, includeParallel: true, $"{book} {label}", failures);
                Compare(container, includeParallel: false, $"{book} {label}", failures);
            }
        }

        Assert.True(failures.Count == 0,
            $"{failures.Count} gather mismatch(es):\n" + string.Join("\n", failures.Take(20)));
        // The net must actually bite.
        Assert.True(books >= 50 && containers >= books,
            $"only {books} books / {containers} containers reached the comparison");
    }

    /// <summary>Every container shape the production gathers walk: the root
    /// itself, every section / part block (ProcessMusicContainer), and every
    /// parallel span's voice blocks (the per-voice flatten walks).</summary>
    private static IEnumerable<(SyntaxNode Container, string Label)> ContainersOf(SyntaxNode root)
    {
        yield return (root, "root");
        foreach (var n in root.DescendantNodes())
        {
            switch (n)
            {
                case SectionDeclarationSyntax:
                case PartBlockSyntax:
                    yield return (n, $"{n.Kind}@{n.Position}");
                    break;
                case ParallelExpressionSyntax par:
                    foreach (var voice in par.Voices)
                        yield return (voice, $"voice@{voice.Position}");
                    break;
            }
        }
    }

    private static void Compare(SyntaxNode container, bool includeParallel, string label,
        List<string> failures)
    {
        var reference = OldRedWalk(container, includeParallel).ToList();
        var finder = MeasureCollector.MusicSites(container, includeParallel).ToList();

        if (reference.Count != finder.Count)
        {
            failures.Add($"{label} (parallel={includeParallel}): " +
                $"count {reference.Count} vs {finder.Count}");
            return;
        }
        for (int i = 0; i < reference.Count; i++)
        {
            if (!ReferenceEquals(reference[i], finder[i]))
            {
                failures.Add($"{label} (parallel={includeParallel}): node {i} differs — " +
                    $"old {reference[i]} vs new {finder[i]}");
                return;
            }
        }
    }

    /// <summary>The old gather spelling, verbatim: every red descendant, the
    /// per-descendant ancestor guard, the candidate type test (collectable
    /// types plus variable references — what the call sites' own type
    /// dispatches act on).</summary>
    private static IEnumerable<SyntaxNode> OldRedWalk(SyntaxNode container, bool includeParallel)
        => container.DescendantNodes().Where(n =>
            !(includeParallel
                ? MeasureCollector.IsInsideProcessedContainer(n)
                : MeasureCollector.IsInsideProcessedContainerExceptParallel(n))
            && (n is VariableReferenceSyntax || MeasureCollector.IsCollectableMusicNode(n)));

    /// <summary>
    /// The lazy production gather (<see cref="MeasureCollector.MusicSitesLazy"/>
    /// — the (green, position) flat list of HANDOFF's retired ▶ ⒭ (the incremental workstream, NOT §2 F ⒭) ⑵′'s latter half) must
    /// agree with the red-yielding <see cref="MeasureCollector.MusicSites"/>
    /// site for site: same count and order, the SAME red instance on
    /// materialization, a red-free <c>Position</c> equal to the node's real
    /// full-span start, and a kind-level collectable filter that answers
    /// exactly as the type-level one. A drifted kind list, a broken position
    /// accumulation, or a mis-linked spine all die here.
    /// </summary>
    [Fact]
    public void MusicSitesLazy_MatchesMaterializedSites_OnEveryFixture()
    {
        var failures = new List<string>();
        int books = 0, containers = 0;

        foreach (var path in CollectResumeTests.NetBooks())
        {
            var text = File.ReadAllText(path);
            SyntaxTree tree;
            try
            {
                tree = SyntaxTree.Parse(text);
            }
            catch
            {
                continue; // the net covers books that parse today
            }
            books++;
            string book = Path.GetFileName(path);

            foreach (var (container, label) in ContainersOf(tree.GetRoot()))
            {
                containers++;
                CompareLazy(container, includeParallel: true, $"{book} {label}", failures);
                CompareLazy(container, includeParallel: false, $"{book} {label}", failures);
            }
        }

        Assert.True(failures.Count == 0,
            $"{failures.Count} lazy-gather mismatch(es):\n" + string.Join("\n", failures.Take(20)));
        Assert.True(books >= 50 && containers >= books,
            $"only {books} books / {containers} containers reached the comparison");
    }

    /// <summary>
    /// The green bar-counting walk (<see cref="MeasureCollector.CountBarsInScope"/>
    /// — session 155: it inherited the whole-book red first-touch when the
    /// gather went lazy) must count exactly what the old red spelling
    /// (<see cref="MeasureCollector.CountBarsInScopeRed"/>, kept as this net's
    /// oracle) counts, on every scope shape GetCanonicalSectionBars walks:
    /// sections, part blocks, and the root.
    /// </summary>
    [Fact]
    public void CanonicalBarsEquivalence_GreenCountMatchesRedCount_OnEveryFixture()
    {
        var failures = new List<string>();
        int books = 0, scopes = 0;

        foreach (var path in CollectResumeTests.NetBooks())
        {
            var text = File.ReadAllText(path);
            SyntaxTree tree;
            try
            {
                tree = SyntaxTree.Parse(text);
            }
            catch
            {
                continue; // the net covers books that parse today
            }
            books++;
            string book = Path.GetFileName(path);

            var root = tree.GetRoot();
            var containers = new List<(SyntaxNode Scope, string Label)> { (root, "root") };
            foreach (var n in root.DescendantNodes())
                if (n is SectionDeclarationSyntax or PartBlockSyntax)
                    containers.Add((n, $"{n.Kind}@{n.Position}"));

            foreach (var (scope, label) in containers)
            {
                scopes++;
                int green = MeasureCollector.CountBarsInScope(scope);
                int red = MeasureCollector.CountBarsInScopeRed(scope);
                if (green != red)
                    failures.Add($"{book} {label}: green {green} vs red {red}");
            }
        }

        Assert.True(failures.Count == 0,
            $"{failures.Count} bar-count mismatch(es):\n" + string.Join("\n", failures.Take(20)));
        Assert.True(books >= 50 && scopes >= books,
            $"only {books} books / {scopes} scopes reached the comparison");
    }

    private static void CompareLazy(SyntaxNode container, bool includeParallel, string label,
        List<string> failures)
    {
        var reference = MeasureCollector.MusicSites(container, includeParallel).ToList();
        var lazy = MeasureCollector.MusicSitesLazy(container, includeParallel).ToList();

        if (reference.Count != lazy.Count)
        {
            failures.Add($"{label} (parallel={includeParallel}): " +
                $"count {reference.Count} vs lazy {lazy.Count}");
            return;
        }
        for (int i = 0; i < reference.Count; i++)
        {
            var site = lazy[i];
            // Address first — Position must be readable without a red and equal
            // the real node's full-span start (checkpoint/splice addressing).
            if (site.Position != reference[i].FullSpan.Start
                || site.Kind != reference[i].Kind)
            {
                failures.Add($"{label} (parallel={includeParallel}): site {i} address/kind — " +
                    $"lazy ({site.Kind}@{site.Position}) vs {reference[i]}");
                return;
            }
            // The kind filter must answer exactly as the type filter the old
            // call sites applied (variable references excluded on both sides).
            bool kindSays = MeasureCollector.IsCollectableMusicKind(site.Kind);
            bool typeSays = reference[i] is not VariableReferenceSyntax
                && MeasureCollector.IsCollectableMusicNode(reference[i]);
            if (kindSays != typeSays)
            {
                failures.Add($"{label} (parallel={includeParallel}): site {i} filter — " +
                    $"kind {kindSays} vs type {typeSays} for {reference[i]}");
                return;
            }
            // Materialization must land on the SAME instance the red walk made
            // (both go through the parent-cached GetChild).
            if (!ReferenceEquals(site.Node, reference[i]))
            {
                failures.Add($"{label} (parallel={includeParallel}): site {i} node differs — " +
                    $"lazy {site.Node} vs {reference[i]}");
                return;
            }
        }
    }
}
