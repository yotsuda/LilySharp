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

using LilySharp.Core.Syntax;

namespace LilySharp.Core.Tablature;

/// <summary>
/// Provides predefined tunings and fret calculation for tablature.
/// MIDI note numbers for standard tunings (index 0 = lowest string).
/// String numbers in tablature: 1 = highest pitch, 6 = lowest pitch (for guitar).
/// </summary>
public static class Tunings
{
    // LILYPOND-REF: ly/string-tunings-init.ly — open-string pitches per tuning:
    // guitar-tuning <e, a, d g b e'>, bass-tuning <e,, a,, d, g,>,
    // bass-five-string-tuning <b,,, e,, a,, d, g,>,
    // bass-six-string-tuning <b,,, e,, a,, d, g, c>, ukulele-tuning <g' c' e' a'>.
    // Guitar: E2=40, A2=45, D3=50, G3=55, B3=59, E4=64 (6弦→1弦)
    /// <summary>Standard 6-string guitar tuning (MIDI notes, index 0 = lowest string).</summary>
    public static readonly int[] Guitar = [40, 45, 50, 55, 59, 64];

    // Bass (4-string): E1=28, A1=33, D2=38, G2=43 (4弦→1弦)
    /// <summary>Standard 4-string bass tuning (MIDI notes, index 0 = lowest string).</summary>
    public static readonly int[] Bass = [28, 33, 38, 43];

    // Bass (5-string): B0=23, E1=28, A1=33, D2=38, G2=43 (5弦→1弦)
    /// <summary>Standard 5-string bass tuning with low B (MIDI notes, index 0 = lowest string).</summary>
    public static readonly int[] Bass5 = [23, 28, 33, 38, 43];

    // Bass (6-string): B0=23, E1=28, A1=33, D2=38, G2=43, C3=48 (6弦→1弦, low B + high C)
    /// <summary>Standard 6-string bass tuning with low B and high C (MIDI notes, index 0 = lowest string).</summary>
    public static readonly int[] Bass6 = [23, 28, 33, 38, 43, 48];

    // Ukulele: G4=67, C4=60, E4=64, A4=69 (4弦→1弦, re-entrant tuning)
    /// <summary>Standard re-entrant ukulele tuning (MIDI notes, index 0 = lowest string).</summary>
    public static readonly int[] Ukulele = [67, 60, 64, 69];

    /// <summary>Returns the tuning array (index 0 = lowest string) for the given tuning type.</summary>
    public static int[] GetTuning(TuningType type) => type switch
    {
        TuningType.Guitar => Guitar,
        TuningType.Bass => Bass,
        TuningType.Bass5 => Bass5,
        TuningType.Bass6 => Bass6,
        TuningType.Ukulele => Ukulele,
        _ => Guitar
    };

    /// <summary>Returns the number of strings for the given tuning type.</summary>
    public static int GetStringCount(TuningType type) => type switch
    {
        TuningType.Guitar => 6,
        TuningType.Bass => 4,
        TuningType.Bass5 => 5,
        TuningType.Bass6 => 6,
        TuningType.Ukulele => 4,
        _ => 6
    };

    /// <summary>
    /// True for bass tunings, which sound an octave BELOW where they are written
    /// in bass clef (the bass guitar is a transposing instrument). Tab frets are
    /// therefore computed from the written pitch shifted down one octave.
    /// </summary>
    public static bool IsBass(TuningType type) =>
        type is TuningType.Bass or TuningType.Bass5 or TuningType.Bass6;

    /// <summary>The DEFAULT sounding transposition (semitones) a tuning implies when a
    /// part gives no explicit <c>transposition</c> and no instrument preset: −12 for
    /// bass tunings (they sound 8vb from bass-clef notation), 0 otherwise. This is the
    /// fallback the resolved <see cref="Svg.Model.Staff.Transposition"/> uses so a bare
    /// <c>tuning bass</c> still frets the sounding pitch.</summary>
    public static int TuningTransposition(TuningType type) => IsBass(type) ? -12 : 0;

    /// <summary>The octave a CLEF already carries: a <c>treble_8</c> part (standard
    /// guitar/tenor notation) sounds an octave below what is written, <c>treble^8</c>
    /// an octave above, <c>bass_8</c> an octave below. LilyPond pitches are SOUNDING
    /// pitches; Lily# writes display pitches, so the tab and MIDI recover the sounding
    /// octave from this plus the part's <c>transposition</c>.</summary>
    public static int ClefOctaveShift(Svg.Model.ClefType clef) => clef switch
    {
        Svg.Model.ClefType.Treble8Below => -12,
        Svg.Model.ClefType.Bass8Below => -12,
        Svg.Model.ClefType.Treble8Above => 12,
        _ => 0,
    };

