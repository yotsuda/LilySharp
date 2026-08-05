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

using System.Collections.Generic;
using System.Linq;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Builds the per-item horizontal skylines (leftmost / rightmost ink extent at each Y)
/// used for note-spacing collision.
/// </summary>
/// <remarks>
/// <para>
/// The shape is LilyPond's: <see cref="ColumnParts"/> lists WHAT THE COLUMN HAS and
/// <see cref="Boxes"/> turns every one of them into a box, exactly as
/// <c>Separation_item::boxes</c> walks the column's <c>elements</c>. ⚠️ PARTICIPATION IS
/// OPT-OUT. A part that reaches somewhere is in the skyline unless something takes it out,
/// and the only things LilyPond takes out are named below.
/// </para>
/// <para>
/// ⚠️ IT USED TO BE OPT-IN — a hand-written list of which parts to include — and that is
/// how the STEM came to be missing from it for as long as it was. The omission was not
/// merely present, it was WRITTEN DOWN AS PORTED: SpacingRules.CalculateNoteheadRightExtent
/// still says "excluding stems and flags" with lily/separation-item.cc:163-164 cited
/// directly underneath, and that reference makes no such exclusion. A citation standing
/// next to an invented rule is worse than no citation, because the next reader stops there.
/// MEASURED, on the book that found it (a bass line in E-flat, `aes,,16 des,8`): LilyPond
/// keeps 0.584700 between the stem's right edge and the flat's ink, Lily# kept 0.045000 —
/// the two heads are 1.5 staff spaces apart vertically, so the ONLY part of that column
/// that the flat could ever meet was the stem.
/// </para>
/// <para>
/// LILYPOND-REF: lily/paper-column-engraver.cc:246-261 Paper_column_engraver::stop_translation_timestep
///   — every acknowledged Item is put
///   into its column's <c>elements</c>; the sole diversions are Accidental_placement and
///   Arpeggio (to <c>conditional-elements</c>) and a bare Accidental (dropped).
/// LILYPOND-REF: lily/separation-item.cc:120-190 Separation_item::boxes — the walk. Axis
///   groups are skipped at :160-161 SO THAT the head, the stem and the dots each enter as
///   their own box rather than one bounding box over all three, and each element's
///   <c>extra-spacing-width</c> is added to its own box at :166-179.
/// LILYPOND-REF: lily/separation-item.cc:89-110 calc_skylines — ONE box list makes the
///   Skyline_pair, so a part reaches in BOTH directions. Lily# used to build the two
///   directions from two different lists (dots and flags on the right only, accidentals on
///   the left only); only the conditional split below is LilyPond's.
/// </para>
/// </remarks>
internal static class ItemSkylineFactory
{
    /// <summary>
    /// Which of a column's parts a skyline is built from — LilyPond's <c>elements</c> vs
    /// <c>conditional-elements</c> split.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/separation-item.cc:112-148 Separation_item::boxes — it returns the
    ///   CONDITIONAL elements when a left grob is given and the ordinary ones otherwise; the
    ///   two sets are disjoint.
    /// LILYPOND-REF: lily/separation-item.cc:47-68 set_distance — the rod takes the left
    ///   column's stored right skyline and merges the right column's CONDITIONAL skyline
    ///   into its left one. So a conditional part only ever faces LEFT, which is why
    ///   <see cref="CreateRightSkyline"/> never asks for one.
    /// </remarks>
    private enum ColumnElements
    {
        /// <summary>The column's ordinary elements: heads, stem, dots, flag.</summary>
        Elements,

        /// <summary>Both sets — the rod's view of the right-hand column.</summary>
        All,
    }

    /// <summary>
    /// One part of a column as it enters the spacing boxes: its ink extent, and the
    /// <c>extra-spacing-width</c> it declares for itself.
    /// </summary>
    /// <remarks>
    /// ⚠️ <c>extra-spacing-height</c> (lily/separation-item.cc:168-169, default (0 . 0)) is
    /// NOT ported: no part here declares one, and the grobs that do in LilyPond — the ones
    /// using (-inf . +inf) to say "never share a Y with a note column" — have no Lily#
    /// counterpart yet. When one arrives it belongs on this record, not in the caller.
    /// </remarks>
    private readonly record struct ColumnPart(
        double YBottom, double YTop, double XLeft, double XRight,
        double ExtraLeft, double ExtraRight, bool Conditional)
    {
        /// <summary>A part taking the default <c>extra-spacing-width</c> (-0.1 . 0.1).</summary>
        public static ColumnPart Ink(double yBottom, double yTop, double xLeft, double xRight)
            => new(yBottom, yTop, xLeft, xRight,
                   -SpacingRules.DefaultExtraSpacingWidth,
                   SpacingRules.DefaultExtraSpacingWidth,
                   Conditional: false);
    }

