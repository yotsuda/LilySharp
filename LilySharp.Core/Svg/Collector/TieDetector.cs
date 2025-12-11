using System.Collections.Immutable;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Collector;

/// <summary>
/// Detects ties between notes in a score.
/// A tie connects two consecutive notes of the same pitch.
/// </summary>
public sealed class TieDetector
{
    /// <summary>
    /// Detects all ties in a score.
    /// </summary>
    public ImmutableArray<TieItem> DetectTies(Score score)
    {
        var ties = new List<TieItem>();
        var measures = score.Voice.Measures;
        
        // Iterate through all measures to find consecutive notes of same pitch
        for (int measureIdx = 0; measureIdx < measures.Length; measureIdx++)
        {
            var measure = measures[measureIdx];
            
            for (int itemIdx = 0; itemIdx < measure.Items.Length; itemIdx++)
            {
                if (measure.Items[itemIdx] is not NoteItem startNote)
                    continue;
                
                // Look for a tie to the next note
                var endNote = FindTiedNote(score, measureIdx, itemIdx, startNote);
                if (endNote != null)
                {
                    var (endMeasureIdx, endItemIdx, note) = endNote.Value;
                    
                    // Determine curve direction
                    // Default: curve opposite to stem direction
                    bool curveUp = !startNote.StemUp;
                    
                    ties.Add(new TieItem(
                        startNote,
                        note,
                        startNote.StaffPosition,
                        curveUp,
                        measureIdx,
                        endMeasureIdx,
                        itemIdx,
                        endItemIdx));
                }
            }
        }
        
        return ties.ToImmutableArray();
    }
    
    /// <summary>
    /// Finds a note tied to the given note.
    /// Returns null if no tie exists.
    /// </summary>
    private (int measureIdx, int itemIdx, NoteItem note)? FindTiedNote(
        Score score,
        int startMeasureIdx,
        int startItemIdx,
        NoteItem startNote)
    {
        // For now, we don't have explicit tie syntax in the parser
        // This is a placeholder that will be enhanced when tie syntax is added
        // Currently returns null (no automatic tie detection)
        
        // In the future, this will check if the note has a tie marker (~)
        // and find the next note with the same pitch
        
        return null;
    }
    
    /// <summary>
    /// Checks if two notes can be tied (same pitch).
    /// </summary>
    private static bool CanTie(NoteItem a, NoteItem b)
    {
        return a.StaffPosition == b.StaffPosition;
    }
}