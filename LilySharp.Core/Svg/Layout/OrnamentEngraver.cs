using System.Collections.Immutable;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Layout information for an ornament mark.
/// All coordinates are in staff spaces.
/// </summary>
/// <remarks>
/// LILYPOND-REF: define-grobs.scm:2175-2230 TrillSpanner grob
/// LILYPOND-REF: script-interface.cc positioning logic
/// </remarks>
public readonly record struct OrnamentLayout(
    int MeasureIndex,       // Measure containing this ornament
    int ItemIndex,          // Item index within measure
    double X,               // X position (center of ornament)
    double Y,               // Y position (above the note)
    string Glyph,           // SMuFL glyph to render
    OrnamentType Type,      // Ornament type
    int SourcePosition      // For click-to-source mapping
);

/// <summary>
/// Calculates positions for ornament marks.
/// Implements LilyPond's ornament positioning algorithm.
/// </summary>
/// <remarks>
/// LILYPOND-REF: trill-spanner-engraver.cc:92-125 positioning
/// LILYPOND-REF: side-position-interface.cc:92-111 axis_aligned_side_helper
/// 
/// Ornaments are placed above the note with:
/// - outside-staff-priority: 50
/// - direction: UP
/// - padding: 0.5 staff spaces
/// </remarks>
public static class OrnamentEngraver
{
    // LILYPOND-REF: define-grobs.scm:2195 padding = 0.5
    private const double Padding = 0.5;
    
    // Height of ornament glyphs in staff spaces
    private const double OrnamentHeight = 1.2;
    
    // Staff top position
    private const double StaffTop = 0.0;
    
    /// <summary>
    /// Calculates layout for all ornaments in a score.
    /// </summary>
    public static ImmutableArray<OrnamentLayout> Calculate(
        Score score,
        ImmutableArray<OrnamentItem> ornaments,
        ImmutableArray<SystemLayout> systems,
        ImmutableArray<MeasureLayout> measureLayouts)
    {
        if (ornaments.IsDefaultOrEmpty)
            return ImmutableArray<OrnamentLayout>.Empty;
        
        var layouts = ImmutableArray.CreateBuilder<OrnamentLayout>(ornaments.Length);
        
        foreach (var ornament in ornaments)
        {
            // Find the measure layout
            if (ornament.MeasureIndex >= measureLayouts.Length)
                continue;
            
            var measureLayout = measureLayouts[ornament.MeasureIndex];
            
            // Find the item layout within the measure
            if (ornament.ItemIndex >= measureLayout.Items.Length)
                continue;
            
            var itemLayout = measureLayout.Items[ornament.ItemIndex];
            
            // Calculate X position (centered on the note)
            double x = measureLayout.X + itemLayout.X;
            
            // Calculate Y position (above the staff)
            // LILYPOND-REF: side-position-interface.cc:128-136 y_aligned_side
            // Ornaments are placed above the staff with padding
            double y = StaffTop - Padding - OrnamentHeight;
            
            layouts.Add(new OrnamentLayout(
                ornament.MeasureIndex,
                ornament.ItemIndex,
                x,
                y,
                ornament.GetGlyph(),
                ornament.Type,
                ornament.SourcePosition
            ));
        }
        
        return layouts.ToImmutable();
    }
}