// Lily# - Music notation compiler
// Copyright (C) 2025-2026 Yoshifumi Tsuda
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

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
        return DetectBeamGroups(score.Voice, score.TimeSignature, score.TupletBrackets);
    }

    /// <summary>
    /// Detects all beam groups in a voice.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/beam.cc — beams may span barlines via manual <c>[</c>/<c>]</c>.
    /// Two passes:
    ///   1. Pre-pass: <see cref="DetectCrossMeasureManualBeams"/> scans the voice for
    ///      manual brackets that open in measure N and close in measure N+M, building
    ///      one cross-measure <see cref="BeamGroup"/> per matched pair.
    ///   2. Per-measure pass: <see cref="DetectBeamGroupsInMeasure"/> handles
    ///      single-measure manual + automatic beaming, skipping items already
    ///      consumed by the cross-measure pass.
    /// </remarks>
    public ImmutableArray<BeamGroup> DetectBeamGroups(Voice voice, TimeSignature timeSignature,
        ImmutableArray<TupletBracketItem> tupletBrackets = default)
    {
        var beamGroups = new List<BeamGroup>();
        var consumed = new HashSet<(int measureIndex, int itemIndex)>();

        // Auto beams never cross a tuplet boundary: a tuplet is its own
        // rhythmic group, so a beam ends where a tuplet starts or ends.
        // LILYPOND-REF: lily/auto-beam-engraver.cc — tuplet spans bound beams.
        var tupletBoundaries = new HashSet<(int measureIndex, int itemIndex)>();
        if (!tupletBrackets.IsDefaultOrEmpty)
        {
            foreach (var bracket in tupletBrackets)
            {
                tupletBoundaries.Add((bracket.MeasureIndex, bracket.StartNoteIndex));
                tupletBoundaries.Add((bracket.MeasureIndex, bracket.EndNoteIndex + 1));
            }
        }

        // Pass 1: cross-measure manual beams.
        DetectCrossMeasureManualBeams(voice, beamGroups, consumed);

        // Pass 2: single-measure detection (skipping consumed items).
        for (int measureIndex = 0; measureIndex < voice.Measures.Length; measureIndex++)
        {
            var measure = voice.Measures[measureIndex];
            DetectBeamGroupsInMeasure(measure, measureIndex, timeSignature, beamGroups, consumed, tupletBoundaries);
        }

        return beamGroups.ToImmutableArray();
    }

    /// <summary>
    /// First pass: identifies <c>[ ... ]</c> ranges that span multiple measures
    /// and builds a single multi-measure <see cref="BeamGroup"/> for each.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/beam.cc — multi-measure manual beams.
    /// Pairs are matched in order: the i-th open <c>[</c> matches the i-th close
    /// <c>]</c>. When the pair lies entirely within one measure we leave it for
    /// the per-measure pass; only true cross-measure pairs are handled here.
    /// </remarks>
    private void DetectCrossMeasureManualBeams(
        Voice voice,
        List<BeamGroup> beamGroups,
        HashSet<(int, int)> consumed)
    {
        // Collect every (measureIndex, itemIndex, isStart) marker.
        var markers = new List<(int Measure, int Item, bool IsStart)>();
        for (int mi = 0; mi < voice.Measures.Length; mi++)
        {
            var measure = voice.Measures[mi];
            for (int ii = 0; ii < measure.Items.Length; ii++)
            {
                var item = measure.Items[ii];
                bool hasStart = item switch
                {
                    NoteItem n => n.HasBeamStart,
                    ChordItem c => c.HasBeamStart,
                    _ => false,
                };
                bool hasEnd = item switch
                {
                    NoteItem n => n.HasBeamEnd,
                    ChordItem c => c.HasBeamEnd,
                    _ => false,
                };
                if (hasStart) markers.Add((mi, ii, IsStart: true));
                if (hasEnd) markers.Add((mi, ii, IsStart: false));
            }
        }

        // Match in order using a stack.
        var openStack = new Stack<(int Measure, int Item)>();
        foreach (var (mi, ii, isStart) in markers)
        {
            if (isStart)
            {
                openStack.Push((mi, ii));
            }
            else if (openStack.Count > 0)
            {
                var (startM, startI) = openStack.Pop();
                if (startM == mi)
                    continue; // within-measure: per-measure pass handles it.

                BuildCrossMeasureBeamGroup(voice, startM, startI, mi, ii, beamGroups, consumed);
            }
        }
    }

    /// <summary>
    /// Constructs a single <see cref="BeamGroup"/> spanning <paramref name="startMeasure"/>
    /// to <paramref name="endMeasure"/> from a manual <c>[</c>...<c>]</c> pair.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/beam.cc — cross-bar beam group.
    /// Each <see cref="BeamMember"/> carries its actual measure index so the
    /// engraver can resolve per-member X positions against the right MeasureLayout.
    /// </remarks>
    private void BuildCrossMeasureBeamGroup(
        Voice voice,
        int startMeasure, int startItem,
        int endMeasure, int endItem,
        List<BeamGroup> beamGroups,
        HashSet<(int, int)> consumed)
    {
        var allEntries = new List<(MusicItem Item, int Index, Fraction StartPos, int Measure)>();
        Fraction running = Fraction.Zero;

        for (int mi = startMeasure; mi <= endMeasure; mi++)
        {
            var measure = voice.Measures[mi];
            int firstItem = (mi == startMeasure) ? startItem : 0;
            int lastItem = (mi == endMeasure) ? endItem : measure.Items.Length - 1;

            // Recompute running position from the start of the measure for accuracy.
            Fraction positionInMeasure = Fraction.Zero;
            for (int j = 0; j < firstItem; j++)
                positionInMeasure += GetDuration(measure.Items[j]);

            for (int ii = firstItem; ii <= lastItem; ii++)
            {
                var item = measure.Items[ii];
                if (IsBeamable(item))
                {
                    allEntries.Add((item, ii, positionInMeasure, mi));
                }
                positionInMeasure += GetDuration(item);
                consumed.Add((mi, ii));
            }
        }

        if (allEntries.Count < 2)
            return;

        // Build per-member metadata mirroring CreateBeamGroup but with explicit measure index.
        var members = new List<BeamMember>(allEntries.Count);
        int totalPosition = 0;
        int noteCount = 0;
        for (int i = 0; i < allEntries.Count; i++)
        {
            var (item, itemIdx, _, mi) = allEntries[i];
            int beamCount = GetBeamCount(item);
            int staffPosition = GetStaffPosition(item);
            int beamCountLeft = beamCount;
            int beamCountRight = beamCount;

            if (i == 0)
                beamCountLeft = 0;
            if (i == allEntries.Count - 1)
                beamCountRight = 0;

            var headRange = GetHeadRange(item);
            members.Add(new BeamMember(
                item, beamCount, beamCountLeft, beamCountRight,
                staffPosition, itemIdx,
                memberStemUp: staffPosition < 0,
                measureIndex: mi,
                headPositionMin: headRange.Min,
                headPositionMax: headRange.Max));

            totalPosition += staffPosition;
            noteCount++;
        }

        bool stemUp = noteCount > 0 && (double)totalPosition / noteCount < 0;
        for (int i = 0; i < members.Count; i++)
        {
            var m = members[i];
            members[i] = new BeamMember(
                m.Item, m.BeamCount, m.BeamCountLeft, m.BeamCountRight,
                m.StaffPosition, m.ItemIndex,
                memberStemUp: stemUp,
                targetStaffIndex: m.TargetStaffIndex,
                measureIndex: m.MeasureIndex,
                headPositionMin: m.HeadPositionMin,
                headPositionMax: m.HeadPositionMax);
        }

        beamGroups.Add(new BeamGroup(
            members.ToImmutableArray(),
            measureIndex: startMeasure,
            startIndex: allEntries[0].Index,
            stemUp,
            growDirection: 0));
    }

    private void DetectBeamGroupsInMeasure(
        Measure measure,
        int measureIndex,
        TimeSignature timeSig,
        List<BeamGroup> beamGroups,
        HashSet<(int, int)>? consumed = null,
        HashSet<(int, int)>? tupletBoundaries = null)
    {
        // Phase 0: Detect manual beam groups (c8[ d e f])
        var manualRanges = DetectManualBeamGroups(measure, measureIndex, beamGroups);

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

            // Skip items covered by manual beam groups (single-measure or cross-measure)
            if (IsInManualRange(i, manualRanges) ||
                (consumed != null && consumed.Contains((measureIndex, i))))
            {
                // Non-beamable break logic still applies for position tracking
                if (currentGroup.Count >= 2)
                {
                    beatGroups.Add(new List<(MusicItem, int, Fraction)>(currentGroup));
                }
                currentGroup.Clear();
                currentPosition = currentPosition + duration;
                continue;
            }

            if (IsBeamable(item))
            {
                // Tuplet boundary: never beam across it (the tuplet is its
                // own rhythmic group, see DetectBeamGroups).
                if (currentGroup.Count > 0 && tupletBoundaries != null
                    && tupletBoundaries.Contains((measureIndex, i)))
                {
                    if (currentGroup.Count >= 2)
                    {
                        beatGroups.Add(new List<(MusicItem, int, Fraction)>(currentGroup));
                    }
                    currentGroup.Clear();
                    groupStartPosition = currentPosition;
                }

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
        var mergedGroups = MergePureEighthNoteGroups(beatGroups, timeSig, measureIndex, tupletBoundaries);

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
        TimeSignature timeSig,
        int measureIndex = 0,
        HashSet<(int, int)>? tupletBoundaries = null)
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
                    // Never re-join groups split at a tuplet boundary.
                    bool atTupletBoundary = tupletBoundaries != null
                        && tupletBoundaries.Contains((measureIndex, group[0].index));

                    if (sameGroup && currentIsPureEighths && !atTupletBoundary)
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

            var headRange = GetHeadRange(item);
            members.Add(new BeamMember(
                item,
                beamCount,
                beamCountLeft,
                beamCountRight,
                staffPosition,
                itemIndex,
                memberStemUp: staffPosition < 0, // Temporary: per-member direction based on position
                headPositionMin: headRange.Min,
                headPositionMax: headRange.Max));
        }

        bool stemUp = noteCount > 0 && (double)totalPosition / noteCount < 0;  // stem up if below middle line

        // Check if first note has feathered beam direction
        // LILYPOND-REF: beam.cc:1039-1082 grow-direction
        int growDirection = group[0].item is NoteItem firstNote ? firstNote.FeatherDirection : 0;

        // Auto-knee detection: knee when the union of (widened) head extents
        // leaves an interior gap larger than auto-knee-gap + the beam stack.
        // LILYPOND-REF: beam.cc:968-1056 consider_auto_knees
        // LILYPOND-REF: define-grobs.scm:437 auto-knee-gap = 5.5
        double? kneeGapCenter = AutoKneeGapCenter(members);

        for (int i = 0; i < members.Count; i++)
        {
            var m = members[i];
            // Knee: each stem points INTO the gap — UP when its head sits
            // below the gap center, DOWN above (beam.cc:1047-1049). Without a
            // knee, every member takes the group direction.
            bool memberUp = kneeGapCenter is { } gapCenter
                ? (m.HeadPositionMin + m.HeadPositionMax) / 2.0 < gapCenter
                : stemUp;
            members[i] = new BeamMember(m.Item, m.BeamCount, m.BeamCountLeft, m.BeamCountRight,
                m.StaffPosition, m.ItemIndex, memberUp,
                headPositionMin: m.HeadPositionMin,
                headPositionMax: m.HeadPositionMax);
        }

        return new BeamGroup(
            members.ToImmutableArray(),
            measureIndex,
            group[0].index,
            stemUp,
            growDirection);
    }

    /// <summary>auto-knee-gap, in staff spaces.</summary>
    /// <remarks>LILYPOND-REF: define-grobs.scm:437 (auto-knee-gap . 5.5)</remarks>
    private const double AutoKneeGap = 5.5;

    /// <summary>
    /// LilyPond's consider_auto_knees: take each member's head-position
    /// interval widened by ±1 position, union them, and find the largest
    /// INTERIOR gap. A knee triggers when that gap exceeds auto-knee-gap plus
    /// the height of the beam stack (thickness/2 + (count−1)·translation).
    /// Returns the gap center (staff positions) or null for no knee.
    /// </summary>
    /// <remarks>LILYPOND-REF: beam.cc:968-1056 consider_auto_knees</remarks>
    private static double? AutoKneeGapCenter(List<BeamMember> members)
    {
        if (members.Count < 2) return null;

        // Head extents in staff positions, widened by 1 like head_extents.widen(1).
        var intervals = members
            .Select(m => (Lo: m.HeadPositionMin - 1.0, Hi: m.HeadPositionMax + 1.0))
            .OrderBy(iv => iv.Lo)
            .ToList();

        // Walk the sorted union; track the largest interior gap.
        double maxGapLen = 0, gapCenter = 0;
        double coveredHi = intervals[0].Hi;
        for (int i = 1; i < intervals.Count; i++)
        {
            if (intervals[i].Lo > coveredHi)
            {
                double len = intervals[i].Lo - coveredHi;
                if (len >= maxGapLen)
                {
                    maxGapLen = len;
                    gapCenter = (coveredHi + intervals[i].Lo) / 2.0;
                }
            }
            coveredHi = Math.Max(coveredHi, intervals[i].Hi);
        }

        // threshold = auto-knee-gap + height_of_beams (staff spaces → ×2 positions).
        // LILYPOND-REF: beam.cc:1033-1040
        int beamCount = members.Max(m => m.BeamCount);
        double heightOfBeams = EngravingDefaults.BeamThickness / 2.0
            + (beamCount - 1) * EngravingDefaults.BeamTranslation;
        double thresholdPositions = (AutoKneeGap + heightOfBeams) * 2.0;

        return maxGapLen > thresholdPositions ? gapCenter : null;
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

    /// <summary>
    /// Notehead staff-position range: single value for notes, bottom/top
    /// chord notes for chords. Feeds concaveness's close/far heads.
    /// LILYPOND-REF: beam-quanting.cc calc_concaveness head_positions_.
    /// </summary>
    private static (int Min, int Max) GetHeadRange(MusicItem item) => item switch
    {
        NoteItem note => (note.StaffPosition, note.StaffPosition),
        ChordItem { Notes.Length: > 0 } chord =>
            (chord.Notes.Min(n => n.StaffPosition), chord.Notes.Max(n => n.StaffPosition)),
        _ => (4, 4)
    };

    /// <summary>
    /// Detects manual beam groups from HasBeamStart/HasBeamEnd flags on notes/chords.
    /// Returns the list of (startIndex, endIndex) ranges that are manually beamed.
    /// </summary>
    private List<(int start, int end)> DetectManualBeamGroups(
        Measure measure,
        int measureIndex,
        List<BeamGroup> beamGroups)
    {
        var ranges = new List<(int start, int end)>();
        int? beamStart = null;

        for (int i = 0; i < measure.Items.Length; i++)
        {
            var item = measure.Items[i];
            bool hasStart = item switch
            {
                NoteItem note => note.HasBeamStart,
                ChordItem chord => chord.HasBeamStart,
                _ => false
            };
            bool hasEnd = item switch
            {
                NoteItem note => note.HasBeamEnd,
                ChordItem chord => chord.HasBeamEnd,
                _ => false
            };

            if (hasStart)
            {
                beamStart = i;
            }

            if (hasEnd && beamStart != null)
            {
                int start = beamStart.Value;
                int end = i;

                // Collect beamable items in this range
                var group = new List<(MusicItem item, int index, Fraction startPos)>();
                Fraction pos = Fraction.Zero;
                // Calculate starting position
                for (int j = 0; j < start; j++)
                    pos = pos + GetDuration(measure.Items[j]);

                for (int j = start; j <= end; j++)
                {
                    var groupItem = measure.Items[j];
                    if (IsBeamable(groupItem))
                    {
                        group.Add((groupItem, j, pos));
                    }
                    pos = pos + GetDuration(groupItem);
                }

                if (group.Count >= 2)
                {
                    beamGroups.Add(CreateBeamGroup(group, measureIndex));
                    ranges.Add((start, end));
                }

                beamStart = null;
            }
        }

        return ranges;
    }

    private static bool IsInManualRange(int index, List<(int start, int end)> ranges)
    {
        foreach (var (start, end) in ranges)
        {
            if (index >= start && index <= end)
                return true;
        }
        return false;
    }
}






