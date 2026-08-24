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
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Layout for a measure number printed above the staff at a system start
/// (or at a fixed period — see <see cref="BarNumberEngraver"/>).
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/bar-number-engraver.cc — BarNumber grob
/// LILYPOND-REF: scm/define-grobs.scm BarNumber — outside-staff-priority = 100
/// </remarks>
public readonly record struct BarNumberLayout(
    int MeasureIndex,
    // Bar number text (typically a 1-based integer).
    string Text,
    // X coordinate of the text anchor.
    double X,
    // Y of the text baseline in the Y-up frame (frame B): staff-spaces ABOVE the
    // system top, up-positive. The renderer reflects it to device
    // (system top − Y-up) against the measure's system top.
    double YUp,
    // When true the text right-aligns to X (TextAnchor.End).
    // Line-start and mid-line bar numbers LEFT-align (false) per BarNumber's
    // self-alignment-X = LEFT, so the number sits above the staff start and
    // extends rightward, clear of the system-start brace.
    bool RightAligned = false,
    // The staff or ROW this number hangs on — LilyPond re-parents the grob onto it and
    // the outside-staff pass then runs in THAT element's axis group, so the two must be
    // the same answer. Null when nothing was found to hang on (LilyPond's
    // move_to_extremal_staff returning #f), and the number keeps the system.
    // LILYPOND-REF: lily/side-position-interface.cc:545-547 move_to_extremal_staff — its
    //   set_y_parent and its Axis_group_interface::add_element, i.e. one element decides both.
    int? AnchorStaffIndex = null);

/// <summary>
/// Calculates BarNumber positions for each system. By default, the first
/// measure of every system after the first gets a bar number. Optionally
/// every Nth measure can be numbered too via the period parameter.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/bar-number-engraver.cc Bar_number_engraver
/// LILYPOND-REF: scm/translation-functions.scm — barNumberFormatter default
/// LILYPOND-REF: scm/define-grobs.scm BarNumber:
///   self-alignment-X = LEFT, padding = 1.0, font-size = -2 (small)
/// </remarks>
internal static class BarNumberEngraver
{
    /// <summary>
    /// Bar number text height: normal text is 11pt at a 20pt staff
    /// (= 2.2 staff spaces) and BarNumber uses font-size -2, i.e.
    /// magstep(-2) = 2^(-2/6).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm BarNumber (font-size . -2)
    /// LILYPOND-REF: scm/paper.scm:69-77 sets <c>text-font-size</c> to <c>11 * (staff-height / 20pt)</c> and <c>output-scale</c> to <c>staff-height / 4</c>, so 11pt against a 5pt staff space = 2.2 ss.
    /// LILYPOND-REF: scm/lily-library.scm <c>magstep</c> = <c>exp((s/6) * log 2)</c>.
    /// ⚠️ THE SECOND ADDRESS SAID <c>ly/paper-defaults-init.ly</c> UNTIL 2026-07-28 and that
    /// file does not mention text-font-size at all. The value was right; the citation was
    /// never read (HANDOFF 5.2.1①). It is corrected because two later constants —
    /// <see cref="EngravingDefaults.LyricTextFontSize"/> and
    /// <see cref="EngravingDefaults.ChordNameFontSize"/> — were derived by copying it.
    /// </remarks>
    public static readonly double FontSize = 2.2 * Math.Pow(2, -2.0 / 6.0);

    /// <summary>
    /// The staff a system's bar number hangs on: the topmost non-hidden SPACEABLE staff.
    /// Null on a staffless sheet.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:320-321 BarNumber after-line-breaking =
    /// ly:side-position-interface::move-to-extremal-staff — the number is re-parented onto
    /// the topmost alignment element whose X-extent intersects the number's own widened by
    /// 1.0 (lily/side-position-interface.cc:510-563). A line-start number hangs INTO the
    /// left margin (X −0.956..0, the probe header on barnumber-chord-row.ly) while a
    /// leading ChordNames/lyrics row's ink starts at the first note, so the two are
    /// X-disjoint and LilyPond leaves the number on the STAFF, tucked BELOW the row —
    /// measured 2026-08-20 on 2.26.0: ink bottom 3.050000 over the staff refpoint with the
    /// chords 5.045 above it. Anchoring on the SYSTEM top instead put the number a whole
    /// band too high on every lead sheet (ledger barnumber.chord-row.staff-to-ink-bottom,
    /// +5.945; the user saw it first).
    /// ⚠️ The X test itself is NOT ported HERE: a row's ink X-range is not on StaffLayout.
    /// For the staffless case it is answered structurally instead — see
    /// <see cref="AnchorRow"/>, whose whole justification is which row reaches x≈0.
    /// </remarks>
    /// ⚠️ THE WALK ITSELF MOVED TO <see cref="StaffAffinity.TopSpaceableStaff"/> on
    /// 2026-08-24; this is the bar number's NAME for it. The remarks above stay here because
    /// they are the bar number's own measurement — the X-disjointness that keeps a
    /// line-start number on the staff — and because the ledger's <c>why</c> cites this
    /// member. The move happened because that entry already claimed this spelling was
    /// "shared with the stacker's tracker choice" and it was not: the REHEARSAL MARK had a
    /// spelling of its own, and with it the same defect, for four more sessions.
    internal static StaffLayout? AnchorStaff(SystemLayout system)
        => StaffAffinity.TopSpaceableStaff(system);

