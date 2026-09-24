// Lily# - Music notation compiler
// Copyright (C) 2025-2026 Yoshifumi Tsuda
//
// Parts of this file are ported from LilyPond, the GNU music typesetter.
// The C# is a modified translation of the following, not a copy of it:
//   lily/dot-configuration.cc
//     Copyright (C) 1997--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>
//   lily/dot-column.cc
//     Copyright (C) 1997--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>
// LilyPond is free software under the GNU General Public License version 3 or
// later; its notices are kept here as that licence requires. The full list is in
// LILYPOND-ATTRIBUTION.md. Lily# is an independent project, not affiliated with
// or endorsed by the LilyPond project.
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

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Augmentation-dot placement for chords: a faithful port of LilyPond's
/// Dot_configuration. Dots are inserted in ascending note order; a dot whose
/// slot is taken (or that lands ON a staff line) displaces itself or its
/// neighbours up/down, choosing whichever direction scores the lower
/// badness (squared displacement, with a bias towards moving UP).
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/dot-configuration.cc — badness(), shifted(),
/// remove_collision().
/// LILYPOND-REF: lily/dot-column.cc:194-224 — insertion order and the second
/// remove_collision call for on-line dots.
/// </remarks>
internal static class DotConfiguration
{
    /// <summary>
    /// One dot: where it is now, where it started, which input it came from, and which way it
    /// would rather move. LP keeps this in a <c>map&lt;int, Dot_position&gt;</c> keyed by the
    /// current position; here the key rides along in <see cref="Pos"/> and a configuration is
    /// a span of these kept sorted by it, which is the same thing said in a way that fits on
    /// the stack.
    /// </summary>
    private struct Slot
    {
        public Slot(int pos, int originalPos, int inputIndex, int dir)
        {
            Pos = pos;
            OriginalPos = originalPos;
            InputIndex = inputIndex;
            Dir = dir;
        }

        public int Pos;
        public int OriginalPos;
        public int InputIndex;
        /// <summary>Preferred displacement direction (Dots.direction); 0 = none.</summary>
        public int Dir;
    }

    /// <summary>
    /// How many dots a chord may have before its scratch comes off the heap instead of the
    /// stack. MEASURED (session 427, the owner's corpus, 231 books × eight forward
    /// keystrokes): of 45,320 calls, 45,248 were handed ONE position, two were handed two,
    /// seventy were handed three or four, and NONE was handed five or more. Eight is that
    /// measurement with room over it, and the path above it is the same code — see Resolve.
    /// </summary>
    private const int StackSlots = 8;

    /// <summary>
    /// LILYPOND-REF: lily/dot-configuration.cc:25-44 badness() —
    /// 2·(p−orig)², +2 when moved against the preferred direction,
    /// else +1 when the move direction is not UP.
    /// </summary>
    private static int Badness(ReadOnlySpan<Slot> cfg)
    {
        int total = 0;
        foreach (ref readonly var ent in cfg)
        {
            int demerit = 2 * (ent.Pos - ent.OriginalPos) * (ent.Pos - ent.OriginalPos);
            int moveDir = Math.Sign(ent.Pos - ent.OriginalPos);
            if (ent.Dir != 0 && moveDir != ent.Dir)
                demerit += 2;
            else if (moveDir != 1)
                demerit += 1;
            total += demerit;
        }
        return total;
    }

    /// <summary>
    /// Puts <paramref name="slot"/> at its position, replacing whatever was there — the
    /// <c>cfg[pos] = ent</c> of the map this replaces, including the part where a write onto
    /// an occupied position DROPS the entry that was there. The span stays sorted by position.
    /// </summary>
    private static void SetAt(Span<Slot> cfg, ref int count, in Slot slot)
    {
        int at = 0;
        while (at < count && cfg[at].Pos < slot.Pos)
            at++;
        if (at < count && cfg[at].Pos == slot.Pos)
        {
            cfg[at] = slot;
            return;
        }
        for (int i = count; i > at; i--)
            cfg[i] = cfg[i - 1];
        cfg[at] = slot;
        count++;
    }

    private static bool Contains(ReadOnlySpan<Slot> cfg, int count, int pos)
    {
        for (int i = 0; i < count; i++)
            if (cfg[i].Pos == pos)
                return true;
        return false;
    }

    /// <summary>
    /// LILYPOND-REF: lily/dot-configuration.cc:55-101 shifted() — move the
    /// entry at K one step (line dots) or two (space dots) in direction D;
    /// following entries (in D's iteration order) cascade by 2·D while their
    /// slots collide, and stop cascading at the first free slot.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE ITERATION ORDER IS PART OF THE ANSWER, which is why this walks the source
    /// ascending for D &gt; 0 and descending for D &lt; 0 rather than sorting afterwards: the
    /// cascade stops at "the first slot the new configuration does not already hold", and what
    /// it holds depends on how far the walk has got. The destination is written in that walk
    /// order and sorted at the end; the map this replaces got the sorting for free and the
    /// order from its own iterator.
    /// </remarks>
    private static int Shifted(
        ReadOnlySpan<Slot> src, int count, int k, int d, Span<Slot> dst)
    {
        int m = 0;
        int offset = 0;

        if (d > 0)
        {
            for (int i = 0; i < count; i++)
                Place(dst, ref m, ref offset, src[i], k, d);
        }
        else
        {
            for (int i = count - 1; i >= 0; i--)
                Place(dst, ref m, ref offset, src[i], k, d);
        }

        SortByPosition(dst, m);
        return m;
    }

