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

namespace LilySharp.Core.Music;

/// <summary>
/// Diatonic (modal) transposition: moves a written pitch by whole SCALE STEPS in a
/// key, the way a phrase reference's interval argument does (<c>Melody'(3)</c> = a
/// third up — c e g becomes e g b in C major, with the third's quality decided by
/// the key). A note IN the scale lands in the scale; a chromatic deviation (an
/// accidental relative to the key) carries over unchanged.
/// </summary>
/// <remarks>
/// LILYPOND-REF: ly/music-functions-init.ly \modalTranspose — scale-degree mapping,
/// as opposed to \transpose's fixed chromatic interval. Shared by the SVG collector,
/// the MIDI exporter and the MusicXML exporter so the three outputs cannot drift.
/// </remarks>
public static class DiatonicShift
{
    /// <summary>
    /// Shifts a written pitch by <paramref name="steps"/> diatonic steps (+2 = up a
    /// third) in the key with <paramref name="keySharps"/> sharps. The step/octave
    /// move together on the diatonic index; the alteration keeps the note's
    /// deviation from the scale.
    /// </summary>
    public static (int Step, int Alter, int Octave) Apply(
        int step, int alter, int octave, int steps, int keySharps)
    {
        if (steps == 0)
            return (step, alter, octave);
        int index = step + octave * 7 + steps;
        int newStep = ((index % 7) + 7) % 7;
        int newOctave = (index - newStep) / 7;
        int newAlter = KeySpelling.Alteration(newStep, keySharps)
                       + (alter - KeySpelling.Alteration(step, keySharps));
        return (newStep, newAlter, newOctave);
    }
}
