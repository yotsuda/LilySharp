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
/// Layout information for a hairpin wedge.
/// All coordinates are in staff spaces.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/hairpin.cc:110-358
/// </remarks>
public readonly record struct HairpinLayout(
    /// <summary>Start measure index (for system Y lookup).</summary>
    int StartMeasureIndex,
    /// <summary>Start X position (staff spaces from score start).</summary>
    double StartX,
    /// <summary>End X position.</summary>
    double EndX,
    /// <summary>Y position (center line of the wedge, staff spaces from staff top).</summary>
    double Y,
    /// <summary>
    /// Opening at the start (left) end (half-height, in staff spaces).
    /// LILYPOND-REF: lily/hairpin.cc:180-220 — continued/continuing height fractions
    /// For crescendo: 0 (point). For decrescendo: full or fractional opening.
    /// </summary>
    double StartOpening,
    /// <summary>
    /// Opening at the end (right) end (half-height, in staff spaces).
    /// For crescendo: full or fractional opening. For decrescendo: 0 (point).
    /// </summary>
    double EndOpening,
    /// <summary>Crescendo or decrescendo.</summary>
    HairpinDirection Direction,
    /// <summary>Source position for click-to-source mapping.</summary>
    int SourcePosition
);

/// <summary>
/// Calculates positions for hairpin (crescendo/decrescendo) wedges.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/hairpin.cc:110-358 print()
/// LILYPOND-REF: scm/define-grobs.scm:1641-1666 Hairpin grob
///
/// Hairpin parameters from LilyPond:
/// - height: 0.6666 staff spaces (maximum opening)
/// - bound-padding: 1.0
/// - minimum-length: 2.0
/// - thickness: 1.0 (staff line widths)
/// </remarks>
public static class HairpinEngraver
{
    /// <summary>
    /// Maximum opening of the wedge (half-height).
    /// </summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:1655 (height . 0.6666)</remarks>
    private const double Height = 0.6666;

    /// <summary>
    /// Horizontal padding from note/dynamic to hairpin endpoint.
    /// </summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:1643 (bound-padding . 1.0)</remarks>
    private const double BoundPadding = 1.0;

    /// <summary>
    /// Minimum hairpin length.
    /// </summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:1659 (minimum-length . 2.0)</remarks>
    private const double MinimumLength = 2.0;

    /// <summary>
    /// Y position below staff for hairpins (same level as dynamics).
    /// </summary>
    private const double BaseY = 5.2;

    /// <summary>
    /// Height fraction for the broken end of a continued hairpin (right edge at line break).
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/hairpin.cc:180-220 — broken hairpin height fractions</remarks>
    private const double ContinuedFraction = 2.0 / 3.0;

    /// <summary>
    /// Height fraction for the broken end of a continuing hairpin (left edge at line start).
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/hairpin.cc:180-220 — broken hairpin height fractions</remarks>
    private const double ContinuingFraction = 1.0 / 3.0;

