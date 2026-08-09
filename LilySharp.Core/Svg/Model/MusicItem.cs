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

namespace LilySharp.Core.Svg.Model;

/// <summary>
/// Notehead style from a <c>@notehead.x</c>-family annotation.
/// LILYPOND-REF: scm/define-grobs.scm NoteHead style property.
/// </summary>
public enum NoteheadStyle
{
    /// <summary>Default (plain) notehead.</summary>
    Default,
    /// <summary>Cross "x" notehead (<c>@notehead.x</c>) for ghost/percussion notes.</summary>
    Cross,
    /// <summary>Diamond notehead (<c>@notehead.diamond</c>) for harmonics.</summary>
    Diamond,
    /// <summary>Triangle notehead (<c>@notehead.triangle</c>) for the shape-note "do".</summary>
    Triangle,
    /// <summary>Slash notehead (<c>@notehead.slash</c>) for rhythm/comping notation.</summary>
    Slash,
    /// <summary>Crossed-circle notehead (<c>@notehead.xcircle</c>) for hi-hat and similar.</summary>
    XCircle,
}

/// <summary>
/// Base type for all music items that have duration.
/// </summary>
public abstract record MusicItem
{
    /// <summary>The duration of this item as a fraction of a whole note.</summary>
    public abstract Fraction Duration { get; }

    /// <summary>Source position in the syntax tree for click-to-source mapping.</summary>
    public abstract int SourcePosition { get; }

    /// <summary>
    /// Whether this item is a "loose" column that does not participate in spacing.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spacing-spanner.cc:200-280
    /// Loose grobs (tuplet brackets, fermata marks, etc.) are skipped during
    /// spring creation. They are positioned relative to their parent columns
    /// rather than being spacing contributors.
    /// </remarks>
    public virtual bool IsLoose => false;

    /// <summary>
    /// The staff position of this item's curve-side edge note: a chord's TOP note
    /// when <paramref name="preferTop"/> (e.g. an up-curving slur or tie), its BOTTOM
    /// note otherwise; a single note is its own edge. Null for items with no note
    /// head (rests, spacers, barlines).
    /// </summary>
    public static int? EdgeStaffPosition(MusicItem item, bool preferTop) => item switch
    {
        NoteItem n => n.StaffPosition,
        ChordItem c when c.Notes.Length > 0
            => preferTop ? c.Notes.Max(n => n.StaffPosition) : c.Notes.Min(n => n.StaffPosition),
        _ => null
    };
}

/// <summary>
/// A single note.
/// </summary>
public sealed record NoteItem : MusicItem
{
    // init so a post-pass (e.g. OttavaTransposer) can shift the DISPLAY position
    // an octave without disturbing pitch/MIDI (which live in Midi/the syntax tree).
    /// <summary>The note's clef-relative vertical staff position, in diatonic steps from the middle staff line.</summary>
    public int StaffPosition { get; init; }
    /// <summary>The written note value before dots and tuplet scaling, as a fraction of a whole note.</summary>
    public Fraction BaseDuration { get; }
    /// <summary>The number of augmentation dots on the note.</summary>
    public int Dots { get; }
    /// <summary>The accidental glyph drawn left of the notehead (e.g. <c>sharp</c>, <c>flat</c>), or null for none.</summary>
    /// <remarks><c>init</c> so a per-STAFF post-pass can take it away: a tablature context
    /// has no Accidental grob at all (see <see cref="Collector.TabResolver.RemoveAccidentals"/>),
    /// and the accidental is decided once per PART, before the score binds that part to a
    /// staff.</remarks>
    public string? Accidental { get; init; }
    /// <summary>Whether the note needs ledger lines because it sits outside the staff.</summary>
    public bool NeedsLedgerLines { get; }
    /// <summary>Number of tremolo beams (0 = no tremolo, 1-3 = tremolo).</summary>
    public int TremoloBeams { get; }
    /// <summary>Two-note (chord) tremolo: the BETWEEN-stems beam count. The
    /// pair is written at the tremolo's total duration and sounds half of it
    /// each (TimeScale ½). 0 = not part of a pair.
    /// LILYPOND-REF: lily/chord-tremolo-engraver.cc.</summary>
    public int TremoloPairBeams { get; init; }
    /// <summary>How many of the pair's beams are GAPPED — drawn short of the
    /// stems so the repeat symbol can't be read as an ordinary beam. 0 for a
    /// half-note pair (halves can't appear in a regular beam, so their tremolo
    /// beams reach the stems).
    /// LILYPOND-REF: lily/chord-tremolo-engraver.cc:117-140 acknowledge_stem —
    /// gap-count = min(flags, intlog2(repeat_count) + 1), set unless
    /// duration_log == 1.</summary>
    public int TremoloGapCount { get; init; }
    /// <summary>Notehead style (x / diamond / triangle / slash / xcircle).</summary>
    public NoteheadStyle Notehead { get; init; }
    /// <summary>Whether this note starts a tie to the next note.</summary>
    public bool HasTieStart { get; }
    /// <summary>Whether this note starts a slur.</summary>
    public bool HasSlurStart { get; }
    /// <summary>Whether this note ends a slur.</summary>
    public bool HasSlurEnd { get; }
    /// <summary>Whether this note starts a manual beam group.</summary>
    public bool HasBeamStart { get; init; }
    /// <summary>Whether this note ends a manual beam group.</summary>
    public bool HasBeamEnd { get; init; }
    /// <summary>
    /// Identity of the beam this note's stem belongs to, or null when it carries no beam.
    /// Set by <see cref="Collector.MeasureCollector"/> once BeamDetector has resolved the
    /// groups; the number itself is meaningless — only equality with another item's
    /// <see cref="BeamId"/> is.
    /// </summary>
    /// <remarks>
    /// This stands in for a Beam grob pointer. LilyPond asks whether two adjacent note
    /// columns share a beam by comparing the two <c>Stem::get_beam</c> results directly —
    /// LILYPOND-REF: lily/note-spacing.cc:288-293 stem_dir_correction
    /// (<c>beams_drul[LEFT] == beams_drul[RIGHT]</c> selects knee_correction over
    /// different_directions_correction). Nothing else about the beam is needed there, so
    /// nothing else is carried here.
    /// </remarks>
    public int? BeamId { get; init; }

