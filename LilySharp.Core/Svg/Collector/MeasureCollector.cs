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
using LilySharp.Core.Syntax;
using LilySharp.Core.Tablature;

namespace LilySharp.Core.Svg.Collector;

/// <summary>
/// Tracks measure boundary alignment status for incremental compilation.
/// </summary>
internal record MeasureBoundary(
    int SourcePosition,
    Fraction AccumulatedDuration,
    bool IsExplicit,  // true if there was an explicit barline
    bool IsAligned    // true if duration matches time signature
);

/// <summary>
/// Reports a lyrics line that has MORE syllables than the notes it binds to, so
/// the trailing syllables found no note and were silently dropped from the
/// engraving. <see cref="Span"/> points at the FIRST dropped syllable, and
/// <see cref="FirstSyllable"/>/<see cref="FirstBar"/> name it and its 1-based
/// bar within the lyric line, so the author lands on the exact word where the
/// miscount starts.
/// </summary>
public record LyricSyllableWarning(
    LilySharp.Core.Syntax.TextSpan Span,
    int UnplacedSyllables,
    string FirstSyllable,
    int FirstBar
);

/// <summary>
/// Represents a bar check warning when barline position doesn't match time signature.
/// </summary>
public record BarCheckWarning(
    int SourcePosition,
    Fraction ExpectedDuration,
    Fraction ActualDuration
);

/// <summary>
/// A tied pair whose two notes carry DIFFERENT explicit tab string numbers
/// (<c>\N</c>). A tie holds one string, so the held note can't change strings;
/// <see cref="SourcePosition"/> points at the destination note's <c>\N</c>.
/// </summary>
public record TabTieStringWarning(
    int SourcePosition,
    int PreviousString,
    int FollowingString
);

/// <summary>
/// A note whose sounding pitch is outside the tab's playable range — below the
/// lowest open string (silently CLAMPED to fret 0, i.e. shown as a wrong open
/// string) or above the 24th fret of every string. Almost always an octave slip.
/// <see cref="SourcePosition"/> points at the note.
/// </summary>
public record TabRangeWarning(
    int SourcePosition,
    bool BelowRange   // true = below the lowest string; false = above the top fret
);

/// <summary>
/// Helper class for building measures from syntax nodes.
/// Supports both explicit barlines and automatic measure detection based on time signature.
/// </summary>
internal sealed class MeasureBuilder
{
    private readonly List<Measure> _measures = new();
    private readonly List<MusicItem> _currentItems = new();
    private readonly List<MeasureBoundary> _boundaries = new();
    private readonly List<BarCheckWarning> _barCheckWarnings = new();

    private Fraction _timeSignature; // mutable: a mid-piece time change re-arms it
    private Fraction _currentDuration = Fraction.Zero;
    private Fraction _defaultDuration = Fraction.Quarter;

    // When a 'partial N' shortens the next measure to a pickup, the meter to
    // restore once that measure closes is parked here. LILYPOND-REF:
    // ly/music-functions-init.ly:1670-1678 — \partial sets measurePosition for one
    // measure; the normal measureLength resumes afterwards.
    private Fraction? _partialRestore;

    private BarlineType _pendingStartBarline = BarlineType.None;
    private BarlineType _pendingEndBarline = BarlineType.None;
    private bool _pendingBreak = false;
    private string? _sectionLabel;
    private int _sectionLabelPosition;
    private int _measureSourceStart;

    /// <summary>
    /// Fires when a measure is completed (auto-fill OR explicit barline), i.e.
    /// a new measure is about to begin. The collector uses this to reset its
    /// per-measure accidental state — LilyPond forgets accidentals at the
    /// barline. LILYPOND-REF: lily/accidental-engraver.cc — accidental state
    /// resets at measure boundaries.
    /// </summary>
    public Action? MeasureCompleted;

    public MeasureBuilder(Fraction timeSignature, int sourceStart = 0)
    {
        _timeSignature = timeSignature;
        _measureSourceStart = sourceStart;
    }

    public IReadOnlyList<MeasureBoundary> Boundaries => _boundaries;
    public IReadOnlyList<BarCheckWarning> BarCheckWarnings => _barCheckWarnings;

    /// <summary>Gets the current accumulated duration within the measure.</summary>
    public Fraction CurrentDuration => _currentDuration;

    /// <summary>Current measure index (completed measures count).</summary>
    public int CurrentMeasureIndex => _measures.Count;

    /// <summary>Current item count within the current measure.</summary>
    public int CurrentItemCount => _currentItems.Count;

    public string? SectionLabel
    {
        get => _sectionLabel;
        set => _sectionLabel = value;
    }

    /// <summary>Source offset of the pending section label's declaration, carried
    /// onto the next measure so its section mark can jump to <c>section X</c>.</summary>
    public int SectionLabelPosition
    {
        get => _sectionLabelPosition;
        set => _sectionLabelPosition = value;
    }

    /// <summary>Re-arms the auto-complete measure length without printing a grob
    /// (used when a leading meter change collapses into the initial time signature).</summary>
    public void SetMeasureLength(Fraction length) => _timeSignature = length;

    /// <summary>
    /// Declares the current (in-progress) measure a pickup of <paramref name="length"/>:
    /// it auto-completes after only that much music, then the real meter resumes.
    /// LILYPOND-REF: ly/music-functions-init.ly:1670-1678 — \partial adjusts the
    /// Timing measurePosition so the current measure ends <paramref name="length"/>
    /// past the point of use; normal measureLength applies thereafter.
    /// </summary>
    public void SetPartial(Fraction length)
    {
        // Remember the meter to restore once the pickup closes (don't stack a
        // second partial onto a still-pending one — the first wins until it ends).
        _partialRestore ??= _timeSignature;
        _timeSignature = length;
    }

    /// <summary>After a pickup measure closes, restore the meter \partial replaced.</summary>
    private void RestorePartialIfPending()
    {
        if (_partialRestore is Fraction restore)
        {
            _timeSignature = restore;
            _partialRestore = null;
        }
    }

    /// <summary>
    /// Adds a music item and automatically completes the measure if duration is reached.
    /// </summary>
    public void AddItem(MusicItem item)
    {
        // A mid-piece meter change re-arms the auto-complete length for the
        // measures that follow. It is a zero-duration grob (printed at the
        // change point), so it never advances timing or completes a measure.
        if (item is TimeSignatureChangeItem tsc)
        {
            _timeSignature = new Fraction(tsc.NewTime.Beats, tsc.NewTime.BeatType);
            _currentItems.Add(item);
            return;
        }

        _currentItems.Add(item);

        // Track duration
        var itemDuration = GetItemDuration(item);
        _currentDuration += itemDuration;

        // Auto-complete measure if we've reached or exceeded time signature
        if (_currentDuration >= _timeSignature)
        {
            AutoCompleteMeasure(item.SourcePosition + 1);
        }
    }

    /// <summary>
    /// Adds a music item without affecting duration tracking.
    /// Used for tuplet notes where duration is calculated separately.
    /// </summary>
    public void AddItemWithoutDuration(MusicItem item)
    {
        _currentItems.Add(item);

        // Update default duration (for subsequent notes)
        Fraction baseDuration = item switch
        {
            NoteItem note => note.BaseDuration,
            RestItem rest => rest.BaseDuration,
            ChordItem chord => chord.BaseDuration,
            _ => Fraction.Zero
        };
        if (baseDuration != Fraction.Zero)
            _defaultDuration = baseDuration;
    }

    /// <summary>
    /// Adds duration and triggers auto-completion if time signature is reached.
    /// Used after processing tuplet notes with scaled duration.
    /// </summary>
    public void AddDuration(Fraction duration, int sourcePosition)
    {
        _currentDuration += duration;

        if (_currentDuration >= _timeSignature)
        {
            AutoCompleteMeasure(sourcePosition);
        }
    }

    private Fraction GetItemDuration(MusicItem item)
    {
        // Duration already includes dots (BaseDuration.Dotted(Dots))
        Fraction duration = item switch
        {
            NoteItem note => note.Duration,
            RestItem rest => rest.Duration,
            ChordItem chord => chord.Duration,
            _ => Fraction.Zero
        };

        // Update default duration (use base duration without dots)
        Fraction baseDuration = item switch
        {
            NoteItem note => note.BaseDuration,
            RestItem rest => rest.BaseDuration,
            ChordItem chord => chord.BaseDuration,
            _ => Fraction.Zero
        };
        if (baseDuration != Fraction.Zero)
            _defaultDuration = baseDuration;

        return duration;
    }

    private void AutoCompleteMeasure(int sourceEnd)
    {
        // Check if duration aligns with time signature
        bool isAligned = _currentDuration == _timeSignature;

        if (_currentItems.Count > 0)
        {
            // Apply pending break if any
            bool hasBreak = _pendingBreak;
            _pendingBreak = false;

            _measures.Add(new Measure(
                _currentItems.ToImmutableArray(),
                _pendingStartBarline,
                _pendingEndBarline != BarlineType.None ? _pendingEndBarline : BarlineType.Single,
                _sectionLabel,
                _measureSourceStart,
                sourceEnd,
                hasBreakAfter: hasBreak,
                sectionLabelPosition: _sectionLabelPosition,
                isPickup: _partialRestore != null));

            // Record boundary
            _boundaries.Add(new MeasureBoundary(
                sourceEnd,
                _currentDuration,
                IsExplicit: false,
                IsAligned: isAligned));

            _currentItems.Clear();
            _sectionLabel = null;
            _sectionLabelPosition = 0;
            _pendingStartBarline = BarlineType.None;
            _pendingEndBarline = BarlineType.None;
            _measureSourceStart = sourceEnd;

            // Handle overflow: if we exceeded time signature, the excess carries over
            if (_currentDuration > _timeSignature)
            {
                // Note: For now we don't handle splitting notes across barlines
                // This would require more complex handling
            }
            _currentDuration = Fraction.Zero;
            RestorePartialIfPending();
            MeasureCompleted?.Invoke();
        }
    }

    public void SetBreak()
    {
        if (_currentItems.Count == 0 && _measures.Count > 0)
        {
            // At measure boundary - apply break to previous measure
            var last = _measures[^1];
            _measures[^1] = new Measure(
                last.Items,
                last.StartBarline,
                last.EndBarline,
                last.SectionLabel,
                last.SourceStart,
                last.SourceEnd,
                hasBreakAfter: true,
                sectionLabelPosition: last.SectionLabelPosition,
                isPickup: last.IsPickup);
        }
        else
        {
            // Mid-measure break - defer to next measure boundary
            _pendingBreak = true;
        }
    }

    /// <summary>
    /// Handles an explicit barline: the WRITTEN barline is the measure
    /// boundary. The current measure is closed HERE, whatever its duration;
    /// a duration mismatch is a warning (bar check), not a layout input.
    /// Full measures are unaffected: duration auto-completion has already
    /// closed them, so the barline arrives on an empty measure (no-op).
    /// </summary>
    /// <remarks>
    /// This is the agreed Lily# semantic (the reverse of LilyPond, where
    /// "|" is only an assertion and Timing draws the bars): see the measure
    /// validator, which checks written measures against the meter.
    /// </remarks>
    public void HandleBarline(BarlineType barType, int position)
    {
        // Bar check: verify current position is at a measure boundary
        bool isAligned = _currentDuration == Fraction.Zero || _currentDuration == _timeSignature;

        if (!isAligned)
        {
            // Emit warning: barline position doesn't match time signature
            _barCheckWarnings.Add(new BarCheckWarning(
                position,
                _timeSignature,
                _currentDuration));
        }

        if (barType == BarlineType.RepeatStart)
        {
            // |: opens the NEXT measure; close anything pending first.
            if (_currentItems.Count > 0)
                CompleteMeasure(position, BarlineType.Single);
            _pendingStartBarline = BarlineType.RepeatStart;
            return;
        }

        var endType = barType == BarlineType.None ? BarlineType.Single : barType;

        if (_currentItems.Count > 0)
        {
            CompleteMeasure(position, endType);
        }
        else if (endType != BarlineType.Single && _measures.Count > 0)
        {
            // Barline at an already-closed boundary (e.g. ":|" right after
            // auto-completion): retro-apply the type to the last measure.
            var lastMeasure = _measures[^1];
            _measures[^1] = new Measure(
                lastMeasure.Items,
                lastMeasure.StartBarline,
                endType,
                lastMeasure.SectionLabel,
                lastMeasure.SourceStart,
                lastMeasure.SourceEnd,
                hasBreakAfter: lastMeasure.HasBreakAfter,
                lineBreakPermission: lastMeasure.LineBreakPermission,
                breakPenalty: lastMeasure.BreakPenalty,
                pageBreakPermission: lastMeasure.PageBreakPermission,
                pageTurnPermission: lastMeasure.PageTurnPermission,
                sectionLabelPosition: lastMeasure.SectionLabelPosition,
                isPickup: lastMeasure.IsPickup);
        }
    }

    /// <summary>
    /// Closes the current measure at an EXPLICIT barline with the given end
    /// barline type.
    /// </summary>
    private void CompleteMeasure(int sourceEnd, BarlineType endType)
    {
        bool isAligned = _currentDuration == _timeSignature;
        bool hasBreak = _pendingBreak;
        _pendingBreak = false;

        _measures.Add(new Measure(
            _currentItems.ToImmutableArray(),
            _pendingStartBarline,
            _pendingEndBarline != BarlineType.None ? _pendingEndBarline : endType,
            _sectionLabel,
            _measureSourceStart,
            sourceEnd,
            hasBreakAfter: hasBreak,
            sectionLabelPosition: _sectionLabelPosition,
            isPickup: _partialRestore != null));

        _boundaries.Add(new MeasureBoundary(
            sourceEnd,
            _currentDuration,
            IsExplicit: true,
            IsAligned: isAligned));

        _currentItems.Clear();
        _sectionLabel = null;
        _sectionLabelPosition = 0;
        _pendingStartBarline = BarlineType.None;
        _pendingEndBarline = BarlineType.None;
        _measureSourceStart = sourceEnd;
        _currentDuration = Fraction.Zero;
        RestorePartialIfPending();
        MeasureCompleted?.Invoke();
    }


    public List<Measure> FinalizeMeasures(bool autoFinalBarline = true)
    {
        // Handle any remaining items as the final measure
        if (_currentItems.Count > 0)
        {
            bool isAligned = _currentDuration == _timeSignature;

            _measures.Add(new Measure(
                _currentItems.ToImmutableArray(),
                _pendingStartBarline,
                _pendingEndBarline != BarlineType.None ? _pendingEndBarline : BarlineType.Single,
                _sectionLabel,
                _measureSourceStart,
                _measureSourceStart,  // End position same as start for incomplete
                sectionLabelPosition: _sectionLabelPosition,
                isPickup: _partialRestore != null));

            _boundaries.Add(new MeasureBoundary(
                _measureSourceStart,
                _currentDuration,
                IsExplicit: false,
                IsAligned: isAligned));
        }

        // Auto-set final barline on the last measure (music convention). Skipped
        // for sub-streams that end mid-piece (a << \\ >> span's extra voice):
        // their last measure is not the piece's last, and the Final would win
        // the cross-voice barline merge.
        if (autoFinalBarline && _measures.Count > 0)
        {
            var last = _measures[^1];
            if (last.EndBarline == BarlineType.Single)
            {
                _measures[^1] = new Measure(
                    last.Items, last.StartBarline, BarlineType.Final,
                    last.SectionLabel, last.SourceStart, last.SourceEnd, last.HasBreakAfter,
                    sectionLabelPosition: last.SectionLabelPosition,
                    isPickup: last.IsPickup);
            }
        }

        return _measures;
    }
}

/// <summary>
/// Collects measures from a syntax tree.
/// </summary>
public sealed class MeasureCollector
{
    private readonly Dictionary<string, SectionDeclarationSyntax> _sections = new();
    // Part-major cells: `part X { section A { music } }` registers (A, X) -> the
    // inner section, whose body IS the music for that part. Lets a section's music
    // live inside the part instead of inside the section.
    private readonly Dictionary<(string section, string part), SectionDeclarationSyntax> _partMajorCells = new();
    private readonly Dictionary<string, SyntaxNode> _variables = new();
    // First expanded-measure index where each section begins, so a `lyrics` block
    // written inside a section aligns to THAT section's notes (not from bar 0).
    // First-occurrence wins; populated during structure/section expansion.
    private readonly Dictionary<string, int> _sectionStartMeasure = new();
    // Section labels for a rows-only score (filled by EnsureSectionStartsForRows).
    private readonly List<(int MeasureIndex, string Label, int Position)> _rowSectionLabels = new();
    private StructureDeclarationSyntax? _structure;
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

    // The relative-octave chain plus the part transpose that composes on top of
    // it, bundled into one named collaborator (see OctaveContext). The main walk
    // drives this in place on every note / chord / grace / tuplet.
    private readonly OctaveContext _octave = new();

    // Dynamic markings
    private readonly List<DynamicItem> _dynamics = new();
    // Global staff index currently being collected (multi-staff). Stamped onto
    // each dynamic so layout positions it under its own staff. 0 for the single-
    // staff/single-Score paths.
    private int _currentStaffIndex = 0;
    // Voice index (within the current staff) being collected. Stamped onto each
    // tuplet bracket so auto-beaming applies a tuplet's boundary only to its OWN
    // voice (a lower voice's eighths must not break at an upper voice's triplet).
    // 0 = primary voice; the parallel sub-voices set it in BuildExtraVoiceTracks.
    private int _currentVoiceIndex = 0;
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
    // Custom text annotations
    private readonly List<CustomTextItem> _customTexts = new();
    // Volta brackets (first/second ending)
    private readonly List<VoltaBracketItem> _voltaBrackets = new();
    // Inline volta endings collected during the current voice walk; finalized
    // (and marked closed/open) once the whole voice has been processed.
    private readonly List<(int startMeasure, int endMeasure, string voltaText, bool isClosed, int sourcePosition)> _pendingInlineVoltas = new();
    // Parallel-voice spans (<< \\ >>) recorded during the primary (voice-0)
    // walk: the parallel node and the measure index where its content begins.
    // Voice 0 flows into the primary stream so measure indices stay continuous;
    // the remaining voices are reconstructed afterwards (BuildMultiVoiceScore).
    // Cleared at the start of each collection.
    private readonly List<(ParallelExpressionSyntax Parallel, int StartMeasure)> _parallelSpans = new();
    // Added to the local measure index when collecting a parallel span's EXTRA
    // voices (they're collected with a fresh 0-based builder), so their
    // per-note metadata — dynamics, articulations, etc. — lands at the span's
    // real measure index instead of measure 0. Zero for the primary stream.
    private int _metadataMeasureOffset = 0;
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
    // Tablature post-pass (tie-string reconciliation + per-tuning string assignment),
    // extracted as a self-contained collaborator. Its warnings are surfaced below.
    private readonly TabResolver _tabResolver = new();
    /// <summary>Notes that fall outside the tab range (clamped). Populated by the
    /// tab-string resolution during multi-staff collection.</summary>
    public IReadOnlyList<TabRangeWarning> TabRangeWarnings => _tabResolver.RangeWarnings;
    /// <summary>Tied note pairs with conflicting explicit tab string numbers.
    /// Populated as a side effect of Collect.</summary>
    public IReadOnlyList<TabTieStringWarning> TabTieWarnings => _tabResolver.TieWarnings;
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
    private readonly List<(bool isStart, int measureIndex, int itemIndex, int sourcePosition, int staffIndex)> _trillSpannerEvents = new();
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
    // Pending grace notes to attach to the next main note
    private GraceExpressionSyntax? _pendingGrace = null;
    // Grace-note infos of the just-collected leading grace group, stamped onto the
    // main note/chord so the spacing can reserve space in front of its column.
    private ImmutableArray<GraceNoteInfo> _pendingLeadingGrace = ImmutableArray<GraceNoteInfo>.Empty;
    // Default duration
    private Fraction _defaultDuration = Fraction.Quarter;

