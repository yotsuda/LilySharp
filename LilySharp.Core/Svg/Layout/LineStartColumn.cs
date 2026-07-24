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

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// One grob's SPACING BOX on a paper column: the grob's column-relative ink extent
/// widened by <c>extra-spacing-width</c>, over the Y band the column reads.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/separation-item.cc:120-190 <c>Separation_item::boxes</c> — a
/// column's <c>horizontal-skylines</c> come from each element's
/// <c>extent (pc, X_AXIS)</c> and <c>pure_y_extent</c>, each widened by
/// <c>extra-spacing-width</c> / <c>extra-spacing-height</c>. It reads EXTENTS and never
/// a glyph outline: measured on 2.26.0, a line-start column's RIGHT skyline is one
/// CONSTANT-x building per grob, each x an element's extent plus its esw
/// (audit/lp-geometry/probes/line-start-mindist.ly). Baking outline skylines for the
/// clef / time-signature / TAB clef glyphs would be more precise than LilyPond, which is
/// as much a defect as being less precise.
/// <para>
/// Y is one frame shared by every box handed to <see cref="LineStartColumn"/>. Which way
/// is up does not matter — only that all the boxes agree, since the skyline distance
/// reads the Y bands solely to decide which boxes face each other.
/// </para>
/// </remarks>
internal readonly record struct ColumnBox(double YBottom, double YTop, double XLeft, double XRight);

/// <summary>
/// LilyPond's line-start column pair — the prefatory <c>NonMusicalPaperColumn</c> holding
/// every staff's clef / key / time, and the first musical column holding every staff's
/// first note — and the <c>min_dist</c> between them.
/// </summary>
/// <remarks>
/// <para>
/// LILYPOND-REF: lily/paper-column.cc:145-164 <c>Paper_column::minimum_distance</c>. It
/// is the distance from the LEFT column's RIGHT skyline to the RIGHT column's LEFT
/// skyline, floored at zero. <c>Staff_spacing::get_spacing</c> then floors the line-start
/// spring's FIXED distance at <c>0.3 + min_dist</c> (lily/staff-spacing.cc:210-215) —
/// which is the quantity Lily# has never had, and which binds on ordinary one-staff
/// scores too, not only on the notation+tab ones (SKC below).
/// </para>
/// <para>
/// This type is the COLUMN; <see cref="BoundaryColumn"/> is the same idea for a mid-line
/// measure boundary (different break-align order, one staff). Both build the box the same
/// way — ink extent widened by esw — because LilyPond has one <c>boxes()</c>.
/// </para>
/// <para>
/// Verified against LilyPond 2.26.0 by <c>LineStartColumnTests</c>, whose expected values
/// are the four numbers in audit/lp-geometry/probes/line-start-mindist.ly: SKC 7.485000,
/// SKD 10.135000, TKC 7.720000, TKA 9.270000.
/// </para>
/// </remarks>
internal static class LineStartColumn
{
    /// <summary>
    /// <c>KeySignature</c>'s own <c>extra-spacing-width</c> right side.
    /// </summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:1936 KeySignature
    /// <c>(extra-spacing-width . (0.0 . 1.0))</c>; the left side is 0.</remarks>
    internal const double KeySignatureEswRight = 1.0;

    /// <summary>
    /// <c>TimeSignature</c>'s own <c>extra-spacing-width</c> right side.
    /// </summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:3933 TimeSignature
    /// <c>(extra-spacing-width . (0.0 . 0.8))</c>; the left side is 0.</remarks>
    internal const double TimeSignatureEswRight = 0.8;

