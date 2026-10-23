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
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Svg.Collector;

/// <summary>
/// The voice walk as a coroutine, and the clock the voices of a <c>&lt;&lt; \\ &gt;&gt;</c> span
/// share (docs/moment-walk-design.md §3.1–3.2, stages ③ and ⑤a). Below
/// <see cref="CollectMeasures"/> / <see cref="CollectMeasuresFromNode"/> the walk is
/// <c>async ValueTask</c>, and every place a moment advances — the three <see cref="MeasureBuilder"/>
/// funnels — first asks the staff's clock to reach that moment. Outside a span one voice is on
/// the clock, it is always reached, and the walk never suspends. Inside a span the branches
/// are coroutines on one clock: each runs until its next item lies past the clock, parks
/// there, and the span's driver (<see cref="RunSpanAsync"/>) advances the clock to the
/// earliest parked moment and resumes the branches standing at it, lower branch first —
/// LilyPond's one-moment-at-a-time loop, span by span.
/// </summary>
/// <remarks>
/// <para>
/// LILYPOND-REF: lily/global-context.cc:174-203 Global_context::iterate — advance to the next
/// moment, process every iterator up to it, OneTimeStep; lily/simultaneous-music-iterator.cc:97-115
/// Simultaneous_music_iterator::process — the children at the moment, in their own order.
/// </para>
/// <para>
/// Until session 970 the extra voices of a span were not walked WITH the first: the span was
/// recorded (node, opening frame, note-value default, metadata) and voices 2..N were re-walked
/// one after another once the whole primary walk had finished (<c>BuildExtraVoiceTracks</c>'
/// save/restore of the collector's fields around each sub-walk). The branches now run together
/// and the collector's VOICE state (docs/moment-walk-design.md §7, V) is swapped in and out at
/// every switch (<see cref="BranchState"/>) — the same fields the sequential sub-walks saved and
/// restored, plus the ones they shared only because they ran after the primary walk was done.
/// Byte-identical by measurement (session 970's sweeps), not by argument: the branches' own
/// walks are untouched; what changed is WHEN they run.
/// </para>
/// </remarks>
public sealed partial class MeasureCollector
{
    /// <summary>
    /// The staff's clock. A voice walk asks it before every moment advance; inside an open span
    /// the ask parks the branch when its moment lies past the clock, and the span's driver
    /// resumes it when the clock gets there.
    /// </summary>
    internal sealed class VoiceClock
    {
        /// <summary>One voice of an open span: where its measures start in the staff, the moment it
        /// is parked at, and the continuation that resumes it.</summary>
        internal sealed class Branch
        {
            public required int Index;
            /// <summary>The staff bar the branch's own builder counts its measure 0 from.</summary>
            public required int MeasureBase;
            public (int Measure, Fraction Position) Target;
            public Action? Continuation;
            public bool Parked;
            public bool Done;
            public ValueTask Task;
            public BranchState? State;
        }

        /// <summary>One open span: its branches and its clock. Spans nest (a phrase with a span
        /// referenced inside a voice of another), so the clock keeps a stack of them.</summary>
        internal sealed class Span
        {
            public readonly List<Branch> Branches = new();
            public (int Measure, Fraction Position) Now;
            public Branch? Current;
        }

        private readonly Stack<Span> _spans = new();

        /// <summary>How many times a walk asked to reach a moment (one per funnel call).</summary>
        public int Reaches { get; private set; }

        /// <summary>How many of those asks parked the branch — 0 outside spans and for a span
        /// with one branch.</summary>
        public int Suspensions { get; private set; }

        /// <summary>How many spans were driven (the parallel arms of every walk of the collect).</summary>
        public int Spans { get; private set; }

        /// <summary>True between <see cref="OpenSpan"/> and <see cref="CloseSpan"/>.</summary>
        public bool SpanOpen => _spans.Count > 0;

        internal Span? CurrentSpan => _spans.Count > 0 ? _spans.Peek() : null;

