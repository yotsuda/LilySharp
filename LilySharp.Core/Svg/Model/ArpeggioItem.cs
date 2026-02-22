namespace LilySharp.Core.Svg.Model;

/// <summary>
/// Represents an arpeggio marking on a chord.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/arpeggio.cc, scm/define-grobs.scm:201-224
/// An arpeggio is a wavy line to the left of a chord indicating
/// the notes should be played in sequence rather than simultaneously.
/// </remarks>
public readonly record struct ArpeggioItem(
    int MeasureIndex,
    int ItemIndex,
    int MinStaffPosition,
    int MaxStaffPosition,
    int SourcePosition);
