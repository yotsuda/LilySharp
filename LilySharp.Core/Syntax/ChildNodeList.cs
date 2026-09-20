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
using System.Collections;
using System.Collections.Generic;

namespace LilySharp.Core.Syntax;

/// <summary>Which child kinds a <see cref="ChildNodeList"/> hands out.</summary>
/// <remarks>
/// ⚠️ A TYPE FILTER IS A PLACE THINGS FALL THROUGH — the warning the accessors carry
/// (<see cref="PitchSyntax.Articulations"/>, <see cref="RestSyntax.Articulations"/>) is about
/// these lists, and it did not move when the walk did. A kind that is parsed into a slot but
/// named by no filter here is held by the tree and handed to nobody.
/// </remarks>
internal enum ChildNodeFilter : byte
{
    /// <summary>Every child that is present. The slot range alone decides.</summary>
    Any,

    /// <summary>A chord member's post-events: scripts, dynamics, marks, string numbers.</summary>
    PitchPostEvents,

    /// <summary>A rest's post-events: scripts, dynamics, marks, and the spanner markers.</summary>
    RestPostEvents,

    /// <summary>Both of the above at once — what a chord and a <c>&lt;&lt; &gt;&gt;</c> group carry.</summary>
    FullPostEvents,
}

/// <summary>
/// A lazy walk over a node's child slots, from a start slot, keeping the kinds one accessor
/// hands out. Walking it with <c>foreach</c> allocates nothing.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️⚠️ THIS TYPE EXISTS IN ORDER NOT TO BE ALLOCATED, and that is the only reason it is a
/// struct. Every accessor that returns one used to be a <c>yield return</c> property or
/// method, and a C# iterator builds its state machine on the CALL, not on the first
/// <c>MoveNext</c> — so <c>HasNamedArticulation(node, "cross")</c> paid for a 48-byte object
/// in order to look at a note that carries no articulations at all.
/// </para>
/// <para>
/// MEASURED (2026-09-20, session 445; the reader's corpus, 231 books x 8 forward keystrokes,
/// Release): the <c>Articulations</c> accessors alone cost <b>453,933 B/keystroke = 9.02% of
/// the keystroke</b>, over 3,223 + 599 + 537x9 reads a keystroke, and <c>ChildNodes</c> added
/// its own. The state machine was 48 bytes whether the note carried nine post-events or none.
/// </para>
/// <para>
/// ⚠️ Keep <see cref="GetEnumerator"/> non-virtual and keep this a struct: <c>foreach</c>
/// binds to the PATTERN before it looks at <see cref="IEnumerable{T}"/>, and that binding is
/// the whole point. Handing one of these out AS an <see cref="IEnumerable{T}"/> — LINQ, or a
/// parameter declared as the interface — boxes it, which costs one object, the same one
/// object the iterator used to cost. So the interface path is not a regression; it is simply
/// where the saving stops.
/// </para>
/// </remarks>
public readonly struct ChildNodeList : IEnumerable<SyntaxNode>
{
    private readonly SyntaxNode? _owner;
    private readonly int _start;
    private readonly ChildNodeFilter _filter;

    internal ChildNodeList(SyntaxNode owner, int start, ChildNodeFilter filter)
    {
        _owner = owner;
        _start = start;
        _filter = filter;
    }

    /// <summary>The walk. <c>foreach</c> binds here, and allocates nothing.</summary>
    /// <remarks>
    /// The default value of this type is the EMPTY walk — which is what a
    /// <c>node switch</c> whose last arm matches nothing wants, and it is free where
    /// <c>Enumerable.Empty&lt;SyntaxNode&gt;()</c> would have forced the whole switch back to
    /// the interface.
    /// </remarks>
    public Enumerator GetEnumerator() => new(_owner, _start, _filter);

    IEnumerator<SyntaxNode> IEnumerable<SyntaxNode>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    internal static bool Accepts(SyntaxNode child, ChildNodeFilter filter) => filter switch
    {
        ChildNodeFilter.Any => true,
        ChildNodeFilter.PitchPostEvents =>
            child is ArticulationSyntax or DynamicSyntax or MusicMarkSyntax
                or StringNumberAnnotationSyntax,
        ChildNodeFilter.RestPostEvents =>
            child is ArticulationSyntax or DynamicSyntax or MusicMarkSyntax
                or TieSyntax or SlurSyntax or BeamMarkerSyntax,
        _ =>
            child is ArticulationSyntax or DynamicSyntax or MusicMarkSyntax
                or StringNumberAnnotationSyntax
                or TieSyntax or SlurSyntax or BeamMarkerSyntax,
    };

    /// <summary>Walks the slots, skipping the absent ones and the kinds the filter excludes.</summary>
    public struct Enumerator : IEnumerator<SyntaxNode>
    {
        private readonly SyntaxNode? _owner;
        private readonly int _start;
        private readonly ChildNodeFilter _filter;
        private int _slot;
        private SyntaxNode? _current;

        internal Enumerator(SyntaxNode? owner, int start, ChildNodeFilter filter)
        {
            _owner = owner;
            _start = start;
            _filter = filter;
            _slot = start;
            _current = null;
        }

        /// <summary>The child the walk is standing on.</summary>
        public readonly SyntaxNode Current => _current!;

        readonly object IEnumerator.Current => _current!;

        /// <summary>Advances to the next child the filter keeps.</summary>
        /// <returns><c>false</c> once the slots run out.</returns>
        public bool MoveNext()
        {
            if (_owner is null)
                return false;
            int count = _owner.SlotCount;
            while (_slot < count)
            {
                var child = _owner.GetChild(_slot++);
                if (child is not null && Accepts(child, _filter))
                {
                    _current = child;
                    return true;
                }
            }
            _current = null;
            return false;
        }

        /// <summary>Returns the walk to the start slot.</summary>
        public void Reset()
        {
            _slot = _start;
            _current = null;
        }

        /// <summary>Nothing is held, so nothing is released.</summary>
        public readonly void Dispose()
        {
        }
    }
}

