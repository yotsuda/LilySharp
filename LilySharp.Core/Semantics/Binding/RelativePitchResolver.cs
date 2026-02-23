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

using LilySharp.Core.Semantics.BoundTree;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics.Binding;

/// <summary>
/// Resolves relative pitches to absolute pitches.
/// </summary>
/// <remarks>
/// In LilyPond-style relative mode, each pitch is interpreted relative to the previous pitch.
/// The closest octave is chosen (within a fourth), with explicit octave markers overriding.
/// </remarks>
public sealed class RelativePitchResolver
{
    private int _currentOctave = 4;
    private int _lastStep = 0; // C=0, D=1, ..., B=6

    /// <summary>
    /// Initializes the resolver with a base pitch.
    /// </summary>
    /// <param name="basePitch">The starting pitch for relative mode.</param>
    public void Initialize(PitchSyntax basePitch)
    {
        _lastStep = GetPitchStep(basePitch.PitchName[0]);
        _currentOctave = 4 + basePitch.OctaveOffset;
    }

    /// <summary>
    /// Initializes the resolver with a specific pitch.
    /// </summary>
    public void Initialize(Pitch pitch)
    {
        _lastStep = pitch.Step;
        _currentOctave = pitch.Octave;
    }

    /// <summary>
    /// Resets to default state (middle C).
    /// </summary>
    public void Reset()
    {
        Reset(4);
    }

    /// <summary>
    /// Resets to a specific base octave.
    /// </summary>
    /// <param name="baseOctave">The octave to reset to.</param>
    public void Reset(int baseOctave)
    {
        _currentOctave = baseOctave;
        _lastStep = 0;
    }

    /// <summary>
    /// Resolves a pitch syntax to an absolute pitch.
    /// </summary>
    /// <param name="pitch">The pitch syntax to resolve.</param>
    /// <returns>The resolved absolute pitch.</returns>
    public Pitch Resolve(PitchSyntax pitch)
    {
        var step = GetPitchStep(pitch.PitchName[0]);

        // Calculate base octave from interval (without OctaveOffset)
        // If interval > 3 (more than a fourth up), go down an octave
        // If interval < -3 (more than a fourth down), go up an octave
        int interval = step - _lastStep;
        int baseOctave = _currentOctave;

        if (interval > 3)
            baseOctave--;
        else if (interval < -3)
            baseOctave++;

        // Apply explicit octave markers
        int actualOctave = baseOctave + pitch.OctaveOffset;

        // Update state for next pitch
        _lastStep = step;
        _currentOctave = actualOctave;

        return new Pitch(step, actualOctave, pitch.AccidentalOffset);
    }

    /// <summary>
    /// Resolves a pitch without updating state (for chord notes after the first).
    /// </summary>
    public Pitch ResolveWithoutUpdate(PitchSyntax pitch)
    {
        var step = GetPitchStep(pitch.PitchName[0]);

        int interval = step - _lastStep;
        int baseOctave = _currentOctave;

        if (interval > 3)
            baseOctave--;
        else if (interval < -3)
            baseOctave++;

        int actualOctave = baseOctave + pitch.OctaveOffset;

        return new Pitch(step, actualOctave, pitch.AccidentalOffset);
    }

    /// <summary>
    /// Resolves multiple pitches (for chords), using the first pitch for state update.
    /// </summary>
    public IReadOnlyList<Pitch> ResolveChord(IEnumerable<PitchSyntax> pitches)
    {
        var result = new List<Pitch>();
        bool first = true;

        foreach (var pitch in pitches)
        {
            if (first)
            {
                result.Add(Resolve(pitch));
                first = false;
            }
            else
            {
                result.Add(ResolveWithoutUpdate(pitch));
            }
        }

        return result;
    }

    /// <summary>
    /// Gets the current state as a Pitch (for debugging/testing).
    /// </summary>
    public Pitch CurrentPitch => new(_lastStep, _currentOctave, 0);

    private static int GetPitchStep(char pitch) => char.ToLower(pitch) switch
    {
        'c' => 0, 'd' => 1, 'e' => 2, 'f' => 3, 'g' => 4, 'a' => 5, 'b' => 6,
        _ => 0
    };
}
