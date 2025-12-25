using System.Collections.Immutable;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Layout information for a dynamic marking.
/// All coordinates are in staff spaces.
/// </summary>
/// <remarks>
/// LILYPOND-REF: define-grobs.scm:1298-1327 DynamicText grob
/// LILYPOND-REF: define-grobs.scm:1270-1297 DynamicLineSpanner grob
/// </remarks>
public readonly record struct DynamicLayout(
    int MeasureIndex,       // Measure containing this dynamic
    int ItemIndex,          // Item index within measure (for X alignment)
    double X,               // Absolute X position (staff spaces from score start)
    double Y,               // Y position (staff spaces from staff top, positive = down)
    string Text,            // Dynamic text ("p", "ff", etc.)
    int SourcePosition      // For click-to-source mapping
);

/// <summary>
/// Calculates positions for dynamic markings.
/// Implements LilyPond's dynamic positioning algorithm.
/// </summary>
/// <remarks>
/// LILYPOND-REF: dynamic-align-engraver.cc:36-61 Dynamic_align_engraver class
/// LILYPOND-REF: side-position-interface.cc:92-111 axis_aligned_side_helper
/// 
/// LilyPond places dynamics below the staff (direction = DOWN) with:
/// - outside-staff-priority: 250
/// - padding: 0.6 staff spaces
/// - staff-padding: 0.1 staff spaces
/// - Y-offset calculated by side-position-interface::y-aligned-side
/// </remarks>
public static class DynamicEngraver
{
    // LILYPOND-REF: define-grobs.scm:1274 direction = DOWN
    private const int Direction = 1;  // DOWN = 1 (positive Y = down in our coordinate system)
    
    // LILYPOND-REF: define-grobs.scm:1277 padding = 0.6
    private const double Padding = 0.6;
    
    // LILYPOND-REF: define-grobs.scm:1280 staff-padding = 0.1
    private const double StaffPadding = 0.1;
    
    // Height of dynamic text in staff spaces (approximate)
    // LILYPOND-REF: define-grobs.scm:1317 Y-offset = (scale-by-font-size -0.6)
    private const double DynamicTextHeight = 1.5;
    
    // Base Y offset from staff bottom (5 lines = 4 staff spaces, so bottom is at 4.0)
    private const double StaffBottom = 4.0;
    
    /// <summary>
    /// Calculates layout for all dynamics in a score.
    /// </summary>
    public static ImmutableArray<DynamicLayout> Calculate(
        Score score,
        ImmutableArray<DynamicItem> dynamics,
        ImmutableArray<SystemLayout> systems,
        ImmutableArray<MeasureLayout> measureLayouts)
    {
        if (dynamics.IsDefaultOrEmpty)
            return ImmutableArray<DynamicLayout>.Empty;
        
        var layouts = ImmutableArray.CreateBuilder<DynamicLayout>(dynamics.Length);
        
        // Calculate Y position for dynamics
        // LILYPOND-REF: side-position-interface.cc:92-111 axis_aligned_side_helper
        // Dynamics are placed below the staff with padding
        double baseY = StaffBottom + StaffPadding + Padding;
        
        // Track maximum extent for collision avoidance
        double currentMaxY = baseY;
        int lastMeasureIndex = -1;
        
        foreach (var dynamic in dynamics)
        {
            // Find the measure layout
            if (dynamic.MeasureIndex >= measureLayouts.Length)
                continue;
            
            var measureLayout = measureLayouts[dynamic.MeasureIndex];
            
            // Find the item layout within the measure
            if (dynamic.ItemIndex >= measureLayout.Items.Length)
                continue;
            
            var itemLayout = measureLayout.Items[dynamic.ItemIndex];
            
            // Calculate X position (centered on the note)
            // LILYPOND-REF: define-grobs.scm:1311 self-alignment-X = CENTER
            double x = measureLayout.X + itemLayout.X;
            
            // Calculate Y position
            // LILYPOND-REF: side-position-interface.cc:128-136 y_aligned_side
            // For now, use a simple placement below the staff
            // TODO: Implement skyline-based collision avoidance
            double y = baseY;
            
            // Reset Y tracking for new measure (simple approach)
            if (dynamic.MeasureIndex != lastMeasureIndex)
            {
                currentMaxY = baseY;
                lastMeasureIndex = dynamic.MeasureIndex;
            }
            
            layouts.Add(new DynamicLayout(
                dynamic.MeasureIndex,
                dynamic.ItemIndex,
                x,
                y,
                dynamic.Text,
                dynamic.SourcePosition
            ));
        }
        
        return layouts.ToImmutable();
    }
}
