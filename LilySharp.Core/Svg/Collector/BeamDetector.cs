using System.Collections.Immutable;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Collector;

/// <summary>
/// Detects beam groups from measures.
/// Based on Lilypond's beaming-pattern.cc.
/// </summary>
public sealed class BeamDetector
{
    /// <summary>
    /// Detects all beam groups in a score.
    /// </summary>
    public ImmutableArray<BeamGroup> DetectBeamGroups(Score score)
    {
        var beamGroups = new List<BeamGroup>();
        
        for (int measureIndex = 0; measureIndex < score.Voice.Measures.Length; measureIndex++)
        {
            var measure = score.Voice.Measures[measureIndex];
            DetectBeamGroupsInMeasure(measure, measureIndex, beamGroups);
        }
        
        return beamGroups.ToImmutableArray();
    }
    
    private void DetectBeamGroupsInMeasure(Measure measure, int measureIndex, List<BeamGroup> beamGroups)
    {
        var currentGroup = new List<(MusicItem item, int index)>();
        
        for (int i = 0; i < measure.Items.Length; i++)
        {
            var item = measure.Items[i];
            
            if (IsBeamable(item))
            {
                currentGroup.Add((item, i));
            }
            else
            {
                // Non-beamable item (rest, whole/half/quarter note) breaks the beam
                FlushBeamGroup(currentGroup, measureIndex, beamGroups);
                currentGroup.Clear();
            }
        }
        
        // Flush any remaining group
        FlushBeamGroup(currentGroup, measureIndex, beamGroups);
    }
    
    private void FlushBeamGroup(List<(MusicItem item, int index)> group, int measureIndex, List<BeamGroup> beamGroups)
    {
        if (group.Count < 2)
            return; // Need at least 2 notes to form a beam
        
        var members = new List<BeamMember>();
        int totalPosition = 0;
        int noteCount = 0;
        
        for (int i = 0; i < group.Count; i++)
        {
            var (item, itemIndex) = group[i];
            int beamCount = GetBeamCount(item);
            int staffPosition = GetStaffPosition(item);
            
            totalPosition += staffPosition;
            noteCount++;
            
            // Calculate beam counts for left and right sides
            int beamCountLeft, beamCountRight;
            
            if (i == 0)
            {
                // First note: no beams on left
                beamCountLeft = 0;
                beamCountRight = beamCount;
            }
            else if (i == group.Count - 1)
            {
                // Last note: no beams on right
                int prevBeamCount = GetBeamCount(group[i - 1].item);
                beamCountLeft = Math.Min(beamCount, prevBeamCount);
                beamCountRight = 0;
            }
            else
            {
                // Middle notes
                int prevBeamCount = GetBeamCount(group[i - 1].item);
                int nextBeamCount = GetBeamCount(group[i + 1].item);
                
                // Continuous beams
                int continuousBeams = Math.Min(Math.Min(beamCount, prevBeamCount), nextBeamCount);
                
                // Beamlets (partial beams)
                int leftBeamlets = Math.Max(0, Math.Min(beamCount, prevBeamCount) - continuousBeams);
                int rightBeamlets = Math.Max(0, Math.Min(beamCount, nextBeamCount) - continuousBeams);
                
                beamCountLeft = continuousBeams + leftBeamlets;
                beamCountRight = continuousBeams + rightBeamlets;
            }
            
            members.Add(new BeamMember(
                item,
                beamCount,
                beamCountLeft,
                beamCountRight,
                staffPosition,
                itemIndex));
        }
        
        // Determine stem direction for the entire group
        // Rule: average position < 4 (middle line) → stem up
        bool stemUp = noteCount > 0 && (double)totalPosition / noteCount < 4;
        
        beamGroups.Add(new BeamGroup(
            members.ToImmutableArray(),
            measureIndex,
            group[0].index,
            stemUp));
    }
    
    /// <summary>
    /// Determines if an item can be beamed (8th note or shorter).
    /// </summary>
    private bool IsBeamable(MusicItem item)
    {
        var baseDuration = item switch
        {
            NoteItem note => note.BaseDuration,
            ChordItem chord => chord.BaseDuration,
            _ => Fraction.Whole
        };
        
        // Beamable if duration <= 1/8 (8th note or shorter)
        // Denominator >= 8 means 8th, 16th, 32nd, etc.
        return baseDuration.Denominator >= 8;
    }
    
    /// <summary>
    /// Gets the number of beams for a note based on its duration.
    /// </summary>
    private int GetBeamCount(MusicItem item)
    {
        var baseDuration = item switch
        {
            NoteItem note => note.BaseDuration,
            ChordItem chord => chord.BaseDuration,
            _ => Fraction.Quarter
        };
        
        // 8th=1, 16th=2, 32nd=3, 64th=4, 128th=5
        // Formula: log2(denominator) - 2 (since 8=2^3, so 3-2=1)
        int log2 = 0;
        long denom = baseDuration.Denominator;
        while (denom > 1)
        {
            denom >>= 1;
            log2++;
        }
        
        return Math.Max(0, log2 - 2);
    }
    
    /// <summary>
    /// Gets the staff position of a note or chord.
    /// For chords, returns the position furthest from the middle line (for stem direction).
    /// </summary>
    private int GetStaffPosition(MusicItem item)
    {
        return item switch
        {
            NoteItem note => note.StaffPosition,
            ChordItem chord => GetChordStaffPosition(chord),
            _ => 4 // Default to middle line
        };
    }
    
    private int GetChordStaffPosition(ChordItem chord)
    {
        if (chord.Notes.Length == 0)
            return 4;
        
        // For stem direction calculation, use average position
        // (same logic as StemUp property in ChordItem)
        return (int)chord.Notes.Average(n => n.StaffPosition);
    }
}