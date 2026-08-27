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
    /// <para>
    /// ⑶ beamdirs (HANDOFF §1): with a <paramref name="memo"/>, the per-measure pass
    /// replays a previous detection of a content-identical measure instead of walking it
    /// again. SOUNDNESS — a single-measure detection is a pure function of exactly three
    /// inputs, all folded into the memo key: ⑴ the measure's position-independent
    /// detection inputs (<see cref="AddDetectionInputs"/> — the hand fold of everything
    /// <see cref="DetectBeamGroupsInMeasure"/>, <see cref="MeasureStartPosition"/> and
    /// <see cref="CreateBeamGroup"/> read off the measure; its remarks carry the field
    /// list, the cost argument and the Debug drift net), ⑵ the meter in EFFECT at the
    /// measure (the same effective-signature chain the live loop tracks — also what
    /// <see cref="BuildTupletSpans"/> prices the spans with), and ⑶ the tuplet brackets
    /// addressed to this measure index (content, in list order — the spans' other input).
    /// Auto beams never cross a bar line (<c>EndBeam()</c> runs at every measure end), so
    /// no other measure reaches into the result. The two NON-local regimes are gated out
    /// rather than keyed: a measure any cross-measure manual pair touches (the
    /// <c>consumed</c> set) detects live, and a caller supplying <paramref name="voiceIndex"/>
    /// ≠ 0 or a <paramref name="forceStemUpAt"/> (the Score entry's multi-voice fan — the
    /// direction forcing and the stored <c>VoiceIndex</c> would go stale) bypasses the memo
    /// entirely. Replayed groups are re-based to the live measure index; their members are
    /// index-addressed (<c>MeasureIndex</c> −1 = the group's), so nothing else in them is
    /// positional. The stale <c>Member.Item</c> references a stored group carries are never
    /// read by the bake (<c>ResolveBeamStemDirections</c> addresses the LIVE measure by
    /// <c>ItemIndex</c>); the groups themselves are discarded after it.
    /// </para>
    /// <para>
    /// ⚠️ BeamId stays OUT of the memo on purpose: identities are numbered by the bake, in
    /// group order, and the memo preserves that order exactly (cross-measure groups first,
    /// then per-measure groups measure by measure, manual before automatic within one) —
    /// which is what keeps a memo-served collect byte-identical to a from-scratch one.
    /// Guards: the incremental==full nets plus the beam-memo nets in
    /// <c>IncrementalCompilerTests</c> and the replay-vs-live equivalence net in
    /// <c>BeamDetectionMemoTests</c>.
    /// </para>
    /// </remarks>
    public ImmutableArray<BeamGroup> DetectBeamGroups(Voice voice, TimeSignature timeSignature,
        ImmutableArray<TupletBracketItem> tupletBrackets = default,
        int voiceIndex = 0, Func<int, bool?>? forceStemUpAt = null,
        BeamDetectionMemo? memo = null)
    {
        var beamGroups = new List<BeamGroup>();
        var consumed = new HashSet<(int measureIndex, int itemIndex)>();

        // Every tuplet bracket resolved to its TIME SPAN — what LilyPond's beam engraver
        // hands the pattern as Tuplet_description. A beam that runs through one keeps the
        // boundary stems' flags CENTER and clamps their outward beamlets to the neighbour's
        // (lily/beaming-pattern.cc:524-540 at_span_start / at_span_stop), and ranks the
        // stems INSIDE one in WRITTEN proportions through the span stack
        // (BeamingPattern.SetRhythmicImportance).
        //
        // ⚠️ A TUPLET STILL NEVER BOUNDS A BEAM. Two sets used to live here — one that ended
        // a beam at every tuplet edge, one that suppressed the beat cut inside a tuplet —
        // and both were LILYSHARP-OWN inventions, falsified by measurement
        // (ledger beam.grouping.sixteenth-triplets.groups: LilyPond beams sixteenth triplets
        // in four groups of six, straight across the edge between two tuplets, where Lily#
        // drew eight of three; beam.grouping.offbeat-triplet.first-group: LilyPond runs one
        // beam of five through both edges of an off-beat triplet). What ends a beam between a
        // triplet and the plain eighths after it is the exception LOOKUP changing with the
        // run's shortest duration, which the one-pass check below now performs.
        var tupletSpans = BuildTupletSpans(voice, timeSignature, tupletBrackets);

        // Pass 1: cross-measure manual beams.
        DetectCrossMeasureManualBeams(voice, timeSignature, beamGroups, consumed,
            tupletSpans, voiceIndex, forceStemUpAt);

        // Pass 2: single-measure detection (skipping consumed items). The beam
        // grouping depends on the meter, which a mid-piece \time changes — track
        // the EFFECTIVE time signature per measure (a TimeSignatureChangeItem
        // carries the new meter from its measure onward) rather than beaming every
        // measure by the initial one (which beamed e.g. a 6/8 measure as 3/4).
        // LILYPOND-REF: lily/beaming-pattern.cc — beaming follows the current
        //   timeSignatureFraction / beatStructure.
        //
        // Memo eligibility (see the method remarks): the collect probe's shape only.
        bool memoEligible = memo != null && voiceIndex == 0 && forceStemUpAt == null;
        var bracketHashes = memoEligible ? BracketHashesByMeasure(tupletBrackets) : null;
        HashSet<int>? crossMeasureTouched = null;
        if (memoEligible && consumed.Count > 0)
        {
            crossMeasureTouched = new HashSet<int>();
            foreach (var (mi, _) in consumed)
                crossMeasureTouched.Add(mi);
        }

        var effectiveTimeSig = timeSignature;
        for (int measureIndex = 0; measureIndex < voice.Measures.Length; measureIndex++)
        {
            var measure = voice.Measures[measureIndex];
            foreach (var item in measure.Items)
                if (item is TimeSignatureChangeItem tsc)
                    effectiveTimeSig = tsc.NewTime;

            if (memoEligible
                && (crossMeasureTouched == null || !crossMeasureTouched.Contains(measureIndex)))
            {
                long bracketsHash = 0;
                bracketHashes?.TryGetValue(measureIndex, out bracketsHash);
                long key = MeasureMemoKey(measure, effectiveTimeSig, bracketsHash);
                if (memo!.TryGet(key, out var stored))
                {
#if DEBUG
                    // Every Debug hit re-detects live and compares — the drift net over
                    // AddDetectionInputs' hand-rolled read set (see its remarks).
                    VerifyReplayAgainstLiveDetection(stored, measure, measureIndex,
                        effectiveTimeSig, consumed, tupletSpans, voiceIndex, forceStemUpAt);
#endif
                    // Re-base to the live measure index; everything else in a stored
                    // per-measure group is measure-local (members carry the −1 sentinel).
                    foreach (var g in stored)
                        beamGroups.Add(g.MeasureIndex == measureIndex ? g : new BeamGroup(
                            g.Members, measureIndex, g.StartIndex, g.StemUp,
                            g.GrowDirection, g.VoiceIndex, g.RestStems));
                    continue;
                }
                int before = beamGroups.Count;
                DetectBeamGroupsInMeasure(measure, measureIndex, effectiveTimeSig, beamGroups,
                    consumed, tupletSpans, voiceIndex, forceStemUpAt);
                var slice = ImmutableArray.CreateBuilder<BeamGroup>(beamGroups.Count - before);
                for (int g = before; g < beamGroups.Count; g++)
                    slice.Add(beamGroups[g]);
                memo.Store(key, slice.MoveToImmutable());
                continue;
            }

            DetectBeamGroupsInMeasure(measure, measureIndex, effectiveTimeSig, beamGroups, consumed, tupletSpans, voiceIndex, forceStemUpAt);
        }

        return beamGroups.ToImmutableArray();
    }

    /// <summary>The memo key of one measure's detection input (see the memo remarks on
    /// <see cref="DetectBeamGroups(Voice, TimeSignature, ImmutableArray{TupletBracketItem}, int, Func{int, bool?}?, BeamDetectionMemo?)"/>):
    /// the detection-input fold + the effective meter + the measure's tuplet brackets.</summary>
    private static long MeasureMemoKey(
        Measure measure, TimeSignature effectiveTimeSig, long bracketsHash)
    {
        var hc = new MeasureContentKey.Hash64();
        AddDetectionInputs(ref hc, measure);
        hc.Add(effectiveTimeSig);
        hc.Add(bracketsHash);
        return hc.ToHashCode();
    }

    /// <summary>
    /// Folds exactly the measure fields a single-measure detection reads — by hand, not
    /// through <see cref="MeasureContentKey.Of"/>'s reflection fold, because the key must
    /// cost far less than the walk it spares: the reflection fold (which boxes every
    /// property of every item) measured 10.5-11.6 ms per 1000 bars against a live
    /// detection of 7-17 ms (session 154, Release), cancelling the memo entirely.
    /// </summary>
    /// <remarks>
    /// The read set, field by field (each line names its reader in this class):
    /// <c>IsPickup</c> (<see cref="MeasureStartPosition"/>); per item, in sequence: the
    /// item's KIND (a non-beamable item ends the beam and holds an index), sounding
    /// <c>Duration</c> (<see cref="GetDuration"/> — the position walk), <c>BaseDuration</c>
    /// (<see cref="IsBeamable"/>/<see cref="GetBeamCount"/>/<see cref="IsBeamedRest"/>),
    /// <c>HasBeamStart</c>/<c>HasBeamEnd</c> (manual ranges), <c>ForcedStemUp</c>
    /// (<see cref="ForcedStemUpOf"/>), <c>TremoloPairBeams</c>, head staff positions
    /// (<see cref="GetStaffPosition"/>/<see cref="GetHeadRange"/> — direction, knees),
    /// <c>FeatherDirection</c> (grow direction), a rest's <c>IsSpacer</c>/
    /// <c>IsMultiMeasure</c>, and a mid-measure \time item's <c>NewTime</c> (the
    /// effective-signature update).
    /// <para>
    /// ⚠️ DRIFT GUARD: a hand-rolled read set can silently fall behind the readers
    /// (HANDOFF §2C's skip-list lesson), and a missing field here is a FALSE REUSE. Two
    /// nets stand on it: this list lives beside the readers it mirrors, and — the real
    /// teeth — every DEBUG-build memo hit re-detects the measure live and compares the
    /// whole bake-visible surface (<c>VerifyReplayAgainstLiveDetection</c>), so the
    /// entire Debug test suite doubles as an exhaustive replay-equivalence net. Release
    /// pays neither.
    /// ⚠️ That name is <c>&lt;c&gt;</c> and not a <c>cref</c> ON PURPOSE: the method is
    /// inside <c>#if DEBUG</c>, and the XML doc file is generated by the RELEASE build
    /// (LilySharp.Core.csproj) — where the symbol does not exist. A cref here resolves in
    /// the editor and fails only in the build that ships the docs.
    /// </para>
    /// </remarks>
    private static void AddDetectionInputs(ref MeasureContentKey.Hash64 hc, Measure measure)
    {
        hc.Add(measure.IsPickup);
        hc.Add(measure.Items.Length);
        foreach (var item in measure.Items)
        {
            switch (item)
            {
                case NoteItem n:
                    hc.Add(1);
                    AddFraction(ref hc, n.Duration);
                    AddFraction(ref hc, n.BaseDuration);
                    hc.Add(n.HasBeamStart);
                    hc.Add(n.HasBeamEnd);
                    hc.Add(n.ForcedStemUp is { } nf ? (nf ? 2 : 1) : 0);
                    hc.Add(n.TremoloPairBeams);
                    hc.Add(n.StaffPosition);
                    hc.Add(n.FeatherDirection);
                    break;
                case ChordItem c:
                    hc.Add(2);
                    AddFraction(ref hc, c.Duration);
                    AddFraction(ref hc, c.BaseDuration);
                    hc.Add(c.HasBeamStart);
                    hc.Add(c.HasBeamEnd);
                    hc.Add(c.ForcedStemUp is { } cf ? (cf ? 2 : 1) : 0);
                    hc.Add(c.TremoloPairBeams);
                    hc.Add(c.Notes.Length);
                    foreach (var note in c.Notes)
                        hc.Add(note.StaffPosition);
                    break;
                case RestItem r:
                    hc.Add(3);
                    AddFraction(ref hc, r.Duration);
                    AddFraction(ref hc, r.BaseDuration);
                    hc.Add(r.IsSpacer);
                    hc.Add(r.IsMultiMeasure);
                    break;
                case TimeSignatureChangeItem tsc:
                    hc.Add(4);
                    hc.Add(tsc.NewTime);
                    break;
                default:
                    // Zero duration, not beamable — the KIND still matters: it ends a
                    // beam being built and occupies an item index the groups address.
                    hc.Add(5);
                    hc.Add(item.GetType());
                    break;
            }
        }
    }

    private static void AddFraction(ref MeasureContentKey.Hash64 hc, Fraction f)
    {
        hc.Add(f.Numerator);
        hc.Add(f.Denominator);
    }

