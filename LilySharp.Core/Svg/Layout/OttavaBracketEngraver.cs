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
using LilySharp.Core.Rendering;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Layout information for an ottava bracket.
/// All coordinates are in staff spaces.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/ottava-bracket.cc Ottava_bracket::print()
/// LILYPOND-REF: scm/define-grobs.scm:2708-2731 OttavaBracket grob defaults
/// </remarks>
public readonly record struct OttavaBracketLayout(
    // Start measure index (for system Y lookup).
    int StartMeasureIndex,
    // Start X position (staff spaces from score start).
    double StartX,
    // End X position.
    double EndX,
    // Y in the Y-up frame: staff-spaces ABOVE the system top, up-positive (frame B).
    // The renderer reflects it to device against the segment's system top
    // (sy + old-Y == sy − YUp).
    double YUp,
    // Display text (e.g., "8va", "8vb", "15ma", "15mb").
    string Text,
    // Whether the bracket is above the staff (true) or below (false).
    bool IsAbove,
    // Edge height for the end hook (in staff spaces).
    double EdgeHeight,
    // Dash period for the dashed line.
    double DashPeriod,
    // Dash fraction for the dashed line.
    double DashFraction,
    // Source position for click-to-source mapping.
    int SourcePosition,
    // F3/B: index of the originating ottava mark in score.MusicMarks,
    // so a reused layout re-derives data-pos from the live score. -1 = unresolved.
    int SourceIndex = -1,
    // The staff this bracket belongs to (0 = the first/only staff). It selects the
    // above-staff stacker's tracker — one per (system, staff), as LilyPond runs
    // Axis_group_interface::skyline_spacing per VerticalAxisGroup — so the bracket clears its OWN
    // staff's ink. (Until 2026-07-30 lower-staff brackets were held out of that pass
    // and fell back to the bare staff-padding floor, drawing through their noteheads:
    // ledger ottava.lower-staff.staff-to-line, −1.727520.)
    int StaffIndex = 0
);

/// <summary>
/// Calculates positions for ottava brackets.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/ottava-bracket.cc Ottava_bracket::print()
/// LILYPOND-REF: scm/define-grobs.scm:2708-2731 OttavaBracket grob defaults
///
/// OttavaBracket parameters from LilyPond:
/// - dash-fraction: 0.3
/// - edge-height: (0 . 0.8) — no hook at start, 0.8 staff spaces hook at end
/// - staff-padding: 2.0
/// - padding: 0.5
/// - shorten-pair: (-0.8 . -0.6)
/// - minimum-length: 0.3
/// - font-series: bold
/// - font-shape: italic
/// </remarks>
internal static class OttavaBracketEngraver
{
    /// <summary>
    /// Dash fraction for the dashed line.
    /// </summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:2710 (dash-fraction . 0.3)</remarks>
    private const double DashFraction = 0.3;

    /// <summary>
    /// Dash period (implicit from LilyPond's default line rendering).
    /// </summary>
    private const double DashPeriod = 2.0;

    /// <summary>
    /// Edge height at the end (right hook).
    /// </summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:2711 (edge-height . (0 . 0.8))</remarks>
    private const double EndEdgeHeight = 0.8;

    /// <summary>
    /// Staff padding — minimum distance from staff. One home: EngravingDefaults'
    /// outside-staff declaration table (the LILYPOND-REF lives beside the entry).
    /// </summary>
    private const double StaffPadding = EngravingDefaults.OttavaBracketStaffPadding;

    /// <summary>
    /// Y-up (above the system top) for 8va/15ma brackets — the staff-padding FLOOR,
    /// measured from the staff's INK edge, not its top line's centre.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/side-position-interface.cc:401-453 aligned_side — "Ensure
    /// 'staff-padding' from my refpoint to the staff" floors the refpoint (the bracket
    /// LINE) at <c>staff_extent[dir] + staff_padding</c>, and the staff's extent is its
    /// ink (2.05 about the middle). Measured six-digit (audit/lp-geometry
    /// ottava.floor.staff-to-line: 4.050000 = 2.05 + 2.0); the bare
    /// <c>StaffPadding</c> here read 4.000000, half a line thickness low.
    /// </remarks>
    private const double AboveStaffYUp =
        StaffPadding + EngravingDefaults.StaffLineThickness / 2.0;

