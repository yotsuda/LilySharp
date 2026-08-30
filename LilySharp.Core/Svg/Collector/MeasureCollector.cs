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

/// <summary>
/// Collects measures from a syntax tree.
/// </summary>
public sealed partial class MeasureCollector
{
    // Section-tracking state grouped into one owner: section declarations by name,
    // part-major cells `part X { section A { … } }` -> (A,X), the first/all
    // expanded-measure starts per section (lyric/chord rows align to them; a
    // reprise like "A2" replays under every start), and rows-only section labels.
    // See SectionState.
    private readonly SectionState _sectionState = new();
    private readonly Dictionary<string, SyntaxNode> _variables = new();
    private FormDeclarationSyntax? _form;
    // A top-level partial N (a GlobalSetting, like time/key): the pickup is a
    // fact of the piece, so it arms EVERY voice's first measure. In-music
    // partial still works per voice (and mid-piece).
    private Semantics.Fraction? _filePartial;
    private string? _voiceName;
    private SyntaxNode? _root;

    /// <summary>
    /// A per-score transpose (from <c>score "Bb" transpose d { ... }</c>) composed on
    /// top of each part's own transpose. Set by the render pipeline before collecting.
    /// </summary>
    public (int step, int alt, int oct)? ScoreTranspose { get; set; }

    /// <summary>
    /// The <c>title</c> / <c>composer</c> the score being collected states for itself
    /// (<c>score sub { title "Violin I" … }</c>). Set by the render pipeline before
    /// collecting, and applied by <see cref="CollectDefinitions"/> after the file-level
    /// walk — so a score that restates nothing inherits the file's header.
    /// </summary>
    public ImmutableArray<MetadataDeclarationSyntax> HeaderOverrides { get; set; }

    /// <summary>
    /// The score's <c>fonts NAME [{ … }]</c> reference, when it wrote one. Set by the
    /// render pipeline before collecting, resolved by <see cref="CollectDefinitions"/>
    /// after the file-level walk — the same road as <see cref="HeaderOverrides"/> — so
    /// a score that references nothing inherits the file's unnamed default.
    /// </summary>
    public FontDeclarationSyntax? FontsOverride { get; set; }

    /// <summary>The score's <c>paper NAME [{ … }]</c> reference, same contract as
    /// <see cref="FontsOverride"/>.</summary>
    public PaperDeclarationSyntax? PaperOverride { get; set; }

    // The relative-octave chain plus the part transpose that composes on top of
    // it, bundled into one named collaborator (see OctaveContext). The main walk
    // drives this in place on every note / chord / grace / tuplet.
    private readonly OctaveContext _octave = new();

    // Dynamic markings
    private readonly List<DynamicItem> _dynamics = new();

    // The walk's POSITION — where the collect currently stands. One struct, so a
    // scope that must save and restore the whole position (a parallel span's
    // sub-voice walk) cannot forget a coordinate when one is added: the
    // 2026-08-26 review named these four a hidden coupling spread over
    // CollectMultiStaff, BuildExtraVoiceTracks' manual save/restore and
    // ProcessMusicNode, and this is that bundling — mechanical on purpose (every
    // reader keeps its meaning; only the spelling moved from four fields to one).
    private struct CollectionCursor
    {
        // Global staff index currently being collected (multi-staff). Stamped onto
        // each dynamic so layout positions it under its own staff. 0 for the single-
        // staff/single-Score paths.
        public int StaffIndex;
        // Voice index (within the current staff) being collected. Stamped onto each
        // tuplet bracket so auto-beaming applies a tuplet's boundary only to its OWN
        // voice (a lower voice's eighths must not break at an upper voice's triplet).
        // 0 = primary voice; the parallel sub-voices set it in BuildExtraVoiceTracks.
        public int VoiceIndex;
        // The render voice number (1-based) when the walk is INSIDE a `voice {}` block, so an
        // override there scopes to that voice; null in the main stream (staff-scoped). Set
        // around each parallel sub-voice's processing (voice 0 in ProcessMusicNode, the extras
        // in BuildExtraVoiceTracks).
        public int? VoiceScope;
        // Added to the local measure index when collecting a parallel span's EXTRA
        // voices (they're collected with a fresh 0-based builder), so their
        // per-note metadata — dynamics, articulations, etc. — lands at the span's
        // real measure index instead of measure 0. Zero for the primary stream.
        public int MetadataMeasureOffset;
    }

    private CollectionCursor _cursor;

    // Saves the whole cursor on entry, installs the given one, and restores the
    // saved cursor on Dispose — the using-scope BuildExtraVoiceTracks' manual
    // save/restore became, so a new cursor coordinate is restored by
    // construction instead of by whoever remembers to write the reset lines.
    private readonly struct CursorScope : IDisposable
    {
        private readonly MeasureCollector _owner;
        private readonly CollectionCursor _saved;

        public CursorScope(MeasureCollector owner, CollectionCursor next)
        {
            _owner = owner;
            _saved = owner._cursor;
            owner._cursor = next;
        }

        public void Dispose() => _owner._cursor = _saved;
    }
    // Articulation marks
    private readonly List<ArticulationItem> _articulations = new();
    // Grace notes
    private readonly List<GraceNoteItem> _graceNotes = new();
    // Lyrics (note-bound lines + independent rows) — collected by a dedicated
    // collaborator that owns the item list and the overflow warnings.
    private readonly LyricsCollector _lyricsCollector = new();
    // Names bound to an independent lyrics ROW (`lyrics name` score row). The
    // note-bound pass skips these — they are collected as rows instead.
    private HashSet<string> _lyricsRowNames = new();
    // Named voices (voice sop { … }) → (voice index, measure track), so a
    // `lyrics sop { … }` block aligns to THAT voice's notes (and its index drives
    // timing-based X for non-primary voices) instead of the default first voice.
    private readonly Dictionary<string, (int Index, List<Measure> Measures)> _voiceMeasuresByName = new();
    // Music marks (segno, coda, fine, D.S., D.C., etc.)
    private readonly List<MusicMarkItem> _musicMarks = new();
    // O(1) probe over _musicMarks' SourcePositions for the per-mark duplicate check
    // (the statement-node walk asks it once per MusicMarkSyntax, and _musicMarks
    // accumulates ACROSS parts — the linear Any made a mark-heavy multi-part book
    // quadratic; 2026-08-26 review, finding 1-4). Synced lazily in MusicMarkExistsAt
    // rather than beside every Add: _musicMarks is APPEND-ONLY (no Clear/Remove/
    // indexer-write anywhere, Reset() included), and the resume machinery bulk-
    // appends adopted prefix/tail slices through the side-table lists — a lazy
    // catch-up from the last synced count sees those without hooking that path.
    private readonly HashSet<int> _musicMarkPositions = new();
    private int _musicMarkPositionsSynced;

    /// <summary>Whether any collected music mark stands at <paramref name="sourcePosition"/>
    /// — the O(1) spelling of <c>_musicMarks.Any(m =&gt; m.SourcePosition == p)</c>
    /// (see <see cref="_musicMarkPositions"/>).</summary>
    private bool MusicMarkExistsAt(int sourcePosition)
    {
        while (_musicMarkPositionsSynced < _musicMarks.Count)
            _musicMarkPositions.Add(_musicMarks[_musicMarkPositionsSynced++].SourcePosition);
        return _musicMarkPositions.Contains(sourcePosition);
    }
    // Custom text annotations
    private readonly List<CustomTextItem> _customTexts = new();
    // Volta brackets (first/second ending)
    private readonly List<VoltaBracketItem> _voltaBrackets = new();
    // The repeat barlines a ROWS-ONLY score's form writes, by measure index. A score with
    // no staff never runs ProcessForm, so nothing turns its `|: … :|` into a barline on any
    // voice; EnsureSectionStartsForRows records them here and CollectMultiStaff hands them
    // to SynchronizeBarlines as one synthetic voice, the same road HarvestOmittedStructure
    // uses for an undrawn part's bars. Empty for every score that draws a staff.
    private readonly Dictionary<int, (BarlineType Start, BarlineType End)> _rowsOnlyFormBars = new();
    // Bars in that grid — the synthetic voice's length, so it never claims bars the rows lack.
    private int _rowsOnlyFormGridBars;
    // Inline volta endings collected during the current voice walk; finalized
    // (and marked closed/open) once the whole voice has been processed.
    private readonly List<(int startMeasure, int endMeasure, string voltaText, bool isClosed, int sourcePosition)> _pendingInlineVoltas = new();
    // Parallel-voice spans (<< \\ >>) recorded during the primary (voice-0)
    // walk: the parallel node, the measure index where its content begins, and
    // the elapsed duration WITHIN that measure at the span's opening.
    // Voice 0 flows into the primary stream so measure indices stay continuous;
    // the remaining voices are reconstructed afterwards (BuildMultiVoiceScore).
    // Cleared at the start of each collection.
    // StartOffset places a mid-measure span's extra voices at the span's beat:
    // in `c4 voice { e } { g' }` voice 0 walks c4 then e inline, so e sits on
    // beat 2 for free — the reconstructed { g' } track needs the recorded offset
    // (a leading spacer) or its g' lands on beat 1 against the c4.
    // The Frame is the relative-octave state at the span's OPENING: every voice of the
    // span reads from it, and the music after the span reads from it too. A voice span is
    // simultaneous music, so its branches do not chain into one another and none of them
    // moves the frame — the same rule Lily# already applies inside a CHORD, where every
    // member stacks on the root and `<c e g>` == `<c g e>` (CreateChordItem). Written
    // 2026-08-01 on the user's call; before that voices 2..N restarted at the PART's
    // default octave and voice 0 leaked its last pitch into the music after the span.
    private readonly List<(ParallelExpressionSyntax Parallel, int StartMeasure, Fraction StartOffset, OctaveSnapshot Frame)> _parallelSpans = new();
    // Next beam identity handed out by ResolveBeamStemDirections. Runs across every call on
    // this collector so two voices of the same staff cannot be handed the same number.
    private int _nextBeamId;
    // Tuplet brackets
    private readonly List<TupletBracketItem> _tupletBrackets = new();
    // Arpeggio markings
    private readonly List<ArpeggioItem> _arpeggios = new();
    // Resolved-pitch trace (every pitch the relative-octave chain produces, in
    // source order), so `check --pitches` can show the author what each note
    // actually resolved to — the relative chain's otherwise-invisible state.
    private readonly List<PitchTraceEntry> _pitchTrace = new();
    /// <summary>Resolved absolute pitch for each note/chord-member/grace, in
    /// source order (e.g. written <c>c''</c> → <c>C6</c>).</summary>
    public IReadOnlyList<PitchTraceEntry> PitchTrace => _pitchTrace;
    /// <summary>Lyric lines whose syllable count overflowed their bound notes
    /// (extra syllables dropped). Populated as a side effect of Collect.</summary>
    public IReadOnlyList<LyricSyllableWarning> LyricWarnings => _lyricsCollector.Warnings;
    /// <summary>Sections whose plain lyric verse is fully shadowed by their <c>[N. …]</c>
    /// verses (never rendered). Populated as a side effect of Collect.</summary>
    public IReadOnlyList<ShadowedPlainLyricWarning> LyricShadowedPlainWarnings =>
        _lyricsCollector.ShadowedPlainWarnings;
    private readonly List<NavigationMarkPlacementWarning> _navPlacementWarnings = new();
    /// <summary>Navigation marks written mid-measure instead of at a barline boundary.
    /// Populated as a side effect of Collect.</summary>
    public IReadOnlyList<NavigationMarkPlacementWarning> NavigationPlacementWarnings => _navPlacementWarnings;
    // Tablature post-pass (tie-string reconciliation + per-tuning string assignment),
    // extracted as a self-contained collaborator. Its warnings are surfaced below.
    private readonly TabResolver _tabResolver = new();
    /// <summary>Notes that fall outside the tab range (clamped). Populated by the
    /// tab-string resolution during multi-staff collection.</summary>
    public IReadOnlyList<TabRangeWarning> TabRangeWarnings => _tabResolver.RangeWarnings;
    /// <summary>Tied note pairs with conflicting explicit tab string numbers.
    /// Populated as a side effect of Collect.</summary>
    public IReadOnlyList<TabTieStringWarning> TabTieWarnings => _tabResolver.TieWarnings;
    // Voice names whose per-voice sanity scan has already run on THIS collector.
    // A part engraved on more than one staff (`score { staff bass  tab bass }`) is
    // collected once per staff, and the sanity scanners below append to collector-wide
    // cumulative lists — so without this guard every complaint they make about that part
    // is emitted once per staff, at the same source position, for the same slip.
    // ONE ROOT CAUSE, ONE DIAGNOSTIC: the same doctrine _warnedSpans states for the
    // measure passes. The scans are display-independent by construction (they run
    // BEFORE the ottava transposition and before any staff-local transform), so the
    // second staff's scan could only ever reproduce the first staff's answer.
    // ⚠️ The key is the VOICE name, not the staff: extra voices from a `<< \\ >>` span
    // are named after their part, so they are covered by the same guard.
    private readonly HashSet<string> _sanityScannedVoices = new();
    // Ties whose next timed item repeats none of the tied pitches (or is a rest).
    // Scanned per finished voice by TieTargetScanner; surfaced by TieTargetValidator.
    private readonly List<TieTargetWarning> _tieTargetWarnings = new();
    /// <summary>Ties (<c>~</c>) whose following item cannot receive them — a pitch
    /// mismatch or an audible rest. Populated as a side effect of Collect.</summary>
    public IReadOnlyList<TieTargetWarning> TieTargetWarnings => _tieTargetWarnings.ToList();
    // Slur marks that pair with nothing, so SlurDetector draws no slur for them.
    // Scanned per finished voice by SlurPairingScanner; surfaced by SlurPairingValidator.
    private readonly List<UnpairedSlurWarning> _unpairedSlurWarnings = new();
    /// <summary>Slur marks — a <c>(</c> never closed or a <c>)</c> with none open — that
    /// draw no slur. Populated as a side effect of Collect.</summary>
    public IReadOnlyList<UnpairedSlurWarning> UnpairedSlurWarnings => _unpairedSlurWarnings.ToList();
    /// <summary>Span marks — a START never closed, a <c>@!</c> with none open, or a second
    /// START inside an open span — that draw nothing. EVERY family that has a terminator is
    /// here. Surfaced by <c>SpanPairingValidator</c>.</summary>
    /// <remarks>
    /// ⚠️ NOT a side effect of Collect, unlike its neighbours here, and it must not become
    /// one: each family's pairing is done by the SAME call the layout draws from
    /// (<c>TextSpannerEngraver.PairTextSpanners</c>,
    /// <c>OttavaBracketEngraver.PairOttavaBrackets</c>, <c>PedalEngraver.PairPedalBrackets</c>)
    /// over the collected marks, so what is
    /// warned about and what is drawn are two halves of one answer rather than two answers.
    /// Recording it during the walk would put the decision in a second place.
    /// </remarks>
    public IReadOnlyList<UnpairedSpanWarning> UnpairedSpanWarnings
    {
        get
        {
            var marks = _musicMarks.ToImmutableArray();
            return
            [
                .. Layout.TextSpannerEngraver.PairTextSpanners(marks).Unpaired,
                .. Layout.OttavaBracketEngraver.PairOttavaBrackets(marks).Unpaired,
                .. Layout.PedalEngraver.PairPedalBrackets(marks).Unpaired,
            ];
        }
    }
    /// <summary>The source position of every REHEARSAL mark this collect produced, so a
    /// caller holding the tree can name the written marks that are not among them.
    /// Surfaced by <c>RehearsalMarkEngravedValidator</c>.</summary>
    /// <remarks>
    /// ⚠️ POSITIONS, NOT ITEMS, and the difference is the whole use: the question this
    /// answers is "was the mark WRITTEN THERE engraved", and a written mark is a source
    /// position — its measure, its label and its staff are all decisions made after the
    /// point where it can still be lost. A mark that reaches the page twice (a part drawn on
    /// a staff and a tab) is one position either way, which is also the rule the collector
    /// itself keeps (<c>MusicMarkExistsAt</c>).
    /// ⚠️ It cannot be asked of marks the collect never reached — music no form plays, a
    /// part no score renders — which is exactly why the validator needs the tree as well.
    /// </remarks>
    public IReadOnlyCollection<int> EngravedRehearsalMarkPositions
        => _musicMarks
            .Where(m => m.Type == MusicMarkType.Rehearsal)
            .Select(m => m.SourcePosition)
            .ToHashSet();
    // Slurs and ties with one end inside a cue region and the other outside it — a span
    // LilyPond cannot make. Recorded by the SAME two scanners that pair them (one IsCue
    // comparison on the pair each already holds); surfaced by CueSpanBoundaryValidator.
    private readonly List<CueSpanBoundaryWarning> _cueSpanBoundaryWarnings = new();
    /// <summary>Slurs and ties that cross a <c>cue { … }</c> boundary. Populated as a side
    /// effect of Collect.</summary>
    public IReadOnlyList<CueSpanBoundaryWarning> CueSpanBoundaryWarnings => _cueSpanBoundaryWarnings.ToList();
    // Manual beam brackets that pair with nothing, so BeamDetector discards them and the
    // notes fall back to automatic beaming.
    // Scanned per finished voice by BeamPairingScanner; surfaced by BeamPairingValidator.
    private readonly List<UnpairedBeamWarning> _unpairedBeamWarnings = new();
    /// <summary>Manual beam brackets — a <c>[</c> never closed or a <c>]</c> with none
    /// open — whose grouping is discarded. Populated as a side effect of Collect.</summary>
    public IReadOnlyList<UnpairedBeamWarning> UnpairedBeamWarnings => _unpairedBeamWarnings.ToList();
    /// <summary>Chord-row grid faults — a bar whose slot count fits no beat-grid
    /// shape, or a '.' at a bar's head. Recorded by the row walk that also places
    /// the symbols (ChordNameCollector); surfaced by ChordRowGridValidator.</summary>
    public IReadOnlyList<ChordRowGridWarning> ChordRowGridWarnings => _chordNameCollector.GridWarnings.ToList();
    // Repeat bars ('|:') that no ':|' ever closes. Scanned per finished voice by
    // RepeatPairingScanner; surfaced by RepeatPairingValidator. Scanned HERE rather than on
    // the written text because the pairing crosses layers (a section's '|:' may be closed
    // by a ':|' the form writes) and is only decidable on the expanded measure stream.
    private readonly List<UnpairedRepeatWarning> _unpairedRepeatWarnings = new();
    /// <summary>Repeat bars — a <c>|:</c> that no <c>:|</c> closes — whose span has no end.
    /// Populated as a side effect of Collect.</summary>
    public IReadOnlyList<UnpairedRepeatWarning> UnpairedRepeatWarnings => _unpairedRepeatWarnings.ToList();
    // Figured bass
    private readonly List<FiguredBassItem> _figuredBasses = new();
    // Chord names (inline c:m marks, chordnames {} streams, chords-name rows) —
    // collected by a dedicated collaborator; the main walk feeds inline marks via AddInline.
    private readonly ChordNameCollector _chordNameCollector = new();
    // Percent repeats
    private readonly List<PercentRepeatItem> _percentRepeats = new();
    // Cross-staff items
    private readonly List<CrossStaffItem> _crossStaffItems = new();
    // Grob property overrides and reverts
    private readonly List<GrobOverride> _grobOverrides = new();
    private readonly List<GrobRevert> _grobReverts = new();
    // Trill spanner start/stop events (paired into TrillSpannerItems after collection)
    private readonly List<(bool isStart, int measureIndex, int itemIndex, int sourcePosition, int staffIndex, int voiceIndex, int forcedDir)> _trillSpannerEvents = new();
    // Within-measure accidental memory: (diatonic step, octave) → the alteration
    // currently in effect for that pitch in the CURRENT measure. Seeded from the
    // key signature and updated as notes are engraved; reset at every measure
    // boundary (via MeasureBuilder.MeasureCompleted). A note prints an accidental
    // only when its alteration differs from the in-effect value — LilyPond's
    // default style. LILYPOND-REF: lily/accidental-engraver.cc.
    private readonly Dictionary<(int step, int octave), int> _measureAccidentals = new();
    // Notes explicitly marked with @courtesy annotation
    private readonly HashSet<int> _courtesySourcePositions = new();
    /// <summary>
    /// Maps a note's source position to its finger number (extracted from <c>@finger.N</c>).
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/fingering-engraver.cc — finger-number event handling.</remarks>
    private readonly Dictionary<int, int> _fingeringByPosition = new();
    /// <summary>
    /// Maps a note-attached compound mark's source position (the <c>@mark("A")</c> of
    /// <c>c1@mark("A")</c>) to the measure index its HOST NOTE begins in.
    /// </summary>
    /// <remarks>
    /// Such a mark is surfaced twice: once as an articulation of its note (recorded here)
    /// and once as a statement node in the music sequence, where the mark itself is built
    /// because that is where the label is read. The statement node is reached AFTER the
    /// host note has been added, so <c>builder.CurrentMeasureIndex</c> there answers
    /// "where is the builder now", not "where was the mark written" — and a note that
    /// fills its bar has already carried the builder across the barline. This records the
    /// answer to the second question while the host note is still in hand.
    /// It also carries <c>_cursor.MetadataMeasureOffset</c>, which the statement node does not
    /// apply — inside a <c>voice { … }</c> span the mark was shifted by that too.
    /// </remarks>
    private readonly Dictionary<int, int> _markHostMeasure = new();
    // Pending grace notes to attach to the next main note
    private GraceExpressionSyntax? _pendingGrace = null;
    /// <summary>
    /// How many <c>cue { … }</c> regions enclose the item being collected. A cue is a
    /// REGION in LilyPond — the size comes from the CueVoice context's <c>fontSize = #-4</c>
    /// and nothing is attached to a note — so this depth, not an annotation, is what makes
    /// an item a cue. Nesting is rejected (Diagnostic LYS4013), so in practice it is 0 or 1;
    /// it is a depth rather than a flag so an unbalanced walk cannot leave it stuck on.
    /// See docs/cue-context-design.md.
    /// </summary>
    private int _cueDepth = 0;
    /// <summary>
    /// True between entering a <c>cue { … }</c> region and the first note or chord it emits —
    /// the one item that gets <see cref="MusicItem.BeginsCueRegion"/>. Set on entry and cleared
    /// BOTH by that item and on the way out, so outside a region it is always false and a
    /// checkpoint never carries it (<see cref="WalkCarriesNothing"/> already refuses to stand
    /// inside a region at all).
    /// </summary>
    private bool _cueRegionPending = false;

    /// <summary>Reads and clears <see cref="_cueRegionPending"/>: true for the first note or
    /// chord of a cue region, false for every later one.</summary>
    private bool TakeCueRegionStart()
    {
        if (!_cueRegionPending)
            return false;
        _cueRegionPending = false;
        return true;
    }
    // Grace-note infos of the just-collected leading grace group, stamped onto the
    // main note/chord so the spacing can reserve space in front of its column.
    private ImmutableArray<GraceNoteInfo> _pendingLeadingGrace = ImmutableArray<GraceNoteInfo>.Empty;
    // Default (sticky) duration: an undurated note/rest/chord takes the WHOLE
    // previous duration — dots included — so the pair travels together.
    // LILYPOND-REF: lily/parser.yy:3505-3514 optional_notemode_duration —
    //   default_duration_ is a full Duration (log AND dots). Until 2026-08-07 only
    //   the value stuck and the dots silently reset to 0 (`r8. r` lost its dot,
    //   dot-rest-beam-trigger.ly), while the SEMANTIC walk (MeasureDurations) and
    //   the MIDI/MusicXML exporters carried ToFraction() with dots — the drawing
    //   walk was the one liar.
    private Fraction _defaultDuration = Fraction.Quarter;
    private int _defaultDots;

    // This voice's score-level key, captured at collection start. A section
    // boundary reverts the running key to it (like octave/duration) so a section
    // is self-contained: a mid-section modulation cannot leak into the next
    // section — nor into the same section reused elsewhere in the form.
    private int _sectionResetKeySharps;
    private string? _sectionResetKeyCustom;

    // This voice's part-default clef, captured at collection start. A section boundary
    // reverts the running clef to it (like key/time) so a section without its own clef
    // uses the part default — a mid-section `clef` change (in one section) must not leak
    // into the next section, nor into the same section reused elsewhere in the form.
    private string _sectionResetClef = "treble";

