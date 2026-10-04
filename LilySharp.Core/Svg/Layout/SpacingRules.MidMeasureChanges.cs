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
    /// <param name="LeftMinDistance">The left spring's minimum — Note_spacing's skyline <c>min_dist</c>.</param>
    /// <param name="RightMinDistance">The right spring's minimum — Staff_spacing's <c>Paper_column::minimum_distance</c>.</param>
    internal readonly record struct MidMeasureChangeSpacing(
        double LeftGap, double RightGap, double LeftMinDistance, double RightMinDistance)
    {
        /// <summary>Previous musical column → the next one, i.e. what one spring must span.</summary>
        public double TotalIdeal => LeftGap + RightGap;

        /// <summary>Minimum for the two together (the two springs' minimums, summed).</summary>
        public double MinDistance => LeftMinDistance + RightMinDistance;
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
    private static double ChangeItemColumnWidth(Rendering.ScoreTextMetrics fonts, MusicItem item) => item switch
    {
        ClefChangeItem cc => GetClefChangeWidth(cc.NewClef),
        KeySignatureChangeItem kc => GetKeySignatureChangeWidth(kc),
        TimeSignatureChangeItem tc => GetTimeSignatureChangeWidth(fonts, tc),
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
        item is not (TimeSignatureChangeItem { Blanked: true } or KeySignatureChangeItem { Blanked: true });

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
        Rendering.ScoreTextMetrics fonts, in ItemColumn firstItems)
    {
        if (firstItems.Count == 0)
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
            prefix += WidestChangeOfKind(fonts, firstItems, ChangeItemKind(item));
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
    private static bool IsFirstChangeOfItsKind(in ItemColumn columnItems, int index)
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
    private static double WidestChangeOfKind(Rendering.ScoreTextMetrics fonts, in ItemColumn columnItems, int kind)
    {
        double widest = 0;
        for (int q = 0; q < columnItems.Count; q++)
        {
            var item = columnItems[q];
            if (!IsChangeItem(item) || !ChangeItemHasInk(item) || ChangeItemKind(item) != kind)
                continue;
            double w = ChangeItemColumnWidth(fonts, item);
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
            // The key change's LAST grob owns the entry: a change to no accidentals ends on its
            // KeyCancellation, any other on its KeySignature.
            (KeySignatureChangeItem key, TimeSignatureChangeItem) => BreakAlignGap(
                KeyChangeGrobWidths(key).Signature > 0
                    ? BreakAlignSymbol.KeySignature
                    : BreakAlignSymbol.KeyCancellation,
                BreakAlignSymbol.TimeSignature),
            _ => 0
        };

    /// <summary>
    /// How far the leftmost ink of a MUSICAL column reaches left of that column's origin,
    /// including the grob's own <c>extra-spacing-width</c> — the right-hand term of
    /// <c>Paper_column::minimum_distance</c>.
    /// </summary>
    internal static double MusicalColumnLeftReach(MusicItem item) =>
        CalculateLeftExtent(item) + LeftmostGrobExtraSpacingWidth(item);

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
    /// These are the two springs' IDEALS and MINIMUMS. Their strengths and rods are
    /// <see cref="MidMeasureChangeSeries"/>'s, which puts both in the timing slot as one series
    /// spring (session 810: until then one spring carried the pair and the split was exact only
    /// at force 0). With a grace run the order decides the chain (session 811):
    /// <see cref="MidMeasureChangeBeforeGraceSeries"/> and <see cref="MidMeasureChangeAfterGraceSeries"/>.
    /// </para>
    /// </remarks>
    internal static MidMeasureChangeSpacing? MidMeasureChangeGaps(
        Rendering.ScoreTextMetrics fonts,
        in ItemColumn columnItems, in ItemColumn prevItems,
        double durationIdeal)
    {
        var (columnWidth, firstChange, lastChange) = MeasureChangeColumn(fonts, columnItems);
        if (firstChange == null)
            return null;

        // --- LEFT: note-spacing.cc:78-82 min_dist, then :105-107 ---
        double leftRod = ChangeColumnLeftMinDistance(fonts, columnItems, prevItems);
        double leftGap = Math.Max(durationIdeal - columnWidth,
                                  (durationIdeal + leftRod) / 2.0);
        // LILYPOND-REF: lily/note-spacing.cc:113 Note_spacing::get_spacing — set_ideal_distance (std::max (0.0, ideal)).
        // stem_dir_correction (:111) adds nothing toward a NonMusical column, and the ideal
        // handed in is :77's unclamped one (ApplyLeftHeadWidth), so the clamp lands here.
        leftGap = Math.Max(0.0, leftGap);
        // …and that wish goes through merge_springs on its own — the LEFT spring, before the
        // right one is added to it — which keeps the ideal at least 0.3 over the minimum.
        // LILYPOND-REF: lily/spacing-spanner.cc:380-393 musical_column_spacing — merge_springs is taken for any non-empty wish list, a single wish included.
        // LILYPOND-REF: lily/spring.cc:122 merge_springs — avg_distance = max (min_distance + 0.3, avg_distance).
        // ⚠️ MeasureLayouter applies the same headroom to the spring that holds BOTH gaps
        // (TotalIdeal against the summed minimums), where it never binds; until session 806
        // that was the only place, and a lone flagged eighth before a change — the one shape
        // whose :105 floor sits under min + 0.3 — stood 0.18 (clef) / 0.13 (key) short
        // (ledger midmeasure.*.flagged-eighth). The right gap has its own 0.3 (RightGap).
        leftGap = Math.Max(leftGap, leftRod + SpringHeadroom);

        // --- RIGHT: staff-spacing.cc:166-215 ---
        double rightRod = RightRod(fonts, columnItems, columnWidth, lastChange!);
        double rightGap = RightGap(columnWidth, lastChange!, rightRod);

        return new MidMeasureChangeSpacing(leftGap, rightGap, leftRod, rightRod);
    }

    /// <summary>
    /// The pair across a mid-measure change column as LilyPond springs it: the Note_spacing
    /// spring into the column and the Staff_spacing spring out of it, IN SERIES — one timing
    /// slot of Lily#'s holds both, and under one force each part keeps its own strengths, its
    /// own minimum and its own column rod.
    /// </summary>
    /// <param name="columnItems">Everything starting at this timing (the change items and the
    /// note(s) after them).</param>
    /// <param name="prevItems">Everything at the previous column.</param>
    /// <param name="gaps">This pair's <see cref="MidMeasureChangeGaps"/>.</param>
    /// <param name="noteSpring">The note spring the left wish was built on — the duration spring
    /// with the left head width (lily/note-spacing.cc:77 Note_spacing::get_spacing), whose strengths the wish keeps.</param>
    /// <remarks>
    /// <para>
    /// LEFT — lily/spacing-spanner.cc:322-393 musical_column_spacing: Note_spacing::get_spacing
    /// moves the ideal (:77, :103-108, :113) and puts the skyline <c>min_dist</c> under it (:83)
    /// through setters that keep the note spring's strengths (lily/spring.cc:131-153 Spring::set_ideal_distance, Spring::set_min_distance), and
    /// merge_springs lifts the ideal to min + 0.3 (lily/spring.cc:122 merge_springs). Both are
    /// <paramref name="gaps"/>' LeftGap and LeftMinDistance.
    /// </para>
    /// <para>
    /// RIGHT — lily/spacing-spanner.cc:478-536 breakable_column_spacing (dt == 0) builds it with
    /// Staff_spacing::get_spacing (<see cref="ChangeColumnStaffSpacing"/>).
    /// ⒝ ONE WISH: LilyPond merges one Staff_spacing per staff, each from its own staff's last
    /// break-aligned grob; Lily#'s column walk takes the widest grob of each kind across the
    /// staves (<see cref="MeasureChangeColumn"/>), as the force-0 gaps always have. A single
    /// wish's merge_springs changes nothing here: its ideal already stands at least 0.3 over
    /// its minimum (staff-spacing.cc:212-215).
    /// </para>
    /// <para>
    /// RODS — lily/spacing-spanner.cc:228-297 set_column_rods puts a rod on each adjacent pair,
    /// lily/separation-item.cc:47-68 set_distance: padding plus the left column's right skyline
    /// against the right column's left one, raised only when positive. They stand on the parts
    /// (<see cref="Spring.WithPartRod"/>). ⚠️ NOTHING IN THE SUITE OBSERVES THE RIGHT ONE:
    /// dropping it leaves every test green (session 810's poison no. 3) — it stands 0.1 over the
    /// right spring's own minimum, and no book compresses a change column's right gap that far.
    /// The left one is observed (ledger midmeasure.force.compress.key.prev-note-to-key).
    /// </para>
    /// <para>
    /// Until session 810 the two gaps rode ONE spring with the note spring's strengths and the
    /// renderer hung the glyph back by the force-0 right gap, exact only on a ragged line:
    /// MEASURED (2.26.0, audit/lp-geometry/probes/midmeasure-force.ly), a clef's right gap
    /// stretches with its line (3.146600 → 7.500316 on 100mm) and a key's does not, and on a
    /// 36mm line the key's left spring stops at its rod 1.504200 where the one spring drew the
    /// signature over the previous head (ledger midmeasure.force.*).
    /// </para>
    /// </remarks>
    internal static Spring MidMeasureChangeSeries(
        Rendering.ScoreTextMetrics fonts,
        in ItemColumn columnItems, in ItemColumn prevItems,
        in MidMeasureChangeSpacing gaps, Spring noteSpring)
    {
        var (columnWidth, _, lastChange) = MeasureChangeColumn(fonts, columnItems);
        var left = new Spring(gaps.LeftGap, gaps.LeftMinDistance,
                              noteSpring.InverseStretchStrength, noteSpring.InverseCompressStrength);
        var right = ChangeColumnStaffSpacing(columnWidth, lastChange!, gaps.RightMinDistance);
        var series = Spring.InSeries(ImmutableArray.Create(left, right),
                                     left.MinDistance + right.MinDistance);

        double leftRod = SeparationRodPadding + ChangeColumnLeftSeparation(fonts, columnItems, prevItems);
        if (leftRod > 0)
            series = series.WithPartRod(0, leftRod);
        double rightRod = SeparationRodPadding + RightSkylineDistance(fonts, columnItems, columnWidth, lastChange!);
        if (rightRod > 0)
            series = series.WithPartRod(1, rightRod);
        return series;
    }

    /// <summary>
    /// Whether a mid-measure change at <paramref name="timing"/> was written BEFORE a grace run
    /// of its own voice — <c>\clef bass \grace e16 f4</c> — so that its column stands at the
    /// grace's moment, in front of the grace columns.
    /// </summary>
    /// <remarks>
    /// LilyPond gives the change the moment it was engraved at: written before the grace that
    /// is the grace's moment (main part t, a negative grace part), written after it the main
    /// note's (t, 0). The grace items are measure items at the same onset, AFTER the change in
    /// the list whichever order was written (a grace's body is walked when its main note
    /// arrives), so the order is the change's own <see cref="MusicItem.WrittenAfterGrace"/> with
    /// a grace item after it — read off the measures, since the timing columns drop grace items
    /// (MeasureLayouter.BuildTimingColumns).
    /// LILYPOND-REF: lily/paper-column-engraver.cc:223-234 Paper_column_engraver::stop_translation_timestep — both columns of a timestep take its now_mom as "when".
    /// LILYPOND-REF: lily/spacing-spanner.cc:396-403 musical_column_spacing and :519-527 breakable_column_spacing — a spring INTO a column whose when_mom has a grace_part_ is scaled by 0.8.
    /// </remarks>
    internal static bool ChangeStandsBeforeGrace(IReadOnlyList<Measure> measures, Fraction timing)
    {
        for (int mi = 0; mi < measures.Count; mi++)
        {
            var onset = Fraction.Zero;
            bool sawChange = false;
            foreach (var item in measures[mi].Items)
            {
                if (onset > timing)
                    break;
                if (onset == timing)
                {
                    if (IsChangeItem(item) && !item.GraceTime && !item.WrittenAfterGrace)
                        sawChange = true;
                    else if (item.GraceTime && sawChange)
                        return true;
                }
                onset += item.Duration;
            }
        }
        return false;
    }

    /// <summary>
    /// The pair across a mid-measure change written BEFORE a grace run
    /// (<see cref="ChangeStandsBeforeGrace"/>): the Note_spacing spring into the change column,
    /// the Staff_spacing spring from it to the FIRST grace column — both scaled by 0.8, since
    /// both end at a column with a grace part — then the run's own springs to the main note,
    /// in series. Null when the run cannot take part (no gaps to stand on).
    /// </summary>
    /// <param name="graceItem">The main item whose leading run is the widest at this moment.</param>
    /// <remarks>
    /// LILYPOND-REF: lily/spacing-spanner.cc:396-403 musical_column_spacing — the left spring *= 0.8 (the change
    ///   column has a grace part, the previous column none).
    /// LILYPOND-REF: lily/spacing-spanner.cc:519-527 breakable_column_spacing — the right spring *= 0.8 (the grace
    ///   column has a grace part).
    /// LILYPOND-REF: lily/spring.cc:85-93 Spring::operator*= — ideal_distance_ = max (min_distance_, ideal × r), the compress
    ///   strength ideal - min, the stretch strength × r (<see cref="Spring.Scale"/>).
    /// The right spring's <c>min_dist</c> is Paper_column::minimum_distance to the grace column
    /// (its left skyline with its accidentals, <see cref="ItemSkylineFactory.CreateGraceLeftSkyline"/>),
    /// and the column rods stand on the same pairs (separation-item.cc:47-68). The run's springs
    /// are <see cref="GraceColumns"/>' gaps, the parts <see cref="SpringIntoGraceRun"/> puts after
    /// its approach — the approach itself is gone: the previous column meets the change column,
    /// not the grace one.
    /// MEASURED (2.26.0, Lab sessions/p808/gr/lp-probe.ly, ledger midmeasure.clef.*grace*): the
    /// previous note → clef 1.802578 = 2.253222 × 0.8, clef → grace 2.517280 = 3.146600 × 0.8, and
    /// with a sharp on the grace the ideal falls to its minimum and the column rod (+0.1) binds.
    /// ⒝ A key's box reaches the next column's height (<see cref="ChangeColumnBoxes"/>); here the
    /// next column is the grace column and the band read is the main note's.
    /// ⚠️ NOTHING OBSERVES THE LEFT COLUMN ROD HERE (session 811's poison no. 4 dropped it:
    /// every test green) — after the 0.8 the ideal still stands over it in every book measured.
    /// The right one is observed (ledger midmeasure.clef.clef-to-grace.grace-sharp).
    /// </remarks>
    internal static Spring? MidMeasureChangeBeforeGraceSeries(
        Rendering.ScoreTextMetrics fonts,
        in ItemColumn columnItems, in ItemColumn prevItems,
        in MidMeasureChangeSpacing gaps, Spring noteSpring, MusicItem graceItem)
    {
        var grace = GraceNotesOf(graceItem);
        if (grace.IsDefaultOrEmpty)
            return null;
        var run = GraceColumns(grace, graceItem);
        if (run.Offsets.IsDefaultOrEmpty || run.Gaps.IsDefaultOrEmpty)
            return null;

        var (columnWidth, _, lastChange) = MeasureChangeColumn(fonts, columnItems);
        var left = new Spring(gaps.LeftGap, gaps.LeftMinDistance,
                              noteSpring.InverseStretchStrength, noteSpring.InverseCompressStrength)
            .Scale(GraceApproachScale);
        double rightDistance = ChangeColumnRightSkyline(fonts, columnItems)
            .Distance(ItemSkylineFactory.CreateGraceLeftSkyline(grace[0]));
        var right = ChangeColumnStaffSpacing(columnWidth, lastChange!, Math.Max(0.0, rightDistance))
            .Scale(GraceApproachScale);

        double gapStretch = GraceSpringInverseStretch();
        var parts = new Spring[run.Offsets.Length + 2];
        parts[0] = left;
        parts[1] = right;
        double min = left.MinDistance + right.MinDistance;
        for (int k = 0; k < run.Offsets.Length; k++)
        {
            parts[k + 2] = new Spring(run.Gap(k), run.Gaps[k].Rod, gapStretch, run.Gaps[k].InverseCompress);
            min += parts[k + 2].MinDistance;
        }
        var series = Spring.InSeries(ImmutableArray.Create(parts), min);

        double leftRod = SeparationRodPadding + ChangeColumnLeftSeparation(fonts, columnItems, prevItems);
        if (leftRod > 0)
            series = series.WithPartRod(0, leftRod);
        double rightRod = SeparationRodPadding + rightDistance;
        if (rightRod > 0)
            series = series.WithPartRod(1, rightRod);
        return series;
    }

    /// <summary>
    /// Whether <paramref name="graceItem"/>'s run can end on a change column written after it —
    /// the run's last column is one <see cref="GraceIntoChangeColumn"/> prices.
    /// </summary>
    internal static bool CanStandAfterGraceRun(MusicItem graceItem)
    {
        var grace = GraceNotesOf(graceItem);
        return !grace.IsDefaultOrEmpty && !grace[^1].IsRest && grace[^1].TabDigitHalfWidth <= 0;
    }

    /// <summary>
    /// The slot across a mid-measure change written AFTER a grace run: the run's springs as
    /// they stand with no change (<paramref name="graceSeries"/>, the approach then one part per
    /// column) with the LAST one — last grace → main note — split into LilyPond's two: the last
    /// grace column → the change column (<see cref="GraceIntoChangeColumn"/>) and the change
    /// column → the main note (Staff_spacing, <see cref="ChangeColumnStaffSpacing"/>), neither
    /// scaled, each with its column rod. Null when <paramref name="graceSeries"/> is not that
    /// series.
    /// </summary>
    /// <remarks>
    /// The change column's moment is the main note's (t, 0) (<see cref="ChangeStandsBeforeGrace"/>),
    /// so it stands between the last grace column and the main note's; the approach keeps its
    /// 0.8 (its right column is the first grace's).
    /// LILYPOND-REF: lily/spacing-spanner.cc:322-393 musical_column_spacing — last grace → change, a Note_spacing wish.
    /// LILYPOND-REF: lily/spacing-spanner.cc:478-536 breakable_column_spacing — change → main, dt == 0, Staff_spacing; no 0.8 (:519-527 asks for a grace_part_ on the right column).
    /// MEASURED (2.26.0, Lab sessions/p808/gr/lp-probe.ly G2, ledger midmeasure.clef.grace-then-clef.*):
    /// the grace 2.201796 after the previous note, the clef 0.698969 after the grace — the
    /// :105 floor (1.397939 + 0) / 2, the grace below the treble staff and the clef's box apart
    /// in height — and the main note 3.146600 after the clef.
    /// ⚠️ NOTHING OBSERVES EITHER COLUMN ROD HERE (session 811's poisons no. 14 and 15 dropped
    /// them: every test green) — no book compresses this slot to its minimums.
    /// </remarks>
    internal static Spring? MidMeasureChangeAfterGraceSeries(
        Rendering.ScoreTextMetrics fonts, in ItemColumn columnItems, Spring graceSeries, MusicItem graceItem)
    {
        var grace = GraceNotesOf(graceItem);
        int n = grace.IsDefaultOrEmpty ? 0 : grace.Length;
        if (n == 0 || !graceSeries.IsSeries || graceSeries.Series.Length != n + 1)
            return null;
        var (columnWidth, _, lastChange) = MeasureChangeColumn(fonts, columnItems);
        if (lastChange == null
            || GraceIntoChangeColumn(grace, ChangeColumnLeftSkyline(fonts, columnItems, default), columnWidth)
               is not { } intoChange)
            return null;
        double rightDistance = RightSkylineDistance(fonts, columnItems, columnWidth, lastChange);
        var outOfChange = ChangeColumnStaffSpacing(columnWidth, lastChange, Math.Max(0.0, rightDistance));

        var parts = new Spring[n + 2];
        double min = 0;
        for (int k = 0; k < n; k++)
        {
            parts[k] = graceSeries.Series[k];
            min += parts[k].Length(double.NegativeInfinity);
        }
        parts[n] = intoChange.Spring;
        parts[n + 1] = outOfChange;
        min += intoChange.Spring.MinDistance + outOfChange.MinDistance;
        var series = Spring.InSeries(ImmutableArray.Create(parts), min);
        if (intoChange.Rod > 0)
            series = series.WithPartRod(n, intoChange.Rod);
        if (SeparationRodPadding + rightDistance > 0)
            series = series.WithPartRod(n + 1, SeparationRodPadding + rightDistance);
        return series;
    }

    /// <summary>The main item at this moment whose leading grace run is the widest — the run the
    /// slot's springs are built from (<see cref="LeadingGraceRun"/>'s choice). Null when none leads
    /// with a grace.</summary>
    internal static MusicItem? WidestLeadingGraceItem(in ItemColumn items)
    {
        MusicItem? widest = null;
        double span = 0;
        for (int i = 0; i < items.Count; i++)
        {
            double s = LeadingGraceRunSpan(items[i]);
            if (s > span)
            {
                span = s;
                widest = items[i];
            }
        }
        return widest;
    }

    /// <summary>
    /// The previous column's right skyline against the change column's left one — the distance
    /// <c>Separation_item::set_distance</c> pads into the left column rod. Negative infinity when
    /// no previous item stands in a column.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/separation-item.cc:47-68 Separation_item::set_distance — <c>lines[LEFT][RIGHT].distance (right)</c>
    ///   over the columns' horizontal-skylines, each padded by its own skyline-vertical-padding when it was
    ///   built (:105-108): the musical column's (<see cref="ItemSkylineFactory.SharedRightSkylineAtColumn"/>),
    ///   none for a NonMusicalPaperColumn (<see cref="NonMusicalColumnSkylineVerticalPadding"/>).
    /// The wish's skyline (<see cref="ChangeColumnLeftMinDistance"/>) is the other reading of the
    /// same pair: the note column alone, no dots. Pairs taken in one staff frame, as there.
    /// </remarks>
    private static double ChangeColumnLeftSeparation(
        Rendering.ScoreTextMetrics fonts, in ItemColumn columnItems, in ItemColumn prevItems)
    {
        var changeLeft = ChangeColumnLeftSkyline(fonts, columnItems, prevItems);
        double distance = double.NegativeInfinity;
        for (int q = 0; q < prevItems.Count; q++)
        {
            if (IsChangeItem(prevItems[q]))
                continue;
            var prevRight = ItemSkylineFactory.SharedRightSkylineAtColumn(prevItems[q], 0.0, 0.0);
            distance = Math.Max(distance, prevRight.Distance(changeLeft));
        }
        return distance;
    }

    /// <summary>
    /// <c>Staff_spacing::get_spacing</c> from a mid-measure change column to the musical column
    /// after it: the spring, with the strengths the space-alist entry gives it.
    /// </summary>
    /// <param name="columnWidth">The change column's extent right of its origin — <c>last_ext[RIGHT]</c>.</param>
    /// <param name="lastChange">The column's rightmost break-aligned grob, whose space-alist is read.</param>
    /// <param name="minDistance"><c>Paper_column::minimum_distance</c> (<see cref="RightRod"/>).</param>
    /// <remarks>
    /// LILYPOND-REF: lily/staff-spacing.cc:117-221 Staff_spacing::get_spacing —
    /// :166-198 fixed and ideal by the entry's type (<see cref="ChangeItemSpaceDef"/>);
    /// :200 stretchability, taken BEFORE the corrections; :204 situational_space, 0 off a bar
    /// line (only full-measure-extra-space feeds it, spacing-spanner.cc:484-489); :206-208 the
    /// optical correction, 0 here — next_notes_correction reads the last grob's bar extent and a
    /// change grob has none (:72-93 bar_y_positions); :212-215 the 0.3 floor on fixed;
    /// :217-219 the spring.
    /// </remarks>
    private static Spring ChangeColumnStaffSpacing(double columnWidth, MusicItem lastChange, double minDistance)
    {
        var (distance, splitsFixed, stretchable) = ChangeItemSpaceDef(lastChange);
        double fixedDistance = columnWidth;
        double ideal;
        if (splitsFixed)
        {
            fixedDistance += distance / 2;
            ideal = fixedDistance + distance / 2;
        }
        else
            ideal = fixedDistance + distance;

        double stretchability = stretchable ? ideal - fixedDistance : 0;

        double minDistanceCorrection = Math.Max(0.0, StaffSpacingFixedHeadroom + minDistance - fixedDistance);
        fixedDistance += minDistanceCorrection;
        ideal = Math.Max(ideal, fixedDistance);

        return new Spring(ideal, minDistance,
                          Math.Max(0.0, stretchability),
                          Math.Max(0.0, ideal - fixedDistance));
    }

    /// <summary>
    /// <c>Note_spacing</c>'s <c>min_dist</c> from the previous musical column to a change
    /// column: the SKYLINE distance between the previous note columns' right side and the
    /// change grobs' boxes, clamped at 0 — the term the left gap's floor
    /// <c>(ideal + min_dist) / 2</c> and the pair's minimum are built from.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/note-spacing.cc:78-82 Note_spacing::get_spacing — <c>skys[LEFT].distance
    ///   (skys[RIGHT], skyline-vertical-padding of right_col)</c>, max'd with 0. The left skyline
    ///   is the voice's NoteColumn (Spacing_interface::skylines over the wish's left-items: heads,
    ///   stem, flag — no dots, which are the paper column's), carrying NoteColumn's own 0.15
    ///   padding (<see cref="ItemSkylineFactory.SharedWishRightSkylineAtColumn"/>); the right
    ///   column is a NonMusicalPaperColumn, which declares no skyline-vertical-padding, so the
    ///   distance is taken with 0 (<see cref="NonMusicalColumnSkylineVerticalPadding"/>).
    /// <para>
    /// ⚠️ UNTIL SESSION 805 THIS WAS A BOX — the previous items' horizontal ink reach
    /// (<see cref="CalculateNoteheadRightExtent"/>: the head, its dots, no stem or flag) plus the
    /// two extra-spacing-widths, whatever their heights — under a comment that called it "the
    /// pure skyline distance". The two agree when the previous column's ink shares a height
    /// with the change's box, which is every key and meter change (their boxes grow to the
    /// neighbours' heights) and a clef after a note whose up-stem stands in the clef's band. A
    /// note ABOVE a mid-line clef, stem down at its left, is where they part: LilyPond 1.666122
    /// against the box's 2.253222 (ledger midmeasure.clef.prev-note-to-clef.head-above-clef,
    /// probe barline-spacing.ly MCH). A dotted note (the dots leave the reach) and a flagged
    /// one (the flag enters it) move too.
    /// </para>
    /// <para>
    /// ⚠️ THE PAIRS ARE TAKEN IN ONE STAFF FRAME. <paramref name="prevItems"/> may hold several
    /// staves' items and the column several staves' changes, and a LilyPond wish pairs a voice
    /// only with its own staff's separation item; here every previous item meets every change
    /// box at the same height. That can only find MORE overlap than LilyPond does — the answer
    /// lies between LilyPond's and the old box, never below LilyPond's — and the one caller
    /// that knows the own-staff neighbour (the loose column) already passes just it.
    /// </para>
    /// </remarks>
    private static double ChangeColumnLeftMinDistance(
        Rendering.ScoreTextMetrics fonts, in ItemColumn columnItems, in ItemColumn prevItems)
    {
        var changeLeft = ChangeColumnLeftSkyline(fonts, columnItems, prevItems);
        double min = 0.0;
        for (int q = 0; q < prevItems.Count; q++)
        {
            if (IsChangeItem(prevItems[q]))
                continue;
            var prevRight = ItemSkylineFactory.SharedWishRightSkylineAtColumn(prevItems[q], 0.0, 0.0);
            min = Math.Max(min, prevRight.Distance(changeLeft, NonMusicalColumnSkylineVerticalPadding));
        }
        return min;
    }

    /// <summary>
    /// The skyline-vertical-padding a NON-musical column's distance is taken with: none.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm NonMusicalPaperColumn — declares no skyline-vertical-padding,
    ///   so lily/note-spacing.cc:80-81 Note_spacing::get_spacing reads its default 0.0.
    /// </remarks>
    internal const double NonMusicalColumnSkylineVerticalPadding = 0.0;

    /// <summary>
    /// The LEFT skyline of a mid-measure change column: one box per break-aligned grob, its X
    /// extent widened by its <c>extra-spacing-width</c> and its Y extent by its
    /// <c>extra-spacing-height</c>, in the frame the previous column's wish skyline is built in
    /// (x from the change column's origin, y down, the staff's middle line at 0).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/separation-item.cc:150-187 Separation_item::boxes — Box (extent X + extra-spacing-width, pure Y extent + extra-spacing-height).
    /// The heights are where the three kinds part:
    /// <list type="bullet">
    /// <item>A CLEF is its "_change" glyph where it stands (<see cref="GlyphMetrics.ClefChangeBBox"/>
    ///   on <see cref="Rendering.SharedRenderer.ClefLineBelowTopLine"/>), widened by ±0.1 —
    ///   LILYPOND-REF: scm/output-lib.scm:929-932 pure-from-neighbor-interface::extra-spacing-height-at-beginning-of-line,
    ///   whose mid-line arm is <c>(cons -0.1 0.1)</c>.</item>
    /// <item>A KEY (cancellation or signature) and a METER reach the staff and every
    ///   neighbour's height — LILYPOND-REF: scm/output-lib.scm:976-979 pure-from-neighbor-interface::extra-spacing-height-including-staff
    ///   (define-grobs.scm:1934-1935, :1980-1981, :3931): the union of the grob's height, the
    ///   staff's and the pure heights of the columns beside it. So their boxes span the staff
    ///   and the previous and next columns' heights here
    ///   (<see cref="ItemSkylineFactory.ColumnYExtent"/>), and the distance to them is the
    ///   previous column's whole reach, as the old box had it.</item>
    /// </list>
    /// X follows the column walk (<see cref="MeasureChangeColumn"/>: one grob per kind, the
    /// widest, in break-align order); a key is one box for its cancellation and its signature
    /// together — the two share the neighbour-wide height, so splitting them changes nothing
    /// the distance can see.
    /// </remarks>
    private static HorizontalSkyline ChangeColumnLeftSkyline(
        Rendering.ScoreTextMetrics fonts, in ItemColumn columnItems, in ItemColumn prevItems)
        => HorizontalSkyline.FromBoxes(ChangeColumnBoxes(fonts, columnItems, prevItems), HorizontalDirection.Left);

    /// <summary>
    /// The RIGHT skyline of a mid-measure change column: the same grob boxes as
    /// <see cref="ChangeColumnLeftSkyline"/>, facing the next musical column — the left column's
    /// side of <c>Paper_column::minimum_distance</c> (<see cref="RightRod"/>).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/separation-item.cc:89-110 Separation_item::calc_skylines — one Skyline_pair over the same boxes, a NonMusicalPaperColumn unpadded.
    /// The neighbour band of a key or meter box is read from THIS column's heights and the
    /// staff's only; LilyPond's also takes the previous column's. That part of the box can
    /// only stand where the next column has no ink — its heights are already in the band —
    /// so the distance to the next column cannot see it, and the renderer's caller
    /// (<see cref="MidMeasureChangeRightGap"/>), which has no previous column, gets the same
    /// answer as the spring's.
    /// </remarks>
    private static HorizontalSkyline ChangeColumnRightSkyline(
        Rendering.ScoreTextMetrics fonts, in ItemColumn columnItems)
        => HorizontalSkyline.FromBoxes(ChangeColumnBoxes(fonts, columnItems, default), HorizontalDirection.Right);

    /// <summary>
    /// The spacing boxes of a mid-measure change column's grobs — what its two skylines
    /// (<see cref="ChangeColumnLeftSkyline"/>, <see cref="ChangeColumnRightSkyline"/>) are built from.
    /// </summary>
    private static List<(double YBottom, double YTop, double XLeft, double XRight)> ChangeColumnBoxes(
        Rendering.ScoreTextMetrics fonts, in ItemColumn columnItems, in ItemColumn prevItems)
    {
        // The neighbour-wide band a key or meter box spans: the staff (lines at ±2 about the
        // middle, y down) and the pure heights of the columns on either side.
        double bandTop = -2.0, bandBottom = 2.0;
        void Widen(in ItemColumn column)
        {
            for (int q = 0; q < column.Count; q++)
            {
                if (IsChangeItem(column[q]))
                    continue;
                var (yMin, yMax) = ItemSkylineFactory.ColumnYExtent(column[q], 0.0);
                bandTop = Math.Min(bandTop, yMin);
                bandBottom = Math.Max(bandBottom, yMax);
            }
        }
        Widen(prevItems);
        Widen(columnItems);

        var boxes = new List<(double YBottom, double YTop, double XLeft, double XRight)>();
        double offset = 0;
        MusicItem? last = null;
        for (int i = 0; i < columnItems.Count; i++)
        {
            var item = columnItems[i];
            if (!IsChangeItem(item) || !ChangeItemHasInk(item) || !IsFirstChangeOfItsKind(columnItems, i))
                continue;
            if (last != null)
                offset += BetweenChangeItemsSpace(last, item);
            double width = WidestChangeOfKind(fonts, columnItems, ChangeItemKind(item));
            var (eswLeft, eswRight) = ChangeItemExtraSpacingWidth(item);
            double xLeft = offset - eswLeft, xRight = offset + width + eswRight;
            if (item is ClefChangeItem)
            {
                // Every staff's clef of this column, each at its own line (one frame: see
                // ChangeColumnLeftMinDistance).
                for (int j = 0; j < columnItems.Count; j++)
                {
                    if (columnItems[j] is not ClefChangeItem clef)
                        continue;
                    var b = GlyphMetrics.ClefChangeBBox(clef.NewClef);
                    double lineUp = 2.0 - Rendering.SharedRenderer.ClefLineBelowTopLine(clef.NewClef);
                    boxes.Add((-(lineUp + b.Top) - ClefMidLineExtraSpacingHeight,
                               -(lineUp + b.Bottom) + ClefMidLineExtraSpacingHeight,
                               xLeft, xRight));
                }
            }
            else
            {
                boxes.Add((bandTop, bandBottom, xLeft, xRight));
            }
            offset += width;
            last = item;
        }
        return boxes;
    }

    /// <summary>A mid-line clef's extra-spacing-height, each way.</summary>
    /// <remarks>LILYPOND-REF: scm/output-lib.scm:929-932 pure-from-neighbor-interface::extra-spacing-height-at-beginning-of-line — <c>(cons -0.1 0.1)</c> off the line start.</remarks>
    private const double ClefMidLineExtraSpacingHeight = 0.1;

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
    internal static double MidMeasureChangeRightGap(Rendering.ScoreTextMetrics fonts, in ItemColumn columnItems)
    {
        var (columnWidth, first, last) = MeasureChangeColumn(fonts, columnItems);
        if (first == null)
            return 0;
        return RightGap(columnWidth, last!, RightRod(fonts, columnItems, columnWidth, last!));
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
    internal static double ChangeColumnGlyphAdvance(Rendering.ScoreTextMetrics fonts, MusicItem change, MusicItem? next) =>
        !ChangeItemHasInk(change)
            ? 0
            : ChangeItemColumnWidth(fonts, change)
              + (next != null && ChangeItemHasInk(next)
                  ? BetweenChangeItemsSpace(change, next) : 0);

    /// <summary>
    /// The ink left of the time signature a mid-line measure OPENS with, from the measure's
    /// X — where the renderer draws it (SharedRenderer.Noteheads' opening-change arm: the
    /// opening bar line's ink, the bar line's space-alist distance to the first change, then
    /// each change's advance) — or null when the measure opens with no meter change.
    /// </summary>
    /// <remarks>
    /// The metronome mark at such a bar aligns its left on this ink
    /// (MusicMarkEngraver.CalculateXPosition) and is held inside the line from it
    /// (MultiStaffLayouter.ColumnOverhangs). ⚠️ The renderer keeps its own walk (it threads a
    /// clef opening the bar, which hangs before the bar line and takes no part here); the
    /// ledger point tempo.x.mid-line-meter-change holds the two to one number — it reads the
    /// mark this places against the meter the renderer draws, EXACT at 0.
    /// </remarks>
    internal static double? OpeningTimeChangeInkLeft(Rendering.ScoreTextMetrics fonts, Measure measure)
    {
        double x = double.NaN;
        var items = measure.Items;
        for (int k = 0; k < items.Length; k++)
        {
            var item = items[k];
            if (item.Duration != Fraction.Zero)
                return null;
            if (item is not (KeySignatureChangeItem or TimeSignatureChangeItem))
                continue;
            if (double.IsNaN(x))
                x = (measure.StartBarline != BarlineType.None
                        ? EngravingDefaults.BarlineDrawnWidth(measure.StartBarline) : 0)
                    + GetBarlineToItemSpace(item);
            if (item is TimeSignatureChangeItem)
                return x;
            var next = k + 1 < items.Length
                       && items[k + 1] is ClefChangeItem or KeySignatureChangeItem or TimeSignatureChangeItem
                ? items[k + 1] : null;
            x += ChangeColumnGlyphAdvance(fonts, item, next);
        }
        return null;
    }

    /// <summary>
    /// Where <paramref name="change"/> sits inside its change column, measured from the
    /// column's origin. Zero for the first change; later ones follow their predecessors'
    /// widths and the break-align gap between them.
    /// </summary>
    internal static double MidMeasureChangeOffsetWithin(
        Rendering.ScoreTextMetrics fonts, in ItemColumn columnItems, MusicItem change)
    {
        if (columnItems.Count == 0)
            return 0;

        double offset = 0;
        MusicItem? previous = null;
        for (int q = 0; q < columnItems.Count; q++)
        {
            var item = columnItems[q];
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
            offset += ChangeItemColumnWidth(fonts, item);
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
        Rendering.ScoreTextMetrics fonts, in ItemColumn columnItems)
    {
        if (columnItems.Count == 0)
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
            width += WidestChangeOfKind(fonts, columnItems, ChangeItemKind(item));
        }
        return (width, first, last);
    }

    /// <summary>
    /// <c>Paper_column::minimum_distance</c> from the change column to the musical one: the
    /// SKYLINE distance between the change column's right side (its grobs' spacing boxes,
    /// <see cref="ChangeColumnRightSkyline"/>) and the musical column's left side with its
    /// accidentals merged in, clamped at 0.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/staff-spacing.cc:210 Staff_spacing::get_spacing — <c>min_dist = Paper_column::minimum_distance (left_col, right_col)</c>.
    /// LILYPOND-REF: lily/paper-column.cc:145-164 Paper_column::minimum_distance — the left
    ///   column's right skyline, the right column's left one merged with its
    ///   <c>conditional_skyline</c>, <c>max (0.0, distance)</c>. The musical column's view is
    ///   <see cref="ItemSkylineFactory.SharedLeftSkylineAtColumn"/> (PaperColumn's 0.08 padding,
    ///   the accidentals unpadded) — the one <see cref="BarlineToColumnMinimum"/> reads for the
    ///   same LilyPond function at a bar line.
    /// <para>
    /// ⚠️ UNTIL SESSION 807 THIS WAS A BOX — the column's width, its last grob's right
    /// extra-spacing-width and the next column's whole leftward reach
    /// (<see cref="MusicalColumnLeftReach"/>), whatever their heights. The two agree after a key
    /// or meter change (their boxes grow to the neighbours' heights, so every height of the next
    /// column meets them — ledger midmeasure.key.key-to-next-note.flat-below-staff) and after a
    /// clef whose band the next column's leftmost ink shares. A sharp BELOW a mid-line bass clef
    /// tucks under it in LilyPond and the space-alist ideal binds (3.146600), where the box
    /// charged the sharp and :213's 0.3 + rod stood the note 1.05 further right
    /// (ledger midmeasure.clef.clef-to-next-note.sharp-below-clef, probe barline-spacing.ly MCA).
    /// </para>
    /// <para>
    /// A non-musical item at the column's moment (a spacer, a grace) keeps the X-only reach it
    /// had: it is not a paper column's skyline — the same split
    /// <see cref="BarlineToColumnMinimum"/> makes. ⚠️ NOTHING IN THE SUITE OBSERVES THAT ARM: dropping
    /// it leaves every test green (session 807's poison no. 3) — whether
    /// LilyPond's grace column would agree is unmeasured. ⚠️ THE PAIRS ARE TAKEN IN ONE STAFF FRAME, as
    /// on the left side (<see cref="ChangeColumnLeftMinDistance"/>): every staff's change box
    /// meets every staff's next item at the same height, which can only find more overlap than
    /// LilyPond does — the answer lies between LilyPond's and the old box.
    /// </para>
    /// </remarks>
    private static double RightRod(
        Rendering.ScoreTextMetrics fonts, in ItemColumn columnItems, double columnWidth, MusicItem lastChange)
        => Math.Max(0.0, RightSkylineDistance(fonts, columnItems, columnWidth, lastChange));

    /// <summary>
    /// <see cref="RightRod"/> before its clamp at 0 — the distance <c>Separation_item::set_distance</c>
    /// pads into the column rod (lily/separation-item.cc:47-68 Separation_item::set_distance: the rod is raised when padding
    /// plus this is positive, so a negative distance still counts). Negative infinity when only
    /// change items stand at this moment.
    /// </summary>
    private static double RightSkylineDistance(
        Rendering.ScoreTextMetrics fonts, in ItemColumn columnItems, double columnWidth, MusicItem lastChange)
    {
        double rod = double.NegativeInfinity;
        HorizontalSkyline? changeRight = null;
        for (int q = 0; q < columnItems.Count; q++)
        {
            var item = columnItems[q];
            if (IsChangeItem(item))
                continue;
            if (!IsMusicalColumn(item))
            {
                rod = Math.Max(rod, columnWidth + ChangeItemExtraSpacingWidth(lastChange).Right
                                    + MusicalColumnLeftReach(item));
                continue;
            }
            changeRight ??= ChangeColumnRightSkyline(fonts, columnItems);
            var nextLeft = ItemSkylineFactory.SharedLeftSkylineAtColumn(item, 0.0, staffY: 0.0);
            rod = Math.Max(rod, changeRight.Distance(nextLeft));
        }
        return rod;
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
    /// The ideal of <see cref="ChangeColumnStaffSpacing"/>, the one implementation.
    /// </remarks>
    private static double RightGap(double columnWidth, MusicItem lastChange, double rightRod) =>
        ChangeColumnStaffSpacing(columnWidth, lastChange, rightRod).IdealDistance;

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
        IReadOnlyList<Measure> measures, in ItemColumn columnItems)
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

        static bool ContainsByReference(in ItemColumn items, MusicItem item)
        {
            for (int q = 0; q < items.Count; q++)
                if (ReferenceEquals(items[q], item))
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
        IReadOnlyList<Model.Measure> measures, in ItemColumn columnItems)
    {
        foreach (var m in measures)
        {
            MusicItem? lastMusical = null;
            foreach (var item in m.Items)
            {
                if (IsChangeItem(item))
                {
                    for (int q = 0; q < columnItems.Count; q++)
                        if (ReferenceEquals(columnItems[q], item))
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
        Rendering.ScoreTextMetrics fonts,
        IReadOnlyList<Fraction> allTimings, Fraction? ownLeftNeighborTiming,
        Fraction changeTiming, in ItemColumn columnItems)
    {
        if (ownLeftNeighborTiming is not { } left)
            return false;

        var (columnWidth, first, _) = MeasureChangeColumn(fonts, columnItems);
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
        Rendering.ScoreTextMetrics fonts,
        ItemColumn columnItems, double permissibleDistance)
    {
        var (columnWidth, first, last) = MeasureChangeColumn(fonts, columnItems);
        if (first == null)
            return 0;

        double minDist = RightRod(fonts, columnItems, columnWidth, last!);
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
    internal static double LeadingGracePrefixWidth(ItemColumn items,
        bool includeMainAccidental = false)
    {
        double w = 0;
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
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
    /// The widest leading grace run among <paramref name="items"/>, its columns as PLACED —
    /// its span is the ANCHOR-TO-ANCHOR width, first grace origin to main note origin, with
    /// no ink allowance; empty when no item leads with a grace.
    /// </summary>
    /// <remarks>
    /// The companion of <see cref="LeadingGracePrefixWidth"/>, which is the same runs
    /// measured WITH the leading ink. The two go to different halves of the spring — see
    /// <see cref="SpringIntoGraceRun"/> — so they are separate readings rather than one
    /// number with a fudge.
    /// </remarks>
    internal static GraceColumnLayout LeadingGraceRun(ItemColumn items)
    {
        var widest = new GraceColumnLayout(ImmutableArray<double>.Empty, 0);
        for (int i = 0; i < items.Count; i++)
        {
            var grace = GraceNotesOf(items[i]);
            if (grace.IsDefaultOrEmpty)
                continue;
            var run = GraceColumns(grace, items[i]);
            if (run.Span > widest.Span)
                widest = run;
        }
        return widest;
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
        Rendering.ScoreTextMetrics fonts, ItemColumn firstItems, bool fillsMeasure,
        IReadOnlyList<IReadOnlyList<MusicItem>>? staffFirstItems = null,
        BarlineType leftBound = BarlineType.Single,
        ReadOnlySpan<double> opticalByStaff = default)
    {
        // `last_grob` is the RIGHTMOST break-aligned grob in the boundary column, which is
        // the bar line only when nothing else opens the measure. A key or time change shares
        // that column, so IT owns the space-alist consulted here and `fixed` opens at its ink
        // right edge instead of the bar line's — COORDINATE_AUDIT.md §4.7.3.
        // LILYPOND-REF: lily/staff-spacing.cc:125-126
        //   Spacing_interface::extremal_break_aligned_grob (me, LEFT, ...).
        var boundary = BoundaryChangePrefix(fonts, firstItems);

        // LILYPOND-REF: lily/staff-spacing.cc:202-204 — 'situational_space' passed by the
        //   caller could include full-measure-extra-space.
        double situationalSpace = fillsMeasure ? FullMeasureExtraSpace : 0;

        // min_dist = Paper_column::minimum_distance — a PURE skyline distance between the
        // two columns, with no space-alist value in it. See GetBarlineToItemMinimum.
        // LILYPOND-REF: lily/staff-spacing.cc:210.
        double minDistance = 0;

        double startLeadGrace = 0;
        if (firstItems.Count > 0)
        {
            if (boundary is var (bPrefix, bLast) && boundary.HasValue)
            {
                // The boundary column reaches to the change's ink right edge plus ITS
                // extra-spacing-width (KeySignature declares (0.0 . 1.0), TimeSignature
                // (0.0 . 0.8) — not the default), and the musical column reaches back by its
                // leftmost ink plus that grob's own. This is the only term that carries an
                // opening accidental into the gap, and it is what decides probe K.
                // Along X alone, unlike the bare bar line's BarlineToColumnMinimum: the change's
                // box reaches the full height of the columns beside it, uncapped, so every part
                // of the note column meets it (that method's remarks carry the measurement).
                // LILYPOND-REF: scm/output-lib.scm:976-979 pure-from-neighbor-interface::extra-spacing-height-including-staff
                double reach = 0;
                for (int i = 0; i < firstItems.Count; i++)
                    if (!IsChangeItem(firstItems[i]))
                        reach = Math.Max(reach, MusicalColumnLeftReach(firstItems[i]));
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
                // The interface is indexed rather than walked: foreach over an
                // IReadOnlyList boxes its enumerator on every bar line (RULES §5.3).
                for (int i = 0; i < firstItems.Count; i++)
                {
                    var item = firstItems[i];
                    if (IsChangeItem(item))
                        continue;
                    minDistance = Math.Max(minDistance,
                        CalculateSkylineDistance(fonts, null, item, staffY: 0));
                }
            }

            // Leading grace notes on the first note hang left of its column, after
            // the bar line (LilyPond gives the grace its own column between the
            // bar line and the main note).
            startLeadGrace = LeadingGracePrefixWidth(
                firstItems, includeMainAccidental: true);
        }

        // ONE Staff_spacing WISH PER STAFF, merged. The left column's spacing-wishes hold a
        // Staff_spacing grob for every staff, each spring built from ITS OWN last break-aligned
        // grob and ITS OWN note columns, against the column pair's one min_dist; the ideals are
        // then averaged (merge_springs). A staff with no key or time change opening the bar ends
        // on its bar line, so it pulls the average below the changed staff's ideal — and a staff
        // with no item at all here (a spacer) still wishes off its bar line.
        // LILYPOND-REF: lily/spacing-spanner.cc:478-536 Spacing_spanner::breakable_column_spacing — one Staff_spacing wish per staff
        // LILYPOND-REF: lily/spring.cc:104-129 merge_springs — ideals averaged, the largest minimum
        // MEASURED (2.26.0, scratch/p390/ks, key-signature-space's key change on the upper staff,
        // first note off the bar line's ink right / ly:paper-column::print ideal): the lower
        // staff resting r1 or s1 12.22 / 12.90, the key change written on BOTH staves 12.77 /
        // 13.45 — the one-staff value. The page drew 12.77 in all three.
        // A loop, not Select: the lambda made the environment Wish shares a class built on every
        // bar line, one staff or many — 556 B a keystroke over the reader's corpus, plus its
        // delegate 243 (session 470's allocation-tick price by type).
        Spring spring;
        if (staffFirstItems is { Count: > 1 })
        {
            var wishes = new List<Spring>(staffFirstItems.Count);
            for (int s = 0; s < staffFirstItems.Count; s++)
            {
                wishes.Add(Wish(BoundaryChangePrefix(fonts, new ItemColumn(staffFirstItems[s])),
                    s < opticalByStaff.Length ? opticalByStaff[s] : null));
            }
            spring = Spring.MergeSprings(wishes);
        }
        else
            spring = Wish(boundary, opticalByStaff.Length > 0 ? opticalByStaff[0] : null);

        // A GRACE RUN OPENING THE BAR: the merged spring stops at the grace column, and when that
        // column has a grace part LilyPond scales the whole spring by 0.8 — column origin to
        // column origin, so the bar line's own width is inside what is scaled. Lily# hangs the run
        // off the main column, so the approach is scaled and the run added exactly as mid-bar
        // (AdjustSpringForGraceNotes), in the column frame: shifted out by the bar line's width
        // and back. This replaces a rigid GraceSpacing spacing-increment (0.8) taken as the gap.
        // LILYPOND-REF: lily/spacing-spanner.cc:519-527 Spacing_spanner::breakable_column_spacing — spring *= 0.8 on a grace_part_ right column
        // MEASURED (2.26.0, scratch/p390/kg kg1.ly, `\grace d''16 c''4` opening a bar): the grace
        // head 0.682 off the bar line's ink right = 0.8 x (0.19 + 0.9) - 0.19, the main note 2.6207
        // (the run's 1.9386 after it, ledger grace.column.single.to-main); the page drew 0.80 / 2.74.
        // Not measured here: a clef before the bar line (it moves the column origin too), an
        // accidental on the main note, grace runs on several staves (the widest run is taken).
        if (startLeadGrace > 0 && firstItems.Count > 0)
        {
            double origin = EngravingDefaults.BarlineDrawnWidth(leftBound);
            var inColumnFrame = new Spring(spring.IdealDistance + origin, spring.MinDistance + origin,
                spring.InverseStretchStrength, spring.InverseCompressStrength);
            Spring? widest = null;
            for (int q = 0; q < firstItems.Count; q++)
            {
                var item = firstItems[q];
                var grace = item switch
                {
                    NoteItem n => n.LeadingGrace,
                    ChordItem c => c.LeadingGrace,
                    _ => ImmutableArray<GraceColumnInfo>.Empty,
                };
                if (grace.IsDefaultOrEmpty)
                    continue;
                var run = AdjustSpringForGraceNotes(inColumnFrame, grace, GraceSpacingParameters.Default, item);
                if (widest == null || run.IdealDistance > widest.IdealDistance)
                    widest = run;
            }
            // Back out of the column frame through the approach, so a series spring keeps its
            // run's parts (Spring.Series).
            if (widest != null)
                spring = widest.WithIdealDistance(widest.IdealDistance - origin)
                    .WithMinDistance(Math.Max(0.0, widest.MinDistance - origin));
        }

        // The column ROD over this pair: set_column_rods walks every adjacent column pair,
        // the breakable ones included, and the rod is the spanner's padding over the SAME
        // skyline distance min_dist is. The compress strength above stays measured against
        // `fixed` (LilyPond's), so only the blocking point moves. MEASURED (2.26.0,
        // scratch/p323/fx/m-base.ly compressed to its minimum): bar line origin → next note
        // column 0.490000 = 0.19 ink + 0.1 + 0.1 + 0.1, where min_dist alone is 0.2 past
        // the ink; until 2026-09-03 this spring stopped at 0.2.
        // LILYPOND-REF: lily/spacing-spanner.cc:315-316 generate_springs;
        // LILYPOND-REF: lily/spacing-spanner.cc:228-297 set_column_rods;
        // LILYPOND-REF: lily/separation-item.cc:47-68 set_distance.
        return firstItems.Count == 0
            ? spring
            : spring.EnsureMinDistance(minDistance + SeparationRodPadding);

        // One staff's Staff_spacing::get_spacing, against the column pair's min_dist.
        // LILYPOND-REF: lily/staff-spacing.cc:118-221 Staff_spacing::get_spacing
        // ⚠️ A STAFF'S WISH TAKES ONLY ITS OWN LAST BREAK-ALIGNED GROB, not its items: the one
        // term that reads note columns — the down-stem correction below — reads the WHOLE
        // column by LilyPond's design (right-items is the musical PaperColumn). It took the
        // staff's column until session 485 and never read it: session 470's poison handing it
        // the whole column instead was an identity, green over the suite and the corpus.
        Spring Wish((double Prefix, MusicItem LastChange)? own, double? staffOptical)
        {
            var (distance, fixedDistance, isStretchable) = SpaceFrom(own);
            // Every arm involved puts the IDEAL at last_ext[RIGHT] + distance; they differ only
            // in what lands in `fixed`. LILYPOND-REF: lily/staff-spacing.cc:169-198.
            double ideal = (own?.Prefix ?? 0) + distance;

            // Fixed BEFORE situational_space and before the min-distance correction — the
            // order matters, both of those move `ideal` away from `fixed` without making the
            // spring any more stretchable.
            // LILYPOND-REF: lily/staff-spacing.cc:200.
            double stretchability = isStretchable ? ideal - fixedDistance : 0;
            ideal += situationalSpace;

            // The optical correction for a DOWN stem standing just after the bar line, applied
            // to BOTH fixed and ideal — and AFTER stretchability was taken, so it widens the
            // gap without making the spring any more stretchable.
            // LILYPOND-REF: lily/staff-spacing.cc:206-208.
            // Only when the BAR LINE is the column's last grob: a key or time change standing
            // after it takes the stem's place beside the note, and the correction reads the
            // last grob's bar extent, which only a bar line has.
            // LILYPOND-REF: lily/staff-spacing.cc:72-93 Staff_spacing::bar_y_positions — empty unless bar-line-interface
            // MEASURED (2.26.0, scratch/p390/keyw kn-b.ly / kn-u.ly, `\key b \major` opening a bar):
            // a down-stem first note 9.00 off the bar line's ink right, the same as an up-stem one;
            // the page drew the down-stem note 0.19 further right. LineStartColumn already gates
            // the same correction on the staff bar being last.
            // The stem it reads is the WHOLE column's, though: a Staff_spacing's right-items is
            // the musical PaperColumn itself, whose elements — every staff's note columns — are
            // what get_note_columns walks. So a staff whose own first note has no down stem still
            // carries the column's correction, and the merge does not dilute it.
            // LILYPOND-REF: lily/separating-line-group-engraver.cc:147-150 Separating_line_group_engraver::stop_translation_timestep — right-items = currentMusicalColumn
            // LILYPOND-REF: lily/spacing-interface.cc:150-169 get_note_columns — a Separation_item's elements, recursively
            // MEASURED (2.26.0, scratch/p390/ks/verify): test/articulations-lower-staff,
            // instrument-names and multi-staff-ottava put the first note 1.09 / 1.07 / 1.01 off
            // the bar line — exactly the column-wide correction; each staff's own drew 0.10 short.
            // And when a grace run opens the bar, the spring stops at the GRACE column, whose note
            // columns are the graces alone: the correction reads THEIR stems — up for a grace, so
            // it answers nothing (the correction only answers a down stem), except a lower
            // voice's, which is DOWN (GraceColumnInfo.StemDown). The mid-bar approach hands its
            // correction the first grace for the same reason (SpacingRules.ApproachColumn), and
            // this reads the same stand-in.
            // LILYPOND-REF: scm/music-functions.scm:652-656 score-grace-settings — Voice Stem direction UP
            // MEASURED (2.26.0, scratch/p390/kg kg1.ly, a down-stem c'' behind `\grace d''16`): the
            // main note 2.6207 off the bar line, where the column's down stem would have added 0.13.
            // ⚠️ THE STAND-IN'S STEM IS FULL LENGTH (ApproachColumn's remarks): a grace's down stem
            // is 0.8 of it at the grace font, so where its end falls inside the bar's ±2 the
            // overlap — and the correction — reads long. No book opens a bar on a lower voice's
            // grace (session 726: the two that write one put it mid-bar).
            double opticalCorrection = own.HasValue
                ? 0.0
                : startLeadGrace > 0
                    ? LeadGraceOpticalCorrection(firstItems)
                    // Beside a tab, each staff's own: every column against ITS bar
                    // (TabBarlineToNextNotesCorrections).
                    : staffOptical ?? BarlineToNextNotesCorrection(firstItems);
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
            // A single wish needs no merge_springs headroom: the correction just above already
            // guarantees ideal >= fixed >= 0.3 + min_distance.
            return new Spring(ideal, minDistance,
                              Math.Max(0.0, stretchability),
                              Math.Max(0.0, ideal - fixedDistance));
        }

        // The last grob's space-alist entry: its distance, where `fixed` opens, and whether the
        // spring may stretch.
        static (double Distance, double Fixed, bool Stretchable) SpaceFrom((double Prefix, MusicItem LastChange)? own)
        {
            if (own is var (prefix, lastChange) && own.HasValue)
            {
                var def = ChangeItemSpaceDef(lastChange);
                // fixed opens at last_ext[RIGHT] — in this spring's frame, the bar line's own
                // width is already behind us, so that is the prefix.
                // LILYPOND-REF: lily/staff-spacing.cc:166.
                return (def.Distance, prefix + (def.SplitsFixed ? def.Distance / 2 : 0), def.Stretchable);
            }
            // semi-fixed-space: fixed += d/2, ideal = fixed + d/2. `is_stretchable` stays
            // TRUE — only shrink-space and semi-shrink-space clear it, so the resulting
            // spring is NOT rigid. (LilySharp used to pass inverseStretchStrength 0 here on
            // the strength of a comment claiming semi-fixed was unstretchable; the source
            // says otherwise.)
            // LILYPOND-REF: lily/staff-spacing.cc:164-180.
            double d = EngravingDefaults.BarLineToNextNoteSpace;
            return (d, d / 2, true);
        }
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
        Rendering.ScoreTextMetrics fonts,
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
            return Rigid(ChangeItemColumnWidth(fonts, left) + BetweenChangeItemsSpace(left, right));

        var columnItems = ChangeColumnAt(spacingItems, leftIsChange ? leftIndex : leftIndex + 1);

        // note → the column's origin.
        if (!leftIsChange)
        {
            var gaps = MidMeasureChangeGaps(fonts, columnItems, new ItemColumn(left), durationIdeal);
            return gaps is { } g ? Rigid(g.LeftGap) : null;
        }

        // last change → the note: what is left of the right gap once the column's own
        // glyphs are subtracted, since the right gap is measured from the column ORIGIN.
        return Rigid(MidMeasureChangeRightGap(fonts, columnItems)
                     - MidMeasureChangeOffsetWithin(fonts, columnItems, left));

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
