using System.Collections.Immutable;
using LilySharp.Core.Semantics;

namespace LilySharp.Core.Svg.Model;

/// <summary>
/// Type of grace note.
/// </summary>
public enum GraceNoteType
{
    /// <summary>Regular grace note (no slash).</summary>
    Grace,
    /// <summary>Acciaccatura (slashed grace note, very short).</summary>
    Acciaccatura,
    /// <summary>Appoggiatura (unslashed grace note, takes time from main note).</summary>
    Appoggiatura
}

/// <summary>
/// Information about a single note within a grace note group.
/// </summary>
public readonly record struct GraceNoteInfo(
    int StaffPosition,      // Staff position (-6 = middle C in treble clef)
    string? Accidental,     // "sharp", "flat", "natural", "doubleSharp", "doubleFlat", or null
    bool NeedsLedger        // Whether ledger lines are needed
);

/// <summary>
/// A group of grace notes attached to a main note.
/// </summary>
/// <remarks>
/// LILYPOND-REF: grace-engraver.cc:36-80 Grace_engraver class
/// LILYPOND-REF: define-grobs.scm:1358-1402 GraceSpacing grob definition
/// 
/// Grace notes are rendered smaller (typically 65% of normal size) and
/// placed before their main note. Acciaccaturas have a diagonal slash
/// through the stem.
/// </remarks>
public sealed record GraceNoteItem
{
    /// <summary>The type of grace note.</summary>
    public GraceNoteType Type { get; }
    
    /// <summary>The notes in this grace group.</summary>
    public ImmutableArray<GraceNoteInfo> Notes { get; }
    
    /// <summary>The measure index where this grace note appears.</summary>
    public int MeasureIndex { get; }
    
    /// <summary>The item index of the main note this grace is attached to.</summary>
    public int MainNoteItemIndex { get; }
    
    /// <summary>Source position for click-to-source mapping.</summary>
    public int SourcePosition { get; }
    
    public GraceNoteItem(
        GraceNoteType type,
        ImmutableArray<GraceNoteInfo> notes,
        int measureIndex,
        int mainNoteItemIndex,
        int sourcePosition)
    {
        Type = type;
        Notes = notes;
        MeasureIndex = measureIndex;
        MainNoteItemIndex = mainNoteItemIndex;
        SourcePosition = sourcePosition;
    }
    
    /// <summary>
    /// Scale factor for grace notes relative to normal notes.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: define-grobs.scm:1389 font-size = -3
    /// Font size -3 corresponds to approximately 0.65 scaling.
    /// </remarks>
    public const double ScaleFactor = 0.65;
}