    // This voice's score-level time signature, snapshotted at collection start (before any
    // section music is walked). A section boundary reverts the running meter to it — like
    // key/clef — so a mid-section `time` change cannot leak into the next section. The
    // snapshot is essential: mid-music time changes MUTATE _meta.TimeBeats, so by the next
    // boundary _meta no longer holds the score-level meter.
    private int _sectionResetTimeBeats = 4;
    private int _sectionResetTimeBeatType = 4;
    private string? _sectionResetTimeBeatsText;
    private bool _sectionResetTimeSenzaMisura;

    // The grob-override state a section boundary reverts to (the grob analogue of
    // _sectionResetClef, but a SET): the part-default values — global + this voice's
    // part-body overrides — snapshotted at collection start. Section-internal overrides
    // reset to this at each boundary, so they never leak into the next section.
    private readonly Dictionary<(string Grob, string Prop), LysValue> _sectionResetOverrides = new();
    // Grob properties changed by an IN-MUSIC override in the current section; each is
    // reset (to the part default, or reverted) at the next section boundary.
    private readonly HashSet<(string Grob, string Prop)> _sectionActiveGrobProps = new();

    // Running ambient key tonic (as WRITTEN, before any part transpose) tracked
    // across the voice walk for phrase auto-transpose: a phrase written in the
    // score's home key is shifted to whatever key is in effect where it is
    // referenced. Reset to the home key per voice and at each section boundary,
    // advanced by each mid-stream key change. Invalid = a custom/atonal ambient
    // key (no tonic) → the phrase is placed unshifted.
    private int _ambientTonicStep;
    private int _ambientTonicAlter;
    private bool _ambientTonicValid;

    // Saved transpose targets around phrase-reference expansions (a stack so a
    // phrase referenced inside another restores cleanly). Pushed at the reset
    // marker, popped at the paired phrase-end marker.
    private readonly Stack<(int step, int alt, int oct)?> _phraseTransposeSaves = new();
    // Each open reference's outgoing anchor (bare letter + octave, resolved in
    // the phrase's own frame at entry), handed to the relative chain at exit —
    // the chord rule; null = pitchless body. Pushed/popped alongside the above.
    private readonly Stack<(char Name, int Octave)?> _phraseAnchorSaves = new();
    // Saved absolute-mode anchors (OctaveBase). A reference's trailing marks move
    // the frame the body resolves in; in ABSOLUTE mode the frame IS OctaveBase, so
    // that is what moves. Pushed/popped alongside the above.
    private readonly Stack<int> _phraseAbsoluteBaseSaves = new();

    // Piece-level metadata (title/composer/tempo/time/key/clef + header source
    // positions) grouped into one owner. See MetadataState.
    private readonly MetadataState _meta = new();
    // Active `repeat tremolo N { … }` transform: the body note prints ONCE at
    // the combined duration with the subdivision's stem slashes.
    private int _tremoloRepeatCount = 1;
    // Active two-note tremolo: (display value, display dots, between-beams,
    // gapped-beam count); both notes print at the pair's TOTAL duration and
    // sound half (TimeScale ½). GapCount = how many beams stop short of the
    // stems (0 for a half-note pair — LP's duration_log == 1 exemption).
    private (int Value, int Dots, int Beams, int GapCount)? _tremoloPairShape;
    private bool _tremoloPairFirst;
    private Dictionary<string, DrumInfo>? _drumOverrides; // drummap { } per-score
    // measure -> (tonic step, sharps) at each key change, so a chord's Roman degree
    // follows the key in force at its bar (a mid-piece modulation re-bases the degrees).
    private readonly SortedDictionary<int, (int TonicStep, int Sharps)> _keyByMeasure = new();
    // WALK-ORDER JOURNAL of every _keyByMeasure write (RecordKeyAtMeasure is the one
    // write site) — append-only across the collect, so a WalkCheckpoint stores a
    // WATERMARK into it instead of copying the map per boundary, and a restore/splice
    // rebuilds the map by replaying the source's journal prefix. Replay is last-wins,
    // exactly the indexer's semantics (a later voice's transposed re-record wins).
    private readonly List<(int Measure, int TonicStep, int Sharps)> _keyByMeasureLog = new();
    // Same journal for RecordSectionStart events (first-wins StartMeasure + deduped
    // AllStarts are reproduced by replaying the events through RecordSectionStart
    // itself — one spelling of the bookkeeping, never a second).
    private readonly List<(string Name, int StartMeasure)> _sectionStartLog = new();
    // The opening key a section states for the voice CURRENTLY being collected (bar 0,
    // before any note). Recorded per voice — the collect entry points fold it into that
    // voice's own signature (score key for a single staff, the staff's own key for a
    // multi-staff part), never the shared score key, so sibling staves with different
    // opening keys do not overwrite each other. See ApplyKeySignatureChange.
    private (int Sharps, string? Custom)? _openingKeyOverride;
    // section name -> its own starting key, for a section that carries a `key` but no
    // inline music: a section-major section (`section A { key g major  melody { … } }`)
    // or a standalone part-major header (`section A { key g major }`). Applied to every
    // part playing that section (an inline-music section walks its key as music instead).
    private readonly Dictionary<string, KeySignatureSyntax> _sectionHeaderKeys = new();
    // section name -> its own starting time / tempo, same rule as the header key: a
    // section-major section or a standalone part-major header that carries the directive
    // but no inline music. Applied to every part of the section.
    private readonly Dictionary<string, TimeSignatureSyntax> _sectionHeaderTimes = new();
    private readonly Dictionary<string, TempoDeclarationSyntax> _sectionHeaderTempos = new();
    private readonly Dictionary<string, PartialDeclarationSyntax> _sectionHeaderPartials = new();
    // section node -> canonical bar count (GetCanonicalSectionBars). A pure function of
    // the syntax within one collect, so it is counted once — not once per part per
    // reprise. Keyed by node identity; cleared per collect (Reset).
    private readonly Dictionary<SectionDeclarationSyntax, int> _canonicalSectionBars = new();

    // --- checkpoint/resume probe (S5 substrate — see CollectWalkProbe.cs) ---
    // Null in production: every guard below is a null check, so the probe costs
    // the walk nothing until the per-measure collect memo (HANDOFF ▶ ⒭ ⑵) wires
    // it across keystrokes.
    /// <summary>Record checkpoints (recorder) or resume from them (resumer).</summary>
    internal CollectWalkProbe? WalkProbe { get; set; }

    /// <summary>⑶ beamdirs: the per-measure beam-DETECTION memo
    /// <see cref="ResolveBeamStemDirections"/> hands its detector. Null in production
    /// (lysc, direct SvgGenerator callers) — detection then runs fully, unchanged;
    /// <c>IncrementalCompiler</c> attaches one instance across edits so a keystroke
    /// re-detects only the measures the edit changed. The BAKE always runs live,
    /// so BeamId numbering (and with it the resolved model) is byte-identical to a
    /// memo-free collect — see <see cref="BeamDetector"/>'s memo remarks.</summary>
    internal BeamDetectionMemo? BeamMemo { get; set; }

    /// <summary>The session's resume channels for this collector's NESTED collects
    /// (finding 3-5: the omitted-structure harvest and the sung-melody collect used
    /// to run a complete fresh collect per keystroke, outside the resume machinery).
    /// Null outside the incremental session — the nested sites then construct the
    /// historical fresh collector, so the CLI path is untouched.</summary>
    internal NestedCollectResume? NestedResume { get; set; }
    private int _walkOrdinal;                        // Nth CollectMeasures call of this collect
    private int _invocationInSection;                // ProcessNodes calls within the current section (or the walk, pre-section)
    private int _sectionVisit;                       // ProcessSection entries within the current walk
    private int _formRepeatDepth;                    // > 0 inside a `|: … :|` form repeat block — no checkpoint/splice there (finding 3-4)
    private VoiceWalkRecording? _probeRecording;     // record mode: the current walk's recording
    private VoiceResumePlan? _resumePending;         // resume mode: set until the target checkpoint is restored
    private int _sectionStartMeasureForResume;       // mirror of ProcessSection's startMeasure local, for capture
    private int? _resumeRestoredSectionStart;        // the target section's true start, injected at restore
    // Resume mode, suffix side (see CollectWalkProbe's suffix-splice remarks):
    // the current walk's plan when it carries suffix candidates, those candidates
    // keyed by their SHIFTED walk-order address for O(1) lookup at each clean
    // boundary, the dirty window the shifts go through, and whether the walk
    // already spliced (everything after a splice is skipped).
    private VoiceResumePlan? _suffixPlan;
    private Dictionary<(int Visit, int Invocation, int NodeStart), WalkCheckpoint>? _suffixTargets;
    private CollectTailShifter.Window _suffixWindow;
    private bool _suffixSpliced;
    // Record mode only: the end of the furthest source text this walk has read
    // (WalkCheckpoint.MaxSourceRead — see its remarks for the fold sites).
    private int _walkMaxSourceRead;
    // Record mode only: the header spans this walk read, in walk order (part
    // name/config at entry, then each visited section's name + header directives).
    // See VoiceWalkRecording.HeaderReads.
    private readonly List<TextSpan> _walkHeaderReads = new();

    /// <summary>
    /// Gets the time signature as a Fraction.
    /// </summary>
    private Fraction TimeSignatureFraction => new(_meta.TimeBeats, _meta.TimeBeatType);

    /// <summary>
    /// Snapshots the accumulated piece-level metadata and annotation lists into an
    /// immutable <see cref="ScoreContent"/>. Call once, after all collection is done;
    /// <see cref="ScoreAssembler"/> turns it into the Score / MultiStaffScore. This is
    /// the single reader of the collector's output state — the three build sites used
    /// to each re-list ~25 arguments (and drifted).
    /// </summary>
    /// <param name="measureCount">Total measures collected — the virtual stop an
    /// unterminated trill spanner runs to (one past the last measure).</param>
    private ScoreContent CaptureScoreContent(int measureCount) => new(
        new TimeSignature(_meta.TimeBeats, _meta.TimeBeatType, _meta.TimeBeatsText, _meta.TimeSenzaMisura),
        new KeySignature(_meta.InitialKeySharps, _meta.InitialKeyCustom), // initial key, not the post-change state
        _meta.InitialClef, // initial clef, not the post-change state
        _meta.Tempo,
        _meta.Title,
        _meta.Composer,
        _meta.SwingSubdivision,
        _dynamics.ToImmutableArray(),
        _articulations.ToImmutableArray(),
        _graceNotes.ToImmutableArray(),
        _lyricsCollector.Lyrics.ToImmutableArray(),
        _musicMarks.ToImmutableArray(),
        _customTexts.ToImmutableArray(),
        _voltaBrackets.ToImmutableArray(),
        _tupletBrackets.ToImmutableArray(),
        _arpeggios.ToImmutableArray(),
        _figuredBasses.ToImmutableArray(),
        _chordNameCollector.Items.ToImmutableArray(),
        _percentRepeats.ToImmutableArray(),
        _crossStaffItems.ToImmutableArray(),
        _grobOverrides.ToImmutableArray(),
        _grobReverts.ToImmutableArray(),
        PairTrillSpannerEvents(measureCount),
        new HeaderPositions(_meta.TitlePosition, _meta.ComposerPosition, _meta.TimePosition, _meta.KeyPosition, _meta.ClefPosition, _meta.TempoPosition),
        _meta.TempoText,
        _meta.TempoBeatUnit,
        _meta.TempoDots,
        _meta.Fonts,
        _meta.Paper);

    /// <summary>
    /// Collects a Score from a syntax tree.
    /// </summary>
    public Score Collect(SyntaxTree tree, string? voiceName = null,
        FormDeclarationSyntax? localForm = null,
        string? attachedChordPart = null,
        ChordDisplayMode attachedChordDisplay = ChordDisplayMode.Names,
        IReadOnlyList<string>? attachedLyricParts = null,
        RenderSpec? renderSpec = null)
    {
        _voiceName = voiceName;
        Reset();

        // Phase 1: Collect definitions
        CollectDefinitions(tree.GetRoot());
        // An explicitly passed form overrides the primary-form default for this
        // collection (used by callers that render a specific form directly).
        if (localForm != null)
            _form = localForm;

        // Phase 1.5: If voiceName specified, look up clef and octave from part definition
        if (voiceName != null)
        {
            // Part-body grob defaults (`part <voice> { override … }`) — staff 0 here.
            CollectPartBodyOverrides(tree.GetRoot(), voiceName, _cursor.StaffIndex);
            var (partClef, partOctave, partExplicitOctave, partTranspose, partClefPos, partKey) = GetPartDefaults(tree.GetRoot(), voiceName);
            // The POSITION follows the clef it describes. A part without its own
            // `clef` keeps the top-level one, so overwriting the offset regardless
            // dropped it to 0 — and 0 reads as "no position", which left the clef
            // with no data-pos and nothing to click through to.
            if (partClef != null)
            {
                _meta.Clef = partClef;
                _meta.ClefPosition = partClefPos;
            }
            _octave.CurrentOctave = partOctave ?? InstrumentDefaults.GetDefaultOctave(ParseClefType(_meta.Clef));
            // ABSOLUTE mode sees the part's OWN `octave N` and nothing else — not the preset
            // and not the clef. See GetPartDefaults' remarks for what folding them cost.
            _octave.OctaveBase = InstrumentDefaults.AbsoluteBaseOctave(partExplicitOctave);
            ApplyTranspose(partTranspose);
            // A part-header key overrides the file key for THIS part (CollectDefinitions
            // left the global key in place; a part that sets none keeps it).
            if (partKey != null)
                ApplyPartHeaderKey(partKey);
            // Transpose the written key signature (CollectDefinitions set it
            // before the part option was known) so the displayed key and the
            // accidental engine match the transposed pitches.
            _meta.KeySharps = _octave.TransposeKeySharps(_meta.KeySharps);
        }
        else
        {
            _octave.CurrentOctave = InstrumentDefaults.GetDefaultOctave(ParseClefType(_meta.Clef));
        }
        _octave.InitialOctave = _octave.CurrentOctave;
        _meta.InitialClef = _meta.Clef; // Preserve initial clef before music processing
        _meta.InitialKeySharps = _meta.KeySharps; // Preserve initial key before music processing
        _meta.InitialKeyCustom = _meta.KeyCustom;
        _octave.InitialOctaveAbsolute = _octave.OctaveAbsolute; // file-level octave mode default

        // Phase 2: Collect the primary (voice-0) stream. A << \\ >> span is
        // handled INLINE during this walk (its first voice flows into the
        // stream, the span is recorded in _parallelSpans), so sequential
        // measures and any number of parallel spans interleave correctly.
        _parallelSpans.Clear();
        _openingKeyOverride = null;
        var measures = CollectMeasures();
        ResolveBeamStemDirections(measures);

        // Score-level structure from the parts this score OMITS (|: :|, navigation
        // marks, inline voltas) — the SAME harvest CollectMultiStaff runs, on the same
        // resume channel. The single-staff road never called it, so `score main
        // { staff sax }` dropped the band's repeats that the multi-staff spelling of
        // the same book drew (measured 2026-08-27: with/without the omitted part's
        // |: :| the single-staff page was byte-identical). Placed BEFORE the
        // multi-voice branch so both single-staff roads get it: the mark/volta merge
        // is a side effect on this collector's state (CaptureScoreContent reads it on
        // both roads), and the barline sync lands on `measures`, which is track 0 of
        // the multi-voice road and THE voice of the single road. Gated on a real
        // RenderSpec: the spec names what is rendered (the omitted set is its
        // complement) and the nested harvest spec is built from it — the legacy
        // no-spec callers (tests, ChordHarmonizer's isolated melody reads) keep the
        // historical harvest-free shape.
        if (renderSpec != null)
        {
            var harvestVoices = HarvestOmittedStructure(tree, renderSpec);
            if (harvestVoices.Count > 0)
            {
                // "" cannot be a part name, so the self key never collides with an
                // "omit:<part>" key; the omit voices are sync inputs only, never drawn
                // (the same sentinel contract as CollectMultiStaff's flatVoices).
                var flat = new Dictionary<string, Voice>
                {
                    [""] = new Voice(_voiceName ?? "default", measures.ToImmutableArray()),
                };
                foreach (var v in harvestVoices)
                    flat["omit:" + v.Name] = v;
                SynchronizeBarlines(flat);
                var synced = flat[""].Measures;
                for (int i = 0; i < measures.Count; i++)
                    measures[i] = synced[i];
            }
        }

        // A section that OPENS with its own key folds into this single staff's opening
        // signature (Score.KeySignature reads _meta.InitialKey*). See ApplyKeySignatureChange.
        if (_openingKeyOverride is { } openingKey)
        {
            _meta.InitialKeySharps = openingKey.Sharps;
            _meta.InitialKeyCustom = openingKey.Custom;
        }

        // If any parallel span was seen, reconstruct the additional voices.
        // Pass the attached chord part through: BuildMultiVoiceScore collects it
        // itself (this method's CollectAttached below is never reached for the
        // multi-voice path).
        if (_parallelSpans.Count > 0)
            return BuildMultiVoiceScore(measures, tree.GetRoot(), attachedChordPart, attachedChordDisplay, attachedLyricParts);

        // Single voice
        var voice = _tabResolver.ResolveVoiceTabTies(new Voice(_voiceName ?? "default", measures.ToImmutableArray()));
        // Tie sanity scan runs BEFORE the ottava display transposition (it compares
        // written staff positions; an 8va span must not fake a pitch change).
        ScanVoiceSanity(voice);
        // One voice IS the score here, so this voice already carries the score-level
        // barlines. (The multi-staff path scans after SynchronizeBarlines instead.)
        RepeatPairingScanner.Scan(voice, _unpairedRepeatWarnings);

        // Ottava DISPLAY transposition: notes under an 8va draw an octave lower
        // (etc.) while sounding at their written pitch. Single-staff score, so
        // every ottava mark is on staff 0. See OttavaTransposer.
        voice = OttavaTransposer.Transpose(voice, DetectOttavaSpans(0));

        // Lyrics attach EXPLICITLY via `score { staff X with lyrics L }`. There is NO
        // implicit auto-attach anywhere: a `lyrics {}` block that no score references is a
        // LYS4006 error (a scoreless loose-music file simply cannot show lyrics).
        if (attachedLyricParts is { Count: > 0 })
            _lyricsCollector.CollectAttached(tree.GetRoot(), attachedLyricParts, measures, 0,
                _lyricsRowNames, _voiceMeasuresByName, _sectionState.StartMeasure, _sectionState.AllStarts);
        _chordNameCollector.KeyByMeasure = BuildKeyTimeline();
        _chordNameCollector.SectionStarts = _sectionState.AllStarts;
        // (The nameless `chords { }` auto-attach is gone — LYS0032. A chord part
        // renders only where a score places its row.)
        if (attachedChordPart != null)
            _chordNameCollector.CollectAttached(
                tree.GetRoot(), attachedChordPart, _sectionState.StartMeasure, _cursor.StaffIndex,
                _meta.TimeBeats, _meta.TimeBeatType, attachedChordDisplay);

        return ScoreAssembler.BuildScore(voice, CaptureScoreContent(voice.Measures.Length));
    }

    /// <summary>
    /// Collects a MultiStaffScore from a syntax tree based on a render specification.
    /// </summary>

    /// <summary>
    /// Propagates the strongest start/end barline at each measure index to
    /// every voice (score-level Timing semantics — see CollectMultiStaff).
    /// </summary>
    private static void SynchronizeBarlines(Dictionary<string, Voice> voiceDict)
    {
        if (voiceDict.Count < 2)
            return;

        int maxLen = voiceDict.Values.Max(v => v.Measures.Length);
        var starts = new BarlineType[maxLen];
        var ends = new BarlineType[maxLen];
        for (int i = 0; i < maxLen; i++)
        {
            var start = BarlineType.None;
            var end = BarlineType.None;
            foreach (var v in voiceDict.Values)
            {
                if (i >= v.Measures.Length)
                    continue;
                start = Stronger(start, v.Measures[i].StartBarline);
                end = Stronger(end, v.Measures[i].EndBarline);
            }
            starts[i] = start;
            ends[i] = end;
        }

        foreach (var name in voiceDict.Keys.ToArray())
        {
            var voice = voiceDict[name];
            var measures = voice.Measures;
            ImmutableArray<Measure>.Builder? builder = null;
            for (int i = 0; i < measures.Length; i++)
            {
                var m = measures[i];
                if (m.StartBarline == starts[i] && m.EndBarline == ends[i])
                    continue;
                builder ??= measures.ToBuilder();
                builder[i] = new Measure(
                    m.Items, starts[i], ends[i], m.SectionLabel,
                    m.SourceStart, m.SourceEnd,
                    hasBreakAfter: m.HasBreakAfter,
                    lineBreakPermission: m.LineBreakPermission,
                    breakPenalty: m.BreakPenalty,
                    pageBreakPermission: m.PageBreakPermission,
                    pageTurnPermission: m.PageTurnPermission,
                    sectionLabelPosition: m.SectionLabelPosition,
                    isPickup: m.IsPickup);
            }
            if (builder != null)
                voiceDict[name] = new Voice(voice.Name, builder.ToImmutable());
        }
    }

    /// <summary>
    /// Of two barline types at the same timestep, the more significant wins
    /// (repeats and finals over plain bars; both-repeat over either half).
    /// </summary>
    /// <remarks>
    /// ⚠️ Shared with <see cref="MeasureBuilder.FinalizeMeasures"/>, which merges the bar of
    /// a trailing clef-only column into the measure before it. Two barlines landing on one
    /// moment get ONE answer, and it is this one — the cross-voice merge and the
    /// trailing-clef merge must not drift into two rules.
    /// </remarks>
    internal static BarlineType Stronger(BarlineType a, BarlineType b)
    {
        // A repeat-end meeting a repeat-start at the same point = both.
        if ((a == BarlineType.RepeatEnd && b == BarlineType.RepeatStart)
            || (a == BarlineType.RepeatStart && b == BarlineType.RepeatEnd))
            return BarlineType.RepeatBoth;
        return Rank(a) >= Rank(b) ? a : b;

        static int Rank(BarlineType t) => t switch
        {
            BarlineType.None => 0,
            BarlineType.Single => 1,
            BarlineType.Double => 2,
            BarlineType.RepeatStart => 3,
            BarlineType.RepeatEnd => 3,
            BarlineType.RepeatBoth => 4,
            BarlineType.Final => 5,
            _ => 0
        };
    }

    /// <summary>
    /// Collects a <see cref="MultiStaffScore"/> from a syntax tree based on a render specification.
    /// </summary>
    /// <remarks>
    /// The blanking pass is the LAST thing the collect phase does, and it is here rather than
    /// in <c>SvgGenerator.CollectScore</c> because this is the method callers actually reach
    /// for: the existing nets for the line-start half of the same defect
    /// (<c>TabOnlyKeyPrefixTests</c>) call it directly and would otherwise have measured a
    /// model the render path never produces — the "one true path and a fallback" shape
    /// HANDOFF 5.2.1② names as the worst one. <c>SvgGenerator</c> still applies it to the
    /// SINGLE-staff wrap, which is assembled there and never passes through here.
    /// See <see cref="MeterStencil"/>.
    /// </remarks>
    public MultiStaffScore CollectMultiStaff(SyntaxTree tree, RenderSpec renderSpec)
        => MeterStencil.Blank(CollectMultiStaff(tree, renderSpec, harvestStructureMarks: true));

