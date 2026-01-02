using System.Collections.Immutable;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Collector;

/// <summary>
/// Detects ties between notes of the same pitch.
/// </summary>
public sealed class TieDetector
{
    public ImmutableArray<TieItem> DetectTies(Score score)
    {
        var ties = new List<TieItem>();
        var measures = score.Voice.Measures;
        
        for (int measureIdx = 0; measureIdx < measures.Length; measureIdx++)
        {
            var measure = measures[measureIdx];
            
            for (int itemIdx = 0; itemIdx < measure.Items.Length; itemIdx++)
            {
                if (measure.Items[itemIdx] is not NoteItem startNote)
                    continue;
                
                if (!startNote.HasTieStart)
                    continue;
                
                var endNote = FindNextSamePitchNote(score, measureIdx, itemIdx, startNote);
                if (endNote != null)
                {
                    var (endMeasureIdx, endItemIdx, note) = endNote.Value;
                    
                    // Tie curves opposite to stem direction
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
    
    private (int measureIdx, int itemIdx, NoteItem note)? FindNextSamePitchNote(
        Score score,
        int startMeasureIdx,
        int startItemIdx,
        NoteItem startNote)
    {
        var measures = score.Voice.Measures;
        
        // Search in current measure first
        var currentMeasure = measures[startMeasureIdx];
        for (int i = startItemIdx + 1; i < currentMeasure.Items.Length; i++)
        {
            if (currentMeasure.Items[i] is NoteItem candidate && 
                candidate.StaffPosition == startNote.StaffPosition)
            {
                return (startMeasureIdx, i, candidate);
            }
        }
        
        // Search in subsequent measures
        for (int m = startMeasureIdx + 1; m < measures.Length; m++)
        {
            var measure = measures[m];
            for (int i = 0; i < measure.Items.Length; i++)
            {
                if (measure.Items[i] is NoteItem candidate &&
                    candidate.StaffPosition == startNote.StaffPosition)
                {
                    return (m, i, candidate);
                }
            }
        }
        
        return null;
    }
}