    /// <summary>
    /// <c>Paper_column::minimum_distance</c> between the line-start prefatory column and
    /// the first note column.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/paper-column.cc:145-164 — <c>max (0, skys[LEFT].distance
    /// (skys[RIGHT]))</c>, where <c>skys[LEFT]</c> is the left column's RIGHT skyline and
    /// <c>skys[RIGHT]</c> the right column's LEFT one. The accidentals reach this through
    /// <c>Separation_item::conditional_skyline</c> rather than through the column's
    /// elements (Accidental grobs are deliberately absent from <c>'elements</c>,
    /// lily/paper-column-engraver.cc:259) — here they are simply boxes in
    /// <paramref name="firstNote"/>, since the merge is a union either way.
    /// </remarks>
    public static double MinimumDistance(
        IReadOnlyList<ColumnBox> prefatory, IReadOnlyList<ColumnBox> firstNote)
    {
        if (prefatory.Count == 0 || firstNote.Count == 0)
            return 0.0;

        var right = HorizontalSkyline.FromBoxes(ToTuples(prefatory), HorizontalDirection.Right);
        var left = HorizontalSkyline.FromBoxes(ToTuples(firstNote), HorizontalDirection.Left);
        return Math.Max(0.0, right.Distance(left));
    }

    private static IEnumerable<(double YBottom, double YTop, double XLeft, double XRight)>
        ToTuples(IReadOnlyList<ColumnBox> boxes)
    {
        foreach (var b in boxes)
            yield return (b.YBottom, b.YTop, b.XLeft, b.XRight);
    }

    /// <summary>
    /// The Y band a PREFATORY grob's box covers: its own ink, stretched to the staff and
    /// to its neighbours.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/output-lib.scm:976-979
    /// <c>pure-from-neighbor-interface::extra-spacing-height-including-staff</c> is the
    /// pairwise (min, max) of :900-910
    /// <c>item::extra-spacing-height-including-staff</c> (stretch the box to the
    /// StaffSymbol's extent) and :934-942
    /// <c>pure-from-neighbor-interface::extra-spacing-height</c> (stretch it to the union
    /// with the NEIGHBOURS — the pure-relevant items in the ADJACENT columns,
    /// lily/pure-from-neighbor-engraver.cc:110-137). Applied to the grob's own height,
    /// that is exactly the union of the three extents.
    /// <para>
    /// The Clef takes the neighbour half alone
    /// (<c>…::extra-spacing-height-at-beginning-of-line</c>, :929-932, at a line start),
    /// but the staff never widens a clef anyway — a treble clef's ink already spans it —
    /// so one union serves both. Measured on 2.26.0: SKC's TimeSignature own height
    /// -1.000..1.000 with neighbours -3.545..2.050 gives the dumped esh
    /// (-2.545 . 1.050), and the Clef's own -3.550..3.800 already covers them, giving the
    /// dumped (0 . 0).
    /// </para>
    /// <para>
    /// ⚠️ The neighbour of a line-start prefatory grob IS the first note column, so every
    /// prefatory box vertically COVERS the note column it is measured against. That is
    /// why <see cref="MinimumDistance"/> never depends on the first note's pitch, and why
    /// the two staves of a notation+tab score do not interact: the esh LilyPond reports is
    /// identical on the one-staff and the two-staff score, i.e. the neighbour set is
    /// per-STAFF.
    /// </para>
    /// </remarks>
    public static (double Bottom, double Top) PrefatoryY(
        double inkBottom, double inkTop,
        double staffBottom, double staffTop,
        double neighbourBottom, double neighbourTop)
        => (Math.Min(inkBottom, Math.Min(staffBottom, neighbourBottom)),
            Math.Max(inkTop, Math.Max(staffTop, neighbourTop)));

    /// <summary>
    /// The box one PREFATORY grob contributes: its column-relative ink widened by its
    /// <c>extra-spacing-width</c>, over <see cref="PrefatoryY"/>'s band.
    /// </summary>
    /// <remarks>
    /// One call is one iteration of <c>Separation_item::boxes</c>'s loop
    /// (separation-item.cc:152-187). The caller places the ink, because the X placement is
    /// the break-align column table (<see cref="BreakAlignSpacing.SolvePrefixColumns"/>) —
    /// ONE table for every staff, which is the whole point of break-alignment.
    /// </remarks>
    public static ColumnBox PrefatoryBox(
        double inkLeft, double inkRight, double inkBottom, double inkTop,
        double eswLeft, double eswRight,
        double staffBottom, double staffTop,
        double neighbourBottom, double neighbourTop)
    {
        var (b, t) = PrefatoryY(inkBottom, inkTop,
            staffBottom, staffTop, neighbourBottom, neighbourTop);
        return new ColumnBox(b, t, inkLeft + eswLeft, inkRight + eswRight);
    }

