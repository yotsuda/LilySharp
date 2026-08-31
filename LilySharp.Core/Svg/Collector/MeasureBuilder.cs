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
/// Helper class for building measures from syntax nodes.
/// Supports both explicit barlines and automatic measure detection based on time signature.
/// </summary>
internal sealed class MeasureBuilder
{
    private readonly List<Measure> _measures = new();
    private readonly List<MusicItem> _currentItems = new();

    // True when the current measure boundary can absorb ONE confirming bare barline
    // silently: an AUTO-FILL close (duration reached the meter; the following `|` merely
    // confirms what the meter already closed), a phrase EXIT that left a closed bar, and
    // a barline the FORM synthesised (ArmBoundaryForStructuralBarline).
    // False after any WRITTEN barline consumed the boundary — a written close, a typed
    // decoration, an absorbed confirmation, a placeholder — so a bare `|` there is the
    // second of a `| |` PAIR and opens an empty placeholder measure (which the engine
    // then FILLS with a full-measure spacer — EmitEmptyMeasure).
    // ⚠️ A SCOPE START IS *NOT* CONFIRMABLE (owner's decision, 2026-08-28). It was until
    // that day: a leading `|` "anchored" the start and created nothing, so `| c1` was
    // `c1` and `section A { | | | | }` was THREE bars, not four. The author's own books
    // read the other way — `君の恋人になったら` is written four bars to the line
    // throughout and only the two blocks that OPEN with `|` came out a bar short — and
    // amazing-grace's chord row, which writes a leading `|` for the pickup bar that
    // carries no chord, printed every chord one bar early. The rule is now the one
    // sentence the language can state: A WRITTEN `|` CLOSES EXACTLY ONE MEASURE, and a
    // measure with nothing in it is an empty one. The block END still closes nothing —
    // that is what keeps a trailing `c1 |` one bar (497 tracked books spell it).
    private bool _confirmableBoundary;
    // True at a SCOPE START — the piece, a section, a phrase body — until the first
    // barline or emitted measure. Only the `|:` arm of HandleBarline reads it, and only
    // to keep `|:` from inventing a bar there: a `|` CLOSES the span a scope opens (so
    // `{ | | | | }` is four bars), but `|:` closes nothing, so `{ |: c1 :| }` is one.
    private bool _atScopeStart = true;
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

    /// <summary>
    /// The items of the measure now under construction, oldest first — a READ-ONLY view, so
    /// a caller can look at what it just added without being able to reorder the measure.
    /// </summary>
    /// <remarks>
    /// The one reader is <c>MeasureCollector.DeriveGraceColumns</c>, which reads back the
    /// columns the grace walk added rather than parsing the grace body a second time.
    /// </remarks>
    public IReadOnlyList<MusicItem> CurrentItems => _currentItems;

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

    /// <summary>Re-arms the confirmable boundary for a barline the FORM emits, so it
    /// CONFIRMS the boundary instead of pairing with the barline the previous section
    /// wrote.</summary>
    /// <remarks>
    /// A form repeat synthesises its barlines into the music stream
    /// (<c>MeasureCollector.Form.cs</c> ProcessRepeatBlockCore), so by the time they reach
    /// <see cref="HandleBarline"/> they are indistinguishable from ones the author typed —
    /// and the section before them has usually closed its last bar with a written <c>|</c>,
    /// which leaves the boundary consumed. Without this, <c>form main { A |: D :| }</c> read
    /// as a written <c>| |:</c> PAIR and opened an empty bar between every section and every
    /// form-level repeat sign (measured: 3 bars became 5). The pair rule is about two
    /// barlines written NEXT TO EACH OTHER in one music stream; a structural barline is not
    /// the second of any such pair, whatever the state of the section that preceded it.
    /// </remarks>
    public void ArmBoundaryForStructuralBarline() => _confirmableBoundary = true;
    /// <summary>Re-arms the auto-complete measure length without printing a grob
    /// (used when a leading meter change collapses into the initial time signature).</summary>
    public void SetMeasureLength(Fraction length) => _timeSignature = length;

