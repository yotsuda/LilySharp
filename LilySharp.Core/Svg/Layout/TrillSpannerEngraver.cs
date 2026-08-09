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

/// <summary>
/// Layout information for a trill spanner ("tr" glyph + wavy line extension).
/// All coordinates are in staff spaces.
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/define-grobs.scm:4048-4094 TrillSpanner grob
/// </remarks>
public readonly record struct TrillSpannerLayout(
    // Start measure index (for system Y lookup).
    int StartMeasureIndex,
    // X position of the "tr" glyph.
    double GlyphX,
    // X position where the wavy line starts (after "tr" glyph).
    double LineStartX,
    // X position where the wavy line ends.
    double LineEndX,
    // Y in the LilyPond-native Y-up frame: staff-spaces ABOVE the system top,
    // up-positive (frame B). The renderer reflects it to device against the
    // segment's system top (sy + old-Y == sy − YUp).
    double YUp,
    // Source position for click-to-source mapping.
    int SourcePosition,
    // Global staff index this spanner belongs to (multi-staff). It selects the
    // above-staff stacker's tracker: that pass keeps one per (system, staff), the way
    // LilyPond runs Axis_group_interface::skyline_spacing on each staff's own VerticalAxisGroup.
    // (Until 2026-07-30 the stacker de-collided staff 0 only and lower staves kept
    // their engraver Y — ledger trill.lower-staff.staff-to-line priced that at −2.455.)
    int StaffIndex = 0,
    int SourceIndex = -1,  // F3/B: index into score.TrillSpanners (shared by all broken pieces)
    // Resolved direction: +1 above the staff (the grob default), −1 below. Resolved
    // ONCE here (writer's ^/_ > voice default > UP) so the outside-staff passes and
    // any later consumer read one answer instead of re-deriving it.
    // LILYPOND-REF: scm/scheme-engravers.scm:1818-1820 Trill_spanner_engraver;
    // LILYPOND-REF: scm/music-functions.scm:617-634 direction-polyphonic-grobs;
    // LILYPOND-REF: scm/define-grobs.scm TrillSpanner — (direction . UP), the grob default.
    int Direction = 1
);

/// <summary>
/// Calculates positions for trill spanners (tr symbol + wavy line extension).
/// Handles cross-system spanners by extending to system edge.
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/scheme-engravers.scm Trill_spanner_engraver class
/// LILYPOND-REF: scm/define-grobs.scm:4048-4094 TrillSpanner grob defaults
///
/// TrillSpanner parameters from LilyPond:
/// - direction: UP (always above staff)
/// - style: trill (wavy line)
/// - padding: 0.5
/// - staff-padding: 1.0
/// - outside-staff-priority: 50
/// </remarks>
internal static class TrillSpannerEngraver
{
    /// <summary>
    /// Horizontal padding from bound objects.
    /// </summary>
    /// <remarks>LILYSHARP-OWN: an X-axis bound gap. LilyPond's <c>(padding . 0.5)</c>
    /// at define-grobs.scm:4079 is the VERTICAL side-position padding (now
    /// <c>EngravingDefaults.TrillSpannerPadding</c>); its bound-details declare no
    /// horizontal padding, so this stays a Lily# device with the same value.</remarks>
    private const double BoundPadding = 0.5;

    // The invented TrillGlyphWidth 1.6 / GlyphLinePadding 0.3 are GONE (2026-07-30):
    // the glyph-bearing bound is one LilyPond calculation, not two constants. The bound
    // text attaches at the CENTRE of the bound note column's X extent (bound-details.left
    // attach-dir CENTER, the default), and the line's own left end advances by the bound
    // stencil's X extent on the line's side — the glyph's TRUE (outline) right — with no
    // gap, because TrillSpanner declares no bound-details padding.
    // LILYPOND-REF: lily/line-spanner.cc:155-175 calc_bound_info — attach-dir read from
    //   bound-details, x_coord = the bound grob's extent linear_combination (attach);
    //   :621-626 print — span_points[d] += the bound stencil's extent[-d];
    //   :561-562 print — gaps[d] = the bound-details padding (absent for TrillSpanner).

