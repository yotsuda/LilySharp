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
/// Resolves scale-degree chord members (<c>&lt;d 3 5 7,&gt;</c>) into concrete
/// written pitches. A degree stacks on the chord's root by DIATONIC scale steps
/// in the current key: degree N sits N−1 steps above the root's letter, taking
/// that letter's accidental from the key signature; a glued <c>is</c>/<c>es</c>
/// then shifts it chromatically, and <c>'</c>/<c>,</c> marks move its octave.
/// The spelling is letter-preserving (never respelled to an enharmonic), exactly
/// like a written pitch. Shared by the renderer's collector and the exporters.
/// </summary>
public static class ChordDegrees
{
    /// <summary>
    /// Resolves one degree against a root into a written pitch (before any part /
    /// phrase transpose, which the caller applies uniformly to every chord note).
    /// </summary>
    /// <param name="rootStep">Root's diatonic step (0=C..6=B).</param>
    /// <param name="rootOctave">Root's absolute octave.</param>
    /// <param name="degree">Degree number (1=root, 3=third, 8=octave, 9=ninth, …).</param>
    /// <param name="degreeAlteration">Glued accidental in semitones (is=+1, es=−1, …).</param>
    /// <param name="octaveMarks">Net <c>'</c>/<c>,</c> shift on this degree.</param>
    /// <param name="keySharps">The current key signature's sharp count (−7..+7).</param>
    /// <returns>The written (step 0..6, alteration in semitones, absolute octave).</returns>
    public static (int step, int alteration, int octave) Resolve(
        int rootStep, int rootOctave, int degree, int degreeAlteration, int octaveMarks, int keySharps)
    {
        // Degree N counts up (N−1) diatonic steps from the root's letter; the octave
        // carries past B→C automatically, so 8/9/11/13 need no special case.
        int absStep = rootStep + (degree - 1);
        int step = Mod(absStep, 7);
        int octave = rootOctave + FloorDiv(absStep, 7) + octaveMarks;
        int alteration = KeySpelling.Alteration(step, keySharps) + degreeAlteration;
        return (step, alteration, octave);
    }

    private static int Mod(int a, int b) => ((a % b) + b) % b;
    private static int FloorDiv(int a, int b) => (int)System.Math.Floor((double)a / b);
}
