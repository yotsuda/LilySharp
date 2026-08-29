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
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Layout information for a text spanner (text + dashed line).
/// All coordinates are in staff spaces.
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/define-grobs.scm:3835-3864 TextSpanner grob
/// </remarks>
public readonly record struct TextSpannerLayout(
    // Start measure index (for system Y lookup).
    int StartMeasureIndex,
    // Start X position (staff spaces from score start).
    double StartX,
    // End X position.
    double EndX,
    // X position where the text ends and the line begins.
    double LineStartX,
    // Y in the Y-up frame: staff-spaces ABOVE the system top, up-positive (frame B).
    // The renderer reflects it to device against the segment's system top
    // (sy + old-Y == sy − YUp).
    double YUp,
    // Display text (e.g., "rit.", "accel.").
    string Text,
    // Line style.
    TextSpannerStyle Style,
    // Dash period (length of one dash+gap cycle, in staff spaces).
    double DashPeriod,
    // Dash fraction (proportion of period that is visible).
    double DashFraction,
    // Source position for click-to-source mapping.
    int SourcePosition,
    // F3/B: index of the originating rit/accel mark in score.MusicMarks,
    // so a reused layout re-derives data-pos from the live score. -1 = unresolved.
    int SourceIndex = -1,
    // Which staff this spanner hangs under (per-staff stacking).
    int StaffIndex = 0
);

/// <summary>
/// Calculates positions for text spanners (text + dashed line markings).
/// Implements LilyPond's outside-staff-priority stacking.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/line-spanner.cc:526-648 Line_spanner::print()
/// LILYPOND-REF: scm/define-grobs.scm:3835-3864 TextSpanner grob defaults
/// LILYPOND-REF: lily/axis-group-interface.cc:859-985 skyline_spacing()
///
/// TextSpanner (outside-staff-priority = 350) is placed BELOW
/// DynamicLineSpanner (outside-staff-priority = 250).
/// The priority-based stacking from axis-group-interface.cc ensures that
/// higher-priority elements are placed further from the staff.
///
/// TextSpanner parameters from LilyPond:
/// - dash-period: 3.0
/// - dash-fraction: 0.2
/// - bound-padding: 0.25
/// - style: dashed-line
/// - font-shape: italic
/// - staff-padding: 0.8
/// </remarks>
internal static class TextSpannerEngraver
{
    /// <summary>
    /// Dash period: length of one dash+gap cycle.
    /// </summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:3845 (dash-period . 3.0)</remarks>
    private const double DashPeriod = 3.0;

    /// <summary>
    /// Dash fraction: proportion of the period that is visible line.
    /// </summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:3844 (dash-fraction . 0.2)</remarks>
    private const double DashFraction = 0.2;

    /// <summary>
    /// Horizontal padding from bound objects.
    /// </summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:3837 (padding . 0.25)</remarks>
    private const double BoundPadding = 0.25;

    /// <summary>
    /// Estimated text width per character for italic text (staff spaces).
    /// </summary>
    private const double CharWidth = 0.55;

    /// <summary>
    /// Padding between text and line start.
    /// </summary>
    private const double TextLinePadding = 0.5;

    /// <summary>
    /// Minimum line length to be drawn.
    /// </summary>
    private const double MinimumLineLength = 1.0;

    // Staff geometry
    private const double StaffBottom = 4.0;

    /// <summary>
    /// Staff padding for text spanners. One home: EngravingDefaults' outside-staff
    /// declaration table (the LILYPOND-REF lives beside the entry). Consumed here for
    /// the below-staff seed only — the ABOVE-staff refpoint floor the declaration
    /// really is lives in OutsideStaffStacker.PlaceTextSpanners.
    /// </summary>
    private const double StaffPadding = EngravingDefaults.TextSpannerStaffPadding;

    /// <summary>
    /// Text ascent above baseline for text spanner text (italic serif, font-size 2.0).
    /// </summary>
    private const double TextAscent = 1.0;

