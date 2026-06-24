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
/// Diatonic-interval transposition — LilyPond's <c>\transpose</c> with the FROM
/// pitch fixed at <c>c</c>. Every pitch is shifted by the interval from c to the
/// part's <c>transpose:</c> target and RE-SPELLED so its chromatic distance is
/// preserved: <c>c d e f</c> with <c>transpose: d</c> becomes <c>d e fis g</c>
/// (a name-based shift, never a blind semitone offset).
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/pitch.cc Pitch::transpose — add the interval's diatonic
/// step (carrying the octave past b) then set the alteration to land on the
/// target semitone. The single-token target gives an UP interval within one
/// octave (c→c … c→b); a downward / wider interval needs an octave mark on the
/// target, which the part-option parser does not yet carry.
/// </remarks>
public static class PitchTransposer
{
    // Semitones of each natural diatonic step above its octave's c.
    // Index = step (c=0 … b=6), matching RelativeOctave.StepIndex.
    private static readonly int[] StepSemitone = { 0, 2, 4, 5, 7, 9, 11 };

    // Circle-of-fifths distance of each natural step from C (F=−1 … B=+5).
    private static readonly int[] StepFifths = { 0, 2, 4, -1, 1, 3, 5 };

    /// <summary>
    /// Parses a transpose target — a pitch letter optionally followed by a
    /// known accidental suffix (<c>is</c>/<c>isis</c>/<c>es</c>/<c>eses</c>) —
    /// into its diatonic step (0–6) and alteration in semitones (−2…+2).
    /// Returns false for anything else.
    /// </summary>
    public static bool TryParseTarget(string? text, out int step, out int alteration)
    {
        step = 0;
        alteration = 0;
        if (string.IsNullOrEmpty(text))
            return false;

        char letter = char.ToLowerInvariant(text[0]);
        if (letter is < 'a' or > 'g')
            return false;
        step = RelativeOctave.StepIndex(letter);

        switch (text[1..])
        {
            case "": alteration = 0; break;
            case "is": alteration = 1; break;
            case "isis": alteration = 2; break;
            case "es": alteration = -1; break;
            case "eses": alteration = -2; break;
            default: return false;
        }
        return true;
    }

    /// <summary>
    /// Transposes one pitch by the interval from c to (toStep, toAlteration),
    /// preserving its chromatic distance. The octave is absolute (apply AFTER
    /// relative-octave resolution); a b→c roll-over carries the octave.
    /// </summary>
    /// <param name="step">Diatonic step of the pitch (c=0 … b=6).</param>
    /// <param name="alteration">Its accidental in semitones (−2…+2).</param>
    /// <param name="octave">Its absolute octave.</param>
    /// <param name="toStep">Target's diatonic step (the interval's top, from c).</param>
    /// <param name="toAlteration">Target's accidental in semitones.</param>
    public static (int step, int alteration, int octave) Transpose(
        int step, int alteration, int octave, int toStep, int toAlteration)
    {
        int deltaSemitones = StepSemitone[toStep] + toAlteration; // c → target

        int targetSemitones = octave * 12 + StepSemitone[step] + alteration + deltaSemitones;

        int rawStep = step + toStep;          // 0..12 (toStep is the step delta from c)
        int newOctave = octave + rawStep / 7; // carry once past b
        int newStep = rawStep % 7;

        int naturalSemitones = newOctave * 12 + StepSemitone[newStep];
        int newAlteration = targetSemitones - naturalSemitones;

        return (newStep, newAlteration, newOctave);
    }

    /// <summary>
    /// How a key signature's sharp count changes when the music is transposed
    /// up by the c→(toStep, toAlteration) interval — each fifth adds one sharp.
    /// C major (0) with <c>transpose: d</c> → D major (+2 sharps).
    /// </summary>
    public static int KeySignatureFifthsShift(int toStep, int toAlteration)
        => StepFifths[toStep] + 7 * toAlteration;
}
