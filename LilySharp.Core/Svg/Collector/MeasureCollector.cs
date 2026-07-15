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
/// A section's plain (unbracketed) lyric verse that is fully shadowed by the section's
/// <c>[N. …]</c> verses: every written-out occurrence already has a numbered verse, so
/// the plain line — which only fills an occurrence NO bracket covers — never renders.
/// <see cref="Span"/> anchors the first shadowed plain syllable.
/// </summary>
public record ShadowedPlainLyricWarning(
    LilySharp.Core.Syntax.TextSpan Span,
    string SectionName
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

/// <summary>A navigation mark (segno/coda/D.S./…) written mid-measure rather than at a
/// barline boundary — an unusual placement worth flagging.</summary>
public record NavigationMarkPlacementWarning(int SourcePosition, string MarkText);

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

    // When a 'partial N' shortens the next measure to a pickup, the meter to
    // restore once that measure closes is parked here. LILYPOND-REF:
    // ly/music-functions-init.ly:1670-1678 — \partial sets measurePosition for one
    // measure; the normal measureLength resumes afterwards.
    private Fraction? _partialRestore;

    private BarlineType _pendingStartBarline = BarlineType.None;
    private BarlineType _pendingEndBarline = BarlineType.None;
    private bool _pendingBreak = false;
    private bool _pendingNoBreak = false;
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

    /// <summary>The auto-complete measure length currently in force (the running
    /// meter). A section boundary reads this to decide whether reverting to the
    /// score meter needs a redrawn time signature.</summary>
    public Fraction CurrentMeasureLength => _timeSignature;

    /// <summary>Current measure index (completed measures count).</summary>
    public int CurrentMeasureIndex => _measures.Count;

    /// <summary>Current item count within the current measure.</summary>
    public int CurrentItemCount => _currentItems.Count;

    /// <summary>True at a measure boundary: no items yet in the current measure (just
    /// after a barline, or the very start), or the measure is already full (a mark
    /// written right before its barline). A navigation landmark belongs at such a
    /// boundary; anything else is mid-measure.</summary>
    public bool AtMeasureBoundary => _currentItems.Count == 0 || _currentDuration == _timeSignature;

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
            // Collapse a section reset immediately followed by the section's own
            // `time`: keep the last meter so two time signatures don't overprint.
            if (_currentItems.Count > 0 && _currentItems[^1] is TimeSignatureChangeItem)
                _currentItems[^1] = item;
            else
                _currentItems.Add(item);
            return;
        }

        // Collapse consecutive key changes at the same measure start — a section
        // boundary reset (revert to the score key) immediately followed by the
        // section's own `key`. Draw ONE change from the ORIGINAL previous key to the
        // FINAL new key, or nothing if the net key is unchanged; otherwise the two
        // signatures (e.g. a cancel-natural and the new flat) overprint.
        if (item is KeySignatureChangeItem kc
            && _currentItems.Count > 0 && _currentItems[^1] is KeySignatureChangeItem prevKc)
        {
            var merged = new KeySignatureChangeItem(kc.NewKey, prevKc.PreviousKey, kc.SourcePosition);
            if (merged.NewKey == merged.PreviousKey)
                _currentItems.RemoveAt(_currentItems.Count - 1); // net no change
            else
                _currentItems[^1] = merged;
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

        return duration;
    }

    /// <summary>
    /// Auto-completes the current measure at a duration boundary (no explicit
    /// barline written): the end barline defaults to a single bar and the recorded
    /// boundary is non-explicit.
    /// </summary>
    private void AutoCompleteMeasure(int sourceEnd)
        => EmitMeasure(sourceEnd, BarlineType.Single, explicitBar: false);

    /// <summary>
    /// Emits the current measure and resets per-measure state. Shared by the
    /// duration-boundary (auto) and explicit-barline paths, which differ only in
    /// the default end barline and whether the boundary is marked explicit.
    /// A no-op when no items are pending (so <c>_pendingBreak</c> survives to the
    /// next real measure).
    /// </summary>
    private void EmitMeasure(int sourceEnd, BarlineType endType, bool explicitBar)
    {
        if (_currentItems.Count == 0)
            return;

        bool isAligned = _currentDuration == _timeSignature;
        bool hasBreak = _pendingBreak;
        bool noBreak = _pendingNoBreak;
        _pendingBreak = false;
        _pendingNoBreak = false;

        _measures.Add(new Measure(
            _currentItems.ToImmutableArray(),
            _pendingStartBarline,
            _pendingEndBarline != BarlineType.None ? _pendingEndBarline : endType,
            _sectionLabel,
            _measureSourceStart,
            sourceEnd,
            hasBreakAfter: hasBreak,
            // `nobreak` forbids a break after this measure (Force wins if both).
            lineBreakPermission: noBreak ? Layout.BreakPermission.Forbid : Layout.BreakPermission.Allow,
            sectionLabelPosition: _sectionLabelPosition,
            isPickup: _partialRestore != null));

        _boundaries.Add(new MeasureBoundary(
            sourceEnd,
            _currentDuration,
            IsExplicit: explicitBar,
            IsAligned: isAligned));

        _currentItems.Clear();
        _sectionLabel = null;
        _sectionLabelPosition = 0;
        _pendingStartBarline = BarlineType.None;
        _pendingEndBarline = BarlineType.None;
        _measureSourceStart = sourceEnd;

        // BY DESIGN: Lily# is "explicit over implicit" (no hidden state) and does
        // NOT auto-split a note across a barline the way LilyPond does. A note that
        // overruns the meter makes an OVERFULL measure, which is a user error that
        // MeasureValidator flags ("Measure duration exceeds time signature"); the
        // fix is to write an explicit tie (c4 d e f4~ | f4 …). The renderer draws
        // every note as written (nothing is lost — the bar is simply drawn wide)
        // and resets the beat counter so the following (clean) measures stay
        // aligned. So the excess beat count is intentionally dropped here, not
        // carried into a tied continuation.
        _currentDuration = Fraction.Zero;
        RestorePartialIfPending();
        MeasureCompleted?.Invoke();
    }

    public void SetBreak()
    {
        if (_currentItems.Count == 0 && _measures.Count > 0)
        {
            // At measure boundary - apply break to previous measure. `with`
            // preserves break penalty and page/turn permissions; the old
            // full rebuild silently reset them to defaults.
            var last = _measures[^1];
            _measures[^1] = last with { LineBreakPermission = Layout.BreakPermission.Force };
        }
        else
        {
            // Mid-measure break - defer to next measure boundary
            _pendingBreak = true;
        }
    }

    /// <summary>Forbids a line break after this measure (<c>nobreak</c>, LP's
    /// <c>\noBreak</c>) — the mirror of <see cref="SetBreak"/>.</summary>
    public void SetNoBreak()
    {
        if (_currentItems.Count == 0 && _measures.Count > 0)
            _measures[^1] = _measures[^1] with { LineBreakPermission = Layout.BreakPermission.Forbid };
        else
            _pendingNoBreak = true;
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
            _measures[^1] = lastMeasure with { EndBarline = endType };
        }
    }

    /// <summary>
    /// Closes the current measure at an EXPLICIT barline with the given end
    /// barline type.
    /// </summary>
    private void CompleteMeasure(int sourceEnd, BarlineType endType)
        => EmitMeasure(sourceEnd, endType, explicitBar: true);


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
    // The render voice number (1-based) when the walk is INSIDE a `voice {}` block, so an
    // override there scopes to that voice; null in the main stream (staff-scoped). Set
    // around each parallel sub-voice's processing (voice 0 in ProcessMusicNode, the extras
    // in BuildExtraVoiceTracks).
    private int? _currentVoiceScope;
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

    // The grob-override state a section boundary reverts to (the grob analogue of
    // _sectionResetClef, but a SET): the part-default values — global + this voice's
    // part-body overrides — snapshotted at collection start. Section-internal overrides
    // reset to this at each boundary, so they never leak into the next section.
    private readonly Dictionary<(string Grob, string Prop), string> _sectionResetOverrides = new();
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

    // Piece-level metadata (title/composer/tempo/time/key/clef + header source
    // positions) grouped into one owner. See MetadataState.
    private readonly MetadataState _meta = new();
    // Active `repeat tremolo N { … }` transform: the body note prints ONCE at
    // the combined duration with the subdivision's stem slashes.
    private int _tremoloRepeatCount = 1;
    // Active two-note tremolo: (display value, display dots, between-beams);
    // both notes print at the pair's TOTAL duration and sound half (TimeScale ½).
    private (int Value, int Dots, int Beams)? _tremoloPairShape;
    private bool _tremoloPairFirst;
    private Dictionary<string, DrumInfo>? _drumOverrides; // drummap { } per-score
    // measure -> (tonic step, sharps) at each key change, so a chord's Roman degree
    // follows the key in force at its bar (a mid-piece modulation re-bases the degrees).
    private readonly SortedDictionary<int, (int TonicStep, int Sharps)> _keyByMeasure = new();
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
    private ScoreContent CaptureScoreContent() => new(
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
        PairTrillSpannerEvents(),
        new HeaderPositions(_meta.TitlePosition, _meta.ComposerPosition, _meta.TimePosition, _meta.KeyPosition, _meta.ClefPosition),
        _meta.TempoText,
        _meta.TempoBeatUnit,
        _meta.TempoDots,
        _meta.TextFont,
        _meta.EmbedFont);

    /// <summary>
    /// Collects a Score from a syntax tree.
    /// </summary>
    public Score Collect(SyntaxTree tree, string? voiceName = null,
        FormDeclarationSyntax? localForm = null,
        string? attachedChordPart = null,
        ChordDisplayMode attachedChordDisplay = ChordDisplayMode.Names)
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
            CollectPartBodyOverrides(tree.GetRoot(), voiceName, _currentStaffIndex);
            var (partClef, partOctave, partTranspose, partClefPos) = GetPartDefaults(tree.GetRoot(), voiceName);
            if (partClef != null)
                _meta.Clef = partClef;
            _meta.ClefPosition = partClefPos;
            _octave.CurrentOctave = partOctave ?? InstrumentDefaults.GetDefaultOctave(ParseClefType(_meta.Clef));
            _octave.OctaveBase = partOctave ?? 4;
            ApplyTranspose(partTranspose);
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
        var measures = CollectMeasures();
        ResolveBeamStemDirections(measures);

        // If any parallel span was seen, reconstruct the additional voices.
        // Pass the attached chord part through: BuildMultiVoiceScore collects it
        // itself (this method's CollectAttached below is never reached for the
        // multi-voice path).
        if (_parallelSpans.Count > 0)
            return BuildMultiVoiceScore(measures, tree.GetRoot(), attachedChordPart, attachedChordDisplay);

        // Single voice
        var voice = _tabResolver.ResolveVoiceTabTies(new Voice(_voiceName ?? "default", measures.ToImmutableArray()));

        // Ottava DISPLAY transposition: notes under an 8va draw an octave lower
        // (etc.) while sounding at their written pitch. Single-staff score, so
        // every ottava mark is on staff 0. See OttavaTransposer.
        voice = OttavaTransposer.Transpose(voice, DetectOttavaSpans(0));

        // Collect lyrics
        _lyricsCollector.CollectNoteBound(tree.GetRoot(), measures, _lyricsRowNames, _voiceMeasuresByName, _sectionState.StartMeasure, _sectionState.AllStarts);
        _chordNameCollector.KeyByMeasure = BuildKeyTimeline();
        _chordNameCollector.SectionStarts = _sectionState.AllStarts;
        _chordNameCollector.CollectBlocks(tree.GetRoot(), _sectionState.StartMeasure, _currentStaffIndex);
        // `staff NAME with chords CHORDPART [as roman|both]` on a single-staff score.
        if (attachedChordPart != null)
            _chordNameCollector.CollectAttached(
                tree.GetRoot(), attachedChordPart, _sectionState.StartMeasure, _currentStaffIndex,
                attachedChordDisplay);

        return ScoreAssembler.BuildScore(voice, CaptureScoreContent());
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
        // This score renders its bound form (resolved by name in the RenderSpec).
        // Fall back to the primary form only when the reference is unresolved (a
        // validator error) so a typo still previews something.
        _form = renderSpec.Form ?? _form;
        _meta.InitialKeySharps = _meta.KeySharps; // Preserve initial key before music processing
        _meta.InitialKeyCustom = _meta.KeyCustom;
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
        var attachedChords = new List<(string PartName, int StaffIndex, ChordDisplayMode Mode)>();
        // Chord rows are also deferred (see the ChordRowSpec branch below).
        var pendingChordRows = new List<(string Name, int StaffIndex, ChordDisplayMode Mode)>();
        foreach (var (voiceName, withChords, chordDisplay) in renderSpec.GetVoiceBindings())
        {
            _voiceName = voiceName;
            _currentStaffIndex = collectStaffIndex++;
            _octave.LastPitchName = 'c';
            _defaultDuration = Fraction.Quarter;

            // Part-body grob defaults (`part <voice> { override … }`) scope to this staff.
            CollectPartBodyOverrides(tree.GetRoot(), voiceName, _currentStaffIndex);

            // `staff NAME with chords CHORDPART [as roman|both]`: remember the
            // attachment (and its display); the chord symbols are collected AFTER
            // the voice loop, once every section's start measure is registered.
            if (withChords != null)
                attachedChords.Add((withChords, _currentStaffIndex, chordDisplay));

            // An independent chord row (`chords name [as roman|both]` in the score).
            // Defer its collection until AFTER the music voices: the section start
            // table fills while music is processed, and a row spec listed first (or a
            // rows-only score) would otherwise collect every section's block from bar
            // 0, overprinting them.
            if (renderSpec.Items.OfType<ChordRowSpec>().Any(c => c.PartName == voiceName))
            {
                pendingChordRows.Add((voiceName, _currentStaffIndex, chordDisplay));
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
            _meta.Clef = partClef ?? "treble";
            _meta.ClefPosition = partClefPos;

            // Set initial octave: explicit > instrument default > clef default
            _octave.CurrentOctave = partOctave ?? InstrumentDefaults.GetDefaultOctave(ParseClefType(_meta.Clef));
            _octave.InitialOctave = _octave.CurrentOctave;
            _octave.OctaveBase = partOctave ?? 4;
            _octave.OctaveAbsolute = _octave.InitialOctaveAbsolute; // restore file-level octave mode
            ApplyTranspose(partTranspose);

            // Re-arm this voice's running key from the written initial key,
            // transposed by THIS part's option, so the accidental engine
            // suppresses in-key accidentals correctly and the key does not leak
            // between voices.
            _meta.KeySharps = _octave.TransposeKeySharps(_meta.InitialKeySharps);
            if (_octave.HasTranspose)
                voiceKeyDict[voiceName] = new KeySignature(_meta.KeySharps);

            staffVoices[voiceName] = CollectStaffVoices(voiceName);
        }

        // Rows collect AFTER the music. A rows-only score has no music to
        // register the section starts — derive them from the row blocks.
        if (pendingChordRows.Count > 0 || pendingLyricsRows.Count > 0)
            EnsureSectionStartsForRows();
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
                var rowMeasures = _lyricsCollector.CollectRow(
                    tree.GetRoot(), name, idx, wrapBars, _sectionState.StartMeasure, _meta.TimeBeats, _meta.TimeBeatType);
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
            _lyricsCollector.CollectNoteBound(tree.GetRoot(), firstStaffVoices[0].Measures.ToList(), _lyricsRowNames, _voiceMeasuresByName, _sectionState.StartMeasure, _sectionState.AllStarts);
        }
        _chordNameCollector.KeyByMeasure = BuildKeyTimeline();
        _chordNameCollector.SectionStarts = _sectionState.AllStarts;
        _chordNameCollector.CollectBlocks(tree.GetRoot(), _sectionState.StartMeasure, _currentStaffIndex);
        foreach (var (attachedPart, attachedStaff, attachedMode) in attachedChords)
            _chordNameCollector.CollectAttached(
                tree.GetRoot(), attachedPart, _sectionState.StartMeasure, attachedStaff, attachedMode);

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
                        ? st with { Voices = st.Voices.SetItem(0, _tabResolver.ResolveTabStrings(st.PrimaryVoice, st.Tuning.Value, st.TabSourceClef, st.Transposition)) }
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

        return ScoreAssembler.BuildMultiStaffScore(staffGroups, CaptureScoreContent());
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
            voice, new TimeSignature(_meta.TimeBeats, _meta.TimeBeatType, _meta.TimeBeatsText, _meta.TimeSenzaMisura), _tupletBrackets.ToImmutableArray());

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
                    hasBreakAfter: measure.HasBreakAfter,
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
    private Score BuildMultiVoiceScore(List<Measure> track0, SyntaxNode root,
        string? attachedChordPart = null,
        ChordDisplayMode attachedChordDisplay = ChordDisplayMode.Names)
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
        _lyricsCollector.CollectNoteBound(root, track0, _lyricsRowNames, _voiceMeasuresByName, _sectionState.StartMeasure, _sectionState.AllStarts);
        _chordNameCollector.KeyByMeasure = BuildKeyTimeline();
        _chordNameCollector.SectionStarts = _sectionState.AllStarts;
        _chordNameCollector.CollectBlocks(root, _sectionState.StartMeasure, _currentStaffIndex);
        // `staff NAME with chords CHORDPART [as roman|both]` on a multi-voice single
        // staff — collected here (after CollectBlocks, matching the single-voice order),
        // because Collect's own CollectAttached is skipped by the multi-voice early return.
        if (attachedChordPart != null)
            _chordNameCollector.CollectAttached(
                root, attachedChordPart, _sectionState.StartMeasure, _currentStaffIndex,
                attachedChordDisplay);

        // A single-staff score surfaces the same annotations whether it has one
        // voice or several — a multi-voice (voice { } blocks) score keeps its chord
        // names / percent repeats, which the old construction here silently dropped.
        return ScoreAssembler.BuildScore(voices.ToImmutableArray(), CaptureScoreContent());
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
                EnterDefaultFrame();

                // Per-note metadata in this sub-voice is keyed by its local 0-based
                // measure index; shift it to the span's real start so dynamics etc.
                // land in the right measure.
                _metadataMeasureOffset = start;
                // Tag this sub-voice's tuplets with its voice index so their
                // beam-breaking boundaries never leak into a sibling voice.
                _currentVoiceIndex = t;
                // Render voice number is t+1 — an override in this sub-voice scopes to it.
                _currentVoiceScope = t + 1;
                // No auto-final barline: this is a SPAN inside the piece, and a
                // Final stamped on the span's last measure would win the
                // cross-voice barline merge and print a final barline mid-piece.
                var sub = CollectMeasuresFromNode(blocks[t], autoFinalBarline: false,
                    applyFilePartial: start == 0);
                ResolveBeamStemDirections(sub);
                _currentVoiceScope = null;
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
                || IsInsideGrace(node) || IsInsideInlineVolta(node) || node.IsInside<ArpeggioSyntax>())
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
            // Skip nodes inside a container expression (they travel as one
            // wrapper) — EXCEPT parallel: the per-voice path flattens << \\ >>
            // (see GatherMusicNode), so its descendants must reach the walk.
            if (IsInsideTuplet(node) || IsInsideRepeat(node) || IsInsideGrace(node)
                || IsInsideInlineVolta(node) || IsInsideOnce(node) || node.IsInside<ArpeggioSyntax>())
                continue;

            GatherMusicNode(node, musicNodes);
        }

        ProcessMusicNodeSequence(musicNodes, builder);

        FinalizeInlineVoltas();

        return builder.FinalizeMeasures(autoFinalBarline);
    }

    private void Reset()
    {
        _sectionState.Reset();
        _variables.Clear();
        _dynamics.Clear();
        _currentStaffIndex = 0;
        _currentVoiceIndex = 0;
        _currentVoiceScope = null;
        _articulations.Clear();
        _graceNotes.Clear();
        _arpeggios.Clear();
        _figuredBasses.Clear();
        _chordNameCollector.Clear();
        _percentRepeats.Clear();
        _crossStaffItems.Clear();
        _grobOverrides.Clear();
        _grobReverts.Clear();
        _sectionResetOverrides.Clear();
        _sectionActiveGrobProps.Clear();
        _keyByMeasure.Clear();
        _voiceMeasuresByName.Clear();
        _trillSpannerEvents.Clear();
        _courtesySourcePositions.Clear();
        _measureAccidentals.Clear();
        _fingeringByPosition.Clear();
        // Reused-instance hygiene: without these, a second Collect/CollectMultiStaff
        // on the same collector would carry a stale part-major cell map and lyric-row
        // names, and PitchTrace would grow without bound. (All current callers use a
        // fresh instance, so this only matters for reuse via the public API.)
        _pitchTrace.Clear();
        _lyricsRowNames = new();
        _form = null;
        _filePartial = null;
        _root = null;
        _octave.ResetAll();
        _defaultDuration = Fraction.Quarter;
        _meta.Reset();
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
    private void EnterPhraseTranspose()
    {
        var saved = _octave.GetTranspose();
        _phraseTransposeSaves.Push(saved);
        if (PhraseTransposeTarget() is { } phrase)
            _octave.SetTranspose(ComposeTranspose(phrase, saved));
    }

    /// <summary>Restores the transpose saved by <see cref="EnterPhraseTranspose"/>.</summary>
    private void ExitPhraseTranspose()
    {
        if (_phraseTransposeSaves.Count > 0)
            _octave.SetTranspose(_phraseTransposeSaves.Pop());
    }

    /// <summary>
    /// Collects a part's body-level grob directives (<c>part melody { override … }</c>) as
    /// staff-scoped defaults at (0,0): they apply to this part's staff for the whole part,
    /// persisting across its sections. Only DIRECT children of the part declaration are
    /// taken (a directive inside a section is walked as music instead). Runs once per part
    /// during the voice loop, where the staff index is known.
    /// </summary>
    private void CollectPartBodyOverrides(SyntaxNode root, string partName, int staffIndex)
    {
        foreach (var partDecl in root.DescendantNodes().OfType<PartDeclarationSyntax>())
        {
            if (partDecl.Name.Text != partName)
                continue;
            foreach (var node in partDecl.DescendantNodes())
            {
                if (node.Parent != partDecl)
                    continue; // direct children only; section-internal directives are walked
                // Only a plain `override` is a valid part default; `revert` / `once` in a
                // part header are positional and meaningless (flagged by the validator).
                if (node is OverrideDeclarationSyntax od)
                    CollectOverride(od, 0, 0, isOnce: false, staffIndex: staffIndex);
            }
        }
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
                {
                    // Join ALL value tokens — a hyphenated preset ("electric-bass")
                    // is word+minus+word in the green tree, so child(2) alone is just
                    // "electric" and would fall through to the default treble clef.
                    var texts = new List<string>();
                    for (int vi = 2; vi < prop.SlotCount; vi++)
                        if (prop.GetChild(vi) is SyntaxTokenNode vt)
                            texts.Add(vt.Text);
                    instrument = InstrumentDefaults.SplitInstrument(texts).Preset.ToLowerInvariant();
                }
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

                case FontDeclarationSyntax font:
                    // `font "NAME" [embedded]` sets the text font-family for all
                    // non-music text; the embedded flag is collected but unused this
                    // phase (font embedding is deferred to a later phase).
                    _meta.TextFont = font.FontName;
                    _meta.EmbedFont = font.Embedded;
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
                        _meta.TimeBeats = timeSig.Beats;
                        _meta.TimeBeatsText = timeSig.BeatsText;
                        _meta.TimeSenzaMisura = timeSig.IsSenzaMisura;
                        _meta.TimeBeatType = timeSig.BeatType;
                        _meta.TimePosition = timeSig.Span.Start;
                    }
                    break;

                case KeySignatureSyntax key:
                    // Only process top-level key declarations (not inside phrases/sections)
                    if (!IsInsideMusicContent(key))
                    {
                        _meta.KeySharps = key.IsCustom ? 0 : CalculateKeySharps(key);
                        if (!key.IsCustom)
                        {
                            _meta.KeyTonicStep = Math.Max(0,
                                LilySharp.Core.Music.KeySpelling.StepOf(key.Pitch.PitchName[0]));
                            _meta.KeyTonicAlter = key.Pitch.AccidentalOffset;
                        }
                        _meta.KeyCustom = key.IsCustom
                            ? KeySignature.EncodeCustom(key.CustomAlterations)
                            : null;
                        _meta.KeyPosition = key.Span.Start;
                    }
                    break;

                case ClefDeclarationSyntax clef:
                    _meta.Clef = clef.ClefName.Text.ToLowerInvariant();
                    _meta.ClefPosition = clef.ClefName.Span.Start;
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
                    // A section INSIDE a `chords` / `lyrics` block is that track's cell,
                    // not a structure section: it must not become a structure
                    // ordering/label rep or a part cell (its body is chord entries or
                    // syllables, not music). The chord/lyric collectors read it via
                    // ChordPartBlockSyntax.Sections / LyricsBlockSyntax.Sections.
                    if (IsInsidePartMajorTrack(section))
                        break;
                    // First declaration of a name wins as the order/label
                    // representative (source order), so a name appearing in both
                    // forms stays stable.
                    if (!_sectionState.Sections.ContainsKey(section.SectionName))
                        _sectionState.Sections[section.SectionName] = section;
                    // Part-major: an inner section binds its music to the part it
                    // lives in. Record the (section, part) cell for voice lookup.
                    var owningPart = EnclosingPartName(section);
                    if (owningPart != null)
                        _sectionState.PartMajorCells[(section.SectionName, owningPart)] = section;
                    // A section that carries its own key / time / tempo but no inline
                    // music applies those to every part of the section: section-major
                    // (`section A { key g major  melody { … } }`) or a standalone
                    // part-major header (`section A { key g major }`). An inline-music
                    // section walks the directives as music, so it is excluded to avoid a
                    // double application. First one wins.
                    if (!SectionHasInlineMusic(section))
                    {
                        var nm = section.SectionName;
                        if (FirstDirect<KeySignatureSyntax>(section) is { } hk && !_sectionHeaderKeys.ContainsKey(nm))
                            _sectionHeaderKeys[nm] = hk;
                        if (FirstDirect<TimeSignatureSyntax>(section) is { } ht && !_sectionHeaderTimes.ContainsKey(nm))
                            _sectionHeaderTimes[nm] = ht;
                        if (FirstDirect<TempoDeclarationSyntax>(section) is { } htp && !_sectionHeaderTempos.ContainsKey(nm))
                            _sectionHeaderTempos[nm] = htp;
                        if (FirstDirect<PartialDeclarationSyntax>(section) is { } hp && !_sectionHeaderPartials.ContainsKey(nm))
                            _sectionHeaderPartials[nm] = hp;
                    }
                    break;

                case FormDeclarationSyntax form:
                    // A score binds its form by name (from the RenderSpec). When a
                    // path doesn't specify one (single-staff Collect, exporters),
                    // fall back to the PRIMARY form: `main` if present, else the
                    // first declared. (`main` is matched case-sensitively.)
                    if (form.NameText == "main" || _form == null)
                        _form = form;
                    break;

                case VariableDeclarationSyntax varDecl:
                    _variables[varDecl.Name.Text] = varDecl.Expression;
                    break;

                case PhraseDeclarationSyntax phraseDecl:
                    _variables[phraseDecl.Name.Text] = phraseDecl.Body;
                    break;
            }
        }

        // A STRUCTURED file (a form or sections) can carry a top-level override / revert /
        // once (grammar §2.1 lists them as TopLevelItems). Such a directive sits OUTSIDE
        // the music stream — the per-voice walk runs through sections and never reaches a
        // root-level directive — so it is a document-wide default: seed it here at the
        // first item (measure 0, item 0) so it is active from the first note of every
        // voice. A BARE-music file has no such outer scope: there the overrides ARE the
        // music stream (a mid-stream override's position matters) and the fallback walk in
        // CollectMeasures collects them, so this is skipped to avoid double-counting.
        if (_form != null || _sectionState.Sections.Count > 0)
        {
            foreach (var node in root.DescendantNodes())
            {
                if (node.Parent != root)
                    continue; // only true top-level items; in-section overrides are walked
                // Only a plain `override` is a valid global default. `revert` / `once` here
                // are positional and have no effect at the structural top level — flagged by
                // RevertContextValidator — so they are not collected.
                if (node is OverrideDeclarationSyntax od)
                    CollectOverride(od, 0, 0, isOnce: false, staffIndex: null); // global = all staves
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
                    _meta.Title = titleToken.Text.Trim('"');
                    _meta.TitlePosition = titleToken.Span.Start;
                }
                break;
            case "composer":
                if (values.Count > 0 && values[0] is SyntaxTokenNode composerToken)
                {
                    _meta.Composer = composerToken.Text.Trim('"');
                    _meta.ComposerPosition = composerToken.Span.Start;
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
            _meta.Tempo = bpm;
        if (tempoDecl.Marking is string marking)
            _meta.TempoText = marking;
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
                ? TempoBeatUnitRegex().Match(tokens[i].Text)
                : System.Text.RegularExpressions.Match.Empty;
            if (m.Success)
            {
                _meta.TempoBeatUnit = int.Parse(m.Groups[1].Value);
                _meta.TempoDots = dots + m.Groups[2].Value.Length;
            }
        }
        if (tempoDecl.SwingSubdivision != 0)
            _meta.SwingSubdivision = tempoDecl.SwingSubdivision;
    }

    // The tempo beat-unit token: digits then optional dots (e.g. "4", "8."). Source-
    // generated so the regex is built at compile time, not parsed/JIT'd at runtime.
    [System.Text.RegularExpressions.GeneratedRegex(@"^([0-9]+)(\.*)$")]
    private static partial System.Text.RegularExpressions.Regex TempoBeatUnitRegex();

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
        if (_meta.KeyCustom != null)
        {
            foreach (var (s, a) in KeySignature.DecodeCustom(_meta.KeyCustom))
                if (s == step)
                    return a;
            return 0;
        }
        return LilySharp.Core.Music.KeySpelling.Alteration(step, _meta.KeySharps);
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

    private List<Measure> CollectMeasures()
    {
        // Snapshot this voice's score-level key (already transposed for the part)
        // so each section boundary can revert the running key to it.
        _sectionResetKeySharps = _meta.KeySharps;
        _sectionResetKeyCustom = _meta.KeyCustom;
        // Same for the clef: the part default a section without its own clef reverts to.
        _sectionResetClef = _meta.Clef;
        // And the grob-override part default (global + this voice's part-body overrides,
        // already collected at (0,0)) — the state each section boundary reverts to.
        _sectionResetOverrides.Clear();
        foreach (var ov in _grobOverrides)
            if (ov.StaffIndex is null || ov.StaffIndex == _currentStaffIndex)
                _sectionResetOverrides[(ov.GrobType, ov.PropertyName)] = ov.Value;
        _sectionActiveGrobProps.Clear();

        // Arm the ambient tonic at the score's home key for this voice's walk
        // (phrase auto-transpose baseline).
        ResetAmbientTonicToHome();
        _phraseTransposeSaves.Clear();

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
                if (node is RelativeResetMarker reset)
                {
                    EnterDefaultFrame(reset.OctaveOffset);
                    EnterPhraseTranspose();
                    continue;
                }

                if (node is PhraseEndMarker)
                {
                    ExitPhraseTranspose();
                    continue;
                }

                var next = i + 1 < nodeList.Count ? nodeList[i + 1] : null;
                ProcessMusicNode(node, builder, PeekMarkers(next));
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
                RecordSectionStart(section.SectionName, builder.CurrentMeasureIndex);
                builder.SectionLabel = section.SectionName;
                builder.SectionLabelPosition = section.Name.Span.Start;
                ProcessSection(section, ProcessNodes, builder);
            }
        }
        else if (_root != null)
        {
            var musicNodes = _root.DescendantNodes()
                .Where(n => !IsInsideProcessedContainer(n) && IsCollectableMusicNode(n));
            ProcessNodes(musicNodes);
        }

        FinalizeInlineVoltas();

        return builder.FinalizeMeasures();
    }

    /// <summary>Records where a section occurrence begins: the first-only anchor
    /// (<see cref="_sectionState.StartMeasure"/>) plus EVERY occurrence
    /// (<see cref="_sectionState.AllStarts"/>), so a chord/lyric track can repeat under a
    /// reprise (e.g. A played again as "A2").</summary>
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

    private void ProcessForm(Action<IEnumerable<SyntaxNode>> processNodes, MeasureBuilder builder)
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
                        RecordSectionStart(reference.SectionName, builder.CurrentMeasureIndex);
                        builder.SectionLabel = ResolveSectionLabel(reference);
                        builder.SectionLabelPosition = SectionDeclPos(reference.SectionName);
                        ProcessSection(section, processNodes, builder);
                    }
                    break;

                case FormRepeatBlockSyntax repeat:
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
                    // ProcessForm runs once PER PART; a structure-level mark
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
                          && _sectionState.Sections.TryGetValue(nameTok.Text, out var silentSection):
                    RecordSectionStart(nameTok.Text, builder.CurrentMeasureIndex);
                    builder.SectionLabel = null;
                    builder.SectionLabelPosition = SectionDeclPos(nameTok.Text);
                    ProcessSection(silentSection, processNodes, builder);
                    break;

                // `break` / `nobreak` between sections force / forbid a system break
                // after the section just played (SetBreak/SetNoBreak flag the last
                // emitted measure). Runs once per part; each flags the same measure
                // index, so the score-wide break stays consistent.
                case BreakSyntax brk when !IsInsideRepeatBlock(brk):
                    if (brk.IsNoBreak) builder.SetNoBreak();
                    else builder.SetBreak();
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
    private static bool IsInsidePartMajorTrack(SyntaxNode node)
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
        if (_sectionState.StartMeasure.Count > 0 || _sectionState.Sections.Count == 0)
            return;

        // Walk the structure's children IN SOURCE ORDER so navigation marks
        // (segno / to coda / D.S. …) interleave with the section references at
        // the right bars — a rows-only score never runs ProcessForm, so
        // the band grid lost exactly the signs a band chart needs. Labels are
        // stamped onto the grid row's measures afterwards.
        int cur = 0;
        void AdvanceSection(string name, string? label, int pos)
        {
            if (!_sectionState.Sections.TryGetValue(name, out var section))
                return;
            if (!_sectionState.StartMeasure.ContainsKey(name))
            {
                _sectionState.StartMeasure[name] = cur;
                if (label != null)
                    _sectionState.RowLabels.Add((cur, label, pos));
            }

            int chordBars = 0, lyricBars = 0;
            foreach (var cb in section.DescendantNodes().OfType<ChordPartBlockSyntax>())
                chordBars = Math.Max(chordBars, ChordNameCollector.CountBars(cb));
            foreach (var lb in section.DescendantNodes().OfType<LyricsBlockSyntax>())
                lyricBars = Math.Max(lyricBars, lb.Syllables.Count());
            cur = _sectionState.StartMeasure[name] + (chordBars > 0 ? chordBars : lyricBars);
        }

        if (_form != null)
        {
            foreach (var child in _form.DescendantNodes())
            {
                switch (child)
                {
                    case SectionReferenceSyntax r when !IsInsideRepeatBlock(r):
                        AdvanceSection(r.SectionName, ResolveSectionLabel(r), SectionDeclPos(r.SectionName));
                        break;
                    case NavigationMarkSyntax nav when !IsInsideRepeatBlock(nav):
                        // Same anchoring as ProcessForm: targets (segno/coda)
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
            foreach (var s in _sectionState.Sections.Values.OrderBy(s => s.Name.Span.Start))
                AdvanceSection(s.SectionName, s.SectionName, s.Name.Span.Start);
        }
    }

    private static bool IsInsideRepeatBlock(SyntaxNode node) => node.IsInside<FormRepeatBlockSyntax>();

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

    /// <summary>
    /// Single source of truth for "this node is a flat music node the collector
    /// consumes directly". Container expressions (tuplet/repeat/grace/inline-volta/
    /// parallel/once) appear here too but travel as ONE wrapper node — their inner
    /// content is skipped via <see cref="IsInsideProcessedContainer"/>. Keeping the
    /// membership test in one place stops the per-walk whitelists from drifting
    /// apart (which silently dropped overrides, drum notes, clefs, etc.).
    /// </summary>
    private static bool IsCollectableMusicNode(SyntaxNode node) =>
        node is NoteSyntax or DrumNoteSyntax or RestSyntax or ChordSyntax or ArpeggioSyntax
            or BarlineSyntax or BreakSyntax or TieSyntax or SlurSyntax or BeamMarkerSyntax
            or GraceExpressionSyntax or TupletExpressionSyntax or RepeatExpressionSyntax
            or ParallelExpressionSyntax or InlineVoltaSyntax or MusicMarkSyntax
            or NavigationMarkSyntax
            or OverrideDeclarationSyntax or RevertDeclarationSyntax or OnceModifierSyntax
            or ClefDeclarationSyntax or OctaveDirectiveSyntax or KeySignatureSyntax
            or TimeSignatureSyntax or TempoDeclarationSyntax or PartialDeclarationSyntax;

    /// <summary>
    /// True when a node lives inside a container expression that owns its own
    /// walk (tuplet/repeat/grace/inline-volta/parallel/once). Such nodes must be
    /// skipped by the outer walks so the wrapper is processed once, not flattened.
    /// </summary>
    private static bool IsInsideProcessedContainer(SyntaxNode node) =>
        IsInsideTuplet(node) || IsInsideRepeat(node) || IsInsideGrace(node)
        || IsInsideInlineVolta(node) || IsInsideParallel(node) || IsInsideOnce(node)
        || node.IsInside<ArpeggioSyntax>();

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
                artSyntax.NameToken.Text is "glissando" or "slide")
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
                ExpandVariable(varRef.Name.Text, varRef.OctaveOffset, bodyNodes);
            else
                bodyNodes.Add(item);
        }

        // Route through the shared sequence walker so ties/slurs/beams inside a
        // repeat body get the one-node marker lookahead (a manual per-item loop
        // here previously dropped them — e.g. `repeat volta 2 { c4( d e f) }`).
        void ProcessBodyOnce() => ProcessMusicNodeSequence(bodyNodes, builder);

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

                int graceMidi = PitchToMidi(rp.DisplayStep, rp.DisplayAlteration, rp.DisplayOctave);
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

}
