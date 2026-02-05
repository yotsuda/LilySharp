using System.Collections.Immutable;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Layout information for a volta bracket.
/// All coordinates are in staff spaces.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/volta-bracket.cc:60-120 print method
/// </remarks>
public readonly record struct VoltaBracketLayout(
    int StartMeasureIndex,      // First measure of this volta
    int EndMeasureIndex,        // Last measure of this volta
    double StartX,              // X position of bracket start
    double EndX,                // X position of bracket end
    double Y,                   // Y position (above staff)
    string VoltaText,           // Text to display (e.g., "1.")
    bool IsClosed,              // Has right hook
    int SourcePosition          // For click-to-source mapping
);

/// <summary>
/// Calculates positions for volta brackets.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/volta-bracket.cc:1-200 Volta_bracket_interface
/// LILYPOND-REF: lily/volta-engraver.cc:1-150 Volta_engraver
///
/// LilyPond volta brackets:
/// - Start with a vertical hook (downward)
/// - Have horizontal line at consistent Y above staff
/// - Display number text at start
/// - End with vertical hook if closed, or open if continuing
/// </remarks>
public static class VoltaBracketEngraver
{
    // LILYPOND-REF: scm/define-grobs.scm:4870 edge-height = (2.0 . 2.0)
    private const double EdgeHeight = 2.0;
    
    // LILYPOND-REF: scm/define-grobs.scm:4865 Y offset above staff
    private const double YOffset = -3.0;
    
    // Padding from barline
    private const double StartPadding = 0.3;
    private const double EndPadding = 0.3;

    /// <summary>
    /// Calculates layout for all volta brackets.
    /// </summary>
    public static ImmutableArray<VoltaBracketLayout> Calculate(
        ImmutableArray<VoltaBracketItem> voltaBrackets,
        ImmutableArray<SystemLayout> systems,
        ImmutableArray<MeasureLayout> measureLayouts)
    {
        if (voltaBrackets.IsDefaultOrEmpty)
            return ImmutableArray<VoltaBracketLayout>.Empty;

        var layouts = ImmutableArray.CreateBuilder<VoltaBracketLayout>(voltaBrackets.Length);

        foreach (var bracket in voltaBrackets)
        {
            // Find measure layouts for start and end
            if (bracket.StartMeasureIndex >= measureLayouts.Length ||
                bracket.EndMeasureIndex >= measureLayouts.Length)
                continue;

            var startMeasure = measureLayouts[bracket.StartMeasureIndex];
            var endMeasure = measureLayouts[bracket.EndMeasureIndex];

            // Calculate X positions
            double startX = startMeasure.X + StartPadding;
            double endX = endMeasure.X + endMeasure.Width - EndPadding;

            // Y position above staff
            double y = YOffset;

            layouts.Add(new VoltaBracketLayout(
                bracket.StartMeasureIndex,
                bracket.EndMeasureIndex,
                startX,
                endX,
                y,
                bracket.VoltaText,
                bracket.IsClosed,
                bracket.SourcePosition
            ));
        }

        return layouts.ToImmutable();
    }

    /// <summary>
    /// Gets the edge height for volta bracket hooks.
    /// </summary>
    public static double GetEdgeHeight() => EdgeHeight;
}