    /// <summary>
    /// Calculates layout for all trill spanners.
    /// Handles cross-system spanners by extending the wavy line to the system edge.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/scheme-engravers.scm:1798-1868 positioning
    /// LILYPOND-REF: lily/line-spanner.cc:526-648 cross-system spanner handling
    /// </remarks>
    public static ImmutableArray<TrillSpannerLayout> Calculate(
        ImmutableArray<TrillSpannerItem> trillSpanners,
        ImmutableArray<SystemLayout> systems,
        ImmutableArray<MeasureLayout> measureLayouts,
        Func<int, int, double>? staffYAt = null,
        Dictionary<int, ImmutableArray<Voice>>? voicesByStaff = null,
        ImmutableArray<BeamLayout> beamLayouts = default)
    {
        if (trillSpanners.IsDefaultOrEmpty)
            return ImmutableArray<TrillSpannerLayout>.Empty;

        var measureToSystem = SpannerBreakSubstitution.BuildMeasureToSystemMap(systems);
        // Beam membership per (staff, voice, measure, item) — a beamed support
        // column's stem ends at the quanted beam face, not the unbeamed formula
        // (ledger trill.beam-face.staff-to-line). The same map the dynamics' support
        // uses, so the two consumers cannot disagree about who is beamed.
        // LILYPOND-REF: lily/stem.cc:490-497 internal_calc_stem_end_position — a
        //   beamed stem's end fires `quantized-positions` and reads the beam, so the
        //   support extent the trill clears IS the quanted face.
        var beamMembers = DynamicEngraver.BuildBeamMembers(beamLayouts);
        var layouts = ImmutableArray.CreateBuilder<TrillSpannerLayout>();

        for (int ti = 0; ti < trillSpanners.Length; ti++)
        {
            var spanner = trillSpanners[ti];
            if (spanner.StartMeasureIndex >= measureLayouts.Length)
                continue;

            var trillVoices = voicesByStaff != null
                && voicesByStaff.TryGetValue(spanner.StaffIndex, out var vv)
                ? vv : ImmutableArray<Voice>.Empty;

            var startMeasure = measureLayouts[spanner.StartMeasureIndex];
            // Existence is the VOICE's — a later voice's item indices do not exist in
            // the layout's item slots (those are the primary voice's), and reading them
            // there dropped every voice-two trill whose index ran past the primary's
            // count (trill-spanner-direction.ly: the spacer branch has ONE item, so
            // trills 2..4 vanished and the first ran to the measure end).
            if (spanner.StartItemIndex >= VoiceItemCount(
                    trillVoices, spanner.VoiceIndex, spanner.StartMeasureIndex, startMeasure))
                continue;

            double startX = startMeasure.X + VoiceItemX(
                trillVoices, spanner.VoiceIndex, startMeasure,
                spanner.StartMeasureIndex, spanner.StartItemIndex);

            // The grob's direction: the writer's ^/_ wins, then the voice default
            // (TrillSpanner is a direction-polyphonic grob, so an even voice's trills
            // sit BELOW), then the grob's own UP.
            // LILYPOND-REF: scm/scheme-engravers.scm:1818-1820 Trill_spanner_engraver —
            //   the start event's direction is set on the grob over the context's;
            // LILYPOND-REF: scm/music-functions.scm:617-634 direction-polyphonic-grobs
            //   (TrillSpanner is in the list); scm/define-grobs.scm:4076 (direction . UP).
            // ⚠️ The voice default is read at MEASURE granularity (the reach
            //   VoiceDefaults.IsPolyphonicAt has); a trill starting after its span
            //   closed mid-measure would keep the voice's direction — unbound, the same
            //   family as the collector stamp's named approximation.
            int dir = spanner.Direction != 0
                ? spanner.Direction
                : VoiceDefaults.GetDefaultStemUpAt(
                        trillVoices, spanner.VoiceIndex, spanner.StartMeasureIndex)
                    is bool voiceUp ? (voiceUp ? 1 : -1) : 1;
            // The left bound attaches at the CENTRE of the bound note column's X extent
            // (attach-dir CENTER) — the trill's OWN voice's column, since the engraver
            // lives in that Voice context — which Lily# reads as the column X plus the drawn
            // head's INK centre, the same aligned_on_parent quantity the dynamics' anchor
            // spends, so the two consumers cannot disagree about where a column's centre is.
            // ⚠️ It said "half the drawn head's advance" until 2026-08-05 (session 95), and
            // that is what it computed. trill.x.wave-zone.staff-to-line moved
            // -0.000179688 -> -0.000060062 when the shared house was corrected to the ink —
            // a point in THIS island paid for a defect measured in the dynamics' one, which
            // is what sharing the house is for.
            // ⚠️ Named approximation: LilyPond takes the whole NoteColumn's extent, so an
            // accidental or a down stem widens it and shifts this centre.
            double glyphOrigin = startX + DynamicEngraver.AnchorCentreOffset(
                DynamicEngraver.AnchorItem(trillVoices, spanner.VoiceIndex,
                    spanner.StartMeasureIndex, spanner.StartItemIndex));

            // The RIGHT bound under to-barline: a stop event landing on a measure START
            // binds the trill to the BAR LINE before it — TrillSpanner has to-barline
            // = #t, so the Bar_engraver rewrites the right bound to the BarLine item
            // standing at the end timestep, and the spanner never enters the stop
            // measure (the piece list stops a measure early, like the hairpin's).
            // Unlike the hairpin, nothing is then subtracted: TrillSpanner's right
            // bound-details declare (attach-dir . LEFT) and no padding, so the line
            // ends at the bar's ink LEFT edge.
            // LILYPOND-REF: scm/define-grobs.scm TrillSpanner — (to-barline . #t)
            // LILYPOND-REF: lily/bar-engraver.cc:580-588 acknowledge_end_spanner and
            //   :548-558 process_acknowledged — set_bound (RIGHT, bar_)
            // The gate is the BAR's existence, not the measure boundary's: the
            // acknowledger runs only `if (bar_)`, so a boundary whose bar is None
            // keeps the note-column bound.
            bool endsOnMeasureStart = spanner.EndItemIndex == 0
                && spanner.EndMeasureIndex > spanner.StartMeasureIndex
                && spanner.EndMeasureIndex <= measureLayouts.Length
                && EndBarlineOf(spanner.EndMeasureIndex - 1, trillVoices) != BarlineType.None;
            int lastSpannedMeasure = endsOnMeasureStart
                ? spanner.EndMeasureIndex - 1 : spanner.EndMeasureIndex;

            // The broken pieces' X geometry first: aligned_side is POINTWISE, so the Y
            // below is read against these very X ranges.
            // LILYPOND-REF: lily/spanner.cc:36-144 — Spanner::do_break_processing
            var pieces = new List<(SpannerBreakSegment Segment, double GlyphX, double LineStartX, double EndX)>();
            foreach (var (segment, system) in SpannerBreakSubstitution.BrokenPieces(
                spanner.StartMeasureIndex, lastSpannedMeasure, systems, measureToSystem))
            {
                // First segment carries the "tr" glyph; continuation segments draw line only.
                double glyphX, lineStartX;
                if (segment.IsFirst)
                {
                    glyphX = glyphOrigin;
                    lineStartX = glyphX + GlyphMetrics.OrnTrillGlyphOutline.Right;
                }
                else
                {
                    // No glyph on continuation — set GlyphX == LineStartX so the renderer suppresses it.
                    glyphX = system.PrefixWidth + BoundPadding;
                    lineStartX = glyphX;
                }

                double endX;
                if (segment.IsLast)
                {
                    endX = endsOnMeasureStart
                        ? BarlineLeftEdge(spanner.EndMeasureIndex - 1, measureLayouts, trillVoices)
                        : GetEndX(spanner, measureLayouts, trillVoices);
                }
                else
                {
                    endX = system.Width - BoundPadding;
                }

                if (endX <= glyphX)
                    continue;

                // ⚠️ LineEndX stays the BOUND, not the drawn end. The line is a run of
                // glyphs and ends where its last whole element does — short of the bound by
                // the remainder (0.0486 in the TXW dump) — but that fit is ONE LilyPond
                // calculation, run ONCE on the allotted span inside make_trill_line, and
                // every consumer here (this aligned_side, the outside-staff entry, the
                // renderer) reaches it through TrillWaveOutline from this same bound.
                // ⚠️ Storing the FITTED end in the layout instead was tried, and ledger
                // trill.x.wave-zone fell from 4.720541 back to its quiet 3.550000. The
                // mechanism was NOT isolated — it is not the obvious re-fit arithmetic,
                // which measures identical both orders for every span these books use — so
                // this comment records the observation and not a story. Keep the bound.
                // LILYPOND-REF: lily/line-interface.cc:84-102 make_trill_line — the
                //   at-least-one-element rule and total_len.
                pieces.Add((segment, glyphX, lineStartX, endX));
            }
            if (pieces.Count == 0)
                continue;

            // EACH broken piece sides off ITS OWN system's grobs: LilyPond clones the
            // spanner per system and every clone runs aligned_side for itself, so a trill
            // crossing a break sits at a different height on each line. Lily# already emits
            // one layout per piece, so the Y belongs per piece too — and it must be read
            // per piece anyway now that the reading is pointwise, because measure X restarts
            // in every system. (Until 2026-07-30 one Y, the max over the pieces, served them
            // all — safe but not the letter.)
            // LILYPOND-REF: lily/spanner.cc:36-144 Spanner::do_break_processing clones the
            //   spanner per system; scm/define-grobs.scm:4051
            //   ly:spanner::kill-zero-spanned-time is what drops the empty ones.
            foreach (var piece in pieces)
            {
                double lineUp = AlignedSideLineY(
                    spanner, piece.Segment, piece.GlyphX, piece.LineStartX, piece.EndX,
                    trillVoices, measureLayouts, beamMembers, dir);
                // AlignedSideLineY answers in the staff-MIDDLE frame (the frame the ledger's
                // staff-to-line entries read); this record's frame has its origin on the
                // staff TOP line, 2 above it, and carries the staff's own offset within the
                // system — resolved for the piece's OWN starting measure.
                double staffOffset =
                    staffYAt?.Invoke(piece.Segment.StartMeasureIndex, spanner.StaffIndex) ?? 0;
                layouts.Add(new TrillSpannerLayout(
                    piece.Segment.StartMeasureIndex, piece.GlyphX, piece.LineStartX,
                    piece.EndX, lineUp - EngravingDefaults.StaffMiddle - staffOffset,
                    spanner.SourcePosition, spanner.StaffIndex, ti, dir));
            }
        }

        return layouts.ToImmutable();
    }

