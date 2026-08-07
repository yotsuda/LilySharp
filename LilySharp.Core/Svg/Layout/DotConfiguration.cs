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
internal sealed class DotConfiguration
{
    private readonly struct Entry
    {
        public Entry(int originalPos, int inputIndex, int dir)
        {
            OriginalPos = originalPos;
            InputIndex = inputIndex;
            Dir = dir;
        }

        public int OriginalPos { get; }
        public int InputIndex { get; }
        /// <summary>Preferred displacement direction (Dots.direction); 0 = none.</summary>
        public int Dir { get; }
    }

    // Sorted by current dot position, like LP's map<int, Dot_position>.
    private readonly SortedDictionary<int, Entry> _entries = new();

    /// <summary>
    /// LILYPOND-REF: lily/dot-configuration.cc:25-44 badness() —
    /// 2·(p−orig)², +2 when moved against the preferred direction,
    /// else +1 when the move direction is not UP.
    /// </summary>
    private int Badness()
    {
        int total = 0;
        foreach (var (p, ent) in _entries)
        {
            int demerit = 2 * (p - ent.OriginalPos) * (p - ent.OriginalPos);
            int moveDir = Math.Sign(p - ent.OriginalPos);
            if (ent.Dir != 0 && moveDir != ent.Dir)
                demerit += 2;
            else if (moveDir != 1)
                demerit += 1;
            total += demerit;
        }
        return total;
    }

    /// <summary>
    /// LILYPOND-REF: lily/dot-configuration.cc:55-101 shifted() — move the
    /// entry at K one step (line dots) or two (space dots) in direction D;
    /// following entries (in D's iteration order) cascade by 2·D while their
    /// slots collide, and stop cascading at the first free slot.
    /// </summary>
    private DotConfiguration Shifted(int k, int d)
    {
        var newCfg = new DotConfiguration();
        int offset = 0;

        void Process(int p, Entry ent)
        {
            if (p == k)
            {
                // On a line: one step puts the dot in the adjacent space.
                p += IsOnLine(p) ? d : 2 * d;
                offset = 2 * d;
                newCfg._entries[p] = ent;
            }
            else
            {
                if (!newCfg._entries.ContainsKey(p))
                    offset = 0;
                newCfg._entries[p + offset] = ent;
            }
        }

        if (d > 0)
        {
            foreach (var (p, ent) in _entries)
                Process(p, ent);
        }
        else
        {
            foreach (var (p, ent) in _entries.Reverse())
                Process(p, ent);
        }

        return newCfg;
    }

    /// <summary>
    /// LILYPOND-REF: lily/dot-configuration.cc:103-122 remove_collision() —
    /// when P is occupied, take the better of shifting up vs down.
    /// </summary>
    private void RemoveCollision(int p)
    {
        if (!_entries.ContainsKey(p))
            return;

        var up = Shifted(p, +1);
        var down = Shifted(p, -1);
        var best = up.Badness() < down.Badness() ? up : down;

        _entries.Clear();
        foreach (var (pos, ent) in best._entries)
            _entries[pos] = ent;
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
        var cfg = new DotConfiguration();
        var order = Enumerable.Range(0, notePositions.Count)
            .OrderBy(i => notePositions[i])
            .ToArray();

        foreach (int i in order)
        {
            int p = notePositions[i];
            cfg.RemoveCollision(p);
            cfg._entries[p] = new Entry(p, i, dir: directions?[i] ?? 0);
            if (IsOnLine(p))
                cfg.RemoveCollision(p);
        }

        var result = new int[notePositions.Count];
        foreach (var (pos, ent) in cfg._entries)
            result[ent.InputIndex] = pos;
        return result;
    }
}