    /// <summary>
    /// Y-up (below the system top, so negative) for 8vb/15mb brackets:
    /// StaffHeight (4) + the same ink-edge floor below the staff.
    /// </summary>
    /// <remarks>
    /// The same single claim as <see cref="AboveStaffYUp"/> — aligned_side reads
    /// <c>staff_extent[DOWN]</c>, which is the ink edge too. ⚠️ No ledger point measures
    /// the below-staff regime; this moves with the above side because splitting one
    /// claim's two halves is how HANDOFF 5.0's cap/baseline trap fires.
    /// </remarks>
    private const double BelowStaffYUp =
        -(4.0 + StaffPadding + EngravingDefaults.StaffLineThickness / 2.0);

    /// <summary>
    /// Left shorten (extends bracket slightly left).
    /// </summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:2717 (shorten-pair . (-0.8 . -0.6))</remarks>
    private const double LeftShorten = -0.8;

    /// <summary>
    /// Right shorten (extends bracket slightly right).
    /// </summary>
    private const double RightShorten = -0.6;

    /// <summary>
    /// The gap between the label's right edge and the dashed line's left end.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/ottava-bracket.cc:124-135 Ottava_bracket::print —
    /// <c>text_size = text.extent (X_AXIS)[RIGHT] + 0.3</c>, and the source's own comment
    /// beside it is "0.3 is ~ italic correction".
    /// MEASURED: ledger ottava.x.line-start-to-notehead — the 0.5 spent off the advance
    /// until 2026-08-02 put the line 2.937897638 too far right in the OTC book, which the
    /// bracket's 2.0-early bound then partly cancelled.</remarks>
    private const double LabelLineItalicCorrection = 0.3;

    /// <summary>
    /// Where the dashed line starts: past the label's ADVANCE plus
    /// <see cref="LabelLineItalicCorrection"/>. The one spelling the draw and the
    /// reservations share.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE ADVANCE, NOT THE INK, and the distinction was got wrong here once (ported as
    /// the ink on 2026-08-02, corrected the same day). <c>text.extent (X_AXIS)</c> is a
    /// STENCIL X-extent, and LilyPond builds a text stencil's box with X from Pango's
    /// LOGICAL rectangle and only Y from the ink one —
    /// LILYPOND-REF: lily/pango-font.cc:351-362 Pango_font::pango_item_string_stencil,
    /// <c>Box (Interval (PANGO_LBEARING (logical_rect), PANGO_RBEARING (logical_rect)),
    /// Interval (-PANGO_DESCENT (ink_rect), PANGO_ASCENT (ink_rect)))</c>. A text grob's
    /// left edge is therefore its pen origin, which ledger
    /// <c>textscript.x.pen-to-notehead-left</c> pins from the other side for two strings
    /// with different first-glyph bearings.
    /// MEASURED (audit/lp-geometry/probes/text-advance.ly, ledger <c>text.width.*</c>): the
    /// arithmetic says the same thing without the source — LilyPond's widths are whole
    /// 1200-dpi pixels of 0.034143307086614 ss, the ottava book's derived 1.213302362 for a
    /// bold-italic "8" is not one (35.539), and the ADVANCE is exactly 37 of them
    /// (1.263302362). The 0.05 between them is the drawn dashed line's half thickness: the
    /// OTC dump read the LINE'S STENCIL, not <c>bracket_span_points[LEFT]</c>.
    /// </remarks>
    internal static double LineStartX(ScoreTextMetrics fonts, string text, double startX,
        double fontSize)
        => startX
           + fonts.Advance(text, fontSize, TextRole.Ottava, FontStyle.BoldItalic)
           + LabelLineItalicCorrection;