    /// <summary>
    /// Creates the right skyline for a music item.
    /// The right skyline represents the rightmost extent at each Y coordinate.
    /// </summary>
    /// <param name="item">The music item</param>
    /// <param name="referenceX">X coordinate of the reference point (notehead center)</param>
    /// <param name="staffY">Y coordinate of the staff's middle line</param>
    public static HorizontalSkyline CreateRightSkyline(MusicItem item, double referenceX, double staffY)
        => HorizontalSkyline.FromBoxes(
            Boxes(item, referenceX, staffY, ColumnElements.Elements),
            HorizontalDirection.Right);

    /// <summary>
    /// Creates the left skyline for a music item.
    /// The left skyline represents the leftmost extent at each Y coordinate.
    /// </summary>
    /// <remarks>
    /// ⚠️ THIS TAKES THE CONDITIONAL PARTS TOO, AND ONE OF ITS TWO CALLERS SHOULD NOT.
    /// LilyPond puts accidentals and arpeggios in <c>conditional-elements</c>, which the ROD
    /// merges in and which are ABSENT from the stored <c>horizontal-skylines</c> the SPRING's
    /// minimum reads. So in LilyPond an accidental raises the rod and never the spring floor,
    /// while in Lily# <see cref="SpacingRules.CalculateSkylineDistance"/> (the spring) and
    /// <see cref="SpacingRules.SeparationRodDistance"/> (the rod) both see it.
    /// LILYPOND-REF: lily/spacing-interface.cc:37-82 Spacing_interface::skylines — it reads
    ///   each column's stored <c>horizontal-skylines</c> property and nothing else.
    /// LILYPOND-REF: lily/note-spacing.cc:78-83 Note_spacing::get_spacing — the spring's
    ///   min_dist is that pair's distance, so no conditional part can reach it.
    /// NOT CHANGED HERE: it moves every column that has an accidental, so it wants its own
    /// ledger point and its own measurement, not a quiet ride inside a port that is
    /// otherwise output-preserving.
    /// </remarks>
    public static HorizontalSkyline CreateLeftSkyline(MusicItem item, double referenceX, double staffY)
        => HorizontalSkyline.FromBoxes(
            Boxes(item, referenceX, staffY, ColumnElements.All),
            HorizontalDirection.Left);

    /// <summary>
    /// The column's parts, each widened by its own <c>extra-spacing-width</c>.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/separation-item.cc:152-187 Separation_item::boxes — the loop body, including
    ///   <c>x[LEFT] += extra_width[LEFT]</c> / <c>x[RIGHT] += extra_width[RIGHT]</c> and the
    ///   empty-extent guard at :185.
    /// </remarks>
    private static List<(double YBottom, double YTop, double XLeft, double XRight)> Boxes(
        MusicItem item, double referenceX, double staffY, ColumnElements which)
    {
        var boxes = new List<(double, double, double, double)>();
        foreach (var p in ColumnParts(item, referenceX, staffY))
        {
            if (p.Conditional && which != ColumnElements.All)
                continue;
            boxes.Add((p.YBottom, p.YTop, p.XLeft + p.ExtraLeft, p.XRight + p.ExtraRight));
        }
        return boxes;
    }