    /// <summary>The total written→sounding shift for a tab staff: the clef octave plus
    /// the part's resolved <c>transposition</c> (which already folds in the bass/preset
    /// default). Both the fret calculation and MIDI playback read this one value, so a
    /// note frets, sounds, and prints consistently.</summary>
    public static int SoundingShift(Svg.Model.ClefType clef, int transposition) =>
        ClefOctaveShift(clef) + transposition;

    /// <summary>
    /// String/fret allocation for a CHORD, mimicking LilyPond: notes with an
    /// explicit string number claim it first; the rest, highest pitch first,
    /// take the HIGHEST free string whose fret is playable (≥ 0) and within
    /// the maximum stretch (4) of the frets already chosen. Results are in
    /// input-note order.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/translation-functions.scm determine-frets-and-strings;
    /// scm/translation-functions.scm:864 maximumFretStretch default 4.
    /// </remarks>
    public static (int stringNum, int fret)[] CalculateChordFrets(
        System.Collections.Generic.IReadOnlyList<(int Midi, int? StringNumber)> notes,
        int[] tuning)
    {
        const int maxStretch = 4;
        int stringCount = tuning.Length;
        var result = new (int stringNum, int fret)[notes.Count];
        var freeStrings = new System.Collections.Generic.List<int>();
        for (int s = 1; s <= stringCount; s++)
            freeStrings.Add(s);
        var chosenFrets = new System.Collections.Generic.List<int>();

        int FretOn(int midi, int stringNum) => midi - tuning[stringCount - stringNum];
        bool CloseEnough(int fret)
        {
            foreach (int f in chosenFrets)
                if (f != 0 && fret != 0 && System.Math.Abs(fret - f) > maxStretch)
                    return false;
            return true;
        }

        // Assigned strings first.
        for (int i = 0; i < notes.Count; i++)
        {
            result[i] = (0, -1);
            if (notes[i].StringNumber is int s && s >= 1 && s <= stringCount)
            {
                int fret = FretOn(notes[i].Midi, s);
                if (fret >= 0)
                {
                    result[i] = (s, fret);
                    freeStrings.Remove(s);
                    chosenFrets.Add(fret);
                }
            }
        }

        // Unassigned notes, highest pitch first.
        var order = new System.Collections.Generic.List<int>();
        for (int i = 0; i < notes.Count; i++)
            if (result[i].stringNum == 0)
                order.Add(i);
        order.Sort((a, b) => notes[b].Midi.CompareTo(notes[a].Midi));

        foreach (int i in order)
        {
            int chosen = -1, chosenFret = 0;
            foreach (int s in freeStrings) // ascending = highest string first
            {
                int fret = FretOn(notes[i].Midi, s);
                if (fret >= 0 && fret <= 24 && CloseEnough(fret))
                {
                    chosen = s;
                    chosenFret = fret;
                    break;
                }
            }
            if (chosen < 0)
            {
                // LP warns "No string for pitch"; keep the note on the lowest
                // string rather than dropping it.
                chosen = stringCount;
                chosenFret = System.Math.Max(0, FretOn(notes[i].Midi, stringCount));
            }
            result[i] = (chosen, chosenFret);
            freeStrings.Remove(chosen);
            chosenFrets.Add(chosenFret);
        }
        return result;
    }

    /// <summary>
    /// How many frets the left hand covers from where it sits — one finger per fret, so the
    /// hand reaches <c>position</c> through <c>position + HandSpan - 1</c> without moving.
    /// </summary>
    /// <remarks>
    /// LILYSHARP-OWN. Four is the guitarist's one-finger-per-fret span and also the number
    /// LilyPond picks for the neighbouring question of how far apart one CHORD's frets may
    /// sit (<c>maximumFretStretch</c>, default 4, scm/define-context-properties.scm) — so it
    /// is at least a number the same instrument suggested, not one invented here.
    /// </remarks>
    public const int HandSpan = 4;

    /// <summary>
    /// What moving the left hand is worth, measured in FRETS of height: a shift is taken
    /// when it buys more than this many frets of lower position.
    /// </summary>
    /// <remarks>
    /// LILYSHARP-OWN. Without it the hand never comes down on its own — "do not move" beats
    /// every alternative, so a passage that once climbed to the twelfth fret stays there
    /// until an open string happens along. With it the choice is a single number per string,
    /// <c>fret + (the hand must move ? HandShiftCost : 0)</c>, smallest wins, and a tie is
    /// broken toward NOT moving. Five is a hand's width plus one: a shift pays for itself
    /// only if it lands more than a whole hand lower, so a scale sitting at the seventh fret
    /// stays put while a stray twelfth-fret note drops to the second.
    /// <para>
    /// ⚠️ It costs nothing to compute — the same single pass over the strings, one addition
    /// per string — which is the budget this chooser was given: cheap and roughly right,
    /// because no automatic fingering is right anyway.
    /// </para>
    /// </remarks>
    public const int HandShiftCost = HandSpan + 1;

