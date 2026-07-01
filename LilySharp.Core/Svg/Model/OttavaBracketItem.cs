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
/// Type of ottava transposition.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/ottava-engraver.cc middleCOffset handling
/// </remarks>
public enum OttavaType
{
    /// <summary>8va - up one octave.</summary>
    Ottava8va,
    /// <summary>8vb - down one octave.</summary>
    Ottava8vb,
    /// <summary>15ma - up two octaves.</summary>
    Quindicesima15ma,
    /// <summary>15mb - down two octaves.</summary>
    Quindicesima15mb
}

/// <summary>
/// Represents an ottava bracket spanning multiple measures.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/ottava-bracket.cc OttavaBracket grob
/// LILYPOND-REF: lily/ottava-engraver.cc Ottava_spanner_engraver
/// LILYPOND-REF: scm/define-grobs.scm:2445-2468 OttavaBracket grob defaults
/// </remarks>
public sealed record OttavaBracketItem(
    /// <summary>The type of ottava transposition.</summary>
    OttavaType Type,
    /// <summary>Measure index of the start.</summary>
    int StartMeasureIndex,
    /// <summary>Measure index of the end (where loco or next ottava appears).</summary>
    int EndMeasureIndex,
    /// <summary>Source position for click-to-source mapping.</summary>
    int SourcePosition,
    /// <summary>F3/B: index of the originating ottava mark in score.MusicMarks,
    /// so a reused layout re-derives data-pos from the live score. -1 = unresolved.</summary>
    int SourceIndex = -1,
    /// <summary>The staff this ottava was authored on (0 = the first/only staff).
    /// The bracket is stacked over/under THAT staff on a grand staff.</summary>
    int StaffIndex = 0
);