    /// <summary>
    /// Everything the column has that reaches somewhere. One entry per drawn part, in no
    /// significant order — a skyline takes the extreme at each Y, so the list is a set.
    /// </summary>
    private static List<ColumnPart> ColumnParts(MusicItem item, double referenceX, double staffY)
    {
        var parts = new List<ColumnPart>();

        int noteValue = SpacingRules.GetNoteValue(item);
        var noteheadBBox = GlyphMetrics.GetNoteheadBBox(noteValue);
        double noteheadLeftX = referenceX - noteheadBBox.CenterX;
        double noteheadWidth = noteheadBBox.Width;
        double maxNoteheadRightX = noteheadLeftX + noteheadWidth;

        if (item is ChordItem chord)
        {
            // Within-chord seconds: reversed heads shift sideways. Use the SAME
            // per-head offsets the renderer and the left skyline use, so the skyline
            // reflects where the heads are actually drawn — a second, simpler
            // displacement model here (keyed by staff position, so unisons and clusters
            // diverged) mispredicted the skyline.
            // LILYPOND-REF: lily/stem.cc:606-760 calc_positioning_done.
            double[] headOffsets = ChordHeadPositioning.CalculateOffsets(
                chord.Notes, chord.StemUp, noteValue);

            for (int i = 0; i < chord.Notes.Length; i++)
            {
                double noteY = staffY - chord.Notes[i].StaffPosition / 2.0;
                double thisLeftX = noteheadLeftX + headOffsets[i];
                double thisRightX = thisLeftX + noteheadWidth;
                parts.Add(ColumnPart.Ink(
                    noteY - noteheadBBox.Top, noteY - noteheadBBox.Bottom, thisLeftX, thisRightX));
                maxNoteheadRightX = Math.Max(maxNoteheadRightX, thisRightX);
            }

            AddAccidentals(parts, chord, noteheadLeftX, staffY, headOffsets, noteValue);
            AddArpeggio(parts, chord, noteheadLeftX, staffY, headOffsets);
        }
        else
        {
            // A rest, and anything else without a staff position, sits on the middle line.
            // ⚠️ A REST TAKES A NOTEHEAD-SHAPED BOX HERE while SpacingRules
            //   .CalculateNoteheadRightExtent gives it the REST glyph's own extent
            //   (lily/rest.cc Rest::width). Two spellings of one quantity, named rather
            //   than fixed: it is pre-existing and changing it is not this port.
            double noteY = item switch
            {
                NoteItem n => staffY - n.StaffPosition / 2.0,
                _ => staffY
            };
            parts.Add(ColumnPart.Ink(
                noteY - noteheadBBox.Top, noteY - noteheadBBox.Bottom,
                noteheadLeftX, noteheadLeftX + noteheadWidth));

            if (item is NoteItem note)
                AddAccidental(parts, note, noteheadLeftX, staffY);
        }

        AddStem(parts, item, noteheadLeftX, staffY);
        AddFlag(parts, item, noteheadLeftX, staffY, noteValue);
        AddDots(parts, item, maxNoteheadRightX, staffY);

        return parts;
    }

    /// <summary>
    /// The STEM. Horizontally it never reaches past the notehead it stands on — an up
    /// stem's right edge IS the head's right edge — so it only ever widens the Y band the
    /// column occupies at that x. A neighbour that used to slip past above or below the
    /// head now meets the stem.
    /// </summary>
    /// <remarks>
    /// <para>
    /// LILYPOND-REF: scm/define-grobs.scm:3429-3474 extra-spacing-width — the Stem grob
    ///   declares none, so it takes the default applied in <see cref="Boxes"/>,
    ///   and <c>X-extent</c> is <c>ly:stem::width</c> = ±thickness/2 about its X-offset.
    /// </para>
    /// <para>
    /// ⚠️ The Y range is the UNBEAMED one, and the beamed branch is NOT PORTED — stated
    /// rather than hidden.
    /// LILYPOND-REF: lily/stem.cc:393-398 Stem::internal_pure_height — with
    ///   <c>calc_beam = false</c> (or no beam at all) the interval is just this stem's own
    ///   <c>internal_height</c>, which is what is ported here.
    /// LILYPOND-REF: lily/stem.cc:399-444 Stem::internal_pure_height — the
    ///   <c>calc_beam = true</c> branch unites it with the pure heights of the beam's other
    ///   same-direction stems. That needs the beam's members and this walk has one item.
    /// LILYPOND-REF: lily/stem.cc:443 Stem::internal_pure_height —
    ///   <c>iv.intersect (overshoot)</c> clips the union back on the NON-stem side, so the
    ///   union can only ever grow the interval past its own end in the stem's direction.
    ///   Therefore the unbeamed range UNDER-reserves and never over-reserves, and it is
    ///   exact whenever the stem is its group's extreme one — the case a collision is
    ///   usually about.
    /// </para>
    /// </remarks>
    private static void AddStem(List<ColumnPart> parts, MusicItem item,
                                double noteheadLeftX, double staffY)
    {
        // Null is exactly "no Stem grob to walk": rests, whole notes.
        // The SAME range is the stem's y-extent for the optical stem correction — one house,
        // because LilyPond reads one Y-extent for both.
        // LILYPOND-REF: lily/separation-item.cc:163 Separation_item::boxes — the box's Y is
        //   the element's <c>pure_y_extent</c>, i.e. the very callback the correction reads.
        if (SpacingRules.StemSpacingInfo(item) is not { } stem)
            return;

        double centreX = LayoutUtilities.StemX(noteheadLeftX, stem.StemUp,
            GlyphMetrics.NoteValueOf(item));
        double half = EngravingDefaults.StemThickness / 2;

        // Staff positions are +up, the box frame is y-down: the MAX position is the
        // numerically smaller y.
        parts.Add(ColumnPart.Ink(
            staffY - stem.StemMax / 2.0, staffY - stem.StemMin / 2.0,
            centreX - half, centreX + half));
    }

