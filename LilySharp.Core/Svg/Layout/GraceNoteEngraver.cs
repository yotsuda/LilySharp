using System.Collections.Immutable;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Layout information for a grace note group.
/// All coordinates are in staff spaces.
/// </summary>
/// <remarks>
/// LILYPOND-REF: define-grobs.scm:1358-1402 GraceSpacing grob
/// LILYPOND-REF: grace-spacing.cc positioning logic
/// </remarks>
public readonly record struct GraceNoteLayout(
    int MeasureIndex,                    // Measure containing this grace
    int MainNoteItemIndex,               // Item index of the main note
    double X,                            // X position (left edge of grace group)
    double Y,                            // Y position of first grace note
    ImmutableArray<GraceNoteInfo> Notes, // Notes in the grace group
    GraceNoteType Type,                  // Grace type (for slash rendering)
    double Scale,                        // Scale factor (0.65 for grace notes)
    int SourcePosition                   // For click-to-source mapping
);

/// <summary>
/// Calculates positions for grace notes.
/// Implements LilyPond's grace note positioning algorithm.
/// </summary>
/// <remarks>
/// LILYPOND-REF: grace-engraver.cc:92-125 Grace_engraver::process_music
/// LILYPOND-REF: grace-spacing.cc:36-80 Grace_spacing::calc_springs
/// 
/// Grace notes are placed immediately before their main note with:
/// - Smaller size (65% of normal)
/// - Tighter spacing between grace notes
/// - Acciaccatura slash through stem
/// </remarks>
public static class GraceNoteEngraver
{
    // LILYPOND-REF: define-grobs.scm:1389 font-size = -3 (approximately 0.65)
    private const double GraceScale = GraceNoteItem.ScaleFactor;
    
    // Width of a single grace note in staff spaces (scaled)
    private const double GraceNoteWidth = 1.2;
    
    // Space between grace notes
    private const double GraceNoteSpacing = 0.3;
    
    // Space between grace group and main note
    private const double GraceToMainSpacing = 0.4;
    
    /// <summary>
    /// Calculates layout for all grace notes in a score.
    /// </summary>
    public static ImmutableArray<GraceNoteLayout> Calculate(
        Score score,
        ImmutableArray<GraceNoteItem> graceNotes,
        ImmutableArray<SystemLayout> systems,
        ImmutableArray<MeasureLayout> measureLayouts)
    {
        if (graceNotes.IsDefaultOrEmpty)
            return ImmutableArray<GraceNoteLayout>.Empty;
        
        var layouts = ImmutableArray.CreateBuilder<GraceNoteLayout>(graceNotes.Length);
        
        foreach (var grace in graceNotes)
        {
            // Find the measure layout
            if (grace.MeasureIndex >= measureLayouts.Length)
                continue;
            
            var measureLayout = measureLayouts[grace.MeasureIndex];
            
            // Find the main note's item layout
            if (grace.MainNoteItemIndex >= measureLayout.Items.Length)
                continue;
            
            var mainNoteLayout = measureLayout.Items[grace.MainNoteItemIndex];
            
            // Calculate grace group width
            double graceGroupWidth = grace.Notes.Length * GraceNoteWidth * GraceScale
                                   + (grace.Notes.Length - 1) * GraceNoteSpacing * GraceScale;
            
            // Position grace notes to the left of the main note
            // LILYPOND-REF: grace-spacing.cc:65-80 positioning before main note
            double x = measureLayout.X + mainNoteLayout.X - graceGroupWidth - GraceToMainSpacing;
            
            // Y position based on first note's staff position
            double y = 0;
            if (grace.Notes.Length > 0)
            {
                // Convert staff position to Y coordinate
                // Staff position 0 = top line (B5 in treble), each step = 0.5 staff spaces
                y = grace.Notes[0].StaffPosition * 0.5;
            }
            
            layouts.Add(new GraceNoteLayout(
                grace.MeasureIndex,
                grace.MainNoteItemIndex,
                x,
                y,
                grace.Notes,
                grace.Type,
                GraceScale,
                grace.SourcePosition
            ));
        }
        
        return layouts.ToImmutable();
    }
    
    /// <summary>
    /// Gets the total width required for a grace note group.
    /// Used by spacing calculations to reserve space.
    /// </summary>
    public static double GetGraceGroupWidth(int noteCount)
    {
        return noteCount * GraceNoteWidth * GraceScale
             + (noteCount - 1) * GraceNoteSpacing * GraceScale
             + GraceToMainSpacing;
    }
}