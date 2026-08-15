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
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Where a tab chord's fret digits sit ACROSS the note column — the zigzag that pulls
/// digits on adjacent strings into a left and a right column so they do not overlap at
/// Lily#'s (deliberately larger) fret font size.
/// </summary>
/// <remarks>
/// ⚠️ IT IS A LAYOUT QUANTITY, AND IT MOVED HERE BECAUSE THREE READERS WANT IT. It was a
/// private rule of the renderer while only the renderer drew from it; the spacing engine
/// already had to reach across into <c>Rendering</c> for it through
/// <c>SharedRenderer.TabItemHalfExtent</c>, and the tie's attachment
/// (<see cref="ElementCoordinator"/>) is the third — a tie hangs off ONE digit, so it has
/// to know which column that digit is in. Reaching from Layout into Rendering a second
/// time would have been the "same quantity, second spelling" this repo keeps paying for
/// (docs/RULES.md §5.2.1②).
/// <para>
/// LILYSHARP-OWN, all of it, and LilyPond cannot be asked: its tab digits are small enough
/// to stack on ONE x, so it never faces the question. MEASURED on 2.26.0 with the
/// test/tab-chord-tie twin — all three TabNoteHeads of <c>&lt;c' e' g'&gt;</c> report the
/// same X-offset (chord 1 at 8.82, chord 2 at 12.951). The zigzag is what Lily# owes for
/// digits a player can read from a stand (docs/HANDOFF.md §3, 2026-07-24).
/// </para>
/// </remarks>
internal static class TabChordColumns
{
    /// <summary>Drawn width of a fret number at <see cref="TabConstants.FretFontSize"/>.</summary>
    public static double FretWidth(int fret) =>
        TabConstants.FretGlyphWidth(
            fret.ToString(CultureInfo.InvariantCulture), TabConstants.FretFontSize);

    /// <summary>
    /// Half the distance between the zigzag's two columns: half the widest digit plus a
    /// small gap, so even two-digit frets in the two columns clear each other. A note's
    /// offset is exactly ±this, or 0 when it has no string-adjacent neighbour.
    /// </summary>
    private static double ColumnDelta(IReadOnlyList<(int str, int fret)> notes)
        => notes.Max(p => FretWidth(p.fret)) / 2 + 0.1;

    /// <summary>
    /// Horizontal offset for each chord note (notes ordered top string → bottom) so
    /// digits on ADJACENT strings (which would overlap vertically at the larger font)
    /// are pulled apart into a left and a right column — a zigzag down each run of
    /// adjacent strings; a note with no adjacent neighbour stays centred.
    /// </summary>
    /// <remarks>
    /// LILYSHARP-OWN, tuned on request (2026-08-06): each run's zigzag PHASE puts the
    /// column holding the LARGER frets on the RIGHT — the digits kept left are the small
    /// ones. Concretely the two columns' frets are compared largest-first
    /// (<see cref="CompareFretColumns"/>); ties keep the top note left. A two-note pair is
    /// the same rule at run length 2 (the smaller fret lands left), and an open string's
    /// 0 needs no rule of its own — it is just the smallest digit, and where it lands
    /// follows from what it is stacked against: 0/4/5 top-down puts 4 alone on the left
    /// (its column loses to the {5,0} column's 5), 0/5/4 puts {0,4} left ({5} wins).
    /// </remarks>
    public static double[] Offsets(IReadOnlyList<(int str, int fret)> notes)
    {
        int n = notes.Count;
        var off = new double[n];
        if (n < 2) return off;

        double delta = ColumnDelta(notes);

        // Walk the maximal runs of string-adjacent notes; each run zigzags on its own.
        // The column buffers live OUTSIDE the loop (stackalloc in a loop grows the
        // frame per iteration, CA2014); each run rewrites its own prefix.
        Span<int> evenFrets = stackalloc int[MaxRunColumn];
        Span<int> oddFrets = stackalloc int[MaxRunColumn];
        int i = 0;
        while (i < n)
        {
            int start = i;
            while (i + 1 < n && notes[i + 1].str == notes[i].str + 1)
                i++;
            int end = i;
            i++;

            if (end == start)
            {
                off[start] = 0; // no adjacent neighbour: the digit stays centred.
                continue;
            }

            // The run's two columns, by zigzag parity from the run's top note.
            // ⚠️ ALLOCATION-FREE ON PURPOSE: this runs inside the spacing loop's
            // chord-extent measurements, not once per drawn chord — the first cut
            // (two Lists + two Sorts per run) put +16% on a tab-chord page's render.
            int evenCount = 0, oddCount = 0;
            for (int k = start; k <= end && evenCount < MaxRunColumn; k++)
            {
                if ((k - start) % 2 == 0) evenFrets[evenCount++] = notes[k].fret;
                else oddFrets[oddCount++] = notes[k].fret;
            }
            SortDescending(evenFrets[..evenCount]);
            SortDescending(oddFrets[..oddCount]);

            // The column with the larger frets goes right; a tie keeps the top note left.
            bool evenRight = CompareFretColumns(
                evenFrets[..evenCount], oddFrets[..oddCount]) > 0;
            for (int k = start; k <= end; k++)
            {
                bool right = ((k - start) % 2 == 0) == evenRight;
                off[k] = right ? delta : -delta;
            }
        }
        return off;
    }

    /// <summary>
    /// One zigzag column's capacity: a run spans at most one note per string, so on any
    /// real tuning (up to 8 strings) a column holds at most 4 — 8 is double that. The
    /// gather above stops filling past it (an impossible run keeps its first 16 notes'
    /// phase), rather than growing the stack by the input.
    /// </summary>
    private const int MaxRunColumn = 8;

    /// <summary>Insertion sort, descending — the columns hold at most a few frets.</summary>
    private static void SortDescending(Span<int> s)
    {
        for (int i = 1; i < s.Length; i++)
        {
            int v = s[i];
            int j = i - 1;
            while (j >= 0 && s[j] < v)
            {
                s[j + 1] = s[j];
                j--;
            }
            s[j + 1] = v;
        }
    }

    /// <summary>
    /// Orders a zigzag's two columns: compares their frets largest-first, walking down
    /// the shared length. Equal all the way — including one column simply being the
    /// longer of two otherwise-equal stacks — is a tie (0): the caller keeps the
    /// default phase, which is what holds an all-equal stack (0/0/0) at left-right-left.
    /// </summary>
    private static int CompareFretColumns(ReadOnlySpan<int> a, ReadOnlySpan<int> b)
    {
        for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
            if (a[i] != b[i])
                return a[i].CompareTo(b[i]);
        return 0;
    }
}