    /// <summary>Settles the measure boundary at a section/phrase edge. A section or phrase
    /// START passes <paramref name="retargetableClose"/> false: the boundary is CONSUMED,
    /// so a <c>|</c> opening that scope closes an empty measure like any other written
    /// barline (owner's decision, 2026-08-28 — see <see cref="_confirmableBoundary"/>).
    /// A phrase EXIT passes true: if the phrase ended with a CLOSED bar (its own trailing
    /// <c>|</c>, or an auto-fill), an outer <c>|</c> confirms it, owns that barline, and
    /// retargets the phrase's last measure onto the written <c>|</c>.</summary>
    public void ResetMeasureBoundary(bool retargetableClose = false)
    {
        _boundaryRetargetable = retargetableClose
            && _currentItems.Count == 0 && _measures.Count > 0;
        // The exit's confirmation and its retarget are THE SAME FACT — there is a bar
        // just closed here to attach to — so they are read off one condition.
        _confirmableBoundary = _boundaryRetargetable;
        _atScopeStart = !retargetableClose; // a START opens a scope; an EXIT closes one
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
    /// How deep into <c>grace { }</c> bodies this builder currently is. While it is
    /// non-zero every added item is stamped <see cref="MusicItem.GraceTime"/> and takes no
    /// measure time.
    /// </summary>
    private int _graceDepth;

    /// <summary>Whether the builder is currently inside a <c>grace { }</c> body.</summary>
    public bool InGraceTime => _graceDepth > 0;

    /// <summary>
    /// Opens grace time: from here until the matching <see cref="ExitGraceTime"/> every item
    /// added is stamped <see cref="MusicItem.GraceTime"/> and adds NO measure duration.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE SWITCH IS HERE, NOT AT THE HUNDRED-ODD <c>AddItem</c> CALL SITES. A grace body
    /// is walked by the ORDINARY walker (<c>MeasureCollector.ProcessGraceRegion</c>), so every
    /// arm of that walk reaches this method; routing the duration decision at the one place
    /// that owns the clock is what lets the walk itself stay unchanged, which is the whole
    /// point of walking it (see <see cref="MusicItem.GraceTime"/> for LilyPond's shape).
    /// <para>
    /// ⚠️ A DEPTH, NOT A FLAG: <c>grace { }</c> cannot nest in Lily# today, but the reset in
    /// <c>MeasureCollector.Resume</c> checkpoints on the counter being zero, and a counter
    /// says "unbalanced" where a bool would silently forgive it.
    /// </para>
    /// </remarks>
    public void EnterGraceTime() => _graceDepth++;

    /// <summary>Closes the region <see cref="EnterGraceTime"/> opened.</summary>
    public void ExitGraceTime()
    {
        if (_graceDepth > 0)
            _graceDepth--;
    }

    /// <summary>
    /// Reduces an item entering grace time to what a grace column can still be engraved
    /// from, and stamps it <see cref="MusicItem.GraceTime"/>.
    /// </summary>
    /// <remarks>
    /// ⚠️ A WHITELIST, NOT A LIST OF THINGS TO STRIP, and that is the whole point. The
    /// ordinary factories fill a note with everything the ordinary engravers can draw —
    /// fingering, laissez-vibrer, repeat tie, notehead style, editorial accidental, tremolo,
    /// courtesy, feather — and a grace group is still drawn from its derived
    /// <c>GraceNoteItem</c>, which reads exactly SEVEN of those fields. A blacklist would go
    /// stale the first time somebody adds an eighth kind of ink to a note: the new field
    /// would ride into grace time, some engraver would draw it full size over columns that
    /// are not drawn, and the reader would still be told by LYS4020 that it was dropped.
    /// Naming what SURVIVES cannot go stale that way — a new field is absent by default.
    /// <para>
    /// ⚠️ SCAFFOLDING, and the most temporary piece of it: everything this drops is on
    /// LYS4020's drop list, and HANDOFF §2 U8 ⒝2 retires the whole method by letting the
    /// ordinary engravers draw grace time at the grace font — at which point a grace note is
    /// an ordinary note that happens to carry <see cref="MusicItem.GraceTime"/>, and narrowing
    /// it would be the defect rather than the fix.
    /// </para>
    /// <para>
    /// The seven fields are the ones <c>MeasureCollector.DeriveGraceColumns</c> reads:
    /// staff position, accidental, ledger, base duration, dots, MIDI and string number — plus
    /// the source position, which is click-to-source data no engraver reads.
    /// </para>
    /// </remarks>
    private static MusicItem NarrowToGraceTime(MusicItem item) => item switch
    {
        NoteItem n => new NoteItem(
            n.StaffPosition, n.BaseDuration, n.Dots, n.Accidental, n.NeedsLedgerLines,
            n.SourcePosition)
        {
            GraceTime = true,
            Midi = n.Midi,
            StringNumber = n.StringNumber,
        },

        ChordItem c => new ChordItem(
            // The members keep the same seven answers and nothing else — a member can carry
            // its own fingering and its own notehead style, and both are drops here too.
            c.Notes.Select(static m => new ChordNoteInfo(
                m.StaffPosition, m.Accidental, m.NeedsLedgerLines,
                StringNumber: m.StringNumber, Midi: m.Midi,
                SourcePosition: m.SourcePosition)).ToImmutableArray(),
            c.BaseDuration, c.Dots, c.SourcePosition)
        {
            GraceTime = true,
        },

        RestItem r => new RestItem(r.BaseDuration, r.Dots, r.SourcePosition)
        {
            GraceTime = true,
            IsSpacer = r.IsSpacer,
        },

        _ => item with { GraceTime = true },
    };

    /// <summary>
    /// Adds a music item and automatically completes the measure if duration is reached.
    /// </summary>
    public void AddItem(MusicItem item)
    {
        // GRACE TIME TAKES NO MEASURE TIME. LilyPond's grace notes live in a negative
        // "grace part" of the moment and the main stream's clock does not see them
        // (LILYPOND-REF: lily/moment.cc — Moment's grace_part_); Lily# says the same by
        // routing the whole add through the no-duration door. The stamp rides the item so
        // the readers downstream can tell a grace column from a main one without asking
        // where in the item list it stands.
        if (_graceDepth > 0)
        {
            AddItemWithoutDuration(item);
            return;
        }

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
    /// <remarks>
    /// ⚠️ THE GRACE STAMP GOES ON HERE, NOT IN <see cref="AddItem"/>, because this is the
    /// COMMON SINK: a tuplet's members come through here directly (their duration is
    /// accounted separately), and stamping only in AddItem let a tuplet inside a grace body
    /// put UNSTAMPED, full-size notes on the main column grid — the group then drew twice and
    /// the bar grew by half its width. Every item that enters a measure enters through this
    /// one line.
    /// </remarks>
    public void AddItemWithoutDuration(MusicItem item)
    {
        _currentItems.Add(_graceDepth > 0 ? NarrowToGraceTime(item) : item);
        _confirmableBoundary = false;
    }

    /// <summary>
    /// Adds duration and triggers auto-completion if time signature is reached.
    /// Used after processing tuplet notes with scaled duration.
    /// </summary>
    public void AddDuration(Fraction duration, int sourcePosition)
    {
        // GRACE TIME TAKES NO MEASURE TIME — the same rule AddItem applies, at the other door
        // into the clock. A tuplet reports its scaled duration here after its members have
        // been added, and a tuplet inside a grace body would otherwise advance the bar by the
        // sounding length of music the bar does not contain.
        if (_graceDepth > 0)
            return;

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
        _atScopeStart = false; // a measure was emitted: the scope's opening span is over
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
        bool atScopeStart = _atScopeStart;
        _atScopeStart = false; // any written barline ends the scope's opening span
        if (barType == BarlineType.RepeatStart)
        {
            // |: opens the NEXT measure; close anything pending first. A directive-only
            // span (a leading clef) is NOT closed — it carries into the first repeat bar.
            if (HasMeasureContent)
            {
                CompleteMeasure(position, BarlineType.Single);
            }
            else if (!_confirmableBoundary && !atScopeStart)
            {
                // THE SECOND OF A PAIR: `… | |: …`. Two written barlines with nothing
                // between them is an empty measure, and `|:` is no exception — owner's
                // decision, 2026-08-28, reported against a `partial` pickup written
                // `c8 | |: c'4 d e f :|` whose middle bar was not drawn.
                // ⚠️ THIS IS WHY `|:` IS NOT ONE OF THE "TYPED" BARLINES BELOW. `||`,
                // `|.` and `:|` on an empty span DECORATE the bar behind them — they
                // retro-type its end and create nothing. `|:` decorates nothing: it
                // OPENS the bar in front of it, so the span before it has no owner and
                // is exactly the gap `| |` describes. Sorting it with the decorations
                // was the category error, and the two spellings answered differently
                // (`c8 | | …` drew the gap, `c8 | |: …` swallowed it) with nothing in
                // the language to explain why.
                EmitEmptyMeasure(position, BarlineType.Single);
            }
            // …and a boundary with no span for `|:` to leave unowned — a bar this stream
            // just closed (CONFIRMABLE), or THE SCOPE START — is merely ANCHORED by it.
            // ⚠️ THE SCOPE-START ARM IS WHY `_atScopeStart` EXISTS, and it is the one place
            // `|` and `|:` must answer differently now that a scope start is not
            // confirmable: `|` CLOSES the empty span the scope opened (that is the whole
            // point of the 2026-08-28 decision), while `|:` closes nothing — it opens the
            // bar in front of it — so `|: c'4 d e f :|` is one bar, not an empty one and
            // then the repeat. Written out: a span becomes an empty measure when a written
            // `|` CLOSES it, or when a `|:` leaves it unowned AFTER a written bar opened it.
            _pendingStartBarline = BarlineType.RepeatStart;
            // The `|:` IS the next measure's start boundary — record its offset so the
            // drawn start barline's click/highlight lands on the written `|:`, not on
            // the previous close (SourceStart otherwise carries the last SourceEnd).
            _measureSourceStart = position;
            // A WRITTEN barline consumed the boundary, so a further bare `|` after this
            // one is the second of a pair (`|: |` opens a gap, exactly as `| |` does).
            _confirmableBoundary = false;
            _boundaryRetargetable = false;
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
            // A bare `|` merely CONFIRMS the boundary it sits on — a bar this stream just
            // closed by AUTO-FILL, a phrase exit that left one closed, or a barline the
            // form synthesised. Record the `|` as an end source (retargeting the click to
            // it and keeping the bar's own close, if written, as a highlight alias). A
            // FURTHER bare `|` with no closed bar to attach to is the second of a `| |`
            // pair (the else branch). ⚠️ A SCOPE START NO LONGER LANDS HERE: it is not
            // confirmable, so a leading `|` closes an empty measure (see the field).
            if (_boundaryRetargetable && _measures.Count > 0)
                AddEndBarlineSource(position);
            _confirmableBoundary = false;
            _boundaryRetargetable = false;
        }
        else
        {
            // The second barline of a `| |` PAIR (nothing between two written bars) —
            // mid-piece or trailing — opens a real placeholder measure: it holds a slot so
            // other parts stay aligned and renders as an empty bar. The engine then FILLS
            // it (EmitEmptyMeasure) with a full-measure spacer, so it is not reported: an
            // empty measure is still always VISIBLE in the source as `| |`, but the author
            // no longer has to answer for it.
            EmitEmptyMeasure(position, endType);
        }
    }

    /// <summary>
    /// Emits a placeholder measure for a bare barline gap — one full measure of SPACER,
    /// drawing nothing. See <see cref="Measure.IsEmptyPlaceholder"/>.
    /// </summary>
    private void EmitEmptyMeasure(int sourceEnd, BarlineType endType)
    {
        // A pending break/nobreak belongs to THIS measure (as in EmitMeasure) — a `break`
        // just before a bare `|` breaks after the placeholder, not the next real bar.
        bool hasBreak = _pendingBreak;
        bool noBreak = _pendingNoBreak;
        _pendingBreak = false;
        _pendingNoBreak = false;

        // THE BAR IS FILLED, not left empty: one full-measure SPACER, which is the `s1`
        // (or `s2.`, or whatever the meter's own length spells) the author would otherwise
        // have had to type. Owner's decision, 2026-08-28.
        // ⚠️ IT IS THE METER IN FORCE, not a whole note — `_timeSignature` is the running
        // measure length, so a 3/4 gap is worth a dotted half and a `partial` pickup's gap
        // is worth the shortened bar it opens.
        // ⚠️ WHY A SPACER RATHER THAN NOTHING, and it is not the page: the layouter walks
        // BARS, so an item-less placeholder already aligned across parts correctly. The MIDI
        // exporter walks DURATIONS, so a zero-length bar let every later note in that part
        // sound a whole bar EARLY against the others — measured on a two-staff book, the
        // upper part's third bar started at tick 1920 where the lower part's started at
        // 3840. A spacer is invisible and never collapses into a multi-measure rest
        // (MusicItem.IsSpacer), so nothing is drawn that was not drawn before.
        // ⚠️ THE DIRECTIVES IN THE SPAN COME WITH IT. A span with no DURATION can still
        // hold zero-duration items — a `time`/`key` change written just before the bar —
        // and this measure is where the author put them. Dropping them was a real defect,
        // reachable before 2026-08-28 as `c1 | time 3/4 | | c2.` (measured: the 3/4 was
        // never drawn and never took effect) and reachable far more easily after it,
        // since a scope may now open `time 3/4 | …`.
        var spacer = new RestItem(_timeSignature, 0, _measureSourceStart) { IsSpacer = true };
        _measures.Add(new Measure(
            _currentItems.Count == 0
                ? ImmutableArray.Create<MusicItem>(spacer)
                : _currentItems.Append(spacer).ToImmutableArray(),
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

