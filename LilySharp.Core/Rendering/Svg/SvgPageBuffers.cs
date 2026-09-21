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

using System.Text;

namespace LilySharp.Core.Rendering.Svg;

/// <summary>
/// One render session's page body buffers, kept across renders: slot <c>i</c> holds the
/// <see cref="StringBuilder"/> page <c>i</c> was drawn into last time, already cleared and
/// already the right size. <see cref="SvgDocumentContext.BeginPage"/> takes from here and
/// <see cref="SvgDocumentContext.ReleaseBuffers"/> gives back — after the document has been
/// materialized, never before.
/// </summary>
/// <remarks>
/// THE POINT IS THE ARRAY, NOT THE OBJECT. A page body is 76,047 chars on the mean book of
/// the corpus (max 164,217), and a builder grown from empty allocates a CHAIN of chunks —
/// 16, 16, 32, … 8000, 8000 — which it never lets go of. MEASURED (session 455 census,
/// 231 books × 8 keystrokes): 506,766 B per keystroke at that one <c>new StringBuilder()</c>,
/// 12.1% of the render half, of which the slack the census calls waste is only 27,355.
/// Handing the same buffer back is what removes the other 479,411 — and it removes it
/// exactly, to zero:
/// <list type="bullet">
/// <item><c>Clear()</c> on a builder that outgrew its first chunk walks to the head of the
/// chain and consolidates it into ONE array of the whole capacity. That array is allocated
/// once, here, at the render that grew it.</item>
/// <item><c>Clear()</c> on a single-chunk builder is an early return that allocates nothing,
/// and appending back into it allocates nothing while it fits. MEASURED (.NET 10.0.12,
/// GC.GetAllocatedBytesForCurrentThread, 76,000 chars): a fresh builder costs 152,040 B more
/// than a cleared one, and the second, third and fourth reuse cost 0.</item>
/// </list>
/// ⚠️ SO THE CLEAR IS THE PRICE OF THE REUSE, AND IT IS PAID AT <see cref="Give"/>, WHERE THE
/// RENDER THAT GREW THE BUFFER IS THE ONE CHARGED FOR IT. It is also the only place anything
/// clears a page buffer: <see cref="Take"/> deliberately does NOT clear defensively, so that
/// a buffer parked dirty shows up as a page with the previous render's text in front of it —
/// loud, in every SVG net there is — instead of being quietly swept up here.
/// <para>
/// WHY THE SESSION AND NOT A STATIC: one <see cref="SvgPageBuffers"/> per
/// <see cref="LilySharp.Core.Svg.IncrementalCompiler"/>, i.e. per open document, because that is what the
/// slot index means — "page 3 OF THIS DOCUMENT was this big last keystroke". RULES §5.3-128:
/// the choice between an instance field and one buffer per thread is decided by calls ÷
/// distinct instances, and that is 3.15 pages per document per keystroke, not a global 3.15.
/// </para>
/// <para>
/// ⚠️ A TAKEN SLOT IS AN EMPTY SLOT. Two renders of one session overlapping — a preview
/// render abandoned mid-flight while the next keystroke's starts — must not be handed the
/// same buffer, so <see cref="Take"/> removes it. The loser of such a race allocates a fresh
/// builder and the pool simply misses; nothing is shared and nothing is corrupted. Same for
/// a render that throws: its buffers are never given back, and the next render starts from
/// empty slots. The pool is an optimization with no semantics, which is what the
/// "pool = null" poison pins.
/// </para>
/// </remarks>
internal sealed class SvgPageBuffers
{
    /// <summary>Slot i = page i's buffer, or null when nothing is parked (never taken, or
    /// taken and not yet given back).</summary>
    private readonly List<StringBuilder?> _slots = new();

    /// <summary>Below this much free room a parked buffer is replaced by a roomier one
    /// rather than kept — see <see cref="Give"/>.</summary>
    private const int MinSlack = 1024;

    /// <summary>How the page buffers of this session have been paid for over its lifetime:
    /// pages served from a parked buffer, pages that had to build one, and pages re-parked
    /// into a roomier buffer. For diagnostics / tests — the liveness half of the pool's net
    /// (a pool that never serves is indistinguishable from no pool at all by output).</summary>
    internal (int Served, int Fresh, int Reparked) Stats { get; private set; }

    /// <summary>The buffer for page <paramref name="pageIndex"/>: the one that page used
    /// last render (cleared, and sized by what it then held), or a new one.</summary>
    internal StringBuilder Take(int pageIndex)
    {
        if (pageIndex >= 0 && pageIndex < _slots.Count && _slots[pageIndex] is { } parked)
        {
            _slots[pageIndex] = null;
            Stats = (Stats.Served + 1, Stats.Fresh, Stats.Reparked);
            return parked;
        }
        Stats = (Stats.Served, Stats.Fresh + 1, Stats.Reparked);
        return new StringBuilder();
    }

    /// <summary>Parks <paramref name="buffer"/> for the next render of page
    /// <paramref name="pageIndex"/>, emptied. The caller must be done reading it.</summary>
    internal void Give(int pageIndex, StringBuilder buffer)
    {
        if (pageIndex < 0)
            return;
        while (_slots.Count <= pageIndex)
            _slots.Add(null);

        int used = buffer.Length;
        // Capacity is never below Length here, so "it overflowed" cannot be read off these
        // two — what CAN be read off them is whether the next render has room, which is the
        // question that matters. Thin room ⇒ park a roomier builder instead (ONE array, and
        // the chain, if the page did outgrow its chunk, is dropped with it); otherwise clear
        // and keep, which for a single-chunk builder allocates nothing at all.
        // ⚠️ THE HEADROOM IS WHAT MAKES THE STEADY STATE FREE. Parking at exactly `used`
        // would re-allocate on any growth whatsoever — a one-character page is enough — so
        // the fixed slack is what a keystroke edits within, and the eighth is what a page
        // grows within before it is worth re-parking.
        if (buffer.Capacity - used < MinSlack)
        {
            _slots[pageIndex] = new StringBuilder(used + used / 8 + MinSlack);
            Stats = (Stats.Served, Stats.Fresh, Stats.Reparked + 1);
            return;
        }
        buffer.Clear();
        _slots[pageIndex] = buffer;
    }
}