    /// <summary>
    /// The ROW a system with no staff at all hangs its bar number on: the grid row, the one
    /// that draws the measure barlines. Null when the system has none, and the number then
    /// keeps the system as LilyPond's does.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/side-position-interface.cc:510-563 move_to_extremal_staff — it
    /// widens the number's own X extent
    /// by 1.0 and asks the system's VerticalAlignment for the extremal element in the number's
    /// direction; LILYPOND-REF: lily/staff-grouper-interface.cc
    /// <c>Staff_grouper_interface::get_extremal_staff</c> walks EVERY row's VerticalAxisGroup
    /// and returns the first live one whose X extent overlaps, testing neither
    /// <c>is_spaceable</c> nor for a StaffSymbol. The name says staff; the code says row.
    /// MEASURED on 2.26.0 (audit/lp-geometry/probes/barnumber-staffless.ly, books SLN/SL3):
    /// the chord row starts at 1.237025 and the number's widened interval ends at 1.000000,
    /// so the chord row is skipped BY 0.237 and the lyric row takes the number; book SL3 puts
    /// three rows up so that "the bottom row" and "the topmost that reaches" disagree, and
    /// the number takes the UPPER one. "The bottom row" is a killed hypothesis, not a rule.
    /// <para>
    /// ⚠️ WHY THIS IS A LOOKUP AND NOT A WALK WITH AN X TEST. A Lily# row's ink X-range is
    /// not on <see cref="StaffLayout"/>, and its per-(system, staff) skyline profile is EMPTY
    /// for a text row (measured 2026-08-24: both rows of the reported book report an empty
    /// up-profile, because a row's ink is drawn by the lyric and chord engravers and never
    /// seeded there). What CAN be answered exactly is the question the X test is asking:
    /// which row reaches the number's column. Only one thing in a Lily# lead sheet is drawn
    /// at the system's left edge — the grid row's opening barline
    /// (<c>SharedRenderer</c> draws it at <c>systemStartX</c>) — while every chord name and
    /// syllable starts after the line-start prefix, measured at 3.74 and 4.42 against a
    /// widened interval ending at 1.0 on the reported book. So the grid row is the row that
    /// overlaps, and it overlaps for the SAME REASON a StaffSymbol wins in LilyPond: it is
    /// the one thing that spans the system from x≈0.
    /// </para>
    /// <para>
    /// ⚠️ THE TWO ENGINES REACH IT BY DIFFERENT INK, and both halves belong here or the next
    /// reader deletes one as redundant: LilyPond's lyric row reaches because its first
    /// SYLLABLE sits at x=0 (a lead sheet there has no prefix), Lily#'s reaches because
    /// Lily# opens each system of a grid with a barline and LilyPond draws none at a line
    /// start. Same row, different ink.
    /// </para>
    /// </remarks>
    internal static StaffLayout? AnchorRow(SystemLayout system, int gridBarlineRowIndex)
    {
        if (gridBarlineRowIndex < 0 || system.StaffGroups.IsDefaultOrEmpty)
            return null;
        foreach (var group in system.StaffGroups)
        {
            if (group.Staves.IsDefaultOrEmpty) continue;
            foreach (var st in group.Staves)
                if (!st.IsHidden && st.StaffIndex == gridBarlineRowIndex)
                    return st;
        }
        return null;
    }