    /// <summary>
    /// The PURE tip of this note's beamed stem, in staff positions (+up): the extreme of
    /// the beam group's same-direction members' UNBEAMED stem tips. Null when unbeamed.
    /// Baked by <c>MeasureCollector.ResolveBeamStemDirections</c> — the Lily# form of
    /// LilyPond's per-stem pure-height cache — and consumed by
    /// <c>SpacingRules.StemSpacingInfo</c>, whose whole band every spacing reader shares.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/stem.cc:387-447 Stem::internal_pure_height — the calc_beam
    ///   branch unites the same-direction members' unbeamed heights and clips the
    ///   NON-stem side back to this stem's own (:443 iv.intersect); the clip means one
    ///   number (the stem-side extreme) carries the whole result.
    /// LILYPOND-REF: lily/stem.cc:449-458 Stem::cache_pure_height — LilyPond itself
    ///   stores the answer on each stem, which is what baking it here mirrors.
    /// </remarks>
    public double? PureBeamedStemTip { get; init; }

    /// <summary>Whether this note belongs to a beam group (set by BeamDetector once the
    /// group is resolved). Unlike <see cref="HasBeamStart"/>/<see cref="HasBeamEnd"/> this
    /// is true for a mid-beam note too, so slur edge scoring can ask "is this note beamed".
    /// It is exactly "<see cref="BeamId"/> is set" — one fact, one field.</summary>
    public bool IsBeamed => BeamId is not null;
    /// <summary>Whether this note has a glissando to the next note.</summary>
    public bool HasGlissando { get; }
    /// <summary>Feathered beam direction: 0=none, 1=right (accel), -1=left (rit).</summary>
    /// <remarks>LILYPOND-REF: beam.cc:1039-1082 grow-direction</remarks>
    public int FeatherDirection { get; }
    /// <summary>Whether this accidental is a courtesy (cautionary) accidental shown in parentheses.</summary>
    /// <remarks>LILYPOND-REF: lily/accidental.cc:147-148 parenthesized property</remarks>
    public bool IsCourtesy { get; }

    /// <summary>
    /// This note's accidental ink-left X, in staff spaces from the NOTE COLUMN's reference
    /// point, once the whole staff column's accidentals have been packed together — or null
    /// where nothing but this item stands on the column and the per-item solve is the same
    /// answer. See <see cref="Collector.StaffAccidentalColumns"/>.
    /// </summary>
    /// <remarks>
    /// The frame is the COLUMN, not this item: LilyPond's accidentals do NOT ride the
    /// note-collision shift (MEASURED — audit/lp-geometry/probes/cross-voice-accidental.ly,
    /// book XVA: the shifted voice's own flat still sits 0.35 left of the OTHER voice's head).
    /// Every reservation site already measures from the column, so they read this bare; the
    /// renderer draws at the shifted X and must subtract that shift back off.
    /// </remarks>
    public double? AccidentalX { get; init; }
    /// <summary>Whether this note is a cue note (drawn at reduced size).</summary>
    /// <remarks>LILYPOND-REF: ly/engraver-init.ly CueVoice context — fontSize = #-4, magstep(-4) ≈ 0.66</remarks>
    public bool IsCue { get; }
    /// <summary>
    /// Editorial (suggestion) accidental kind ("sharp", "flat", "natural", ...)
    /// shown as a small accidental ABOVE the note (musica ficta), or null.
    /// When set, the regular left-of-note <see cref="Accidental"/> is
    /// suppressed — the suggestion replaces it.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:96-123 AccidentalSuggestion —
    /// (direction . UP), (font-size . -2), centered on the notehead.
    /// Different from IsCourtesy: editorial accidentals are musicological
    /// suggestions, while courtesy accidentals are reminders of canceled
    /// accidentals (parenthesized, left of the note).
    /// </remarks>
    /// <remarks><c>init</c> for the same reason as <see cref="Accidental"/>: the
    /// Accidental_engraver makes AccidentalSuggestion grobs too, so removing it removes
    /// these as well.</remarks>
    public string? EditorialAccidental { get; init; }

