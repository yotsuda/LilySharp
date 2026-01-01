using System.Collections.Immutable;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Model;

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
    public double CalculateSystemHeight(MultiStaffScore score)
    {
        double height = 0;
        double staffHeight = _options.StaffHeight;
        double grandStaffSpacing = _options.GrandStaffSpacing;
        double staffGroupSpacing = _options.StaffGroupSpacing;

        for (int i = 0; i < score.StaffGroups.Length; i++)
        {
            var group = score.StaffGroups[i];

            if (group.IsGrandStaff)
            {
                height += staffHeight * 2 + grandStaffSpacing;
            }
            else
            {
                height += staffHeight * group.StaffCount;
                if (group.StaffCount > 1)
                    height += grandStaffSpacing * (group.StaffCount - 1);
            }

            if (i < score.StaffGroups.Length - 1)
                height += staffGroupSpacing;
        }

        return height;
    }

    /// <summary>
    /// Layouts all staff groups within a system.
    /// </summary>
    public ImmutableArray<StaffGroupLayout> LayoutStaffGroups(MultiStaffScore score, double systemY)
    {
        var builder = ImmutableArray.CreateBuilder<StaffGroupLayout>();
        double currentY = 0;
        double staffHeight = _options.StaffHeight;
        double grandStaffSpacing = _options.GrandStaffSpacing;
        double staffGroupSpacing = _options.StaffGroupSpacing;
        int globalStaffIndex = 0;

        foreach (var group in score.StaffGroups)
        {
            if (group.IsGrandStaff)
            {
                var layout = LayoutGrandStaffGroup(group, currentY, staffHeight, grandStaffSpacing, globalStaffIndex);
                builder.Add(layout);
                currentY += layout.Height + staffGroupSpacing;
                globalStaffIndex += group.StaffCount;
            }
            else
            {
                var layout = LayoutSingleStaffGroup(group, currentY, staffHeight, grandStaffSpacing, globalStaffIndex);
                builder.Add(layout);
                currentY += layout.Height + staffGroupSpacing;
                globalStaffIndex += group.StaffCount;
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Layouts a grand staff group (piano/organ style with brace).
    /// </summary>
    private StaffGroupLayout LayoutGrandStaffGroup(
        StaffGroup group, double y, double staffHeight, double staffSpacing, int startIndex)
    {
        var staffLayouts = ImmutableArray.CreateBuilder<StaffLayout>();
        double currentY = y;

        for (int i = 0; i < group.Staves.Length; i++)
        {
            var staff = group.Staves[i];
            staffLayouts.Add(new StaffLayout(
                StaffIndex: startIndex + i,
                Clef: staff.Clef,
                Y: currentY,
                Height: staffHeight));

            if (i < group.Staves.Length - 1)
                currentY += staffHeight + staffSpacing;
        }

        double totalHeight = currentY + staffHeight - y;
        double braceX = _options.MarginLeft - 2;

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
    private StaffGroupLayout LayoutSingleStaffGroup(
        StaffGroup group, double y, double staffHeight, double staffSpacing, int startIndex)
    {
        var staffLayouts = ImmutableArray.CreateBuilder<StaffLayout>();
        double currentY = y;

        for (int i = 0; i < group.Staves.Length; i++)
        {
            var staff = group.Staves[i];
            staffLayouts.Add(new StaffLayout(
                StaffIndex: startIndex + i,
                Clef: staff.Clef,
                Y: currentY,
                Height: staffHeight));

            if (i < group.Staves.Length - 1)
                currentY += staffHeight + staffSpacing;
        }

        double totalHeight = group.StaffCount == 1
            ? staffHeight
            : currentY + staffHeight - y;

        return StaffGroupLayout.CreateSingle(
            staffLayouts[0],
            y,
            totalHeight);
    }

    /// <summary>
    /// Layouts measures for multi-staff scores with timing-based column information.
    /// </summary>
    public ImmutableArray<MeasureLayout> LayoutMeasures(MultiStaffScore score, int systemIndex)
    {
        var primaryVoice = score.StaffGroups[0].PrimaryStaff.PrimaryVoice;
        
        var layouts = ImmutableArray.CreateBuilder<MeasureLayout>();
        double currentX = _options.MarginLeft + SpacingRules.CalculatePrefixWidth(score.KeySignature.Sharps, systemIndex == 0);

        for (int measureIndex = 0; measureIndex < primaryVoice.Measures.Length; measureIndex++)
        {
            var allTimings = CollectAllTimingsForMeasure(score, measureIndex);
            var primaryMeasure = primaryVoice.Measures[measureIndex];
            double measureWidth = SpacingRules.CalculateMeasureIdealWidth(primaryMeasure);
            
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
