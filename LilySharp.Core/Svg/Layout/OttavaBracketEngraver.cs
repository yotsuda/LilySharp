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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
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
    /// Dash period: OttavaBracket declares none, so Line_interface's default of one staff
    /// space applies (the drawing then fits it to the line, SharedRenderer.FittedDash).
    /// </summary>
    /// <remarks>
    /// MEASURED 2.26.0 (Lab sessions/p1388/m o1): a 7.519 line dashes 0.309 every 1.030 —
    /// seven whole periods of about 1.0. This was 2.0 until 2026-11-25.
    /// LILYPOND-REF: lily/line-interface.cc:245-246 Line_interface::line (dash-period default 1.0).
    /// </remarks>
    private const double DashPeriod = 1.0;

    /// <summary>
    /// Edge height at the end (right hook).
    /// </summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:2711 (edge-height . (0 . 0.8))</remarks>
    private const double EndEdgeHeight = 0.8;

    /// <summary>Whether this score draws its ottavas' lines as a wave with no hook
    /// (<c>layout { ottavaLine wave }</c>). The run is the trill's element, the one line-interface.cc
    /// builds for <c>style = #'trill</c> whichever grob asks (<see cref="TrillWaveOutline"/>).</summary>
    internal static bool Wave => EngravingStyle.Current.OttavaWave;

    /// <summary>How far the drawn line reaches either side of its Y: the wave's ink, or half the rule.</summary>
    internal static double LineReach
        => Wave ? TrillWaveOutline.InkReach : EngravingDefaults.StaffLineThickness / 2.0;

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
    private static double AboveStaffYUp =>
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
    private static double BelowStaffYUp =>
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

    /// <summary>A moment past any bar's end: "every column of the bar" for
    /// <see cref="StaffLastColumnBefore"/> when a span runs to its bar's end.</summary>
    private static readonly Fraction WholeBar = new(1 << 20);

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
    /// The shortest dashed line the label leaves before the right end: a span too short for
    /// its label slides the LABEL left until this much line remains.
    /// </summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm OttavaBracket (minimum-length . 0.3), read by
    /// lily/ottava-bracket.cc:131-134 Ottava_bracket::print —
    /// <c>span_points[LEFT] = min (span_points[LEFT], span_points[RIGHT] - text_size - minimum-length)</c>.</remarks>
    private const double MinimumLength = 0.3;

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
           + fonts.Advance(text, fontSize, TextRole.Ottava, LabelStyle(fonts))
           + LabelLineItalicCorrection;

    /// <summary>The label's em for THIS score: <see cref="EngravingDefaults.OttavaBracketFontSize"/>
    /// unless the score's <c>fonts { }</c> wrote a <c>step</c> or <c>size</c> for <c>ottava</c>
    /// (or <c>numbers</c>). The draw, the bracket's own skyline pair and the line's start all
    /// read it (the draw scales it once more for an ossia).</summary>
    internal static double LabelEm(ScoreTextMetrics fonts)
        => fonts.Size(TextRole.Ottava, EngravingDefaults.OttavaBracketFontSize);

    /// <summary>The label's weight and slant: bold italic (OttavaBracket's font-series and
    /// font-shape) unless the score wrote a style.</summary>
    internal static FontStyle LabelStyle(ScoreTextMetrics fonts)
        => fonts.Style(TextRole.Ottava, FontStyle.BoldItalic);

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
        double fontSize = LabelEm(fonts);
        double half = EngravingDefaults.StaffLineThickness / 2.0;
        // The label's ink is CENTRED on the line, so its baseline sits that much below.
        var (up, down) = TextOutlineSkylines.Place(
            text, fontSize, fonts.Face(TextRole.Ottava, LabelStyle(fonts)),
            startX, lineY - LabelInkCentre(fonts, text, fontSize));
        if (lineStartX < endX)
        {
            if (Wave)
            {
                var (waveUp, waveDown) = TrillWaveOutline.Place(lineStartX, endX - lineStartX, lineY);
                up.Merge(waveUp);
                down.Merge(waveDown);
            }
            else
            {
                up.MergeBox(lineStartX, endX, lineY - half, lineY + half);
                down.MergeBox(lineStartX, endX, lineY - half, lineY + half);
            }
        }
        if (edgeHeight > 0)
        {
            // The hook reaches TOWARD the staff, and it is drawn as a rule of the line's
            // thickness standing at EndX (SharedRenderer.DrawOttavaBrackets), so that is
            // what gets reserved.
            double tip = lineY + (isAbove ? -edgeHeight : edgeHeight);
            double lo = Math.Min(lineY - half, tip);
            double hi = Math.Max(lineY + half, tip);
            up.MergeBox(endX - half, endX + half, lo, hi);
            down.MergeBox(endX - half, endX + half, lo, hi);
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
        var ink = fonts.Ink(text, fontSize, TextRole.Ottava, LabelStyle(fonts));
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
        ImmutableArray<BeamLayout> beamLayouts = default,
        Func<int, int>? staffLinesOf = null,
        ImmutableArray<GraceNoteItem> graceNotes = default,
        // A staff's rest shifts (by staff index): a rest moved off the staff is a support where
        // it was moved to (DynamicEngraver.MergeColumnSupport). Absent, no rest is read.
        Func<int, IReadOnlyDictionary<RestShiftKey, double>?>? restShiftsOf = null)
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
            // The offset to this bracket's OWN staff on a grand staff (so an 8va over the lower
            // staff sits above THAT staff) is read per broken piece below.

            var ottavaVoices = voicesByStaff != null
                && voicesByStaff.TryGetValue(bracket.StaffIndex, out var vv)
                ? vv : ImmutableArray<Voice>.Empty;
            // A bar of multi-measure rest is no note column, and the right bound is the last
            // note column: a span stopping over `R1` (or `R1*N`) ends after the bar before it,
            // and draws no piece over the rest — not even on a line of its own.
            // LILYPOND-REF: lily/ottava-engraver.cc:138-151 acknowledge_note_column — only
            //   NoteColumns become bounds; a MultiMeasureRest has none.
            // MEASURED (2.26.0, Lab sessions/p1378/m a1, a2, a5): `c''4@8va d'' e'' f'' | R1@!8va |`
            //   hooks 0.6 right of the f'' (Lily# hooked inside the rest's bar, and over `R1*2`
            //   raised the line over the bar count); with the `R1` opening the next line LilyPond
            //   draws nothing there and hooks after the last note of the line before.
            bool endsOverRests = false;
            while (endMeasureIdx > bracket.StartMeasureIndex && OnlyMultiMeasureRests(ottavaVoices, endMeasureIdx))
            {
                endMeasureIdx--;
                endsOverRests = true;
            }
            // The staff floor under the supports and the staff-padding floor are this staff's
            // own extent (an `as lines N` staff's drawn lines); a caller without staves stands
            // on five.
            int staffLines = staffLinesOf?.Invoke(bracket.StaffIndex)
                ?? EngravingDefaults.DefaultStaffLines;

            string text = bracket.Type switch
            {
                OttavaType.Ottava8va => "8va",
                OttavaType.Ottava8vb => "8vb",
                OttavaType.Quindicesima15ma => "15ma",
                OttavaType.Quindicesima15mb => "15mb",
                _ => "8va"
            };

            foreach (var (segment, system) in SpannerBreakSubstitution.BrokenPieces(
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
                // A CONTINUATION piece's bound is broken (below).
                // …and the column is the STAFF's, not the voice's that wrote the mark: the first
                // voice's that has one at that moment, where the note collision put it.
                // LILYPOND-REF: lily/ottava-engraver.cc:138-151 acknowledge_note_column — the
                //   Staff's engraver takes the first NoteColumn it is told of as the LEFT bound,
                //   and the voices' columns of one moment come in the voices' order.
                // MEASURED (2.26.0, Lab sessions/p1304/ot o8, o9): `voice { b'2 c''2 } { c''2@8vb
                //   d''2@!8vb }` — the upper voice's b' is set 1.102 right of the c'' under it,
                //   and the bracket begins at 17.423, 0.8 left of THAT head (18.223); Lily# began
                //   it 0.8 left of the mark's own c'' (16.32) until session 1304.
                // A continuation: this system's piece of a bracket begun on a line before — a
                // later broken piece, or the one-system reservation's clipped item.
                bool continues = !segment.IsFirst || bracket.ContinuesFromLineBefore;
                double startX = !continues
                        && (StaffFirstColumnAt(segStartMeasure, ottavaVoices, bracket.VoiceIndex,
                                bracket.StartMeasureIndex, bracket.StartItemIndex)
                            ?? ItemX(segStartMeasure, ottavaVoices, bracket.VoiceIndex,
                                bracket.StartMeasureIndex, bracket.StartItemIndex,
                                graceNotes, bracket.StaffIndex)) is { } startItemX
                    ? segStartMeasure.X + startItemX + LeftShorten
                    : !continues
                        ? segStartMeasure.X + LeftShorten
                        // A BROKEN left bound is the line-start column's right edge, unshortened
                        // (ottava-bracket.cc:111-116, as the right one above). MEASURED (2.26.0,
                        // Lab sessions/p1378/m a3, a4): the continuation starts on the clef's right
                        // edge; Lily# started it 0.8 left of the first bar's X — over the clef —
                        // until session 1378.
                        : system.Measures[0].X;
                // A span running to its bar's END — closed on the next bar's first moment
                // (`f'4 <>@!8va | g'1`, the next START there), cut at its section's end, or never
                // closed — ends after its bar's last note column too: LilyPond's right bound is
                // the last note column whatever moment the stop is at (lily/ottava-engraver.cc:176-180
                // typeset_all; finalize, :219-226, takes the same path). MEASURED (2.26.0, Lab sessions/p1316/lp/o1.ly):
                // `\ottava #1 c''4 d'' e'' f'' \ottava #0 | g'1` hooks at 28.03, 0.6 right of the
                // f''s head, with the bar line at 29.37; Lily# drew `f'' <>@!8va |` to the
                // measure's edge (28.86) until session 1316, while `f''@!8va |` hooked at 28.03.
                var spanEnd = endsOverRests ? WholeBar : bracket.EndMoment ?? WholeBar;
                var lastStaffColumn = segment.IsLast
                    ? StaffLastColumnBefore(segEndMeasure, ottavaVoices,
                        endsOverRests ? endMeasureIdx : bracket.EndMeasureIndex, spanEnd)
                    : null;
                double endX = !segment.IsLast
                    // A BROKEN right bound is the line's end — its last bar's right edge — and
                    // takes no shorten.
                    // LILYPOND-REF: lily/ottava-bracket.cc:111-116 Ottava_bracket::print —
                    //   broken: span_points[d] = generic_bound_extent (b)[RIGHT]; shorten[d] = 0.
                    // MEASURED (2.26.0, Lab sessions/p1378/m a3, a4): the first piece ends on the
                    //   line's last bar line's right edge; Lily# ended it 0.6 short until session 1378.
                    ? SpannerBreakSubstitution.ReattachSpanX(segment, system, 0, 0).EndX
                    : (lastStaffColumn?.X
                            ?? (!endsOverRests && bracket.EndMoment is { } endMoment
                                ? LastColumnBefore(segEndMeasure, endMoment, bracket.EndItemIndex) : null)) is { } lastColX
                    // A span stopping INSIDE its last bar ends where LilyPond's does: just after
                    // the last note head it covers — and that head's dots — by the shorten-pair's
                    // right half.
                    // LILYPOND-REF: lily/ottava-engraver.cc:168-191 typeset_all — the RIGHT bound
                    //   is the last note column; lily/ottava-bracket.cc:89-121 print —
                    //   span_points[RIGHT] = the heads' X extent's right (:100), united with
                    //   their dots' on the right side (:101-104), minus RIGHT * shorten.
                    // ⚠️ Until 2026-11-12 the head was read as a BLACK head's width whatever the
                    //   column held, and the dots not at all — "no book stops an ottava mid-bar
                    //   after one yet". One did, writing large scores to try the grammar:
                    //   `a''2.@trill@!8va` stood its hook 0.96 left of LilyPond's (2.26.0, the same
                    //   book: 2.88 right of the head's left edge against Lily#'s 1.91), inside the
                    //   dot's column and down the side of the trill sign over the note.
                    // ⚠️ Still not read: a REST as the last column (LilyPond takes the column's
                    //   whole extent there, :108-109) and the timing-column fallback, which has
                    //   no item — both keep the black head's width.
                    ? lastColX + HeadsAndDotsRight(lastStaffColumn?.Item) - RightShorten
                    // A bar with no note column of this staff (a percent repeat; no voices
                    // supplied) keeps the measure's right edge, pulled in by the same 0.6 —
                    // LilyPond's command column there (lily/ottava-engraver.cc:177-179 typeset_all; a percent
                    // repeat disqualifies the note before it, :153-159).
                    : segEndMeasure.X + segEndMeasure.Width + RightShorten;

                // First segment shows the bare text ("8va"); a continuation repeats it in
                // parentheses, "(8va)". LILYSHARP-OWN: the common engraving convention for a sign carried
                // over a line break (it says "still in force", not "begins here"), kept by owner
                // decision of 2026-11-25 (session 1379) although LilyPond repeats the bare "8va".
                // ⚠️ When the parenthesised label and the shortest line do not fit between the
                // line's start and the right end, the continuation falls back to the bare label
                // — LilyPond's own. Until session 1379 the wider label slid left over the clef
                // (MEASURED 2.26.0, Lab sessions/p1378/m a4: one whole note into the line, the
                // "(8va)" began 1.16 left of the clef's right edge and the skyline lifted the whole
                // bracket 1.0; LilyPond's "8va" starts on the clef's edge).
                // Last segment carries the hook; non-last ends are open.
                string segText = continues ? $"({text})" : text;
                double textSize = LineStartX(fonts, segText, 0.0, LabelEm(fonts));
                if (continues && startX > endX - textSize - MinimumLength)
                {
                    segText = text;
                    textSize = LineStartX(fonts, segText, 0.0, LabelEm(fonts));
                }
                double segEdgeHeight = segment.IsLast && !Wave ? EndEdgeHeight : 0;

                // A span shorter than its label plus the minimum line slides the label LEFT,
                // so the hook stands after the label instead of on it. Not ported until
                // 2026-10-20: an 8vb over one eighth drew its hook through the "b" (OMR's
                // requests of 2026-10-20, item 2's book, bar 1).
                // LILYPOND-REF: lily/ottava-bracket.cc:127-134 Ottava_bracket::print —
                //   text_size = text.extent (X_AXIS)[RIGHT] + 0.3, then
                //   span_points[LEFT] = min (span_points[LEFT], span_points[RIGHT] - text_size - minimum-length).
                startX = Math.Min(startX, endX - textSize - MinimumLength);

                // EACH broken piece sides off ITS OWN system's columns, the way LilyPond's
                // per-system clone does — and it must, now that the reading is pointwise:
                // measure X restarts in every system.
                // LILYPOND-REF: lily/spanner.cc:36-144 Spanner::do_break_processing.
                double lineMiddleFrame = AlignedSideLineY(
                    fonts, segText, startX, endX, segEdgeHeight, isAbove,
                    segment, ottavaVoices, measureLayouts, beamMembers, bracket.StaffIndex,
                    staffLines, graceNotes, restShiftsOf?.Invoke(bracket.StaffIndex));
                // aligned_side answers in the staff-MIDDLE frame (the frame the ledger's
                // staff-to-line entries read); this record's frame has its origin on the
                // staff TOP line, 2 above it, and carries the staff's own within-system
                // downward offset, which SUBTRACTS.
                // ⚠️ THE PIECE'S OWN SYSTEM: a continuation stands on another system, whose
                // staves sit at their own offsets. Until 2026-10-20 every piece read the START
                // measure's, so a lower staff's "(8vb)" after a break was hung from the offset
                // of the system before — over the wrong staff (OMR report 2026-10-20, item 6).
                double staffOffset = staffYAt?.Invoke(segment.StartMeasureIndex, bracket.StaffIndex) ?? 0;
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
    /// The measure-relative X of item <paramref name="itemIndex"/> of the staff's voice
    /// <paramref name="voiceIndex"/>, or null when there is no such item.
    /// </summary>
    /// <remarks>
    /// ⚠️ On a multi-staff layout the item slots are the FIRST staff's, so a lower staff's
    /// items — and any later voice's — are found through the timing columns
    /// (<see cref="LayoutUtilities.GetItemXOffset"/>). Until 2026-10-20 the bracket's label and
    /// its supports read the slots directly, and a left hand's "8vb" stood at the right hand's
    /// column of the same index, up to 46 spaces away (OMR report 2026-10-20, item 6). With no
    /// voices supplied the slots are read as before.
    /// A GRACE item (session 925, HANDOFF §2 U8) is its grace column, read off the run:
    /// GetItemXOffset answers no grace column. It is a start's bound and, inside the piece, a
    /// support at its own X.
    /// </remarks>
    private static double? ItemX(MeasureLayout ml, ImmutableArray<Voice> voices,
        int voiceIndex, int measureIndex, int itemIndex,
        ImmutableArray<GraceNoteItem> graceNotes = default, int staffIndex = 0)
    {
        if (itemIndex < 0)
            return null;
        if (!graceNotes.IsDefaultOrEmpty && !voices.IsDefaultOrEmpty
            && voiceIndex >= 0 && voiceIndex < voices.Length
            && ElementCoordinator.GraceColumnAt(voices[voiceIndex], graceNotes, staffIndex, voiceIndex,
                measureIndex, itemIndex, ml, SpacingRules.VoiceCollisionShiftsOf(voices)) is { } grace)
            return grace.HeadLeftX - ml.X;
        if (voices.IsDefaultOrEmpty)
            return itemIndex < ml.Items.Length ? ml.Items[itemIndex].X : null;
        var measures = LayoutUtilities.ResolveVoiceMeasures(voices, voiceIndex, default);
        if (measureIndex < 0 || measureIndex >= measures.Length
            || itemIndex >= measures[measureIndex].Items.Length)
            return null;
        return LayoutUtilities.GetItemXOffset(measures, measureIndex, itemIndex, ml);
    }

    /// <summary>
    /// The X of the last note column OF THIS STAFF before <paramref name="endMoment"/> in
    /// <paramref name="measure"/> — the latest note, chord or rest any of its voices starts
    /// there — or null when the staff has none there (or no voices were supplied).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/ottava-engraver.cc:168-191 typeset_all — the right bound is the last
    ///   NoteColumn the Staff-context engraver acknowledged, so another staff's columns are not
    ///   candidates. ⚠️ <see cref="LastColumnBefore"/> read the system's timing columns, which
    ///   every staff shares: a right hand's sixteenth between a left hand's last covered note and
    ///   its closing chord became the bound, and the hook stood past the chord — read as under
    ///   the line (OMR follow-up 2026-10-20, `ottava-hook-over-next-2026-10-20.lys`).
    /// Of the voices' columns at that last moment it is the LAST voice's — the last the
    /// engraver was told of (<c>note_col_ = it</c>, :149) — standing where the note collision
    /// put it. MEASURED (2.26.0, Lab sessions/p1304/ot): o1 <c>voice { c''2 d''2 } { b'2@8vb
    /// c''2@!8vb }</c> ends 0.6 right of the lower voice's shifted head (24.840; Lily# ended
    /// it at the unshifted column's, 1.377 short, until session 1304); o5 ends after the
    /// lower voice's quarter head and not the upper's half; o10 after the lower voice's head
    /// where the upper voice rests.
    /// </remarks>
    private static (double X, MusicItem Item)? StaffLastColumnBefore(MeasureLayout measure, ImmutableArray<Voice> voices,
        int measureIndex, Fraction endMoment)
    {
        if (voices.IsDefaultOrEmpty)
            return null;
        Fraction? best = null;
        double bestX = 0;
        MusicItem? bestItem = null;
        for (int vi = 0; vi < voices.Length; vi++)
        {
            var measures = voices[vi].Measures;
            if (measureIndex < 0 || measureIndex >= measures.Length)
                continue;
            var items = measures[measureIndex].Items;
            var onset = Fraction.Zero;
            for (int ii = 0; ii < items.Length; ii++)
            {
                var item = items[ii];
                bool column = !item.GraceTime
                    && item is NoteItem or ChordItem or RestItem { IsSpacer: false, IsMultiMeasure: false };
                if (column && onset < endMoment && (best is null || onset >= best.Value))
                {
                    best = onset;
                    bestX = measure.X + LayoutUtilities.GetItemXOffset(measures, measureIndex, ii, measure)
                        + HairpinEngraver.CollisionShift(voices, vi, measureIndex, ii);
                    bestItem = item;
                }
                onset += item.Duration;
            }
        }
        return best is null || bestItem is null ? null : (bestX, bestItem);
    }

    /// <summary>
    /// Whether bar <paramref name="measureIndex"/> of this staff holds no note column — only
    /// multi-measure rest (and spacers) — in every voice, with at least one such rest.
    /// </summary>
    private static bool OnlyMultiMeasureRests(ImmutableArray<Voice> voices, int measureIndex)
    {
        if (voices.IsDefaultOrEmpty)
            return false;
        bool any = false;
        foreach (var voice in voices)
        {
            if (measureIndex < 0 || measureIndex >= voice.Measures.Length)
                continue;
            foreach (var item in voice.Measures[measureIndex].Items)
            {
                if (item is RestItem { IsMultiMeasure: true })
                    any = true;
                else if (item is not RestItem { IsSpacer: true })
                    return false;
            }
        }
        return any;
    }

    /// <summary>
    /// The measure-relative X of the first note column OF THIS STAFF at the moment the mark's
    /// own item starts — the first voice's that has a note, chord or rest there, where the
    /// note collision put it — or null when there is none to read (no voices supplied, a grace
    /// start: <see cref="ItemX"/> answers those).
    /// </summary>
    /// <remarks>The left bound's citation and measurement stand at the call.</remarks>
    private static double? StaffFirstColumnAt(MeasureLayout measure, ImmutableArray<Voice> voices,
        int voiceIndex, int measureIndex, int itemIndex)
    {
        if (voices.IsDefaultOrEmpty || voiceIndex < 0 || voiceIndex >= voices.Length || measureIndex < 0)
            return null;
        var own = voices[voiceIndex].Measures;
        if (measureIndex >= own.Length || itemIndex < 0 || itemIndex >= own[measureIndex].Items.Length
            || own[measureIndex].Items[itemIndex].GraceTime)
            return null;
        var start = Fraction.Zero;
        for (int i = 0; i < itemIndex; i++)
            start += own[measureIndex].Items[i].Duration;
        for (int vi = 0; vi < voices.Length; vi++)
        {
            var measures = voices[vi].Measures;
            if (measureIndex >= measures.Length)
                continue;
            var items = measures[measureIndex].Items;
            var onset = Fraction.Zero;
            for (int ii = 0; ii < items.Length && onset <= start; ii++)
            {
                var item = items[ii];
                if (onset == start && !item.GraceTime
                    && item is NoteItem or ChordItem or RestItem { IsSpacer: false })
                    return LayoutUtilities.GetItemXOffset(measures, measureIndex, ii, measure)
                        + HairpinEngraver.CollisionShift(voices, vi, measureIndex, ii);
                onset += item.Duration;
            }
        }
        return null;
    }

    /// <summary>
    /// How far right of its column X the right bound's ink reaches: the heads' extent, united
    /// with their dots' — what <c>Ottava_bracket::print</c> measures the bracket's right end
    /// from. A black head's width for anything that is not a note or a chord (see the call).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/ottava-bracket.cc:96-106 Ottava_bracket::print — <c>ext.unite
    ///   (h->extent (common, X_AXIS))</c> per head, then <c>if (dots &amp;&amp; d == RIGHT)
    ///   ext.unite (dots->extent (common, X_AXIS))</c>.
    /// The head and dot arithmetic is the staff profile's own (SkylineBuilder's NoteItem and
    /// ChordItem dot seeds, which follow the renderer): the head's ink right — a chord's
    /// pushed out by its reversed heads — then one dot padding, and the row's dots two dot
    /// widths apart.
    /// </remarks>
    private static double HeadsAndDotsRight(MusicItem? item)
    {
        int dots;
        double headRight;
        switch (item)
        {
            case NoteItem note:
                headRight = GlyphMetrics.GetNoteheadBBox(
                    LayoutUtilities.GetNoteValueFromFraction(note.BaseDuration)).Right;
                dots = note.Dots;
                break;
            case ChordItem { Notes.Length: > 0 } chord:
                int chordValue = LayoutUtilities.GetNoteValueFromFraction(chord.BaseDuration);
                headRight = GlyphMetrics.GetNoteheadBBox(chordValue).Right
                    + Math.Max(0, ChordHeadPositioning.CalculateOffsets(
                        chord.Notes, chord.ForcedStemUp ?? chord.StemUp, chordValue).Max());
                dots = chord.Dots;
                break;
            default:
                return GlyphMetrics.GetNoteheadBBox(4).Right;
        }
        if (dots <= 0)
            return headRight;
        var dotBox = MusicFont.Current.FullSize.Box(MusicGlyph.AugmentationDot);
        return headRight + EngravingDefaults.DotPadding(dotBox.Width)
            + (dots - 1) * 2 * dotBox.Width + dotBox.Right;
    }

    /// <summary>
    /// The X of the last note column before <paramref name="endMoment"/> in
    /// <paramref name="measure"/> — the span's right bound when it stops inside that bar — or
    /// null when no column precedes it (the bracket then keeps the measure's edge).
    /// </summary>
    /// <remarks>
    /// On the timing-column path (a multi-staff measure) the last column earlier than the
    /// moment; on the item-slot path the item before the closing note in the closing mark's
    /// voice, which on a single staff is the staff's own item list.
    /// </remarks>
    private static double? LastColumnBefore(MeasureLayout measure, Fraction endMoment, int endItemIndex)
    {
        if (!measure.Columns.IsDefaultOrEmpty)
        {
            double? x = null;
            foreach (var c in measure.Columns)
                if (c.Timing < endMoment)
                    x = measure.X + c.X;
            return x;
        }
        int last = endItemIndex - 1;
        return last >= 0 && last < measure.Items.Length
            ? measure.X + measure.Items[last].X
            : null;
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
            (BeamLayout Beam, double StemX, bool StemUp)> beamMembers,
        int staffIndex, int staffLines, ImmutableArray<GraceNoteItem> graceNotes = default,
        IReadOnlyDictionary<RestShiftKey, double>? restShifts = null)
    {
        double dir = isAbove ? 1.0 : -1.0;
        // :225-259 — my_dim, the grob's own profile about its line (Y = 0 here).
        var my = Skylines(
            fonts,
            text, startX,
            LineStartX(fonts, text, startX, LabelEm(fonts)),
            endX, edgeHeight, isAbove, 0.0);

        // :265-330 — the spanned columns of THIS piece, every voice of the staff, floored
        // by the staff symbol's own extent (include_staff, which declaring staff-padding
        // turns on).
        var support = DynamicEngraver.SpanSupportSkylines(
            voices, 0, Array.Empty<(int, int, double)>(), null, staffLines);
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
                    columns.Add((mi, ii, ml.X + (ItemX(ml, voices, vi, mi, ii, graceNotes, staffIndex) ?? 0.0)));
            }
            if (columns.Count == 0)
                continue;
            int voiceIndex = vi;
            var (vUp, vDown) = DynamicEngraver.SpanSupportSkylines(
                voices, voiceIndex, columns,
                (v, m, i) => beamMembers.TryGetValue((staffIndex, v, m, i), out var b)
                    ? b : null,
                staffLines, restShifts);
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
        double diff = DynamicEngraver.StaffExtentToward(dir, staffLines)
            + EngravingDefaults.OttavaBracketStaffPadding - dir * totalOff;
        totalOff += dir * Math.Max(diff, 0.0);
        return totalOff;
    }

    /// <summary>
    /// Pairs the ottava marks of a played score into brackets, and reports the marks that
    /// pair with nothing.
    /// </summary>
    /// <remarks>
    /// The text spanner's walk, on this family: START opens, <c>@!</c> closes, one open span
    /// per (staff, voice), and a mark nobody closed draws NOTHING. See
    /// <c>TextSpannerEngraver.PairTextSpanners</c> for the shape and for why played order
    /// removes the need for a self-replay guard.
    /// <para>
    /// ⚠️⚠️ LILYPOND'S ANSWER FOR AN UNCLOSED OTTAVA IS **NOT PORTED HERE**, deliberately.
    /// <c>Ottava_spanner_engraver::finalize</c> (lily/ottava-engraver.cc:220-226) does NOT
    /// warn and does NOT <c>suicide()</c>: it hands the open span to <c>typeset_all</c>, so
    /// LilyPond DRAWS an unterminated ottava to the end of the music, in silence. Only the
    /// TEXT spanner's engraver kills its unterminated span — the claim that LilyPond holds
    /// ONE answer for every unclosed span was written from that one engraver and is false
    /// here (read both <c>finalize</c>s: the pedal's does not warn either, it sets the RIGHT
    /// bound to <c>currentCommandColumn</c> and typesets). The language takes one answer for
    /// every family instead (user decision, §3), so an unclosed ottava is reported and drawn
    /// nowhere. Declared in docs/APPROXIMATIONS.md.
    /// </para>
    /// <para>
    /// ⚠️ WHAT THIS RETIRES, and it is the same three the text spanner retired: the
    /// one-measure fallback (<c>endMeasure = mark.MeasureIndex + 1</c>), the "the next ottava
    /// mark ends this one" search, and — never added here — the guard against a mark ending
    /// its own second playing. That last one matters: sessions 288 fixed the self-replay
    /// defect in the text spanner and in the hairpin and did not look at the ottava, which
    /// had it too (the search took "the next ottava mark on the same staff" with no test for
    /// a shared SourcePosition, so a repeated section's second playing of one written
    /// <c>@ottava</c> terminated its first). Pairing in played order cannot make that
    /// mistake, so the defect is closed here without a rule against it.
    /// </para>
    /// <para>
    /// ⚠️ ONE STOP FOR THE FAMILY: <c>@!ottava</c> and <c>@!quindicesima</c> are the same
    /// mark, as <c>\ottava #0</c> cancels whatever octavation runs. The bracket's TYPE comes
    /// from its start, which is the only place it is written.
    /// </para>
    /// </remarks>
    /// <param name="musicMarks">The score's marks.</param>
    /// <param name="plays">The score's section plays, or null: the section carry rule
    /// (<c>TextSpannerEngraver.PairTextSpanners</c> says it) — a bracket whose end is refused
    /// is cut at the end of its own section, and so is the display transposition under it,
    /// which reads the same brackets.</param>
    /// <param name="carrySink">Where the refused carries go; null to report none.</param>
    internal static (ImmutableArray<OttavaBracketItem> Brackets,
                     ImmutableArray<UnpairedSpanWarning> Unpaired)
        PairOttavaBrackets(ImmutableArray<MusicMarkItem> musicMarks,
            Collector.SectionPlays? plays = null, List<Collector.SectionCarryWarning>? carrySink = null)
    {
        if (musicMarks.IsDefaultOrEmpty)
            return ([], []);

        // F3/B: keep each mark's ORIGINAL index in musicMarks (== score.MusicMarks) so the
        // bracket can re-derive its data-pos from the live score on reuse.
        var ottavaMarks = musicMarks
            .Select((m, i) => (Mark: m, Index: i))
            .Where(x => x.Mark.Type is MusicMarkType.OttavaUp or MusicMarkType.OttavaDown
                        or MusicMarkType.QuindicesUp or MusicMarkType.QuindicesDown
                        or MusicMarkType.OttavaStop)
            // Played order: by measure, then by the moment within it, then by the order the
            // collector wrote — the same ordering the text spanner pairs in.
            .OrderBy(x => x.Mark.MeasureIndex)
            .ThenBy(x => x.Mark.AnchorItemIndex)
            .ThenBy(x => x.Index)
            .ToList();

        if (ottavaMarks.Count == 0)
            return ([], []);

        var brackets = ImmutableArray.CreateBuilder<OttavaBracketItem>();
        var unpaired = ImmutableArray.CreateBuilder<UnpairedSpanWarning>();
        var reported = new HashSet<(int Position, SpanPairingFault Fault)>();
        var open = new Dictionary<(int Staff, int Voice), (MusicMarkItem Mark, int Index)>();
        var openedIn = plays != null ? new HashSet<((int, int) Key, int Play)>() : null;

        void Report(int sourcePosition, SpanPairingFault fault)
        {
            if (reported.Add((sourcePosition, fault)))
                unpaired.Add(new UnpairedSpanWarning(sourcePosition, SpanKind.Ottava, fault));
        }

        // The bracket a start makes when `closer` ends it — cut at its own section's end when
        // the section carry rule refuses the end (the hairpin's cut).
        OttavaBracketItem Closed((MusicMarkItem Mark, int Index) start, MusicMarkItem closer)
        {
            if (plays?.Judge(start.Mark.MeasureIndex, closer.MeasureIndex) is { } fault)
            {
                carrySink?.Add(plays.Refused(start.Mark.SourcePosition, Collector.SectionSpanKind.Ottava,
                    fault, start.Mark.MeasureIndex));
                return CutAt(start, plays.CutMeasure(start.Mark.MeasureIndex));
            }
            return BracketFrom(start, closer);
        }

        foreach (var (mark, srcIndex) in ottavaMarks)
        {
            // (A combined staff's end whose part changed streams is paired in its start's voice.)
            var key = (mark.StaffIndex, mark.PairingVoice);

            if (mark.Type == MusicMarkType.OttavaStop)
            {
                if (!open.TryGetValue(key, out var start))
                {
                    if (plays != null && plays.OnUnmatchedStop(mark.MeasureIndex,
                            openedIn!.Contains((key, plays.PlayAt(mark.MeasureIndex)))) is { } closeFault)
                        carrySink?.Add(plays.RefusedClose(mark.SourcePosition, Collector.SectionSpanKind.Ottava,
                            closeFault, mark.MeasureIndex));
                    else
                        Report(mark.SourcePosition, SpanPairingFault.StopWithNoStart);
                    continue;
                }
                open.Remove(key);
                brackets.Add(Closed(start, closer: mark));
                continue;
            }

            // ⚠️ A START WHILE ONE IS OPEN IS A CHANGE OF OCTAVATION, NOT A NESTED SPAN, and
            // this is where the ottava parts company with the text spanner. LilyPond's
            // Ottava_spanner_engraver::process_music (lily/ottava-engraver.cc:122-136) finishes
            // the open span on ANY ottava event and then starts a new one unless the event is
            // `\ottava #0` — so `8va` running straight into `8vb` is two brackets, not a
            // refusal. Session 289 first wrote the text spanner's rule here and
            // audit/lpreg/ottcons.lys, the twin of LilyPond's own ottava-consecutive.ly,
            // caught it: that book exists to say consecutive ottavas are not merged.
            if (open.TryGetValue(key, out var previous))
                brackets.Add(Closed(previous, closer: mark));
            open[key] = (mark, srcIndex);
            openedIn?.Add((key, plays!.PlayAt(mark.MeasureIndex)));
        }

        foreach (var (mark, _) in open.Values)
        {
            // An earlier playing of the same written start may have drawn its bracket.
            bool closedElsewhere = false;
            foreach (var b in brackets)
                closedElsewhere |= b.SourcePosition == mark.SourcePosition;
            if (reported.Add((mark.SourcePosition, SpanPairingFault.Unterminated)))
                unpaired.Add(new UnpairedSpanWarning(mark.SourcePosition, SpanKind.Ottava,
                    SpanPairingFault.Unterminated, closedElsewhere));
        }

        return (brackets.ToImmutable(), unpaired.ToImmutable());
    }

    /// <summary>
    /// The bracket one open START makes, closed by <paramref name="closer"/>.
    /// </summary>
    /// <remarks>
    /// `@!ottava` (or the next START) marks the first note back at written pitch, so the span
    /// stops just BEFORE the closer's moment — the note-granular stop LilyPond's
    /// <c>\ottava #0</c> makes, the event ending the span at its own moment
    /// (lily/ottava-engraver.cc:122-136). A closer on its measure's first moment ends the span
    /// with the measure before, as before; a closer later in a bar ends it INSIDE that bar
    /// (<see cref="OttavaBracketItem.EndMoment"/>). A start later than its bar's first moment
    /// likewise begins the octavation there (<see cref="OttavaBracketItem.StartMoment"/>).
    /// A closer in the start's own first moment keeps the bracket on that measure rather than
    /// inverting it.
    /// ⚠️ ONE HOME because there are TWO closing sites — the terminator and the next START —
    /// and a bracket built differently by each is the "one quantity, two spellings" shape.
    /// </remarks>
    private static OttavaBracketItem BracketFrom(
        (MusicMarkItem Mark, int Index) start, MusicMarkItem closer)
    {
        var closeAt = MidMeasureMoment(closer);
        int endMeasure = closeAt is null ? closer.MeasureIndex - 1 : closer.MeasureIndex;
        return new(
            Type: TypeOf(start.Mark.Type),
            StartMeasureIndex: start.Mark.MeasureIndex,
            EndMeasureIndex: Math.Max(endMeasure, start.Mark.MeasureIndex),
            SourcePosition: start.Mark.SourcePosition,
            SourceIndex: start.Index,
            StaffIndex: start.Mark.StaffIndex,
            // The note the mark was written on IS the spanner's left bound. The collector
            // already anchors ottava marks to their host column (MeasureCollector.Annotations,
            // the compound-mark path), so this is a hand-over, not a new resolution.
            StartItemIndex: start.Mark.AnchorItemIndex,
            StartMoment: MidMeasureMoment(start.Mark),
            EndMoment: endMeasure >= start.Mark.MeasureIndex ? closeAt : null,
            EndItemIndex: closeAt is null ? -1 : closer.AnchorItemIndex,
            VoiceIndex: start.Mark.VoiceIndex);
    }

    /// <summary>The bracket one open START makes when the section carry rule refuses its end:
    /// it runs to the bar line its own section ends at (<paramref name="cutMeasure"/> is the
    /// next section's first measure), no end column.</summary>
    private static OttavaBracketItem CutAt((MusicMarkItem Mark, int Index) start, int cutMeasure)
        => new(
            Type: TypeOf(start.Mark.Type),
            StartMeasureIndex: start.Mark.MeasureIndex,
            EndMeasureIndex: Math.Max(cutMeasure - 1, start.Mark.MeasureIndex),
            SourcePosition: start.Mark.SourcePosition,
            SourceIndex: start.Index,
            StaffIndex: start.Mark.StaffIndex,
            StartItemIndex: start.Mark.AnchorItemIndex,
            StartMoment: MidMeasureMoment(start.Mark),
            EndMoment: null,
            EndItemIndex: -1,
            VoiceIndex: start.Mark.VoiceIndex);

    /// <summary>A mark's moment within its measure when it is past the measure's first one,
    /// else null (the measure's start, or no anchor — <c>AnchorTiming</c>'s default is 0/0).</summary>
    private static Fraction? MidMeasureMoment(MusicMarkItem mark)
        => mark.AnchorTiming.Denominator != 0 && mark.AnchorTiming > Fraction.Zero
            ? mark.AnchorTiming : null;

    /// <summary>The bracket an ottava START mark denotes.</summary>
    private static OttavaType TypeOf(MusicMarkType type) => type switch
    {
        MusicMarkType.OttavaUp => OttavaType.Ottava8va,
        MusicMarkType.OttavaDown => OttavaType.Ottava8vb,
        MusicMarkType.QuindicesUp => OttavaType.Quindicesima15ma,
        MusicMarkType.QuindicesDown => OttavaType.Quindicesima15mb,
        _ => OttavaType.Ottava8va
    };

    /// <summary>
    /// The ottava brackets a played score draws — the paired half of
    /// <see cref="PairOttavaBrackets"/>. The unpaired half is reported by
    /// <c>SpanPairingValidator</c>, which reads the SAME call, so a mark can never be warned
    /// about and drawn at once.
    /// </summary>
    public static ImmutableArray<OttavaBracketItem> DetectOttavaBrackets(
        ImmutableArray<MusicMarkItem> musicMarks, Collector.SectionPlays? plays = null)
        => PairOttavaBrackets(musicMarks, plays).Brackets;
}
