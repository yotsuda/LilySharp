namespace LilySharp.Core.Svg.Model;

/// <summary>
/// Represents custom text in the score (e.g., "molto rit.", "a tempo").
/// </summary>
/// <remarks>
/// LILYPOND-REF: text-interface.cc Text rendering
/// LILYPOND-REF: define-grobs.scm:3900-3950 TextScript grob
///
/// Custom text annotations are placed at the end of measures/sections,
/// typically below the staff for expression indications.
/// </remarks>
public sealed record CustomTextItem(
    /// <summary>The text content.</summary>
    string Text,

    /// <summary>The measure index where this text appears.</summary>
    int MeasureIndex,

    /// <summary>Source position for click-to-source mapping.</summary>
    int SourcePosition
);