    /// <summary>One step of <see cref="Shifted"/>'s walk: where this entry lands, and the
    /// cascade offset the next one inherits. A method rather than the local function it reads
    /// like, because a local function may not capture a span.</summary>
    private static void Place(
        Span<Slot> dst, ref int m, ref int offset, in Slot ent, int k, int d)
    {
        int p = ent.Pos;
        var moved = ent;
        if (p == k)
        {
            // On a line: one step puts the dot in the adjacent space.
            p += IsOnLine(p) ? d : 2 * d;
            offset = 2 * d;
            moved.Pos = p;
        }
        else
        {
            if (!Contains(dst, m, p))
                offset = 0;
            moved.Pos = p + offset;
        }

        for (int i = 0; i < m; i++)
        {
            if (dst[i].Pos != moved.Pos)
                continue;
            dst[i] = moved;           // a write onto an occupied slot replaces it, as the map did
            return;
        }
        dst[m++] = moved;
    }

    /// <summary>Insertion sort — the configurations are chord-sized (never more than four in
    /// the corpus measured for <see cref="StackSlots"/>), and positions are unique inside one,
    /// so there is no tie for a stable sort to have an opinion about.</summary>
    private static void SortByPosition(Span<Slot> cfg, int count)
    {
        for (int i = 1; i < count; i++)
        {
            var x = cfg[i];
            int j = i - 1;
            while (j >= 0 && cfg[j].Pos > x.Pos)
            {
                cfg[j + 1] = cfg[j];
                j--;
            }
            cfg[j + 1] = x;
        }
    }

    /// <summary>
    /// LILYPOND-REF: lily/dot-configuration.cc:103-122 remove_collision() —
    /// when P is occupied, take the better of shifting up vs down.
    /// </summary>
    private static void RemoveCollision(
        Span<Slot> cfg, ref int count, int p, Span<Slot> up, Span<Slot> down)
    {
        if (!Contains(cfg, count, p))
            return;

        int upCount = Shifted(cfg, count, p, +1, up);
        int downCount = Shifted(cfg, count, p, -1, down);
        bool takeUp = Badness(up[..upCount]) < Badness(down[..downCount]);

        var best = takeUp ? up : down;
        count = takeUp ? upCount : downCount;
        best[..count].CopyTo(cfg);
    }

    private static bool IsOnLine(int position) => position % 2 == 0;

    /// <summary>
    /// Resolves final dot positions for the given note staff positions.
    /// Returns one resolved position per input, in input order.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/dot-column.cc:194-224 — dots processed in ascending
    /// position order; each insertion first frees its slot, and ON-LINE dots
    /// trigger a second remove_collision so they end up in a space.
    ///
    /// <paramref name="directions"/> supplies each dot's preferred displacement
    /// (LP Dots.direction): 0 = none (the badness already prefers UP), +1/-1 = an
    /// explicit preference (moving against it costs +2). In multi-voice music a
    /// collision sets +1 on the down voice's dots when the up group moved right.
    /// LILYPOND-REF: lily/note-collision.cc:374-397 check_meshing_chords — the
    ///   direction rule (an earlier form here forced DOWN off the staff line alone,
    ///   citing <c>:411-448</c>, a range holding no dot code).
    /// </remarks>
    public static int[] Resolve(IReadOnlyList<int> notePositions, IReadOnlyList<int>? directions = null)
    {
        int n = notePositions.Count;
        if (n == 0)
            return Array.Empty<int>();
        var result = new int[n];

        // ⚠️ THE ONE HOME, not a fast path beside a slow one (RULES §5.2.1②). Only the SOURCE
        // of the scratch forks on size; every line below runs for a chord of one dot and for a
        // chord of forty. A configuration never grows — a shift can merge two dots onto one
        // position but never invent one — so n slots is the bound for all three buffers.
        Span<Slot> cfg = n <= StackSlots ? stackalloc Slot[StackSlots] : new Slot[n];
        Span<Slot> up = n <= StackSlots ? stackalloc Slot[StackSlots] : new Slot[n];
        Span<Slot> down = n <= StackSlots ? stackalloc Slot[StackSlots] : new Slot[n];
        Span<int> order = n <= StackSlots ? stackalloc int[StackSlots] : new int[n];

        // Ascending by position, and STABLE, because two dots can share one: the map keyed by
        // position would keep whichever arrived last, so which input that is has to stay what
        // LINQ's OrderBy made it. Insertion sort is stable and the arrays are chord-sized.
        for (int i = 0; i < n; i++)
            order[i] = i;
        for (int i = 1; i < n; i++)
        {
            int x = order[i];
            int key = notePositions[x];
            int j = i - 1;
            while (j >= 0 && notePositions[order[j]] > key)
            {
                order[j + 1] = order[j];
                j--;
            }
            order[j + 1] = x;
        }

        int count = 0;
        for (int oi = 0; oi < n; oi++)
        {
            int i = order[oi];
            int p = notePositions[i];
            RemoveCollision(cfg, ref count, p, up, down);
            SetAt(cfg, ref count, new Slot(p, p, i, dir: directions?[i] ?? 0));
            if (IsOnLine(p))
                RemoveCollision(cfg, ref count, p, up, down);
        }

        for (int s = 0; s < count; s++)
            result[cfg[s].InputIndex] = cfg[s].Pos;
        return result;
    }
}
