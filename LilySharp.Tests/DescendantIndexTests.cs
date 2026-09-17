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

using System.Reflection;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The root's descendant index (<see cref="DescendantIndex"/>) against the plain walk it
/// replaces: the flat list is the walk, the typed lookup is <c>OfType&lt;T&gt;()</c> over
/// it for every red type, and the "one kind, one red type" fact the kind buckets rest on
/// holds over the net books.
/// </summary>
public sealed class DescendantIndexTests
{
    private static IEnumerable<(string Path, CompilationUnitSyntax Root)> Roots()
    {
        foreach (var path in CollectResumeTests.NetBooks())
            yield return (path, SyntaxTree.Parse(File.ReadAllText(path)).GetRoot());
    }

    /// <summary>Every red type in Core: the sealed syntax classes plus the abstract base
    /// (which takes the scan path) — the whole set a typed lookup can be asked for.</summary>
    private static IEnumerable<Type> RedTypes()
        => typeof(SyntaxNode).Assembly.GetTypes()
            .Where(t => typeof(SyntaxNode).IsAssignableFrom(t))
            .OrderBy(t => t.FullName, StringComparer.Ordinal);

    private static readonly MethodInfo TypedLookup = typeof(SyntaxNode).GetMethods()
        .Single(m => m.Name == nameof(SyntaxNode.DescendantNodes) && m.IsGenericMethodDefinition);

    [Fact]
    public void TheRootsFlatList_IsTheWalk_AndIsBuiltOnce_OnEveryNetBook()
    {
        int books = 0, nodes = 0;
        foreach (var (path, root) in Roots())
        {
            var walk = root.WalkDescendants().ToList();
            var listed = root.DescendantNodes().ToList();
            Assert.True(walk.Count == listed.Count, $"{path}: walk {walk.Count} nodes, index {listed.Count}");
            for (int i = 0; i < walk.Count; i++)
                Assert.Same(walk[i], listed[i]);
            // Built once: a second ask hands back the very same list.
            Assert.Same(root.DescendantNodes(), root.DescendantNodes());
            books++;
            nodes += walk.Count;
        }
        Assert.True(books >= 200, $"only {books} books");
        Assert.True(nodes >= 30_000, $"only {nodes} nodes"); // 46,935 when written
    }

    [Fact]
    public void TheTypedLookup_IsOfTypeOverTheWalk_ForEveryRedType_OnEveryNetBook()
    {
        var typesAnswered = new HashSet<Type>();
        foreach (var (path, root) in Roots())
        {
            var walk = root.WalkDescendants().ToList();
            foreach (var type in RedTypes())
            {
                var expected = walk.Where(type.IsInstanceOfType).ToList();
                var answered = ((System.Collections.IEnumerable)TypedLookup.MakeGenericMethod(type)
                    .Invoke(root, null)!).Cast<SyntaxNode>().ToList();
                Assert.True(expected.Count == answered.Count,
                    $"{path}: {type.Name} — walk {expected.Count}, index {answered.Count}");
                for (int i = 0; i < expected.Count; i++)
                    Assert.Same(expected[i], answered[i]);
                if (expected.Count > 0)
                    typesAnswered.Add(type);
            }
        }
        // Not vacuous: the net actually exercises most of the red types (a bucket answer and
        // the scan answer for the abstract base and the token/generic wrappers).
        Assert.True(typesAnswered.Count >= 50, $"only {typesAnswered.Count} red types occur in the net");
        Assert.Contains(typeof(SyntaxNode), typesAnswered);
        Assert.Contains(typeof(SyntaxTokenNode), typesAnswered);
        Assert.Contains(typeof(GenericSyntaxNode), typesAnswered);
    }

    [Fact]
    public void OneKindHasOneRedType_AndOneSealedTypeHasOneKind_OnEveryNetBook()
    {
        // The bucket a type is answered from is its kind's, so the two maps must be
        // functions in both directions for every sealed class but the two wrappers
        // (SyntaxTokenNode wraps every token kind, GenericSyntaxNode every kind CreateRed
        // does not name). A violation would not be wrong — the index falls back to the
        // scan — but it would silently turn an O(matches) lookup into an O(tree) one.
        var typeOfKind = new Dictionary<SyntaxKind, Type>();
        var kindsOfType = new Dictionary<Type, HashSet<SyntaxKind>>();
        foreach (var (path, root) in Roots())
        {
            foreach (var node in root.DescendantNodes())
            {
                var type = node.GetType();
                if (typeOfKind.TryGetValue(node.Kind, out var seen))
                    Assert.True(seen == type, $"{path}: kind {node.Kind} is {seen.Name} and {type.Name}");
                else
                    typeOfKind[node.Kind] = type;
                if (!kindsOfType.TryGetValue(type, out var kinds))
                    kindsOfType[type] = kinds = new();
                kinds.Add(node.Kind);
            }
        }
        foreach (var (type, kinds) in kindsOfType)
        {
            if (type == typeof(SyntaxTokenNode) || type == typeof(GenericSyntaxNode))
                continue;
            Assert.True(type.IsSealed, $"{type.Name} is not sealed");
            Assert.True(kinds.Count == 1, $"{type.Name} owns {kinds.Count} kinds: {string.Join(", ", kinds)}");
        }
        Assert.True(kindsOfType.Count >= 50, $"only {kindsOfType.Count} red types occur in the net");
    }

    [Fact]
    public void BelowTheRoot_TheLookupIsStillTheWalk()
    {
        // A subtree question is not indexed (nothing keeps it); it must still answer.
        var tree = SyntaxTree.Parse(
            "part v { clef treble }\nsection A { v { c4 d e f | } }\nsection B { v { g4 a b c' | } }\nscore { staff v }\n");
        var root = tree.GetRoot();
        var sections = root.DescendantNodes<SectionDeclarationSyntax>().ToList();
        Assert.Equal(2, sections.Count);
        foreach (var section in sections)
        {
            var walked = section.WalkDescendants().OfType<NoteSyntax>().ToList();
            var typed = section.DescendantNodes<NoteSyntax>().ToList();
            Assert.Equal(4, walked.Count);
            Assert.Equal(walked, typed);
            Assert.Equal(section.WalkDescendants().ToList(), section.DescendantNodes().ToList());
        }
        // And the root's typed answer for a type the book does not write is empty, not a scan
        // that finds nothing by luck: a sealed type with no bucket answers at once.
        Assert.Empty(root.DescendantNodes<GraceExpressionSyntax>());
    }
}
