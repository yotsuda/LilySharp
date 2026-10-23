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
/// save/restore of the collector's fields around each sub-walk). The branches now run together,
/// and the collector's VOICE state (docs/moment-walk-design.md §7, V) is one object per branch
/// (<see cref="BranchState"/>) that the walk reads through <c>_v</c>: a switch points <c>_v</c>
/// at the branch's (stage ⑤b, session 971; stage ⑤a copied the fields in and out instead).
/// Byte-identical by measurement (sessions 970–971's sweeps), not by argument: the branches'
/// own walks are untouched; what changed is WHEN they run and WHERE their state lives.
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
            /// <summary>The moment the branch is parked at, in the staff's bars (every builder
            /// counts its bars from the staff's, <see cref="MeasureBuilder.MeasureIndexBase"/>).</summary>
            public (int Measure, Fraction Position) Target;
            /// <summary>True while the branch is parked on a <c>key</c> change: the driver resumes
            /// it before the branches parked on sounding items at the same moment, so the key is
            /// the staff's for every head of the moment (<see cref="ReachAsync"/>).</summary>
            public bool PendingKey;
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
        /// <param name="keyChange">True when the item waiting to be added is a <c>key</c> change.
        /// At its moment the driver resumes a branch parked on one BEFORE the branches parked on
        /// sounding items, whatever their index — LilyPond hears the key event before any head of
        /// the timestep is judged (lily/key-engraver.cc:164-202 Key_engraver::read_event sets
        /// keyAlterations as the event arrives; lily/accidental-engraver.cc:495-500
        /// Accidental_engraver::process_music reads it after). Scoped to the key with stage ⑥;
        /// any other item keeps the branches' index order.</param>
        public ReachAwaitable ReachAsync(MeasureBuilder builder, bool keyChange = false)
        {
            Reaches++;
            if (_spans.Count == 0)
                return new ReachAwaitable(this, completed: true);
            var span = _spans.Peek();
            var branch = span.Current;
            if (branch == null || span.Branches.Count <= 1)
                return new ReachAwaitable(this, completed: true);
            var target = (builder.CurrentMeasureIndex, builder.CurrentMoment);
            if (Compare(target, span.Now) <= 0)
                return new ReachAwaitable(this, completed: true);
            branch.Target = target;
            branch.PendingKey = keyChange;
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
    // one place a moment advances is a place the clock is asked (a grep for `builder.Add` in
    // the walk should find none). Since session 971 the clock is asked FIRST at the head of
    // ProcessMusicNode / OpenSoundingItem, before anything about the node is decided (so a
    // head reads the key a sibling wrote at the same moment); the funnel's ask is then
    // answered at once, and parks only for a member the node emits at a later moment.

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
    /// The collector's VOICE state (docs/moment-walk-design.md §7, V): everything the walk owns
    /// per voice, as one object. The collector reads it through its <c>_v</c> field — the
    /// primary's for the whole collect, and inside a span the running branch's, which the span's
    /// driver points <c>_v</c> at on every switch (nothing is copied). A child branch starts from
    /// <see cref="ForChild"/> — the state the sequential sub-walk used to be given: the frame,
    /// note-value default and metadata the span opened in, its own cursor, and nothing pending.
    /// The field initializers are the primary's opening values (what the collector's own fields
    /// held until session 971) and a child's too, where ForChild does not say otherwise.
    /// </summary>
    internal sealed class BranchState
    {
        public CollectionCursor Cursor;

        // The relative-octave chain plus the part transpose that composes on top of
        // it, bundled into one named collaborator (see OctaveContext). The main walk
        // drives this in place on every note / chord / grace / tuplet. A child branch
        // gets its own (ForChild), so inside a span every voice has its own frame.
        public OctaveContext Octave = new();

        // Piece-level metadata (title/composer/tempo/time/key/clef + header source
        // positions) grouped into one owner. See MetadataState. A child branch gets a
        // clone (ForChild); the key part of it is written across the branches by
        // ShareKeyWithSpanBranches (stage ⑥).
        public MetadataState Meta = new();

        // Default (sticky) duration: an undurated note/rest/chord takes the WHOLE
        // previous duration — dots included — so the pair travels together.
        // LILYPOND-REF: lily/parser.yy:3505-3514 optional_notemode_duration —
        //   default_duration_ is a full Duration (log AND dots). Until 2026-08-07 only
        //   the value stuck and the dots silently reset to 0 (`r8. r` lost its dot,
        //   dot-rest-beam-trigger.ly), while the SEMANTIC walk (MeasureDurations) and
        //   the MIDI/MusicXML exporters carried ToFraction() with dots — the drawing
        //   walk was the one liar.
        public Fraction DefaultDuration = Fraction.Quarter;
        public int DefaultDots;

        // Pending grace notes to attach to the next main note
        public GraceExpressionSyntax? PendingGrace = null;

        // Where the measure stood when the grace was written: a change added after this point and
        // before the grace's body is walked was WRITTEN AFTER the grace (MusicItem.WrittenAfterGrace).
        // The clef too: the grace is engraved in the clef in force where it was WRITTEN, not in a clef
        // written between it and its main note (OpenSoundingItem).
        public (int Measure, int ItemCount, string? Clef) PendingGraceAt;

        // Grace-note infos of the just-collected leading grace group, stamped onto the
        // main note/chord so the spacing can reserve space in front of its column.
        public ImmutableArray<GraceColumnInfo> PendingLeadingGrace = ImmutableArray<GraceColumnInfo>.Empty;

        /// <summary>
        /// How deep into <c>grace { }</c> bodies this walk currently is. Non-zero means the items
        /// being produced belong to GRACE TIME: they take no measure duration, they carry
        /// <see cref="MusicItem.GraceTime"/>, and the annotation collectors stand down because the
        /// grace group is still drawn from its derived <see cref="GraceNoteItem"/> and nothing
        /// would place them (HANDOFF §2 U8 ⒝1).
        /// </summary>
        /// <remarks>
        /// ⚠️ THE COLLECTOR'S COPY OF <c>MeasureBuilder.InGraceTime</c>, and the two are not
        /// redundant: the builder's decides the CLOCK, at the one place that owns it; this one
        /// gates walk-side work the builder never sees. They are entered and left together in
        /// <c>ProcessGraceRegion</c>, and <see cref="WalkCarriesNothing"/> refuses a checkpoint
        /// while either is open, exactly as it does for <see cref="CueDepth"/>.
        /// </remarks>
        public int GraceDepth = 0;

        /// <summary>
        /// How many <c>cue { … }</c> regions enclose the item being collected. A cue is a
        /// REGION in LilyPond — the size comes from the CueVoice context's <c>fontSize = #-4</c>
        /// and nothing is attached to a note — so this depth, not an annotation, is what makes
        /// an item a cue. Nesting is rejected (Diagnostic LYS4013), so in practice it is 0 or 1;
        /// it is a depth rather than a flag so an unbalanced walk cannot leave it stuck on.
        /// See docs/cue-context-design.md.
        /// </summary>
        public int CueDepth = 0;

        /// <summary>
        /// True between entering a <c>cue { … }</c> region and the first note or chord it emits —
        /// the one item that gets <see cref="MusicItem.BeginsCueRegion"/>. Set on entry and cleared
        /// BOTH by that item and on the way out, so outside a region it is always false and a
        /// checkpoint never carries it (<see cref="WalkCarriesNothing"/> already refuses to stand
        /// inside a region at all).
        /// </summary>
        public bool CueRegionPending = false;

        /// <summary>
        /// Non-zero while the walk is re-playing a <c>repeat percent</c> body for an iteration
        /// that a % sign (or a double sign) stands for. LilyPond never plays those iterations:
        /// its iterator hands the context ONE percent event in place of the music, so nothing
        /// the body writes — no slur, no tie, no script, no dynamic — exists there at all.
        /// LILYPOND-REF: lily/percent-repeat-iterator.cc:75-111 Percent_repeat_iterator::next_element —
        ///   for every repetition after the first, <c>make_music_by_name (event_type_)</c> is
        ///   reported and the body's music is not.
        /// </summary>
        /// <remarks>
        /// ⚠️⚠️ NOTHING RAISES THIS DEPTH ANY MORE, and the reason is that the halfway house it
        /// named is gone. Lily# used to re-walk the body for every repetition and hide the notes
        /// by measure, keeping this depth to drop what RIDES those notes — a slur, a tie, a
        /// script, a dynamic — because the engravers that draw those never ask which measures are
        /// covered (measured 2026-09-02: scratch/ベースタブLy/tab-percent.lys drew the slur a
        /// second time over the % sign, scratch/p320/percent-dyn.lys drew the <c>p</c>, the accent
        /// and the <c>f</c> under it). Session 332 measured what the kept notes cost in SPACING —
        /// a repeated bar came out as wide as the music underneath it, 13.11 ss against
        /// LilyPond's 5.51 (scratch/p332/t7) — and replaced the re-walk with the spacers the beat
        /// slash already used, so now no repetition writes a note and there is nothing left to
        /// drop. The gate is honoured by absence rather than by a flag.
        /// <para>
        /// ⚠️ KEPT, NOT DELETED, because deletion is the user's call and because the readers below
        /// state the rule they enforce: <see cref="WithoutBowsUnderPercent"/>, the annotation gates
        /// in <c>MeasureCollector.Annotations</c>, and the resume gate. A shape that has to re-walk
        /// a body some day re-arms them by raising this depth again. Until then they are correct
        /// and unreachable, which is why no test can pin them.
        /// </para>
        /// </remarks>
        public int PercentCoveredDepth = 0;

        // The phrases the walk is inside, pushed and popped by the named reset/end markers
        // (ProcessMusicNodeSequence). A cue region gathers its body when it is walked, after the
        // enclosing expansion's cycle guard is gone, so it seeds its own guard from this list.
        public List<string> OpenPhrases = new();

        // …and where each of them is referenced (the reset marker's call site), outermost first:
        // the address WrittenBarAt finds the written bar of music inlined from a phrase body by
        // (MeasureModel.Split's index). Never open at a checkpoint (WalkCarriesNothing asks that
        // no phrase is open), so no checkpoint carries it.
        public List<int> PhraseCallSites = new();

        // Saved transpose targets around phrase-reference expansions (a stack so a
        // phrase referenced inside another restores cleanly). Pushed at the reset
        // marker, popped at the paired phrase-end marker.
        public Stack<(int step, int alt, int oct)?> PhraseTransposeSaves = new();

        // Each open reference's outgoing anchor (bare letter + octave, resolved in
        // the phrase's own frame at entry), handed to the relative chain at exit —
        // the chord rule; null = pitchless body. Pushed/popped alongside the above.
        public Stack<(char Name, int Octave)?> PhraseAnchorSaves = new();

        // Saved absolute-mode anchors (OctaveBase). A reference's trailing marks move
        // the frame the body resolves in; in ABSOLUTE mode the frame IS OctaveBase, so
        // that is what moves. Pushed/popped alongside the above.
        public Stack<int> PhraseAbsoluteBaseSaves = new();

        // Active `repeat tremolo N { … }` transform: the body note prints ONCE at
        // the combined duration with the subdivision's stem slashes.
        public int TremoloRepeatCount = 1;

        // Active two-note tremolo: (display value, display dots, between-beams,
        // gapped-beam count); both notes print at the pair's TOTAL duration and
        // sound half (TimeScale ½). GapCount = how many beams stop short of the
        // stems (0 for a half-note pair — LP's duration_log == 1 exemption).
        public (int Value, int Dots, int Beams, int GapCount)? TremoloPairShape;
        public bool TremoloPairFirst;

        // A slur mark written on an empty chord, waiting for the item that occupies the empty
        // chord's moment. See TakeEmptyChordSlurs.
        public bool PendingEmptyChordSlurStart;
        public bool PendingEmptyChordSlurEnd;

        // ...and where that mark was written, which travels with it: the bow drawn for
        // `<>( c4 d4)` cites the `(` on the empty chord, not the note that carries it.
        public int PendingEmptyChordSlurStartSource;
        public int PendingEmptyChordSlurEndSource;

        /// <summary>The <c>chord(…)</c> member of a <c>&lt;&lt; &gt;&gt;</c> group being played one
        /// note at a time (<see cref="Music.ArpeggioSpread"/>) and the note it plays now; null
        /// outside that member. Keyed by the item's green node — a red node is re-created per read.</summary>
        public (Syntax.InternalSyntax.GreenNode Item, Music.ShapeNote Note)? SpreadNote;

        // Running ambient key tonic (as WRITTEN, before any part transpose) tracked
        // across the voice walk for phrase auto-transpose: a phrase written in the
        // score's home key is shifted to whatever key is in effect where it is
        // referenced. Reset to the home key per voice and at each section boundary,
        // advanced by each mid-stream key change. Invalid = a custom/atonal ambient
        // key (no tonic) → the phrase is placed unshifted.
        public int AmbientTonicStep;
        public int AmbientTonicAlter;
        public bool AmbientTonicValid;

        // Inline volta endings collected during the current voice walk; finalized
        // (and marked closed/open) once the whole voice has been processed.
        public List<(int startMeasure, int endMeasure, string voltaText, bool isClosed, int sourcePosition)> PendingInlineVoltas = new();

        // The accidental memory — LilyPond's `localAlterations`: (diatonic step, octave) →
        // the alteration last engraved for that pitch, the BAR it was engraved in, and the
        // ORDER it was written in. A note prints an accidental when its alteration differs
        // from what the score's style remembers (GetDisplayAccidental).
        // LILYPOND-REF: lily/accidental-engraver.cc:396-420 stop_translation_timestep —
        //   localAlterations entries are ((octave . notename) alter barnum . end-mom), and a
        //   new one is PREPENDED (ly_assoc_prepend_x), which is what Order reproduces: the
        //   any-octave rule reads the FIRST entry with a matching note name, i.e. the most
        //   recently engraved octave (scm/music-functions.scm:1713-1720).
        // At the bar line the map keeps only what a rule can still read: under a style that
        // forgets there (AccidentalStyleSpec.ForgetsAtBar — the default's, and every style
        // whose laziness is 0) it is CLEARED, under a lazier one the entries stamped further
        // back than the laziest rule's reach are dropped (AdvanceAccidentalBar, the style's
        // MemoryHorizon). Observationally the same as carrying every stamp — and what remains
        // is what a checkpoint carries (WalkCheckpoint.Accidentals). ⚠️ The resume gate used
        // to demand the map EMPTY at every boundary instead, which no book under `modern` /
        // `noReset` satisfied after its first note: those books never resumed (session 396).
        public Dictionary<(int step, int octave), (int Alter, int Bar, int Order)> MeasureAccidentals = new();

        // The bar the walk is in, counted from its start, and the order counter above. Only
        // DIFFERENCES of the bar number are read (AccidentalRule.RecentEnough), so counting
        // from 0 per walk answers exactly what LilyPond's absolute measure number answers.
        public int AccidentalBar;
        public int AccidentalOrder;
        public MeasureBuilder? KeyCourtesyBuilder;

        /// <summary>Where the breathing sign the grace body being walked ends on was written
        /// (<c>GraceBodySupport.CarriedBreath</c>), or -1 — the one breath the grace-time
        /// annotation gate lets through (CollectArticulations), so ArticulationEngraver draws it in
        /// the column ProcessGraceRegion adds after the run.</summary>
        public int CarriedGraceBreathStart = -1;

        /// <summary>Where the breathing signs on the grace body's INNER columns were written
        /// (<c>GraceBodySupport.InnerBreaths</c>, session 936), or null — the grace-time annotation
        /// gate lets them through (CollectArticulations), and AddBreathingSignColumn records each
        /// one's column in <see cref="InnerGraceBreathItems"/>.</summary>
        public HashSet<int>? InnerGraceBreathStarts;

        /// <summary>The item index of each grace column an inner breathing sign was written on, and
        /// the sign — read by DeriveGraceColumns into <c>GraceColumnInfo.BreathAfter</c>.</summary>
        public Dictionary<int, ArticulationType>? InnerGraceBreathItems;

        /// <summary>The tuplets written inside the grace body being walked, as ITEM index ranges with
        /// their ratio (innermost first, the order ProcessTuplet closes them) — read once the body is
        /// walked into the columns' beamlet counts (GraceNoteItem.WithBeamletCounts).</summary>
        public List<(int Start, int End, int Actual, int Normal)>? GraceTupletSpans;

        /// <summary>The chord symbols written on the grace body being walked, by the grace column's
        /// item index (session 964) — read by DeriveGraceColumns into
        /// <see cref="GraceColumnInfo.ChordName"/>, the stamp the run's spacing prices.</summary>
        public Dictionary<int, ChordNameItem>? GraceChordNames;

        /// <summary>The chord diagrams on the grace body being walked — an <c>@diagram</c>'s spec
        /// or the shape an <c>@chord</c> chose — by the grace column's item index (session 964);
        /// read by DeriveGraceColumns into <see cref="GraceColumnInfo.FrameSpec"/>, the box the run's
        /// spacing rods against its neighbours.</summary>
        public Dictionary<int, string>? GraceFrameSpecs;
        public bool LastEndsOffTheBar;

        /// <summary>A child branch's opening state: the span's frame, defaults and metadata,
        /// its own cursor, and otherwise the initializers — its own empty lists, nothing
        /// pending — what <c>BuildExtraVoiceTracks</c> installed around each sequential
        /// sub-walk, and what the collector's fields held when those sub-walks ran (after the
        /// primary walk, with every carry quiescent).</summary>
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
            };
        }
    }

    /// <summary>
    /// Stage ⑥, the key only: a <c>key</c> written in one branch of a span is the STAFF's, so
    /// the other branches' later notes read it. <c>ApplyKeySignatureChange</c> calls this once
    /// the running branch's key and tonic are set; it writes the same into every other branch's
    /// state, at every level of nesting. The running branch's own state is <c>_v</c> itself (and
    /// in a nested span the enclosing branch's shares its metadata object), so those are skipped.
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
    /// The moment of the change: LilyPond hears the key event before any head of the timestep is
    /// judged, so a head in a LOWER branch at that very moment reads the new key too. The span's
    /// driver reproduces that by resuming a branch parked on a key change before the branches
    /// parked on sounding items at the same moment (<see cref="VoiceClock.ReachAsync"/>'s
    /// <c>keyChange</c>, session 971; until then that head was judged against the old key).
    /// </para>
    /// </remarks>
    private void ShareKeyWithSpanBranches()
    {
        if (!_clock.SpanOpen)
            return;
        foreach (var span in _clock.OpenSpans)
            foreach (var b in span.Branches)
            {
                if (b.State is not { } s || ReferenceEquals(s.Meta, _v.Meta))
                    continue;
                s.Meta.KeySharps = _v.Meta.KeySharps;
                s.Meta.KeyCustom = _v.Meta.KeyCustom;
                s.AmbientTonicStep = _v.AmbientTonicStep;
                s.AmbientTonicAlter = _v.AmbientTonicAlter;
                s.AmbientTonicValid = _v.AmbientTonicValid;
                // The reset walks the Staff's children in LilyPond (update_local_key_signature's
                // preorder_walk): every voice's memory empties at the key statement.
                s.MeasureAccidentals.Clear();
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
        var spanFrame = _v.Octave.Snapshot();
        var (spanDuration, spanDots) = (_v.DefaultDuration, _v.DefaultDots);
        var spanMeta = _v.Meta.Clone();
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
        var opening = OctaveCheckpoint.Capture(_v.Octave);
        var outerCursor = _v.Cursor;
        _v.Cursor.VoiceScope = 1;
        // spanStart is the STAFF's bar already: every builder counts from the staff's
        // (MeasureBuilder.MeasureIndexBase), a nested span's enclosing one included.
        var children = await RunSpanAsync(
            builder,
            branch0: () => drums != null
                ? EmitDrumVoice(drums.Upper, builder, stemUp: true)
                : ProcessMusicNodeSequence(GatherVoiceMusicNodes(voiceBlocks[0]), builder),
            childCount: voiceCount - 1,
            childState: t => BranchState.ForChild(outerCursor with
            {
                VoiceIndex = outerCursor.VoiceIndex + t + 1,
                VoiceScope = t + 2,
            }, opening, spanFrame, spanDuration, spanDots, spanMeta),
            childBody: t => CollectMeasuresFromNode(
                drums != null ? parallel : voiceBlocks[t + 1],
                applyFilePartial: spanStart == 0,
                leadingOffset: spanOffset,
                logicalIndexBase: MidBarBreaks?.ToLogical(spanStart) ?? spanStart,
                measureIndexBase: spanStart,
                emit: drums != null ? b => EmitDrumVoice(drums.Lower, b, stemUp: false) : null));
        _v.Cursor.VoiceScope = null;
        // The beam stamp (ResolveBeamStemDirections) is NOT taken here but where the tracks are
        // laid (BuildExtraVoiceTracks), after the primary's own: beam ids are handed out in that
        // order, and a resumed collect that adopts a span from its recording must hand out the
        // same ones the full collect does.
        for (int t = 0; t < children.Count; t++)
            _spanTracks.Add(new SpanTrack(parallel, spanStart, t + 1, voiceCount, children[t]));
        if (drums == null)
        {
            _v.Octave.Restore(spanFrame);
            (_v.DefaultDuration, _v.DefaultDots) = (spanDuration, spanDots);
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
    /// past the clock) or finishes; every switch points <c>_v</c> at the branch's state. The branches' tasks
    /// complete on this call stack, so a <c>CollectResumeAbortException</c> thrown inside one
    /// surfaces from the awaits at the end, as it did from the sequential sub-walks.
    /// </remarks>
    private async ValueTask<List<List<Measure>>> RunSpanAsync(
        MeasureBuilder primary, Func<ValueTask> branch0, int childCount,
        Func<int, BranchState> childState, Func<int, ValueTask<List<Measure>>> childBody)
    {
        var span = _clock.OpenSpan((primary.CurrentMeasureIndex, primary.CurrentMoment));
        var b0 = new VoiceClock.Branch { Index = 0 };
        span.Branches.Add(b0);
        var kids = new VoiceClock.Branch[childCount];
        var kidTasks = new ValueTask<List<Measure>>[childCount];
        for (int t = 0; t < childCount; t++)
        {
            kids[t] = new VoiceClock.Branch { Index = t + 1, State = childState(t) };
            span.Branches.Add(kids[t]);
        }

        // Start branch 0 in the collector's live state (which becomes its own), then each
        // child in its own.
        span.Current = b0;
        b0.State = _v;
        b0.Task = branch0();
        b0.Done = b0.Task.IsCompleted;
        for (int t = 0; t < childCount; t++)
        {
            var kid = kids[t];
            _v = kid.State!;
            span.Current = kid;
            kidTasks[t] = childBody(t);
            kid.Done = kidTasks[t].IsCompleted;
        }

        // The loop.
        while (true)
        {
            // The lowest branch standing at the clock — a branch parked on a `key` change
            // before any other (VoiceClock.ReachAsync's keyChange), as LilyPond hears the event
            // before it judges the moment's heads.
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
                    if (b.PendingKey)
                    {
                        pick = b;
                        break;
                    }
                    pick ??= b;
                    continue;
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
            _v = pick.State!;
            span.Current = pick;
            var resume = pick.Continuation!;
            pick.Continuation = null;
            pick.Parked = false;
            resume();
            pick.Done = pick.Index == 0 ? b0.Task.IsCompleted : kidTasks[pick.Index - 1].IsCompleted;
        }

        _v = b0.State!;
        span.Current = null;
        _clock.CloseSpan(span);

        await b0.Task;
        var results = new List<List<Measure>>(childCount);
        for (int t = 0; t < childCount; t++)
            results.Add(await kidTasks[t]);
        return results;
    }
}