    /// <summary>
    /// The box one grob of the FIRST NOTE column contributes, at its ink extent RELATIVE
    /// TO THE COLUMN ORIGIN (which LilyPond puts at the notehead's left edge).
    /// </summary>
    /// <remarks>
    /// A note column grob carries no <c>extra-spacing-height</c> (measured: NoteHead's is
    /// (0 . 0)), so its box keeps its own ink Y. Its pitch therefore decides where it
    /// sits, and the prefatory boxes cover it wherever that is — see
    /// <see cref="PrefatoryY"/>.
    /// </remarks>
    public static ColumnBox FirstNoteBox(
        double inkLeft, double inkRight, double inkBottom, double inkTop,
        double eswLeft = -SpacingRules.DefaultExtraSpacingWidth,
        double eswRight = SpacingRules.DefaultExtraSpacingWidth)
        => new ColumnBox(inkBottom, inkTop, inkLeft + eswLeft, inkRight + eswRight);

    /// <summary>
    /// <c>min_dist</c> for a line start of <paramref name="score"/> — the largest column
    /// distance any of its staves demands.
    /// </summary>
    /// <remarks>
    /// <para>
    /// LilyPond makes ONE <c>Paper_column::minimum_distance</c> call over every staff's
    /// boxes at once. Taking the max of per-staff calls is the same number, and the reason is
    /// measured rather than assumed: a prefatory grob's <c>extra-spacing-height</c> stretches
    /// its box to its own STAFF and to its NEIGHBOURS, and the neighbour set is per-staff —
    /// the esh LilyPond dumps for the notation staff's TimeSignature is IDENTICAL on the
    /// one-staff score (SKC) and the notation+tab one (TKC), so no staff's prefatory box ever
    /// faces another staff's note column (audit/lp-geometry/probes/line-start-mindist.ly).
    /// A skyline distance is a max over Y bands, and disjoint bands make the max
    /// distributive.
    /// </para>
    /// <para>
    /// ⚠️ Y is therefore not modelled per grob here: every box of one staff is given the same
    /// band. That is not a simplification of the ANSWER — the stretch guarantees each
    /// prefatory box faces the note column whatever the first note's pitch, which is exactly
    /// why <see cref="MinimumDistance"/> is pitch-independent — but it does mean these boxes
    /// must not be reused for a question where the bands matter. The distance still goes
    /// through <see cref="MinimumDistance"/> rather than a reach subtraction, so there is one
    /// implementation of the skyline step and it is the one the four LilyPond numbers pin.
    /// </para>
    /// </remarks>
    /// <param name="columns">The break-align table this line start is drawn on
    /// (<see cref="BreakAlignSpacing.SolvePrefixColumns"/>) — ONE table for every staff.</param>
    /// <param name="clefGroupLeft"><see cref="SpacingRules.ClefGroupExtent"/>'s Left: each
    /// clef keeps its own stencil offset inside the group, whose ink-left lands on the
    /// column.</param>
    /// <param name="keyInkWidth">The KeySignature GROUP's engraved ink width. Passing the
    /// group's width for every staff that engraves one overstates a narrower staff's own box,
    /// which cannot change the MAX this returns — and the key is shadowed by the meter
    /// whenever there is one (measured: probe SKD).</param>
    /// <param name="timeInkWidth">The TimeSignature's ink width, 0 when the prefix has
    /// none.</param>
    public static double MinimumDistanceAtLineStart(
        Model.MultiStaffScore score,
        BreakAlignSpacing.PrefixColumns columns,
        double clefGroupLeft,
        double keyInkWidth,
        double timeInkWidth,
        int startMeasureIndex)
    {
        double worst = 0.0;
        foreach (var (_, staff, _) in score.EnumerateStaves())
        {
            // A lyric / chord row engraves no prefatory grob, and its text does not join the
            // horizontal skylines at all.
            if (staff.IsTextRow)
                continue;

            var notes = FirstNoteBoxes(staff, startMeasureIndex);
            if (notes.Count == 0)
                continue;

            worst = Math.Max(worst, MinimumDistance(
                PrefatoryBoxes(staff, columns, clefGroupLeft, keyInkWidth, timeInkWidth),
                notes));
        }
        return worst;
    }