    /// <summary>
    /// The size the spanner's "rit."/"accel." is SET IN — one home for a number that had
    /// three spellings (the draw's <c>FontSize * 0.5</c>, and a bare <c>4.0 * 0.5</c> in
    /// both the collision pass and the paging silhouette). It has to be the drawn size
    /// wherever it is read: every consumer measures this text's INK, and an ink measured at
    /// another size reserves a band the page does not draw.
    /// </summary>
    internal const double TextFontSize = 4.0 * 0.5;

    /// <summary>
    /// Vertical padding between outside-staff layers.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: axis-group-interface.cc default_outside_staff_padding_ = 0.46
    /// </remarks>
    private const double BetweenLayerPadding = 0.46;

    /// <summary>
    /// Horizontal tolerance for overlap detection (staff spaces).
    /// </summary>
    private const double HorizontalOverlapTolerance = 1.5;

    /// <summary>
    /// Calculates layout for all text spanners, respecting outside-staff-priority stacking.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: axis-group-interface.cc:864-989 skyline_spacing()
    /// TextSpanner (priority 350) is placed below DynamicLineSpanner (priority 250).
    /// For each text spanner, we find overlapping dynamics and place the text spanner
    /// below the lowest dynamic in that horizontal range.
    /// </remarks>
    public static ImmutableArray<TextSpannerLayout> Calculate(
        ImmutableArray<TextSpannerItem> textSpanners,
        ImmutableArray<SystemLayout> systems,
        ImmutableArray<MeasureLayout> measureLayouts,
        ImmutableArray<DynamicLayout> dynamicLayouts,
        Func<int, int, double>? staffYAt = null)
    {
        if (textSpanners.IsDefaultOrEmpty)
            return ImmutableArray<TextSpannerLayout>.Empty;

        var measureToSystem = SpannerBreakSubstitution.BuildMeasureToSystemMap(systems);
        var layouts = ImmutableArray.CreateBuilder<TextSpannerLayout>(textSpanners.Length);

        foreach (var spanner in textSpanners)
        {
            if (spanner.StartMeasureIndex >= measureLayouts.Length ||
                spanner.EndMeasureIndex >= measureLayouts.Length)
                continue;

            // LILYPOND-REF: lily/spanner.cc:36-144 — Spanner::do_break_processing
            // LILYPOND-REF: lily/line-spanner.cc:546-600 — Line_spanner breaks at system boundaries
            foreach (var (segment, system) in SpannerBreakSubstitution.BrokenPieces(
                spanner.StartMeasureIndex, spanner.EndMeasureIndex, systems, measureToSystem))
            {
                if (segment.StartMeasureIndex >= measureLayouts.Length ||
                    segment.EndMeasureIndex >= measureLayouts.Length)
                    continue;

                if (system.Measures.IsDefaultOrEmpty)
                    continue;

                // LILYPOND-REF: lily/line-spanner.cc:149-176 Line_spanner::calc_bound_info —
                //   the bound COLUMN's extent read at bound-details.left's attach-dir (LEFT),
                //   which for these columns is the note-head anchor.
                // LILYPOND-REF: lily/line-spanner.cc:596-600 Line_spanner::print —
                //   span_points[d] += -d * gaps[d] * magstep * dz.direction (), i.e. the
                //   bound's own `padding` is spent BEFORE the left text stencil is
                //   translated there. MEASURED: ledger textspanner.x.control.label-to-notehead
                //   is that 0.25 alone (LilyPond +0.250000000, TXH).
                // ⚠️ A CONTINUATION piece keeps the measure's origin: LilyPond's `left-broken`
                //   details flip attach-dir to RIGHT (scm/define-grobs.scm, TextSpanner
                //   bound-details) and that branch is NOT PORTED HERE. No ledger point reads a
                //   broken piece's left edge, so it is named rather than guessed at.
                double startX;
                if (segment.IsFirst && spanner.StartItemIndex < measureLayouts[segment.StartMeasureIndex].Items.Length)
                {
                    var startItem = measureLayouts[segment.StartMeasureIndex].Items[spanner.StartItemIndex];
                    startX = measureLayouts[segment.StartMeasureIndex].X + startItem.X + BoundPadding;
                }
                else
                {
                    startX = measureLayouts[segment.StartMeasureIndex].X + BoundPadding;
                }

                // ⚠️ THE RIGHT BOUND'S ARITHMETIC IS NOT PORTED HERE, and the left repair is
                //   what made that legible: bound-details.right declares NO attach-dir, so
                //   LilyPond's calc_bound_info takes the bound column's CENTRE
                //   (linear_combination at CENTER) where this takes its LEFT edge — MEASURED
                //   in probes/textspanner-left-bound.ly's own dump, rightX 31.309760708228
                //   against that column's head anchor 30.657660708228, about 0.65 apart. No
                //   ledger point reads the right end, so closing it starts with a point.
                double endX;
                if (segment.IsLast)
                {
                    var endMeasure = measureLayouts[segment.EndMeasureIndex];
                    if (spanner.EndItemIndex < endMeasure.Items.Length)
                    {
                        var endItem = endMeasure.Items[spanner.EndItemIndex];
                        endX = endMeasure.X + endItem.X - BoundPadding;
                    }
                    else
                    {
                        endX = endMeasure.X + endMeasure.Width - BoundPadding;
                    }
                }
                else
                {
                    // LILYPOND-REF: lily/line-spanner.cc:577-600 — extend to system edge for broken right.
                    var lastMeasure = system.Measures[^1];
                    endX = lastMeasure.X + lastMeasure.Width - BoundPadding;
                }

                // First segment shows the text; continuation segments draw line only.
                string segText = segment.IsFirst ? spanner.Text : "";
                double textWidth = segText.Length * CharWidth;
                double lineStartX = segText.Length > 0
                    ? startX + textWidth + TextLinePadding
                    : startX;

                if (lineStartX > endX - MinimumLineLength)
                    lineStartX = endX;

                // Below THIS spanner's staff, clear of the SAME staff's dynamics
                // only. The staff's within-system offset moves the whole band down.
                double staffOffset = staffYAt?.Invoke(spanner.StartMeasureIndex, spanner.StaffIndex) ?? 0;
                var sameStaffDynamics = dynamicLayouts.IsDefaultOrEmpty
                    ? dynamicLayouts
                    : dynamicLayouts.Where(d => d.StaffIndex == spanner.StaffIndex).ToImmutableArray();
                double y = CalculateYWithPriorityStacking(
                    startX, endX, segment.StartMeasureIndex,
                    sameStaffDynamics, measureToSystem, staffOffset);

                layouts.Add(new TextSpannerLayout(
                    StartMeasureIndex: segment.StartMeasureIndex,
                    StartX: startX,
                    EndX: endX,
                    LineStartX: lineStartX,
                    // Store Y-up from the system top (= −device y); the internal
                    // placement still computes device y (it reads the device dynamics band).
                    YUp: -y,
                    Text: segText,
                    Style: spanner.Style,
                    DashPeriod: DashPeriod,
                    DashFraction: DashFraction,
                    SourcePosition: spanner.SourcePosition,
                    SourceIndex: spanner.SourceIndex,
                    StaffIndex: spanner.StaffIndex
                ));
            }
        }

        return layouts.ToImmutable();
    }

