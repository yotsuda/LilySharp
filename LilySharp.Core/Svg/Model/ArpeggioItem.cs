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
/// Represents an arpeggio marking on a chord.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/arpeggio.cc, scm/define-grobs.scm:201-224
/// An arpeggio is a wavy line to the left of a chord indicating
/// the notes should be played in sequence rather than simultaneously.
/// </remarks>
public readonly record struct ArpeggioItem(
    int MeasureIndex,
    int ItemIndex,
    int MinStaffPosition,
    int MaxStaffPosition,
    int SourcePosition,
    // Global staff index this arpeggio belongs to (multi-staff routing;
    // see DynamicItem.StaffIndex). 0 for single-staff.
    int StaffIndex = 0,
    // Non-arpeggiate: a straight BRACKET instead of the wavy line — the
    // chord is NOT to be rolled. LILYPOND-REF: \arpeggioBracket.
    bool Bracket = false,
    // The voice of that staff the chord was written in: ItemIndex counts THAT voice's items
    // (see DynamicItem.VoiceIndex), so the engraver resolves the column through it.
    int VoiceIndex = 0,
    // Written @arpeggio(cross) (owner's decision 2026-11-12): ONE line from this chord down
    // through what begins at the same moment on the staff below it in its grand staff. What
    // the walk writes; whether there IS something below is only known once every staff is
    // collected (PartnerStaffIndex, set by Collector.CrossArpeggios.Connect).
    bool Cross = false,
    // The global index of the staff the line runs down to, or -1: not connected (no cross
    // written, or nothing to connect to — the arpeggio is then its own chord's).
    // LILYPOND-REF: lily/span-arpeggio-engraver.cc:94-120 process_acknowledged — a span
    //   Arpeggio is made when more than one arpeggio stands at a moment of one PianoStaff.
    int PartnerStaffIndex = -1,
    // The lowest head the line reaches on that staff, in ITS staff positions (its clef, about
    // its middle line) — the bottom of the head interval the span covers.
    // LILYPOND-REF: lily/arpeggio.cc:83-115 calc_positions — the stems' head positions of
    //   every staff, each about its own staff symbol, united in the common frame.
    int PartnerLowestHead = 0);

/// <summary>
/// The part of an arpeggio a column holds without drawing it: the invisible Arpeggio (or
/// ChordBracket) of its own that every staff's chord keeps under a cross-staff one, so the
/// column's spacing makes the room the one drawn line stands in.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/span-arpeggio-engraver.cc:150-157 stop_translation_timestep — "we can't
///   kill the children, since we don't want the previous note to bump into the span
///   arpeggio; so we make it transparent".
/// </remarks>
public enum SpanArpeggioPart : byte
{
    /// <summary>No cross-staff arpeggio runs through this column.</summary>
    None,
    /// <summary>The wavy line's.</summary>
    Wiggle,
    /// <summary>The bracket's (<c>@arpeggio(cross bracket)</c>).</summary>
    Bracket,
}
