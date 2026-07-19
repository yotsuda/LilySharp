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
/// Layout information for a figured bass group.
/// All coordinates are in staff spaces.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/figured-bass-engraver.cc:200-350 print method
/// LILYPOND-REF: scm/define-grobs.scm:352-364 BassFigure defaults
/// </remarks>
public readonly record struct FiguredBassLayout(
    int MeasureIndex,
    double X,                                         // X position (staff spaces)
    double YUp,                                       // Y-up (frame B): staff-spaces above
                                                      // the staff middle, up+ (topmost figure)
    ImmutableArray<string> FigureTexts,               // Text for each figure, top to bottom
    int SourcePosition,
    int SourceIndex = -1,                             // F3/B: index into score.FiguredBasses
    int StaffIndex = -1                               // owning staff, so the draw resolves its middle
);

/// <summary>
/// Calculates positions for figured bass figures.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/figured-bass-engraver.cc - Figured_bass_engraver
/// LILYPOND-REF: lily/figured-bass-position-engraver.cc - positioning
/// LILYPOND-REF: scm/define-grobs.scm:352-364 BassFigure defaults
///
/// Figured bass appears below the staff, with figures stacked vertically.
/// Each figure occupies ~1.6 staff spaces of vertical height (an approximation of
/// LP's BassFigureAlignment stacking; see FigureSpacing).
/// </remarks>
internal static class FiguredBassEngraver
{
    // LILYPOND-REF: scm/define-grobs.scm:352 BassFigure (no numeric padding/baseline-skip of its own)
    private const double StaffPadding = 1.0;   // Padding below staff bottom
    // APPROXIMATION (ungrounded value): LP has no BassFigure baseline-skip. Stacked
    // figures are aligned by BassFigureAlignment (define-grobs.scm:366 —
    // align-to-minimum-distances, stacking-dir DOWN, padding -inf), i.e. purely by each
    // BassFigureLine's Y-extent. We don't model that alignment skyline; 1.6 ss is a
    // hand-picked per-row height standing in for it (revisit in the B-tier value pass).
    internal const double FigureSpacing = 1.6;  // Vertical spacing between stacked figures
    private const double BelowStaffY = 5.0;    // Y offset below staff (staff bottom = 4.0)

    /// <summary>
    /// Calculates layout for all figured bass items.
    /// </summary>
    // Digit CAP height at the 3.0 ss figure font (measured ~0.5 em for the
    // serif face): the top digit's ink above its baseline. The old 0.76
    // was roughly half the real ascent, so a skyline-dropped row could
    // still print its digit tops through the content above (e.g. a
    // staccatissimo dagger under a stem).
    internal const double FigureTopExtent = 1.5;
    internal const double MinFigureBoxWidth = 0.8;
    // relatedstaff-spacing padding + the distance→drop math live in SkylineDrop (shared with lyrics).

    public static ImmutableArray<FiguredBassLayout> Calculate(
        ImmutableArray<FiguredBassItem> figuredBasses,
        ImmutableArray<SystemLayout> systems,
        ImmutableArray<MeasureLayout> measureLayouts,
        ImmutableArray<Measure> measures = default,
        Dictionary<int, ImmutableArray<Measure>>? measuresByStaff = null,
        IReadOnlyList<(VerticalSkyline up, VerticalSkyline down)>? systemSkylines = null)
    {
        if (figuredBasses.IsDefaultOrEmpty)
            return ImmutableArray<FiguredBassLayout>.Empty;

        var layouts = ImmutableArray.CreateBuilder<FiguredBassLayout>(figuredBasses.Length);

        for (int fbi = 0; fbi < figuredBasses.Length; fbi++)
        {
            var fb = figuredBasses[fbi];
            if (fb.MeasureIndex >= measureLayouts.Length)
                continue;

            var measureLayout = measureLayouts[fb.MeasureIndex];

            if (measureLayout.Columns.IsDefaultOrEmpty
                && fb.ItemIndex >= measureLayout.Items.Length)
                continue;

            // Resolve this figure's OWN staff (multi-staff) measures for the item X.
            // The staff offset is no longer baked: the Y is stored relative to the
            // staff middle (frame B) and resolved to the right staff at each consumer.
            var fbMeasures = LayoutUtilities.ResolveStaffMeasures(measuresByStaff, fb.StaffIndex, measures);

            double x = measureLayout.X + LayoutUtilities.GetItemXOffset(
                fbMeasures, fb.MeasureIndex, fb.ItemIndex, measureLayout);
            // Y-up (frame B): the topmost figure sits below the staff, in staff-spaces
            // above the staff middle (BelowStaffY+StaffPadding below the staff top,
            // reflected through the middle at 2).
            double yUp = 2.0 - (BelowStaffY + StaffPadding);

            var figureTexts = fb.Figures
                .Select(f => f.DisplayText)
                .ToImmutableArray();

            layouts.Add(new FiguredBassLayout(
                fb.MeasureIndex,
                x,
                yUp,
                figureTexts,
                fb.SourcePosition,
                fbi,
                StaffIndex: fb.StaffIndex));
        }

        var result = layouts.ToImmutable();
        if (systemSkylines != null && !systems.IsDefaultOrEmpty)
            result = ApplySkylineDrop(result, systems, systemSkylines);
        return result;
    }

    private static ImmutableArray<FiguredBassLayout> ApplySkylineDrop(
        ImmutableArray<FiguredBassLayout> layouts, ImmutableArray<SystemLayout> systems,
        IReadOnlyList<(VerticalSkyline up, VerticalSkyline down)> systemSkylines)
    {
        var measureToSystem = SpannerBreakSubstitution.BuildMeasureToSystemMap(systems);

        var fbUp = new Dictionary<int, VerticalSkyline>();
        var basicY = new Dictionary<int, double>();
        foreach (var lay in layouts)
        {
            if (!measureToSystem.TryGetValue(lay.MeasureIndex, out int s)) continue;
            double halfW = MinFigureBoxWidth / 2.0;
            var box = VerticalSkyline.FromBox(
                lay.X - halfW, lay.X + halfW, 0, FigureTopExtent, VerticalDirection.Up);
            if (fbUp.TryGetValue(s, out var sky)) sky.Merge(box);
            else fbUp[s] = box;
            // basicY is the system-relative device floor (the old lay.Y). Reconstruct
            // it from Y-up against this figure's own staff offset.
            double staffOffset = LayoutUtilities.StaffOffsetInSystem(systems[s], lay.StaffIndex);
            double layY = staffOffset + (2.0 - lay.YUp);
            basicY[s] = basicY.TryGetValue(s, out var b) ? System.Math.Min(b, layY) : layY;
        }

        // Figured bass uses each system's own lowest figure as the basic-distance floor.
        var systemDrop = SkylineDrop.Compute(fbUp, s => basicY[s], systemSkylines);

        if (systemDrop.Count == 0)
            return layouts;

        // The drop d is a downward (device) shift; in Y-up that is a decrease.
        return System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(layouts, lay =>
            measureToSystem.TryGetValue(lay.MeasureIndex, out int s)
            && systemDrop.TryGetValue(s, out var d) && d > 0
                ? lay with { YUp = lay.YUp - d }
                : lay)).ToImmutableArray();
    }
}