    /// <summary>
    /// The trill line's resting Y in the staff-MIDDLE frame (up-positive) for one broken
    /// piece: <c>aligned_side</c> transcribed POINTWISE — the piece's spanned note columns
    /// as support skylines at their own X, floored by the staff extent, and the distance
    /// taken against the spanner's OWN facing (DOWN) profile: the flat glyph plateau over
    /// the "tr" glyph's true X extent and the wave over the rest of the piece.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/side-position-interface.cc:188-455 aligned_side, transcribed for
    ///   this grob (side axis Y, dir UP, one staff space per <c>ss</c>):
    /// <code>
    ///   :225-259  my_dim = skyp[-dir], the grob's OWN vertical-skylines on the facing side
    ///   :265-321  every side-support element's skyline merged into dim
    ///   :323-330  if (include_staff) dim.set_minimum_height (staff_extents[dir])
    ///   :354-358  total_off = dir * dim.distance (my_dim, horizon-padding)
    ///   :370      total_off += dir * ss * padding
    ///   :433-453  diff = dir * staff_extent[dir] + staff_padding - dir * total_off;
    ///             total_off += dir * max (diff, 0.0)
    /// </code>
    ///   Two properties in that chain are ABSENT on TrillSpanner rather than zero-valued,
    ///   which is why no term for them is written here: <c>horizon-padding</c> (:354-358
    ///   reads it through <c>get_maybe_pure_property</c> with a 0.0 default) and
    ///   <c>minimum-space</c>, so the :384-385 floor between padding and the refpoint floor
    ///   has nothing to read at all. DynamicLineSpanner DOES declare minimum-space (1.2),
    ///   which is why <c>DynamicEngraver.BaselineY</c> spells that step and this does not.
    ///   The support set is the spanned NOTE COLUMNS — whole columns, not heads and stems
    ///   severally (scm/scheme-engravers.scm:1830 side-support-elements adds the
    ///   note-column-interface grob), so the Stem-direction skip at :273-281 never fires
    ///   here; on the UP side an away-pointing stem's box lies under its own head anyway.
    ///   my_dim is a 2-PIECE profile because that is what the grob's stencil is: the left
    ///   bound text is wrapped so as to "set up a straight line as the vertical skyline for
    ///   the trill glyph" (LilyPond's own comment) — a FLAT plateau at the stencil-offset
    ///   reach 1.0 below the line over the glyph's true X extent — and the line itself is
    ///   the repeated wave over the rest.
    /// LILYPOND-REF: scm/define-grobs.scm:4085 TrillSpanner vertical-skylines
    ///   (grob::unpure-vertical-skylines-from-stencil), :4054-4068 bound-details left text
    ///   (make-with-dimension-from-markup / make-with-true-dimension-markup, stencil-offset).
    /// ⚠️ MEASURED, and it is why this is pointwise and not a scalar max: ledger
    ///   trill.x.wave-zone (TXW) puts an X-away tall column under the WAVE and LilyPond
    ///   reads the QUIET 3.550000 — the column imposes NOTHING because the line's ink ends
    ///   0.0486 left of it — while trill.x.glyph-zone (TXG) puts the same column under the
    ///   GLYPH and reads 8.000000. A scalar edge cannot answer both; it read 8.000000 twice.
    /// The wave part is the real thing: <see cref="TrillWaveOutline"/>, the run of
    ///   scripts.trill_element glyphs LilyPond builds the line from, whose binding value
    ///   depends on WHERE the obstacle starts (there is no constant reach — TXW's ledger
    ///   meets it at -0.160721).
    /// </remarks>
    private static double AlignedSideLineY(
        TrillSpannerItem spanner, in SpannerBreakSegment segment,
        double glyphX, double lineStartX, double endX,
        ImmutableArray<Voice> voices, ImmutableArray<MeasureLayout> measureLayouts,
        Dictionary<(int Staff, int Voice, int Measure, int Item),
            (BeamLayout Beam, double MemberX, bool StemUp)> beamMembers,
        int dir)
    {
        // The DOWN arm (dir = −1) is the same aligned_side with every side flipped: my_dim
        // becomes the grob's UP profile (skyp[-dir]), the supports contribute their DOWN
        // skylines, the staff floor sits at the BOTTOM extent, and the answer leaves with
        // dir's sign. The glyph still hangs its stencil-offset reach BELOW the line in
        // both directions — the offset is (0 . -1) unconditionally (LP oracle
        // trillsdir-lp.svg: tr baseline 0.6 below the line on both sides).
        // LILYPOND-REF: lily/side-position-interface.cc:188-455 aligned_side — every
        //   dir-signed step below cites the same lines the UP transcription does.

        // :225-259 — my_dim: the grob's own FACING profile about its line (Y = 0 here). Its
        // two pieces are kept SEPARATE and their distances maxed below rather than merged
        // into one skyline: the pieces' X ranges are disjoint, so for a facing profile
        // max(distance(plateau), distance(run)) IS distance(merge(plateau, run)) — the
        // merge would only resolve the run's buildings a second time, and a long line has
        // one per glyph edge per copy.
        // MEASURED (min-of-50x3, trill-heavy synthetics): it buys 52.2 -> 49.6 ms and
        // 48.9 -> 46.8 ms — real but SMALL, so the run profile's bulk cost is elsewhere
        // (it is built twice per trill, here and in the stacker, and copied out of the
        // cache each time). Named in HANDOFF with the lever; do not read this comment as
        // "the merge was the problem".
        var facing = dir > 0 ? VerticalDirection.Down : VerticalDirection.Up;
        double reach = EngravingDefaults.TrillSpannerTextOffsetDown;
        VerticalSkyline? plateau = segment.IsFirst
            ? VerticalSkyline.FromBox(
                glyphX + GlyphMetrics.OrnTrillGlyphOutline.Left,
                glyphX + GlyphMetrics.OrnTrillGlyphOutline.Right,
                -reach, GlyphMetrics.OrnTrillGlyph.Top - reach, facing)
            : null;
        // The line's own ink: the run of trill_element glyphs, pointwise.
        var runPair = lineStartX < endX
            ? TrillWaveOutline.Place(lineStartX, endX - lineStartX, 0.0)
            : default((VerticalSkyline Up, VerticalSkyline Down)?);
        VerticalSkyline? run = runPair is { } rp ? (dir > 0 ? rp.Down : rp.Up) : null;

        // :323-330 — the staff symbol's extent is the minimum under whatever the columns
        // contribute (include_staff, which declaring staff-padding turns on) — the
        // dir-side extent: +2 for an UP spanner, −2 for a DOWN one.
        var support = VerticalSkyline.FromBox(
            double.NegativeInfinity, double.PositiveInfinity,
            dir * DynamicEngraver.StaffExtent, dir * DynamicEngraver.StaffExtent,
            dir > 0 ? VerticalDirection.Up : VerticalDirection.Down);
        // ⚠️ NOT LITERAL, and named rather than fixed: LilyPond's support here is each
        // NoteColumn's whole skyline, so every element of the column is in it — dots,
        // accidentals, flags — while ColumnSupportSkylines builds the HEAD and the STEM
        // only. That house is literal where it was written (dynamic-align-engraver.cc:108-117
        // acknowledges rhythmic heads and stems SEVERALLY, so the dynamics' support really
        // is those two), and reusing it here imports the gap. It can only under-reserve, and
        // only when a column element out-reaches both head and stem on the trill's side —
        // an accidental over a low note, say. No probe book has one, so there is no point to
        // gate a fix on: the next step is a book, not a patch.
        // :265-321 — the spanned columns of THIS piece, each at its own X, and only the
        // trill's OWN voice's: Trill_spanner_engraver is a Voice-context engraver
        // (ly/engraver-init.ly:376 \consists) so it acknowledges its own voice's note
        // columns (scm/scheme-engravers.scm:1824-1830 the note-column-interface
        // acknowledger adds them to side-support-elements). Another voice's ink reaches the
        // trill through the outside-staff collision pass over the whole staff profile
        // instead — LilyPond's division of labour, the same one the dynamics' support
        // follows since 2026-07-29. (Until 2026-07-30 this unioned every voice.)
        int voiceIndex = Math.Clamp(spanner.VoiceIndex, 0, Math.Max(0, voices.Length - 1));
        for (int mi = segment.StartMeasureIndex;
             !voices.IsDefaultOrEmpty
                 && mi <= segment.EndMeasureIndex && mi < measureLayouts.Length; mi++)
        {
            var ml = measureLayouts[mi];
            var voice = voices[voiceIndex];
            int count = mi < voice.Measures.Length ? voice.Measures[mi].Items.Length : 0;
            int first = mi == spanner.StartMeasureIndex ? spanner.StartItemIndex : 0;
            int last = mi == spanner.EndMeasureIndex
                ? Math.Min(spanner.EndItemIndex, count - 1)
                : count - 1;
            for (int ii = first; ii <= last; ii++)
            {
                double xColumn = ml.X + VoiceItemX(voices, voiceIndex, ml, mi, ii);
                int cmi = mi, cii = ii;
                var (up, down) = DynamicEngraver.ColumnSupportSkylines(voices, voiceIndex, mi, ii,
                    xColumn,
                    v => beamMembers.TryGetValue((spanner.StaffIndex, v, cmi, cii),
                        out var b) ? b : null);
                support.Merge(dir > 0 ? up : down);
            }
        }

        // :354-358 (total_off = dir * dim.distance(my_dim), horizon-padding absent)
        // and :370 (+= dir * padding) — carried as the dir-FACING magnitude and signed
        // once at the return.
        double overlap = double.NegativeInfinity;
        if (plateau is { } p)
            overlap = Math.Max(overlap, p.Distance(support));
        if (run is { } r)
            overlap = Math.Max(overlap, r.Distance(support));
        double totalOff = overlap + EngravingDefaults.TrillSpannerPadding;
        // :433-453 — the refpoint floor (dir * staff_extent[dir] + staff-padding, against
        // the dir-signed offset — the same magnitude both sides). The trill's reach
        // subsumes it whenever reach > staff-padding - padding, which 1.0 always
        // satisfies; it is written because LilyPond computes it (HANDOFF §5.2: do not
        // fold a term to its value).
        double diff = DynamicEngraver.StaffExtent
            + EngravingDefaults.TrillSpannerStaffPadding - totalOff;
        return dir * (totalOff + Math.Max(diff, 0.0));
    }