#if DEBUG
    /// <summary>Debug-only teeth of the memo's drift guard: a hit re-runs the live
    /// per-measure detection and compares the whole bake-visible surface against the
    /// stored groups. A mismatch means <see cref="AddDetectionInputs"/> fell behind a
    /// reader (a false reuse) — fail loudly, right here, in whichever suite test drove
    /// the compile. Release builds never pay this.</summary>
    private void VerifyReplayAgainstLiveDetection(
        ImmutableArray<BeamGroup> stored, Measure measure, int measureIndex,
        TimeSignature effectiveTimeSig, HashSet<(int, int)>? consumed,
        IReadOnlyDictionary<int, List<TupletSpan>>? tupletSpans,
        int voiceIndex, Func<int, bool?>? forceStemUpAt)
    {
        var live = new List<BeamGroup>();
        DetectBeamGroupsInMeasure(measure, measureIndex, effectiveTimeSig, live, consumed,
            tupletSpans, voiceIndex, forceStemUpAt);
        bool Mismatch() // any difference on the surface the bake consumes
        {
            if (live.Count != stored.Length)
                return true;
            for (int i = 0; i < live.Count; i++)
            {
                var a = live[i];
                var b = stored[i];
                if (a.StartIndex != b.StartIndex || a.StemUp != b.StemUp
                    || a.GrowDirection != b.GrowDirection || a.VoiceIndex != b.VoiceIndex
                    || a.Members.Length != b.Members.Length
                    || !a.RestStems.SequenceEqual(b.RestStems))
                    return true;
                for (int m = 0; m < a.Members.Length; m++)
                {
                    var am = a.Members[m];
                    var bm = b.Members[m];
                    if (am.ItemIndex != bm.ItemIndex || am.MeasureIndex != bm.MeasureIndex
                        || am.MemberStemUp != bm.MemberStemUp || am.BeamCount != bm.BeamCount
                        || am.BeamCountLeft != bm.BeamCountLeft
                        || am.BeamCountRight != bm.BeamCountRight
                        || am.StaffPosition != bm.StaffPosition
                        || am.HeadPositionMin != bm.HeadPositionMin
                        || am.HeadPositionMax != bm.HeadPositionMax
                        || am.TargetStaffIndex != bm.TargetStaffIndex)
                        return true;
                }
            }
            return false;
        }
        if (Mismatch())
            throw new InvalidOperationException(
                $"BeamDetectionMemo replay diverged from live detection at measure {measureIndex}: "
                + "AddDetectionInputs' read set has fallen behind a detection reader (false reuse).");
    }