        /// <summary>The open spans, innermost first — for the staff-level state a branch shares
        /// with every other branch on the clock (<see cref="ShareKeyWithSpanBranches"/>).</summary>
        internal IEnumerable<Span> OpenSpans => _spans;

        /// <summary>Opens a span at <paramref name="now"/>, the moment the primary stands at.</summary>
        internal Span OpenSpan((int Measure, Fraction Position) now)
        {
            var span = new Span { Now = now };
            _spans.Push(span);
            Spans++;
            return span;
        }

        internal void CloseSpan(Span span)
        {
            if (_spans.Count == 0 || !ReferenceEquals(_spans.Peek(), span))
                throw new InvalidOperationException("the voice clock's spans closed out of order");
            _spans.Pop();
        }

        /// <summary>A reach the caller answered itself (no span open): counted, nothing else.</summary>
        public void CountReach() => Reaches++;

        /// <summary>
        /// Reaches the moment <paramref name="builder"/> stands at. Completed at once when no span
        /// is open, when the span has one branch, or when the moment is not past the clock;
        /// otherwise the awaiting branch parks until the span's driver brings the clock there.
        /// </summary>
        public ReachAwaitable ReachAsync(MeasureBuilder builder)
        {
            Reaches++;
            if (_spans.Count == 0)
                return new ReachAwaitable(this, completed: true);
            var span = _spans.Peek();
            var branch = span.Current;
            if (branch == null || span.Branches.Count <= 1)
                return new ReachAwaitable(this, completed: true);
            var target = (branch.MeasureBase + builder.CurrentMeasureIndex, builder.CurrentMoment);
            if (Compare(target, span.Now) <= 0)
                return new ReachAwaitable(this, completed: true);
            branch.Target = target;
            return new ReachAwaitable(this, completed: false);
        }

        /// <summary>The awaiting branch's continuation: it parks here.</summary>
        internal void Park(Action continuation)
        {
            var span = _spans.Peek();
            var branch = span.Current ?? throw new InvalidOperationException("a voice walk parked with no branch running");
            branch.Continuation = continuation;
            branch.Parked = true;
            Suspensions++;
        }

        internal static int Compare((int Measure, Fraction Position) a, (int Measure, Fraction Position) b)
        {
            int byMeasure = a.Measure.CompareTo(b.Measure);
            return byMeasure != 0 ? byMeasure : a.Position.CompareTo(b.Position);
        }
    }

    /// <summary>The awaitable <see cref="VoiceClock.ReachAsync"/> returns: a struct, so a reach that
    /// is already complete — every one outside a span — allocates nothing.</summary>
    internal readonly struct ReachAwaitable : ICriticalNotifyCompletion
    {
        private readonly VoiceClock _clock;
        private readonly bool _completed;

        public ReachAwaitable(VoiceClock clock, bool completed)
        {
            _clock = clock;
            _completed = completed;
        }

        public ReachAwaitable GetAwaiter() => this;
        public bool IsCompleted => _completed;
        public void GetResult() { }
        public void OnCompleted(Action continuation) => _clock.Park(continuation);
        public void UnsafeOnCompleted(Action continuation) => _clock.Park(continuation);
    }

    private readonly VoiceClock _clock = new();

    /// <summary>The clock the walks of this collector wait on (counters for the nets and probes).</summary>
    internal VoiceClock Clock => _clock;

    /// <summary>
    /// Runs a walk to completion from a synchronous entry point. Outside a span the walk never
    /// suspends, and inside one the span's driver resumes every branch before the primary's
    /// coroutine is handed back — so a task that is still pending here is a defect, said in words
    /// rather than waited on.
    /// </summary>
    private static T Run<T>(ValueTask<T> walk)
    {
        if (!walk.IsCompleted)
            throw new InvalidOperationException(
                "The voice walk suspended on its clock with no scheduler to resume it (docs/moment-walk-design.md stage 3: one voice never suspends).");
        return walk.GetAwaiter().GetResult();
    }

    /// <summary><see cref="Run{T}"/> for the nets (VoiceWalkCoroutineTests), which hand it a
    /// task of their own making.</summary>
    internal static T RunForTests<T>(ValueTask<T> walk) => Run(walk);