    /// <summary>
    /// The FLAG. A BEAMED note has none — its stem joins the beam — so including one
    /// spuriously reserves ~1 notehead of extra horizontal space and makes beamed runs
    /// rod-bound wider than LilyPond.
    /// </summary>
    /// <remarks>
    /// The fact is read from the ITEM, which the collector resolved before any spacing ran
    /// (MeasureCollector.ResolveBeamStemDirections bakes IsBeamed), exactly as LilyPond
    /// reads it from the grobs that exist: its Flag grob has already SUICIDED by spacing
    /// time, so nothing asks "is this beamed?" — the ink simply is not there to be walked.
    /// Taking it as a caller-supplied argument instead let a call site answer WRONGLY: the
    /// line-break gate never passed it, so it priced every beamed note with a flag
    /// (+0.984029 per bar on probe JN) — see MeasureLayouter and
    /// audit/lp-geometry/probes/jn-line-forces.ly.
    /// LILYPOND-REF: lily/stem-engraver.cc:165-172 Stem_engraver::kill_unused_flags — a
    ///   stem with a `beam` object kills its Flag item.
    /// </remarks>
    private static void AddFlag(List<ColumnPart> parts, MusicItem item,
                                double noteheadLeftX, double staffY, int noteValue)
    {
        // A flag is the STEM's, indifferent to how many heads hang on it (LilyPond
        // makes one Flag per Stem), so a chord's flag boxes exactly like a note's,
        // hung from the stem-tip-side head — the same head the renderer reckons the
        // stem from. This used to be NoteItem-only, in step with the renderer's
        // missing chord-flag branch: the two were consistent about the same absence.
        // LILYPOND-REF: lily/stem-engraver.cc:120-140 (Flag per Stem).
        bool stemUp; int tipPos; bool beamed;
        switch (item)
        {
            case NoteItem n:
                stemUp = n.StemUp; tipPos = n.StaffPosition; beamed = n.IsBeamed;
                break;
            case ChordItem c when c.Notes.Length > 0:
                stemUp = c.StemUp; beamed = c.IsBeamed;
                tipPos = c.Notes[0].StaffPosition;
                foreach (var cn in c.Notes)
                    tipPos = stemUp ? Math.Max(tipPos, cn.StaffPosition)
                                    : Math.Min(tipPos, cn.StaffPosition);
                break;
            default:
                return;
        }
        if (noteValue < 8 || beamed)
            return;

        var flagBBox = GlyphMetrics.GetFlagBBox(noteValue, stemUp);
        if (flagBBox == default)
            return;

        double noteY = staffY - tipPos / 2.0;

        // Flag is attached to the stem end
        double stemHeight = EngravingDefaults.IdealStemLength;
        double stemEndY = stemUp ? noteY - stemHeight : noteY + stemHeight;

        // Flag position: a flag hangs on the STEM, so its ink is reserved in the
        // STEM's frame and not the head's — LayoutUtilities.StemX is the one
        // house that spells that x, and it is where SharedRenderer draws it.
        // ⚠️ THE FRAME IS THE STEM'S CENTRE, NOT ITS RIGHT EDGE, and that is not
        //   readable off either callback alone — they are a SELF-CANCELLING PAIR:
        // LILYPOND-REF: lily/flag.cc:198-205 Flag::calc_x_offset — the offset is
        //   stem->extent(stem, X_AXIS)[RIGHT], i.e. +thickness/2.
        // LILYPOND-REF: lily/flag.cc:49-67 Flag::width, stem via get_x_parent —
        //   the DECLARED X-extent is the stencil's extent MINUS that same [RIGHT]
        //   (the file calls it a bad hard-coding and leaves it). Offset + extent
        //   puts the reserved ink back on the stem's centre exactly.
        // LILYPOND-REF: lily/stem.cc:889-906 Stem::width — an is_invisible stem
        //   aside, a stem's own extent is (-1,1)·thickness/2, so that [RIGHT] is
        //   0.065 and nothing else.
        // MEASURED (ledger flag.down.reach.low-neighbour with its
        //   high-neighbour-control): moving ONLY the neighbour's pitch into the
        //   flag's Y band closes LilyPond's gap by 0.172400 and Lily#'s by
        //   0.237400 — Lily# let the neighbour tuck 0.065000 further under the
        //   flag, half a stem thickness to four digits. The PAIR is the reading:
        //   both points also carry a common +0.100 that is a DIFFERENT and still
        //   undiagnosed defect, so neither one alone is this flag.
        // ⚠️ WHAT THIS DOES *NOT* CLOSE, and it is worth writing down before the
        //   next reader rediscovers it and "fixes" this line back: LilyPond's own
        //   draw and reserve disagree here too. lily/flag.cc:118-165 Flag::print
        //   returns the glyph stencil UNTRANSLATED, so it is drawn at the grob's
        //   X-offset — the stem's RIGHT EDGE — while the cancellation above puts
        //   the reserved extent on the stem's CENTRE. SharedRenderer draws this
        //   glyph at the centre (DrawNote/DrawChord pass LayoutUtilities.StemX
        //   straight to DrawGlyph), so Lily#'s flag ink is 0.065 LEFT of
        //   LilyPond's while its spacing now agrees. ⚠️ READ OFF THE SOURCE AND
        //   NOT MEASURED — no ledger point reads a flag's draw x, and the last
        //   read-off-the-source claim on this very line (the 0.065) only became
        //   trustworthy when a pair measured it. Open a point before moving it.
        // ⚠️ BOTH DIRECTIONS, and the UP one was wrong the other way: it read
        //   GlyphMetrics.StemUpSE.X = NoteheadBlackAdvance = 1.304000 where the
        //   stem stands at 1.239200 = the attachment EXTENT 1.304200 − 0.065.
        //   That is the FOURTH site of the advance-versus-extent claim three
        //   others were fixed for; it survived because ledger flag.up.reach read
        //   −1.613200 and hid it, and −1.613200 was not a defect at all but an
        //   OCTAVE in the twin (the .ly said d' = D4 while the Lily# book said
        //   d' = D5, so the two sides were not the same music and Lily#'s column
        //   had a DOWN stem). With the books at one pitch the point read
        //   +0.164800, and 0.164800 − 0.100000 is 0.064800 — this term exactly,
        //   on top of the same common +0.100 the down pair carries.
        // MEASURED: routing both directions through the one house took it to
        //   +0.100000, so all three flag points now read ONE number.
        double stemX = LayoutUtilities.StemX(noteheadLeftX, stemUp, noteValue);

        double flagYBottom, flagYTop;
        if (stemUp)
        {
            // Flag extends downward from stem end
            flagYBottom = stemEndY;
            flagYTop = stemEndY - flagBBox.Bottom - flagBBox.Top;
        }
        else
        {
            // Flag extends upward from stem end
            flagYTop = stemEndY;
            flagYBottom = stemEndY + flagBBox.Top - flagBBox.Bottom;
        }

        parts.Add(ColumnPart.Ink(
            Math.Min(flagYBottom, flagYTop), Math.Max(flagYBottom, flagYTop),
            stemX, stemX + flagBBox.Width));
    }