    /// <summary>The one Y band every box of a staff is given — see the remarks on
    /// <see cref="MinimumDistanceAtLineStart"/> for why one band is enough.</summary>
    private const double SharedBand = 1.0;

    /// <summary>
    /// The prefatory boxes one staff contributes: its clef, and the key and meter it
    /// ENGRAVES, at the shared break-align columns.
    /// </summary>
    /// <remarks>
    /// A tab staff engraves neither: LilyPond removes its <c>Key_engraver</c>
    /// (ly/engraver-init.ly:1214) and gives its TimeSignature no stencil — dumped as an EMPTY
    /// extent, which is why the probe harness reports skipping <c>TKC TABTIME</c>. Its TAB
    /// clef IS an ordinary Clef grob in the shared group, and a wide one.
    /// </remarks>
    private static List<ColumnBox> PrefatoryBoxes(
        Model.Staff staff,
        BreakAlignSpacing.PrefixColumns columns,
        double clefGroupLeft, double keyInkWidth, double timeInkWidth)
    {
        var stencil = staff.IsTab
            ? SpacingRules.TabClefStencil
            : SpacingRules.ClefStencil(staff.Clef);
        double anchor = columns.ClefX - clefGroupLeft;

        var boxes = new List<ColumnBox>
        {
            new ColumnBox(-SharedBand, SharedBand,
                anchor + stencil.Left - SpacingRules.DefaultExtraSpacingWidth,
                anchor + stencil.Right + SpacingRules.DefaultExtraSpacingWidth),
        };

        bool engravesPrefatoryStaffGrobs = SpacingRules.ContributesToKeyColumnWidth(staff);
        if (columns.HasKey && keyInkWidth > 0.0 && engravesPrefatoryStaffGrobs)
            boxes.Add(new ColumnBox(-SharedBand, SharedBand,
                columns.KeyX, columns.KeyX + keyInkWidth + KeySignatureEswRight));

        if (columns.HasTime && timeInkWidth > 0.0 && engravesPrefatoryStaffGrobs)
            boxes.Add(new ColumnBox(-SharedBand, SharedBand,
                columns.TimeX, columns.TimeX + timeInkWidth + TimeSignatureEswRight));

        return boxes;
    }

    /// <summary>
    /// The first musical column of <paramref name="staff"/>'s measure
    /// <paramref name="measureIndex"/>, as the single box its LEFTMOST ink makes.
    /// </summary>
    /// <remarks>
    /// The leftward reach is <see cref="SpacingRules.MusicalColumnLeftReach"/> — the item's
    /// own left extent plus its <c>extra-spacing-width</c>, which is 0.2 for a note carrying
    /// an accidental and 0.1 otherwise. That is the quantity TKA measures: a sharp on the
    /// first note moves min_dist by 1.45 + 0.1 = 1.55. Every voice of the staff is walked and
    /// the furthest-reaching wins, a paper column being shared by all of them.
    /// </remarks>
    private static List<ColumnBox> FirstNoteBoxes(Model.Staff staff, int measureIndex)
    {
        double reachLeft = double.NegativeInfinity;
        double reachRight = 0.0;
        foreach (var voice in staff.Voices)
        {
            if (measureIndex < 0 || measureIndex >= voice.Measures.Length)
                continue;
            foreach (var item in voice.Measures[measureIndex].Items)
            {
                if (!SpacingRules.IsMusicalColumn(item))
                    continue;
                reachLeft = Math.Max(reachLeft, SpacingRules.MusicalColumnLeftReach(item));
                reachRight = Math.Max(reachRight,
                    SpacingRules.CalculateNoteheadRightExtent(item)
                    + SpacingRules.DefaultExtraSpacingWidth);
                break;   // the FIRST column of this voice, not every column
            }
        }

        return double.IsNegativeInfinity(reachLeft)
            ? new List<ColumnBox>()
            : new List<ColumnBox> { new ColumnBox(-SharedBand, SharedBand, -reachLeft, reachRight) };
    }

