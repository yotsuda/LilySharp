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

                double startX;
                if (segment.IsFirst && spanner.StartItemIndex < measureLayouts[segment.StartMeasureIndex].Items.Length)
                {
                    var startItem = measureLayouts[segment.StartMeasureIndex].Items[spanner.StartItemIndex];
                    startX = measureLayouts[segment.StartMeasureIndex].X + startItem.X;
                }
                else
                {
                    startX = measureLayouts[segment.StartMeasureIndex].X + BoundPadding;
                }

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
    /// Detects text spanner spans from music marks.
    /// Expression marks (rit., accel.) that should span a duration
    /// are converted to TextSpannerItems.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/text-spanner-engraver.cc start/end event handling
    ///
    /// A text spanner starts at a rit/accel mark and extends to:
    /// 1. The next rit/accel mark on the same staff
    /// 2. The end of the next measure (if no terminating event found)
    /// <para>
    /// ⚠️ LILYSHARP-OWN, ALL OF IT. LilyPond's spanner runs from <c>\startTextSpan</c> to an
    /// explicit <c>\stopTextSpan</c> and there is no terminator spelling here at all — no
    /// stop form in the annotation vocabulary and no length argument on the mark — so both
    /// arms above are this engine's own convention rather than a port, and a bare
    /// <c>@rit</c> can only ever mean "one measure" unless another mark cuts it short.
    /// </para>
    /// <para>
    /// ⚠️ A TEMPO CHANGE DOES NOT END ONE, whatever arm 1 used to say. It read "the next
    /// tempo-related mark (another rit/accel, or a tempo change)" and the search list is
    /// <c>ritAccelMarks</c>: a Tempo mark has never been in it. MEASURED 2026-08-29 with a
    /// rit in bar 1 and a <c>tempo 160</c> section starting at bar 5 — the spanner came out
    /// one bar, the fallback, not four. Whether it SHOULD end there is a real question and
    /// an open one; what is not open is that the comment described a branch that does not
    /// exist.
    /// </para>
    /// <para>
    /// ⚠️ A MARK CANNOT BE ENDED BY ANOTHER PLAYING OF ITSELF, and until 2026-08-29 it was.
    /// <c>musicMarks</c> holds the marks of the PLAYED piece, so a section the form repeats
    /// contributes one instance per playing — the same written <c>@rit</c>, at the same
    /// <c>SourcePosition</c>, in two different measures. The "next rit/accel" search then
    /// found the second instance and ran the first spanner all the way to it, across every
    /// bar in between. MEASURED on the reported book (user report 2026-08-29,
    /// <c>scratch/ベースタブLy/Untitled-6.lys</c>, form <c>A |: B :| A "A2"</c>): the first
    /// rit. covered six bars and ran through the whole of section B, while the second — with
    /// no later instance to find — covered the one bar the fallback gives. Same source, two
    /// lengths. The minimal pair is <c>test/rit-span-in-a-repeated-section.lys</c> against
    /// its once-played control.
    /// </para>
    /// <para>
    /// ⚠️ THE IDENTITY IS THE SOURCE POSITION, not the object: two playings are two
    /// <c>MusicMarkItem</c>s, so <c>!=</c> does not see it. Every mark is built with its
    /// syntax node's <c>SourceStart</c> (MeasureCollector), so two instances share a position
    /// exactly when they are one written mark, and two separately written marks never do —
    /// including two <c>@rit</c>s written in the same bar.
    /// </para>
    /// </remarks>
    public static ImmutableArray<TextSpannerItem> DetectTextSpanners(
        ImmutableArray<MusicMarkItem> musicMarks)
    {
        var spanners = ImmutableArray.CreateBuilder<TextSpannerItem>();

        // F3/B: keep each mark's ORIGINAL index in musicMarks (== score.MusicMarks) so the
        // spanner can re-derive its data-pos from the live score on reuse.
        var ritAccelMarks = musicMarks
            .Select((m, i) => (Mark: m, Index: i))
            .Where(x => x.Mark.Type == MusicMarkType.Rit || x.Mark.Type == MusicMarkType.Accel)
            .OrderBy(x => x.Mark.MeasureIndex)
            .ToList();

        if (ritAccelMarks.Count == 0)
            return ImmutableArray<TextSpannerItem>.Empty;

        foreach (var (mark, srcIndex) in ritAccelMarks)
        {
            // Find the next rit/accel mark ON THE SAME STAFF (terminates this
            // spanner). Without the staff filter a rit on staff 2 would end at a rit
            // in a later measure on staff 1 (they share score.MusicMarks).
            // ...and NOT ANOTHER PLAYING OF THIS SAME WRITTEN MARK, which is what a form
            // that repeats a section produces — see this method's remark for the reported
            // book and why the object identity `!=` cannot see it.
            var nextMark = ritAccelMarks
                .Select(x => x.Mark)
                .FirstOrDefault(m =>
                    m != mark && m.SourcePosition != mark.SourcePosition &&
                    m.StaffIndex == mark.StaffIndex &&
                    m.MeasureIndex > mark.MeasureIndex);

            int endMeasure;
            int endItem;

            if (nextMark != null)
            {
                endMeasure = nextMark.MeasureIndex;
                endItem = 0;
            }
            else
            {
                // No end found — extend to end of the mark's measure + 1
                endMeasure = mark.MeasureIndex + 1;
                endItem = 0;
            }

            // Only add if there's actually a span
            if (endMeasure > mark.MeasureIndex)
            {
                spanners.Add(new TextSpannerItem(
                    Text: mark.Text,
                    StartMeasureIndex: mark.MeasureIndex,
                    StartItemIndex: 0,
                    EndMeasureIndex: endMeasure,
                    EndItemIndex: endItem,
                    Style: TextSpannerStyle.DashedLine,
                    SourcePosition: mark.SourcePosition,
                    SourceIndex: srcIndex,
                    StaffIndex: mark.StaffIndex
                ));
            }
        }

        return spanners.ToImmutable();
    }

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
