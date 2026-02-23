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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Tablature;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Handles layout calculations specific to multi-staff scores.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/system.cc
/// LILYPOND-REF: lily/staff-spacing.cc
/// </remarks>
public sealed class MultiStaffLayouter
{
    private readonly LayoutOptions _options;
    private readonly MeasureLayouter _measureLayouter;

    public MultiStaffLayouter(LayoutOptions options, MeasureLayouter measureLayouter)
    {
        _options = options;
        _measureLayouter = measureLayouter;
    }

    /// <summary>
    /// Calculates the total height of a multi-staff system.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/align-interface.cc internal_get_minimum_translations()
    /// Uses StaffSpacingParameters for intra-group and inter-group spacing.
    /// </remarks>
    public double CalculateSystemHeight(MultiStaffScore score)
    {
        double height = 0;
        double staffHeight = _options.StaffHeight;
        var sp = _options.StaffSpacing;

        for (int i = 0; i < score.StaffGroups.Length; i++)
        {
            var group = score.StaffGroups[i];

            if (group.IsGrandStaff)
            {
                // Intra-group: staff-staff-spacing basic distance
                height += staffHeight * 2 + (sp.StaffStaff.BasicDistance - staffHeight);
            }
            else
            {
                foreach (var staff in group.Staves)
                {
                    height += GetStaffHeight(staff);
                }
                if (group.StaffCount > 1)
                {
                    // Intra-group spacing for each pair
                    double intraSpacing = sp.StaffStaff.BasicDistance - staffHeight;
                    height += Math.Max(0, intraSpacing) * (group.StaffCount - 1);
                }
            }

            if (i < score.StaffGroups.Length - 1)
            {
                // Inter-group: staffgroup-staff-spacing basic distance
                double interGroupGap = sp.StaffGroupStaff.BasicDistance - staffHeight;
                bool nextIsOssia = score.StaffGroups[i + 1].Staves.Any(s => s.IsOssia);
                bool currentIsOssia = group.Staves.Any(s => s.IsOssia);
                if (nextIsOssia || currentIsOssia)
                    interGroupGap *= OssiaScaleFactor;
                height += interGroupGap;
            }
        }

        return height;
    }

    /// <summary>
    /// Scale factor for ossia staves: magstep(-3) = 2^(-3/6) ≈ 0.707.
    /// LILYPOND-REF: ly/engraver-init.ly — ossia staves typically use fontSize = #-3
    /// with \override StaffSymbol.staff-space = #(magstep -3)
    /// </summary>
    private const double OssiaScaleFactor = 0.70;

    /// <summary>
    /// Gets the height of a staff in staff spaces.
    /// Standard staves have 4 staff spaces (5 lines).
    /// Tab staves have (stringCount - 1) staff spaces.
    /// Ossia staves are scaled down.
    /// </summary>
    private double GetStaffHeight(Staff staff)
    {
        if (staff.IsTab && staff.Tuning.HasValue)
        {
            int stringCount = Tunings.GetStringCount(staff.Tuning.Value);
            return stringCount - 1; // e.g., Guitar: 5, Bass: 3
        }
        if (staff.IsOssia)
            return _options.StaffHeight * OssiaScaleFactor;
        return _options.StaffHeight;
    }

