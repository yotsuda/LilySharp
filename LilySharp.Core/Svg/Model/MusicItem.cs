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
    private readonly int _sourcePosition;
    
    public override Fraction Duration => Dots > 0 ? BaseDuration.Dotted(Dots) : BaseDuration;
    public override int SourcePosition => _sourcePosition;
    
    /// <summary>Determines stem direction based on staff position.</summary>
    public bool StemUp => StaffPosition < 4;
    
    public NoteItem(int staffPosition, Fraction baseDuration, int dots, string? accidental, bool needsLedgerLines, int sourcePosition)
    {
        StaffPosition = staffPosition;
        BaseDuration = baseDuration;
        Dots = dots;
        Accidental = accidental;
        NeedsLedgerLines = needsLedgerLines;
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
    bool NeedsLedgerLines
);

/// <summary>
/// A chord (multiple notes played simultaneously).
/// </summary>
public sealed record ChordItem : MusicItem
{
    public ImmutableArray<ChordNoteInfo> Notes { get; }
    public Fraction BaseDuration { get; }
    public int Dots { get; }
    private readonly int _sourcePosition;
    
    public override Fraction Duration => Dots > 0 ? BaseDuration.Dotted(Dots) : BaseDuration;
    public override int SourcePosition => _sourcePosition;
    
    /// <summary>Determines stem direction based on average staff position.</summary>
    public bool StemUp => Notes.Length > 0 && Notes.Average(n => n.StaffPosition) < 4;
    
    public ChordItem(ImmutableArray<ChordNoteInfo> notes, Fraction baseDuration, int dots, int sourcePosition)
    {
        Notes = notes;
        BaseDuration = baseDuration;
        Dots = dots;
        _sourcePosition = sourcePosition;
    }
}