    /// <summary>Whether this note carries an editorial (suggestion) accidental.</summary>
    public bool IsEditorial => EditorialAccidental != null;
    /// <summary>
    /// Optional finger number (1..5) attached to this note. Null when no fingering.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/fingering-engraver.cc — Fingering grob from finger event
    /// LilyPond syntax: <c>c4-1</c>; LilySharp surface: <c>c4@finger.1</c> via the
    /// existing compound-mark parser (no parser change required).
    /// </remarks>
    public int? Fingering { get; }

    /// <summary>
    /// True when this note has a laissez-vibrer (l.v.) tie — a half-tie pointing
    /// to the right from the note, indicating "let ring".
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/laissez-vibrer-engraver.cc — LaissezVibrerTie grob
    /// LilyPond syntax: <c>c4\laissezVibrer</c>; LilySharp surface: <c>c4@laissezVibrer</c>.
    /// </remarks>
    public bool HasLaissezVibrer { get; }

    /// <summary>
    /// Forced curve side of the l.v. tie from <c>@laissezVibrer.up/.down</c>
    /// (LP's <c>^\laissezVibrer</c>/<c>_\laissezVibrer</c>); null = automatic
    /// (the standard-directions rule — see TieVariantEngraver.SemiTiesOf).
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/laissez-vibrer-engraver.cc:99-103 acknowledge_note_head
    /// — the event's direction property is copied onto the tie.</remarks>
    public bool? LaissezVibrerUp { get; init; }

    /// <summary>
    /// True when this note has a repeat-tie — a half-tie pointing in from the LEFT,
    /// typically used after a repeat barline to indicate continuation from the
    /// previous volta.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/repeat-tie-engraver.cc — RepeatTie grob
    /// LilyPond syntax: <c>c4\repeatTie</c>; LilySharp surface: <c>c4@repeatTie</c>.
    /// </remarks>
    public bool HasRepeatTie { get; }

    /// <summary>
    /// Forced curve side of the repeat tie from <c>@repeatTie.up/.down</c>
    /// (LP's <c>^\repeatTie</c>/<c>_\repeatTie</c>); null = automatic.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/laissez-vibrer-engraver.cc:99-103 acknowledge_note_head
    /// — the event's direction is copied onto the tie; Repeat_tie_engraver inherits
    /// this whole path (repeat-tie-engraver.cc:27-33, a Laissez_vibrer_engraver that
    /// only swaps the event class and grob names).</remarks>
    public bool? RepeatTieUp { get; init; }
    private readonly int _sourcePosition;

    /// <summary>
    /// Time scale applied by enclosing tuplets (base/ratio, compounded when
    /// nested); 1 outside tuplets. <see cref="BaseDuration"/> stays the
    /// written notation; <see cref="Duration"/> is actual time.
    /// </summary>
    public Fraction TimeScale { get; init; } = new Fraction(1, 1);

    /// <summary>The sounding duration with dots and tuplet <see cref="TimeScale"/> applied, as a fraction of a whole note.</summary>
    public override Fraction Duration =>
        (Dots > 0 ? BaseDuration.Dotted(Dots) : BaseDuration) * TimeScale;
    /// <summary>The note's source position in the syntax tree.</summary>
    public override int SourcePosition => _sourcePosition;

    /// <summary>
    /// Beam-resolved stem direction. A beam forces ONE direction onto all its
    /// members; the collector bakes that in here so spacing (skyline rods,
    /// stem-direction corrections) sees the same stems the renderer draws.
    /// LilyPond resolves directions in the engravers BEFORE spacing runs.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/beam.cc:182-246 Beam::calc_direction.</remarks>
    public bool? StemUpOverride { get; init; }

    /// <summary>
    /// A stem direction the WRITER asked for — <c>@stemUp</c> / <c>@stemDown</c> — or null.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/beam.cc:894-905 Beam::get_default_dir and :946-956
    /// set_stem_directions — LilyPond keeps the asked-for direction and the derived one in
    /// SEPARATE properties (<c>direction</c>, set by <c>\stemUp</c>, against
    /// <c>default-direction</c>, the pitch callback), and the beam reads both: a stem that
    /// already carries a <c>direction</c> makes <c>force_dir</c> true — which turns OFF the
    /// "farthest head decides" rule and hands the beam to a vote among the stems — and it
    /// KEEPS its own direction when the beam stamps the group's onto the others.
    /// <para>
    /// ⚠️ THIS USED TO BE THE SAME FIELD AS <see cref="StemUpOverride"/>, and that is exactly
    /// why <c>@stemDown</c> did nothing to a beamed note: the beam resolver wrote the group's
    /// direction over every member and the annotation vanished without a word. Two quantities
    /// in one slot — the mirror of the trap HANDOFF 5.2 records about a bow's two heights.
    /// </para>
    /// </remarks>
    public bool? ForcedStemUp { get; init; }