    // The three funnels, gated. The walk calls these — never builder.Add* directly — so the
    // one place a moment advances is the one place the clock is asked (the calls are the
    // coroutine's yield points; a grep for `builder.Add` in the walk should find none).

    // Each wrapper has a synchronous fast path for the common case — no span open, the clock
    // always reached — and awaits only inside a span: an `async` method costs a state machine
    // even when nothing suspends, and the fast path pays none (MEASURED, session 970: the
    // all-async form added 1.5 MB to perf-plain1k's full render, a book with no span at all).

    /// <summary>Reaches the builder's moment, then <see cref="MeasureBuilder.AddItem"/>.</summary>
    private ValueTask AddItemAsync(MeasureBuilder builder, MusicItem item)
    {
        if (!_clock.SpanOpen)
        {
            _clock.CountReach();
            builder.AddItem(item);
            return default;
        }
        return AddItemInSpanAsync(builder, item);
    }

    private async ValueTask AddItemInSpanAsync(MeasureBuilder builder, MusicItem item)
    {
        await _clock.ReachAsync(builder);
        builder.AddItem(item);
    }

    /// <summary>Reaches the builder's moment, then <see cref="MeasureBuilder.AddItemWithoutDuration"/>.</summary>
    private ValueTask AddItemWithoutDurationAsync(MeasureBuilder builder, MusicItem item)
    {
        if (!_clock.SpanOpen)
        {
            _clock.CountReach();
            builder.AddItemWithoutDuration(item);
            return default;
        }
        return AddItemWithoutDurationInSpanAsync(builder, item);
    }

    private async ValueTask AddItemWithoutDurationInSpanAsync(MeasureBuilder builder, MusicItem item)
    {
        await _clock.ReachAsync(builder);
        builder.AddItemWithoutDuration(item);
    }

    /// <summary>Reaches the builder's moment, then <see cref="MeasureBuilder.AddDuration"/>.</summary>
    private ValueTask AddDurationAsync(MeasureBuilder builder, Fraction duration, int sourcePosition)
    {
        if (!_clock.SpanOpen)
        {
            _clock.CountReach();
            builder.AddDuration(duration, sourcePosition);
            return default;
        }
        return AddDurationInSpanAsync(builder, duration, sourcePosition);
    }

    private async ValueTask AddDurationInSpanAsync(MeasureBuilder builder, Fraction duration, int sourcePosition)
    {
        await _clock.ReachAsync(builder);
        builder.AddDuration(duration, sourcePosition);
    }

    // ===== the span's voices: tracks, state, driver =====

    /// <summary>
    /// What one voice of a span produced: its measures, 0-based from the span's bar, to be laid
    /// into staff voice <paramref name="Slot"/>'s full-length track once the walk is done
    /// (<c>BuildExtraVoiceTracks</c>). <paramref name="VoiceCount"/> is how many voices the span
    /// puts on the staff (a merged drum chart one or two, any other span one a block), so the
    /// track count is the maximum over the walk's spans. <paramref name="Node"/> is kept for the
    /// readers that ask which syntax a track came from (the named-voice binding of lyrics, the
    /// resume planner's window guard) — never to re-walk it.
    /// </summary>
    internal sealed record SpanTrack(ParallelExpressionSyntax Node, int StartMeasure, int Slot, int VoiceCount, List<Measure> Measures);

