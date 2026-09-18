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

using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// <see cref="MeasureValidator.WorkKinds"/> against the walk it replaced. Until session 408
/// the bar checker found its work by recursing through the whole book and pruning the
/// containers whose bodies are not bar streams; it now asks the tree's descendant index for
/// the kinds its switch acts on and asks the prunes of a candidate's ANCESTORS. The two are
/// only the same while the kind list mirrors the switch, so the old recursion is kept HERE,
/// as this net's reference — a second spelling on purpose (RULES §7.7), living in the test
/// rather than in the product.
/// </summary>
/// <remarks>
/// ⚠️ The reference below must name the TYPES its switch names, never
/// <see cref="MeasureValidator.WorkKinds"/> — a reference that asked the same list would
/// agree with it for free and this net would pin nothing.
/// </remarks>
public sealed class MeasureValidatorWalkTests
{
    /// <summary>The walk as it stood before session 408, collecting what it would act on.</summary>
    private static void Reference(SyntaxNode node, List<SyntaxNode> worked)
    {
        if (MeasureValidator.IsPrunedContainer(node))
            return;
        if (node is MusicBlockSyntax or SectionDeclarationSyntax
            or TimeSignatureSyntax or PartialDeclarationSyntax)
            worked.Add(node);
        for (int i = 0; i < node.SlotCount; i++)
        {
            var child = node.GetChild(i);
            if (child != null && child is not SyntaxTokenNode)
                Reference(child, worked);
        }
    }

    private static List<SyntaxNode> Listed(CompilationUnitSyntax root)
    {
        var listed = new List<SyntaxNode>();
        foreach (var node in root.DescendantNodesOfKinds(MeasureValidator.WorkKinds))
        {
            bool pruned = false;
            for (var p = node.Parent; p != null && !pruned; p = p.Parent)
                pruned = MeasureValidator.IsPrunedContainer(p);
            if (!pruned)
                listed.Add(node);
        }
        return listed;
    }

    [Fact]
    public void TheKindWalk_ActsOnExactlyWhatTheRecursionDid_OnEveryNetBook()
    {
        int books = 0, worked = 0;
        foreach (var path in CollectResumeTests.NetBooks())
        {
            var root = SyntaxTree.Parse(File.ReadAllText(path)).GetRoot();
            var reference = new List<SyntaxNode>();
            Reference(root, reference);
            var listed = Listed(root);

            Assert.True(reference.Count == listed.Count,
                $"{Path.GetFileName(path)}: the recursion acts on {reference.Count} nodes, the kind walk on {listed.Count}");
            for (int i = 0; i < reference.Count; i++)
                Assert.Same(reference[i], listed[i]);   // same nodes, same document order
            books++;
            worked += reference.Count;
        }
        Assert.True(books >= 200, $"only {books} books");
        Assert.True(worked >= 800, $"only {worked} worked-at nodes"); // 1,093 when written
    }

    /// <summary>
    /// Each pruned container, with music inside it that WOULD be reported as a short bar if
    /// the walk entered it — which is what every entry of the prune set was reported for.
    /// A book here that starts warning means the prune stopped being asked.
    /// </summary>
    [Theory]
    [InlineData("melody { c8 c c c c c tuplet 3/2 { c8 c c } | }")]        // TupletExpression
    [InlineData("melody { c8 c c c c c c grace { c16 } c8 | }")]           // GraceExpression
    [InlineData("melody { c8 c c c c c c cue treble { c8 } | }")]          // CueExpression
    [InlineData("melody { repeat percent 2 { c8 c c c c c c c } }")]       // RepeatExpression
    [InlineData("melody { voice { c2 c2 } voice { e2 e2 } }")]             // ParallelExpression
    [InlineData("melody { c2 voice { d2 } voice { e2 } }")]                // … mid-bar
    [InlineData("chords prog { section A { | C | } }")]                    // ChordPartBlock
    public void MusicInsideAPrunedContainer_IsNotValidatedAsItsOwnBar(string book)
    {
        // ⚠️ Each bar here is EXACTLY full counting the container's own music, so anything
        // these report is the walk having entered a body it must not. (The first cut of this
        // net wrote bars that were genuinely overfull and failed on its own books.)
        var validator = new MeasureValidator();
        validator.Validate(SyntaxTree.Parse(book));
        Assert.DoesNotContain(validator.Diagnostics,
            d => d.Code is DiagnosticCodes.MeasureIncomplete or DiagnosticCodes.MeasureOverflow);
    }

    /// <summary>
    /// … and the checker still speaks where it should: a genuinely short bar OUTSIDE any
    /// pruned container is still reported, so the test above is not passing by silence.
    /// </summary>
    [Fact]
    public void AShortBarOutsideEveryPrunedContainer_IsStillReported()
    {
        var validator = new MeasureValidator();
        // A LATER bar, not the first: a short first bar is the pickup hint (LYS2006) instead.
        validator.Validate(SyntaxTree.Parse("melody { c8 c c c c c c c | c8 c c | }"));
        Assert.Contains(validator.Diagnostics, d => d.Code == DiagnosticCodes.MeasureIncomplete);
    }
}
