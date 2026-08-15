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
/// A tie (<c>~</c>) whose immediately following timed item cannot receive it — a
/// note/chord repeating none of the tied pitches, or an audible rest. A tie joins
/// two notes of the SAME pitch, so this is almost always an authoring slip (a slur
/// was meant, or the target note was mistyped). <see cref="SourcePosition"/> points
/// at the following item (the one that fails to match).
/// </summary>
public record TieTargetWarning(
    int SourcePosition,
    bool IntoRest     // true = the tie runs into a rest; false = a pitch mismatch
);

/// <summary>
/// A slur mark that pairs with nothing, so no slur is drawn: a <c>(</c> that is never
/// closed (including one left open when its voice ends — a slur does not cross voices)
/// or a <c>)</c> read with none open. <see cref="SourcePosition"/> points at the NOTE the
/// mark is written on, because that is where the mark binds: a slur mark annotates the
/// note BEFORE it (MeasureCollector.MusicWalk PeekMarkers), which is also why a <c>(</c>
/// with no note before it never becomes a mark at all and is seen here only through the
/// <c>)</c> that then has nothing to pair with.
/// </summary>
public record UnpairedSlurWarning(
    int SourcePosition,
    bool IsOpen       // true = an unclosed '('; false = a ')' with nothing open
);

/// <summary>
/// Helper class for building measures from syntax nodes.
/// Supports both explicit barlines and automatic measure detection based on time signature.
/// </summary>
internal sealed class MeasureBuilder
{
    private readonly List<Measure> _measures = new();
    private readonly List<MusicItem> _currentItems = new();

    // True when the current measure boundary can absorb ONE confirming bare barline
    // silently: the piece/section START (a leading `|` merely anchors the boundary) and
    // an AUTO-FILL close (duration reached the meter; the following `|` confirms it).
    // False after any WRITTEN barline consumed the boundary — a written close, a typed
    // decoration, an absorbed confirmation, a placeholder — so a bare `|` there is the
    // second of a `| |` PAIR and opens an empty placeholder measure. An empty measure
    // is always a visible `| |` pair; a single bare `|` never creates one. See
    // HandleBarline.
    private bool _confirmableBoundary = true;
    // True when the confirmable boundary sits right after a bar this stream JUST closed
    // (an auto-fill, or a phrase whose last bar was closed by its own trailing `|`), so a
    // written `|`/`:|`/… that confirms it is recorded as a SOURCE of that measure's end
    // (see AddEndBarlineSource). Cleared at section/phrase STARTS so a leading `|` there
    // never attaches to a prior measure (which belongs to the previous section/the
    // pre-phrase stream).
    private bool _boundaryRetargetable;
    // True when _measures[^1] closed by AUTO-FILL with no written barline, so its SourceEnd
    // is a placeholder (note+1). The FIRST written bar to confirm it replaces that, rather
    // than being added alongside as an alias (there is no written bar there to keep).
    private bool _lastEndAutoFill;

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

    /// <summary>True at the very opening of the piece — bar 0 with no music yet (zero-duration
    /// grobs like a clef may already sit there). A directive here (a section's own
    /// key / time / tempo overriding the score default) IS the opening value, not a change
    /// within the piece, so it collapses into the initial signature / mark.</summary>
    public bool AtPieceOpening => _measures.Count == 0 && _currentDuration == Fraction.Zero;

    /// <summary>True at a measure boundary: no items yet in the current measure (just
    /// after a barline, or the very start), or the measure is already full (a mark
    /// written right before its barline). A navigation landmark belongs at such a
    /// boundary; anything else is mid-measure.</summary>
    public bool AtMeasureBoundary => _currentItems.Count == 0 || _currentDuration == _timeSignature;

    /// <summary>True when the current span holds measure-worthy content — something with
    /// duration (a note/rest/chord) — as opposed to only zero-duration directives (a
    /// leading <c>clef</c>/<c>key</c>/<c>time</c>). A bare or leading <c>|</c> CLOSES a
    /// span with content; a directive-only span it merely CONFIRMS, carrying the
    /// directive into the first real measure so no spurious directive-only empty bar is
    /// drawn (the <c>clef treble x | …</c> case).</summary>
    private bool HasMeasureContent => _currentDuration > Fraction.Zero;

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

    /// <summary>Re-arms the confirmable boundary at a section start: a section that
    /// OPENS with a bare <c>|</c> anchors its own start boundary (no empty measure),
    /// regardless of how the previous section's last bar was closed. See
    /// <see cref="_confirmableBoundary"/>.</summary>
    /// <summary>Re-arms the confirmable boundary at a section/phrase edge. A section or
    /// phrase START passes <paramref name="retargetableClose"/> false (a leading `|` there
    /// is an anchor). A phrase EXIT passes true: if the phrase ended with a CLOSED bar
    /// (its own trailing `|`, or an auto-fill), an outer `|` that confirms it owns that
    /// barline and retargets the phrase's last measure onto the written `|`.</summary>
    public void ResetMeasureBoundary(bool retargetableClose = false)
    {
        _confirmableBoundary = true;
        _boundaryRetargetable = retargetableClose
            && _currentItems.Count == 0 && _measures.Count > 0;
    }

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

        var itemDuration = GetItemDuration(item);
        _currentItems.Add(item);

        // Real content fills this span, so a following barline closes IT, not an empty
        // measure. A ZERO-duration directive (a clef change) does not fill anything — it
        // leaves the boundary confirmable, so a bare or leading `|` carries the directive
        // into the first real measure instead of closing a spurious clef-only empty bar
        // (`clef treble x | …`).
        if (itemDuration > Fraction.Zero)
            _confirmableBoundary = false;

        // Track duration
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
        _confirmableBoundary = false;
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