    /// <summary>
    /// The collector's VOICE state (docs/moment-walk-design.md §7, V): everything a branch of a
    /// span owns for itself and the driver swaps at every switch. The lists are swapped by
    /// REFERENCE (a branch keeps its own), the value fields by copy. A child branch starts from
    /// <see cref="ForChild"/> — the state the sequential sub-walk used to be given: the frame,
    /// note-value default and metadata the span opened in, its own cursor, and nothing pending.
    /// </summary>
    internal sealed class BranchState
    {
        public CollectionCursor Cursor;
        /// <summary>The branch's own frame and metadata OBJECTS, swapped by reference: the
        /// collector's <c>_octave</c> / <c>_meta</c> fields point at the running branch's.</summary>
        public OctaveContext Octave = null!;
        public MetadataState Meta = null!;
        public Fraction DefaultDuration;
        public int DefaultDots;
        public GraceExpressionSyntax? PendingGrace;
        public (int Measure, int ItemCount, string? Clef) PendingGraceAt;
        public ImmutableArray<GraceColumnInfo> PendingLeadingGrace;
        public int GraceDepth;
        public int CueDepth;
        public bool CueRegionPending;
        public int PercentCoveredDepth;
        public List<string> OpenPhrases = null!;
        public List<int> PhraseCallSites = null!;
        public Stack<(int step, int alt, int oct)?> PhraseTransposeSaves = null!;
        public Stack<(char Name, int Octave)?> PhraseAnchorSaves = null!;
        public Stack<int> PhraseAbsoluteBaseSaves = null!;
        public int TremoloRepeatCount;
        public (int Value, int Dots, int Beams, int GapCount)? TremoloPairShape;
        public bool TremoloPairFirst;
        public bool PendingEmptyChordSlurStart;
        public bool PendingEmptyChordSlurEnd;
        public int PendingEmptyChordSlurStartSource;
        public int PendingEmptyChordSlurEndSource;
        public (Syntax.InternalSyntax.GreenNode Item, Music.ShapeNote Note)? SpreadNote;
        public int AmbientTonicStep;
        public int AmbientTonicAlter;
        public bool AmbientTonicValid;
        public List<(int startMeasure, int endMeasure, string voltaText, bool isClosed, int sourcePosition)> PendingInlineVoltas = null!;
        public Dictionary<(int step, int octave), (int Alter, int Bar, int Order)> MeasureAccidentals = null!;
        public int AccidentalBar;
        public int AccidentalOrder;
        public MeasureBuilder? KeyCourtesyBuilder;
        public int CarriedGraceBreathStart;
        public HashSet<int>? InnerGraceBreathStarts;
        public Dictionary<int, ArticulationType>? InnerGraceBreathItems;
        public List<(int Start, int End, int Actual, int Normal)>? GraceTupletSpans;
        public Dictionary<int, ChordNameItem>? GraceChordNames;
        public Dictionary<int, string>? GraceFrameSpecs;
        public bool LastEndsOffTheBar;

        /// <summary>A child branch's opening state: the span's frame, defaults and metadata,
        /// its own cursor, its own empty lists, nothing pending — what
        /// <c>BuildExtraVoiceTracks</c> installed around each sequential sub-walk, and what
        /// the collector's fields held when those sub-walks ran (after the primary walk, with
        /// every carry quiescent).</summary>
        public static BranchState ForChild(CollectionCursor cursor, OctaveCheckpoint octave, OctaveSnapshot frame,
            Fraction duration, int dots, MetadataState meta)
        {
            // The sequential sub-walk restored the span's two running octave fields onto the
            // collector's OctaveContext; the rest of the frame is the opening's.
            var own = new OctaveContext();
            octave.Restore(own);
            own.Restore(frame);
            return new BranchState
            {
                Cursor = cursor,
                Octave = own,
                Meta = meta.Clone(),
                DefaultDuration = duration,
                DefaultDots = dots,
                PendingLeadingGrace = ImmutableArray<GraceColumnInfo>.Empty,
                OpenPhrases = new(),
                PhraseCallSites = new(),
                PhraseTransposeSaves = new(),
                PhraseAnchorSaves = new(),
                PhraseAbsoluteBaseSaves = new(),
                TremoloRepeatCount = 1,
                PendingInlineVoltas = new(),
                MeasureAccidentals = new(),
                CarriedGraceBreathStart = -1,
            };
        }
    }

