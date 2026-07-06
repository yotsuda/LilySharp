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

    /// <summary>The octave shift (semitones) to apply to written pitches before
    /// computing tab frets: −12 for bass tunings (8vb), 0 otherwise.</summary>
    public static int OctaveShift(TuningType type) => IsBass(type) ? -12 : 0;

    /// <summary>
    /// Clef-aware octave shift. LilyPond pitches are SOUNDING pitches; Lily#
    /// writes display pitches, so the tab must recover the sounding octave
    /// from the notation convention: a treble_8 part (standard guitar
    /// notation) sounds an octave below what is written, and bass tunings
    /// sound 8vb from their bass-clef notation. Without this, an open-string
    /// arpeggio landed on frets 7–12 of the top string.
    /// </summary>
    public static int OctaveShift(TuningType type, Svg.Model.ClefType clef) =>
        clef == Svg.Model.ClefType.Treble8Below ? -12
        : IsBass(type) ? -12
        : 0;

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
    /// Calculates the best string and fret for a given MIDI pitch.
    /// </summary>
    /// <param name="midiPitch">The MIDI note number to place.</param>
    /// <param name="tuning">The tuning array (index 0 = lowest string).</param>
    /// <param name="preferredString">Preferred string (1 = highest, 0 = auto).</param>
    /// <param name="nearFret">When set (and no preferred string applies), pick the
    /// string whose fret is CLOSEST to this value — the previous note's fret — so
    /// the hand stays in position. When null, auto-selection prefers the lowest
    /// fret (the historical behaviour).</param>
    /// <returns>A tuple of (stringNumber, fret) where stringNumber 1 = highest pitch string.</returns>
    public static (int stringNum, int fret) CalculateFret(int midiPitch, int[] tuning,
        int preferredString = 0, int? nearFret = null)
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

        // Auto: score each playable string. Without a hand-position hint the score
        // is the fret itself (prefer low positions); with one it is the distance to
        // the previous fret, ties broken toward the lower fret.
        int bestString = stringCount; // lowest string as fallback
        int bestFret = 99;
        int bestScore = int.MaxValue;

        // Search from highest to lowest string
        for (int idx = stringCount - 1; idx >= 0; idx--)
        {
            int openPitch = tuning[idx];
            int fret = midiPitch - openPitch;

            if (fret >= 0 && fret <= 24)
            {
                int score = nearFret.HasValue ? System.Math.Abs(fret - nearFret.Value) : fret;
                if (score < bestScore || (score == bestScore && fret < bestFret))
                {
                    bestScore = score;
                    bestString = ToStringNum(idx);
                    bestFret = fret;
                }
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