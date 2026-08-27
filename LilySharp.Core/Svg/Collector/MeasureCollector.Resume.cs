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

using System.Collections;
using System.Collections.Immutable;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using LilySharp.Core.Syntax.InternalSyntax;

namespace LilySharp.Core.Svg.Collector;


public sealed partial class MeasureCollector
{
    /// <summary>
    /// Records a checkpoint at a clean measure boundary — unless a cross-measure
    /// carry is in flight, in which case the boundary is silently skipped (a missed
    /// checkpoint only costs reuse; see CollectWalkProbe's eligibility remarks).
    /// </summary>
    private void TryCaptureWalkCheckpoint(
        VoiceWalkRecording rec, MeasureBuilder builder, int invocation, int nodeIndex,
        int nodeStart)
    {
        if (!WalkCarriesNothing())
            return;
        // Inside a `|: … :|` form repeat block no boundary is a resume point:
        // ProcessRepeatBlock's own bookkeeping (volta start/end pairing, the
        // synthesized barlines between sections) lives in locals no checkpoint
        // captures, so restoring mid-block would re-enter with it lost. The
        // suppression is the finding-3-4 refinement of the old wholesale
        // "form-repeat-block" ineligibility: boundaries BEFORE and AFTER the
        // block stay resume points, and a suffix splice from a pre-block
        // boundary adopts the whole block's recorded output (its side effects
        // are all in the watermarked tables and measures).
        if (_formRepeatDepth > 0)
            return;
        rec.Checkpoints.Add(BuildWalkCheckpoint(
            builder, _sectionVisit - 1, invocation, nodeIndex, nodeStart));
    }

    /// <summary>True when no cross-measure carry is in flight — the shared
    /// eligibility of a mid-walk checkpoint (record side) and of a suffix
    /// splice boundary (live side; the same quiescence the recorded checkpoint
    /// vouches for must hold in the live walk before their states can meet).</summary>
    private bool WalkCarriesNothing()
        => _pendingGrace == null && _pendingLeadingGrace.IsDefaultOrEmpty
            && !_pendingEmptyChordSlurStart && !_pendingEmptyChordSlurEnd
            && _tremoloPairShape == null
            && _cueDepth == 0 && _cursor.VoiceScope == null && _cursor.MetadataMeasureOffset == 0
            && _phraseTransposeSaves.Count == 0
            && _measureAccidentals.Count == 0;

    /// <summary>The checkpoint capture core (see <see cref="WalkCheckpoint"/>'s
    /// inventory remarks); the end-of-walk capture passes sentinel address
    /// fields (-2/-1) — a splice consumes its value state, never its address.</summary>
    private WalkCheckpoint BuildWalkCheckpoint(
        MeasureBuilder builder, int sectionVisit, int invocation, int nodeIndex, int nodeStart)
    {
        var tables = CumulativeSideTables();
        var counts = new int[tables.Length];
        for (int t = 0; t < tables.Length; t++)
            counts[t] = tables[t].Count;

        return new WalkCheckpoint
        {
            SectionVisit = sectionVisit, // -1 = the section-less root path
            Invocation = invocation,
            NodeIndex = nodeIndex,
            SectionStartMeasure = _sectionStartMeasureForResume,
            MaxSourceRead = _walkMaxSourceRead,
            NodeStart = nodeStart,
            HeaderReadCount = _walkHeaderReads.Count,
            Builder = builder.Capture(),
            Octave = OctaveCheckpoint.Capture(_octave),
            Meta = _meta.Clone(),
            DefaultDuration = _defaultDuration,
            DefaultDots = _defaultDots,
            AmbientTonicStep = _ambientTonicStep,
            AmbientTonicAlter = _ambientTonicAlter,
            AmbientTonicValid = _ambientTonicValid,
            OpeningKeyOverride = _openingKeyOverride,
            TremoloRepeatCount = _tremoloRepeatCount,
            TremoloPairShape = _tremoloPairShape,
            TremoloPairFirst = _tremoloPairFirst,
            SectionActiveGrobProps = new(_sectionActiveGrobProps),
            KeyByMeasure = new(_keyByMeasure),
            SectionStartMeasures = new(_sectionState.StartMeasure),
            SectionAllStarts = _sectionState.AllStarts
                .ToDictionary(kv => kv.Key, kv => new List<int>(kv.Value)),
            TableCounts = counts,
            PendingInlineVoltaCount = _pendingInlineVoltas.Count,
            ParallelSpanCount = _parallelSpans.Count,
            ResolvedSpellingCount = _resolvedSpellingLog.Count,
            RepetitionReadCount = _repetitionOriginalReads.Count,
            MeasureCount = builder.CurrentMeasureIndex,
        };
    }