    /// <summary>
    /// The bracket's OWN vertical skyline pair about its LINE at <paramref name="lineY"/>:
    /// the label's glyph OUTLINE, the dashed rule, and the end hook — the three pieces
    /// LilyPond's stencil is built from, with the gap between label and line left EMPTY.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:2721 OttavaBracket grob::unpure-vertical-skylines-from-stencil
    ///   — the profile is the STENCIL's, not an extent box.
    /// LILYPOND-REF: lily/ottava-bracket.cc <c>Ottava_bracket::print</c> — the text is
    ///   centred on the line (<c>text.align_to (Y_AXIS, CENTER)</c>), the line is built at
    ///   the stencil's own Y=0 and the bracket's Y-box is erased ("vertical lines should
    ///   not take space"), so only the drawn ink is in the profile.
    /// ⚠️ ONE profile does THREE jobs — the aligned_side distance, the collision pass's
    /// move, and the entry a later grob clears — for the reason the trill's does
    /// (<see cref="TrillSpannerEngraver"/>): LilyPond hands the same <c>v_skylines</c> to
    /// both passes, so a second spelling is a defect waiting for a book.
    /// MEASURED (ottava-floor.ly OTC): the flat box at the HOOK's depth this replaced
    /// over-reserved by 0.067480009 at the binding x, which was the mover's half of
    /// ledger ottava.support.staff-to-line.
    /// </remarks>
    internal static (VerticalSkyline Up, VerticalSkyline Down) Skylines(
        ScoreTextMetrics fonts,
        string text, double startX, double lineStartX, double endX,
        double edgeHeight, bool isAbove, double lineY)
    {
        double fontSize = EngravingDefaults.OttavaBracketFontSize;
        double half = EngravingDefaults.StaffLineThickness / 2.0;
        // The label's ink is CENTRED on the line, so its baseline sits that much below.
        var (up, down) = TextOutlineSkylines.Place(
            text, fontSize, fonts.Face(TextRole.Ottava, FontStyle.BoldItalic),
            startX, lineY - LabelInkCentre(fonts, text, fontSize));
        if (lineStartX < endX)
        {
            up.Merge(VerticalSkyline.FromBox(
                lineStartX, endX, lineY - half, lineY + half, VerticalDirection.Up));
            down.Merge(VerticalSkyline.FromBox(
                lineStartX, endX, lineY - half, lineY + half, VerticalDirection.Down));
        }
        if (edgeHeight > 0)
        {
            // The hook reaches TOWARD the staff, and it is drawn as a rule of the line's
            // thickness standing at EndX (SharedRenderer.DrawOttavaBrackets), so that is
            // what gets reserved.
            double tip = lineY + (isAbove ? -edgeHeight : edgeHeight);
            double lo = Math.Min(lineY - half, tip);
            double hi = Math.Max(lineY + half, tip);
            up.Merge(VerticalSkyline.FromBox(
                endX - half, endX + half, lo, hi, VerticalDirection.Up));
            down.Merge(VerticalSkyline.FromBox(
                endX - half, endX + half, lo, hi, VerticalDirection.Down));
        }
        return (up, down);
    }

    /// <summary>How far the label's ink CENTRE sits above its baseline — the offset the
    /// draw and the skylines both apply so the ink lands centred on the line.</summary>
    /// <remarks>MEASURED: ledger ottava.label.line-to-ink-centre — LilyPond's answer is 0
    /// by construction and Lily#'s was +0.621000054, the baseline sitting where the centre
    /// belongs.</remarks>
    internal static double LabelInkCentre(ScoreTextMetrics fonts, string text, double fontSize)
    {
        var ink = fonts.Ink(text, fontSize, TextRole.Ottava, FontStyle.BoldItalic);
        return (ink.Top + ink.Bottom) / 2.0;
    }

