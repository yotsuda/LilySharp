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
    private readonly int _sourcePosition;

    public override Fraction Duration => Dots > 0 ? BaseDuration.Dotted(Dots) : BaseDuration;
    public override int SourcePosition => _sourcePosition;

    /// <summary>Determines stem direction based on staff position.</summary>
    public bool StemUp => StaffPosition < 0;

    /// <summary>Whether this note has a tremolo marking.</summary>
    public bool HasTremolo => TremoloBeams > 0;

    public NoteItem(int staffPosition, Fraction baseDuration, int dots, string? accidental, bool needsLedgerLines, int sourcePosition, int tremoloBeams = 0, bool hasTieStart = false, bool hasSlurStart = false, bool hasSlurEnd = false, bool hasBeamStart = false, bool hasBeamEnd = false, bool hasGlissando = false, int featherDirection = 0, bool isCourtesy = false)
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

    public override Fraction Duration => Dots > 0 ? BaseDuration.Dotted(Dots) : BaseDuration;
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
    bool IsCourtesy = false
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
    private readonly int _sourcePosition;

    public override Fraction Duration => Dots > 0 ? BaseDuration.Dotted(Dots) : BaseDuration;
    public override int SourcePosition => _sourcePosition;

    /// <summary>Determines stem direction based on average staff position.</summary>
    public bool StemUp => Notes.Length > 0 && Notes.Average(n => n.StaffPosition) < 0;

    /// <summary>Whether this chord has a tremolo marking.</summary>
    public bool HasTremolo => TremoloBeams > 0;

    public ChordItem(ImmutableArray<ChordNoteInfo> notes, Fraction baseDuration, int dots, int sourcePosition, int tremoloBeams = 0, bool hasBeamStart = false, bool hasBeamEnd = false, bool hasArpeggio = false)
    {
        Notes = notes;
        BaseDuration = baseDuration;
        Dots = dots;
        TremoloBeams = Math.Clamp(tremoloBeams, 0, 3);
        HasBeamStart = hasBeamStart;
        HasBeamEnd = hasBeamEnd;
        HasArpeggio = hasArpeggio;
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
