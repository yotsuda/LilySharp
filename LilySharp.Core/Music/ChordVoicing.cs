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
using System.Linq;

namespace LilySharp.Core.Music;

/// <summary>
/// How a chord SYMBOL sounds — the pitches a <c>chords { }</c> row plays in the MIDI and
/// lists in the editor's hover. A symbol names a chord but voices none, so Lily# picks one
/// voicing, the same one everywhere it is asked.
/// </summary>
/// <remarks>
/// <para>
/// LILYSHARP-OWN (owner decision 2026-09-25, HANDOFF §1.1 第625): the WINDOW voicing. Every
/// tone of the chord takes the one pitch it has in the octave from G3 (MIDI 55) up to, not
/// including, G4; a slash bass takes its pitch in the octave below that (G2 up to G3). A
/// chord without a slash gets no bass of its own — the books that write chord rows are
/// mostly bass tabs, whose bass part already plays the roots. The register stays put from
/// chord to chord, and the pitches depend on the SYMBOL alone — no previous chord, no
/// context — so the hover can say exactly what the MIDI plays.
/// </para>
/// <para>
/// A quality the registry does not know (a raw suffix, <c>Cx</c>) has no interval set: it
/// plays its root alone (and its slash bass). Tones that land on the same key sound once.
/// </para>
/// </remarks>
public static class ChordVoicing
{
    /// <summary>The window's lowest key, G3; the window is this key and the eleven above it.</summary>
    public const int WindowLow = 55;

    /// <summary>One sounding tone: its spelling (diatonic step 0=C..6=B and alteration) and
    /// its MIDI key.</summary>
    public readonly record struct VoicedTone(int Step, int Alter, int Midi)
    {
        /// <summary>The octave number of the spelled tone (C4 = middle C): B♯3 is key 60.</summary>
        public int Octave => (Midi - Semantics.RelativeOctave.StepSemitoneOf(Step) - Alter) / 12 - 1;
    }

    /// <summary>The window voicing of <paramref name="chord"/>, lowest first: the slash bass
    /// (if any), then the chord's tones inside the window.</summary>
    public static ImmutableArray<VoicedTone> Window(ChordStructure chord)
    {
        var voiced = ImmutableArray.CreateBuilder<VoicedTone>();
        if (chord.BassStep is { } bassStep)
            voiced.Add(Place(bassStep, chord.BassAlter ?? 0, WindowLow - 12));

        var upper = new System.Collections.Generic.List<VoicedTone>();
        if (chord.RawSuffix != null)
            upper.Add(Place(chord.RootStep, chord.RootAlter, WindowLow));
        else
            foreach (var tone in chord.Tones)
            {
                var placed = Place(tone.Step, tone.Alter, WindowLow);
                if (!upper.Any(u => u.Midi == placed.Midi))
                    upper.Add(placed);
            }
        voiced.AddRange(upper.OrderBy(u => u.Midi));
        return voiced.ToImmutable();
    }

    private static VoicedTone Place(int step, int alter, int windowLow)
    {
        int pitchClass = Semantics.RelativeOctave.StepSemitoneOf(step) + alter;
        int offset = ((pitchClass - windowLow) % 12 + 12) % 12;
        return new VoicedTone(step, alter, windowLow + offset);
    }
}
