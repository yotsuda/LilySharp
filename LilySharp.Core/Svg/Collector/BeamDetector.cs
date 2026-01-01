using System.Collections.Immutable;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Collector;

/// <summary>
/// Detects beam groups from measures.
/// Based on Lilypond's beaming-pattern.cc and auto-beam-engraver.cc.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/beaming-pattern.cc
/// LILYPOND-REF: lily/auto-beam-engraver.cc
/// 
/// Beams are grouped according to the time signature's beat structure.
/// Mixed durations (8th + 16th) within the same beat are beamed together.
/// - Pure 8th notes: grouped per half-measure (4 notes in 4/4)
/// - 16th notes or mixed: grouped per beat
/// </remarks>
public sealed class BeamDetector
{
    /// <summary>
    /// Detects all beam groups in a score.
    /// </summary>
    public ImmutableArray<BeamGroup> DetectBeamGroups(Score score)
    {
        return DetectBeamGroups(score.Voice, score.TimeSignature);
    }
    
    /// <summary>
    /// Detects all beam groups in a voice.
    /// </summary>
    public ImmutableArray<BeamGroup> DetectBeamGroups(Voice voice, TimeSignature timeSignature)
    {
        var beamGroups = new List<BeamGroup>();
        
        for (int measureIndex = 0; measureIndex < voice.Measures.Length; measureIndex++)
        {
            var measure = voice.Measures[measureIndex];
            DetectBeamGroupsInMeasure(measure, measureIndex, timeSignature, beamGroups);
        }
        
        return beamGroups.ToImmutableArray();
    }
    
    private void DetectBeamGroupsInMeasure(
        Measure measure, 
        int measureIndex, 
        TimeSignature timeSig,
        List<BeamGroup> beamGroups)
    {
        // First pass: collect groups at beat boundaries
        var beatGroups = new List<List<(MusicItem item, int index, Fraction startPos)>>();
        var currentGroup = new List<(MusicItem item, int index, Fraction startPos)>();
        Fraction currentPosition = Fraction.Zero;
        Fraction groupStartPosition = Fraction.Zero;
        
        // Calculate beat length
        Fraction beatLength;
        if (timeSig.BeatType == 8 && timeSig.Beats % 3 == 0)
        {
            // Compound meter: dotted quarter
            beatLength = new Fraction(3, 8);
        }
        else
        {
            // Simple meter: one beat
            beatLength = new Fraction(1, timeSig.BeatType);
        }
        
        for (int i = 0; i < measure.Items.Length; i++)
        {
            var item = measure.Items[i];
            var duration = GetDuration(item);
            
            if (IsBeamable(item))
            {
                if (currentGroup.Count > 0 && CrossesGroupBoundary(groupStartPosition, currentPosition, beatLength))
                {
                    // Flush current group at beat boundary
                    if (currentGroup.Count >= 2)
                    {
                        beatGroups.Add(new List<(MusicItem, int, Fraction)>(currentGroup));
                    }
                    currentGroup.Clear();
                    groupStartPosition = currentPosition;
                }
                
                if (currentGroup.Count == 0)
                {
                    groupStartPosition = currentPosition;
                }
                
                currentGroup.Add((item, i, currentPosition));
            }
            else
            {
                // Non-beamable item breaks the beam
                if (currentGroup.Count >= 2)
                {
                    beatGroups.Add(new List<(MusicItem, int, Fraction)>(currentGroup));
                }
                currentGroup.Clear();
            }
            
            currentPosition = currentPosition + duration;
        }
        
        // Flush any remaining group
        if (currentGroup.Count >= 2)
        {
            beatGroups.Add(new List<(MusicItem, int, Fraction)>(currentGroup));
        }
        
        // Second pass: merge consecutive pure-8th-note groups in same half-measure
        var mergedGroups = MergePureEighthNoteGroups(beatGroups, timeSig);
        
        // Convert to BeamGroups
        foreach (var group in mergedGroups)
        {
            var beamGroup = CreateBeamGroup(group, measureIndex);
            beamGroups.Add(beamGroup);
        }
    }
    
