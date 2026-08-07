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
/// Style of glissando line.
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/define-grobs.scm:1575 (style . line)
/// </remarks>
public enum GlissandoStyle
{
    /// <summary>Straight line (LilyPond default).</summary>
    Line,
    /// <summary>Zigzag/wavy line.</summary>
    Zigzag
}

/// <summary>
/// Represents a glissando connecting two notes.
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/scheme-engravers.scm - Glissando_engraver
/// A glissando is a sliding line between two notes of different pitch.
/// </remarks>
public readonly record struct GlissandoItem(
    int StartMeasureIndex,
    int StartItemIndex,
    int StartStaffPosition,
    int EndMeasureIndex,
    int EndItemIndex,
    int EndStaffPosition,
    GlissandoStyle Style,
    int SourcePosition,
    // Index of the voice this glissando belongs to (0 = the primary/only
    // voice); the layout resolves its endpoints against THIS voice's measures.
    int VoiceIndex = 0,
    // Which chord MEMBER this line hangs on at each end (−1 = the item is a
    // single note). A chord glissando is one line PER note head, paired in
    // written order — the member picks whose head ink the X anchor reads.
    // LILYPOND-REF: scm/scheme-engravers.scm:2519-2579 Glissando_engraver — one
    //   Glissando per note head, bounds set to the HEAD grobs in array order.
    int StartMemberIndex = -1,
    int EndMemberIndex = -1);