    /// <summary>
    /// Fast-forwards the walk to <paramref name="plan"/>'s checkpoint: restores the
    /// collector's value state, adopts the recorded prefix (measures, walk-local
    /// lists, cumulative side-table slices), and clears the pending plan so
    /// everything after the checkpoint runs live.
    /// </summary>
    private void RestoreWalkCheckpoint(VoiceResumePlan plan, MeasureBuilder builder)
    {
        var ck = plan.Checkpoint!; // only a prefix-armed plan reaches the restore
        var rec = plan.Recording;
        if (rec.IneligibleReason is { } why)
            throw new CollectResumeAbortException($"resuming an ineligible walk recording: {why}");

        // Value state.
        ck.Octave.Restore(_octave);
        _meta.CopyFrom(ck.Meta);
        _defaultDuration = ck.DefaultDuration;
        _defaultDots = ck.DefaultDots;
        _ambientTonicStep = ck.AmbientTonicStep;
        _ambientTonicAlter = ck.AmbientTonicAlter;
        _ambientTonicValid = ck.AmbientTonicValid;
        _openingKeyOverride = ck.OpeningKeyOverride;
        _tremoloRepeatCount = ck.TremoloRepeatCount;
        _tremoloPairShape = ck.TremoloPairShape;
        _tremoloPairFirst = ck.TremoloPairFirst;
        _sectionActiveGrobProps.Clear();
        foreach (var prop in ck.SectionActiveGrobProps)
            _sectionActiveGrobProps.Add(prop);
        _keyByMeasure.Clear();
        foreach (var kv in ck.KeyByMeasure)
            _keyByMeasure[kv.Key] = kv.Value;
        _sectionState.StartMeasure.Clear();
        foreach (var kv in ck.SectionStartMeasures)
            _sectionState.StartMeasure[kv.Key] = kv.Value;
        _sectionState.AllStarts.Clear();
        foreach (var kv in ck.SectionAllStarts)
            _sectionState.AllStarts[kv.Key] = new List<int>(kv.Value);
        _measureAccidentals.Clear();
        _pendingGrace = null;
        _pendingLeadingGrace = ImmutableArray<GraceNoteInfo>.Empty;
        _pendingEmptyChordSlurStart = false;
        _pendingEmptyChordSlurEnd = false;

        // Walk-local lists: the recorded prefix (append-only within the walk, so
        // the recording's first N entries are exactly the checkpoint's state).
        _pendingInlineVoltas.Clear();
        for (int i = 0; i < ck.PendingInlineVoltaCount; i++)
            _pendingInlineVoltas.Add(rec.PendingInlineVoltas![i]);
        _parallelSpans.Clear();
        for (int i = 0; i < ck.ParallelSpanCount; i++)
            _parallelSpans.Add(rec.ParallelSpans![i]);

        // Resolved spellings (finding 3-4): the prefix's dictionary entries, RE-KEYED
        // onto this tree's nodes — the readers (a q / bare duration past the restore
        // point) look originals up by the NEW tree's OriginalOf answers, and red
        // nodes are not shared between trees. Positions are unshifted here (every
        // adopted read is ≤ the common prefix — MaxSourceRead's guarantee), so the
        // identity window resolves each node at its recorded position; replay order
        // preserves a form replay's last-write-wins. The recorded VALUES are what a
        // live walk of the identical prefix would compute (the same determinism the
        // adopted measures stand on). Any resolution failure is structural drift.
        var idWindow = new CollectTailShifter.Window(int.MaxValue, int.MaxValue, 0);
        for (int i = 0; i < ck.ResolvedSpellingCount; i++)
        {
            var (oldNode, resolvedMembers) = rec.ResolvedSpellings![i];
            if (_root == null
                || CollectTailShifter.ResolveShifted(_root, oldNode, idWindow) is not { } rekeyed)
                throw new CollectResumeAbortException(
                    "collect resume could not re-key an adopted resolved spelling");
            if (rekeyed is NoteSyntax n)
                _resolvedNotes[n] = resolvedMembers[0];
            else if (rekeyed is ChordSyntax c)
                _resolvedChordMembers[c] = resolvedMembers;
            else
                throw new CollectResumeAbortException(
                    "collect resume re-keyed a resolved spelling onto an unexpected node kind");
        }

        // Cumulative side tables: extend to the checkpoint's watermark from the
        // source's FINAL lists (append-only across the collect, so entries
        // [0..count) are the prefix regardless of what later walks appended).
        // Entries below the current count were appended by THIS collect's own
        // earlier walks and are identical by determinism.
        var src = plan.Source.CumulativeSideTables();
        var dst = CumulativeSideTables();
        for (int t = 0; t < dst.Length; t++)
        {
            if (dst[t].Count > ck.TableCounts[t])
                throw new CollectResumeAbortException(
                    $"resume: side table {t} is already past its checkpoint watermark");
            for (int j = dst[t].Count; j < ck.TableCounts[t]; j++)
                dst[t].Add(src[t][j]);
        }

        // Builder: adopt the pre-finalize prefix measures, with the boundary-time
        // value of the last one (the walk can rewrite _measures[^1] after the
        // checkpoint — SetBreak / AddEndBarlineSource — and did, in the recording).
        builder.Restore(ck.Builder, rec.PreFinalizeMeasures!.GetRange(0, ck.MeasureCount));

        _resumeRestoredSectionStart = ck.SectionStartMeasure;
        _resumePending = null;
        plan.Consumed = true;
    }