    /// <summary>The collector's voice state, as it stands, written INTO <paramref name="s"/>
    /// (the branch's own record — a switch allocates nothing; the frame and metadata objects
    /// are the branch's and travel by reference).</summary>
    private BranchState CaptureBranch(BranchState? s = null)
    {
        s ??= new BranchState();
        s.Cursor = _cursor;
        s.Octave = _octave;
        s.Meta = _meta;
        s.DefaultDuration = _defaultDuration;
        s.DefaultDots = _defaultDots;
        s.PendingGrace = _pendingGrace;
        s.PendingGraceAt = _pendingGraceAt;
        s.PendingLeadingGrace = _pendingLeadingGrace;
        s.GraceDepth = _graceDepth;
        s.CueDepth = _cueDepth;
        s.CueRegionPending = _cueRegionPending;
        s.PercentCoveredDepth = _percentCoveredDepth;
        s.OpenPhrases = _openPhrases;
        s.PhraseCallSites = _phraseCallSites;
        s.PhraseTransposeSaves = _phraseTransposeSaves;
        s.PhraseAnchorSaves = _phraseAnchorSaves;
        s.PhraseAbsoluteBaseSaves = _phraseAbsoluteBaseSaves;
        s.TremoloRepeatCount = _tremoloRepeatCount;
        s.TremoloPairShape = _tremoloPairShape;
        s.TremoloPairFirst = _tremoloPairFirst;
        s.PendingEmptyChordSlurStart = _pendingEmptyChordSlurStart;
        s.PendingEmptyChordSlurEnd = _pendingEmptyChordSlurEnd;
        s.PendingEmptyChordSlurStartSource = _pendingEmptyChordSlurStartSource;
        s.PendingEmptyChordSlurEndSource = _pendingEmptyChordSlurEndSource;
        s.SpreadNote = _spreadNote;
        s.AmbientTonicStep = _ambientTonicStep;
        s.AmbientTonicAlter = _ambientTonicAlter;
        s.AmbientTonicValid = _ambientTonicValid;
        s.PendingInlineVoltas = _pendingInlineVoltas;
        s.MeasureAccidentals = _measureAccidentals;
        s.AccidentalBar = _accidentalBar;
        s.AccidentalOrder = _accidentalOrder;
        s.KeyCourtesyBuilder = _keyCourtesyBuilder;
        s.CarriedGraceBreathStart = _carriedGraceBreathStart;
        s.InnerGraceBreathStarts = _innerGraceBreathStarts;
        s.InnerGraceBreathItems = _innerGraceBreathItems;
        s.GraceTupletSpans = _graceTupletSpans;
        s.GraceChordNames = _graceChordNames;
        s.GraceFrameSpecs = _graceFrameSpecs;
        s.LastEndsOffTheBar = _lastEndsOffTheBar;
        return s;
    }

    /// <summary>Installs <paramref name="s"/> as the collector's voice state.</summary>
    private void RestoreBranch(BranchState s)
    {
        _cursor = s.Cursor;
        _octave = s.Octave;
        _meta = s.Meta;
        _defaultDuration = s.DefaultDuration;
        _defaultDots = s.DefaultDots;
        _pendingGrace = s.PendingGrace;
        _pendingGraceAt = s.PendingGraceAt;
        _pendingLeadingGrace = s.PendingLeadingGrace;
        _graceDepth = s.GraceDepth;
        _cueDepth = s.CueDepth;
        _cueRegionPending = s.CueRegionPending;
        _percentCoveredDepth = s.PercentCoveredDepth;
        _openPhrases = s.OpenPhrases;
        _phraseCallSites = s.PhraseCallSites;
        _phraseTransposeSaves = s.PhraseTransposeSaves;
        _phraseAnchorSaves = s.PhraseAnchorSaves;
        _phraseAbsoluteBaseSaves = s.PhraseAbsoluteBaseSaves;
        _tremoloRepeatCount = s.TremoloRepeatCount;
        _tremoloPairShape = s.TremoloPairShape;
        _tremoloPairFirst = s.TremoloPairFirst;
        _pendingEmptyChordSlurStart = s.PendingEmptyChordSlurStart;
        _pendingEmptyChordSlurEnd = s.PendingEmptyChordSlurEnd;
        _pendingEmptyChordSlurStartSource = s.PendingEmptyChordSlurStartSource;
        _pendingEmptyChordSlurEndSource = s.PendingEmptyChordSlurEndSource;
        _spreadNote = s.SpreadNote;
        _ambientTonicStep = s.AmbientTonicStep;
        _ambientTonicAlter = s.AmbientTonicAlter;
        _ambientTonicValid = s.AmbientTonicValid;
        _pendingInlineVoltas = s.PendingInlineVoltas;
        _measureAccidentals = s.MeasureAccidentals;
        _accidentalBar = s.AccidentalBar;
        _accidentalOrder = s.AccidentalOrder;
        _keyCourtesyBuilder = s.KeyCourtesyBuilder;
        _carriedGraceBreathStart = s.CarriedGraceBreathStart;
        _innerGraceBreathStarts = s.InnerGraceBreathStarts;
        _innerGraceBreathItems = s.InnerGraceBreathItems;
        _graceTupletSpans = s.GraceTupletSpans;
        _graceChordNames = s.GraceChordNames;
        _graceFrameSpecs = s.GraceFrameSpecs;
        _lastEndsOffTheBar = s.LastEndsOffTheBar;
    }

