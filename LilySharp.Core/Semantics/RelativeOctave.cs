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

namespace LilySharp.Core.Semantics;

/// <summary>
/// The single source of truth for LilyPond's relative-octave rule: the new
/// pitch takes the octave that puts it closest to the previous pitch (an
/// interval of a fourth or less), then the explicit <c>'</c>/<c>,</c> offset
/// is applied on top.
/// </summary>
/// <remarks>
/// Every consumer that walks pitches in relative mode (MeasureCollector,
/// MidiExporter, MusicXmlExporter, RelativePitchResolver) MUST resolve
/// octaves through this method. The three walkers keep their own running
/// state and octave conventions; only the algorithm is shared, so it cannot
/// drift between render, MIDI, and MusicXML again (cf. the chord-octave
/// divergence fixed in 96a38ad).
/// LILYPOND-REF: lily/pitch.cc Pitch::to_relative_octave — closest-octave rule.
/// </remarks>
public static class RelativeOctave
{
    /// <summary>Diatonic step index: c=0, d=1, … b=6.</summary>
    public static int StepIndex(char baseName)
    {
        int step = char.ToLowerInvariant(baseName) switch
        {
            'c' => 0, 'd' => 1, 'e' => 2, 'f' => 3, 'g' => 4, 'a' => 5, 'b' => 6,
            _ => -1
        };
        // Parser validation guarantees a diatonic base name; a miss means a
        // malformed pitch slipped through — surface it in debug, fall back to c.
        System.Diagnostics.Debug.Assert(step >= 0, $"RelativeOctave.StepIndex: non-diatonic base name '{baseName}'");
        return step < 0 ? 0 : step;
    }

    /// <summary>Chromatic semitone offset of a diatonic step within its octave
    /// (c=0, d=2, e=4, f=5, g=7, a=9, b=11).</summary>
    private static readonly int[] StepSemitone = { 0, 2, 4, 5, 7, 9, 11 };

    /// <summary>The semitone (0–11) of a diatonic step (0–6).</summary>
    public static int StepSemitoneOf(int step) => StepSemitone[step];

    /// <summary>
    /// Absolute MIDI number for a diatonic step (0–6), its alteration in semitones,
    /// and its octave (scientific, where octave 4 = the octave of middle C). The
    /// single source for the <c>(octave+1)*12 + semitone + alter</c> formula that was
    /// inlined in the collector, the MIDI exporter, the renderer and the transposer.
    /// </summary>
    public static int StepToMidi(int step, int alter, int octave) =>
        (octave + 1) * 12 + StepSemitone[step] + alter;

    /// <summary>
    /// Resolves the octave of the new pitch relative to the previous pitch.
    /// Octave numbers are caller-convention agnostic (pure step arithmetic),
    /// so the collector's display octaves and the exporters' MIDI octaves can
    /// both use this.
    /// </summary>
    /// <param name="prevStep">Diatonic step of the previous pitch (0–6).</param>
    /// <param name="prevOctave">Octave of the previous pitch.</param>
    /// <param name="step">Diatonic step of the new pitch (0–6).</param>
    /// <param name="octaveOffset">Explicit ' (+1 each) / , (−1 each) marks.</param>
    /// <returns>The resolved octave of the new pitch, offset applied.</returns>
    public static int Resolve(int prevStep, int prevOctave, int step, int octaveOffset)
    {
        // Up/down octave candidates; pick the one closest in diatonic steps.
        // When the step repeats (prevStep == step) both candidates collapse to
        // prevOctave — a genuine tie the `<` resolves to downOctave (== prevOctave),
        // so a repeated note stays in its octave. Otherwise the candidates differ.
        int upOctave = prevOctave;
        if (prevStep > step) upOctave++;
        int downOctave = prevOctave;
        if (prevStep < step) downOctave--;

        int prevSteps = prevStep + prevOctave * 7;
        int upSteps = step + upOctave * 7;
        int downSteps = step + downOctave * 7;

        int octave = Math.Abs(upSteps - prevSteps) < Math.Abs(downSteps - prevSteps)
            ? upOctave
            : downOctave;

        return octave + octaveOffset;
    }
}