    /// <summary>
    /// Calculates layout for all ottava brackets.
    /// </summary>
    public static ImmutableArray<OttavaBracketLayout> Calculate(
        ScoreTextMetrics fonts,
        ImmutableArray<OttavaBracketItem> ottavaBrackets,
        ImmutableArray<SystemLayout> systems,
        ImmutableArray<MeasureLayout> measureLayouts,
        Func<int, int, double>? staffYAt = null,
        Dictionary<int, ImmutableArray<Voice>>? voicesByStaff = null,
        ImmutableArray<BeamLayout> beamLayouts = default)
    {
        if (ottavaBrackets.IsDefaultOrEmpty)
            return ImmutableArray<OttavaBracketLayout>.Empty;

        // LILYPOND-REF: lily/ottava-bracket.cc — brackets split at system breaks.
        var measureToSystemIdx = SpannerBreakSubstitution.BuildMeasureToSystemMap(systems);
        // A beamed support column's stem ends at the quanted beam face — the same map the
        // dynamics and the trill read, so the three consumers cannot disagree about who is
        // beamed.
        var beamMembers = DynamicEngraver.BuildBeamMembers(beamLayouts);
        var layouts = ImmutableArray.CreateBuilder<OttavaBracketLayout>();

        foreach (var bracket in ottavaBrackets)
        {
            if (bracket.StartMeasureIndex >= measureLayouts.Length)
                continue;

            int endMeasureIdx = Math.Min(bracket.EndMeasureIndex, measureLayouts.Length - 1);

            bool isAbove = bracket.Type == OttavaType.Ottava8va ||
                           bracket.Type == OttavaType.Quindicesima15ma;
            // Offset to this bracket's OWN staff on a grand staff, so an 8va over
            // the lower staff sits above THAT staff, not the top one. Single-staff
            // (offset 0) is unchanged. Mirrors HairpinEngraver/TrillSpannerEngraver.
            double staffOffset = staffYAt?.Invoke(bracket.StartMeasureIndex, bracket.StaffIndex) ?? 0;

            var ottavaVoices = voicesByStaff != null
                && voicesByStaff.TryGetValue(bracket.StaffIndex, out var vv)
                ? vv : ImmutableArray<Voice>.Empty;

            string text = bracket.Type switch
            {
                OttavaType.Ottava8va => "8va",
                OttavaType.Ottava8vb => "8vb",
                OttavaType.Quindicesima15ma => "15ma",
                OttavaType.Quindicesima15mb => "15mb",
                _ => "8va"
            };

            foreach (var (segment, _) in SpannerBreakSubstitution.BrokenPieces(
                bracket.StartMeasureIndex, endMeasureIdx, systems, measureToSystemIdx))
            {
                if (segment.StartMeasureIndex >= measureLayouts.Length ||
                    segment.EndMeasureIndex >= measureLayouts.Length)
                    continue;

                var segStartMeasure = measureLayouts[segment.StartMeasureIndex];
                var segEndMeasure = measureLayouts[segment.EndMeasureIndex];

                // The LEFT BOUND is the ottava's own start NOTE COLUMN, shortened by
                // LeftShorten — not the measure's origin, which on a first measure sits a
                // clef and a time signature to the left of it (ledger
                // ottava.x.label-to-notehead read -2.800000000 against LilyPond's
                // -0.800000000 while this was the measure, and -2.0 was exactly that gap).
                // LILYPOND-REF: lily/ottava-bracket.cc:121-176 Ottava_bracket::print —
                //   span_points[LEFT] = ext[LEFT], ext being the union of the BOUND NOTE
                //   COLUMN's note-heads' X extents, then span_points[d] -= d * shorten[d].
                // ⚠️ Named approximation: LilyPond unites the note-HEADS' extents, so a
                //   chord with a shifted head starts its bracket further left than the
                //   column's X; Lily# reads the column. No book has such a chord under an
                //   ottava — the next step there is a book, not a widening.
                // ⚠️ A CONTINUATION piece keeps the measure's origin: its bound is broken,
                //   and LilyPond's broken branch takes generic_bound_extent and sets
                //   shorten to 0, which is a different arithmetic with no point on it yet.
                double startX = segment.IsFirst && bracket.StartItemIndex >= 0
                        && bracket.StartItemIndex < segStartMeasure.Items.Length
                    ? segStartMeasure.X + segStartMeasure.Items[bracket.StartItemIndex].X
                        + LeftShorten
                    : segStartMeasure.X + LeftShorten;
                double endX = segEndMeasure.X + segEndMeasure.Width + RightShorten;

                // First segment shows the bare text ("8va"); continuation pieces use "(8va)".
                // Last segment carries the hook; non-last ends are open.
                string segText = segment.IsFirst ? text : $"({text})";
                double segEdgeHeight = segment.IsLast ? EndEdgeHeight : 0;

                // EACH broken piece sides off ITS OWN system's columns, the way LilyPond's
                // per-system clone does — and it must, now that the reading is pointwise:
                // measure X restarts in every system.
                // LILYPOND-REF: lily/spanner.cc:36-144 Spanner::do_break_processing.
                double lineMiddleFrame = AlignedSideLineY(
                    fonts, segText, startX, endX, segEdgeHeight, isAbove,
                    segment, ottavaVoices, measureLayouts, beamMembers, bracket.StaffIndex);
                // aligned_side answers in the staff-MIDDLE frame (the frame the ledger's
                // staff-to-line entries read); this record's frame has its origin on the
                // staff TOP line, 2 above it, and carries the staff's own within-system
                // downward offset, which SUBTRACTS.
                double yUp = lineMiddleFrame - 2.0 - staffOffset;

                layouts.Add(new OttavaBracketLayout(
                    StartMeasureIndex: segment.StartMeasureIndex,
                    StartX: startX,
                    EndX: endX,
                    YUp: yUp,
                    Text: segText,
                    IsAbove: isAbove,
                    EdgeHeight: segEdgeHeight,
                    DashPeriod: DashPeriod,
                    DashFraction: DashFraction,
                    SourcePosition: bracket.SourcePosition,
                    SourceIndex: bracket.SourceIndex,
                    StaffIndex: bracket.StaffIndex
                ));
            }
        }

        return layouts.ToImmutable();
    }

