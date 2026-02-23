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
/// Type of piano pedal.
/// </summary>
/// <remarks>
/// LILYPOND-REF: piano-pedal-engraver.cc - Pedal_type_info
/// </remarks>
public enum PedalType
{
    /// <summary>Sustain pedal (damper pedal)</summary>
    Sustain,
    /// <summary>Sostenuto pedal (center pedal)</summary>
    Sostenuto,
    /// <summary>Una corda pedal (soft pedal)</summary>
    UnaCorda,
}

/// <summary>
/// Represents a pedal bracket span (from pedal-on to pedal-off).
/// </summary>
/// <remarks>
/// LILYPOND-REF: piano-pedal-engraver.cc:256-400 Pedal event processing
/// LILYPOND-REF: define-grobs.scm:2586-2605 PianoPedalBracket grob
/// </remarks>
public readonly record struct PedalBracketItem(
    PedalType Type,
    int StartMeasureIndex,
    int EndMeasureIndex,
    int SourcePosition
);
