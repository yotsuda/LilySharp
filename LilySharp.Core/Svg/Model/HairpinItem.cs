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
/// Direction of a hairpin (crescendo or decrescendo).
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/hairpin.cc:140-142 grow_dir
/// </remarks>
public enum HairpinDirection
{
    /// <summary>Crescendo: opening wedge (grows louder).</summary>
    Crescendo,
    /// <summary>Decrescendo: closing wedge (grows softer).</summary>
    Decrescendo
}

/// <summary>
/// Represents a hairpin (crescendo/decrescendo wedge) spanning multiple notes.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/hairpin.cc:110-358
/// LILYPOND-REF: scm/define-grobs.scm:1641-1666 Hairpin grob
/// </remarks>
public sealed record HairpinItem(
    /// <summary>Crescendo or decrescendo.</summary>
    HairpinDirection Direction,
    /// <summary>Measure index of the start note.</summary>
    int StartMeasureIndex,
    /// <summary>Item index within the start measure.</summary>
    int StartItemIndex,
    /// <summary>Measure index of the end note.</summary>
    int EndMeasureIndex,
    /// <summary>Item index within the end measure.</summary>
    int EndItemIndex,
    /// <summary>Source position for click-to-source mapping.</summary>
    int SourcePosition,
    /// <summary>F3/B: index of the originating cresc/decresc mark in score.MusicMarks,
    /// so a reused layout re-derives data-pos from the live score. -1 = unresolved.</summary>
    int SourceIndex = -1
);