    /// <summary>
    /// Where the bracket's LINE sits, in the staff-MIDDLE frame (up-positive), BEFORE the
    /// outside-staff collision pass: LilyPond's <c>aligned_side</c> for this grob.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/side-position-interface.cc:188-455 aligned_side, transcribed for
    ///   OttavaBracket (side axis Y, one staff space per <c>ss</c>):
    /// <code>
    ///   :225-259  my_dim = skyp[-dir], the grob's OWN vertical-skylines on the facing side
    ///   :265-321  every side-support element's skyline merged into dim
    ///   :323-330  if (include_staff) dim.set_minimum_height (staff_extents[dir])
    ///   :354-358  total_off = dir * dim.distance (my_dim, horizon-padding)
    ///   :370      total_off += dir * ss * padding          (OttavaBracket's 0.5)
    ///   :433-453  diff = dir * staff_extent[dir] + staff_padding - dir * total_off;
    ///             total_off += dir * max (diff, 0.0)       (the 2.0 floor)
    /// </code>
    ///   <c>horizon-padding</c> and <c>minimum-space</c> are ABSENT on OttavaBracket rather
    ///   than zero-valued, so no term is written for them (:354-358 defaults 0.0, and
    ///   :384-385 has nothing to read).
    /// ⚠️ The support set is the STAFF's note columns — ALL its voices, not one — because
    ///   Ottava_spanner_engraver lives in the Staff context, unlike the trill's and the
    ///   dynamics' Voice-context engravers.
    /// LILYPOND-REF: ly/engraver-init.ly:77 <c>\consists Ottava_spanner_engraver</c> inside
    ///   the Staff block — DrumStaff and TabStaff are the two contexts that
    ///   <c>\remove Ottava_spanner_engraver</c>, which is the same claim read backwards;
    ///   scm/scheme-engravers.scm the note-column-interface acknowledger is what puts the
    ///   columns into <c>side-support-elements</c>.
    /// ⚠️ NOT LITERAL, and named rather than fixed: the columns enter through
    ///   <see cref="DynamicEngraver.SpanSupportSkylines"/>, which builds the HEAD and the
    ///   direction-matching STEM only, where LilyPond's support is each NoteColumn's whole
    ///   skyline (dots, accidentals, flags too). The gap is inherited from that house and
    ///   can only UNDER-reserve; no probe book has a column whose dot or accidental
    ///   out-reaches both head and stem on the bracket's side, so the next step is a book,
    ///   not a patch.
    /// ⚠️ The BELOW side (8vb / 15mb) runs the same claim with dir = −1 and has NO ledger
    ///   point — the ottava books are all above-staff. It moves with the above side because
    ///   this IS one claim: splitting its two halves is the trap HANDOFF 5.0 records under
    ///   cap/baseline, and the pre-2026-08-02 code already treated the floor that way.
    /// </remarks>
    private static double AlignedSideLineY(
        ScoreTextMetrics fonts,
        string text, double startX, double endX, double edgeHeight, bool isAbove,
        in SpannerBreakSegment segment, ImmutableArray<Voice> voices,
        ImmutableArray<MeasureLayout> measureLayouts,
        Dictionary<(int Staff, int Voice, int Measure, int Item),
            (BeamLayout Beam, double MemberX, bool StemUp)> beamMembers,
        int staffIndex)
    {
        double dir = isAbove ? 1.0 : -1.0;
        // :225-259 — my_dim, the grob's own profile about its line (Y = 0 here).
        var my = Skylines(
            fonts,
            text, startX,
            LineStartX(fonts, text, startX, EngravingDefaults.OttavaBracketFontSize),
            endX, edgeHeight, isAbove, 0.0);

        // :265-330 — the spanned columns of THIS piece, every voice of the staff, floored
        // by the staff symbol's own extent (include_staff, which declaring staff-padding
        // turns on).
        var support = DynamicEngraver.SpanSupportSkylines(
            voices, 0, Array.Empty<(int, int, double)>(), null);
        for (int vi = 0; vi < voices.Length; vi++)
        {
            var columns = new List<(int Measure, int Item, double X)>();
            for (int mi = segment.StartMeasureIndex;
                 mi <= segment.EndMeasureIndex && mi < measureLayouts.Length; mi++)
            {
                var ml = measureLayouts[mi];
                int count = mi < voices[vi].Measures.Length
                    ? voices[vi].Measures[mi].Items.Length : 0;
                for (int ii = 0; ii < count; ii++)
                    columns.Add((mi, ii, ml.X + (ii < ml.Items.Length ? ml.Items[ii].X : 0.0)));
            }
            if (columns.Count == 0)
                continue;
            int voiceIndex = vi;
            var (vUp, vDown) = DynamicEngraver.SpanSupportSkylines(
                voices, voiceIndex, columns,
                (v, m, i) => beamMembers.TryGetValue((staffIndex, v, m, i), out var b)
                    ? b : null);
            support.Up.Merge(vUp);
            support.Down.Merge(vDown);
        }

        // :354-358 (horizon-padding absent) and :370.
        double overlap = isAbove
            ? my.Down.Distance(support.Up)
            : my.Up.Distance(support.Down);
        double totalOff = dir * overlap + dir * EngravingDefaults.OttavaBracketPadding;
        // :433-453 — the refpoint floor. Unlike the trill's, this one really binds over a
        // quiet staff: the label's downward reach (about 0.79) is well under
        // staff-padding − padding = 1.5 (ledger ottava.floor.staff-to-line = 2.05 + 2.0).
        double diff = DynamicEngraver.StaffExtent
            + EngravingDefaults.OttavaBracketStaffPadding - dir * totalOff;
        totalOff += dir * Math.Max(diff, 0.0);
        return totalOff;
    }

