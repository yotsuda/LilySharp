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

        var right = Skyline(ref t_prefatorySkyline, prefatory, HorizontalDirection.Right);
        var left = Skyline(ref t_firstNoteSkyline, firstNote, HorizontalDirection.Left);
        double distance = right.Distance(left);
        right.Clear();
        left.Clear();
        t_prefatorySkyline = right;
        t_firstNoteSkyline = left;
        return Math.Max(0.0, distance);
    }

    // Lent from the drawer (taken out, so a nested call builds its own) and given back
    // cleared by MinimumDistance, whose one distance is all that reads them.
    private static HorizontalSkyline Skyline(
        ref HorizontalSkyline? drawer, IReadOnlyList<ColumnBox> boxes, HorizontalDirection direction)
    {
        var skyline = drawer ?? new HorizontalSkyline(direction);
        drawer = null;
        var tuples = ToTuples(boxes);
        HorizontalSkyline.FromBoxesInto(skyline, tuples);
        HorizontalSkyline.GiveBoxList(tuples);
        return skyline;
    }

    /// <summary>
    /// The two skylines <see cref="MinimumDistance"/> measures, kept by the thread between
    /// line starts.
    /// </summary>
    /// <remarks>
    /// MEASURED (session 498, Release, the reader's corpus, eight forward keystrokes a book):
    /// 6.97 distances a keystroke, 1,903 B between the two skylines built for each, and
    /// neither read again once the distance returned.
    /// </remarks>
    [ThreadStatic]
    private static HorizontalSkyline? t_prefatorySkyline;

    [ThreadStatic]
    private static HorizontalSkyline? t_firstNoteSkyline;

    // A list and not an iterator: the answer is as long as its input, and
    // HorizontalSkyline.FromBoxesInto sizes its buildings from that length. The list is lent
    // (HorizontalSkyline.RentBoxList) — FromBoxesInto copies it, so Skyline gives it straight back.
    private static List<(double YBottom, double YTop, double XLeft, double XRight)>
        ToTuples(IReadOnlyList<ColumnBox> boxes)
    {
        var tuples = HorizontalSkyline.RentBoxList(boxes.Count);
        // Indexed, not foreach: `boxes` is an interface, so foreach would box an enumerator
        // on every line start (RULES §5.3).
        for (int i = 0; i < boxes.Count; i++)
        {
            var b = boxes[i];
            tuples.Add((b.YBottom, b.YTop, b.XLeft, b.XRight));
        }
        return tuples;
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
    /// <param name="clefGroupLeft">
    /// <see cref="SpacingRules.ClefGroupExtent(LilySharp.Core.Svg.Model.MultiStaffScore)"/>'s Left: each
    /// clef keeps its own stencil offset inside the group, whose ink-left lands on the
    /// column.</param>
    /// <param name="timeInkWidth">The TimeSignature's ink width, 0 when the prefix has
    /// none.</param>
    public static double MinimumDistanceAtLineStart(
        Model.MultiStaffScore score,
        BreakAlignSpacing.PrefixColumns columns,
        double clefGroupLeft,
        double timeInkWidth,
        int startMeasureIndex)
    {
        double worst = 0.0;
        // Lent, and given back cleared below (see t_prefatoryBoxes).
        var boxes = t_prefatoryBoxes ?? new List<ColumnBox>();
        t_prefatoryBoxes = null;
        foreach (var (_, staff, staffIndex) in score.EnumerateStaves())
        {
            // A lyric / chord row engraves no prefatory grob, and its text does not join the
            // horizontal skylines at all.
            if (staff.IsTextRow)
                continue;

            var notes = FirstNoteBoxes(score.TextMetrics, staff, startMeasureIndex);
            AddFirstColumnDiagrams(score, staff, staffIndex, startMeasureIndex, notes);
            if (notes.Count == 0)
                continue;

            // A line start AFTER A BREAK: its prefatory grobs are the broken copies, which have
            // no neighbours, so each box keeps its own ink's Y (BrokenLineStartDistance). A tab
            // staff (whose TAB clef spans it) and a column carrying a chord diagram (whose box
            // spans every Y) keep the shared band, which those boxes face anyway.
            if (startMeasureIndex > 0 && !staff.IsTab && notes.Count == 1)
            {
                worst = Math.Max(worst, BrokenLineStartDistance(
                    score, staff, columns, clefGroupLeft, timeInkWidth, startMeasureIndex));
                continue;
            }

            boxes.Clear();
            foreach (var g in PrefatoryGrobs(
                         score, staff, columns, clefGroupLeft, timeInkWidth, startMeasureIndex))
                boxes.Add(new ColumnBox(-SharedBand, SharedBand,
                    g.InkLeft + g.EswLeft, g.InkRight + g.EswRight));

            worst = Math.Max(worst, MinimumDistance(boxes, notes));
        }
        boxes.Clear();
        t_prefatoryBoxes = boxes;
        return worst;
    }

    /// <summary>
    /// Adds to <paramref name="notes"/> the box of every chord DIAGRAM standing on
    /// <paramref name="staff"/>'s first musical column — the same box the bar's own diagram
    /// spacing reads (<see cref="SpacingRules.ApplyFretFrameSpacing"/>): its frame, the
    /// <c>textLengthOn</c> extra-spacing-width (-0.0 . 0.4), and an extra-spacing-height of
    /// (-inf . +inf), so it meets the prefatory boxes whatever their height.
    /// </summary>
    /// <remarks>
    /// The twin writes the diagram as a TextScript with exactly those two tweaks, so LilyPond's
    /// min_dist reaches it (lily/paper-column.cc Paper_column::minimum_distance — the column's
    /// whole skyline). MEASURED (2.26.0, Lab sessions/p833/ch c5, `e'4@chord(C x32010)` opening
    /// a first line): the head stands 0.79 right of where a bare one does. Until session 834
    /// the line-start spring reached it only through the floor of the bar-line spring's minimum
    /// (ownFixedFloor), 0.12 too far — and the grid was drawn centred on the head, 0.45 left of
    /// LilyPond's (<see cref="FretFrameGeometry.GridCentreFromColumnOrigin"/>), which is why
    /// this box could not replace the floor until the drawing moved.
    /// </remarks>
    private static void AddFirstColumnDiagrams(
        Model.MultiStaffScore score, Model.Staff staff, int staffIndex, int measureIndex,
        List<ColumnBox> notes)
    {
        if (score.Articulations.IsDefaultOrEmpty)
            return;
        var voices = staff.Voices;
        foreach (var art in score.Articulations)
        {
            if (art.Type != Syntax.ArticulationType.FretFrame || art.StaffIndex != staffIndex
                || art.MeasureIndex != measureIndex || art.VoiceIndex >= voices.Length
                || art.FrameSpec is null
                || measureIndex >= voices[art.VoiceIndex].Measures.Length)
                continue;
            var items = voices[art.VoiceIndex].Measures[measureIndex].Items;
            // On the bar's opening column — a note's, or a spacer's that the diagram alone keeps.
            if (art.ItemIndex >= items.Length || !OpensTheBar(items, art.ItemIndex))
                continue;
            // The box is about the grid's centre; the column's origin is the head's left edge.
            var box = FretFrameGeometry.Box(art.FrameSpec, score.TextMetrics);
            double centre = FretFrameGeometry.GridCentreFromColumnOrigin(box);
            notes.Add(new ColumnBox(-SharedBand, SharedBand, centre + box.Left, centre + box.Right + 0.4));
        }
    }

    private static bool OpensTheBar(System.Collections.Immutable.ImmutableArray<Model.MusicItem> items, int index)
    {
        var onset = Semantics.Fraction.Zero;
        for (int i = 0; i < index; i++)
            onset += items[i].Duration;
        return onset == Semantics.Fraction.Zero;
    }

    /// <summary>
    /// <c>min_dist</c> from a line start's prefatory column to the BAR-LINE column that closes
    /// its first bar — the pair a multi-measure rest opening the line bounds, and the first
    /// term of its rod (<see cref="SpacingRules.MmrRodDistance"/>).
    /// </summary>
    /// <remarks>
    /// The rest's left bound is the line-start column itself: a run opening a line has no bar
    /// line of its own before it, so LilyPond's <c>Multi_measure_rest::calculate_spacing_rods</c>
    /// reads <c>Paper_column::minimum_distance</c> from the prefix (clef, key, meter) where a
    /// run mid-line reads it from a bar line (<see cref="SpacingRules.MmrRodMinimumDistance"/>).
    /// The right column's left skyline is its bar line's box, which reaches the default
    /// <c>extra-spacing-width</c> −0.1 left of the column origin, as mid-line.
    /// MEASURED (2.26.0, LilySharp-Lab sessions/p714/mmrcol r1.ly and m1.ly, ragged, a treble
    /// staff in 4/4 opening on <c>R1</c>): the line-start column 8.535827, the bar line
    /// 23.520827 — 14.985 = 7.485 (this distance, SKC's figure, the meter's ink + 0.8 + 0.1)
    /// + the rod's 7.5.
    /// LILYPOND-REF: lily/multi-measure-rest.cc:374-389 calculate_spacing_rods — rod.distance_
    ///   = max (Paper_column::minimum_distance (li, ri) + length, minlen), li the left bound's column.
    /// </remarks>
    public static double MinimumDistanceToBarAtLineStart(
        Model.MultiStaffScore score,
        BreakAlignSpacing.PrefixColumns columns,
        double clefGroupLeft,
        double timeInkWidth,
        int startMeasureIndex,
        double doublePercentHalfWidth = 0.0)
    {
        double worst = 0.0;
        // Lent, and given back cleared below (see t_prefatoryBoxes).
        var boxes = t_prefatoryBoxes ?? new List<ColumnBox>();
        t_prefatoryBoxes = null;
        // The bar line's box from its column origin (its left edge): the default
        // extra-spacing-width, separation-item.cc:166-167. Only its left reach is read.
        var bar = new List<ColumnBox>
        {
            new ColumnBox(-SharedBand, SharedBand,
                -SpacingRules.DefaultExtraSpacingWidth, SpacingRules.DefaultExtraSpacingWidth),
        };
        foreach (var (_, staff, staffIndex) in score.EnumerateStaves())
        {
            if (staff.IsTextRow)
                continue;
            // …and the double percent sign THIS staff draws straddling that bar line, whose
            // left half reaches back into the bar (BoundaryColumn.DoublePercentBox, as the
            // bar-to-bar pair reads it). Per staff: break alignment is, so a staff's prefix
            // meets its own sign — a tab's is half as wide again as the staff's above it.
            // MEASURED (2.26.0, Lab sessions/p836/pk pk1 / dp.ly, E-flat major over a 5-string
            // tab): giving every staff the tab's sign set the staff's key signature against
            // it and stood the first bar line 1.07 right of LilyPond's.
            if (bar.Count > 1)
                bar.RemoveAt(1);
            double ownHalf = doublePercentHalfWidth > 0
                ? ScoreSideTables.DoublePercentHalfWidthOn(score, startMeasureIndex + 1, staffIndex)
                : 0;
            if (ownHalf > 0)
            {
                var sign = BoundaryColumn.DoublePercentBox(ownHalf);
                bar.Add(new ColumnBox(-SharedBand, SharedBand, sign.XLeft, sign.XRight));
            }
            boxes.Clear();
            foreach (var g in PrefatoryGrobs(
                         score, staff, columns, clefGroupLeft, timeInkWidth, startMeasureIndex))
                boxes.Add(new ColumnBox(-SharedBand, SharedBand,
                    g.InkLeft + g.EswLeft, g.InkRight + g.EswRight));
            worst = Math.Max(worst, MinimumDistance(boxes, bar));
        }
        boxes.Clear();
        t_prefatoryBoxes = boxes;
        return worst;
    }

    /// <summary>
    /// Spring 0 of an EMPTY bar that opens a line — every column of it unused, so its two
    /// columns are the prefatory column and its closing bar line. Rigid, sized so that it and
    /// the bar's closing pair spring <paramref name="closingPair"/> (SpacingRules.EmptyBarSprings,
    /// bar line to bar line) add up to LilyPond's one spring between those two columns.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spacing-basic.cc:40-66 standard_breakable_column_spacing — both columns
    ///   breakable: <c>Spring (min_dist + space, min_dist)</c>, inverse stretch <c>space</c>; its
    ///   own comment names this case ("an empty first measure on a line (which has a large
    ///   min_dist because of the clef)").
    /// The closing pair is the same breakable-pair spring with the bar-to-bar <c>min_dist</c>,
    /// so its ideal − min is the same <c>space</c> and its strengths are LilyPond's: a rigid
    /// spring of <c>min_dist − frame − closingPair.min</c> in front of it makes every reading of
    /// the series the prefix-to-bar spring's. MEASURED (2.26.0, Lab sessions/p833/pr pw2, a
    /// continuation line opening on the empty first bar of a double-percent pair): LilyPond's
    /// first bar line stands 2.0 left of where Lily# drew it until session 833, which priced
    /// the line start as if a first NOTE followed the clef and then the pair after it.
    /// </remarks>
    public static Spring EmptyBarLineStartSpring(
        Model.MultiStaffScore score,
        BreakAlignSpacing.PrefixColumns columns,
        double clefGroupLeft,
        double timeInkWidth,
        int startMeasureIndex,
        double measureStartBarWidth,
        Spring closingPair,
        double doublePercentHalfWidth,
        bool closingBreakable = true,
        double closingLeftBarWidth = 0.0)
    {
        double minDistance = MinimumDistanceToBarAtLineStart(
            score, columns, clefGroupLeft, timeInkWidth, startMeasureIndex, doublePercentHalfWidth);
        double frame = columns.Right + measureStartBarWidth;
        double length = Math.Max(0.0, minDistance - frame - closingPair.MinDistance);
        // A breakable pair stretches by `space` alone, which the closing pair already carries.
        // Where the closing bar line forbids a break (the middle of a double percent pair) the
        // spring is standard_breakable_column_spacing's other branch, whose stretch is its
        // IDEAL, prefix column to column: the closing pair (EmptyBarSprings) stretches by the
        // bar-to-bar one, so this leg carries the difference of the two minima.
        // MEASURED (2.26.0, Lab sessions/p836/pk pk1, a continuation line opening on the first
        // bar of a double percent pair over a tab): the first bar line 19.41 after the clef.
        // LILYPOND-REF: lily/spacing-basic.cc:68-83 standard_breakable_column_spacing — the
        //   dt != 0 branch, Spring (ideal, min_dist) with its default strengths.
        double stretch = closingBreakable
            ? 0.0
            : Math.Max(0.0, minDistance - (closingPair.MinDistance + closingLeftBarWidth));
        return new Spring(length, length, stretch, 0.0);
    }

    /// <summary>
    /// The prefatory boxes of one staff at a time for <see cref="MinimumDistanceAtLineStart"/>,
    /// lent from one list the thread keeps between line starts.
    /// </summary>
    /// <remarks>
    /// MEASURED (session 457's census, Release, the reader's corpus, eight forward keystrokes
    /// a book): 6.98 lists a keystroke at 1.54 boxes (max 3), 1,284 B with the growth ladder,
    /// and every one unreachable when the render returned. The list's one reader is
    /// <see cref="MinimumDistance"/>, which copies it into a skyline (<c>ToTuples</c> →
    /// <c>HorizontalSkyline.FromBoxesInto</c>) and keeps nothing. RENTING TAKES IT OUT OF THE
    /// DRAWER (session 421's idiom); THE CLEARING IS ON GIVE (session 456) and before each
    /// staff — a stale box would widen the next staff's <c>min_dist</c> with a grob it does
    /// not engrave. <see cref="ColumnBox"/> holds four doubles, so the drawer pins nothing.
    /// ⚠️ THE CLEAR BEFORE EACH STAFF had no observer until session 478 (session 467, by poison):
    /// the answer is a MAX over staves, so a later staff that also sees the earlier staves'
    /// boxes changes it only when an earlier staff reaches further right AND the later staff's
    /// first note further left — a shape neither population holds. The net builds one (a second
    /// line, alto sax over C): LineStartColumnTests.ALineStartsMinDist_DoesNotDependOnTheStaffOrder.
    /// </remarks>
    [ThreadStatic]
    private static List<ColumnBox>? t_prefatoryBoxes;

    /// <summary>The one Y band every box of a staff is given — see the remarks on
    /// <see cref="MinimumDistanceAtLineStart"/> for why one band is enough.</summary>
    private const double SharedBand = 1.0;

    /// <summary>
    /// One break-aligned grob of one staff's prefatory column: its own INK, in the column
    /// frame, plus the <c>extra-spacing-width</c> that widens it into a spacing BOX.
    /// </summary>
    /// <remarks>
    /// Both questions the prefatory column answers read this: <c>min_dist</c> wants the
    /// BOX (ink + esw, <see cref="MinimumDistanceAtLineStart"/>) and
    /// <c>Staff_spacing::get_spacing</c> wants the bare INK (<c>last_ext</c>,
    /// <see cref="LineStartSpring"/>). One walk, so the two cannot come to disagree about
    /// which grobs a staff engraves.
    /// </remarks>
    private readonly record struct PrefatoryGrob(
        BreakAlignSymbol Symbol,
        double InkLeft, double InkRight, double EswLeft, double EswRight);

    /// <summary>
    /// The break-aligned grobs one staff contributes to the line-start prefatory column:
    /// its clef, and the key and meter it ENGRAVES, at the shared break-align columns.
    /// </summary>
    /// <remarks>
    /// A tab staff engraves no KEY in either mode: ly/engraver-init.ly:1214 is \remove
    /// Key_engraver in the TabStaff context, and <c>tabFullNotation</c> has nothing to revert
    /// because a removed engraver is not an override. Its
    /// METER is the mode's to decide — blanked stencil bare, reverted under
    /// <c>\tabFullNotation</c>, which is Lily#'s default <c>tab</c>
    /// (<see cref="SpacingRules.ContributesToTimeColumnWidth"/>). The probe harness reports
    /// skipping <c>TKC TABTIME</c> because that probe's twin is a BARE TabStaff. Its TAB
    /// clef IS an ordinary Clef grob in the shared group, and a wide one.
    /// <para>
    /// The key ink is the one THIS staff engraves (<see cref="SpacingRules.ActiveKeyInkForStaff"/>),
    /// not the group's union: the column X is shared (that is what break-alignment is) but
    /// the extent is the grob's own, so a transposed part's wider signature does not widen
    /// its neighbour's grob.
    /// </para>
    /// </remarks>
    /// <summary>
    /// The right edge of <paramref name="staff"/>'s OWN line-start prefatory ink — its clef,
    /// the key and meter it engraves, the opening bar line — in the break-align frame
    /// (<paramref name="columns"/>), or null for a row that engraves none.
    /// </summary>
    /// <remarks>
    /// What a spanner broken at this line start begins from: lily/tie-formatting-problem.cc
    /// :262-270 set_minimum_height reads <c>Axis_group_interface::staff_extent</c> of the
    /// break column — the extent of the grobs on THIS staff, not the column's across the
    /// system. A notation staff beside a tab ends on its bass clef, 0.1166 left of the wider
    /// TAB clef the shared column is sized by (Kokomo.lys, Lab corpus). Same walk as the
    /// spacing (PrefatoryGrobs), bare ink with no extra-spacing-width.
    /// </remarks>
    public static double? StaffInkRight(
        Model.MultiStaffScore score, Model.Staff staff,
        BreakAlignSpacing.PrefixColumns columns,
        double clefGroupLeft, double timeInkWidth, int startMeasureIndex)
    {
        if (staff.IsTextRow)
            return null;
        double right = double.NegativeInfinity;
        foreach (var g in PrefatoryGrobs(score, staff, columns, clefGroupLeft, timeInkWidth, startMeasureIndex))
            right = Math.Max(right, g.InkRight);
        return double.IsNegativeInfinity(right) ? null : right;
    }

    private static List<PrefatoryGrob> PrefatoryGrobs(
        Model.MultiStaffScore score,
        Model.Staff staff,
        BreakAlignSpacing.PrefixColumns columns,
        double clefGroupLeft, double timeInkWidth, int startMeasureIndex)
    {
        var stencil = staff.IsTab
            ? SpacingRules.TabClefStencil
            : SpacingRules.ClefStencil(staff.Clef);
        double anchor = columns.ClefX - clefGroupLeft;

        var grobs = new List<PrefatoryGrob>();
        // Clef declares no extra-spacing-width, so it takes separation-item.cc:166-167's
        // default (-0.1 . 0.1). A one-line (rhythm) staff engraves none (EngravesClef).
        if (SpacingRules.EngravesClef(staff))
            grobs.Add(new PrefatoryGrob(BreakAlignSymbol.Clef,
                anchor + stencil.Left, anchor + stencil.Right,
                -SpacingRules.DefaultExtraSpacingWidth, SpacingRules.DefaultExtraSpacingWidth));

        double keyInkWidth = SpacingRules.ActiveKeyInkForStaff(score, staff, startMeasureIndex);
        if (columns.HasKey && keyInkWidth > 0.0)
            grobs.Add(new PrefatoryGrob(BreakAlignSymbol.KeySignature,
                columns.KeyX, columns.KeyX + keyInkWidth, 0.0, KeySignatureEswRight));

        // ⚠️ THE METER'S OWN PREDICATE, NOT THE KEY'S. This read ContributesToKeyColumnWidth,
        // which is the right answer for a tab staff only while a tab staff engraves neither.
        // It engraves no key in either mode but a meter under \tabFullNotation, so borrowing
        // the key's predicate blanked the meter of every full-notation tab book: the staff
        // ended its prefix on the TAB clef and wished off the clef's minimum-fixed-space 5.0
        // where LilyPond wishes off the meter's semi-shrink-space 2.0.
        if (columns.HasTime && timeInkWidth > 0.0
            && SpacingRules.ContributesToTimeColumnWidth(staff))
            grobs.Add(new PrefatoryGrob(BreakAlignSymbol.TimeSignature,
                columns.TimeX, columns.TimeX + timeInkWidth, 0.0, TimeSignatureEswRight));

        // The bar line the system OPENS with (a `|:`), LAST in the begin-of-line order and
        // so the grob every staff's first-note wish is measured from
        // (BarLine.space-alist (first-note . (semi-shrink-space . 1.3))). Every staff draws
        // it — a tab staff too. BarLine declares no extra-spacing-width of its own
        // (LILYPOND-REF: scm/define-grobs.scm:268-302 BarLine, no extra-spacing-width entry),
        // so its box takes separation-item.cc:166-167's default (-0.1 . 0.1).
        if (columns.HasBar)
            grobs.Add(new PrefatoryGrob(BreakAlignSymbol.StaffBar,
                columns.BarX, columns.BarX + columns.BarWidth,
                -SpacingRules.DefaultExtraSpacingWidth, SpacingRules.DefaultExtraSpacingWidth));

        return grobs;
    }

    /// <summary>
    /// The line-start optical correction of one staff's wish: the FIRST musical column of
    /// every voice of every staff — the musical PaperColumn a Staff_spacing's right-items is —
    /// against that staff's bar, <paramref name="barHalfSpaces"/> high each way; a tab voice
    /// read by the TabVoice's own stem (drawn, or a numbers-only tab's stub).
    /// </summary>
    /// <remarks>
    /// MEASURED (2.26.0, LilySharp-Lab sessions/p661/merge/ls2.ly, a piano staff opening a
    /// system on <c>.|:</c>): the lower staff's down-stem E3 under the upper staff's up-stem c'
    /// puts the first column 0.133646 further off than an up/up opening — the whole
    /// correction, as mid-line. Until session 661 each staff read only its own voices, so the
    /// upper staff's wish took none and the mean halved it.
    /// LILYPOND-REF: lily/separating-line-group-engraver.cc:147-150 Separating_line_group_engraver::stop_translation_timestep — right-items = currentMusicalColumn
    /// LILYPOND-REF: lily/staff-spacing.cc:95-110 Staff_spacing::next_notes_correction.
    /// </remarks>
    private static double ColumnOptical(Model.MultiStaffScore score, int measureIndex, double barHalfSpaces)
    {
        double max = 0;
        foreach (var (_, staff, _) in score.EnumerateStaves())
        {
            if (staff.IsTextRow)
                continue;
            foreach (var voice in staff.Voices)
            {
                if (measureIndex < 0 || measureIndex >= voice.Measures.Length)
                    continue;
                var measure = voice.Measures[measureIndex];
                foreach (var item in measure.Items)
                {
                    if (!SpacingRules.IsMusicalColumn(item))
                        continue;
                    // A line start's first note shows the accidental its broken tie kept.
                    max = Math.Max(max, SpacingRules.StaffSpacingOpticalCorrection(
                        Model.TiedAccidentals.LineStartView(item), staff, measure, barHalfSpaces));
                    break;
                }
            }
        }
        return max;
    }

    /// <summary>
    /// <c>min_dist</c> of ONE staff at a line start that follows a break: each prefatory grob
    /// at its own ink's Y, against the first musical column's real left skyline.
    /// </summary>
    /// <remarks>
    /// The grobs a continuation line opens with are the BROKEN copies of the break column's
    /// items, and the neighbours Pure_from_neighbor_engraver gave the original stay with it:
    /// the copies have none, so <c>extra-spacing-height</c> stretches nothing — the clef's box is
    /// its ink (…-at-beginning-of-line takes the neighbours alone), the key's and the meter's
    /// their ink and the staff (…-including-staff). A first note far outside the staff then
    /// passes under or over the key, and what meets it is its stem.
    /// LILYPOND-REF: lily/pure-from-neighbor-engraver.cc:110-137 Pure_from_neighbor_engraver::finalize — the neighbors
    ///   pointers; scm/output-lib.scm:929-932 extra-spacing-height-at-beginning-of-line and
    ///   :976-979 extra-spacing-height-including-staff;
    /// LILYPOND-REF: scm/define-grobs.scm NonMusicalPaperColumn — no skyline-vertical-padding of
    ///   its own (0), PaperColumn 0.08 (the first column's skyline, ItemSkylineFactory's rod view).
    /// MEASURED (2.26.0, Lab sessions/p851/tie, a bass staff over a 5-string tab in E-flat, the
    /// second line opening on E-flat 2): the line-start key's esh (0 . 0) with 0 neighbours (the
    /// first line's: 22), its band facing the stem, min_dist 7.105 and the first column 8.542;
    /// with the band stretched over the head Lily# put it at 9.13. On the first line the
    /// stretch stands (MinimumDistanceAtLineStart's shared band).
    /// </remarks>
    private static double BrokenLineStartDistance(
        Model.MultiStaffScore score, Model.Staff staff,
        BreakAlignSpacing.PrefixColumns columns,
        double clefGroupLeft, double timeInkWidth, int startMeasureIndex)
    {
        // Device frame (y down) about the staff's middle line, the ItemSkylineFactory frame.
        double half = (Math.Max(staff.Lines, 1) - 1) / 2.0;
        var clef = Rendering.SharedRenderer.ResolveClefAt(staff, startMeasureIndex);
        var boxes = new List<ColumnBox>();
        var (clefChange, keyChange) = OpeningChanges(staff, startMeasureIndex);
        foreach (var g in PrefatoryGrobs(score, staff, columns, clefGroupLeft, timeInkWidth, startMeasureIndex))
        {
            // A CHANGE engraved at this break — a meter (a continuation line prints one only
            // as a change), or a key or clef written at the line's first moment — is created
            // at the break column, keeps its neighbours, and stretches over the first column
            // as on the first line. MEASURED (2.26.0, Lab sessions/p851, Desperado's twin): the
            // 2/4 → 4/4 that opens its third line reports 7 / 12 neighbours and esh
            // (-2.545 . 1.5), where the reprinted clef and key beside it report 0 and their ink.
            bool change = g.Symbol switch
            {
                BreakAlignSymbol.TimeSignature => true,
                BreakAlignSymbol.KeySignature => keyChange,
                BreakAlignSymbol.Clef => clefChange,
                _ => false,
            };
            (double Bottom, double Top) up = change ? (-StretchedBand, StretchedBand) : g.Symbol switch
            {
                // The clef's own ink, about the line it names.
                BreakAlignSymbol.Clef => ClefInkUp(clef),
                // The key's accidentals at their positions, and the staff.
                BreakAlignSymbol.KeySignature => Union(KeyInkUp(score, staff, clef, startMeasureIndex), (-half, half)),
                // The meter's digits (two staff spaces each side of the middle) and the staff.
                BreakAlignSymbol.TimeSignature => Union((-2.0, 2.0), (-half, half)),
                _ => (-half, half),
            };
            boxes.Add(new ColumnBox(-up.Top, -up.Bottom, g.InkLeft + g.EswLeft, g.InkRight + g.EswRight));
        }
        if (boxes.Count == 0)
            return 0.0;

        var right = Skyline(ref t_prefatorySkyline, boxes, HorizontalDirection.Right);
        double worst = 0.0;
        foreach (var voice in staff.Voices)
        {
            if (startMeasureIndex >= voice.Measures.Length)
                continue;
            foreach (var raw in voice.Measures[startMeasureIndex].Items)
            {
                if (!SpacingRules.IsMusicalColumn(raw))
                    continue;
                var item = Model.TiedAccidentals.LineStartView(raw);
                var left = ItemSkylineFactory.CreateLeftSkylineAtColumn(item, 0.0, 0.0);
                worst = Math.Max(worst, right.Distance(left));
                break;
            }
        }
        right.Clear();
        t_prefatorySkyline = right;
        return Math.Max(0.0, worst);

        static (double, double) Union((double B, double T) a, (double B, double T) b)
            => (Math.Min(a.B, b.B), Math.Max(a.T, b.T));
    }

    /// <summary>A stretched box's band in the Y-aware frame: wide enough to face any first
    /// column, as the neighbours' union does.</summary>
    private const double StretchedBand = 1000.0;

    /// <summary>Whether a clef change and a key change are written at the first moment of
    /// <paramref name="staff"/>'s measure <paramref name="measureIndex"/> — engraved in the
    /// line-start prefix as changes rather than reprints.</summary>
    private static (bool Clef, bool Key) OpeningChanges(Model.Staff staff, int measureIndex)
    {
        bool clef = false, key = false;
        foreach (var voice in staff.Voices)
        {
            if (measureIndex >= voice.Measures.Length)
                continue;
            foreach (var item in voice.Measures[measureIndex].Items)
            {
                if (item.Duration > Semantics.Fraction.Zero)
                    break;
                clef |= item is Model.ClefChangeItem;
                key |= item is Model.KeySignatureChangeItem;
            }
        }
        return (clef, key);
    }

    /// <summary>The line-start clef's ink, Y-up about the staff's middle line.</summary>
    private static (double Bottom, double Top) ClefInkUp(Model.ClefType clef)
    {
        var box = GlyphMetrics.LineStartClefBBox(clef);
        double line = 2.0 - Rendering.SharedRenderer.ClefLineBelowTopLine(clef);
        return (line + box.Bottom, line + box.Top);
    }

    /// <summary>The engraved key signature's ink, Y-up about the staff's middle line —
    /// (0, 0) for none.</summary>
    private static (double Bottom, double Top) KeyInkUp(
        Model.MultiStaffScore score, Model.Staff staff, Model.ClefType clef, int startMeasureIndex)
    {
        if (SpacingRules.ActiveKeyForStaff(score, staff, startMeasureIndex) is not { } key)
            return (0.0, 0.0);
        double bottom = double.PositiveInfinity, top = double.NegativeInfinity;
        foreach (var (kind, _, pos) in Rendering.SharedRenderer.KeySignatureGlyphs(key, clef, out _))
        {
            var b = GlyphMetrics.GetAccidentalBBox(kind);
            bottom = Math.Min(bottom, pos / 2.0 + b.Bottom);
            top = Math.Max(top, pos / 2.0 + b.Top);
        }
        return double.IsInfinity(bottom) ? (0.0, 0.0) : (bottom, top);
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
    private static List<ColumnBox> FirstNoteBoxes(Rendering.ScoreTextMetrics fonts, Model.Staff staff, int measureIndex)
    {
        double reachLeft = double.NegativeInfinity;
        double reachRight = 0.0;
        foreach (var voice in staff.Voices)
        {
            if (measureIndex < 0 || measureIndex >= voice.Measures.Length)
                continue;
            foreach (var raw in voice.Measures[measureIndex].Items)
            {
                if (!SpacingRules.IsMusicalColumn(raw))
                    continue;
                // The column is a line start's, so a tie broken here brings back the
                // accidental it swallowed (Model.TiedAccidentals — LilyPond's break
                // reminder, counted only on a line-start column: accidental-placement.cc:86-100 split_accidentals).
                // The break DP and the laid-out system both reach this through
                // LineStartSpring, so the two price the reminder identically.
                var item = Model.TiedAccidentals.LineStartView(raw);
                reachLeft = Math.Max(reachLeft, SpacingRules.MusicalColumnLeftReach(item));
                reachRight = Math.Max(reachRight,
                    SpacingRules.CalculateNoteheadRightExtent(fonts, item)
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
    /// <param name="ideal">The space-alist ideal
    /// (<see cref="BreakAlignSpacing.SpaceAlistDistances"/>'s <c>Ideal</c>).</param>
    /// <param name="fixed_">The space-alist FIXED distance (that same helper's <c>Fixed</c>,
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

    /// <summary>
    /// The line-start prefatory-column → first-note spring for the WHOLE system: one
    /// <c>Staff_spacing</c> wish per staff, merged.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spacing-spanner.cc:492-517 <c>breakable_column_spacing</c> — for a
    /// column pair at the same moment (<c>dt == 0</c>, which a prefatory column and the note
    /// column it opens always are) LilyPond walks the LEFT column's <c>spacing-wishes</c>,
    /// keeps the ones carrying <c>Staff_spacing</c> — one per STAFF — builds a spring from
    /// each with <c>Staff_spacing::get_spacing</c>, and ends in
    /// <c>spring = merge_springs (springs)</c>. Only when there is no wish at all does it
    /// fall back to <c>standard_breakable_column_spacing</c>.
    /// <para>
    /// The staves differ in WHICH grob is extremal and how wide its ink is, so they differ
    /// in the space-alist entry consulted and in the distance it yields. On a system pairing
    /// a notation staff with a NUMBERS-ONLY tab the notation staff ends on its TimeSignature
    /// (semi-shrink-space 2.0) while the tab ends on its TAB clef (minimum-fixed-space 5.0)
    /// — its meter's stencil is blanked, so the grob is skipped for having an empty extent —
    /// and the two ideals are averaged. Taking the notation staff's wish alone put the first
    /// note 0.4 too far right on both halves of the ledger pair
    /// <c>line-start.time-to-first-note.tab-{concert,keyed}</c>, whose twins are bare
    /// TabStaves. A FULL-NOTATION tab ends on its meter like its neighbour and the two
    /// wishes coincide.
    /// </para>
    /// <para>
    /// <c>min_dist</c> is a property of the column PAIR, not of a staff
    /// (<c>Paper_column::minimum_distance (left_col, right_col)</c>, staff-spacing.cc:210),
    /// so every wish is floored against the same <see cref="MinimumDistanceAtLineStart"/>.
    /// The optical correction (staff-spacing.cc:206) needs <c>bar_y_positions</c> of the
    /// extremal grob, which is empty unless that grob is a bar line — so it is 0 on an
    /// ordinary line start and live on one that OPENS WITH A REPEAT, where the drawn
    /// <c>|:</c> is the extremal grob and a down stem right after it earns
    /// <see cref="SpacingRules.BarlineToNextNotesCorrection"/>, exactly as mid-line.
    /// </para>
    /// <para>
    /// THE OPENING BAR LINE AND THE TWO FRAMES. When the opening measure draws a start bar
    /// line, <paramref name="columns"/> carries it as the <c>staff-bar</c> column (after the
    /// meter — scm/define-grobs.scm:668-683 break-align-orders) and the wish is built off ITS ink, as LilyPond's
    /// is: TimeSignature→staff-bar 1.0, the 1.84 of <c>.|:</c>, then BarLine's
    /// <c>(first-note . (semi-shrink-space . 1.3))</c>. MEASURED (2.26.0,
    /// audit/lp-geometry/probes/initial-repeat-bar.ly IR): TIME 4.885+1.7, BAR 7.585+1.84,
    /// HEAD 10.725. But the measure frame this spring is spliced into ALREADY inserts the
    /// measure's own start bar line before spring 0 (<c>MeasureLayouter</c>,
    /// <c>x = startBarlineWidth + positions[i + 1]</c>), so
    /// <paramref name="measureStartBarWidth"/> — that inserted width — is taken OUT of the
    /// returned distances and put INTO the floor's frame, and the two frames agree: first
    /// head = prefix right + measure bar + spring 0 = LilyPond's column. Until session 328
    /// the bar was not in the column at all: the wish was the meter's 2.0 and the bar's
    /// 1.84 was inserted after it for free, which put the opener 0.15 too far right and the
    /// first head 0.30 too close to it (the ledger's OPEN −0.30 on
    /// line-start.time-to-first-note.initial-repeat).
    /// </para>
    /// </remarks>
    /// <param name="measureStartBarWidth">The width the measure frame inserts before spring 0
    /// — <see cref="SpacingRules.GetBarlineWidth"/> of the opening measure's OWN
    /// <c>StartBarline</c>. Usually equal to <c>columns.BarWidth</c>; 0 when the measure
    /// record says None but a <c>|:</c> is still drawn (the begin-of-line piece of a
    /// predecessor's <c>:|:</c>), where the whole column is priced through this spring.</param>
    /// <returns>The merged spring in the caller's MEASURE frame (0 = where the prefix ink
    /// ends, <see cref="BreakAlignSpacing.PrefixColumns.Right"/>, plus the opening measure's
    /// own start bar line width, <paramref name="measureStartBarWidth"/>), which the
    /// measure's spring chain speaks. The wishes themselves are built in LilyPond's
    /// COLUMN-relative frame, because <c>last_ext</c> only means anything there; the shift
    /// between the two is a constant, and a spring's two strengths are differences, so only
    /// the two distances move.</returns>
    public static Spring LineStartSpring(
        Model.MultiStaffScore score,
        BreakAlignSpacing.PrefixColumns columns,
        double clefGroupLeft,
        double timeInkWidth,
        int startMeasureIndex,
        double measureStartBarWidth = 0.0)
    {
        // A grace run opening the line puts its first grace column next to the prefatory one,
        // so min_dist is measured to THAT column and the merged spring runs into the run
        // (IntoGraceRun).
        // (Until session 832 every wish's FIXED distance was also floored at the opening
        // measure's bar-line spring minimum — Lily#'s own stand-in for the grace and lyric
        // columns. With the grace priced as columns it bound only a tab's zigzag digits, by
        // 0.03; a syllable reaches the prefix through LyricSpacing, and LilyPond twins of eleven
        // lyric line starts match with or without it — Lab sessions/p832/lsg.)
        var openingGrace = OpeningGraceRun(score, startMeasureIndex);
        double minDistance = openingGrace != null
            ? MinimumDistanceToGraceAtLineStart(
                score, columns, clefGroupLeft, timeInkWidth, startMeasureIndex)
            : MinimumDistanceAtLineStart(
                score, columns, clefGroupLeft, timeInkWidth, startMeasureIndex);
        // The caller's frame starts where the measure's own start bar line ENDS (see the
        // remarks): prefix right + the inserted bar width.
        double frame = columns.Right + measureStartBarWidth;

        // Lent, and given back at both exits below (see SpacingRules.RentWishes).
        var wishes = SpacingRules.RentWishes();
        foreach (var (_, staff, _) in score.EnumerateStaves())
        {
            // A lyric / chord row is a Lyrics-like context: no Staff_spacing grob, hence no
            // spacing wish — the same set MinimumDistanceAtLineStart walks.
            if (staff.IsTextRow)
                continue;

            var grobs = PrefatoryGrobs(
                score, staff, columns, clefGroupLeft, timeInkWidth, startMeasureIndex);
            if (ExtremalBreakAlignedGrob(grobs) is not { } last)
                continue;

            // staff-spacing.cc:206 next_notes_correction — bar_y_positions is empty for
            // anything but a bar line, so only the opening `|:` earns it.
            // Every staff's first column against THIS staff's bar (ColumnOptical); a tab voice
            // reads the TabVoice's own stems, in its own frame — a numbers-only tab's
            // zero-length stubs included: Stem::is_normal_stem reads heads and duration, not
            // the stencil.
            double optical = last.Symbol != BreakAlignSymbol.StaffBar
                ? 0.0
                : ColumnOptical(score, startMeasureIndex, SpacingRules.BarHalfSpaces(staff));

            wishes.Add(WishFrom(
                last.Symbol, last.InkLeft, last.InkRight, minDistance, optical));
        }

        // No Staff_spacing wish at all — a system made only of chord / lyric rows. Neither
        // Lyrics (ly/engraver-init.ly:632-649) nor ChordNames (:703-725) consists a
        // Clef_engraver, a Key_engraver or a Time_signature_engraver, and neither makes a
        // Staff_spacing grob, so LilyPond's `springs` is empty here too.
        //
        // LILYPOND-REF: lily/spacing-spanner.cc:514-515 — an empty wish list falls to
        // standard_breakable_column_spacing; lily/spacing-basic.cc:71-82 — for a dt == 0
        // pair `ideal = min_dist + 0.5`, returned as `Spring (ideal, min_dist)`, whose
        // default strengths (lily/spring.cc:49-60,198-216) are inverse_stretch = ideal and
        // inverse_compress = max(0, ideal - min_dist). min_dist is
        // std::max (0.0, Paper_column::minimum_distance (l, r)) (spacing-basic.cc:44), and
        // MinimumDistanceAtLineStart gives 0 here because a text row contributes no box —
        // exactly LilyPond, whose left column engraves nothing to measure against.
        //
        // MEASURED (audit/lp-geometry/probes/staffless-system.ly, scores CO/CO3/COK): the
        // first chord name of a staff-less system lands on 0.500000, identical to 15 digits
        // under 4/4 and 3/4 and under a 4-sharp key — that 0.5 and nothing else.
        //
        // ⚠️ What this does NOT port is LilyPond's keep-inside-line rod
        // (lily/simple-spacer.cc:431-432,556-560), which pushes the first column right when
        // a grob on it reaches left of the margin. That is what puts a LEAD SHEET's first
        // column at 2.312539 rather than 0.5 (probe scores CL/CLX/CLL), and it is a general
        // margin constraint on every column of every system, not a staff-less special case —
        // so it belongs with the spacer, not here. See docs/HANDOFF.md section 1.
        if (wishes.Count == 0)
        {
            // LILYSHARP-OWN, the lead-sheet meter (decided divergence 2026-08-20, see
            // SpacingRules.AnyStaffEngravesTime): when the prefix DOES engrave a time
            // signature — the grid row of a staff-less lead sheet — the row takes the
            // SAME wish a staff whose prefix ends on the meter would (semi-shrink off
            // the meter's ink), not LilyPond's bare standard spacing: LilyPond has no
            // meter here at all, so its measured 0.5 is the meterless regime's number
            // and cannot price a column the decision added.
            // LILYSHARP-OWN, the same decision one step on: a text row DOES draw the bar
            // line its measure opens with (DrawBarlines runs on the grid row), so with a
            // `|:` in the column the row wishes off the BAR's ink — the grob the column
            // actually ends on — and the bar's box is the left column's reach for min_dist
            // (a text row contributes no box of its own). LilyPond has no bar line on a
            // ChordNames / Lyrics line at all, so there is no number to pin this to.
            if (columns.HasBar)
            {
                minDistance = Math.Max(minDistance,
                    columns.BarX + columns.BarWidth + 2 * SpacingRules.DefaultExtraSpacingWidth);
                wishes.Add(WishFrom(BreakAlignSymbol.StaffBar,
                    columns.BarX, columns.BarX + columns.BarWidth, minDistance));
            }
            else if (columns.HasTime && timeInkWidth > 0.0)
            {
                wishes.Add(WishFrom(BreakAlignSymbol.TimeSignature,
                    columns.TimeX, columns.TimeX + timeInkWidth, minDistance));
            }
            else
            {
                var standard = StandardBreakableColumnSpacing(minDistance);
                SpacingRules.GiveWishes(wishes);
                return new Spring(
                    standard.IdealDistance - frame, standard.MinDistance - frame,
                    standard.InverseStretchStrength);
            }
        }

        var merged = Spring.MergeSprings(wishes);
        SpacingRules.GiveWishes(wishes);

        if (openingGrace is { } run)
        {
            // A series spring keeps its run's parts through the shift: only the approach moves.
            var series = IntoGraceRun(merged, run, minDistance);
            return series.WithIdealDistance(series.IdealDistance - frame)
                .WithMinDistance(Math.Max(0.0, series.MinDistance - frame));
        }

        return new Spring(
            merged.IdealDistance - frame, merged.MinDistance - frame,
            merged.InverseStretchStrength, merged.InverseCompressStrength);
    }

    /// <summary>
    /// The line-start spring when a grace run opens the line: the prefatory column's merged
    /// wish ENDS AT THE FIRST GRACE COLUMN and is scaled by 0.8, then the run's own springs
    /// follow it in series to the main note. COLUMN frame, like the wish.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spacing-spanner.cc:519-527 Spacing_spanner::breakable_column_spacing — spring *= 0.8 when the right column has a grace part.
    /// LILYPOND-REF: lily/spring.cc:85-93 Spring::operator*= — ideal = max (min, ideal × 0.8) (<see cref="Spring.Scale"/>).
    /// LILYPOND-REF: lily/spacing-spanner.cc:228-297 set_column_rods — the rod over the pair, min_dist + padding.
    /// The same shape a grace run opening a bar mid-line takes off its bar line
    /// (<see cref="SpacingRules.BarlineToFirstColumnSpring"/>) and a change written before a grace
    /// (<see cref="SpacingRules.MidMeasureChangeBeforeGraceSeries"/>).
    /// MEASURED (2.26.0, Lab sessions/p832/lsg, treble staff, ragged, column origin to the first
    /// grace head): a continuation line (clef only) 4.640000 = 5.8 × 0.8, the plain first note's
    /// 5.8 scaled; a first line in 4/4 7.585 = 6.585 + 0.8 + 0.1 + 0.1 — the meter's ink right,
    /// its extra-spacing-width, the grace head's and the rod's padding — where 8.585 × 0.8 = 6.868
    /// falls below the rod. Lily# drew the first grace 0.80 and 1.08 left of those until session
    /// 832: it floored the spring to the MAIN note at the bar-line spring's minimum (the run's
    /// width) and hung the run off it, with no 0.8 and no column of its own.
    /// </remarks>
    private static Spring IntoGraceRun(Spring merged, SpacingRules.GraceColumnLayout run, double minDistance)
    {
        var approach = merged.Scale(SpacingRules.GraceApproachScale);
        int columns = run.Offsets.IsDefaultOrEmpty ? 0 : run.Offsets.Length;
        if (columns == 0 || run.Gaps.IsDefaultOrEmpty)
            return new Spring(approach.IdealDistance + run.Span, approach.MinDistance + run.Span,
                approach.InverseStretchStrength);

        double gapStretch = SpacingRules.GraceSpringInverseStretch();
        var parts = new Spring[columns + 1];
        parts[0] = approach;
        double min = approach.MinDistance;
        for (int k = 0; k < columns; k++)
        {
            parts[k + 1] = new Spring(run.Gap(k), run.Gaps[k].Rod, gapStretch, run.Gaps[k].InverseCompress);
            min += parts[k + 1].MinDistance;
        }
        return Spring.InSeries(System.Collections.Immutable.ImmutableArray.Create(parts), min)
            .WithPartRod(0, minDistance + SpacingRules.SeparationRodPadding);
    }

    /// <summary>
    /// The widest leading grace run on the first musical column of the line's opening measure,
    /// across every staff and voice, as it is PLACED (<see cref="SpacingRules.LeadingGraceRun"/>'s
    /// reading) — or null when no item there leads with a grace.
    /// </summary>
    private static SpacingRules.GraceColumnLayout? OpeningGraceRun(
        Model.MultiStaffScore score, int startMeasureIndex)
    {
        SpacingRules.GraceColumnLayout? widest = null;
        foreach (var (_, staff, _) in score.EnumerateStaves())
        {
            if (staff.IsTextRow)
                continue;
            foreach (var voice in staff.Voices)
            {
                if (FirstMusicalItem(voice, startMeasureIndex) is not { } item)
                    continue;
                var grace = SpacingRules.GraceNotesOf(item);
                if (grace.IsDefaultOrEmpty)
                    continue;
                var run = SpacingRules.GraceColumns(grace, item);
                if (widest is not { } w || run.Span > w.Span)
                    widest = run;
            }
        }
        return widest;
    }

    private static Model.MusicItem? FirstMusicalItem(Model.Voice voice, int measureIndex)
    {
        if (measureIndex < 0 || measureIndex >= voice.Measures.Length)
            return null;
        foreach (var item in voice.Measures[measureIndex].Items)
            if (SpacingRules.IsMusicalColumn(item))
                return item;
        return null;
    }

    /// <summary>
    /// <c>min_dist</c> from a line start's prefatory column to the FIRST GRACE column a grace
    /// run opening the line stands in — <see cref="MinimumDistanceAtLineStart"/> with the grace
    /// heads (and their accidentals) as the right column. A staff with no grace there has nothing
    /// in that column and constrains nothing.
    /// </summary>
    /// <remarks>
    /// The grace column's left reach is <see cref="SpacingRules.GraceColumnLeftReach"/>, the
    /// reading the run's own gaps take.
    /// LILYPOND-REF: lily/paper-column.cc Paper_column::minimum_distance — the two columns' skylines.
    /// </remarks>
    private static double MinimumDistanceToGraceAtLineStart(
        Model.MultiStaffScore score,
        BreakAlignSpacing.PrefixColumns columns,
        double clefGroupLeft,
        double timeInkWidth,
        int startMeasureIndex)
    {
        double worst = 0.0;
        var boxes = new List<ColumnBox>();
        foreach (var (_, staff, _) in score.EnumerateStaves())
        {
            if (staff.IsTextRow)
                continue;
            double reachLeft = double.NegativeInfinity;
            foreach (var voice in staff.Voices)
            {
                if (FirstMusicalItem(voice, startMeasureIndex) is not { } item)
                    continue;
                var grace = SpacingRules.GraceNotesOf(item);
                if (!grace.IsDefaultOrEmpty)
                    reachLeft = Math.Max(reachLeft, SpacingRules.GraceColumnLeftReach(grace[0]));
            }
            if (double.IsNegativeInfinity(reachLeft))
                continue;

            boxes.Clear();
            foreach (var g in PrefatoryGrobs(
                         score, staff, columns, clefGroupLeft, timeInkWidth, startMeasureIndex))
                boxes.Add(new ColumnBox(-SharedBand, SharedBand,
                    g.InkLeft + g.EswLeft, g.InkRight + g.EswRight));
            var grace0 = new List<ColumnBox>
            {
                new ColumnBox(-SharedBand, SharedBand, -reachLeft, SpacingRules.DefaultExtraSpacingWidth),
            };
            worst = Math.Max(worst, MinimumDistance(boxes, grace0));
        }
        return worst;
    }

    /// <summary>
    /// <c>Spacing_spanner::standard_breakable_column_spacing</c> for a <c>dt == 0</c> pair —
    /// the spring LilyPond falls back to when the left column carries no
    /// <c>Staff_spacing</c> wish. COLUMN-relative, like everything else here.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spacing-basic.cc:41-83.
    /// <code>
    ///   Real min_dist = std::max (0.0, Paper_column::minimum_distance (l, r));   // :44
    ///   if (dt == Moment (0, 0)) ideal = min_dist + 0.5;                         // :71-77
    ///   return Spring (ideal, min_dist);                                         // :82
    /// </code>
    /// and lily/spring.cc:49-60,198-216 — <c>Spring (dist, min_dist)</c> takes the DEFAULT
    /// strengths: <c>inverse_stretch_strength = ideal_distance</c> and
    /// <c>inverse_compress_strength = ideal &gt;= min ? ideal - min : 0</c>, which is what the
    /// three-argument <see cref="Spring"/> constructor computes.
    /// <para>
    /// The comment at :73-76 explains the 0.5: "In this case, Staff_spacing should handle the
    /// job, using dt when it is 0 is silly." Where there IS a Staff_spacing this is never
    /// reached; a system of chord / lyric rows has none.
    /// </para>
    /// </remarks>
    public static Spring StandardBreakableColumnSpacing(double minDistance)
    {
        double minDist = Math.Max(0.0, minDistance);
        double ideal = minDist + 0.5;
        return new Spring(ideal, minDist, ideal);
    }

    /// <summary>
    /// One staff's <c>Staff_spacing</c> wish: its extremal prefatory grob's
    /// <c>first-note</c> space-alist entry against that grob's own ink.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/staff-spacing.cc:143-220 — the alist lookup
    /// (<see cref="BreakAlignSpacing.SpaceAlistDistances"/>), the optical correction
    /// (:206-208, <paramref name="opticalCorrection"/> added to BOTH fixed and ideal) and
    /// the <c>0.3 + min_dist</c> correction (<see cref="SpringWithMinimumDistanceFloor"/>)
    /// that ends it.</remarks>
    private static Spring WishFrom(
        BreakAlignSymbol symbol, double inkLeft, double inkRight,
        double minDistance, double opticalCorrection = 0.0)
    {
        var entry = BreakAlignSpacing.GetSpacing(symbol, BreakAlignSymbol.FirstNote);
        var (fixed_, ideal, stretchability) =
            BreakAlignSpacing.SpaceAlistDistances(entry, inkLeft, inkRight);
        fixed_ += opticalCorrection;
        ideal += opticalCorrection;
        return SpringWithMinimumDistanceFloor(ideal, fixed_, stretchability, minDistance);
    }

    /// <summary>
    /// <c>Spacing_interface::extremal_break_aligned_grob</c> with <c>d == LEFT</c>: the
    /// prefatory grob whose ink reaches FURTHEST RIGHT, skipping the ones whose extent is
    /// empty.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spacing-interface.cc:190-230. The comparison is
    /// <c>d * (ext[-d] - last_ext[-d]) &lt; 0</c>, which for <c>d == LEFT</c> reads
    /// <c>ext[RIGHT] &gt; last_ext[RIGHT]</c>, and :219-220 <c>continue</c>s on an empty
    /// extent — the branch that makes a tab staff's stencil-less TimeSignature invisible
    /// here even though the grob is in the shared time column. The walk runs BACKWARDS
    /// (:201 <c>for (vsize i = elts.size (); i--;)</c>) and the comparison is strict, so a
    /// tie is won by the grob LATEST in break-align order; <paramref name="grobs"/> is in
    /// that order.
    /// </remarks>
    private static PrefatoryGrob? ExtremalBreakAlignedGrob(List<PrefatoryGrob> grobs)
    {
        PrefatoryGrob? last = null;
        for (int i = grobs.Count; i-- > 0;)
        {
            if (grobs[i].InkRight <= grobs[i].InkLeft)
                continue;   // ext.is_empty ()
            if (last is not { } l || grobs[i].InkRight > l.InkRight)
                last = grobs[i];
        }
        return last;
    }
}
