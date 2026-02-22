using System.Collections.Immutable;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Layout information for a figured bass group.
/// All coordinates are in staff spaces.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/figured-bass-engraver.cc:200-350 print method
/// LILYPOND-REF: scm/define-grobs.scm:362-380 BassFigure defaults
/// </remarks>
public readonly record struct FiguredBassLayout(
    int MeasureIndex,
    double X,                                         // X position (staff spaces)
    double Y,                                         // Y position of topmost figure (staff spaces)
    ImmutableArray<string> FigureTexts,               // Text for each figure, top to bottom
    int SourcePosition
);

/// <summary>
/// Calculates positions for figured bass figures.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/figured-bass-engraver.cc - Figured_bass_engraver
/// LILYPOND-REF: lily/figured-bass-position-engraver.cc - positioning
/// LILYPOND-REF: scm/define-grobs.scm:362-380 BassFigure defaults
///
/// Figured bass appears below the staff, with figures stacked vertically.
/// Each figure occupies approximately 1.5 staff spaces of vertical height.
/// </remarks>
public static class FiguredBassEngraver
{
    // LILYPOND-REF: scm/define-grobs.scm:362 BassFigure defaults
    private const double StaffPadding = 1.0;   // Padding below staff bottom
    private const double FigureSpacing = 1.5;  // Vertical spacing between stacked figures
    private const double BelowStaffY = 5.0;    // Y offset below staff (staff bottom = 4.0)

    /// <summary>
    /// Calculates layout for all figured bass items.
    /// </summary>
    public static ImmutableArray<FiguredBassLayout> Calculate(
        ImmutableArray<FiguredBassItem> figuredBasses,
        ImmutableArray<SystemLayout> systems,
        ImmutableArray<MeasureLayout> measureLayouts)
    {
        if (figuredBasses.IsDefaultOrEmpty)
            return ImmutableArray<FiguredBassLayout>.Empty;

        var layouts = ImmutableArray.CreateBuilder<FiguredBassLayout>(figuredBasses.Length);

        foreach (var fb in figuredBasses)
        {
            if (fb.MeasureIndex >= measureLayouts.Length)
                continue;

            var measureLayout = measureLayouts[fb.MeasureIndex];

            if (fb.ItemIndex >= measureLayout.Items.Length)
                continue;

            var itemLayout = measureLayout.Items[fb.ItemIndex];
            double x = measureLayout.X + itemLayout.X;
            double y = BelowStaffY + StaffPadding;

            var figureTexts = fb.Figures
                .Select(f => f.DisplayText)
                .ToImmutableArray();

            layouts.Add(new FiguredBassLayout(
                fb.MeasureIndex,
                x,
                y,
                figureTexts,
                fb.SourcePosition));
        }

        return layouts.ToImmutable();
    }
}
