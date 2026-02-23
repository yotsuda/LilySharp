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
/// LILYPOND-REF: scm/define-grobs.scm:3020-3060 LyricText grob
/// </remarks>
public sealed record LyricLayout(
    /// <summary>The original lyric item.</summary>
    LyricItem Item,

    /// <summary>X position (center of syllable, in staff spaces).</summary>
    double X,

    /// <summary>Y position (baseline of text, in staff spaces).</summary>
    double Y,

    /// <summary>Width of the syllable text (in staff spaces).</summary>
    double Width,

    /// <summary>Whether to draw a hyphen after this syllable.</summary>
    bool DrawHyphen = false,

    /// <summary>X position of hyphen if drawn (in staff spaces).</summary>
    double HyphenX = 0,

    /// <summary>Whether to draw an extender line after this syllable.</summary>
    bool DrawExtender = false,

    /// <summary>End X position of extender if drawn (in staff spaces).</summary>
    double ExtenderEndX = 0
);
