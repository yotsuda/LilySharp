using System.Collections.Immutable;

namespace LilySharp.Core.Svg.Model;

/// <summary>
/// Represents a volta bracket (first/second ending bracket).
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/volta-bracket.cc:1-200 Volta_bracket_interface
/// LILYPOND-REF: scm/define-grobs.scm:4850-4900 VoltaBracket grob
///
/// Volta brackets show which measures to play on each repeat:
/// - [1. ] = first ending (play on first time through)
/// - [2. ] = second ending (play on second time through)
/// - [1, 3. ] = play on first and third time
/// - [1-3. ] = play on first through third time
/// </remarks>
public sealed record VoltaBracketItem(
    /// <summary>Starting measure index (inclusive).</summary>
    int StartMeasureIndex,
    
    /// <summary>Ending measure index (inclusive).</summary>
    int EndMeasureIndex,
    
    /// <summary>Volta number text (e.g., "1.", "2.", "1, 3.", "1-3.").</summary>
    string VoltaText,
    
    /// <summary>Whether the bracket is closed at the end (has right hook).</summary>
    bool IsClosed,
    
    /// <summary>Source position for click-to-source mapping.</summary>
    int SourcePosition
);