    /// <summary>
    /// The suffix splice (see CollectWalkProbe's remarks): at a live clean
    /// boundary whose shifted address matched the recorded checkpoint
    /// <paramref name="ck"/>, compare the ENTIRE live value state against the
    /// checkpoint (positions through the window map) and, on a match, adopt the
    /// recorded tail — position-shifted copies of the measures and side-table
    /// slices up to the end-of-walk watermarks, re-resolved parallel-span nodes
    /// — jump the value state to the recorded end of the walk, and skip the
    /// rest. Every validation failure returns false and the walk keeps running
    /// live: a declined splice costs reuse, never correctness. Two phases on
    /// purpose — everything is validated and copied BEFORE the first mutation,
    /// so a decline never leaves the collector half-spliced.
    /// </summary>
    private bool TrySpliceSuffix(WalkCheckpoint ck, MeasureBuilder builder)
    {
        var plan = _suffixPlan!;
        var rec = plan.Recording;
        var w = _suffixWindow;
        if (rec.EndCheckpoint is not { } endCk || rec.PreFinalizeMeasures is not { } pre)
            return false;

        // Live-side carry quiescence: the recorded boundary was carry-free, so
        // the live one must be too before their states are even comparable.
        if (!WalkCarriesNothing())
            return false;

        // Recorded-side rewrite quiescence: the recorded tail must not have
        // rewritten the measure standing AT this boundary (SetBreak /
        // AddEndBarlineSource reach back into _measures[^1]); the live [^1] is
        // the edited text's own and must stand as produced. The recorded final
        // value differing from the checkpoint-time pin is exactly "the tail
        // rewrote it" — a later candidate (past the rewrite) remains usable.
        if (ck.MeasureCount > 0
            && !pre[ck.MeasureCount - 1].Equals(ck.Builder.LastMeasure))
            return false;

        // Cheapest decline first: a tail measure whose source span overlaps a
        // NON-EMPTY dirty window can never be adopted (the window lies inside
        // its text), and finding out inside the copy loop below would first
        // COPY every measure before it — for a candidate standing before the
        // window (the m=0 case) that is half the book, paid on every keystroke
        // just to decline.
        // ⚠️ AN EMPTY WINDOW WITH Δ≠0 IS NOT "NOTHING CHANGED" (2026-08-27,
        // found by the finding-4-5 net): the inserted/deleted text lands AT one
        // point, and a measure STRADDLING that point has different text even
        // though the old text's prefix and suffix both survive. This comment
        // used to claim the straddler "shifts per-position and is fine" — true
        // for trivia (a duplicated space), and served the OLD note for content
        // (`d` → `dis`: same pitch-letter TOKEN KIND, so the parse agreement
        // does not object the way a letter swap makes it, and the whole chain —
        // stale item, equal content keys, whole-layout reuse — handed back the
        // previous keystroke's picture). The splice cannot tell trivia from
        // content here, so a straddled measure always declines and is walked
        // live; candidates PAST the point keep splicing. Only the identity
        // window (Δ=0, empty) adopts a straddler — the texts are equal.
        if (w.Prefix < w.SuffixStart || w.Delta != 0)
        {
            for (int i = ck.MeasureCount; i < pre.Count; i++)
            {
                if (pre[i].SourceStart < w.SuffixStart && pre[i].SourceEnd > w.Prefix)
                    return false;
            }
        }

        // Repetition certification (finding 3-4): a q / bare duration in the tail
        // copies state recorded at its ORIGINAL's walk. That copy is certified for
        // an original in the unchanged common prefix (identical text, deterministic
        // walk) and for one in the tail itself (the boundary state equality below
        // certifies the recorded walk of the tail wholesale) — but NOT for an
        // original in [prefix, boundary): that region was re-walked LIVE this
        // collect and can resolve differently even when the boundary state
        // reconverges (a relative frame moved by the window and reset before the
        // boundary). An EMPTY window makes the live region's text identical and
        // the whole range certified by determinism, like the overlap check above.
        if (w.Prefix < w.SuffixStart)
        {
            for (int i = ck.RepetitionReadCount; i < endCk.RepetitionReadCount; i++)
            {
                var (originalStart, originalEnd) = rec.RepetitionOriginalReads![i];
                // Certified iff the WHOLE original lies before the window or at/after
                // the boundary — the whole span, because an edit INSIDE the original
                // node leaves its start before the window while its value changed.
                if (!(originalEnd <= w.Prefix || originalStart >= ck.NodeStart))
                    return false;
            }
        }

        if (!SuffixStateMatches(ck, builder, w))
            return false;

        // The adopted tail contains section spacer padding whose COUNT is the
        // canonical section bar count — a function of EVERY part's cell text,
        // invisible to the per-position checks. Verified once per collect
        // against the recording's memo (HANDOFF §1 ⒭: Δm in another part).
        var probe = WalkProbe!;
        probe.CanonicalBarsVerified ??= CanonicalBarsMatch(plan.Source);
        if (probe.CanonicalBarsVerified != true)
            return false;

        // Layer 4's suffix half (lazy, memoized once per collect): the parse
        // agreements that make an adopted tail trustworthy at all — see
        // CollectResumePlanner.ParseAgreementsHold. Placed after the cheap
        // per-walk guards so a book that always declines never pays it.
        if (!CollectResumePlanner.ParseAgreementsHold(probe))
            return false;

        // --- prepare: validate and copy everything before touching any state ---
        // IDENTITY fast path: an empty window with Δ=0 means the baseline and
        // edited TEXTS are equal (the restore keystroke of an alternating edit
        // session) — every shift is the identity, so the recorded instances are
        // adopted by reference instead of being cloned one by one (the models
        // are immutable records; the prefix adoption shares them the same way,
        // old-tree node references included: an identical text's walk is
        // value-identical by determinism).
        bool identity = w.Delta == 0 && w.Prefix >= w.SuffixStart;

        List<Measure> tailMeasures;
        if (identity)
        {
            tailMeasures = pre.GetRange(ck.MeasureCount, pre.Count - ck.MeasureCount);
        }
        else
        {
            tailMeasures = new List<Measure>(pre.Count - ck.MeasureCount);
            for (int i = ck.MeasureCount; i < pre.Count; i++)
            {
                if (CollectTailShifter.ShiftMeasure(pre[i], w) is not { } m)
                    return false;
                tailMeasures.Add(m);
            }
        }

        var src = plan.Source.CumulativeSideTables();
        var dst = CumulativeSideTables();
        var tailSlices = new List<object>[dst.Length];
        for (int t = 0; t < dst.Length; t++)
        {
            var slice = new List<object>(endCk.TableCounts[t] - ck.TableCounts[t]);
            for (int j = ck.TableCounts[t]; j < endCk.TableCounts[t]; j++)
            {
                if (identity)
                {
                    slice.Add(src[t][j]!);
                    continue;
                }
                if (CollectTailShifter.ShiftSideEntry(src[t][j]!, w) is not { } entry)
                    return false;
                slice.Add(entry);
            }
            tailSlices[t] = slice;
        }

        var voltaTail = new List<(int, int, string, bool, int)>(
            endCk.PendingInlineVoltaCount - ck.PendingInlineVoltaCount);
        for (int i = ck.PendingInlineVoltaCount; i < endCk.PendingInlineVoltaCount; i++)
        {
            var v = rec.PendingInlineVoltas![i];
            if (!w.TryShift(v.Item5, out int vp))
                return false;
            voltaTail.Add((v.Item1, v.Item2, v.Item3, v.Item4, vp));
        }

        // The wall (HANDOFF §1 ⑶): the recorded spans hold OLD-tree node
        // references, and their extra voices are walked LIVE after this walk —
        // they must be re-resolved against the new tree, not adopted (except on
        // the identity path, where the old tree's text IS the new text).
        var spanTail = new List<(ParallelExpressionSyntax, int, Fraction, OctaveSnapshot)>(
            endCk.ParallelSpanCount - ck.ParallelSpanCount);
        for (int i = ck.ParallelSpanCount; i < endCk.ParallelSpanCount; i++)
        {
            var (oldNode, startMeasure, startOffset, frame) = rec.ParallelSpans![i];
            if (identity)
            {
                spanTail.Add((oldNode, startMeasure, startOffset, frame));
                continue;
            }
            if (_root == null
                || CollectTailShifter.ResolveShifted(_root, oldNode, w)
                    is not ParallelExpressionSyntax resolved)
                return false;
            spanTail.Add((resolved, startMeasure, startOffset, frame));
        }

        // Resolved spellings of the adopted tail (finding 3-4), re-keyed onto this
        // tree — the identity path included: even with identical text the NEW tree's
        // OriginalOf answers new red nodes, so old-node keys would never be hit.
        // Values are certified by the boundary state equality plus the repetition
        // guard above. Read by whatever runs live after the splice (a parallel
        // span's extra voices).
        var spellTail = new List<(SyntaxNode Node, ImmutableArray<ResolvedChordMember> Members)>(
            endCk.ResolvedSpellingCount - ck.ResolvedSpellingCount);
        for (int i = ck.ResolvedSpellingCount; i < endCk.ResolvedSpellingCount; i++)
        {
            var (oldNode, resolvedMembers) = rec.ResolvedSpellings![i];
            if (_root == null
                || CollectTailShifter.ResolveShifted(_root, oldNode, w)
                    is not { } rekeyed
                || rekeyed is not (NoteSyntax or ChordSyntax))
                return false;
            spellTail.Add((rekeyed, resolvedMembers));
        }

        var endMeta = endCk.Meta.Clone();
        if (!identity && !ShiftMetaPositions(endMeta, w))
            return false;

        var shiftedEndBuilder = endCk.Builder;
        if (!identity)
        {
            if (!w.TryShift(shiftedEndBuilder.SectionLabelPosition, out int endLabelPos)
                || !w.TryShift(shiftedEndBuilder.MeasureSourceStart, out int endSourceStart))
                return false;
            Measure? endLast = null;
            if (shiftedEndBuilder.LastMeasure is { } last)
            {
                endLast = CollectTailShifter.ShiftMeasure(last, w);
                if (endLast == null)
                    return false;
            }
            shiftedEndBuilder = shiftedEndBuilder with
            {
                SectionLabelPosition = endLabelPos,
                MeasureSourceStart = endSourceStart,
                LastMeasure = endLast,
            };
        }

        // --- commit: jump to the recorded end of the walk ---
        endCk.Octave.Restore(_octave);
        _meta.CopyFrom(endMeta);
        _defaultDuration = endCk.DefaultDuration;
        _defaultDots = endCk.DefaultDots;
        _ambientTonicStep = endCk.AmbientTonicStep;
        _ambientTonicAlter = endCk.AmbientTonicAlter;
        _ambientTonicValid = endCk.AmbientTonicValid;
        _openingKeyOverride = endCk.OpeningKeyOverride;
        _tremoloRepeatCount = endCk.TremoloRepeatCount;
        _tremoloPairShape = endCk.TremoloPairShape;
        _tremoloPairFirst = endCk.TremoloPairFirst;
        _sectionActiveGrobProps.Clear();
        foreach (var prop in endCk.SectionActiveGrobProps)
            _sectionActiveGrobProps.Add(prop);
        _keyByMeasure.Clear();
        foreach (var kv in endCk.KeyByMeasure)
            _keyByMeasure[kv.Key] = kv.Value;
        _sectionState.StartMeasure.Clear();
        foreach (var kv in endCk.SectionStartMeasures)
            _sectionState.StartMeasure[kv.Key] = kv.Value;
        _sectionState.AllStarts.Clear();
        foreach (var kv in endCk.SectionAllStarts)
            _sectionState.AllStarts[kv.Key] = new List<int>(kv.Value);
        _measureAccidentals.Clear();

        for (int t = 0; t < dst.Length; t++)
            foreach (var entry in tailSlices[t])
                dst[t].Add(entry);
        _pendingInlineVoltas.AddRange(voltaTail);
        _parallelSpans.AddRange(spanTail);
        foreach (var (node, resolvedMembers) in spellTail)
        {
            if (node is NoteSyntax n)
                _resolvedNotes[n] = resolvedMembers[0];
            else
                _resolvedChordMembers[(ChordSyntax)node] = resolvedMembers;
        }

        var measures = builder.MeasuresSnapshot();
        measures.AddRange(tailMeasures);
        builder.Restore(shiftedEndBuilder, measures);

        _suffixSpliced = true;
        plan.SplicedMeasures = pre.Count - ck.MeasureCount;
        return true;
    }

