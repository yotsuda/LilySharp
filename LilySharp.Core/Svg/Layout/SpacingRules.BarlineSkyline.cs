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
    /// Calculates the minimum distance between two items using skyline collision detection.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/note-spacing.cc:44-86
    /// Uses skylines to find the actual minimum distance where items don't overlap,
    /// considering the shape of noteheads and accidentals at each Y coordinate.
    /// </remarks>
    /// <summary>
    /// Gets the space from a barline to the next item, based on item type.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm BarLine.space-alist
    /// LILYPOND-REF: lily/separation-item.cc:49-70 set_distance()
    ///
    /// Different item types get different amounts of space after a barline:
    ///   next-note:      semi-fixed-space  0.9 (mostly fixed)
    ///   clef:           extra-space       1.0
    ///   key-signature:  extra-space       1.0
    ///   time-signature: extra-space       0.75
    /// `first-note` (semi-shrink-space 1.3) is deliberately absent: LilyPond reads it
    /// only at a system start, which is not this path — see the note on the note arm.
    ///
    /// These are IDEALS, never minimums — see <see cref="GetBarlineToItemMinimum"/> for the
    /// minimum, which used to be taken from here. The `next-note` arm reaches the spring
    /// through EngravingDefaults.BarLineToNextNoteSpace
    /// (<see cref="BarlineToFirstColumnSpring"/>); the clef / key-signature /
    /// time-signature arms belong to break-align spacing, because
    /// Staff_spacing::get_spacing consults only `first-note` and `next-note`
    /// (lily/staff-spacing.cc:147-153) and never keys on the right column's content. Those
    /// arms therefore have no production caller of their own yet — folding them into
    /// BreakAlignSpacing is part of the §3.I role-overlap cleanup in COORDINATE_AUDIT.md.
    /// </remarks>
    public static double GetBarlineToItemSpace(MusicItem? nextItem)
    {
        // LILYPOND-REF: scm/define-grobs.scm BarLine space-alist
        return nextItem switch
        {
            ClefChangeItem => 1.0,             // (clef . (extra-space . 1.0))
            KeySignatureChangeItem => 1.0,     // (key-signature . (extra-space . 1.0))
            TimeSignatureChangeItem => 0.75,   // (time-signature . (extra-space . 0.75))
            // (next-note . (semi-fixed-space . 0.9)). NOT first-note: LilyPond picks
            // `first-note` only when the bar line's break_status_dir differs from
            // CENTER, i.e. at the START OF A SYSTEM — never at an ordinary mid-line
            // bar line, which every measure start inside a system is. Measured on
            // LilyPond 2.24.4: overriding BarLine's `first-note` from 0.0 to 5.0 does
            // not move a single grob in `c'1 c'1`, because that entry is never read
            // there. The system-start case is handled separately, and correctly, by
            // BreakAlignSpacing.FirstNoteSpring (prefix -> first note).
            // LILYPOND-REF: lily/staff-spacing.cc:147-153.
            _ => 0.9
        };
    }

    /// <summary>
    /// Gets the MINIMUM distance from a bar line to the next item — the mirror of
    /// <see cref="GetItemToBarlineSpace"/>, and NOT <see cref="GetBarlineToItemSpace"/>.
    /// </summary>
    /// <remarks>
    /// LilyPond's bar line → column minimum is <c>Paper_column::minimum_distance</c>, a
    /// PURE skyline distance: the bar line reaches its ink right edge + extra-spacing-width
    /// 0.1, the next column's leftmost grob reaches its ink left edge - 0.1, so the gap
    /// beyond the item's own ink is exactly 0.1 + 0.1. No space-alist value enters.
    /// <para>
    /// LilySharp used to feed <see cref="GetBarlineToItemSpace"/>'s 0.9 in here. That is the
    /// `next-note` semi-fixed-space entry, i.e. the IDEAL, and using it as the minimum made
    /// this spring rigid at its ideal and 0.7 ss over-constrained — which in turn is why
    /// merge_springs' headroom could not be applied to it (it would have floored the ideal
    /// at 0.9 + 0.3 and fattened every measure start by 0.3). With the minimum corrected the
    /// headroom is a no-op here: 0.2 + 0.3 &lt; 0.9.
    /// </para>
    /// ⚠️ The change-item arms below kept their space-alist value because
    /// <see cref="CalculateLeftExtent"/>'s change branch used to be on the CENTRE basis. That
    /// justification is GONE — the branch now returns 0, like any other grob whose origin is
    /// its ink left edge — and these arms have not been re-derived against LilyPond since. A
    /// change item reaches them only through a path LilyPond does not have (a change sharing
    /// the LAST timing of a measure, so Lily# measures it toward the closing bar line); no
    /// fixture exercises it. Recorded in the roadmap rather than guessed at.
    /// LILYPOND-REF: lily/staff-spacing.cc:210 <c>Paper_column::minimum_distance</c>;
    ///   lily/separation-item.cc:166-167 default extra-spacing-width
    ///   <c>Interval (-0.1, 0.1)</c>; lily/note-spacing.cc:78-83 sets the spring minimum to
    ///   the padding-free skyline distance.
    /// </remarks>
    public static double GetBarlineToItemMinimum(MusicItem? nextItem)
    {
        return nextItem switch
        {
            ClefChangeItem => 1.0,
            KeySignatureChangeItem => 1.0,
            TimeSignatureChangeItem => 0.75,
            // Bar line's own extra-spacing-width (the default 0.1) plus the LEFTmost grob's.
            // That grob is an accidental whenever the column carries one, and an accidental
            // declares 0.2 rather than the default — see AccidentalExtraSpacingWidthLeft.
            // A head reversed left of the stem is still an ordinary NoteHead and keeps 0.1.
            _ => DefaultExtraSpacingWidth
                 + (HasAccidental(nextItem)
                        ? AccidentalExtraSpacingWidthLeft
                        : DefaultExtraSpacingWidth)
        };
    }

    /// <summary>
    /// Gets the space from the last item in a measure to the barline.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/separation-item.cc:49-70
    /// The distance from item to barline uses BarlinePadding for normal notes,
    /// with extra space for non-musical items.
    /// </remarks>
    public static double GetItemToBarlineSpace(MusicItem? prevItem)
    {
        return prevItem switch
        {
            // ⚠️ These kept their own constant because CalculateNoteheadRightExtent's change
            // branch returned width/2 — the CENTRE basis. It now returns the glyph's full
            // width, so that justification no longer holds and these three have not been
            // re-derived. See the matching note on GetBarlineToItemMinimum: LilyPond has no
            // change-item-to-bar-line pair at all, and no fixture reaches this.
            ClefChangeItem => 1.0,
            KeySignatureChangeItem => 1.0,
            TimeSignatureChangeItem => 1.0,
            // LilyPond's item → boundary minimum is a pure skyline distance between the
            // two columns' boxes, with NO padding term: the left column reaches its ink
            // right edge + extra-spacing-width 0.1, the boundary column's leftmost grob
            // reaches its ink left edge - 0.1. So the gap beyond the item's own ink is
            // exactly 0.1 + 0.1. (The rod adds a further `padding` 0.1, but the rod is
            // not what binds at force >= 0 — see BoundaryClefAllowance / the merge_springs
            // headroom in ApplyMergeSpringsHeadroom.)
            // LILYPOND-REF: lily/separation-item.cc:166-167 default extra-spacing-width
            //   Interval (-0.1, 0.1); lily/note-spacing.cc:78-83 sets the spring minimum
            //   to the padding-free skyline distance.
            _ => 2 * DefaultExtraSpacingWidth
        };
    }

    /// <param name="prevShift">X the LEFT item is drawn at, relative to its column — a
    /// note-collision voice shift. LilyPond's separation boxes are extents in the PAPER
    /// COLUMN's frame, so a shifted voice's ink (and its dots) reaches further right; the
    /// default 0 keeps every existing caller on the unshifted frame.</param>
    /// <param name="nextShift">Same for the RIGHT item.</param>
    public static double CalculateSkylineDistance(MusicItem? prevItem, MusicItem? nextItem,
                                                   double staffY,
                                                   NoteSpacingParameters? noteParams = null,
                                                   double prevShift = 0, double nextShift = 0)
    {
        // LILYPOND-REF: scm/define-grobs.scm — skyline-horizontal-padding (LP default 0.1).
        // LilySharp historically used GlyphMetrics.MinItemGap (0.4) as the static
        // constant; the parameter override path lets callers tune it down for
        // tighter LP-style proportional spacing.
        double minItemGap = noteParams?.MinItemGap ?? MinItemGap;

        // For barline-to-item or item-to-barline, use LP space-alist based calculation
        // LILYPOND-REF: lily/separation-item.cc:49-70 set_distance()
        if (prevItem == null || nextItem == null)
        {
            if (prevItem == null && nextItem != null)
            {
                // Barline → item: the padding-free skyline minimum, NOT the space-alist
                // ideal. LILYPOND-REF: lily/staff-spacing.cc:210.
                double barlinePad = GetBarlineToItemMinimum(nextItem);
                double itemExtent = CalculateLeftExtent(nextItem);
                return barlinePad + itemExtent;
            }
            else if (prevItem != null && nextItem == null)
            {
                // Item → barline: use type-aware barline padding
                double itemExtent = CalculateNoteheadRightExtent(prevItem);
                double barlinePad = GetItemToBarlineSpace(prevItem);
                return itemExtent + barlinePad;
            }
            else
            {
                // Both null (shouldn't happen): return default
                return BarlinePadding * 2 + minItemGap;
            }
        }

        // LilyPond's spring minimum for a note-to-note pair, literally: the distance
        // between the two columns' skylines, taken with the RIGHT column's
        // skyline-vertical-padding, and clamped at 0. No gap is added here — the 0.2 that
        // separates two heads is already in the boxes, as each grob's extra-spacing-width
        // (ItemSkylineFactory). The rod adds a further `padding` on top and takes the
        // distance WITHOUT that vertical padding; that is SeparationRodDistance, not this.
        // LILYPOND-REF: lily/note-spacing.cc:78-83 —
        //   `Real distance = skys[LEFT].distance (skys[RIGHT], skyline-vertical-padding);
        //    Real min_dist = max (0.0, distance); base.set_min_distance (min_dist);`
        // ⚠️ There is no fall-back branch for skylines that do not overlap vertically:
        // LilyPond has none, and the one that used to be here (prevRight + nextLeft + gap)
        // could exceed the skyline answer it was standing in for. Nor is one needed —
        // a non-overlapping pair gives -infinity and max(0, -inf) is 0, which is LilyPond's
        // own answer through its own max.
        return SkylineFloorPair(
            ItemSkylineFactory.CreateRightSkyline(prevItem, prevShift, staffY),
            ItemSkylineFactory.CreateLeftSkyline(nextItem, nextShift, staffY)).SkyMin;
    }

    /// <summary>
    /// LilyPond's TWO constraints over one column pair, computed from skylines the
    /// caller already holds — the ONE home for both clamp shapes.
    /// <c>SkyMin</c> is the SPRING's minimum (distance with the right column's
    /// skyline-vertical-padding, clamped at 0); <c>Rod</c> is the column rod (the
    /// spanner's 0.1 padding plus the padding-free distance, clamped after adding).
    /// <see cref="CalculateSkylineDistance"/> and <see cref="SeparationRodDistance"/>
    /// are these same clamps asked of a bare item pair; a caller that prices MANY
    /// pairs (ApplyCrossVoiceColumnSpacing) builds each item's skylines once and asks
    /// this directly — same numbers, half the skyline construction.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/note-spacing.cc:78-83 (the spring minimum);
    /// LILYPOND-REF: lily/separation-item.cc:47-68 set_distance + lily/spacing-spanner.cc:315-316
    ///   (the rod: <c>dist = padding + …distance (right)</c>, clamped at 0 after).
    /// </remarks>
    internal static (double SkyMin, double Rod) SkylineFloorPair(
        HorizontalSkyline prevRight, HorizontalSkyline nextLeft)
        => (Math.Max(0.0, prevRight.Distance(nextLeft, MusicalColumnSkylineVerticalPadding)),
            Math.Max(0.0, SeparationRodPadding + prevRight.Distance(nextLeft, 0.0)));

    /// <summary>
    /// The ROD between two musical columns: the skyline minimum plus the spacing spanner's
    /// padding. This is the hard floor a compressed line cannot cross, and it is what the
    /// drawn gap saturates at.
    /// </summary>
    /// <remarks>
    /// LilyPond keeps the two apart, and so does this: <see cref="CalculateSkylineDistance"/>
    /// is the SPRING's min_distance (note-spacing.cc:78-83) and this is the rod, raised over
    /// the same pair by Spacing_spanner::set_column_rods.
    /// <para>
    /// ⚠️ The two differ in more than the padding, which is why this does not simply add 0.1
    /// to the other. LilyPond's rod takes the ONE-ARGUMENT <c>Skyline::distance</c> — no
    /// skyline-vertical-padding — and clamps AFTER adding the padding, so a pair whose bare
    /// distance is slightly negative still yields a rod. Reusing the spring's number would
    /// have inherited the 0.08 and clamped in the wrong order; it agrees on two same-pitch
    /// heads (where the boxes overlap in Y outright) and would drift elsewhere.
    /// </para>
    /// MEASURED (audit/lp-geometry/probes/compressed-note-spacing.ly): for two same-pitch
    /// quarters LilyPond's rod is 1.604200 = 0.1 + 1.504200, and every column in that dump
    /// carries exactly that. Spring::length saturates at min_distance (lily/spring.cc:236)
    /// and the rod is the floor under it, so the compressed plateau is this number.
    /// LILYPOND-REF: lily/separation-item.cc:47-68 Separation_item::set_distance —
    ///   <c>Real dist = padding + lines[LEFT][RIGHT].distance (right); … return
    ///   std::max (dist, 0.0);</c>
    /// LILYPOND-REF: lily/spacing-spanner.cc:315-316 generate_springs — the padding passed to
    ///   set_column_rods is the last column's `padding`, defaulting to 0.1.
    /// </remarks>
    public static double SeparationRodDistance(MusicItem? prevItem, MusicItem? nextItem,
                                               double staffY,
                                               NoteSpacingParameters? noteParams = null,
                                               double prevShift = 0, double nextShift = 0)
    {
        // A boundary (bar line) pair is priced by its space-alist, not by a column rod —
        // that is CalculateSkylineDistance's own branch, and it has no separate rod.
        if (prevItem == null || nextItem == null)
            return CalculateSkylineDistance(prevItem, nextItem, staffY, noteParams);

        return SkylineFloorPair(
            ItemSkylineFactory.CreateRightSkyline(prevItem, prevShift, staffY),
            ItemSkylineFactory.CreateLeftSkyline(nextItem, nextShift, staffY)).Rod;
    }

    /// <summary>
    /// Calculates the item's RIGHTward ink reach from its column: the HEAD's right edge,
    /// which is what the bar-line pair is measured from.
    /// </summary>
    /// <remarks>
    /// ⚠️ STEMS AND FLAGS ARE OUT OF THIS ONE, AND THAT IS A FACT ABOUT THE BAR-LINE PAIR,
    /// NOT ABOUT THE COLUMN'S SKYLINE. This doc used to say "excluding stems and flags" with
    /// the separation-item citation below standing right under it, and the next reader (twice)
    /// took that as "LilyPond's column skyline has no stem in it". It has one — every Item in
    /// the column is a box (see <see cref="ItemSkylineFactory"/>), and a bass line in E-flat
    /// found the omission by running a flat through a stem. What LilyPond really reads for
    /// THIS quantity is the first head's own extent:
    /// LILYPOND-REF: lily/note-spacing.cc:46-70 Note_spacing::get_spacing —
    ///   <c>left_head_end = g->extent (col, X_AXIS)[RIGHT]</c> where <c>g</c> is the rest or
    ///   <c>Note_column::first_head</c>, and it is that end the ideal is measured from at :77.
    /// The reference point is the column, which coincides with the note head's LEFT edge —
    /// the same convention <see cref="CalculateLeftExtent"/> documents and LilyPond uses
    /// (dumping <c>ly:grob-relative-coordinate</c> for a PaperColumn and its NoteHead in
    /// 2.24.4 gives the same X). So a plain head reaches its FULL ink width to the right.
    /// LILYPOND-REF: lily/separation-item.cc:163-164 boxes — pure_y_extent over the column's x extent; the spacing box is
    /// <c>il-&gt;extent (pc, X_AXIS)</c>, the grob's extent in its PAPER COLUMN's frame.
    /// LILYPOND-REF: lily/rest.cc Rest::width — the rest branch below uses the same frame.
    /// </remarks>
    internal static double CalculateNoteheadRightExtent(MusicItem item)
    {
        // Mirror of CalculateLeftExtent: the origin is the change glyph's ink left edge, so
        // its rightward reach is its full width — not half of it plus a padding.
        if (IsChangeItem(item))
            return ChangeItemColumnWidth(item);

        int noteValue = GetNoteValue(item);

        // A rest is drawn glyph-left-aligned at its column X (DrawRest: DrawGlyph at x),
        // so its right reach from the column origin is the rest glyph's right edge —
        // wide for a whole/half rest. Using the (smaller) notehead box here let a whole
        // rest's glyph collide with the following barline. LILYPOND-REF: lily/rest.cc
        // Rest::width / generic_extent_callback — the rest stencil's own X-extent feeds
        // the column skyline / separation.
        double extent;
        if (item is RestItem)
        {
            extent = GlyphMetrics.GetRestBBox(noteValue).Right;
        }
        else
        {
            var noteheadBBox = GlyphMetrics.GetNoteheadBBox(noteValue);
            // The column sits at the head's LEFT edge (see the remarks above and
            // CalculateLeftExtent, which returns 0 leftward for the same reason), so the
            // rightward reach is the head's own right edge — mirroring the rest branch.
            // Seeding this with `Width - CenterX` treated the column as if it were at the
            // head's CENTRE, which under-charged a black head by ~0.65 ss; paired with a
            // LEFT extent that had already been converted to the left-edge basis, the two
            // sides of the same box were being measured in different frames.
            extent = noteheadBBox.Right;
        }

        // Add dots if present
        int dots = GetDots(item);
        if (dots > 0)
        {
            var dotBBox = GlyphMetrics.AugmentationDot;
            double dotWidth = dotBBox.Width;
            double dotGap = EngravingDefaults.DotGap;
            extent += dotGap + dots * dotWidth + (dots - 1) * dotGap;
        }

        // A laissez-vibrer half-tie hangs off the head's right ink edge, and its
        // ink is an item of the column like any other, so the spacing boxes carry
        // it — without this the NEXT column's arpeggio ran straight through the
        // tie (laissez-vibrer-arpeggio.ly: LP holds tie-end → arpeggio clear).
        // The tie's span is headRight + xGap .. headRight + OpenReach − xGap
        // (TieVariantEngraver, LP's from_semi_ties open-outline numbers).
        // LILYPOND-REF: lily/separation-item.cc:163-164 Separation_item::boxes —
        //   every item's extent in the paper column's frame joins the spacing box.
        // LILYPOND-REF: lily/tie-formatting-problem.cc:436-441 from_semi_ties.
        // Plain loop, not LINQ: this runs per column in the spacing rods, and
        // an IEnumerable Any() boxes ImmutableArray's enumerator on every call.
        bool hasLv = item is NoteItem { HasLaissezVibrer: true };
        if (item is ChordItem lvChord)
            foreach (var m in lvChord.Notes)
                if (m.HasLaissezVibrer) { hasLv = true; break; }
        if (hasLv)
            extent = Math.Max(extent, GlyphMetrics.GetNoteheadBBox(noteValue).Right
                + TieVariantEngraver.OpenReach - TieDetails.Default.XGap);

        return extent;
    }
}
