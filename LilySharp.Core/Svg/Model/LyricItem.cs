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
    // The text content of this syllable.
    string Text,

    // Index of the measure containing this syllable.
    int MeasureIndex,

    // Index of the note this syllable is attached to.
    int ItemIndex,

    // Type of connector after this syllable.
    LyricConnectorType ConnectorType = LyricConnectorType.None,

    // Voice ID this lyric belongs to (for multi-voice lyrics). 0 = the
    // primary voice; greater than 0 = a named bound voice, whose X is resolved
    // from Timing against the shared column grid.
    int VoiceId = 0,

    // Verse number (1-based) for multiple lyric lines.
    int VerseNumber = 1,

    // Musical moment of this syllable's note within its measure. Used
    // to place a bound (non-primary) voice's syllable over its real note column
    // (which the primary voice's item index would miss when rhythms differ).
    LilySharp.Core.Semantics.Fraction Timing = default,

    // Byte offset of this syllable's word in the source. Emitted as the
    // SVG data-pos so the preview can highlight the syllable from the editor and
    // jump back to it on click, like notes and other grobs.
    int SourcePosition = 0,

    // Global staff index for an independent lyrics ROW (a lyrics name
    // score row), so the engraver places the syllable at that row's Y. 0 for
    // ordinary lyrics sitting below the first staff.
    int StaffIndex = 0,

    // True when this syllable belongs to an independent lyrics ROW. The
    // engraver then places it WITHIN its row's band (by StaffIndex)
    // and takes its X from Timing, not from a note column.
    bool IsLyricsRow = false,

    // True to suppress this verse's stanza-number prefix ("N."). Set by a
    // `~`-marked per-occurrence verse (`[~2. …]`): the words apply to the pass but
    // the number label is hidden. Does not affect the multi-verse count, so other
    // verses still get their numbers.
    bool HideStanza = false
);