    /// <summary>The suffix splice's state comparison: the live walk state at a
    /// clean boundary against a recorded checkpoint, field for field over the
    /// SAME inventory <see cref="BuildWalkCheckpoint"/> snapshots — recorded
    /// positions compared through the window map. Anything unequal (including
    /// an unshiftable recorded position) means the dirty window changed state
    /// the tail depends on: no splice.</summary>
    private bool SuffixStateMatches(WalkCheckpoint ck, MeasureBuilder builder, in CollectTailShifter.Window w)
    {
        if (builder.CurrentMeasureIndex != ck.MeasureCount)
            return false;
        if (_sectionStartMeasureForResume != ck.SectionStartMeasure)
            return false;

        // Builder cross-measure state. LastMeasure content is deliberately NOT
        // compared — the boundary measure is the edited text's own (its content
        // legitimately differs from the recording); what the tail needs from it
        // is only that it will not be rewritten (checked by the caller).
        var live = builder.Capture();
        var recB = ck.Builder;
        if (live.ConfirmableBoundary != recB.ConfirmableBoundary
            || live.BoundaryRetargetable != recB.BoundaryRetargetable
            || live.LastEndAutoFill != recB.LastEndAutoFill
            || live.TimeSignature != recB.TimeSignature
            || live.PartialRestore != recB.PartialRestore
            || live.PendingStartBarline != recB.PendingStartBarline
            || live.PendingEndBarline != recB.PendingEndBarline
            || live.PendingBreak != recB.PendingBreak
            || live.PendingNoBreak != recB.PendingNoBreak
            || !string.Equals(live.SectionLabel, recB.SectionLabel, StringComparison.Ordinal))
            return false;
        if (!w.TryShift(recB.SectionLabelPosition, out int labelPos)
            || live.SectionLabelPosition != labelPos)
            return false;
        if (!w.TryShift(recB.MeasureSourceStart, out int sourceStart)
            || live.MeasureSourceStart != sourceStart)
            return false;

        if (OctaveCheckpoint.Capture(_octave) != ck.Octave)
            return false;
        if (!MetaMatchesShifted(ck.Meta, w))
            return false;
        if (_defaultDuration != ck.DefaultDuration || _defaultDots != ck.DefaultDots)
            return false;
        if (_ambientTonicStep != ck.AmbientTonicStep
            || _ambientTonicAlter != ck.AmbientTonicAlter
            || _ambientTonicValid != ck.AmbientTonicValid)
            return false;
        if (_openingKeyOverride != ck.OpeningKeyOverride)
            return false;
        if (_tremoloRepeatCount != ck.TremoloRepeatCount
            || _tremoloPairShape != ck.TremoloPairShape
            || _tremoloPairFirst != ck.TremoloPairFirst)
            return false;

        if (_sectionActiveGrobProps.Count != ck.SectionActiveGrobProps.Count
            || !_sectionActiveGrobProps.SetEquals(ck.SectionActiveGrobProps))
            return false;

        if (_keyByMeasure.Count != ck.KeyByMeasure.Count)
            return false;
        foreach (var kv in ck.KeyByMeasure)
            if (!_keyByMeasure.TryGetValue(kv.Key, out var key) || key != kv.Value)
                return false;

        if (_sectionState.StartMeasure.Count != ck.SectionStartMeasures.Count)
            return false;
        foreach (var kv in ck.SectionStartMeasures)
            if (!_sectionState.StartMeasure.TryGetValue(kv.Key, out int start) || start != kv.Value)
                return false;
        if (_sectionState.AllStarts.Count != ck.SectionAllStarts.Count)
            return false;
        foreach (var kv in ck.SectionAllStarts)
            if (!_sectionState.AllStarts.TryGetValue(kv.Key, out var starts)
                || !starts.SequenceEqual(kv.Value))
                return false;

        // Watermark equality = "the window produced exactly the recorded item
        // counts", which is what makes the adopted tail slices land at the same
        // indices — and is the Δm≠0 bail for this walk's own measures/items.
        var tables = CumulativeSideTables();
        for (int t = 0; t < tables.Length; t++)
            if (tables[t].Count != ck.TableCounts[t])
                return false;

        if (_pendingInlineVoltas.Count != ck.PendingInlineVoltaCount)
            return false;
        for (int i = 0; i < _pendingInlineVoltas.Count; i++)
        {
            var recorded = planVolta(i);
            if (!w.TryShift(recorded.Item5, out int pos))
                return false;
            var liveVolta = _pendingInlineVoltas[i];
            if (liveVolta.Item1 != recorded.Item1 || liveVolta.Item2 != recorded.Item2
                || !string.Equals(liveVolta.Item3, recorded.Item3, StringComparison.Ordinal)
                || liveVolta.Item4 != recorded.Item4 || liveVolta.Item5 != pos)
                return false;
        }

        if (_parallelSpans.Count != ck.ParallelSpanCount)
            return false;
        for (int i = 0; i < _parallelSpans.Count; i++)
        {
            var (liveNode, liveStart, liveOffset, liveFrame) = _parallelSpans[i];
            var (recNode, recStart, recOffset, recFrame) = _suffixPlan!.Recording.ParallelSpans![i];
            if (liveStart != recStart || liveOffset != recOffset || liveFrame != recFrame)
                return false;
            if (!w.TryShift(recNode.FullSpan.Start, out int nodeStart)
                || liveNode.FullSpan.Start != nodeStart
                || liveNode.FullSpan.End - liveNode.FullSpan.Start
                    != recNode.FullSpan.End - recNode.FullSpan.Start)
                return false;
        }

        return true;

        (int, int, string, bool, int) planVolta(int i)
            => _suffixPlan!.Recording.PendingInlineVoltas![i];
    }

