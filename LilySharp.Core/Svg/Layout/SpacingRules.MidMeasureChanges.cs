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
    /// The two gaps LilyPond puts around a MID-MEASURE clef / key / time change, plus the
    /// pair's minimum. Distances are column origin to column origin.
    /// </summary>
    /// <param name="LeftGap">Previous musical column → the change column's origin.</param>
    /// <param name="RightGap">The change column's origin → the next musical column.</param>
    /// <param name="MinDistance">Minimum for the two together (the rods, summed).</param>
    internal readonly record struct MidMeasureChangeSpacing(
        double LeftGap, double RightGap, double MinDistance)
    {
        /// <summary>Previous musical column → the next one, i.e. what one spring must span.</summary>
        public double TotalIdeal => LeftGap + RightGap;
    }

    /// <summary>
    /// The change column's own extent right — <c>last_ext[RIGHT]</c> in
    /// <c>Staff_spacing::get_spacing</c>. Zero for anything that is not a change item.
    /// </summary>
    /// <remarks>
    /// The column's origin is the glyph's INK LEFT edge (measured on 2.24.4: a mid-measure
    /// bass clef's anchor plus its ink width plus 1.0 lands exactly on the next note head),
    /// so this is simply the glyph's width.
    /// LILYPOND-REF: lily/spacing-interface.cc:217 — <c>ext = break_item->extent (col, X_AXIS)</c>.
    /// </remarks>
    private static double ChangeItemColumnWidth(MusicItem item) => item switch
    {
        ClefChangeItem cc => GetClefChangeWidth(cc.NewClef),
        KeySignatureChangeItem kc => GetKeySignatureChangeWidth(kc),
        TimeSignatureChangeItem tc => GetTimeSignatureChangeWidth(tc),
        _ => 0
    };

    private static bool IsChangeItem(MusicItem item) =>
        item is ClefChangeItem or KeySignatureChangeItem or TimeSignatureChangeItem;

    /// <summary>
    /// Whether this change grob puts INK in the non-musical column — i.e. whether the column
    /// walks may take its width, its break-align gap and its space-alist entry. False only for
    /// a BLANKED meter (<see cref="TimeSignatureChangeItem.Blanked"/>), which is a grob with an
    /// empty X extent.
    /// </summary>
    /// <remarks>
    /// This is deliberately NOT folded into <see cref="IsChangeItem"/>. The two questions are
    /// different and LilyPond answers them differently: a blanked TimeSignature IS still an
    /// Item of the non-musical column — so it must keep failing
    /// <see cref="IsMidMeasureChangeColumn"/>'s test for a MUSICAL item, or
    /// MeasureLayouter.ItemStartingAt would hand a zero-duration grob to the skyline as the
    /// note at that moment — while every walk that reads an EXTENT steps over it:
    /// LILYPOND-REF: lily/break-alignment-interface.cc:144-156 calc_positioning_done — the
    ///   alignment walk advances past each element whose extent <c>is_empty ()</c>, so a
    ///   blanked grob is given no offset and widens the group by nothing.
    /// LILYPOND-REF: lily/spacing-interface.cc:217-220 extremal_break_aligned_grob —
    ///   <c>if (ext.is_empty ()) continue;</c>, so a blanked grob never becomes the
    ///   <c>last_grob</c> whose <c>space-alist</c> prices the following note either.
    /// <para>
    /// MEASURED (audit/lp-geometry/probes/tab-numbers-meter.ly, ledger points
    /// mid-piece.tab-numbers.* and mid-measure.tab-numbers.meter-identity): on a bare TabStaff
    /// a mid-piece <c>\time 2/4</c> and a bar grid reached with <c>\set Timing.measureLength</c>
    /// and NO meter command at all render byte-identical, and every bar of the probe puts its
    /// first fret digit 0.945513437989928 from the bar line's ink right whether that bar
    /// carries a change or not. The column is ABSENT, not zero-wide — which is why this
    /// predicate removes the item from the walk instead of returning a width of 0: a
    /// zero-width member would still spend its
    /// (first-note . (semi-shrink-space . 2.0)) distance.
    /// </para>
    /// </remarks>
    internal static bool ChangeItemHasInk(MusicItem item) =>
        item is not TimeSignatureChangeItem { Blanked: true };

    /// <summary>
    /// Whether this item stands in the non-musical change column rather than the musical
    /// one — i.e. whether <see cref="MidMeasureChangeGaps"/> owns its spacing.
    /// </summary>
    internal static bool IsMidMeasureChangeColumn(MusicItem item) => IsChangeItem(item);

    /// <summary>
    /// The <c>space-alist</c> distance from a change item to the following note.
    /// </summary>
    /// <remarks>
    /// ⚠️ <c>Staff_spacing::get_spacing</c> looks up <c>first-note</c> and only replaces it
    /// with <c>next-note</c> when that entry EXISTS (staff-spacing.cc:147-153). Clef is the
    /// only one of the three that has a <c>next-note</c> entry, so a MID-LINE key or time
    /// change — where nothing is starting a line — is nevertheless priced by
    /// <c>first-note</c>. Counter-intuitive, and confirmed by measurement: probes MK and MC
    /// land on 2.5 and 1.0 to six digits (COORDINATE_AUDIT.md §4.7.2).
    /// <para>
    /// All three of the alist types involved (extra-space, shrink-space, semi-shrink-space)
    /// put the IDEAL at <c>last_ext[RIGHT] + distance</c>; they differ only in what becomes
    /// `fixed` and whether the spring stretches, neither of which this single-spring model
    /// carries yet. LILYPOND-REF: lily/staff-spacing.cc:174-198.
    /// </para>
    /// </remarks>
    private static double ChangeItemSpaceToNextNote(MusicItem item) =>
        ChangeItemSpaceDef(item).Distance;

    /// <summary>
    /// The whole space-alist entry a change grob offers the following note: the distance and
    /// which of <c>Staff_spacing</c>'s arms consumes it.
    /// </summary>
    /// <returns>
    /// <c>Distance</c>, plus the two flags that say which arm consumes it.
    /// <para>
    /// <c>SplitsFixed</c> is semi-shrink-space, which puts HALF the distance into
    /// <c>fixed</c> before the ideal (staff-spacing.cc:193-198). extra-space and shrink-space
    /// leave <c>fixed</c> alone, so they differ from it under compression even though all
    /// three put the ideal at <c>last_ext[RIGHT] + distance</c>.
    /// </para>
    /// <para>
    /// <c>Stretchable</c>: shrink-space and semi-shrink-space clear
    /// <c>is_stretchable</c> (:191, :197); extra-space does not.
    /// </para>
    /// </returns>
    private static (double Distance, bool SplitsFixed, bool Stretchable)
        ChangeItemSpaceDef(MusicItem item) => item switch
        {
            // (next-note . (extra-space . 1.0))            scm/define-grobs.scm:924
            ClefChangeItem => (1.0, false, true),
            // (first-note . (shrink-space . 2.5))          scm/define-grobs.scm:1947
            KeySignatureChangeItem => (2.5, false, false),
            // (first-note . (semi-shrink-space . 2.0))     scm/define-grobs.scm:3948
            TimeSignatureChangeItem => (2.0, true, false),
            _ => (0, false, true)
        };

    /// <summary>
    /// A key or time change opening a measure shares the bar line's non-musical column. This
    /// returns how far its ink right edge sits from the bar line's ink RIGHT edge — the frame
    /// <see cref="BarlineToFirstColumnSpring"/> works in — and which grob ends the column.
    /// Null when nothing break-aligned opens the measure.
    /// </summary>
    /// <remarks>
    /// Inside the column, break alignment puts each group's left edge at the previous group's
    /// ink right plus the LEFT group's space-alist entry keyed on the RIGHT group's
    /// break-align-symbol: BarLine gives key-signature 1.0 and time-signature 0.75
    /// (scm/define-grobs.scm BarLine.space-alist, transcribed in
    /// <see cref="GetBarlineToItemSpace"/>). Measured on 2.24.4: bar-line ink right to the
    /// signature's anchor is exactly 1.000000 and 0.750000 — COORDINATE_AUDIT.md §4.7.3.
    /// <para>
    /// A CLEF change opening a measure is excluded: break-align-orders engraves it BEFORE the
    /// bar line (scm/define-grobs.scm:650-664), so it is paid for by the preceding measure's
    /// closing gap via <see cref="BoundaryClefAllowance"/> and contributes nothing here.
    /// </para>
    /// <para>
    /// ⚠️ SIMPLIFICATION: LilyPond splits a key change into a KeyCancellation grob and a
    /// KeySignature grob with 0.5 between them (KeyCancellation.space-alist), where Lily#
    /// carries both in one KeySignatureChangeItem whose width already sums the naturals. The
    /// corpus does not reach that case — probe K goes from no accidentals to three, so no
    /// cancellation is engraved — and it is a separate defect from this one.
    /// </para>
    /// </remarks>
    internal static (double Prefix, MusicItem LastChange)? BoundaryChangePrefix(
        IReadOnlyList<MusicItem>? firstItems)
    {
        if (firstItems == null)
            return null;

        double prefix = 0;
        MusicItem? last = null;
        for (int i = 0; i < firstItems.Count; i++)
        {
            var item = firstItems[i];
            // ChangeItemHasInk for the same reason MeasureChangeColumn asks it: a blanked
            // meter has an empty extent, so break alignment steps over it and it neither
            // takes the bar line's space-alist entry nor offers one to the note after it.
            if (item is ClefChangeItem || !IsChangeItem(item) || !ChangeItemHasInk(item))
                continue;
            // ⚠️ ONE GROB PER KIND — see IsFirstChangeOfItsKind. This list is aggregated
            // ACROSS STAVES, so a key change opening a measure of a grand staff arrives once
            // per staff, and adding each in turn charged the measure one extra signature per
            // extra staff.
            if (!IsFirstChangeOfItsKind(firstItems, i))
                continue;
            prefix += last == null
                ? GetBarlineToItemSpace(item)
                : BetweenChangeItemsSpace(last, item);
            prefix += WidestChangeOfKind(firstItems, ChangeItemKind(item));
            last = item;
        }
        return last == null ? null : (prefix, last);
    }

    /// <summary>
    /// Which break-align slot a change item occupies: 0 clef, 1 key, 2 meter.
    /// </summary>
    private static int ChangeItemKind(MusicItem item) => item switch
    {
        ClefChangeItem => 0,
        KeySignatureChangeItem => 1,
        TimeSignatureChangeItem => 2,
        _ => throw new ArgumentOutOfRangeException(
            nameof(item), item?.GetType().Name, "not a change item"),
    };

    /// <summary>
    /// Is the item at <paramref name="index"/> the FIRST inked grob of its break-align kind in
    /// this column?
    /// </summary>
    /// <remarks>
    /// ⚠️ A COLUMN'S ITEM LIST IS AGGREGATED ACROSS STAVES
    /// (MeasureLayouter.BuildTimingToItemsMap), so the same key change arrives once per staff.
    /// They are not grobs standing side by side — every staff draws its own signature at the
    /// SAME x, and the column is one signature wide. Summing them cost one extra signature per
    /// extra staff: measured on a 3-sharp change, 1.64 ss of bar-to-note on one staff, 4.94 on
    /// two, 8.24 on three (+3.300030 each), which an owner read as space reserved for a time
    /// signature that never appeared.
    /// <para>
    /// LILYPOND, MEASURED (audit/lp-geometry/probes/key-column-staves.ly, scores KS1/KS2/KS3):
    /// one, two and three staves put the bar line, the KeySignature and the following note
    /// head at the same x to twelve digits. The column does not widen with the staff count.
    /// </para>
    /// <para>
    /// Order is the LIST's, not break-align's: the renderer sequences a SINGLE staff's items
    /// through <see cref="MidMeasureChangeOffsetWithin"/>, so the sizing walk must keep the
    /// same sequence or the space and the glyph part company. Only duplicates are dropped.
    /// </para>
    /// <para>
    /// Linear scans rather than a table: a column holds at most a clef, a key and a meter per
    /// staff, so this is a handful of comparisons and allocates nothing — the walks it serves
    /// run once per measure per layout pass, and layout passes run per line-break trial.
    /// </para>
    /// </remarks>
    private static bool IsFirstChangeOfItsKind(IReadOnlyList<MusicItem> columnItems, int index)
    {
        int kind = ChangeItemKind(columnItems[index]);
        for (int i = 0; i < index; i++)
        {
            var other = columnItems[i];
            if (IsChangeItem(other) && ChangeItemHasInk(other) && ChangeItemKind(other) == kind)
                return false;
        }
        return true;
    }

    /// <summary>
    /// The widest ink any staff contributes for <paramref name="kind"/> in this column — the
    /// column is as wide as the widest staff's grob, which also covers staves whose signatures
    /// differ from each other. See <see cref="IsFirstChangeOfItsKind"/>.
    /// </summary>
    private static double WidestChangeOfKind(IReadOnlyList<MusicItem> columnItems, int kind)
    {
        double widest = 0;
        foreach (var item in columnItems)
        {
            if (!IsChangeItem(item) || !ChangeItemHasInk(item) || ChangeItemKind(item) != kind)
                continue;
            double w = ChangeItemColumnWidth(item);
            if (w > widest)
                widest = w;
        }
        return widest;
    }

    /// <summary>
    /// A change grob's own <c>extra-spacing-width</c>, as (leftward, rightward) reach.
    /// </summary>
    /// <remarks>
    /// These are NOT the default <c>(-0.1 . 0.1)</c>: KeySignature and KeyCancellation
    /// declare <c>(0.0 . 1.0)</c> (scm/define-grobs.scm:1936, :1982) and TimeSignature
    /// <c>(0.0 . 0.8)</c> (:3933); Clef declares nothing and keeps the default
    /// (lily/separation-item.cc:167). The zero on the left is measurable: it is exactly why
    /// the mid-measure key and clef probes' left gaps differ by 0.05 — half of the 0.1.
    /// </remarks>
    private static (double Left, double Right) ChangeItemExtraSpacingWidth(MusicItem item) =>
        item switch
        {
            KeySignatureChangeItem => (0.0, 1.0),
            TimeSignatureChangeItem => (0.0, 0.8),
            _ => (DefaultExtraSpacingWidth, DefaultExtraSpacingWidth)
        };

    /// <summary>
    /// The gap between two change items sharing one column, from the LEFT one's space-alist
    /// keyed on the right one's <c>break-align-symbol</c>.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:922-923 Clef (key-signature 0.82, time-signature
    /// 1.52); :1945 KeySignature (time-signature 1.15). Only these orders occur, because
    /// break-align-orders fixes the sequence clef → key-signature → time-signature
    /// (scm/define-grobs.scm:650-664).
    /// </remarks>
    private static double BetweenChangeItemsSpace(MusicItem left, MusicItem right) =>
        (left, right) switch
        {
            (ClefChangeItem, KeySignatureChangeItem) => 0.82,
            (ClefChangeItem, TimeSignatureChangeItem) => 1.52,
            (KeySignatureChangeItem, TimeSignatureChangeItem) => 1.15,
            _ => 0
        };

    /// <summary>
    /// How far the leftmost ink of a MUSICAL column reaches left of that column's origin,
    /// including the grob's own <c>extra-spacing-width</c> — the right-hand term of
    /// <c>Paper_column::minimum_distance</c>.
    /// </summary>
    internal static double MusicalColumnLeftReach(MusicItem item) =>
        CalculateLeftExtent(item)
        + (HasAccidental(item) ? AccidentalExtraSpacingWidthLeft : DefaultExtraSpacingWidth);

    /// <summary>
    /// Prices a mid-measure clef / key / time change the way LilyPond does: as its own
    /// non-musical column between two musical ones, with the two gaps around it computed by
    /// DIFFERENT formulas. Returns null when <paramref name="columnItems"/> holds no change.
    /// </summary>
    /// <param name="columnItems">Everything starting at this timing — the change items and
    /// the note(s) that share their moment.</param>
    /// <param name="prevItems">Everything at the previous column, across voices and staves;
    /// the rod takes the furthest-reaching of them, as a paper column aggregates all staves.</param>
    /// <param name="durationIdeal">The plain note-to-note ideal for this pair, i.e. what the
    /// spring would be with no change item in the way.</param>
    /// <remarks>
    /// <para>
    /// LEFT — lily/note-spacing.cc:87-108. The right column is NonMusical and, mid-measure,
    /// has no staff-bar group, so the :103-108 branch is taken: the whole change column's
    /// width is subtracted from the ideal, and the result is floored at half way between the
    /// ideal and the rod. In practice the floor is what binds; the subtraction can only win
    /// when the duration ideal exceeds <c>2 × width + rod</c>, e.g. a whole note before a
    /// clef change. Both are implemented because LilyPond implements both.
    /// </para>
    /// <para>
    /// RIGHT — lily/staff-spacing.cc:166-215. The ideal is the change column's own width plus
    /// the space-alist distance, then lifted to <c>0.3 + min_dist</c> by the :213 correction
    /// when a wide accidental on the next note would otherwise collide.
    /// </para>
    /// <para>
    /// ⚠️ NOT modelled: LilyPond has TWO springs here and Lily# still has one, so the split
    /// is exact only at force 0 (which is where the corpus measures). Under justification
    /// LilyPond stretches the two independently, and for a key or time change the right one
    /// does not stretch at all (shrink-space / semi-shrink-space set
    /// <c>is_stretchable = false</c>, staff-spacing.cc:191, :197). Fixing that needs the real
    /// second column — the same work roadmap item 3 needs at a bar line.
    /// </para>
    /// </remarks>
    internal static MidMeasureChangeSpacing? MidMeasureChangeGaps(
        IReadOnlyList<MusicItem>? columnItems, IReadOnlyList<MusicItem>? prevItems,
        double durationIdeal)
    {
        var (columnWidth, firstChange, lastChange) = MeasureChangeColumn(columnItems);
        if (firstChange == null)
            return null;

        // --- LEFT: note-spacing.cc:79-82 rod, then :105-107 ---
        // The rod is the pure skyline distance between the previous column and this one:
        // the previous item's own ink reach plus each side's extra-spacing-width.
        double prevReach = 0;
        if (prevItems != null)
            foreach (var item in prevItems)
                if (!IsChangeItem(item))
                    prevReach = Math.Max(prevReach, CalculateNoteheadRightExtent(item));
        double leftRod = prevReach
                         + DefaultExtraSpacingWidth
                         + ChangeItemExtraSpacingWidth(firstChange).Left;
        double leftGap = Math.Max(durationIdeal - columnWidth,
                                  (durationIdeal + leftRod) / 2.0);

        // --- RIGHT: staff-spacing.cc:166-215 ---
        double rightRod = RightRod(columnItems!, columnWidth, lastChange!);
        double rightGap = RightGap(columnWidth, lastChange!, rightRod);

        return new MidMeasureChangeSpacing(leftGap, rightGap, leftRod + rightRod);
    }

    /// <summary>
    /// The change column's origin → the next musical column: the SAME quantity
    /// <see cref="MidMeasureChangeGaps"/> puts in the spring, so the drawn glyph and the
    /// reserved space come from one place and cannot drift. Zero when there is no change.
    /// </summary>
    /// <remarks>
    /// This depends only on the items, never on the solved force, so the renderer may
    /// position the change column by hanging it back from the next musical column. That is
    /// also what keeps a change glyph clear of a wide accidental at any line width — the
    /// accidental enters through the rod, exactly as in LilyPond.
    /// </remarks>
    internal static double MidMeasureChangeRightGap(IReadOnlyList<MusicItem>? columnItems)
    {
        var (columnWidth, first, last) = MeasureChangeColumn(columnItems);
        if (first == null)
            return 0;
        return RightGap(columnWidth, last!, RightRod(columnItems!, columnWidth, last!));
    }

    /// <summary>
    /// How far the next change grob in the same column sits from this one's origin: this
    /// glyph's own width plus the break-align gap to <paramref name="next"/>.
    /// </summary>
    /// <remarks>
    /// A BLANKED grob advances nothing and earns no gap on either side
    /// (<see cref="ChangeItemHasInk"/>): break alignment steps over an empty extent, so the
    /// gap runs from the previous PRESENT grob to the next PRESENT one.
    /// </remarks>
    internal static double ChangeColumnGlyphAdvance(MusicItem change, MusicItem? next) =>
        !ChangeItemHasInk(change)
            ? 0
            : ChangeItemColumnWidth(change)
              + (next != null && ChangeItemHasInk(next)
                  ? BetweenChangeItemsSpace(change, next) : 0);

    /// <summary>
    /// Where <paramref name="change"/> sits inside its change column, measured from the
    /// column's origin. Zero for the first change; later ones follow their predecessors'
    /// widths and the break-align gap between them.
    /// </summary>
    internal static double MidMeasureChangeOffsetWithin(
        IReadOnlyList<MusicItem>? columnItems, MusicItem change)
    {
        if (columnItems == null)
            return 0;

        double offset = 0;
        MusicItem? previous = null;
        foreach (var item in columnItems)
        {
            // Same skip as MeasureChangeColumn, so this walk and the one that sized the
            // column place the same grobs at the same offsets. A blanked meter is drawn
            // nowhere (SharedRenderer.Tab's engravesMeter), so the branch it would take
            // below is unreachable in the render path; keeping the two walks identical is
            // what stops a clef sharing its column from being offset by a phantom width.
            if (!IsChangeItem(item) || !ChangeItemHasInk(item))
                continue;
            if (previous != null)
                offset += BetweenChangeItemsSpace(previous, item);
            if (ReferenceEquals(item, change))
                return offset;
            offset += ChangeItemColumnWidth(item);
            previous = item;
        }
        return 0;
    }

    /// <summary>
    /// Walks a column's items and returns the change column's total extent right together
    /// with its leftmost and rightmost change grobs. Changes sharing a column are drawn side
    /// by side in break-align order (clef → key-signature → time-signature), separated by
    /// the LEFT one's space-alist entry for the right one's break-align-symbol.
    /// LILYPOND-REF: scm/define-grobs.scm:650-664 break-align-orders.
    /// </summary>
    private static (double Width, MusicItem? First, MusicItem? Last) MeasureChangeColumn(
        IReadOnlyList<MusicItem>? columnItems)
    {
        if (columnItems == null)
            return (0, null, null);

        double width = 0;
        MusicItem? first = null, last = null;
        for (int i = 0; i < columnItems.Count; i++)
        {
            var item = columnItems[i];
            // The break-align walk steps over an empty extent (ChangeItemHasInk), so a
            // blanked meter is neither the first nor the last grob of the column and adds
            // neither its width nor a gap to its neighbour. When it is the ONLY change here,
            // `first` stays null and the caller prices the pair as if nothing stood between
            // the two musical columns — which is what LilyPond draws.
            if (!IsChangeItem(item) || !ChangeItemHasInk(item))
                continue;
            // ⚠️ ONE GROB PER KIND, THE WIDEST — see IsFirstChangeOfItsKind. Same aggregation
            // across staves as the measure-opening walk, and the same defect: here the surplus
            // shows up as space BEFORE the change (the column is wider, so its origin sits
            // further right), where at a measure opening it shows up after.
            if (!IsFirstChangeOfItsKind(columnItems, i))
                continue;
            if (first == null)
                first = item;
            else
                width += BetweenChangeItemsSpace(last!, item);
            last = item;
            width += WidestChangeOfKind(columnItems, ChangeItemKind(item));
        }
        return (width, first, last);
    }

    /// <summary>
    /// <c>Paper_column::minimum_distance</c> from the change column to the musical one: the
    /// change column's own reach plus whatever the next column's leftmost ink hangs left.
    /// </summary>
    private static double RightRod(
        IReadOnlyList<MusicItem> columnItems, double columnWidth, MusicItem lastChange)
    {
        double reach = 0;
        foreach (var item in columnItems)
            if (!IsChangeItem(item))
                reach = Math.Max(reach, MusicalColumnLeftReach(item));
        return columnWidth + ChangeItemExtraSpacingWidth(lastChange).Right + reach;
    }

    /// <summary>
    /// <c>Staff_spacing::get_spacing</c>'s ideal for the change column → next note, with the
    /// :213 minimum-distance correction.
    /// </summary>
    /// <remarks>
    /// The space-alist consulted belongs to the RIGHTMOST break-aligned grob in the column
    /// (<c>Spacing_interface::extremal_break_aligned_grob</c> with <c>d == LEFT</c> picks the
    /// one whose right edge is largest), which under break-align-orders is the last of
    /// clef / key / time present.
    /// LILYPOND-REF: lily/staff-spacing.cc:166-175 (ideal), :213-215 (the 0.3 correction).
    /// </remarks>
    private static double RightGap(double columnWidth, MusicItem lastChange, double rightRod) =>
        Math.Max(columnWidth + ChangeItemSpaceToNextNote(lastChange),
                 SpringHeadroom + rightRod);

    // ========================================
    // Loose change columns (multi-staff polyphony)
    // ========================================

    /// <summary>
    /// The timing of the change column's LEFT NEIGHBOR in its own staff: the onset of the
    /// last musical item before the change in the voice(s) that carry it. Null when no item
    /// precedes the change (an opening change, which the boundary column owns instead).
    /// </summary>
    /// <remarks>
    /// LilyPond's <c>left-neighbor</c> is set from spacing wishes, which link a note column
    /// to the NEXT column of the SAME staff (Note_spacing_engraver keys its
    /// <c>last_spacings_</c> map by the voice's parent context), so another staff's column
    /// in between is invisible to it — that mismatch is exactly what
    /// <see cref="IsLooseChangeColumn"/> detects. When several staves change at one moment,
    /// the neighbor with the LARGEST rank wins.
    /// LILYPOND-REF: lily/spacing-determine-loose-columns.cc:283-319 set_explicit_neighbor_columns
    ///   — :311-315 keeps the left col with <c>left_rank > old_left_neighbor->get_rank ()</c>.
    /// ⚠️ SIMPLIFICATION: the walk sees only the voice the change item sits in. In LilyPond
    /// the neighbor map is per STAFF, so a second voice of the same staff with a note
    /// between this voice's note and the change would BE the neighbor and make the column
    /// not loose; the corpus has no mid-measure change in a multi-voice staff yet.
    /// </remarks>
    internal static Fraction? LooseChangeLeftNeighborTiming(
        IReadOnlyList<Measure> measures, IReadOnlyList<MusicItem> columnItems)
    {
        Fraction? best = null;
        foreach (var m in measures)
        {
            var t = Fraction.Zero;
            Fraction? lastMusicalOnset = null;
            foreach (var item in m.Items)
            {
                if (IsChangeItem(item) && ContainsByReference(columnItems, item))
                {
                    if (lastMusicalOnset is { } onset && (best is not { } b || onset > b))
                        best = onset;
                    break;
                }
                if (!IsChangeItem(item))
                    lastMusicalOnset = t;
                t += item.Duration;
            }
        }
        return best;

        static bool ContainsByReference(IReadOnlyList<MusicItem> items, MusicItem item)
        {
            foreach (var candidate in items)
                if (ReferenceEquals(candidate, item))
                    return true;
            return false;
        }
    }

    /// <summary>
    /// The loose change's OWN-STAFF left-neighbour ITEM — the left bound of LilyPond's
    /// next_door pair, whose ink the pruned column's rod arms are measured from
    /// (lily/spacing-determine-loose-columns.cc:135-185 set_distances_for_loose_col:
    /// <c>r.item_drul_ = next_door</c>, the loose column's own-staff neighbours).
    /// ⚠️ Until 2026-08-21 the rod's left arm read the furthest reach of the UNION
    /// previous column instead — another staff's intervening note (sploose's A4). The
    /// two spellings agreed byte-for-byte while item M priced every scaled head as
    /// black; the notated-head fix split them (the A4 is a DRAWN half, the own C#3 an
    /// eighth), and the LP-pinned loose net moved +0.08 the moment the wrong column's
    /// head got its true width — which is how the wrong input was found.
    /// </summary>
    internal static MusicItem? LooseChangeOwnPrevItem(
        IReadOnlyList<Model.Measure> measures, IReadOnlyList<MusicItem> columnItems)
    {
        foreach (var m in measures)
        {
            MusicItem? lastMusical = null;
            foreach (var item in m.Items)
            {
                if (IsChangeItem(item))
                {
                    foreach (var candidate in columnItems)
                        if (ReferenceEquals(candidate, item))
                            return lastMusical;
                    continue;
                }
                lastMusical = item;
            }
        }
        return null;
    }

    /// <summary>
    /// Whether a mid-measure change column is LOOSE: fixed to neither neighbor by the
    /// spring chain, because another staff's column stands between it and its own staff's
    /// previous note. A loose column is pruned from the springs (the pair across it is
    /// priced as if the change were not there) and the renderer drapes it back from its
    /// right neighbor by <see cref="LooseChangeColumnHangDistance"/>.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spacing-determine-loose-columns.cc:45-133 is_loose_column.
    /// The clauses, in LilyPond's order:
    /// <list type="bullet">
    /// <item><c>allow-loose-spacing</c> — #t by default on both PaperColumn and
    ///   NonMusicalPaperColumn (scm/define-grobs.scm), and Lily# has no override
    ///   spelling.</item>
    /// <item>float_nonmusical_columns_ / float_grace_columns_ (:52-56) — both come from
    ///   <c>uniform-stretching</c> options that default off.</item>
    /// <item>a musical column is never loose (:58-59) — this is only called for change
    ///   columns.</item>
    /// <item>missing neighbors (:82-90) — <paramref name="ownLeftNeighborTiming"/> null.</item>
    /// <item>the series check (:95-99): placed nicely in series with its neighbors AND
    ///   non-empty → not loose. The right neighbor always matches (the change shares its
    ///   note's moment, so no column can intervene); the left one mismatches exactly when
    ///   some timing falls strictly between the own-staff neighbor and the change.</item>
    /// <item>sensible bounds (:106-112) — the own-staff neighbors are note onsets, i.e.
    ///   musical columns, so this always holds here.</item>
    /// <item>never move bar lines (:117-130) — a Lily# mid-measure change column carries
    ///   no staff-bar group.</item>
    /// </list>
    /// </remarks>
    internal static bool IsLooseChangeColumn(
        IReadOnlyList<Fraction> allTimings, Fraction? ownLeftNeighborTiming,
        Fraction changeTiming, IReadOnlyList<MusicItem>? columnItems)
    {
        if (ownLeftNeighborTiming is not { } left)
            return false;

        var (columnWidth, first, _) = MeasureChangeColumn(columnItems);
        if (first == null)
            return false;

        // The series check: `(l == l_neighbor) && (r == r_neighbor)` with positive width.
        // LILYPOND-REF: lily/spacing-determine-loose-columns.cc:95-99 is_loose_column —
        //   `col->extent (col, X_AXIS).length () > 0`.
        bool leftNeighborAdjacent = true;
        foreach (var t in allTimings)
            if (t > left && t < changeTiming)
            {
                leftNeighborAdjacent = false;
                break;
            }
        if (leftNeighborAdjacent && columnWidth > 0)
            return false;

        return true;
    }

    /// <summary>
    /// How far a LOOSE change column's origin hangs back from its right neighbor's origin,
    /// given the room the solved line actually left for it. Aims for the ideal spacing and
    /// falls back on the tight (minimum) spacing as the room closes.
    /// </summary>
    /// <param name="columnItems">Everything at the change's moment — the change items and
    /// the note(s) they precede.</param>
    /// <param name="permissibleDistance">Solved room for the clique: the right neighbor
    /// column's origin minus the left neighbor column's ink RIGHT edge.
    /// LILYPOND-REF: lily/spacing-loose-columns.cc:182-184 — <c>permissible_distance =
    /// clique.back ()->relative_coordinate (...) - robust_relative_extent (clique[0], ...)[RIGHT]</c>.</param>
    /// <remarks>
    /// The ideal/tight pair for a change → note clique edge comes from
    /// <c>standard_breakable_column_spacing</c>. The loose column shares its note's
    /// moment, so that lands in the dt == 0 arm — "Staff_spacing should handle the job,
    /// using dt when it is 0 is silly" — whose spring is simply
    /// <c>(min_dist + 0.5, min_dist)</c> over <c>Paper_column::minimum_distance</c>;
    /// both are then floored by the loose column's own length. (NOT the
    /// <c>Staff_spacing::get_spacing</c> ideal the change column's own spring carries —
    /// misread that way at first, which only the scale factor could have told apart.)
    /// LILYPOND-REF: lily/spacing-basic.cc:41-83 standard_breakable_column_spacing —
    ///   :44 min_dist; :71-77 the dt == 0 arm, <c>ideal = min_dist + 0.5</c>.
    /// LILYPOND-REF: lily/spacing-loose-columns.cc:151-179 set_loose_columns — the spring,
    ///   then <c>base_note_space = std::max (..., loose_col_horizontal_length)</c> and the
    ///   same for <c>tight_note_space</c>.
    /// <para>
    /// ⚠️ SIMPLIFICATION: a Lily# clique is always the three columns [left neighbor, loose,
    /// right neighbor]. LilyPond chains consecutive loose columns into one clique
    /// (spacing-loose-columns.cc:51-81); two adjacent loose change columns would each hang
    /// from their own right neighbor here. The corpus has no such book.
    /// </para>
    /// </remarks>
    internal static double LooseChangeColumnHangDistance(
        IReadOnlyList<MusicItem>? columnItems, double permissibleDistance)
    {
        var (columnWidth, first, last) = MeasureChangeColumn(columnItems);
        if (first == null)
            return 0;

        double minDist = RightRod(columnItems!, columnWidth, last!);
        double tight = Math.Max(minDist, columnWidth);
        double ideal = Math.Max(minDist + LooseColumnZeroDtSpace, columnWidth);

        // "currently a magic number - what would be a good grob to hold this property?"
        // LILYPOND-REF: lily/spacing-loose-columns.cc:192 — <c>Real left_padding = 0.15</c>.
        const double leftPadding = 0.15;

        // The single-pair clique sums are just the pair itself (clique_spacing[0] is 0.0).
        // A zero denominator reaches the same answer LilyPond's clamp does: scale 1 and
        // ideal == tight collapse to tight either way.
        // LILYPOND-REF: lily/spacing-loose-columns.cc:198-201 — <c>scale_factor = std::max
        //   (0.0, std::min (1.0, (permissible_distance - left_padding - sum_tight_spacing)
        //   / (sum_spacing - sum_tight_spacing)))</c>.
        double scale = ideal > tight
            ? Math.Max(0.0, Math.Min(1.0, (permissibleDistance - leftPadding - tight)
                                          / (ideal - tight)))
            : 1.0;

        // LILYPOND-REF: lily/spacing-loose-columns.cc:209-213 — <c>distance_to_next =
        //   clique_tight_spacing[j] + (clique_spacing[j] - clique_tight_spacing[j]) *
        //   scale_factor</c>, hung back from the right point.
        return tight + (ideal - tight) * scale;
    }

    /// <summary>
    /// The ideal headroom a same-moment (dt == 0) pair gets over its minimum in
    /// <c>standard_breakable_column_spacing</c> — the spring a loose column's clique edge
    /// is priced by, since a change column shares its note's moment.
    /// LILYPOND-REF: lily/spacing-basic.cc:71-77 — <c>ideal = min_dist + 0.5</c>.
    /// </summary>
    private const double LooseColumnZeroDtSpace = 0.5;

    /// <summary>
    /// Width that leading grace notes need in FRONT of their main note's column.
    /// Grace notes hang to the left of the note (like a mid-measure clef change),
    /// so the spring into the column reserves their group width. When several
    /// voices have grace at the same moment the groups align, so the MAX is taken.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/grace-spacing-engraver.cc:36-80 — grace columns precede
    ///   the main note's musical column; their span is reserved before it.
    /// The width equals <see cref="CalculateGraceGroupSpringWidth"/> (grace springs
    /// plus the grace→main rod), the same measure GraceNoteEngraver uses to PLACE
    /// the group, so reserved space and drawn space agree.
    /// </remarks>
    internal static double LeadingGracePrefixWidth(IEnumerable<MusicItem>? items,
        bool includeMainAccidental = false)
    {
        if (items == null) return 0;
        double w = 0;
        foreach (var item in items)
        {
            var grace = item switch
            {
                NoteItem n => n.LeadingGrace,
                ChordItem c => c.LeadingGrace,
                _ => ImmutableArray<GraceColumnInfo>.Empty
            };
            if (grace.IsDefaultOrEmpty)
                continue;
            double hang = CalculateGraceGroupSpringWidth(grace);
            // At a LINE START the grace hangs left of the main item's OWN left ink
            // (its accidental) with nothing before it, so the front spring must
            // reserve grace + accidental, not their max — otherwise the grace
            // overflows into the clef/key/time prefix. (Mid-line the previous note
            // already provides that room, so the accidental is left out there.)
            bool hasAccidental = item switch
            {
                NoteItem n => n.Accidental != null,
                ChordItem c => c.Notes.Any(cn => cn.Accidental != null),
                _ => false
            };
            if (includeMainAccidental && hasAccidental)
                hang += CalculateLeftExtent(item);
            w = Math.Max(w, hang);
        }
        return w;
    }

    /// <summary>
    /// The widest leading grace run's ANCHOR-TO-ANCHOR span among <paramref name="items"/> —
    /// first grace origin to main note origin, with no ink allowance.
    /// </summary>
    /// <remarks>
    /// The companion of <see cref="LeadingGracePrefixWidth"/>, which is the same runs
    /// measured WITH the leading ink. The two go to different halves of the spring — see
    /// <see cref="SpringIntoGraceRun"/> — so they are separate readings rather than one
    /// number with a fudge.
    /// </remarks>
    internal static double LeadingGraceRunSpan(IEnumerable<MusicItem>? items)
    {
        if (items == null) return 0;
        double w = 0;
        foreach (var item in items)
            w = Math.Max(w, LeadingGraceRunSpan(item));
        return w;
    }

    /// <summary>One item's leading grace run span, measured the way the run is PLACED.</summary>
    /// <remarks>
    /// ⚠️ The main item has to go in. <c>GraceColumns</c> answers a different span without
    /// it — 0.2 wider on the ledger's book, which is the first grace's own left ink — and
    /// GraceNoteEngraver places the run WITH it. Feeding the mainItem-less number to the
    /// ideal put that ink straight back into the approach the scaling had just taken out.
    /// </remarks>
    internal static double LeadingGraceRunSpan(MusicItem? item)
    {
        if (item == null) return 0;
        var grace = GraceNotesOf(item);
        return grace.IsDefaultOrEmpty ? 0 : GraceColumns(grace, item).Span;
    }

    /// <summary>
    /// The spring from a mid-line bar line to the first musical column after it —
    /// the SINGLE implementation shared by both spring systems.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A transcription of <c>Staff_spacing::get_spacing</c>. The gap after a bar line is
    /// governed by the BarLine space-alist, NOT by the first note's duration: LilyPond
    /// reaches this pair through Staff_spacing, not Note_spacing, so duration space never
    /// enters. A mid-line bar line always has <c>break_status_dir () == CENTER</c>, which
    /// selects `next-note` (semi-fixed-space 0.9) and never `first-note`; the system-start
    /// case is BreakAlignSpacing.FirstNoteSpring.
    /// </para>
    /// <para>
    /// FRAME: LilyPond measures column origin → column origin, so its <c>fixed</c> opens at
    /// <c>last_ext[RIGHT]</c> — the bar line's right edge expressed in the boundary column's
    /// frame, which is why a clef sitting before the bar line makes that term jump from 0.19
    /// to ~3.04. This spring starts AT the bar line's ink right edge, so that term is
    /// identically 0 here and every quantity below is LilyPond's minus <c>last_ext[RIGHT]</c>.
    /// Measured on 2.24.4, bar-line ink right edge → next notehead ink left edge is
    /// 0.900000 both with and without a clef change at the bar line.
    /// </para>
    /// <para>
    /// The optical correction for a DOWN stem just after the bar line is
    /// <see cref="BarlineToNextNotesCorrection"/>; the measured 2x2 that identifies it as a
    /// STEM effect rather than a clef one is recorded there.
    /// </para>
    /// <para>
    /// This lived only in MeasureLayouter, so the item system priced the same gap as
    /// a quarter note's duration space — 3.6 against the correct 0.9, ~2.7 ss too
    /// wide on every measure it estimated.
    /// </para>
    /// LILYPOND-REF: lily/staff-spacing.cc:118-221 Staff_spacing::get_spacing;
    ///   scm/define-grobs.scm:301 BarLine space-alist
    ///   (next-note . (semi-fixed-space . 0.9)).
    /// LILYPOND-REF: lily/spacing-spanner.cc:484-489 breakable_column_spacing —
    ///   full-measure-extra-space is `situational_space` on THIS spring, keyed on the
    ///   measure AFTER the bar line, so the caller decides and passes it in.
    /// </remarks>
    internal static Spring BarlineToFirstColumnSpring(
        IReadOnlyList<MusicItem>? firstItems, bool fillsMeasure)
    {
        // `last_grob` is the RIGHTMOST break-aligned grob in the boundary column, which is
        // the bar line only when nothing else opens the measure. A key or time change shares
        // that column, so IT owns the space-alist consulted here and `fixed` opens at its ink
        // right edge instead of the bar line's — COORDINATE_AUDIT.md §4.7.3.
        // LILYPOND-REF: lily/staff-spacing.cc:125-126
        //   Spacing_interface::extremal_break_aligned_grob (me, LEFT, ...).
        var boundary = BoundaryChangePrefix(firstItems);

        double distance;
        double fixedDistance;
        bool isStretchable;
        if (boundary is var (prefix, lastChange) && boundary.HasValue)
        {
            var def = ChangeItemSpaceDef(lastChange);
            distance = def.Distance;
            // fixed opens at last_ext[RIGHT] — in this spring's frame, the bar line's own
            // width is already behind us, so that is the prefix.
            // LILYPOND-REF: lily/staff-spacing.cc:166.
            fixedDistance = prefix + (def.SplitsFixed ? distance / 2 : 0);
            isStretchable = def.Stretchable;
        }
        else
        {
            distance = EngravingDefaults.BarLineToNextNoteSpace;
            // semi-fixed-space: fixed += d/2, ideal = fixed + d/2. `is_stretchable` stays
            // TRUE — only shrink-space and semi-shrink-space clear it, so the resulting
            // spring is NOT rigid. (LilySharp used to pass inverseStretchStrength 0 here on
            // the strength of a comment claiming semi-fixed was unstretchable; the source
            // says otherwise.)
            // LILYPOND-REF: lily/staff-spacing.cc:164-180.
            fixedDistance = distance / 2;
            isStretchable = true;
        }
        // Every arm involved puts the IDEAL at last_ext[RIGHT] + distance; they differ only
        // in what lands in `fixed`. LILYPOND-REF: lily/staff-spacing.cc:169-198.
        double ideal = (boundary?.Prefix ?? 0) + distance;

        // Fixed BEFORE situational_space and before the min-distance correction — the
        // order matters, both of those move `ideal` away from `fixed` without making the
        // spring any more stretchable.
        // LILYPOND-REF: lily/staff-spacing.cc:200.
        double stretchability = isStretchable ? ideal - fixedDistance : 0;

        // LILYPOND-REF: lily/staff-spacing.cc:202-204 — 'situational_space' passed by the
        //   caller could include full-measure-extra-space.
        double situationalSpace = fillsMeasure ? FullMeasureExtraSpace : 0;
        ideal += situationalSpace;

        // min_dist = Paper_column::minimum_distance — a PURE skyline distance between the
        // two columns, with no space-alist value in it. See GetBarlineToItemMinimum.
        // LILYPOND-REF: lily/staff-spacing.cc:210.
        double minDistance = 0;

        double startLeadGrace = 0;
        if (firstItems != null)
        {
            if (boundary is var (bPrefix, bLast) && boundary.HasValue)
            {
                // The boundary column reaches to the change's ink right edge plus ITS
                // extra-spacing-width (KeySignature declares (0.0 . 1.0), TimeSignature
                // (0.0 . 0.8) — not the default), and the musical column reaches back by its
                // leftmost ink plus that grob's own. This is the only term that carries an
                // opening accidental into the gap, and it is what decides probe K.
                double reach = 0;
                foreach (var item in firstItems)
                    if (!IsChangeItem(item))
                        reach = Math.Max(reach, MusicalColumnLeftReach(item));
                minDistance = bPrefix + ChangeItemExtraSpacingWidth(bLast).Right + reach;
            }
            else
            {
                // Skyline reach: bar line → first MUSICAL item (max across all voices). No
                // change grob belongs on this side of the bar line: a clef change is engraved
                // BEFORE it (break-align-orders puts clef before staff-bar), and a key or time
                // change stands in the boundary column, which is the branch above.
                // ⚠️ This used to skip ClefChangeItem ALONE, and that was exact only because a
                // key or time change here forced the other branch. A BLANKED meter does not
                // (SpacingRules.ChangeItemHasInk removes it from BoundaryChangePrefix), so it
                // arrived on this walk and was measured as if it were a note: +0.150000 on
                // ledger point mid-piece.tab-numbers.change-bar-vs-plain-bar, which is what
                // caught it. Asking IsChangeItem is a no-op for every book that reached here
                // before — the only change item that could was the clef.
                foreach (var item in firstItems)
                {
                    if (IsChangeItem(item))
                        continue;
                    minDistance = Math.Max(minDistance,
                        CalculateSkylineDistance(null, item, staffY: 0));
                }
            }

            // Leading grace notes on the first note hang left of its column, after
            // the bar line (LilyPond gives the grace its own column between the
            // bar line and the main note).
            startLeadGrace = LeadingGracePrefixWidth(firstItems, includeMainAccidental: true);
        }

        if (startLeadGrace > 0)
        {
            // The grace is now the FIRST musical column after the bar line, so the
            // barline→grace gap uses tight GRACE spacing (spacing-increment). The
            // whole front block is rigid (grace columns don't stretch), so this branch
            // does NOT take the semi-fixed spring above.
            // LILYPOND-REF: scm/define-grobs.scm:1721 GraceSpacing
            //   (spacing-increment . 0.8) — grace columns space tighter than notes.
            // LILYPOND-REF: lily/grace-spacing-engraver.cc — barline → first grace
            //   column → … → main column.
            double graceApproach = GraceSpacingParameters.Default.SpacingIncrement;
            double front = Math.Max(Math.Max(distance, minDistance),
                                    graceApproach + startLeadGrace);
            return new Spring(front + situationalSpace, front, inverseStretchStrength: 0);
        }

        // The optical correction for a DOWN stem standing just after the bar line, applied
        // to BOTH fixed and ideal — and AFTER stretchability was taken, so it widens the
        // gap without making the spring any more stretchable.
        // LILYPOND-REF: lily/staff-spacing.cc:206-208.
        double opticalCorrection = BarlineToNextNotesCorrection(firstItems);
        fixedDistance += opticalCorrection;
        ideal += opticalCorrection;

        // "Ensure that the 'fixed' distance will leave a gap of at least 0.3 ss."
        // LILYPOND-REF: lily/staff-spacing.cc:212-215.
        double minDistanceCorrection =
            Math.Max(0.0, StaffSpacingFixedHeadroom + minDistance - fixedDistance);
        fixedDistance += minDistanceCorrection;
        ideal = Math.Max(ideal, fixedDistance);

        // LILYPOND-REF: lily/staff-spacing.cc:217-220 — the compress strength is measured
        //   against `fixed`, not against the minimum, so it is NOT the Spring 3-argument
        //   constructor's default.
        // No ApplyMergeSpringsHeadroom call follows: breakable_column_spacing does hand this
        // wish on to merge_springs, but the correction just above already guarantees
        // ideal >= fixed >= 0.3 + min_distance, so the headroom is provably a no-op here.
        return new Spring(ideal, minDistance,
                          Math.Max(0.0, stretchability),
                          Math.Max(0.0, ideal - fixedDistance));
    }

    /// <summary>
    /// The ITEM spring system's share of a mid-measure change column, or null when this pair
    /// does not touch one. Its total across the pair matches the timing-column system's
    /// single spring, which is what keeps line-break width estimates honest.
    /// </summary>
    /// <param name="spacingItems">The measure's spacing items, in order.</param>
    /// <param name="leftIndex">Index of the LEFT item of the pair being sprung.</param>
    /// <param name="durationIdeal">The pair's plain duration ideal, used only when this is
    /// the note → change-column gap.</param>
    /// <remarks>
    /// The item system gives a change item its own slot, so it already has the two springs
    /// LilyPond has and can carry the split directly, where the timing-column system has to
    /// lump both into one (a change shares the next note's timing). The three cases are the
    /// column's LEFT gap, an internal gap between two changes sharing the column, and the
    /// remainder of the RIGHT gap from the last change to the note.
    /// <para>
    /// These come back rigid. The item system feeds width ESTIMATES
    /// (<see cref="CalculateMeasureIdealWidth"/>) and the break gate, where what matters is
    /// that the ideals sum to the same total the layout will produce; modelling how the two
    /// LilyPond springs share a stretch needs the real column (roadmap item 3).
    /// </para>
    /// </remarks>
    private static Spring? ChangeColumnItemSpring(
        IReadOnlyList<MusicItem> spacingItems, int leftIndex, double durationIdeal)
    {
        var left = spacingItems[leftIndex];
        var right = spacingItems[leftIndex + 1];
        bool leftIsChange = IsChangeItem(left);
        bool rightIsChange = IsChangeItem(right);
        if (!leftIsChange && !rightIsChange)
            return null;

        // change → change: the left one's own width plus their break-align gap.
        if (leftIsChange && rightIsChange)
            return Rigid(ChangeItemColumnWidth(left) + BetweenChangeItemsSpace(left, right));

        var columnItems = ChangeColumnAt(spacingItems, leftIsChange ? leftIndex : leftIndex + 1);

        // note → the column's origin.
        if (!leftIsChange)
        {
            var gaps = MidMeasureChangeGaps(columnItems, new[] { left }, durationIdeal);
            return gaps is { } g ? Rigid(g.LeftGap) : null;
        }

        // last change → the note: what is left of the right gap once the column's own
        // glyphs are subtracted, since the right gap is measured from the column ORIGIN.
        return Rigid(MidMeasureChangeRightGap(columnItems)
                     - MidMeasureChangeOffsetWithin(columnItems, left));

        static Spring Rigid(double d) => new(Math.Max(0, d), Math.Max(0, d), 0);
    }

    /// <summary>
    /// The change column containing <paramref name="index"/>: the whole run of changes it
    /// belongs to, plus the musical item that shares their moment.
    /// </summary>
    private static List<MusicItem> ChangeColumnAt(IReadOnlyList<MusicItem> items, int index)
    {
        int start = index;
        while (start > 0 && IsChangeItem(items[start - 1]))
            start--;

        var column = new List<MusicItem>();
        for (int k = start; k < items.Count; k++)
        {
            column.Add(items[k]);
            if (!IsChangeItem(items[k]))
                break;
        }
        return column;
    }

}