    /// <summary>
    /// Grace notes written immediately before this note, "hanging" to the left of
    /// its column. Like a mid-measure clef change, they occupy horizontal space in
    /// FRONT of this column, so the spacing reserves their width via the same
    /// prefix-width mechanism (see MeasureLayouter). Empty when there is none.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/grace-spacing-engraver.cc — grace columns precede the main note's column.</remarks>
    public ImmutableArray<GraceNoteInfo> LeadingGrace { get; init; } = ImmutableArray<GraceNoteInfo>.Empty;

    /// <summary>
    /// Explicit tab string number (1 = highest-pitch string) from a <c>\N</c>
    /// annotation, or null for automatic string selection. On a tab staff the
    /// fret is computed for THIS string; ignored on a notation staff.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/tab-note-heads-engraver.cc — string number forces the fret's string.</remarks>
    public int? StringNumber { get; init; }

    /// <summary>The absolute sounding MIDI note number (clef-independent), used by
    /// the tab staff to compute frets — <see cref="StaffPosition"/> is a
    /// clef-relative visual position and must not be used for pitch.</summary>
    public int Midi { get; init; }

    /// <summary>
    /// True when this note is the DESTINATION of a tie (the held continuation).
    /// On a tab staff its fret number is hidden — the held string is not re-struck,
    /// so only the tie line shows the continuation (the rhythm/stem still draws).
    /// Ignored on a notation staff, which shows the tied notehead normally.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/tab-note-heads-engraver.cc — tied tab heads are transparent.</remarks>
    public bool IsTieTarget { get; init; }

    /// <summary>
    /// True when this is a dead (muted / ghost) note — a cross "×" notehead in
    /// notation and an "×" in place of the fret number in tab.
    /// </summary>
    /// <remarks>LILYPOND-REF: ly/property-init.ly \deadNote — cross notehead, muted.</remarks>
    public bool IsDead { get; init; }

    /// <summary>
    /// True when this note sounds BELOW the tab's lowest string, so it cannot be
    /// fretted (it would otherwise clamp to a wrong open string, fret 0). On a tab
    /// staff nothing is drawn for it — no fret number, no stem, no beam — since a
    /// wrong pitch is worse than a gap. Set per tab staff (TabResolver); ignored on
    /// notation staves, which show the note at its true pitch. A TabOutOfRange
    /// warning still fires (TabRangeValidator).
    /// </summary>
    public bool TabBelowRange { get; init; }

    /// <summary>Stem direction: beam-resolved if beamed, else asked for, else by staff position.</summary>
    public bool StemUp => StemUpOverride ?? ForcedStemUp ?? StaffPosition < 0;

    /// <summary>Whether this note has a tremolo marking.</summary>
    public bool HasTremolo => TremoloBeams > 0;

    /// <summary>Initializes a new <see cref="NoteItem"/>.</summary>
    public NoteItem(int staffPosition, Fraction baseDuration, int dots, string? accidental, bool needsLedgerLines, int sourcePosition, int tremoloBeams = 0, bool hasTieStart = false, bool hasSlurStart = false, bool hasSlurEnd = false, bool hasBeamStart = false, bool hasBeamEnd = false, bool hasGlissando = false, int featherDirection = 0, bool isCourtesy = false, bool isCue = false, string? editorialAccidental = null, int? fingering = null, bool hasLaissezVibrer = false, bool hasRepeatTie = false)
    {
        StaffPosition = staffPosition;
        BaseDuration = baseDuration;
        Dots = dots;
        Accidental = accidental;
        NeedsLedgerLines = needsLedgerLines;
        TremoloBeams = Math.Clamp(tremoloBeams, 0, 3);
        HasTieStart = hasTieStart;
        HasSlurStart = hasSlurStart;
        HasSlurEnd = hasSlurEnd;
        HasBeamStart = hasBeamStart;
        HasBeamEnd = hasBeamEnd;
        HasGlissando = hasGlissando;
        FeatherDirection = Math.Clamp(featherDirection, -1, 1);
        IsCourtesy = isCourtesy;
        IsCue = isCue;
        EditorialAccidental = editorialAccidental;
        Fingering = fingering;
        HasLaissezVibrer = hasLaissezVibrer;
        HasRepeatTie = hasRepeatTie;
        _sourcePosition = sourcePosition;
    }
}

/// <summary>
/// A rest.
/// </summary>
public sealed record RestItem : MusicItem
{
    /// <summary>The written rest value before dots and tuplet scaling, as a fraction of a whole note.</summary>
    public Fraction BaseDuration { get; }
    /// <summary>The number of augmentation dots on the rest.</summary>
    public int Dots { get; }
    private readonly int _sourcePosition;

    /// <summary>Tuplet time scale; see <see cref="NoteItem.TimeScale"/>.</summary>
    public Fraction TimeScale { get; init; } = new Fraction(1, 1);

    /// <summary>
    /// An invisible time-filler (LilyPond <c>s</c> skip), used by chord rows to give
    /// the layout timing columns without drawing anything. Never rendered, and never
    /// collapses into a multi-measure rest.
    /// </summary>
    public bool IsSpacer { get; init; }