    /// <summary>
    /// Calculates bar number layouts. When <paramref name="period"/> is greater
    /// than 1, also numbers every Nth measure within a system; default 0 means
    /// system starts only. <paramref name="numberFirstMeasure"/> set to false (LP
    /// default) suppresses the score's very first measure number.
    /// Collision handling lives in OutsideStaffStacker.StackAboveStaff.
    /// </summary>
    public static ImmutableArray<BarNumberLayout> Calculate(
        Rendering.ScoreTextMetrics fonts,
        ImmutableArray<SystemLayout> systems,
        int period = 0,
        bool numberFirstMeasure = false,
        int numberOffset = 0,
        int gridBarlineRowIndex = -1)
    {
        if (systems.IsDefaultOrEmpty)
            return ImmutableArray<BarNumberLayout>.Empty;

        var builder = ImmutableArray.CreateBuilder<BarNumberLayout>();

        for (int sysIdx = 0; sysIdx < systems.Length; sysIdx++)
        {
            var system = systems[sysIdx];
            if (system.Measures.IsDefaultOrEmpty)
                continue;

            // WHAT THIS SYSTEM'S NUMBER HANGS ON, and the top of it — the "support" whose
            // up-skyline LilyPond's side positioning sits `padding' above.
            // LILYPOND-REF: lily/side-position-interface.cc:203-370 aligned_side — dist =
            //   (support UP skyline).distance(my DOWN skyline), then
            //   total_off += dir * ss * padding. The support set is `stavesFound':
            // LILYPOND-REF: lily/bar-number-engraver.cc:188-190 stop_translation_timestep. So:
            //   MEASURED (2.26.0, probes/barnumber-staffless.ly, books SLC/SLN/SLP/SLQ):
            //     with a staff  2.05 (StaffSymbol top) + 1.0 + 0.020473 = 3.070473
            //     with none         0 (aligned_side's `dim.is_empty ()' branch replaces an
            //                          empty support with a FLAT SKYLINE AT HEIGHT 0)
            //                        + 1.0 + 0.020473 = 1.020473
            //   Book SLP (outside-staff-priority off) prints that 1.020473 alone, so the
            //   two stages are separated by measurement and not by reading.
            var anchorStaff = AnchorStaff(system);
            var anchorRow = anchorStaff is null ? AnchorRow(system, gridBarlineRowIndex) : null;
            int? anchorIndex = anchorStaff?.StaffIndex ?? anchorRow?.StaffIndex;
            // The staff's own ink: its top LINE plus half that line's thickness. Written as
            // the derivation rather than as 1.05 so it follows the staff symbol (HANDOFF 5.2.1⑤).
            double anchorUp = anchorStaff is { } st
                ? st.Y + EngravingDefaults.StaffLineThickness / 2
                // ⚠️ LILYSHARP-OWN, AND IT IS THE BAND TOP, NOT LILYPOND'S REFPOINT.
                // LilyPond has no band: a Lyrics/ChordNames VerticalAxisGroup's reference
                // point IS the text baseline (MultiStaffLayouter.TextRowRefpointBelowTop
                // says so and holds the two constants), so a literal port of the branch
                // above would put the number `padding' over that BASELINE and let stage two
                // — the outside-staff pass — lift it clear of the row's ink, which is how
                // LilyPond's "5" ends up superscripting the first syllable.
                // THAT STAGE CANNOT RUN HERE: a Lily# text row's per-(system, staff) skyline
                // profile is EMPTY (measured 2026-08-24 — a row's ink is drawn by the lyric
                // and chord engravers and never seeded into a staff profile), so the pass
                // has nothing to lift the number off and the literal spelling lands it 2.6
                // LOW on the reported book, inside the drawn band.
                // ⇒ The datum is the grid row's BAND TOP, which is the same expression the
                // staff branch uses applied to the object that plays the staff's part here:
                // SharedRenderer calls the grid row "a staff with the lines removed", and a
                // band has no line, so there is no half-thickness to add. A band top is
                // Lily#'s own object, hence LILYSHARP-OWN rather than a REF.
                // GOES WHEN a text row carries an ink profile and the outside-staff pass can
                // do LilyPond's half of the work; the ledger point that watches the gap is
                // barnumber.rows-only.row-to-ink-bottom.
                // ⚠️ USER DECISION 2026-08-24: shown both pictures rendered, the user chose
                // this one ("この小節番号は上手に配置できている" on samples/drunken-sailor.lys).
                : anchorRow is { } row ? row.Y
                // Nothing to hang on — LilyPond's move_to_extremal_staff returns #f and the
                // number keeps the system. Unchanged from before this branch existed.
                : EngravingDefaults.StaffLineThickness / 2;

            // First measure of every system after the first is always numbered.
            // LILYPOND-REF: scm/translation-functions.scm — barNumberVisibility default
            // (first-bar-number-invisible-and-no-parenthesized-bar-numbers).
            for (int i = 0; i < system.Measures.Length; i++)
            {
                var ml = system.Measures[i];
                int measureIndex = ml.MeasureIndex;
                bool isFirstSystem = sysIdx == 0;
                bool isFirstInSystem = i == 0;
                bool isFirstOfScore = measureIndex == 0 || ml.MeasureIndex == 0;

                bool show =
                    (isFirstInSystem && !isFirstSystem) ||
                    (isFirstOfScore && numberFirstMeasure) ||
                    (period > 0 && measureIndex > 0 && (measureIndex % period == 0));

                if (!show)
                    continue;

                // LP shows 1-based numbers. measureIndex is 0-based. A leading
                // \partial pickup shifts everything down by one (numberOffset = -1)
                // so the pickup is bar 0 and the first full measure is bar 1.
                int displayedNumber = measureIndex + 1 + numberOffset;

                // Line-start numbers break-align to the LEFT EDGE — the staff-line
                // origin, BEFORE the clef, as LilyPond's own comment on
                // break-align-symbols says — and at a line start they align their
                // RIGHT edge to it, so the number hangs into the left margin and
                // the clef is never underneath it.
                //
                // LILYPOND-REF: scm/define-grobs.scm:323 BarNumber
                //   break-align-symbols = (left-edge staff-bar), and :334
                //   self-alignment-X = (break-alignment-list LEFT LEFT RIGHT).
                // ⚠️ THAT TRIPLE IS (end-of-line middle begin-of-line) —
                // scm/output-lib.scm:506 names the three arguments in that order — so
                // at a LINE START it is RIGHT, and only a mid-line number is LEFT.
                // This code read the triple the other way round for as long as it
                // existed, put the number over the clef, and the above-staff stacker
                // then lifted it clear: MEASURED at 4.260000 above the staff refpoint
                // against LilyPond's 3.074440 (audit/lp-geometry,
                // barnumber.{low,high}-melody.staff-to-baseline). That excess is not
                // cosmetic — a bar number is inside its staff's skyline, so it IS the
                // ink the system reserves above its own reference point, which floors
                // the system-to-system spring and closes the previous system's
                // loose-line chain (page-layout-problem.cc:625-629, :931-932).
                //
                // MEASURED, LilyPond 2.26.0 on a continuation system (probe
                // page-vertical.ly, book BNL): the number spans X (-0.956013 .. 0.0)
                // and the clef (0.800000 .. 3.365000). Disjoint, by 0.8.
                //
                // ⚠️ horizon-padding 0.05 is a SKYLINE padding, not an X shift; the
                // 0.05 that used to be added here had no counterpart in LilyPond.
                bool atLineStart = isFirstInSystem;
                // The system's left edge is where the staff lines start:
                // the indent (margins live in the page transform). ml.X is
                // the prefix END, and PrefixWidth is not reliable per-system
                // here, so anchor on the staff-line origin directly.
                double x = atLineStart ? system.Indent : ml.X;

                // The number's INK BOTTOM sits padding 1.0 above the staff's own
                // up-skyline, and that skyline is the top staff LINE plus half its
                // thickness — not the line's centre. Written as the derivation rather
                // than as 1.05 so it follows the staff symbol if that ever changes
                // (HANDOFF 5.2.1⑤).
                // Collisions with protruding staff content and other outside-staff
                // grobs are resolved afterwards by OutsideStaffStacker.StackAboveStaff.
                // LILYPOND-REF: scm/define-grobs.scm:333 BarNumber padding = 1.0;
                // lily/side-position-interface.cc y_aligned_side.
                //
                // ...and the BASELINE is that ink bottom plus the digits' OWN overshoot
                // below it, which is why this reads the face rather than assuming zero.
                // It said "Lily# has no measured bottom overshoot for its digits" until
                // 2026-07-28 and that had stopped being true: TextFontMetrics.Ink measures
                // the drawn path. MEASURED, and it is PER STRING, which is the shape
                // LilyPond's own dump has: a round digit overshoots by 0.024446 and a "1"
                // by nothing, against LilyPond's 3.074440 for "6" and 3.076208 for another
                // digit over the staff refpoint (probe page-vertical.ly, books BNL/BNH).
                // A constant here would be right for one numeral and wrong for the next.
                const double padding = 1.0;
                string text = displayedNumber.ToString();
                double overshoot = -fonts.Ink(
                    text, FontSize, Rendering.TextRole.BarNumber, Rendering.FontStyle.Bold).Bottom;
                // ...measured from the ANCHOR's top, not the system top: the two are the
                // same place only until a chords/lyrics row leads the system. See
                // AnchorStaff / AnchorRow for the LilyPond mechanism and the measurement,
                // and the anchorUp derivation above for which top it is.
                double yUp = anchorUp + padding + overshoot;

                builder.Add(new BarNumberLayout(
                    MeasureIndex: measureIndex,
                    Text: text,
                    X: x,
                    YUp: yUp,
                    RightAligned: atLineStart,
                    AnchorStaffIndex: anchorIndex));
            }
        }

        return builder.ToImmutable();
    }
}
