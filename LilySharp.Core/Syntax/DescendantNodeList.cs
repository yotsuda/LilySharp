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

using System.Collections;
using System.Collections.Generic;

namespace LilySharp.Core.Syntax;

/// <summary>
/// What <see cref="SyntaxNode.DescendantNodes()"/> answers: the root's kept array, or a
/// subtree's lazy walk, behind one type that <c>foreach</c> can walk without an interface.
/// </summary>
/// <remarks>
/// <para>
/// WHY (session 408, HANDOFF §2 R13⒩): the diagnostics pass runs after every settled
/// keystroke and a dozen of its validators each scan the whole flat list with a type test to
/// reach a handful of nodes. On the root that list IS an array — the index's — but it was
/// handed back as an <see cref="IEnumerable{T}"/>, so every one of perf-fingbeam1k's 234,030
/// elements cost an interface <c>MoveNext</c>: MEASURED at 0.56 ms per scan against 0.40 ms
/// walking the same array directly, about a dozen times per keystroke. A struct with a
/// struct <c>GetEnumerator</c> gives <c>foreach</c> the array walk at every existing call
/// site without changing one of them.
/// </para>
/// <para>
/// ⚠️ IT IS THE SAME SEQUENCE, NOT A NEW ONE: the same nodes in the same pre-order, from the
/// same two sources as before. Nothing here decides WHAT is walked — <see cref="SyntaxNode"/>
/// still does — so no answer can move. It still implements <see cref="IEnumerable{T}"/>, and
/// a LINQ call (<c>.OfType&lt;T&gt;()</c>, <c>.Any()</c>, <c>.Count()</c>) boxes it once, the
/// way the old return already cost one allocation per walk.
/// </para>
/// </remarks>
public readonly struct DescendantNodeList : IEnumerable<SyntaxNode>
{
    private readonly SyntaxNode[]? _array;
    private readonly IEnumerable<SyntaxNode>? _walk;

    internal DescendantNodeList(SyntaxNode[] array) => _array = array;

    internal DescendantNodeList(IEnumerable<SyntaxNode> walk) => _walk = walk;

    /// <summary>The struct enumerator <c>foreach</c> binds to.</summary>
    public Enumerator GetEnumerator() => new(_array, _walk);

    IEnumerator<SyntaxNode> IEnumerable<SyntaxNode>.GetEnumerator()
        => _array is { } a ? ((IEnumerable<SyntaxNode>)a).GetEnumerator() : _walk!.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator()
        => ((IEnumerable<SyntaxNode>)this).GetEnumerator();

    /// <summary>
    /// Walks the array by index, or defers to the subtree walk's own enumerator.
    /// </summary>
    public struct Enumerator
    {
        private readonly SyntaxNode[]? _array;
        private readonly IEnumerator<SyntaxNode>? _inner;
        private int _index;

        internal Enumerator(SyntaxNode[]? array, IEnumerable<SyntaxNode>? walk)
        {
            _array = array;
            _inner = array is null ? walk!.GetEnumerator() : null;
            _index = -1;
            Current = null!;
        }

        /// <summary>The node at the current position.</summary>
        public SyntaxNode Current { get; private set; }

        /// <summary>Advances to the next node, false at the end.</summary>
        public bool MoveNext()
        {
            if (_array is { } array)
            {
                int next = _index + 1;
                if (next >= array.Length)
                    return false;
                _index = next;
                Current = array[next];
                return true;
            }

            if (!_inner!.MoveNext())
                return false;
            Current = _inner.Current;
            return true;
        }
    }
}
