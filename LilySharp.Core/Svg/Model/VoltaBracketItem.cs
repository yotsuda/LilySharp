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
