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
    // Crescendo or decrescendo.
    HairpinDirection Direction,
    // Measure index of the start note.
    int StartMeasureIndex,
    // Item index within the start measure.
    int StartItemIndex,
    // Measure index of the end note.
    int EndMeasureIndex,
    // Item index within the end measure.
    int EndItemIndex,
    // Source position for click-to-source mapping.
    int SourcePosition,
    // F3/B: index of the originating cresc/decresc mark in score.MusicMarks,
    // so a reused layout re-derives data-pos from the live score. -1 = unresolved.
    int SourceIndex = -1,
    // The staff this hairpin belongs to (0 = first/only staff).
    int StaffIndex = 0
);
