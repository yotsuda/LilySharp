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
/// LILYPOND-REF: scm/define-grobs.scm:3504-3535 TextSpanner grob
/// </remarks>
public readonly record struct TextSpannerLayout(
    /// <summary>Start measure index (for system Y lookup).</summary>
    int StartMeasureIndex,
    /// <summary>Start X position (staff spaces from score start).</summary>
    double StartX,
    /// <summary>End X position.</summary>
    double EndX,
    /// <summary>X position where the text ends and the line begins.</summary>
    double LineStartX,
    /// <summary>Y position (staff spaces from staff top, positive = down).</summary>
    double Y,
    /// <summary>Display text (e.g., "rit.", "accel.").</summary>
    string Text,
    /// <summary>Line style.</summary>
    TextSpannerStyle Style,
    /// <summary>Dash period (length of one dash+gap cycle, in staff spaces).</summary>
    double DashPeriod,
    /// <summary>Dash fraction (proportion of period that is visible).</summary>
    double DashFraction,
    /// <summary>Source position for click-to-source mapping.</summary>
    int SourcePosition
);

/// <summary>
/// Calculates positions for text spanners (text + dashed line markings).
/// Implements LilyPond's outside-staff-priority stacking.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/line-spanner.cc:526-648 Line_spanner::print()
/// LILYPOND-REF: scm/define-grobs.scm:3504-3535 TextSpanner grob defaults
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
public static class TextSpannerEngraver
{
    /// <summary>
    /// Dash period: length of one dash+gap cycle.
    /// </summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:3514 (dash-period . 3.0)</remarks>
    private const double DashPeriod = 3.0;

    /// <summary>
    /// Dash fraction: proportion of the period that is visible line.
    /// </summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:3513 (dash-fraction . 0.2)</remarks>
    private const double DashFraction = 0.2;

    /// <summary>
    /// Horizontal padding from bound objects.
    /// </summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:3506 (padding . 0.25)</remarks>
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
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:3521 (staff-padding . 0.8)</remarks>
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
        ImmutableArray<DynamicLayout> dynamicLayouts)
    {
        if (textSpanners.IsDefaultOrEmpty)
            return ImmutableArray<TextSpannerLayout>.Empty;

        // Build measure-to-system index mapping for comparing elements in the same system
        var measureToSystem = new Dictionary<int, int>();
        for (int sysIdx = 0; sysIdx < systems.Length; sysIdx++)
            foreach (var m in systems[sysIdx].Measures)
                measureToSystem[m.MeasureIndex] = sysIdx;

        var layouts = ImmutableArray.CreateBuilder<TextSpannerLayout>(textSpanners.Length);

        foreach (var spanner in textSpanners)
        {
            if (spanner.StartMeasureIndex >= measureLayouts.Length ||
                spanner.EndMeasureIndex >= measureLayouts.Length)
                continue;

            var startMeasure = measureLayouts[spanner.StartMeasureIndex];

            // Determine if the spanner crosses system boundaries.
            // Each system's MeasureLayout.X resets from the left margin, so we cannot
            // mix X coordinates from different systems.
            // LILYPOND-REF: line-spanner.cc:546-600 Line_spanner breaks at system boundaries
            bool crossesSystem = false;
            if (measureToSystem.TryGetValue(spanner.StartMeasureIndex, out int startSys) &&
                measureToSystem.TryGetValue(spanner.EndMeasureIndex, out int endSys))
            {
                crossesSystem = startSys != endSys;
            }

            // Calculate start X: at the start note
            double startX;
            if (spanner.StartItemIndex < startMeasure.Items.Length)
            {
                var startItem = startMeasure.Items[spanner.StartItemIndex];
                startX = startMeasure.X + startItem.X;
            }
            else
            {
                startX = startMeasure.X + BoundPadding;
            }

            // Calculate end X: before the end note, or at system edge for cross-system spanners
            double endX;
            if (crossesSystem)
            {
                // Clip to end of the start system's last measure
                // LILYPOND-REF: line-spanner.cc:577-590 broken piece extends to system edge
                var startSystemMeasures = systems[startSys].Measures;
                var lastMeasureInSystem = startSystemMeasures[startSystemMeasures.Length - 1];
                endX = lastMeasureInSystem.X + lastMeasureInSystem.Width - BoundPadding;
            }
            else
            {
                var endMeasure = measureLayouts[spanner.EndMeasureIndex];
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

            // Calculate where the text ends and the line starts
            double textWidth = spanner.Text.Length * CharWidth;
            double lineStartX = startX + textWidth + TextLinePadding;

            // Only draw line if there's enough space
            if (lineStartX > endX - MinimumLineLength)
            {
                lineStartX = endX; // No line, just text
            }

            // Calculate Y position using outside-staff-priority stacking
            double y = CalculateYWithPriorityStacking(
                startX, endX, spanner.StartMeasureIndex,
                dynamicLayouts, measureToSystem);

            layouts.Add(new TextSpannerLayout(
                StartMeasureIndex: spanner.StartMeasureIndex,
                StartX: startX,
                EndX: endX,
                LineStartX: lineStartX,
                Y: y,
                Text: spanner.Text,
                Style: spanner.Style,
                DashPeriod: DashPeriod,
                DashFraction: DashFraction,
                SourcePosition: spanner.SourcePosition
            ));
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
        Dictionary<int, int> measureToSystem)
    {
        // Minimum Y: below staff with padding + text ascent
        double minY = StaffBottom + StaffPadding + TextAscent;

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
                maxDynamicY = Math.Max(maxDynamicY, dyn.Y);
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

        var ritAccelMarks = musicMarks
            .Where(m => m.Type == MusicMarkType.Rit || m.Type == MusicMarkType.Accel)
            .OrderBy(m => m.MeasureIndex)
            .ToList();

        if (ritAccelMarks.Count == 0)
            return ImmutableArray<TextSpannerItem>.Empty;

        foreach (var mark in ritAccelMarks)
        {
            // Find the next rit/accel mark (terminates this spanner)
            var nextMark = ritAccelMarks
                .FirstOrDefault(m =>
                    m != mark && m.MeasureIndex > mark.MeasureIndex);

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
                    SourcePosition: mark.SourcePosition
                ));
            }
        }

        return spanners.ToImmutable();
    }
}
