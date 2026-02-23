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
/// LILYPOND-REF: lily/stem.cc:698-717 Stem::calc_default_direction()
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
    public static bool GetStemUp(int staffPosition, int? voiceNumber = null)
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
                _ => staffPosition < MiddleLine
            };
        }

        // Automatic: notes below middle line have stems up
        return staffPosition < MiddleLine;
    }

    /// <summary>
    /// Determines stem direction for a chord based on average position.
    /// </summary>
    public static bool GetStemUp(IReadOnlyList<int> staffPositions, int? voiceNumber = null)
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
                _ => CalculateChordStemDirection(staffPositions)
            };
        }

        return CalculateChordStemDirection(staffPositions);
    }

    /// <summary>
    /// Calculates stem direction for a chord based on note positions.
    /// Uses the "majority rule" - direction that minimizes stem length.
    /// </summary>
    private static bool CalculateChordStemDirection(IReadOnlyList<int> staffPositions)
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

        // Tie: prefer stems down (traditional convention)
        return false;
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