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

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// A bucket that holds its FIRST item in the struct itself and only builds a list from the
/// second one on — the shape of a dictionary value whose bucket almost always receives one
/// thing.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️⚠️ LIKE <see cref="ItemColumn"/>, THIS TYPE EXISTS IN ORDER NOT TO BE ALLOCATED. The
/// dictionary that holds it pays two words a bucket instead of one, and that is the whole
/// price; the <c>List&lt;T&gt;</c> object and its first array — 32 + 56 bytes for a bucket
/// of one reference — are not paid at all.
/// </para>
/// <para>
/// MEASURED (2026-09-21, session 455; the reader's corpus, 231 books x 8 forward keystrokes,
/// Release): <c>ArticulationEngraver</c>'s slur buckets finish holding EXACTLY ONE item on
/// 85.3% of 10.88 builds a keystroke, so 817 of the site's 962 B/keystroke sits on buckets
/// of one. The dictionary's own entry grows 8 bytes a bucket against that (+279 B/keystroke,
/// measured at :422 in the same run) — which is why this shape is only right where the
/// one-item share is high. It was NOT right at the ledger-line spanner's entry map (one
/// 27.1%; that engraver was deleted in session 523), where the entry growth cost more than
/// the buckets saved: session 455's prediction.txt has that arithmetic.
/// </para>
/// <para>
/// ⚠️ <c>Add</c> mutates, so the value must be reached by reference —
/// <c>CollectionsMarshal.GetValueRefOrAddDefault</c> — never by <c>TryGetValue</c>, which
/// hands back a COPY and would drop every item after the first.
/// </para>
/// </remarks>
/// <typeparam name="T">A reference type: the single slot is a null-or-item field, so a value
/// type would need a separate occupancy flag and a third word.</typeparam>
internal struct OneOrMany<T> where T : class
{
    private T? _one;
    private List<T>? _rest;

    /// <summary>How many items the bucket holds.</summary>
    public readonly int Count => _one is null ? 0 : 1 + (_rest?.Count ?? 0);

    /// <summary>The i-th item, in the order it was added.</summary>
    public readonly T this[int i] => i == 0 ? _one! : _rest![i - 1];

    /// <summary>Adds one item, building the overflow list only on the second.</summary>
    public void Add(T item)
    {
        if (_one is null)
            _one = item;
        else
            (_rest ??= []).Add(item);
    }
}
