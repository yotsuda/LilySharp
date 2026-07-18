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

using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Layout information for a single lyric syllable.
/// All coordinates are in staff spaces.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/lyric-engraver.cc:32-52
/// LILYPOND-REF: scm/define-grobs.scm:2213-2239 LyricText grob
/// </remarks>
public sealed record LyricLayout(
    // The original lyric item.
    LyricItem Item,

    // X position (center of syllable, in staff spaces).
    double X,

    // Y of the text baseline in the LilyPond-native Y-up frame: staff-spaces ABOVE
    // this line's system top, up-positive (frame B). Lyrics sit below the system,
    // so this is negative. The renderer reflects it to device via
    // StaffFrame.ToDevice against the measure's system top.
    double YUp,

    // Width of the syllable text (in staff spaces).
    double Width,

    // F3/B: index of this syllable in the score's Lyrics side-table
    // — a position-independent reference so a reused (cached) layout re-derives its
    // data-pos (LyricItem.SourcePosition) from the live score at render
    // time (SharedRenderer.ResolveDataPos). -1 = unresolved (direct unit-test construction).
    int SourceIndex = -1
);