    /// <summary>
    /// Detects ottava bracket spans from music marks.
    /// An ottava starts at an 8va/8vb/15ma/15mb mark and ends at
    /// loco or the next ottava mark.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/ottava-engraver.cc process_music() and stop_translation_timestep()
    /// </remarks>
    public static ImmutableArray<OttavaBracketItem> DetectOttavaBrackets(
        ImmutableArray<MusicMarkItem> musicMarks)
    {
        var brackets = ImmutableArray.CreateBuilder<OttavaBracketItem>();

        // F3/B: keep each mark's ORIGINAL index in musicMarks (== score.MusicMarks) so the
        // bracket can re-derive its data-pos from the live score on reuse.
        var ottavaMarks = musicMarks
            .Select((m, i) => (Mark: m, Index: i))
            .Where(x => x.Mark.Type == MusicMarkType.OttavaUp ||
                        x.Mark.Type == MusicMarkType.OttavaDown ||
                        x.Mark.Type == MusicMarkType.QuindicesUp ||
                        x.Mark.Type == MusicMarkType.QuindicesDown ||
                        x.Mark.Type == MusicMarkType.Loco)
            .OrderBy(x => x.Mark.MeasureIndex)
            .ToList();

        if (ottavaMarks.Count == 0)
            return ImmutableArray<OttavaBracketItem>.Empty;

        // Walk through marks: each non-loco mark starts a bracket,
        // terminated by the next ottava/loco mark
        for (int i = 0; i < ottavaMarks.Count; i++)
        {
            var (mark, srcIndex) = ottavaMarks[i];

            // Skip loco marks (they only terminate, don't start)
            if (mark.Type == MusicMarkType.Loco)
                continue;

            OttavaType type = mark.Type switch
            {
                MusicMarkType.OttavaUp => OttavaType.Ottava8va,
                MusicMarkType.OttavaDown => OttavaType.Ottava8vb,
                MusicMarkType.QuindicesUp => OttavaType.Quindicesima15ma,
                MusicMarkType.QuindicesDown => OttavaType.Quindicesima15mb,
                _ => OttavaType.Ottava8va
            };

            // Find the end: next ottava/loco mark ON THE SAME STAFF. On a grand
            // staff each staff runs its own ottava, so a loco under the lower
            // staff must not terminate an 8va over the upper staff.
            // LILYPOND-REF: lily/ottava-engraver.cc — per-staff Ottava_spanner_engraver;
            //   bracket ends just before the terminating mark, so use measure - 1.
            MusicMarkItem? terminator = null;
            for (int j = i + 1; j < ottavaMarks.Count; j++)
                if (ottavaMarks[j].Mark.StaffIndex == mark.StaffIndex)
                {
                    terminator = ottavaMarks[j].Mark;
                    break;
                }

            int endMeasure;
            if (terminator != null)
            {
                // Bracket covers up to the measure before the terminator
                endMeasure = terminator.MeasureIndex - 1;
                if (endMeasure < mark.MeasureIndex)
                    endMeasure = mark.MeasureIndex; // at minimum, cover the start measure
            }
            else
            {
                // No end found — extend to one measure after the start
                endMeasure = mark.MeasureIndex + 1;
            }

            if (endMeasure >= mark.MeasureIndex)
            {
                brackets.Add(new OttavaBracketItem(
                    Type: type,
                    StartMeasureIndex: mark.MeasureIndex,
                    EndMeasureIndex: endMeasure,
                    SourcePosition: mark.SourcePosition,
                    SourceIndex: srcIndex,
                    StaffIndex: mark.StaffIndex,
                    // The note the mark was written on IS the spanner's left bound. The
                    // collector already anchors ottava marks to their host column
                    // (MeasureCollector.Annotations, the compound-mark path), so this is a
                    // hand-over, not a new resolution.
                    StartItemIndex: mark.AnchorItemIndex
                ));
            }
        }

        return brackets.ToImmutable();
    }
}
