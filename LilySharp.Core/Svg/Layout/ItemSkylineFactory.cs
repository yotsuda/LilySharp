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
/// Builds the per-item horizontal skyline (rightmost / leftmost ink extent at
/// each Y) used for note-spacing collision. Extracted from the SpacingRules
/// "Skyline Generation" section; shares GetNoteValue/GetDots with SpacingRules.
/// </summary>
internal static class ItemSkylineFactory
{
    /// <summary>
    /// Creates the right skyline for a music item.
    /// The right skyline represents the rightmost extent at each Y coordinate.
    /// </summary>
    /// <param name="item">The music item</param>
    /// <param name="referenceX">X coordinate of the reference point (notehead center)</param>
    /// <param name="staffY">Y coordinate of the staff's middle line</param>
    public static HorizontalSkyline CreateRightSkyline(MusicItem item, double referenceX, double staffY)
    {
        var boxes = new List<(double YBottom, double YTop, double XLeft, double XRight)>();

        int noteValue = SpacingRules.GetNoteValue(item);
        var noteheadBBox = GlyphMetrics.GetNoteheadBBox(noteValue);
        double noteheadCenterX = noteheadBBox.CenterX;
        double noteheadLeftX = referenceX - noteheadCenterX;
        double noteheadWidth = noteheadBBox.Width;
        double maxNoteheadRightX = noteheadLeftX + noteheadWidth;

        if (item is ChordItem chord)
        {
            // Within-chord seconds: reversed heads shift sideways. Use the SAME
            // per-head offsets the renderer and the left skyline use, so the right
            // skyline reflects where the heads are actually drawn — a second,
            // simpler displacement model here (keyed by staff position, so unisons
            // and clusters diverged) mispredicted the skyline.
            // LILYPOND-REF: lily/stem.cc:606-760 calc_positioning_done.
            double[] headOffsets = ChordHeadPositioning.CalculateOffsets(
                chord.Notes, chord.StemUp, noteValue);

            for (int i = 0; i < chord.Notes.Length; i++)
            {
                var noteInfo = chord.Notes[i];
                double noteY = staffY - noteInfo.StaffPosition / 2.0;
                double noteheadYBottom = noteY - noteheadBBox.Top;
                double noteheadYTop = noteY - noteheadBBox.Bottom;

                double thisLeftX = noteheadLeftX + headOffsets[i];
                double thisRightX = thisLeftX + noteheadWidth;

                boxes.Add((noteheadYBottom, noteheadYTop, thisLeftX, thisRightX));
                maxNoteheadRightX = Math.Max(maxNoteheadRightX, thisRightX);
            }

            // Add dots for chord notes (placed after rightmost notehead)
            int chordDots = SpacingRules.GetDots(item);
            if (chordDots > 0)
            {
                var dotBBox = GlyphMetrics.AugmentationDot;
                double dotWidth = dotBBox.Width;
                double dotGap = EngravingDefaults.DotGap;

                foreach (var noteInfo in chord.Notes)
                {
                    double dotYOffset = (noteInfo.StaffPosition % 2 == 0) ? -0.5 : 0;
                    double noteY = staffY - noteInfo.StaffPosition / 2.0;
                    for (int d = 0; d < chordDots; d++)
                    {
                        double dotX = maxNoteheadRightX + dotGap + d * (dotWidth + dotGap);
                        double dotYCenter = noteY + dotYOffset;
                        double dotRadius = dotBBox.Height / 2;
                        boxes.Add((dotYCenter - dotRadius, dotYCenter + dotRadius, dotX, dotX + dotWidth));
                    }
                }
            }
        }
        else
        {
            // Get note Y position
            double noteY = item switch
            {
                NoteItem note => staffY - note.StaffPosition / 2.0,
                _ => staffY
            };

            // Add notehead box
            double noteheadYBottom = noteY - noteheadBBox.Top;
            double noteheadYTop = noteY - noteheadBBox.Bottom;
            boxes.Add((noteheadYBottom, noteheadYTop, noteheadLeftX, noteheadLeftX + noteheadWidth));

            // Add flag if present (8th notes and shorter with stems). A BEAMED note has
            // NO flag — its stem joins the beam — so including one here spuriously
            // reserves ~1 notehead of extra horizontal space and makes beamed runs
            // rod-bound wider than LilyPond.
            // The fact is read from the ITEM, which the collector resolved before any
            // spacing ran (MeasureCollector.ResolveBeamStemDirections bakes IsBeamed),
            // exactly as LilyPond reads it from the grobs that exist: its Flag grob has
            // already SUICIDED by spacing time, so nothing asks "is this beamed?" — the
            // ink simply is not there to be walked. Taking it as a caller-supplied
            // argument instead let a call site answer WRONGLY: the line-break gate never
            // passed it, so it priced every beamed note with a flag (+0.984029 per bar on
            // probe JN) — see MeasureLayouter and audit/lp-geometry/probes/jn-line-forces.ly.
            // LILYPOND-REF: lily/stem-engraver.cc:165-172 Stem_engraver::kill_unused_flags —
            //   a stem with a `beam` object kills its Flag item.
            // LILYPOND-REF: lily/separation-item.cc:130-164 Separation_item::boxes — the
            //   column's skyline boxes are built by walking the grobs that EXIST in it.
            if (item is NoteItem note2 && noteValue >= 8 && !note2.IsBeamed)
            {
                var flagBBox = GlyphMetrics.GetFlagBBox(noteValue, note2.StemUp);
                if (flagBBox != default)
                {
                    // Flag is attached to the stem end
                    double stemHeight = EngravingDefaults.IdealStemLength;
                    double stemEndY = note2.StemUp ? noteY - stemHeight : noteY + stemHeight;

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
                    // ⚠️ THE UP BRANCH IS DELIBERATELY NOT CHANGED WITH IT. It still reads the
                    //   ADVANCE (StemUpSE.X = NoteheadBlackAdvance = 1.304000) where the stem
                    //   stands at 1.239200 = the attachment EXTENT 1.304200 − 0.065, so it is
                    //   wrong by 0.064800 the other way. But ledger flag.up.reach says Lily#
                    //   answers 2.504200 for that column, which is LilyPond's number for the
                    //   same shape with NO flag reach and NO accidental reach in it: an
                    //   up-stem column is not reserving this ink AT ALL. Not reserving it is a
                    //   different defect from reserving it in the wrong PLACE, and it has to
                    //   be measured before it is moved — flag.up.reach must not move here.
                    double stemX = note2.StemUp
                        ? noteheadLeftX + GlyphMetrics.StemUpSE.X
                        : LayoutUtilities.StemX(noteheadLeftX, up: false);

                    double flagYBottom, flagYTop;
                    if (note2.StemUp)
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

                    double flagWidth = flagBBox.Width;
                    boxes.Add((Math.Min(flagYBottom, flagYTop), Math.Max(flagYBottom, flagYTop),
                               stemX, stemX + flagWidth));
                }
            }

            // Add dots if present
            int dots = SpacingRules.GetDots(item);
            if (dots > 0)
            {
                var dotBBox = GlyphMetrics.AugmentationDot;
                double dotWidth = dotBBox.Width;
                double dotGap = EngravingDefaults.DotGap;

                // Dots must avoid staff lines - if note is on a line, shift dot up
                int staffPosition = item switch
                {
                    NoteItem note => note.StaffPosition,
                    _ => 1  // Default to odd (not on line)
                };
                double dotYOffset = (staffPosition % 2 == 0) ? -0.5 : 0;

                for (int d = 0; d < dots; d++)
                {
                    double dotX = maxNoteheadRightX + dotGap + d * (dotWidth + dotGap);
                    double dotYCenter = noteY + dotYOffset;
                    double dotRadius = dotBBox.Height / 2;
                    boxes.Add((dotYCenter - dotRadius, dotYCenter + dotRadius, dotX, dotX + dotWidth));
                }
            }
        }

        // Every spacing box is widened by its grob's own extra-spacing-width before it
        // enters the column's skyline — the INK is untouched, this is spacing width. The
        // default is Interval (-0.1, 0.1) and only the left side has exceptions
        // (see CreateLeftSkyline), so the right side is uniform.
        // LILYPOND-REF: lily/separation-item.cc:166-179 Separation_item::boxes —
        //   `x[RIGHT] += extra_width[RIGHT]`, extra-spacing-width defaulting to
        //   Interval (-0.1, 0.1) at :166-167.
        for (int i = 0; i < boxes.Count; i++)
            boxes[i] = (boxes[i].YBottom, boxes[i].YTop, boxes[i].XLeft,
                        boxes[i].XRight + SpacingRules.DefaultExtraSpacingWidth);

        return HorizontalSkyline.FromBoxes(boxes, HorizontalDirection.Right);
    }

