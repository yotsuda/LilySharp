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
    // Semitone of each diatonic step comes from RelativeOctave.StepSemitoneOf (the
    // single source); only the circle-of-fifths table is local to transposition.
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
    /// <param name="toOctave">
    /// Target's octave offset from c (from <c>'</c>/<c>,</c> marks). 0 keeps the
    /// interval within one octave (up); +1 makes it a ninth, −1 transposes DOWN.
    /// </param>
    public static (int step, int alteration, int octave) Transpose(
        int step, int alteration, int octave, int toStep, int toAlteration, int toOctave = 0)
    {
        // Interval from c (step 0, octave 0) up/down to the target.
        int deltaStep = toStep + 7 * toOctave;
        int deltaSemitones = RelativeOctave.StepSemitoneOf(toStep) + toAlteration + 12 * toOctave;

        int targetSemitones = octave * 12 + RelativeOctave.StepSemitoneOf(step) + alteration + deltaSemitones;

        int rawStep = step + deltaStep;             // may be negative (downward)
        int newOctave = octave + FloorDiv(rawStep, 7);
        int newStep = Mod(rawStep, 7);

        int naturalSemitones = newOctave * 12 + RelativeOctave.StepSemitoneOf(newStep);
        int newAlteration = targetSemitones - naturalSemitones;

        return (newStep, newAlteration, newOctave);
    }

    // Floored division / modulo so a downward interval (negative rawStep) carries
    // the octave the same way an upward one does.
    private static int FloorDiv(int a, int b) => (int)Math.Floor((double)a / b);
    private static int Mod(int a, int b) => ((a % b) + b) % b;

    /// <summary>
    /// The chromatic size, in semitones, of the interval from c to the target.
    /// A sounding pitch (MIDI) only needs this shift — no respelling.
    /// </summary>
    public static int IntervalSemitones(int toStep, int toAlteration, int toOctave = 0)
        => RelativeOctave.StepSemitoneOf(toStep) + toAlteration + 12 * toOctave;

    /// <summary>
    /// How a key signature's sharp count changes when the music is transposed
    /// up by the c→(toStep, toAlteration) interval — each fifth adds one sharp.
    /// C major (0) with <c>transpose: d</c> → D major (+2 sharps).
    /// </summary>
    public static int KeySignatureFifthsShift(int toStep, int toAlteration)
        => StepFifths[toStep] + 7 * toAlteration;

    /// <summary>
    /// The c-relative transpose target (for <see cref="Transpose"/>) that moves
    /// music from a home key's tonic to an ambient key's tonic by the NEAREST
    /// octave — up when the shift is a tritone or less, otherwise down. Returns
    /// null when there is nothing to do (the tonics are identical). Both tonics
    /// are given as a diatonic step (0..6) and alteration in semitones.
    /// </summary>
    /// <remarks>
    /// The single definition of "how a movable phrase lands," shared by the
    /// renderer's collector and the MIDI / MusicXML exporters.
    /// </remarks>
    public static (int step, int alt, int oct)? MovableInterval(
        int homeStep, int homeAlteration, int ambientStep, int ambientAlteration)
    {
        int dStep = Mod(ambientStep - homeStep, 7);
        int dSemi = Mod(
            (RelativeOctave.StepSemitoneOf(ambientStep) + ambientAlteration)
            - (RelativeOctave.StepSemitoneOf(homeStep) + homeAlteration), 12);
        if (dStep == 0 && dSemi == 0)
            return null;

        int toOctave = dSemi <= 6 ? 0 : -1;                      // nearest octave
        int toAlteration = dSemi - RelativeOctave.StepSemitoneOf(dStep);
        return (dStep, toAlteration, toOctave);
    }

    /// <summary>
    /// Composes two c→target transpose intervals: apply <paramref name="outer"/>
    /// after <paramref name="inner"/> (the written pitch moves by inner first,
    /// then outer). Null on either side is the identity.
    /// </summary>
    public static (int step, int alt, int oct)? Compose(
        (int step, int alt, int oct)? inner, (int step, int alt, int oct)? outer)
    {
        if (inner is not { } i) return outer;
        if (outer is not { } o) return inner;
        return Transpose(i.step, i.alt, i.oct, o.step, o.alt, o.oct);
    }
}