        // An auto-filled close leaves an UNCONFIRMED boundary (a following written barline
        // just confirms it); a written-barline close consumed the boundary, so a following
        // bare barline is the second of a `| |` pair and opens a placeholder measure. See
        // HandleBarline.
        ResetPerMeasureState(sourceEnd, confirmableBoundary: !explicitBar);
        // The confirmable boundary an auto-fill leaves is attachable: a following written
        // barline records itself as this measure's end source (see HandleBarline). An
        // auto-fill left no written bar at SourceEnd, so the first such bar REPLACES it.
        _boundaryRetargetable = !explicitBar;
        _lastEndAutoFill = !explicitBar;
    }

    /// <summary>
    /// Records a WRITTEN barline at the current boundary as a source of the last measure's
    /// END barline. A drawn barline can collapse several written ones (a phrase's <c>:|</c>,
    /// a section <c>|</c>/<c>:|:</c> confirming it): a caret on ANY highlights it, and a
    /// click jumps to the OUTERMOST — the largest offset, which is the section bar since a
    /// phrase is declared (early) before it is referenced (later). An unwritten auto-fill
    /// placeholder is replaced by the first written bar rather than kept as an alias.
    /// </summary>
    private void AddEndBarlineSource(int position)
    {
        var m = _measures[^1];
        if (_lastEndAutoFill)
        {
            _measures[^1] = m with { SourceEnd = position };
            _lastEndAutoFill = false;
        }
        else
        {
            var all = m.EndHighlightAliases.Append(m.SourceEnd).Append(position).Distinct().ToList();
            int click = all.Max();
            _measures[^1] = m with
            {
                SourceEnd = click,
                EndHighlightAliases = all.Where(p => p != click).ToImmutableArray(),
            };
        }
        _measureSourceStart = position; // the next measure starts after this written bar
    }

    /// <summary>
    /// Clears the per-measure accumulator after a measure is emitted — by content
    /// (<see cref="EmitMeasure"/>) or as an empty placeholder (<see cref="EmitEmptyMeasure"/>):
    /// resets the pending barlines / section label / duration, advances the source start,
    /// sets the auto-fill boundary flag, restores a pending pickup meter, and fires
    /// <see cref="MeasureCompleted"/>.
    /// </summary>
    /// <remarks>
    /// The excess of an OVERFULL bar is intentionally dropped here: Lily# is "explicit over
    /// implicit" and does NOT auto-split a note across a barline the way LilyPond does. An
    /// overrun makes an overfull measure (MeasureValidator flags it; the fix is an explicit
    /// tie <c>c4 d e f4~ | f4 …</c>), drawn as written with the beat counter reset so the
    /// following bars stay aligned — the excess is not carried into a tied continuation.
    /// </remarks>
    private void ResetPerMeasureState(int sourceEnd, bool confirmableBoundary)
    {
        _confirmableBoundary = confirmableBoundary;
        // Only EmitMeasure(auto) re-arms these immediately after; every other reset
        // (explicit close, empty placeholder) leaves a settled, non-attachable boundary.
        _boundaryRetargetable = false;
        _lastEndAutoFill = false;
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
    /// a duration mismatch is flagged by the measure validator (see remarks),
    /// not a layout input. Full measures are unaffected: duration auto-completion
    /// has already closed them, so the barline arrives on an empty measure (no-op).
    /// </summary>
    /// <remarks>
    /// This is the agreed Lily# semantic (the reverse of LilyPond, where
    /// "|" is only an assertion and Timing draws the bars): see the measure
    /// validator, which checks written measures against the meter.
    /// </remarks>
    public void HandleBarline(BarlineType barType, int position)
    {
        if (barType == BarlineType.RepeatStart)
        {
            // |: opens the NEXT measure; close anything pending first. A directive-only
            // span (a leading clef) is NOT closed — it carries into the first repeat bar.
            if (HasMeasureContent)
                CompleteMeasure(position, BarlineType.Single);
            _pendingStartBarline = BarlineType.RepeatStart;
            // The `|:` IS the next measure's start boundary — record its offset so the
            // drawn start barline's click/highlight lands on the written `|:`, not on
            // the previous close (SourceStart otherwise carries the last SourceEnd).
            _measureSourceStart = position;
            return;
        }

        var endType = barType == BarlineType.None ? BarlineType.Single : barType;

        if (HasMeasureContent)
        {
            CompleteMeasure(position, endType);
        }
        else if (endType != BarlineType.Single && _measures.Count > 0)
        {
            // A TYPED barline (":|", "||", "|.") on an empty span decorates the PREVIOUS
            // measure's end — retro-apply the type (never an empty placeholder). When this
            // stream just closed that bar, record the typed bar as an end SOURCE: it takes
            // the click target (the outer section repeat) while the bar's own close stays a
            // highlight alias — so a phrase's `:|` still lights at every call site.
            _measures[^1] = _measures[^1] with { EndBarline = endType };
            if (_boundaryRetargetable)
                AddEndBarlineSource(position);
            _confirmableBoundary = false;
            _boundaryRetargetable = false;
        }
        else if (_confirmableBoundary)
        {
            // A bare `|` merely CONFIRMS the boundary it sits on — a section start (leading
            // `|`, nothing to attach) or a bar this stream just closed. In the latter case
            // record the `|` as an end source (retargeting the click to it and keeping the
            // bar's own close, if written, as a highlight alias). A FURTHER bare `|` with no
            // closed bar to attach to is the second of a `| |` pair (the else branch).
            if (_boundaryRetargetable && _measures.Count > 0)
                AddEndBarlineSource(position);
            _confirmableBoundary = false;
            _boundaryRetargetable = false;
        }
        else
        {
            // The second barline of a `| |` PAIR (nothing between two written bars) —
            // mid-piece or trailing — opens a real placeholder measure: it holds a slot
            // so other parts stay aligned, renders as an empty bar, and is flagged
            // shorter-than-the-meter until the author fills it (MeasureValidator, over the
            // shared MeasureModel). An empty measure is thus always VISIBLE in the source
            // as `| |`.
            EmitEmptyMeasure(position, endType);
        }
    }

    /// <summary>
    /// Emits an empty placeholder measure (0 items, 0 duration) for a bare barline gap.
    /// See <see cref="Measure.IsEmptyPlaceholder"/>.
    /// </summary>
    private void EmitEmptyMeasure(int sourceEnd, BarlineType endType)
    {
        // A pending break/nobreak belongs to THIS measure (as in EmitMeasure) — a `break`
        // just before a bare `|` breaks after the placeholder, not the next real bar.
        bool hasBreak = _pendingBreak;
        bool noBreak = _pendingNoBreak;
        _pendingBreak = false;
        _pendingNoBreak = false;

        _measures.Add(new Measure(
            ImmutableArray<MusicItem>.Empty,
            _pendingStartBarline,
            _pendingEndBarline != BarlineType.None ? _pendingEndBarline : endType,
            _sectionLabel,
            _measureSourceStart,
            sourceEnd,
            hasBreakAfter: hasBreak,
            lineBreakPermission: noBreak ? Layout.BreakPermission.Forbid : Layout.BreakPermission.Allow,
            sectionLabelPosition: _sectionLabelPosition,
            isPickup: _partialRestore != null)
        {
            IsEmptyPlaceholder = true,
        });

        // Closed by a written barline, so a further bare barline opens ANOTHER placeholder
        // (`| | |` = two empty measures).
        ResetPerMeasureState(sourceEnd, confirmableBoundary: false);
    }

    /// <summary>
    /// Closes the current measure at an EXPLICIT barline with the given end
    /// barline type.
    /// </summary>
    private void CompleteMeasure(int sourceEnd, BarlineType endType)
        => EmitMeasure(sourceEnd, endType, explicitBar: true);


    public List<Measure> FinalizeMeasures()
    {
        // Handle any remaining items as the final measure
        if (_currentItems.Count > 0)
        {
            _measures.Add(new Measure(
                _currentItems.ToImmutableArray(),
                _pendingStartBarline,
                _pendingEndBarline != BarlineType.None ? _pendingEndBarline : BarlineType.Single,
                _sectionLabel,
                _measureSourceStart,
                _measureSourceStart,  // End position same as start for incomplete
                sectionLabelPosition: _sectionLabelPosition,
                isPickup: _partialRestore != null));
        }

        // Back-to-back repeats collapse: a measure that ENDS with a repeat (`:|` or
        // `:|:`) immediately followed by one that STARTS with a repeat (`|:` or `:|:`)
        // is ONE combined barline (`:|:`), not two piled up — a phrase ending `:|`
        // referenced right before another opening `|:` (or a section `:|:` between
        // them) otherwise stacks thick bars and doubles the dots. The join becomes
        // RepeatBoth and the next measure drops its now-duplicate start barline.
        // LILYPOND-REF: scm/bar-line.scm:1308-1310 define-bar-line — ":|.:" and ":|.|:" are
        // single declared glyphs whose END piece is ":|." and whose BEGIN piece is ".|:",
        // i.e. LilyPond spells the back-to-back pair as ONE bar line rather than two.
        for (int i = 0; i + 1 < _measures.Count; i++)
        {
            bool endsRepeat = _measures[i].EndBarline
                is BarlineType.RepeatEnd or BarlineType.RepeatBoth;
            bool startsRepeat = _measures[i + 1].StartBarline
                is BarlineType.RepeatStart or BarlineType.RepeatBoth;
            if (endsRepeat && startsRepeat)
            {
                // Fold the absorbed `|:`'s source into the combined `:|:`'s highlight set
                // (a RepeatStart carries a written `|:`; a RepeatBoth start does not add a
                // new one). The click target stays the outermost (max) offset.
                var end = _measures[i];
                var sources = end.EndHighlightAliases.Append(end.SourceEnd);
                if (_measures[i + 1].StartBarline == BarlineType.RepeatStart)
                    sources = sources.Append(_measures[i + 1].SourceStart);
                var all = sources.Distinct().ToList();
                int click = all.Max();
                _measures[i] = end with
                {
                    EndBarline = BarlineType.RepeatBoth,
                    SourceEnd = click,
                    EndHighlightAliases = all.Where(p => p != click).ToImmutableArray(),
                };
                _measures[i + 1] = _measures[i + 1] with { StartBarline = BarlineType.None };
            }
        }

        // ⚠️ NO AUTOMATIC FINAL BARLINE. The last measure keeps the barline it was
        // written with. `|.` is a thing the author writes.
        // ⚠️ MEASURED, NOT CITED — deliberately. The claim is that LilyPond does NOT do
        //   something, and the honest evidence for an absence is LilyPond's own output,
        //   not an address. (A first draft cited lily/bar-line.cc here; that file is not
        //   in the 2.26.0 tree at all. Absence claims are where invented citations grow.)
        // MEASURED (scratch/beamskip/lp-bar.ly, 4 scores, same paper): a complete final measure ends with a
        //   THIN bar 0.19 wide; an INCOMPLETE final measure (`{ c'4 }`) gets NO bar at all;
        //   0.19 + 0.60 appears only where `\bar "|."` is written.
        // ⚠️ This used to stamp BarlineType.Final here on the claim "music convention",
        //   with no LilyPond citation behind it. It made the last measure 0.9 ss wider
        //   than LilyPond's in essentially every book — the single most visible systematic
        //   divergence in the LP regression corpus, and a documented comparison trap
        //   (HANDOFF 5.3 "LP は \bar "|." を書かないと終止線を細い | にする").
        // ⚠️ STILL NOT LILYPOND: an incomplete final measure gets a thin bar here where
        //   LilyPond draws none. That is a different rule (whether a bar is engraved at
        //   all) and is not fixed by this change.

        // A trailing measure holding ONLY clef changes (a clef written after the last
        // note — clef-change-at-end.ly) owns no bar moment of its own: LilyPond engraves
        // that clef on the SAME break-align column as the closing bar (the unbroken order
        // is `… clef, staff-bar …`), so the piece's end barline moves onto the PREVIOUS
        // measure and this column keeps only the clef, zero width, no bar. The clef then
        // hangs back into the closing gap that measure's spring already reserves
        // (SpacingRules.BoundaryClefAllowance). Key/time changes are NOT treated this
        // way: break-align puts those AFTER the bar.
        // LILYPOND-REF: scm/define-grobs.scm:650-664 break-align-orders
        if (_measures.Count >= 2)
        {
            var tail = _measures[^1];
            if (tail.Items.Length > 0 && tail.Items.All(i => i is ClefChangeItem))
            {
                // ⚠️ MERGE, DO NOT OVERWRITE. A typed barline written against a
                // directive-only span has ALREADY been retro-applied to the previous
                // measure by HandleBarline (the `endType != Single` branch), so the
                // previous measure is where `g'1 clef bass |.` keeps its `|.`. Copying the
                // tail's EndBarline over it threw that away and printed a plain `|`:
                // measured, `g'1 clef bass |.` and `g'1 |. clef bass` both drew one thin
                // bar, and so did `g'1 clef bass ||` — every written type was lost, not
                // just the final. It went unnoticed while a final barline was stamped on
                // the last measure automatically, because then both sides were Final and
                // the overwrite was a no-op.
                // The rule is the one two barlines at one moment already follow elsewhere
                // in this collector — see Stronger, the cross-voice merge.
                var prev = _measures[^2];
                var merged = MeasureCollector.Stronger(prev.EndBarline, tail.EndBarline);
                // The click target follows the bar that WON. When the tail brings only the
                // default Single (the ordinary `g'1 clef bass`), the tail's source stays the
                // target exactly as before, so no existing fixture's data-pos moves.
                bool prevWon = merged == prev.EndBarline && merged != tail.EndBarline;
                _measures[^2] = prev with
                {
                    EndBarline = merged,
                    SourceEnd = prevWon ? prev.SourceEnd : tail.SourceEnd,
                };
                _measures[^1] = tail with
                {
                    EndBarline = BarlineType.None,
                    IsTrailingClefColumn = true,
                };
            }
        }

        return _measures;
    }

    // --- checkpoint/resume substrate (CollectWalkProbe) ---

    /// <summary>Every cross-measure field of the builder, captured at a clean
    /// boundary (<see cref="AtCleanBoundary"/> — no pending items, no elapsed
    /// duration, so <c>_currentItems</c>/<c>_currentDuration</c> need no slot).
    /// <see cref="LastMeasure"/> pins the value of <c>_measures[^1]</c> AT the
    /// boundary: it is the one already-emitted element the walk can still
    /// rewrite afterwards (<see cref="SetBreak"/>, <see cref="AddEndBarlineSource"/>),
    /// so a prefix harvested from the walk's final list must put it back.</summary>
    internal readonly record struct BuilderCheckpoint(
        bool ConfirmableBoundary,
        bool BoundaryRetargetable,
        bool LastEndAutoFill,
        Fraction TimeSignature,
        Fraction? PartialRestore,
        BarlineType PendingStartBarline,
        BarlineType PendingEndBarline,
        bool PendingBreak,
        bool PendingNoBreak,
        string? SectionLabel,
        int SectionLabelPosition,
        int MeasureSourceStart,
        Measure? LastMeasure);

    /// <summary>True at a checkpointable boundary: nothing pending in the
    /// current measure, not even a zero-duration directive.</summary>
    internal bool AtCleanBoundary
        => _currentItems.Count == 0 && _currentDuration == Fraction.Zero;

    internal BuilderCheckpoint Capture() => new(
        _confirmableBoundary, _boundaryRetargetable, _lastEndAutoFill,
        _timeSignature, _partialRestore,
        _pendingStartBarline, _pendingEndBarline,
        _pendingBreak, _pendingNoBreak,
        _sectionLabel, _sectionLabelPosition, _measureSourceStart,
        _measures.Count > 0 ? _measures[^1] : null);

    /// <summary>Restores a captured boundary state, adopting <paramref name="prefix"/>
    /// as the measures emitted before it. The <see cref="MeasureCompleted"/> hook
    /// stays as registered — the resume re-enters THIS builder, not a new one.</summary>
    internal void Restore(BuilderCheckpoint ck, IReadOnlyList<Measure> prefix)
    {
        _measures.Clear();
        _measures.AddRange(prefix);
        if (ck.LastMeasure is { } last && _measures.Count > 0)
            _measures[^1] = last;
        _currentItems.Clear();
        _currentDuration = Fraction.Zero;
        _confirmableBoundary = ck.ConfirmableBoundary;
        _boundaryRetargetable = ck.BoundaryRetargetable;
        _lastEndAutoFill = ck.LastEndAutoFill;
        _timeSignature = ck.TimeSignature;
        _partialRestore = ck.PartialRestore;
        _pendingStartBarline = ck.PendingStartBarline;
        _pendingEndBarline = ck.PendingEndBarline;
        _pendingBreak = ck.PendingBreak;
        _pendingNoBreak = ck.PendingNoBreak;
        _sectionLabel = ck.SectionLabel;
        _sectionLabelPosition = ck.SectionLabelPosition;
        _measureSourceStart = ck.MeasureSourceStart;
    }

    /// <summary>A copy of the emitted measures BEFORE <see cref="FinalizeMeasures"/>
    /// mutates them — the values a resumed walk re-enters with.</summary>
    internal List<Measure> MeasuresSnapshot() => new(_measures);
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

    /// <summary>
    /// The <c>title</c> / <c>composer</c> the score being collected states for itself
    /// (<c>score sub { title "Violin I" … }</c>). Set by the render pipeline before
    /// collecting, and applied by <see cref="CollectDefinitions"/> after the file-level
    /// walk — so a score that restates nothing inherits the file's header.
    /// </summary>
    public ImmutableArray<MetadataDeclarationSyntax> HeaderOverrides { get; set; }

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
    // Added to the local measure index when collecting a parallel span's EXTRA
    // voices (they're collected with a fresh 0-based builder), so their
    // per-note metadata — dynamics, articulations, etc. — lands at the span's
    // real measure index instead of measure 0. Zero for the primary stream.
    private int _metadataMeasureOffset = 0;
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
    // Saved diatonic-shift values, pushed/popped alongside _phraseTransposeSaves.
    private readonly Stack<int> _phraseDiatonicSaves = new();
    // Each open reference's outgoing anchor (bare letter + octave, resolved in
    // the phrase's own frame at entry), handed to the relative chain at exit —
    // the chord rule; null = pitchless body. Pushed/popped alongside the above.
    private readonly Stack<(char Name, int Octave)?> _phraseAnchorSaves = new();

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
    private int _walkOrdinal;                        // Nth CollectMeasures call of this collect
    private int _invocationInSection;                // ProcessNodes calls within the current section (or the walk, pre-section)
    private int _sectionVisit;                       // ProcessSection entries within the current walk
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
        _meta.TextFont,
        _meta.EmbedFont);

    /// <summary>
    /// Collects a Score from a syntax tree.
    /// </summary>
    public Score Collect(SyntaxTree tree, string? voiceName = null,
        FormDeclarationSyntax? localForm = null,
        string? attachedChordPart = null,
        ChordDisplayMode attachedChordDisplay = ChordDisplayMode.Names,
        IReadOnlyList<string>? attachedLyricParts = null)
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
        TieTargetScanner.Scan(voice, _tieTargetWarnings);
        SlurPairingScanner.Scan(voice, _unpairedSlurWarnings);

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
        _chordNameCollector.CollectBlocks(tree.GetRoot(), _sectionState.StartMeasure, _currentStaffIndex);
        // `staff NAME with chords CHORDPART [as roman|both]` on a single-staff score.
        if (attachedChordPart != null)
            _chordNameCollector.CollectAttached(
                tree.GetRoot(), attachedChordPart, _sectionState.StartMeasure, _currentStaffIndex,
                attachedChordDisplay);

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
    public MultiStaffScore CollectMultiStaff(SyntaxTree tree, RenderSpec renderSpec)
        => CollectMultiStaff(tree, renderSpec, harvestStructureMarks: true);

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
        foreach (var (voiceName, withChords, chordDisplay, withLyrics, sharesStaff) in renderSpec.GetVoiceBindings())
        {
            _voiceName = voiceName;
            // A condensed staff yields one binding per part but ONE staff, so its later
            // parts take the staff index already handed out instead of opening a new one
            // (see GetVoiceBindings) — otherwise every staff below would be tagged one
            // index too high.
            _currentStaffIndex = sharesStaff ? collectStaffIndex - 1 : collectStaffIndex++;
            _octave.LastPitchName = 'c';
            _defaultDuration = Fraction.Quarter;
            _defaultDots = 0;

            // Part-body grob defaults (`part <voice> { override … }`) scope to this staff.
            CollectPartBodyOverrides(tree.GetRoot(), voiceName, _currentStaffIndex);

            // `staff NAME with chords CHORDPART [as roman|both]`: remember the
            // attachment (and its display); the chord symbols are collected AFTER
            // the voice loop, once every section's start measure is registered.
            if (withChords != null)
                attachedChords.Add((withChords, _currentStaffIndex, chordDisplay));

            // `staff NAME with lyrics L`: remember each named lyrics part to align
            // under THIS staff (post-loop, once section starts are registered).
            foreach (var lyName in withLyrics)
                attachedLyrics.Add((lyName, _currentStaffIndex, voiceName));

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
        // A part this score OMITS still contributes score-level barlines (|: :|) and navigation
        // marks. Feed its voices into the barline sync so the repeat propagates onto the drawn
        // rows; the sentinel key keeps them out of the write-back below (only staffVoices' real
        // names are drawn). Its navigation marks are merged into _musicMarks inside the harvest.
        if (harvestStructureMarks)
            foreach (var v in HarvestOmittedStructure(tree, renderSpec))
                flatVoices["omit:" + v.Name] = v;
        SynchronizeBarlines(flatVoices);
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
        // Anonymous chords{} on a multi-staff score go above the TOP staff (index
        // 0), not whatever staff the collect loop ended on — _currentStaffIndex
        // still holds the LAST staff here, which hung the names between the
        // staves of a grand staff (corpus: chord-names-in-grand-staff.ly).
        _chordNameCollector.CollectBlocks(tree.GetRoot(), _sectionState.StartMeasure, staffIndex: 0);
        foreach (var (attachedPart, attachedStaff, attachedMode) in attachedChords)
            _chordNameCollector.CollectAttached(
                tree.GetRoot(), attachedPart, _sectionState.StartMeasure, attachedStaff, attachedMode);

        // Phase 3: Build staff groups from render spec
        var staffGroups = renderSpec.ToStaffGroups(name =>
            staffVoices.TryGetValue(name, out var v) ? v
                : ImmutableArray.Create(new Voice(name, ImmutableArray<Measure>.Empty)))
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

        return ScoreAssembler.BuildMultiStaffScore(staffGroups, CaptureScoreContent(
            staffGroups.SelectMany(sg => sg.Staves).SelectMany(st => st.Voices)
                .Select(v => v.Measures.Length).DefaultIfEmpty(0).Max()));
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
        // separate, and the inner call skips the harvest so it can't recurse.
        var items = omitted
            .Select(n => (RenderItemSpec)new SingleStaffSpec(new StaffSpec(ClefType.Treble, n)))
            .ToImmutableArray();
        MultiStaffScore harvested;
        try { harvested = new MeasureCollector().CollectMultiStaff(tree, renderSpec with { Items = items }, harvestStructureMarks: false); }
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

    /// <summary>True when <paramref name="partName"/> writes score-level structure (a navigation
    /// mark or a repeat barline) in its music — the cheap gate that skips the isolated harvest
    /// pass when there is nothing to harvest.</summary>
    private static bool PartHasStructure(SyntaxNode root, string partName)
    {
        var part = root.ChildNodes().OfType<PartDeclarationSyntax>()
            .FirstOrDefault(p => p.Name.Text == partName);
        // Green finder (kind pre-filter, red type test stays the authority):
        // this gate runs per part per collect, and the red DescendantNodes walk
        // materialized the part's whole subtree just to type-test it.
        return part != null && part.GreenSites(static g => (
                g.Kind is SyntaxKind.NavigationMark or SyntaxKind.InlineVolta or SyntaxKind.Barline,
                Descend: true))
            .Any(n => n is NavigationMarkSyntax or InlineVoltaSyntax
                || (n is BarlineSyntax bl && bl.BarToken.Text.Contains(':')));
    }

    /// <summary>The score-level mark types worth harvesting from an unrendered part: navigation
    /// and rehearsal marks (not per-staff dynamics or the piece-wide tempo).</summary>
    private static bool IsStructuralMark(MusicMarkType t) => t is
        MusicMarkType.Segno or MusicMarkType.Coda or MusicMarkType.Fine or MusicMarkType.ToCoda
        or MusicMarkType.DalSegno or MusicMarkType.DaCapo
        or MusicMarkType.DalSegnoAlFine or MusicMarkType.DalSegnoAlCoda
        or MusicMarkType.DaCapoAlFine or MusicMarkType.DaCapoAlCoda
        or MusicMarkType.Rehearsal;

    /// <summary>
    /// Bakes the VOICE-forced stem directions into a polyphonic staff's items
    /// (voice 1 up, voice 2 down, …), for the same reason
    /// <see cref="ResolveBeamStemDirections"/> bakes the beam-resolved ones:
    /// LilyPond's <c>\voiceOne</c>/<c>\voiceTwo</c> set Stem.direction in the
    /// engravers, BEFORE spacing runs, so everything downstream must see the
    /// direction that actually gets printed.
    /// </summary>
    /// <remarks>
    /// The renderer already forces these when it draws — SharedRenderer.cs
    /// <c>forcedStemUp ?? note.StemUp</c>, with <c>forcedStemUp</c> from
    /// <see cref="VoiceDefaults.GetDefaultStemUp"/> — but nothing wrote them back
    /// into the model, so an UNBEAMED note in a second voice reached the spacing
    /// engine claiming its pitch-derived direction while the renderer drew the
    /// opposite one. (Beamed notes were already correct, because a beam bakes its
    /// own direction into the same slot.) That broke the stem-direction spacing
    /// corrections in exactly the polyphonic case they exist for: measured against
    /// LilyPond 2.24.4, merging the per-voice wishes with pitch-derived directions
    /// moved a bar's last-column → bar-line distance the WRONG WAY (+0.036 where
    /// LilyPond has −0.100 relative to the same bar set monophonically).
    ///
    /// Applied last, over the beam-resolved directions, to match the renderer's
    /// precedence — but NOT over a direction the writer asked for
    /// (<see cref="NoteItem.ForcedStemUp"/>). In LilyPond only the <c>\\</c> sub-lists
    /// are voicified, so music before the construct in the same measure — which this
    /// measure-granular span cannot tell apart — never receives the voice props at all,
    /// and an explicit <c>\stemDown</c> inside a block is a later property set that
    /// beats <c>\voiceOne</c>'s. Either way the writer's ask survives.
    /// Voices 5+ get <c>null</c> from GetDefaultStemUp and keep their own.
    ///
    /// Only INSIDE the span, though — see <see cref="VoiceDefaults.IsPolyphonicAt"/>.
    /// LilyPond's <c>\\</c> gives each block its own Voice context, so the forcing
    /// dies with the span; baking it across the whole part instead pinned the stems
    /// of monophonic sections that merely shared a part with one <c>voice { }</c>.
    /// LILYPOND-REF: scm/music-functions.scm:1042-1057 voicify-sublist / make-voice-props-set
    /// </remarks>
    /// <remarks>
    /// ⚠️ Also called from <see cref="RenderSpec.ToStaffGroups"/> for a
    /// <c>condensedStaff</c>, whose voices come from SEPARATE parts and so are only a
    /// polyphonic staff once they have been put together. Applying the voice props is the
    /// staff's business, not the part's, and running it per part left the rests of a
    /// condensed staff with no direction at all — both parts' whole rests landed on the
    /// centre line, one on top of the other, where LilyPond's <c>\voiceOne</c>/<c>\voiceTwo</c>
    /// control puts them at ±4 (measured: scratch/lpreg/pcsil-a-cond.lys against pcsil-ctl.ly).
    /// The stems were right on their own, because the renderer re-derives THOSE from the
    /// voice index; it is the rests that read the stamp.
    /// ⚠️ NOT for a <c>combinedStaff</c>: the combiner has already decided each item's
    /// direction, and LilyPond's shared and solo contexts carry no voice settings at all.
    /// </remarks>
    internal static ImmutableArray<Voice> ResolveVoiceStemDirections(ImmutableArray<Voice> voices)
    {
        if (voices.Length <= 1)
            return voices;

        // Duration already includes dots (the instance GetItemDuration's rule; this
        // walk is static, so the three-arm switch is restated here).
        static Fraction ItemSoundingDuration(MusicItem item) => item switch
        {
            NoteItem note => note.Duration,
            RestItem rest => rest.Duration,
            ChordItem chord => chord.Duration,
            _ => Fraction.Zero,
        };

        var rebuilt = voices.ToBuilder();
        for (int vi = 0; vi < voices.Length; vi++)
        {
            if (VoiceDefaults.GetDefaultStemUp(vi + 1) is not { } forced)
                continue;

            var measures = voices[vi].Measures.ToBuilder();
            bool changed = false;
            for (int mi = 0; mi < measures.Count; mi++)
            {
                if (!VoiceDefaults.IsPolyphonicAt(voices, mi))
                    continue;

                var measure = measures[mi];
                var items = measure.Items.ToBuilder();
                bool measureChanged = false;
                // The span's reach WITHIN the measure: the primary voice's stream keeps
                // flowing after the span closes (`voice { fis2. } { e2. } r4`), and the
                // music after it is back in the surrounding context — LilyPond leaves it
                // unforced (probe vrest-probe.ly: the trailing r2 sits on the MIDDLE
                // line while the span's own rest takes the voiced +4, spacer partner or
                // not). The extra voices' tracks hold nothing but span content, so the
                // span is over where their content ends. ⚠️ An approximation with the
                // same named reach as the measure-granular one above: a first block
                // LONGER than every later one (`voice { fis2. r8 } { e2. } …`) stops
                // forcing where the later blocks stop, where LilyPond's \voiceOne holds
                // to the end of its own block. Carrying the span's extent on the model
                // is what closing that would take; the corpus binds only the trailing
                // case (collision-harmonic-no-dots.ly).
                var spanEnd = Fraction.Zero;
                if (vi == 0)
                {
                    for (int ov = 1; ov < voices.Length; ov++)
                    {
                        if (mi >= voices[ov].Measures.Length)
                            continue;
                        var covered = Fraction.Zero;
                        foreach (var it in voices[ov].Measures[mi].Items)
                            covered += ItemSoundingDuration(it);
                        if (covered > spanEnd)
                            spanEnd = covered;
                    }
                }
                var onset = Fraction.Zero;
                for (int ii = 0; ii < items.Count; ii++)
                {
                    var itemOnset = onset;
                    onset += ItemSoundingDuration(items[ii]);
                    if (vi == 0 && itemOnset >= spanEnd)
                        continue;
                    // The same voice-props distribution reaches RESTS: LilyPond's
                    // make-voice-props-set puts direction on every
                    // direction-polyphonic-grob, and Rest is in that list — the
                    // spacing reads it as the rest's pure voiced position.
                    // LILYPOND-REF: scm/music-functions.scm:666-674 make-voice-props-set
                    int restDir = forced ? 1 : -1;
                    MusicItem? updated = items[ii] switch
                    {
                        NoteItem n when n.ForcedStemUp is null && n.StemUpOverride != forced
                            => n with { StemUpOverride = forced },
                        ChordItem c when c.ForcedStemUp is null && c.StemUpOverride != forced
                            => c with { StemUpOverride = forced },
                        RestItem { IsSpacer: false, IsMultiMeasure: false } r
                                when r.VoiceDirection != restDir
                            => r with { VoiceDirection = restDir },
                        _ => null,
                    };
                    if (updated == null)
                        continue;
                    items[ii] = updated;
                    measureChanged = true;
                }
                if (!measureChanged)
                    continue;

                measures[mi] = new Measure(
                    items.ToImmutable(),
                    measure.StartBarline, measure.EndBarline, measure.SectionLabel,
                    measure.SourceStart, measure.SourceEnd,
                    hasBreakAfter: measure.HasBreakAfter,
                    lineBreakPermission: measure.LineBreakPermission,
                    breakPenalty: measure.BreakPenalty,
                    pageBreakPermission: measure.PageBreakPermission,
                    pageTurnPermission: measure.PageTurnPermission,
                    sectionLabelPosition: measure.SectionLabelPosition,
                    isPickup: measure.IsPickup);
                changed = true;
            }

            if (changed)
                rebuilt[vi] = voices[vi] with { Measures = measures.ToImmutable() };
        }
        return rebuilt.ToImmutable();
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
            voice, new TimeSignature(_meta.TimeBeats, _meta.TimeBeatType, _meta.TimeBeatsText, _meta.TimeSenzaMisura), _tupletBrackets.ToImmutableArray(),
            memo: BeamMemo);

        foreach (var group in groups)
        {
            // One identity per BeamGroup, stamped on every member — the stand-in for the
            // Beam grob pointer two stems are compared through. Running across calls so
            // the voices of one staff never collide.
            int beamId = _nextBeamId++;
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
                    NoteItem n => n with { StemUpOverride = member.MemberStemUp, BeamId = beamId },
                    ChordItem c => c with { StemUpOverride = member.MemberStemUp, BeamId = beamId },
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

            // Bake the PURE beamed stem tip on every member: the extreme of the group's
            // same-direction members' UNBEAMED stem tips. Spacing runs before any beam
            // is quanted, so LilyPond prices a beamed stem by its PURE height — the
            // calc_beam branch unites the same-direction members' unbeamed heights and
            // clips the non-stem side back to the stem's own, so the whole result is
            // the own head-side end plus this one shared tip. LilyPond caches the
            // answer per stem; this bake is that cache.
            // The cross-staff coords term (:421-436) is identically zero here: a Lily#
            // beam group never spans staves, so every member's pure Y refpoint is the
            // same and the per-member adjustment vanishes.
            // LILYPOND-REF: lily/stem.cc:387-447 Stem::internal_pure_height — :399-444
            //   the calc_beam branch; :443 iv.intersect (overshoot).
            // LILYPOND-REF: lily/stem.cc:449-458 Stem::cache_pure_height.
            double upTip = double.NegativeInfinity, downTip = double.PositiveInfinity;
            var memberBands = new List<(int Mi, int ItemIndex, bool StemUp)>();
            foreach (var member in group.Members)
            {
                int mi = member.MeasureIndex >= 0 ? member.MeasureIndex : group.MeasureIndex;
                if (mi < 0 || mi >= measures.Count
                    || member.ItemIndex < 0 || member.ItemIndex >= measures[mi].Items.Length)
                    continue;
                // The items were just stamped with their resolved directions, and their
                // PureBeamedStemTip is still unset, so this reads the UNBEAMED band.
                if (Layout.SpacingRules.StemSpacingInfo(measures[mi].Items[member.ItemIndex])
                    is not { } info)
                    continue;
                if (info.StemUp)
                    upTip = Math.Max(upTip, info.StemMax);
                else
                    downTip = Math.Min(downTip, info.StemMin);
                memberBands.Add((mi, member.ItemIndex, info.StemUp));
            }
            foreach (var (mi, itemIndex, stemUp) in memberBands)
            {
                double tip = stemUp ? upTip : downTip;
                if (double.IsInfinity(tip))
                    continue;
                var measure = measures[mi];
                MusicItem? withTip = measure.Items[itemIndex] switch
                {
                    NoteItem n => n with { PureBeamedStemTip = tip },
                    ChordItem c => c with { PureBeamedStemTip = tip },
                    _ => null,
                };
                if (withTip == null)
                    continue;
                measures[mi] = new Measure(
                    measure.Items.SetItem(itemIndex, withTip),
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

            // Bake the PURE beam-push estimate into every rest this manual beam runs
            // over, so horizontal spacing sees the rest roughly where the beam will
            // put it — spacing runs before any beam is quanted; the print later uses
            // the real collision shift (ElementCoordinator.CalculateRestShifts).
            // LILYPOND-REF: lily/beam.cc:1421-1494 Beam::pure_rest_collision_callback.
            int beamDir = group.StemUp ? 1 : -1;
            foreach (var restStem in group.RestStems)
            {
                int mi = restStem.MeasureIndex >= 0 ? restStem.MeasureIndex : group.MeasureIndex;
                if (mi < 0 || mi >= measures.Count)
                    continue;
                var measure = measures[mi];
                if (restStem.ItemIndex < 0 || restStem.ItemIndex >= measure.Items.Length
                    || measure.Items[restStem.ItemIndex] is not RestItem restItem)
                    continue;

                // beam.cc:1443-1469 left/right are the nearest stems WITH HEADS — other
                // rests are not in my_stems, so these are the flanking visible members.
                var left = group.Members[restStem.BeforeMember - 1];
                var right = group.Members[restStem.BeforeMember];

                // beam.cc:1471-1478 the closest beam is estimated four staff positions
                // past the neighbouring heads' beam-side average, and never crosses the
                // staff centre by more than two positions.
                double beamPos = ((beamDir > 0 ? left.HeadPositionMax : left.HeadPositionMin)
                        + (beamDir > 0 ? right.HeadPositionMax : right.HeadPositionMin)) / 2.0
                    + 4.0 * beamDir;
                beamPos = Math.Max(-2.0, beamPos * beamDir) * beamDir;

                // beam.cc:1480-1491 offset = beam_pos·ss/2 − minimum_distance·dir −
                // extent[dir], floored to whole staff spaces, only ever away from the
                // beam (a semibreve's default origin hangs one space up, rest.cc:101-121).
                var restBox = Layout.GlyphMetrics.GetRestBBox(restStem.NoteValue);
                double restExtentAtDir = beamDir > 0 ? restBox.Top : restBox.Bottom;
                double offsetSs = beamPos / 2.0
                    - EngravingDefaults.RestMinimumDistance * beamDir - restExtentAtDir;
                double previousSs = restStem.NoteValue == 1 ? 1.0 : 0.0;
                double shiftSs =
                    Math.Floor(Math.Min(0.0, (offsetSs - previousSs) * beamDir)) * beamDir;
                if (shiftSs == 0.0)
                    continue;

                measures[mi] = new Measure(
                    measure.Items.SetItem(restStem.ItemIndex,
                        restItem with { PureBeamShift = shiftSs * 2.0 }),
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
        {
            TieTargetScanner.Scan(v, _tieTargetWarnings);
            SlurPairingScanner.Scan(v, _unpairedSlurWarnings);
        }

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

        // Explicit `staff NAME with lyrics L` attach — no implicit auto-attach (see Collect).
        // Named blocks whose name is a `voice NAME` bind to that voice; the rest align to voice 1.
        if (attachedLyricParts is { Count: > 0 })
            _lyricsCollector.CollectAttached(root, attachedLyricParts, track0, 0,
                _lyricsRowNames, _voiceMeasuresByName, _sectionState.StartMeasure, _sectionState.AllStarts);
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

                // Per-note metadata in this sub-voice is keyed by its local 0-based
                // measure index; shift it to the span's real start so dynamics etc.
                // land in the right measure.
                _metadataMeasureOffset = start;
                // Tag this sub-voice's tuplets with its voice index so their
                // beam-breaking boundaries never leak into a sibling voice.
                _currentVoiceIndex = t;
                // Render voice number is t+1 — an override in this sub-voice scopes to it.
                _currentVoiceScope = t + 1;
                var sub = CollectMeasuresFromNode(blocks[t], applyFilePartial: start == 0,
                    leadingOffset: startOffset);
                ResolveBeamStemDirections(sub);
                _currentVoiceScope = null;
                _currentVoiceIndex = 0;
                _metadataMeasureOffset = 0;

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
        {
            TieTargetScanner.Scan(v, _tieTargetWarnings);
            SlurPairingScanner.Scan(v, _unpairedSlurWarnings);
        }
        return ResolveStaffColumns(voices.ToImmutable());
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
        var builder = new MeasureBuilder(TimeSignatureFraction, voiceNode.Position);
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
            builder.AddItem(new RestItem(offset, 0, voiceNode.Position) { IsSpacer = true });
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
        _canonicalSectionBars.Clear();
        _trillSpannerEvents.Clear();
        _courtesySourcePositions.Clear();
        _measureAccidentals.Clear();
        _fingeringByPosition.Clear();
        _tieTargetWarnings.Clear();
        _unpairedSlurWarnings.Clear();
        _openingKeyOverride = null;
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
        _defaultDots = 0;
        _meta.Reset();
        // Probe bookkeeping restarts per collect; WalkProbe itself is the caller's
        // (set before Collect, read after).
        _walkOrdinal = 0;
        _probeRecording = null;
        _resumePending = null;
        _resumeRestoredSectionStart = null;
        _walkMaxSourceRead = 0;
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
    private void EnterPhraseTranspose(int diatonicSteps = 0, int? anchorStep = null)
    {
        var saved = _octave.GetTranspose();
        _phraseTransposeSaves.Push(saved);
        if (PhraseTransposeTarget() is { } phrase)
            _octave.SetTranspose(ComposeTranspose(phrase, saved));
        // The reference's interval argument (Melody'(3)) shifts the body's pitches
        // by scale steps; nested references compose additively.
        _phraseDiatonicSaves.Push(_octave.DiatonicShiftSteps);
        _octave.DiatonicShiftSteps += diatonicSteps;
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
    /// <c>Melody'(3)</c> is relative to the phrase's shifted anchor ('(8) == ',
    /// after the phrase included) and editing the tail of a phrase body never
    /// moves the music that follows a reference. A pitchless body (rests only)
    /// hands nothing off. Only the anchoring moves; following notes still sound
    /// as written.
    /// </summary>
    private void ExitPhraseTranspose()
    {
        if (_phraseTransposeSaves.Count > 0)
            _octave.SetTranspose(_phraseTransposeSaves.Pop());
        if (_phraseDiatonicSaves.Count > 0)
        {
            int restored = _phraseDiatonicSaves.Pop();
            int delta = _octave.DiatonicShiftSteps - restored;
            _octave.DiatonicShiftSteps = restored;
            if (_phraseAnchorSaves.Count > 0 && _phraseAnchorSaves.Pop() is { } anchor)
            {
                int s = GetPitchIndex(anchor.Name);
                int o = anchor.Octave;
                if (delta != 0)
                    (s, _, o) = Music.DiatonicShift.Apply(s, 0, o,
                        delta, _meta.KeySharps - _octave.TransposeKeySharps(0));
                _octave.LastPitchName = "cdefgab"[s];
                _octave.CurrentOctave = o;
            }
        }
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
        foreach (var partDecl in root.ChildNodes().OfType<PartDeclarationSyntax>())
        {
            if (partDecl.Name.Text != partName)
                continue;
            foreach (var node in partDecl.ChildNodes())
            {
                // Direct children only; section-internal directives are walked as music.
                // Only a plain `override` is a valid part default; `revert` / `once` in a
                // part header are positional and meaningless (flagged by the validator).
                if (node is OverrideDeclarationSyntax od)
                    CollectOverride(od, 0, 0, isOnce: false, staffIndex: staffIndex);
            }
        }
    }

    /// <remarks>
    /// ⚠️ <paramref name="octave"/> AND <paramref name="explicitOctave"/> ARE NOT THE SAME
    /// QUANTITY and the caller must not use one for the other. <c>octave</c> is the RELATIVE
    /// mode's anchor and folds in the instrument preset (explicit &gt; preset &gt; clef, the
    /// chain InstrumentDefaults.AnchorOctave spells); <c>explicitOctave</c> is only what the
    /// part WROTE, and it is all that ABSOLUTE mode may see
    /// (InstrumentDefaults.AbsoluteBaseOctave). Folding them was a real defect until
    /// 2026-08-02: the preset's octave reached the absolute base, so `octave absolute` was
    /// not absolute at all. MEASURED then, one `c4` per part:
    ///   instrument bass   drew C3 and sounded C3 — the preset's −1 octave silently CANCELLED
    ///                     the instrument's own −12, so a bass sounded what it printed.
    ///   instrument flute  drew C5 and sounded C4 — a −12 on a non-transposing instrument.
    ///   instrument tuba   drew C2 and sounded C4 — the two shifts ADDED, to +24.
    ///   instrument guitar drew C4 and sounded C3 — the only correct one, and correct because
    ///                     its octave rides a treble_8 CLEF and never went through here.
    /// The written→sounding shift is one mode-independent quantity
    /// (PartHeaderDefaults.SoundingShiftSemitones); only the ANCHOR is per-mode, which is what
    /// the two modes are for. See AbsoluteModeAnchorTests.
    /// </remarks>
    // NOTE (cross-edit resume): the part-level config reads that seed a walk's entry
    // state — GetPartDefaults (clef/instrument/octave/transpose/header key) and
    // CollectPartBodyOverrides — are plan-time-checkable constants, verified by
    // CollectResumePlanner.WindowRespectsTopLevel (every part declaration's
    // non-section direct children must be content- and position-stable across the
    // edit), NOT folded into MaxSourceRead. See ProcessSection's matching note.
    private static (string? clef, int? octave, int? explicitOctave, (int step, int alt, int oct)? transpose, int clefPos, KeySignatureSyntax? key) GetPartDefaults(SyntaxNode root, string partName)
    {
        foreach (var partDecl in root.ChildNodes().OfType<PartDeclarationSyntax>())
        {
            if (partDecl.Name.Text != partName)
                continue;

            string? clef = null;
            string? instrument = null;
            int? octave = null;
            int clefPos = 0;
            (int step, int alt, int oct)? transpose = null;

            // A part-header key (`part p { key bes major … }`) is this part's default
            // key — applied per-part below, not folded into the global (file) key.
            KeySignatureSyntax? partKey = partDecl.ChildNodes()
                .OfType<KeySignatureSyntax>().FirstOrDefault();

            // Check properties for clef, instrument, octave, and transpose
            foreach (var prop in partDecl.Properties)
            {
                var propName = prop.NameToken.Text.ToLowerInvariant();
                var valueToken = prop.GetChild(2) as SyntaxTokenNode;
                if (valueToken == null) continue;

                if (propName == "clef")
                {
                    clef = valueToken.Text.ToLowerInvariant();
                    // The VALUE, not the property name: a clicked clef puts the caret
                    // on what it says (`clef: |bass`), the same rule the top-level
                    // `clef` and the time signature follow.
                    clefPos = valueToken.Span.Start;
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
                else if (propName == "octave" && prop.Value?.AsInt is int oct)
                    // Read off the typed value instead of reparsing the first token —
                    // the three branches of this loop used to interpret the same node
                    // three different ways (docs/VALUE_SITE_AUDIT.md §1.1 A3).
                    octave = oct;
            }

            transpose = PartTranspose.Read(root, partName);

            // Resolve clef: explicit > instrument > null
            string? resolvedClef = clef;
            int? resolvedOctave = octave;

            if (instrument != null)
            {
                var (defaultClef, defaultOctave) = InstrumentDefaults.GetDefaults(instrument);
                resolvedClef ??= InstrumentDefaults.ClefWord(defaultClef);
                resolvedOctave ??= defaultOctave;
            }

            return (resolvedClef, resolvedOctave, octave, transpose, clefPos, partKey);
        }

        return (null, null, null, null, 0, null);
    }

    // Applies a part-header key as THIS part's written key: mirrors the global-key
    // walk (see the KeySignatureSyntax case in CollectDefinitions) but scoped to the
    // part being collected. Returns the written (pre-transpose) sharp count so the
    // caller can transpose it like it would the global key.
    private void ApplyPartHeaderKey(KeySignatureSyntax key)
    {
        _meta.KeySharps = key.IsCustom ? 0 : CalculateKeySharps(key);
        if (!key.IsCustom)
        {
            _meta.KeyTonicStep = Math.Max(0,
                LilySharp.Core.Music.KeySpelling.StepOf(key.Pitch.PitchName[0]));
            _meta.KeyTonicAlter = key.Pitch.AccidentalOffset;
        }
        _meta.KeyCustom = key.IsCustom ? KeySignature.EncodeCustom(key.CustomAlterations) : null;
        _meta.KeyPosition = KeyDataPos(key);
    }

    private void CollectDefinitions(SyntaxNode root)
    {
        _root = root;
        List<DrummapDeclarationSyntax>? drummaps = null;

        // A top-level `clef`/`key`/`time`/`tempo` is unconditionally the FILE DEFAULT.
        // It used to depend on whether bare music had already streamed past (the whole
        // point of the retired `topLevelMusicSeen` guard): music at the top level meant a
        // later directive was that stream's mid-music change, and the same spelling
        // therefore meant "default" or "change" by position alone. Top-level music is now
        // a parse error (LYS0020), so the ambiguity — and the four ways it was got wrong —
        // cannot arise: the only mid-music directives left are inside a part/section/
        // phrase, which IsInsideMusicContent already separates.
        foreach (var node in DefinitionSites(root))
        {
            switch (node)
            {
                case DrummapDeclarationSyntax dm:
                    // Gathered here (document order) instead of a second whole-tree
                    // walk in DrumOverrides.Build(root); built after the loop — the
                    // map's readers are all in the music walk, which runs later.
                    (drummaps ??= new List<DrummapDeclarationSyntax>()).Add(dm);
                    break;

                case MetadataDeclarationSyntax metadata:
                    // A `title` / `composer` written inside a `score { … }` belongs to
                    // THAT score, not the file: it is applied below, for the score being
                    // collected only. Reading it here would make one score's header the
                    // file's and leak it into every other score (last one wins).
                    if (!IsInsideRenderDeclaration(metadata))
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
                        _meta.TimePosition = TimeDataPos(timeSig);
                    }
                    break;

                case KeySignatureSyntax key:
                    // Only process top-level key declarations (not inside phrases/sections).
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
                        _meta.KeyPosition = KeyDataPos(key);
                    }
                    break;

                case ClefDeclarationSyntax clef:
                    // Only a TOP-LEVEL `clef` declares the file default. A `clef` written
                    // inside a phrase / section is a mid-music change, engraved from its
                    // own position by the music walk (MeasureCollector.MusicWalk) — letting
                    // it land here made it the file default too, so a part that declared no
                    // clef of its own started in the CHANGED clef (wrong system-start glyph
                    // and wrong default octave, since Phase 1.5 derives both from _meta.Clef).
                    // The neighbouring key / octave / partial cases already guard this way.
                    if (!IsInsideMusicContent(clef))
                    {
                        _meta.Clef = clef.ClefName.Text.ToLowerInvariant();
                        _meta.ClefPosition = clef.ClefName.Span.Start;
                    }
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

        _drumOverrides = drummaps == null ? null : DrumOverrides.Build(drummaps);

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
            foreach (var node in root.ChildNodes())
            {
                // Only true top-level items; in-section overrides are walked.
                // Only a plain `override` is a valid global default. `revert` / `once` here
                // are positional and have no effect at the structural top level — flagged by
                // RevertContextValidator — so they are not collected.
                if (node is OverrideDeclarationSyntax od)
                    CollectOverride(od, 0, 0, isOnce: false, staffIndex: null); // global = all staves
            }
        }

        // LAST: the score being collected restates the header for itself
        // (`score sub { title "Violin I" … }`). After the file-level walk so it WINS,
        // and only for this render — the walk above skipped every render-scoped
        // metadata, so no score's header can reach another. A score that restates
        // one of the two keeps the file's other.
        if (!HeaderOverrides.IsDefaultOrEmpty)
            foreach (var meta in HeaderOverrides)
                CollectMetadata(meta);
    }

    /// <summary>True for exactly the node kinds <see cref="CollectDefinitions"/>'s
    /// switch consumes — a kind missing here silently skips its case, so the list
    /// must track the switch (the full suite plus the snapshot books are the net:
    /// every fixture book exercises the file defaults).</summary>
    private static bool IsDefinitionKind(SyntaxKind kind) => kind is
        SyntaxKind.MetadataDeclaration or SyntaxKind.FontDeclaration
        or SyntaxKind.TempoDeclaration or SyntaxKind.TimeSignature
        or SyntaxKind.KeySignature or SyntaxKind.ClefDeclaration
        or SyntaxKind.OctaveDirective or SyntaxKind.PartialDeclaration
        or SyntaxKind.SectionDeclaration or SyntaxKind.FormDeclaration
        or SyntaxKind.VariableDeclaration or SyntaxKind.PhraseDeclaration
        or SyntaxKind.DrummapDeclaration;

    /// <summary>
    /// The definitions walk's node source: every node of exactly the kinds the
    /// <see cref="CollectDefinitions"/> switch consumes, in the same pre-order
    /// <see cref="SyntaxNode.DescendantNodes()"/> yields them. Walks the GREEN
    /// tree and materializes a red node only at a match — through the parent
    /// chain's <see cref="SyntaxNode.GetChild"/>, so the yielded node carries its
    /// full Parent chain and every ancestor guard the case bodies run
    /// (IsInsideMusicContent, IsInsideRenderDeclaration, IsInsidePartMajorTrack,
    /// EnclosingPartName) works unchanged.
    /// </summary>
    /// <remarks>
    /// WHY (session 152, red-creation counters in HANDOFF §1): after the splice
    /// machinery this walk was the keystroke path's first whole-tree RED walk —
    /// materializing every red wrapper of the edited tree just to visit nodes the
    /// switch immediately ignores. The green walk visits the SAME node set (every
    /// green, tokens included, in the same order — there is no pruning decision
    /// to drift, HANDOFF §2C ⑴'s skip-list lesson) and pays a red spine only per
    /// match. ⚠️ The red-materialization cost this stops paying does not vanish
    /// for free: the next whole-tree red walker (the music walk's flat-list
    /// gather, ProcessMusicContainer) inherits first-touch creation for whatever
    /// it enumerates — measured and priced in HANDOFF §1 session 152.
    /// </remarks>
    private static IEnumerable<SyntaxNode> DefinitionSites(SyntaxNode root)
        => root.GreenSites(static g => (IsDefinitionKind(g.Kind), Descend: true));

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

    /// <summary>
    /// Where a <c>tempo</c> declaration's metronome mark points its data-pos: at the
    /// declaration's FIRST VALUE, so clicking the mark in the preview lands on the
    /// thing worth editing rather than on the keyword —
    /// <c>tempo "|Moderato" 4 = 92</c> and <c>tempo |4 = 92</c>.
    /// </summary>
    /// <remarks>
    /// The caret lands INSIDE a marking's quotes for free: a string value's own span
    /// starts at the opening quote, and the editor's jump steps over one (the same
    /// rule that puts the caret inside a title's string). Falls back to the keyword
    /// for a declaration with no values at all.
    /// <para>
    /// ⚠️ A TOKEN's Span.Start, never the declaration's Position or Span. Trivia hangs
    /// off the TOKEN here, so the declaration's own span still starts at the newline
    /// in front of it — measured: `tempo` sits at 111 in test/notes.lys and both
    /// Position and Span.Start reported 110. The editor's jump steps over spaces and
    /// tabs but deliberately never crosses a newline, so that landed a line short.
    /// </para>
    /// </remarks>
    private static int TempoDataPos(TempoDeclarationSyntax tempoDecl)
        => tempoDecl.Values.FirstOrDefault()?.Span.Start
           ?? tempoDecl.TempoKeyword.Span.Start;

    /// <summary>
    /// Where a <c>time</c> declaration's meter points its data-pos: at the NUMERATOR,
    /// so clicking the time signature in the preview lands on the value —
    /// <c>time |4/4</c>. Same rule as <see cref="TempoDataPos"/>, and the same reason
    /// for reading a TOKEN's span: the declaration's own span starts at the trivia in
    /// front of it, which would put the caret a line short.
    /// </summary>
    private static int TimeDataPos(TimeSignatureSyntax timeSig)
        => timeSig.Numerator.Span.Start;

    /// <summary>
    /// Where a <c>key</c> declaration's signature points its data-pos: at the TONIC
    /// (<c>key |f major</c>), or at the <c>custom</c> word for a custom signature.
    /// Same rule as <see cref="TempoDataPos"/> and <see cref="TimeDataPos"/>, and the
    /// same reason for reading a TOKEN's span rather than the declaration's.
    /// </summary>
    private static int KeyDataPos(KeySignatureSyntax key)
        => key.GetChild(1) switch
        {
            PitchSyntax pitch => pitch.PitchToken.Span.Start,  // key f major
            SyntaxTokenNode word => word.Span.Start,           // key custom …
            _ => key.KeyKeyword.Span.Start,
        };

    private void CollectTempo(TempoDeclarationSyntax tempoDecl)
    {
        // Every written form reaches the opening mark: `tempo 120`,
        // `tempo "Grave"`, `tempo "Grave" 120`, `tempo "Grave" 4 = 54`,
        // `tempo "Lively" 4. = 116`. The text form used to be dropped
        // silently (only a bare leading integer was read).
        //
        // ⚠️ Read the run ONCE. This method used to hold a SIXTH reading of it — a
        // step back from the `=` over the dot tokens plus a regex on the token before
        // them — beside the five on the syntax node. The two beat-unit readings
        // disagreed: on `tempo "x" = 90` this one matched nothing and silently left
        // whatever the PREVIOUS tempo had put in _meta, while TempoValue.BeatUnit says
        // a quarter, which is what the '=' with no unit means.
        var tempo = tempoDecl.Value;
        if (tempo.Bpm is int bpm)
            _meta.Tempo = bpm;
        if (tempo.Marking is string marking)
            _meta.TempoText = marking;
        _meta.TempoPosition = TempoDataPos(tempoDecl);
        // No '=' means no beat unit was written, so the standing one stays.
        if (tempo.BeatUnit is int unit)
        {
            _meta.TempoBeatUnit = unit;
            _meta.TempoDots = tempo.BeatDots;
        }
        if (tempo.SwingSubdivision != 0)
            _meta.SwingSubdivision = tempo.SwingSubdivision;
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
        if (_meta.KeyCustom != null)
        {
            foreach (var (s, a) in KeySignature.DecodeCustom(_meta.KeyCustom))
                if (s == step)
                    return a;
            return 0;
        }
        return LilySharp.Core.Music.KeySpelling.Alteration(step, _meta.KeySharps);
    }

    /// <summary>The accidental glyph name ("doubleSharp" / "sharp" / "natural" / "flat" /
    /// "doubleFlat") the current key signature dictates for diatonic <paramref name="step"/>.
    /// Forced onto a note that shows none when it is made a courtesy or editorial accidental.</summary>
    private string KeySignatureAccidentalName(int step) => GetKeySignatureAlteration(step) switch
    {
        >= 2 => "doubleSharp", 1 => "sharp", <= -2 => "doubleFlat", -1 => "flat", _ => "natural"
    };

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
    private string? GetDisplayAccidental(int step, int actual, int octave)
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

        if (actual == inEffect)
            return null;

        // RESTORE-FIRST: stepping DOWN within the same sign (𝄪→♯, 𝄫→♭) prepends a
        // natural to the printed accidental. The default accidental style reads
        // extraNatural = #t, which is what gates the restore onto the grob — Lily#
        // ports only that default style, so the gate is constant here.
        // LILYPOND-REF: scm/music-functions.scm:1746-1752 check-pitch-against-signature —
        //   need-restore = this-alt ≠ 0 ∧ |this-alt| < |prev-alt| ∧ prev-alt·this-alt > 0;
        // LILYPOND-REF: scm/music-functions.scm:1909-1911 accidental-styles `default`
        //   (extraNatural #t); lily/accidental-engraver.cc:272-275 — restore-first is set
        //   only when extraNatural holds.
        // The composite travels as a NAME ("naturalSharp"/"naturalFlat") so every box,
        // skyline and draw consumer reads the composed stencil through the same pipes a
        // plain glyph takes — see GlyphMetrics.RestoreMainOf.
        bool restore = actual != 0
            && Math.Abs(actual) < Math.Abs(inEffect)
            && inEffect * actual > 0;

        return actual switch
        {
            2 => "doubleSharp",
            1 => restore ? "naturalSharp" : "sharp",
            0 => "natural",
            -1 => restore ? "naturalFlat" : "flat",
            -2 => "doubleFlat",
            _ => null
        };
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
            if (ov.StaffIndex is null || ov.StaffIndex == _currentStaffIndex)
                _sectionResetOverrides[(ov.GrobType, ov.PropertyName)] = ov.Value;
        _sectionActiveGrobProps.Clear();

        // Arm the ambient tonic at the score's home key for this voice's walk
        // (phrase auto-transpose baseline).
        ResetAmbientTonicToHome();
        _phraseTransposeSaves.Clear();
        _phraseDiatonicSaves.Clear();
        _phraseAnchorSaves.Clear();
        _octave.DiatonicShiftSteps = 0;

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
                // Only the TARGET section's invocations (or the section-less root
                // path's) reach here — earlier sections return whole at
                // ProcessSection's entry gate. An invocation before the target is
                // wholly inside the adopted prefix.
                if (invocation < target.Invocation)
                    return;
                if (invocation > target.Invocation)
                    throw new CollectResumeAbortException(
                        $"collect resume overshot its target invocation ({invocation} > {target.Invocation})");
                // Cross-edit address revalidation: the prefix text is unchanged, so
                // an unchanged walk-order address holds a node with an unchanged
                // start. Anything else is structural drift — bail to a full collect.
                // (Site.Position == Node.FullSpan.Start, no red materialized.)
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
                    && targets.TryGetValue(
                        (_sectionVisit - 1, invocation, site.Position), out var spliceTarget)
                    && TrySpliceSuffix(spliceTarget, builder))
                    return;

                // Phrase-reference boundary: evaluate the body in the default
                // frame (same handling as ProcessMusicNodeSequence). The boundary
                // re-arms the confirmable boundary so an edge barline of the phrase
                // body does not pair with an adjacent outer barline into an empty bar.
                // Kind None belongs to the synthetic markers alone (their reds are
                // preset — no gather kind is None), so real sites skip both type
                // tests on the kind read.
                if (site.Kind == SyntaxKind.None)
                {
                    if (site.Node is RelativeResetMarker reset)
                    {
                        EnterDefaultFrame(reset.OctaveOffset);
                        EnterPhraseTranspose(reset.DiatonicSteps, reset.AnchorStep);
                        builder.ResetMeasureBoundary();
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
                // the adopted prefix (RecordSectionStart via the checkpoint's section
                // maps, the label via the builder state) — re-running it here would
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
    private IList[] CumulativeSideTables() => new IList[]
    {
        _dynamics, _articulations, _graceNotes, _musicMarks, _customTexts,
        _voltaBrackets, _tupletBrackets, _arpeggios, _figuredBasses,
        _percentRepeats, _crossStaffItems, _grobOverrides, _grobReverts,
        _trillSpannerEvents, _pitchTrace, _navPlacementWarnings,
        _tieTargetWarnings, _unpairedSlurWarnings, _chordNameCollector.ItemsList,
    };

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
            && _cueDepth == 0 && _currentVoiceScope == null && _metadataMeasureOffset == 0
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
        // just to decline. An EMPTY window (a pure position shift — prefix ==
        // suffixStart) overlaps nothing: a measure straddling the insertion
        // point shifts per-position and is fine.
        if (w.Prefix < w.SuffixStart)
        {
            for (int i = ck.MeasureCount; i < pre.Count; i++)
            {
                if (pre[i].SourceStart < w.SuffixStart && pre[i].SourceEnd > w.Prefix)
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
            && _meta.TextFont == rec.TextFont
            && _meta.EmbedFont == rec.EmbedFont
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
                            && m.SourcePosition == nav.Position))
                        _musicMarks.Add(new MusicMarkItem(navMark, navMeasure, nav.Position));
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

    /// <summary>
    /// Rows-only scores reach row collection with an EMPTY section-start
    /// table — sections normally register while MUSIC is processed. Derive
    /// the starts from the row blocks themselves: walk the structure (or
    /// declaration) order, advancing by each section's widest row block
    /// (chord bars preferred, lyric bars otherwise). Without this a
    /// two-section chord grid printed both sections' symbols from bar 0,
    /// overlapped. No-op when music already filled the table.
    /// </summary>
    private void EnsureSectionStartsForRows(SyntaxNode root)
    {
        // Not `Sections.Count == 0`: a rows-only score's sections live INSIDE the chord / lyric
        // tracks (chords X { section A { … } }) and are deliberately kept out of the structure
        // Sections map, so bailing on an empty map stacked every section at bar 0.
        if (_sectionState.StartMeasure.Count > 0)
            return;

        // Walk the structure's children IN SOURCE ORDER so navigation marks
        // (segno / to coda / D.S. …) interleave with the section references at
        // the right bars — a rows-only score never runs ProcessForm, so
        // the band grid lost exactly the signs a band chart needs. Labels are
        // stamped onto the grid row's measures afterwards.
        int cur = 0;
        void AdvanceSection(string name, string? label, int pos)
        {
            int secBars = RowGridSectionBars(root, name);
            // An unknown name — neither a track cell nor a structure section — has nothing to place.
            if (secBars == 0 && !_sectionState.Sections.ContainsKey(name))
                return;
            if (!_sectionState.StartMeasure.ContainsKey(name))
            {
                _sectionState.StartMeasure[name] = cur;
                if (label != null)
                    _sectionState.RowLabels.Add((cur, label, pos));
            }
            cur = _sectionState.StartMeasure[name] + secBars;
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

    /// <summary>
    /// The bar span section <paramref name="name"/> occupies in the chord / lyric ROW grid. A
    /// rows-only score never runs ProcessForm, so the section starts are laid out from here — and
    /// the section must be counted however it is written: as a part-major chord / lyric TRACK
    /// inner section (<c>chords X { section NAME { … } }</c>), whose bars live on the section
    /// itself (the block is its ancestor, not a descendant), OR as chord / lyric blocks nested in
    /// a section-major section. The descendant-only count missed the track form, so a rows-only
    /// score with several sections stacked every section at bar 0.
    /// </summary>
    private int RowGridSectionBars(SyntaxNode root, string name)
    {
        int bars = 0;

        // Part-major TRACKS: the section sits INSIDE the chord / lyric block.
        foreach (var block in root.KindSites(SyntaxKind.ChordPartBlock).OfType<ChordPartBlockSyntax>())
            if (block.HasSections)
                foreach (var sec in block.Sections)
                    if (sec.SectionName == name)
                        bars = Math.Max(bars, ChordNameCollector.CountSectionBars(sec));
        foreach (var block in root.KindSites(SyntaxKind.LyricsBlock).OfType<LyricsBlockSyntax>())
            if (block.HasSections)
                foreach (var sec in block.Sections)
                    if (sec.SectionName == name)
                        bars = Math.Max(bars, LyricSyllableReader.CountBars(sec));

        // Section-major: the chord / lyric blocks are nested in the (registered) section itself.
        if (_sectionState.Sections.TryGetValue(name, out var representative))
        {
            foreach (var block in representative.KindSites(SyntaxKind.ChordPartBlock).OfType<ChordPartBlockSyntax>())
                bars = Math.Max(bars, ChordNameCollector.CountBars(block));
            foreach (var block in representative.KindSites(SyntaxKind.LyricsBlock).OfType<LyricsBlockSyntax>())
                bars = Math.Max(bars, LyricSyllableReader.CountBars(block));
        }

        return bars;
    }

    private static bool IsInsideRepeatBlock(SyntaxNode node) => node.IsInside<FormRepeatBlockSyntax>();

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
            or ChordRepetitionSyntax or ArpeggioSyntax
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
            or SyntaxKind.ChordRepetition or SyntaxKind.Arpeggio
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
                    _dynamics.Add(new DynamicItem(level, measureIndex, itemIndex, dynamicSyntax.Position, _currentStaffIndex)
                    {
                        IsAbove = dynamicSyntax.ForcedAbove == true,
                        VoiceIndex = _currentVoiceIndex,
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
                            dynamicSyntax.Position, itemIndex) { StaffIndex = _currentStaffIndex });
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
                ExpandVariable(varRef.Name.Text, varRef.OctaveOffset, bodyNodes, varRef.DiatonicShiftSteps);
            else
                bodyNodes.Add(new GreenSite(item));
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
            && bodyNodes.All(b => b.Node is NoteSyntax or ChordSyntax or ChordRepetitionSyntax)
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
            // For volta/unfold (and non-printable tremolo shapes): unfold the
            // body count times.
            for (int i = 0; i < count; i++)
            {
                ProcessBodyOnce();
            }
        }
    }

    /// <summary>The written duration value of a note or chord (0 when it declares none, or
    /// the node is neither) — the base a tremolo's total duration is computed from.</summary>
    private static int NoteOrChordDurationValue(SyntaxNode n) => n switch
    {
        NoteSyntax ns => ns.Duration?.Value ?? 0,
        ChordSyntax cs => cs.Duration?.Value ?? 0,
        ChordRepetitionSyntax rep => rep.Duration?.Value ?? 0,
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

        foreach (var item in grace.Body.Items)
        {
            if (item is NoteSyntax note)
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