    /// <summary>Augmentation DOTS, placed after the column's rightmost head.</summary>
    private static void AddDots(List<ColumnPart> parts, MusicItem item,
                                double maxNoteheadRightX, double staffY)
    {
        int dots = SpacingRules.GetDots(item);
        if (dots == 0)
            return;

        var dotBBox = GlyphMetrics.AugmentationDot;
        double dotWidth = dotBBox.Width;
        double dotGap = EngravingDefaults.DotGap;
        double dotRadius = dotBBox.Height / 2;

        // Dots must avoid staff lines - if note is on a line, shift dot up
        IEnumerable<int> positions = item switch
        {
            ChordItem chord => chord.Notes.Select(n => n.StaffPosition),
            NoteItem note => new[] { note.StaffPosition },
            _ => new[] { 1 }  // Default to odd (not on line)
        };

        foreach (int staffPosition in positions)
        {
            double noteY = item is NoteItem or ChordItem ? staffY - staffPosition / 2.0 : staffY;
            double dotYCenter = noteY + ((staffPosition % 2 == 0) ? -0.5 : 0);
            for (int d = 0; d < dots; d++)
            {
                double dotX = maxNoteheadRightX + dotGap + d * (dotWidth + dotGap);
                parts.Add(ColumnPart.Ink(
                    dotYCenter - dotRadius, dotYCenter + dotRadius, dotX, dotX + dotWidth));
            }
        }
    }

