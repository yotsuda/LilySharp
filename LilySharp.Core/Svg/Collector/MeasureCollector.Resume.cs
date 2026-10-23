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
        int nodeStart, SyntaxKind nodeKind, bool nodeIsPhraseEnd, int[]? gatherPath)
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
            builder, _sectionVisit - 1, invocation, nodeIndex, nodeStart, nodeKind, nodeIsPhraseEnd, gatherPath,
            previous: rec.Checkpoints.Count > 0 ? rec.Checkpoints[^1] : null));
    }

    /// <summary>True when no cross-measure carry is in flight — the shared
    /// eligibility of a mid-walk checkpoint (record side) and of a suffix
    /// splice boundary (live side; the same quiescence the recorded checkpoint
    /// vouches for must hold in the live walk before their states can meet).
    /// ⚠️ The accidental memory is NOT a carry: it is value state, captured and
    /// compared (<see cref="WalkCheckpoint.Accidentals"/>). Demanding it empty
    /// here — as this gate did until session 396 — was satisfiable under the
    /// default style only (the map is cleared at the bar line); a book under
    /// <c>modern</c> / <c>noReset</c> had no checkpoint after its first note and
    /// re-collected in full on every keystroke.</summary>
    private bool WalkCarriesNothing()
        => _v.PendingGrace == null && _v.PendingLeadingGrace.IsDefaultOrEmpty
            && !_v.PendingEmptyChordSlurStart && !_v.PendingEmptyChordSlurEnd
            && _v.TremoloPairShape == null
            && _v.CueDepth == 0 && _v.PercentCoveredDepth == 0
            && _v.Cursor.VoiceScope == null
            && _v.PhraseTransposeSaves.Count == 0;

    /// <summary>The accidental memory as <see cref="AccidentalMemorySnapshot"/> reads
    /// it: entries in engraving order, each bar as an offset behind the walk's current
    /// bar.</summary>
    private AccidentalMemorySnapshot CaptureAccidentalMemory()
    {
        if (_v.MeasureAccidentals.Count == 0)
            return AccidentalMemorySnapshot.Empty;
        var byOrder = new (int Step, int Octave, int Alter, int BarsBack, int Order)[_v.MeasureAccidentals.Count];
        int n = 0;
        foreach (var (key, entry) in _v.MeasureAccidentals)
            byOrder[n++] = (key.step, key.octave, entry.Alter, _v.AccidentalBar - entry.Bar, entry.Order);
        Array.Sort(byOrder, static (a, b) => a.Order.CompareTo(b.Order));
        var entries = new (int Step, int Octave, int Alter, int BarsBack)[byOrder.Length];
        for (int i = 0; i < byOrder.Length; i++)
            entries[i] = (byOrder[i].Step, byOrder[i].Octave, byOrder[i].Alter, byOrder[i].BarsBack);
        return new AccidentalMemorySnapshot(entries);
    }

    /// <summary>Rebuilds the memory from a snapshot against the walk's CURRENT bar and
    /// order counters — a prefix resume's fresh 0 as much as a splice's live count. Only
    /// bar differences and the ordering are ever read, so the counters need not be the
    /// recording's.</summary>
    private void RestoreAccidentalMemory(AccidentalMemorySnapshot snapshot)
    {
        _v.MeasureAccidentals.Clear();
        foreach (var (step, octave, alter, barsBack) in snapshot.Entries)
            _v.MeasureAccidentals[(step, octave)] = (alter, _v.AccidentalBar - barsBack, _v.AccidentalOrder++);
    }

    /// <summary>The empty grob-prop set every checkpoint taken outside a section-level
    /// override shares (<see cref="WalkCheckpoint.SectionActiveGrobProps"/> is only ever
    /// iterated, counted and set-compared, never written).</summary>
    private static readonly HashSet<(string, string)> s_noSectionGrobProps = new();

    /// <summary>The checkpoint capture core (see <see cref="WalkCheckpoint"/>'s
    /// inventory remarks); the end-of-walk capture passes sentinel address
    /// fields (-2/-1) — a splice consumes its value state, never its address.</summary>
    /// <remarks>
    /// <paramref name="previous"/> is the recording's last checkpoint (null for its first):
    /// the three members a checkpoint only ever READS — the metadata clone, the side-table
    /// watermarks and the grob-prop set — are shared with it when nothing moved in between,
    /// which is nearly always at a bar line. MEASURED (session 522, Release, the owner's
    /// corpus, 232 books × eight forward keystrokes; a keystroke in eight re-records):
    /// 33 checkpoints a keystroke at 687 B each, the metadata unchanged at 32 of them, the
    /// grob-prop set empty at 33, the 21 watermarks equal at 30 — 10 KB a keystroke of
    /// copies of the previous checkpoint's own values.
    /// </remarks>
    private WalkCheckpoint BuildWalkCheckpoint(
        MeasureBuilder builder, int sectionVisit, int invocation, int nodeIndex, int nodeStart,
        SyntaxKind nodeKind = SyntaxKind.None, bool nodeIsPhraseEnd = false, int[]? gatherPath = null,
        WalkCheckpoint? previous = null)
    {
        var tables = CumulativeSideTables();
        Span<int> live = tables.Length <= 64 ? stackalloc int[tables.Length] : new int[tables.Length];
        for (int t = 0; t < tables.Length; t++)
            live[t] = tables[t].Count;
        int[] counts = previous != null && live.SequenceEqual(previous.TableCounts)
            ? previous.TableCounts
            : live.ToArray();
        var meta = previous != null && _v.Meta.SameAs(previous.Meta) ? previous.Meta : _v.Meta.Clone();
        var props = _sectionActiveGrobProps.Count == 0
            ? s_noSectionGrobProps
            : new HashSet<(string, string)>(_sectionActiveGrobProps);

        return new WalkCheckpoint
        {
            SectionVisit = sectionVisit, // -1 = the section-less root path
            Invocation = invocation,
            NodeIndex = nodeIndex,
            SectionStartMeasure = _sectionStartMeasureForResume,
            MaxSourceRead = _walkMaxSourceRead,
            NodeStart = nodeStart,
            NodeKind = nodeKind,
            NodeIsPhraseEnd = nodeIsPhraseEnd,
            GatherPath = gatherPath,
            HeaderReadCount = _walkHeaderReads.Count,
            CanonicalReadCount = _walkCanonicalReads.Count,
            Builder = builder.Capture(),
            Octave = OctaveCheckpoint.Capture(_v.Octave),
            Meta = meta,
            DefaultDuration = _v.DefaultDuration,
            DefaultDots = _v.DefaultDots,
            AmbientTonicStep = _v.AmbientTonicStep,
            AmbientTonicAlter = _v.AmbientTonicAlter,
            AmbientTonicValid = _v.AmbientTonicValid,
            OpeningKeyOverride = _openingKeyOverride,
            TremoloRepeatCount = _v.TremoloRepeatCount,
            TremoloPairShape = _v.TremoloPairShape,
            TremoloPairFirst = _v.TremoloPairFirst,
            Accidentals = CaptureAccidentalMemory(),
            SectionActiveGrobProps = props,
            KeyLogCount = _keyByMeasureLog.Count,
            SectionStartLogCount = _sectionStartLog.Count,
            TableCounts = counts,
            PendingInlineVoltaCount = _v.PendingInlineVoltas.Count,
            SpanTrackCount = _spanTracks.Count,
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
        ck.Octave.Restore(_v.Octave);
        _v.Meta.CopyFrom(ck.Meta);
        _v.DefaultDuration = ck.DefaultDuration;
        _v.DefaultDots = ck.DefaultDots;
        _v.AmbientTonicStep = ck.AmbientTonicStep;
        _v.AmbientTonicAlter = ck.AmbientTonicAlter;
        _v.AmbientTonicValid = ck.AmbientTonicValid;
        _openingKeyOverride = ck.OpeningKeyOverride;
        _v.TremoloRepeatCount = ck.TremoloRepeatCount;
        _v.TremoloPairShape = ck.TremoloPairShape;
        _v.TremoloPairFirst = ck.TremoloPairFirst;
        _sectionActiveGrobProps.Clear();
        foreach (var prop in ck.SectionActiveGrobProps)
            _sectionActiveGrobProps.Add(prop);
        // The two journaled maps: rebuilt from the SOURCE's journal prefixes (the
        // journals are append-only across the collect, so entries [0..count) are
        // the checkpoint's state — the same argument the side tables make below).
        RestoreKeyLog(plan.Source, rec.StartKeyLogCount, ck.KeyLogCount);
        RestoreSectionStartLog(plan.Source, rec.StartSectionStartLogCount, ck.SectionStartLogCount);
        RestoreAccidentalMemory(ck.Accidentals);
        _v.PendingGrace = null;
        _v.PendingLeadingGrace = ImmutableArray<GraceColumnInfo>.Empty;
        _v.PendingEmptyChordSlurStart = false;
        _v.PendingEmptyChordSlurEnd = false;
        _v.PendingEmptyChordSlurStartSource = 0;
        _v.PendingEmptyChordSlurEndSource = 0;

        // Walk-local lists: the recorded prefix (append-only within the walk, so
        // the recording's first N entries are exactly the checkpoint's state).
        _v.PendingInlineVoltas.Clear();
        for (int i = 0; i < ck.PendingInlineVoltaCount; i++)
            _v.PendingInlineVoltas.Add(rec.PendingInlineVoltas![i]);
        // The span tracks (session 970: what each span's voices PRODUCED — their measures are
        // adopted like the primary's, a function of the unchanged prefix) with their node
        // re-keyed onto this tree, as the spellings below are: the node is read for its
        // named voices (BuildMultiVoiceScore), and an old-tree node must not outlive its tree.
        var idWindow = new CollectTailShifter.Window(int.MaxValue, int.MaxValue, 0);
        _spanTracks.Clear();
        for (int i = 0; i < ck.SpanTrackCount; i++)
        {
            var span = rec.SpanTracks![i];
            if (_root == null
                || CollectTailShifter.ResolveShifted(_root, span.Node, idWindow)
                    is not ParallelExpressionSyntax rekeyed)
                throw new CollectResumeAbortException(
                    "collect resume could not re-key an adopted parallel span");
            _spanTracks.Add(span with { Node = rekeyed });
        }

        // Resolved spellings (finding 3-4): the prefix's dictionary entries, RE-KEYED
        // onto this tree's nodes — the readers (a q / bare duration past the restore
        // point) look originals up by the NEW tree's OriginalOf answers, and red
        // nodes are not shared between trees. Positions are unshifted here (every
        // adopted read is ≤ the common prefix — MaxSourceRead's guarantee), so the
        // identity window resolves each node at its recorded position; replay order
        // preserves a form replay's last-write-wins. The recorded VALUES are what a
        // live walk of the identical prefix would compute (the same determinism the
        // adopted measures stand on). Any resolution failure is structural drift.
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
        _spliceCandidateStart = ck.NodeStart;
        if (rec.EndCheckpoint is not { } endCk || rec.PreFinalizeMeasures is not { } pre)
            return DeclineSplice("the recording has no end checkpoint to jump to");

        // Live-side carry quiescence: the recorded boundary was carry-free, so
        // the live one must be too before their states are even comparable.
        if (!WalkCarriesNothing())
            return DeclineSplice("a cross-measure carry is in flight at the live boundary");

        // Recorded-side rewrite quiescence: the recorded tail must not have
        // rewritten the measure standing AT this boundary (SetBreak /
        // AddEndBarlineSource reach back into _measures[^1]); the live [^1] is
        // the edited text's own and must stand as produced. The recorded final
        // value differing from the checkpoint-time pin is exactly "the tail
        // rewrote it" — a later candidate (past the rewrite) remains usable.
        if (ck.MeasureCount > 0
            && !pre[ck.MeasureCount - 1].Equals(ck.Builder.LastMeasure))
            return DeclineSplice("the recorded tail rewrote the measure standing at this boundary");

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
        // ⚠️ A MEASURE ENDING EXACTLY AT THE WINDOW IS A STRADDLER (2026-09-10,
        // session 366, user report on scratch/ベースタブLy/tooLongChords.lys): a
        // measure's SourceEnd is the position of its CLOSING bar line, and text
        // inserted at that very position lands BEFORE the bar line — inside this
        // measure. `| | |` with an `e` typed into the middle bar: the old empty
        // bar [61,63) ended at the insertion point 63, this test read it as
        // untouched (`SourceEnd > Prefix` was false), the splice from the FIRST
        // boundary adopted the whole old tail (7 of 7 measures), and the page
        // drew the bar empty — same bar count, one note gone — until the next
        // edit moved the window elsewhere (the report: "typing an e made the c
        // appear; deleting it made the c vanish"). Hence `>=`. The mirror on the
        // suffix side stays strict: a measure STARTING at the suffix's first
        // character opens with that unchanged bar line, and the window's text
        // precedes it. Pinned by CollectEditResumeTests'
        // InsertionAtAMeasuresClosingBarLine_DoesNotSpliceThatMeasure.
        if (w.Prefix < w.SuffixStart || w.Delta != 0)
        {
            // ⚠️ A CANDIDATE STANDING BEFORE THE WINDOW adopts a tail that WALKS ACROSS
            // it (2026-09-10, session 366, the audit sweep — 88 divergences in 258
            // books, all at container edges the item-span test below cannot see: a
            // note typed after a body's last item, before its first, or between two
            // phrase references). Such a tail is the old node stream; it is still
            // right only when the edit changed no token (trivia: positions shift and
            // nothing else). Anything else declines; candidates PAST the window are
            // unaffected. (Until session 970 a window inside an EXTRA voice of a
            // parallel span was the other exception — the extra voices were re-walked
            // live after the splice; now their measures are part of the tail, so an edit
            // in one is an edit in the tail, and the v2bow whole-walk shape declines.)
            // ⚠️ COUNTED (session 458, the reader's 231-book corpus, eight forward keystrokes a
            // book): this guard is asked 7,016 times a sweep — 3.80 a keystroke — and DECLINES
            // all 7,016. WindowIsTriviaOnly answered true zero times; the token walk behind it
            // ran 891 times (it is memoized per keystroke). A poison that forces the answer TRUE
            // — every one of those 7,016 splices taken — leaves the suite green (8,783) AND the
            // corpus byte-identical (5,824 page hashes) under FORWARD keystrokes.
            // ⚠️ AND YET IT HAS AN OBSERVER (session 523): session 366's sweep rerun with this
            // guard forced open — 36 edit classes at every container edge of every book —
            // diverged nowhere in 259 net books and in 3 edits of 2 books of the owner's corpus,
            // all a note typed after a BY-PART cell's last item (tab-chord.lys, 想い人.lys).
            // The suite's by-section twin of that hole stayed green with the guard open; the
            // corpus's shapes are TrailingGapSpliceTests, which asserts the decline is THIS
            // guard's (so another guard taking over would not keep it green by accident).
            if (ck.NodeStart < w.Prefix
                && !CollectResumePlanner.WindowIsTriviaOnly(WalkProbe!))
                return DeclineSplice("the candidate stands before a content window its tail walks across");

            for (int i = ck.MeasureCount; i < pre.Count; i++)
            {
                // The measure's TEXT REGION, gaps included: from the previous measure's
                // closing bar line (the text after it, up to this measure's first item,
                // is this measure's) to its own closing bar line — or, for a measure
                // with no bar line of its own (the walk's last, a body's trailing
                // bar), to the next measure's start / the walk's furthest read. A
                // neighbour that lies elsewhere in the file (a phrase body) bounds
                // nothing, and the item span stands.
                int left = i > 0 && pre[i - 1].SourceEnd <= pre[i].SourceStart
                    ? pre[i - 1].SourceEnd : pre[i].SourceStart;
                int right = pre[i].SourceEnd;
                if (i + 1 < pre.Count)
                {
                    if (pre[i + 1].SourceStart >= right)
                        right = pre[i + 1].SourceStart;
                }
                else
                {
                    // The walk's furthest read bounds the last measure's trailing gap —
                    // including an extra voice's text, which the primary measures never
                    // own but the span tracks in the tail do (session 970).
                    right = Math.Max(right, endCk.MaxSourceRead);
                }
                if (left < w.SuffixStart && right >= w.Prefix)
                    return DeclineSplice("a tail measure straddles the window");
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
                    return DeclineSplice("a repetition in the tail copies an original re-walked live");
            }
        }

        // A section the tail PLAYS AGAIN (the form's `A |: A … :| A`) is walked from its
        // body's text wherever that body stands — usually ABOVE the candidate, so the
        // measure-span test above cannot see it: a replayed measure's SourceStart is the
        // form's bar or the previous play's end, its items cite the body. The tail's
        // measures ARE the old walk of that body, so a window inside it makes them stale.
        // MEASURED (user report 2026-09-29, C:\tmp\diagram.lys): `chord(C x35550)1@chord()`
        // in a section the form plays six times; stepping the shape (Ctrl+Shift+↑) redrew
        // the first play's diagram and the splice adopted the other five with the old
        // shape — window [84,87), the replays' measures [124,99) / [102,99), items at 74.
        // Each play appends the section's STRUCTURE read (ProcessSection), so the reads in
        // the tail's range name exactly the sections it plays again; the structure test
        // itself is by shape, never by span (HeaderRead.Structure says why), and the span
        // test here is the suffix side's own. A trivia-only window changes no token and is
        // let through, as the candidate-before-window guard lets it.
        if ((w.Prefix < w.SuffixStart || w.Delta != 0) && rec.HeaderReads is { } tailReads)
        {
            for (int i = ck.HeaderReadCount; i < endCk.HeaderReadCount && i < tailReads.Count; i++)
            {
                var read = tailReads[i];
                if (read.Structure != null
                    && read.Span.Start < w.SuffixStart && read.Span.End >= w.Prefix
                    && !CollectResumePlanner.WindowIsTriviaOnly(WalkProbe!))
                    return DeclineSplice("a section the tail plays again holds the window");
            }
        }

        if (!SuffixStateMatches(ck, builder, w))
            return false; // SuffixStateMatches names the field

        // The adopted tail contains section spacer padding whose COUNT is the
        // canonical section bar count — a function of EVERY part's cell text,
        // invisible to the per-position checks. Verified once per collect
        // against the recording's memo (HANDOFF §1 ⒭: Δm in another part).
        var probe = WalkProbe!;
        probe.CanonicalBarsVerified ??= CanonicalBarsMatch(plan.Source);
        if (probe.CanonicalBarsVerified != true)
            return DeclineSplice("a canonical section bar count changed (another part's cell)");

        // Layer 4's suffix half (lazy, memoized once per collect): the parse
        // agreements that make an adopted tail trustworthy at all — see
        // CollectResumePlanner.ParseAgreementsHold. Placed after the cheap
        // per-walk guards so a book that always declines never pays it.
        if (!CollectResumePlanner.ParseAgreementsHold(probe))
            return DeclineSplice("the parse agreements do not hold for the suffix");

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
                    return DeclineSplice("a tail measure holds a position inside the window");
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
                    return DeclineSplice("a side-table entry holds a position inside the window");
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
                return DeclineSplice("a tail volta holds a position inside the window");
            voltaTail.Add((v.Item1, v.Item2, v.Item3, v.Item4, vp));
        }

        // The span tracks of the adopted tail (session 970): the children's measures are
        // adopted as position-shifted COPIES exactly as the primary's tail measures are
        // (a position inside the window declines), and the node is re-resolved on the
        // new tree for its readers. Until session 970 the span was re-walked live after
        // the splice; now its voices' output is part of the tail, so an edit inside an
        // extra voice can no longer be spliced across (the straddle test above sees it
        // through the primary's last measure's read extent).
        var spanTail = new List<SpanTrack>(endCk.SpanTrackCount - ck.SpanTrackCount);
        for (int i = ck.SpanTrackCount; i < endCk.SpanTrackCount; i++)
        {
            var span = rec.SpanTracks![i];
            if (identity)
            {
                spanTail.Add(span);
                continue;
            }
            if (_root == null
                || CollectTailShifter.ResolveShifted(_root, span.Node, w)
                    is not ParallelExpressionSyntax resolved)
                return DeclineSplice("a tail parallel span does not resolve on the new tree");
            var shiftedMeasures = new List<Measure>(span.Measures.Count);
            foreach (var m in span.Measures)
            {
                if (CollectTailShifter.ShiftMeasure(m, w) is not { } sm)
                    return DeclineSplice("a tail parallel span's measure holds a position inside the window");
                shiftedMeasures.Add(sm);
            }
            spanTail.Add(span with { Node = resolved, Measures = shiftedMeasures });
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
                return DeclineSplice("a tail resolved spelling does not re-key onto the new tree");
            spellTail.Add((rekeyed, resolvedMembers));
        }

        var endMeta = endCk.Meta.Clone();
        if (!identity && !ShiftMetaPositions(endMeta, w))
            return DeclineSplice("an end-of-walk header position lies inside the window");

        var shiftedEndBuilder = endCk.Builder;
        if (!identity)
        {
            if (!w.TryShift(shiftedEndBuilder.SectionLabelPosition, out int endLabelPos)
                || !w.TryShift(shiftedEndBuilder.MeasureSourceStart, out int endSourceStart))
                return DeclineSplice("an end-of-walk builder position lies inside the window");
            Measure? endLast = null;
            if (shiftedEndBuilder.LastMeasure is { } last)
            {
                endLast = CollectTailShifter.ShiftMeasure(last, w);
                if (endLast == null)
                    return DeclineSplice("the end-of-walk last measure holds a position inside the window");
            }
            shiftedEndBuilder = shiftedEndBuilder with
            {
                SectionLabelPosition = endLabelPos,
                MeasureSourceStart = endSourceStart,
                LastMeasure = endLast,
            };
        }

        // --- commit: jump to the recorded end of the walk ---
        endCk.Octave.Restore(_v.Octave);
        _v.Meta.CopyFrom(endMeta);
        _v.DefaultDuration = endCk.DefaultDuration;
        _v.DefaultDots = endCk.DefaultDots;
        _v.AmbientTonicStep = endCk.AmbientTonicStep;
        _v.AmbientTonicAlter = endCk.AmbientTonicAlter;
        _v.AmbientTonicValid = endCk.AmbientTonicValid;
        _openingKeyOverride = endCk.OpeningKeyOverride;
        _v.TremoloRepeatCount = endCk.TremoloRepeatCount;
        _v.TremoloPairShape = endCk.TremoloPairShape;
        _v.TremoloPairFirst = endCk.TremoloPairFirst;
        _sectionActiveGrobProps.Clear();
        foreach (var prop in endCk.SectionActiveGrobProps)
            _sectionActiveGrobProps.Add(prop);
        // Jump the journaled maps to the recorded end of the walk (names and
        // measure indices carry no source positions — nothing to shift).
        RestoreKeyLog(plan.Source, rec.StartKeyLogCount, endCk.KeyLogCount);
        RestoreSectionStartLog(plan.Source, rec.StartSectionStartLogCount, endCk.SectionStartLogCount);
        RestoreAccidentalMemory(endCk.Accidentals);

        for (int t = 0; t < dst.Length; t++)
            foreach (var entry in tailSlices[t])
                dst[t].Add(entry);
        _v.PendingInlineVoltas.AddRange(voltaTail);
        _spanTracks.AddRange(spanTail);
        foreach (var (node, resolvedMembers) in spellTail)
        {
            if (node is NoteSyntax n)
                _resolvedNotes[n] = resolvedMembers[0];
            else
                _resolvedChordMembers[(ChordSyntax)node] = resolvedMembers;
        }

        var measures = builder.MeasuresSnapshot();
        measures.AddRange(tailMeasures);
        // ⚠️ AN EMPTY TAIL ADOPTS NO MEASURE, so the end-of-walk LastMeasure is the RECORDED
        // copy of the live boundary measure — the edited text's own, which the rewrite guard
        // above already proved the recorded tail never touched. Restoring it overwrote the
        // live bar with the recording's: MEASURED (session 596, a random edit of the tracked
        // key-signature-space.lys, `@stemUp` → `@stemUpp` in the walk's last bar), the note
        // kept the stem direction its deleted annotation had forced.
        if (tailMeasures.Count == 0)
            shiftedEndBuilder = shiftedEndBuilder with { LastMeasure = null };
        builder.Restore(shiftedEndBuilder, measures);

        _suffixSpliced = true;
        plan.SplicedMeasures = pre.Count - ck.MeasureCount;
        plan.LastSpliceDecline = null;
        return true;
    }

    /// <summary>The candidate <see cref="TrySpliceSuffix"/> is judging (its recorded
    /// node start), so a decline can be filed against it.</summary>
    private int _spliceCandidateStart;

    /// <summary>The one exit of every splice guard: names the reason on the plan
    /// (<see cref="VoiceResumePlan.LastSpliceDecline"/>; every candidate's when
    /// <see cref="CollectWalkProbe.RecordSpliceDeclines"/> is on) and returns false.
    /// Reasons are literals on purpose — a decline is the common case at most
    /// boundaries of most keystrokes, and formatting one would be paid with nobody
    /// reading it.</summary>
    private bool DeclineSplice(string why)
    {
        var plan = _suffixPlan!;
        plan.LastSpliceDecline = why;
        if (WalkProbe!.RecordSpliceDeclines)
            (plan.SpliceDeclines ??= new()).Add((_spliceCandidateStart, why));
        return false;
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
            return DeclineSplice("measure count differs");
        if (_sectionStartMeasureForResume != ck.SectionStartMeasure)
            return DeclineSplice("section start measure differs");

        // Builder cross-measure state: the WHOLE checkpoint record, less the three
        // fields the boundary measure legitimately owns — LastMeasure is the edited
        // text's own (its content differs from the recording; what the tail needs
        // from it is only that it will not be rewritten, checked by the caller), and
        // the two positions are compared through the window map below. Record
        // equality on purpose: a field captured by MeasureBuilder.Capture is compared
        // here without being named. The field-by-field spelling this replaced named
        // eleven of fifteen (session 396): SenzaMisura, FrozenPosition and
        // LogicalCount were skipped, and a cadenza's frozen clock reading — an edit
        // BEFORE the `time none` — spliced the recorded bars with the old reading
        // (Measure.UnmeteredPosition) while every named field matched.
        var live = builder.Capture();
        var recB = ck.Builder;
        static MeasureBuilder.BuilderCheckpoint Comparable(MeasureBuilder.BuilderCheckpoint b)
            => b with { LastMeasure = null, SectionLabelPosition = 0, MeasureSourceStart = 0 };
        if (Comparable(live) != Comparable(recB))
            return DeclineSplice("builder state differs");
        if (!w.TryShift(recB.SectionLabelPosition, out int labelPos)
            || live.SectionLabelPosition != labelPos)
            return DeclineSplice("section label position differs");
        if (!w.TryShift(recB.MeasureSourceStart, out int sourceStart)
            || live.MeasureSourceStart != sourceStart)
            return DeclineSplice("measure source start differs");

        if (OctaveCheckpoint.Capture(_v.Octave) != ck.Octave)
            return DeclineSplice("octave frame differs");
        if (!MetaMatchesShifted(ck.Meta, w))
            return DeclineSplice("metadata differs");
        if (_v.DefaultDuration != ck.DefaultDuration || _v.DefaultDots != ck.DefaultDots)
            return DeclineSplice("default duration differs");
        if (_v.AmbientTonicStep != ck.AmbientTonicStep
            || _v.AmbientTonicAlter != ck.AmbientTonicAlter
            || _v.AmbientTonicValid != ck.AmbientTonicValid)
            return DeclineSplice("ambient tonic differs");
        if (_openingKeyOverride != ck.OpeningKeyOverride)
            return DeclineSplice("opening key override differs");
        if (_v.TremoloRepeatCount != ck.TremoloRepeatCount
            || _v.TremoloPairShape != ck.TremoloPairShape
            || _v.TremoloPairFirst != ck.TremoloPairFirst)
            return DeclineSplice("tremolo state differs");
        // The accidental memory as the rules read it (re-based, so an inserted note
        // before the boundary does not decline every later one). Under `modern` the
        // edited bar's trace is dropped at the next bar line (the style's memory
        // horizon) and the comparison reconverges; under `noReset` nothing is ever
        // dropped, so a new pitch in the window declines until the tail rewrites
        // every entry it touched — reuse lost, never correctness.
        if (!CaptureAccidentalMemory().SameAs(ck.Accidentals))
            return DeclineSplice("accidental memory differs");

        if (_sectionActiveGrobProps.Count != ck.SectionActiveGrobProps.Count
            || !_sectionActiveGrobProps.SetEquals(ck.SectionActiveGrobProps))
            return DeclineSplice("section-active grob properties differ");

        // The journaled maps compare as JOURNAL PREFIXES against the source's logs
        // (the checkpoint holds watermarks, not copies). Same-event-sequence implies
        // same map state; the very rare converse (equal maps from a different event
        // order) declines, which costs reuse, never correctness.
        // Below this walk's entry counts the journals are the live collect's earlier
        // walks' (VoiceWalkRecording.StartKeyLogCount) — the recording's copies are
        // not the reference; the walk-entry validation pinned their lengths.
        var srcCollector = _suffixPlan!.Source;
        var srcRec = _suffixPlan.Recording;
        if (_keyByMeasureLog.Count != ck.KeyLogCount)
            return DeclineSplice("key journal length differs");
        for (int i = srcRec.StartKeyLogCount; i < ck.KeyLogCount; i++)
            if (_keyByMeasureLog[i] != srcCollector._keyByMeasureLog[i])
                return DeclineSplice("key journal entry differs");
        if (_sectionStartLog.Count != ck.SectionStartLogCount)
            return DeclineSplice("section start journal length differs");
        for (int i = srcRec.StartSectionStartLogCount; i < ck.SectionStartLogCount; i++)
        {
            var (liveName, liveStart) = _sectionStartLog[i];
            var (recName, recStart) = srcCollector._sectionStartLog[i];
            if (liveStart != recStart
                || !string.Equals(liveName, recName, StringComparison.Ordinal))
                return DeclineSplice("section start journal entry differs");
        }

        // Watermark equality = "the window produced exactly the recorded item
        // counts", which is what makes the adopted tail slices land at the same
        // indices — and is the Δm≠0 bail for this walk's own measures/items.
        var tables = CumulativeSideTables();
        for (int t = 0; t < tables.Length; t++)
            if (tables[t].Count != ck.TableCounts[t])
                return DeclineSplice("a side-table count differs");

        if (_v.PendingInlineVoltas.Count != ck.PendingInlineVoltaCount)
            return DeclineSplice("pending inline volta count differs");
        for (int i = 0; i < _v.PendingInlineVoltas.Count; i++)
        {
            var recorded = planVolta(i);
            if (!w.TryShift(recorded.Item5, out int pos))
                return DeclineSplice("a pending inline volta lies inside the window");
            var liveVolta = _v.PendingInlineVoltas[i];
            if (liveVolta.Item1 != recorded.Item1 || liveVolta.Item2 != recorded.Item2
                || !string.Equals(liveVolta.Item3, recorded.Item3, StringComparison.Ordinal)
                || liveVolta.Item4 != recorded.Item4 || liveVolta.Item5 != pos)
                return DeclineSplice("a pending inline volta differs");
        }

        if (_spanTracks.Count != ck.SpanTrackCount)
            return DeclineSplice("parallel span count differs");
        for (int i = 0; i < _spanTracks.Count; i++)
        {
            var liveSpan = _spanTracks[i];
            var recordedSpan = _suffixPlan!.Recording.SpanTracks![i];
            if (liveSpan.StartMeasure != recordedSpan.StartMeasure || liveSpan.Slot != recordedSpan.Slot
                || liveSpan.VoiceCount != recordedSpan.VoiceCount
                || liveSpan.Measures.Count != recordedSpan.Measures.Count)
                return DeclineSplice("a parallel span's start or shape differs");
            if (!w.TryShift(recordedSpan.Node.FullSpan.Start, out int nodeStart)
                || liveSpan.Node.FullSpan.Start != nodeStart
                || liveSpan.Node.FullSpan.End - liveSpan.Node.FullSpan.Start
                    != recordedSpan.Node.FullSpan.End - recordedSpan.Node.FullSpan.Start)
                return DeclineSplice("a parallel span's node differs");
        }

        return true;

        (int, int, string, bool, int) planVolta(int i)
            => _suffixPlan!.Recording.PendingInlineVoltas![i];
    }

    /// <summary>Compares the live <see cref="MetadataState"/> against a recorded
    /// one, the eight header positions through the window map. All other fields
    /// are value scalars/strings.</summary>
    private bool MetaMatchesShifted(MetadataState rec, in CollectTailShifter.Window w)
    {
        if (!w.TryShift(rec.TitlePosition, out int title) || _v.Meta.TitlePosition != title
            || !w.TryShift(rec.ComposerPosition, out int composer) || _v.Meta.ComposerPosition != composer
            || !w.TryShift(rec.SubtitlePosition, out int subtitle) || _v.Meta.SubtitlePosition != subtitle
            || !w.TryShift(rec.PoetPosition, out int poet) || _v.Meta.PoetPosition != poet
            || !w.TryShift(rec.TimePosition, out int time) || _v.Meta.TimePosition != time
            || !w.TryShift(rec.KeyPosition, out int key) || _v.Meta.KeyPosition != key
            || !w.TryShift(rec.ClefPosition, out int clef) || _v.Meta.ClefPosition != clef
            || !w.TryShift(rec.TempoPosition, out int tempo) || _v.Meta.TempoPosition != tempo
            || !w.TryShift(rec.TempoUnitPosition, out int unit) || _v.Meta.TempoUnitPosition != unit
            || !w.TryShift(rec.TempoCountPosition, out int count) || _v.Meta.TempoCountPosition != count
            || !w.TryShift(rec.SwingPosition, out int swing) || _v.Meta.SwingPosition != swing
            || !w.TryShift(rec.SwingValuePosition, out int swingValue) || _v.Meta.SwingValuePosition != swingValue)
            return false;

        return _v.Meta.Title == rec.Title
            && _v.Meta.Composer == rec.Composer
            && _v.Meta.Subtitle == rec.Subtitle
            && _v.Meta.Poet == rec.Poet
            && _v.Meta.Fonts.Equals(rec.Fonts)
            && _v.Meta.Paper.Equals(rec.Paper)
            && _v.Meta.LayoutPlan.Equals(rec.LayoutPlan)
            && _v.Meta.Tempo == rec.Tempo
            && _v.Meta.TempoText == rec.TempoText
            && _v.Meta.TempoBeatUnit == rec.TempoBeatUnit
            && _v.Meta.TempoDots == rec.TempoDots
            && _v.Meta.SwingSubdivision == rec.SwingSubdivision
            && _v.Meta.TimeBeats == rec.TimeBeats
            && _v.Meta.TimeBeatsText == rec.TimeBeatsText
            && _v.Meta.TimeGrouping == rec.TimeGrouping
            && _v.Meta.TimeSenzaMisura == rec.TimeSenzaMisura
            && _v.Meta.TimeBeatType == rec.TimeBeatType
            && _v.Meta.KeySharps == rec.KeySharps
            && _v.Meta.KeyCustom == rec.KeyCustom
            && _v.Meta.InitialKeyCustom == rec.InitialKeyCustom
            && _v.Meta.InitialKeySharps == rec.InitialKeySharps
            && _v.Meta.KeyTonicStep == rec.KeyTonicStep
            && _v.Meta.KeyTonicAlter == rec.KeyTonicAlter
            && _v.Meta.Clef == rec.Clef
            && _v.Meta.InitialClef == rec.InitialClef;
    }

    /// <summary>Re-homes a cloned <see cref="MetadataState"/>'s eight header
    /// positions through the window map (the splice's end-state jump); false
    /// when one lies inside the dirty window.</summary>
    private static bool ShiftMetaPositions(MetadataState meta, in CollectTailShifter.Window w)
    {
        if (!w.TryShift(meta.TitlePosition, out int title)
            || !w.TryShift(meta.ComposerPosition, out int composer)
            || !w.TryShift(meta.SubtitlePosition, out int subtitle)
            || !w.TryShift(meta.PoetPosition, out int poet)
            || !w.TryShift(meta.TimePosition, out int time)
            || !w.TryShift(meta.KeyPosition, out int key)
            || !w.TryShift(meta.ClefPosition, out int clef)
            || !w.TryShift(meta.TempoPosition, out int tempo)
            || !w.TryShift(meta.TempoUnitPosition, out int unit)
            || !w.TryShift(meta.TempoCountPosition, out int count)
            || !w.TryShift(meta.SwingPosition, out int swing)
            || !w.TryShift(meta.SwingValuePosition, out int swingValue))
            return false;
        meta.TempoUnitPosition = unit;
        meta.TempoCountPosition = count;
        meta.SwingPosition = swing;
        meta.SwingValuePosition = swingValue;
        meta.TitlePosition = title;
        meta.ComposerPosition = composer;
        meta.SubtitlePosition = subtitle;
        meta.PoetPosition = poet;
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