    // <paramref name="harvestStructureMarks"/> is false on the isolated recursion that harvests
    // unrendered parts' score-level marks, so that pass never re-enters the harvest.
    private MultiStaffScore CollectMultiStaff(SyntaxTree tree, RenderSpec renderSpec, bool harvestStructureMarks)
    {
        Reset();

        // Phase 1: Collect definitions
        CollectDefinitions(tree.GetRoot());
        // This score renders its bound form (resolved by name in the RenderSpec).
        // Fall back to the primary form only when the reference is unresolved (a
        // validator error) so a typo still previews something.
        _form = renderSpec.Form ?? _form;
        _meta.InitialKeySharps = _meta.KeySharps; // Preserve initial key before music processing
        _meta.InitialKeyCustom = _meta.KeyCustom;
        // The file key's tonic/position too, so a part that sets its OWN header key
        // can be undone for the next part that sets none (restored per voice below).
        int globalKeyTonicStep = _meta.KeyTonicStep;
        int globalKeyTonicAlter = _meta.KeyTonicAlter;
        int globalKeyPosition = _meta.KeyPosition;
        // Capture the file-level `octave absolute/relative` default AFTER the
        // pre-scan, mirroring the single-staff path. Without this each part's
        // line-702 restore reads the post-Reset `false`, so a top-level
        // `octave absolute` was silently ignored for every staff in a
        // multi-part score (notes fell back to relative and ran off the staff).
        _octave.InitialOctaveAbsolute = _octave.OctaveAbsolute;

        // Phase 2: Build voice dictionary. Each staff maps to ALL its voices
        // (the primary stream plus any from << \\ >> spans inside that staff).
        var staffVoices = new Dictionary<string, ImmutableArray<Voice>>();
        _lyricsRowNames = renderSpec.Items.OfType<LyricsRowSpec>()
            .Select(s => s.PartName).ToHashSet();
        // Per-voice transposed key signature (only for transposed parts); used
        // to give that voice's staff its own key in a multi-staff score.
        var voiceKeyDict = new Dictionary<string, KeySignature>();
        // Per-voice source offset of the `clef` that set that part's clef (0 = none), so
        // each staff's line-start clef can carry its own data-pos.
        var voiceClefPosDict = new Dictionary<string, int>();
        // GetVoiceNames() yields names in the SAME order ToStaffGroups() builds
        // staves, so this counter equals the global staff index (see
        // EnumerateStaves) and tags each staff's dynamics correctly.
        int collectStaffIndex = 0;
        // …and this one is the same counter for the OTHER axis: how many voices the
        // bindings already on the staff being filled have used up, so an APPENDED binding
        // (a condensed staff's second part) is collected at the slot its voices really take
        // in Staff.Voices, not at 0. Advanced from the PREVIOUS binding's collected voices
        // at the top of each turn, which is the one place every branch below passes through.
        int staffVoiceSlots = 0;
        string? previousBinding = null;
        // Lyrics rows are collected AFTER the music, so the per-section bar count
        // (used to auto-wrap one block's verses) is known from the real content.
        var pendingLyricsRows = new List<(string Name, int StaffIndex)>();
        // `staff NAME with chords CHORDPART` attachments, applied post-loop.
        var attachedChords = new List<(string PartName, int StaffIndex, ChordDisplayMode Mode)>();
        // `staff NAME with lyrics L [with lyrics L2 …]`: named lyrics parts aligned
        // note-by-note BELOW that staff, applied post-loop (verses in written order).
        // StaffVoice = the staff's primary voice, whose notes the syllables align to.
        var attachedLyrics = new List<(string PartName, int StaffIndex, string StaffVoice)>();
        // Chord rows are also deferred (see the ChordRowSpec branch below).
        var pendingChordRows = new List<(string Name, int StaffIndex, ChordDisplayMode Mode)>();
        foreach (var (voiceName, withChords, chordDisplay, withLyrics, slotting) in renderSpec.GetVoiceBindings())
        {
            _voiceName = voiceName;
            // What the binding before this one placed on the staff. Read here rather than
            // at each assignment site because two branches below `continue` out of the turn.
            if (previousBinding is { } prev && staffVoices.TryGetValue(prev, out var prevVoices))
                staffVoiceSlots += prevVoices.Length;
            previousBinding = voiceName;
            // A condensed staff yields one binding per part but ONE staff, so its later
            // parts take the staff index already handed out instead of opening a new one
            // (see GetVoiceBindings) — otherwise every staff below would be tagged one
            // index too high.
            _cursor.StaffIndex = slotting == VoiceSlotting.OwnStaff
                ? collectStaffIndex++
                : collectStaffIndex - 1;
            // …and the voice slot those items are addressed by. A staff of its own starts
            // at 0; a part that SHARES one starts after the parts already on it. For a
            // condensed staff that is the final answer, because Staff.Voices is exactly
            // that concatenation. For a COMBINED staff it is provisional — the combiner
            // rewrites both streams — but it is the same arithmetic, because collecting
            // both parts at 0 is what made the second one's items address the first one's
            // notes, and concatenation space is the one space where they differ at all.
            // CombinedStaffAddressing translates it once the staff has been built.
            if (slotting == VoiceSlotting.OwnStaff)
                staffVoiceSlots = 0;
            _cursor.VoiceIndex = staffVoiceSlots;
            _octave.LastPitchName = 'c';
            _defaultDuration = Fraction.Quarter;
            _defaultDots = 0;

            // Part-body grob defaults (`part <voice> { override … }`) scope to this staff.
            CollectPartBodyOverrides(tree.GetRoot(), voiceName, _cursor.StaffIndex);

            // `staff NAME with chords CHORDPART [as roman]`: remember the
            // attachment (and its display); the chord symbols are collected AFTER
            // the voice loop, once every section's start measure is registered.
            if (withChords != null)
                attachedChords.Add((withChords, _cursor.StaffIndex, chordDisplay));

            // `staff NAME with lyrics L`: remember each named lyrics part to align
            // under THIS staff (post-loop, once section starts are registered).
            foreach (var lyName in withLyrics)
                attachedLyrics.Add((lyName, _cursor.StaffIndex, voiceName));

            // An independent chord row (`chords name [as roman]` in the score).
            // Defer its collection until AFTER the music voices: the section start
            // table fills while music is processed, and a row spec listed first (or a
            // rows-only score) would otherwise collect every section's block from bar
            // 0, overprinting them.
            if (renderSpec.Items.OfType<ChordRowSpec>().Any(c => c.PartName == voiceName))
            {
                pendingChordRows.Add((voiceName, _cursor.StaffIndex, chordDisplay));
                staffVoices[voiceName] = ImmutableArray.Create(
                    new Voice(voiceName, ImmutableArray<Measure>.Empty));
                continue;
            }

            // An independent lyrics row (`lyrics name` in the score). Defer its
            // collection (placeholder voice for now) until the music bar count is
            // known, so one block of flat verses can auto-wrap to that bar count.
            if (renderSpec.Items.OfType<LyricsRowSpec>().Any(c => c.PartName == voiceName))
            {
                pendingLyricsRows.Add((voiceName, _cursor.StaffIndex));
                staffVoices[voiceName] = ImmutableArray.Create(
                    new Voice(voiceName, ImmutableArray<Measure>.Empty));
                continue;
            }

            // Set clef and octave for this voice from part definition
            var (partClef, partOctave, partExplicitOctave, partTranspose, partClefPos, partKey) = GetPartDefaults(tree.GetRoot(), voiceName);
            _meta.Clef = partClef ?? "treble";
            _meta.ClefPosition = partClefPos;
            // …and remember it per VOICE: the staff built for this part carries its own
            // clef's offset, so a multi-staff score's line-start clefs each click through
            // to the `clef` that set them (stamped after ToStaffGroups below).
            voiceClefPosDict[voiceName] = partClefPos;

            // Set initial octave: explicit > instrument default > clef default
            _octave.CurrentOctave = partOctave ?? InstrumentDefaults.GetDefaultOctave(ParseClefType(_meta.Clef));
            _octave.InitialOctave = _octave.CurrentOctave;
            // …and the ABSOLUTE base from the part's OWN `octave N` alone (see GetPartDefaults).
            _octave.OctaveBase = InstrumentDefaults.AbsoluteBaseOctave(partExplicitOctave);
            _octave.OctaveAbsolute = _octave.InitialOctaveAbsolute; // restore file-level octave mode
            ApplyTranspose(partTranspose);

            // Re-arm this voice's running key. Restore the file key's tonic/custom
            // first so a previous part's header key does not leak into a part that
            // sets none; then, if THIS part has its own header key, apply it. The
            // running key is transposed by THIS part's option so the accidental engine
            // suppresses in-key accidentals correctly and the key does not leak.
            _meta.KeyCustom = _meta.InitialKeyCustom;
            _meta.KeyTonicStep = globalKeyTonicStep;
            _meta.KeyTonicAlter = globalKeyTonicAlter;
            _meta.KeyPosition = globalKeyPosition;
            if (partKey != null)
            {
                ApplyPartHeaderKey(partKey);
                _meta.KeySharps = _octave.TransposeKeySharps(_meta.KeySharps);
                voiceKeyDict[voiceName] = new KeySignature(_meta.KeySharps, _meta.KeyCustom);
            }
            else
            {
                _meta.KeySharps = _octave.TransposeKeySharps(_meta.InitialKeySharps);
                if (_octave.HasTranspose)
                    voiceKeyDict[voiceName] = new KeySignature(_meta.KeySharps);
            }

            _openingKeyOverride = null;
            staffVoices[voiceName] = CollectStaffVoices(voiceName);

            // A section that OPENS with its own key (`part b { section A { key a … } }`)
            // gives THIS staff its own opening signature — a sibling staff's different
            // opening key must not overwrite it via the shared score key. Overrides the
            // transpose entry above when both apply (the section key is what is written).
            if (_openingKeyOverride is { } openingKey)
                voiceKeyDict[voiceName] = new KeySignature(openingKey.Sharps, openingKey.Custom);
        }

        // Rows collect AFTER the music. A rows-only score has no music to
        // register the section starts — derive them from the row blocks.
        if (pendingChordRows.Count > 0 || pendingLyricsRows.Count > 0)
            EnsureSectionStartsForRows(tree.GetRoot());
        _chordNameCollector.KeyByMeasure = BuildKeyTimeline();
        _chordNameCollector.SectionStarts = _sectionState.AllStarts;
        foreach (var (rowName, rowIdx, rowMode) in pendingChordRows)
        {
            var rowMeasures = _chordNameCollector.CollectPart(
                tree.GetRoot(), rowName, rowIdx, _sectionState.StartMeasure, _meta.TimeBeats, _meta.TimeBeatType, rowMode);
            staffVoices[rowName] = ImmutableArray.Create(new Voice(rowName, rowMeasures));
        }

        // Now that the music is collected, gather the lyrics rows. The wrap bar
        // count is the longest real (non-lyrics-row) part — so a single block whose
        // verses are written flat auto-stacks every `wrapBars` measures.
        if (pendingLyricsRows.Count > 0)
        {
            int wrapBars = 0;
            foreach (var kv in staffVoices)
                if (!_lyricsRowNames.Contains(kv.Key))
                    foreach (var v in kv.Value)
                        wrapBars = Math.Max(wrapBars, v.Measures.Length);
            foreach (var (name, idx) in pendingLyricsRows)
            {
                // A track that SINGS a part places its syllables at that part's
                // rhythm: the row is the lyric line of a melody the score does
                // not engrave (the LilyPond shape is \lyricsto over a NullVoice
                // - the moments join the spacing, the notes print nothing).
                if (Music.LyricBindings.TargetOf(tree.GetRoot(), name) is { } sings)
                {
                    var melody = staffVoices.TryGetValue(sings, out var mv)
                        && mv.Length > 0 && mv[0].Measures.Length > 0
                        ? mv[0].Measures
                        : CollectMelodyFor(tree, renderSpec, sings);
                    if (!melody.IsDefaultOrEmpty)
                    {
                        _lyricsCollector.CollectBoundRow(tree.GetRoot(), name, idx,
                            melody.ToList(), _sectionState.StartMeasure, _sectionState.AllStarts);
                        staffVoices[name] = ImmutableArray.Create(new Voice(name, SpacerSkeleton(melody)));
                        continue;
                    }
                }
                var rowMeasures = _lyricsCollector.CollectRow(
                    tree.GetRoot(), name, idx, wrapBars, _sectionState.StartMeasure, _meta.TimeBeats, _meta.TimeBeatType,
                    _sectionState.AllStarts);
                staffVoices[name] = ImmutableArray.Create(new Voice(name, rowMeasures));
            }
        }

        // A rows-only score prints its section labels from the FIRST row's
        // measures (that row is the PrimaryContentStaff fallback the mark
        // merge reads). No-op for mixed scores: the label list only fills
        // when no music registered the sections.
        if (_sectionState.RowLabels.Count > 0)
        {
            string? firstRowName = renderSpec.Items
                .Select(it => it switch
                {
                    ChordRowSpec c => c.PartName,
                    LyricsRowSpec l => l.PartName,
                    _ => null
                })
                .FirstOrDefault(n => n != null && staffVoices.ContainsKey(n));
            if (firstRowName != null
                && staffVoices[firstRowName] is { Length: > 0 } rowVoices
                && rowVoices[0].Measures.Length > 0)
            {
                var ms = rowVoices[0].Measures.ToArray();
                foreach (var (idx, label, pos) in _sectionState.RowLabels)
                {
                    if (idx >= 0 && idx < ms.Length)
                        ms[idx] = ms[idx] with { SectionLabel = label, SectionLabelPosition = pos };
                }
                staffVoices[firstRowName] = ImmutableArray.Create(
                    new Voice(firstRowName, ms.ToImmutableArray()));
            }
        }

        // Bar lines are score-synchronized: LilyPond's Timing context lives
        // at Score level, so a repeat/double/final bar set by ANY part
        // appears in EVERY part at that measure.
        // LILYPOND-REF: ly/engraver-init.ly — "Timing" alias on Score;
        //   lily/bar-engraver.cc reads Timing.whichBar score-wide.
        // Sync barlines score-wide across EVERY voice (including a staff's extra
        // voices). Voice names are unique (part name, plus a ".N" suffix per
        // intra-staff voice), so a flat dict round-trips cleanly.
        var flatVoices = new Dictionary<string, Voice>();
        foreach (var vs in staffVoices.Values)
            foreach (var v in vs)
                flatVoices[v.Name] = v;
        // A part this score OMITS still contributes score-level barlines (|: :|) and navigation
        // marks. Feed its voices into the barline sync so the repeat propagates onto the drawn
        // rows; the sentinel key keeps them out of the write-back below (only staffVoices' real
        // names are drawn). Its navigation marks are merged into _musicMarks inside the harvest.
        if (harvestStructureMarks)
            foreach (var v in HarvestOmittedStructure(tree, renderSpec))
                flatVoices["omit:" + v.Name] = v;
        // A score with NO staff has no voice that ran ProcessForm, so nothing turned its
        // form's `|: … :|` into a barline anywhere — the grid landed on the right bars but
        // drew none of the lines around them. EnsureSectionStartsForRows recorded them;
        // hand them over as one synthetic voice and the sync above spreads them onto the
        // drawn rows, exactly as it does for an undrawn part's. Added LAST so the pairing
        // scan below still reads a real voice, and behind the same "omit:" sentinel so the
        // write-back never draws it.
        // ⚠️ This is the ONE road for both shapes of a staffless score. The harvest cannot
        // serve the other one: a part-less chord grid (`chords X { section A { … } }`) has
        // no omitted part to collect, and a `|:` written in the FORM is invisible to
        // PartHasStructure either way, since that gate reads a part's MUSIC.
        if (_rowsOnlyFormBars.Count > 0 && _rowsOnlyFormGridBars > 0)
            flatVoices["omit:form-structure"] = new Voice("form-structure",
                Enumerable.Range(0, _rowsOnlyFormGridBars)
                    .Select(i =>
                    {
                        var (start, end) = _rowsOnlyFormBars.TryGetValue(i, out var b)
                            ? b : (BarlineType.None, BarlineType.None);
                        return new Measure(ImmutableArray<MusicItem>.Empty, start, end,
                            sectionLabel: null, sourceStart: 0, sourceEnd: 0);
                    })
                    .ToImmutableArray());
        SynchronizeBarlines(flatVoices);
        // The repeat-bar pairing is a SCORE-level fact, so it is read here — after the sync
        // that gives every voice the score's barlines, and including the omitted parts fed
        // in above — from ONE voice, not once per staff.
        foreach (var anyVoice in flatVoices.Values)
        {
            RepeatPairingScanner.Scan(anyVoice, _unpairedRepeatWarnings);
            break;
        }
        foreach (var key in staffVoices.Keys.ToArray())
            staffVoices[key] = staffVoices[key]
                .Select(v => _tabResolver.ResolveVoiceTabTies(flatVoices[v.Name])).ToImmutableArray();

        // Note-bound lyrics attach EXPLICITLY via `staff NAME with lyrics L` — there is
        // NO implicit auto-attach (an unreferenced `lyrics {}` block is a LYS4006 error).
        // Group each staff's `with lyrics` names, align them to that staff's primary
        // voice, and tag them with its staff index so they sit under THAT staff.
        foreach (var group in attachedLyrics.GroupBy(a => (a.StaffIndex, a.StaffVoice)))
        {
            if (staffVoices.TryGetValue(group.Key.StaffVoice, out var lyStaffVoices)
                && lyStaffVoices.Length > 0)
                _lyricsCollector.CollectAttached(
                    tree.GetRoot(), group.Select(a => a.PartName).ToList(),
                    lyStaffVoices[0].Measures.ToList(), group.Key.StaffIndex,
                    _lyricsRowNames, _voiceMeasuresByName,
                    _sectionState.StartMeasure, _sectionState.AllStarts);
        }
        _chordNameCollector.KeyByMeasure = BuildKeyTimeline();
        _chordNameCollector.SectionStarts = _sectionState.AllStarts;
        // (The nameless `chords { }` auto-attach is gone — LYS0032. It was the one
        // band a score never placed, and its "co-written staff" association was a
        // hard-coded staff 0 on any multi-staff score.)
        foreach (var (attachedPart, attachedStaff, attachedMode) in attachedChords)
            _chordNameCollector.CollectAttached(
                tree.GetRoot(), attachedPart, _sectionState.StartMeasure, attachedStaff,
                _meta.TimeBeats, _meta.TimeBeatType, attachedMode);

        // Phase 3: Build staff groups from render spec
        // A combinedStaff also reports how it re-addressed its parts, because building it
        // is where the two streams are rewritten (see CombinedStaffAddressing).
        var combinedAddressings = new List<CombinedStaffAddressing>();
        var staffGroups = renderSpec.ToStaffGroups(name =>
            staffVoices.TryGetValue(name, out var v) ? v
                : ImmutableArray.Create(new Voice(name, ImmutableArray<Measure>.Empty)),
            combinedAddressings)
            .ToImmutableArray();

        // Per-staff facts only the collector knows, stamped onto the staves the render
        // spec just built (a staff is keyed by its primary voice's name):
        //   · the transposed part's own key — concert staves keep null and fall back to
        //     the score key;
        //   · the offset of the `clef` that set this staff, so each staff's line-start
        //     clef carries its OWN data-pos instead of the score sharing one.
        if (voiceKeyDict.Count > 0 || voiceClefPosDict.Count > 0)
            staffGroups = staffGroups
                .Select(sg => sg with
                {
                    Staves = sg.Staves
                        .Select(st =>
                        {
                            if (voiceKeyDict.TryGetValue(st.PrimaryVoice.Name, out var k))
                                st = st with { PerStaffKeySignature = k };
                            if (voiceClefPosDict.TryGetValue(st.PrimaryVoice.Name, out var cp) && cp > 0)
                                st = st with { ClefPosition = cp };
                            return st;
                        })
                        .ToImmutableArray()
                })
                .ToImmutableArray();

        // Size each lyrics text row's band to its tallest verse stack, so a
        // multi-verse row (auto-wrapped, or 1番/2番/3番) reserves room for verse 2+
        // instead of overlapping the staff below. Staff order matches the global
        // staff index the lyrics were tagged with (GetVoiceNames == ToStaffGroups).
        var rowVerses = new Dictionary<int, int>();
        foreach (var ly in _lyricsCollector.Lyrics)
            if (ly.IsLyricsRow)
                rowVerses[ly.StaffIndex] = Math.Max(
                    rowVerses.TryGetValue(ly.StaffIndex, out var mv) ? mv : 0, ly.VerseNumber);
        if (rowVerses.Count > 0)
        {
            int gsi = 0;
            staffGroups = staffGroups
                .Select(sg => sg with
                {
                    Staves = sg.Staves
                        .Select(st =>
                        {
                            int idx = gsi++;
                            // Every LYRIC row is tagged (it lays out as a staff
                            // with the lines removed); the verse count grows its
                            // band beyond the first line.
                            // ...and it takes the Lyrics context's affinity, UP, where
                            // Staff.CreateTextRow left it at the ChordNames default of DOWN.
                            // LILYPOND-REF: ly/engraver-init.ly:648 Lyrics staff-affinity = UP.
                            return st.IsTextRow && rowVerses.TryGetValue(idx, out var verses)
                                ? st with
                                {
                                    TextRowVerses = verses,
                                    IsLyricsTextRow = true,
                                    StaffAffinity = Layout.StaffAffinityDirection.Up,
                                }
                                : st;
                        })
                        .ToImmutableArray()
                })
                .ToImmutableArray();
        }

        // Resolve tab string numbers per tab staff (tuning-dependent): explicit
        // \N kept, repeated pitches in a bar reuse the first string, the rest
        // auto-pick the nearest-fret string. Done here so the layout and every
        // render pass (fret number, stem, beam) read one consistent string.
        // A tablature context ALSO has no Accidental_engraver (ly/engraver-init.ly:1189,
        // :1213), so the same per-tab-staff copy of the voice drops every accidental —
        // see TabResolver.RemoveAccidentals for what they were reserving.
        staffGroups = staffGroups
            .Select(sg => sg with
            {
                Staves = sg.Staves
                    .Select(st => st.IsTab && st.Tuning.HasValue
                        ? st with { Voices = st.Voices.SetItem(0, TabResolver.RemoveAccidentals(_tabResolver.ResolveTabStrings(st.PrimaryVoice, st.Tuning.Value, st.TabSourceClef, st.Transposition))) }
                        : st)
                    .ToImmutableArray()
            })
            .ToImmutableArray();

        // Ottava DISPLAY transposition per staff (see OttavaTransposer): notes
        // under an 8va draw an octave lower (etc.) while sounding at the written
        // pitch. Each staff transposes only the spans authored on ITS OWN staff.
        // The global staff index is walked in the same order as the lyrics
        // band-sizing above, matching each mark's StaffIndex.
        {
            int ottavaStaffIndex = 0;
            staffGroups = staffGroups
                .Select(sg => sg with
                {
                    Staves = sg.Staves
                        .Select(st =>
                        {
                            var spans = DetectOttavaSpans(ottavaStaffIndex++);
                            return spans.Count == 0
                                ? st
                                : st with { Voices = st.Voices
                                    .Select(v => OttavaTransposer.Transpose(v, spans)).ToImmutableArray() };
                        })
                        .ToImmutableArray()
                })
                .ToImmutableArray();
        }

        var content = CaptureScoreContent(
            staffGroups.SelectMany(sg => sg.Staves).SelectMany(st => st.Voices)
                .Select(v => v.Measures.Length).DefaultIfEmpty(0).Max());
        // The voice-addressed islands were collected in each part's own terms; on a
        // combined staff that is not where they ended up. Translated HERE, on the finished
        // lists, because it is the last moment both things are known — the routing, which
        // only the staff build has, and the paired trill spanners, which only
        // CaptureScoreContent has. Everything the collector did with those addresses
        // BEFORE this point (ProbeTupletBrackets, feeding the beam stem resolution) read
        // the parts as they were collected, which is the space they are still in there.
        if (combinedAddressings.Count > 0)
            content = CombinedStaffReaddress.Apply(content, combinedAddressings);
        return ScoreAssembler.BuildMultiStaffScore(staffGroups, content);
    }

    /// <summary>
    /// The structure a part contributes is SCORE-LEVEL even when the part isn't drawn: navigation
    /// / rehearsal marks (segno / D.S. / …) and repeat barlines (|: :|) — every part shares the bar
    /// grid and repeats / navigates together. This collects the parts this score OMITS from an
    /// isolated pass against the SAME form (so bar indices match), merges the navigation marks the
    /// score is missing into <see cref="_musicMarks"/>, and RETURNS the omitted parts' voices so
    /// the caller feeds them to <see cref="SynchronizeBarlines"/> — that existing score-wide sync
    /// then propagates their repeat barlines onto the drawn rows. Voltas (spanning brackets, not a
    /// per-measure barline) remain a follow-up.
    /// </summary>
    private IReadOnlyList<Voice> HarvestOmittedStructure(SyntaxTree tree, RenderSpec renderSpec)
    {
        var root = tree.GetRoot();
        var rendered = renderSpec.GetVoiceNames().ToHashSet(StringComparer.Ordinal);
        var omitted = root.ChildNodes().OfType<PartDeclarationSyntax>()
            .Select(p => p.Name.Text)
            .Where(n => !rendered.Contains(n))
            .Distinct(StringComparer.Ordinal)
            .Where(n => PartHasStructure(root, n))
            .ToList();
        if (omitted.Count == 0)
            return System.Array.Empty<Voice>();

        // Isolated pass: draw ONLY the omitted parts against the SAME form, so their structure
        // lands on the same bar indices this score uses. A fresh collector keeps its state
        // separate, and the inner call skips the harvest so it can't recurse. In the
        // incremental session the pass rides the "harvest" resume channel (finding 3-5)
        // instead of walking the omitted parts' whole book per keystroke.
        var items = omitted
            .Select(n => (RenderItemSpec)new SingleStaffSpec(new StaffSpec(ClefType.Treble, n)))
            .ToImmutableArray();
        MultiStaffScore harvested;
        try { harvested = CollectNested("harvest", tree, renderSpec with { Items = items }, harvestStructureMarks: false); }
        catch { return System.Array.Empty<Voice>(); } // a harvest failure must never break the render

        foreach (var mark in harvested.MusicMarks)
        {
            if (!IsStructuralMark(mark.Type))
                continue;
            if (_musicMarks.Any(m => m.Type == mark.Type && m.MeasureIndex == mark.MeasureIndex
                    && m.SourcePosition == mark.SourcePosition))
                continue;
            _musicMarks.Add(mark);
        }

        // Volta brackets ([1.] [2.]) are system-level like navigation marks — merge the ones the
        // score is missing (a part the score draws already contributed its own, deduped here).
        foreach (var volta in harvested.VoltaBrackets)
        {
            if (_voltaBrackets.Any(v => v.StartMeasureIndex == volta.StartMeasureIndex
                    && v.VoltaText == volta.VoltaText && v.SourcePosition == volta.SourcePosition))
                continue;
            _voltaBrackets.Add(volta);
        }

        return harvested.StaffGroups.SelectMany(g => g.Staves).SelectMany(s => s.Voices).ToList();
    }

