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
    public string? Accidental { get; }
    /// <summary>Whether the note needs ledger lines because it sits outside the staff.</summary>
    public bool NeedsLedgerLines { get; }
    /// <summary>Number of tremolo beams (0 = no tremolo, 1-3 = tremolo).</summary>
    public int TremoloBeams { get; }
    /// <summary>Two-note (chord) tremolo: the BETWEEN-stems beam count. The
    /// pair is written at the tremolo's total duration and sounds half of it
    /// each (TimeScale ½). 0 = not part of a pair.
    /// LILYPOND-REF: lily/chord-tremolo-engraver.cc.</summary>
    public int TremoloPairBeams { get; init; }
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
    /// <summary>Whether this note has a glissando to the next note.</summary>
    public bool HasGlissando { get; }
    /// <summary>Feathered beam direction: 0=none, 1=right (accel), -1=left (rit).</summary>
    /// <remarks>LILYPOND-REF: beam.cc:1039-1082 grow-direction</remarks>
    public int FeatherDirection { get; }
    /// <summary>Whether this accidental is a courtesy (cautionary) accidental shown in parentheses.</summary>
    /// <remarks>LILYPOND-REF: lily/accidental.cc:147-148 parenthesized property</remarks>
    public bool IsCourtesy { get; }
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
    public string? EditorialAccidental { get; }

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
    /// True when this note has a repeat-tie — a half-tie pointing in from the LEFT,
    /// typically used after a repeat barline to indicate continuation from the
    /// previous volta.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/repeat-tie-engraver.cc — RepeatTie grob
    /// LilyPond syntax: <c>c4\repeatTie</c>; LilySharp surface: <c>c4@repeatTie</c>.
    /// </remarks>
    public bool HasRepeatTie { get; }
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
    /// <remarks>LILYPOND-REF: lily/beam.cc — Beam::calc_direction.</remarks>
    public bool? StemUpOverride { get; init; }

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

    /// <summary>Stem direction: beam-resolved if beamed, else by staff position.</summary>
    public bool StemUp => StemUpOverride ?? StaffPosition < 0;

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
    int SourcePosition = -1
);

/// <summary>
/// A chord (multiple notes played simultaneously).
/// </summary>
public sealed record ChordItem : MusicItem
{
    /// <summary>The notes making up this chord.</summary>
    public ImmutableArray<ChordNoteInfo> Notes { get; init; }
    /// <summary>The written chord value before dots and tuplet scaling, as a fraction of a whole note.</summary>
    public Fraction BaseDuration { get; }
    /// <summary>The number of augmentation dots on the chord.</summary>
    public int Dots { get; }
    /// <summary>Two-note tremolo between-beams count (see NoteItem).</summary>
    public int TremoloPairBeams { get; init; }
    /// <summary>Notehead style applied to ALL heads of this chord.</summary>
    public NoteheadStyle Notehead { get; init; }
    /// <summary>Number of tremolo beams (0 = no tremolo, 1-3 = tremolo).</summary>
    public int TremoloBeams { get; }
    /// <summary>Whether this chord starts a manual beam group.</summary>
    public bool HasBeamStart { get; }
    /// <summary>Whether this chord ends a manual beam group.</summary>
    public bool HasBeamEnd { get; }
    /// <summary>Whether this chord has an arpeggio marking.</summary>
    public bool HasArpeggio { get; }
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

    /// <summary>Leading grace notes hanging left of this chord's column; see
    /// <see cref="NoteItem.LeadingGrace"/>.</summary>
    public ImmutableArray<GraceNoteInfo> LeadingGrace { get; init; } = ImmutableArray<GraceNoteInfo>.Empty;

    /// <summary>Stem direction: beam-resolved if beamed, else by average staff position.</summary>
    public bool StemUp => StemUpOverride ?? (Notes.Length > 0 && Notes.Average(n => n.StaffPosition) < 0);

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

    /// <summary>Initializes a new <see cref="ClefChangeItem"/>.</summary>
    public ClefChangeItem(ClefType newClef, int sourcePosition)
    {
        NewClef = newClef;
        _sourcePosition = sourcePosition;
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
