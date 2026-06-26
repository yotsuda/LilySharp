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
/// Type of connector between lyric syllables.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/lyric-hyphen.cc:1-150
/// </remarks>
public enum LyricConnectorType
{
    /// <summary>No connector.</summary>
    None,

    /// <summary>Hyphen between syllables of the same word.</summary>
    Hyphen,

    /// <summary>Extender line for melisma (single syllable over multiple notes).</summary>
    Extender
}

/// <summary>
/// Represents a single lyric syllable in a score.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/lyric-engraver.cc:32-52
/// LILYPOND-REF: scm/define-grobs.scm:3020-3060 LyricText grob
///
/// A syllable is a unit of lyric text that corresponds to one or more notes.
/// Multiple syllables form words, connected by hyphens.
/// </remarks>
public sealed record LyricItem(
    /// <summary>The text content of this syllable.</summary>
    string Text,

    /// <summary>Index of the measure containing this syllable.</summary>
    int MeasureIndex,

    /// <summary>Index of the note this syllable is attached to.</summary>
    int ItemIndex,

    /// <summary>Type of connector after this syllable.</summary>
    LyricConnectorType ConnectorType = LyricConnectorType.None,

    /// <summary>Voice ID this lyric belongs to (for multi-voice lyrics). 0 = the
    /// primary voice; greater than 0 = a named bound voice, whose X is resolved
    /// from <see cref="Timing"/> against the shared column grid.</summary>
    int VoiceId = 0,

    /// <summary>Verse number (1-based) for multiple lyric lines.</summary>
    int VerseNumber = 1,

    /// <summary>Musical moment of this syllable's note within its measure. Used
    /// to place a bound (non-primary) voice's syllable over its real note column
    /// (which the primary voice's item index would miss when rhythms differ).</summary>
    LilySharp.Core.Semantics.Fraction Timing = default
);