    /// <summary>
    /// The last three statements of <c>Staff_spacing::get_spacing</c>: floor the FIXED
    /// distance at <c>0.3 + min_dist</c>, lift the ideal to it, and build the spring.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/staff-spacing.cc:210-220.
    /// <code>
    ///   min_dist_correction = max (0, 0.3 + min_dist - fixed);
    ///   fixed += min_dist_correction;
    ///   ideal  = max (ideal, fixed);
    ///   Spring ret (ideal, min_dist);
    ///   ret.set_inverse_stretch_strength  (max (0, stretchability));
    ///   ret.set_inverse_compress_strength (max (0, ideal - fixed));
    /// </code>
    /// <para>
    /// ⚠️ Note WHICH distance ends up as the spring's minimum: it is <c>min_dist</c>, the
    /// column-to-column skyline distance, NOT <c>fixed</c>. <c>fixed</c> is the distance
    /// reached at force −1 and enters only through the compress strength. Lily# had been
    /// putting <c>fixed</c> in <see cref="Spring.MinDistance"/> and deriving the compress
    /// strength from it, which is why the floor cannot be added without this: a floor
    /// applied to that field would raise the spring's hard minimum instead of stiffening it.
    /// </para>
    /// <para>
    /// Every distance here is measured from the SAME origin. The callers work in
    /// prefix-relative terms (0 = where the prefix ink ends, which is
    /// <see cref="BreakAlignSpacing.PrefixColumns.Right"/>), LilyPond in column-relative
    /// ones; the difference is a constant that cancels, since all four quantities shift
    /// together.
    /// </para>
    /// </remarks>
    /// <param name="ideal">The space-alist ideal (<see cref="BreakAlignSpacing.FirstNoteSpring"/>'s
    /// <c>Ideal</c>).</param>
    /// <param name="fixed_">The space-alist FIXED distance (that same helper's <c>Min</c>,
    /// which is LilyPond's <c>fixed</c> and not its <c>min_dist</c>).</param>
    /// <param name="stretchability">
    /// <c>is_stretchable ? ideal - fixed : 0</c> (staff-spacing.cc:200). Zero for all three
    /// line-start entries: Clef's <c>minimum-fixed-space</c> leaves ideal == fixed, and
    /// KeySignature's <c>shrink-space</c> and TimeSignature's <c>semi-shrink-space</c> set
    /// <c>is_stretchable = false</c> (:191, :197). Measured: probe JN's first head sits on
    /// its natural 8.585000 on a JUSTIFIED line, i.e. the spring does not stretch.</param>
    /// <param name="minDistance"><see cref="MinimumDistance"/>, in the same frame.</param>
    public static Spring SpringWithMinimumDistanceFloor(
        double ideal, double fixed_, double stretchability, double minDistance)
    {
        double correctedFixed =
            fixed_ + Math.Max(0.0, SpacingRules.SpringHeadroom + minDistance - fixed_);
        double correctedIdeal = Math.Max(ideal, correctedFixed);
        return new Spring(correctedIdeal, minDistance,
            Math.Max(0.0, stretchability),
            Math.Max(0.0, correctedIdeal - correctedFixed));
    }
}
