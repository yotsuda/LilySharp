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

    /// <summary>The number's em for THIS score: <see cref="FontSize"/> unless the score's
    /// <c>fonts { }</c> wrote a <c>step</c> or <c>size</c> for <c>barNumbers</c>. Every reader
    /// of the number's em — the draw, the paging silhouette, the outside-staff pass — asks
    /// here, so a score that writes one moves them together.</summary>
    public static double Em(Rendering.ScoreTextMetrics fonts)
        => fonts.Size(Rendering.TextRole.BarNumber, FontSize);

    /// <summary>The number's weight and slant: bold (LilyPond's BarNumber font-series)
    /// unless the score wrote a style for <c>barNumbers</c>.</summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm BarNumber (font-series . bold).</remarks>
    public static Rendering.FontStyle Style(Rendering.ScoreTextMetrics fonts)
        => fonts.Style(Rendering.TextRole.BarNumber, Rendering.FontStyle.Bold);

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
    /// <see cref="AnchorRow"/>, whose whole justification is which row reaches x≈0. For a
    /// MID-LINE number it IS asked, against the placed symbols (<see cref="MidLineRowAnchor"/>,
    /// session 788), and the number moves onto the chord row where the ROW's X extent — the
    /// union of its symbols' ink, as an axis group's extent is its elements' — meets the
    /// number's own widened by 1.0 (session 789: the first port asked the nearest SYMBOL to
    /// reach, which left the bar after a `|:` or a mid-line key change on the staff).
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
    /// The number each measure DISPLAYS, indexed by measure: one more than the count of
    /// counted measures before it, plus <paramref name="numberOffset"/> (a leading pickup's
    /// −1). A measure closed under <c>time none</c> (<see cref="Measure.Unmetered"/>) is not
    /// counted, so the measure after it carries the same number — and neither is a measure
    /// whose successor CONTINUES its bar (<see cref="Measure.ContinuesBar"/>): the first half
    /// of a bar a line break splits, or the last bar of a section the next section's first
    /// bar completes.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/timing-translator.cc:478-507 Timing_translator::start_translation_timestep
    ///   — `++cbn` (currentBarNumber) sits inside `if (timing)`, so the number stands still
    ///   across the cadenza's bars and resumes at the same value when timing returns;
    ///   ly/property-init.ly cadenzaOn / cadenzaOff are the ##f / ##t of that property.
    /// MEASURED (2.26.0, scratch/p354/lp/senza-fixed.ly): bar 1 in 4/4, then \cadenzaOn with
    /// two \bar "|", \break, \cadenzaOff \time 4/4 — the second line's BarNumber reads 2.
    /// </remarks>
    public static ImmutableArray<int> NumberMeasures(ImmutableArray<Measure> measures, int numberOffset)
    {
        if (measures.IsDefaultOrEmpty)
            return ImmutableArray<int>.Empty;
        var numbers = ImmutableArray.CreateBuilder<int>(measures.Length);
        int counted = 0;
        for (int i = 0; i < measures.Length; i++)
        {
            numbers.Add(counted + 1 + numberOffset);
            // A measure continuing the one before it (a mid-bar break, a section opening
            // with the rest of the bar) does not advance the count; a later volta ending
            // continuing the bar the repeat's BODY left short (ContinuedFromMeasure set)
            // does, because the ending before it closed on a bar line. LILYPOND-REF
            // ly/engraver-init.ly alternativeRestores = (measurePosition measureLength
            // measureStartNow lastChord): the position is restored at each alternative,
            // currentBarNumber is not — "bar numbers continue through alternatives"
            // (define-context-properties.scm, alternativeNumberingStyle unset).
            bool nextContinues = i + 1 < measures.Length && measures[i + 1].ContinuesBar
                && measures[i + 1].ContinuedFromMeasure < 0;
            if (!measures[i].Unmetered && !nextContinues)
                counted++;
        }
        return numbers.MoveToImmutable();
    }

    /// <summary>
    /// Calculates bar number layouts under <paramref name="policy"/> — the score's
    /// <c>layout { barNumbers … }</c> switch (<see cref="Semantics.BarNumberPolicy"/>):
    /// <c>lines</c> numbers the first bar of every system after the first (LilyPond's
    /// default), <c>none</c> numbers nothing, <c>every N</c> numbers every bar whose
    /// displayed number is a multiple of N wherever it stands.
    /// Collision handling lives in OutsideStaffStacker.StackAboveStaff.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/translation-functions.scm:1005-1007 first-bar-number-invisible-and-no-parenthesized-bar-numbers
    ///   — the default barNumberVisibility (engraver-init.ly:858): barnum > 1 at a bar's
    ///   start; LILYPOND-REF: scm/define-grobs.scm:324 begin-of-line-visible — BarNumber's
    ///   break-visibility, which then keeps only the line-start ones. Together: the
    ///   <c>lines</c> arm.
    /// LILYPOND-REF: scm/translation-functions.scm:987-988 every-nth-bar-number-visible —
    ///   (= 0 (modulo barnum n)), the <c>every</c> arm; the number stands mid-line because
    ///   the writer overrides break-visibility to end-of-line-invisible (#(#f #t #t)), which
    ///   is what the twin writes beside it. A line-start bar that is not a multiple carries
    ///   NO number under it — the visibility function is asked first and answers no.
    /// LILYPOND-REF: ly/engraver-init.ly:774 — \consists Bar_number_engraver in Score;
    ///   \remove Bar_number_engraver is the <c>none</c> arm (no grob is ever made).
    /// </remarks>
    public static ImmutableArray<BarNumberLayout> Calculate(
        Rendering.ScoreTextMetrics fonts,
        ImmutableArray<SystemLayout> systems,
        Semantics.BarNumberPolicy policy = default,
        int numberOffset = 0,
        int gridBarlineRowIndex = -1,
        ImmutableArray<int> displayedNumbers = default,
        ImmutableArray<Measure> measures = default,
        MmrRunMap? runMap = null,
        // The placed chord symbols: a MID-LINE number re-parents onto the chord row whose X
        // extent (its symbols' union) meets the number's own widened by 1.0 (see
        // MidLineRowAnchor). Default: no row is asked.
        ImmutableArray<ChordNameLayout> chordNames = default)
    {
        if (systems.IsDefaultOrEmpty || policy.Mode == Semantics.BarNumberMode.None)
            return ImmutableArray<BarNumberLayout>.Empty;

        var builder = RentLayoutBuilder();
        // The rows' symbols per system, bucketed once — only `every N` prints a mid-line
        // number, and only a mid-line number asks (MidLineRowAnchor).
        var rowSymbolsBySystem = policy.Mode == Semantics.BarNumberMode.Every
            ? RowSymbolsBySystem(systems, chordNames) : null;

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

            // The chord rows this system's MID-LINE numbers may hang on, each with its X
            // extent — gathered once per system (see RowExtents); null when none can take one.
            var rowSymbols = rowSymbolsBySystem?[sysIdx];
            var rowExtents = anchorStaff is { } rowHost && rowSymbols is { Count: > 0 }
                ? RowExtents(fonts, system, rowHost, rowSymbols) : null;

            for (int i = 0; i < system.Measures.Length; i++)
            {
                var ml = system.Measures[i];
                int measureIndex = ml.MeasureIndex;
                bool isFirstSystem = sysIdx == 0;
                bool isFirstInSystem = i == 0;

                // A bar swallowed by a compressed multi-measure rest has no bar line of its
                // own and no column: its layout sits at the run's end, so a number made for
                // it would stand on top of the next bar's (seen by eye, session 767: `R1*2`
                // under `barNumbers every 1` printed "16" over "17"). LilyPond makes a
                // BarNumber only where a bar line stands (or at a break, or the start).
                // LILYPOND-REF: lily/bar-number-engraver.cc:66-70 consider_creating_bar_number —
                //   its comment: "Allow a bar number if any of these conditions is met: there
                //   is a bar line, there is a break point, we are at the start of the piece";
                //   acknowledge_bar_line sets saw_bar_line_, and a compressed run
                //   (lily/multi-measure-rest.cc) spans ONE column pair, so no bar line is
                //   acknowledged inside it.
                if (runMap != null && runMap.IsInterior(measureIndex))
                    continue;

                // LP shows 1-based numbers. measureIndex is 0-based. A leading
                // \partial pickup shifts everything down by one (numberOffset = -1)
                // so the pickup is bar 0 and the first full measure is bar 1 — and a
                // measure closed under `time none` advances nothing, which is what the
                // per-measure table from NumberMeasures says when the caller has one.
                int displayedNumber = !displayedNumbers.IsDefault && measureIndex < displayedNumbers.Length
                    ? displayedNumbers[measureIndex]
                    : measureIndex + 1 + numberOffset;

                // `lines`: the first measure of every system after the first (the default
                // visibility function's barnum > 1, kept to line starts by the grob's
                // begin-of-line-visible). `every N`: the visibility function alone —
                // (= 0 (modulo barnum n)) — with break-visibility opened up, so a mid-line
                // multiple is numbered and a line-start non-multiple is not (the remarks
                // on this method cite both).
                bool show = policy.Mode == Semantics.BarNumberMode.Every
                    ? policy.Period > 0 && displayedNumber % policy.Period == 0
                    : isFirstInSystem && !isFirstSystem;

                // A system that opens MID-BAR — the second half of a bar a line break split
                // (Measure.ContinuesBar) — opens with no bar line and so with no number:
                // LilyPond's BarNumber is made with the BarLine, and there is none at the
                // break's moment. MEASURED (2.26.0, scratch/p357/lp/mb1.ly against mb8.ly):
                // `c4 d \break e f |` prints no PROBEBN on its second system, where
                // `c4 d e f \break |` prints "2".
                if (isFirstInSystem && !measures.IsDefault && measureIndex < measures.Length
                    && measures[measureIndex].ContinuesBar)
                    show = false;

                if (!show)
                    continue;

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
                // MID-LINE the number's LEFT edge stands on its bar line's break-align
                // anchor — the centre of the bar's strokes with the dots dropped
                // (EngravingDefaults.BarlineAnchorFromInkLeft, the house the rehearsal mark
                // already hangs on: 0.095 into a `|', 0.545 into a `.|:'), NOT on the
                // measure's X, which is a plain bar's RIGHT edge and a `|:`'s LEFT.
                // LILYPOND-REF: scm/define-grobs.scm:334-337 BarNumber — X-offset self-aligned-on-breakable
                //   (self-alignment-interface), self-alignment-X (break-alignment-list LEFT
                //   LEFT RIGHT): mid-line the triple's middle entry, LEFT — the number's left
                //   on the parent's anchor.
                // MEASURED 2.26.0 (probes/barnumber-row-extent.ly): BRX "3" at 0.095000
                //   past a `|'s ink left; BRR "3" at 0.545000 past a `.|:'s; BRK "3" at
                //   0.095000 past the `|' a key and a meter change follow. Lily# stood every
                //   mid-line number at ml.X until session 789.
                double x = atLineStart ? system.Indent
                    : (measures.IsDefault ? null : MusicMarkEngraver.MidLineBarAnchorX(ml, measures)) ?? ml.X;

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
                int? numberAnchor = anchorIndex;

                // A MID-LINE number stands at its bar line, and the CHORD ROW takes it where
                // the row's X extent — its first symbol's left to its last symbol's right —
                // meets the number's own widened by 1.0: its ink bottom is then padding 1.0
                // over the symbols' BASELINE (the row's refpoint), lifted by the outside-staff
                // pass only where it overlaps a symbol. Sessions 788-789; see MidLineRowAnchor
                // for the mechanism and the numbers.
                if (!atLineStart && rowExtents is { Count: > 0 } && rowSymbols is not null)
                {
                    double width = fonts.Advance(text, Em(fonts), Rendering.TextRole.BarNumber, Style(fonts));
                    if (MidLineRowAnchor(rowExtents, x, width) is { } hostRow)
                    {
                        double bottomUp = hostRow.RefpointUp + padding;
                        foreach (var sym in rowSymbols)
                            if (sym.RowStaffIndex == hostRow.StaffIndex
                                && sym.X < x + width
                                && sym.X + ChordNameEngraver.SymbolInkWidth(fonts, sym) > x)
                                bottomUp = Math.Max(bottomUp,
                                    sym.YUp + ChordNameEngraver.SymbolInk(fonts, sym).Top
                                    + OutsideStaffStacker.OutsideStaffPadding);
                        yUp = bottomUp + overshoot;
                        numberAnchor = hostRow.StaffIndex;
                    }
                }

                builder.Add(new BarNumberLayout(
                    MeasureIndex: measureIndex,
                    Text: text,
                    X: x,
                    YUp: yUp,
                    RightAligned: atLineStart,
                    AnchorStaffIndex: numberAnchor));
            }
        }

        // ToImmutable COPIES (see the drawer's remark), so the builder is finished with here.
        var numbers = builder.ToImmutable();
        GiveLayoutBuilder(builder);
        return numbers;
    }

    /// <summary>
    /// How far LilyPond widens a mark's X extent when it asks which row the mark re-parents
    /// onto — the reach a chord ROW's extent has to come within for the row to take a number.
    /// </summary>
    // LILYPOND-REF: lily/side-position-interface.cc:521-523 move_to_extremal_staff —
    //   `Interval iv = me->extent (sys, X_AXIS); iv.widen (1.0);'
    private const double ExtremalStaffReach = 1.0;

    /// <summary>
    /// A chord row a mid-line number may hang on: its staff index, its Y (up from the system
    /// top), its X extent in the system — the UNION of its placed symbols' ink, as LilyPond's
    /// ChordNames axis group's extent is its elements' — and its refpoint, the symbols'
    /// baseline (the LINE's: the lowest, a symbol lifted over another standing higher).
    /// </summary>
    private readonly record struct RowExtent(
        int StaffIndex, double RowY, double Left, double Right, double RefpointUp);

    /// <summary>
    /// The chord rows ABOVE <paramref name="anchorStaff"/> that this system's mid-line
    /// numbers may hang on, each with its X extent and refpoint — gathered once per system
    /// from the placed symbols, so every number of the system asks the same few intervals.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/axis-group-interface.cc:178-182 Axis_group_interface::width —
    ///   a VerticalAxisGroup's X-extent is generic_group_extent (:221-237), the
    ///   relative_group_extent (:85-89) of its elements: relative_maybe_bound_group_extent
    ///   (:92-110) unites the elements' extents. The ChordNames group's elements are its
    ///   ChordName grobs, so its X extent runs from the line's first symbol to its last.
    /// </remarks>
    private static List<RowExtent> RowExtents(
        Rendering.ScoreTextMetrics fonts, SystemLayout system, StaffLayout anchorStaff,
        List<ChordNameLayout> rowSymbols)
    {
        var rows = new List<RowExtent>();
        List<int>? rejected = null;
        foreach (var sym in rowSymbols)
        {
            double right = sym.X + ChordNameEngraver.SymbolInkWidth(fonts, sym);
            int at = -1;
            for (int r = 0; r < rows.Count; r++)
                if (rows[r].StaffIndex == sym.RowStaffIndex) { at = r; break; }
            if (at >= 0)
            {
                var row = rows[at];
                rows[at] = row with
                {
                    Left = Math.Min(row.Left, sym.X),
                    Right = Math.Max(row.Right, right),
                    RefpointUp = Math.Min(row.RefpointUp, sym.YUp),
                };
                continue;
            }
            if (rejected is not null && rejected.Contains(sym.RowStaffIndex))
                continue;
            var layout = RowLayout(system, sym.RowStaffIndex);
            // Above the anchor staff (Y-up from the system top: larger is higher).
            if (layout is null || layout.IsHidden || layout.Y <= anchorStaff.Y)
            {
                (rejected ??= new List<int>()).Add(sym.RowStaffIndex);
                continue;
            }
            rows.Add(new RowExtent(sym.RowStaffIndex, layout.Y, sym.X, right, sym.YUp));
        }
        return rows;
    }

    /// <summary>
    /// The chord ROW a mid-line number hangs on, with its refpoint — the topmost of
    /// <paramref name="rows"/> whose X extent meets the number's own widened by
    /// <see cref="ExtremalStaffReach"/> — or null when none does and the number keeps the
    /// staff (a number outside every row's span: ledger
    /// barnumber.mid-line.no-chord-near.staff-to-ink-bottom, 3.050000 on both engines).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/side-position-interface.cc:513-547 move_to_extremal_staff — the
    ///   number's Y-parent becomes the extremal live element of the alignment that meets its
    ///   widened X; :549-562 drop every side-support element that no longer shares the
    ///   parent, so the STAFF (stavesFound) is gone and aligned_side (:347-351, :370) pays
    ///   the padding off an empty support: a height-0 skyline at the ROW's refpoint, the
    ///   symbols' baseline.
    /// LILYPOND-REF: lily/staff-grouper-interface.cc:31-56 get_extremal_staff — from the
    ///   top, the first live element whose X extent (its ELEMENTS', for an axis group — see
    ///   RowExtents) intersects the interval; a touching edge counts, the test being
    /// LILYPOND-REF: flower/include/interval.hh:212 is_empty — `left () > right ()', strict.
    /// MEASURED 2.26.0 (probes/barnumber-mid-line.ly): BNM number "2" ink bottom 1.000000
    /// over the chord baseline (6.045 over the staff refpoint); BNT, every chord sharped, the
    /// same 1.000000 — the symbols' height does not enter; BNE, a chord in bar 1 only,
    /// 3.050000 over the staff: the row's extent ends at bar 1 and spans none of 2-4.
    /// MEASURED 2.26.0 (probes/barnumber-row-extent.ly, session 789): BRX, a chord in bars 1
    /// and 5 only, numbers 2 3 4 ALL 1.000000 over the chord baseline though no symbol is
    /// within a bar of them — the row's extent spans them; BRR "3" after a mid-line `.|:'
    /// (the chord 2.9 past the bar) 1.000000; BRK "3" after a mid-line key+meter change
    /// 1.000000. Session 788's port asked the nearest SYMBOL to reach instead, which kept
    /// BRR's and BRK's shapes — dogfood leadsheet-collide bars 3 and 11 — on the staff.
    /// Before session 788 Lily# set every mid-line number at the staff's 3.05 and, on a
    /// chord row carrying diagrams, printed it through the fingering (bars 12-13, session 767).
    /// <para>
    /// ⚠️ THE EXTENT IS THE SYMBOLS' INK, as LilyPond's ChordNames group's extent is its
    /// ChordName grobs'. A row's DIAGRAMS are not in it: LilyPond draws those in a FretBoards
    /// group of its own, which would take a number the names' span misses and pad it off that
    /// group's refpoint instead — a shape no book has shown yet, left unported and named here.
    /// A LYRICS row above the staff is not asked either (no corpus book has one).
    /// </para>
    /// </remarks>
    private static (int StaffIndex, double RefpointUp)? MidLineRowAnchor(
        List<RowExtent> rows, double x, double width)
    {
        double left = x - ExtremalStaffReach, right = x + width + ExtremalStaffReach;
        RowExtent? best = null;
        foreach (var row in rows)
        {
            if (row.Left > right || row.Right < left)
                continue;
            // The topmost of those the interval meets (Y-up: larger is higher).
            if (best is { } b && row.RowY <= b.RowY)
                continue;
            best = row;
        }
        return best is { } host ? (host.StaffIndex, host.RefpointUp) : null;
    }

    private static StaffLayout? RowLayout(SystemLayout system, int staffIndex)
    {
        if (system.StaffGroups.IsDefaultOrEmpty)
            return null;
        foreach (var group in system.StaffGroups)
            foreach (var st in group.Staves)
                if (st.StaffIndex == staffIndex)
                    return st;
        return null;
    }

    /// <summary>
    /// The rows' placed symbols (those on a row's line, <see cref="ChordNameLayout.RowStaffIndex"/>
    /// ≥ 0) bucketed by system, built once per calculation; null when there are none.
    /// </summary>
    private static List<ChordNameLayout>[]? RowSymbolsBySystem(
        ImmutableArray<SystemLayout> systems, ImmutableArray<ChordNameLayout> chordNames)
    {
        if (chordNames.IsDefaultOrEmpty)
            return null;
        int maxMeasure = -1;
        foreach (var system in systems)
            if (!system.Measures.IsDefaultOrEmpty)
                maxMeasure = Math.Max(maxMeasure, system.Measures[^1].MeasureIndex);
        if (maxMeasure < 0)
            return null;
        var systemOfMeasure = new int[maxMeasure + 1];
        Array.Fill(systemOfMeasure, -1);
        for (int s = 0; s < systems.Length; s++)
            if (!systems[s].Measures.IsDefaultOrEmpty)
                foreach (var ml in systems[s].Measures)
                    if ((uint)ml.MeasureIndex < (uint)systemOfMeasure.Length)
                        systemOfMeasure[ml.MeasureIndex] = s;
        List<ChordNameLayout>[]? buckets = null;
        foreach (var cn in chordNames)
        {
            if (cn.RowStaffIndex < 0 || (uint)cn.MeasureIndex >= (uint)systemOfMeasure.Length)
                continue;
            int s = systemOfMeasure[cn.MeasureIndex];
            if (s < 0)
                continue;
            buckets ??= new List<ChordNameLayout>[systems.Length];
            (buckets[s] ??= new List<ChordNameLayout>()).Add(cn);
        }
        return buckets;
    }

    /// <summary>
    /// The builder <see cref="Calculate"/> gathers a score's bar-number layouts into, lent
    /// from one builder the thread keeps between calculations.
    /// </summary>
    /// <remarks>
    /// MEASURED (session 457's census, Release, the reader's corpus, eight forward keystrokes
    /// a book): 2.17 calculations a keystroke at 22.68 numbers each (max 47), and all 4,010
    /// builders built were unreachable by the time the render that built them returned. The
    /// builders and their growth ladders were 5,232 B a keystroke, 0.14% of it.
    /// <para>
    /// ⚠️ NOT THE SAME SHAPE AS <see cref="NumberMeasures"/>'s builder above, and
    /// the difference is the whole reason one is parked and the other is not: that one knows
    /// its size (<c>measures.Length</c>) and hands its array over with
    /// <c>MoveToImmutable</c> — it allocates exactly the array that becomes the answer, so
    /// there is nothing to save. This one cannot know its size (a system's numbers depend on
    /// the visibility policy, the line starts and the every-nth arm), so it climbs from
    /// capacity 0 every call.
    /// </para>
    /// <para>
    /// RENTING TAKES IT OUT OF THE DRAWER (session 421's idiom), THE CLEARING IS ON GIVE
    /// (session 456) — a builder parked dirty would open the next score's numbers with this
    /// score's, which every snapshot of a numbered score sees. There is no early return and no
    /// throw between the rent and the give.
    /// </para>
    /// <para>
    /// WHY IT IS SAFE TO PARK, and this is the question the whole family of parked builders
    /// turned on: <c>ImmutableArray&lt;T&gt;.Builder.ToImmutable</c> COPIES, so the array handed
    /// to the caller is never the one the drawer keeps. MEASURED rather than read off the
    /// documentation (session 459, .NET 10.0.12, reference identity through reflection on
    /// <c>Builder._elements</c> and <c>ImmutableArray.array</c>): not aliased at
    /// <c>Count == Capacity</c>, below capacity, at <c>Count == 0</c>, or after growing from
    /// capacity 0 — while the same probe DID see <c>MoveToImmutable</c> and
    /// <c>DrainToImmutable</c> hand their array over, which is what calibrates it. ⚠️ AND
    /// THOSE TWO DETACH IT (they leave the builder at capacity 0), so no exit can leave a
    /// parked builder owning a caller's array; what a Move/Drain site loses instead is the
    /// POINT of parking, since the drawer would start from empty every time. That is why the
    /// exact-sized siblings — <see cref="NumberMeasures"/>'s list above,
    /// <c>LayoutEngine.Prelim</c>'s carried moves, <c>MeasureLayouter</c>'s item layouts — are
    /// not parked: they already hand their array over. (This remark lived on the ledger-line
    /// spanner's drawer until session 523 deleted that engraver; every other parked builder's
    /// remark points here.)
    /// </para>
    /// <para>
    /// WHAT IT RETAINS is one builder a thread at that thread's longest score — 47 numbers,
    /// emptied, so it pins no layout.
    /// </para>
    /// </remarks>
    [ThreadStatic]
    private static ImmutableArray<BarNumberLayout>.Builder? t_layoutBuilder;

    /// <summary>Takes the thread's layout builder, or makes the thread's first.</summary>
    private static ImmutableArray<BarNumberLayout>.Builder RentLayoutBuilder()
    {
        var builder = t_layoutBuilder;
        if (builder is null)
            return ImmutableArray.CreateBuilder<BarNumberLayout>();
        t_layoutBuilder = null;
        return builder;
    }

    /// <summary>Puts a finished calculation's builder back, emptied, with its capacity.</summary>
    private static void GiveLayoutBuilder(ImmutableArray<BarNumberLayout>.Builder builder)
    {
        builder.Clear();
        t_layoutBuilder = builder;
    }
}
