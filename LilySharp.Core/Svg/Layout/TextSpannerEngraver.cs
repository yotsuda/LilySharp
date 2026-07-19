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
/// LILYPOND-REF: lily/axis-group-interface.cc:864-989 skyline_spacing()
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
    /// Staff padding for text spanners.
    /// </summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:3852 (staff-padding . 0.8)</remarks>
    private const double StaffPadding = 0.8;

    /// <summary>
    /// Text ascent above baseline for text spanner text (italic serif, font-size 2.0).
    /// </summary>
    private const double TextAscent = 1.0;

    /// <summary>
    /// Descent of dynamic text below baseline (for "p", "g" etc. descenders).
    /// </summary>
    private const double DynamicTextDescent = 0.6;

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
                    YUp: 0.0 - y,
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

        // Find the lowest (maximum Y) dynamic that overlaps horizontally in the same system
        double maxDynamicY = double.MinValue;
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
                maxDynamicY = Math.Max(maxDynamicY, dynY);
            }
        }

        if (maxDynamicY > double.MinValue)
        {
            // Place text spanner below the lowest overlapping dynamic.
            // The dynamic's visual bottom = baseline + text descent.
            // The text spanner's visual top = baseline - text ascent.
            // Constraint: spanner_top >= dynamic_bottom + padding
            // => spanner_baseline >= dynamic_bottom + padding + text_ascent
            double dynamicBottom = maxDynamicY + DynamicTextDescent;
            double requiredY = dynamicBottom + BetweenLayerPadding + TextAscent;
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
    /// 1. The next tempo-related mark (another rit/accel, or a tempo change)
    /// 2. The end of the next measure (if no terminating event found)
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
            var nextMark = ritAccelMarks
                .Select(x => x.Mark)
                .FirstOrDefault(m =>
                    m != mark && m.StaffIndex == mark.StaffIndex &&
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
}