/// <summary>
/// A node's direct children of ONE kind — the walk the top-level lookups make.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️⚠️ THIS TYPE EXISTS IN ORDER NOT TO BE ALLOCATED, for the same reason
/// <see cref="ChildNodeList"/> does. <c>ChildNodes().OfType&lt;T&gt;()</c> reads exactly
/// these children, but handing a struct walk to LINQ BOXES it and then builds a filter
/// iterator on top of the box — two objects per ask, on a walk whose whole point is to
/// allocate nothing.
/// </para>
/// <para>
/// MEASURED (2026-09-20, session 446; the reader's corpus, 231 books x 8 forward
/// keystrokes, Release): the part-declaration lookups that read the root this way asked
/// 4.96 + 4.96 + 3.77 + 3.73 + 2.48 + 1.84 + 1.84 times a keystroke, 1,793 B/keystroke
/// between them — for a root whose children a book counts on one hand.
/// </para>
/// </remarks>
/// <typeparam name="T">The child kind to keep.</typeparam>
public readonly struct TypedChildNodeList<T> : IEnumerable<T> where T : SyntaxNode
{
    private readonly SyntaxNode? _owner;

    internal TypedChildNodeList(SyntaxNode owner) => _owner = owner;

    /// <summary>The walk. <c>foreach</c> binds here, and allocates nothing.</summary>
    public Enumerator GetEnumerator() => new(_owner);

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Walks the children, keeping the ones of type <typeparamref name="T"/>.</summary>
    public struct Enumerator : IEnumerator<T>
    {
        private ChildNodeList.Enumerator _children;
        private T? _current;

        internal Enumerator(SyntaxNode? owner)
        {
            _children = new ChildNodeList.Enumerator(owner, 0, ChildNodeFilter.Any);
            _current = null;
        }

        /// <summary>The child the walk is standing on.</summary>
        public readonly T Current => _current!;

        readonly object IEnumerator.Current => _current!;

        /// <summary>Advances to the next child of the kind.</summary>
        /// <returns><c>false</c> once the children run out.</returns>
        public bool MoveNext()
        {
            while (_children.MoveNext())
            {
                if (_children.Current is T t)
                {
                    _current = t;
                    return true;
                }
            }
            _current = null;
            return false;
        }

        /// <summary>Returns the walk to the first child.</summary>
        public void Reset()
        {
            _children.Reset();
            _current = null;
        }

        /// <summary>Nothing is held, so nothing is released.</summary>
        public readonly void Dispose()
        {
        }
    }
}