    /// <summary>Compares the live <see cref="MetadataState"/> against a recorded
    /// one, the six header positions through the window map. All other fields
    /// are value scalars/strings.</summary>
    private bool MetaMatchesShifted(MetadataState rec, in CollectTailShifter.Window w)
    {
        if (!w.TryShift(rec.TitlePosition, out int title) || _meta.TitlePosition != title
            || !w.TryShift(rec.ComposerPosition, out int composer) || _meta.ComposerPosition != composer
            || !w.TryShift(rec.TimePosition, out int time) || _meta.TimePosition != time
            || !w.TryShift(rec.KeyPosition, out int key) || _meta.KeyPosition != key
            || !w.TryShift(rec.ClefPosition, out int clef) || _meta.ClefPosition != clef
            || !w.TryShift(rec.TempoPosition, out int tempo) || _meta.TempoPosition != tempo)
            return false;

        return _meta.Title == rec.Title
            && _meta.Composer == rec.Composer
            && _meta.Fonts.Equals(rec.Fonts)
            && _meta.Paper.Equals(rec.Paper)
            && _meta.Tempo == rec.Tempo
            && _meta.TempoText == rec.TempoText
            && _meta.TempoBeatUnit == rec.TempoBeatUnit
            && _meta.TempoDots == rec.TempoDots
            && _meta.SwingSubdivision == rec.SwingSubdivision
            && _meta.TimeBeats == rec.TimeBeats
            && _meta.TimeBeatsText == rec.TimeBeatsText
            && _meta.TimeSenzaMisura == rec.TimeSenzaMisura
            && _meta.TimeBeatType == rec.TimeBeatType
            && _meta.KeySharps == rec.KeySharps
            && _meta.KeyCustom == rec.KeyCustom
            && _meta.InitialKeyCustom == rec.InitialKeyCustom
            && _meta.InitialKeySharps == rec.InitialKeySharps
            && _meta.KeyTonicStep == rec.KeyTonicStep
            && _meta.KeyTonicAlter == rec.KeyTonicAlter
            && _meta.Clef == rec.Clef
            && _meta.InitialClef == rec.InitialClef;
    }