    /// <summary>
    /// Layouts all staff groups within a system.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/align-interface.cc internal_get_minimum_translations()
    /// Uses StaffSpacingParameters for intra-group and inter-group spacing.
    /// </remarks>
    public ImmutableArray<StaffGroupLayout> LayoutStaffGroups(MultiStaffScore score, double systemY)
    {
        var builder = ImmutableArray.CreateBuilder<StaffGroupLayout>();
        double currentY = 0;
        double staffHeight = _options.StaffHeight;
        var sp = _options.StaffSpacing;
        int globalStaffIndex = 0;

        for (int i = 0; i < score.StaffGroups.Length; i++)
        {
            var group = score.StaffGroups[i];

            if (group.IsGrandStaff)
            {
                var layout = LayoutGrandStaffGroup(group, currentY, staffHeight, sp.StaffStaff, globalStaffIndex);
                builder.Add(layout);
                currentY += layout.Height;
            }
            else
            {
                var layout = LayoutSingleStaffGroup(group, currentY, staffHeight, sp.StaffStaff, globalStaffIndex);
                builder.Add(layout);
                currentY += layout.Height;
            }

            if (i < score.StaffGroups.Length - 1)
            {
                // Inter-group gap: staffgroup-staff basic distance
                // Reduce spacing when adjacent to an ossia staff
                double interGroupGap = sp.StaffGroupStaff.BasicDistance - staffHeight;
                bool nextIsOssia = score.StaffGroups[i + 1].Staves.Any(s => s.IsOssia);
                bool currentIsOssia = group.Staves.Any(s => s.IsOssia);
                if (nextIsOssia || currentIsOssia)
                    interGroupGap *= OssiaScaleFactor;
                currentY += interGroupGap;
            }

            globalStaffIndex += group.StaffCount;
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Layouts a grand staff group (piano/organ style with brace).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:3042-3045 staff-staff-spacing
    /// </remarks>
    private StaffGroupLayout LayoutGrandStaffGroup(
        StaffGroup group, double y, double staffHeight, VerticalSpacingSpec staffSpec, int startIndex)
    {
        var staffLayouts = ImmutableArray.CreateBuilder<StaffLayout>();
        double currentY = y;

        // Intra-group spacing: staff-staff basic distance is center-to-center
        double staffSpacing = staffSpec.BasicDistance - staffHeight;

        for (int i = 0; i < group.Staves.Length; i++)
        {
            var staff = group.Staves[i];
            staffLayouts.Add(new StaffLayout(
                StaffIndex: startIndex + i,
                Clef: staff.Clef,
                Y: currentY,
                Height: staffHeight,
                Tuning: staff.Tuning,
                InstrumentName: staff.InstrumentName));

            if (i < group.Staves.Length - 1)
                currentY += staffHeight + Math.Max(0, staffSpacing);
        }

        double totalHeight = currentY + staffHeight - y;
        double braceX = _options.MarginLeft - 1.0;

        var grandStaffLayout = new GrandStaffLayout(
            Staves: staffLayouts.ToImmutable(),
            BraceX: braceX,
            BraceTop: y,
            BraceBottom: y + totalHeight);

        return StaffGroupLayout.CreateGrandStaff(
            staffLayouts.ToImmutable(),
            y,
            totalHeight,
            grandStaffLayout);
    }

    /// <summary>
    /// Layouts a single staff or bracket group.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:3042-3045 staff-staff-spacing
    /// </remarks>
    private StaffGroupLayout LayoutSingleStaffGroup(
        StaffGroup group, double y, double staffHeight, VerticalSpacingSpec staffSpec, int startIndex)
    {
        var staffLayouts = ImmutableArray.CreateBuilder<StaffLayout>();
        double currentY = y;

        // Intra-group spacing: staff-staff basic distance is center-to-center
        double staffSpacing = staffSpec.BasicDistance - staffHeight;

        for (int i = 0; i < group.Staves.Length; i++)
        {
            var staff = group.Staves[i];
            double thisStaffHeight = GetStaffHeight(staff);
            staffLayouts.Add(new StaffLayout(
                StaffIndex: startIndex + i,
                Clef: staff.Clef,
                Y: currentY,
                Height: thisStaffHeight,
                Tuning: staff.Tuning,
                InstrumentName: staff.InstrumentName,
                IsOssia: staff.IsOssia));

            if (i < group.Staves.Length - 1)
                currentY += thisStaffHeight + Math.Max(0, staffSpacing);
        }

        double lastStaffHeight = GetStaffHeight(group.Staves[^1]);
        double totalHeight = group.StaffCount == 1
            ? lastStaffHeight
            : currentY + lastStaffHeight - y;

        return StaffGroupLayout.CreateSingle(
            staffLayouts[0],
            y,
            totalHeight);
    }

    /// <summary>
    /// Layouts measures for multi-staff scores with timing-based column information.
    /// Supports measure ranges for system breaking and proportional justification.
    /// </summary>
    public ImmutableArray<MeasureLayout> LayoutMeasures(
        MultiStaffScore score, int systemIndex,
        int startMeasureIndex = 0, int? measureCount = null,
        bool isLastSystem = false)
    {
        var primaryVoice = score.StaffGroups[0].PrimaryStaff.PrimaryVoice;
        int endMeasureIndex = measureCount.HasValue
            ? startMeasureIndex + measureCount.Value
            : primaryVoice.Measures.Length;

        double prefixWidth = SpacingRules.CalculatePrefixWidth(score.KeySignature.Sharps, systemIndex == 0,
            score.TimeSignature.Beats, score.TimeSignature.BeatType);
        double startX = _options.MarginLeft + prefixWidth;
        double availableWidth = _options.PageWidth - _options.MarginLeft - _options.MarginRight - prefixWidth;

        // First pass: calculate ideal widths
        var idealWidths = new List<double>();
        double totalIdealWidth = 0;

        for (int i = startMeasureIndex; i < endMeasureIndex; i++)
        {
            double w = SpacingRules.CalculateMeasureIdealWidth(primaryVoice.Measures[i]);
            idealWidths.Add(w);
            totalIdealWidth += w;
        }

        // Scale factor: justify to fill available width (except last system)
        double scaleFactor = (!isLastSystem && totalIdealWidth > 0 && totalIdealWidth < availableWidth)
            ? availableWidth / totalIdealWidth
            : 1.0;

        // Second pass: layout with scaled widths
        var layouts = ImmutableArray.CreateBuilder<MeasureLayout>();
        double currentX = startX;

        for (int i = 0; i < idealWidths.Count; i++)
        {
            int measureIndex = startMeasureIndex + i;
            double measureWidth = idealWidths[i] * scaleFactor;

            var allTimings = CollectAllTimingsForMeasure(score, measureIndex);
            var primaryMeasure = primaryVoice.Measures[measureIndex];

            var itemLayouts = _measureLayouter.LayoutItems(primaryMeasure, measureWidth);
            var columnLayouts = _measureLayouter.LayoutColumns(primaryMeasure, measureWidth, allTimings);

            var measureLayout = new MeasureLayout(measureIndex, currentX, measureWidth, itemLayouts, columnLayouts);
            layouts.Add(measureLayout);
            currentX += measureLayout.Width;
        }

        return layouts.ToImmutable();
    }

    /// <summary>
    /// Collects all unique timings from all voices for a specific measure.
    /// </summary>
    private List<Fraction> CollectAllTimingsForMeasure(MultiStaffScore score, int measureIndex)
    {
        var timings = new HashSet<Fraction>();

        foreach (var staffGroup in score.StaffGroups)
        {
            foreach (var staff in staffGroup.Staves)
            {
                foreach (var voice in staff.Voices)
                {
                    if (measureIndex < voice.Measures.Length)
                    {
                        var measure = voice.Measures[measureIndex];
                        var currentTiming = Fraction.Zero;

                        foreach (var item in measure.Items)
                        {
                            timings.Add(currentTiming);
                            currentTiming += item.Duration;
                        }
                    }
                }
            }
        }

        var sortedTimings = timings.ToList();
        sortedTimings.Sort();
        return sortedTimings;
    }
}
