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

using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Within-chord notehead X displacement for seconds and unisons: walking the
/// heads along the stem direction, every other head of a cluster closer than
/// a second is "reversed" to the far side of the stem.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/stem.cc:606-760 Stem::calc_positioning_done —
/// reversed heads shift by <c>ell − thickness·reverse_overlap</c> in the stem
/// direction (UP: right of the stem, DOWN: left), where <c>ell</c> is the
/// head's right ink extent. reverse_overlap is 0.5 normally (reversed heads
/// overlap the stem by 50%), 1.1 for the first pair when it is a unison
/// (head slant would otherwise leave a gap, LP issue 346), and 2 for
/// stemless semibreve chords (positioned nearer to read as one chord).
/// </remarks>
public static class ChordHeadPositioning
{
    /// <summary>
    /// Per-note X offsets (staff spaces, relative to the chord column) in the
    /// same order as <paramref name="notes"/>. All zero when the chord has no
    /// seconds/unisons.
    /// </summary>
    /// <param name="notes">The chord's notes (any order).</param>
    /// <param name="stemUp">Stem direction (drives the shift side).</param>
    /// <param name="noteValue">1=whole, 2=half, 4=quarter, … (head glyph and
    /// the stemless-semibreve rule).</param>
    public static double[] CalculateOffsets(
        IReadOnlyList<ChordNoteInfo> notes, bool stemUp, int noteValue)
    {
        var offsets = new double[notes.Count];
        if (notes.Count < 2)
            return offsets;

        // LILYPOND-REF: stem.cc:684 — ell = head right ink extent.
        double ell = GlyphMetrics.GetNoteheadBBox(noteValue).Right;
        double thick = EngravingDefaults.StemThickness;
        int dir = stemUp ? 1 : -1;

        // LILYPOND-REF: stem.cc:619-623 — sort by position, reversed for DOWN,
        // so the walk always runs in the stem direction from the support head.
        var order = new int[notes.Count];
        for (int i = 0; i < notes.Count; i++) order[i] = i;
        Array.Sort(order, (a, b) =>
            (dir * notes[a].StaffPosition).CompareTo(dir * notes[b].StaffPosition));

        // LILYPOND-REF: stem.cc:667-760 — parity walk over adjacent intervals.
        bool parity = true;
        double lastPos = notes[order[0]].StaffPosition;
        const int threshold = 1; // note-collision-threshold (staff positions)
        for (int k = 1; k < order.Length; k++)
        {
            double p = notes[order[k]].StaffPosition;
            double dy = Math.Abs(lastPos - p);

            if (dy < 0.1 + threshold)
            {
                if (parity)
                {
                    // LILYPOND-REF: stem.cc:697-740 reverse_overlap selection.
                    double reverseOverlap = 0.5;
                    if (k == 1 && dy < 0.1)
                        reverseOverlap = 1.1;
                    if (noteValue == 1)
                        reverseOverlap = 2; // stemless semibreve chord

                    offsets[order[k]] = (ell - thick * reverseOverlap) * dir;
                }
                parity = !parity;
            }
            else
            {
                parity = true;
            }
            lastPos = p;
        }

        return offsets;
    }
}