    /// <summary>Runs one NESTED collect (finding 3-5): in the incremental session it
    /// rides the named resume channel — a planned resume adopts the channel baseline's
    /// unchanged prefix, a full run re-records the channel — and without a session it
    /// is exactly the historical fresh-collector call. THE ABORT CONTRACT: a resumed
    /// nested collect that bails on structural drift falls back to a FULL nested
    /// collect here, before any caller's catch-all can turn the bail into an empty
    /// result (an empty harvest would silently drop the score's repeat barlines).</summary>
    private MultiStaffScore CollectNested(string channelKey, SyntaxTree tree, RenderSpec spec,
        bool harvestStructureMarks, bool blankMeter = false)
    {
        MultiStaffScore Finish(MultiStaffScore s) => blankMeter ? MeterStencil.Blank(s) : s;

        if (NestedResume?.Begin(channelKey) is not { } begun)
            return Finish(new MeasureCollector { BeamMemo = BeamMemo }
                .CollectMultiStaff(tree, spec, harvestStructureMarks));

        if (begun.IsResume)
        {
            try
            {
                return Finish(new MeasureCollector { WalkProbe = begun.Probe, BeamMemo = BeamMemo }
                    .CollectMultiStaff(tree, spec, harvestStructureMarks));
            }
            catch (CollectResumeAbortException)
            {
                // Structural drift the plan-time guards could not see: run full below
                // (which also re-records the channel's baseline).
            }
            begun = (CollectWalkProbe.Recorder(), false);
        }

        var sub = new MeasureCollector { WalkProbe = begun.Probe, BeamMemo = BeamMemo };
        var result = sub.CollectMultiStaff(tree, spec, harvestStructureMarks);
        NestedResume!.Complete(channelKey, begun.Probe, sub);
        return Finish(result);
    }