    /// <summary>
    /// True iff this rest was written as an explicit multi-measure rest (LilyPond's
    /// capital <c>R</c>), which is centred between the bar lines as a Multi_measure_rest.
    /// A plain lowercase <c>r</c> that happens to fill the measure is an ordinary Rest:
    /// it sits at its rhythmic moment (beat 1) and is never collapsed/centred.
    /// LILYPOND-REF: scm/define-grobs.scm Rest vs MultiMeasureRest.
    /// </summary>
    public bool IsMultiMeasure { get; init; }

    /// <summary>
    /// The PURE estimate of how far a beam will push this rest, in staff positions from
    /// its default origin (up-positive), 0 when the rest is under no beam. Baked by
    /// <c>MeasureCollector.ResolveBeamStemDirections</c> for every rest a manual beam
    /// runs over, so the horizontal spacing sees the rest roughly where the beam will
    /// put it — BEFORE any beam is quanted, exactly LilyPond's split: spacing reads the
    /// estimate, the print reads the real collision shift.
    /// LILYPOND-REF: lily/beam.cc:1421-1494 Beam::pure_rest_collision_callback —
    /// the closest beam is guessed four staff positions past the neighbouring heads'
    /// average, never crossing the staff centre by more than two.
    /// </summary>
    public double PureBeamShift { get; init; }

    /// <summary>
    /// The voice direction forced on this rest by a <c>voice { } { }</c> span
    /// (+1 up / −1 down), 0 outside any span. Baked by
    /// <c>MeasureCollector.BakeVoicedRestDirections</c> from the same
    /// <c>VoiceDefaults.GetDefaultStemUpAt</c> reading every other consumer spends,
    /// so the horizontal spacing can price the rest at its PURE voiced position —
    /// LilyPond's Rest carries <c>direction</c> from the voice-props layer and its
    /// separation box reads the pure Y that direction produces.
    /// LILYPOND-REF: scm/music-functions.scm:666-674 make-voice-props-set —
    /// direction on direction-polyphonic-grobs (Rest is in the list);
    /// LILYPOND-REF: lily/separation-item.cc:163 boxes — pure_y_extent.
    /// </summary>
    public int VoiceDirection { get; init; }

    /// <summary>
    /// True when a slur OPENS on this rest (LilyPond <c>r16(</c>). A rest is a
    /// legal slur bound: LilyPond's rests live inside NoteColumn grobs, so the
    /// Slur_engraver binds to them like any column ("slur-rest-direction.ly").
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/slur-scoring.cc:587-619 get_base_attachments —
    /// a rest bound is NOT a note-column bound to the scorer (extremes_ carries
    /// no note_column_): the fallback loop reads the first/last ENCOMPASSED
    /// column's Y extent — the rest's own ink — plus dir·0.5·staff-space.
    /// MEASURED with debug-slur-scoring (the all-rest 16th slur's winner is
    /// idx=0 TOTAL=0.00 at ink-bottom 2.05 + 0.5 = 2.55); an earlier remark here
    /// claimed the note-column branch (:543-573), which that measurement
    /// refuted.</remarks>
    public bool HasSlurStart { get; init; }

    /// <summary>True when a slur CLOSES on this rest (LilyPond <c>r)</c>).
    /// See <see cref="HasSlurStart"/>.</summary>
    public bool HasSlurEnd { get; init; }

    /// <summary>The sounding duration with dots and tuplet <see cref="TimeScale"/> applied, as a fraction of a whole note.</summary>
    public override Fraction Duration =>
        (Dots > 0 ? BaseDuration.Dotted(Dots) : BaseDuration) * TimeScale;
    /// <summary>The rest's source position in the syntax tree.</summary>
    public override int SourcePosition => _sourcePosition;

    /// <summary>Initializes a new <see cref="RestItem"/>.</summary>
    public RestItem(Fraction baseDuration, int dots, int sourcePosition)
    {
        BaseDuration = baseDuration;
        Dots = dots;
        _sourcePosition = sourcePosition;
    }
}

