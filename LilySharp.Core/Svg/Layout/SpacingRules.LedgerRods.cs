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

using System.Collections.Immutable;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

internal static partial class SpacingRules
{
    /// <summary>
    /// LedgerLineSpanner's <c>minimum-length-fraction</c>: how much of a head's width the rod keeps
    /// for ledger line on each side of the gap between two ledgered columns.
    /// </summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:2069 LedgerLineSpanner minimum-length-fraction — 0.25.</remarks>
    private const double LedgerMinimumLengthFraction = 0.25;

    /// <summary>
    /// One timing column of one staff whose heads carry ledger lines: the column-frame X extent
    /// of its ledgered heads above and below the staff (empty when <c>Left &gt; Right</c>), and the
    /// widest of those heads.
    /// </summary>
    /// <param name="Column">The column's index in the measure's timing list.</param>
    /// <param name="UpLeft">Leftmost edge of the ledgered heads above the staff.</param>
    /// <param name="UpRight">Rightmost edge of the ledgered heads above the staff.</param>
    /// <param name="DownLeft">Leftmost edge of the ledgered heads below the staff.</param>
    /// <param name="DownRight">Rightmost edge of the ledgered heads below the staff.</param>
    /// <param name="HeadWidth">The widest ledgered head in the column.</param>
    internal readonly record struct LedgerColumn(
        int Column, double UpLeft, double UpRight, double DownLeft, double DownRight, double HeadWidth)
    {
        /// <summary>Whether a head above the staff carries ledger lines.</summary>
        public bool HasUp => UpLeft <= UpRight;

        /// <summary>Whether a head below the staff carries ledger lines.</summary>
        public bool HasDown => DownLeft <= DownRight;
    }

    /// <summary>
    /// The columns of <paramref name="staff"/>'s measure whose heads carry ledger lines, in column
    /// order — the heads <c>Ledger_line_spanner::set_spacing_rods</c> walks.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/ledger-line-spanner.cc:96-133 Ledger_line_spanner::set_spacing_rods — every head of the
    /// Staff's spanner (all its voices), skipping a head whose <c>Staff_symbol::ledger_positions</c>
    /// is empty; each kept head's <c>extent (column, X_AXIS)</c> is united into its side's interval
    /// (the sign of its staff position) and the column's head width is the widest kept head.
    /// The column frame is the drawn one: a chord's displaced head at its offset
    /// (<see cref="ChordHeadPositioning.CalculateOffsets"/>) and a colliding voice at its shift
    /// (<see cref="VoiceCollisionShiftsOf(Model.Staff)"/>).
    /// <para>
    /// A head carries ledger lines at |staff position| &gt;= 6 — the renderer's own test
    /// (SharedRenderer.Noteheads, DrawPlannedLedgers), so the rod answers for exactly the ledgers
    /// that are drawn. ⚠️ Like the renderer it assumes the five-line staff; LilyPond's
    /// <c>ledger_positions</c> follows a staff's own line positions.
    /// </para>
    /// <para>
    /// ⚠️ Grace heads take no part: LilyPond's grace columns are paper columns of their own and
    /// carry this rod too, but Lily#'s grace notes are not columns of the spacing chain.
    /// </para>
    /// </remarks>
    /// <remarks>
    /// The list is built on the first ledgered column, not on entry: 87.0% of these calls
    /// find no head at |position| &gt;= 6 at all (session 451's census, 231 books × 8
    /// keystrokes) and handed back an empty List object for 528 B/keystroke. Both callers
    /// read Count and the indexer only, so the empty answer is the shared empty array.
    /// </remarks>
    internal static IReadOnlyList<LedgerColumn> LedgerColumnsOf(
        Model.Staff staff, int measureIndex, IReadOnlyList<Fraction> timings)
    {
        List<LedgerColumn>? result = null;
        if (staff.IsTab || staff.IsTextRow || timings.Count == 0)
            return Array.Empty<LedgerColumn>();

        int n = timings.Count;
        var upLeft = new double[n];
        var upRight = new double[n];
        var downLeft = new double[n];
        var downRight = new double[n];
        var headWidth = new double[n];
        for (int t = 0; t < n; t++)
        {
            upLeft[t] = downLeft[t] = double.PositiveInfinity;
            upRight[t] = downRight[t] = double.NegativeInfinity;
        }

        var voices = staff.Voices;
        var shifts = VoiceCollisionShiftsOf(staff);
        for (int v = 0; v < voices.Length; v++)
        {
            if (measureIndex >= voices[v].Measures.Length)
                continue;
            var items = voices[v].Measures[measureIndex].Items;
            var onset = Fraction.Zero;
            for (int oi = 0; oi < items.Length; oi++)
            {
                var item = items[oi];
                if (!item.GraceTime && item is NoteItem or ChordItem)
                {
                    int t = -1;
                    for (int k = 0; k < n; k++)
                        if (timings[k] == onset) { t = k; break; }
                    if (t >= 0)
                    {
                        double shift = shifts.ShiftOf(measureIndex, v + 1, oi);
                        var font = IsCueItem(item) ? EngravingDefaults.CueFont : GlyphMetrics.Design20;
                        int noteValue = GetNoteValue(item);
                        double width = GlyphMetrics.GetNoteheadBBox(font, noteValue).Width;
                        if (item is NoteItem note)
                            AddLedgeredHead(t, note.StaffPosition, shift);
                        else if (item is ChordItem chord && chord.Notes.Length > 0)
                        {
                            double[] offsets = ChordHeadPositioning.CalculateOffsets(
                                chord.Notes, chord.StemUp, noteValue, font);
                            for (int h = 0; h < chord.Notes.Length; h++)
                                AddLedgeredHead(t, chord.Notes[h].StaffPosition, shift + offsets[h]);
                        }

                        void AddLedgeredHead(int col, int position, double x)
                        {
                            if (Math.Abs(position) < 6)
                                return;
                            if (position > 0)
                            {
                                upLeft[col] = Math.Min(upLeft[col], x);
                                upRight[col] = Math.Max(upRight[col], x + width);
                            }
                            else
                            {
                                downLeft[col] = Math.Min(downLeft[col], x);
                                downRight[col] = Math.Max(downRight[col], x + width);
                            }
                            headWidth[col] = Math.Max(headWidth[col], width);
                        }
                    }
                }
                onset += item.Duration;
            }
        }

        for (int t = 0; t < n; t++)
            if (headWidth[t] > 0)
                (result ??= new List<LedgerColumn>()).Add(
                    new LedgerColumn(t, upLeft[t], upRight[t], downLeft[t], downRight[t], headWidth[t]));
        return (IReadOnlyList<LedgerColumn>?)result ?? Array.Empty<LedgerColumn>();
    }