    /// <summary>
    /// A single note's ACCIDENTAL — a CONDITIONAL part, and the one that does not take the
    /// default <c>extra-spacing-width</c>: it reaches 0.2 left rather than 0.1.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm AccidentalPlacement
    ///   (extra-spacing-width . (-0.2 . 0.0)).
    /// </remarks>
    private static void AddAccidental(List<ColumnPart> parts, NoteItem note,
                                      double noteheadLeftX, double staffY)
    {
        if (note.Accidental == null)
            return;

        // A note that shares its column with another voice was packed into that column's
        // single accidental column (StaffAccidentalColumns) — in this very frame, since
        // noteheadLeftX is the column. Otherwise it is the only ape and solves alone.
        double? offset = note.AccidentalX;
        if (offset is null)
        {
            var placement = new AccidentalPlacement();
            offset = placement.CalculateSinglePosition(note)?.XOffset;
        }
        if (offset is not { } layoutX)
            return;

        var accBBox = GlyphMetrics.GetAccidentalBBox(note.Accidental);
        double accX = noteheadLeftX + layoutX;
        double noteY = staffY - note.StaffPosition / 2.0;

        parts.Add(Accidental(noteY - accBBox.Top, noteY - accBBox.Bottom,
                             accX, accX + accBBox.Width));
    }

    /// <summary>A chord's ACCIDENTALS, staggered by <see cref="AccidentalPlacement"/>.</summary>
    private static void AddAccidentals(List<ColumnPart> parts, ChordItem chord,
                                       double noteheadLeftX, double staffY,
                                       double[] headOffsets, int noteValue)
    {
        foreach (var (accidental, position, offset) in ChordAccidentalXs(chord, headOffsets))
        {
            var accBBox = GlyphMetrics.GetAccidentalBBox(accidental);
            // The offset is negative (left of notehead), relative to notehead left edge
            double accX = noteheadLeftX + offset;
            double noteY = staffY - position / 2.0;
            parts.Add(Accidental(noteY - accBBox.Top, noteY - accBBox.Bottom,
                                 accX, accX + accBBox.Width));
        }
    }

    /// <summary>
    /// Each of a chord's accidentals as (glyph, staff position, ink-left X from the column):
    /// the packing the whole staff column got when another voice stands on it
    /// (<see cref="Collector.StaffAccidentalColumns"/>), else this chord's own
    /// <c>position_apes</c> solve, which is the same thing when it stands alone.
    /// </summary>
    private static IEnumerable<(string Accidental, int StaffPosition, double X)> ChordAccidentalXs(
        ChordItem chord, double[] headOffsets)
    {
        if (chord.HasPackedAccidentals)
        {
            foreach (var n in chord.Notes)
                if (n.Accidental is { } acc && n.AccidentalX is { } x)
                    yield return (acc, n.StaffPosition, x);
            yield break;
        }

        var placement = new AccidentalPlacement();
        foreach (var layout in placement.CalculatePositions(chord.Notes, headOffsets))
            yield return (layout.Accidental, layout.StaffPosition, layout.XOffset);
    }

    /// <summary>An accidental's part: conditional, and 0.2 of extra width on the left.</summary>
    private static ColumnPart Accidental(double yBottom, double yTop, double xLeft, double xRight)
        => new(yBottom, yTop, xLeft, xRight,
               -SpacingRules.AccidentalExtraSpacingWidthLeft, 0.0, Conditional: true);