/// <summary>
/// Information about a single note within a chord.
/// </summary>
public readonly record struct ChordNoteInfo(
    int StaffPosition,
    string? Accidental,
    bool NeedsLedgerLines,
    // Whether this accidental is a courtesy (cautionary) accidental shown in parentheses.
    bool IsCourtesy = false,
    // Optional per-pitch finger number attached via @finger.N inside a chord.
    // LILYPOND-REF: lily/fingering-engraver.cc — FingeringColumn stacking.
    int? Fingering = null,
    // Explicit tab string number (1 = highest) via \N inside a chord, or null for auto.
    int? StringNumber = null,
    // Per-note notehead style (drum chords mix heads: bd default,
    // hh cross); Default falls back to the chord-level style.
    NoteheadStyle Notehead = NoteheadStyle.Default,
    // Absolute sounding MIDI number (clef-independent), for tab frets.
    int Midi = 0,
    // Source offset of THIS member's pitch token (so the interactive preview can
    // highlight/select one chord note at a time and jump the caret to its exact
    // pitch, not the chord's '<'). -1 = fall back to the chord's SourcePosition.
    int SourcePosition = -1,
    // This member's accidental ink-left X in staff spaces from the NOTE COLUMN's
    // reference point, once the whole staff column's accidentals have been packed
    // together — null where this chord stands alone on its column and the per-item
    // solve is the same answer. Same frame and same reason as NoteItem.AccidentalX.
    double? AccidentalX = null,
    // Laissez-vibrer half-tie on THIS member. A chord-level @laissezVibrer marks
    // every member; a member-level one marks just its own head.
    // LILYPOND-REF: lily/laissez-vibrer-engraver.cc:66-108 acknowledge_note_head —
    // "use the heard event_ for all note heads, or an individual event for just
    // a single note head (attached as an articulation inside a chord)".
    bool HasLaissezVibrer = false,
    // Forced curve side from ^/_ on the l.v. event (acknowledge_note_head copies
    // the event's direction onto the tie, :99-103); null = automatic.
    bool? LaissezVibrerUp = null,
    // Repeat-tie half-tie on THIS member — the same fan as the l.v. above:
    // Repeat_tie_engraver IS a Laissez_vibrer_engraver (it only swaps the event
    // class and the grob names), so a chord-level @repeatTie half-ties every
    // member and a member-level one just its own head.
    // LILYPOND-REF: lily/repeat-tie-engraver.cc:27-33 Repeat_tie_engraver —
    // class Repeat_tie_engraver final : public Laissez_vibrer_engraver.
    bool HasRepeatTie = false,
    // Forced curve side from ^/_ on the repeat-tie event; null = automatic.
    bool? RepeatTieUp = null
);

/// <summary>
/// A chord (multiple notes played simultaneously).
/// </summary>
public sealed record ChordItem : MusicItem
{
    /// <summary>The notes making up this chord.</summary>
    public ImmutableArray<ChordNoteInfo> Notes { get; init; }

    /// <summary>
    /// Whether this chord's accidentals were packed with the rest of its staff column
    /// (<see cref="Collector.StaffAccidentalColumns"/>), i.e. whether any member carries a
    /// <see cref="ChordNoteInfo.AccidentalX"/>.
    /// </summary>
    /// <remarks>
    /// ⚠️ A HAND-WRITTEN LOOP, ON PURPOSE. Five layout sites ask this per chord, and spacing
    /// asks it once per item per column on every break attempt; the obvious
    /// <c>Notes.Any(n =&gt; n.AccidentalX.HasValue)</c> boxes ImmutableArray's struct
    /// enumerator on each of those calls. Measured on the corpus's polyphonic books, that is
    /// the only allocation this predicate needs to not make.
    /// </remarks>
    public bool HasPackedAccidentals
    {
        get
        {
            var notes = Notes;
            for (int i = 0; i < notes.Length; i++)
                if (notes[i].AccidentalX.HasValue)
                    return true;
            return false;
        }
    }
    /// <summary>The written chord value before dots and tuplet scaling, as a fraction of a whole note.</summary>
    public Fraction BaseDuration { get; }
    /// <summary>The number of augmentation dots on the chord.</summary>
    public int Dots { get; }
    /// <summary>Two-note tremolo between-beams count (see NoteItem).</summary>
    public int TremoloPairBeams { get; init; }
    /// <summary>Gapped tremolo-pair beam count (see <see cref="NoteItem.TremoloGapCount"/>).</summary>
    public int TremoloGapCount { get; init; }
    /// <summary>Notehead style applied to ALL heads of this chord.</summary>
    public NoteheadStyle Notehead { get; init; }
    /// <summary>Whether this chord starts glissandi to the next note/chord — one line
    /// per member head, paired in written order (see <see cref="GlissandoItem"/>).</summary>
    /// <remarks>LILYPOND-REF: scm/scheme-engravers.scm:2519-2579 Glissando_engraver —
    ///   the start column contributes every note head as a left bound.</remarks>
    public bool HasGlissando { get; init; }
    /// <summary>Number of tremolo beams (0 = no tremolo, 1-3 = tremolo).</summary>
    public int TremoloBeams { get; }
    /// <summary>Whether this chord starts a manual beam group (init so the
    /// tremolo-pair path can join the pair, like NoteItem's).</summary>
    public bool HasBeamStart { get; init; }
    /// <summary>Whether this chord ends a manual beam group.</summary>
    public bool HasBeamEnd { get; init; }
    /// <summary>Identity of the beam this chord's stem belongs to; see
    /// <see cref="NoteItem.BeamId"/>.</summary>
    public int? BeamId { get; init; }
    /// <summary>The PURE tip of this chord's beamed stem, in staff positions (+up); see
    /// <see cref="NoteItem.PureBeamedStemTip"/>.</summary>
    public double? PureBeamedStemTip { get; init; }
    /// <summary>Whether this chord belongs to a beam group (set by BeamDetector; true for a
    /// mid-beam chord too). See <see cref="NoteItem.IsBeamed"/>.</summary>
    public bool IsBeamed => BeamId is not null;
    /// <summary>Whether this chord has an arpeggio marking.</summary>
    public bool HasArpeggio { get; }
    /// <summary>
    /// Whether that marking is a BRACKET rather than a wiggle — <c>@arpeggio(bracket)</c>,
    /// LilyPond's <c>\nonArpeggiato</c>: the chord is NOT to be rolled.
    /// </summary>
    /// <remarks>
    /// ⚠️ SEPARATE FROM <see cref="HasArpeggio"/> BECAUSE THEY ARE DIFFERENT GROBS AND THE
    /// SPACING HAS TO TELL THEM APART. LilyPond's Arpeggio_engraver makes ONE item per chord
    /// whose type is Arpeggio, ChordBracket or ChordSlur
    /// (lily/arpeggio-engraver.cc:132-148 process_music), and their widths differ: a wiggle is
    /// the <c>scripts.arpeggio</c> glyph's 0.800000, a bracket is line-thickness plus
    /// protrusion, 0.500000. Both are reserved for — :124-129 <c>acknowledge_note_column</c>
    /// adds whichever one was made to the note column as a conditional item, blind to type.
    /// <para>
    /// Until 2026-08-03 nothing carried this to the spacing: <c>ItemSkylineFactory.AddArpeggio</c>
    /// gated on <see cref="HasArpeggio"/>, which <c>HasArpeggioArticulation</c> sets only for a
    /// plain <c>@arpeggio</c>, so a bracketed chord reserved NOTHING and a compressed column
    /// came closer than the bracket's own ink — measured as audit/lp-geometry
    /// <c>chordbracket.x.previous-head-to-bracket.compressed</c>, 0.300000 of column pitch.
    /// </para>
    /// </remarks>
    public bool HasArpeggioBracket { get; init; }
    /// <summary>Whether this chord is a cue chord (drawn at reduced size).</summary>
    /// <remarks>LILYPOND-REF: ly/engraver-init.ly CueVoice context — fontSize = #-4, magstep(-4) ≈ 0.66</remarks>
    public bool IsCue { get; }
    private readonly int _sourcePosition;

