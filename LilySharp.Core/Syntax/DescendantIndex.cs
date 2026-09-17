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

namespace LilySharp.Core.Syntax;

/// <summary>
/// The pre-order of every descendant of a tree's ROOT, walked once and kept on the root
/// (<see cref="CompilationUnitSyntax.Descendants"/>): the flat node list
/// <see cref="SyntaxNode.DescendantNodes()"/> answers from, and the same nodes bucketed by
/// kind so <see cref="SyntaxNode.DescendantNodes{T}()"/> answers a typed question in
/// O(matches) instead of O(tree).
/// </summary>
/// <remarks>
/// <para>
/// WHY (session 401, HANDOFF §2 R13): the diagnostics pass runs after every settled
/// keystroke, and 28 of its validators each began with their own full red walk of the
/// tree to find a handful of nodes — MEASURED on a warm tree (Zz401Probe, min of 5):
/// about 1 ms per walk on perf-plain1k (43,043 nodes) and 4.5–5 ms on perf-fingbeam1k
/// (234,030 nodes), which was 25 ms of that book's 51 ms pass and 108 ms of the other's
/// 292 ms. The red tree is immutable and its reds are parent-cached, so the walk's answer
/// is a pure function of the root: walk once, keep the list, and every later question is a
/// lookup. The red materialization those validators already paid is unchanged; what this
/// adds per tree is one reference per node in the flat list and one in its kind bucket.
/// </para>
/// <para>
/// ⚠️ ROOT ONLY. A subtree walk (<c>section.DescendantNodes()</c>) stays the lazy iterator:
/// there is no place to keep it, and subtree questions are asked once. And a GREEN finder
/// (<see cref="SyntaxNode.KindSites"/>, <see cref="SyntaxNode.GreenSites"/>) is NOT routed
/// through this: the render path's keystroke walk is red-free by design (HANDOFF's
/// incremental workstream), and building this index materializes every red in the book.
/// The index exists for the passes that materialize them anyway.
/// </para>
/// <para>
/// ⚠️ THE TYPED LOOKUP RESTS ON "ONE KIND, ONE RED TYPE". <see cref="SyntaxNode"/>'s
/// <c>CreateRed</c> maps each <see cref="SyntaxKind"/> to exactly one red class, and every
/// red class that is not <see cref="GenericSyntaxNode"/> or <see cref="SyntaxTokenNode"/>
/// is the image of exactly one kind. The bucket a type is answered from is therefore its
/// kind's, in pre-order. The index does not trust that: it records the type each kind's
/// nodes actually had, and a type that turns out to own several kinds — or a type that is
/// not sealed (an <c>is T</c> test could match subclasses) — falls back to scanning the
/// flat list, which is always right. <c>DescendantIndexTests</c> pin both answers against
/// the plain walk over the net books.
/// </para>
/// </remarks>
internal sealed class DescendantIndex
{
    /// <summary>Every descendant of the root, tokens included, in the pre-order
    /// <see cref="SyntaxNode.DescendantNodes()"/> yields.</summary>
    internal readonly SyntaxNode[] Nodes;

    // Per kind: that kind's nodes in pre-order (null = none in this tree), and the red
    // type those nodes had (null = none).
    private readonly SyntaxNode[]?[] _byKind;
    private readonly Type?[] _typeOfKind;

    private static readonly int KindCount = Enum.GetValues<SyntaxKind>().Max(k => (int)k) + 1;

    private DescendantIndex(SyntaxNode[] nodes, SyntaxNode[]?[] byKind, Type?[] typeOfKind)
    {
        Nodes = nodes;
        _byKind = byKind;
        _typeOfKind = typeOfKind;
    }

    /// <summary>Walks <paramref name="root"/>'s subtree once (the plain
    /// <see cref="SyntaxNode.DescendantNodes()"/> order) and buckets what it found.</summary>
    internal static DescendantIndex Build(SyntaxNode root)
    {
        var nodes = new List<SyntaxNode>();
        var counts = new int[KindCount];
        foreach (var node in root.WalkDescendants())
        {
            nodes.Add(node);
            counts[(int)node.Kind]++;
        }

        var byKind = new SyntaxNode[]?[KindCount];
        var fill = new int[KindCount];
        var typeOfKind = new Type?[KindCount];
        for (int k = 0; k < KindCount; k++)
            if (counts[k] > 0)
                byKind[k] = new SyntaxNode[counts[k]];
        foreach (var node in nodes)
        {
            int k = (int)node.Kind;
            byKind[k]![fill[k]++] = node;
        }
        for (int k = 0; k < KindCount; k++)
            if (byKind[k] is { } bucket)
                typeOfKind[k] = bucket[0].GetType();

        return new DescendantIndex(nodes.ToArray(), byKind, typeOfKind);
    }

    /// <summary>The nodes whose red type is exactly <typeparamref name="T"/> — the answer
    /// <c>DescendantNodes().OfType&lt;T&gt;()</c> gives, in the same order — from the
    /// kind bucket when the type owns exactly one kind, else by scanning the flat list.</summary>
    internal IEnumerable<T> OfType<T>() where T : SyntaxNode
    {
        var type = typeof(T);
        if (type.IsSealed)
        {
            SyntaxNode[]? bucket = null;
            int owned = 0;
            for (int k = 0; k < _typeOfKind.Length; k++)
            {
                if (_typeOfKind[k] == type)
                {
                    owned++;
                    bucket = _byKind[k];
                }
            }
            if (owned == 0)
                return [];
            if (owned == 1)
                return Cast<T>(bucket!);
        }
        return Scan<T>();
    }

    private static IEnumerable<T> Cast<T>(SyntaxNode[] bucket) where T : SyntaxNode
    {
        foreach (var node in bucket)
            yield return (T)node;
    }

    private IEnumerable<T> Scan<T>() where T : SyntaxNode
    {
        foreach (var node in Nodes)
            if (node is T typed)
                yield return typed;
    }
}