    /// <summary>
    /// Calculates the Y position for a text spanner, placing it below any
    /// overlapping dynamics (respecting outside-staff-priority ordering).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: axis-group-interface.cc:912-972 priority-based stacking
    /// Elements are sorted by outside-staff-priority. Lower priority (250 for dynamics)
    /// is placed closer to the staff. Higher priority (350 for text spanners) is placed
    /// further from the staff, avoiding collision with already-placed lower-priority elements.
    /// </remarks>
    private static double CalculateYWithPriorityStacking(
        double startX, double endX, int startMeasureIndex,
        ImmutableArray<DynamicLayout> dynamicLayouts,
        Dictionary<int, int> measureToSystem,
        double staffOffset = 0)
    {
        // Minimum Y: below THIS staff (its within-system offset) with padding + text ascent
        double minY = StaffBottom + staffOffset + StaffPadding + TextAscent;

        if (dynamicLayouts.IsDefaultOrEmpty)
            return minY;

        // Find the system this text spanner belongs to
        if (!measureToSystem.TryGetValue(startMeasureIndex, out int spannerSystem))
            return minY;

        // Find the lowest (maximum Y) dynamic BOTTOM that overlaps horizontally in the same
        // system. The bottom, not the baseline: each dynamic's descent is its own glyph's
        // ink (DynamicEngraver.InkOf), so the deepest baseline is not always the deepest
        // ink — `p` hangs 0.584 under its baseline where `m` hangs 0.028.
        double maxDynamicBottom = double.MinValue;
        foreach (var dyn in dynamicLayouts)
        {
            // Must be in the same system
            if (!measureToSystem.TryGetValue(dyn.MeasureIndex, out int dynSystem) ||
                dynSystem != spannerSystem)
                continue;

            // Check horizontal overlap (with tolerance for nearby elements)
            if (dyn.X + HorizontalOverlapTolerance > startX &&
                dyn.X - HorizontalOverlapTolerance < endX)
            {
                // dyn.YUp is Y-up; the caller passes only SAME-staff dynamics, so this
                // staff's offset (staffOffset) reflects it into the system-relative
                // device frame this method (and minY) works in.
                double dynY = staffOffset + (2.0 - dyn.YUp);
                var (_, descent) = DynamicEngraver.InkOf(dyn.Text, dyn.IsExpressiveText);
                // Device-down frame: the glyph's bottom is BELOW its baseline, i.e. +.
                maxDynamicBottom = Math.Max(maxDynamicBottom, dynY + descent);
            }
        }

        if (maxDynamicBottom > double.MinValue)
        {
            // Place text spanner below the lowest overlapping dynamic.
            // The text spanner's visual top = baseline - text ascent.
            // Constraint: spanner_top >= dynamic_bottom + padding
            // => spanner_baseline >= dynamic_bottom + padding + text_ascent
            double requiredY = maxDynamicBottom + BetweenLayerPadding + TextAscent;
            return Math.Max(requiredY, minY);
        }

        return minY;
    }

