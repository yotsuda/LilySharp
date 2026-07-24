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
}
