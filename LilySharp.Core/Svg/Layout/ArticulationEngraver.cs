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
    /// <remarks>
    /// LILYPOND-REF: side-position-interface.cc:193-400 aligned_side()
    /// Articulations are positioned relative to the note's staff position:
    /// - For notes above middle line: articulations go below (unless overridden)
    /// - For notes below middle line: articulations go above (unless overridden)
    /// - Fermata and ornaments always go above
    /// </remarks>
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
            
            // Get the music item to determine staff position
            // LILYPOND-REF: script-engraver.cc:92-125 acknowledge_note_head
            var measure = score.Voice.Measures[articulation.MeasureIndex];
            var item = measure.Items[articulation.ItemIndex];
            
            // Get staff position of the note
            int staffPosition = GetStaffPosition(item);
            bool stemUp = GetStemUp(item, staffPosition);
            
            // Calculate X position (centered on the note)
            // LILYPOND-REF: define-grobs.scm:2289 self-alignment-X = CENTER
            double x = measureLayout.X + itemLayout.X;
            
            // Calculate Y position based on note position and direction
            // LILYPOND-REF: side-position-interface.cc:229-264 skyline calculation
            double y = CalculateYPosition(articulation, staffPosition, stemUp);
            
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
    
    /// <summary>
    /// Gets the staff position of a music item.
    /// </summary>
    private static int GetStaffPosition(MusicItem item) => item switch
    {
        NoteItem note => note.StaffPosition,
        ChordItem chord => chord.Notes.Length > 0 
            ? (chord.Notes.Max(n => n.StaffPosition) + chord.Notes.Min(n => n.StaffPosition)) / 2
            : 4,
        _ => 4 // Default to middle line
    };
    
    /// <summary>
    /// Determines stem direction from the item.
    /// </summary>
    private static bool GetStemUp(MusicItem item, int staffPosition) => item switch
    {
        NoteItem note => note.StemUp,
        ChordItem chord => chord.StemUp,
        _ => staffPosition >= 4 // Default: stem down for notes on/above middle line
    };
    
    /// <summary>
    /// Calculates Y position for an articulation.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: side-position-interface.cc:323-337 staff padding
    /// LILYPOND-REF: define-grobs.scm:2280 padding = 0.2
    /// 
    /// Articulations are placed:
    /// - On the opposite side of the stem (except fermata/ornaments)
    /// - With padding from the note/staff
    /// - Ensuring they stay outside the staff if necessary
    /// </remarks>
    private static double CalculateYPosition(ArticulationItem articulation, int staffPosition, bool stemUp)
    {
        // LILYPOND-REF: define-grobs.scm:1365 fermata: direction = UP
        // LILYPOND-REF: define-grobs.scm:2175 TrillSpanner: direction = UP
        bool forceAbove = articulation.Type == ArticulationType.Fermata || articulation.IsOrnament;
        bool isAbove = forceAbove || articulation.IsAbove;
        
        // Convert staff position to Y coordinate
        // Staff position 0 = top line (staff spaces * 0.5)
        // LILYPOND-REF: staff-symbol-referencer.cc:76-89 staff_symbol_referencer::get_position
        double noteY = staffPosition * 0.5;
        
        if (isAbove)
        {
            // Place above the note
            // LILYPOND-REF: side-position-interface.cc:330-337 include_staff
            double targetY = noteY - ArticulationHeight - Padding;
            
            // Ensure articulation is above staff (with staff-padding)
            // LILYPOND-REF: define-grobs.scm:2295 staff-padding = 0.25
            double minY = StaffTop - StaffPadding - ArticulationHeight;
            return Math.Min(targetY, minY);
        }
        else
        {
            // Place below the note
            double targetY = noteY + Padding + ArticulationHeight;
            
            // Ensure articulation is below staff (with staff-padding)
            double maxY = StaffBottom + StaffPadding;
            return Math.Max(targetY, maxY);
        }
    }
}
