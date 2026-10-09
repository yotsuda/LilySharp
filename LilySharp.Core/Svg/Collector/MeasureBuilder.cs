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

    // ---- the collector's look-ahead (session 550, HANDOFF ⒮²³) ----
    // The WRITTEN bar line that immediately follows the site being processed — its token
    // start, or -1 — and its type. An auto-fill that closes the bar while this is set emits
    // the measure with THAT source end (and a typed bar's type) from the start, so the bar's
    // own confirmation (AddEndBarlineSource's auto-fill arm, the typed retro-apply in
    // HandleBarline) finds the value already there and writes nothing: the emit-then-rewrite
    // `with` copy was 96.8 + 1.1 Measure objects a keystroke on the reader's corpus (session
    // 511's construction census), each 96 B.
    // ⚠️ AN EARLY WRITE OF THE SAME VALUE, NOTHING MORE. HandleBarline still runs every arm it
    // ran; the two rewrites now compare first, so a bar the walk does not reach the way the
    // hint expected rewrites the value exactly as before. The collector sets it only for a
    // note / rest / chord site whose NEXT site is the bar (never `|:`, whose arm does not
    // retarget), and clears it after the site. A resume sees no new dependency: the
    // checkpoint captured between the note and its bar already has the bar's span in its
    // read watermark (ProcessNodes folds the marker peek's terminator — the bar).
    private int _followingBarlineStart = -1;
    private BarlineType _followingBarlineType;
    // ...and the ONE break directive read ahead right AFTER that bar (`| break`, the corpus's
    // spelling: 6,106 sites to 3 of `break |`), or null (session 551). The emit writes the
    // measure's permissions as that directive's boundary-time setter (SetBreak / SetNoBreak /
    // SetPageBreak / SetNoPageBreak) would write them; the setter then finds the value there.
    // ⚠️ ONE DIRECTIVE, NOT THE RUN: the setters run in order and each writes its own value,
    // so an emit holding the FINAL value of `| break noBreak` is still rewritten by the first
    // setter (it finds Forbid and writes Force). A second directive writes as it always did.
    private BreakKind? _followingBreak;

    /// <summary>The bar line the collector has read ahead — see the field.</summary>
    internal void SetFollowingBarline(int tokenStart, BarlineType type)
    {
        _followingBarlineStart = tokenStart;
        _followingBarlineType = type;
        _followingBreak = null;
    }

    /// <summary>The break directive the collector has read ahead right after that bar.</summary>
    internal void SetFollowingBreak(BreakKind kind) => _followingBreak = kind;

    /// <summary>The site is done: no bar is read ahead any more.</summary>
    internal void ClearFollowingBarline()
    {
        _followingBarlineStart = -1;
        _followingBreak = null;
    }

    /// <summary>The boundary read ahead, for a site that adds SEVERAL bar-closing items
    /// (<c>R1*N</c>) and must hand it to the LAST one only — the start is -1 when none.</summary>
    internal (int Start, BarlineType Type, BreakKind? Break) FollowingBoundary
        => (_followingBarlineStart, _followingBarlineType, _followingBreak);

    /// <summary>Puts back what <see cref="FollowingBoundary"/> read, for that last item.</summary>
    internal void RestoreFollowingBoundary((int Start, BarlineType Type, BreakKind? Break) boundary)
    {
        _followingBarlineStart = boundary.Start;
        _followingBarlineType = boundary.Type;
        _followingBreak = boundary.Break;
    }

    /// <summary>
    /// How many emitted measures this thread's builders have REWRITTEN at their boundary
    /// (a `with` copy of <c>_measures[^1]</c> for a bar's source end or type) — the liveness
    /// meter of the look-ahead above, for <c>MeasureBuilderLookAheadTests</c> alone.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE LOOK-AHEAD IS INVISIBLE IN EVERY OUTPUT BY DESIGN — a wrong hint is rewritten to
    /// the right value and the page is the same (session 550's off-by-one poison: 0 pages
    /// moved, 8,913 tests green, and the whole saving gone). The pages cannot say whether
    /// the saving is still there; this count can (RULES §5.4, the page-buffer pool's lesson:
    /// an equality half and a liveness half).
    /// </remarks>
    [System.ThreadStatic]
    internal static int t_boundaryRewrites;

    // The meter in force, `time none` and the pickup pending — Semantics.BarContext, the one
    // spelling every output reads (REFACTOR_PLAN stage C5, the page last). What the bar in
    // progress is worth is _bars.BarLength: the pickup while one is pending, else one bar of the
    // meter.
    private readonly BarContext _bars = new();
    private Fraction _currentDuration = Fraction.Zero;
    // LilyPond's measurePosition where the last emitted bar ENDS: zero when the bars so far
    // filled the meter, not zero after a bar closed short (a written `|` is only a bar check
    // there, so LilyPond's bars run on across it). FinalizeMeasures reads it: music that stops
    // off the bar gets no bar line after it.
    private Fraction _barPosition = Fraction.Zero;
    // The LilyPond measurePosition where the current volta group's FIRST ending began: each
    // ending but the last hands the position back to it (BeginAlternatives / EndAlternative).
    private Fraction? _alternativeStart;
    // What the last hand-back moved the position by (undone if the group closes after it: the
    // last ending does not hand back), and whether an ending's music is being walked now.
    private Fraction? _alternativeUndo;
    private bool _inEnding;
    // True while a note, rest or chord (or a tuplet's reported duration) has entered the
    // measure under construction. Read where "is there music in this span" is asked —
    // HasMeasureContent, AtPieceOpening — instead of `_currentDuration > 0`, because under
    // `time none` the clock is FROZEN (see _frozenPosition) and a cadenza's notes add no
    // duration: the span still holds music, and a written `|` still has a bar to close.
    private bool _hasMeasureContent;

    // `time none` — senza misura (_bars.SenzaMisura). LilyPond's \cadenzaOn is `\set Timing.timing = ##f`
    // (LILYPOND-REF: ly/property-init.ly cadenzaOn), `timing` is "keep administration of
    // measure length, position, bar number, etc.?" (scm/define-context-properties.scm), and
    // the administration it switches off is one function: LILYPOND-REF:
    // lily/timing-translator.cc:478-507 Timing_translator::start_translation_timestep — with
    // `timing` false, measurePosition is not advanced by the elapsed moment and
    // currentBarNumber is not incremented. So while it is set here: the clock does not
    // advance (a cadenza's notes add nothing to _currentDuration, exactly as LilyPond's
    // measurePosition stands still), no measure auto-completes (no measureLength to reach),
    // and every measure a WRITTEN barline closes is stamped Measure.Unmetered so the bar
    // number does not advance across it. _bars.Meter keeps the meter written before it,
    // for the `| |` placeholder's spacer. Cleared by the next `time N/M`.
    // MEASURED (2.26.0, scratch/p354/lp/senza-fixed.ly): `\cadenzaOn … \bar "|" … \bar "|"
    // \break \cadenzaOff \time 4/4 c'1 |` draws the two written bars, breaks where told, and
    // numbers the next line 2 — the cadenza and the bar after it share one number.

    // The clock's reading when `time none` froze it — LilyPond's measurePosition, which
    // start_translation_timestep leaves alone while `timing` is off and which nothing in the
    // cadenza resets (not its written `\bar "|"`, not \cadenzaOff). Zero when the span opened
    // at a bar line. Stamped on every unmetered measure (Measure.UnmeteredPosition) for the
    // one reader that asks the clock inside a cadenza: the auto-beam check. Cleared by the
    // next metered `time`. MEASURED (2.26.0, scratch/p359/lp/midbar-8th.ly): `c'8 d \cadenzaOn
    // e8 f g a b c d e f4 g` is ONE Beam grob over the ten eighths — the beat check reads 1/4
    // at every stem and 1/4 ends no eighth beam in 4/4 — where `c'4 d \cadenzaOn e8 …` (frozen
    // at 1/2, a beam end) makes none.
    private Fraction _frozenPosition = Fraction.Zero;

    private BarlineType _pendingStartBarline = BarlineType.None;
    private BarlineType _pendingEndBarline = BarlineType.None;
    private bool _pendingBreak = false;
    private bool _pendingNoBreak = false;
    // `pageBreak` / `noPageBreak` written mid-measure, deferred to the boundary like the
    // line pair above (a `pageBreak` also raises _pendingBreak: the line break it implies).
    private bool _pendingPageBreak = false;
    private bool _pendingNoPageBreak = false;

    // ---- a line break INSIDE a bar (Measure.BreaksMidBar / Measure.ContinuesBar) ----
    // The score-settled table of bars to split, and the sink for the breaks THIS walk meets
    // mid-bar — see MidBarBreakTable's remarks: the collector collects twice, the first pass
    // discovers (every builder records a request), the second splits (every builder cuts the
    // bar where the table says). Both are the collector's; null in a bare builder.
    internal MidBarBreakTable? MidBarBreaks { get; set; }
    internal List<MidBarBreakRequest>? MidBarBreakRequests { get; set; }
    // …and the breaks this walk can tell at once are NOT splittable — inside a tuplet (the
    // clock has not been told the members' time yet, so there is no offset to ask for) or
    // under `time none` (the clock stands still). Reported like the table's refusals (LYS1037).
    internal List<MidBarBreakConflict>? MidBarBreakRefusals { get; set; }
    // The BAR this builder's first measure stands in: 0 for a part's own stream, the span's
    // opening bar for a `voice { }` sub-voice collected on its own (BuildExtraVoiceTracks).
    internal int LogicalIndexBase { get; set; }
    // Bars BEGUN by the measures emitted so far: every emitted measure but a split's head
    // (its tail is the same bar). The table is keyed in bars, and so is the bar number.
    private int _logicalCount;
    // A `break` met mid-bar, waiting to learn whether MUSIC follows it in the same bar (then
    // it is a request to split there) or a bar line does (then it is the bar-line break it
    // always was — the corpus's `e2 break |` sites ask for nothing new).
    private (Fraction Offset, bool Page, int Position, string? Refusal)? _pendingMidBar;
    // True while the measure under construction is the second half of a split bar.
    private bool _continuesBar;

    /// <summary>The bar under construction, counted in bars (a split's second half is the
    /// same bar as its first) from <see cref="LogicalIndexBase"/> — the key
    /// <see cref="MidBarBreaks"/> reads and the number <c>BarNumberEngraver</c> prints.</summary>
    public int LogicalMeasureIndex => LogicalIndexBase + _logicalCount;

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

    public MeasureBuilder(Meter meter, int sourceStart = 0, bool senzaMisura = false)
    {
        // The opening meter as one value: `time none` at the score level keeps the pair for the
        // placeholder spacer and freezes nothing (the clock opens at zero).
        _bars.Restore(new BarContext.MeterState(meter, senzaMisura));
        _measureSourceStart = sourceStart;
    }

    /// <summary>The meter in force AS WRITTEN (6/8 is not 3/4, "3+2" is not 5) — the running
    /// <see cref="CurrentMeasureLength"/> is a reduced fraction. Set by the collector at the
    /// opening and moved by every meter change this builder takes; read when a section head is
    /// armed (<see cref="SectionHeadState"/>).</summary>
    public TimeSignature MeterInForce { get; set; }

    /// <summary>True while the running meter is <c>time none</c> (<see cref="BarContext.SenzaMisura"/>;
    /// the clock it freezes is <c>_frozenPosition</c>). A section boundary compares this alongside
    /// <see cref="CurrentMeasureLength"/> to decide whether reverting to the score meter needs a
    /// redrawn time signature.</summary>
    public bool SenzaMisura => _bars.SenzaMisura;

    /// <summary>Gets the current accumulated duration within the measure.</summary>
    public Fraction CurrentDuration => _currentDuration;

    /// <summary>The auto-complete measure length currently in force — the running meter, or
    /// the pickup while one is pending (<see cref="BarContext.BarLength"/>). A section boundary
    /// reads this to decide whether reverting to the score meter needs a redrawn time signature.</summary>
    public Fraction CurrentMeasureLength => _bars.BarLength;

    /// <summary>Current measure index (completed measures count).</summary>
    public int CurrentMeasureIndex => _measures.Count;

    /// <summary>Current item count within the current measure.</summary>
    public int CurrentItemCount => _currentItems.Count;

    /// <summary>
    /// Whether the voice's previous note or chord ties INTO a head of this pitch — a head at
    /// <paramref name="staffPosition"/> sounding <paramref name="midi"/> on the event that
    /// last entered, which carries a tie. A rest between them (or no event yet) answers no.
    /// </summary>
    /// <remarks>
    /// LilyPond's accidental engraver marks the accidental of a tie's RIGHT head as tied
    /// (lily/accidental-engraver.cc:352-379 stop_translation_timestep), and only when the two heads share a pitch
    /// (:358-365, the enharmonic tie). Read off what this builder already holds — the current
    /// measure's items, then the emitted measures — so it is state a resume restores with
    /// the prefix.
    /// Grace notes count: LilyPond's are ordinary events of the voice.
    /// </remarks>
    public bool TiesInto(int staffPosition, int midi)
    {
        var last = LastSoundingItem();
        return last switch
        {
            NoteItem n => n.HasTieStart && n.StaffPosition == staffPosition && n.Midi == midi,
            ChordItem c => c.HasTieStart && ChordHas(c, staffPosition, midi),
            _ => false,
        };

        static bool ChordHas(ChordItem c, int staffPosition, int midi)
        {
            var notes = c.Notes;
            for (int i = 0; i < notes.Length; i++)
                if (notes[i].StaffPosition == staffPosition && notes[i].Midi == midi)
                    return true;
            return false;
        }
    }

    private MusicItem? LastSoundingItem()
    {
        for (int i = _currentItems.Count - 1; i >= 0; i--)
            if (_currentItems[i] is NoteItem or ChordItem or RestItem)
                return _currentItems[i];
        for (int m = _measures.Count - 1; m >= 0; m--)
        {
            var items = _measures[m].Items;
            for (int i = items.Length - 1; i >= 0; i--)
                if (items[i] is NoteItem or ChordItem or RestItem)
                    return items[i];
        }
        return null;
    }

    /// <summary>
    /// The phrasing-slur marks written on the node the walk is emitting — the source
    /// positions of its <c>@phrasingSlur</c> (<c>Start</c>) and <c>@!phrasingSlur</c>
    /// (<c>End</c>), −1 where none was written — or null. The next note, chord or sounding
    /// rest to enter (<see cref="TakePendingPhrasingSlur"/>) takes them and clears this; the
    /// walk clears it too once the node is done, so a mark on something that made no column
    /// cannot land on a later one.
    /// </summary>
    /// <remarks>
    /// ⚠️ A HAND-OFF, NOT A FLAG PER CONSTRUCTOR. A slur's bools go through every item
    /// factory's parameters; this rides the one sink every item enters through instead
    /// (<see cref="MusicItem.HasPhrasingSlurStart"/>).
    /// </remarks>
    public (int Start, int End, int Direction)? PendingPhrasingSlur { get; set; }

    /// <summary>The columns a phrasing slur binds to — the same three a slur does
    /// (<c>SlurDetector.TryGetSlurFlags</c>): a note, a chord, a sounding rest.</summary>
    private static bool BindsAPhrasingSlur(MusicItem item)
        => item is NoteItem or ChordItem || item is RestItem { IsSpacer: false };

    /// <summary>Stamps <see cref="PendingPhrasingSlur"/> onto <paramref name="item"/> when it
    /// is a column one binds to, and clears it; returns the item unchanged otherwise. Both
    /// doors call it — <see cref="AddItem"/> adds directly and does not pass through
    /// <see cref="AddItemWithoutDuration"/>.</summary>
    private MusicItem TakePendingPhrasingSlur(MusicItem item)
    {
        item = TakePendingSectionPlay(item);
        // Not in grace time: a grace column is not the note the mark was written on.
        if (_graceDepth > 0 || PendingPhrasingSlur is not { } phrasing || !BindsAPhrasingSlur(item))
            return item;
        PendingPhrasingSlur = null;
        return item with
        {
            HasPhrasingSlurStart = phrasing.Start >= 0,
            PhrasingSlurStartSourcePosition = phrasing.Start,
            HasPhrasingSlurEnd = phrasing.End >= 0,
            PhrasingSlurEndSourcePosition = phrasing.End,
            PhrasingSlurDirection = phrasing.Direction,
        };
    }

    // The section play the next timed item opens (MusicItem.BeginsSectionPlay), armed by the
    // section prologue; null when no play is waiting for its first item.
    private SectionPlayStamp? _pendingPlay;

    /// <summary>Arms the stamp for a new section play: the next note, chord or rest to enter
    /// (grace time excepted) carries it. A play that wrote no timed item leaves its stamp
    /// pending; it merges into this one and the stronger edge wins
    /// (<see cref="SectionPlayEdge"/>'s order), so a span can never be carried over an edge it
    /// did not see.</summary>
    public void ArmSectionPlay(SectionPlayStamp stamp)
    {
        if (_pendingPlay is { } earlier && earlier.Edge > stamp.Edge)
            stamp = stamp with { Edge = earlier.Edge };
        _pendingPlay = stamp;
    }

    /// <summary>What was in force on the staff when a section play began, BEFORE the
    /// boundary reset ran: the meter, the key (with its tonic) and the clef. A section head
    /// that states one of them again is no change (<see cref="SectionHead"/>).</summary>
    internal readonly record struct SectionHeadState(
        TimeSignature Time, KeySignature Key, int TonicStep, int TonicAlter, bool TonicValid,
        ClefType Clef)
    {
        /// <summary><see cref="MeasureBuilder.SameMeter"/> against the head's meter.</summary>
        public bool SameMeter(TimeSignature t) => MeasureBuilder.SameMeter(Time, t);
    }

    /// <summary>The same meter as printed: the numerator as drawn (the additive spelling when
    /// there is one), the beat type and whether it is unmetered — 6/8 is not 3/4.</summary>
    internal static bool SameMeter(TimeSignature a, TimeSignature b)
        => a.Beats == b.Beats && a.BeatType == b.BeatType && a.SenzaMisura == b.SenzaMisura
           && (a.BeatsText ?? a.Beats.ToString()) == (b.BeatsText ?? b.Beats.ToString());

    // The armed section head and the measure count it was armed at (SectionHead's gate).
    private SectionHeadState? _sectionHead;
    private int _sectionHeadMeasures;

    /// <summary>Arms <see cref="SectionHead"/> for a new section play (the section prologue,
    /// before its reset).</summary>
    public void ArmSectionHead(SectionHeadState head)
    {
        _sectionHead = head;
        _sectionHeadMeasures = _measures.Count;
    }

    /// <summary>
    /// The state in force before the section reset, while the walk still stands at the
    /// section's first moment — no measure closed and no time elapsed since it was armed —
    /// else null. It is what a section head's <c>time</c>, <c>key</c> and <c>clef</c> are
    /// compared with to tell whether they change anything.
    /// </summary>
    /// <remarks>
    /// LILYSHARP-OWN (owner's decisions, 2026-10-02, HANDOFF §1.1 第737): a <c>time</c>, <c>key</c>
    /// or <c>clef</c> that changes nothing is NOT drawn — at a section head, nothing compared
    /// with what the section before left (and the reset it stands on is withdrawn with it);
    /// anywhere else, nothing compared with the value in force — unless it is written with
    /// <c>!</c> after its keyword (<c>key! ees major</c>), which draws it regardless. A section
    /// boundary resets the meter, the key and the clef, so a section that continues in what the
    /// one before left has to say it again — every section after a modulation must — and that
    /// restatement is not meant to be read. LilyPond draws a grob for every \time and \key event
    /// (time-signature-engraver.cc:94-122 compares the spec by identity; key-engraver.cc:141-152
    /// creates a key on every event); the twin writes what the page draws (LilyPondExporter).
    /// </remarks>
    public SectionHeadState? SectionHead
        => _sectionHead is { } h && _measures.Count == _sectionHeadMeasures
           && _currentDuration == Fraction.Zero && !_hasMeasureContent
            ? h : null;

    /// <summary>Withdraws the change of kind <typeparamref name="T"/> standing at this moment
    /// (a section reset a restatement cancels; see <see cref="SectionHead"/>).</summary>
    internal void WithdrawStanding<T>() where T : MusicItem
    {
        int i = FindInPrefixRun<T>();
        if (i >= 0)
            _currentItems.RemoveAt(i);
    }

    /// <summary>Restamps the key changes standing at this moment with <paramref name="clef"/>
    /// — what an added clef change does to them (AddItem), for a clef that is withdrawn
    /// instead of added.</summary>
    internal void RestampStandingKeys(ClefType clef)
    {
        for (int i = _currentItems.Count - 1;
             i >= 0 && _currentItems[i] is ClefChangeItem or KeySignatureChangeItem or TimeSignatureChangeItem;
             i--)
            if (_currentItems[i] is KeySignatureChangeItem standing)
                _currentItems[i] = standing with { Clef = clef };
    }

    /// <summary>Stamps the armed section play onto <paramref name="item"/> when it is the
    /// play's first timed item, and clears it. Both doors call it, through
    /// <see cref="TakePendingPhrasingSlur"/>.</summary>
    private MusicItem TakePendingSectionPlay(MusicItem item)
    {
        if (_pendingPlay is not { } play || _graceDepth > 0
            || item is not (NoteItem or ChordItem or RestItem))
            return item;
        _pendingPlay = null;
        return item with
        {
            BeginsSectionPlay = play.Edge,
            SectionPlayName = play.Section,
            SectionRepeatRole = play.Role,
            SectionRepeatRunStart = play.RunStart,
            SectionRepeatCount = play.Count,
            SectionEndingPasses = play.Passes,
            SectionPlayRewinds = play.Rewinds,
            SectionPlayMarksBefore = play.MarksBefore,
        };
    }

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
    public bool AtPieceOpening => _measures.Count == 0 && !_hasMeasureContent;

    /// <summary>True when the current span holds measure-worthy content — something with
    /// duration (a note/rest/chord) — as opposed to only zero-duration directives (a
    /// leading <c>clef</c>/<c>key</c>/<c>time</c>). A bare or leading <c>|</c> CLOSES a
    /// span with content; a directive-only span it merely CONFIRMS, carrying the
    /// directive into the first real measure so no spurious directive-only empty bar is
    /// drawn (the <c>clef treble x | …</c> case).</summary>
    public bool HasMeasureContent => _hasMeasureContent;

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
    /// which leaves the boundary consumed. Without this, <c>form { A |: D :| }</c> read
    /// as a written <c>| |:</c> PAIR and opened an empty bar between every section and every
    /// form-level repeat sign (measured: 3 bars became 5). The pair rule is about two
    /// barlines written NEXT TO EACH OTHER in one music stream; a structural barline is not
    /// the second of any such pair, whatever the state of the section that preceded it.
    /// </remarks>
    public void ArmBoundaryForStructuralBarline() => _confirmableBoundary = true;
    /// <summary>Re-arms the auto-complete measure length without printing a grob
    /// (used when a leading meter change collapses into the initial time signature, and for a
    /// section head's own meter or the reset to the score's).
    /// <paramref name="senzaMisura"/> true is <c>time none</c>: the pair is kept for the
    /// placeholder spacer but nothing auto-completes until a metered <c>time</c> follows.</summary>
    public void SetMeter(Meter meter, bool senzaMisura = false) => FreezeOrThaw(meter, senzaMisura);

    /// <summary>The score-level meter this voice's sections revert to (<see cref="RevertMeterToHome"/>)
    /// — the file-level <c>time</c> as the definitions walk left it, armed by the collector for
    /// every voice (<c>MeasureCollector._scoreTime</c>; not <c>_meta</c> at the voice's start,
    /// which an earlier voice's opening <c>time</c> has rewritten by then).</summary>
    public void SetHomeMeter(Meter home, bool senzaMisura)
    {
        _bars.HomeMeter = home;
        _bars.HomeSenzaMisura = senzaMisura;
    }

    /// <summary>
    /// A section boundary with no section meter: the running meter reverts to the SCORE level
    /// (<see cref="SetHomeMeter"/>), for the same self-containment as key and clef — a
    /// mid-section <c>time</c> cannot leak past the section end, nor into the same section
    /// reused elsewhere by the form. Only redrawn when a prior section actually left a different
    /// meter — the PAIR or <c>time none</c> (<see cref="BarContext.LeftHome"/>: what the twin
    /// writes a <c>\time</c> for), so the common case emits nothing and the first section is a
    /// no-op; the redraw makes the revert visible instead of silently leaving the previous
    /// signature on the staff. A section that ended unmetered against a 4/4 score meter differs,
    /// and the 4/4 is redrawn — LilyPond prints a grob for every \time event (measured 2.26.0,
    /// scratch/p354/lp/senza-reprint.ly: `\time 4/4 … \time 4/4` prints twice). A
    /// <c>time none</c> home carries no ink and no width (<see cref="TimeSignatureChangeItem.Blanked"/>).
    /// </summary>
    public void RevertMeterToHome(int sourcePosition)
    {
        if (!_bars.LeftHome)
            return;
        var home = _bars.HomeMeter;
        bool senza = _bars.HomeSenzaMisura;
        AddItem(new TimeSignatureChangeItem(
            new TimeSignature(home.Beats, home.BeatType, home.BeatsText, senza), sourcePosition)
        {
            Blanked = senza,
        });
        // For a metered home the change item above has already moved the meter (AddItem); this
        // adds the pair for a `time none` home, whose change item carries none (p748 poison 4).
        SetMeter(home, senza);
    }

    /// <summary>The one place the clock freezes and thaws (see <c>_frozenPosition</c>): a
    /// <c>time none</c> arriving on a metered clock freezes it at its current reading; a
    /// metered <c>time</c> thaws it. A second <c>time none</c> inside a cadenza changes nothing.
    /// Then the meter moves (<see cref="BarContext.SetTime(Meter?, bool)"/>: null is a
    /// <c>time none</c> change item, which carries none; a pending pickup is left alone).</summary>
    private void FreezeOrThaw(Meter? meter, bool senzaMisura)
    {
        if (senzaMisura && !_bars.SenzaMisura)
            _frozenPosition = _currentDuration;
        else if (!senzaMisura)
            _frozenPosition = Fraction.Zero;
        _bars.SetTime(meter, senzaMisura);
    }

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
    /// LILYPOND-REF: ly/music-functions-init.ly:1697-1705 partial = context-spec-music 'Timing — \partial adjusts the
    /// Timing measurePosition so the current measure ends <paramref name="length"/>
    /// past the point of use; normal measureLength applies thereafter.
    /// </summary>
    public void SetPartial(Fraction length) => _bars.SetPartial(length);

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
    /// <param name="slash">
    /// True for an <c>acciaccatura { }</c>: every item added inside the region also carries
    /// <see cref="MusicItem.GraceSlash"/>, the way LilyPond states the stroke as a property
    /// of the note's own Flag (see that member).
    /// </param>
    /// <param name="stemDown">
    /// True for a grace written by a lower voice: every note and chord added inside the region
    /// carries <see cref="MusicItem.GraceStemDown"/> (see that member for LilyPond's rule).
    /// </param>
    public void EnterGraceTime(bool slash = false, bool stemDown = false)
    {
        _graceDepth++;
        if (slash)
            _graceSlashDepth++;
        _graceStemDown = stemDown;
    }

    /// <summary>The stem direction of the grace body being walked — see
    /// <see cref="EnterGraceTime"/>. A bool, not a depth: a grace body cannot nest.</summary>
    private bool _graceStemDown;

    /// <summary>How deep into <c>acciaccatura { }</c> bodies this builder is — a counter for
    /// the reason <see cref="_graceDepth"/> is one.</summary>
    private int _graceSlashDepth;

    /// <summary>Closes the region <see cref="EnterGraceTime"/> opened.</summary>
    public void ExitGraceTime(bool slash = false)
    {
        if (_graceDepth > 0)
            _graceDepth--;
        if (slash && _graceSlashDepth > 0)
            _graceSlashDepth--;
        if (_graceDepth == 0)
            _graceStemDown = false;
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
    /// the source position, which is click-to-source data no engraver reads. Since session 725
    /// the slur marks survive too: the slur is the first of the ordinary engravers to draw
    /// grace time (⒝2's slur half).
    /// </para>
    /// </remarks>
    private MusicItem NarrowToGraceTime(MusicItem item)
    {
        // The stroke an acciaccatura's flag carries — a property of the FLAG in LilyPond, so
        // it rides the item and is drawn by whoever draws that flag (MusicItem.GraceSlash).
        bool slash = _graceSlashDepth > 0;
        return item switch
        {
            // ⚠️ THE CUE FLAG SURVIVES: a grace inside `cue { }` is in BOTH, and LilyPond adds
            // the two sizes (GrobFontSize.StepOf) — dropping it here is what made
            // `cue { grace { … } }` an ordinary full-context grace (ledger
            // cue.grace.column.to-main, session 573).
            // ⚠️ THE SLUR MARKS SURVIVE (session 725): a grace column is a slur bound like any
            // note, paired by the ordinary SlurDetector and laid out at the grace's own X and
            // font (ElementCoordinator.LayoutSlurs) — `grace { d'16( e') }`, `grace { g16( } a8)`.
            NoteItem n => new NoteItem(
                n.StaffPosition, n.BaseDuration, n.Dots, n.Accidental, n.NeedsLedgerLines,
                n.SourcePosition, hasSlurStart: n.HasSlurStart, hasSlurEnd: n.HasSlurEnd, isCue: n.IsCue)
            {
                GraceTime = true,
                GraceSlash = slash,
                GraceStemDown = _graceStemDown,
                Midi = n.Midi,
                StringNumber = n.StringNumber,
                SlurStartSourcePosition = n.SlurStartSourcePosition,
                SlurEndSourcePosition = n.SlurEndSourcePosition,
            },

            ChordItem c => new ChordItem(
                // The members keep the same seven answers and nothing else — a member can carry
                // its own fingering and its own notehead style, and both are drops here too.
                c.Notes.Select(static m => new ChordNoteInfo(
                    m.StaffPosition, m.Accidental, m.NeedsLedgerLines,
                    StringNumber: m.StringNumber, Midi: m.Midi,
                    SourcePosition: m.SourcePosition)).ToImmutableArray(),
                c.BaseDuration, c.Dots, c.SourcePosition, isCue: c.IsCue,
                hasSlurStart: c.HasSlurStart, hasSlurEnd: c.HasSlurEnd)
            {
                GraceTime = true,
                GraceSlash = slash,
                GraceStemDown = _graceStemDown,
                SlurStartSourcePosition = c.SlurStartSourcePosition,
                SlurEndSourcePosition = c.SlurEndSourcePosition,
                SlurStartHeadPosition = c.SlurStartHeadPosition,
                SlurEndHeadPosition = c.SlurEndHeadPosition,
            },

            // A REST TAKES NO SLASH: the stroke is the Flag's, and a rest has none.
            RestItem r => new RestItem(r.BaseDuration, r.Dots, r.SourcePosition)
            {
                GraceTime = true,
                IsSpacer = r.IsSpacer,
                HasSlurStart = r.HasSlurStart,
                HasSlurEnd = r.HasSlurEnd,
                SlurStartSourcePosition = r.SlurStartSourcePosition,
                SlurEndSourcePosition = r.SlurEndSourcePosition,
            },

            _ => item with { GraceTime = true },
        };
    }

    /// <summary>
    /// The index of the last <typeparamref name="T"/> in the run of clef / key / time
    /// changes at the end of the current measure's items — the changes standing at this
    /// moment, since each takes no time — or -1 when the run holds none.
    /// </summary>
    private int FindInPrefixRun<T>() where T : MusicItem
    {
        for (int i = _currentItems.Count - 1;
             i >= 0 && _currentItems[i] is ClefChangeItem or KeySignatureChangeItem or TimeSignatureChangeItem;
             i--)
        {
            if (_currentItems[i] is T)
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Adds a clef / key / time change to the run of changes standing at this moment in
    /// LilyPond's break-align order — clef, then key, then time — whichever order the source
    /// wrote them in (`time 4/4 key d major` draws the key first), and whichever order a
    /// section reset queued them. The layout and the renderer walk the run in list order
    /// (SpacingRules.MidMeasureChanges), so the list itself carries the order.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:650-664 break-align-orders
    /// </remarks>
    private void InsertInBreakAlignOrder(MusicItem item)
    {
        int rank = BreakAlignRank(item);
        int at = _currentItems.Count;
        while (at > 0 && _currentItems[at - 1] is ClefChangeItem or KeySignatureChangeItem or TimeSignatureChangeItem
               && BreakAlignRank(_currentItems[at - 1]) > rank)
        {
            at--;
        }
        _currentItems.Insert(at, item);
    }

    /// <summary>
    /// Stamps <see cref="MusicItem.WrittenAfterGrace"/> on the clef / key / time changes added
    /// since item <paramref name="fromIndex"/> of this measure — the changes written after a
    /// pending grace, which reach the list before the grace's own items do.
    /// </summary>
    public void MarkChangesWrittenAfterGrace(int fromIndex)
    {
        for (int i = Math.Max(0, fromIndex); i < _currentItems.Count; i++)
        {
            if (_currentItems[i] is ClefChangeItem or KeySignatureChangeItem or TimeSignatureChangeItem)
                _currentItems[i] = _currentItems[i] with { WrittenAfterGrace = true };
        }
    }

    private static int BreakAlignRank(MusicItem item) => item switch
    {
        ClefChangeItem => 0,
        KeySignatureChangeItem => 1,
        _ => 2,
    };

    /// <summary>
    /// Adds a music item and automatically completes the measure if duration is reached.
    /// </summary>
    public void AddItem(MusicItem item)
    {
        CloseAlternatives();
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
            // `time none` freezes the clock (see _frozenPosition) and keeps the last metered
            // meter; a metered `time` after it thaws the clock and re-arms.
            FreezeOrThaw(tsc.NewTime.SenzaMisura ? null : MeterOf(tsc.NewTime), tsc.NewTime.SenzaMisura);
            MeterInForce = tsc.NewTime;
            // Collapse a section reset followed by the section's own `time`: keep the last
            // meter so two time signatures don't print side by side ("C ♮ C"). The reset
            // queues the time before the key, so the key change can stand between them —
            // look back through the whole run at this moment, not just the last item.
            int standingTime = FindInPrefixRun<TimeSignatureChangeItem>();
            if (standingTime >= 0)
                _currentItems[standingTime] = item;
            else
                InsertInBreakAlignOrder(item);
            return;
        }

        // A clef change RESTAMPS a key change already standing at its moment: break alignment
        // prints the clef before the signature, and the signature's accidentals take their
        // staff positions from the clef in effect, whichever of the two the source wrote first.
        // LILYPOND-REF: scm/output-lib.scm:1056 key-signature-interface::alteration-positions — reads the staff's c0-position
        if (item is ClefChangeItem clefChange)
        {
            for (int i = _currentItems.Count - 1;
                 i >= 0 && _currentItems[i] is ClefChangeItem or KeySignatureChangeItem or TimeSignatureChangeItem;
                 i--)
            {
                if (_currentItems[i] is KeySignatureChangeItem standing)
                    _currentItems[i] = standing with { Clef = clefChange.NewClef };
            }
        }

        // Collapse consecutive key changes at the same measure start — a section
        // boundary reset (revert to the score key) immediately followed by the
        // section's own `key`. Draw ONE change from the ORIGINAL previous key to the
        // FINAL new key, or nothing if the net key is unchanged; otherwise the two
        // signatures (e.g. a cancel-natural and the new flat) overprint. As with the time
        // above, a time change may stand between the two (`time 4/4 key e minor`).
        if (item is KeySignatureChangeItem kc
            && FindInPrefixRun<KeySignatureChangeItem>() is var standingKey and >= 0)
        {
            var prevKc = (KeySignatureChangeItem)_currentItems[standingKey];
            var merged = new KeySignatureChangeItem(kc.NewKey, prevKc.PreviousKey, kc.SourcePosition)
            {
                Clef = kc.Clef,
            };
            // ⚠️ KEPT even when the signature comes back to where it was. Until session 737 that
            // case was removed as "net no change" — but the signature is not the key: E minor →
            // G major is one sharp either way, and the reset draws it (SectionResetTests,
            // RelativeMinorSection_RedrawsTheScoreKeyOnItsWayBack). A true restatement — the
            // same key and tonic the section before left — never reaches here: the collector
            // withdraws the reset and adds nothing (SectionHead).
            _currentItems[standingKey] = merged;
            return;
        }

        var itemDuration = GetItemDuration(item);

        // A SPACER sounding across the bar's split is cut in two — it draws nothing, so
        // the cut is invisible, and it is how an empty `| |` bar of another part, a row's
        // slot or a `voice { }` sub-voice's lead-in pad gets split with the music
        // (MidBarBreakTable's remarks). A sounding item across the split was refused by the
        // table's builder before the table existed, so none can reach here.
        if (itemDuration > Fraction.Zero && !_bars.SenzaMisura
            && item is RestItem { IsSpacer: true } spacer
            && MidBarBreaks?.At(LogicalMeasureIndex) is { } cut
            && _currentDuration < cut.Offset && _currentDuration + itemDuration > cut.Offset)
        {
            var head = cut.Offset - _currentDuration;
            // A beat slash's grob stands in the column the spacer OPENS, so the head piece
            // keeps the mark and the tail is the event still sounding (RestItem.RepeatSlashCount).
            AddItem(new RestItem(head, 0, spacer.SourcePosition)
            {
                IsSpacer = true,
                RepeatSlashCount = spacer.RepeatSlashCount,
            });
            AddItem(new RestItem(itemDuration - head, 0, spacer.SourcePosition) { IsSpacer = true });
            return;
        }

        if (item is ClefChangeItem or KeySignatureChangeItem)
            InsertInBreakAlignOrder(item);
        else
            _currentItems.Add(TakePendingPhrasingSlur(item));

        // Real content fills this span, so a following barline closes IT, not an empty
        // measure. A ZERO-duration directive (a clef change) does not fill anything — it
        // leaves the boundary confirmable, so a bare or leading `|` carries the directive
        // into the first real measure instead of closing a spurious clef-only empty bar
        // (`clef treble x | …`).
        if (itemDuration > Fraction.Zero)
        {
            _confirmableBoundary = false;
            _hasMeasureContent = true;
            // Music after a mid-bar `break` in the same bar: the break is a request to split.
            RequestPendingMidBar();
        }

        // UNMETERED: the clock stands still and nothing auto-completes — LilyPond's
        // measurePosition under `timing = ##f`. The item is in the measure; only a written
        // barline will end it. See _frozenPosition.
        if (_bars.SenzaMisura)
            return;

        // Track duration
        _currentDuration += itemDuration;

        // Auto-complete measure if we've reached or exceeded what the bar is worth (the
        // meter, or the pickup while one is pending)
        if (_currentDuration >= _bars.BarLength)
        {
            AutoCompleteMeasure(item.SourcePosition + 1);
            return;
        }

        TrySplitAtMidBar(item.SourcePosition + 1);
    }

    /// <summary>
    /// Records the mid-bar <c>break</c> waiting in <see cref="_pendingMidBar"/> as a
    /// <see cref="MidBarBreakRequest"/>, now that a sounding item has followed it in the
    /// same bar. A break the bar line follows instead is cleared by the measure's emit and
    /// asks for nothing — it is the bar-line break every `break` was before session 356.
    /// </summary>
    private void RequestPendingMidBar()
    {
        if (_pendingMidBar is not { } p)
            return;
        _pendingMidBar = null;
        if (p.Refusal is { } why)
            MidBarBreakRefusals?.Add(new MidBarBreakConflict(p.Position, why));
        else
            MidBarBreakRequests?.Add(new MidBarBreakRequest(LogicalMeasureIndex, p.Offset, p.Page, p.Position));
    }

    /// <summary>
    /// Remembers a <c>break</c> written INSIDE the bar under construction, so that the next
    /// sounding item in the same bar can turn it into a request (<see cref="RequestPendingMidBar"/>)
    /// — or into a refusal, where this builder can already tell the bar cannot be cut there:
    /// under <c>time none</c> the clock stands still, and inside a tuplet the members' time
    /// has not reached the clock yet (AddDuration follows the last member), so neither has an
    /// offset to ask for. The offset otherwise is the clock's reading at the break.
    /// </summary>
    private void NoteMidBarCandidate(bool page, int sourcePosition)
    {
        if (MidBarBreakRequests == null || _currentItems.Count == 0)
        {
            _pendingMidBar = null;
            return;
        }
        bool pageToo = page || (_pendingMidBar?.Page ?? false);
        if (_bars.SenzaMisura)
        {
            _pendingMidBar = (Fraction.Zero, pageToo, sourcePosition,
                "the bar is unmetered (time none), so it has no beat to break at");
            return;
        }
        var written = Fraction.Zero;
        foreach (var item in _currentItems)
            written += GetItemDuration(item);
        if (written != _currentDuration)
        {
            _pendingMidBar = (Fraction.Zero, pageToo, sourcePosition,
                "the break stands inside a tuplet");
            return;
        }
        if (!_hasMeasureContent || _currentDuration <= Fraction.Zero || _currentDuration >= _bars.BarLength)
        {
            _pendingMidBar = null;
            return;
        }
        _pendingMidBar = (_currentDuration, pageToo, sourcePosition, null);
    }

    /// <summary>
    /// Cuts the bar under construction at the offset the score's table gives it, when the
    /// clock has just reached that offset: the items so far become the bar's first half
    /// (<see cref="EmitSplitHead"/>) and the clock carries on into its second.
    /// </summary>
    private void TrySplitAtMidBar(int sourceEnd)
    {
        if (MidBarBreaks?.At(LogicalMeasureIndex) is not { } split)
            return;
        if (_currentItems.Count == 0 || _continuesBar || _currentDuration != split.Offset)
            return;
        EmitSplitHead(split, sourceEnd);
    }

    /// <summary>
    /// Emits the FIRST HALF of a bar a line break splits (<see cref="Measure.BreaksMidBar"/>):
    /// no end bar line, the line break forced, the bar count not advanced. The clock, the
    /// content flag and the meter carry on — the measure that follows is the same bar's
    /// second half (<see cref="Measure.ContinuesBar"/>) — while the per-measure decorations
    /// (section label, start bar line, pending breaks) were the head's.
    /// </summary>
    /// <remarks>
    /// <see cref="MeasureCompleted"/> is deliberately NOT raised: it resets the collector's
    /// accidental memory, and LilyPond forgets accidentals at the bar line, not at a
    /// system's edge — a sharp before the break still governs the same pitch after it.
    /// LILYPOND-REF: lily/accidental-engraver.cc — the memory is keyed by measurePosition's
    /// bar, which a mid-bar break does not advance.
    /// </remarks>
    private void EmitSplitHead(MidBarSplit split, int sourceEnd, bool isEmptyPlaceholder = false)
    {
        var pagePermission = split.PageBreak
            ? Layout.BreakPermission.Force
            : TakePendingPagePermission();
        _measures.Add(new Measure(
            _currentItems.ToImmutableArray(),
            _pendingStartBarline,
            BarlineType.None,
            _sectionLabel,
            _measureSourceStart,
            sourceEnd,
            hasBreakAfter: true,
            pageBreakPermission: pagePermission,
            sectionLabelPosition: _sectionLabelPosition,
            isPickup: _bars.Partial is not null,
            breaksMidBar: true)
        {
            IsEmptyPlaceholder = isEmptyPlaceholder,
        });
        _currentItems.Clear();
        _sectionLabel = null;
        _sectionLabelPosition = 0;
        _pendingStartBarline = BarlineType.None;
        _pendingBreak = false; // the head carries it
        _pendingNoBreak = false;
        _pendingMidBar = null;
        _measureSourceStart = sourceEnd;
        _confirmableBoundary = false;
        _boundaryRetargetable = false;
        _lastEndAutoFill = false;
        _atScopeStart = false;
        _continuesBar = true;
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
        item = TakePendingPhrasingSlur(item);
        _currentItems.Add(_graceDepth > 0 ? NarrowToGraceTime(item) : item);
        _confirmableBoundary = false;
        // A tuplet's member is sounding music after a mid-bar `break` too (its duration
        // arrives later through AddDuration); grace time is not — it takes no bar time.
        if (_graceDepth == 0 && GetItemDuration(item) > Fraction.Zero)
            RequestPendingMidBar();
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

        if (duration > Fraction.Zero)
            _hasMeasureContent = true;

        // UNMETERED: the clock stands still (see AddItem).
        if (_bars.SenzaMisura)
            return;

        _currentDuration += duration;

        if (_currentDuration >= _bars.BarLength)
        {
            AutoCompleteMeasure(sourcePosition);
            return;
        }

        TrySplitAtMidBar(sourcePosition);
    }

    /// <summary>
    /// Moves <see cref="_barPosition"/> over the bar about to be emitted: a pickup ends on the
    /// downbeat; an unmetered bar leaves it where it was (the clock stands still); any other bar
    /// adds its length and comes round the meter.
    /// </summary>
    private void AdvanceBarPosition()
    {
        if (_bars.SenzaMisura)
            return;
        if (_bars.Partial is not null)
        {
            _barPosition = Fraction.Zero;
            return;
        }
        var p = _barPosition + _currentDuration;
        var bar = _bars.BarLength;
        if (bar > Fraction.Zero)
            while (p >= bar)
                p -= bar;
        _barPosition = p;
    }

    /// <summary>
    /// LilyPond's measurePosition NOW: the last emitted bar's end plus what the open bar holds,
    /// round the meter.
    /// </summary>
    private Fraction CurrentBarPosition()
    {
        var p = _barPosition + _currentDuration;
        var bar = _bars.BarLength;
        if (bar > Fraction.Zero)
            while (p >= bar)
                p -= bar;
        return p;
    }

    /// <summary>
    /// An ending of a volta group begins here. The group's FIRST ending remembers LilyPond's
    /// measurePosition, which every ending but the last hands back (<see cref="EndAlternative"/>).
    /// Called at every ending.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/alternative-sequence-iterator.cc:195-225 save_context_properties —
    /// the Timing properties named by alternativeRestores (ly/engraver-init.ly:856:
    /// measurePosition, measureLength, measureStartNow…) are saved at the first alternative
    /// and restored at the end of each alternative but the last (:170-193).
    /// </remarks>
    internal void BeginAlternatives()
    {
        if (_bars.SenzaMisura)
            return;
        _alternativeStart ??= CurrentBarPosition();
        _inEnding = true;
    }

    /// <summary>
    /// An ending is over. Every one but the last puts LilyPond's measurePosition back where the
    /// first began — so the next ending, and the music after the last, count their bars from
    /// there. The builder's own bars are untouched; only the position <see cref="EndsOffTheBar"/>
    /// reads moves (shifted so that the open bar, if any, ends where LilyPond's would).
    /// A caller that cannot tell which ending is the last (an ending written inside the music)
    /// passes <c>false</c> every time: the hand-back is remembered, and the first music after
    /// the group (or the end of the piece) takes the last one away (<see cref="CloseAlternatives"/>).
    /// </summary>
    internal void EndAlternative(bool last)
    {
        _inEnding = false;
        if (_alternativeStart is not { } start)
            return;
        if (last)
        {
            _alternativeStart = null;
            _alternativeUndo = null;
            return;
        }
        var before = _barPosition;
        _barPosition = RoundTheMeter(start - _currentDuration);
        _alternativeUndo = _barPosition - before;
    }

    /// <summary>
    /// Music (or the end of the piece) after a volta group whose last ending was closed as if
    /// another followed: that last hand-back never happened in LilyPond, so it is taken away.
    /// </summary>
    private void CloseAlternatives()
    {
        if (_inEnding || _alternativeStart is null)
            return;
        if (_alternativeUndo is { } undo)
            _barPosition = RoundTheMeter(_barPosition - undo);
        _alternativeStart = null;
        _alternativeUndo = null;
    }

    /// <summary>A position brought into [0, meter).</summary>
    private Fraction RoundTheMeter(Fraction p)
    {
        var bar = _bars.BarLength;
        if (bar <= Fraction.Zero)
            return p;
        while (p < Fraction.Zero)
            p += bar;
        while (p >= bar)
            p -= bar;
        return p;
    }

    /// <summary>The pair a <see cref="TimeSignature"/> states, as the bar context keeps it.</summary>
    private static Meter MeterOf(TimeSignature time) => new(time.Beats, time.BeatType, time.BeatsText);


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
    {
        // The bar the collector read ahead confirms this close: its token start is the
        // source end the confirmation would retarget to, and a typed bar is the end type
        // its retro-apply would write (overriding a pending type, as the retro-apply does).
        if (_followingBarlineStart >= 0)
        {
            bool typed = _followingBarlineType != BarlineType.Single;
            EmitMeasure(_followingBarlineStart,
                typed ? _followingBarlineType : BarlineType.Single,
                explicitBar: false, forceEndType: typed, followingBreak: _followingBreak);
            return;
        }
        EmitMeasure(sourceEnd, BarlineType.Single, explicitBar: false);
    }

    /// <summary>
    /// Emits the current measure and resets per-measure state. Shared by the
    /// duration-boundary (auto) and explicit-barline paths, which differ only in
    /// the default end barline and whether the boundary is marked explicit.
    /// A no-op when no items are pending (so <c>_pendingBreak</c> survives to the
    /// next real measure).
    /// </summary>
    /// <param name="forceEndType"><paramref name="endType"/> wins over a pending end type —
    /// the read-ahead typed bar's retro-apply, written at the emit (AutoCompleteMeasure).</param>
    /// <param name="followingBreak">The break directive read ahead right after the bar
    /// (<see cref="SetFollowingBreak"/>), applied to the permissions as its boundary-time
    /// setter would apply it.</param>
    private void EmitMeasure(int sourceEnd, BarlineType endType, bool explicitBar,
        bool forceEndType = false, BreakKind? followingBreak = null)
    {
        if (_currentItems.Count == 0)
            return;

        bool hasBreak = _pendingBreak;
        bool noBreak = _pendingNoBreak;
        var pagePermission = TakePendingPagePermission();
        _pendingBreak = false;
        _pendingNoBreak = false;

        // A mid-bar `break` forces, a mid-bar `noBreak` forbids (Force wins if both) — then
        // the directive read ahead at the boundary, as SetBreak / SetNoBreak / SetPageBreak /
        // SetNoPageBreak writes it there.
        var line = hasBreak ? Layout.BreakPermission.Force
            : noBreak ? Layout.BreakPermission.Forbid : Layout.BreakPermission.Allow;
        var page = pagePermission;
        switch (followingBreak)
        {
            case BreakKind.Line: line = Layout.BreakPermission.Force; break;
            case BreakKind.NoLine: line = Layout.BreakPermission.Forbid; break;
            case BreakKind.Page: line = Layout.BreakPermission.Force; page = Layout.BreakPermission.Force; break;
            case BreakKind.NoPage: page = Layout.BreakPermission.Forbid; break;
        }

        AdvanceBarPosition();
        _measures.Add(new Measure(
            _currentItems.ToImmutableArray(),
            _pendingStartBarline,
            !forceEndType && _pendingEndBarline != BarlineType.None ? _pendingEndBarline : endType,
            _sectionLabel,
            _measureSourceStart,
            sourceEnd,
            lineBreakPermission: line,
            pageBreakPermission: page,
            sectionLabelPosition: _sectionLabelPosition,
            isPickup: _bars.Partial is not null,
            unmetered: _bars.SenzaMisura,
            continuesBar: _continuesBar,
            unmeteredPosition: _frozenPosition));

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
            // Already the bar's when the emit read the bar ahead (_followingBarlineStart).
            if (m.SourceEnd != position)
            {
                t_boundaryRewrites++;
                _measures[^1] = m with { SourceEnd = position };
            }
            _lastEndAutoFill = false;
        }
        else
        {
            var all = m.EndHighlightAliases.Append(m.SourceEnd).Append(position).Distinct().ToList();
            int click = all.Max();
            t_boundaryRewrites++;
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
        _hasMeasureContent = false;
        // The bar is over: a split's second half closed it, or a whole bar did. Either way
        // the next measure begins a new bar, and no mid-bar break is still waiting.
        _continuesBar = false;
        _pendingMidBar = null;
        _logicalCount++;
        // The bar is closed: a pending pickup is spent and the meter is back in force.
        _bars.SpendPartial();
        MeasureCompleted?.Invoke();
    }

    /// <summary>Forces a line break here (<c>break</c>, LP's <c>\break</c>). At a bar
    /// boundary it flags the measure just closed; INSIDE a bar it flags the measure the bar
    /// closes into — unless music follows it in the same bar, in which case the score splits
    /// the bar there on its second collect (<see cref="MidBarBreaks"/>; the request is made
    /// by <see cref="RequestPendingMidBar"/>, the cut by <see cref="TrySplitAtMidBar"/>).</summary>
    public void SetBreak(int sourcePosition = -1)
    {
        if (_currentItems.Count == 0 && _measures.Count > 0)
        {
            // At measure boundary - apply break to previous measure. `with`
            // preserves break penalty and page/turn permissions; the old
            // full rebuild silently reset them to defaults. Already Force when the emit
            // read this directive ahead (_followingBreaks).
            var last = _measures[^1];
            if (last.LineBreakPermission != Layout.BreakPermission.Force)
            {
                t_boundaryRewrites++;
                _measures[^1] = last with { LineBreakPermission = Layout.BreakPermission.Force };
            }
        }
        else
        {
            // Mid-measure break - defer to next measure boundary, and remember it as a
            // candidate for splitting the bar should music follow it (NoteMidBarCandidate).
            _pendingBreak = true;
            NoteMidBarCandidate(page: false, sourcePosition);
        }
    }

    /// <summary>Forbids a line break after this measure (<c>noBreak</c>, LP's
    /// <c>\noBreak</c>) — the mirror of <see cref="SetBreak"/>.</summary>
    public void SetNoBreak()
    {
        if (_currentItems.Count == 0 && _measures.Count > 0)
        {
            if (_measures[^1].LineBreakPermission != Layout.BreakPermission.Forbid)
            {
                t_boundaryRewrites++;
                _measures[^1] = _measures[^1] with { LineBreakPermission = Layout.BreakPermission.Forbid };
            }
        }
        else
            _pendingNoBreak = true;
    }

    /// <summary>Forces a page break after this measure (<c>pageBreak</c>, LP's
    /// <c>\pageBreak</c>) — and the line break that goes with it: a page cannot end
    /// mid-line, and LilyPond's event carries both permissions.</summary>
    /// <remarks>
    /// LILYPOND-REF: ly/music-functions-init.ly:1411-1418 pageBreak — line-break-permission 'force AND page-break-permission 'force
    /// </remarks>
    public void SetPageBreak(int sourcePosition = -1)
    {
        if (_currentItems.Count == 0 && _measures.Count > 0)
        {
            var last = _measures[^1];
            if (last.LineBreakPermission != Layout.BreakPermission.Force
                || last.PageBreakPermission != Layout.BreakPermission.Force)
            {
                t_boundaryRewrites++;
                _measures[^1] = last with
                {
                    LineBreakPermission = Layout.BreakPermission.Force,
                    PageBreakPermission = Layout.BreakPermission.Force,
                };
            }
        }
        else
        {
            _pendingBreak = true;
            _pendingPageBreak = true;
            NoteMidBarCandidate(page: true, sourcePosition);
        }
    }

    /// <summary>Forbids a page break after this measure (<c>noPageBreak</c>, LP's
    /// <c>\noPageBreak</c>) — the line-break permission is untouched.</summary>
    /// <remarks>
    /// LILYPOND-REF: ly/music-functions-init.ly:1255-1259 noPageBreak — page-break-permission 'forbid alone
    /// </remarks>
    public void SetNoPageBreak()
    {
        if (_currentItems.Count == 0 && _measures.Count > 0)
        {
            if (_measures[^1].PageBreakPermission != Layout.BreakPermission.Forbid)
            {
                t_boundaryRewrites++;
                _measures[^1] = _measures[^1] with { PageBreakPermission = Layout.BreakPermission.Forbid };
            }
        }
        else
            _pendingNoPageBreak = true;
    }

    /// <summary>Applies one break directive — the one dispatch every collector site
    /// calls, so the four keywords have one meaning wherever they stand (a section's
    /// music, a form, a repeat block).</summary>
    /// <param name="kind">Which of the four keywords.</param>
    /// <param name="sourcePosition">Where it was written — what a mid-bar break's request
    /// (and LYS1037, should the bar refuse to split there) points at.</param>
    public void ApplyBreak(BreakKind kind, int sourcePosition = -1)
    {
        switch (kind)
        {
            case BreakKind.NoLine: SetNoBreak(); break;
            case BreakKind.Page: SetPageBreak(sourcePosition); break;
            case BreakKind.NoPage: SetNoPageBreak(); break;
            default: SetBreak(sourcePosition); break;
        }
    }

    /// <summary>The page-break permission the measure being emitted carries, from the
    /// pending page directives, which it consumes (Force wins over Forbid, as the line
    /// pair's Force does).</summary>
    private Layout.BreakPermission TakePendingPagePermission()
    {
        var permission = _pendingPageBreak ? Layout.BreakPermission.Force
            : _pendingNoPageBreak ? Layout.BreakPermission.Forbid
            : Layout.BreakPermission.Allow;
        _pendingPageBreak = false;
        _pendingNoPageBreak = false;
        return permission;
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
            // Already typed when the emit read the bar ahead (_followingBarlineType).
            if (_measures[^1].EndBarline != endType)
            {
                t_boundaryRewrites++;
                _measures[^1] = _measures[^1] with { EndBarline = endType };
            }
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
        // A pending break/noBreak belongs to THIS measure (as in EmitMeasure) — a `break`
        // just before a bare `|` breaks after the placeholder, not the next real bar.
        bool hasBreak = _pendingBreak;
        bool noBreak = _pendingNoBreak;
        var pagePermission = TakePendingPagePermission();
        _pendingBreak = false;
        _pendingNoBreak = false;

        // THE BAR IS FILLED, not left empty: one full-measure SPACER, which is the `s1`
        // (or `s2.`, or whatever the meter's own length spells) the author would otherwise
        // have had to type. Owner's decision, 2026-08-28.
        // ⚠️ IT IS THE METER IN FORCE, not a whole note — `_bars.BarLength` is the running
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
        // Under `time none` there is no meter to fill; the spacer keeps the last METERED
        // length (the clock is frozen, so it advances nothing here either way), which is
        // what keeps every reader of a spacer's duration off zero.
        var bar = _bars.BarLength;
        var spacerLength = bar;
        // An empty bar of THIS part at a bar the score breaks inside: the placeholder is
        // cut where the music is — a head spacer up to the break, then the tail — so this
        // part's measure indices keep step with the split (MidBarBreakTable's remarks).
        if (!_bars.SenzaMisura && !_continuesBar && _currentItems.Count == 0
            && MidBarBreaks?.At(LogicalMeasureIndex) is { } split
            && split.Offset > Fraction.Zero && split.Offset < bar)
        {
            _currentItems.Add(new RestItem(split.Offset, 0, _measureSourceStart) { IsSpacer = true });
            _currentDuration = split.Offset;
            EmitSplitHead(split, sourceEnd, isEmptyPlaceholder: true);
            spacerLength = bar - split.Offset;
        }
        var spacer = new RestItem(spacerLength, 0, _measureSourceStart) { IsSpacer = true };
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
            pageBreakPermission: pagePermission,
            sectionLabelPosition: _sectionLabelPosition,
            isPickup: _bars.Partial is not null,
            unmetered: _bars.SenzaMisura,
            continuesBar: _continuesBar,
            unmeteredPosition: _frozenPosition)
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
        CloseAlternatives();
        // Handle any remaining items as the final measure
        if (_currentItems.Count > 0)
        {
            AdvanceBarPosition();
            _measures.Add(new Measure(
                _currentItems.ToImmutableArray(),
                _pendingStartBarline,
                _pendingEndBarline != BarlineType.None ? _pendingEndBarline : BarlineType.Single,
                _sectionLabel,
                _measureSourceStart,
                _measureSourceStart,  // End position same as start for incomplete
                sectionLabelPosition: _sectionLabelPosition,
                isPickup: _bars.Partial is not null,
                unmetered: _bars.SenzaMisura,
                continuesBar: _continuesBar,
                unmeteredPosition: _frozenPosition));
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
        // An incomplete final measure gets its thin bar here too, and LilyPond draws none —
        //   that is decided with the whole score in view: EndsOffTheBar / DropFinalBarLine below,
        //   called by the collector (session 659).

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

    /// <summary>
    /// True when the music this builder collected stops INSIDE a bar as LilyPond counts bars:
    /// its measurePosition at the end is not zero. Read after <see cref="FinalizeMeasures"/>.
    /// The collector decides from it — with every voice of the score in view — whether the
    /// piece's last bar line is engraved at all (<see cref="DropFinalBarLine"/>).
    /// </summary>
    /// <remarks>
    /// ⚠️ The position is the RUN's (<see cref="_barPosition"/>), not the last bar's own
    /// length: a bar written short earlier moves every later LilyPond bar line (test/barcheck:
    /// 3/4 then 5/4 comes back round to a bar line; test/cross-voice-accidental: four 2/4 bars
    /// in 4/4 end on one — LilyPond draws the final line in both, measured session 659).
    /// </remarks>
    public bool EndsOffTheBar => _barPosition > Fraction.Zero;

    /// <summary>
    /// Music that stops off the bar stops with no bar line after it: LilyPond engraves a bar
    /// line only where measurePosition comes round to zero (or where one is written with
    /// \bar). Takes the plain bar of the last bar with music (a trailing clef-only column hands
    /// its bar back first, above) away; a typed bar (`|.`, `||`, `:|`…) is a \bar and stays,
    /// an unmetered bar is left alone. A plain `|` is the twin's bar CHECK, which draws nothing.
    /// </summary>
    /// <remarks>
    /// MEASURED (scratch/beamskip/lp-bar.ly, see FinalizeMeasures: `{ c'4 }` gets NO bar; Lab
    /// sessions/p658/endbeam/vis2.ly: `c'8 d'` ends with no line, `c'2 c'8 d' e' f'` with one;
    /// Lab sessions/p659/bars/endpos.ps1: LilyPond's own measurePosition at the end of 56
    /// books whose last line Lily# drops, zero in none of them).
    /// </remarks>
    internal static void DropFinalBarLine(IList<Measure> measures)
    {
        int lastBar = measures.Count - 1;
        if (lastBar >= 0 && measures[lastBar].IsTrailingClefColumn)
            lastBar--;
        if (lastBar >= 0
            && measures[lastBar] is { EndBarline: BarlineType.Single, Unmetered: false } closing)
            measures[lastBar] = closing with { EndBarline = BarlineType.None };
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
        bool AtScopeStart,
        BarContext.MeterState Meter,
        Fraction FrozenPosition,
        Fraction? Partial,
        BarlineType PendingStartBarline,
        BarlineType PendingEndBarline,
        bool PendingBreak,
        bool PendingNoBreak,
        bool PendingPageBreak,
        bool PendingNoPageBreak,
        string? SectionLabel,
        int SectionLabelPosition,
        int MeasureSourceStart,
        Measure? LastMeasure,
        int LogicalCount,
        Fraction BarPosition,
        Fraction? AlternativeStart,
        Fraction? AlternativeUndo,
        bool InEnding,
        SectionPlayStamp? PendingPlay,
        // A clean boundary can stand at a section head (a prologue that drew no reset), and a
        // resume from it skips the prologue that armed this.
        SectionHeadState? SectionHead,
        int SectionHeadMeasures,
        TimeSignature MeterInForce);

    /// <summary>True at a checkpointable boundary: nothing pending in the
    /// current measure, not even a zero-duration directive — and not inside a split bar
    /// (its second half is under construction from the head's emit to its own close).</summary>
    internal bool AtCleanBoundary
        => _currentItems.Count == 0 && _currentDuration == Fraction.Zero && !_continuesBar;

    // ⚠️ EVERY cross-measure field goes in, including the ones only one arm reads:
    // _atScopeStart was left out until session 396, and a prefix resume restored into a
    // FRESH builder (still at its scope start) made the `|:` of `c1 | |: d1 :|` read
    // itself as anchoring the scope — the `| |:` empty bar vanished from the
    // incremental page only. The splice's state comparison reads the whole record
    // (MeasureCollector.SuffixStateMatches), so a field captured here is compared there.
    internal BuilderCheckpoint Capture() => new(
        _confirmableBoundary, _boundaryRetargetable, _lastEndAutoFill, _atScopeStart,
        _bars.Save(), _frozenPosition, _bars.Partial,
        _pendingStartBarline, _pendingEndBarline,
        _pendingBreak, _pendingNoBreak, _pendingPageBreak, _pendingNoPageBreak,
        _sectionLabel, _sectionLabelPosition, _measureSourceStart,
        _measures.Count > 0 ? _measures[^1] : null,
        _logicalCount, _barPosition, _alternativeStart, _alternativeUndo, _inEnding,
        _pendingPlay, _sectionHead, _sectionHeadMeasures, MeterInForce);

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
        _hasMeasureContent = false;
        _continuesBar = false;
        _pendingMidBar = null;
        _logicalCount = ck.LogicalCount;
        _confirmableBoundary = ck.ConfirmableBoundary;
        _boundaryRetargetable = ck.BoundaryRetargetable;
        _lastEndAutoFill = ck.LastEndAutoFill;
        _atScopeStart = ck.AtScopeStart;
        _bars.Restore(ck.Meter);
        _frozenPosition = ck.FrozenPosition;
        _bars.Partial = ck.Partial;
        _pendingStartBarline = ck.PendingStartBarline;
        _pendingEndBarline = ck.PendingEndBarline;
        _pendingBreak = ck.PendingBreak;
        _pendingNoBreak = ck.PendingNoBreak;
        _pendingPageBreak = ck.PendingPageBreak;
        _pendingNoPageBreak = ck.PendingNoPageBreak;
        _sectionLabel = ck.SectionLabel;
        _sectionLabelPosition = ck.SectionLabelPosition;
        _measureSourceStart = ck.MeasureSourceStart;
        _barPosition = ck.BarPosition;
        _alternativeStart = ck.AlternativeStart;
        _alternativeUndo = ck.AlternativeUndo;
        _inEnding = ck.InEnding;
        _pendingPlay = ck.PendingPlay;
        _sectionHead = ck.SectionHead;
        _sectionHeadMeasures = ck.SectionHeadMeasures;
        MeterInForce = ck.MeterInForce;
    }

    /// <summary>A copy of the emitted measures BEFORE <see cref="FinalizeMeasures"/>
    /// mutates them — the values a resumed walk re-enters with.</summary>
    internal List<Measure> MeasuresSnapshot() => new(_measures);
}

