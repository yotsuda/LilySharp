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

namespace LilySharp.Core.Music;

/// <summary>
/// One diatonic triad of a key: its scale degree, root spelling, quality, and the
/// pitch classes of its three tones. The single source of a key's diatonic chords,
/// shared by the '@chord(…)' completion and the auto-harmonizer.
/// </summary>
public readonly record struct DiatonicChord(
    int Degree,          // 0 = I, 1 = ii, … 6 = vii
    char RootLetter,     // 'c'..'b'
    int RootAlter,       // -2..+2 semitones from the natural letter
    string Quality,      // "" major, "m" minor, "dim", "aug"
    ImmutableArray<int> PitchClasses)   // root, third, fifth (0..11)
{
    /// <summary>Chord-symbol display: "C", "Dm", "F#m", "Bdim".</summary>
    public string Symbol => RootDisplay + Quality;

    /// <summary>The root as a chord symbol ("C", "F#", "Bb").</summary>
    public string RootDisplay =>
        char.ToUpperInvariant(RootLetter)
        + RootAlter switch { 2 => "##", 1 => "#", -1 => "b", -2 => "bb", _ => "" };

    /// <summary>The root as a Lily# chord-entry pitch ("c", "fis", "bes").</summary>
    public string LilyRoot =>
        RootLetter + RootAlter switch { 2 => "isis", 1 => "is", -1 => "es", -2 => "eses", _ => "" };

    /// <summary>The Lily# chord-entry quality suffix ("" / ":m" / ":dim" / ":aug").</summary>
    public string LilyQualitySuffix => Quality.Length == 0 ? "" : ":" + Quality;

    /// <summary>Roman-numeral degree: uppercase major, lowercase minor/dim, with °/+.</summary>
    public string Roman
    {
        get
        {
            string r = new[] { "I", "II", "III", "IV", "V", "VI", "VII" }[Degree];
            if (Quality is "m" or "dim") r = r.ToLowerInvariant();
            return Quality switch { "dim" => r + "°", "aug" => r + "+", _ => r };
        }
    }
}

/// <summary>
/// Builds the seven diatonic triads of a key from its tonic letter and signature.
/// </summary>
public static class DiatonicChords
{
    private const string Letters = "cdefgab"; // step 0..6

    /// <summary>
    /// The seven diatonic triads, in scale order (I..vii), of the key with the given
    /// tonic LETTER and sharp(+)/flat(−) count. Works for any mode: each degree's
    /// root is spelled under the signature and its quality read off the third/fifth
    /// intervals — so C major → C Dm Em F G Am Bdim, A minor → Am Bdim C Dm Em F G.
    /// </summary>
    public static ImmutableArray<DiatonicChord> ForKey(char tonic, int sharps)
    {
        int tonicStep = KeySpelling.StepOf(tonic);
        if (tonicStep < 0) tonicStep = 0;

        var chords = ImmutableArray.CreateBuilder<DiatonicChord>(7);
        for (int deg = 0; deg < 7; deg++)
        {
            int rootStep = (tonicStep + deg) % 7;
            char root = Letters[rootStep];
            char third = Letters[(rootStep + 2) % 7];
            char fifth = Letters[(rootStep + 4) % 7];

            int rPc = Pc(root, sharps), tPc = Pc(third, sharps), fPc = Pc(fifth, sharps);
            int t3 = (tPc - rPc + 12) % 12, t5 = (fPc - rPc + 12) % 12;
            string quality = (t3, t5) switch
            {
                (4, 7) => "",
                (3, 7) => "m",
                (3, 6) => "dim",
                (4, 8) => "aug",
                _ => "",
            };

            chords.Add(new DiatonicChord(
                deg, root, KeySpelling.Alteration(rootStep, sharps), quality,
                ImmutableArray.Create(rPc, tPc, fPc)));
        }
        return chords.MoveToImmutable();
    }

    private static int BaseSemitone(char letter) => letter switch
    { 'c' => 0, 'd' => 2, 'e' => 4, 'f' => 5, 'g' => 7, 'a' => 9, 'b' => 11, _ => 0 };

    /// <summary>Pitch class (0..11) of a note letter under a key signature.</summary>
    public static int Pc(char letter, int sharps)
        => ((BaseSemitone(letter) + KeySpelling.Alteration(KeySpelling.StepOf(letter), sharps)) % 12 + 12) % 12;
}
