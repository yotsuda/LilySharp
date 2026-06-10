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

        // Closest-octave rule + explicit '/, offset — shared with the collector
        // and the exporters (RelativeOctave is the single source of truth).
        int actualOctave = RelativeOctave.Resolve(
            _lastStep, _currentOctave, step, pitch.OctaveOffset);

        // Update state for next pitch
        _lastStep = step;
        _currentOctave = actualOctave;

        return new Pitch(step, actualOctave, pitch.AccidentalOffset);
    }

    /// <summary>
    /// Resolves a pitch without updating state.
    /// </summary>
    public Pitch ResolveWithoutUpdate(PitchSyntax pitch)
    {
        var step = GetPitchStep(pitch.PitchName[0]);
        int actualOctave = RelativeOctave.Resolve(
            _lastStep, _currentOctave, step, pitch.OctaveOffset);

        return new Pitch(step, actualOctave, pitch.AccidentalOffset);
    }

    /// <summary>
    /// Resolves multiple pitches (for chords). Within the chord each pitch is
    /// relative to the previous chord note; the following note/chord is relative
    /// to the chord's FIRST pitch, matching LilyPond and MeasureCollector
    /// (cf. the chord-octave divergence fixed in 96a38ad).
    /// </summary>
    public IReadOnlyList<Pitch> ResolveChord(IEnumerable<PitchSyntax> pitches)
    {
        var result = new List<Pitch>();
        int firstStep = _lastStep, firstOctave = _currentOctave;

        foreach (var pitch in pitches)
        {
            result.Add(Resolve(pitch));
            if (result.Count == 1)
            {
                firstStep = _lastStep;
                firstOctave = _currentOctave;
            }
        }

        // Next note is reckoned from the chord's first pitch (LilyPond spec).
        _lastStep = firstStep;
        _currentOctave = firstOctave;
        return result;
    }

    /// <summary>
    /// Gets the current state as a Pitch (for debugging/testing).
    /// </summary>
    public Pitch CurrentPitch => new(_lastStep, _currentOctave, 0);

    private static int GetPitchStep(char pitch) => RelativeOctave.StepIndex(pitch);
}