    /// <summary>
    /// Calculates layout for all hairpins in a score.
    /// Handles broken hairpins across system breaks with correct height fractions.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/hairpin.cc:180-220 — broken hairpin height calculation
    /// When a hairpin crosses a system break:
    /// - continued (end of first system): opening = height * 2/3
    /// - continuing (start of next system): opening = height * 1/3
    /// </remarks>
    public static ImmutableArray<HairpinLayout> Calculate(
        ImmutableArray<HairpinItem> hairpins,
        ImmutableArray<SystemLayout> systems,
        ImmutableArray<MeasureLayout> measureLayouts)
    {
        if (hairpins.IsDefaultOrEmpty)
            return ImmutableArray<HairpinLayout>.Empty;

        // Build measure → system index mapping
        var measureToSystemIdx = new Dictionary<int, int>();
        for (int sysIdx = 0; sysIdx < systems.Length; sysIdx++)
        {
            foreach (var ml in systems[sysIdx].Measures)
                measureToSystemIdx[ml.MeasureIndex] = sysIdx;
        }

        var layouts = ImmutableArray.CreateBuilder<HairpinLayout>();

        foreach (var hairpin in hairpins)
        {
            if (hairpin.StartMeasureIndex >= measureLayouts.Length ||
                hairpin.EndMeasureIndex >= measureLayouts.Length)
                continue;

            if (!measureToSystemIdx.TryGetValue(hairpin.StartMeasureIndex, out int startSysIdx) ||
                !measureToSystemIdx.TryGetValue(hairpin.EndMeasureIndex, out int endSysIdx))
                continue;

            double fullOpening = Height / 2.0;

            if (startSysIdx == endSysIdx)
            {
                // Same system: normal hairpin
                var (startX, endX) = CalculateEndpoints(hairpin, measureLayouts);
                double startOpening, endOpening;
                if (hairpin.Direction == HairpinDirection.Crescendo)
                {
                    startOpening = 0;
                    endOpening = fullOpening;
                }
                else
                {
                    startOpening = fullOpening;
                    endOpening = 0;
                }

                layouts.Add(new HairpinLayout(
                    hairpin.StartMeasureIndex, startX, endX, BaseY,
                    startOpening, endOpening, hairpin.Direction, hairpin.SourcePosition));
            }
            else
            {
                // LILYPOND-REF: lily/hairpin.cc:180-220 — broken hairpin across system(s)
                double continuedOpening = fullOpening * ContinuedFraction;
                double continuingOpening = fullOpening * ContinuingFraction;

                for (int sysIdx = startSysIdx; sysIdx <= endSysIdx; sysIdx++)
                {
                    bool isFirst = sysIdx == startSysIdx;
                    bool isLast = sysIdx == endSysIdx;
                    var system = systems[sysIdx];

                    double segStartX, segEndX;
                    int segStartMeasure;

                    if (isFirst)
                    {
                        segStartX = CalculateStartX(hairpin, measureLayouts);
                        segEndX = system.Measures[^1].X + system.Measures[^1].Width;
                        segStartMeasure = hairpin.StartMeasureIndex;
                    }
                    else if (isLast)
                    {
                        segStartX = system.Measures[0].X;
                        segEndX = CalculateEndX(hairpin, measureLayouts);
                        segStartMeasure = system.Measures[0].MeasureIndex;
                    }
                    else
                    {
                        // Middle system: spans entire system
                        segStartX = system.Measures[0].X;
                        segEndX = system.Measures[^1].X + system.Measures[^1].Width;
                        segStartMeasure = system.Measures[0].MeasureIndex;
                    }

                    if (segEndX - segStartX < MinimumLength)
                        segEndX = segStartX + MinimumLength;

                    double startOpening, endOpening;
                    if (hairpin.Direction == HairpinDirection.Crescendo)
                    {
                        startOpening = isFirst ? 0 : continuingOpening;
                        endOpening = isLast ? fullOpening : continuedOpening;
                    }
                    else
                    {
                        startOpening = isFirst ? fullOpening : continuingOpening;
                        endOpening = isLast ? 0 : continuedOpening;
                    }

                    layouts.Add(new HairpinLayout(
                        segStartMeasure, segStartX, segEndX, BaseY,
                        startOpening, endOpening, hairpin.Direction, hairpin.SourcePosition));
                }
            }
        }

        return layouts.ToImmutable();
    }

    private static (double startX, double endX) CalculateEndpoints(
        HairpinItem hairpin, ImmutableArray<MeasureLayout> measureLayouts)
    {
        double startX = CalculateStartX(hairpin, measureLayouts);
        double endX = CalculateEndX(hairpin, measureLayouts);
        if (endX - startX < MinimumLength)
            endX = startX + MinimumLength;
        return (startX, endX);
    }

