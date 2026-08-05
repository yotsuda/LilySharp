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

using System;
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
/// A beam ends where <see cref="AutoBeamCheck"/> says it must — at the end of one of the
/// groups the meter's beamExceptions ask for, chosen by the SHORTEST duration in the beam so
/// far, and at the beats themselves when the meter offers no entry for that duration. So the
/// eighths of a 4/4 bar make two beams of four (the half-measure exception), its sixteenths
/// four of four (the beat), and the sixteenths of a 6/4 bar six of four (an exception FINER
/// than that meter's dotted-half beat).
/// </remarks>
internal sealed class BeamDetector
{
    /// <summary>
    /// Detects all beam groups in a score, across every voice.
    /// </summary>
    /// <remarks>
    /// Automatic beaming never crosses voices — each voice groups its own notes.
    /// Inside a <c>voice { }</c> span the stem direction is FORCED by voice (voice 1
    /// up, voice 2 down), mirroring the renderer's
    /// <see cref="VoiceDefaults.GetDefaultStemUpAt"/>, so a lower voice's beam sits
    /// below its notes. Measures the span does not reach keep the position-based
    /// direction — the forcing lives and dies with the span, so a monophonic section
    /// elsewhere in the part is untouched.
    /// LILYPOND-REF: lily/auto-beam-engraver.cc — one Beam per Voice context.
    /// LILYPOND-REF: scm/music-functions.scm:1042-1057 voicify-sublist / make-voice-props-set
    ///   — each <c>\\</c> sublist gets its own Voice context with the property set at its head.
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
            int voiceIndex = v;
            all.AddRange(DetectBeamGroups(
                score.Voices[v], score.TimeSignature, voiceTuplets,
                voiceIndex: v,
                forceStemUpAt: mi => VoiceDefaults.GetDefaultStemUpAt(score.Voices, voiceIndex, mi)));
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
        int voiceIndex = 0, Func<int, bool?>? forceStemUpAt = null)
    {
        var beamGroups = new List<BeamGroup>();
        var consumed = new HashSet<(int measureIndex, int itemIndex)>();

        // The two ENDS of each tuplet span, which a beam that runs through one must not hang
        // a beamlet out of: LilyPond keeps such a stem's flag CENTER and then clamps its
        // outward count to the neighbour's.
        // LILYPOND-REF: lily/beaming-pattern.cc:524-540 at_span_start / at_span_stop.
        //
        // ⚠️ THESE ARE THE ONLY THING A TUPLET DOES TO A BEAM. Two further sets used to live
        // here — one that ended a beam at every tuplet edge, one that suppressed the beat cut
        // inside a tuplet — and both were LILYSHARP-OWN inventions, falsified by measurement
        // (ledger beam.grouping.sixteenth-triplets.groups: LilyPond beams sixteenth triplets
        // in four groups of six, straight across the edge between two tuplets, where Lily#
        // drew eight of three; beam.grouping.offbeat-triplet.first-group: LilyPond runs one
        // beam of five through both edges of an off-beat triplet). What ends a beam between a
        // triplet and the plain eighths after it is the exception LOOKUP changing with the
        // run's shortest duration, which the one-pass check below now performs.
        var tupletStarts = new HashSet<(int measureIndex, int itemIndex)>();
        var tupletStops = new HashSet<(int measureIndex, int itemIndex)>();
        if (!tupletBrackets.IsDefaultOrEmpty)
        {
            foreach (var bracket in tupletBrackets)
            {
                tupletStarts.Add((bracket.MeasureIndex, bracket.StartNoteIndex));
                tupletStops.Add((bracket.MeasureIndex, bracket.EndNoteIndex));
            }
        }

        // Pass 1: cross-measure manual beams.
        DetectCrossMeasureManualBeams(voice, timeSignature, beamGroups, consumed,
            tupletStarts, tupletStops, voiceIndex, forceStemUpAt);

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
            DetectBeamGroupsInMeasure(measure, measureIndex, effectiveTimeSig, beamGroups, consumed, tupletStarts, tupletStops, voiceIndex, forceStemUpAt);
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
        Func<int, bool?>? forceStemUpAt = null)
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
                    consumed, tupletStarts, tupletStops, voiceIndex, forceStemUpAt);
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
        Func<int, bool?>? forceStemUpAt = null)
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
            // A pickup measure starts mid-bar (see MeasureStartPosition) — seeding it at
            // zero here would also skew every following measure's moment, since the
            // cross-measure moments below add whole periods per measure.
            Fraction positionInMeasure = MeasureStartPosition(
                measure, BeamingPattern.Options.For(timeSignature));
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
        // The beam is asked where it STARTS — one beam has one direction.
        bool stemUp = forceStemUpAt?.Invoke(startMeasure) ?? DefaultBeamStemUp(members);
        for (int i = 0; i < members.Count; i++)
        {
            var m = members[i];
            members[i] = new BeamMember(
                m.Item, m.BeamCount, m.BeamCountLeft, m.BeamCountRight,
                m.StaffPosition, m.ItemIndex,
                // A stem the writer turned keeps its own side (beam.cc:946-956).
                memberStemUp: ForcedStemUpOf(m) ?? stemUp,
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

    /// <summary>
    /// The automatic beams of one measure — LilyPond's auto-beam engraver, walked once.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/auto-beam-engraver.cc:336-407 Auto_beam_engraver::handle_current_stem
    /// — the shape of this loop, stem by stem: keep the SHORTEST duration seen so far; ask
    /// whether the beam must end here (with that shortest); ask whether one may begin here
    /// (with this note's own duration); add the stem; and if the shortest just got shorter,
    /// recheck the boundaries already passed.
    /// <para>
    /// ⚠️ THIS REPLACED TWO PASSES AND COULD NOT HAVE BEEN SPLIT OUT OF THEM. Lily# used to
    /// cut at every beat and then merge back the runs that were all eighths, which can only
    /// produce groups COARSER than the beat. Half of LilyPond's table asks for FINER ones —
    /// 6/4 and 4/2 beam sixteenths by the quarter against beats of a dotted half and a half
    /// (ledger beam.grouping.six-four-sixteenths, four-two-sixteenths), 2/2 and 3/2 do the
    /// same to thirty-seconds — so the merge, the eighth-only exception length it consulted,
    /// and the two tuplet guards that were papering over the difference all went together.
    /// </para>
    /// </remarks>
    /// <summary>
    /// The beat position a measure's FIRST item stands on. Zero for an ordinary
    /// measure; a PICKUP starts mid-bar, at the meter's period minus the pickup's
    /// own length — so the beat structure its stems are checked against is the
    /// bar's TAIL, not its head (a 4/8 pickup in 6/8 beams 1+3, not 3+1 —
    /// LP regression auto-beam-partial.ly, the book that found this seeded at zero).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: ly/music-functions-init.ly:1697-1705 — <c>\partial dur</c> is a
    ///   PartialSet on Timing; lily/timing-translator.cc handles it by setting
    ///   <c>measurePosition</c> to <c>−dur</c>, and the auto-beam engraver's boundary
    ///   checks read that position — <c>−dur ≡ period − dur</c> under the modular
    ///   arithmetic AutoBeamCheck applies (EuclideanRemainder over the period).
    /// </remarks>
    private Fraction MeasureStartPosition(Measure measure, BeamingPattern.Options options)
    {
        if (!measure.IsPickup)
            return Fraction.Zero;
        var content = Fraction.Zero;
        foreach (var item in measure.Items)
            content += GetDuration(item);
        return content < options.Period ? options.Period - content : Fraction.Zero;
    }

    private void DetectBeamGroupsInMeasure(
        Measure measure,
        int measureIndex,
        TimeSignature timeSig,
        List<BeamGroup> beamGroups,
        HashSet<(int, int)>? consumed = null,
        HashSet<(int, int)>? tupletStarts = null,
        HashSet<(int, int)>? tupletStops = null,
        int voiceIndex = 0,
        Func<int, bool?>? forceStemUpAt = null)
    {
        // The beat grid and the meter's beamExceptions. Derived ONCE per measure rather than
        // per beam group: they depend only on the meter, and building them allocates.
        var beamOptions = BeamingPattern.Options.For(timeSig);

        // Phase 0: Detect manual beam groups (c8[ d e f])
        var manualRanges = DetectManualBeamGroups(measure, measureIndex, beamOptions, beamGroups,
            tupletStarts, tupletStops, voiceIndex, forceStemUpAt);

        var stems = new List<(MusicItem item, int index, Fraction startPos)>();
        // LILYPOND-REF: lily/auto-beam-engraver.cc:241 junk_beam / :278 end_beam — shortest_dur_
        // is a quarter whenever no beam is being built, which is why the first stem of a beam
        // never sets it: nothing beamable is that long, so the SECOND stem is what makes the
        // lookup real, and recheck_beam is what goes back for the boundary the first one passed.
        var shortest = Fraction.Quarter;
        Fraction position = MeasureStartPosition(measure, beamOptions);

        // LILYPOND-REF: lily/auto-beam-engraver.cc:252-279 end_beam — a beam of fewer than two
        // stems is junked, not typeset. This is the ONLY place that decides a lone note is a
        // flagged note rather than a beam.
        void EndBeam()
        {
            if (stems.Count >= 2)
                beamGroups.Add(CreateBeamGroup(stems, measureIndex, beamOptions,
                    tupletStarts, tupletStops, voiceIndex, forceStemUpAt));
            stems.Clear();
            shortest = Fraction.Quarter;
        }

        // LILYPOND-REF: lily/auto-beam-engraver.cc:409-459 recheck_beam — the beam's own
        // boundaries are re-asked once a shorter note has changed which exception answers. A
        // split typesets the head and keeps the tail, then starts over from the beginning,
        // because the tail may break again. ⚠️ shortest_dur_ SURVIVES the split (LilyPond
        // saves and restores it around end_beam, :434-451): the tail still holds the short
        // note that caused the recheck.
        void RecheckBeam()
        {
            for (int i = 0; i + 1 < stems.Count; )
            {
                var endOfStem = stems[i].startPos + GetDuration(stems[i].item);
                if (!AutoBeamCheck.EndsBeam(endOfStem, shortest, beamOptions))
                {
                    i++;
                    continue;
                }

                var head = stems.GetRange(0, i + 1);
                var tail = stems.GetRange(i + 1, stems.Count - (i + 1));
                if (head.Count >= 2)
                    beamGroups.Add(CreateBeamGroup(head, measureIndex, beamOptions,
                        tupletStarts, tupletStops, voiceIndex, forceStemUpAt));
                stems.Clear();
                stems.AddRange(tail);
                i = 0;
            }
        }

        for (int i = 0; i < measure.Items.Length; i++)
        {
            var item = measure.Items[i];
            var duration = GetDuration(item);

            // A rest, a note too long to be beamed, and a stem that already carries a beam of
            // its own (manual, or a cross-measure pair claimed by the first pass) all end the
            // beam being built.
            // LILYPOND-REF: lily/auto-beam-engraver.cc:324-328 acknowledge_rest (force_end_),
            //   :350-355 the head_count / get_beam test, :368-373 duration_log <= 2.
            if (IsInManualRange(i, manualRanges) ||
                (consumed != null && consumed.Contains((measureIndex, i))) ||
                !IsBeamable(item))
            {
                EndBeam();
                position = position + duration;
                continue;
            }

            // LILYPOND-REF: lily/auto-beam-engraver.cc:385-390 in handle_current_stem — a new
            // shortest duration is remembered in shortest_dur_ and marks the beam for rechecking.
            bool recheckNeeded = false;
            if (duration < shortest)
            {
                shortest = duration;
                recheckNeeded = true;
            }

            // LILYPOND-REF: lily/auto-beam-engraver.cc:392-395 consider_end / consider_begin —
            // "end should be based on shortest_dur_, begin should be based on current duration".
            if (stems.Count > 0 && AutoBeamCheck.EndsBeam(position, shortest, beamOptions))
                EndBeam();

            if (stems.Count == 0 && !AutoBeamCheck.StartsBeam(position, duration, beamOptions))
            {
                position = position + duration;
                continue;
            }

            stems.Add((item, i, position));
            if (recheckNeeded)
                RecheckBeam();

            position = position + duration;
        }

        // LILYPOND-REF: lily/auto-beam-engraver.cc:462-485 process_acknowledged — currentBarLine
        // forces the beam to end. Lily# builds one measure at a time, so the bar line is here.
        EndBeam();
    }

    private BeamGroup CreateBeamGroup(List<(MusicItem item, int index, Fraction startPos)> group, int measureIndex,
        BeamingPattern.Options beamOptions,
        HashSet<(int, int)>? tupletStarts = null, HashSet<(int, int)>? tupletStops = null,
        int voiceIndex = 0, Func<int, bool?>? forceStemUpAt = null)
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
        bool? forcedStemUp = forceStemUpAt?.Invoke(measureIndex);
        bool stemUp = forcedStemUp ?? DefaultBeamStemUp(members);

        // Check if first note has feathered beam direction
        // LILYPOND-REF: beam.cc:1039-1082 grow-direction
        int growDirection = group[0].item is NoteItem firstNote ? firstNote.FeatherDirection : 0;

        // Auto-knee detection: knee when the union of (widened) head extents
        // leaves an interior gap larger than auto-knee-gap + the beam stack.
        // LILYPOND-REF: beam.cc:968-1056 consider_auto_knees
        // LILYPOND-REF: define-grobs.scm:476 auto-knee-gap = 5.5
        // A forced-direction (polyphonic) voice never knees — every stem stays on
        // the voice's side, so auto-knee only runs in a neutral single voice.
        double? kneeGapCenter = forcedStemUp is null ? AutoKneeGapCenter(members) : null;

        for (int i = 0; i < members.Count; i++)
        {
            var m = members[i];
            // Knee: each stem points INTO the gap — UP when its head sits
            // below the gap center, DOWN above (beam.cc:1047-1049). Without a
            // knee, every member takes the group direction — EXCEPT one the writer
            // turned, which keeps its own side: LilyPond stamps the group's direction
            // only onto stems that do not already carry one.
            // LILYPOND-REF: lily/beam.cc:946-956 Beam::set_stem_directions.
            bool memberUp = ForcedStemUpOf(m) ?? (kneeGapCenter is { } gapCenter
                ? (m.HeadPositionMin + m.HeadPositionMax) / 2.0 < gapCenter
                : stemUp);
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

    /// <summary>
    /// A chord's single "staff position": the mean of its heads. See the warning on
    /// <see cref="BeamMember.StaffPosition"/> — the beam geometry does not read this.
    /// </summary>
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

        // ⚠️ A STEM THE WRITER TURNED (@stemUp / @stemDown) TURNS THE RULE OFF. LilyPond
        // sets force_dir while tallying — a stem whose `direction` property is already a
        // Direction contributes THAT rather than its pitch-derived one — and then skips the
        // extremes check entirely, so the beam is decided by the VOTE below instead of by
        // the farthest head. This is what makes `\stemDown` turn a whole beam over.
        // LILYPOND-REF: lily/beam.cc:894-905 Beam::get_default_dir (force_dir), :918 (the gate).
        bool forceDir = false;
        foreach (var m in members)
        {
            if (ForcedStemUpOf(m) is not null) { forceDir = true; break; }
        }

        // The farther extreme wins.
        // LILYPOND-REF: lily/beam.cc:918-924 Beam::get_default_dir (extremes check).
        if (!forceDir)
        {
            if (Math.Abs(extremeUp) > -extremeDown) return false; // DOWN
            if (extremeUp < -extremeDown) return true;            // UP
        }

        // Tie: per-stem majority vote by each stem's own natural direction.
        // A stem whose head is exactly on (or symmetric about) the middle line has no
        // natural direction, so LP counts it with neutral-direction = DOWN: its
        // Stem::calc_default_direction is CENTER, and get_default_dir falls back to
        // neutral-direction before tallying. Counting it UP flipped whole beams centred
        // on the line (e.g. g'..d'' straddling the middle equally: LP=down, us=up), so
        // the test is `>=`, not `>`.
        // LILYPOND-REF: lily/beam.cc:895-916 (per-stem default/neutral direction tally),
        // LILYPOND-REF: lily/beam.cc:928 (count[UP] - count[DOWN] majority vote).
        // Each stem also contributes HOW FAR it reaches on its own side, which is what
        // breaks a tied vote: LilyPond accumulates
        // `total[dir] += max (int (-dir * head_positions (s)[-dir]), 0)` — the position of
        // the head the stem STARTS from, i.e. the far one, and never below zero.
        // LILYPOND-REF: lily/beam.cc:913-914 in get_default_dir, over head_positions.
        // ⚠️ A FORCED STEM VOTES ITS OWN WAY, not its pitch's. LilyPond reads
        // `get_property_data (s, "direction")` first and only falls back to
        // `default-direction` when nothing set it — the same branch that raises force_dir.
        // The DISTANCE it contributes is still read off its heads, on the side its
        // (forced) direction starts from.
        // LILYPOND-REF: lily/beam.cc:895-916 Beam::get_default_dir (the per-stem tally).
        int upVotes = 0, downVotes = 0, totalUp = 0, totalDown = 0;
        foreach (var m in members)
        {
            int mUp = Math.Max(0, m.HeadPositionMax);
            int mDown = Math.Min(0, m.HeadPositionMin);
            bool voteUp = ForcedStemUpOf(m) ?? !(Math.Abs(mUp) >= -mDown);
            if (!voteUp)
            {
                downVotes++;
                totalDown += Math.Max(m.HeadPositionMax, 0);
            }
            else
            {
                upVotes++;
                totalUp += Math.Max(-m.HeadPositionMin, 0);
            }
        }
        if (upVotes != downVotes) return upVotes > downVotes;

        // Tied vote: compare how far the two sides reach ON AVERAGE, then in total, and
        // only then fall back to the neutral direction.
        // LILYPOND-REF: lily/beam.cc:930-937 get_default_dir's tail —
        //   `total[UP]/count[UP] - total[DOWN]/count[DOWN]`
        //   (both sides non-empty), else `total[UP] - total[DOWN]`, else neutral-direction.
        // ⚠️ INTEGER division, because LilyPond's total_ and count_ are Drul_array<int>:
        //   two sides reaching 5 and 4 average the same there and fall through to the sum.
        // ⚠️ This replaced the SUM OF THE MEMBERS' StaffPosition — a chord's arithmetic mean,
        //   which is not a quantity LilyPond computes. MEASURED on the pair
        //   `<d' f'>8 <c'' g''>`: extremes tie at ±5 and the vote ties 1-1, LilyPond answers
        //   DOWN (both averages 5, both totals 5, so neutral wins) and the mean sum said −1
        //   and answered UP.
        if (upVotes > 0 && downVotes > 0)
        {
            int avgDiff = totalUp / upVotes - totalDown / downVotes;
            if (avgDiff != 0) return avgDiff > 0;
        }
        if (totalUp != totalDown) return totalUp > totalDown;
        return false; // neutral-direction = DOWN
    }

    /// <summary>
    /// The stem direction the writer asked for on this member (<c>@stemUp</c> /
    /// <c>@stemDown</c>), or null when nothing did.
    /// </summary>
    /// <remarks>
    /// ⚠️ READS <c>ForcedStemUp</c> AND NOT <c>StemUp</c>, which is the whole point: by the
    /// time a beam has been resolved <c>StemUp</c> answers the GROUP's direction for every
    /// member, so asking it here would make the beam read its own output.
    /// LILYPOND-REF: lily/beam.cc:898-903 — <c>get_property_data (s, "direction")</c>, the
    /// property nothing has written yet, against the <c>default-direction</c> callback.
    /// </remarks>
    private static bool? ForcedStemUpOf(BeamMember m) => m.Item switch
    {
        NoteItem n => n.ForcedStemUp,
        ChordItem c => c.ForcedStemUp,
        _ => null,
    };

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
        Func<int, bool?>? forceStemUpAt = null)
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

                // Collect beamable items in this range. A pickup measure's positions
                // start mid-bar (MeasureStartPosition) — the manual group's boundaries
                // are the writer's, but its BEAMLET subdivision still reads the beat grid.
                var group = new List<(MusicItem item, int index, Fraction startPos)>();
                Fraction pos = MeasureStartPosition(measure, beamOptions);
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
                        tupletStarts, tupletStops, voiceIndex, forceStemUpAt));
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