#endif

    /// <summary>Per-measure content fold of the voice's tuplet brackets, in list order —
    /// the whole bracket minus its positional fields (measure index, source offset), the
    /// same exclusion set the side-table content keys use. Over-sensitive on purpose: a
    /// bracket the span builder would drop as foreign still moves the key (a missed reuse,
    /// never a false one).</summary>
    private static Dictionary<int, long>? BracketHashesByMeasure(
        ImmutableArray<TupletBracketItem> brackets)
    {
        if (brackets.IsDefaultOrEmpty)
            return null;
        var accs = new Dictionary<int, MeasureContentKey.Hash64>();
        foreach (var b in brackets)
        {
            if (!accs.TryGetValue(b.MeasureIndex, out var h))
                h = new MeasureContentKey.Hash64();
            h.Add(MeasureContentKey.HashSideContent(b));
            accs[b.MeasureIndex] = h;
        }
        var result = new Dictionary<int, long>(accs.Count);
        foreach (var kv in accs)
            result[kv.Key] = kv.Value.ToHashCode();
        return result;
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
        IReadOnlyDictionary<int, List<TupletSpan>>? tupletSpans = null,
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
                    consumed, tupletSpans, voiceIndex, forceStemUpAt);
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
        IReadOnlyDictionary<int, List<TupletSpan>>? tupletSpans = null,
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
                // A rest inside the bracket rides the beam as an invisible stem — same as
                // the per-measure pass. The bracket's own notes are the range's ends.
                else if (IsBeamedRest(item)
                    && !(mi == startMeasure && ii == startItem)
                    && !(mi == endMeasure && ii == endItem))
                {
                    allEntries.Add((item, ii, positionInMeasure, mi));
                }
                positionInMeasure += GetDuration(item);
                pendingConsumed.Add((mi, ii));
            }
        }

        int visibleCount = allEntries.Count(e => e.Item is not RestItem);
        if (visibleCount < 2)
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
        var beamlets = BeamletCounts(moments, options, tupletSpans);

        // The stems, gathered before any member exists — the same one-construction shape
        // CreateBeamGroup uses, and for the same reason (see <see cref="StemCandidate"/>).
        var stems = new StemCandidate[allEntries.Count];
        int stemCount = 0;
        List<BeamRestStem>? restStems = null;
        for (int i = 0; i < allEntries.Count; i++)
        {
            var (item, itemIdx, _, mi) = allEntries[i];

            if (item is RestItem restItem)
            {
                (restStems ??= new List<BeamRestStem>()).Add(new BeamRestStem(
                    itemIdx, BeforeMember: stemCount, beamlets[i].Left, beamlets[i].Right,
                    NoteValue: (int)restItem.BaseDuration.Denominator,
                    MeasureIndex: mi,
                    PrePositioned: restItem.StaffPosition is not null));
                continue;
            }

            var headRange = GetHeadRange(item);
            stems[stemCount++] = new StemCandidate(
                item, itemIdx, mi, GetBeamCount(item),
                beamlets[i].Left, beamlets[i].Right, GetStaffPosition(item),
                headRange.Min, headRange.Max);
        }
        var visible = new ReadOnlySpan<StemCandidate>(stems, 0, stemCount);

        // A polyphonic voice forces its direction; otherwise the farthest head decides.
        // The beam is asked where it STARTS — one beam has one direction.
        bool stemUp = forceStemUpAt?.Invoke(startMeasure) ?? DefaultBeamStemUp(visible);

        var members = ImmutableArray.CreateBuilder<BeamMember>(stemCount);
        for (int i = 0; i < stemCount; i++)
        {
            var c = visible[i];
            // clip-edges on the outer VISIBLE stems, as in CreateBeamGroup.
            // LILYPOND-REF: lily/beam.cc:1264-1268 Beam::set_beaming.
            members.Add(new BeamMember(
                c.Item, c.BeamCount,
                i == 0 ? 0 : c.BeamletLeft,
                i == stemCount - 1 ? 0 : c.BeamletRight,
                c.StaffPosition, c.ItemIndex,
                // A stem the writer turned keeps its own side (beam.cc:946-956).
                memberStemUp: ForcedStemUpOf(c.Item) ?? stemUp,
                measureIndex: c.MeasureIndex,
                headPositionMin: c.HeadMin,
                headPositionMax: c.HeadMax));
        }

        beamGroups.Add(new BeamGroup(
            members.MoveToImmutable(),
            measureIndex: startMeasure,
            startIndex: allEntries[0].Index,
            stemUp,
            growDirection: 0,
            voiceIndex: voiceIndex,
            restStems: RestStemsStandingIn(restStems, stemCount)));
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
    ///   PartialSet on Timing;
    /// LILYPOND-REF: lily/timing-translator.cc:149-160 — the handler sets
    ///   <c>measurePosition = measureLength − dur</c> (the mid-piece branch spells it
    ///   <c>mp = mlen - Rational (*dur)</c> at :158; the at-start case in
    ///   listen_partial carries the same quantity), and the auto-beam engraver's
    ///   boundary checks read that position.
    /// ⚠️ Lily# derives the pickup's length from the measure's CONTENT sum where
    /// LilyPond reads the DECLARED <c>\partial</c> duration. MeasureBuilder.SetPartial
    /// sizes the pickup to the declaration, so the two coincide for a filled pickup;
    /// an underfilled one would seed from its real content instead.
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
        IReadOnlyDictionary<int, List<TupletSpan>>? tupletSpans = null,
        int voiceIndex = 0,
        Func<int, bool?>? forceStemUpAt = null)
    {
        // The beat grid and the meter's beamExceptions. Derived ONCE per measure rather than
        // per beam group: they depend only on the meter, and building them allocates.
        var beamOptions = BeamingPattern.Options.For(timeSig);

        // Phase 0: Detect manual beam groups (c8[ d e f])
        var manualRanges = DetectManualBeamGroups(measure, measureIndex, beamOptions, beamGroups,
            tupletSpans, voiceIndex, forceStemUpAt);

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
                    tupletSpans, voiceIndex, forceStemUpAt));
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
                        tupletSpans, voiceIndex, forceStemUpAt));
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
        IReadOnlyDictionary<int, List<TupletSpan>>? tupletSpans = null,
        int voiceIndex = 0, Func<int, bool?>? forceStemUpAt = null)
    {
        var moments = new (MusicItem Item, Fraction Moment, int Measure, int Index)[group.Count];
        for (int i = 0; i < group.Count; i++)
            moments[i] = (group[i].item, group[i].startPos, measureIndex, group[i].index);
        var beamlets = BeamletCounts(moments, beamOptions, tupletSpans);

        var stems = new StemCandidate[group.Count];
        int stemCount = 0;
        List<BeamRestStem>? restStems = null;
        for (int i = 0; i < group.Count; i++)
        {
            var (item, itemIndex, _) = group[i];

            // A rest rides the beam as an INVISIBLE stem: no member, no head, no drawn
            // stem — just its clamped counts standing in the segment walk.
            if (item is RestItem restItem)
            {
                (restStems ??= new List<BeamRestStem>()).Add(new BeamRestStem(
                    itemIndex, BeforeMember: stemCount, beamlets[i].Left, beamlets[i].Right,
                    NoteValue: (int)restItem.BaseDuration.Denominator,
                    PrePositioned: restItem.StaffPosition is not null));
                continue;
            }

            var headRange = GetHeadRange(item);
            stems[stemCount++] = new StemCandidate(
                item, itemIndex, MeasureIndex: -1, GetBeamCount(item),
                beamlets[i].Left, beamlets[i].Right, GetStaffPosition(item),
                headRange.Min, headRange.Max);
        }
        var visible = new ReadOnlySpan<StemCandidate>(stems, 0, stemCount);

        // A polyphonic voice forces its direction (voice 1 up / voice 2 down);
        // otherwise the head farthest from the middle line decides (LP get_default_dir).
        bool? forcedStemUp = forceStemUpAt?.Invoke(measureIndex);
        bool stemUp = forcedStemUp ?? DefaultBeamStemUp(visible);

        // Check if first note has feathered beam direction
        // LILYPOND-REF: beam.cc:1039-1082 grow-direction
        int growDirection = group[0].item is NoteItem firstNote ? firstNote.FeatherDirection : 0;

        // Auto-knee detection: knee when the union of (widened) head extents
        // leaves an interior gap larger than auto-knee-gap + the beam stack.
        // LILYPOND-REF: beam.cc:968-1056 consider_auto_knees
        // LILYPOND-REF: define-grobs.scm:476 auto-knee-gap = 5.5
        // A forced-direction (polyphonic) voice never knees — every stem stays on
        // the voice's side, so auto-knee only runs in a neutral single voice.
        double? kneeGapCenter = forcedStemUp is null ? AutoKneeGapCenter(visible) : null;

        var members = ImmutableArray.CreateBuilder<BeamMember>(stemCount);
        for (int i = 0; i < stemCount; i++)
        {
            var c = visible[i];
            // Knee: each stem points INTO the gap — UP when its head sits
            // below the gap center, DOWN above (beam.cc:1047-1049). Without a
            // knee, every member takes the group direction — EXCEPT one the writer
            // turned, which keeps its own side: LilyPond stamps the group's direction
            // only onto stems that do not already carry one.
            // LILYPOND-REF: lily/beam.cc:946-956 Beam::set_stem_directions.
            bool memberUp = ForcedStemUpOf(c.Item) ?? (kneeGapCenter is { } gapCenter
                ? (c.HeadMin + c.HeadMax) / 2.0 < gapCenter
                : stemUp);
            // clip-edges (default #t): the OUTER side of an outer stem carries nothing, and
            // LilyPond zeroes it after the pattern has been beamified rather than in it. Its
            // INNER side keeps the stem's own count — the pattern only ever reduces interior
            // stems, so an outer one is never chipped down to its neighbour's count. The outer
            // STEMS are the outer VISIBLE stems (a rest has no stem to clip), which is why
            // this indexes the candidates rather than the group.
            // LILYPOND-REF: lily/beam.cc:1264-1268 Beam::set_beaming.
            members.Add(new BeamMember(
                c.Item, c.BeamCount,
                i == 0 ? 0 : c.BeamletLeft,
                i == stemCount - 1 ? 0 : c.BeamletRight,
                c.StaffPosition, c.ItemIndex,
                memberStemUp: memberUp,
                measureIndex: c.MeasureIndex,
                headPositionMin: c.HeadMin,
                headPositionMax: c.HeadMax));
        }

        return new BeamGroup(
            members.MoveToImmutable(),
            measureIndex,
            group[0].index,
            stemUp,
            growDirection,
            voiceIndex,
            // A rest can only STAND IN a beam, between two visible stems; one drifting outside
            // (a degenerate bracket whose edge note was not beamable) has nothing to hang from.
            RestStemsStandingIn(restStems, stemCount));
    }

    /// <summary>The rest stems that stand BETWEEN two visible stems — the rest hang from
    /// nothing outside them. Empty (and allocation-free) for the overwhelmingly common beam
    /// that runs over no rest at all.</summary>
    private static ImmutableArray<BeamRestStem> RestStemsStandingIn(
        List<BeamRestStem>? restStems, int stemCount)
    {
        if (restStems is null) return ImmutableArray<BeamRestStem>.Empty;
        var kept = ImmutableArray.CreateBuilder<BeamRestStem>(restStems.Count);
        foreach (var r in restStems)
            if (r.BeforeMember > 0 && r.BeforeMember < stemCount)
                kept.Add(r);
        return kept.Count == kept.Capacity ? kept.MoveToImmutable() : kept.ToImmutable();
    }

    /// <summary>
    /// How many beam lines reach each member on each side, from the beam's rhythm.
    /// </summary>
    /// <remarks>
    /// The one door to <see cref="BeamingPattern"/> — both the per-measure and the
    /// cross-measure builder come through here, so the two cannot answer differently.
    /// Each member carries its INNERMOST tuplet span as a
    /// <see cref="BeamingPattern.TupletDescription"/>; the pattern itself derives the span
    /// boundaries and the written-proportion ranking from it. The spans' moments are
    /// measure-relative, and the beam's run from the measure holding its first stem with a
    /// whole period added per bar crossed — the same shift
    /// <see cref="BuildCrossMeasureBeamGroup"/> gives the stems — so a span's Start meets
    /// its first note's stem moment exactly. One description per span per GROUP, parents
    /// included, because the pattern's span stack compares them by identity.
    /// </remarks>
    private (int Left, int Right)[] BeamletCounts(
        IReadOnlyList<(MusicItem Item, Fraction Moment, int Measure, int Index)> members,
        BeamingPattern.Options options,
        IReadOnlyDictionary<int, List<TupletSpan>>? tupletSpans)
    {
        int firstMeasure = members.Count > 0 ? members[0].Measure : 0;
        Dictionary<TupletSpan, BeamingPattern.TupletDescription>? described = null;

        BeamingPattern.TupletDescription Describe(TupletSpan span)
        {
            described ??= new Dictionary<TupletSpan, BeamingPattern.TupletDescription>();
            if (!described.TryGetValue(span, out var d))
            {
                Fraction shift = new Fraction(span.MeasureIndex - firstMeasure) * options.Period;
                d = new BeamingPattern.TupletDescription(
                    span.Start + shift, span.Stop + shift, span.LpNumerator, span.LpDenominator,
                    span.Parent is null ? null : Describe(span.Parent));
                described[span] = d;
            }
            return d;
        }

        TupletSpan? InnermostSpan(int measure, int index)
        {
            // The measure index is the reason this is cheap — see BuildTupletSpans' remarks.
            if (tupletSpans is null || !tupletSpans.TryGetValue(measure, out var measureSpans))
                return null;
            TupletSpan? best = null;
            foreach (var s in measureSpans)
                if (s.StartIndex <= index && index <= s.EndIndex
                    && (best is null || s.NestingDepth > best.NestingDepth))
                    best = s;
            return best;
        }

        var infos = new BeamingPattern.Element[members.Count];
        for (int i = 0; i < members.Count; i++)
        {
            var m = members[i];
            var span = InnermostSpan(m.Measure, m.Index);
            infos[i] = new BeamingPattern.Element(
                m.Moment, GetDuration(m.Item), GetBeamCount(m.Item),
                Tuplet: span is null ? null : Describe(span),
                Invisible: m.Item is RestItem);
        }
        return BeamingPattern.Beamify(infos, options);
    }

    /// <summary>
    /// A tuplet bracket resolved to its time span — the voice-level input from which
    /// <see cref="BeamletCounts"/> builds each group's
    /// <see cref="BeamingPattern.TupletDescription"/>s. Start and Stop are in the bracket's
    /// OWN measure's moments (a pickup's first item starts mid-bar, as everywhere else).
    /// LILYSHARP-OWN as a carrier: LilyPond has no such intermediate — its
    /// Tuplet_description objects arrive ready-made from the tuplet engraver's event stream,
    /// where Lily# reconstructs the same data from the brackets after the fact.
    /// </summary>
    private sealed class TupletSpan(
        int measureIndex, int startIndex, int endIndex, int nestingDepth,
        Fraction start, Fraction stop, int lpNumerator, int lpDenominator)
    {
        public int MeasureIndex { get; } = measureIndex;
        public int StartIndex { get; } = startIndex;
        public int EndIndex { get; } = endIndex;
        public int NestingDepth { get; } = nestingDepth;
        public Fraction Start { get; } = start;
        public Fraction Stop { get; } = stop;
        /// <summary>LilyPond's <c>numerator_</c> — see
        /// <see cref="BeamingPattern.TupletDescription"/> for the reversal warning.</summary>
        public int LpNumerator { get; } = lpNumerator;
        public int LpDenominator { get; } = lpDenominator;
        /// <summary>The enclosing span; linked after construction because siblings may
        /// precede their parent in the bracket list.</summary>
        public TupletSpan? Parent { get; set; }
    }

    /// <summary>
    /// Resolves every tuplet bracket of a voice to its time span, parents linked by
    /// containment, INDEXED BY MEASURE. Null when the voice has no tuplets, so the common
    /// path stays free.
    /// </summary>
    /// <remarks>
    /// <para>
    /// LILYSHARP-OWN wiring: LilyPond's beam engravers receive each stem's
    /// Tuplet_description directly from the tuplet engraver
    /// (lily/template-engraver-for-beams.cc add_stem); Lily# derives the same five fields
    /// from the recorded brackets, and the derivation rules below — the measure walk for the
    /// moments, deepest-shallower-containing for the parent — are this port's, argued
    /// equivalent rather than transcribed.
    /// </para>
    /// <para>
    /// The moments come from the same walk the beam builders use — running positions from
    /// <see cref="MeasureStartPosition"/> over <see cref="GetDuration"/>, under the meter in
    /// effect at that measure (tracked exactly as <see cref="DetectBeamGroupsInMeasure"/>'s
    /// caller tracks it) — so a span's Start IS its first note's stem moment.
    /// ⚠️ One corner can disagree: <see cref="BuildCrossMeasureBeamGroup"/> seeds ITS
    /// pickup positions from the score's INITIAL meter, this walk from the effective one, so
    /// a pickup measure after a mid-piece <c>\time</c> could shift a span against a
    /// cross-measure beam's stems. That inconsistency is the cross-measure builder's own,
    /// pre-existing approximation; fixing it belongs there.
    /// </para>
    /// <para>
    /// ⚠️ THE BRACKET'S RATIO IS HANDED OVER REVERSED: LilyPond's Tuplet_description stores
    /// <c>\tuplet 3/2</c> as numerator 2, denominator 3 (ly/music-functions-init.ly:2488-2494
    /// — <c>'numerator (cdr ratio)</c>), while <see cref="TupletBracketItem"/> keeps the
    /// printed 3 in Numerator. So LpNumerator takes the bracket's Denominator and
    /// LpDenominator its Numerator.
    /// </para>
    /// <para>
    /// A parent is the DEEPEST shallower bracket of the same measure whose index range
    /// contains the child's — <c>Tuplet_description::parent_</c>. Brackets never span a bar
    /// line in this model, so containment within the measure is the whole test.
    /// </para>
    /// <para>
    /// ⚠️ THE PER-MEASURE INDEX IS LOAD-BEARING, not a convenience: BeamletCounts asks for
    /// each member's innermost span, and the skyline pass re-runs beam detection over the
    /// whole voice once PER SYSTEM (MultiStaffLayouter.BuildAllStaffSkylines →
    /// StaffTupletBracketLayouts). As one flat list this walk was members × every span of
    /// the VOICE, and 120 bars of beamed triplets paid +17〜45% end-to-end for it —
    /// measured, both orders, before this index existed.
    /// </para>
    /// </remarks>
    private Dictionary<int, List<TupletSpan>>? BuildTupletSpans(
        Voice voice, TimeSignature timeSignature, ImmutableArray<TupletBracketItem> brackets)
    {
        if (brackets.IsDefaultOrEmpty)
            return null;

        // Brackets grouped by measure up front: every later step is per-measure, and the
        // whole builder re-runs once per system (see the remarks) — a flat scan per measure
        // was measurable at corpus scale.
        var bracketsByMeasure = new Dictionary<int, List<TupletBracketItem>>();
        foreach (var b in brackets)
        {
            if (!bracketsByMeasure.TryGetValue(b.MeasureIndex, out var list))
                bracketsByMeasure[b.MeasureIndex] = list = new List<TupletBracketItem>();
            list.Add(b);
        }

        var byMeasure = new Dictionary<int, List<TupletSpan>>();
        var effectiveTimeSig = timeSignature;
        for (int mi = 0; mi < voice.Measures.Length; mi++)
        {
            var measure = voice.Measures[mi];
            foreach (var item in measure.Items)
                if (item is TimeSignatureChangeItem tsc)
                    effectiveTimeSig = tsc.NewTime;

            if (!bracketsByMeasure.TryGetValue(mi, out var measureBrackets))
                continue;

            // positions[k] = moment of measure.Items[k]; one extra entry so a bracket
            // closing on the measure's last item still has its stop.
            var options = BeamingPattern.Options.For(effectiveTimeSig);
            var positions = new Fraction[measure.Items.Length + 1];
            Fraction position = MeasureStartPosition(measure, options);
            for (int k = 0; k < measure.Items.Length; k++)
            {
                positions[k] = position;
                position += GetDuration(measure.Items[k]);
            }
            positions[measure.Items.Length] = position;

            var spans = new List<TupletSpan>(measureBrackets.Count);
            foreach (var b in measureBrackets)
            {
                // LILYSHARP-OWN guard: a bracket whose indices do not address this voice's
                // items is another stream's, a situation LilyPond never sees. The index-set
                // representation this replaced was naturally tolerant of foreign keys —
                // out-of-range ones just never matched — so keep that tolerance rather than
                // crash on them.
                //
                // ⚠️ THE CALLERS NOW SCOPE, AND THIS GUARD IS STILL LIVE. Session 193 gave
                // every caller that hands over a single stream the same scoping rule
                // (TupletBracketItem.AddressedTo, by staff AND voice), which closed the case
                // this remark used to describe — the stem-direction probe handing over the
                // whole collector's list. MEASURED over the 566-book tree WITH that scoping
                // in place: 2882 brackets kept, 2 dropped, both in audit/lpreg/pctend-probe.lys.
                //
                // ⚠️⚠️ AND THOSE 2 NAME THE AXIS (StaffIndex, VoiceIndex) CANNOT CUT. A
                // condensedStaff yields ONE BINDING PER PART sharing ONE staff index
                // (RenderSpec.GetVoiceBindings' SharesStaffWithPrevious, consumed by
                // MeasureCollector's `_cursor.StaffIndex = sharesStaff ? … - 1 : …++`) and
                // collects each part with VoiceIndex 0 — so two condensed parts are
                // INDISTINGUISHABLE to the scoping rule, and each is probed with the other's
                // brackets. In this book they are caught only because the second part's bar
                // is a lone R1, one item long; a condensed part with longer bars would take
                // the other's bracket IN RANGE and silently, which is the same defect one
                // axis further out. Closing it needs a per-stream discriminator the model
                // does not have, so it is written up in HANDOFF §2 rather than patched here.
                if (b.StartNoteIndex < 0 || b.EndNoteIndex < b.StartNoteIndex
                    || b.EndNoteIndex >= measure.Items.Length)
                    continue;
                spans.Add(new TupletSpan(
                    mi, b.StartNoteIndex, b.EndNoteIndex, b.NestingDepth,
                    positions[b.StartNoteIndex], positions[b.EndNoteIndex + 1],
                    lpNumerator: b.Denominator, lpDenominator: b.Numerator));
            }
            if (spans.Count == 0)
                continue;

            // Parent linking stays within the measure — brackets never span a bar line, so
            // a parent can only live in its child's own measure.
            foreach (var s in spans)
            {
                TupletSpan? parent = null;
                foreach (var t in spans)
                    if (t.NestingDepth < s.NestingDepth
                        && t.StartIndex <= s.StartIndex && s.EndIndex <= t.EndIndex
                        && (parent is null || t.NestingDepth > parent.NestingDepth))
                        parent = t;
                s.Parent = parent;
            }

            byMeasure[mi] = spans;
        }

        return byMeasure.Count == 0 ? null : byMeasure;
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
    private static double? AutoKneeGapCenter(ReadOnlySpan<StemCandidate> members)
    {
        if (members.Length < 2) return null;

        // Head extents in staff positions, widened by 1 like head_extents.widen(1), in
        // ascending Lo. ⚠️ ONE ARRAY AND A STABLE INSERTION SORT, not Select/OrderBy/ToList:
        // this runs once per beam group, and the LINQ chain's iterators, sort buffers and
        // List were 2.29 MB of a 60.2 MB perf-plain1k keystroke (session 192, measured) —
        // 600 bytes to order four intervals. Insertion sort keeps OrderBy's STABLE order, so
        // intervals sharing a Lo stay in member order and the walk below sees the same
        // sequence it always has.
        var intervals = new (double Lo, double Hi)[members.Length];
        for (int i = 0; i < members.Length; i++)
        {
            var iv = (Lo: members[i].HeadMin - 1.0, Hi: members[i].HeadMax + 1.0);
            int j = i - 1;
            while (j >= 0 && intervals[j].Lo > iv.Lo)
            {
                intervals[j + 1] = intervals[j];
                j--;
            }
            intervals[j + 1] = iv;
        }

        // Walk the sorted union; track the largest interior gap.
        double maxGapLen = 0, gapCenter = 0;
        double coveredHi = intervals[0].Hi;
        for (int i = 1; i < intervals.Length; i++)
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
        // LILYPOND-REF: beam.cc:1033-1040 — height_of_beams reads get_beam_translation,
        // which narrows to (3·ss + line − thickness)/3 from FOUR beams up (:129-145).
        int beamCount = members[0].BeamCount;
        for (int i = 1; i < members.Length; i++)
            if (members[i].BeamCount > beamCount) beamCount = members[i].BeamCount;
        double heightOfBeams = EngravingDefaults.BeamThickness / 2.0
            + (beamCount - 1) * EngravingDefaults.BeamTranslationOf(
                EngravingDefaults.BeamThickness, 1.0, beamCount);
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

    /// <summary>
    /// A rest a MANUAL beam runs over — it earns an invisible stem in the pattern. A spacer
    /// (LilyPond <c>s</c>) produces no grobs at all, so no stem and no pattern entry; a
    /// multi-measure <c>R</c> cannot stand inside a beam. Automatic beams still END at every
    /// rest (<see cref="IsBeamable"/> is unchanged) — only a written bracket spans one.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/beam-engraver.cc:211-218 acknowledge_rest,
    /// lily/auto-beam-engraver.cc:324-328 acknowledge_rest (force_end_).
    /// </remarks>
    private static bool IsBeamedRest(MusicItem item)
        => item is RestItem { IsSpacer: false, IsMultiMeasure: false };

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
            // A beamed rest's invisible stem carries the rest's own written count into the
            // pattern (the Duration the beam engraver hands add_stem is the rest event's) —
            // an r4's stem carries zero, exactly LilyPond's "[r4 c8] can just as well be
            // modern notation" case (lily/beam-engraver.cc:253-262).
            RestItem rest => rest.BaseDuration,
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
    /// <remarks>⚠️ TAKES A SPAN OF <see cref="StemCandidate"/>, NOT the built members: the
    /// rule is asked BEFORE the members exist, which is what lets each one be built once
    /// (see <see cref="StemCandidate"/>). Walking a List through IReadOnlyList also boxed
    /// its struct enumerator once per loop — three loops per beam group, 0.31 MB of a
    /// perf-plain1k keystroke (session 192, measured).</remarks>
    private static bool DefaultBeamStemUp(ReadOnlySpan<StemCandidate> members)
    {
        int extremeUp = 0, extremeDown = 0;
        foreach (var m in members)
        {
            if (m.HeadMax > 0) extremeUp = Math.Max(extremeUp, m.HeadMax);
            if (m.HeadMin < 0) extremeDown = Math.Min(extremeDown, m.HeadMin);
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
            if (ForcedStemUpOf(m.Item) is not null) { forceDir = true; break; }
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
            int mUp = Math.Max(0, m.HeadMax);
            int mDown = Math.Min(0, m.HeadMin);
            bool voteUp = ForcedStemUpOf(m.Item) ?? !(Math.Abs(mUp) >= -mDown);
            if (!voteUp)
            {
                downVotes++;
                totalDown += Math.Max(m.HeadMax, 0);
            }
            else
            {
                upVotes++;
                totalUp += Math.Max(-m.HeadMin, 0);
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
    /// One stem's beaming inputs, gathered BEFORE any <see cref="BeamMember"/> exists.
    /// </summary>
    /// <remarks>
    /// ⚠️ THIS TYPE IS WHY THE MEMBERS ARE BUILT ONCE. The direction rules
    /// (<see cref="DefaultBeamStemUp"/> and <see cref="AutoKneeGapCenter"/>) read only these
    /// fields, but neither can answer until every stem of the beam is known — so both
    /// builders used to construct a <see cref="BeamMember"/> per stem, clip the two outer
    /// ones into two MORE, and then rewrite EVERY member again with the direction the rules
    /// had just returned: ten objects for a four-stem beam. MEASURED (session 192, one
    /// perf-plain1k keystroke): 1.40 MB in the rewrite and about as much again in the first
    /// construction and the clip, out of 60.2 MB. Gathering the inputs here first lets each
    /// member be constructed once, already clipped and already pointed.
    /// </remarks>
    private readonly record struct StemCandidate(
        MusicItem Item, int ItemIndex, int MeasureIndex, int BeamCount,
        int BeamletLeft, int BeamletRight, int StaffPosition, int HeadMin, int HeadMax);

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
    private static bool? ForcedStemUpOf(MusicItem item) => item switch
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
        IReadOnlyDictionary<int, List<TupletSpan>>? tupletSpans = null,
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

                int visibleCount = 0;
                for (int j = start; j <= end; j++)
                {
                    var groupItem = measure.Items[j];
                    if (IsBeamable(groupItem))
                    {
                        group.Add((groupItem, j, pos));
                        visibleCount++;
                    }
                    // A rest INSIDE the bracket rides the beam as an invisible stem — the
                    // bracket itself always opens and closes on a note, so interior is
                    // strict. LilyPond's beam engraver takes the stem the rest's
                    // rhythmic-head interface earns it and finds it invisible
                    // (lily/template-engraver-for-beams.cc:75 Stem::is_invisible).
                    else if (j > start && j < end && IsBeamedRest(groupItem))
                    {
                        group.Add((groupItem, j, pos));
                    }
                    pos = pos + GetDuration(groupItem);
                }

                if (visibleCount >= 2)
                {
                    beamGroups.Add(CreateBeamGroup(group, measureIndex, beamOptions,
                        tupletSpans, voiceIndex, forceStemUpAt));
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