    /// <summary>Tuplet time scale; see <see cref="NoteItem.TimeScale"/>.</summary>
    public Fraction TimeScale { get; init; } = new Fraction(1, 1);

    /// <summary>The sounding duration with dots and tuplet <see cref="TimeScale"/> applied, as a fraction of a whole note.</summary>
    public override Fraction Duration =>
        (Dots > 0 ? BaseDuration.Dotted(Dots) : BaseDuration) * TimeScale;
    /// <summary>The chord's source position in the syntax tree.</summary>
    public override int SourcePosition => _sourcePosition;

    /// <summary>Beam-resolved stem direction; see <see cref="NoteItem.StemUpOverride"/>.</summary>
    public bool? StemUpOverride { get; init; }

    /// <summary>A stem direction the writer asked for; see <see cref="NoteItem.ForcedStemUp"/>.</summary>
    public bool? ForcedStemUp { get; init; }

    /// <summary>Leading grace notes hanging left of this chord's column; see
    /// <see cref="NoteItem.LeadingGrace"/>.</summary>
    public ImmutableArray<GraceNoteInfo> LeadingGrace { get; init; } = ImmutableArray<GraceNoteInfo>.Empty;

    /// <summary>Stem direction: beam-resolved if beamed, else down unless the
    /// midpoint of the EXTREME heads lies below the middle line. LilyPond weighs
    /// the notes farthest from centre (top and bottom of the chord), not the
    /// mean of all heads, so a lopsided chord straddling the middle line takes
    /// its stem from the side with the more distant note.</summary>
    /// <remarks>LILYPOND-REF: lily/stem.cc:800-805 calc_default_direction —
    /// dir = sign(ddistance − udistance) = sign(−(hp[UP] + hp[DOWN])).</remarks>
    public bool StemUp => StemUpOverride ?? ForcedStemUp ?? (Notes.Length > 0
        && Notes.Max(n => n.StaffPosition) + Notes.Min(n => n.StaffPosition) < 0);

    /// <summary>Whether this chord has a tremolo marking.</summary>
    public bool HasTremolo => TremoloBeams > 0;

    /// <summary>
    /// True when the chord is followed by <c>~</c>: every matching pitch in the
    /// next chord/note is tied (LP TieColumn behaviour).
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/tie-column.cc — TieColumn groups chord ties.</remarks>
    public bool HasTieStart { get; }

    /// <summary>Whether this chord opens a slur (a <c>(</c> follows it).</summary>
    public bool HasSlurStart { get; }
    /// <summary>Whether this chord closes a slur (a <c>)</c> follows it).</summary>
    public bool HasSlurEnd { get; }

