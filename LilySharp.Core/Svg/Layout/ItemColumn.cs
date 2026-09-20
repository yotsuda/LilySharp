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
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// The items one spacing column holds at a moment: none, exactly one, or a voice list.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️⚠️ THIS TYPE EXISTS IN ORDER NOT TO BE ALLOCATED, and that is the only reason the
/// spacing rules do not simply take <c>IEnumerable&lt;MusicItem&gt;</c> any more. The
/// callers arrive in two shapes — the multi-voice ones hold a <c>List&lt;MusicItem&gt;</c>
/// built by <c>MeasureLayouter</c>, and the single-voice ones have one item in hand — and
/// the old signature charged BOTH for the shape: the list was walked through an interface,
/// which boxes its enumerator on every call, and the single item was wrapped in a
/// <c>yield return</c> one-item sequence, which builds a state machine on the CALL.
/// </para>
/// <para>
/// MEASURED (2026-09-20, session 446; the reader's corpus, 231 books x 8 forward
/// keystrokes, Release): the boxed walks alone were 3,024 + 1,793 + 1,793 B/keystroke at
/// the three <c>foreach</c> sites, and the wrapper's own 48 bytes a call were invisible to
/// that instrument because they are spent at the CALLER's line, not at the walk's.
/// </para>
/// <para>
/// An ABSENT column and an EMPTY one are the same value here (<c>default</c>), which the
/// readers already agreed on: the one caller that passed null did so for a column with no
/// items, and every reader answers a mask of 0 / a width of 0 for both.
/// </para>
/// <para>
/// Both arms are observed. Session 446 poisoned the single-item one — the column of one item
/// reporting itself empty — and four tests went red; the walk ORDER, poisoned separately in
/// <c>ApplyLeftHeadWidth</c>, is not observed and need not be (a max and a mean are
/// order-blind, which is what makes the indexed rewrite safe — RULES §5.4).
/// </para>
/// </remarks>
internal readonly struct ItemColumn
{
    private readonly MusicItem? _one;
    private readonly IReadOnlyList<MusicItem>? _many;

    /// <summary>The column of a single item — the single-voice callers' shape.</summary>
    internal ItemColumn(MusicItem item)
    {
        _one = item;
        _many = null;
    }

    /// <summary>The column a voice list holds; a null list is the empty column.</summary>
    /// <remarks>
    /// The list is held through the INTERFACE on purpose: the callers hold two different
    /// concrete lists, and reading one by index through an interface allocates nothing —
    /// it is <c>foreach</c> over an interface, not indexing it, that boxes an enumerator.
    /// </remarks>
    internal ItemColumn(IReadOnlyList<MusicItem>? items)
    {
        _one = null;
        _many = items;
    }

    /// <summary>A voice list IS a column, so those callers keep reading as they did.</summary>
    /// <remarks>
    /// C# forbids a user-defined conversion from an INTERFACE, so the callers that hold
    /// their column as <see cref="IReadOnlyList{T}"/> name the constructor instead.
    /// </remarks>
    public static implicit operator ItemColumn(List<MusicItem>? items) => new(items);

    /// <summary>How many items the column holds.</summary>
    public int Count => _many?.Count ?? (_one is null ? 0 : 1);

    /// <summary>The i-th item, in the order the caller built them.</summary>
    public MusicItem this[int i] => _many is null ? _one! : _many[i];
}
