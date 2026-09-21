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

using System;
using System.Collections.Generic;
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
        => ShouldHideStaff(staff, staffIndex: -1, score: null, startMeasure, endMeasure, isFirstSystem);

    /// <summary>
    /// The per-system suicide filter every <c>LayoutStaffGroups</c> overload hands down:
    /// <see cref="ShouldHideStaff(Staff, int, MultiStaffScore?, int, int, bool)"/> bound to
    /// this score and system, with each staff's global index resolved on the ask.
    /// </summary>
    public static Func<Staff, bool> DeadFilter(
        MultiStaffScore score, int startMeasure, int endMeasure, bool isFirstSystem)
    {
        // The index is looked up by walking the staves on each ask, not out of a map built
        // per system: the walk is a struct and allocates nothing, a score has 1.77 staves on
        // the reader's corpus, and the map was 6,012 B a keystroke at 27.83 filters (session
        // 464's census). The FIRST staff by reference wins, as the map's TryAdd kept it.
        // ⚠️ A SCORE WITH NO `RemoveEmpty` STAFF GETS THE SHARED FILTER: its closure — the
        // environment and the delegate, 104 B — was built per system for an answer that is
        // false for every staff it can be asked about (ShouldHideStaff's first test), which
        // was 2,911 B a keystroke over the tab corpus (session 469, A/B of this file alone).
        bool anyRemovable = false;
        foreach (var (_, s, _) in score.EnumerateStaves())
            if (s.RemoveEmpty) { anyRemovable = true; break; }
        if (!anyRemovable)
            return NothingDies;
        return LiveFilter(score, startMeasure, endMeasure, isFirstSystem);
    }

    /// <summary>The filter for a score none of whose staves may commit hara-kiri.</summary>
    private static readonly Func<Staff, bool> NothingDies = static _ => false;

    /// <summary><see cref="DeadFilter"/>'s closure, in a method of its own so its environment
    /// is built only when it is returned.</summary>
    private static Func<Staff, bool> LiveFilter(
        MultiStaffScore score, int startMeasure, int endMeasure, bool isFirstSystem)
    {
        return staff => ShouldHideStaff(
            staff, GlobalIndexOf(score, staff), score,
            startMeasure, endMeasure, isFirstSystem);
    }

    private static int GlobalIndexOf(MultiStaffScore score, Staff staff)
    {
        foreach (var (_, s, globalIndex) in score.EnumerateStaves())
            if (ReferenceEquals(s, staff))
                return globalIndex;
        return -1;
    }

    /// <summary>
    /// <see cref="ShouldHideStaff(Staff, int, int, bool)"/> with the score's side tables in
    /// reach, so the grobs LilyPond's <c>keepAliveInterfaces</c> names beyond note heads count.
    /// </summary>
    public static bool ShouldHideStaff(Staff staff, int staffIndex, MultiStaffScore? score,
        int startMeasure, int endMeasure, bool isFirstSystem)
    {
        if (!staff.RemoveEmpty)
            return false;

        // LILYPOND-REF: lily/hara-kiri-group-spanner.cc:request_suicide_alone()
        // If remove-first is false and we're in the first system, don't hide
        if (isFirstSystem && !staff.RemoveFirst)
            return false;

        return IsStaffEmpty(staff, staffIndex, score, startMeasure, endMeasure);
    }

    /// <summary>
    /// Checks whether a staff has any musical content (keepAliveInterfaces grobs) in a measure range.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: ly/engraver-init.ly:987-1001 keepAliveInterfaces — note-head-interface,
    ///   tab-note-head-interface, dynamic-interface, lyric-syllable-interface,
    ///   lyric-interface, chord-name-interface, bass-figure-interface,
    ///   cluster-beacon-interface, fret-diagram-interface, percent-repeat-interface,
    ///   stanza-number-interface.
    /// In Lily#'s model those grobs live in two places: note heads are the staff's own
    /// <see cref="NoteItem"/> / <see cref="ChordItem"/> (a tab staff's numbers are the same
    /// items), and dynamics, chord names, bass figures and percent repeats are the score's
    /// side tables, each entry carrying the global staff index it hangs on. Until session
    /// 395 only the heads counted, so a rest-only bar carrying a dynamic, a chord symbol or
    /// a figure was hidden where LilyPond keeps the staff.
    /// ⚠️ Lyrics are NOT consulted: <see cref="LyricItem.StaffIndex"/> is the global index of
    /// an independent lyrics ROW and 0 for lyrics under a staff whichever staff that is, so
    /// it cannot say which staff a syllable keeps alive. A staff with syllables has the notes
    /// they are sung to, which keep it alive anyway; the row case (a lyrics-only row is not a
    /// removeEmpty staff) does not arise. Clusters, fret diagrams and stanza numbers have no
    /// separate grob here.
    /// </remarks>
    public static bool IsStaffEmpty(Staff staff, int staffIndex, MultiStaffScore? score,
        int startMeasure, int endMeasure)
    {
        foreach (var voice in staff.Voices)
        {
            for (int m = startMeasure; m < endMeasure && m < voice.Measures.Length; m++)
            {
                foreach (var item in voice.Measures[m].Items)
                {
                    // note-head-interface / tab-note-head-interface: NoteItem and ChordItem
                    if (item is NoteItem or ChordItem)
                        return false;
                }
            }
        }

        if (score == null || staffIndex < 0)
            return true;

        // dynamic-interface
        foreach (var d in score.Dynamics)
            if (d.StaffIndex == staffIndex && d.MeasureIndex >= startMeasure && d.MeasureIndex < endMeasure)
                return false;
        // chord-name-interface
        foreach (var c in score.ChordNames)
            if (c.StaffIndex == staffIndex && c.MeasureIndex >= startMeasure && c.MeasureIndex < endMeasure)
                return false;
        // bass-figure-interface
        foreach (var f in score.FiguredBasses)
            if (f.StaffIndex == staffIndex && f.MeasureIndex >= startMeasure && f.MeasureIndex < endMeasure)
                return false;
        // percent-repeat-interface
        foreach (var p in score.PercentRepeats)
            if (p.StaffIndex == staffIndex && p.MeasureIndex >= startMeasure && p.MeasureIndex < endMeasure)
                return false;

        return true;
    }

    /// <summary>The note-head-only reading, kept for callers without a score in reach.</summary>
    public static bool IsStaffEmpty(Staff staff, int startMeasure, int endMeasure)
        => IsStaffEmpty(staff, staffIndex: -1, score: null, startMeasure, endMeasure);
}
