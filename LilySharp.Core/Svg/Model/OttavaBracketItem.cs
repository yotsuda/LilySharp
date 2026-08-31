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
/// Type of ottava transposition.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/ottava-engraver.cc middleCOffset handling
/// </remarks>
public enum OttavaType
{
    /// <summary>8va - up one octave.</summary>
    Ottava8va,
    /// <summary>8vb - down one octave.</summary>
    Ottava8vb,
    /// <summary>15ma - up two octaves.</summary>
    Quindicesima15ma,
    /// <summary>15mb - down two octaves.</summary>
    Quindicesima15mb
}

/// <summary>
/// Represents an ottava bracket spanning multiple measures.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/ottava-bracket.cc OttavaBracket grob
/// LILYPOND-REF: lily/ottava-engraver.cc Ottava_spanner_engraver
/// LILYPOND-REF: scm/define-grobs.scm:2445-2468 OttavaBracket grob defaults
/// </remarks>
public sealed record OttavaBracketItem(
    // The type of ottava transposition.
    OttavaType Type,
    // Measure index of the start.
    int StartMeasureIndex,
    // Measure index of the end (where loco or next ottava appears).
    int EndMeasureIndex,
    // Source position for click-to-source mapping.
    int SourcePosition,
    // F3/B: index of the originating ottava mark in score.MusicMarks,
    // so a reused layout re-derives data-pos from the live score. -1 = unresolved.
    int SourceIndex = -1,
    // The staff this ottava was authored on (0 = the first/only staff).
    // The bracket is stacked over/under THAT staff on a grand staff.
    int StaffIndex = 0,
    // Index, within the start measure, of the NOTE the ottava mark was written on —
    // the spanner's LEFT BOUND. -1 when the mark did not resolve to a note, and then
    // the measure's own origin stands in for it.
    // LILYPOND-REF: lily/ottava-bracket.cc:121-176 Ottava_bracket::print — span_points[LEFT]
    //   is the BOUND note column's note-heads' X extent, not the measure's. Measured:
    //   ledger ottava.x.label-to-notehead read -2.800000000 against LilyPond's -0.800000000
    //   while this was the measure's origin, and -2.0 is exactly the clef-and-time-signature
    //   gap the first column sits past it.
    int StartItemIndex = -1
)
{
    // Identity, not value equality: see ModelIdentity.
    public bool Equals(OttavaBracketItem? other) => ReferenceEquals(this, other);

    /// <inheritdoc/>
    public override int GetHashCode() => ModelIdentity.HashOf(this);
}
