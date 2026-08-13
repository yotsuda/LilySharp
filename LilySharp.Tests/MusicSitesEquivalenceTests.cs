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
/// (<see cref="MeasureCollector.MusicSites"/> — HANDOFF ▶ ⒭ ⑵′): on every
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
}