    /// <summary>
    /// The line's right end: the stop note column's LEFT edge.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/line-spanner.cc:155-175 calc_bound_info — the right
    ///   bound-details declare <c>(attach-dir . LEFT)</c> (scm/define-grobs.scm:4072-4074),
    ///   so <c>x_coord</c> is the bound column extent's LEFT point, and :561-562 reads no
    ///   bound-details padding for TrillSpanner, so nothing is subtracted from it.
    /// ⚠️ Until 2026-07-30 this spent <see cref="BoundPadding"/> here, and that 0.5 was
    ///   load bearing in the wrong direction: it held the line's ink clear of the stop
    ///   column's LEDGER lines, which reach only length-fraction * head width (0.326) left
    ///   of the column, so Lily#'s outside-staff pass never saw the obstacle LilyPond
    ///   clears. Ledger trill.x.wave-zone is that measurement (its binding support is the
    ///   ledger at 4.100000, not the head or the stem).
    /// ⚠️ LilyPond then shortens the DRAWN line to whole <c>scripts.trill_element</c>
    ///   repetitions (line-interface.cc:86-102 make_trill_line), which is why its dump ends
    ///   0.0486 short of this point. Lily# draws a continuous polyline, so it reaches the
    ///   bound exactly; the remainder goes when the wave becomes the real glyph run.
    /// </remarks>
    /// <summary>
    /// The ink LEFT edge of the bar line closing <paramref name="measureIndex"/> — the
    /// bound a measure-start terminator rewrites to under <c>to-barline</c>.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/line-spanner.cc:155-175 calc_bound_info — the right
    ///   bound-details' <c>(attach-dir . LEFT)</c> takes the bound grob extent's LEFT
    ///   point, and TrillSpanner declares no bound-details padding, so nothing more is
    ///   subtracted. The drawn bar's right edge sits on the measure's right edge
    ///   (the same identity MultiMeasureRestEngraver's frame spends).
    /// </remarks>
    private static double BarlineLeftEdge(
        int measureIndex, ImmutableArray<MeasureLayout> measureLayouts,
        ImmutableArray<Voice> voices)
    {
        var ml = measureLayouts[measureIndex];
        return ml.X + ml.Width
            - EngravingDefaults.BarlineDrawnWidth(EndBarlineOf(measureIndex, voices));
    }