    private static double CalculateStartX(HairpinItem hairpin, ImmutableArray<MeasureLayout> measureLayouts)
    {
        var startMeasure = measureLayouts[hairpin.StartMeasureIndex];
        if (hairpin.StartItemIndex < startMeasure.Items.Length)
        {
            var startItem = startMeasure.Items[hairpin.StartItemIndex];
            return startMeasure.X + startItem.X + startItem.Width + BoundPadding * 0.5;
        }
        return startMeasure.X + BoundPadding;
    }

    private static double CalculateEndX(HairpinItem hairpin, ImmutableArray<MeasureLayout> measureLayouts)
    {
        var endMeasure = measureLayouts[hairpin.EndMeasureIndex];
        if (hairpin.EndItemIndex < endMeasure.Items.Length)
        {
            var endItem = endMeasure.Items[hairpin.EndItemIndex];
            return endMeasure.X + endItem.X - BoundPadding * 0.5;
        }
        return endMeasure.X + endMeasure.Width - BoundPadding;
    }

    /// <summary>
    /// Detects hairpin spans from music marks and dynamics.
    /// A hairpin starts at a cresc/decresc mark and ends at the next absolute dynamic.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/dynamic-engraver.cc start/end event handling
    /// </remarks>
    public static ImmutableArray<HairpinItem> DetectHairpins(
        ImmutableArray<MusicMarkItem> musicMarks,
        ImmutableArray<DynamicItem> dynamics)
    {
        var hairpins = ImmutableArray.CreateBuilder<HairpinItem>();

        // Sort all events by position (measure, item)
        var crescMarks = musicMarks
            .Where(m => m.Type == MusicMarkType.Cresc ||
                        m.Type == MusicMarkType.Decresc ||
                        m.Type == MusicMarkType.Dim)
            .OrderBy(m => m.MeasureIndex)
            .ToList();

        if (crescMarks.Count == 0)
            return ImmutableArray<HairpinItem>.Empty;

        // Sort dynamics by position
        var sortedDynamics = dynamics
            .OrderBy(d => d.MeasureIndex)
            .ThenBy(d => d.ItemIndex)
            .ToList();

        foreach (var mark in crescMarks)
        {
            var direction = mark.Type == MusicMarkType.Cresc
                ? HairpinDirection.Crescendo
                : HairpinDirection.Decrescendo;

            // Find the next absolute dynamic after this cresc/decresc
            var nextDynamic = sortedDynamics
                .FirstOrDefault(d =>
                    d.MeasureIndex > mark.MeasureIndex ||
                    (d.MeasureIndex == mark.MeasureIndex && d.ItemIndex > 0));

            // Find the next cresc/decresc mark (another hairpin starts there)
            var nextMark = crescMarks
                .FirstOrDefault(m =>
                    m != mark &&
                    (m.MeasureIndex > mark.MeasureIndex ||
                     (m.MeasureIndex == mark.MeasureIndex)));

            // End at whichever comes first: next dynamic or next cresc/decresc
            int endMeasure;
            int endItem;

            if (nextDynamic != null && (nextMark == null ||
                nextDynamic.MeasureIndex < nextMark.MeasureIndex ||
                (nextDynamic.MeasureIndex == nextMark.MeasureIndex && nextDynamic.ItemIndex <= 0)))
            {
                endMeasure = nextDynamic.MeasureIndex;
                endItem = nextDynamic.ItemIndex;
            }
            else if (nextMark != null)
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
            if (endMeasure > mark.MeasureIndex ||
                (endMeasure == mark.MeasureIndex && endItem > 0))
            {
                hairpins.Add(new HairpinItem(
                    Direction: direction,
                    StartMeasureIndex: mark.MeasureIndex,
                    StartItemIndex: 0,
                    EndMeasureIndex: endMeasure,
                    EndItemIndex: endItem,
                    SourcePosition: mark.SourcePosition
                ));
            }
        }

        return hairpins.ToImmutable();
    }
}