    /// <summary>
    /// Stage ⑥, the key only: a <c>key</c> written in one branch of a span is the STAFF's, so
    /// the other branches' later notes read it. <c>ApplyKeySignatureChange</c> calls this once
    /// the running branch's key and tonic are set; it writes the same into every other branch's
    /// state, at every level of nesting. The running branch's own record (and, in a nested span,
    /// the enclosing branch's, which shares its metadata object) is skipped: the collector's
    /// fields are the live copy, and the next capture writes them back.
    /// </summary>
    /// <remarks>
    /// <para>
    /// LILYPOND-REF: lily/key-engraver.cc:141-162 — Key_engraver lives in Staff and sets
    /// keyAlterations there; lily/accidental-engraver.cc:495-500 Accidental_engraver::process_music
    /// re-reads keyAlterations every timestep, so every voice of the staff judges its heads
    /// against the one key. The reminder memory (localAlterations) is NOT shared here: the owner
    /// kept the cross-voice reminder on 2026-10-02 (the staff's own rule ⑴ in StaffAccidentals)
    /// and scoped this stage to the key on 2026-10-23 (docs/moment-walk-design.md §3.3, §4 ⑥).
    /// </para>
    /// <para>
    /// Known gap, written by no book in the nets: LilyPond hears the key event before any head of
    /// the timestep is judged, so a head in a LOWER branch at the very moment of the change reads
    /// the new key there; here that branch has already run at the moment (the driver runs the
    /// branches standing at one moment lowest first) and judged its head against the old key.
    /// </para>
    /// </remarks>
    private void ShareKeyWithSpanBranches()
    {
        if (!_clock.SpanOpen)
            return;
        foreach (var span in _clock.OpenSpans)
            foreach (var b in span.Branches)
            {
                if (b.State is not { } s || ReferenceEquals(s.Meta, _meta))
                    continue;
                s.Meta.KeySharps = _meta.KeySharps;
                s.Meta.KeyCustom = _meta.KeyCustom;
                s.AmbientTonicStep = _ambientTonicStep;
                s.AmbientTonicAlter = _ambientTonicAlter;
                s.AmbientTonicValid = _ambientTonicValid;
            }
    }

