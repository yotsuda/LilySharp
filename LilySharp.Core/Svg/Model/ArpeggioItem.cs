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