    /// <summary>Re-homes a cloned <see cref="MetadataState"/>'s six header
    /// positions through the window map (the splice's end-state jump); false
    /// when one lies inside the dirty window.</summary>
    private static bool ShiftMetaPositions(MetadataState meta, in CollectTailShifter.Window w)
    {
        if (!w.TryShift(meta.TitlePosition, out int title)
            || !w.TryShift(meta.ComposerPosition, out int composer)
            || !w.TryShift(meta.TimePosition, out int time)
            || !w.TryShift(meta.KeyPosition, out int key)
            || !w.TryShift(meta.ClefPosition, out int clef)
            || !w.TryShift(meta.TempoPosition, out int tempo))
            return false;
        meta.TitlePosition = title;
        meta.ComposerPosition = composer;
        meta.TimePosition = time;
        meta.KeyPosition = key;
        meta.ClefPosition = clef;
        meta.TempoPosition = tempo;
        return true;
    }

    /// <summary>Verifies every canonical section bar count the RECORDED collect
    /// computed still holds on the edited text (the live memo fills as a side
    /// effect, so later section ends reuse the counts). A section name that no
    /// longer resolves, or a changed count, declines the splice — some part's
    /// cell inside the window gained or lost a bar (Δm in another part).</summary>
    private bool CanonicalBarsMatch(MeasureCollector recordedSource)
    {
        foreach (var (oldSection, recordedBars) in recordedSource._canonicalSectionBars)
        {
            if (!_sectionState.Sections.TryGetValue(oldSection.SectionName, out var liveSection))
                return false;
            if (GetCanonicalSectionBars(liveSection) != recordedBars)
                return false;
        }
        return true;
    }

}