    /// <summary>
    /// The rod LilyPond raises between two consecutive ledgered columns of one staff, or a
    /// non-positive number when they carry ledgers on no common side.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/ledger-line-spanner.cc:39-61 set_rods — for each side both columns carry,
    /// <c>distance = 2 * min_length - previous_extents[d][LEFT] + current_extents[d][RIGHT]</c>, where the
    /// walk runs right to left, so "current" is the LEFT column and "previous" the right one, and
    /// <c>min_length</c> is the LEFT column's head width times <see cref="LedgerMinimumLengthFraction"/>
    /// (:113-114). Two rods over the same pair act as their maximum.
    /// MEASURED, LilyPond 2.26.0 (scratch/p380/incr/cols.ps1, floor2.lys): black 32nds a'' → b'' →
    /// c''' → d''' stand 1.9563 apart = 2 × 1.3042 × 0.25 + 1.3042, at every increment from 1.0 to
    /// 1.5, where the skyline minimum and merge_springs' headroom give 1.80.
    /// </remarks>
    internal static double LedgerRodDistance(LedgerColumn left, LedgerColumn right)
    {
        double minLength = left.HeadWidth * LedgerMinimumLengthFraction;
        double distance = double.NegativeInfinity;
        if (left.HasUp && right.HasUp)
            distance = Math.Max(distance, 2 * minLength - right.UpLeft + left.UpRight);
        if (left.HasDown && right.HasDown)
            distance = Math.Max(distance, 2 * minLength - right.DownLeft + left.DownRight);
        return distance;
    }

    /// <summary>
    /// Raises the ledger-line rods between the consecutive ledgered columns of one staff inside
    /// one measure (the rods across a bar line are the system's, in MultiStaffLayouter).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/ledger-line-spanner.cc:39-61 set_rods — <c>rod.add_to_cols ()</c>, a rod between the two
    /// columns however many columns of other staves stand between them, so it goes through the one
    /// Simple_spacer::add_rod port (<see cref="SpringSolver.ApplyRods"/>). Spring 0 runs from the
    /// bar line to column 0, so column <c>c</c> is the right end of spring <c>c</c>.
    /// Called from <c>MultiStaffLayouter.ApplySharedColumnReservations</c>, the home both the
    /// layout and the line-break gate read.
    /// </remarks>
    internal static ImmutableArray<Spring> ApplyLedgerLineRods(
        ImmutableArray<Spring> springs, IReadOnlyList<Fraction> timings, Model.Staff staff, int measureIndex)
    {
        if (springs.Length != timings.Count + 1)
            return springs;
        var columns = LedgerColumnsOf(staff, measureIndex, timings);
        if (columns.Count < 2)
            return springs;
        var rods = new List<(int Left, int Right, double Distance)>();
        for (int i = 1; i < columns.Count; i++)
        {
            double distance = LedgerRodDistance(columns[i - 1], columns[i]);
            if (distance > 0)
                rods.Add((columns[i - 1].Column + 1, columns[i].Column + 1, distance));
        }
        return rods.Count == 0 ? springs : SpringSolver.ApplyRods(springs, rods);
    }
}
