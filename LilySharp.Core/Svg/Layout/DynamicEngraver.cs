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
    /// <remarks>
    /// LILYPOND-REF: side-position-interface.cc:193-400 aligned_side()
    /// LILYPOND-REF: dynamic-align-engraver.cc:120-180 process_acknowledged()
    /// 
    /// Dynamics are placed below the staff, avoiding collision with notes
    /// that extend below the staff (low notes, stems down).
    /// </remarks>
    public static ImmutableArray<DynamicLayout> Calculate(
        Score score,
        ImmutableArray<DynamicItem> dynamics,
        ImmutableArray<SystemLayout> systems,
        ImmutableArray<MeasureLayout> measureLayouts)
    {
        if (dynamics.IsDefaultOrEmpty)
            return ImmutableArray<DynamicLayout>.Empty;
        
        var layouts = ImmutableArray.CreateBuilder<DynamicLayout>(dynamics.Length);
        
        // LILYPOND-REF: side-position-interface.cc:323-337 staff padding
        // Base Y position: below staff with padding
        double baseY = StaffBottom + StaffPadding + Padding;
        
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
            
            // Get the music item to determine if we need to avoid collision
            // LILYPOND-REF: dynamic-align-engraver.cc:92-110 acknowledge_note_head
            var measure = score.Voice.Measures[dynamic.MeasureIndex];
            var item = measure.Items[dynamic.ItemIndex];
            
            // Calculate X position (centered on the note)
            // LILYPOND-REF: define-grobs.scm:1311 self-alignment-X = CENTER
            double x = measureLayout.X + itemLayout.X;
            
            // Calculate Y position with collision avoidance
            // LILYPOND-REF: side-position-interface.cc:266-320 skyline-based positioning
            double y = CalculateYPosition(item, baseY);
            
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
    
    /// <summary>
    /// Calculates Y position for a dynamic, avoiding collision with the note.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: side-position-interface.cc:229-264 skyline calculation
    /// 
    /// Simple collision avoidance: if the note extends below the staff
    /// (low notes or stem-down notes), push the dynamic further down.
    /// </remarks>
    private static double CalculateYPosition(MusicItem item, double baseY)
    {
        // Get the lowest extent of the note/chord
        double lowestY = GetLowestExtent(item);
        
        // If the note extends below the staff, push the dynamic down
        // LILYPOND-REF: side-position-interface.cc:330-337 include_staff
        double requiredY = lowestY + Padding + DynamicTextHeight * 0.5;
        
        return Math.Max(baseY, requiredY);
    }
    
    /// <summary>
    /// Gets the lowest Y extent of a music item (in staff spaces from top).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: stem.cc:876-920 calc_stem_end_position
    /// Accounts for note position and stem direction.
    /// </remarks>
    private static double GetLowestExtent(MusicItem item)
    {
        switch (item)
        {
            case NoteItem note:
                // Note Y position
                double noteY = note.StaffPosition * 0.5;
                
                // If stem down, add stem length
                if (!note.StemUp)
                {
                    // LILYPOND-REF: stem.cc:93 stem-length = 3.5
                    double stemLength = 3.5;
                    return noteY + stemLength;
                }
                
                // Half a notehead height below center
                return noteY + 0.5;
                
            case ChordItem chord:
                // Find lowest note in chord
                int lowestPos = chord.Notes.Max(n => n.StaffPosition); // Higher position = lower on staff
                double lowestNoteY = lowestPos * 0.5;
                
                // If stem down, add stem length from lowest note
                if (!chord.StemUp)
                {
                    double stemLength = 3.5;
                    return lowestNoteY + stemLength;
                }
                
                return lowestNoteY + 0.5;
                
            case RestItem:
                // Rest is typically around middle of staff
                return StaffBottom * 0.5 + 1.0;
                
            default:
                return StaffBottom;
        }
    }
}