    // Metadata
    private string? _title;
    private string? _composer;
    // Source offsets of the header grobs (0 = none), emitted as data-pos so the
    // preview can click-to-jump to the title/composer/time/key declarations.
    private int _titlePosition;
    private int _composerPosition;
    private int _timePosition;
    private int _keyPosition;
    private int? _tempo;
    // Active `repeat tremolo N { … }` transform: the body note prints ONCE at
    // the combined duration with the subdivision's stem slashes.
    private int _tremoloRepeatCount = 1;
    // Active two-note tremolo: (display value, display dots, between-beams);
    // both notes print at the pair's TOTAL duration and sound half (TimeScale ½).
    private (int Value, int Dots, int Beams)? _tremoloPairShape;
    private bool _tremoloPairFirst;
    private string? _tempoText;
    private int _tempoBeatUnit = 4;
    private int _tempoDots;
    private int _swingSubdivision;
    private int _timeBeats = 4;
    private string? _timeBeatsText; // additive numerator as written ("3+2")
    private bool _timeSenzaMisura;  // time none — unmeasured
    private string? _keyCustom;     // encoded custom key (KeySignature.EncodeCustom)
    private string? _initialKeyCustom;
    private Dictionary<string, DrumInfo>? _drumOverrides; // drummap { } per-score
    private int _timeBeatType = 4;
    private int _keySharps = 0;
    private int _initialKeySharps = 0; // Preserved for Score.KeySignature (not mutated by mid-measure key changes)
    private string _clef = "treble";
    private string _initialClef = "treble"; // Preserved for Score.Clef (not mutated by mid-measure clef changes)
    private int _clefPosition; // Source offset of the clef declaration (0 = none), for data-pos

    /// <summary>
    /// Gets the time signature as a Fraction.
    /// </summary>
    private Fraction TimeSignatureFraction => new(_timeBeats, _timeBeatType);

