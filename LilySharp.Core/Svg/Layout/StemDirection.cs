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
/// Calculates stem direction for notes and chords.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/stem.cc:790-809 Stem::calc_default_direction()
/// LILYPOND-REF: lily/stem.cc:767-788 Stem::calc_direction() (voice-based override)
///
/// IMPLEMENTED — neutral direction from context (stem.cc:670-671):
///   When default-direction is CENTER (note on middle line), use neutral-direction property.
///   LilyPond default: DOWN. Configurable via neutralStemUp parameter.
/// IMPLEMENTED (in SvgRenderer) — cross-staff stem direction (stem.cc:672-676):
///   Cross-staff stems get direction from their beam. Handled by _beamedStemUp in SvgRenderer.
/// IMPLEMENTED (in SvgRenderer) — beam-member stem direction override (stem.cc:672-676):
///   Beam members inherit the beam's direction. Handled by _beamedStemUp in SvgRenderer.
/// </remarks>
public static class StemDirection
{
    /// <summary>
    /// Middle line of the staff (B4 in treble clef).
    /// Notes at or above this position have stems down by default.
    /// </summary>
    private const int MiddleLine = 4;

    /// <summary>
    /// Determines stem direction for a single note.
    /// </summary>
    /// <param name="staffPosition">Staff position of the note.</param>
    /// <param name="voiceNumber">Voice number (1-4) for forced direction, or null for auto.</param>
    /// <param name="neutralStemUp">Direction when note is on middle line (tie-break).
    /// LILYPOND-REF: stem.cc:784 neutral-direction property. LP default: DOWN (false).</param>
    public static bool GetStemUp(int staffPosition, int? voiceNumber = null, bool neutralStemUp = false)
    {
        // Voice number overrides automatic direction
        if (voiceNumber.HasValue)
        {
            return voiceNumber.Value switch
            {
                1 => true,   // Voice 1: always stems up
                2 => false,  // Voice 2: always stems down
                3 => true,   // Voice 3: stems up
                4 => false,  // Voice 4: stems down
                _ => staffPosition == MiddleLine ? neutralStemUp : staffPosition < MiddleLine
            };
        }

        // Automatic: notes below middle line have stems up
        // On middle line: use neutral direction
        if (staffPosition == MiddleLine)
            return neutralStemUp;
        return staffPosition < MiddleLine;
    }

    /// <summary>
    /// Determines stem direction for a chord based on average position.
    /// </summary>
    /// <param name="staffPositions">Staff positions of chord notes.</param>
    /// <param name="voiceNumber">Voice number (1-4) for forced direction, or null for auto.</param>
    /// <param name="neutralStemUp">Direction when chord is centered on middle line (tie-break).
    /// LILYPOND-REF: stem.cc:784 neutral-direction property. LP default: DOWN (false).</param>
    public static bool GetStemUp(IReadOnlyList<int> staffPositions, int? voiceNumber = null, bool neutralStemUp = false)
    {
        if (staffPositions.Count == 0)
            return true;

        // Voice number overrides automatic direction
        if (voiceNumber.HasValue)
        {
            return voiceNumber.Value switch
            {
                1 => true,
                2 => false,
                3 => true,
                4 => false,
                _ => CalculateChordStemDirection(staffPositions, neutralStemUp)
            };
        }

        return CalculateChordStemDirection(staffPositions, neutralStemUp);
    }

    /// <summary>
    /// Calculates stem direction for a chord based on note positions.
    /// Uses the extremal note distance rule.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/stem.cc:790-809 Stem::calc_default_direction()
    /// Compares distance of highest and lowest note from middle line.
    /// Tie-break: uses neutral-direction (LP default: DOWN).
    /// </remarks>
    private static bool CalculateChordStemDirection(IReadOnlyList<int> staffPositions, bool neutralStemUp = false)
    {
        if (staffPositions.Count == 0)
            return true;

        // Calculate distance of extreme notes from middle line
        int lowest = staffPositions.Min();
        int highest = staffPositions.Max();

        int distanceFromTop = highest - MiddleLine;
        int distanceFromBottom = MiddleLine - lowest;

        // Stem goes in direction that minimizes total stem length
        // If furthest note is above middle, stem down
        // If furthest note is below middle, stem up
        if (distanceFromTop > distanceFromBottom)
            return false;  // Stem down
        if (distanceFromBottom > distanceFromTop)
            return true;   // Stem up

        // LILYPOND-REF: stem.cc:784 neutral-direction property
        // Tie: use neutral direction (LP default: DOWN)
        return neutralStemUp;
    }

    /// <summary>
    /// Gets stem direction for a note item.
    /// </summary>
    public static bool GetStemUp(NoteItem note, int? voiceNumber = null)
    {
        return GetStemUp(note.StaffPosition, voiceNumber);
    }

    /// <summary>
    /// Gets stem direction for a chord item.
    /// </summary>
    public static bool GetStemUp(ChordItem chord, int? voiceNumber = null)
    {
        var positions = chord.Notes.Select(n => n.StaffPosition).ToList();
        return GetStemUp(positions, voiceNumber);
    }

    /// <summary>
    /// Gets stem direction for any music item.
    /// </summary>
    public static bool? GetStemUp(MusicItem item, int? voiceNumber = null)
    {
        return item switch
        {
            NoteItem note => GetStemUp(note, voiceNumber),
            ChordItem chord => GetStemUp(chord, voiceNumber),
            _ => null  // Rests don't have stems
        };
    }
}