    private static BarlineType EndBarlineOf(int measureIndex, ImmutableArray<Voice> voices)
        => !voices.IsDefaultOrEmpty && measureIndex >= 0
            && measureIndex < voices[0].Measures.Length
            ? voices[0].Measures[measureIndex].EndBarline
            : BarlineType.Single;

    private static double GetEndX(TrillSpannerItem spanner,
        ImmutableArray<MeasureLayout> measureLayouts, ImmutableArray<Voice> voices)
    {
        if (spanner.EndMeasureIndex < measureLayouts.Length)
        {
            var endMeasure = measureLayouts[spanner.EndMeasureIndex];
            if (spanner.EndItemIndex < VoiceItemCount(
                    voices, spanner.VoiceIndex, spanner.EndMeasureIndex, endMeasure))
                return endMeasure.X + VoiceItemX(
                    voices, spanner.VoiceIndex, endMeasure,
                    spanner.EndMeasureIndex, spanner.EndItemIndex);
            // No stop column: the bound is the bar line (to-barline #t). Lily# stops a
            // BoundPadding short of the measure's end — a device, kept (LILYSHARP-OWN,
            // named at the constant), because no point measures a barline-bound trill.
            return endMeasure.X + endMeasure.Width - BoundPadding;
        }
        var lastMeasure = measureLayouts[^1];
        return lastMeasure.X + lastMeasure.Width - BoundPadding;
    }

