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

using System.Collections.Immutable;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Collector;

/// <summary>
/// The staff distance LilyPond's knee judges a cross-staff beam by: not the placed distance but
/// the PURE one, which needs no layout. That is why the knee, and so every stem's direction, is
/// known before the spacing reads them (<see cref="MeasureCollector"/>'s move of the notes), and
/// again when the beam is laid (<c>LayoutEngine.LayCrossStaffBeams</c>).
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/align-interface.cc:228-238 internal_get_minimum_translations — dy =
///   skyline distance + padding, and for the whole score max (dy, basic-distance).
/// LILYPOND-REF: lily/align-interface.cc:90-125 get_skylines — pure: a flat box of the rest of
///   the line, the clefs apart.
/// Not literal: the boxes here hold heads and the estimated length of a stem that is not a
/// cross-staff beam's, where LilyPond's hold every inside-staff grob's pure height.
/// MEASURED, LilyPond 2.26.0 (Lab sessions/p1093/k2): `&lt;c' e'&gt;8@cross g'` knees where the
/// placed staves stand too close to knee, so the placed distance cannot answer.
/// </remarks>
internal static class CrossStaffPure
{
    /// <summary>staff-staff-spacing's basic-distance and padding, which the pure distance reads.
    /// LILYPOND-REF: scm/define-grobs.scm VerticalAxisGroup — staff-staff-spacing.</summary>
    private const double BasicDistance = 9.0, Padding = 1.0;

    /// <summary>The length a pure stem is estimated at: the ideal unbeamed stem.</summary>
    private const double StemLength = 3.5;

    /// <summary>The middle-to-middle distance, in staff spaces, between two adjacent staves.</summary>
    public static double Distance(Staff upper, Staff lower) =>
        Math.Max(BasicDistance, ExtentOf(lower).Hi - ExtentOf(upper).Lo + Padding);

    /// <summary>The beams of a staff that have a member on another staff, by their stamp.</summary>
    private static HashSet<int> CrossStaffBeamsOf(Staff staff)
    {
        var ids = new HashSet<int>();
        foreach (var voice in staff.Voices)
            foreach (var measure in voice.Measures)
                foreach (var item in measure.Items)
                    if (item.CrossStaffIndex >= 0
                        && (item is NoteItem n ? n.BeamId : item is ChordItem c ? c.BeamId : null) is { } id)
                        ids.Add(id);
        return ids;
    }

    /// <summary>A staff's pure box, in staff spaces about its middle line: its lines, its own
    /// notes and the notes moved onto it.</summary>
    private static (double Lo, double Hi) ExtentOf(Staff staff)
    {
        double lo = -2, hi = 2;
        void Add(MusicItem item, bool stem)
        {
            if (MusicItem.EdgeStaffPosition(item, preferTop: true) is not { } top
                || MusicItem.EdgeStaffPosition(item, preferTop: false) is not { } bottom)
                return;
            lo = Math.Min(lo, bottom / 2.0 - 0.5);
            hi = Math.Max(hi, top / 2.0 + 0.5);
            // A whole note or breve has no stem to reach with (lily/stem.cc:370-377 Stem::is_normal_stem).
            // MEASURED, LilyPond 2.26.0 (Lab sessions/p1111/c2): a `c1` on each staff gave the
            // estimate two stems, 11.0 apart where LilyPond's pure distance is 10.10, and kneed a
            // beam LilyPond does not.
            if (stem && item is NoteItem { StemUp: var up } && Layout.NoteColumnLayout.Of(item) is { HasStem: true })
            {
                if (up) hi = Math.Max(hi, bottom / 2.0 + StemLength);
                else lo = Math.Min(lo, top / 2.0 - StemLength);
            }
        }
        var crossBeams = CrossStaffBeamsOf(staff);
        foreach (var voice in staff.Voices)
            foreach (var measure in voice.Measures)
                foreach (var item in measure.Items)
                {
                    if (item.CrossStaffIndex >= 0 || item.GraceTime || item is not (NoteItem or ChordItem))
                        continue;
                    int? beam = item is NoteItem n ? n.BeamId : ((ChordItem)item).BeamId;
                    Add(item, beam is not { } id || !crossBeams.Contains(id));
                }
        foreach (var crossed in staff.CrossedIn)
            Add(crossed.Item, false);
        return (lo, hi);
    }
}