    /// <summary>
    /// The <c>&lt;&lt; \\ &gt;&gt;</c> span arm of the walk: branch 0 joins the primary stream (this
    /// builder) so measure indices stay continuous, and the other voices — each in its own
    /// builder, 0-based from the span's bar — are walked WITH it on the span's clock
    /// (<see cref="RunSpanAsync"/>); what they produce is kept as <see cref="SpanTrack"/>s for
    /// <c>BuildExtraVoiceTracks</c>.
    /// </summary>
    /// <remarks>
    /// The frame at the span's OPENING is what every voice reads from, and what the music
    /// after the span reads from — a span of simultaneous music does not move the relative
    /// frame. The note-value default travels by the same rule (session 398): every voice opens
    /// at the value in force at the span, and so does the music after it — voice 0's last value
    /// used to leak out, the way its last pitch did before 2026-08-01. The meter is the one IN
    /// FORCE at the span, not the piece's opening one: a mid-piece <c>time</c> re-arms this
    /// builder only (the TimeSignatureChangeItem), and the other voices used to measure their
    /// bars in the opening meter — a 12/8 bar after 4/4 cut voice 2 at one whole note and
    /// dropped the rest (LilySharp-Omr feedback #20). A drum chart whose lines merge: the hands
    /// are branch 0, the feet the one child (MeasureCollector.Drums); any other span one branch
    /// a block.
    /// </remarks>
    private async ValueTask ProcessParallelSpan(ParallelExpressionSyntax parallel, MeasureBuilder builder)
    {
        var voiceBlocks = parallel.Voices.ToList();
        var spanFrame = _octave.Snapshot();
        var (spanDuration, spanDots) = (_defaultDuration, _defaultDots);
        var spanMeta = _meta.Clone();
        // (A `time none` keeps the opening's numbers: the frozen clock has no bar.)
        if (builder.MeterInForce is { Beats: > 0, BeatType: > 0, SenzaMisura: false } inForce)
        {
            spanMeta.TimeBeats = inForce.Beats;
            spanMeta.TimeBeatType = inForce.BeatType;
            spanMeta.TimeBeatsText = inForce.BeatsText;
            spanMeta.TimeGrouping = inForce.Grouping;
        }
        int spanStart = builder.CurrentMeasureIndex;
        var spanOffset = builder.CurrentDuration;
        var drums = parallel.IsDrums
            ? DrumPlanOf(parallel, spanOffset, BarLengthOf(spanMeta), spanDuration, spanDots)
            : null;
        int voiceCount = drums != null ? (drums.Lower.Count > 0 ? 2 : 1) : voiceBlocks.Count;
        if (voiceCount == 0)
            return;
        // Every branch opens in the frame, note-value default and metadata the span opened in;
        // a child's cursor is the span's bar and its slot (what BuildExtraVoiceTracks' sequential
        // sub-walks installed until session 970). Branch 0 is render voice 1: an override in its
        // block scopes to it.
        var opening = OctaveCheckpoint.Capture(_octave);
        var outerCursor = _cursor;
        _cursor.VoiceScope = 1;
        var children = await RunSpanAsync(
            builder,
            branch0: () => drums != null
                ? EmitDrumVoice(drums.Upper, builder, stemUp: true)
                : ProcessMusicNodeSequence(GatherVoiceMusicNodes(voiceBlocks[0]), builder),
            childCount: voiceCount - 1,
            childMeasureBase: spanStart + outerCursor.MetadataMeasureOffset,
            childState: t => BranchState.ForChild(outerCursor with
            {
                MetadataMeasureOffset = spanStart + outerCursor.MetadataMeasureOffset,
                VoiceIndex = outerCursor.VoiceIndex + t + 1,
                VoiceScope = t + 2,
            }, opening, spanFrame, spanDuration, spanDots, spanMeta),
            childBody: t => CollectMeasuresFromNode(
                drums != null ? parallel : voiceBlocks[t + 1],
                applyFilePartial: spanStart + outerCursor.MetadataMeasureOffset == 0,
                leadingOffset: spanOffset,
                logicalIndexBase: MidBarBreaks?.ToLogical(spanStart) ?? spanStart,
                emit: drums != null ? b => EmitDrumVoice(drums.Lower, b, stemUp: false) : null));
        _cursor.VoiceScope = null;
        // The beam stamp (ResolveBeamStemDirections) is NOT taken here but where the tracks are
        // laid (BuildExtraVoiceTracks), after the primary's own: beam ids are handed out in that
        // order, and a resumed collect that adopts a span from its recording must hand out the
        // same ones the full collect does.
        for (int t = 0; t < children.Count; t++)
            _spanTracks.Add(new SpanTrack(parallel, spanStart, t + 1, voiceCount, children[t]));
        if (drums == null)
        {
            _octave.Restore(spanFrame);
            (_defaultDuration, _defaultDots) = (spanDuration, spanDots);
        }
    }