    /// <summary>
    /// How many items the spanner's OWN voice has in a measure — the frame its item
    /// indices live in. The layout's item slots are the primary voice's; falling back to
    /// them keeps the historical single-voice behaviour when no voices were supplied.
    /// </summary>
    private static int VoiceItemCount(
        ImmutableArray<Voice> voices, int voiceIndex, int measureIndex, MeasureLayout ml)
        => !voices.IsDefaultOrEmpty
            && voiceIndex >= 0 && voiceIndex < voices.Length
            && measureIndex < voices[voiceIndex].Measures.Length
            ? voices[voiceIndex].Measures[measureIndex].Items.Length
            : ml.Items.Length;

    /// <summary>
    /// A voice item's X within its measure layout (measure-relative). The PRIMARY
    /// voice's items are the layout's own item slots; a later voice's map through their
    /// musical timing — the same alignment the cross-voice beam collisions use.
    /// LILYPOND-REF: the note column IS shared across voices at one moment (one
    /// PaperColumn per timestep), so timing is the identity that finds it.
    /// </summary>
    private static double VoiceItemX(
        ImmutableArray<Voice> voices, int voiceIndex, MeasureLayout ml,
        int measureIndex, int itemIndex)
    {
        if (voiceIndex <= 0 || voices.IsDefaultOrEmpty || voiceIndex >= voices.Length
            || measureIndex >= voices[voiceIndex].Measures.Length)
            return itemIndex < ml.Items.Length ? ml.Items[itemIndex].X : 0.0;

        var items = voices[voiceIndex].Measures[measureIndex].Items;
        var t = Fraction.Zero;
        for (int i = 0; i < itemIndex && i < items.Length; i++)
            t += items[i] switch
            {
                NoteItem n => n.Duration,
                ChordItem c => c.Duration,
                RestItem r => r.Duration,
                _ => Fraction.Zero,
            };
        return ml.GetXForTiming(t);
    }
}