    /// <summary>
    /// Pairs the text-spanner marks of a played score into spans, and reports the marks
    /// that pair with nothing.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/text-spanner-engraver.cc:59-88 Text_spanner_engraver::process_music, :117-127 Text_spanner_engraver::finalize —
    /// this is that engraver, with its three answers:
    /// a STOP with nothing open is "cannot find start of text spanner" and makes no grob;
    /// a START while one is open is "already have a text spanner" and the OPEN one keeps
    /// the span (the new mark is dropped, not nested); and a span still open at the end is
    /// "unterminated text spanner" followed by <c>suicide()</c> — the text vanishes with the
    /// line, because a spanner with no right bound is not a shorter spanner.
    /// <para>
    /// ⚠️ ONE OPEN SPAN PER (STAFF, VOICE), because that is where LilyPond keeps the
    /// engraver (<c>ly/engraver-init.ly:375</c>, the <c>Voice</c> context). A
    /// <c>@!rit</c> written in another voice reaches nothing, which is the same answer
    /// LilyPond gives and the reason <see cref="MusicMarkItem.VoiceIndex"/> had to exist.
    /// </para>
    /// <para>
    /// ⚠️ THE MARKS ARE THE PLAYED PIECE'S, so a section the form repeats contributes one
    /// instance of each mark PER PLAYING — the same written <c>@rit</c>, at the same
    /// <c>SourcePosition</c>, in two different measures. Pairing in played order handles
    /// that without knowing it: playing 1's START meets playing 1's STOP before playing 2's
    /// START is reached. The three devices that stood here before this — a one-measure
    /// fallback, a "next rit/accel" search, and a guard against a mark ending its own second
    /// playing — were all consequences of there being no terminator to pair with, and all
    /// three are retired with it (session 288's remark carries what each got wrong).
    /// </para>
    /// <para>
    /// ⚠️ ONE ROOT CAUSE, ONE DIAGNOSTIC: an unterminated mark inside a repeated section is
    /// unterminated once, however many times it is played, so the warnings are reported per
    /// <c>SourcePosition</c> — the same identity <see cref="MusicMarkItem.SourcePosition"/>
    /// carries everywhere, and the same doctrine the measure passes state.
    /// </para>
    /// </remarks>
    internal static (ImmutableArray<TextSpannerItem> Spanners,
                     ImmutableArray<UnpairedSpanWarning> Unpaired)
        PairTextSpanners(ImmutableArray<MusicMarkItem> musicMarks)
    {
        if (musicMarks.IsDefaultOrEmpty)
            return ([], []);

        // F3/B: keep each mark's ORIGINAL index in musicMarks (== score.MusicMarks) so the
        // spanner can re-derive its data-pos from the live score on reuse.
        var spanMarks = musicMarks
            .Select((m, i) => (Mark: m, Index: i))
            .Where(x => x.Mark.Type is MusicMarkType.TextSpanStart or MusicMarkType.TextSpanStop)
            // Played order: by measure, then by the moment within it. A measure-start mark
            // carries AnchorItemIndex −1 and so sorts before the notes, which is where it
            // stands. The original index breaks the remaining ties, keeping the order the
            // collector wrote — two marks on the SAME note are read left to right.
            .OrderBy(x => x.Mark.MeasureIndex)
            .ThenBy(x => x.Mark.AnchorItemIndex)
            .ThenBy(x => x.Index)
            .ToList();

        if (spanMarks.Count == 0)
            return ([], []);

        var spanners = ImmutableArray.CreateBuilder<TextSpannerItem>();
        var unpaired = ImmutableArray.CreateBuilder<UnpairedSpanWarning>();
        var reported = new HashSet<(int Position, SpanPairingFault Fault)>();
        var open = new Dictionary<(int Staff, int Voice), (MusicMarkItem Mark, int Index)>();

        void Report(int sourcePosition, SpanPairingFault fault)
        {
            if (reported.Add((sourcePosition, fault)))
                unpaired.Add(new UnpairedSpanWarning(sourcePosition, SpanKind.TextSpanner, fault));
        }

        foreach (var (mark, srcIndex) in spanMarks)
        {
            var key = (mark.StaffIndex, mark.VoiceIndex);

            if (mark.Type == MusicMarkType.TextSpanStop)
            {
                if (!open.TryGetValue(key, out var start))
                {
                    Report(mark.SourcePosition, SpanPairingFault.StopWithNoStart);
                    continue;
                }
                open.Remove(key);
                spanners.Add(new TextSpannerItem(
                    Text: start.Mark.Text,
                    StartMeasureIndex: start.Mark.MeasureIndex,
                    // BOTH ENDS ARE THE WRITER'S. The note the mark stands on IS the
                    // bound, on the left exactly as on the right — a measure-start mark
                    // carries AnchorItemIndex −1 and clamps to the measure's first item,
                    // which is where it stands. This used to be the constant 0, and the
                    // asymmetry that left (a `@rit` on the third note drawn from the
                    // measure's head, while `@!rit` took its own note) is what ledger
                    // textspanner.x.label-to-notehead was opened to see.
                    StartItemIndex: Math.Max(start.Mark.AnchorItemIndex, 0),
                    EndMeasureIndex: mark.MeasureIndex,
                    EndItemIndex: Math.Max(mark.AnchorItemIndex, 0),
                    Style: TextSpannerStyle.DashedLine,
                    SourcePosition: start.Mark.SourcePosition,
                    SourceIndex: start.Index,
                    StaffIndex: start.Mark.StaffIndex));
                continue;
            }

            if (open.ContainsKey(key))
            {
                // LilyPond warns on the NEW mark and keeps the open one; a second span does
                // not nest and does not replace.
                Report(mark.SourcePosition, SpanPairingFault.StartWhileOpen);
                continue;
            }
            open[key] = (mark, srcIndex);
        }

        foreach (var (mark, _) in open.Values)
            Report(mark.SourcePosition, SpanPairingFault.Unterminated);

        return (spanners.ToImmutable(), unpaired.ToImmutable());
    }

