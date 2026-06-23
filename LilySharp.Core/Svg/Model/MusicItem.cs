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
}

/// <summary>
/// A single note.
/// </summary>
public sealed record NoteItem : MusicItem
{
    public int StaffPosition { get; }
    public Fraction BaseDuration { get; }
    public int Dots { get; }
    public string? Accidental { get; }
    public bool NeedsLedgerLines { get; }
    /// <summary>Number of tremolo beams (0 = no tremolo, 1-3 = tremolo).</summary>
    public int TremoloBeams { get; }
    /// <summary>Whether this note starts a tie to the next note.</summary>
    public bool HasTieStart { get; }
    /// <summary>Whether this note starts a slur.</summary>
    public bool HasSlurStart { get; }
    /// <summary>Whether this note ends a slur.</summary>
    public bool HasSlurEnd { get; }
    /// <summary>Whether this note starts a manual beam group.</summary>
    public bool HasBeamStart { get; }
    /// <summary>Whether this note ends a manual beam group.</summary>
    public bool HasBeamEnd { get; }
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

    public override Fraction Duration =>
        (Dots > 0 ? BaseDuration.Dotted(Dots) : BaseDuration) * TimeScale;
    public override int SourcePosition => _sourcePosition;

    /// <summary>
    /// Beam-resolved stem direction. A beam forces ONE direction onto all its
    /// members; the collector bakes that in here so spacing (skyline rods,
    /// stem-direction corrections) sees the same stems the renderer draws.
    /// LilyPond resolves directions in the engravers BEFORE spacing runs.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/beam.cc — Beam::calc_direction.</remarks>
    public bool? StemUpOverride { get; init; }

    /// <summary>Stem direction: beam-resolved if beamed, else by staff position.</summary>
    public bool StemUp => StemUpOverride ?? StaffPosition < 0;

    /// <summary>Whether this note has a tremolo marking.</summary>
    public bool HasTremolo => TremoloBeams > 0;

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
    public Fraction BaseDuration { get; }
    public int Dots { get; }
    private readonly int _sourcePosition;

    /// <summary>Tuplet time scale; see <see cref="NoteItem.TimeScale"/>.</summary>
    public Fraction TimeScale { get; init; } = new Fraction(1, 1);

    public override Fraction Duration =>
        (Dots > 0 ? BaseDuration.Dotted(Dots) : BaseDuration) * TimeScale;
    public override int SourcePosition => _sourcePosition;

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
    /// <summary>Whether this accidental is a courtesy (cautionary) accidental shown in parentheses.</summary>
    bool IsCourtesy = false,
    /// <summary>
    /// Optional per-pitch finger number attached via <c>@finger.N</c> inside a chord.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/fingering-engraver.cc — FingeringColumn stacking.</remarks>
    int? Fingering = null
);

/// <summary>
/// A chord (multiple notes played simultaneously).
/// </summary>
public sealed record ChordItem : MusicItem
{
    public ImmutableArray<ChordNoteInfo> Notes { get; }
    public Fraction BaseDuration { get; }
    public int Dots { get; }
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

    public override Fraction Duration =>
        (Dots > 0 ? BaseDuration.Dotted(Dots) : BaseDuration) * TimeScale;
    public override int SourcePosition => _sourcePosition;

    /// <summary>Beam-resolved stem direction; see <see cref="NoteItem.StemUpOverride"/>.</summary>
    public bool? StemUpOverride { get; init; }

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

    public ChordItem(ImmutableArray<ChordNoteInfo> notes, Fraction baseDuration, int dots, int sourcePosition, int tremoloBeams = 0, bool hasBeamStart = false, bool hasBeamEnd = false, bool hasArpeggio = false, bool isCue = false, bool hasTieStart = false)
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

    public override Fraction Duration => Fraction.Zero;
    public override int SourcePosition => _sourcePosition;

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

    public override Fraction Duration => Fraction.Zero;
    public override int SourcePosition => _sourcePosition;

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

    public override Fraction Duration => Fraction.Zero;
    public override int SourcePosition => _sourcePosition;

    public TimeSignatureChangeItem(TimeSignature newTime, int sourcePosition)
    {
        NewTime = newTime;
        _sourcePosition = sourcePosition;
    }
}