    /// <summary>
    /// Collects a Score from a syntax tree.
    /// </summary>
    public Score Collect(SyntaxTree tree, string? voiceName = null,
        StructureDeclarationSyntax? localStructure = null,
        string? attachedChordPart = null)
    {
        _voiceName = voiceName;
        Reset();

        // Phase 1: Collect definitions
        CollectDefinitions(tree.GetRoot());
        // A score-local `structure { ... }` overrides the top-level structure for
        // this render only.
        if (localStructure != null)
            _structure = localStructure;

        // Phase 1.5: If voiceName specified, look up clef and octave from part definition
        if (voiceName != null)
        {
            var (partClef, partOctave, partTranspose, partClefPos) = GetPartDefaults(tree.GetRoot(), voiceName);
            if (partClef != null)
                _clef = partClef;
            _clefPosition = partClefPos;
            _octave.CurrentOctave = partOctave ?? InstrumentDefaults.GetDefaultOctave(ParseClefType(_clef));
            _octave.OctaveBase = partOctave ?? 4;
            ApplyTranspose(partTranspose);
            // Transpose the written key signature (CollectDefinitions set it
            // before the part option was known) so the displayed key and the
            // accidental engine match the transposed pitches.
            _keySharps = _octave.TransposeKeySharps(_keySharps);
        }
        else
        {
            _octave.CurrentOctave = InstrumentDefaults.GetDefaultOctave(ParseClefType(_clef));
        }
        _octave.InitialOctave = _octave.CurrentOctave;
        _initialClef = _clef; // Preserve initial clef before music processing
        _initialKeySharps = _keySharps; // Preserve initial key before music processing
        _initialKeyCustom = _keyCustom;
        _octave.InitialOctaveAbsolute = _octave.OctaveAbsolute; // file-level octave mode default

        // Phase 2: Collect the primary (voice-0) stream. A << \\ >> span is
        // handled INLINE during this walk (its first voice flows into the
        // stream, the span is recorded in _parallelSpans), so sequential
        // measures and any number of parallel spans interleave correctly.
        _parallelSpans.Clear();
        var measures = CollectMeasures();
        ResolveBeamStemDirections(measures);

        // If any parallel span was seen, reconstruct the additional voices.
        if (_parallelSpans.Count > 0)
            return BuildMultiVoiceScore(measures, tree.GetRoot());

        // Single voice
        var voice = _tabResolver.ResolveVoiceTabTies(new Voice(_voiceName ?? "default", measures.ToImmutableArray()));

        // Ottava DISPLAY transposition: notes under an 8va draw an octave lower
        // (etc.) while sounding at their written pitch. Single-staff score, so
        // every ottava mark is on staff 0. See OttavaTransposer.
        voice = OttavaTransposer.Transpose(voice, DetectOttavaSpans(0));

        // Collect lyrics
        _lyricsCollector.CollectNoteBound(tree.GetRoot(), measures, _lyricsRowNames, _voiceMeasuresByName, _sectionStartMeasure);
        _chordNameCollector.CollectBlocks(tree.GetRoot(), _sectionStartMeasure, _currentStaffIndex);
        // `staff NAME with chords CHORDPART` on a single-staff score.
        if (attachedChordPart != null)
            _chordNameCollector.CollectAttached(
                tree.GetRoot(), attachedChordPart, _sectionStartMeasure, _currentStaffIndex);

        return new Score(
            voice,
            new TimeSignature(_timeBeats, _timeBeatType, _timeBeatsText, _timeSenzaMisura),
            new KeySignature(_initialKeySharps, _initialKeyCustom), // Use initial key, not the final state after key changes
            _initialClef, // Use initial clef, not the final state after clef changes
            _tempo,
            _title,
            _composer,
            _dynamics.ToImmutableArray(),
            _articulations.ToImmutableArray(),
            _graceNotes.ToImmutableArray(),
            swingSubdivision: _swingSubdivision,
            lyrics: _lyricsCollector.Lyrics.ToImmutableArray(),
            musicMarks: _musicMarks.ToImmutableArray(),
            customTexts: _customTexts.ToImmutableArray(),
            voltaBrackets: _voltaBrackets.ToImmutableArray(),
            tupletBrackets: _tupletBrackets.ToImmutableArray(),
            arpeggios: _arpeggios.ToImmutableArray(),
            figuredBasses: _figuredBasses.ToImmutableArray(),
            chordNames: _chordNameCollector.Items.ToImmutableArray(),
            percentRepeats: _percentRepeats.ToImmutableArray(),
            crossStaffItems: _crossStaffItems.ToImmutableArray(),
            grobOverrides: _grobOverrides.ToImmutableArray(),
            grobReverts: _grobReverts.ToImmutableArray(),
            trillSpanners: PairTrillSpannerEvents(),
            header: new HeaderPositions(_titlePosition, _composerPosition, _timePosition, _keyPosition, _clefPosition))
        {
            TempoText = _tempoText,
            TempoBeatUnit = _tempoBeatUnit,
            TempoDots = _tempoDots,
        };
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
                    hasBreakAfter: false,
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
    private static BarlineType Stronger(BarlineType a, BarlineType b)
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
    public MultiStaffScore CollectMultiStaff(SyntaxTree tree, RenderSpec renderSpec)
    {
        Reset();

        // Phase 1: Collect definitions
        CollectDefinitions(tree.GetRoot());
        // A score-local `structure { ... }` overrides the top-level structure for
        // this render only.
        if (renderSpec.LocalStructure != null)
            _structure = renderSpec.LocalStructure;
        _initialKeySharps = _keySharps; // Preserve initial key before music processing
        _initialKeyCustom = _keyCustom;
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
        // GetVoiceNames() yields names in the SAME order ToStaffGroups() builds
        // staves, so this counter equals the global staff index (see
        // EnumerateStaves) and tags each staff's dynamics correctly.
        int collectStaffIndex = 0;
        // Lyrics rows are collected AFTER the music, so the per-section bar count
        // (used to auto-wrap one block's verses) is known from the real content.
        var pendingLyricsRows = new List<(string Name, int StaffIndex)>();
        // `staff NAME with chords CHORDPART` attachments, applied post-loop.
        var attachedChords = new List<(string PartName, int StaffIndex)>();
        // Chord rows are also deferred (see the ChordRowSpec branch below).
        var pendingChordRows = new List<(string Name, int StaffIndex)>();
        foreach (var (voiceName, withChords) in renderSpec.GetVoiceBindings())
        {
            _voiceName = voiceName;
            _currentStaffIndex = collectStaffIndex++;
            _octave.LastPitchName = 'c';
            _defaultDuration = Fraction.Quarter;

            // `staff NAME with chords CHORDPART`: remember the attachment; the
            // chord symbols are collected AFTER the voice loop, once every
            // section's start measure is registered.
            if (withChords != null)
                attachedChords.Add((withChords, _currentStaffIndex));

            // An independent chord row (`chords name` in the score). Defer its
            // collection until AFTER the music voices: the section start table
            // fills while music is processed, and a row spec listed first (or a
            // rows-only score) would otherwise collect every section's block
            // from bar 0, overprinting them.
            if (renderSpec.Items.OfType<ChordRowSpec>().Any(c => c.PartName == voiceName))
            {
                pendingChordRows.Add((voiceName, _currentStaffIndex));
                staffVoices[voiceName] = ImmutableArray.Create(
                    new Voice(voiceName, ImmutableArray<Measure>.Empty));
                continue;
            }

            // An independent lyrics row (`lyrics name` in the score). Defer its
            // collection (placeholder voice for now) until the music bar count is
            // known, so one block of flat verses can auto-wrap to that bar count.
            if (renderSpec.Items.OfType<LyricsRowSpec>().Any(c => c.PartName == voiceName))
            {
                pendingLyricsRows.Add((voiceName, _currentStaffIndex));
                staffVoices[voiceName] = ImmutableArray.Create(
                    new Voice(voiceName, ImmutableArray<Measure>.Empty));
                continue;
            }

            // Set clef and octave for this voice from part definition
            var (partClef, partOctave, partTranspose, partClefPos) = GetPartDefaults(tree.GetRoot(), voiceName);
            _clef = partClef ?? "treble";
            _clefPosition = partClefPos;

            // Set initial octave: explicit > instrument default > clef default
            _octave.CurrentOctave = partOctave ?? InstrumentDefaults.GetDefaultOctave(ParseClefType(_clef));
            _octave.InitialOctave = _octave.CurrentOctave;
            _octave.OctaveBase = partOctave ?? 4;
            _octave.OctaveAbsolute = _octave.InitialOctaveAbsolute; // restore file-level octave mode
            ApplyTranspose(partTranspose);

            // Re-arm this voice's running key from the written initial key,
            // transposed by THIS part's option, so the accidental engine
            // suppresses in-key accidentals correctly and the key does not leak
            // between voices.
            _keySharps = _octave.TransposeKeySharps(_initialKeySharps);
            if (_octave.HasTranspose)
                voiceKeyDict[voiceName] = new KeySignature(_keySharps);

            staffVoices[voiceName] = CollectStaffVoices(voiceName);
        }

        // Rows collect AFTER the music. A rows-only score has no music to
        // register the section starts — derive them from the row blocks.
        if (pendingChordRows.Count > 0 || pendingLyricsRows.Count > 0)
            EnsureSectionStartsForRows();
        foreach (var (rowName, rowIdx) in pendingChordRows)
        {
            var rowMeasures = _chordNameCollector.CollectPart(
                tree.GetRoot(), rowName, rowIdx, _sectionStartMeasure, _timeBeats, _timeBeatType);
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
                var rowMeasures = _lyricsCollector.CollectRow(
                    tree.GetRoot(), name, idx, wrapBars, _sectionStartMeasure, _timeBeats, _timeBeatType);
                staffVoices[name] = ImmutableArray.Create(new Voice(name, rowMeasures));
            }
        }

        // A rows-only score prints its section labels from the FIRST row's
        // measures (that row is the PrimaryContentStaff fallback the mark
        // merge reads). No-op for mixed scores: the label list only fills
        // when no music registered the sections.
        if (_rowSectionLabels.Count > 0)
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
                foreach (var (idx, label, pos) in _rowSectionLabels)
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
        SynchronizeBarlines(flatVoices);
        foreach (var key in staffVoices.Keys.ToArray())
            staffVoices[key] = staffVoices[key]
                .Select(v => _tabResolver.ResolveVoiceTabTies(flatVoices[v.Name])).ToImmutableArray();

        // Lyrics align to the melody — the primary voice of the FIRST staff.
        // (Single-staff scores collect lyrics in Collect(); the grand-staff path
        // did not, so lyrics silently vanished on a multi-part score.)
        var firstVoiceName = renderSpec.GetVoiceNames().FirstOrDefault();
        if (firstVoiceName != null
            && staffVoices.TryGetValue(firstVoiceName, out var firstStaffVoices)
            && firstStaffVoices.Length > 0)
        {
            _lyricsCollector.CollectNoteBound(tree.GetRoot(), firstStaffVoices[0].Measures.ToList(), _lyricsRowNames, _voiceMeasuresByName, _sectionStartMeasure);
        }
        _chordNameCollector.CollectBlocks(tree.GetRoot(), _sectionStartMeasure, _currentStaffIndex);
        foreach (var (attachedPart, attachedStaff) in attachedChords)
            _chordNameCollector.CollectAttached(
                tree.GetRoot(), attachedPart, _sectionStartMeasure, attachedStaff);

        // Phase 3: Build staff groups from render spec
        var staffGroups = renderSpec.ToStaffGroups(name =>
            staffVoices.TryGetValue(name, out var v) ? v
                : ImmutableArray.Create(new Voice(name, ImmutableArray<Measure>.Empty)))
            .ToImmutableArray();

        // Attach per-staff key signatures to transposed parts (a staff is keyed
        // by its primary voice's name). Concert-pitch staves keep null and fall
        // back to the score key.
        if (voiceKeyDict.Count > 0)
            staffGroups = staffGroups
                .Select(sg => sg with
                {
                    Staves = sg.Staves
                        .Select(st => voiceKeyDict.TryGetValue(st.PrimaryVoice.Name, out var k)
                            ? st with { PerStaffKeySignature = k }
                            : st)
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
                            return st.IsTextRow && rowVerses.TryGetValue(idx, out var verses)
                                ? st with { TextRowVerses = verses, IsLyricsTextRow = true }
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
        staffGroups = staffGroups
            .Select(sg => sg with
            {
                Staves = sg.Staves
                    .Select(st => st.IsTab && st.Tuning.HasValue
                        ? st with { Voices = st.Voices.SetItem(0, _tabResolver.ResolveTabStrings(st.PrimaryVoice, st.Tuning.Value, st.TabSourceClef)) }
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

        return new MultiStaffScore(
            staffGroups,
            new TimeSignature(_timeBeats, _timeBeatType, _timeBeatsText, _timeSenzaMisura),
            new KeySignature(_initialKeySharps, _initialKeyCustom), // Use initial key, not the final state after key changes
            _tempo,
            _title,
            _composer,
            swingSubdivision: _swingSubdivision,
            lyrics: _lyricsCollector.Lyrics.ToImmutableArray(),
            musicMarks: _musicMarks.ToImmutableArray(),
            customTexts: _customTexts.ToImmutableArray(),
            voltaBrackets: _voltaBrackets.ToImmutableArray(),
            tupletBrackets: _tupletBrackets.ToImmutableArray(),
            dynamics: _dynamics.ToImmutableArray(),
            articulations: _articulations.ToImmutableArray(),
            graceNotes: _graceNotes.ToImmutableArray(),
            arpeggios: _arpeggios.ToImmutableArray(),
            figuredBasses: _figuredBasses.ToImmutableArray(),
            chordNames: _chordNameCollector.Items.ToImmutableArray(),
            percentRepeats: _percentRepeats.ToImmutableArray(),
            crossStaffItems: _crossStaffItems.ToImmutableArray(),
            trillSpanners: PairTrillSpannerEvents(),
            header: new HeaderPositions(_titlePosition, _composerPosition, _timePosition, _keyPosition, _clefPosition))
        {
            TempoText = _tempoText,
            TempoBeatUnit = _tempoBeatUnit,
            TempoDots = _tempoDots,
        };
    }

    /// <summary>
    /// Bakes beam-resolved stem directions into the collected items, IN
    /// PLACE. A beam forces one direction onto all members, and LilyPond
    /// resolves directions in the engravers BEFORE spacing — skyline rods and
    /// stem-direction corrections must see the same stems the renderer draws,
    /// or beamed runs space differently from LilyPond (this showed up as
    /// down-natural 8ths inside an up-beam getting ~8% extra room).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/beam.cc Beam::calc_direction — beam direction wins.
    /// LILYPOND-REF: lily/beam.cc:894-982 consider_auto_knees — per-member
    /// directions for kneed beams (BeamMember.MemberStemUp).
    /// </remarks>
    private void ResolveBeamStemDirections(List<Measure> measures)
    {
        if (measures.Count == 0)
            return;

        var voice = new Voice("beam-direction-probe", measures.ToImmutableArray());
        var groups = new BeamDetector().DetectBeamGroups(
            voice, new TimeSignature(_timeBeats, _timeBeatType, _timeBeatsText, _timeSenzaMisura), _tupletBrackets.ToImmutableArray());

        foreach (var group in groups)
        {
            foreach (var member in group.Members)
            {
                int mi = member.MeasureIndex >= 0 ? member.MeasureIndex : group.MeasureIndex;
                if (mi < 0 || mi >= measures.Count)
                    continue;
                var measure = measures[mi];
                if (member.ItemIndex < 0 || member.ItemIndex >= measure.Items.Length)
                    continue;

                MusicItem? updated = measure.Items[member.ItemIndex] switch
                {
                    NoteItem n => n with { StemUpOverride = member.MemberStemUp },
                    ChordItem c => c with { StemUpOverride = member.MemberStemUp },
                    _ => null,
                };
                if (updated == null)
                    continue;

                measures[mi] = new Measure(
                    measure.Items.SetItem(member.ItemIndex, updated),
                    measure.StartBarline, measure.EndBarline, measure.SectionLabel,
                    measure.SourceStart, measure.SourceEnd,
                    hasBreakAfter: false,
                    lineBreakPermission: measure.LineBreakPermission,
                    breakPenalty: measure.BreakPenalty,
                    pageBreakPermission: measure.PageBreakPermission,
                    pageTurnPermission: measure.PageTurnPermission,
                    sectionLabelPosition: measure.SectionLabelPosition,
                    isPickup: measure.IsPickup);
            }
        }
    }

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
    private Score BuildMultiVoiceScore(List<Measure> track0, SyntaxNode root)
    {
        var voices = new List<Voice>
        {
            new Voice("voice1", track0.ToImmutableArray())
        };
        var extras = BuildExtraVoiceTracks(track0);
        for (int i = 0; i < extras.Count; i++)
            voices.Add(new Voice($"voice{i + 2}", extras[i]));

        // Ottava DISPLAY transposition (single staff → staff 0). See OttavaTransposer.
        var multiVoiceOttava = DetectOttavaSpans(0);
        if (multiVoiceOttava.Count > 0)
            for (int i = 0; i < voices.Count; i++)
                voices[i] = OttavaTransposer.Transpose(voices[i], multiVoiceOttava);

        // Map named voices (voice sop { … }) to their measure track so a
        // `lyrics sop { … }` block can bind to it. Track 0 is voice 1, then extras.
        foreach (var (parallel, _) in _parallelSpans)
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

        // Unnamed lyrics align with the primary voice; named ones bind above.
        _lyricsCollector.CollectNoteBound(root, track0, _lyricsRowNames, _voiceMeasuresByName, _sectionStartMeasure);
        _chordNameCollector.CollectBlocks(root, _sectionStartMeasure, _currentStaffIndex);

        return new Score(
            voices.ToImmutableArray(),
            new TimeSignature(_timeBeats, _timeBeatType, _timeBeatsText, _timeSenzaMisura),
            new KeySignature(_initialKeySharps, _initialKeyCustom),
            _initialClef,
            _tempo,
            _title,
            _composer,
            _dynamics.ToImmutableArray(),
            _articulations.ToImmutableArray(),
            _graceNotes.ToImmutableArray(),
            swingSubdivision: _swingSubdivision,
            lyrics: _lyricsCollector.Lyrics.ToImmutableArray(),
            musicMarks: _musicMarks.ToImmutableArray(),
            customTexts: _customTexts.ToImmutableArray(),
            voltaBrackets: _voltaBrackets.ToImmutableArray(),
            tupletBrackets: _tupletBrackets.ToImmutableArray(),
            arpeggios: _arpeggios.ToImmutableArray(),
            figuredBasses: _figuredBasses.ToImmutableArray(),
            grobOverrides: _grobOverrides.ToImmutableArray(),
            grobReverts: _grobReverts.ToImmutableArray(),
            trillSpanners: PairTrillSpannerEvents(),
            header: new HeaderPositions(_titlePosition, _composerPosition, _timePosition, _keyPosition, _clefPosition))
        {
            TempoText = _tempoText,
            TempoBeatUnit = _tempoBeatUnit,
            TempoDots = _tempoDots,
        };
    }

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
        foreach (var (parallel, _) in _parallelSpans)
            voiceCount = Math.Max(voiceCount, parallel.Voices.Count());

        var tracks = new List<ImmutableArray<Measure>>();
        for (int t = 1; t < voiceCount; t++)
        {
            var trackMeasures = new Measure[totalMeasures];
            for (int m = 0; m < totalMeasures; m++)
                trackMeasures[m] = EmptyMeasure(track0[m]);

            foreach (var (parallel, start) in _parallelSpans)
            {
                var blocks = parallel.Voices.ToList();
                if (t >= blocks.Count)
                    continue;

                // Each sub-voice evaluates in a fresh relative frame anchored at
                // this voice's initial octave (the part's default), then maps onto
                // the span's measures.
                var savedOctave = _octave.Snapshot();
                var savedDuration = _defaultDuration;
                _octave.ResetToInitial();
                _defaultDuration = Fraction.Quarter;

                // Per-note metadata in this sub-voice is keyed by its local 0-based
                // measure index; shift it to the span's real start so dynamics etc.
                // land in the right measure.
                _metadataMeasureOffset = start;
                // Tag this sub-voice's tuplets with its voice index so their
                // beam-breaking boundaries never leak into a sibling voice.
                _currentVoiceIndex = t;
                // No auto-final barline: this is a SPAN inside the piece, and a
                // Final stamped on the span's last measure would win the
                // cross-voice barline merge and print a final barline mid-piece.
                var sub = CollectMeasuresFromNode(blocks[t], autoFinalBarline: false,
                    applyFilePartial: start == 0);
                ResolveBeamStemDirections(sub);
                _currentVoiceIndex = 0;
                _metadataMeasureOffset = 0;

                _octave.Restore(savedOctave);
                _defaultDuration = savedDuration;

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
        return voices.ToImmutable();
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
    private List<SyntaxNode> GatherVoiceMusicNodes(SyntaxNode voiceNode)
    {
        var musicNodes = new List<SyntaxNode>();
        foreach (var node in voiceNode.DescendantNodes())
        {
            if (IsInsideTuplet(node) || IsInsideRepeat(node) || IsInsideOnce(node)
                || IsInsideGrace(node) || IsInsideInlineVolta(node))
                continue;
            GatherMusicNode(node, musicNodes);
        }
        return musicNodes;
    }

    private List<Measure> CollectMeasuresFromNode(SyntaxNode voiceNode, bool autoFinalBarline = true,
        bool applyFilePartial = true)
    {
        var builder = new MeasureBuilder(TimeSignatureFraction, voiceNode.Position);
        // The file-level pickup arms a sub-collection only when it really sits
        // at the piece's start (a mid-piece voice{} span must not shorten its
        // own first bar).
        if (applyFilePartial && _filePartial is { } subPickup)
            builder.SetPartial(subPickup);
        _measureAccidentals.Clear();
        builder.MeasureCompleted = _measureAccidentals.Clear;

        _pendingInlineVoltas.Clear();

        // Collect all music nodes, expanding variable references
        var musicNodes = new List<SyntaxNode>();

        foreach (var node in voiceNode.DescendantNodes())
        {
            // Skip nodes that are inside a tuplet, repeat, grace, once, or inline
            // volta (they'll be processed by those handlers)
            if (IsInsideTuplet(node) || IsInsideRepeat(node) || IsInsideOnce(node)
                || IsInsideGrace(node) || IsInsideInlineVolta(node))
                continue;

            GatherMusicNode(node, musicNodes);
        }

        ProcessMusicNodeSequence(musicNodes, builder);

        FinalizeInlineVoltas();

        return builder.FinalizeMeasures(autoFinalBarline);
    }

    /// <summary>
    /// Adds a single descendant node to the flat music-node list, expanding
    /// variable references in place.
    /// </summary>
    private void GatherMusicNode(SyntaxNode node, List<SyntaxNode> musicNodes)
    {
        switch (node)
        {
            case NoteSyntax:
            case DrumNoteSyntax:
            case RestSyntax:
            case ChordSyntax:
            case BarlineSyntax:
            case BreakSyntax:
            case TieSyntax:
            case SlurSyntax:
            case BeamMarkerSyntax:
            case InlineVoltaSyntax:
            case GraceExpressionSyntax:
            case TupletExpressionSyntax:
            case RepeatExpressionSyntax:
            case MusicMarkSyntax:
            case OverrideDeclarationSyntax:
            case RevertDeclarationSyntax:
            case OnceModifierSyntax:
            case ClefDeclarationSyntax:
            case OctaveDirectiveSyntax:
            case KeySignatureSyntax:
            case TimeSignatureSyntax:
            case TempoDeclarationSyntax:
            case PartialDeclarationSyntax:
                musicNodes.Add(node);
                break;

            case VariableReferenceSyntax varRef:
                ExpandVariable(varRef.Name.Text, musicNodes);
                break;
        }
    }

    /// <summary>
    /// Processes a flat list of music nodes with one-node lookahead for
    /// ties/slurs/beams (which annotate the preceding note).
    /// </summary>
    private void ProcessMusicNodeSequence(List<SyntaxNode> musicNodes, MeasureBuilder builder)
    {
        for (int i = 0; i < musicNodes.Count; i++)
        {
            var node = musicNodes[i];

            // Phrase-reference boundary: evaluate the body in the default frame.
            if (node is RelativeResetMarker)
            {
                _octave.ResetToInitial();
                _defaultDuration = Fraction.Quarter;
                continue;
            }

            bool hasTieAfter = i + 1 < musicNodes.Count && musicNodes[i + 1] is TieSyntax;
            bool hasSlurStartAfter = i + 1 < musicNodes.Count && musicNodes[i + 1] is SlurSyntax slurS && slurS.IsOpen;
            bool hasSlurEndAfter = i + 1 < musicNodes.Count && musicNodes[i + 1] is SlurSyntax slurE && !slurE.IsOpen;
            bool hasBeamStartAfter = i + 1 < musicNodes.Count && musicNodes[i + 1] is BeamMarkerSyntax beamS && beamS.IsStart;
            bool hasBeamEndAfter = i + 1 < musicNodes.Count && musicNodes[i + 1] is BeamMarkerSyntax beamE && !beamE.IsStart;
            ProcessMusicNode(node, builder, hasTieAfter, hasSlurStartAfter, hasSlurEndAfter, hasBeamStartAfter, hasBeamEndAfter);
        }
    }

    /// <summary>
    /// Converts the inline volta endings collected during this voice walk into
    /// volta brackets. Each ending's right cap follows its source: a closing ']'
    /// draws the cap (closed), omitting it leaves the ending open. The engraver's
    /// segment splitter still opens a closed bracket only where a line break cuts
    /// it, so a closed ending never dangles a hook mid-system.
    /// </summary>
    private void FinalizeInlineVoltas()
    {
        foreach (var (startMeasure, endMeasure, voltaText, isClosed, sourcePosition) in _pendingInlineVoltas)
            _voltaBrackets.Add(new VoltaBracketItem(startMeasure, endMeasure, voltaText, isClosed, sourcePosition));
        _pendingInlineVoltas.Clear();
    }

    private void ProcessMusicNode(SyntaxNode node, MeasureBuilder builder, bool hasTieAfter = false, bool hasSlurStartAfter = false, bool hasSlurEndAfter = false, bool hasBeamStartAfter = false, bool hasBeamEndAfter = false)
    {
        switch (node)
        {
            case GraceExpressionSyntax grace:
                // Store grace expression to attach to the next note
                _pendingGrace = grace;
                break;

            case ParallelExpressionSyntax parallel:
                {
                    // << \\ >> span. Voice 0 joins the primary stream (this
                    // builder) so measure indices stay continuous; the extra
                    // voices are reconstructed later from the recorded span.
                    var voiceBlocks = parallel.Voices.ToList();
                    _parallelSpans.Add((parallel, builder.CurrentMeasureIndex));
                    if (voiceBlocks.Count > 0)
                        ProcessMusicNodeSequence(GatherVoiceMusicNodes(voiceBlocks[0]), builder);
                }
                break;

            case NoteSyntax note:
                {
                    int measureIndex = builder.CurrentMeasureIndex + _metadataMeasureOffset;
                    int itemIndex = builder.CurrentItemCount;
                    // Onset timing of this note (elapsed duration before it is added)
                    // — anchors note-attached marks to the right column.
                    Fraction noteAnchorTiming = builder.CurrentDuration;
                    // Process grace notes BEFORE the main note so they get correct octave context
                    if (_pendingGrace != null)
                    {
                        CollectGraceNotes(_pendingGrace, measureIndex, itemIndex);
                        _pendingGrace = null;
                    }
                    bool hasGliss = HasGlissandoArticulation(note);
                    int featherDir = GetFeatherDirection(note);
                    bool isCue = HasCueAnnotation(note);
                    // Pre-scan for @courtesy annotation before creating note
                    if (HasCourtesyAnnotation(note))
                        _courtesySourcePositions.Add(note.Position);
                    var noteItem = CreateNoteItem(note, hasTieAfter, hasSlurStartAfter, hasSlurEndAfter, hasBeamStartAfter, hasBeamEndAfter, hasGliss, featherDir, isCue);
                    if (ExtractNoteheadStyle(note) is var nhStyle && nhStyle != NoteheadStyle.Default)
                        noteItem = noteItem with { Notehead = nhStyle };
                    if (_tremoloPairShape is { } tpn)
                    {
                        // Halve the sounding time (display stays the total)
                        // and join the pair with the subdivision's beams.
                        noteItem = noteItem with
                        {
                            TimeScale = noteItem.TimeScale * new Fraction(1, 2),
                            TremoloPairBeams = tpn.Beams,
                            HasBeamStart = _tremoloPairFirst,
                            HasBeamEnd = !_tremoloPairFirst,
                        };
                        _tremoloPairFirst = false;
                    }
                    if (!_pendingLeadingGrace.IsDefaultOrEmpty)
                    {
                        noteItem = noteItem with { LeadingGrace = _pendingLeadingGrace };
                        _pendingLeadingGrace = ImmutableArray<GraceNoteInfo>.Empty;
                    }
                    builder.AddItem(noteItem);
                    CollectDynamics(note, measureIndex, itemIndex);
                    CollectArticulations(note, measureIndex, itemIndex, noteItem.StemUp,
                        noteItem.EditorialAccidental, noteAnchorTiming);
                    CollectFiguredBass(note, measureIndex, itemIndex);
                    CollectChordNames(note, measureIndex, itemIndex);
                    CollectCrossStaff(note, measureIndex, itemIndex);
                }
                break;

            case DrumNoteSyntax drumNote:
                {
                    int drumMeasureIndex = builder.CurrentMeasureIndex + _metadataMeasureOffset;
                    int drumItemIndex = builder.CurrentItemCount;
                    Fraction drumAnchorTiming = builder.CurrentDuration;
                    var drumItem = CreateDrumNoteItem(drumNote);
                    builder.AddItem(drumItem);
                    // The drums-style table marks the closed hi-hat "+" and
                    // the open hi-hat "○" automatically.
                    if (DrumOverrides.Resolve(_drumOverrides, drumNote.DrumName) is { Mark: not null } dInfoMark)
                        _articulations.Add(new ArticulationItem(
                            dInfoMark.Mark == "stopped"
                                ? ArticulationType.Stopped
                                : ArticulationType.Flageolet,
                            drumMeasureIndex, drumItemIndex, true,
                            drumNote.Position, _currentStaffIndex));
                    CollectDynamics(drumNote, drumMeasureIndex, drumItemIndex);
                    CollectArticulations(drumNote, drumMeasureIndex, drumItemIndex, drumItem.StemUp,
                        null, drumAnchorTiming);
                }
                break;

            case RestSyntax rest:
                {
                    var restItem = CreateRestItem(rest);
                    int restMeasureIndex = builder.CurrentMeasureIndex + _metadataMeasureOffset;
                    int restItemIndex = builder.CurrentItemCount;
                    Fraction restAnchorTiming = builder.CurrentDuration;
                    int count = rest.MeasureCount;
                    if (count <= 1)
                    {
                        builder.AddItem(restItem);
                        // Post-events on the rest (r4@fermata, r2@coda, ...).
                        // Rests have no stem; stemUp=false makes the default
                        // direction UP, matching scripts over rests.
                        CollectArticulations(rest, restMeasureIndex, restItemIndex, stemUp: false, anchorTiming: restAnchorTiming);
                    }
                    else
                    {
                        // LILYPOND-REF: lily/lily-parser.yy — R<dur>*N expands to N
                        // consecutive measure-rests semantically. The MeasureBuilder
                        // auto-completes each measure when its duration reaches the
                        // time signature.
                        for (int i = 0; i < count; i++)
                            builder.AddItem(restItem);
                    }
                }
                break;

            case ChordSyntax chord:
                {
                    int measureIndex = builder.CurrentMeasureIndex + _metadataMeasureOffset;
                    int itemIndex = builder.CurrentItemCount;
                    Fraction chordAnchorTiming = builder.CurrentDuration;
                    // Process grace notes BEFORE the main chord so they get correct octave context
                    if (_pendingGrace != null)
                    {
                        CollectGraceNotes(_pendingGrace, measureIndex, itemIndex);
                        _pendingGrace = null;
                    }
                    bool hasArpeggio = HasArpeggioArticulation(chord);
                    bool isCue = HasCueAnnotation(chord);
                    var chordItem = CreateChordItem(chord, hasBeamStartAfter, hasBeamEndAfter, hasArpeggio, isCue, hasTieAfter: hasTieAfter, hasSlurStartAfter: hasSlurStartAfter, hasSlurEndAfter: hasSlurEndAfter);
                    if (ExtractNoteheadStyle(chord) is var chStyle && chStyle != NoteheadStyle.Default)
                        chordItem = chordItem with { Notehead = chStyle };
                    if (!_pendingLeadingGrace.IsDefaultOrEmpty)
                    {
                        chordItem = chordItem with { LeadingGrace = _pendingLeadingGrace };
                        _pendingLeadingGrace = ImmutableArray<GraceNoteInfo>.Empty;
                    }
                    builder.AddItem(chordItem);
                    CollectDynamics(chord, measureIndex, itemIndex);
                    // Use chord stem direction for articulation placement
                    CollectArticulations(chord, measureIndex, itemIndex, chordItem.StemUp, anchorTiming: chordAnchorTiming);
                    CollectFiguredBass(chord, measureIndex, itemIndex);
                    CollectChordNames(chord, measureIndex, itemIndex);
                    CollectCrossStaff(chord, measureIndex, itemIndex);
                    // Collect arpeggio if present
                    // @arpeggio(bracket) = non-arpeggiate (do NOT roll).
                    bool arpBracket = chord.Articulations.Any(art =>
                        art is MusicMarkSyntax { } am
                        && am.MarkName.Equals("arpeggio.bracket", StringComparison.OrdinalIgnoreCase));
                    if ((hasArpeggio || arpBracket) && chordItem.Notes.Length > 0)
                    {
                        int minPos = chordItem.Notes.Min(n => n.StaffPosition);
                        int maxPos = chordItem.Notes.Max(n => n.StaffPosition);
                        _arpeggios.Add(new ArpeggioItem(measureIndex, itemIndex, minPos, maxPos, chord.Position, _currentStaffIndex,
                            Bracket: arpBracket));
                    }
                }
                break;

            case BarlineSyntax barline:
                var barType = ParseBarlineType(barline.BarToken.Text);
                builder.HandleBarline(barType, barline.Position);
                break;

            case InlineVoltaSyntax volta:
                {
                    // Render the ending's music in place (the body before |: … :| is
                    // written once; repeat barlines imply repetition) and overlay a
                    // volta bracket across the measures the ending occupies.
                    int startMeasureIndex = builder.CurrentMeasureIndex;

                    var innerNodes = new List<SyntaxNode>();
                    foreach (var item in volta.Items)
                        GatherMusicNode(item, innerNodes);
                    ProcessMusicNodeSequence(innerNodes, builder);

                    int endMeasureIndex = builder.CurrentMeasureIndex;
                    if (builder.CurrentItemCount > 0)
                        endMeasureIndex++; // include the in-progress measure
                    int lastMeasure = Math.Max(startMeasureIndex, endMeasureIndex - 1);
                    _pendingInlineVoltas.Add((startMeasureIndex, lastMeasure, volta.VoltaText, volta.IsClosed, volta.Position));
                }
                break;

            case BreakSyntax:
                // 'break' keyword triggers line break
                builder.SetBreak();
                break;

            case MusicMarkSyntax mark:
                {
                    // A note-attached compound mark (e.g. b@ped.off) is also surfaced
                    // here as a statement node; CollectArticulations already created it
                    // anchored to its host note. Skip this un-anchored duplicate so the
                    // release ("*") stays at its note rather than snapping to the bar.
                    if (_musicMarks.Any(m => m.SourcePosition == mark.Position))
                        break;
                    var markType = MusicMarkItem.ParseMarkName(mark.MarkName);
                    if (markType != null)
                    {
                        if (markType.Value == MusicMarkType.Rehearsal)
                        {
                            string text = MusicMarkItem.ParseRehearsalText(mark.MarkName);
                            _musicMarks.Add(new MusicMarkItem(MusicMarkType.Rehearsal, text, builder.CurrentMeasureIndex, mark.Position));
                        }
                        else
                        {
                            _musicMarks.Add(new MusicMarkItem(markType.Value, builder.CurrentMeasureIndex, mark.Position));
                        }
                    }
                }
                break;

            case ClefDeclarationSyntax clefDecl:
                {
                    // Mid-measure clef change
                    // LILYPOND-REF: lily/clef-engraver.cc — inspect_clef_properties()
                    string newClef = clefDecl.ClefName.Text.ToLowerInvariant();
                    _clef = newClef;
                    _octave.CurrentOctave = InstrumentDefaults.GetDefaultOctave(ParseClefType(_clef));
                    var clefChange = new ClefChangeItem(ParseClefType(newClef), clefDecl.Position);
                    builder.AddItem(clefChange);
                }
                break;

            case OctaveDirectiveSyntax octaveDir:
                // Mid-stream octave-mode switch: affects only how subsequent
                // pitches resolve '/, marks; emits no grob.
                _octave.OctaveAbsolute = octaveDir.IsAbsolute;
                break;

            case KeySignatureSyntax keySig:
                {
                    // Mid-measure key signature change
                    // LILYPOND-REF: lily/key-engraver.cc — process_music() creates KeySignature grob
                    var previousKey = new KeySignature(_keySharps, _keyCustom);
                    KeySignature newKey;
                    if (keySig.IsCustom)
                    {
                        // Mid-piece custom signature: alterations as written
                        // (transpose does not respell a custom map).
                        _keySharps = 0;
                        _keyCustom = KeySignature.EncodeCustom(keySig.CustomAlterations);
                        newKey = new KeySignature(0, _keyCustom);
                    }
                    else
                    {
                        int newSharps = _octave.TransposeKeySharps(CalculateKeySharps(keySig));
                        _keySharps = newSharps;
                        _keyCustom = null;
                        newKey = new KeySignature(newSharps);
                    }
                    var keyChange = new KeySignatureChangeItem(newKey, previousKey, keySig.Position);
                    builder.AddItem(keyChange);
                }
                break;

            case TimeSignatureSyntax timeSigChange:
                {
                    // LilyPond's Time_signature_engraver makes ONE TimeSignature
                    // grob per timestep, reflecting the CURRENT value, and the very
                    // first timestep compares against last_spec_ = null. So a
                    // \time before any note collapses INTO the initial signature
                    // (only the new value prints) — the default 4/4 never gets its
                    // own grob. A \time at the first moment of the piece therefore
                    // REPLACES the initial signature rather than printing a separate
                    // change grob on top of it ("C 3/4").
                    // LILYPOND-REF: lily/time-signature-engraver.cc:94-122
                    //   process_music — `if (time_signature_) return;` (one per
                    //   timestep) and the last_spec_ comparison.
                    if (builder.CurrentMeasureIndex == 0 && builder.CurrentDuration == Fraction.Zero)
                    {
                        _timeBeats = timeSigChange.Beats;
                        _timeBeatsText = timeSigChange.BeatsText;
                        _timeBeatType = timeSigChange.BeatType;
                        builder.SetMeasureLength(new Fraction(timeSigChange.Beats, timeSigChange.BeatType));
                    }
                    else
                    {
                        // Mid-piece change: a zero-duration grob printed at the
                        // change point, re-arming the following measures' length.
                        var newTime = new TimeSignature(timeSigChange.Beats, timeSigChange.BeatType, timeSigChange.BeatsText);
                        builder.AddItem(new TimeSignatureChangeItem(newTime, timeSigChange.Position));
                    }
                }
                break;

            case TempoDeclarationSyntax tempoChange:
                {
                    // Mid-piece tempo change: a metronome mark (♩= NNN) above the
                    // staff at this point (the initial tempo is drawn from
                    // Score.Tempo). LILYPOND-REF: scm/define-grobs.scm MetronomeMark.
                    // Anchor on the note that FOLLOWS the \tempo (its musical
                    // moment) so a mid-measure change prints above that note, as
                    // LilyPond does — not snapped to the measure start. The next
                    // item appended to this measure takes index CurrentItemCount.
                    // CurrentDuration is the time elapsed in this measure, used
                    // to resolve the column X on a grand staff (where the voice's
                    // item index would point into the wrong staff's note list).
                    if (tempoChange.Bpm is int bpm)
                        _musicMarks.Add(new MusicMarkItem(
                            MusicMarkType.Tempo, bpm.ToString(),
                            builder.CurrentMeasureIndex, tempoChange.Position,
                            builder.CurrentItemCount, builder.CurrentDuration)
                        {
                            // The mid-music path dropped everything but the
                            // number — "tempo Lively 4. = 80" rendered ♩=80.
                            TempoText = tempoChange.Marking,
                            TempoBeatUnit = tempoChange.BeatUnit ?? 4,
                            TempoDots = tempoChange.BeatDots,
                            SwingSubdivision = tempoChange.SwingSubdivision,
                        });
                    else if (tempoChange.Marking is { } markingOnly)
                        // Text-only change ("tempo Meno mosso"): bold marking,
                        // no metronome equation.
                        _musicMarks.Add(new MusicMarkItem(
                            MusicMarkType.Tempo, "",
                            builder.CurrentMeasureIndex, tempoChange.Position,
                            builder.CurrentItemCount, builder.CurrentDuration)
                        {
                            TempoText = markingOnly,
                        });
                }
                break;

            case PartialDeclarationSyntax partial:
                // Anacrusis: shorten the current measure to the declared pickup
                // length so it auto-completes early; the meter resumes after.
                // LILYPOND-REF: ly/music-functions-init.ly:1670-1678 \partial.
                builder.SetPartial(partial.ToFraction());
                break;

            case TieSyntax:
            case SlurSyntax:
            case BeamMarkerSyntax:
                // Already processed with the preceding note
                break;

            case TupletExpressionSyntax tuplet:
                // LILYPOND-REF: lily/tuplet-engraver.cc - process tuplet as a unit
                ProcessTuplet(tuplet, builder, nestingDepth: 0);
                break;

            case RepeatExpressionSyntax repeat:
                // LILYPOND-REF: lily/percent-repeat-engraver.cc - percent repeat handling
                ProcessRepeatExpression(repeat, builder);
                break;

            case OverrideDeclarationSyntax overrideDecl:
                CollectOverride(overrideDecl, builder.CurrentMeasureIndex, builder.CurrentItemCount, isOnce: false);
                break;

            case RevertDeclarationSyntax revertDecl:
                CollectRevert(revertDecl, builder.CurrentMeasureIndex, builder.CurrentItemCount);
                break;

            case OnceModifierSyntax onceModifier:
                if (onceModifier.Command is OverrideDeclarationSyntax innerOverride)
                    CollectOverride(innerOverride, builder.CurrentMeasureIndex, builder.CurrentItemCount, isOnce: true);
                else if (onceModifier.Command is RevertDeclarationSyntax innerRevert)
                    CollectRevert(innerRevert, builder.CurrentMeasureIndex, builder.CurrentItemCount);
                break;
        }
    }

    /// <summary>
    /// Processes a tuplet expression, collecting notes and creating a bracket item.
    /// Supports nested tuplets via recursive calls with increasing nesting depth.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tuplet-engraver.cc - Tuplet_engraver class
    /// LILYPOND-REF: lily/tuplet-bracket.cc:400-500 - nested bracket stacking
    ///
    /// For nested tuplets, duration scaling compounds:
    /// outer 3/2 containing inner 3/2 { e8 f g } →
    /// inner actual = 3/8 * 2/3 = 1/4, then outer scales again.
    /// Only the top-level tuplet (nestingDepth=0) adds duration to the measure.
    /// </remarks>
    /// <returns>The actual (scaled) duration of this tuplet.</returns>
    private Fraction ProcessTuplet(TupletExpressionSyntax tuplet, MeasureBuilder builder, int nestingDepth,
        Fraction? parentScale = null)
    {
        int measureIndex = builder.CurrentMeasureIndex;
        int startNoteIndex = builder.CurrentItemCount;

        // Cumulative time scale for items inside this tuplet. Items store
        // their ACTUAL duration (written × base/ratio, compounded through
        // nesting): BaseDuration carries the notation, Duration carries time.
        // Beat-based beaming and spacing need real time positions — a triplet
        // of written 8ths occupies ONE beat, so its beam group is the tuplet
        // itself, not "three 8ths plus whatever fills the half note".
        Fraction scale = (parentScale ?? new Fraction(1, 1))
            * new Fraction(tuplet.BaseDivision, tuplet.TupletRatio);

        // Track written duration of all items in the tuplet
        Fraction writtenDuration = Fraction.Zero;
        int lastSourcePosition = tuplet.Position;

        // Process all notes inside the tuplet body using Items property
        // (not DescendantNodes which includes all nested nodes)
        // Use AddItemWithoutDuration to avoid incorrect auto-completion
        foreach (var item in tuplet.Body.Items)
        {
            if (item is NoteSyntax note)
            {
                var noteItem = CreateNoteItem(note, false, false, false);
                writtenDuration += noteItem.Duration;
                builder.AddItemWithoutDuration(noteItem with { TimeScale = scale });
                lastSourcePosition = note.Position;
            }
            else if (item is RestSyntax rest)
            {
                var restItem = CreateRestItem(rest);
                writtenDuration += restItem.Duration;
                builder.AddItemWithoutDuration(restItem with { TimeScale = scale });
                lastSourcePosition = rest.Position;
            }
            else if (item is ChordSyntax chord)
            {
                var chordItem = CreateChordItem(chord);
                writtenDuration += chordItem.Duration;
                builder.AddItemWithoutDuration(chordItem with { TimeScale = scale });
                lastSourcePosition = chord.Position;
            }
            else if (item is TupletExpressionSyntax nestedTuplet)
            {
                // LILYPOND-REF: lily/tuplet-bracket.cc - nested tuplet processing
                // Recursively process nested tuplet; its actual duration
                // counts as "written" duration for this outer tuplet
                Fraction nestedActualDuration = ProcessTuplet(nestedTuplet, builder, nestingDepth + 1, scale);
                writtenDuration += nestedActualDuration;
                lastSourcePosition = nestedTuplet.Position;
            }
        }

        // Calculate actual duration: written × base / ratio
        // e.g., tuplet 3/2: 3 quarters (3/4) → actual 2/4
        // LILYPOND-REF: lily/tuplet-bracket.cc - tuplet duration scaling
        int ratio = tuplet.TupletRatio;   // e.g., 3 (play 3 notes)
        int @base = tuplet.BaseDivision;  // e.g., 2 (in time of 2)
        Fraction actualDuration = new Fraction(
            writtenDuration.Numerator * @base,
            writtenDuration.Denominator * ratio);

        // Record the bracket BEFORE adding the duration: AddDuration can
        // auto-complete (roll) the measure, after which CurrentItemCount is
        // reset and the indexes would be garbage — that dropped the second
        // nested tuplet's outer bracket and mis-indexed its inner one.
        int endNoteIndex = builder.CurrentItemCount - 1;

        // Only add bracket if we have at least 2 notes
        if (endNoteIndex >= startNoteIndex)
        {
            _tupletBrackets.Add(new TupletBracketItem(
                tuplet.TupletRatio,
                tuplet.BaseDivision,
                startNoteIndex,
                endNoteIndex,
                measureIndex,
                tuplet.Position,
                nestingDepth,
                _currentStaffIndex,
                _currentVoiceIndex
            ));
        }

        // Only add duration to the measure at the top level
        // Nested tuplets return their duration to the parent for compounding
        if (nestingDepth == 0)
        {
            builder.AddDuration(actualDuration, lastSourcePosition + 1);
        }

        return actualDuration;
    }

    private void Reset()
    {
        _sections.Clear();
        _variables.Clear();
        _dynamics.Clear();
        _currentStaffIndex = 0;
        _currentVoiceIndex = 0;
        _articulations.Clear();
        _graceNotes.Clear();
        _arpeggios.Clear();
        _figuredBasses.Clear();
        _chordNameCollector.Clear();
        _percentRepeats.Clear();
        _crossStaffItems.Clear();
        _grobOverrides.Clear();
        _grobReverts.Clear();
        _sectionStartMeasure.Clear();
        _rowSectionLabels.Clear();
        _voiceMeasuresByName.Clear();
        _trillSpannerEvents.Clear();
        _courtesySourcePositions.Clear();
        _measureAccidentals.Clear();
        _fingeringByPosition.Clear();
        _structure = null;
        _filePartial = null;
        _root = null;
        _octave.ResetAll();
        _defaultDuration = Fraction.Quarter;
        _title = null;
        _composer = null;
        _titlePosition = 0;
        _composerPosition = 0;
        _timePosition = 0;
        _keyPosition = 0;
        _clefPosition = 0;
        _tempo = null;
        _tempoText = null;
        _tempoBeatUnit = 4;
        _tempoDots = 0;
        _swingSubdivision = 0;
        _timeBeats = 4;
        _timeBeatsText = null;
        _timeSenzaMisura = false;
        _timeBeatType = 4;
        _keySharps = 0;
        _keyCustom = null;
        _initialKeyCustom = null;
        _initialKeySharps = 0;
        _clef = "treble";
        _initialClef = "treble";
    }

    /// <summary>
    /// Looks up clef and octave defaults from a part definition by name.
    /// Priority: explicit attributes > instrument defaults > clef-based defaults.
    /// </summary>
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
    {
        if (inner == null) return outer;
        if (outer == null) return inner;
        var i = inner.Value;
        var o = outer.Value;
        return PitchTransposer.Transpose(i.step, i.alt, i.oct, o.step, o.alt, o.oct);
    }


    private static (string? clef, int? octave, (int step, int alt, int oct)? transpose, int clefPos) GetPartDefaults(SyntaxNode root, string partName)
    {
        foreach (var partDecl in root.DescendantNodes().OfType<PartDeclarationSyntax>())
        {
            if (partDecl.Name.Text != partName)
                continue;

            string? clef = null;
            string? instrument = null;
            int? octave = null;
            int clefPos = 0;
            (int step, int alt, int oct)? transpose = null;

            // Check properties for clef, instrument, octave, and transpose
            foreach (var prop in partDecl.Properties)
            {
                var propName = prop.NameToken.Text.ToLowerInvariant();
                var valueToken = prop.GetChild(2) as SyntaxTokenNode;
                if (valueToken == null) continue;

                if (propName == "clef")
                {
                    clef = valueToken.Text.ToLowerInvariant();
                    clefPos = prop.NameToken.Span.Start;
                }
                else if (propName == "instrument")
                    instrument = valueToken.Text.ToLowerInvariant();
                else if (propName == "octave" && int.TryParse(valueToken.Text, out var oct))
                    octave = oct;
            }

            transpose = PartTranspose.Read(root, partName);

            // Resolve clef: explicit > instrument > null
            string? resolvedClef = clef;
            int? resolvedOctave = octave;

            if (instrument != null)
            {
                var (defaultClef, defaultOctave) = InstrumentDefaults.GetDefaults(instrument);
                resolvedClef ??= defaultClef switch
                {
                    ClefType.Treble => "treble",
                    ClefType.Bass => "bass",
                    ClefType.Alto => "alto",
                    ClefType.Tenor => "tenor",
                    ClefType.Treble8Below => "treble_8",
                    ClefType.Treble8Above => "treble^8",
                    ClefType.Soprano => "soprano",
                    ClefType.MezzoSoprano => "mezzosoprano",
                    ClefType.Baritone => "baritone",
                    ClefType.Bass8Below => "bass_8",
                    ClefType.Percussion => "percussion",
                    _ => "treble"
                };
                resolvedOctave ??= defaultOctave;
            }

            return (resolvedClef, resolvedOctave, transpose, clefPos);
        }

        return (null, null, null, 0);
    }

    private void CollectDefinitions(SyntaxNode root)
    {
        _root = root;
        _drumOverrides = DrumOverrides.Build(root);

        foreach (var node in root.DescendantNodes())
        {
            switch (node)
            {
                case MetadataDeclarationSyntax metadata:
                    CollectMetadata(metadata);
                    break;

                case TempoDeclarationSyntax tempoDecl:
                    // Only the top-level (initial) tempo sets the score default;
                    // mid-music tempo changes are handled in the music stream
                    // (a Tempo MusicMark at the change point).
                    if (!IsInsideMusicContent(tempoDecl))
                        CollectTempo(tempoDecl);
                    break;

                case TimeSignatureSyntax timeSig:
                    // Only the top-level (initial) time sets the global default;
                    // mid-music changes are handled in the music stream (a
                    // TimeSignatureChangeItem re-arms the per-measure length).
                    if (!IsInsideMusicContent(timeSig))
                    {
                        _timeBeats = timeSig.Beats;
                        _timeBeatsText = timeSig.BeatsText;
                        _timeSenzaMisura = timeSig.IsSenzaMisura;
                        _timeBeatType = timeSig.BeatType;
                        _timePosition = timeSig.Span.Start;
                    }
                    break;

                case KeySignatureSyntax key:
                    // Only process top-level key declarations (not inside phrases/sections)
                    if (!IsInsideMusicContent(key))
                    {
                        _keySharps = key.IsCustom ? 0 : CalculateKeySharps(key);
                        _keyCustom = key.IsCustom
                            ? KeySignature.EncodeCustom(key.CustomAlterations)
                            : null;
                        _keyPosition = key.Span.Start;
                    }
                    break;

                case ClefDeclarationSyntax clef:
                    _clef = clef.ClefName.Text.ToLowerInvariant();
                    _clefPosition = clef.ClefName.Span.Start;
                    break;

                case OctaveDirectiveSyntax octaveDir:
                    // A top-level `octave absolute/relative` sets the file default;
                    // mid-music switches are handled in the music stream.
                    if (!IsInsideMusicContent(octaveDir))
                        _octave.OctaveAbsolute = octaveDir.IsAbsolute;
                    break;

                case PartialDeclarationSyntax partialDecl:
                    // A top-level `partial N` declares the pickup once for every
                    // part (grammar feedback: writing it in each voice repeated a
                    // fact of the piece). Mid-music `partial` stays per voice.
                    if (!IsInsideMusicContent(partialDecl))
                        _filePartial = partialDecl.ToFraction();
                    break;

                case SectionDeclarationSyntax section:
                    // First declaration of a name wins as the order/label
                    // representative (source order), so a name appearing in both
                    // forms stays stable.
                    if (!_sections.ContainsKey(section.SectionName))
                        _sections[section.SectionName] = section;
                    // Part-major: an inner section binds its music to the part it
                    // lives in. Record the (section, part) cell for voice lookup.
                    var owningPart = EnclosingPartName(section);
                    if (owningPart != null)
                        _partMajorCells[(section.SectionName, owningPart)] = section;
                    break;

                case StructureDeclarationSyntax structure:
                    // Only the top-level structure becomes the file default. A
                    // structure nested in a `score { }` block is a per-score
                    // override, applied later from the RenderSpec.
                    if (!IsInsideRender(structure))
                        _structure = structure;
                    break;

                case VariableDeclarationSyntax varDecl:
                    _variables[varDecl.Name.Text] = varDecl.Expression;
                    break;

                case PhraseDeclarationSyntax phraseDecl:
                    _variables[phraseDecl.Name.Text] = phraseDecl.Body;
                    break;

                case RenderDeclarationSyntax render:
                    ExtractVoiceName(render);
                    break;
            }
        }
    }

    private void CollectMetadata(MetadataDeclarationSyntax metadata)
    {
        var keyword = metadata.Keyword.ToLowerInvariant();
        var values = metadata.Values.ToList();

        switch (keyword)
        {
            case "title":
                if (values.Count > 0 && values[0] is SyntaxTokenNode titleToken)
                {
                    _title = titleToken.Text.Trim('"');
                    _titlePosition = titleToken.Span.Start;
                }
                break;
            case "composer":
                if (values.Count > 0 && values[0] is SyntaxTokenNode composerToken)
                {
                    _composer = composerToken.Text.Trim('"');
                    _composerPosition = composerToken.Span.Start;
                }
                break;
        }
    }

    private void CollectTempo(TempoDeclarationSyntax tempoDecl)
    {
        // Every written form reaches the opening mark: `tempo 120`,
        // `tempo "Grave"`, `tempo "Grave" 120`, `tempo "Grave" 4 = 54`,
        // `tempo "Lively" 4. = 116`. The text form used to be dropped
        // silently (only a bare leading integer was read).
        if (tempoDecl.Bpm is int bpm)
            _tempo = bpm;
        if (tempoDecl.Marking is string marking)
            _tempoText = marking;
        // Beat unit incl. dots: walk back from `=` over the dot tokens to the
        // unit number ("4." lexes as IntegerLiteral 4 + Dot at declaration
        // level, so the dots arrive as separate tokens).
        var tokens = tempoDecl.Values.OfType<SyntaxTokenNode>().ToList();
        int eq = tokens.FindIndex(t => t.Kind == SyntaxKind.Equals);
        if (eq > 0)
        {
            int i = eq - 1, dots = 0;
            while (i >= 0 && tokens[i].Kind == SyntaxKind.Dot)
            {
                dots++;
                i--;
            }
            var m = i >= 0
                ? System.Text.RegularExpressions.Regex.Match(tokens[i].Text, @"^([0-9]+)(\.*)$")
                : System.Text.RegularExpressions.Match.Empty;
            if (m.Success)
            {
                _tempoBeatUnit = int.Parse(m.Groups[1].Value);
                _tempoDots = dots + m.Groups[2].Value.Length;
            }
        }
        if (tempoDecl.SwingSubdivision != 0)
            _swingSubdivision = tempoDecl.SwingSubdivision;
    }

    private int CalculateKeySharps(KeySignatureSyntax key)
    {
        // PitchName already includes accidental suffix (e.g., "bes", "fis")
        return LilySharp.Core.Music.KeySpelling.SharpsFor(
            key.Pitch.PitchName, key.Mode.Text) ?? 0;
    }

    /// <summary>
    /// Gets the expected alteration for a pitch step based on the current key signature.
    /// </summary>
    private int GetKeySignatureAlteration(int step)
    {
        if (_keyCustom != null)
        {
            foreach (var (s, a) in KeySignature.DecodeCustom(_keyCustom))
                if (s == step)
                    return a;
            return 0;
        }
        return LilySharp.Core.Music.KeySpelling.Alteration(step, _keySharps);
    }

    /// <summary>
    /// Determines the displayed accidental for a pitch using LilyPond's default
    /// accidental style: an accidental is printed when the pitch's alteration
    /// differs from the one currently IN EFFECT for that (step, octave) within
    /// the measure. The in-effect value starts at the key signature each measure
    /// and is updated by every engraved note, so a sharp/flat persists to the
    /// barline (a later same-pitch note in the measure needs no repeat, and a
    /// return to the key value prints a cancelling natural). Memory is
    /// octave-specific and resets at the barline (MeasureBuilder.MeasureCompleted).
    /// Explicit @courtesy is layered on at the call site. Verified against
    /// LilyPond 2.24.4.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/accidental-engraver.cc — default style.</remarks>
    // Takes the DISPLAY pitch (post-transpose): diatonic step (0–6), its
    // accidental in semitones, and octave.
    private (string? accidental, bool isCourtesy) GetDisplayAccidentalWithCourtesy(int step, int actual, int octave)
    {
        var key = (step, octave);
        // In effect: a prior accidental on this exact pitch this measure, else
        // the key signature. A mid-measure key change updates the latter for
        // pitches not yet altered this measure (GetKeySignatureAlteration reads
        // the live key) without disturbing remembered alterations.
        int inEffect = _measureAccidentals.TryGetValue(key, out int remembered)
            ? remembered
            : GetKeySignatureAlteration(step);

        // Remember this pitch's alteration for the rest of the measure.
        _measureAccidentals[key] = actual;

        if (actual != inEffect)
        {
            return (actual switch
            {
                2 => "doubleSharp",
                1 => "sharp",
                0 => "natural",
                -1 => "flat",
                -2 => "doubleFlat",
                _ => null
            }, false);
        }

        return (null, false);
    }

    private static int PitchNameToStep(char name) => char.ToLower(name) switch
    {
        'c' => 0, 'd' => 1, 'e' => 2, 'f' => 3, 'g' => 4, 'a' => 5, 'b' => 6,
        _ => 0
    };

    private void ExtractVoiceName(RenderDeclarationSyntax render)
    {
        // Inference only: never clobber a voice the caller pinned (SvgGenerator
        // passes the selected render's voice), and with multiple render blocks
        // the FIRST one wins — previously every render block overwrote
        // _voiceName, so a two-render file always collected the LAST render's
        // part regardless of which render was being generated.
        if (_voiceName != null)
            return;

        if (render.GetChild(1) is not SyntaxTokenNode outputType || outputType.Text != "score")
            return;

        foreach (var child in render.DescendantNodes())
        {
            if (child is StaffRenderSyntax staff)
            {
                for (int i = 0; i < staff.SlotCount; i++)
                {
                    if (staff.GetChild(i) is SyntaxTokenNode token &&
                        token.Kind == SyntaxKind.Identifier &&
                        token.Text != "staff" && token.Text != "treble" &&
                        token.Text != "bass" && token.Text != "alto" && token.Text != "tenor")
                    {
                        _voiceName = token.Text;
                        return;
                    }
                }
            }
        }
    }

    private List<Measure> CollectMeasures()
    {
        var builder = new MeasureBuilder(TimeSignatureFraction);
        if (_filePartial is { } filePickup)
            builder.SetPartial(filePickup); // top-level partial N arms every voice
        _measureAccidentals.Clear();
        builder.MeasureCompleted = _measureAccidentals.Clear;

        _pendingInlineVoltas.Clear();

        void ProcessNodes(IEnumerable<SyntaxNode> nodes)
        {
            var nodeList = nodes.ToList();
            for (int i = 0; i < nodeList.Count; i++)
            {
                var node = nodeList[i];

                // Phrase-reference boundary: evaluate the body in the default
                // frame (same handling as ProcessMusicNodeSequence).
                if (node is RelativeResetMarker)
                {
                    _octave.ResetToInitial();
                    _defaultDuration = Fraction.Quarter;
                    continue;
                }

                // Check if next node is a tie, slur, or beam marker
                bool hasTieAfter = i + 1 < nodeList.Count && nodeList[i + 1] is TieSyntax;
                bool hasSlurStartAfter = i + 1 < nodeList.Count && nodeList[i + 1] is SlurSyntax slurS && slurS.IsOpen;
                bool hasSlurEndAfter = i + 1 < nodeList.Count && nodeList[i + 1] is SlurSyntax slurE && !slurE.IsOpen;
                bool hasBeamStartAfter = i + 1 < nodeList.Count && nodeList[i + 1] is BeamMarkerSyntax beamS && beamS.IsStart;
                bool hasBeamEndAfter = i + 1 < nodeList.Count && nodeList[i + 1] is BeamMarkerSyntax beamE && !beamE.IsStart;
                ProcessMusicNode(node, builder, hasTieAfter, hasSlurStartAfter, hasSlurEndAfter, hasBeamStartAfter, hasBeamEndAfter);
            }
        }

        // Process based on structure or sections
        if (_structure != null)
        {
            ProcessStructure(ProcessNodes, builder);
        }
        else if (_sections.Count > 0)
        {
            // No `structure { }` — default to the order the sections were declared
            // (source order), so a single-section piece needs no structure at all.
            foreach (var section in _sections.Values.OrderBy(s => s.Name.Span.Start))
            {
                if (!_sectionStartMeasure.ContainsKey(section.SectionName))
                    _sectionStartMeasure[section.SectionName] = builder.CurrentMeasureIndex;
                builder.SectionLabel = section.SectionName;
                builder.SectionLabelPosition = section.Name.Span.Start;
                ProcessSection(section, ProcessNodes);
            }
        }
        else if (_root != null)
        {
            var musicNodes = _root.DescendantNodes()
                .Where(n => !IsInsideTuplet(n) && !IsInsideRepeat(n) && !IsInsideOnce(n) && !IsInsideGrace(n) && !IsInsideInlineVolta(n) && !IsInsideParallel(n) && n is NoteSyntax or RestSyntax or ChordSyntax or BarlineSyntax or BreakSyntax or TieSyntax or SlurSyntax or BeamMarkerSyntax or InlineVoltaSyntax or GraceExpressionSyntax or TupletExpressionSyntax or RepeatExpressionSyntax or ParallelExpressionSyntax or OverrideDeclarationSyntax or RevertDeclarationSyntax or OnceModifierSyntax or KeySignatureSyntax or TimeSignatureSyntax or TempoDeclarationSyntax);
            ProcessNodes(musicNodes);
        }

        FinalizeInlineVoltas();

        return builder.FinalizeMeasures();
    }

    private void ProcessStructure(Action<IEnumerable<SyntaxNode>> processNodes, MeasureBuilder builder)
    {
        foreach (var child in _structure!.DescendantNodes())
        {
            switch (child)
            {
                case SectionReferenceSyntax reference:
                    // Skip if inside a repeat block (will be handled by ProcessRepeatBlock)
                    if (IsInsideRepeatBlock(reference))
                        break;
                    if (_sections.TryGetValue(reference.SectionName, out var section))
                    {
                        if (!_sectionStartMeasure.ContainsKey(reference.SectionName))
                            _sectionStartMeasure[reference.SectionName] = builder.CurrentMeasureIndex;
                        builder.SectionLabel = ResolveSectionLabel(reference);
                        builder.SectionLabelPosition = SectionDeclPos(reference.SectionName);
                        ProcessSection(section, processNodes);
                    }
                    break;

                case StructureRepeatBlockSyntax repeat:
                    ProcessRepeatBlock(repeat, processNodes, builder);
                    break;

                // Navigation marks in the structure (segno / coda / fine / to coda /
                // D.C. / D.S. al fine|coda) — engraved like the inline @-marks, at the
                // boundary of the section just played.
                case NavigationMarkSyntax nav when !IsInsideRepeatBlock(nav):
                    var navMark = NavigationToMusicMark(nav.MarkType);
                    // Target signs (segno/coda — where a jump lands) sit at the START
                    // of the next section; the jump-from text (fine / to coda / D.S. /
                    // D.C.) sits at the END of the section just played.
                    bool target = navMark is MusicMarkType.Segno or MusicMarkType.Coda;
                    int navMeasure = target
                        ? builder.CurrentMeasureIndex
                        : Math.Max(0, builder.CurrentMeasureIndex - 1);
                    // ProcessStructure runs once PER PART; a structure-level mark
                    // must engrave once per SCORE — without this guard a grand
                    // staff printed "Fine" / "D.C. al Fine" twice, stacked.
                    if (!_musicMarks.Any(m => m.Type == navMark
                            && m.MeasureIndex == navMeasure
                            && m.SourcePosition == nav.Position))
                        _musicMarks.Add(new MusicMarkItem(navMark, navMeasure, nav.Position));
                    break;

                // _"text" — a free text directive between sections, engraved like
                // the jump-from navigation text at the END of the section just
                // played. The grammar has carried this form all along; the
                // collector never produced the item, so it parsed but silently
                // printed nothing.
                case CustomTextSyntax custom when !IsInsideRepeatBlock(custom):
                    // Same per-part guard as the navigation marks above.
                    int textMeasure = Math.Max(0, builder.CurrentMeasureIndex - 1);
                    if (!_customTexts.Any(t => t.Text == custom.Text
                            && t.MeasureIndex == textMeasure
                            && t.SourcePosition == custom.Position))
                        _customTexts.Add(new CustomTextItem(
                            custom.Text, textMeasure, custom.Position));
                    break;

                // ~Name — render the section's music but show NO label (the dedicated
                // form for an unlabelled section, e.g. a Coda). Without this the whole
                // section was silently dropped.
                case { Kind: SyntaxKind.SilentSectionReference } silent
                        when !IsInsideRepeatBlock(silent)
                          && silent.GetChild(1) is SyntaxTokenNode nameTok
                          && _sections.TryGetValue(nameTok.Text, out var silentSection):
                    if (!_sectionStartMeasure.ContainsKey(nameTok.Text))
                        _sectionStartMeasure[nameTok.Text] = builder.CurrentMeasureIndex;
                    builder.SectionLabel = null;
                    builder.SectionLabelPosition = SectionDeclPos(nameTok.Text);
                    ProcessSection(silentSection, processNodes);
                    break;
            }
        }
    }

    /// <summary>
    /// The printed mark for one section occurrence: the per-occurrence display
    /// label when given (<c>structure { First First "First (reprise)" }</c> —
    /// an empty string suppresses the mark like <c>~First</c>), else the
    /// section identifier.
    /// </summary>
    /// <summary>Source offset of a section's <c>section X</c> declaration (0 if the
    /// name is unknown), so its label mark can jump to the declaration. Anchored on
    /// the <c>section</c> keyword (not the name) so hovering anywhere on the
    /// declaration highlights the label in the preview. Sections are registered
    /// before structure expansion, so the lookup is populated here.</summary>
    private int SectionDeclPos(string sectionName)
        => _sections.TryGetValue(sectionName, out var s) ? s.SectionKeyword.Span.Start : 0;

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

    private static bool IsInsideRender(SyntaxNode node) => node.IsInside<RenderDeclarationSyntax>();

    /// <summary>
    /// Rows-only scores reach row collection with an EMPTY section-start
    /// table — sections normally register while MUSIC is processed. Derive
    /// the starts from the row blocks themselves: walk the structure (or
    /// declaration) order, advancing by each section's widest row block
    /// (chord bars preferred, lyric bars otherwise). Without this a
    /// two-section chord grid printed both sections' symbols from bar 0,
    /// overlapped. No-op when music already filled the table.
    /// </summary>
    private void EnsureSectionStartsForRows()
    {
        if (_sectionStartMeasure.Count > 0 || _sections.Count == 0)
            return;

        // Walk the structure's children IN SOURCE ORDER so navigation marks
        // (segno / to coda / D.S. …) interleave with the section references at
        // the right bars — a rows-only score never runs ProcessStructure, so
        // the band grid lost exactly the signs a band chart needs. Labels are
        // stamped onto the grid row's measures afterwards.
        int cur = 0;
        void AdvanceSection(string name, string? label, int pos)
        {
            if (!_sections.TryGetValue(name, out var section))
                return;
            if (!_sectionStartMeasure.ContainsKey(name))
            {
                _sectionStartMeasure[name] = cur;
                if (label != null)
                    _rowSectionLabels.Add((cur, label, pos));
            }

            int chordBars = 0, lyricBars = 0;
            foreach (var cb in section.DescendantNodes().OfType<ChordPartBlockSyntax>())
                chordBars = Math.Max(chordBars, ChordNameCollector.CountBars(cb));
            foreach (var lb in section.DescendantNodes().OfType<LyricsBlockSyntax>())
                lyricBars = Math.Max(lyricBars, lb.Syllables.Count());
            cur = _sectionStartMeasure[name] + (chordBars > 0 ? chordBars : lyricBars);
        }

        if (_structure != null)
        {
            foreach (var child in _structure.DescendantNodes())
            {
                switch (child)
                {
                    case SectionReferenceSyntax r when !IsInsideRepeatBlock(r):
                        AdvanceSection(r.SectionName, ResolveSectionLabel(r), SectionDeclPos(r.SectionName));
                        break;
                    case NavigationMarkSyntax nav when !IsInsideRepeatBlock(nav):
                        // Same anchoring as ProcessStructure: targets (segno/coda)
                        // at the NEXT section's start, jump text at the end of
                        // the section just played.
                        var navMark = NavigationToMusicMark(nav.MarkType);
                        bool target = navMark is MusicMarkType.Segno or MusicMarkType.Coda;
                        int navMeasure = target ? cur : Math.Max(0, cur - 1);
                        _musicMarks.Add(new MusicMarkItem(navMark, navMeasure, nav.Position));
                        break;
                    case CustomTextSyntax custom when !IsInsideRepeatBlock(custom):
                        _customTexts.Add(new CustomTextItem(
                            custom.Text, Math.Max(0, cur - 1), custom.Position));
                        break;
                }
            }
        }
        else
        {
            foreach (var s in _sections.Values.OrderBy(s => s.Name.Span.Start))
                AdvanceSection(s.SectionName, s.SectionName, s.Name.Span.Start);
        }
    }

    private static bool IsInsideRepeatBlock(SyntaxNode node) => node.IsInside<StructureRepeatBlockSyntax>();

    /// <summary>
    /// Checks if a node is inside music content (phrase/section/variable body).
    /// Used by CollectDefinitions to distinguish top-level declarations from mid-music changes.
    /// </summary>
    private static bool IsInsideMusicContent(SyntaxNode node)
    {
        var parent = node.Parent;
        while (parent != null)
        {
            if (parent is PhraseDeclarationSyntax or SectionDeclarationSyntax
                or VariableDeclarationSyntax or PartBlockSyntax)
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

    private void ProcessRepeatBlock(StructureRepeatBlockSyntax repeat, Action<IEnumerable<SyntaxNode>> processNodes, MeasureBuilder builder)
    {
        bool afterRepeatStart = false;
        var pendingVoltaBrackets = new List<(int startMeasure, int endMeasure, string voltaText, bool isClosed, int sourcePosition)>();

        for (int i = 0; i < repeat.SlotCount; i++)
        {
            var child = repeat.GetChild(i);

            if (child is SyntaxTokenNode token)
            {
                if (token.Text == "|:")
                {
                    processNodes(new[] { CreateBarlineSyntax(token.Text, token.Position) });
                    afterRepeatStart = true;
                }
                else if (token.Text == ":|")
                {
                    processNodes(new[] { CreateBarlineSyntax(token.Text, token.Position) });
                }
            }
            else if (afterRepeatStart)
            {
                if (child is SectionReferenceSyntax reference)
                {
                    if (_sections.TryGetValue(reference.SectionName, out var section))
                    {
                        if (!_sectionStartMeasure.ContainsKey(reference.SectionName))
                            _sectionStartMeasure[reference.SectionName] = builder.CurrentMeasureIndex;
                        builder.SectionLabel = ResolveSectionLabel(reference);
                        builder.SectionLabelPosition = SectionDeclPos(reference.SectionName);
                        ProcessSection(section, processNodes);
                    }
                }
                else if (child is StructureAlternativeSyntax alt)
                {
                    string altSectionName = alt.SectionName.Text;
                    if (_sections.TryGetValue(altSectionName, out var section))
                    {
                        // Track measure index before processing this alternative
                        int startMeasureIndex = builder.CurrentMeasureIndex;

                        builder.SectionLabel = altSectionName;
                        builder.SectionLabelPosition = SectionDeclPos(altSectionName);
                        ProcessSection(section, processNodes);

                        // Track measure index after processing
                        int endMeasureIndex = builder.CurrentMeasureIndex;
                        // If we're mid-measure, include that measure
                        if (builder.CurrentItemCount > 0)
                            endMeasureIndex++;

                        // Collect volta bracket info if bracket style
                        // endMeasureIndex is exclusive (one-past-end); convert to inclusive
                        // for VoltaBracketItem which stores the last measure index
                        if (alt.HasBracket && !alt.IsSilent)
                        {
                            int lastMeasure = Math.Max(startMeasureIndex, endMeasureIndex - 1);
                            pendingVoltaBrackets.Add((startMeasureIndex, lastMeasure, alt.VoltaText, alt.IsClosed, alt.Position));
                        }
                    }
                }
            }
        }

        // Each ending's right cap follows its source ']' (present = closed); the
        // engraver's segment splitter opens only line-break pieces of a closed one.
        foreach (var (startMeasure, endMeasure, voltaText, isClosed, sourcePosition) in pendingVoltaBrackets)
            _voltaBrackets.Add(new VoltaBracketItem(startMeasure, endMeasure, voltaText, isClosed, sourcePosition));
    }

    private void ProcessSection(SectionDeclarationSyntax section, Action<IEnumerable<SyntaxNode>> processNodes)
    {
        // Reset the relative frame (and revert the octave mode to the file
        // default) at each section boundary.
        _octave.ResetForSection();

        bool matched = false;
        foreach (var child in section.DescendantNodes())
        {
            if (child is PartBlockSyntax partBlock)
            {
                if (_voiceName == null || partBlock.Name == _voiceName)
                {
                    ProcessMusicContainer(partBlock, processNodes);
                    matched = true;

                    if (_voiceName != null) return;
                }
            }
        }

        // Part-major fallback: this section's music for the current voice is not a
        // part-block here but lives inside `part <voice> { section <name> { ... } }`.
        if (!matched && _voiceName != null
            && _partMajorCells.TryGetValue((section.SectionName, _voiceName), out var cell))
        {
            ProcessMusicContainer(cell, processNodes);
        }
    }

    /// <summary>
    /// Process the music inside a container node — a <c>part-block</c> (section-major)
    /// or a part-major inner <c>section</c>. Both expose their music as descendants.
    /// </summary>
    private void ProcessMusicContainer(SyntaxNode container, Action<IEnumerable<SyntaxNode>> processNodes)
    {
        // Collect all music nodes, expanding variable references
        var musicNodes = new List<SyntaxNode>();

        foreach (var node in container.DescendantNodes())
        {
            // Skip nodes inside containers (tuplet/repeat/grace/inline volta/
            // parallel) — they'll be processed by those handlers. Inline voltas
            // in particular must pass through as ONE wrapper node, or the
            // bracket ([1. ]/[2.]) is lost while its notes leak out flat. A
            // << \\ >> span likewise passes through as one node.
            if (IsInsideTuplet(node) || IsInsideRepeat(node) || IsInsideGrace(node)
                || IsInsideInlineVolta(node) || IsInsideParallel(node))
                continue;

            switch (node)
            {
                case NoteSyntax:
                case DrumNoteSyntax:
                case RestSyntax:
                case ChordSyntax:
                case BarlineSyntax:
                case BreakSyntax:
                case TieSyntax:
                case SlurSyntax:
                case BeamMarkerSyntax:
                case GraceExpressionSyntax:
                case TupletExpressionSyntax:
                case RepeatExpressionSyntax:
                case ParallelExpressionSyntax:
                case InlineVoltaSyntax:
                case MusicMarkSyntax:
                case ClefDeclarationSyntax:
                case OctaveDirectiveSyntax:
                case KeySignatureSyntax:
                case TimeSignatureSyntax:
                case TempoDeclarationSyntax:
                case PartialDeclarationSyntax:
                    musicNodes.Add(node);
                    break;

                case VariableReferenceSyntax varRef:
                    ExpandVariable(varRef.Name.Text, musicNodes);
                    break;
            }
        }

        processNodes(musicNodes);
    }

    private void ExpandVariable(string name, List<SyntaxNode> musicNodes)
    {
        if (!_variables.TryGetValue(name, out var expression))
            return;

        // Each phrase reference evaluates its body in a FRESH relative frame
        // (default octave / pitch / duration): a phrase's pitches must not
        // depend on what happened to be played before the reference, or the
        // same $phrase would render differently at every call site. This is
        // the moral equivalent of LilyPond variables carrying their own
        // \relative block. State flows OUT of the phrase normally, so a note
        // following $phrase is relative to the phrase's last note.
        musicNodes.Add(RelativeResetMarker.Instance);

        // Include expression itself if it is a music node
        if (expression is NoteSyntax or DrumNoteSyntax or RestSyntax or ChordSyntax or BarlineSyntax or TieSyntax or SlurSyntax or BeamMarkerSyntax
            or GraceExpressionSyntax or TupletExpressionSyntax or RepeatExpressionSyntax or ParallelExpressionSyntax or InlineVoltaSyntax
            or OverrideDeclarationSyntax or RevertDeclarationSyntax or OnceModifierSyntax or MusicMarkSyntax or BreakSyntax
            or ClefDeclarationSyntax or OctaveDirectiveSyntax or KeySignatureSyntax or TimeSignatureSyntax or TempoDeclarationSyntax
            or PartialDeclarationSyntax)
        {
            musicNodes.Add(expression);
        }

        // Get music nodes from the variable expression descendants.
        // Skip nodes inside containers (grace, tuplet, repeat, once, inline
        // volta, parallel) — they'll be processed by those handlers; the inline
        // volta and the << \\ >> span must travel as ONE wrapper node each.
        var nodes = expression.DescendantNodes()
            .Where(n => !IsInsideGrace(n) && !IsInsideTuplet(n) && !IsInsideRepeat(n) && !IsInsideOnce(n)
                && !IsInsideInlineVolta(n) && !IsInsideParallel(n)
                && n is NoteSyntax or DrumNoteSyntax or RestSyntax or ChordSyntax or BarlineSyntax or TieSyntax or SlurSyntax or BeamMarkerSyntax
                or GraceExpressionSyntax or TupletExpressionSyntax or RepeatExpressionSyntax or ParallelExpressionSyntax or InlineVoltaSyntax
                or OverrideDeclarationSyntax or RevertDeclarationSyntax or OnceModifierSyntax or MusicMarkSyntax or BreakSyntax
                or ClefDeclarationSyntax or OctaveDirectiveSyntax or KeySignatureSyntax or TimeSignatureSyntax or TempoDeclarationSyntax
                or PartialDeclarationSyntax);

        musicNodes.AddRange(nodes);
    }

    private static BarlineSyntax CreateBarlineSyntax(string barText, int position)
    {
        var kind = barText switch
        {
            "|:" => SyntaxKind.RepeatStartBar,
            ":|" => SyntaxKind.RepeatEndBar,
            "||" => SyntaxKind.DoubleBar,
            "|." => SyntaxKind.FinalBar,
            _ => SyntaxKind.Bar
        };

        var token = new LilySharp.Core.Syntax.InternalSyntax.SyntaxToken(kind, barText);
        var green = new LilySharp.Core.Syntax.InternalSyntax.BarlineGreen(token);
        return new BarlineSyntax(green, null, position);
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
                    _dynamics.Add(new DynamicItem(level, measureIndex, itemIndex, dynamicSyntax.Position, _currentStaffIndex)
                    {
                        IsAbove = dynamicSyntax.ForcedAbove == true,
                    });
                }
                else
                {
                    // @cresc, @decresc, @dim — parsed as DynamicSyntax but Level=None
                    // Collect as MusicMark for hairpin detection
                    var markName = dynamicSyntax.DynamicToken.Text;
                    var markType = MusicMarkItem.ParseMarkName(markName);
                    if (markType != null)
                    {
                        _musicMarks.Add(new MusicMarkItem(markType.Value, measureIndex, dynamicSyntax.Position) { StaffIndex = _currentStaffIndex });
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
    /// Detects whether a note carries a <c>@repeatTie</c> articulation.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/repeat-tie-engraver.cc — repeat-tie attachment.</remarks>
    private static bool HasRepeatTieAnnotation(SyntaxNode node)
        => HasNamedArticulation(node, "repeattie");

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
        DrumNoteSyntax drum => drum.Articulations,
        _ => Enumerable.Empty<SyntaxNode>()
    };

    /// <summary>Notehead style from a <c>@notehead.x</c>-family compound mark
    /// on the note/chord, or Default. LILYPOND-REF: NoteHead style property.</summary>
    private static NoteheadStyle ExtractNoteheadStyle(SyntaxNode node)
    {
        foreach (var art in ArticulationsOf(node))
        {
            if (art is MusicMarkSyntax mark
                && mark.MarkName.StartsWith("notehead.", StringComparison.OrdinalIgnoreCase))
            {
                return mark.MarkName[9..].ToLowerInvariant() switch
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

    /// <summary>The finger number from a <c>@finger.N</c> compound mark, or null.</summary>
    /// <remarks>LILYPOND-REF: lily/fingering-engraver.cc — finger event handling.</remarks>
    private static int? ParseFingerMark(MusicMarkSyntax mark)
    {
        var name = mark.MarkName.ToLowerInvariant();
        return name.StartsWith("finger.") && int.TryParse(name.AsSpan(7), out int finger) && finger >= 0
            ? finger : null;
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
    /// position). Feeds <see cref="NoteItem.StemUpOverride"/>, the same slot beam
    /// resolution writes — so a stem override on a BEAMED note is superseded by the
    /// beam's shared direction (the beam carries one stem direction for the group).
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
            if (art is MusicMarkSyntax markSyntax && ParseFingerMark(markSyntax) is { } finger)
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
            if (art is MusicMarkSyntax markSyntax && ParseFingerMark(markSyntax) is { } finger)
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
                artSyntax.NameToken.Text is "glissando" or "gliss" or "slide")
                return true;
        }
        return false;
    }

    /// <summary>
    /// Checks if a note/chord has a @cue annotation.
    /// LILYPOND-REF: ly/engraver-init.ly CueVoice context — fontSize = #-4
    /// </summary>
    private static bool HasCueAnnotation(SyntaxNode node)
    {
        var articulations = ArticulationsOf(node);

        foreach (var art in articulations)
        {
            if (art is ArticulationSyntax artSyntax &&
                artSyntax.Type == ArticulationType.None &&
                artSyntax.NameToken.Text.Equals("cue", StringComparison.OrdinalIgnoreCase))
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
        // (each $call evaluates in the default frame).
        var bodyNodes = new List<SyntaxNode>();
        foreach (var item in repeat.Body.Items)
        {
            if (item is VariableReferenceSyntax varRef)
                ExpandVariable(varRef.Name.Text, bodyNodes);
            else
                bodyNodes.Add(item);
        }

        void ProcessBodyOnce()
        {
            foreach (var item in bodyNodes)
            {
                if (item is RelativeResetMarker)
                {
                    _octave.ResetToInitial();
                    _defaultDuration = Fraction.Quarter;
                    continue;
                }
                ProcessMusicNode(item, builder);
            }
        }

        if (type == "percent")
        {
            // First iteration: process body normally
            int startMeasure = builder.CurrentMeasureIndex;
            ProcessBodyOnce();
            int bodyMeasureCount = builder.CurrentMeasureIndex - startMeasure;

            // Additional iterations: process body again but mark as percent repeat
            for (int iter = 1; iter < count; iter++)
            {
                int iterStart = builder.CurrentMeasureIndex;
                ProcessBodyOnce();

                // Mark all measures in this iteration as percent repeats
                for (int m = 0; m < bodyMeasureCount; m++)
                {
                    _percentRepeats.Add(new PercentRepeatItem(
                        iterStart + m,
                        repeat.Position,
                        _currentStaffIndex));
                }
            }
        }
        else if (type == "tremolo" && bodyNodes.Count == 2
            && bodyNodes.All(b => b is NoteSyntax or ChordSyntax)
            && TremoloPairShape(count, bodyNodes) is { } pairShape)
        {
            // Two-note (chord) tremolo: both notes are WRITTEN with the
            // pair's total duration, sound half of it each, and are joined
            // by the subdivision's beams between the stems.
            // LILYPOND-REF: lily/chord-tremolo-engraver.cc / chord-tremolo-iterator.cc.
            _tremoloPairShape = pairShape;
            _tremoloPairFirst = true;
            ProcessBodyOnce();
            _tremoloPairShape = null;
        }
        else if (type == "tremolo" && bodyNodes.Count == 1
            && (bodyNodes[0] is NoteSyntax || bodyNodes[0] is ChordSyntax)
            && TremoloTotalIsPrintable(count, bodyNodes[0]))
        {
            // LILYPOND-REF: lily/chord-tremolo-iterator.cc +
            // lily/stem-tremolo.cc — `\repeat tremolo 8 { c32 }` engraves ONE
            // quarter note whose stem carries the 32nd's three slashes (the
            // same drawing as the c4:32 suffix); the repetition is aural.
            _tremoloRepeatCount = count;
            ProcessBodyOnce();
            _tremoloRepeatCount = 1;
        }
        else
        {
            // For volta/unfold (and non-printable tremolo shapes): unfold the
            // body count times.
            for (int i = 0; i < count; i++)
            {
                ProcessBodyOnce();
            }
        }
    }

    /// <summary>True when count × body duration reduces to a plain or dotted
    /// printable note value (1 → base, 3 → dotted, 7 → double-dotted).</summary>
    private static bool TremoloTotalIsPrintable(int count, SyntaxNode body)
    {
        int value = body switch
        {
            NoteSyntax n => n.Duration?.Value ?? 0,
            ChordSyntax ch => ch.Duration?.Value ?? 0,
            _ => 0
        };
        if (value < 8 || count < 2)
            return false;
        return CombineTremoloDuration(count, value) != null;
    }

    /// <summary>Shape of a two-note tremolo, or null when not printable:
    /// display duration = count × (both notes), equal written values required;
    /// beams = the subdivision's flag count (16th → 2).</summary>
    private static (int Value, int Dots, int Beams)? TremoloPairShape(int count, List<SyntaxNode> body)
    {
        int V(SyntaxNode n) => n switch
        {
            NoteSyntax ns => ns.Duration?.Value ?? 0,
            ChordSyntax cs => cs.Duration?.Value ?? 0,
            _ => 0
        };
        int v1 = V(body[0]), v2 = V(body[1]);
        if (v2 == 0)
            v2 = v1; // second note inherits the first's duration (c16 e)
        if (v1 < 8 || v1 != v2 || count < 1)
            return null;
        // total = count × 2 × (1/v1); reuse the single-note reducer.
        var total = CombineTremoloDuration(count * 2, v1);
        if (total == null)
            return null;
        int beams = (int)Math.Log2(v1) - 2;
        return (total.Value.Value, total.Value.Dots, beams);
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
    /// Collects figured bass annotations from note/chord modifiers.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/figured-bass-engraver.cc - listen_bass_figure
    /// Syntax: @fig.6 (single), @fig.6.4 (two figures), @fig.6.s (with sharp)
    /// </remarks>
    private void CollectFiguredBass(SyntaxNode node, int measureIndex, int itemIndex)
    {
        var articulations = ArticulationsOf(node);

        foreach (var child in articulations)
        {
            if (child is MusicMarkSyntax markSyntax)
            {
                var figures = FiguredBassItem.ParseFigures(markSyntax.MarkName);
                if (figures != null)
                {
                    _figuredBasses.Add(new FiguredBassItem(
                        figures.Value,
                        measureIndex,
                        itemIndex,
                        markSyntax.Position,
                        _currentStaffIndex));
                }
            }
        }
    }

    /// <summary>
    /// Collects chord name annotations (@chord.TEXT) from a note or chord's articulations.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/scheme-engravers.scm:1309 - Current_chord_text_engraver
    /// Syntax: @chord.Cm7, @chord.Bb7, @chord.Am
    /// </remarks>
    private void CollectChordNames(SyntaxNode node, int measureIndex, int itemIndex)
    {
        var articulations = ArticulationsOf(node);

        foreach (var child in articulations)
        {
            if (child is MusicMarkSyntax markSyntax)
            {
                var chordText = ChordNameItem.ParseChordName(markSyntax.MarkName);
                if (chordText != null)
                    _chordNameCollector.AddInline(
                        chordText, measureIndex, itemIndex, markSyntax.Position, _currentStaffIndex);
            }
        }
    }

    /// <summary>
    /// Detects @cross annotation on a note or chord for cross-staff rendering.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/beam.cc:1451-1459 - cross-staff detection
    /// Syntax: @cross marks a note for rendering on the other staff in a grand staff.
    ///
    /// In a grand staff context:
    /// - If voice is on staff 0 (treble), @cross moves to staff 1 (bass)
    /// - If voice is on staff 1 (bass), @cross moves to staff 0 (treble)
    /// The TargetStaffIndex is resolved later during layout based on voice assignment.
    /// Here we use 0 as a placeholder (actual target resolved by layout engine).
    /// </remarks>
    private void CollectCrossStaff(SyntaxNode node, int measureIndex, int itemIndex)
    {
        var articulations = ArticulationsOf(node);

        foreach (var child in articulations)
        {
            // @cross is parsed as ArticulationSyntax (single Identifier, no dot)
            if (child is ArticulationSyntax artSyntax && artSyntax.NameToken.Text == "cross")
            {
                _crossStaffItems.Add(new CrossStaffItem(
                    measureIndex,
                    itemIndex,
                    0,
                    artSyntax.Position));
                return;
            }
        }
    }

    /// <summary>
    /// Collects a grob property override from an OverrideDeclarationSyntax.
    /// LILYPOND-REF: lily/context-property.cc (push)
    /// </summary>
    private void CollectOverride(OverrideDeclarationSyntax node, int measureIndex, int itemIndex, bool isOnce)
    {
        string grobType = node.GrobName.Text;
        string propertyName = node.PropertyName.Text;
        // A quoted value (e.g. color = "red") stores its CONTENT, not the quotes, so the
        // resolver's GetString/ParseColor see "red" — matching the bare-identifier form.
        // A combined negative number keeps any interior whitespace ("- 5") in its token
        // text for round-tripping, so strip it here for the numeric reparse (GetInt /
        // GetDouble). Identifiers and positive integers carry no interior whitespace, so
        // stripping is a no-op for them.
        string value = node.ValueToken.Kind == SyntaxKind.StringLiteral
            ? node.ValueToken.Text.Trim('"')
            : string.Concat(node.ValueToken.Text.Where(c => !char.IsWhiteSpace(c)));
        _grobOverrides.Add(new GrobOverride(grobType, propertyName, value, measureIndex, itemIndex, isOnce));
    }

    /// <summary>
    /// Collects a grob property revert from a RevertDeclarationSyntax.
    /// LILYPOND-REF: lily/context-property.cc (pop)
    /// </summary>
    private void CollectRevert(RevertDeclarationSyntax node, int measureIndex, int itemIndex)
    {
        string grobType = node.GrobName.Text;
        string propertyName = node.PropertyName.Text;
        _grobReverts.Add(new GrobRevert(grobType, propertyName, measureIndex, itemIndex));
    }

    /// <summary>
    /// Gets the feathered beam direction from a note's articulations.
    /// Returns 0 (none), 1 (right/accel), or -1 (left/rit).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: beam.cc:1039-1082 grow-direction
    /// Syntax: @feather.right (accelerando) or @feather.left (ritardando)
    /// </remarks>
    private static int GetFeatherDirection(SyntaxNode node)
    {
        if (node is not NoteSyntax note)
            return 0;

        foreach (var child in note.Articulations)
        {
            if (child is MusicMarkSyntax markSyntax)
            {
                var name = markSyntax.MarkName.ToLowerInvariant();
                if (name == "feather.right" || name == "feather.accel")
                    return 1;
                if (name == "feather.left" || name == "feather.rit")
                    return -1;
            }
        }
        return 0;
    }

    /// <summary>
    /// Collects articulation marks from note/chord modifiers.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: script-engraver.cc:92-125 Script_engraver::acknowledge_note_head
    /// </remarks>
    private void CollectArticulations(SyntaxNode node, int measureIndex, int itemIndex, bool stemUp,
        string? editorialAccidental = null, Fraction anchorTiming = default)
    {
        var articulations = node switch
        {
            NoteSyntax note => note.Articulations,
            ChordSyntax chord => chord.Articulations,
            RestSyntax rest => rest.Articulations,
            _ => Enumerable.Empty<SyntaxNode>()
        };

        foreach (var articulation in articulations)
        {
            if (articulation is ArticulationSyntax articulationSyntax)
            {
                var type = articulationSyntax.Type;
                if (type != ArticulationType.None)
                {
                    // LILYPOND-REF: script-interface.cc:23-45 direction calculation
                    // Articulations go opposite to stem direction by default
                    bool isAbove = !stemUp;

                    // Fermata and ornaments always go above
                    // LILYPOND-REF: define-grobs.scm:1365 fermata: direction = UP
                    // LILYPOND-REF: define-grobs.scm:2175 ornaments: direction = UP
                    if (type == ArticulationType.Fermata ||
                        type == ArticulationType.Trill ||
                        type == ArticulationType.Mordent ||
                        type == ArticulationType.Prall ||
                        type == ArticulationType.Turn ||
                        type == ArticulationType.InvertedTurn ||
                        type == ArticulationType.PrallTriller ||
                        // Breathing signs always sit at the top of the staff.
                        type == ArticulationType.Breath ||
                        type == ArticulationType.Caesura)
                    {
                        isAbove = true;
                    }

                    // An explicit '.up' / '.down' qualifier overrides the automatic side.
                    if (articulationSyntax.ForcedAbove is bool forcedAbove)
                        isAbove = forcedAbove;

                    _articulations.Add(new ArticulationItem(type, measureIndex, itemIndex, isAbove, articulationSyntax.Position, _currentStaffIndex));
                }
                else
                {
                    // Check for trill spanner start/stop
                    // LILYPOND-REF: scm/scheme-engravers.scm — \startTrillSpan / \stopTrillSpan
                    var nameText = articulationSyntax.NameToken.Text;
                    var nameLower = nameText.ToLowerInvariant();
                    if (nameLower == "starttrillspan")
                    {
                        _trillSpannerEvents.Add((true, measureIndex, itemIndex, articulationSyntax.Position, _currentStaffIndex));
                    }
                    else if (nameLower == "stoptrillspan")
                    {
                        _trillSpannerEvents.Add((false, measureIndex, itemIndex, articulationSyntax.Position, _currentStaffIndex));
                    }
                    else if (nameLower == "courtesy")
                    {
                        // LILYPOND-REF: lily/accidental.cc:147-148 — parenthesized property
                        // Explicit @courtesy annotation forces courtesy (parenthesized) accidental
                        _courtesySourcePositions.Add(node.Position);
                    }
                    else if (nameLower == "editorial" && editorialAccidental != null)
                    {
                        // Editorial (suggestion) accidental: a small accidental
                        // ABOVE the note; the kind was resolved in CreateNoteItem.
                        // LILYPOND-REF: scm/define-grobs.scm:96-123 AccidentalSuggestion
                        _articulations.Add(new ArticulationItem(
                            ArticulationItem.EditorialTypeFor(editorialAccidental),
                            measureIndex, itemIndex, isAbove: true,
                            articulationSyntax.Position, _currentStaffIndex));
                    }
                    else
                    {
                        // Check if this articulation is a MusicMark (cresc, rit, mark.A, ottava, ped, etc.)
                        var markType = MusicMarkItem.ParseMarkName(nameText);
                        if (markType != null)
                        {
                            if (markType.Value == MusicMarkType.Rehearsal)
                            {
                                string text = MusicMarkItem.ParseRehearsalText(nameText);
                                _musicMarks.Add(new MusicMarkItem(MusicMarkType.Rehearsal, text, measureIndex, articulationSyntax.Position, itemIndex, anchorTiming) { StaffIndex = _currentStaffIndex });
                            }
                            else
                            {
                                // Anchor to the host note's column so note-attached
                                // marks (e.g. pedal "Ped.") sit at the note, not the
                                // measure start.
                                _musicMarks.Add(new MusicMarkItem(markType.Value, measureIndex, articulationSyntax.Position, itemIndex, anchorTiming) { StaffIndex = _currentStaffIndex });
                            }
                        }
                    }
                }
            }
            else if (articulation is MusicMarkSyntax markSyntax)
            {
                // Handle compound mark syntax: @trillSpan.start / @trillSpan.stop
                var markName = markSyntax.MarkName.ToLowerInvariant();
                if (markName == "trillspan.start")
                {
                    _trillSpannerEvents.Add((true, measureIndex, itemIndex, markSyntax.Position, _currentStaffIndex));
                }
                else if (markName == "trillspan.stop")
                {
                    _trillSpannerEvents.Add((false, measureIndex, itemIndex, markSyntax.Position, _currentStaffIndex));
                }
                else if (markName.StartsWith("pluck.") && markName.Length == 7
                         && markName[6] is 'p' or 'i' or 'm' or 'a')
                {
                    // p-i-m-a right-hand fingering, printed BELOW the note.
                    _articulations.Add(new ArticulationItem(
                        ArticulationType.Pluck, measureIndex, itemIndex, false,
                        markSyntax.Position, _currentStaffIndex)
                    { PluckLetter = markName[6..] });
                }
                else if (markName.StartsWith("frame."))
                {
                    // @frame(x32010) — chord diagram above the note.
                    // LILYPOND-REF: MusicXML <frame>; LP \fret-diagram.
                    var spec = markName[6..];
                    if (spec.Length is >= 4 and <= 8
                        && spec.All(ch => ch is 'x' or 'o' or (>= '0' and <= '9')))
                        _articulations.Add(new ArticulationItem(
                            ArticulationType.FretFrame, measureIndex, itemIndex, true,
                            markSyntax.Position, _currentStaffIndex)
                        { FrameSpec = spec });
                }
                else if (markName.StartsWith("bend."))
                {
                    // @bend(full|half|N) — guitar bend-up, N in semitones.
                    // LILYPOND-REF: MusicXML <bend><bend-alter> semantics.
                    int? semis = markName[5..] switch
                    {
                        "half" => 1,
                        "full" => 2,
                        var s when int.TryParse(s, out int n) && n is > 0 and <= 12 => n,
                        _ => null,
                    };
                    if (semis is { } sv)
                        _articulations.Add(new ArticulationItem(
                            ArticulationType.Bend, measureIndex, itemIndex, true,
                            markSyntax.Position, _currentStaffIndex)
                        { BendSemitones = sv });
                }
                else if (markName.StartsWith("notehead."))
                {
                    // Consumed by ExtractNoteheadStyle at item creation — not a
                    // printed mark.
                }
                else if (markName.StartsWith("finger."))
                {
                    // LILYPOND-REF: lily/fingering-engraver.cc — finger event attaches to
                    // the host note. Keyed by the note's source position.
                    if (ParseFingerMark(markSyntax) is { } finger)
                        _fingeringByPosition[node.Position] = finger;
                }
                else if (markName == "text" || markName.StartsWith("text."))
                {
                    // @text("dolce")[.up/.down] — free expressive text on the host
                    // note. Rides the DynamicText pipeline as expressive text: it
                    // is NOT a dynamic level, so hairpins ignore it and MIDI is
                    // untouched. LILYPOND-REF: TextScript (LP's c^"text"/c_"text"),
                    // direction DOWN by default.
                    if (ExtractTextAnnotation(markSyntax, out bool forcedUp) is { } freeText)
                        _dynamics.Add(new DynamicItem(
                            freeText, measureIndex, itemIndex, markSyntax.Position, _currentStaffIndex)
                        {
                            IsAbove = forcedUp,
                        });
                }
                else if (MusicMarkItem.ParseMarkName(markSyntax.MarkName) is { } compoundMark
                         && (IsNoteAnchoredPedalMark(compoundMark) || IsOttavaMark(compoundMark)))
                {
                    // A compound PEDAL mark (e.g. @ped.off) or a compound OTTAVA mark
                    // (the down forms @ottava(bassa) / @quindicesima(bassa)) written ON
                    // a note. Like @ped and the plain @ottava above, anchor it to the
                    // host note's column via itemIndex/anchorTiming and — crucially —
                    // carry _currentStaffIndex so the bracket and its octave
                    // transposition land on the AUTHORING staff. Without this the
                    // statement-level handler created it with no staff (defaulting to
                    // staff 0), so on a grand staff a lower-staff 8vb was attributed to
                    // the top staff. The statement-level handler then de-dupes this by
                    // source position. Non-pedal, non-ottava compound marks (e.g.
                    // @mark.A rehearsal) are left to that handler, which extracts text.
                    // LILYPOND-REF: piano-pedal-engraver.cc / ottava-engraver.cc.
                    _musicMarks.Add(new MusicMarkItem(
                        compoundMark, measureIndex, markSyntax.Position, itemIndex, anchorTiming) { StaffIndex = _currentStaffIndex });
                }
            }
        }
    }

    /// <summary>
    /// The string payload of a <c>@text("…")</c> annotation (quotes stripped),
    /// plus whether a trailing <c>.up</c> forces it above the staff. Null when
    /// the annotation carries no string literal (e.g. a bare <c>@text</c>).
    /// </summary>
    private static string? ExtractTextAnnotation(MusicMarkSyntax mark, out bool forcedUp)
    {
        forcedUp = false;
        string? text = null;
        SyntaxTokenNode? last = null;
        for (int i = 0; i < mark.SlotCount; i++)
        {
            if (mark.GetChild(i) is not SyntaxTokenNode token)
                continue;
            if (token.Kind == SyntaxKind.StringLiteral && text == null)
            {
                var raw = token.Text;
                text = raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"'
                    ? raw[1..^1]
                    : raw;
            }
            last = token;
        }
        forcedUp = last is { Kind: SyntaxKind.Identifier, Text: "up" };
        return text;
    }

    /// <summary>
    /// True for the pedal music marks that anchor to the host note's column
    /// (the engage/release marks). Compound pedal marks like @ped.off arrive as
    /// MusicMarkSyntax note articulations and need this anchoring; other compound
    /// marks (rehearsal, etc.) are handled at the statement level instead.
    /// </summary>
    private static bool IsNoteAnchoredPedalMark(MusicMarkType type) =>
        type is MusicMarkType.SustainOn or MusicMarkType.SustainOff
             or MusicMarkType.SostenutoOn or MusicMarkType.SostenutoOff
             or MusicMarkType.UnaCordaOn or MusicMarkType.UnaCordaOff;

    /// <summary>
    /// True for the ottava music marks. The down forms (@ottava(bassa) /
    /// @quindicesima(bassa)) arrive as compound MusicMarkSyntax note articulations
    /// and, like pedals, must be anchored WITH the staff index so a grand staff's
    /// lower-staff 8vb is drawn and transposed on its own staff. (The up forms are
    /// plain @ottava / @quindicesima identifiers handled on the simple mark path.)
    /// </summary>
    private static bool IsOttavaMark(MusicMarkType type) =>
        type is MusicMarkType.OttavaUp or MusicMarkType.OttavaDown
             or MusicMarkType.QuindicesUp or MusicMarkType.QuindicesDown;

    /// <summary>
    /// Pairs trill spanner start/stop events into TrillSpannerItems.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/scheme-engravers.scm:47-85 start/stop pairing
    /// </remarks>
    private ImmutableArray<TrillSpannerItem> PairTrillSpannerEvents()
    {
        if (_trillSpannerEvents.Count == 0)
            return ImmutableArray<TrillSpannerItem>.Empty;

        var items = ImmutableArray.CreateBuilder<TrillSpannerItem>();
        (bool isStart, int measureIndex, int itemIndex, int sourcePosition, int staffIndex)? pendingStart = null;

        foreach (var evt in _trillSpannerEvents)
        {
            if (evt.isStart)
            {
                pendingStart = evt;
            }
            else if (pendingStart != null)
            {
                items.Add(new TrillSpannerItem(
                    pendingStart.Value.measureIndex,
                    pendingStart.Value.itemIndex,
                    evt.measureIndex,
                    evt.itemIndex,
                    pendingStart.Value.sourcePosition,
                    pendingStart.Value.staffIndex));
                pendingStart = null;
            }
        }

        return items.ToImmutable();
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

        // LILYPOND-REF: lily/grace-spacing-engraver.cc — grace notes carry their own durations
        // Default to eighth note if no explicit duration (LilyPond grace note default)
        Fraction graceDefaultDuration = Fraction.Eighth;

        foreach (var item in grace.Body.Items)
        {
            if (item is NoteSyntax note)
            {
                var rp = CalculateStaffPosition(note.Pitch);
                _octave.CurrentOctave = rp.RelativeOctave;
                int staffPosition = rp.StaffPosition;

                bool needsLedger = staffPosition <= -6 || staffPosition >= 6;
                var (accidental, _) = GetDisplayAccidentalWithCourtesy(rp.DisplayStep, rp.DisplayAlteration, rp.DisplayOctave);

                // Resolve grace note duration (inherit previous grace duration if not specified)
                int noteValue = note.Duration?.Value ?? (int)graceDefaultDuration.Denominator;
                var baseDuration = Fraction.FromNoteValue(noteValue);
                graceDefaultDuration = baseDuration;

                int graceMidi = PitchToMidi(rp.DisplayStep, rp.DisplayAlteration, rp.RelativeOctave);
                graceNoteInfos.Add(new GraceNoteInfo(staffPosition, accidental, needsLedger, baseDuration, graceMidi));
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
                grace.Position,
                _currentStaffIndex));
            // Hand the infos to the next main note/chord so it can reserve front space.
            _pendingLeadingGrace = infos;
        }
    }

    private NoteItem CreateNoteItem(NoteSyntax note, bool hasTieAfter = false, bool hasSlurStartAfter = false, bool hasSlurEndAfter = false, bool hasBeamStartAfter = false, bool hasBeamEndAfter = false, bool hasGlissando = false, int featherDirection = 0, bool isCue = false)
    {
        var rp = CalculateStaffPosition(note.Pitch);
        _octave.CurrentOctave = rp.RelativeOctave;
        int staffPosition = rp.StaffPosition;

        int noteValue = note.Duration?.Value ?? (int)_defaultDuration.Denominator;
        if (note.Duration != null)
            _defaultDuration = Fraction.FromNoteValue(noteValue);

        int dots = note.Duration?.DotCount ?? 0;
        bool needsLedger = staffPosition <= -6 || staffPosition >= 6;

        // Parse tremolo suffix (:8 = 1 beam, :16 = 2 beams, :32 = 3 beams)
        int tremoloBeams = ParseTremoloBeams(note.Tremolo);

        // Inside `repeat tremolo N { … }`: print ONE note at the combined
        // duration, slashes from the body's subdivision.
        if (_tremoloRepeatCount > 1
            && CombineTremoloDuration(_tremoloRepeatCount, noteValue) is { } combined)
        {
            tremoloBeams = Math.Max(tremoloBeams, (int)Math.Log2(noteValue) - 2);
            noteValue = combined.Value;
            dots = combined.Dots;
        }

        // Two-note tremolo: this note prints at the pair's total duration.
        if (_tremoloPairShape is { } pairDisp)
        {
            noteValue = pairDisp.Value;
            dots = pairDisp.Dots;
        }

        var (accidental, isCourtesy) = GetDisplayAccidentalWithCourtesy(rp.DisplayStep, rp.DisplayAlteration, rp.DisplayOctave);

        // Quarter tones always print their own accidental (they are never in
        // the key). LILYPOND-REF: quarter-tone note names ih/eh/isih/eseh.
        if (note.Pitch.QuarterOffset != 0)
        {
            accidental = QuarterToneAccidental(note.Pitch, accidental);
            isCourtesy = false;
        }

        // Check for explicit @courtesy annotation
        if (!isCourtesy && _courtesySourcePositions.Contains(note.Position))
        {
            isCourtesy = true;
            // If no accidental shown, force the key-signature-matching accidental
            if (accidental == null)
            {
                int step = rp.DisplayStep;
                int alt = GetKeySignatureAlteration(step);
                accidental = alt switch
                {
                    1 => "sharp", -1 => "flat", _ => "natural"
                };
            }
        }

        // LILYPOND-REF: lily/fingering-engraver.cc — finger event lookup at note creation.
        // CollectArticulations runs after CreateNoteItem, so we scan the note's
        // articulations directly here (mirroring HasCourtesyAnnotation's pattern).
        int? fingering = ExtractFingering(note);
        bool hasLv = HasLaissezVibrerAnnotation(note);
        bool hasRepeatTie = HasRepeatTieAnnotation(note);

        // @editorial: the accidental this note resolves to becomes a SUGGESTION
        // above the note instead of a regular accidental at its left; when the
        // note has no printed accidental, force the key-signature alteration
        // (same rule as @courtesy).
        // LILYPOND-REF: scm/define-grobs.scm:96-123 AccidentalSuggestion;
        // suggestAccidentals replaces Accidental with AccidentalSuggestion.
        string? editorialAccidental = null;
        if (HasNamedArticulation(note, "editorial"))
        {
            if (accidental != null)
            {
                editorialAccidental = accidental;
            }
            else
            {
                int step = rp.DisplayStep;
                int alt = GetKeySignatureAlteration(step);
                editorialAccidental = alt switch
                {
                    1 => "sharp", -1 => "flat", _ => "natural"
                };
            }
            accidental = null; // suggestion replaces the left-of-note accidental
        }

        return new NoteItem(
            staffPosition,
            Fraction.FromNoteValue(noteValue),
            dots,
            accidental,
            needsLedger,
            note.Position,
            tremoloBeams,
            hasTieStart: hasTieAfter,
            hasSlurStart: hasSlurStartAfter,
            hasSlurEnd: hasSlurEndAfter,
            hasBeamStart: hasBeamStartAfter,
            hasBeamEnd: hasBeamEndAfter,
            hasGlissando: hasGlissando,
            featherDirection: featherDirection,
            isCourtesy: isCourtesy,
            isCue: isCue,
            editorialAccidental: editorialAccidental,
            fingering: fingering,
            hasLaissezVibrer: hasLv,
            hasRepeatTie: hasRepeatTie)
        {
            StringNumber = ExtractStringNumber(note),
            Midi = PitchToMidi(rp.DisplayStep, rp.DisplayAlteration, rp.RelativeOctave),
            IsDead = HasNamedArticulation(note, "dead"),
            StemUpOverride = GetStemDirectionOverride(note),
        };
    }

    /// <summary>Drum note → NoteItem: placement/notehead/GM key from the
    /// DrumNameRegistry (LP drums-style table); duration semantics identical
    /// to pitched notes.</summary>
    /// <remarks>LILYPOND-REF: lily/drum-note-engraver.cc — Drum_notes_engraver
    /// reads drumStyleTable for position + style.</remarks>
    private NoteItem CreateDrumNoteItem(DrumNoteSyntax drum)
    {
        var info = DrumOverrides.Resolve(_drumOverrides, drum.DrumName);
        int noteValue = drum.Duration?.Value ?? (int)_defaultDuration.Denominator;
        if (drum.Duration != null)
            _defaultDuration = Fraction.FromNoteValue(noteValue);
        int dots = drum.Duration?.DotCount ?? 0;
        int tremoloBeams = ParseTremoloBeams(drum.Tremolo);
        bool needsLedger = info.StaffPosition <= -6 || info.StaffPosition >= 6;

        return new NoteItem(
            info.StaffPosition,
            Fraction.FromNoteValue(noteValue),
            dots,
            accidental: null,
            needsLedger,
            drum.Position,
            tremoloBeams)
        {
            Notehead = info.Notehead,
            Midi = info.GmKey,
            StemUpOverride = GetStemDirectionOverride(drum),
        };
    }

    private RestItem CreateRestItem(RestSyntax rest)
    {
        int noteValue = rest.Duration?.Value ?? (int)_defaultDuration.Denominator;
        if (rest.Duration != null)
            _defaultDuration = Fraction.FromNoteValue(noteValue);

        int dots = rest.Duration?.DotCount ?? 0;

        // 's' is a spacer rest: it occupies time/width but is never drawn (unlike 'r').
        return new RestItem(Fraction.FromNoteValue(noteValue), dots, rest.Position)
        {
            IsSpacer = rest.RestToken.Text == "s",
            // Capital R = explicit multi-measure rest (centred). Lowercase r = plain
            // rest at beat 1, even when it fills the measure.
            IsMultiMeasure = rest.RestToken.Text == "R"
        };
    }

    /// <summary>
    /// Parses tremolo suffix into beam count.
    /// :8 = 1 beam, :16 = 2 beams, :32 = 3 beams
    /// </summary>
    private static int ParseTremoloBeams(SyntaxTokenNode? tremolo)
    {
        if (tremolo == null)
            return 0;

        // Tremolo text is ":8", ":16", or ":32"
        var text = tremolo.Text;
        if (text.Length < 2 || text[0] != ':')
            return 0;

        return text[1..] switch
        {
            "8" => 1,
            "16" => 2,
            "32" => 3,
            _ => 0
        };
    }

    private ChordItem CreateChordItem(ChordSyntax chord, bool hasBeamStartAfter = false, bool hasBeamEndAfter = false, bool hasArpeggio = false, bool isCue = false, bool hasTieAfter = false, bool hasSlurStartAfter = false, bool hasSlurEndAfter = false)
    {
        var notes = new List<ChordNoteInfo>();

        // Track first note's state for subsequent chord/note relative calculation
        int firstOctave = _octave.CurrentOctave;
        char firstPitchName = _octave.LastPitchName;

        foreach (var pitch in chord.Pitches)
        {
            var rp = CalculateStaffPosition(pitch);
            _octave.CurrentOctave = rp.RelativeOctave;
            int staffPosition = rp.StaffPosition;

            // Remember first pitch's state (original octave drives the relative chain)
            if (notes.Count == 0)
            {
                firstOctave = rp.RelativeOctave;
                firstPitchName = pitch.PitchName.ToLowerInvariant()[0];
            }

            var (accidental, isCourtesy) = GetDisplayAccidentalWithCourtesy(rp.DisplayStep, rp.DisplayAlteration, rp.DisplayOctave);

            // Quarter tones always print their own accidental (never in the key).
            if (pitch.QuarterOffset != 0)
            {
                accidental = QuarterToneAccidental(pitch, accidental);
                isCourtesy = false;
            }

            bool needsLedger = staffPosition <= -6 || staffPosition >= 6;

            // LILYPOND-REF: lily/fingering-engraver.cc — per-pitch finger via <c@finger.N>.
            int? pitchFingering = ExtractPitchFingering(pitch);

            notes.Add(new ChordNoteInfo(
                staffPosition, accidental, needsLedger,
                IsCourtesy: isCourtesy,
                Fingering: pitchFingering,
                StringNumber: pitch.Articulations.OfType<StringNumberAnnotationSyntax>().FirstOrDefault()?.StringNumber,
                Midi: PitchToMidi(rp.DisplayStep, rp.DisplayAlteration, rp.RelativeOctave)));
        }

        // Drum chord members (<bd hh>): placement/head/GM key from the
        // registry, mixed freely with pitched members.
        foreach (var drum in chord.DrumNames)
        {
            var dinfo = DrumOverrides.Resolve(_drumOverrides, drum.DrumName);
            notes.Add(new ChordNoteInfo(
                dinfo.StaffPosition, null,
                dinfo.StaffPosition is <= -6 or >= 6,
                Notehead: dinfo.Notehead,
                Midi: dinfo.GmKey));
        }

        // Next chord/note is relative to first pitch of this chord (Lilypond spec)
        _octave.CurrentOctave = firstOctave;
        _octave.LastPitchName = firstPitchName;

        int noteValue = chord.Duration?.Value ?? (int)_defaultDuration.Denominator;
        if (chord.Duration != null)
            _defaultDuration = Fraction.FromNoteValue(noteValue);

        int dots = chord.Duration?.DotCount ?? 0;
        int tremoloBeams = ParseTremoloBeams(chord.Tremolo);

        // Inside `repeat tremolo N { … }` (see CreateNoteItem).
        if (_tremoloRepeatCount > 1
            && CombineTremoloDuration(_tremoloRepeatCount, noteValue) is { } combined)
        {
            tremoloBeams = Math.Max(tremoloBeams, (int)Math.Log2(noteValue) - 2);
            noteValue = combined.Value;
            dots = combined.Dots;
        }

        return new ChordItem(notes.ToImmutableArray(), Fraction.FromNoteValue(noteValue), dots, chord.Position, tremoloBeams, hasBeamStartAfter, hasBeamEndAfter, hasArpeggio, isCue, hasTieStart: hasTieAfter, hasSlurStart: hasSlurStartAfter, hasSlurEnd: hasSlurEndAfter);
    }

    /// <summary>
    /// A pitch resolved for rendering. <see cref="RelativeOctave"/> is the
    /// ORIGINAL (written) octave that drives the relative-octave chain for the
    /// next note; the Display* fields are what is actually drawn — equal to the
    /// written pitch, or its transposition when the part has a transpose option.
    /// </summary>
    private readonly record struct ResolvedPitch(
        int StaffPosition, int RelativeOctave, int DisplayStep, int DisplayAlteration, int DisplayOctave);

    /// <summary>One entry in the resolved-pitch trace: the source position of the
    /// written pitch and its resolved absolute spelling (e.g. "C6").</summary>
    public readonly record struct PitchTraceEntry(int Position, string Pitch);

    /// <summary>
    /// Ottava spans (measure range + type) for one staff, derived from the
    /// collected @ottava/@loco marks. Reuses the SAME detector the bracket uses
    /// at layout time, so the display transposition and the drawn bracket stay in
    /// lockstep. The detector is a pure function of the marks (no layout geometry).
    /// </summary>
    private List<OttavaBracketItem> DetectOttavaSpans(int staffIndex)
    {
        var result = new List<OttavaBracketItem>();
        foreach (var b in Layout.OttavaBracketEngraver.DetectOttavaBrackets(_musicMarks.ToImmutableArray()))
            if (b.StaffIndex == staffIndex)
                result.Add(b);
        return result;
    }

    private ResolvedPitch CalculateStaffPosition(PitchSyntax pitch)
    {
        char pitchName = pitch.PitchName.ToLowerInvariant()[0];
        int step = GetPitchIndex(pitchName);

        // Absolute mode: '/, are offsets from a fixed C4 anchor (bare c = C4),
        // stateless — every note is independent. Relative mode (default): the
        // closest-octave rule + explicit '/, offset, shared with the exporters.
        // The relative chain runs on the ORIGINAL pitches; transpose is applied
        // afterwards, so a transposed part still resolves octaves from what the
        // user wrote.
        int actualOctave = _octave.Resolve(step, pitch.OctaveOffset, pitchName);

        // Display pitch = written pitch, transposed if the part has transpose:.
        var (dStep, dAlt, dOctave) = _octave.TransposePitch(step, pitch.AccidentalOffset, actualOctave);

        // Staff position 0 = middle line of the staff.
        //   Treble: B4   Bass: D3   Alto: C4 (middle line)   Tenor: A3
        // The C clefs differ — alto puts middle C on the middle line, tenor on
        // the 4th line (so the middle line is A3, a third lower). Without their
        // own cases both fell through to the treble default and rendered alike.
        int basePosition = _clef switch
        {
            "treble" or "treble_8" or "treble^8" => dStep - GetPitchIndex('b') + (dOctave - 4) * 7,
            "bass" or "bass_8" => dStep - GetPitchIndex('d') + (dOctave - 3) * 7,
            "alto" or "percussion" => dStep - GetPitchIndex('c') + (dOctave - 4) * 7,
            "tenor" => dStep - GetPitchIndex('a') + (dOctave - 3) * 7,
            // C clefs on other lines: middle line = G4 (soprano), E4 (mezzo), F3 (baritone).
            "soprano" => dStep - GetPitchIndex('g') + (dOctave - 4) * 7,
            "mezzosoprano" => dStep - GetPitchIndex('e') + (dOctave - 4) * 7,
            "baritone" => dStep - GetPitchIndex('f') + (dOctave - 3) * 7,
            _ => dStep - GetPitchIndex('b') + (dOctave - 4) * 7
        };

        // RelativeOctave keeps the ORIGINAL octave for the next note's chain.
        _pitchTrace.Add(new PitchTraceEntry(pitch.Position, FormatPitch(dStep, dAlt, dOctave)));
        return new ResolvedPitch(basePosition, actualOctave, dStep, dAlt, dOctave);
    }

    /// <summary>Formats a resolved pitch as a letter + accidental + octave number
    /// (C4 = middle C), e.g. "C4", "F#5", "Bb3", "Cx4" (double sharp).</summary>
    private static string FormatPitch(int step, int alteration, int octave)
    {
        char letter = "CDEFGAB"[((step % 7) + 7) % 7];
        string acc = alteration switch
        {
            >= 2 => "x",   // double sharp
            1 => "#",
            -1 => "b",
            <= -2 => "bb",
            _ => ""
        };
        return $"{letter}{acc}{octave}";
    }

    private static int GetPitchIndex(char pitch) => RelativeOctave.StepIndex(pitch);

    // internal (not private) so the F3 MeasureContext chain can map the
    // score-level initial clef string to a ClefType with the SAME canonical
    // table the collector uses (no duplicate clef map). Visibility only.
    internal static ClefType ParseClefType(string clef) => clef switch
    {
        "bass" => ClefType.Bass,
        "alto" => ClefType.Alto,
        "tenor" => ClefType.Tenor,
        "treble_8" => ClefType.Treble8Below,
        "treble^8" => ClefType.Treble8Above,
        "soprano" => ClefType.Soprano,
        "mezzosoprano" => ClefType.MezzoSoprano,
        "baritone" => ClefType.Baritone,
        "bass_8" => ClefType.Bass8Below,
        "percussion" => ClefType.Percussion,
        _ => ClefType.Treble
    };

    internal static BarlineType ParseBarlineType(string text) => text switch
    {
        "|:" => BarlineType.RepeatStart,
        ":|" => BarlineType.RepeatEnd,
        ":|:" => BarlineType.RepeatBoth,
        "||" => BarlineType.Double,
        "|." => BarlineType.Final,
        "!" => BarlineType.Dashed,
        _ => BarlineType.Single
    };

}