    /// <summary>
    /// Calculates the best string and fret for a given MIDI pitch.
    /// </summary>
    /// <param name="midiPitch">The MIDI note number to place.</param>
    /// <param name="tuning">The tuning array (index 0 = lowest string).</param>
    /// <param name="preferredString">Preferred string (1 = highest, 0 = auto).</param>
    /// <param name="handPosition">The fret the left hand is at, or null when it is not
    /// placed yet. A note the hand can reach WITHOUT MOVING wins; among those, and among
    /// all of them when the hand must move anyway, the lowest fret wins.</param>
    /// <returns>A tuple of (stringNumber, fret) where stringNumber 1 = highest pitch string.</returns>
    /// <remarks>
    /// LILYSHARP-OWN, and deliberately not LilyPond's. LilyPond takes the first string from
    /// the top with a non-negative integer fret (scm/translation-functions.scm:591-796
    /// determine-frets-and-strings), so an open string always wins and the hand is never
    /// considered — playable, but awkward to read. What this wants instead, in the words it
    /// was specified in: track where the left hand IS and pick the fret that moves it least;
    /// if it must move, move it as low as possible; and do not pay much for the answer,
    /// because no automatic chooser gets fingering right anyway.
    /// <para>
    /// So: the hand covers <see cref="HandSpan"/> frets from where it sits, an OPEN string
    /// needs no hand at all, and the tie-break in both branches is simply the lowest fret.
    /// One pass over the strings, no lookahead, no backtracking.
    /// </para>
    /// <para>
    /// ⚠️ This replaced a rule that scored |fret − previous fret| and nothing else, which
    /// kept the hand still by walking one string all the way up: <c>test/tab-indent</c> came
    /// out 3 5 7 8 10 12 on a single string, and <c>test/tab-beam-script</c> took an A at the
    /// fifth fret with the open string right there. Distance alone has no reason to come back
    /// down.
    /// </para>
    /// </remarks>
    public static (int stringNum, int fret) CalculateFret(int midiPitch, int[] tuning,
        int preferredString = 0, int? handPosition = null)
    {
        int stringCount = tuning.Length;

        // Convert 1-based string number to array index
        // String 1 (highest) = index stringCount-1
        // String N (lowest) = index 0
        int ToIndex(int str) => stringCount - str;
        int ToStringNum(int idx) => stringCount - idx;

        // If preferred string is specified, use it
        if (preferredString >= 1 && preferredString <= stringCount)
        {
            int idx = ToIndex(preferredString);
            int openPitch = tuning[idx];
            int fret = midiPitch - openPitch;
            if (fret >= 0 && fret <= 24)
            {
                return (preferredString, fret);
            }
        }

        // Auto: one pass, one number per string — how high the note sits, plus what the
        // shift would cost if the hand cannot reach it from where it is. Smallest wins;
        // a tie goes to NOT moving, so the hand only leaves a position for a clearly lower
        // one. Both halves of the answer fall out of the same comparison: "stay if you can"
        // and "if you must move, move low".
        int bestString = stringCount; // lowest string as fallback
        int bestFret = 99;
        int bestScore = int.MaxValue;
        bool bestReachable = false;

        // Search from highest to lowest string
        for (int idx = stringCount - 1; idx >= 0; idx--)
        {
            int openPitch = tuning[idx];
            int fret = midiPitch - openPitch;
            if (fret < 0 || fret > 24) continue;

            // An open string is reached with no hand at all; a stopped one only from where
            // the hand already sits.
            bool reachable = fret == 0
                || (handPosition.HasValue
                    && fret >= handPosition.Value
                    && fret <= handPosition.Value + HandSpan - 1);
            int score = fret + (reachable ? 0 : HandShiftCost);

            if (score < bestScore
                || (score == bestScore && reachable && !bestReachable)
                || (score == bestScore && reachable == bestReachable && fret < bestFret))
            {
                bestScore = score;
                bestReachable = reachable;
                bestString = ToStringNum(idx);
                bestFret = fret;
            }
        }

        // If no valid position found, return lowest string with calculated fret
        if (bestFret == 99)
        {
            bestFret = midiPitch - tuning[0];
            if (bestFret < 0) bestFret = 0;
        }

        return (bestString, bestFret);
    }
}