    /// <summary>
    /// The ARPEGGIO's wavy line, hanging to the LEFT of the chord — the other conditional
    /// part ("for now only arpeggios", lily/separation-item.cc:135). Reserved with the SAME
    /// constants ArpeggioEngraver places it with, so it does not collide with the barline or
    /// the previous note — without this the arpeggio was drawn but never spaced for, like
    /// grace notes were.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm Arpeggio (direction . LEFT),
    ///   (X-extent . ly:arpeggio::width) — the grob participates in spacing.
    /// </remarks>
    private static void AddArpeggio(List<ColumnPart> parts, ChordItem chord,
                                    double noteheadLeftX, double staffY, double[] headOffsets)
    {
        // ⚠️ A BRACKET RESERVES TOO, and until 2026-08-03 this read only HasArpeggio — which
        // MeasureCollector.HasArpeggioArticulation sets for a plain @arpeggio and not for
        // @arpeggio(bracket) — so a bracketed chord returned here and was drawn with no room
        // kept for it. LilyPond makes no such distinction: lily/arpeggio-engraver.cc:124-129
        // acknowledge_note_column adds whichever of Arpeggio / ChordBracket / ChordSlur
        // process_music built, blind to the type. Measured as audit/lp-geometry
        // chordbracket.x.previous-head-to-bracket.compressed: 0.300000 of column pitch.
        if ((!chord.HasArpeggio && !chord.HasArpeggioBracket) || chord.Notes.Length == 0)
            return;

        // Placed and sized by the SAME house the drawing goes through, so the two cannot
        // drift apart again: until 2026-08-03 this reserved from the head's ink left while
        // ArpeggioEngraver drew from the head's centre, and the wiggle stood most of a
        // head width left of the space kept for it.
        //
        // ⚠️ AND THE COLUMN'S LEFT INCLUDES A REVERSED HEAD. A second in a STEM-DOWN chord
        // puts a head a full width LEFT of the column, the wiggle clears THAT head
        // (ArpeggioEngraver reads the same offsets as minHeadOffset), and a reservation
        // taken from the un-displaced column left would sit a head width right of the ink:
        // measured on test/arpeggio-second, whose last chord is the only stem-down one, the
        // wiggle was drawn ON the previous chord's notehead.
        double minHeadOffset = headOffsets.Length == 0 ? 0 : Math.Min(0, headOffsets.Min());
        double arpRight = noteheadLeftX + minHeadOffset - ArpeggioEngraver.Padding;
        int minPosition = chord.Notes.Min(n => n.StaffPosition);
        int maxPosition = chord.Notes.Max(n => n.StaffPosition);

        // The two grobs have different widths AND different lengths, so the reservation asks
        // the same house the drawing does for whichever one this chord has: a wiggle is the
        // scripts.arpeggio glyph stacked to a whole number of its own heights, a bracket is
        // one shape thick + protrusion across and the head interval widened 0.75 either side.
        //
        // ⚠️ THIS IS THE FRAME BOUNDARY. ArpeggioEngraver answers in Y-UP staff spaces (up
        // positive, from the staff middle) and the skyline runs DEVICE Y-DOWN, so every value
        // crossing here is `staffY − yUp` and the two ends SWAP: the visually TOP end has the
        // numerically SMALLER device y, which is what ColumnPart calls yBottom. Units are
        // staff spaces on both sides; only the direction turns over. SharedRenderer, the other
        // consumer, does NOT convert — it draws in Y-up and the output context flips.
        double arpLeft;
        double arpYBottom, arpYTop;
        if (chord.HasArpeggioBracket)
        {
            arpLeft = arpRight - ArpeggioEngraver.BracketWidth;
            var (bracketBottomYUp, bracketTopYUp) =
                ArpeggioEngraver.BracketExtent(minPosition, maxPosition);
            arpYBottom = staffY - bracketTopYUp;
            arpYTop = staffY - bracketBottomYUp;
        }
        else
        {
            arpLeft = arpRight - ArpeggioEngraver.WiggleWidth;
            var (pileBottomYUp, copies) = ArpeggioEngraver.Pile(minPosition, maxPosition);
            arpYBottom = staffY - (pileBottomYUp + copies * ArpeggioEngraver.WiggleHeight);
            arpYTop = staffY - pileBottomYUp;
        }

        parts.Add(new ColumnPart(arpYBottom, arpYTop, arpLeft, arpRight,
                                 -SpacingRules.DefaultExtraSpacingWidth,
                                 SpacingRules.DefaultExtraSpacingWidth,
                                 Conditional: true));
    }
}