    /// <summary>Initializes a new <see cref="ChordItem"/>.</summary>
    public ChordItem(ImmutableArray<ChordNoteInfo> notes, Fraction baseDuration, int dots, int sourcePosition, int tremoloBeams = 0, bool hasBeamStart = false, bool hasBeamEnd = false, bool hasArpeggio = false, bool isCue = false, bool hasTieStart = false, bool hasSlurStart = false, bool hasSlurEnd = false)
    {
        Notes = notes;
        BaseDuration = baseDuration;
        Dots = dots;
        TremoloBeams = Math.Clamp(tremoloBeams, 0, 3);
        HasBeamStart = hasBeamStart;
        HasBeamEnd = hasBeamEnd;
        HasArpeggio = hasArpeggio;
        IsCue = isCue;
        HasTieStart = hasTieStart;
        HasSlurStart = hasSlurStart;
        HasSlurEnd = hasSlurEnd;
        _sourcePosition = sourcePosition;
    }
}

/// <summary>
/// A mid-measure clef change. Has zero duration — occupies horizontal space
/// but does not advance the timing position.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/clef-engraver.cc — mid-measure clef changes use smaller
/// "_change" glyph variants (e.g., clefs.G_change instead of clefs.G).
/// LILYPOND-REF: lily/clef.cc:29-52 — calc_glyph_name appends "_change" suffix.
/// </remarks>
public sealed record ClefChangeItem : MusicItem
{
    /// <summary>The new clef type after the change.</summary>
    public ClefType NewClef { get; }

    private readonly int _sourcePosition;

    /// <summary>Always <c>Fraction.Zero</c> — the clef change occupies horizontal space but no time.</summary>
    public override Fraction Duration => Fraction.Zero;
    /// <summary>The clef change's source position in the syntax tree.</summary>
    public override int SourcePosition => _sourcePosition;

    /// <summary>
    /// True for the two clefs a <c>cue &lt;clef&gt; { … }</c> region raises — LilyPond's
    /// <c>\cueClef</c> and <c>\cueClefUnset</c>.
    /// </summary>
    /// <remarks>
    /// MEASURED (audit/lp-geometry/probes/cue-span.ly, book D-WITH): a cue clef is the
    /// PLAIN glyph at font-size −4, not the <c>_change</c> variant an ordinary mid-measure
    /// change uses — <c>glyph=clefs.F fontsize=-4</c> going in and <c>glyph=clefs.G
    /// fontsize=-4</c> coming back out. Both are drawn; the region's notes are positioned
    /// in the cue clef, and without the closing one the change leaks into the rest of the
    /// staff (book D-NOUNSET reads the following note 12 staff positions away).
    /// LILYPOND-REF: ly/music-functions-init.ly cueClef / cueClefUnset;
    ///   scm/define-grobs.scm CueClef / CueEndClef.
    /// </remarks>
    public bool IsCue { get; }

    /// <summary>Initializes a new <see cref="ClefChangeItem"/>.</summary>
    public ClefChangeItem(ClefType newClef, int sourcePosition, bool isCue = false)
    {
        NewClef = newClef;
        _sourcePosition = sourcePosition;
        IsCue = isCue;
    }
}

/// <summary>
/// A mid-measure key signature change. Has zero duration — occupies horizontal space
/// but does not advance the timing position.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/key-engraver.cc — process_music() creates KeySignature grob
/// when keyAlterations changes. Cancellation naturals show notes removed from previous key.
/// </remarks>
public sealed record KeySignatureChangeItem : MusicItem
{
    /// <summary>The new key signature after the change.</summary>
    public KeySignature NewKey { get; }

    /// <summary>The previous key signature (for cancellation naturals).</summary>
    public KeySignature PreviousKey { get; }

    private readonly int _sourcePosition;

    /// <summary>Always <c>Fraction.Zero</c> — the key change occupies horizontal space but no time.</summary>
    public override Fraction Duration => Fraction.Zero;
    /// <summary>The key change's source position in the syntax tree.</summary>
    public override int SourcePosition => _sourcePosition;

    /// <summary>Initializes a new <see cref="KeySignatureChangeItem"/>.</summary>
    public KeySignatureChangeItem(KeySignature newKey, KeySignature previousKey, int sourcePosition)
    {
        NewKey = newKey;
        PreviousKey = previousKey;
        _sourcePosition = sourcePosition;
    }
}

/// <summary>
/// A mid-piece time signature change. Has zero duration — occupies horizontal
/// space and is printed at the change point, but does not advance timing.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/time-signature-engraver.cc — a new TimeSignature grob is
/// created when the measureLength/timeSignatureFraction changes.
/// </remarks>
public sealed record TimeSignatureChangeItem : MusicItem
{
    /// <summary>The new time signature after the change.</summary>
    public TimeSignature NewTime { get; }

    private readonly int _sourcePosition;

    /// <summary>Always <c>Fraction.Zero</c> — the time-signature change occupies horizontal space but no time.</summary>
    public override Fraction Duration => Fraction.Zero;
    /// <summary>The time-signature change's source position in the syntax tree.</summary>
    public override int SourcePosition => _sourcePosition;

    /// <summary>Initializes a new <see cref="TimeSignatureChangeItem"/>.</summary>
    public TimeSignatureChangeItem(TimeSignature newTime, int sourcePosition)
    {
        NewTime = newTime;
        _sourcePosition = sourcePosition;
    }
}