    /// <summary>
    /// Merges consecutive pure-8th-note groups that fall within the same grouping unit.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/beaming-pattern.cc
    /// 
    /// 8th note grouping rules:
    /// - 4/4: group per half-measure (2 beats)
    /// - 3/4: group per full measure (all 3 beats)
    /// - 2/4: group per full measure (both beats)
    /// - 6/8, 9/8, 12/8: group per dotted quarter (already handled)
    /// </remarks>
    private List<List<(MusicItem item, int index, Fraction startPos)>> MergePureEighthNoteGroups(
        List<List<(MusicItem item, int index, Fraction startPos)>> beatGroups,
        TimeSignature timeSig)
    {
        if (beatGroups.Count == 0)
            return beatGroups;
        
        // For compound meter, don't merge (already grouped correctly)
        if (timeSig.BeatType == 8 && timeSig.Beats % 3 == 0)
            return beatGroups;
        
        // Calculate grouping length for 8th notes
        Fraction groupLength;
        if (timeSig.Beats >= 4)
        {
            // 4/4 or larger: half measure
            groupLength = new Fraction(timeSig.Beats / 2, timeSig.BeatType);
        }
        else
        {
            // 2/4, 3/4: full measure
            groupLength = new Fraction(timeSig.Beats, timeSig.BeatType);
        }
        var result = new List<List<(MusicItem item, int index, Fraction startPos)>>();
        var currentMerged = new List<(MusicItem item, int index, Fraction startPos)>();
        Fraction mergeStartPos = Fraction.Zero;
        
        foreach (var group in beatGroups)
        {
            bool isPureEighths = group.All(g => GetBeamCount(g.item) == 1);
            Fraction groupStart = group[0].startPos;            
            if (isPureEighths)
            {
                // Check if we can merge with current
                if (currentMerged.Count > 0)
                {
                    // Check if in same group
                    bool sameGroup = !CrossesGroupBoundary(mergeStartPos, groupStart, groupLength);
                    bool currentIsPureEighths = currentMerged.All(g => GetBeamCount(g.item) == 1);
                    
                    if (sameGroup && currentIsPureEighths)
                    {
                        // Merge
                        currentMerged.AddRange(group);
                        continue;
                    }
                    else
                    {
                        // Flush current and start new
                        result.Add(new List<(MusicItem, int, Fraction)>(currentMerged));
                        currentMerged.Clear();
                    }
                }
                
                currentMerged.AddRange(group);
                mergeStartPos = groupStart;
            }
            else
            {
                // Not pure eighths - flush current and add this group separately
                if (currentMerged.Count > 0)
                {
                    result.Add(new List<(MusicItem, int, Fraction)>(currentMerged));
                    currentMerged.Clear();
                }
                result.Add(group);
            }
        }
        
        // Flush remaining
        if (currentMerged.Count > 0)
        {
            result.Add(currentMerged);
        }
        
        return result;
    }
    
    /// <summary>
    /// Checks if the current position crosses a group boundary from the group start.
    /// </summary>
    private bool CrossesGroupBoundary(Fraction groupStart, Fraction currentPos, Fraction groupLength)
    {
        long startGroup = (groupStart.Numerator * groupLength.Denominator) / 
                          (groupStart.Denominator * groupLength.Numerator);
        long currentGroup = (currentPos.Numerator * groupLength.Denominator) / 
                            (currentPos.Denominator * groupLength.Numerator);
        
        return currentGroup > startGroup;
    }
    
    private BeamGroup CreateBeamGroup(List<(MusicItem item, int index, Fraction startPos)> group, int measureIndex)
    {
        var members = new List<BeamMember>();
        int totalPosition = 0;
        int noteCount = 0;
        
        for (int i = 0; i < group.Count; i++)
        {
            var (item, itemIndex, _) = group[i];
            int beamCount = GetBeamCount(item);
            int staffPosition = GetStaffPosition(item);
            
            totalPosition += staffPosition;
            noteCount++;
            
            // Calculate beam counts for left and right sides
            int beamCountLeft, beamCountRight;
            
            if (i == 0)
            {
                beamCountLeft = 0;
                beamCountRight = beamCount;
            }
            else if (i == group.Count - 1)
            {
                int prevBeamCount = GetBeamCount(group[i - 1].item);
                beamCountLeft = Math.Min(beamCount, prevBeamCount);
                beamCountRight = 0;
            }
            else
            {
                int prevBeamCount = GetBeamCount(group[i - 1].item);
                int nextBeamCount = GetBeamCount(group[i + 1].item);
                
                int continuousBeams = Math.Min(Math.Min(beamCount, prevBeamCount), nextBeamCount);
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
        
        bool stemUp = noteCount > 0 && (double)totalPosition / noteCount < 4;
        
        return new BeamGroup(
            members.ToImmutableArray(),
            measureIndex,
            group[0].index,
            stemUp);
    }
    
    private Fraction GetDuration(MusicItem item)
    {
        return item switch
        {
            NoteItem note => note.Duration,
            ChordItem chord => chord.Duration,
            RestItem rest => rest.Duration,
            _ => Fraction.Zero
        };
    }
    
    private bool IsBeamable(MusicItem item)
    {
        var baseDuration = item switch
        {
            NoteItem note => note.BaseDuration,
            ChordItem chord => chord.BaseDuration,
            _ => Fraction.Whole
        };
        
        return baseDuration.Denominator >= 8;
    }
    
    private int GetBeamCount(MusicItem item)
    {
        var baseDuration = item switch
        {
            NoteItem note => note.BaseDuration,
            ChordItem chord => chord.BaseDuration,
            _ => Fraction.Quarter
        };
        
        int log2 = 0;
        long denom = baseDuration.Denominator;
        while (denom > 1)
        {
            denom >>= 1;
            log2++;
        }
        
        return Math.Max(0, log2 - 2);
    }
    
    private int GetStaffPosition(MusicItem item)
    {
        return item switch
        {
            NoteItem note => note.StaffPosition,
            ChordItem chord => GetChordStaffPosition(chord),
            _ => 4
        };
    }
    
    private int GetChordStaffPosition(ChordItem chord)
    {
        if (chord.Notes.Length == 0)
            return 4;
        
        return (int)chord.Notes.Average(n => n.StaffPosition);
    }
}