    /// <summary>
    /// Creates the left skyline for a music item.
    /// The left skyline represents the leftmost extent at each Y coordinate.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/separation-item.cc, lily/accidental-placement.cc
    /// For chords, includes all noteheads and uses AccidentalPlacement for proper
    /// staggered accidental positions.
    /// </remarks>
    public static HorizontalSkyline CreateLeftSkyline(MusicItem item, double referenceX, double staffY)
    {
        var boxes = new List<(double YBottom, double YTop, double XLeft, double XRight)>();
        // Accidentals are collected apart because they are the one grob here that does not
        // take the default extra-spacing-width — see the widening at the end.
        var accidentalBoxes = new List<(double YBottom, double YTop, double XLeft, double XRight)>();

        int noteValue = SpacingRules.GetNoteValue(item);
        var noteheadBBox = GlyphMetrics.GetNoteheadBBox(noteValue);
        double noteheadCenterX = noteheadBBox.CenterX;
        double noteheadLeftX = referenceX - noteheadCenterX;
        double noteheadWidth = noteheadBBox.Width;

        if (item is ChordItem chord)
        {
            // Within-chord seconds: reversed heads shift sideways.
            // LILYPOND-REF: lily/stem.cc:606-760 calc_positioning_done.
            double[] headOffsets = ChordHeadPositioning.CalculateOffsets(
                chord.Notes, chord.StemUp, noteValue);

            // Add all noteheads from the chord (at their real, shifted X)
            for (int i = 0; i < chord.Notes.Length; i++)
            {
                double noteY = staffY - chord.Notes[i].StaffPosition / 2.0;
                double noteheadYBottom = noteY - noteheadBBox.Top;
                double noteheadYTop = noteY - noteheadBBox.Bottom;
                double hx = noteheadLeftX + headOffsets[i];
                boxes.Add((noteheadYBottom, noteheadYTop, hx, hx + noteheadWidth));
            }

            // Add accidentals using AccidentalPlacement for proper staggering
            var placement = new AccidentalPlacement();
            var layouts = placement.CalculatePositions(chord.Notes, headOffsets);

            foreach (var layout in layouts)
            {
                var accBBox = GlyphMetrics.GetAccidentalBBox(layout.Accidental);
                double accWidth = accBBox.Width;
                // XOffset is negative (left of notehead), relative to notehead left edge
                double accX = noteheadLeftX + layout.XOffset;

                double noteY = staffY - layout.StaffPosition / 2.0;
                double accYBottom = noteY - accBBox.Top;
                double accYTop = noteY - accBBox.Bottom;
                accidentalBoxes.Add((accYBottom, accYTop, accX, accX + accWidth));
            }

            // Arpeggio wavy line hangs to the LEFT of the chord. Reserve its extent
            // (using the SAME constants ArpeggioEngraver places it with) so it does
            // not collide with the barline or the previous note — without this the
            // arpeggio was drawn but never spaced for, like grace notes were.
            // LILYPOND-REF: scm/define-grobs.scm Arpeggio (direction . LEFT),
            //   (X-extent . ly:arpeggio::width) — the grob participates in spacing.
            if (chord.HasArpeggio && chord.Notes.Length > 0)
            {
                double arpRight = noteheadLeftX - ArpeggioEngraver.Padding;
                double arpLeft = arpRight - 2 * ArpeggioEngraver.WaveAmplitude;
                int maxPos = chord.Notes.Max(n => n.StaffPosition);
                int minPos = chord.Notes.Min(n => n.StaffPosition);
                double arpYBottom = (staffY - maxPos / 2.0) - ArpeggioEngraver.Protrusion;
                double arpYTop = (staffY - minPos / 2.0) + ArpeggioEngraver.Protrusion;
                boxes.Add((arpYBottom, arpYTop, arpLeft, arpRight));
            }
        }
        else if (item is NoteItem note)
        {
            // Single note
            double noteY = staffY - note.StaffPosition / 2.0;
            double noteheadYBottom = noteY - noteheadBBox.Top;
            double noteheadYTop = noteY - noteheadBBox.Bottom;
            boxes.Add((noteheadYBottom, noteheadYTop, noteheadLeftX, noteheadLeftX + noteheadWidth));

            // Add accidental if present
            if (note.Accidental != null)
            {
                var placement = new AccidentalPlacement();
                var layout = placement.CalculateSinglePosition(note);
                if (layout.HasValue)
                {
                    var accBBox = GlyphMetrics.GetAccidentalBBox(layout.Value.Accidental);
                    double accWidth = accBBox.Width;
                    double accX = noteheadLeftX + layout.Value.XOffset;

                    double accYBottom = noteY - accBBox.Top;
                    double accYTop = noteY - accBBox.Bottom;
                    accidentalBoxes.Add((accYBottom, accYTop, accX, accX + accWidth));
                }
            }
        }
        else
        {
            // Rest or other items
            double noteY = staffY;
            double noteheadYBottom = noteY - noteheadBBox.Top;
            double noteheadYTop = noteY - noteheadBBox.Bottom;
            boxes.Add((noteheadYBottom, noteheadYTop, noteheadLeftX, noteheadLeftX + noteheadWidth));
        }

        // The same widening as the right skyline, on the other side — and here one grob
        // declares its own value: an Accidental reaches 0.2 left rather than the default 0.1.
        // LILYPOND-REF: lily/separation-item.cc:166-179 Separation_item::boxes —
        //   `x[LEFT] += extra_width[LEFT]`, the default Interval (-0.1, 0.1) at :166-167;
        //   scm/define-grobs.scm Accidental (extra-spacing-width . (-0.2 . 0.0)).
        for (int i = 0; i < boxes.Count; i++)
            boxes[i] = (boxes[i].YBottom, boxes[i].YTop,
                        boxes[i].XLeft - SpacingRules.DefaultExtraSpacingWidth, boxes[i].XRight);
        foreach (var b in accidentalBoxes)
            boxes.Add((b.YBottom, b.YTop,
                       b.XLeft - SpacingRules.AccidentalExtraSpacingWidthLeft, b.XRight));

        return HorizontalSkyline.FromBoxes(boxes, HorizontalDirection.Left);
    }
}
