using System.Collections.Immutable;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Collector;

/// <summary>
/// Detects slurs between notes in a score.
/// A slur connects notes for phrasing (legato).
/// </summary>
public sealed class SlurDetector
{
    /// <summary>
    /// Detects all slurs in a score.
    /// </summary>
    public ImmutableArray<SlurItem> DetectSlurs(Score score)
    {
        var slurs = new List<SlurItem>();
        
        // For now, this is a placeholder implementation
        // Slurs will be detected when slur syntax is added to the parser
        // Slur syntax in LilyPond: c'( d' e' f')
        
        // In the future, this will:
        // 1. Detect '(' marking slur start
        // 2. Track notes until ')' marking slur end
        // 3. Determine curve direction based on stem direction
        
        return slurs.ToImmutableArray();
    }
    
    /// <summary>
    /// Determines default slur direction based on note positions.
    /// </summary>
    private static bool DetermineSlurDirection(NoteItem startNote, NoteItem endNote)
    {
        // Default: curve opposite to average stem direction
        // If both stems up, slur curves down
        // If both stems down, slur curves up
        // If mixed, use the majority or default to up
        
        int avgPosition = (startNote.StaffPosition + endNote.StaffPosition) / 2;
        bool avgStemUp = avgPosition < 4;
        
        // Curve opposite to stem direction
        return !avgStemUp;
    }
}