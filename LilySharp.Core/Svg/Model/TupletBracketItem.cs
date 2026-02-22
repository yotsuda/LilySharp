using System.Collections.Immutable;
using LilySharp.Core.Semantics;

namespace LilySharp.Core.Svg.Model;

/// <summary>
/// Represents a tuplet bracket with ratio information.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/tuplet-bracket.cc:1-400 Tuplet_bracket class
/// </remarks>
public sealed record TupletBracketItem(
    /// <summary>Tuplet ratio numerator (e.g., 3 for triplets).</summary>
    int Numerator,
    /// <summary>Tuplet ratio denominator (e.g., 2 for triplets).</summary>
    int Denominator,
    /// <summary>Starting note index within the measure.</summary>
    int StartNoteIndex,
    /// <summary>Ending note index within the measure.</summary>
    int EndNoteIndex,
    /// <summary>Measure index containing this tuplet.</summary>
    int MeasureIndex,
    /// <summary>Source position for click-to-source mapping.</summary>
    int SourcePosition,
    /// <summary>
    /// Nesting depth for nested tuplets (0 = top-level, 1 = first nesting, etc.).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tuplet-bracket.cc:400-500 nested bracket stacking
    /// </remarks>
    int NestingDepth = 0
)
{
    /// <summary>
    /// Gets the display text for the tuplet number (e.g., "3" for triplets).
    /// </summary>
    public string DisplayText => Numerator.ToString();
    
    /// <summary>
    /// Gets the tuplet duration factor (notes play faster than written).
    /// For triplets (3/2): each note plays at 2/3 of its written duration.
    /// </summary>
    public Fraction DurationFactor => new Fraction(Denominator, Numerator);
}
