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
