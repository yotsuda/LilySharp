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
/// Hara-kiri: automatic hiding of empty staves per system.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/hara-kiri-group-spanner.cc — core suicide decision logic
/// LILYPOND-REF: lily/axis-group-engraver.cc — registers interesting grobs via keepAliveInterfaces
/// LILYPOND-REF: ly/context-mods-init.ly — RemoveEmptyStaves / RemoveAllEmptyStaves definitions
///
/// A staff is considered "alive" if it contains at least one grob implementing a keepAliveInterfaces
/// interface within the system's measure range. In LilySharp terms, this means at least one
/// NoteItem or ChordItem in any voice of the staff. Rests, clef changes, and key changes alone
/// do not keep a staff alive.
///
/// keepAliveInterfaces (from ly/engraver-init.ly):
///   note-head-interface, tab-note-head-interface, dynamic-interface,
///   lyric-syllable-interface, lyric-interface, chord-name-interface,
///   bass-figure-interface, cluster-beacon-interface, fret-diagram-interface,
///   percent-repeat-interface, stanza-number-interface
/// </remarks>
internal static class HaraKiri
{
    /// <summary>
    /// Determines whether a staff should be hidden for a given measure range.
    /// </summary>
    /// <param name="staff">The staff to check.</param>
    /// <param name="startMeasure">Start measure index (inclusive).</param>
    /// <param name="endMeasure">End measure index (exclusive).</param>
    /// <param name="isFirstSystem">Whether this is the first system of the score.</param>
    /// <returns>True if the staff should be hidden (has no musical content).</returns>
    /// <remarks>
    /// LILYPOND-REF: lily/hara-kiri-group-spanner.cc:request_suicide_alone()
    /// Checks items-worth-living (grobs matching keepAliveInterfaces) in column range.
    /// If remove-first is false and this is the first system, never hides.
    /// </remarks>
    public static bool ShouldHideStaff(Staff staff, int startMeasure, int endMeasure, bool isFirstSystem)
    {
        if (!staff.RemoveEmpty)
            return false;

        // LILYPOND-REF: lily/hara-kiri-group-spanner.cc:request_suicide_alone()
        // If remove-first is false and we're in the first system, don't hide
        if (isFirstSystem && !staff.RemoveFirst)
            return false;

        return IsStaffEmpty(staff, startMeasure, endMeasure);
    }

    /// <summary>
    /// Checks whether a staff has any musical content (keepAliveInterfaces grobs) in a measure range.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: ly/engraver-init.ly — keepAliveInterfaces list
    /// In LilySharp, the equivalent of keepAliveInterfaces is:
    ///   NoteItem → note-head-interface
    ///   ChordItem → note-head-interface (contains multiple noteheads)
    /// Future extensions could include dynamics, lyrics, etc. attached to the staff.
    /// </remarks>
    public static bool IsStaffEmpty(Staff staff, int startMeasure, int endMeasure)
    {
        foreach (var voice in staff.Voices)
        {
            for (int m = startMeasure; m < endMeasure && m < voice.Measures.Length; m++)
            {
                foreach (var item in voice.Measures[m].Items)
                {
                    // note-head-interface: NoteItem and ChordItem keep the staff alive
                    if (item is NoteItem or ChordItem)
                        return false;
                }
            }
        }

        return true;
    }
}