    /// <summary>
    /// The text spanners a played score draws — the paired half of
    /// <see cref="PairTextSpanners"/>. The unpaired half is reported by
    /// <c>SpanPairingValidator</c>, which reads the SAME call, so a mark can never be
    /// warned about and drawn (or drawn and not warned about) at once.
    /// </summary>
    public static ImmutableArray<TextSpannerItem> DetectTextSpanners(
        ImmutableArray<MusicMarkItem> musicMarks)
        => PairTextSpanners(musicMarks).Spanners;

    /// <summary>
    /// THIS STAFF'S accel./rit. SPANNERS AS INK ABOVE THE STAFF, in the staff-local frame
    /// the per-staff skyline is built in (origin = the staff's TOP LINE, up-positive) — so
    /// that a LINE STANDING ABOVE the staff makes room for them.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/axis-group-interface.cc:860-985 skyline_spacing — an outside-staff
    /// grob is placed and then LEFT IN its VerticalAxisGroup's skyline, which is the profile
    /// lily/align-interface.cc:217-268 walks and lily/page-layout-problem.cc:948-990
    /// distributes the loose lines (ChordNames, Lyrics) against. Lily# placed the spanner in
    /// the collision pass (<c>OutsideStaffStacker.PlaceTextSpanners</c>) and then spaced the
    /// row above against a staff silhouette the spanner was not in, so the two never met:
    /// `@rit` printed THROUGH the chord row and through the lyric row above its staff
    /// (reported 2026-08-28 against Untitled-6.lys). Books TSU/TSY in
    /// probes/textspanner-under-row.ly measure LilyPond's answer: the row above rises by
    /// 2.370858871 = the spanner's ink top over the staff's own, exactly.
    /// <para>
    /// ⚠️ THE TWO TERMS ARE THE ONES <c>OutsideStaffStacker.PlaceTextSpanners</c> USES, spelt here
    /// because this pass runs BEFORE the systems exist and the stacker's answer is not
    /// available yet — the same reason <c>StaffTupletBracketLayouts</c> re-runs its engraver
    /// staff-locally. They are: aligned_side's staff-padding FLOOR
    /// (<c>StaffLineThickness/2 + 0.8</c> over the top line), and the collision pass's
    /// outside-staff-padding 0.46 over whatever this staff's profile already holds.
    /// ⚠️ WHAT THIS DOES NOT SEE, named rather than hidden: a mover the STACKER adds after
    /// the dynamics — an inline `@chord` seed, a volta — can lift the DRAWN spanner above
    /// the band reserved here, and the row would then be spaced for the lower one. The
    /// observers are the ledger's textspanner.chord-row.* / textspanner.lyric-row.* pairs;
    /// a book that puts a rit. under a row AND an inline chord symbol on the same staff is
    /// the one to cut when that configuration acquires a reader.
    /// </para>
    /// </remarks>
    internal static VerticalSkyline InkAboveStaff(
        ScoreTextMetrics fonts,
        ImmutableArray<TextSpannerItem> spanners,
        ImmutableArray<MeasureLayout> measureLayouts,
        VerticalSkyline accumulatedUp)
    {
        var ink = new VerticalSkyline(VerticalDirection.Up);
        if (spanners.IsDefaultOrEmpty || measureLayouts.IsDefaultOrEmpty)
            return ink;

        // The measures of THIS system — the array the caller hands down is one system's,
        // and a spanner reaching past either end is a broken piece whose visible part is
        // clamped to it (the same shape SpannerBreakSubstitution.BrokenPieces gives the
        // drawn pass, decided here by measure INDEX because this array is not indexed by it).
        int firstMeasure = measureLayouts[0].MeasureIndex;
        int lastMeasure = measureLayouts[^1].MeasureIndex;
        double lineHalf = EngravingDefaults.StaffLineThickness / 2.0;

        foreach (var spanner in spanners)
        {
            if (spanner.EndMeasureIndex < firstMeasure || spanner.StartMeasureIndex > lastMeasure)
                continue;

            bool startsHere = spanner.StartMeasureIndex >= firstMeasure;
            bool endsHere = spanner.EndMeasureIndex <= lastMeasure;
            var startLayout = startsHere
                ? FindMeasure(measureLayouts, spanner.StartMeasureIndex)
                : measureLayouts[0];
            var endLayout = endsHere
                ? FindMeasure(measureLayouts, spanner.EndMeasureIndex)
                : measureLayouts[^1];
            if (startLayout is null || endLayout is null)
                continue;

            double startX = startsHere && spanner.StartItemIndex < startLayout.Items.Length
                ? startLayout.X + startLayout.Items[spanner.StartItemIndex].X
                : startLayout.X + BoundPadding;
            double endX = endsHere && spanner.EndItemIndex < endLayout.Items.Length
                ? endLayout.X + endLayout.Items[spanner.EndItemIndex].X - BoundPadding
                : endLayout.X + endLayout.Width - BoundPadding;
            if (endX <= startX)
                continue;

            // Only the FIRST piece carries the text; a continuation draws the rule alone
            // (PlaceTextSpanners reads the same rule off segment.IsFirst).
            string text = startsHere ? spanner.Text : "";
            double top = lineHalf, bottom = lineHalf;
            if (!string.IsNullOrEmpty(text))
            {
                var textInk = fonts.Ink(text, TextFontSize, TextRole.Text, FontStyle.Italic);
                top = Math.Max(top, textInk.Top);
                bottom = Math.Max(bottom, -textInk.Bottom);
            }

            double y = Math.Max(
                lineHalf + StaffPadding,
                accumulatedUp.MaxProtrusionInRange(startX, endX)
                    + OutsideStaffStacker.OutsideStaffPadding + bottom);

            // The DRAWN shape, not a flat band over the span: the rule is lineHalf thick
            // everywhere and the text stands only where the text is, which is what a row
            // whose symbols sit past the label has to clear (LilyPond reads the stencil's
            // own outline).
            ink.Merge(VerticalSkyline.FromBox(
                startX, endX, y - lineHalf, y + lineHalf, VerticalDirection.Up));
            if (!string.IsNullOrEmpty(text))
                ink.Merge(VerticalSkyline.FromBox(
                    startX, Math.Min(startX + text.Length * CharWidth + TextLinePadding, endX),
                    y - bottom, y + top, VerticalDirection.Up));
        }

        return ink;
    }

    /// <summary>The layout of one measure of this system, by its score-wide index.</summary>
    private static MeasureLayout? FindMeasure(
        ImmutableArray<MeasureLayout> measureLayouts, int measureIndex)
    {
        foreach (var m in measureLayouts)
            if (m.MeasureIndex == measureIndex)
                return m;
        return null;
    }
}
