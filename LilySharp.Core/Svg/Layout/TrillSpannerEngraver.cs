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
    // Start measure index (for system Y lookup).
    int StartMeasureIndex,
    // X position of the "tr" glyph.
    double GlyphX,
    // X position where the wavy line starts (after "tr" glyph).
    double LineStartX,
    // X position where the wavy line ends.
    double LineEndX,
    // Y position (staff spaces from staff top, negative = above staff).
    double Y,
    // Source position for click-to-source mapping.
    int SourcePosition,
    // Global staff index this spanner belongs to (multi-staff). The
    // above-staff stacker only de-collides staff 0; lower staves keep their
    // engraver Y so they stay over their own staff.
    int StaffIndex = 0,
    int SourceIndex = -1   // F3/B: index into score.TrillSpanners (shared by all broken pieces)
);

/// <summary>
/// Calculates positions for trill spanners (tr symbol + wavy line extension).
/// Handles cross-system spanners by extending to system edge.
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/scheme-engravers.scm Trill_spanner_engraver class
/// LILYPOND-REF: scm/define-grobs.scm:2175-2230 TrillSpanner grob defaults
///
/// TrillSpanner parameters from LilyPond:
/// - direction: UP (always above staff)
/// - style: trill (wavy line)
/// - padding: 0.5
/// - staff-padding: 1.0
/// - outside-staff-priority: 50
/// </remarks>
internal static class TrillSpannerEngraver
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
    /// LILYPOND-REF: scm/scheme-engravers.scm:92-125 positioning
    /// LILYPOND-REF: lily/line-spanner.cc:526-648 cross-system spanner handling
    /// </remarks>
    public static ImmutableArray<TrillSpannerLayout> Calculate(
        ImmutableArray<TrillSpannerItem> trillSpanners,
        ImmutableArray<SystemLayout> systems,
        ImmutableArray<MeasureLayout> measureLayouts,
        Func<int, int, double>? staffYAt = null)
    {
        if (trillSpanners.IsDefaultOrEmpty)
            return ImmutableArray<TrillSpannerLayout>.Empty;

        var measureToSystem = SpannerBreakSubstitution.BuildMeasureToSystemMap(systems);
        var layouts = ImmutableArray.CreateBuilder<TrillSpannerLayout>();

        for (int ti = 0; ti < trillSpanners.Length; ti++)
        {
            var spanner = trillSpanners[ti];
            if (spanner.StartMeasureIndex >= measureLayouts.Length)
                continue;

            // Y position: above staff with padding, offset to this spanner's OWN
            // staff (multi-staff). LILYPOND-REF: scm/define-grobs.scm:2213
            double staffOffset = staffYAt?.Invoke(spanner.StartMeasureIndex, spanner.StaffIndex) ?? 0;
            double y = -StaffPadding - TrillGlyphHeight + staffOffset;

            var startMeasure = measureLayouts[spanner.StartMeasureIndex];
            if (spanner.StartItemIndex >= startMeasure.Items.Length)
                continue;

            var startItem = startMeasure.Items[spanner.StartItemIndex];
            double startX = startMeasure.X + startItem.X;

            // LILYPOND-REF: lily/spanner.cc:36-144 — Spanner::do_break_processing
            foreach (var (segment, system) in SpannerBreakSubstitution.BrokenPieces(
                spanner.StartMeasureIndex, spanner.EndMeasureIndex, systems, measureToSystem))
            {
                // First segment carries the "tr" glyph; continuation segments draw line only.
                double glyphX, lineStartX;
                if (segment.IsFirst)
                {
                    glyphX = startX;
                    lineStartX = startX + TrillGlyphWidth + GlyphLinePadding;
                }
                else
                {
                    // No glyph on continuation — set GlyphX == LineStartX so the renderer suppresses it.
                    glyphX = system.PrefixWidth + BoundPadding;
                    lineStartX = glyphX;
                }

                double endX;
                if (segment.IsLast)
                {
                    endX = GetEndX(spanner, measureLayouts);
                }
                else
                {
                    endX = system.Width - BoundPadding;
                }

                if (endX <= glyphX)
                    continue;

                layouts.Add(new TrillSpannerLayout(
                    segment.StartMeasureIndex, glyphX, lineStartX, endX, y,
                    spanner.SourcePosition, spanner.StaffIndex, ti));
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
