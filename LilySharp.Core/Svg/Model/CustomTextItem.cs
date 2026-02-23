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