    /// <summary>True when <paramref name="partName"/> writes score-level structure (a navigation
    /// mark, an inline volta or a repeat barline) in its music — the cheap gate that skips the
    /// isolated harvest pass when there is nothing to harvest. A part's music has TWO spellings
    /// (GRAMMAR §7: part-major cells inside <c>part X { section A { … } }</c>, section-major
    /// cells inside <c>section A { X { … } }</c>), and the gate must read BOTH: it used to scan
    /// only the part DECLARATION's subtree, so a repeat barline written in the section-major
    /// spelling — the fixture idiom — never opened the harvest and the omitted part's repeats
    /// silently vanished from the drawn score (measured 2026-08-27: the two spellings of one
    /// book rendered different pages; UnrenderedPartStructureMarkTests' section-major twins pin
    /// the repair). The harvest itself always handled both — ProcessSectionBody walks
    /// section-major cells first and falls back to part-major cells — only this gate was blind.
    /// Structure the part reaches only through a PHRASE REFERENCE (<c>hook</c> where
    /// <c>phrase hook { |: … :| }</c>) counts too: the harvest's nested collect expands
    /// references (ExpandVariable) and carries the structure correctly — measured
    /// 2026-08-27 by forcing the gate open with a direct mark — so a gate blind to them
    /// silently dropped exactly that book's repeats. The gate walks the referenced
    /// phrase bodies transitively (visited set: phrase DAGs share and cycle-guard
    /// elsewhere; first declaration of a duplicate name wins, as in CollectDefinitions).
    /// A reference that resolves to no phrase declaration contributes nothing.</summary>
    private static bool PartHasStructure(SyntaxNode root, string partName)
    {
        // Green finder (kind pre-filter, red type test stays the authority):
        // this gate runs per part per collect, and the red DescendantNodes walk
        // materialized the part's whole subtree just to type-test it. One walk
        // answers both questions — written structure, and which phrases are
        // referenced (scanned after, so a direct hit never pays the phrase walk).
        static bool ScanScope(SyntaxNode scope, List<string> refs)
        {
            foreach (var n in scope.GreenSites(static g => (
                    g.Kind is SyntaxKind.NavigationMark or SyntaxKind.InlineVolta
                        or SyntaxKind.Barline or SyntaxKind.VariableReference,
                    Descend: true)))
            {
                switch (n)
                {
                    case NavigationMarkSyntax or InlineVoltaSyntax:
                        return true;
                    case BarlineSyntax bl when bl.BarToken.Text.Contains(':'):
                        return true;
                    case VariableReferenceSyntax vr:
                        refs.Add(vr.Name.Text);
                        break;
                }
            }
            return false;
        }

        var refs = new List<string>();
        var part = root.ChildNodes().OfType<PartDeclarationSyntax>()
            .FirstOrDefault(p => p.Name.Text == partName);
        if (part != null && ScanScope(part, refs))
            return true;
        // The section-major cells: direct children of each section declaration, the
        // same discovery ProcessSectionBody's own loop uses (grammar guarantee there).
        foreach (var section in root.ChildNodes().OfType<SectionDeclarationSyntax>())
            foreach (var child in section.ChildNodes())
                if (child is PartBlockSyntax pb && pb.Name == partName && ScanScope(pb, refs))
                    return true;
        if (refs.Count == 0)
            return false;

        // Referenced phrase bodies, transitively. Each body is scanned at most once
        // per call (visited), and the declaration map is built only when a reference
        // actually occurred.
        Dictionary<string, PhraseDeclarationSyntax>? phrases = null;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>(refs);
        while (queue.Count > 0)
        {
            var name = queue.Dequeue();
            if (!visited.Add(name))
                continue;
            phrases ??= root.ChildNodes().OfType<PhraseDeclarationSyntax>()
                .GroupBy(p => p.Name.Text, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            if (!phrases.TryGetValue(name, out var decl))
                continue;
            var inner = new List<string>();
            if (ScanScope(decl.Body, inner))
                return true;
            foreach (var n in inner)
                queue.Enqueue(n);
        }
        return false;
    }

    /// <summary>The score-level mark types worth harvesting from an unrendered part: navigation
    /// and rehearsal marks (not per-staff dynamics or the piece-wide tempo).</summary>
    private static bool IsStructuralMark(MusicMarkType t) => t is
        MusicMarkType.Segno or MusicMarkType.Coda or MusicMarkType.Fine or MusicMarkType.ToCoda
        or MusicMarkType.DalSegno or MusicMarkType.DaCapo
        or MusicMarkType.DalSegnoAlFine or MusicMarkType.DalSegnoAlCoda
        or MusicMarkType.DaCapoAlFine or MusicMarkType.DaCapoAlCoda
        or MusicMarkType.Rehearsal;

    private List<Measure> CollectMeasuresForVoice(string voiceName)
    {
        // Reset spans so a << \\ >> inside one staff doesn't leak into the next;
        // the caller (CollectStaffVoices) reads them back to rebuild this staff's
        // extra voices.
        _parallelSpans.Clear();

        // 1. Check variables first
        if (_variables.TryGetValue(voiceName, out var variable))
            return CollectMeasuresFromNode(variable);

        // 2. Delegate to the structure-aware CollectMeasures path so that
        //    structure { A B C ... } expansion concatenates all sections'
        //    PartBlocks for this voice. ProcessSection filters by _voiceName
        //    (already set by the caller) so only matching PartBlocks are taken.
        return CollectMeasures();
    }

    /// <summary>
    /// Reconstructs a multi-voice <see cref="Score"/> after the primary stream
    /// (<paramref name="track0"/>) has been collected and the parallel
    /// <c>voice { … }</c> spans recorded in <see cref="_parallelSpans"/>. Voice 0
    /// is the primary stream; each additional voice is a full-length, synchronized
    /// measure list that is empty except where a span supplies its sub-voice.
    /// </summary>
    private Score BuildMultiVoiceScore(List<Measure> track0, SyntaxNode root,
        string? attachedChordPart = null,
        ChordDisplayMode attachedChordDisplay = ChordDisplayMode.Names,
        IReadOnlyList<string>? attachedLyricParts = null)
    {
        var voices = new List<Voice>
        {
            new Voice("voice1", track0.ToImmutableArray())
        };
        var extras = BuildExtraVoiceTracks(track0);
        for (int i = 0; i < extras.Count; i++)
            voices.Add(new Voice($"voice{i + 2}", extras[i]));

        // Tie sanity scan per voice, BEFORE the ottava display transposition.
        foreach (var v in voices)
            ScanVoiceSanity(v);

        // Ottava DISPLAY transposition (single staff → staff 0). See OttavaTransposer.
        var multiVoiceOttava = DetectOttavaSpans(0);
        if (multiVoiceOttava.Count > 0)
            for (int i = 0; i < voices.Count; i++)
                voices[i] = OttavaTransposer.Transpose(voices[i], multiVoiceOttava);

        // Map named voices (voice sop { … }) to their measure track so a
        // `lyrics sop { … }` block can bind to it. Track 0 is voice 1, then extras.
        foreach (var (parallel, _, _, _) in _parallelSpans)
        {
            int vi = 0;
            foreach (var (name, _) in parallel.NamedVoices)
            {
                if (name != null && !_voiceMeasuresByName.ContainsKey(name))
                {
                    if (vi == 0)
                        _voiceMeasuresByName[name] = (0, track0);
                    else if (vi - 1 < extras.Count)
                        _voiceMeasuresByName[name] = (vi, extras[vi - 1].ToList());
                }
                vi++;
            }
        }

        // Explicit attach by band order (`staff NAME  lyrics L`, folded into
        // WithLyrics) — no implicit auto-attach (see Collect). Named blocks whose
        // name is a `voice NAME` bind to that voice; the rest align to voice 1.
        if (attachedLyricParts is { Count: > 0 })
            _lyricsCollector.CollectAttached(root, attachedLyricParts, track0, 0,
                _lyricsRowNames, _voiceMeasuresByName, _sectionState.StartMeasure, _sectionState.AllStarts);
        _chordNameCollector.KeyByMeasure = BuildKeyTimeline();
        _chordNameCollector.SectionStarts = _sectionState.AllStarts;
        // Attached chords on a multi-voice single staff — collected here because
        // Collect's own CollectAttached is skipped by the multi-voice early return.
        if (attachedChordPart != null)
            _chordNameCollector.CollectAttached(
                root, attachedChordPart, _sectionState.StartMeasure, _cursor.StaffIndex,
                _meta.TimeBeats, _meta.TimeBeatType, attachedChordDisplay);

        // A single-staff score surfaces the same annotations whether it has one
        // voice or several — a multi-voice (voice { } blocks) score keeps its chord
        // names / percent repeats, which the old construction here silently dropped.
        return ScoreAssembler.BuildScore(
            ResolveStaffColumns(voices.ToImmutableArray()),
            CaptureScoreContent(voices.Select(v => v.Measures.Length).DefaultIfEmpty(0).Max()));
    }

    /// <summary>
    /// The two cross-voice facts a staff resolves before spacing sees it: the forced stem
    /// directions, and — because they depend on those and on the collision they produce —
    /// the packing of each column's accidentals into ONE accidental column.
    /// </summary>
    /// <remarks>
    /// Order matters: <see cref="StaffAccidentalColumns"/> asks
    /// <see cref="Layout.NoteCollision"/> which head sits where, and that answer is read off
    /// the stem directions this bakes first.
    /// </remarks>
    private static ImmutableArray<Voice> ResolveStaffColumns(ImmutableArray<Voice> voices)
        => StaffAccidentalColumns.Resolve(ResolveVoiceStemDirections(voices));

    /// <summary>
    /// Builds the measure tracks for voices 1..N-1 of a <c>&lt;&lt; \\ &gt;&gt;</c> mixed stream
    /// from the spans recorded in <see cref="_parallelSpans"/>: each track is
    /// full length, empty except where a span supplies its sub-voice. Shared by
    /// the single-staff Score path (<see cref="BuildMultiVoiceScore"/>) and the
    /// per-staff multi-staff path (<see cref="CollectStaffVoices"/>).
    /// </summary>
    private List<ImmutableArray<Measure>> BuildExtraVoiceTracks(List<Measure> track0)
    {
        int totalMeasures = track0.Count;
        int voiceCount = 1;
        foreach (var (parallel, _, _, _) in _parallelSpans)
            voiceCount = Math.Max(voiceCount, parallel.Voices.Count());

        var tracks = new List<ImmutableArray<Measure>>();
        for (int t = 1; t < voiceCount; t++)
        {
            var trackMeasures = new Measure[totalMeasures];
            for (int m = 0; m < totalMeasures; m++)
                trackMeasures[m] = EmptyMeasure(track0[m]);

            foreach (var (parallel, start, startOffset, spanFrame) in _parallelSpans)
            {
                var blocks = parallel.Voices.ToList();
                if (t >= blocks.Count)
                    continue;

                // Each sub-voice evaluates from the frame the SPAN OPENED IN — the same
                // one voice 0 read, so the voices are order-independent and none of them
                // drags the next (see _parallelSpans). ⚠️ It used to be the part's default
                // octave, which made `voice { c'1 } voice { d1 }` after a low g read its d
                // two octaves from where the MIDI put it.
                var savedOctave = _octave.Snapshot();
                var savedDuration = _defaultDuration;
                var savedDots = _defaultDots;
                _octave.Restore(spanFrame);
                _defaultDuration = Fraction.Quarter;
                _defaultDots = 0;

                // The sub-voice's cursor, installed for exactly the walk below:
                //   * MetadataMeasureOffset — per-note metadata in this sub-voice is
                //     keyed by its local 0-based measure index; shift it to the span's
                //     real start so dynamics etc. land in the right measure.
                //   * VoiceIndex — tag this sub-voice's tuplets so their beam-breaking
                //     boundaries never leak into a sibling voice. Counted FROM THE PART'S
                //     OWN SLOT, not from zero: on a shared staff the part's voices sit at
                //     `_cursor.VoiceIndex + t` of Staff.Voices (the binding loop hands the
                //     part its base; see VoiceSlotting), and on every staff of its own that
                //     base is 0, which is the arithmetic this replaced.
                //   * VoiceScope — render voice number is t+1; an override in this
                //     sub-voice scopes to it. NOT slotted: it is the voice's number WITHIN
                //     ITS PART (what `voice { } { }` wrote), which is what a part-scoped
                //     override and the voice-1-up/voice-2-down default both mean.
                // The scope RESTORES THE SAVED CURSOR where the manual lines it
                // replaced reset to zeros — the same values, because this loop only
                // ever runs on the primary stream's cursor (StaffIndex is carried
                // through unchanged by the `with`, and the walk that fills
                // _parallelSpans restores its own VoiceScope toggle before returning),
                // and iteration t+1 saves what iteration t's Dispose just restored.
                List<Measure> sub;
                using (new CursorScope(this, _cursor with
                {
                    MetadataMeasureOffset = start,
                    VoiceIndex = _cursor.VoiceIndex + t,
                    VoiceScope = t + 1,
                }))
                {
                    sub = CollectMeasuresFromNode(blocks[t], applyFilePartial: start == 0,
                        leadingOffset: startOffset);
                    ResolveBeamStemDirections(sub);
                }

                _octave.Restore(savedOctave);
                _defaultDuration = savedDuration;
                _defaultDots = savedDots;

                for (int k = 0; k < sub.Count && start + k < totalMeasures; k++)
                    trackMeasures[start + k] = sub[k];
            }

            tracks.Add(trackMeasures.ToImmutableArray());
        }
        return tracks;
    }

    /// <summary>
    /// Collects ALL voices of one staff in a multi-staff score: the primary
    /// (voice-0) stream plus any voices contributed by <c>&lt;&lt; \\ &gt;&gt;</c> spans inside
    /// that staff. Voice 0 keeps the part's name (the staff is keyed by it for
    /// per-staff key signatures); extra voices get a derived name.
    /// </summary>
    private ImmutableArray<Voice> CollectStaffVoices(string voiceName)
    {
        var track0 = CollectMeasuresForVoice(voiceName); // clears + fills _parallelSpans
        ResolveBeamStemDirections(track0);

        var voices = ImmutableArray.CreateBuilder<Voice>();
        voices.Add(new Voice(voiceName, track0.ToImmutableArray()));
        var extras = BuildExtraVoiceTracks(track0);
        for (int i = 0; i < extras.Count; i++)
            voices.Add(new Voice($"{voiceName}.{i + 2}", extras[i]));
        // Tie sanity scan per staff voice, BEFORE any display-only transform.
        foreach (var v in voices)
            ScanVoiceSanity(v);
        return ResolveStaffColumns(voices.ToImmutable());
    }

    /// <summary>
    /// Runs the per-voice sanity scanners over a finished voice — ONCE per voice name,
    /// however many staves engrave it. THREE scanners, FOUR sinks: the cue-boundary list is
    /// filled by the tie scanner AND the slur scanner, so counting scanners undercounts what
    /// this guard protects (the first test written for it stopped at three and left the cue
    /// crossing — an error, not a warning — unpinned).
    /// </summary>
    /// <remarks>
    /// ⚠️ THE GUARD IS THE POINT, not the bundling. A part engraved on two staves
    /// (<c>score { staff bass  tab bass }</c> — 260 of the 899-book corpus) is collected
    /// once per staff, and these scanners append to collector-wide cumulative lists
    /// (see <see cref="CumulativeSideTables"/>). Before the guard, every tie, slur and beam
    /// complaint in such a part was printed once per staff, at the SAME source position,
    /// for the SAME slip — measured 2026-08-29 in 4 corpus books, and reproduced for all
    /// three scanners by <c>MultiStaffPartScansOnce</c>.
    /// <para>
    /// Deduping the DIAGNOSTICS instead would have been wrong: the measure passes also emit
    /// byte-identical lines, and there they name genuinely DIFFERENT bars that the widened
    /// lead-in address happens to collapse onto one position (<see cref="Semantics.MeasureValidator"/>,
    /// <c>Reported</c>). Only the repeated SCAN is a duplicate; repeated text is not.
    /// </para></remarks>
    private void ScanVoiceSanity(Voice voice)
    {
        if (!_sanityScannedVoices.Add(voice.Name))
            return;
        TieTargetScanner.Scan(voice, _tieTargetWarnings, _cueSpanBoundaryWarnings);
        SlurPairingScanner.Scan(voice, _unpairedSlurWarnings, _cueSpanBoundaryWarnings);
        BeamPairingScanner.Scan(voice, _unpairedBeamWarnings);
    }

    /// <summary>The measures of a part this score does NOT engrave, collected the
    /// way a staff of it would be - a fresh sub-collector over a one-staff spec of
    /// the SAME form, so bar indexing and section starts line up and none of this
    /// collector's side collections (dynamics, articulations, marks) pick up the
    /// melody's. Used by a melody-bound lyrics row. In the incremental session the
    /// sub-collect rides its own per-part resume channel (finding 3-5).</summary>
    private ImmutableArray<Measure> CollectMelodyFor(SyntaxTree tree, RenderSpec renderSpec, string partName)
    {
        var (partClef, _, _, _, _, _) = GetPartDefaults(tree.GetRoot(), partName);
        var spec = renderSpec with
        {
            Items = ImmutableArray.Create<RenderItemSpec>(
                new SingleStaffSpec(new StaffSpec(ParseClefType(partClef ?? "treble"), partName))),
        };
        try
        {
            // The public CollectMultiStaff's exact behavior (harvest on, meter blanked),
            // through the channel.
            var sub = CollectNested("melody:" + partName, tree, spec,
                harvestStructureMarks: true, blankMeter: true);
            foreach (var group in sub.StaffGroups)
                foreach (var st in group.Staves)
                    if (st.Voices.Length > 0)
                        return st.Voices[0].Measures;
            return ImmutableArray<Measure>.Empty;
        }
        catch
        {
            // A malformed melody surfaces its real error through the validators;
            // the row then falls back to the even-spread reading.
            return ImmutableArray<Measure>.Empty;
        }
    }

    /// <summary>The row skeleton of a melody-bound lyrics row: the melody's
    /// measures with every item replaced by an invisible spacer of the same
    /// length, 1:1 by item index so the syllable alignment's (measure, item)
    /// coordinates and timings stay real columns. The row occupies the melody's
    /// time without drawing its notes.</summary>
    private static ImmutableArray<Measure> SpacerSkeleton(ImmutableArray<Measure> melody)
    {
        var result = ImmutableArray.CreateBuilder<Measure>(melody.Length);
        foreach (var m in melody)
        {
            var items = ImmutableArray.CreateBuilder<MusicItem>(m.Items.Length);
            foreach (var item in m.Items)
                // Duration folds dots and the tuplet TimeScale, so a plain
                // spacer of it occupies exactly the item's sounding time.
                items.Add(new RestItem(item.Duration, 0, item.SourcePosition) { IsSpacer = true });
            result.Add(m with { Items = items.MoveToImmutable() });
        }
        return result.MoveToImmutable();
    }

    /// <summary>An empty placeholder measure (no items) that mirrors the
    /// barline/source span of the primary voice's measure at the same index,
    /// so an absent voice draws nothing while staying barline-aligned.</summary>
    private static Measure EmptyMeasure(Measure reference) =>
        new Measure(
            ImmutableArray<MusicItem>.Empty,
            BarlineType.None,
            reference.EndBarline,
            null,
            reference.SourceStart,
            reference.SourceEnd,
            isPickup: reference.IsPickup);

    /// <summary>
    /// Gathers a voice block's music nodes (variable refs expanded), used to
    /// flow a parallel span's first voice into the primary builder.
    /// </summary>
    private List<GreenSite> GatherVoiceMusicNodes(SyntaxNode voiceNode)
    {
        var musicNodes = new List<GreenSite>();
        foreach (var site in MusicSitesLazy(voiceNode, includeParallel: false))
            GatherMusicSite(site, musicNodes);
        return musicNodes;
    }

    // ⚠️ The `autoFinalBarline: false` parameter this used to carry is GONE with the rule
    // it suppressed (see FinalizeMeasures). Do not reintroduce either.
    private List<Measure> CollectMeasuresFromNode(SyntaxNode voiceNode,
        bool applyFilePartial = true, Fraction? leadingOffset = null)
    {
        var builder = new MeasureBuilder(TimeSignatureFraction, voiceNode.SourceStart);
        // The file-level pickup arms a sub-collection only when it really sits
        // at the piece's start (a mid-piece voice{} span must not shorten its
        // own first bar).
        if (applyFilePartial && _filePartial is { } subPickup)
            builder.SetPartial(subPickup);
        // A mid-measure << \\ >> span: pad the sub-voice up to the span's beat
        // with an invisible spacer, so its first sounding item lands where voice 0
        // (walked inline in the primary stream) already elapsed to. Same device
        // PartCombiner uses to pad a part up to an onset.
        if (leadingOffset is { } offset && offset != Fraction.Zero)
            builder.AddItem(new RestItem(offset, 0, voiceNode.SourceStart) { IsSpacer = true });
        _measureAccidentals.Clear();
        builder.MeasureCompleted = _measureAccidentals.Clear;

        _pendingInlineVoltas.Clear();

        // Collect all music nodes, expanding variable references. Container
        // expressions travel as one wrapper — EXCEPT parallel: the per-voice
        // path flattens << \\ >> (see GatherMusicSite), so its descendants
        // must reach the walk (includeParallel: false).
        var musicNodes = new List<GreenSite>();

        foreach (var site in MusicSitesLazy(voiceNode, includeParallel: false))
            GatherMusicSite(site, musicNodes);

        ProcessMusicNodeSequence(musicNodes, builder);

        FinalizeInlineVoltas();

        return builder.FinalizeMeasures();
    }

    // ===== expansion budget (the liveness guard on phrase/repeat expansion) =====

    /// <summary>
    /// Total expansion budget for ONE collect: the number of sites the score's
    /// phrase-reference DAG, <c>repeat unfold/percent</c> passes and <c>R1*N</c>
    /// interiors may emit before expansion is cut off. Without it this walk held
    /// the repository's only unbounded blowup: <c>ExpandVariable</c> re-expands
    /// sibling references, so <c>phrase p2 { p1 p1 } … p30 { p29 p29 }</c> is
    /// 2^29 sites from 30 written lines — and collect runs per keystroke, so the
    /// preview hung. Cutting off is a TRUNCATION, reported once per collect via
    /// <see cref="ExpansionBudgetExceededAt"/> (LYS1033).
    /// </summary>
    /// <remarks>
    /// LILYSHARP-OWN: no LilyPond counterpart — LilyPond itself hangs on the
    /// equivalent book (unbounded \repeat unfold / variable nesting). This is a
    /// liveness guard, not a semantic limit; it never goes away. Its value is
    /// bracketed from both sides, measured 2026-08-26: ABOVE every real book —
    /// the corpus's largest page-side book is 8,000 written positions (24,000
    /// only as MIDI replays, which no page site pays; audit/LilySharp.Probe
    /// pitches over all 567) — and at the size past which the line-break DP's
    /// dense (n+1)² arrays die TODAY regardless of any budget (a 10^5-site
    /// survivor = ~25k measures took 81 s and then failed allocation when this
    /// cap was tried at 10^5), so nothing the cap truncates could have rendered
    /// without it. The truncation is still reported (LYS1033) even when the
    /// survivor's layout fails: the diagnostic pass collects without layout.
    /// Observed by ExpansionBudgetTests (small-cap truncation + default-cap
    /// pass-through) and ExpansionBudgetValidator (the diagnostic).
    /// The const is THE number's one home — <see cref="Semantics.MeasureModel"/>'s
    /// flattening walk (the diagnostics-path second expander of the same DAG)
    /// caps at the same value.
    /// </remarks>
    internal const int DefaultExpansionBudgetCap = 50_000;

    /// <inheritdoc cref="DefaultExpansionBudgetCap"/>
    internal int ExpansionBudgetCap { get; init; } = DefaultExpansionBudgetCap;

    /// <summary>Remaining budget; replenished by <see cref="Reset"/>.</summary>
    private int _expansionBudget;

    /// <summary>Source position of the first construct that ran out of budget,
    /// or −1. At most one report per collect — every later charge fails silently
    /// (the score is already truncated; more arrows at it help nobody).</summary>
    private int _expansionBudgetExceededAtField = -1;

    /// <summary>Where expansion was cut off, or null when the whole score fit
    /// (the overwhelmingly normal case). Read by ExpansionBudgetValidator.</summary>
    public int? ExpansionBudgetExceededAt
        => _expansionBudgetExceededAtField >= 0 ? _expansionBudgetExceededAtField : null;

    /// <summary>
    /// Takes <paramref name="sites"/> out of the budget; false = the budget is
    /// spent and the caller must stop expanding (emit nothing more, break its
    /// loop). Truncation must be DETERMINISTIC per source, which a resumed
    /// collect cannot guarantee (its adopted prefix charged nothing), so a trip
    /// in resume mode aborts to the full collect instead, and a trip while
    /// recording marks the walk ineligible so no later edit resumes from a
    /// truncated recording. Residual window, named on purpose: an edit that
    /// pushes a book ACROSS the cap can complete via a resume planned from a
    /// pre-trip recording where a from-scratch collect would truncate — it
    /// self-heals at the next full collect, and only books within one edit of
    /// 10^6 sites can see it.
    /// </summary>
    private bool ChargeExpansion(int sites, int sourcePosition)
    {
        if (_expansionBudget >= sites)
        {
            _expansionBudget -= sites;
            return true;
        }
        _expansionBudget = 0;
        if (WalkProbe is { IsRecording: false })
            throw new CollectResumeAbortException(
                "expansion budget exceeded mid-resume; replaying the collect in full");
        _probeRecording?.MarkIneligible("expansion-budget");
        if (_expansionBudgetExceededAtField < 0)
            _expansionBudgetExceededAtField = sourcePosition;
        return false;
    }

    private void Reset()
    {
        _expansionBudget = ExpansionBudgetCap;
        _expansionBudgetExceededAtField = -1;

        // The cumulative output tables clear FROM THE REGISTRY that names them
        // (CumulativeSideTables) — a table added there is reset here by construction,
        // instead of joining a second hand-written enumeration that historically
        // drifted (before this fold, _musicMarks/_customTexts/_voltaBrackets/
        // _tupletBrackets/_navPlacementWarnings were absent from Reset).
        // MeasureCollectorResetTests holds the reverse direction: no collection
        // field of this class escapes Reset entirely.
        foreach (var table in CumulativeSideTables())
            table.Clear();

        _sectionState.Reset();
        _variables.Clear();
        _rowsOnlyFormBars.Clear();
        _rowsOnlyFormGridBars = 0;
        // ⚠️ MetadataMeasureOffset is deliberately NOT written here, preserving the
        // manual reset verbatim (the bundling changed spellings, not behavior): it is
        // nonzero only inside BuildExtraVoiceTracks' span scope, which restores it
        // before anything can call Reset.
        _cursor.StaffIndex = 0;
        _cursor.VoiceIndex = 0;
        _cursor.VoiceScope = null;
        _chordNameCollector.Clear(); // grid warnings; its item list is registry-cleared
        _lyricsCollector.Clear();
        _sectionResetOverrides.Clear();
        _sectionActiveGrobProps.Clear();
        _keyByMeasure.Clear();
        _keyByMeasureLog.Clear();
        _sectionStartLog.Clear();
        _voiceMeasuresByName.Clear();
        _canonicalSectionBars.Clear();
        _courtesySourcePositions.Clear();
        _measureAccidentals.Clear();
        _fingeringByPosition.Clear();
        _markHostMeasure.Clear();
        // Deliberately OUTSIDE CumulativeSideTables (its remark says why): still an
        // output list, so it still resets.
        _unpairedRepeatWarnings.Clear();
        // Also outside the registry, for the opposite reason: this one is not an output
        // at all but the guard that keeps ScanVoiceSanity to one pass per voice, so it
        // is neither adopted nor shifted on resume. It MUST clear here all the same —
        // a reused collector meeting a new tree has to scan that tree's voices, and a
        // stale name would silence every such complaint in the part sharing that name.
        _sanityScannedVoices.Clear();
        // The mark-position probe cache mirrors _musicMarks (registry-cleared above),
        // so it resets with it or IsCollectedMusicMark would answer from a stale set.
        _musicMarkPositions.Clear();
        _musicMarkPositionsSynced = 0;
        // First-one-wins section-header tables: stale entries would WIN over the new
        // tree's headers on reuse, so these clear even though every walk repopulates.
        _sectionHeaderKeys.Clear();
        _sectionHeaderTimes.Clear();
        _sectionHeaderTempos.Clear();
        _sectionHeaderPartials.Clear();
        // Per-node resolution memos keyed by syntax node identity — a reused
        // collector on a NEW tree would never hit them, but a re-collect of the
        // SAME tree would replay stale octave context. Clear both.
        _resolvedNotes.Clear();
        _resolvedChordMembers.Clear();
        _drumOverrides = null;
        _openingKeyOverride = null;
        // Reused-instance hygiene: without these, a second Collect/CollectMultiStaff
        // on the same collector would carry a stale part-major cell map and lyric-row
        // names, and PitchTrace would grow without bound. (All current callers use a
        // fresh instance, so this only matters for reuse via the public API.)
        _lyricsRowNames = new();
        _form = null;
        _filePartial = null;
        _root = null;
        _octave.ResetAll();
        _defaultDuration = Fraction.Quarter;
        _defaultDots = 0;
        _meta.Reset();
        // Per-walk state that every walk clears/balances on its own; cleared here
        // too so an aborted collect (exception mid-walk) cannot leak into a reuse.
        _pendingInlineVoltas.Clear();
        _parallelSpans.Clear();
        _walkHeaderReads.Clear();
        _resolvedSpellingLog.Clear();
        _repetitionOriginalReads.Clear();
        _formRepeatDepth = 0;
        _phraseTransposeSaves.Clear();
        _phraseAnchorSaves.Clear();
        _phraseAbsoluteBaseSaves.Clear();
        // Probe bookkeeping restarts per collect; WalkProbe itself is the caller's
        // (set before Collect, read after).
        _walkOrdinal = 0;
        _probeRecording = null;
        _resumePending = null;
        _resumeRestoredSectionStart = null;
        _walkMaxSourceRead = 0;
        _suffixTargets = null;
    }

    /// <summary>
    /// Arms (or clears) the part-option transpose from the parsed target.
    /// </summary>
    private void ApplyTranspose((int step, int alt, int oct)? transpose)
    {
        // A per-score transpose composes on top of the part's own transpose, so a
        // Bb-part-score of an already-transposed part is shifted exactly once more.
        // OctaveContext owns the transpose state + application; we only compose the
        // effective target here (ScoreTranspose is a collector-level concern).
        _octave.SetTranspose(ComposeTranspose(transpose, ScoreTranspose));
    }

    /// <summary>
    /// Composes two transpose targets: apply <paramref name="outer"/> after
    /// <paramref name="inner"/>. Each target is the c-&gt;target interval; applying the
    /// outer interval to the inner target pitch yields the combined target.
    /// </summary>
    private static (int step, int alt, int oct)? ComposeTranspose(
        (int step, int alt, int oct)? inner, (int step, int alt, int oct)? outer)
        => PitchTransposer.Compose(inner, outer);

    // ===== Phrase auto-transpose (movable motif) =====

    /// <summary>Arms the running ambient tonic at the score's home key.</summary>
    private void ResetAmbientTonicToHome()
    {
        _ambientTonicStep = _meta.KeyTonicStep;
        _ambientTonicAlter = _meta.KeyTonicAlter;
        // A custom/atonal home key has no tonic to transpose from.
        _ambientTonicValid = _meta.InitialKeyCustom == null;
    }

    /// <summary>
    /// The c-relative transpose target that moves a phrase from the score's home
    /// key to the current ambient key, by the nearest octave (up if the shift is a
    /// tritone or less, otherwise down). Null when there is nothing to do — the
    /// ambient key equals home, or either key is custom/atonal — so the common
    /// no-modulation case is an exact no-op.
    /// </summary>
    private (int step, int alt, int oct)? PhraseTransposeTarget()
    {
        if (!_ambientTonicValid || _meta.InitialKeyCustom != null)
            return null;
        return PitchTransposer.MovableInterval(
            _meta.KeyTonicStep, _meta.KeyTonicAlter, _ambientTonicStep, _ambientTonicAlter);
    }

    /// <summary>
    /// Arms the phrase transpose at a phrase-reference boundary and saves the
    /// prior target so the paired <see cref="ExitPhraseTranspose"/> can restore
    /// it. The phrase shift composes UNDER any part/score transpose (the written
    /// pitch moves home→ambient first, then the instrument transpose applies).
    /// </summary>
    private void EnterPhraseTranspose(int? anchorStep = null, int octaveOffset = 0)
    {
        var saved = _octave.GetTranspose();
        _phraseTransposeSaves.Push(saved);
        if (PhraseTransposeTarget() is { } phrase)
            _octave.SetTranspose(ComposeTranspose(phrase, saved));
        // The reference's trailing marks (Chorus' / Chorus,) move the frame the body
        // resolves in. EnterDefaultFrame has already moved the RELATIVE frame; in
        // absolute mode there is no running frame, and the thing that plays its part —
        // the anchor bare c resolves against — is OctaveBase. Moving it here is what
        // makes `octave absolute` honour the marks, and nested references compose
        // additively because each pushes the base it found.
        _phraseAbsoluteBaseSaves.Push(_octave.OctaveBase);
        _octave.OctaveBase += octaveOffset;
        // Resolve the phrase's outgoing ANCHOR now, in the just-reset frame
        // (EnterDefaultFrame ran first at every call site): the bare-letter
        // resolution of the body's first pitched element — or the AMBIENT tonic
        // for a degree-opened body — exactly what a chord root would propagate.
        (char Name, int Octave)? anchor = null;
        if (anchorStep is { } astep)
        {
            int step = astep == Music.PhraseAnchor.Tonic
                ? (_ambientTonicValid ? _ambientTonicStep : 0)
                : astep;
            // Passing the frame's own letter back keeps Resolve's LastPitchName
            // write a no-op — the body's first note must still resolve against
            // the untouched reset frame.
            anchor = ("cdefgab"[step], _octave.Resolve(step, 0, _octave.LastPitchName));
        }
        _phraseAnchorSaves.Push(anchor);
    }

    /// <summary>
    /// Restores the transpose saved by <see cref="EnterPhraseTranspose"/> and
    /// hands the relative frame off at the phrase's ANCHOR — the chord rule: a
    /// reference is ONE item whose interior never leaks, so the note after
    /// <c>Melody'</c> is relative to the phrase's shifted anchor and editing the
    /// tail of a phrase body never moves the music that follows a reference. A
    /// pitchless body (rests only) hands nothing off. Only the anchoring moves;
    /// following notes still sound as written.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE ANCHOR HAND-OFF USED TO BE NESTED INSIDE THE DIATONIC GUARD, because the
    /// removed interval argument (<c>Melody'(3)</c>) had to shift the anchor by the same
    /// scale steps before handing it back. It is unconditional now — the guard went with
    /// the spelling (2026-08-28), and leaving the hand-off inside it would have silently
    /// stopped <c>Chorus'</c> / <c>Chorus,</c> from moving the frame at all.
    /// </remarks>
    private void ExitPhraseTranspose()
    {
        if (_phraseTransposeSaves.Count > 0)
            _octave.SetTranspose(_phraseTransposeSaves.Pop());
        if (_phraseAbsoluteBaseSaves.Count > 0)
            _octave.OctaveBase = _phraseAbsoluteBaseSaves.Pop();
        if (_phraseAnchorSaves.Count > 0 && _phraseAnchorSaves.Pop() is { } anchor)
        {
            _octave.LastPitchName = anchor.Name;
            _octave.CurrentOctave = anchor.Octave;
        }
    }

    private List<Measure> CollectMeasures()
    {
        // Snapshot this voice's score-level key (already transposed for the part)
        // so each section boundary can revert the running key to it.
        _sectionResetKeySharps = _meta.KeySharps;
        _sectionResetKeyCustom = _meta.KeyCustom;
        // Same for the clef: the part default a section without its own clef reverts to.
        _sectionResetClef = _meta.Clef;
        // And the score-level meter: the value a section without its own time reverts to.
        // Captured here (before the section walk mutates _meta.Time via mid-music changes).
        _sectionResetTimeBeats = _meta.TimeBeats;
        _sectionResetTimeBeatType = _meta.TimeBeatType;
        _sectionResetTimeBeatsText = _meta.TimeBeatsText;
        _sectionResetTimeSenzaMisura = _meta.TimeSenzaMisura;
        // And the grob-override part default (global + this voice's part-body overrides,
        // already collected at (0,0)) — the state each section boundary reverts to.
        _sectionResetOverrides.Clear();
        foreach (var ov in _grobOverrides)
            if (ov.StaffIndex is null || ov.StaffIndex == _cursor.StaffIndex)
                _sectionResetOverrides[(ov.GrobType, ov.PropertyName)] = ov.Value;
        _sectionActiveGrobProps.Clear();

        // Arm the ambient tonic at the score's home key for this voice's walk
        // (phrase auto-transpose baseline).
        ResetAmbientTonicToHome();
        _phraseTransposeSaves.Clear();
        _phraseAnchorSaves.Clear();
        _phraseAbsoluteBaseSaves.Clear();

        var builder = new MeasureBuilder(TimeSignatureFraction);
        if (_filePartial is { } filePickup)
            builder.SetPartial(filePickup); // top-level partial N arms every voice
        _measureAccidentals.Clear();
        builder.MeasureCompleted = _measureAccidentals.Clear;

        _pendingInlineVoltas.Clear();

        // Checkpoint/resume probe (CollectWalkProbe). Walks are addressed by their
        // ordinal — the Nth CollectMeasures call of the collect — which is
        // deterministic for a given document, so a recording and a later resume of
        // the same document meet on the same walk.
        int walkOrdinal = _walkOrdinal++;
        _invocationInSection = 0;
        _sectionVisit = 0;
        _sectionStartMeasureForResume = 0;
        _resumeRestoredSectionStart = null;
        _probeRecording = null;
        _resumePending = null;
        _suffixPlan = null;
        _suffixTargets = null;
        _suffixSpliced = false;
        _walkMaxSourceRead = 0;
        _walkHeaderReads.Clear();
        _resolvedSpellingLog.Clear();
        _repetitionOriginalReads.Clear();
        _formRepeatDepth = 0;
        if (WalkProbe is { } probe)
        {
            if (probe.IsRecording)
            {
                var tables = CumulativeSideTables();
                var startCounts = new int[tables.Length];
                for (int t = 0; t < tables.Length; t++)
                    startCounts[t] = tables[t].Count;
                _probeRecording = new VoiceWalkRecording
                {
                    VoiceName = _voiceName,
                    StartTableCounts = startCounts,
                };
                probe.Recordings[walkOrdinal] = _probeRecording;
                // The part-level config reads that seeded this walk's entry state —
                // GetPartDefaults (clef/instrument/octave/transpose/header key) and
                // CollectPartBodyOverrides both consume direct children of the
                // part's declaration(s); part-major section cells are walked as
                // music and fold themselves into MaxSourceRead instead.
                if (_voiceName != null && _root != null)
                {
                    foreach (var partDecl in _root.ChildNodes().OfType<PartDeclarationSyntax>())
                    {
                        if (partDecl.Name.Text != _voiceName)
                            continue;
                        _walkHeaderReads.Add(partDecl.Name.Span);
                        foreach (var child in partDecl.ChildNodes())
                            if (child is not SectionDeclarationSyntax)
                                _walkHeaderReads.Add(child.FullSpan);
                    }
                }
            }
            else if (probe.ResumePlans.TryGetValue(walkOrdinal, out var resume))
            {
                // Cross-edit validity at walk entry: the plan must target the SAME
                // walk (ordinal drift after a part add/remove would pair it with
                // another voice), and every earlier walk of THIS collect must have
                // contributed exactly the counts the recording's watermarks bake in
                // (see VoiceWalkRecording.StartTableCounts). Δ=0 passes trivially.
                if (!string.Equals(resume.Recording.VoiceName, _voiceName, StringComparison.Ordinal))
                    throw new CollectResumeAbortException(
                        $"collect resume walk #{walkOrdinal} is now voice '{_voiceName}', recorded '{resume.Recording.VoiceName}'");
                var tables = CumulativeSideTables();
                var startCounts = resume.Recording.StartTableCounts;
                if (startCounts == null || startCounts.Length != tables.Length)
                    throw new CollectResumeAbortException("collect resume recording has no start-table counts");
                for (int t = 0; t < tables.Length; t++)
                    if (tables[t].Count != startCounts[t])
                        throw new CollectResumeAbortException(
                            $"collect resume: side table {t} has {tables[t].Count} entries at walk entry, recorded {startCounts[t]}");
                if (resume.Checkpoint != null)
                    _resumePending = resume;
                if (resume.SuffixCandidates is { } candidates)
                {
                    _suffixPlan = resume;
                    _suffixWindow = new CollectTailShifter.Window(
                        probe.WindowPrefix, probe.WindowSuffixStart, probe.WindowDelta);
                    // Keyed by shifted address; the planner already filtered out
                    // candidates whose node stands in the window. Indexer, not
                    // Add: two markers can share a start, and a lost pairing
                    // only costs reuse (the state comparison owns correctness).
                    _suffixTargets = new(candidates.Count);
                    foreach (var ck in candidates)
                        if (_suffixWindow.TryShift(ck.NodeStart, out int shifted))
                            _suffixTargets[(ck.SectionVisit, ck.Invocation, shifted)] = ck;
                }
            }
        }

        void ProcessNodes(List<GreenSite> nodeList)
        {
            // A spliced walk is done: everything after the adopted tail is state
            // the splice already restored (the end-of-walk checkpoint).
            if (_suffixSpliced)
                return;
            int invocation = _invocationInSection++;
            int startIndex = 0;
            if (_resumePending is { } plan)
            {
                var target = plan.Checkpoint!; // _resumePending is only armed with a prefix target
                // A checkpoint's address is the TRIPLE (section visit, invocation,
                // node) — what TryCaptureWalkCheckpoint stamps and what the suffix
                // side looks up. The prefix gate compares it in the same order.
                // ⚠️ The visit half is not redundant with ProcessSection's entry
                // gate. That gate only guards the walks that come THROUGH a
                // section; ProcessForm and ProcessRepeatBlock hand their own bars
                // (a `||` written in the form outside any repeat block, `|:`/`:|`
                // of a form-level repeat) straight to this walk, with no section
                // around them. Such a bar arrives holding the invocation counter
                // the LAST section reset to 0 on its way out of the prefix gate —
                // so it read as invocation 0 of the target section, and its single
                // node was compared against the target's address, which names an
                // unrelated node in an unrelated section: a deterministic
                // `address drifted` abort on `form main { A B || C }`.
                // ⚠️ MEASURED SCOPE (2026-08-22): the abort is reachable from the
                // Δ=0 substrate net only. Sweeping every pitch keystroke of both
                // books through CollectResumePlanner, the cross-edit planner never
                // picked a target past a form bar — deepest was visit 1 of `A B ||
                // C` — so the restore had already cleared _resumePending by the
                // time the bar arrived, and poisoned/fixed runs were identical
                // (0 bails either way). This closes the addressing hole; it is NOT
                // a measured latency win, and nothing here should be cited as one.
                int visit = _sectionVisit - 1;
                if (visit < target.SectionVisit)
                    return;
                if (visit > target.SectionVisit)
                    throw new CollectResumeAbortException(
                        $"collect resume overshot its target section visit ({visit} > {target.SectionVisit})");
                if (invocation < target.Invocation)
                    return;
                if (invocation > target.Invocation)
                    throw new CollectResumeAbortException(
                        $"collect resume overshot its target invocation ({invocation} > {target.Invocation})");
                // Cross-edit address revalidation: the prefix text is unchanged, so
                // an unchanged walk-order address holds a node with an unchanged
                // start. Anything else is structural drift — bail to a full collect.
                // (Site.SourceStart == Node.FullSpan.Start, no red materialized.)
                if (target.NodeIndex >= nodeList.Count
                    || nodeList[target.NodeIndex].Position != target.NodeStart)
                    throw new CollectResumeAbortException(
                        $"collect resume address drifted (node {target.NodeIndex} of invocation {invocation})");
                RestoreWalkCheckpoint(plan, builder);
                startIndex = target.NodeIndex;
            }
            for (int i = startIndex; i < nodeList.Count; i++)
            {
                var site = nodeList[i];

                // Record mode: an eligible measure boundary right before node i is a
                // resume point. Cheap when off (_probeRecording null in production).
                if (_probeRecording is { IneligibleReason: null } rec && builder.AtCleanBoundary)
                    TryCaptureWalkCheckpoint(rec, builder, invocation, i, site.Position);

                // Resume mode, suffix side: at a clean boundary whose shifted
                // walk-order address matches a recorded checkpoint, try to splice
                // the recorded tail in place of walking it. A declined attempt
                // (state mismatch, window position, unresolvable node) just keeps
                // the walk live — reuse lost, correctness untouched.
                if (_suffixTargets is { } targets && builder.AtCleanBoundary
                    && _formRepeatDepth == 0
                    && targets.TryGetValue(
                        (_sectionVisit - 1, invocation, site.Position), out var spliceTarget)
                    && TrySpliceSuffix(spliceTarget, builder))
                    return;

                // Phrase-reference boundary: evaluate the body in the default
                // frame (same handling as ProcessMusicNodeSequence). ENTER leaves the
                // measure boundary alone (a `|` at the body's head is a bar of its own —
                // see the remark there); EXIT re-arms it so an outer `|` confirms the close
                // the phrase's trailing `|` already made.
                // Kind None belongs to the synthetic markers alone (their reds are
                // preset — no gather kind is None), so real sites skip both type
                // tests on the kind read.
                if (site.Kind == SyntaxKind.None)
                {
                    if (site.Node is RelativeResetMarker reset)
                    {
                        EnterDefaultFrame(reset.OctaveOffset);
                        EnterPhraseTranspose(reset.AnchorStep, reset.OctaveOffset);
                        continue;
                    }

                    if (site.Node is PhraseEndMarker)
                    {
                        ExitPhraseTranspose();
                        builder.ResetMeasureBoundary(retargetableClose: true);
                        continue;
                    }
                }

                // Same skip as ProcessMusicNodeSequence: a note-attached mark in
                // the flat list must not shadow the tie/slur/beam markers behind it.
                var flags = PeekMarkers(nodeList, i, out var furthestPeeked);
                // The peek READS the following nodes' types, and a checkpoint can
                // sit between the peeking node and a peeked one (`c1 d1` — the
                // boundary is before d, but c's lookahead read d). Fold the furthest
                // peeked extent (spans are ordered, so it covers the whole run) so
                // an edit at any peeked node invalidates that boundary.
                // Nested walks need no such fold: no checkpoint is captured inside
                // them, and every peeked node is processed (and folded) before the
                // enclosing top-level node completes.
                if (_probeRecording != null && furthestPeeked != null)
                    _walkMaxSourceRead = Math.Max(_walkMaxSourceRead, furthestPeeked.FullSpan.End);
                ProcessMusicNode(site.Node, builder, flags);
            }
        }

        // Process based on structure or sections
        if (_form != null)
        {
            ProcessForm(ProcessNodes, builder);
        }
        else if (_sectionState.Sections.Count > 0)
        {
            // No `structure { }` — default to the order the sections were declared
            // (source order), so a single-section piece needs no structure at all.
            foreach (var section in _sectionState.Sections.Values.OrderBy(s => s.Name.Span.Start))
            {
                // Resume: the bookkeeping of a skipped/partially-resumed section is in
                // the adopted prefix (RecordSectionStart via the checkpoint's journal
                // replay, the label via the builder state) — re-running it here would
                // read a pre-restore builder. ProcessSection's own gate does the skip.
                // A SPLICED walk's remaining sections are in the adopted tail the same
                // way (the end checkpoint carries the section maps; the measure index
                // here would be the post-splice total, a wrong start).
                if (_resumePending == null && !_suffixSpliced)
                {
                    RecordSectionStart(section.SectionName, builder.CurrentMeasureIndex);
                    builder.SectionLabel = section.SectionName;
                    builder.SectionLabelPosition = section.Name.Span.Start;
                }
                ProcessSection(section, ProcessNodes, builder);
            }
        }
        else if (_root != null)
        {
            // This path never expanded variable references, so the collectable
            // filter also drops them (as the old spelling's type test did) —
            // IsCollectableMusicKind is the kind-level twin the equivalence net
            // pins to the type test.
            var musicNodes = new List<GreenSite>();
            foreach (var s in MusicSitesLazy(_root, includeParallel: true))
                if (IsCollectableMusicKind(s.Kind))
                    musicNodes.Add(s);
            ProcessNodes(musicNodes);
        }

        if (_resumePending != null)
            throw new CollectResumeAbortException(
                "collect resume never reached its target checkpoint (walk-order address mismatch)");
        if (_probeRecording is { } recording)
        {
            // The end-of-walk state a suffix splice jumps to. Only at a clean,
            // carry-free final boundary (a walk ending mid-measure or with a
            // pending carry keeps EndCheckpoint null and prefix-resumes only).
            // Captured BEFORE FinalizeInlineVoltas: its volta-bracket appends
            // happen after the checkpoint's watermarks on both sides, so a
            // spliced walk's own live finalize reproduces them.
            if (recording.IneligibleReason == null && builder.AtCleanBoundary
                && WalkCarriesNothing())
                recording.EndCheckpoint = BuildWalkCheckpoint(
                    builder, sectionVisit: -2, invocation: -1, nodeIndex: -1, nodeStart: -1);

            // Harvest what a resume adopts: the measures BEFORE FinalizeMeasures
            // mutates them, and the walk-local lists (cleared per walk, so the
            // collector's final state does not retain them for this walk).
            recording.PreFinalizeMeasures = builder.MeasuresSnapshot();
            recording.PendingInlineVoltas = new(_pendingInlineVoltas);
            recording.ParallelSpans = new(_parallelSpans);
            recording.HeaderReads = new(_walkHeaderReads);
            recording.ResolvedSpellings = new(_resolvedSpellingLog);
            recording.RepetitionOriginalReads = new(_repetitionOriginalReads);
            _probeRecording = null;
        }

        FinalizeInlineVoltas();

        return builder.FinalizeMeasures();
    }

    // --- checkpoint/resume probe internals (see CollectWalkProbe.cs) ---

    /// <summary>The append-only output lists a walk grows CUMULATIVELY across the
    /// whole collect (never cleared between walks), in a fixed order shared by
    /// capture (counts) and restore (prefix adoption). Excluded on purpose:
    /// <c>_pendingInlineVoltas</c>/<c>_parallelSpans</c> (cleared per walk —
    /// adopted from the recording's copies), <c>_measureAccidentals</c> (empty at
    /// every checkpoint by eligibility), <c>_courtesySourcePositions</c>/
    /// <c>_fingeringByPosition</c> (position-keyed; an item only ever reads its
    /// OWN position, so prefix entries have no reader in the resumed tail), and
    /// the collaborators that run strictly post-walk (lyrics, tab).</summary>
    internal IList[] CumulativeSideTables() => new IList[]
    {
        _dynamics, _articulations, _graceNotes, _musicMarks, _customTexts,
        _voltaBrackets, _tupletBrackets, _arpeggios, _figuredBasses,
        _percentRepeats, _crossStaffItems, _grobOverrides, _grobReverts,
        _trillSpannerEvents, _pitchTrace, _navPlacementWarnings,
        _tieTargetWarnings, _unpairedSlurWarnings, _unpairedBeamWarnings,
        _cueSpanBoundaryWarnings,
        _chordNameCollector.ItemsList,
        // ⚠️ _unpairedRepeatWarnings IS DELIBERATELY ABSENT, and the omission is the kind
        // this list exists to make impossible — so it is written down. Every other entry
        // ACCUMULATES during the walk, which is why a resumed walk has to adopt and shift
        // it. RepeatPairingScanner runs strictly AFTER the walk, over the finished
        // measures, and CLEARS its sink before filling it, so a resumed walk recomputes it
        // from the measures it ends up with. Nothing to adopt, nothing to shift. Adding it
        // here without a CollectTailShifter arm would throw on the `default:`.
    };

    /// <summary>The key timeline for Roman-numeral chord degrees: the initial key at
    /// bar 0 plus each mid-piece modulation, sorted ascending.</summary>
    private List<(int Measure, int TonicStep, int Sharps)> BuildKeyTimeline()
    {
        var list = new List<(int, int, int)> { (0, _meta.KeyTonicStep, _meta.InitialKeySharps) };
        foreach (var (m, key) in _keyByMeasure)
            if (m > 0)
                list.Add((m, key.TonicStep, key.Sharps));
        return list;
    }

    private void RecordSectionStart(string name, int startMeasure)
    {
        // Journaled BEFORE the dedup (a no-op re-record is still an event): the
        // checkpoint watermarks index this log, and replaying its prefix through
        // this very method reproduces the two maps below exactly.
        _sectionStartLog.Add((name, startMeasure));
        if (!_sectionState.StartMeasure.ContainsKey(name))
            _sectionState.StartMeasure[name] = startMeasure;
        if (!_sectionState.AllStarts.TryGetValue(name, out var list))
            _sectionState.AllStarts[name] = list = new List<int>();
        // ProcessForm runs once PER PART, so the same occurrence is recorded
        // several times on a multi-part score — a distinct start per occurrence, so
        // dedup by value keeps one entry each (and never duplicates the chords/lyrics).
        if (!list.Contains(startMeasure))
            list.Add(startMeasure);
    }

    /// <summary>The one write site of <c>_keyByMeasure</c> — the map write and its
    /// journal entry stay together so the checkpoint watermarks can stand in for a
    /// per-boundary copy of the map (see the journal fields' remarks).</summary>
    private void RecordKeyAtMeasure(int measure, int tonicStep, int sharps)
    {
        _keyByMeasure[measure] = (tonicStep, sharps);
        _keyByMeasureLog.Add((measure, tonicStep, sharps));
    }

    /// <summary>Rebuilds <c>_keyByMeasure</c> (and this collect's journal) as the
    /// materialization of <paramref name="source"/>'s journal prefix [0..count) —
    /// the checkpoint state the old per-boundary map copy used to carry.</summary>
    private void RestoreKeyLog(MeasureCollector source, int count)
    {
        _keyByMeasure.Clear();
        _keyByMeasureLog.Clear();
        for (int i = 0; i < count; i++)
        {
            var (m, step, sharps) = source._keyByMeasureLog[i];
            RecordKeyAtMeasure(m, step, sharps);
        }
    }

    /// <summary>Rebuilds the section-start maps (and this collect's journal) by
    /// replaying <paramref name="source"/>'s journal prefix [0..count) through
    /// <see cref="RecordSectionStart"/> — first-wins and dedup reproduced by the
    /// one spelling of the bookkeeping.</summary>
    private void RestoreSectionStartLog(MeasureCollector source, int count)
    {
        _sectionState.StartMeasure.Clear();
        _sectionState.AllStarts.Clear();
        _sectionStartLog.Clear();
        for (int i = 0; i < count; i++)
        {
            var (name, start) = source._sectionStartLog[i];
            RecordSectionStart(name, start);
        }
    }

    private void ProcessForm(Action<List<GreenSite>> processNodes, MeasureBuilder builder)
    {
        foreach (var child in _form!.DescendantNodes())
        {
            switch (child)
            {
                case SectionReferenceSyntax reference:
                    // Skip if inside a repeat block (will be handled by ProcessRepeatBlock)
                    if (IsInsideRepeatBlock(reference))
                        break;
                    if (_sectionState.Sections.TryGetValue(reference.SectionName, out var section))
                    {
                        // Resume: bookkeeping of a section at-or-before the target is in
                        // the adopted prefix; re-running it here would read a pre-restore
                        // builder. ProcessSection's own gate does the skip. A SPLICED
                        // walk's remaining sections live in the adopted tail the same way.
                        if (_resumePending == null && !_suffixSpliced)
                        {
                            RecordSectionStart(reference.SectionName, builder.CurrentMeasureIndex);
                            builder.SectionLabel = ResolveSectionLabel(reference);
                            builder.SectionLabelPosition = SectionDeclPos(reference.SectionName);
                        }
                        ProcessSection(section, processNodes, builder);
                    }
                    break;

                case FormRepeatBlockSyntax repeat:
                    ProcessRepeatBlock(repeat, processNodes, builder);
                    break;

                // A volta ending that NO repeat block opened — `form main { A [1. B] }`.
                // It is its section and nothing more: there is no repeat for the ending to
                // be an ending OF, so no bracket and no number are engraved.
                // LILYPOND-REF: lily/alternative-sequence-iterator.cc:83-84 — Alternative_sequence_iterator::analyze reads repeat-count, defaulting it to 1
                // when no enclosing repeat has set it, so every alternative plays exactly
                // once and nothing spans a second pass. Confirmed on 2.26.0 rather than
                // inferred: an `\alternative { \volta 1 { … } }` with no `\repeat volta` in
                // front of it renders BYTE-IDENTICALLY to writing the music plainly (and
                // says nothing), while the same book with the `\repeat` restored hashes
                // differently.
                // LilyPondExporter and MusicXmlExporter already read it this way — this arm
                // and MidiExporter.PlayForm are the two walks that were still dropping the
                // section on the floor, so the same file was two different pieces of music
                // depending on which output you asked for. Telling the author that the
                // number they wrote prints nothing is the OTHER half of this repair, and
                // lives in the validator (see FormDeclarationValidator).
                case FormAlternativeSyntax alt when !IsInsideRepeatBlock(alt)
                        && _sectionState.Sections.TryGetValue(alt.SectionName.Text, out var altSection):
                    // Resume: same skip as the plain reference arm above, for the same reason
                    // — this IS a plain reference as far as the page is concerned.
                    if (_resumePending == null && !_suffixSpliced)
                    {
                        RecordSectionStart(alt.SectionName.Text, builder.CurrentMeasureIndex);
                        // `[1. ~B]` hides the label exactly as `~B` does; otherwise the label
                        // rule is the plain reference's, which is what LilyPondExporter:959
                        // already writes for this shape (alt.DisplayLabel ?? the name).
                        builder.SectionLabel = alt.IsSilent
                            ? null : alt.DisplayLabel ?? alt.SectionName.Text;
                        builder.SectionLabelPosition = SectionDeclPos(alt.SectionName.Text);
                    }
                    ProcessSection(altSection, processNodes, builder);
                    break;

                // Navigation marks in the structure (segno / coda / fine / to coda /
                // D.C. / D.S. al fine|coda) — engraved like the inline @-marks, at the
                // boundary of the section just played.
                case NavigationMarkSyntax nav when !IsInsideRepeatBlock(nav):
                    // Resume: prefix marks are in the adopted _musicMarks, and the
                    // pre-restore builder would give this one a garbage measure index.
                    // Post-splice the mirror holds: tail marks are in the adopted
                    // slice, and the builder already stands at the walk's END.
                    if (_resumePending != null || _suffixSpliced)
                        break;
                    var navMark = NavigationToMusicMark(nav.MarkType);
                    // Target signs (segno/coda — where a jump lands) sit at the START
                    // of the next section; the jump-from text (fine / to coda / D.S. /
                    // D.C.) sits at the END of the section just played.
                    bool target = navMark is MusicMarkType.Segno or MusicMarkType.Coda;
                    int navMeasure = target
                        ? builder.CurrentMeasureIndex
                        : Math.Max(0, builder.CurrentMeasureIndex - 1);
                    // ProcessForm runs once PER PART; a structure-level mark
                    // must engrave once per SCORE — without this guard a grand
                    // staff printed "Fine" / "D.C. al Fine" twice, stacked.
                    if (!_musicMarks.Any(m => m.Type == navMark
                            && m.MeasureIndex == navMeasure
                            && m.SourcePosition == nav.SourceStart))
                        _musicMarks.Add(new MusicMarkItem(navMark, navMeasure, nav.SourceStart));
                    break;

                // _"text" — a free text directive between sections, engraved like
                // the jump-from navigation text at the END of the section just
                // played. The grammar has carried this form all along; the
                // collector never produced the item, so it parsed but silently
                // printed nothing.
                case CustomTextSyntax custom when !IsInsideRepeatBlock(custom):
                    // Resume: same reasoning as the navigation-mark arm above
                    // (both directions — prefix pending and post-splice).
                    if (_resumePending != null || _suffixSpliced)
                        break;
                    // Same per-part guard as the navigation marks above.
                    int textMeasure = Math.Max(0, builder.CurrentMeasureIndex - 1);
                    if (!_customTexts.Any(t => t.Text == custom.Text
                            && t.MeasureIndex == textMeasure
                            && t.SourcePosition == custom.SourceStart))
                        _customTexts.Add(new CustomTextItem(
                            custom.Text, textMeasure, custom.SourceStart));
                    break;

                // ~Name — render the section's music but show NO label (the dedicated
                // form for an unlabelled section, e.g. a Coda). Without this the whole
                // section was silently dropped.
                case { Kind: SyntaxKind.SilentSectionReference } silent
                        when !IsInsideRepeatBlock(silent)
                          && silent.GetChild(1) is SyntaxTokenNode nameTok
                          && _sectionState.Sections.TryGetValue(nameTok.Text, out var silentSection):
                    // Resume: same skip as the labelled reference arm above.
                    if (_resumePending == null)
                    {
                        RecordSectionStart(nameTok.Text, builder.CurrentMeasureIndex);
                        builder.SectionLabel = null;
                        builder.SectionLabelPosition = SectionDeclPos(nameTok.Text);
                    }
                    ProcessSection(silentSection, processNodes, builder);
                    break;

                // `break` / `nobreak` between sections force / forbid a system break
                // after the section just played (SetBreak/SetNoBreak flag the last
                // emitted measure). Runs once per part; each flags the same measure
                // index, so the score-wide break stays consistent.
                case BreakSyntax brk when !IsInsideRepeatBlock(brk):
                    // Resume: the flag is baked into the adopted prefix measures —
                    // and, post-splice, into the adopted tail measures.
                    if (_resumePending != null || _suffixSpliced)
                        break;
                    if (brk.IsNoBreak) builder.SetNoBreak();
                    else builder.SetBreak();
                    break;

                // A ':|' written in the form itself, outside any '|: … :|' block. It is
                // the same BarlineSyntax the music stream carries, and it goes into the
                // SAME flattened stream that ProcessRepeatBlock puts a block's own bars
                // into — so the form's repeat bars and a section's repeat bars are
                // siblings by the time anything reads them. The block's own bars are raw
                // tokens inside FormRepeatBlockSyntax, not BarlineSyntax, so this arm
                // cannot double-count them; the guard is for a nested form only.
                case BarlineSyntax formBar when !IsInsideRepeatBlock(formBar):
                    processNodes([new GreenSite(formBar)]);
                    break;
            }
        }
    }

    /// <summary>Source offset of a section's <c>section X</c> declaration (0 if the
    /// name is unknown), so its label mark can jump to the declaration. Anchored on
    /// the <c>section</c> keyword (not the name) so hovering anywhere on the
    /// declaration highlights the label in the preview. Sections are registered
    /// before structure expansion, so the lookup is populated here.</summary>
    private int SectionDeclPos(string sectionName)
        => _sectionState.Sections.TryGetValue(sectionName, out var s) ? s.SectionKeyword.Span.Start : 0;

    private static string? ResolveSectionLabel(SectionReferenceSyntax reference)
    {
        var label = reference.DisplayLabel ?? reference.SectionName;
        return label.Length == 0 ? null : label;
    }

    /// <summary>
    /// The name of the <c>part</c> a node lives inside, or null if it is not inside
    /// any part. Used to bind a part-major inner <c>section</c> to its part.
    /// </summary>
    private static string? EnclosingPartName(SyntaxNode node)
    {
        for (var p = node.Parent; p != null; p = p.Parent)
            if (p is PartDeclarationSyntax part)
                return part.Name.Text;
        return null;
    }

    /// <summary>True when <paramref name="node"/> sits inside a <c>chords</c> or
    /// <c>lyrics</c> block (a part-major track's inner section), so it is that track's
    /// cell rather than a structure section.</summary>
    /// <remarks>
    /// internal because the VALIDATORS need the same answer: a track cell's body is chord
    /// symbols or syllables, which own no duration, so anything that measures MUSIC has to
    /// skip it. <c>MeasureValidator.ValidateEmptyPlaceholders</c> did not, and reported
    /// every inner bar of a <c>chords prog { section A { Dmaj7 | Em7 | … } }</c> as an empty
    /// measure (LYS2001, user report session 240). One spelling, so the collector and the
    /// validator cannot disagree about what a section IS.
    /// </remarks>
    internal static bool IsInsidePartMajorTrack(SyntaxNode node)
    {
        for (var p = node.Parent; p != null; p = p.Parent)
            if (p is ChordPartBlockSyntax or LyricsBlockSyntax)
                return true;
        return false;
    }

    private static MusicMarkType NavigationToMusicMark(NavigationMarkType t) => t switch
    {
        NavigationMarkType.Segno => MusicMarkType.Segno,
        NavigationMarkType.Coda => MusicMarkType.Coda,
        NavigationMarkType.Fine => MusicMarkType.Fine,
        NavigationMarkType.ToCoda => MusicMarkType.ToCoda,
        NavigationMarkType.DaCapo => MusicMarkType.DaCapo,
        NavigationMarkType.DaCapoAlFine => MusicMarkType.DaCapoAlFine,
        NavigationMarkType.DaCapoAlCoda => MusicMarkType.DaCapoAlCoda,
        NavigationMarkType.DalSegno => MusicMarkType.DalSegno,
        NavigationMarkType.DalSegnoAlFine => MusicMarkType.DalSegnoAlFine,
        NavigationMarkType.DalSegnoAlCoda => MusicMarkType.DalSegnoAlCoda,
        _ => MusicMarkType.Segno
    };

    /// <summary>
    /// Checks if a node is inside music content (phrase/section/variable body).
    /// Used by CollectDefinitions to distinguish top-level declarations from mid-music changes.
    /// </summary>
    /// <summary>True when <paramref name="node"/> sits inside a <c>score { … }</c>
    /// block — a per-score setting rather than a file-level one.</summary>
    private static bool IsInsideRenderDeclaration(SyntaxNode node)
    {
        for (var p = node.Parent; p != null; p = p.Parent)
            if (p is RenderDeclarationSyntax)
                return true;
        return false;
    }

    private static bool IsInsideMusicContent(SyntaxNode node)
    {
        var parent = node.Parent;
        while (parent != null)
        {
            if (parent is PhraseDeclarationSyntax or SectionDeclarationSyntax
                or VariableDeclarationSyntax or PartBlockSyntax
                // A directive in a part header (`part p { key bes major … }`) is a
                // PER-PART default, applied when that part is collected (GetPartDefaults),
                // NOT a global one — otherwise it would overwrite the file-level key for
                // every part, including those that set none of their own.
                or PartDeclarationSyntax)
                return true;
            parent = parent.Parent;
        }
        return false;
    }

    /// <summary>
    /// Checks if a node is inside a TupletExpression (to avoid double-counting).
    /// Top-level TupletExpressionSyntax nodes pass through (processed by main loop).
    /// Nested TupletExpressionSyntax nodes are filtered (processed recursively by ProcessTuplet).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tuplet-bracket.cc - notes inside tuplets are processed together
    /// </remarks>
    private static bool IsInsideTuplet(SyntaxNode node) => node.IsInside<TupletExpressionSyntax>();

    /// <summary>
    /// Checks if a node is inside a <c>&lt;&lt; \\ &gt;&gt;</c> parallel expression. The primary
    /// walk uses this to SKIP a span's inner nodes (they are processed by the
    /// ParallelExpressionSyntax handler) while the span node itself passes
    /// through atomically.
    /// </summary>
    private static bool IsInsideParallel(SyntaxNode node) => node.IsInside<ParallelExpressionSyntax>();

    /// <summary>
    /// Checks if a node is inside an OnceModifierSyntax.
    /// Prevents double-processing of inner override/revert in once modifier.
    /// </summary>
    private static bool IsInsideOnce(SyntaxNode node) => node.IsInside<OnceModifierSyntax>();

    /// <summary>
    /// Checks if a node is inside a GraceExpressionSyntax.
    /// Prevents double-processing of notes inside grace expressions.
    /// </summary>
    private static bool IsInsideGrace(SyntaxNode node) => node.IsInside<GraceExpressionSyntax>();

    /// <summary>
    /// Checks if a node is inside a RepeatExpressionSyntax.
    /// Prevents double-processing of notes inside repeat expressions.
    /// </summary>
    private static bool IsInsideRepeat(SyntaxNode node) => node.IsInside<RepeatExpressionSyntax>();

    private static bool IsInsideInlineVolta(SyntaxNode node) => node.IsInside<InlineVoltaSyntax>();

    /// <summary>
    /// Single source of truth for "this node is a flat music node the collector
    /// consumes directly". Container expressions (tuplet/repeat/grace/inline-volta/
    /// parallel/once) appear here too but travel as ONE wrapper node — their inner
    /// content is skipped via <see cref="IsInsideProcessedContainer"/>. Keeping the
    /// membership test in one place stops the per-walk whitelists from drifting
    /// apart (which silently dropped overrides, drum notes, clefs, etc.).
    /// </summary>
    internal static bool IsCollectableMusicNode(SyntaxNode node) =>
        node is NoteSyntax or DrumNoteSyntax or RestSyntax or ChordSyntax
            or ChordRepetitionSyntax or SlashNoteSyntax or BareDurationSyntax or ArpeggioSyntax
            or BarlineSyntax or BreakSyntax or TieSyntax or SlurSyntax or BeamMarkerSyntax
            or GraceExpressionSyntax or CueExpressionSyntax
            or TupletExpressionSyntax or RepeatExpressionSyntax
            or ParallelExpressionSyntax or InlineVoltaSyntax or MusicMarkSyntax
            or NavigationMarkSyntax
            or OverrideDeclarationSyntax or RevertDeclarationSyntax or OnceModifierSyntax
            or ClefDeclarationSyntax or OctaveDirectiveSyntax or KeySignatureSyntax
            or TimeSignatureSyntax or TempoDeclarationSyntax or PartialDeclarationSyntax;

    /// <summary>
    /// Kind-level mirror of <see cref="IsCollectableMusicNode"/> for the lazy
    /// gather (<see cref="MusicSitesLazy"/>), where a type test would cost the
    /// red node the laziness exists to avoid. Exactly
    /// <see cref="IsMusicCandidateKind"/> minus the reference kind the gather
    /// call sites expand in place — kinds are 1:1 with red types (each Green
    /// class hard-codes its kind and <c>SyntaxNode.CreateRed</c> maps it back),
    /// so the two filters admit the same nodes. The net is
    /// MusicSitesEquivalenceTests' lazy fact, which asserts this equality on
    /// every site of every fixture book.
    /// </summary>
    internal static bool IsCollectableMusicKind(SyntaxKind kind)
        => kind != SyntaxKind.VariableReference && IsMusicCandidateKind(kind);

    /// <summary>
    /// True when a node lives inside a container expression that owns its own
    /// walk (tuplet/repeat/grace/inline-volta/parallel/once). Such nodes must be
    /// skipped by the outer walks so the wrapper is processed once, not flattened.
    /// ⚠️ No production caller since the gathers moved to <see cref="MusicSites"/>
    /// (which makes this skip structural) — kept internal as the REFERENCE
    /// SPELLING the equivalence net (MusicSitesEquivalenceTests) runs the old
    /// red walk with. Do not re-grow production callers.
    /// </summary>
    internal static bool IsInsideProcessedContainer(SyntaxNode node)
    {
        // ONE parent-chain walk for the whole family. Chaining the per-type IsInsideXxx
        // helpers re-walks the ancestor chain once per type (IsInside<T> starts over from
        // Parent each call), and these predicates run for every descendant the gather
        // loops visit — measured on grammar-tour's collect, the chained spelling read
        // ~+26% against one walk. The membership itself lives in IsProcessedContainer.
        for (var p = node.Parent; p != null; p = p.Parent)
            if (IsProcessedContainer(p, includeParallel: true))
                return true;
        return false;
    }

    /// <summary>
    /// <see cref="IsInsideProcessedContainer"/> minus the parallel test — the skip set for
    /// the per-voice flatten walks (<see cref="GatherVoiceMusicNodes"/> /
    /// <see cref="CollectMeasuresFromNode"/>), which deliberately let a <c>&lt;&lt; \\ &gt;&gt;</c>
    /// span's descendants through while every OTHER container still travels as one wrapper.
    /// Those two walks used to hand-roll this list, and both copies were missing the cue
    /// test: a whole-measure <c>cue { … }</c> in a sub-voice was walked twice — once as the
    /// region (cue-sized) and once flattened (full size) — so the duplicate rolled the
    /// measure and the piece gained a bar the exporter does not have.
    /// ⚠️ Like <see cref="IsInsideProcessedContainer"/>: no production caller
    /// since <see cref="MusicSites"/> — kept internal for the equivalence net.
    /// </summary>
    internal static bool IsInsideProcessedContainerExceptParallel(SyntaxNode node)
    {
        for (var p = node.Parent; p != null; p = p.Parent)
            if (IsProcessedContainer(p, includeParallel: false))
                return true;
        return false;
    }

    /// <summary>
    /// The ONE membership list behind both walks above — a container expression that owns
    /// its own walk, so its descendants must not be flattened by an outer walk.
    /// </summary>
    internal static bool IsProcessedContainer(SyntaxNode p, bool includeParallel) =>
        p is TupletExpressionSyntax or RepeatExpressionSyntax or GraceExpressionSyntax
            or InlineVoltaSyntax or OnceModifierSyntax or ArpeggioSyntax
            // A cue REGION owns its body's walk (ProcessCueRegion), and the region is the
            // only thing that knows the notes are cue-sized. Letting the outer walk flatten
            // it drops the region silently: the notes still render, at FULL size, and the
            // only symptom is a font-size in the SVG.
            or CueExpressionSyntax
        || (includeParallel && p is ParallelExpressionSyntax);

    /// <summary>Kind-level mirror of <see cref="IsProcessedContainer"/>, for the
    /// green-tree gather (<see cref="MusicSites"/>) — a container's descendants
    /// are not walked into, which is exactly what the red walks' per-descendant
    /// ancestor guard used to skip. The two lists must track each other; kinds
    /// are 1:1 with the red types (each Green class hard-codes its kind, and
    /// <c>SyntaxNode.CreateRed</c> maps it back). The net is
    /// MusicSitesEquivalenceTests (walks every fixture book both ways) plus the
    /// cue double-walk regression the ancestor guard's own doc cites.</summary>
    private static bool IsProcessedContainerKind(SyntaxKind kind, bool includeParallel) => kind is
        SyntaxKind.TupletExpression or SyntaxKind.RepeatExpression or SyntaxKind.GraceExpression
            or SyntaxKind.InlineVolta or SyntaxKind.OnceModifier or SyntaxKind.Arpeggio
            or SyntaxKind.CueExpression
        || (includeParallel && kind is SyntaxKind.ParallelExpression);

    /// <summary>Kind-level mirror of <see cref="IsCollectableMusicNode"/> plus
    /// <see cref="SyntaxKind.VariableReference"/> (the gather call sites expand
    /// references in place), for <see cref="MusicSites"/>. Must track the type
    /// list; the callers keep their type tests as the authority, so an over-wide
    /// kind here yields a node the caller filters out (harmless), while a missing
    /// kind silently drops its nodes from the flat list — the net is
    /// MusicSitesEquivalenceTests plus the full snapshot suite.</summary>
    private static bool IsMusicCandidateKind(SyntaxKind kind) => kind is
        SyntaxKind.Note or SyntaxKind.DrumNote or SyntaxKind.Rest or SyntaxKind.Chord
            or SyntaxKind.ChordRepetition or SyntaxKind.SlashNote or SyntaxKind.BareDuration
            or SyntaxKind.Arpeggio
            or SyntaxKind.Barline or SyntaxKind.Break or SyntaxKind.Tie or SyntaxKind.Slur
            or SyntaxKind.BeamMarker
            or SyntaxKind.GraceExpression or SyntaxKind.CueExpression
            or SyntaxKind.TupletExpression or SyntaxKind.RepeatExpression
            or SyntaxKind.ParallelExpression or SyntaxKind.InlineVolta or SyntaxKind.MusicMark
            or SyntaxKind.NavigationMark
            or SyntaxKind.OverrideDeclaration or SyntaxKind.RevertDeclaration
            or SyntaxKind.OnceModifier
            or SyntaxKind.ClefDeclaration or SyntaxKind.OctaveDirective or SyntaxKind.KeySignature
            or SyntaxKind.TimeSignature or SyntaxKind.TempoDeclaration
            or SyntaxKind.PartialDeclaration
            or SyntaxKind.VariableReference;

    /// <summary>
    /// The music gather's node source: every candidate node (collectable kinds
    /// plus variable references) under <paramref name="container"/>, in the same
    /// pre-order the former <c>DescendantNodes() + IsInsideProcessedContainer*</c>
    /// spelling visited them — spelled as a green walk
    /// (<see cref="SyntaxNode.GreenSites"/>) that materializes a red node only
    /// per candidate, and turns the
    /// per-descendant ancestor guard into a structural decision: a processed
    /// container's subtree is simply not walked into.
    /// <paramref name="includeParallel"/> mirrors the guard's parameter — the
    /// per-voice walks (false) flatten a <c>&lt;&lt; \\ &gt;&gt;</c> span's
    /// descendants while every other container still travels as one wrapper.
    /// </summary>
    /// <remarks>
    /// WHY (session 152→153, red-creation counters in HANDOFF §1): after the
    /// defs walk went green, this gather was the keystroke path's last
    /// whole-tree RED walk — materializing every red wrapper (tokens included)
    /// and running an O(depth) Parent-chain guard per descendant, just to build
    /// the flat list. The green walk visits the same green node set in the same
    /// order and pays a red spine only per collected node.
    /// ⚠️ The old guard's parent-chain walk extends ABOVE the container: a
    /// container standing inside a processed container yielded NOTHING. The
    /// ancestor pre-check reproduces that boundary exactly.
    /// </remarks>
    internal static IEnumerable<SyntaxNode> MusicSites(SyntaxNode container, bool includeParallel)
    {
        for (var p = container.Parent; p != null; p = p.Parent)
            if (IsProcessedContainer(p, includeParallel))
                return [];
        return container.GreenSites(g => (
            IsMusicCandidateKind(g.Kind),
            !IsProcessedContainerKind(g.Kind, includeParallel)));
    }

    /// <summary>
    /// The production music gather: the same candidate set, order and container
    /// boundary as <see cref="MusicSites"/>, but each site is handed out as a
    /// red-free <see cref="GreenSite"/> (green + position + lazy spine). A red
    /// node is created only where a site is CONSUMED — ProcessMusicNode, the
    /// attached-mark peek, variable-reference expansion — so a keystroke walk
    /// that adopts a prefix and splices a tail materializes only the edit
    /// window's nodes (HANDOFF ▶ ⒭ ⑵′ latter half: the flat list's adopted
    /// reds were the keystroke path's last whole-book red creation).
    /// </summary>
    /// <remarks>
    /// ⚠️ <see cref="MusicSites"/> (the red-yielding spelling) has no
    /// production caller left — it stays internal as the equivalence net's
    /// second oracle (MusicSitesEquivalenceTests pins lazy sites ≡ MusicSites
    /// ≡ the old red walk, positions included). Do not re-grow production
    /// callers of the red spelling.
    /// </remarks>
    internal static IEnumerable<GreenSite> MusicSitesLazy(SyntaxNode container, bool includeParallel)
    {
        for (var p = container.Parent; p != null; p = p.Parent)
            if (IsProcessedContainer(p, includeParallel))
                return [];
        return container.GreenSitesLazy(g => (
            IsMusicCandidateKind(g.Kind),
            !IsProcessedContainerKind(g.Kind, includeParallel)));
    }

    /// <summary>
    /// Collects dynamic markings from note/chord modifiers.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: dynamic-engraver.cc:36-61 Dynamic_engraver::listen_dynamic
    /// </remarks>
    private void CollectDynamics(SyntaxNode node, int measureIndex, int itemIndex)
    {
        var articulations = ArticulationsOf(node);

        foreach (var articulation in articulations)
        {
            if (articulation is DynamicSyntax dynamicSyntax)
            {
                var level = dynamicSyntax.Level;
                if (level != DynamicLevel.None)
                {
                    _dynamics.Add(new DynamicItem(level, measureIndex, itemIndex, dynamicSyntax.SourceStart, _cursor.StaffIndex)
                    {
                        IsAbove = dynamicSyntax.ForcedAbove == true,
                        VoiceIndex = _cursor.VoiceIndex,
                    });
                }
                else
                {
                    // @cresc, @decresc, @dim — parsed as DynamicSyntax but Level=None.
                    // Collect as MusicMark for hairpin detection, WITH the host item's
                    // index: the wedge starts at the mark's own moment (LilyPond's \<
                    // is a post-event of its note), not at the measure head — and a
                    // dynamic at that same moment is the START text, never the
                    // terminator. Until 2026-08-07 the index was dropped, so a
                    // mid-measure "c\f\> ..." started its wedge at the measure's
                    // first column and ended it on its own f.
                    var markName = dynamicSyntax.DynamicToken.Text;
                    var markType = MusicMarkItem.ParseMarkName(markName);
                    if (markType != null)
                    {
                        _musicMarks.Add(new MusicMarkItem(markType.Value, measureIndex,
                            dynamicSyntax.SourceStart, itemIndex) { StaffIndex = _cursor.StaffIndex });
                    }
                }
            }
        }
    }

    /// <summary>
    /// Checks if a chord has an @arpeggio articulation.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/arpeggio.cc - arpeggio marking
    /// </remarks>
    private static bool HasArpeggioArticulation(SyntaxNode node)
    {
        var articulations = ArticulationsOf(node);

        foreach (var art in articulations)
        {
            if (art is ArticulationSyntax artSyntax &&
                artSyntax.NameToken.Text == "arpeggio")
                return true;
        }
        return false;
    }

    /// <summary>
    /// Checks if a note/chord has an explicit @courtesy annotation.
    /// </summary>
    private static bool HasCourtesyAnnotation(SyntaxNode node)
    {
        var articulations = ArticulationsOf(node);

        foreach (var art in articulations)
        {
            if (art is ArticulationSyntax artSyntax &&
                artSyntax.Type == ArticulationType.None &&
                artSyntax.NameToken.Text.Equals("courtesy", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Detects whether a note carries a <c>@laissezVibrer</c> articulation.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/laissez-vibrer-engraver.cc — l.v. tie attachment.</remarks>
    private static bool HasLaissezVibrerAnnotation(SyntaxNode node)
        => HasNamedArticulation(node, "laissezvibrer");

    /// <summary>
    /// The forced curve side (<c>.up</c>/<c>.down</c>) of a node's
    /// <c>@laissezVibrer</c> annotation, or null when absent or automatic.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/laissez-vibrer-engraver.cc:99-103 acknowledge_note_head
    /// — the l.v. event's direction is copied onto the LaissezVibrerTie.</remarks>
    private static bool? LaissezVibrerUpOf(SyntaxNode node)
    {
        foreach (var art in ArticulationsOf(node))
            if (art is ArticulationSyntax { Type: ArticulationType.None } a
                && a.NameToken.Text.Equals("laissezvibrer", StringComparison.OrdinalIgnoreCase))
                return a.ForcedAbove;
        return null;
    }

    /// <summary>
    /// Detects whether a note carries a <c>@repeatTie</c> articulation.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/repeat-tie-engraver.cc — repeat-tie attachment.</remarks>
    private static bool HasRepeatTieAnnotation(SyntaxNode node)
        => HasNamedArticulation(node, "repeattie");

    /// <summary>
    /// The forced curve side (<c>.up</c>/<c>.down</c>) of a node's
    /// <c>@repeatTie</c> annotation, or null when absent or automatic.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/laissez-vibrer-engraver.cc:99-103 acknowledge_note_head
    /// — the event's direction is copied onto the tie; Repeat_tie_engraver inherits the
    /// path (repeat-tie-engraver.cc:27-33).</remarks>
    private static bool? RepeatTieUpOf(SyntaxNode node)
    {
        foreach (var art in ArticulationsOf(node))
            if (art is ArticulationSyntax { Type: ArticulationType.None } a
                && a.NameToken.Text.Equals("repeattie", StringComparison.OrdinalIgnoreCase))
                return a.ForcedAbove;
        return null;
    }

    /// <summary>
    /// The source offset of the <c>@</c> that wrote a named annotation on this node, or
    /// <see cref="MusicItem.NoSourcePosition"/> when it is not there — the address the
    /// half-tie that annotation draws names.
    /// </summary>
    /// <remarks>
    /// ⚠️ The node's own <c>SourceStart</c> is the NOTE, which is what the head cites; the
    /// two are never equal, and a bow drawn by an annotation has to cite the annotation —
    /// the rule the reader set for the <c>~</c> of a tie and the <c>( )</c> of a slur.
    /// Only <c>@laissezVibrer</c> and <c>@repeatTie</c> ask: every other annotation draws
    /// ink of its own, which already carries its address through the articulation
    /// side-tables rather than through the note item.
    /// </remarks>
    private static int NamedArticulationSourceOf(SyntaxNode node, string name)
    {
        foreach (var art in ArticulationsOf(node))
            if (art is ArticulationSyntax { Type: ArticulationType.None } a
                && a.NameToken.Text.Equals(name, StringComparison.OrdinalIgnoreCase))
                return a.SourceStart;
        return MusicItem.NoSourcePosition;
    }

    /// <summary>The explicit tab string number from a <c>\N</c> annotation on a
    /// note/chord, or null for automatic string selection.</summary>
    private static int? ExtractStringNumber(SyntaxNode node)
    {
        var articulations = ArticulationsOf(node);
        foreach (var art in articulations)
            if (art is StringNumberAnnotationSyntax s)
                return s.StringNumber;
        return null;
    }

    /// <summary>Display accidental kind for a quarter-tone pitch (ih/eh/isih/eseh).
    /// LILYPOND-REF: quarter-tone note names; glyphs = accidentals.*.slash*.</summary>
    private static string? QuarterToneAccidental(PitchSyntax pitch, string? fallback)
        => (pitch.AccidentalOffset, pitch.QuarterOffset) switch
        {
            (0, 1) => "quarterSharp",
            (1, 1) => "threeQuarterSharp",
            (0, -1) => "quarterFlat",
            (-1, -1) => "threeQuarterFlat",
            _ => fallback,
        };

    /// <summary>Absolute MIDI number from a diatonic step (0=C..6=B), alteration and octave.</summary>
    private static int PitchToMidi(int step, int alter, int octave)
        => RelativeOctave.StepToMidi(((step % 7) + 7) % 7, alter, octave);

    /// <summary>The post-event articulations attached to a note or chord (empty for
    /// anything else). The single source for the former five-copy node switch.</summary>
    private static IEnumerable<SyntaxNode> ArticulationsOf(SyntaxNode node) => node switch
    {
        NoteSyntax note => note.Articulations,
        ChordSyntax chord => chord.Articulations,
        ChordRepetitionSyntax rep => rep.Articulations,
        SlashNoteSyntax slash => slash.Articulations,
        BareDurationSyntax bare => bare.Articulations,
        DrumNoteSyntax drum => drum.Articulations,
        ArpeggioSyntax arpeggio => arpeggio.Articulations,
        // A rest carries post-events too (r2\p) — this arm was missing, so
        // CollectDynamics saw an empty list for every rest and r@p dropped the
        // p silently while the fermata path (its own switch in Annotations.cs)
        // kept working. LILYPOND-REF: lily/parser.yy — post-events attach to
        // rests; regression dynamics-rest-positioning.ly is the pin.
        RestSyntax rest => rest.Articulations,
        _ => Enumerable.Empty<SyntaxNode>()
    };

    /// <summary>Notehead style from a <c>@notehead(style)</c> annotation on the
    /// note/chord, or Default. LILYPOND-REF: NoteHead style property.</summary>
    /// <remarks>
    /// The FIRST notehead annotation decides, whether or not it names a style Lily#
    /// draws — the string form returned Default from inside its match arm, so a second
    /// annotation behind an unrecognised one never got a turn, and that is kept.
    /// </remarks>
    private static NoteheadStyle ExtractNoteheadStyle(SyntaxNode node)
    {
        foreach (var art in ArticulationsOf(node))
        {
            if (art is MusicMarkSyntax mark
                && mark.Name.Equals("notehead", StringComparison.OrdinalIgnoreCase)
                && mark.HasArgumentList)
            {
                return Semantics.AnnotationValues.Notehead(mark) switch
                {
                    "x" or "cross" => NoteheadStyle.Cross,
                    "diamond" => NoteheadStyle.Diamond,
                    "triangle" => NoteheadStyle.Triangle,
                    "slash" => NoteheadStyle.Slash,
                    "xcircle" => NoteheadStyle.XCircle,
                    _ => NoteheadStyle.Default,
                };
            }
        }
        return NoteheadStyle.Default;
    }

    private static bool HasNamedArticulation(SyntaxNode node, string lowerName)
    {
        var articulations = ArticulationsOf(node);

        foreach (var art in articulations)
        {
            if (art is ArticulationSyntax artSyntax &&
                artSyntax.Type == ArticulationType.None &&
                artSyntax.NameToken.Text.Equals(lowerName, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Forced stem direction from a <c>@stemUp</c> / <c>@stemDown</c> annotation:
    /// <c>true</c> = up, <c>false</c> = down, <c>null</c> = automatic (from staff
    /// position). Feeds <see cref="NoteItem.ForcedStemUp"/> — the writer's WISH, which
    /// LilyPond keeps apart from the derived direction and which the beam then reads
    /// (see that property; a beam with a forced stem in it stops using the
    /// farthest-head rule and the forced stem keeps its own side).
    /// </summary>
    private static bool? GetStemDirectionOverride(SyntaxNode node)
    {
        if (HasNamedArticulation(node, "stemup")) return true;
        if (HasNamedArticulation(node, "stemdown")) return false;
        return null;
    }

    /// <summary>
    /// Extracts a finger number from a single pitch's articulations (used for
    /// per-pitch fingerings inside chord brackets).
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/fingering-engraver.cc — finger event on chord pitch.</remarks>
    private static int? ExtractPitchFingering(PitchSyntax pitch)
    {
        foreach (var art in pitch.Articulations)
            if (art is MusicMarkSyntax markSyntax
                && Semantics.AnnotationValues.Finger(markSyntax) is { } finger)
                return finger;
        return null;
    }

    /// <summary>
    /// Extracts the finger number from a note's articulations, looking for
    /// <c>@finger.N</c> compound music marks.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/fingering-engraver.cc — finger event handling.
    /// Returns null when no fingering is attached.
    /// </remarks>
    private static int? ExtractFingering(SyntaxNode node)
    {
        foreach (var art in ArticulationsOf(node))
            if (art is MusicMarkSyntax markSyntax
                && Semantics.AnnotationValues.Finger(markSyntax) is { } finger)
                return finger;
        return null;
    }

    /// <summary>
    /// Checks if a note or chord has a @glissando (or the short alias @gliss)
    /// articulation.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/scheme-engravers.scm - Glissando_engraver::listen_glissando
    /// — LilyPond's user-facing command is the full word \glissando.
    /// </remarks>
    private static bool HasGlissandoArticulation(SyntaxNode node)
    {
        var articulations = ArticulationsOf(node);

        foreach (var art in articulations)
        {
            if (art is ArticulationSyntax artSyntax &&
                artSyntax.NameToken.Text is "glissando" or "slide")
                return true;
        }
        return false;
    }

    /// <summary>
    /// Processes a repeat expression (volta, unfold, percent, tremolo).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/percent-repeat-engraver.cc - percent repeat handling
    /// LILYPOND-REF: lily/percent-repeat-iterator.cc - type determination
    ///
    /// For percent repeats: body is unfolded N times, iterations 2+ marked with PercentRepeatItem.
    /// For volta/unfold: body is simply unfolded N times (basic implementation).
    /// </remarks>
    private void ProcessRepeatExpression(RepeatExpressionSyntax repeat, MeasureBuilder builder)
    {
        string type = repeat.RepeatType.Text;
        int count = int.TryParse(repeat.Count.Text, out int c) ? c : 2;

        // Expand phrase references in the body ONCE. The per-item dispatcher
        // ignores VariableReferenceSyntax, so `repeat unfold 8 { $ground }`
        // previously produced NOTHING, silently. ExpandVariable also inserts
        // the phrase-boundary reset marker; honour it like the main loop does
        // (each $call evaluates in the default frame). The body items are reds
        // already (a repeat body is always live), so the sites wrap them preset.
        var bodyNodes = new List<GreenSite>();
        foreach (var item in repeat.Body.Items)
        {
            if (item is VariableReferenceSyntax varRef)
                ExpandVariable(varRef.Name.Text, varRef.OctaveOffset, bodyNodes);
            else
                bodyNodes.Add(new GreenSite(item));
        }

        // Route through the shared sequence walker so ties/slurs/beams inside a
        // repeat body get the one-node marker lookahead (a manual per-item loop
        // here previously dropped them — e.g. `repeat volta 2 { c4( d e f) }`).
        void ProcessBodyOnce() => ProcessMusicNodeSequence(bodyNodes, builder);

        // Every pass beyond the first re-emits the whole body, and passes nest
        // (an unfold inside an unfold multiplies) — the expansion budget charges
        // each EXTRA pass by the body's size so `repeat unfold 2000000 { c4 }`
        // truncates instead of hanging the per-keystroke collect. The first pass
        // is the written music and stays free (a budget already spent by an
        // enclosing construct still plays the body once, like a plain block).
        int passCost = Math.Max(1, bodyNodes.Count);

        if (type == "percent")
        {
            // First iteration: process body normally
            int startMeasure = builder.CurrentMeasureIndex;
            // …and measure the body's LENGTH while doing it, because that — not the number
            // of measure objects it happened to produce — is what chooses the sign.
            // LILYPOND-REF: lily/percent-repeat-iterator.cc:75-92 next_element — the test is
            //   `body_length_.main_part_ == mlen` / `== mlen * 2`, against the CONTEXT's
            //   measure_length; the iterator never counts bars.
            // ⚠️ COUNTING BARS INSTEAD IS WRONG AND WAS MEASURED WRONG (2026-08-28): the
            // builder's completed-measure count depends on where the body's barlines fall,
            // so a one-measure body can leave the count at 1 or at 2 depending on whether
            // it ends on a `|`. The length does not care.
            var meterAtBody = builder.CurrentMeasureLength;
            var openAtStart = builder.CurrentDuration;
            // The running note value AS THE BODY OPENS, kept because the slash count is read
            // from the body's written durations and the first of them may inherit it
            // (`repeat percent 2 { c16 d e f }` writes the value once). Captured BEFORE the
            // first pass, which advances it.
            var defaultAtBody = _defaultDuration.Dotted(_defaultDots);
            ProcessBodyOnce();
            int bodyMeasureCount = builder.CurrentMeasureIndex - startMeasure;
            var bodyLength = builder.CurrentDuration - openAtStart;
            for (int m = 0; m < bodyMeasureCount; m++)
                bodyLength += meterAtBody;
            // A two-measure body takes the DOUBLE sign; one measure takes the single one;
            // EVERYTHING ELSE takes a repeat slash carrying the body's whole length.
            // ⚠️ THAT LAST BRANCH USED TO BE CUT IN THREE. Below one measure it emitted the
            // slash; at three or more whole measures it marked each repeated measure with a
            // percent and MeasureValidator warned (LYS2014) that the picture could not say
            // what the music was. LilyPond's iterator has no such cut — two equality tests
            // and one else — and what it engraves for a body of three or of eight whole
            // measures is ONE slash in the repetition's first measure with every later
            // measure blank (measured on 2.26.0: scratch/p282/wholebody3.ly, wholebody8.ly).
            // The per-measure percent was Lily#'s invention, the warning was the confession
            // of it, and both are gone.
            // LILYPOND-REF: lily/percent-repeat-iterator.cc:86-99 next_element.
            var bodyShape = PercentRepeatShape.Classify(bodyLength, meterAtBody);
            bool isDoubleBody = bodyShape == PercentBodyShape.Double;
            bool isBeatSlashBody = bodyShape == PercentBodyShape.RepeatSlash;
            // LilyPond decides the count ONCE, from the body's written durations, and the
            // count then chooses the grob as well as the number of slashes.
            // LILYPOND-REF: scm/music-functions.scm:378-390 calc-repeat-slash-count.
            int slashCount = isBeatSlashBody
                ? CalcRepeatSlashCount(bodyNodes, defaultAtBody)
                : 0;

            // Additional iterations: process body again but mark as percent repeat
            for (int iter = 1; iter < count; iter++)
            {
                if (!ChargeExpansion(passCost, repeat.SourceStart))
                    break;

                if (isBeatSlashBody)
                {
                    // LilyPond does not re-engrave the body here AT ALL: the iterator hands
                    // the context a RepeatSlashEvent in place of the music, so the repetition
                    // occupies its own duration with one grob and no notes. Lily# has to say
                    // that in the collector, because everywhere else its unfold KEEPS the
                    // repeated music and lets the visual passes hide it by measure — and a
                    // beat slash covers no measure, so there is no measure to hide.
                    // ⚠️ HIDING WOULD ALSO SPACE IT WRONG: the notes' springs would still be
                    // there, so `{ c16 d e f }` would leave four sixteenths' worth of room
                    // with two slashes floating in it. The spacer below carries the body's
                    // duration and nothing else, which is the spring LilyPond prices.
                    // ⚠️ The spacer's duration is written straight onto the RestItem rather
                    // than as a note value plus dots, because a body's length need not BE a
                    // note value (five sixteenths is not). RestItem.Duration is BaseDuration
                    // when Dots is 0, so an arbitrary fraction rides through unchanged.
                    // ⚠️ PLAYBACK IS UNAFFECTED: MidiExporter walks the SYNTAX tree
                    // (ProcessRepeat, MidiExporter.cs:1895) and never reads these items, and
                    // so do the MusicXML and .ly exporters. Only the engraved page changes.
                    // ⚠️ THE SPACER IS WRITTEN IN BAR-SIZED PIECES, because the builder
                    // auto-completes AT MOST ONE measure per item: a single item carrying
                    // three whole notes closes one bar and swallows the other two, which is
                    // what a three-measure body looked like on the first attempt — one slash
                    // and one measure where LilyPond draws one slash and three measures.
                    // LilyPond has no such item: the RepeatSlashEvent carries the body's
                    // whole length and the CONTEXT's bar machinery keeps closing measures
                    // underneath it, so the repetition occupies exactly as many bars as the
                    // body did. Splitting at the bar lines is how that reads here. The pieces
                    // are all spacers and only the FIRST carries the sign, so a body shorter
                    // than the room left in the open bar is one piece and unchanged.
                    int slashMeasure = builder.CurrentMeasureIndex;
                    var slashTiming = builder.CurrentDuration;
                    int slashItemIndex = builder.CurrentItemCount;
                    var remaining = bodyLength;
                    while (remaining > Fraction.Zero)
                    {
                        // The room left in the bar that is open right now — the builder's
                        // CurrentDuration is always the position INSIDE it. Read the meter
                        // each turn so a body that outlives a mid-piece `time` still lands on
                        // the bar lines the reader sees.
                        var bar = builder.CurrentMeasureLength;
                        var room = bar - builder.CurrentDuration;
                        if (room <= Fraction.Zero)
                            room = bar;
                        var piece = remaining < room ? remaining : room;
                        builder.AddItem(
                            new RestItem(piece, 0, repeat.SourceStart) { IsSpacer = true });
                        remaining -= piece;
                    }
                    _percentRepeats.Add(new PercentRepeatItem(
                        slashMeasure,
                        repeat.SourceStart,
                        _cursor.StaffIndex,
                        BeatTiming: slashTiming,
                        BeatItemIndex: slashItemIndex,
                        SlashCount: slashCount));
                    continue;
                }

                int iterStart = builder.CurrentMeasureIndex;
                ProcessBodyOnce();

                // A TWO-MEASURE body gets ONE double-percent sign for the whole repetition,
                // on the bar line between its two measures — not a single sign in each.
                // LILYPOND-REF: lily/double-percent-repeat-engraver.cc:56-64 process_music —
                //   the item is made when now_mom() reaches start_mom_ = the event's moment
                //   plus one measure_length, i.e. at the SECOND measure's downbeat.
                // ⚠️ A body of THREE OR MORE WHOLE MEASURES still reaches the per-measure
                // percent below, and that is a DECIDED divergence rather than an unported
                // branch — see the isBeatSlashBody remark above for what LilyPond draws there
                // and why it is not worth copying.
                if (isDoubleBody)
                {
                    _percentRepeats.Add(new PercentRepeatItem(
                        iterStart + 1,
                        repeat.SourceStart,
                        _cursor.StaffIndex,
                        IsDouble: true));
                }
                else
                {
                    // Mark all measures in this iteration as percent repeats
                    for (int m = 0; m < bodyMeasureCount; m++)
                    {
                        _percentRepeats.Add(new PercentRepeatItem(
                            iterStart + m,
                            repeat.SourceStart,
                            _cursor.StaffIndex));
                    }
                }
            }
        }
        else if (type == "tremolo" && bodyNodes.Count == 2
            && bodyNodes.All(b => b.Node is NoteSyntax or ChordSyntax or ChordRepetitionSyntax
                or SlashNoteSyntax or BareDurationSyntax)
            && TremoloPairShape(count, bodyNodes[0].Node, bodyNodes[1].Node) is { } pairShape)
        {
            // Two-note (chord) tremolo: both notes are WRITTEN with the
            // pair's total duration, sound half of it each, and are joined
            // by the subdivision's beams between the stems.
            // LILYPOND-REF: lily/chord-tremolo-engraver.cc.
            _tremoloPairShape = pairShape;
            _tremoloPairFirst = true;
            ProcessBodyOnce();
            _tremoloPairShape = null;
        }
        else if (type == "tremolo" && bodyNodes.Count == 1
            && bodyNodes[0].Node is NoteSyntax or ChordSyntax or ChordRepetitionSyntax
                or SlashNoteSyntax or BareDurationSyntax
            && TremoloTotalIsPrintable(count, bodyNodes[0].Node))
        {
            // LILYPOND-REF: lily/chord-tremolo-engraver.cc +
            // lily/stem-tremolo.cc — `\repeat tremolo 8 { c32 }` engraves ONE
            // quarter note whose stem carries the 32nd's three slashes (the
            // same drawing as the c4:32 suffix); the repetition is aural.
            _tremoloRepeatCount = count;
            ProcessBodyOnce();
            _tremoloRepeatCount = 1;
        }
        else
        {
            // `unfold` (and a tremolo whose total will not print as one note): the body is
            // drawn count times.
            // ⚠️ EVERY COPY IS THE SAME MUSIC, so each pass re-opens the relative frame
            // where the repeat OPENED it rather than continuing from what the last copy
            // left. `repeat unfold N` means "play this N times" (decided 2026-08-17,
            // HANDOFF §3) and that is the reading LilyPond gives it too — \relative
            // resolves the chain once and copies the RESULT, so its copies are identical.
            // Without this `repeat unfold 4 { g''8 a }` printed four pairs a rising octave
            // apart (SVG notehead y = 28.7 / 21.7 / 14.7 / 7.7, measured), because `''`
            // counts from the nearest g and the nearest g was the previous copy's.
            // ⚠️ The duration default is part of the frame for the same reason: the second
            // copy of `{ c4 d }` is two quarters, not whatever the last note left behind.
            var frame = new OctaveSnapshot(_octave.CurrentOctave, _octave.LastPitchName);
            var (frameDuration, frameDots) = (_defaultDuration, _defaultDots);
            for (int i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    if (!ChargeExpansion(passCost, repeat.SourceStart))
                        break;
                    _octave.CurrentOctave = frame.Octave;
                    _octave.LastPitchName = frame.PitchName;
                    (_defaultDuration, _defaultDots) = (frameDuration, frameDots);
                }
                ProcessBodyOnce();
            }
        }
    }

    /// <summary>
    /// LilyPond's <c>slash-count</c> for a percent body shorter than a measure: the number of
    /// slashes to draw when every written duration in the body is the same, and 0 — meaning
    /// "mixed", which selects the dotted <c>DoubleRepeatSlash</c> instead — when they are not.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/music-functions.scm:378-390 calc-repeat-slash-count — the durations
    ///   of the body's note/rest/skip events, <c>max (- (ly:duration-log first-dur) 2) 1</c>
    ///   if they are all <c>equal?</c>, else 0.
    /// <para>
    /// ⚠️ THE COMPARISON IS ON THE RESULTING LENGTH, not on LilyPond's duration object, and
    /// the two part company in exactly one place: a body holding both <c>c4.</c> and a
    /// tuplet-scaled <c>c8*3</c> writes two DIFFERENT durations of the same length, which
    /// LilyPond calls mixed (count 0) and this calls equal. Lily# has no duration object to
    /// compare — the collector's running default is a length and a dot count, and the written
    /// value is recovered from the length below — so the fact is stated rather than hidden.
    /// A body of that shape is not in the corpus (0 of 899 books, 2026-08-29).
    /// </para>
    /// <para>
    /// Zero-length nodes are skipped rather than counted as a disagreement: the empty chord
    /// <c>&lt;&gt;</c> is a post-event carrier and occupies no time, so it is not one of the
    /// events LilyPond's <c>extract-named-music</c> collects either.
    /// </para>
    /// </remarks>
    private static int CalcRepeatSlashCount(List<GreenSite> bodyNodes, Fraction defaultDuration)
    {
        Fraction? first = null;
        var running = defaultDuration;
        foreach (var site in bodyNodes)
        {
            var d = MeasureDurations.ItemDuration(site.Node, ref running);
            if (d <= Fraction.Zero)
                continue;
            if (first is null)
                first = d;
            else if (d != first.Value)
                return 0;
        }
        return first is null ? 0 : System.Math.Max(WrittenDurationLog(first.Value) - 2, 1);
    }

    /// <summary>
    /// LilyPond's <c>ly:duration-log</c> recovered from a length: 2 for a quarter, 4 for a
    /// sixteenth, −1 for a breve. Dots do not change it, because a dotted note is longer than
    /// its base and shorter than the next value up — <c>8.</c> is 3/16, which lies in
    /// [1/8, 1/4), so the answer is the 8th's 3.
    /// </summary>
    private static int WrittenDurationLog(Fraction length)
    {
        double v = length.ToDouble();
        int log = 0;
        while (v < 1.0) { v *= 2.0; log++; }
        while (v >= 2.0) { v /= 2.0; log--; }
        return log;
    }

    /// <summary>The written duration value of a note or chord (0 when it declares none, or
    /// the node is neither) — the base a tremolo's total duration is computed from.</summary>
    private static int NoteOrChordDurationValue(SyntaxNode n) => n switch
    {
        NoteSyntax ns => ns.Duration?.Value ?? 0,
        ChordSyntax cs => cs.Duration?.Value ?? 0,
        ChordRepetitionSyntax rep => rep.Duration?.Value ?? 0,
        SlashNoteSyntax slash => slash.Duration?.Value ?? 0,
        BareDurationSyntax bare => bare.Duration.Value,
        _ => 0
    };

    /// <summary>True when count × body duration reduces to a plain or dotted
    /// printable note value (1 → base, 3 → dotted, 7 → double-dotted).</summary>
    private static bool TremoloTotalIsPrintable(int count, SyntaxNode body)
    {
        int value = NoteOrChordDurationValue(body);
        if (value < 8 || count < 2)
            return false;
        return CombineTremoloDuration(count, value) != null;
    }

    /// <summary>Shape of a two-note tremolo, or null when not printable:
    /// display duration = count × (both notes), equal written values required;
    /// beams = the subdivision's flag count (16th → 2). GapCount = how many of
    /// those beams are drawn short of the stems — the repeat-symbol gap —
    /// except for a half-note display, whose beams reach the stems (a half
    /// cannot appear in a regular beam, so there is nothing to disambiguate).
    /// LILYPOND-REF: lily/chord-tremolo-engraver.cc:117-140 acknowledge_stem —
    /// gap_count = min(flags, intlog2(repeat_count) + 1), set on the Beam
    /// unless Stem::duration_log == 1.</summary>
    private static (int Value, int Dots, int Beams, int GapCount)? TremoloPairShape(
        int count, SyntaxNode first, SyntaxNode second)
    {
        int v1 = NoteOrChordDurationValue(first), v2 = NoteOrChordDurationValue(second);
        if (v2 == 0)
            v2 = v1; // second note inherits the first's duration (c16 e)
        if (v1 < 8 || v1 != v2 || count < 1)
            return null;
        // total = count × 2 × (1/v1); reuse the single-note reducer.
        var total = CombineTremoloDuration(count * 2, v1);
        if (total == null)
            return null;
        int beams = (int)Math.Log2(v1) - 2;
        int gapCount = total.Value.Value == 2 ? 0
            : Math.Min(beams, (int)Math.Log2(count) + 1);
        return (total.Value.Value, total.Value.Dots, beams, gapCount);
    }

    /// <summary>Reduces count/value to (noteValue, dots) — 8×1/32 = (4, 0),
    /// 12×1/32 = (4, 1) — or null when the total is not a printable duration.</summary>
    private static (int Value, int Dots)? CombineTremoloDuration(int count, int value)
    {
        int p = count, q = value;
        while (p % 2 == 0 && q % 2 == 0) { p /= 2; q /= 2; }
        if (q < 1)
            return null;
        return p switch
        {
            1 => (q, 0),
            3 => q >= 2 ? (q / 2, 1) : null,
            7 => q >= 4 ? (q / 4, 2) : null,
            _ => null
        };
    }

    /// <summary>
    /// Collects grace notes from a grace expression.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: grace-engraver.cc:36-80 Grace_engraver class
    /// </remarks>
    private void CollectGraceNotes(GraceExpressionSyntax grace, int measureIndex, int mainNoteItemIndex)
    {
        var type = grace.IsAcciaccatura ? GraceNoteType.Acciaccatura
                 : grace.IsAppoggiatura ? GraceNoteType.Appoggiatura
                 : GraceNoteType.Grace;

        // Collect notes from the grace body
        var graceNoteInfos = new List<GraceNoteInfo>();

        // LILYPOND-REF: lily/grace-spacing-engraver.cc — grace notes carry their own durations.
        // LILYSHARP-OWN: an eighth when the source writes no duration. ⚠️ THIS IS NOT A
        // LILYPOND DEFAULT and the comment used to claim it was: LilyPond has no grace-specific
        // rule at all — a bare note takes the PREVIOUS written duration, which at the start of
        // a grace group is whatever the main stream last wrote, or 4 if nothing has.
        // ⚠️ Lily# used to answer this in THREE places and get three answers: here (1/8),
        // Midi/MidiExporter.ProcessGrace (1/32), and LilyPond/LilyPondExporter, which wrote the
        // grace out with no duration at all and so handed LilyPond a QUARTER — a silent twin
        // defect, since that .ly is valid and merely plays something else. Found 2026-08-01,
        // when two ledger books spelled `grace { c' d' }` were quanting one beam against a
        // twin's two. ⇒ THIS LINE IS NOW THE ONE ANSWER: the MIDI exporter reads the eighth
        // from the same rule and the twin writes it out explicitly (docs/HANDOFF.md §1).
        Fraction graceDefaultDuration = Fraction.Eighth;
        // The DOTS ride the default with it. An undurated grace takes the whole previous
        // duration, dots included, exactly as an undurated note in the main stream does
        // (MeasureCollector.CreateNoteItem's _defaultDots) — `grace { d'8. e' }` is two
        // dotted eighths, not a dotted one and a plain one.
        // LILYPOND-REF: lily/parser.yy:3510-3516 optional_notemode_duration — what an
        //   undurated note falls back to is `parser->default_duration_`, a whole Duration;
        //   :3518-3520 steno_duration builds it with `make_duration ($1, dots)`, so the
        //   dots are part of what carries forward, not a separate memory.
        int graceDefaultDots = 0;

        // ⚠️ ONE WALK, TWO READERS — the body is read through the same statement
        // GraceBodyValidator reports from (Semantics.GraceBodySupport), so a phrase
        // reference cannot start being engraved here while LYS4020 goes on calling it
        // dropped. The elements arrive already expanded: a reference is a CONTAINER, and
        // `tuplet { A }`, `cue { A }` and `repeat unfold 2 { A }` have all expanded one
        // since long before this did (scratch/p194/four-containers.lys is the book that
        // checks the four side by side).
        foreach (var (item, _) in Semantics.GraceBodySupport.BodyElements(
                     grace,
                     name => _variables.TryGetValue(name, out var body) ? body : null,
                     () => ChargeExpansion(1, grace.SourceStart)))
        {
            // A phrase body is evaluated in a FRESH frame, exactly as it is at every other
            // call site (MeasureCollector.MusicWalk's ProcessMusicNodeSequence) — a phrase's
            // pitches must not depend on what the grace happened to play before the
            // reference. ⚠️ ONLY THE OCTAVE HALF OF THE RESET IS TAKEN. EnterDefaultFrame
            // also clears the VOICE's running duration, and a grace body never reads that
            // one: an undurated grace falls back to graceDefaultDuration below. Clearing it
            // here would let `grace { A }` change the duration of the note AFTER the grace,
            // which `grace { d'16 }` does not do — a side effect on the host stream that the
            // equivalent inline grace has no way to produce.
            if (item is RelativeResetMarker reset)
            {
                _octave.ResetToInitial();
                _octave.CurrentOctave += reset.OctaveOffset;
                EnterPhraseTranspose(reset.AnchorStep, reset.OctaveOffset);
                // The grace's OWN duration memory does reset, because that one IS what the
                // body reads: `grace { c'16 A }` must give A's undurated first note the
                // group's default eighth, the same note `grace { A }` would give it.
                graceDefaultDuration = Fraction.Eighth;
                graceDefaultDots = 0;
                continue;
            }

            if (item is PhraseEndMarker)
            {
                // Hands the relative chain back at the phrase's ANCHOR (the chord rule), so
                // a grace note written after a reference reads the same frame it would read
                // after one in the main stream.
                ExitPhraseTranspose();
                continue;
            }

            // A TUPLET IS A CONTAINER, AND THE PAGE READS NOTHING OFF ITS RATIO. This arm is
            // deliberately a no-op rather than an absent case: what the ratio changes is the
            // SOUNDING length, and a grace note is drawn from its WRITTEN duration.
            // MEASURED on LilyPond 2.26.0 (session 301, scratch/p301/lp, data-pos masked):
            // `\grace { \tuplet 3/2 { d'16 e' f' } }` puts its three noteheads, stems, beams
            // and accidentals at coordinates BYTE-IDENTICAL to `\grace { d'16 e' f' }`, and
            // the only ink it adds is the italic serif `3` (plus the four bracket lines when
            // the durations are long enough that no beam stands in for them). Those two grobs
            // are what a grace column still cannot hold, and GraceBodyValidator reports them
            // as a GraceDropKind.Bracket. ⚠️ The duration memory is NOT reset here, unlike at
            // a phrase boundary: a tuplet opens no frame in the main stream either, so
            // `grace { tuplet 3/2 { d'16 e' f' } c' }` gives the trailing c a sixteenth.
            if (item is GraceTupletStartMarker or GraceTupletEndMarker)
                continue;

            if (Semantics.GraceBodySupport.CarriedNote(item) is { } note)
            {
                var rp = CalculateStaffPosition(note.Pitch);
                _octave.CurrentOctave = rp.RelativeOctave;
                int staffPosition = rp.StaffPosition;

                bool needsLedger = staffPosition <= -6 || staffPosition >= 6;
                var accidental = GetDisplayAccidental(rp.DisplayStep, rp.DisplayAlteration, rp.DisplayOctave);

                // Resolve grace note duration (inherit previous grace duration if not specified)
                int noteValue = note.Duration?.Value ?? (int)graceDefaultDuration.Denominator;
                var baseDuration = Fraction.FromNoteValue(noteValue);
                graceDefaultDuration = baseDuration;
                int dots = note.Duration?.DotCount ?? graceDefaultDots;
                graceDefaultDots = dots;

                int graceMidi = PitchToMidi(rp.DisplayStep, rp.DisplayAlteration, rp.DisplayOctave);
                // The '\N', read through the same statement the validator reads, so a grace
                // on a tab picks the string the writer asked for rather than the one the
                // resolver would have picked — and so the two can never disagree about
                // whether it is carried (GraceBodySupport.CarriedStringNumber).
                graceNoteInfos.Add(new GraceNoteInfo(staffPosition, accidental, needsLedger,
                    baseDuration, graceMidi,
                    Semantics.GraceBodySupport.CarriedStringNumber(note),
                    dots));

                CollectGraceColumnlessAnnotations(note, measureIndex);
            }
        }

        if (graceNoteInfos.Count > 0)
        {
            var infos = graceNoteInfos.ToImmutableArray();
            _graceNotes.Add(new GraceNoteItem(
                type,
                infos,
                measureIndex,
                mainNoteItemIndex,
                grace.SourceStart,
                _cursor.StaffIndex));
            // Hand the infos to the next main note/chord so it can reserve front space.
            _pendingLeadingGrace = infos;
        }
    }

    /// <summary>
    /// Builds the annotations on a grace note that ask for NO COLUMN of their own — the ones
    /// a grace note can carry although it is not a measure item. Today: the rehearsal mark.
    /// (The string number is the other one, and it is not built here because it is not a grob
    /// at all — it rides <see cref="GraceNoteInfo.StringNumber"/> into the fret resolver.)
    /// </summary>
    /// <remarks>
    /// ⚠️ THIS IS NOT "one annotation family got lucky". LilyPond consists Mark_engraver in
    /// the SCORE context (ly/engraver-init.ly:729 <c>\name Score</c>, :764), so a mark's
    /// grob never belonged to the note's Voice and a grace being a Voice of its own cannot
    /// reach it; Lily# says the same by building the mark with NO <c>itemIndex</c>
    /// (<see cref="CollectArticulations"/> — "a rehearsal mark belongs to the BAR").
    /// A grace note therefore has nothing a mark needs, which is exactly why this one works
    /// while <c>@staccato</c> and <c>@text</c> — which need the note's column, and a grace
    /// note has no <c>itemIndex</c> to give them — are still dropped and reported (LYS4020).
    /// MEASURED on LilyPond 2.26.0, scratch/p298/lpmark.svg: <c>\grace { d'8^\markup{x}
    /// \mark "P" }</c> prints both, and the P is the half this restores.
    /// <para>
    /// ⚠️ The position de-dupe is the one <see cref="CollectArticulations"/> explains and is
    /// doing the same real work here: one written part collected onto BOTH a staff and a tab
    /// walks its grace once per staff, and one written mark is one printed mark.
    /// </para>
    /// </remarks>
    private void CollectGraceColumnlessAnnotations(NoteSyntax note, int measureIndex)
    {
        foreach (var annotation in note.Articulations)
        {
            if (annotation is not MusicMarkSyntax mark
                || !Semantics.GraceBodySupport.NeedsNoColumn(annotation)
                || Semantics.AnnotationValues.Rehearsal(mark, out _) is not { } label)
                continue;
            if (!MusicMarkExistsAt(mark.SourceStart))
                _musicMarks.Add(new MusicMarkItem(
                    MusicMarkType.Rehearsal, label, measureIndex, mark.SourceStart));
        }
    }
}
