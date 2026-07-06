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
    // Tuplet ratio numerator (e.g., 3 for triplets).
    int Numerator,
    // Tuplet ratio denominator (e.g., 2 for triplets).
    int Denominator,
    // Starting note index within the measure.
    int StartNoteIndex,
    // Ending note index within the measure.
    int EndNoteIndex,
    // Measure index containing this tuplet.
    int MeasureIndex,
    // Source position for click-to-source mapping.
    int SourcePosition,
    // Nesting depth for nested tuplets (0 = top-level, 1 = first nesting, etc.).
    // LILYPOND-REF: lily/tuplet-bracket.cc:400-500 nested bracket stacking
    int NestingDepth = 0,
    // Global staff index this tuplet belongs to (multi-staff routing;
    // see DynamicItem.StaffIndex). 0 for single-staff.
    int StaffIndex = 0,
    // Index of the voice (within its staff) that owns this tuplet.
    // 0 = primary. Auto-beaming breaks at a tuplet boundary only within the
    // SAME voice, so the beam detector must not apply one voice's tuplet
    // indices to another voice's note stream.
    int VoiceIndex = 0
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
