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
/// Layout information for a trill spanner ("tr" glyph + wavy line extension).
/// All coordinates are in staff spaces.
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/define-grobs.scm:2175-2230 TrillSpanner grob
/// </remarks>
public readonly record struct TrillSpannerLayout(
    /// <summary>Start measure index (for system Y lookup).</summary>
    int StartMeasureIndex,
    /// <summary>X position of the "tr" glyph.</summary>
    double GlyphX,
    /// <summary>X position where the wavy line starts (after "tr" glyph).</summary>
    double LineStartX,
    /// <summary>X position where the wavy line ends.</summary>
    double LineEndX,
    /// <summary>Y position (staff spaces from staff top, negative = above staff).</summary>
    double Y,
    /// <summary>Source position for click-to-source mapping.</summary>
    int SourcePosition
);

/// <summary>
/// Calculates positions for trill spanners (tr symbol + wavy line extension).
/// Handles cross-system spanners by extending to system edge.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/trill-spanner-engraver.cc Trill_spanner_engraver class
/// LILYPOND-REF: scm/define-grobs.scm:2175-2230 TrillSpanner grob defaults
///
/// TrillSpanner parameters from LilyPond:
/// - direction: UP (always above staff)
/// - style: trill (wavy line)
/// - padding: 0.5
/// - staff-padding: 1.0
/// - outside-staff-priority: 50
/// </remarks>
public static class TrillSpannerEngraver
{
    /// <summary>
    /// Horizontal padding from bound objects.
    /// </summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:2189 (padding . 0.5)</remarks>
    private const double BoundPadding = 0.5;

    /// <summary>
    /// Staff padding for trill spanners.
    /// </summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:2213 (staff-padding . 1.0)</remarks>
    private const double StaffPadding = 1.0;

    /// <summary>
    /// Width of the "tr" glyph in staff spaces.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: feta-scripts.mf scripts.trill glyph metrics
    /// Measured from Emmentaler font: ~1.6 staff spaces
    /// </remarks>
    private const double TrillGlyphWidth = 1.6;

    /// <summary>
    /// Height of the trill glyph in staff spaces.
    /// </summary>
    private const double TrillGlyphHeight = 1.2;

    /// <summary>
    /// Gap between "tr" glyph and wavy line start.
    /// </summary>
    private const double GlyphLinePadding = 0.3;

    /// <summary>
    /// Calculates layout for all trill spanners.
    /// Handles cross-system spanners by extending the wavy line to the system edge.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/trill-spanner-engraver.cc:92-125 positioning
    /// LILYPOND-REF: lily/line-spanner.cc:526-648 cross-system spanner handling
    /// </remarks>
    public static ImmutableArray<TrillSpannerLayout> Calculate(
        ImmutableArray<TrillSpannerItem> trillSpanners,
        ImmutableArray<SystemLayout> systems,
        ImmutableArray<MeasureLayout> measureLayouts)
    {
        if (trillSpanners.IsDefaultOrEmpty)
            return ImmutableArray<TrillSpannerLayout>.Empty;

        // Build measure-to-system mapping
        var measureToSystem = new Dictionary<int, int>();
        for (int sysIdx = 0; sysIdx < systems.Length; sysIdx++)
            foreach (var m in systems[sysIdx].Measures)
                measureToSystem[m.MeasureIndex] = sysIdx;

        var layouts = ImmutableArray.CreateBuilder<TrillSpannerLayout>();

        foreach (var spanner in trillSpanners)
        {
            // Find start position
            if (spanner.StartMeasureIndex >= measureLayouts.Length)
                continue;

            var startMeasure = measureLayouts[spanner.StartMeasureIndex];
            if (spanner.StartItemIndex >= startMeasure.Items.Length)
                continue;

            var startItem = startMeasure.Items[spanner.StartItemIndex];
            double startX = startMeasure.X + startItem.X;

            // Determine which system start and end are on
            int startSys = measureToSystem.GetValueOrDefault(spanner.StartMeasureIndex, 0);
            int endSys = measureToSystem.GetValueOrDefault(spanner.EndMeasureIndex, startSys);

            // Y position: above staff with padding
            // LILYPOND-REF: scm/define-grobs.scm:2213 (staff-padding . 1.0)
            double y = -StaffPadding - TrillGlyphHeight;

            if (startSys == endSys)
            {
                // Same system — simple case
                double endX = GetEndX(spanner, measureLayouts);
                double glyphX = startX;
                double lineStartX = startX + TrillGlyphWidth + GlyphLinePadding;

                if (endX > glyphX)
                {
                    layouts.Add(new TrillSpannerLayout(
                        spanner.StartMeasureIndex, glyphX, lineStartX, endX, y,
                        spanner.SourcePosition));
                }
            }
            else
            {
                // Cross-system spanner: emit one layout per system
                // First system: "tr" glyph + wavy line to system edge
                double systemEdgeX = systems[startSys].Width - BoundPadding;
                double glyphX = startX;
                double lineStartX = startX + TrillGlyphWidth + GlyphLinePadding;

                if (systemEdgeX > glyphX)
                {
                    layouts.Add(new TrillSpannerLayout(
                        spanner.StartMeasureIndex, glyphX, lineStartX, systemEdgeX, y,
                        spanner.SourcePosition));
                }

                // Continuation systems: wavy line only (no "tr" glyph)
                for (int sys = startSys + 1; sys <= endSys && sys < systems.Length; sys++)
                {
                    double contStartX = systems[sys].PrefixWidth + BoundPadding;
                    double contEndX;

                    if (sys == endSys)
                    {
                        // Final system: end at the stop note
                        contEndX = GetEndX(spanner, measureLayouts);
                    }
                    else
                    {
                        // Middle system: extend to system edge
                        contEndX = systems[sys].Width - BoundPadding;
                    }

                    if (contEndX > contStartX)
                    {
                        // Use measure index of the first measure in this system for Y lookup
                        int contMeasureIdx = systems[sys].Measures.Length > 0
                            ? systems[sys].Measures[0].MeasureIndex
                            : spanner.StartMeasureIndex;

                        // No glyph on continuation — set GlyphX = LineStartX to suppress glyph
                        layouts.Add(new TrillSpannerLayout(
                            contMeasureIdx, contStartX, contStartX, contEndX, y,
                            spanner.SourcePosition));
                    }
                }
            }
        }

        return layouts.ToImmutable();
    }

    private static double GetEndX(TrillSpannerItem spanner, ImmutableArray<MeasureLayout> measureLayouts)
    {
        if (spanner.EndMeasureIndex < measureLayouts.Length)
        {
            var endMeasure = measureLayouts[spanner.EndMeasureIndex];
            if (spanner.EndItemIndex < endMeasure.Items.Length)
            {
                var endItem = endMeasure.Items[spanner.EndItemIndex];
                return endMeasure.X + endItem.X - BoundPadding;
            }
            return endMeasure.X + endMeasure.Width - BoundPadding;
        }
        var lastMeasure = measureLayouts[^1];
        return lastMeasure.X + lastMeasure.Width - BoundPadding;
    }
}