    /// <summary>
    /// Drives one span: branch 0 (<paramref name="branch0"/>, the primary's own builder) and the
    /// children (<paramref name="childBody"/>, each with the state <paramref name="childState"/>
    /// gives it) on one clock, from the moment the primary stands at, until every branch has
    /// finished. Returns the children's measures in slot order. The primary's voice state is
    /// branch 0's when this returns, the span closed.
    /// </summary>
    /// <remarks>
    /// The loop is LilyPond's: pick the lowest branch standing at the clock; none — advance the
    /// clock to the earliest parked moment. A branch runs until it parks (its next item lies
    /// past the clock) or finishes; every switch swaps the voice state. The branches' tasks
    /// complete on this call stack, so a <c>CollectResumeAbortException</c> thrown inside one
    /// surfaces from the awaits at the end, as it did from the sequential sub-walks.
    /// </remarks>
    private async ValueTask<List<List<Measure>>> RunSpanAsync(
        MeasureBuilder primary, Func<ValueTask> branch0, int childCount, int childMeasureBase,
        Func<int, BranchState> childState, Func<int, ValueTask<List<Measure>>> childBody)
    {
        var enclosing = _clock.CurrentSpan?.Current;
        int primaryBase = enclosing?.MeasureBase ?? 0;
        var span = _clock.OpenSpan((primaryBase + primary.CurrentMeasureIndex, primary.CurrentMoment));
        var b0 = new VoiceClock.Branch { Index = 0, MeasureBase = primaryBase };
        span.Branches.Add(b0);
        var kids = new VoiceClock.Branch[childCount];
        var kidTasks = new ValueTask<List<Measure>>[childCount];
        for (int t = 0; t < childCount; t++)
        {
            kids[t] = new VoiceClock.Branch { Index = t + 1, MeasureBase = childMeasureBase, State = childState(t) };
            span.Branches.Add(kids[t]);
        }

        // Start branch 0 in the collector's live state, then each child in its own.
        span.Current = b0;
        b0.Task = branch0();
        b0.State = CaptureBranch();
        b0.Done = b0.Task.IsCompleted;
        for (int t = 0; t < childCount; t++)
        {
            var kid = kids[t];
            RestoreBranch(kid.State!);
            span.Current = kid;
            kidTasks[t] = childBody(t);
            CaptureBranch(kid.State);
            kid.Done = kidTasks[t].IsCompleted;
        }

        // The loop.
        while (true)
        {
            VoiceClock.Branch? pick = null;
            (int Measure, Fraction Position)? earliest = null;
            foreach (var b in span.Branches)
            {
                if (b.Done)
                    continue;
                if (!b.Parked)
                    throw new InvalidOperationException("a span branch is neither done nor parked");
                if (VoiceClock.Compare(b.Target, span.Now) <= 0)
                {
                    pick = b;
                    break;
                }
                if (earliest is not { } e || VoiceClock.Compare(b.Target, e) < 0)
                    earliest = b.Target;
            }
            if (pick == null)
            {
                if (earliest is not { } next)
                    break; // every branch is done
                span.Now = next;
                continue;
            }
            RestoreBranch(pick.State!);
            span.Current = pick;
            var resume = pick.Continuation!;
            pick.Continuation = null;
            pick.Parked = false;
            resume();
            CaptureBranch(pick.State);
            pick.Done = pick.Index == 0 ? b0.Task.IsCompleted : kidTasks[pick.Index - 1].IsCompleted;
        }

        RestoreBranch(b0.State!);
        span.Current = null;
        _clock.CloseSpan(span);

        await b0.Task;
        var results = new List<List<Measure>>(childCount);
        for (int t = 0; t < childCount; t++)
            results.Add(await kidTasks[t]);
        return results;
    }
}
