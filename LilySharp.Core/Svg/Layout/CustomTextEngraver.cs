using System.Collections.Immutable;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Layout information for a custom text annotation.
/// All coordinates are in staff spaces.
/// </summary>
/// <remarks>
/// LILYPOND-REF: text-interface.cc Text rendering
/// LILYPOND-REF: define-grobs.scm:3900-3950 TextScript grob
/// </remarks>
public readonly record struct CustomTextLayout(
    int MeasureIndex,       // Measure containing this text
    double X,               // Absolute X position (staff spaces from score start)
    double Y,               // Y position (staff spaces from staff top, positive = down)
    string Text,            // Display text
    int SourcePosition      // For click-to-source mapping
);

/// <summary>
/// Calculates positions for custom text annotations.
/// </summary>
/// <remarks>
/// LILYPOND-REF: text-interface.cc:36-89 Text positioning
/// LILYPOND-REF: side-position-interface.cc:92-111 axis_aligned_side_helper
///
/// Custom text is placed at the end of measures, typically below the staff
/// for expression indications like "molto rit.", "a tempo", etc.
/// </remarks>
public static class CustomTextEngraver
{
    // LILYPOND-REF: define-grobs.scm:3920 direction = DOWN
    private const int Direction = 1;  // DOWN = 1 (positive Y = down)

    // LILYPOND-REF: define-grobs.scm:3925 padding = 0.5
    private const double Padding = 0.5;

    // Y offset below staff for custom text
    private const double BelowStaffOffset = 5.5;

    /// <summary>
    /// Calculates layout for all custom text items in a score.
    /// </summary>
    public static ImmutableArray<CustomTextLayout> Calculate(
        Score score,
        ImmutableArray<CustomTextItem> customTexts,
        ImmutableArray<SystemLayout> systems,
        ImmutableArray<MeasureLayout> measureLayouts)
    {
        if (customTexts.IsDefaultOrEmpty)
            return ImmutableArray<CustomTextLayout>.Empty;

        var layouts = ImmutableArray.CreateBuilder<CustomTextLayout>(customTexts.Length);

        foreach (var customText in customTexts)
        {
            // Find the measure layout
            if (customText.MeasureIndex >= measureLayouts.Length)
                continue;

            var measureLayout = measureLayouts[customText.MeasureIndex];

            // Calculate X position (at end of measure)
            // LILYPOND-REF: text-interface.cc:75-80 self-alignment-X
            double x = measureLayout.X + measureLayout.Width - 1.0;

            // Calculate Y position (below staff)
            double y = BelowStaffOffset + Padding;

            layouts.Add(new CustomTextLayout(
                customText.MeasureIndex,
                x,
                y,
                customText.Text,
                customText.SourcePosition
            ));
        }

        return layouts.ToImmutable();
    }
}