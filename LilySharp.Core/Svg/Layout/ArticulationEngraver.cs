using System.Collections.Immutable;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Layout information for an articulation mark.
/// All coordinates are in staff spaces.
/// </summary>
/// <remarks>
/// LILYPOND-REF: define-grobs.scm:2268-2310 Script grob
/// LILYPOND-REF: script-interface.cc positioning logic
/// </remarks>
public readonly record struct ArticulationLayout(
    int MeasureIndex,       // Measure containing this articulation
    int ItemIndex,          // Item index within measure (for X alignment)
    double X,               // Absolute X position (staff spaces from score start)
    double Y,               // Y position (staff spaces from staff top, positive = down)
    string Glyph,           // SMuFL glyph to render
    bool IsAbove,           // Whether placed above the note
    int SourcePosition      // For click-to-source mapping
);

/// <summary>
/// Calculates positions for articulation marks.
/// Implements LilyPond's articulation positioning algorithm.
/// </summary>
/// <remarks>
/// LILYPOND-REF: script-engraver.cc:92-125 Script_engraver::acknowledge_note_head
/// LILYPOND-REF: side-position-interface.cc:92-111 axis_aligned_side_helper
/// 
/// LilyPond places articulations with:
/// - avoid-slur: around
/// - direction: automatically chosen based on stem direction
/// - padding: 0.2 staff spaces
/// - staff-padding: 0.25 staff spaces
/// </remarks>
public static class ArticulationEngraver
{
    // LILYPOND-REF: define-grobs.scm:2280 padding = 0.2
    private const double Padding = 0.2;
    
    // LILYPOND-REF: define-grobs.scm:2295 staff-padding = 0.25
    private const double StaffPadding = 0.25;
    
    // Approximate height of articulation glyphs in staff spaces
    private const double ArticulationHeight = 0.8;
    
    // Staff middle line position
    private const double StaffMiddle = 2.0;
    
    // Staff top and bottom
    private const double StaffTop = 0.0;
    private const double StaffBottom = 4.0;
    
    /// <summary>
    /// Calculates layout for all articulations in a score.
    /// </summary>
    public static ImmutableArray<ArticulationLayout> Calculate(
        Score score,
        ImmutableArray<ArticulationItem> articulations,
        ImmutableArray<SystemLayout> systems,
        ImmutableArray<MeasureLayout> measureLayouts)
    {
        if (articulations.IsDefaultOrEmpty)
            return ImmutableArray<ArticulationLayout>.Empty;
        
        var layouts = ImmutableArray.CreateBuilder<ArticulationLayout>(articulations.Length);
        
        foreach (var articulation in articulations)
        {
            // Find the measure layout
            if (articulation.MeasureIndex >= measureLayouts.Length)
                continue;
            
            var measureLayout = measureLayouts[articulation.MeasureIndex];
            
            // Find the item layout within the measure
            if (articulation.ItemIndex >= measureLayout.Items.Length)
                continue;
            
            var itemLayout = measureLayout.Items[articulation.ItemIndex];
            
            // Calculate X position (centered on the note)
            // LILYPOND-REF: define-grobs.scm:2289 self-alignment-X = CENTER
            double x = measureLayout.X + itemLayout.X;
            
            // Calculate Y position based on direction
            // LILYPOND-REF: side-position-interface.cc:128-136 y_aligned_side
            double y;
            if (articulation.IsAbove)
            {
                // Place above the staff
                y = StaffTop - StaffPadding - Padding - ArticulationHeight;
            }
            else
            {
                // Place below the staff
                y = StaffBottom + StaffPadding + Padding;
            }
            
            // Special handling for fermata (always above for now)
            if (articulation.Type == ArticulationType.Fermata)
            {
                y = StaffTop - StaffPadding - Padding - ArticulationHeight;
            }
            
            layouts.Add(new ArticulationLayout(
                articulation.MeasureIndex,
                articulation.ItemIndex,
                x,
                y,
                articulation.GetGlyph(),
                articulation.IsAbove,
                articulation.SourcePosition
            ));
        }
        
        return layouts.ToImmutable();
    }
}