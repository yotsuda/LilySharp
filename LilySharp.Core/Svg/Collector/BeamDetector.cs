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
internal sealed class BeamDetector
{
    /// <summary>
    /// Detects all beam groups in a score, across every voice.
    /// </summary>
    /// <remarks>
    /// Automatic beaming never crosses voices — each voice groups its own notes.
    /// In a polyphonic staff the stem direction is FORCED by voice (voice 1 up,
    /// voice 2 down), mirroring the renderer's <see cref="VoiceDefaults.GetDefaultStemUp"/>,
    /// so a lower voice's beam sits below its notes. A single-voice staff keeps
    /// the position-based direction (and thus stays byte-identical).
    /// LILYPOND-REF: lily/auto-beam-engraver.cc — one Beam per Voice context.
    /// LILYPOND-REF: ly/engraver-init.ly — \voiceOne/\voiceTwo force stem direction.
    /// </remarks>
    public ImmutableArray<BeamGroup> DetectBeamGroups(Score score)
    {
        if (score.Voices.Length <= 1)
            return DetectBeamGroups(score.Voice, score.TimeSignature, score.TupletBrackets);

        var all = ImmutableArray.CreateBuilder<BeamGroup>();
        for (int v = 0; v < score.Voices.Length; v++)
        {
            // A tuplet bounds beaming ONLY within its own voice — filtering by
            // VoiceIndex stops an upper voice's triplet from splitting a lower
            // voice's eighth run at the shared item index.
            var voiceTuplets = score.TupletBrackets.IsDefaultOrEmpty
                ? score.TupletBrackets
                : score.TupletBrackets.Where(t => t.VoiceIndex == v).ToImmutableArray();
            all.AddRange(DetectBeamGroups(
                score.Voices[v], score.TimeSignature, voiceTuplets,
                voiceIndex: v, forceStemUp: VoiceDefaults.GetDefaultStemUp(v + 1)));
        }
        return all.ToImmutable();
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
        ImmutableArray<TupletBracketItem> tupletBrackets = default,
        int voiceIndex = 0, bool? forceStemUp = null)
    {
        var beamGroups = new List<BeamGroup>();
        var consumed = new HashSet<(int measureIndex, int itemIndex)>();

        // Auto beams never cross a tuplet boundary: a tuplet is its own
        // rhythmic group, so a beam ends where a tuplet starts or ends.
        // LILYPOND-REF: lily/auto-beam-engraver.cc — tuplet spans bound beams.
        var tupletBoundaries = new HashSet<(int measureIndex, int itemIndex)>();
        // A tuplet is ONE beaming unit: its notes carry their WRITTEN duration (an 8th
        // triplet's notes each read 1/8), so the per-beat grouping would cut the run
        // where the written positions cross a beat — splitting a 3-note 8th triplet 2+1.
        // Suppress the beat-boundary flush INSIDE a tuplet so all its notes beam.
        var tupletInteriors = new HashSet<(int measureIndex, int itemIndex)>();
        // The two ENDS of each tuplet span, which a beam that runs through one must not hang
        // a beamlet out of: LilyPond keeps such a stem's flag CENTER and then clamps its
        // outward count to the neighbour's.
        // LILYPOND-REF: lily/beaming-pattern.cc:524-540 at_span_start / at_span_stop.
        var tupletStarts = new HashSet<(int measureIndex, int itemIndex)>();
        var tupletStops = new HashSet<(int measureIndex, int itemIndex)>();
        if (!tupletBrackets.IsDefaultOrEmpty)
        {
            foreach (var bracket in tupletBrackets)
            {
                tupletBoundaries.Add((bracket.MeasureIndex, bracket.StartNoteIndex));
                tupletBoundaries.Add((bracket.MeasureIndex, bracket.EndNoteIndex + 1));
                tupletStarts.Add((bracket.MeasureIndex, bracket.StartNoteIndex));
                tupletStops.Add((bracket.MeasureIndex, bracket.EndNoteIndex));
                for (int i = bracket.StartNoteIndex; i <= bracket.EndNoteIndex; i++)
                    tupletInteriors.Add((bracket.MeasureIndex, i));
            }
        }

        // Pass 1: cross-measure manual beams.
        DetectCrossMeasureManualBeams(voice, timeSignature, beamGroups, consumed,
            tupletStarts, tupletStops, voiceIndex, forceStemUp);

        // Pass 2: single-measure detection (skipping consumed items). The beam
        // grouping depends on the meter, which a mid-piece \time changes — track
        // the EFFECTIVE time signature per measure (a TimeSignatureChangeItem
        // carries the new meter from its measure onward) rather than beaming every
        // measure by the initial one (which beamed e.g. a 6/8 measure as 3/4).
        // LILYPOND-REF: lily/beaming-pattern.cc — beaming follows the current
        //   timeSignatureFraction / beatStructure.
        var effectiveTimeSig = timeSignature;
        for (int measureIndex = 0; measureIndex < voice.Measures.Length; measureIndex++)
        {
            var measure = voice.Measures[measureIndex];
            foreach (var item in measure.Items)
                if (item is TimeSignatureChangeItem tsc)
                    effectiveTimeSig = tsc.NewTime;
            DetectBeamGroupsInMeasure(measure, measureIndex, effectiveTimeSig, beamGroups, consumed, tupletBoundaries, tupletInteriors, tupletStarts, tupletStops, voiceIndex, forceStemUp);
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
        TimeSignature timeSignature,
        List<BeamGroup> beamGroups,
        HashSet<(int, int)> consumed,
        HashSet<(int, int)>? tupletStarts = null,
        HashSet<(int, int)>? tupletStops = null,
        int voiceIndex = 0,
        bool? forceStemUp = null)
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

                BuildCrossMeasureBeamGroup(voice, timeSignature, startM, startI, mi, ii, beamGroups,
                    consumed, tupletStarts, tupletStops, voiceIndex, forceStemUp);
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
        TimeSignature timeSignature,
        int startMeasure, int startItem,
        int endMeasure, int endItem,
        List<BeamGroup> beamGroups,
        HashSet<(int, int)> consumed,
        HashSet<(int, int)>? tupletStarts = null,
        HashSet<(int, int)>? tupletStops = null,
        int voiceIndex = 0,
        bool? forceStemUp = null)
    {
        var allEntries = new List<(MusicItem Item, int Index, Fraction StartPos, int Measure)>();
        // Items this pair WOULD consume — committed to the shared `consumed` set
        // only once we know a beam group is actually built. A degenerate pair
        // (fewer than two beamable items) must not suppress the per-measure pass
        // from beaming the spanned items itself.
        var pendingConsumed = new List<(int, int)>();

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
                pendingConsumed.Add((mi, ii));
            }
        }

        if (allEntries.Count < 2)
            return;

        foreach (var key in pendingConsumed)
            consumed.Add(key);

        // Build per-member metadata mirroring CreateBeamGroup but with explicit measure index.
        // The moments this beam is beamified in run ACROSS the bar lines it crosses: each
        // entry's position was recomputed from its own measure's start, so the measures
        // before it are added back on. Their length is the beat structure's period — the same
        // quantity LilyPond calls the period and measures beat walks in.
        var options = BeamingPattern.Options.For(timeSignature);
        var moments = new (MusicItem Item, Fraction Moment, int Measure, int Index)[allEntries.Count];
        for (int i = 0; i < allEntries.Count; i++)
        {
            var (item, itemIdx, pos, mi) = allEntries[i];
            moments[i] = (item, pos + new Fraction(mi - startMeasure) * options.Period, mi, itemIdx);
        }
        var beamlets = BeamletCounts(moments, options, tupletStarts, tupletStops);

        var members = new List<BeamMember>(allEntries.Count);
        for (int i = 0; i < allEntries.Count; i++)
        {
            var (item, itemIdx, _, mi) = allEntries[i];
            int beamCount = GetBeamCount(item);
            int staffPosition = GetStaffPosition(item);
            int beamCountLeft = beamlets[i].Left;
            int beamCountRight = beamlets[i].Right;

            // clip-edges: the outer sides of the outer stems carry nothing.
            // LILYPOND-REF: lily/beam.cc:1264-1268 Beam::set_beaming.
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

        }

        // A polyphonic voice forces its direction; otherwise the farthest head decides.
        bool stemUp = forceStemUp ?? DefaultBeamStemUp(members);
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
            growDirection: 0,
            voiceIndex: voiceIndex));
    }

    private void DetectBeamGroupsInMeasure(
        Measure measure,
        int measureIndex,
        TimeSignature timeSig,
        List<BeamGroup> beamGroups,
        HashSet<(int, int)>? consumed = null,
        HashSet<(int, int)>? tupletBoundaries = null,
        HashSet<(int, int)>? tupletInteriors = null,
        HashSet<(int, int)>? tupletStarts = null,
        HashSet<(int, int)>? tupletStops = null,
        int voiceIndex = 0,
        bool? forceStemUp = null)
    {
        // The beat grid the beamlet rule reads. Derived ONCE per measure rather than per beam
        // group: it depends only on the meter, and building it allocates.
        var beamOptions = BeamingPattern.Options.For(timeSig);

        // Phase 0: Detect manual beam groups (c8[ d e f])
        var manualRanges = DetectManualBeamGroups(measure, measureIndex, beamOptions, beamGroups,
            tupletStarts, tupletStops, voiceIndex, forceStemUp);

        // First pass: collect groups at beat boundaries
        var beatGroups = new List<List<(MusicItem item, int index, Fraction startPos)>>();
        var currentGroup = new List<(MusicItem item, int index, Fraction startPos)>();
        Fraction currentPosition = Fraction.Zero;
        Fraction groupStartPosition = Fraction.Zero;

        // The beats a beam may not cross come from beamOptions — the SAME grid the beamlet
        // rule reads, and LilyPond's own (beatBase times beatStructure). There used to be a
        // second, flatter spelling here (the dotted quarter for compound meters, one over the
        // denominator otherwise), which agreed for 4/4, 3/4, 2/4, 6/8, 9/8 and 12/8 and drew
        // NO BEAM AT ALL in 4/8, 5/8, 8/8 and 2/8, where a grid of one eighth per group left
        // every group holding a single note.

        // ⚠️ EVERY flush below keeps a group of ONE. The beat grid is not the last word — a
        // beamException may beam eighths straight across several beats — so a group holding a
        // single note has to survive until MergePureEighthNoteGroups has had its say. Groups
        // still holding one note after that are dropped, at the end of this method. What
        // makes it safe to carry them is the merge's own ADJACENCY test: two groups that were
        // split by a rest, or by a manual beam, are not adjacent in time and are never
        // rejoined. See beam.grouping.beat-split-inside-exception (this) and
        // beam.grouping.rest-inside-exception (that) in the ledger.
        for (int i = 0; i < measure.Items.Length; i++)
        {
            var item = measure.Items[i];
            var duration = GetDuration(item);

            // Skip items covered by manual beam groups (single-measure or cross-measure)
            if (IsInManualRange(i, manualRanges) ||
                (consumed != null && consumed.Contains((measureIndex, i))))
            {
                // Non-beamable break logic still applies for position tracking
                if (currentGroup.Count > 0)
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
                    beatGroups.Add(new List<(MusicItem, int, Fraction)>(currentGroup));
                    currentGroup.Clear();
                    groupStartPosition = currentPosition;
                }

                if (currentGroup.Count > 0
                    && !(tupletInteriors != null && tupletInteriors.Contains((measureIndex, i)))
                    && CrossesBeatBoundary(groupStartPosition, currentPosition, beamOptions))
                {
                    // Flush current group at beat boundary
                    beatGroups.Add(new List<(MusicItem, int, Fraction)>(currentGroup));
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
                if (currentGroup.Count > 0)
                {
                    beatGroups.Add(new List<(MusicItem, int, Fraction)>(currentGroup));
                }
                currentGroup.Clear();
            }

            currentPosition = currentPosition + duration;
        }

        // Flush any remaining group
        if (currentGroup.Count > 0)
        {
            beatGroups.Add(new List<(MusicItem, int, Fraction)>(currentGroup));
        }

        // Second pass: merge consecutive pure-8th-note groups in same half-measure
        var mergedGroups = MergePureEighthNoteGroups(beatGroups, timeSig, measureIndex, tupletBoundaries);

        // Convert to BeamGroups. A group of one note is not a beam — it is a flagged note —
        // and this is the ONLY place that decides so, now that the passes above carry them.
        foreach (var group in mergedGroups)
        {
            if (group.Count < 2)
                continue;
            var beamGroup = CreateBeamGroup(group, measureIndex, beamOptions,
                tupletStarts, tupletStops, voiceIndex, forceStemUp);
            beamGroups.Add(beamGroup);
        }
    }

    /// <summary>
    /// Merges consecutive pure-8th-note groups that fall within the same grouping unit.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/time-signature-settings.scm:69-171 default-time-signature-settings —
    /// the beamExceptions entries keyed on 1/8, which are what beam eighths BEYOND the beat.
    /// See <see cref="EighthNoteBeamExceptionLength"/> for the four of them.
    /// </remarks>
    private List<List<(MusicItem item, int index, Fraction startPos)>> MergePureEighthNoteGroups(
        List<List<(MusicItem item, int index, Fraction startPos)>> beatGroups,
        TimeSignature timeSig,
        int measureIndex = 0,
        HashSet<(int, int)>? tupletBoundaries = null)
    {
        if (beatGroups.Count == 0)
            return beatGroups;

        if (EighthNoteBeamExceptionLength(timeSig) is not { } groupLength)
            return beatGroups;                  // no exception: beam by the beat

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
                    // …nor groups split by anything that OCCUPIED TIME between them. The
                    // exception says where a beam may run to, not that a beam may swallow a
                    // rest: LilyPond ends a beam at one whatever the exception says (ledger
                    // beam.grouping.rest-inside-exception, where 3/4 — the only meter whose
                    // exception group is long enough to hold two runs and a rest — drew one
                    // beam straight over it). A tuplet boundary passes this test, since it
                    // takes no time; that is what the line above is still for.
                    var last = currentMerged[^1];
                    bool adjacentInTime = last.startPos + GetDuration(last.item) == groupStart;

                    if (sameGroup && currentIsPureEighths && !atTupletBoundary && adjacentInTime)
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
    /// How long a run of eighths this meter beams as one unit, ACROSS its beats — or null
    /// when the meter has no such exception and eighths beam by the beat like everything else.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/time-signature-settings.scm:69-171 default-time-signature-settings —
    /// every entry there whose beamExceptions is keyed on 1/8, and no others:
    /// <list type="bullet">
    /// <item>:120 <c>((4 . 4) … (1/8 . (4 4)))</c> — two groups of four eighths, a half measure</item>
    /// <item>:99 <c>((3 . 4) … (1/8 . (6)))</c> — one group of six, the whole measure</item>
    /// <item>:81 <c>((2 . 8) … (1/8 . (2)))</c> — the whole measure</item>
    /// <item>:104 <c>((3 . 8) … (1/8 . (3)))</c> — the whole measure</item>
    /// </list>
    /// Every list is uniform, so ONE length says all of it. ⚠️ 6/8, 9/8 and 12/8 are absent on
    /// purpose — their eighths beam by the dotted-quarter BEAT, which the beat structure
    /// already gives. So is 2/4, which is what says the exceptions are not applied wherever
    /// they would fit (ledger beam.grouping.two-four.groups).
    /// <para>
    /// ⚠️ NOT PORTED, and no point observes it: the same table's exceptions keyed on 1/12
    /// (triplet eighths in 3/4 and 4/4, :100 and :121) and on 1/16 and 1/32 (2/2, 4/2, 6/4,
    /// 9/4, 12/4, 3/2). They want the run's SHORTEST duration to choose the entry, which is a
    /// lookup this pass does not have — it only asks "are these all eighths". It comes back
    /// when that question becomes "what is the shortest note here".
    /// </para>
    /// </remarks>
    private static Fraction? EighthNoteBeamExceptionLength(TimeSignature timeSig) =>
        (timeSig.Beats, timeSig.BeatType) switch
        {
            (4, 4) => new Fraction(1, 2),
            (3, 4) => new Fraction(3, 4),
            (2, 8) => new Fraction(1, 4),
            (3, 8) => new Fraction(3, 8),
            _ => null,
        };

    /// <summary>
    /// Whether a BEAT boundary lies between <paramref name="groupStart"/> and
    /// <paramref name="currentPos"/> — where an automatic beam ends unless an exception
    /// carries it further.
    /// </summary>
    /// <remarks>
    /// The beats are LilyPond's: beatBase times each entry of beatStructure, which is uneven
    /// for 4/8, 5/8 and 8/8. The structure REPEATS past the period, as LilyPond's own walk
    /// does when a beam runs past the end of the beat list
    /// (lily/beaming-pattern.cc:135-144 remaining_beats).
    /// </remarks>
    private static bool CrossesBeatBoundary(
        Fraction groupStart, Fraction currentPos, BeamingPattern.Options options)
        => BeatIndexAt(currentPos, options) > BeatIndexAt(groupStart, options);

    /// <summary>Which beat of the grid <paramref name="position"/> falls in, counting from 0.</summary>
    private static int BeatIndexAt(Fraction position, BeamingPattern.Options options)
    {
        int index = 0;
        var edge = Fraction.Zero;
        // Every step adds beatBase times a structure entry, both strictly positive, so this
        // reaches any finite position.
        for (int k = 0; ; k++)
        {
            edge += new Fraction(options.BeatStructure[k % options.BeatStructure.Length])
                    * options.BeatBase;
            if (position < edge)
                return index;
            index++;
        }
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

    private BeamGroup CreateBeamGroup(List<(MusicItem item, int index, Fraction startPos)> group, int measureIndex,
        BeamingPattern.Options beamOptions,
        HashSet<(int, int)>? tupletStarts = null, HashSet<(int, int)>? tupletStops = null,
        int voiceIndex = 0, bool? forceStemUp = null)
    {
        var members = new List<BeamMember>();

        var moments = new (MusicItem Item, Fraction Moment, int Measure, int Index)[group.Count];
        for (int i = 0; i < group.Count; i++)
            moments[i] = (group[i].item, group[i].startPos, measureIndex, group[i].index);
        var beamlets = BeamletCounts(moments, beamOptions, tupletStarts, tupletStops);

        for (int i = 0; i < group.Count; i++)
        {
            var (item, itemIndex, _) = group[i];
            int beamCount = GetBeamCount(item);
            int staffPosition = GetStaffPosition(item);

            int beamCountLeft = beamlets[i].Left;
            int beamCountRight = beamlets[i].Right;

            // clip-edges (default #t): the OUTER side of an outer stem carries nothing, and
            // LilyPond zeroes it after the pattern has been beamified rather than in it. Its
            // INNER side keeps the stem's own count — the pattern only ever reduces interior
            // stems, so an outer one is never chipped down to its neighbour's count.
            // LILYPOND-REF: lily/beam.cc:1264-1268 Beam::set_beaming.
            if (i == 0)
                beamCountLeft = 0;
            if (i == group.Count - 1)
                beamCountRight = 0;

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

        // A polyphonic voice forces its direction (voice 1 up / voice 2 down);
        // otherwise the head farthest from the middle line decides (LP get_default_dir).
        bool stemUp = forceStemUp ?? DefaultBeamStemUp(members);

        // Check if first note has feathered beam direction
        // LILYPOND-REF: beam.cc:1039-1082 grow-direction
        int growDirection = group[0].item is NoteItem firstNote ? firstNote.FeatherDirection : 0;

        // Auto-knee detection: knee when the union of (widened) head extents
        // leaves an interior gap larger than auto-knee-gap + the beam stack.
        // LILYPOND-REF: beam.cc:968-1056 consider_auto_knees
        // LILYPOND-REF: define-grobs.scm:476 auto-knee-gap = 5.5
        // A forced-direction (polyphonic) voice never knees — every stem stays on
        // the voice's side, so auto-knee only runs in a neutral single voice.
        double? kneeGapCenter = forceStemUp is null ? AutoKneeGapCenter(members) : null;

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
            growDirection,
            voiceIndex);
    }

    /// <summary>
    /// How many beam lines reach each member on each side, from the beam's rhythm.
    /// </summary>
    /// <remarks>
    /// The one door to <see cref="BeamingPattern"/> — both the per-measure and the
    /// cross-measure builder come through here, so the two cannot answer differently.
    /// A member is at a tuplet span's boundary when it is that tuplet's first or last note;
    /// the group's own first and last members are excluded because LilyPond clips the test to
    /// the beam (<c>max(tuplet_start, start_moment (0))</c>), and because the passes that
    /// read it never touch an outer stem anyway.
    /// LILYPOND-REF: lily/beaming-pattern.cc:524-540 at_span_start / at_span_stop.
    /// </remarks>
    private (int Left, int Right)[] BeamletCounts(
        IReadOnlyList<(MusicItem Item, Fraction Moment, int Measure, int Index)> members,
        BeamingPattern.Options options,
        HashSet<(int, int)>? tupletStarts, HashSet<(int, int)>? tupletStops)
    {
        var infos = new BeamingPattern.Element[members.Count];
        for (int i = 0; i < members.Count; i++)
        {
            var m = members[i];
            infos[i] = new BeamingPattern.Element(
                m.Moment, GetDuration(m.Item), GetBeamCount(m.Item),
                AtSpanStart: i > 0 && tupletStarts?.Contains((m.Measure, m.Index)) == true,
                AtSpanStop: i < members.Count - 1 && tupletStops?.Contains((m.Measure, m.Index)) == true);
        }
        return BeamingPattern.Beamify(infos, options);
    }

    /// <summary>auto-knee-gap, in staff spaces.</summary>
    /// <remarks>LILYPOND-REF: define-grobs.scm:476 (auto-knee-gap . 5.5)</remarks>
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
        // A two-note tremolo pair beams regardless of its written duration
        // (halves joined by the subdivision's beams).
        if (item is NoteItem { TremoloPairBeams: > 0 } or ChordItem { TremoloPairBeams: > 0 })
            return true;
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
        switch (item)
        {
            case NoteItem { TremoloPairBeams: > 0 } tn: return tn.TremoloPairBeams;
            case ChordItem { TremoloPairBeams: > 0 } tc: return tc.TremoloPairBeams;
        }
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
    /// Default beam stem direction for a NEUTRAL (non-polyphonic) beam.
    /// LILYPOND-REF: lily/beam.cc:876-940 Beam::get_default_dir — the head FARTHEST
    /// from the middle line decides, exactly as for a single chord: a head far above
    /// the centre puts stems DOWN, far below puts them UP. Only when the two extremes
    /// tie does a per-stem majority vote (then total distance) break it. Positions are
    /// staff positions with 0 = middle line, positive = above (matching LP).
    /// (Was previously the arithmetic MEAN of positions, which flips whole beams that
    /// straddle the line with an outlier, e.g. [+1,+1,+1,-2]: LP=up, mean=down.)
    /// </summary>
    private static bool DefaultBeamStemUp(IReadOnlyList<BeamMember> members)
    {
        int extremeUp = 0, extremeDown = 0;
        foreach (var m in members)
        {
            if (m.HeadPositionMax > 0) extremeUp = Math.Max(extremeUp, m.HeadPositionMax);
            if (m.HeadPositionMin < 0) extremeDown = Math.Min(extremeDown, m.HeadPositionMin);
        }

        // The farther extreme wins.
        // LILYPOND-REF: lily/beam.cc:918-924 Beam::get_default_dir (extremes check).
        if (Math.Abs(extremeUp) > -extremeDown) return false; // DOWN
        if (extremeUp < -extremeDown) return true;            // UP

        // Tie: per-stem majority vote by each stem's own natural direction.
        // A stem whose head is exactly on (or symmetric about) the middle line has no
        // natural direction, so LP counts it with neutral-direction = DOWN: its
        // Stem::calc_default_direction is CENTER, and get_default_dir falls back to
        // neutral-direction before tallying. Counting it UP flipped whole beams centred
        // on the line (e.g. g'..d'' straddling the middle equally: LP=down, us=up), so
        // the test is `>=`, not `>`.
        // LILYPOND-REF: lily/beam.cc:895-916 (per-stem default/neutral direction tally),
        // LILYPOND-REF: lily/beam.cc:928 (count[UP] - count[DOWN] majority vote).
        int upVotes = 0, downVotes = 0, total = 0;
        foreach (var m in members)
        {
            int mUp = Math.Max(0, m.HeadPositionMax);
            int mDown = Math.Min(0, m.HeadPositionMin);
            if (Math.Abs(mUp) >= -mDown) downVotes++; else upVotes++;
            total += m.StaffPosition;
        }
        if (upVotes != downVotes) return upVotes > downVotes;
        // Fully balanced: below the line -> up, otherwise LP's neutral-direction = DOWN.
        // LILYPOND-REF: lily/beam.cc:937 get_default_dir final neutral-direction fallback.
        return total < 0;
    }

    /// <summary>
    /// Detects manual beam groups from HasBeamStart/HasBeamEnd flags on notes/chords.
    /// Returns the list of (startIndex, endIndex) ranges that are manually beamed.
    /// </summary>
    private List<(int start, int end)> DetectManualBeamGroups(
        Measure measure,
        int measureIndex,
        BeamingPattern.Options beamOptions,
        List<BeamGroup> beamGroups,
        HashSet<(int, int)>? tupletStarts = null,
        HashSet<(int, int)>? tupletStops = null,
        int voiceIndex = 0,
        bool? forceStemUp = null)
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
                    beamGroups.Add(CreateBeamGroup(group, measureIndex, beamOptions,
                        tupletStarts, tupletStops, voiceIndex, forceStemUp));
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






