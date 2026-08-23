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
    string SeventhQuality,   // the diatonic 7-chord suffix: "maj7", "7", "m7", "m7-5", …
    ImmutableArray<int> PitchClasses)   // root, third, fifth (0..11)
{
    /// <summary>Chord-symbol display: "C", "Dm", "F#m", "Bdim".</summary>
    public string Symbol => RootDisplay + Quality;

    /// <summary>The diatonic seventh chord: "Cmaj7", "Dm7", "G7", "Bm7-5".</summary>
    public string SeventhSymbol => RootDisplay + SeventhQuality;

    /// <summary>The suspended-fourth chord on this root ("Csus4").</summary>
    public string SusFourthSymbol => RootDisplay + "sus4";

    /// <summary>The suspended-second chord on this root ("Csus2").</summary>
    public string SusSecondSymbol => RootDisplay + "sus2";

    /// <summary>The root as a chord symbol ("C", "F#", "Bb").</summary>
    public string RootDisplay =>
        char.ToUpperInvariant(RootLetter)
        + RootAlter switch { 2 => "##", 1 => "#", -1 => "b", -2 => "bb", _ => "" };

    // (The Dutch-pitch entry spellings — LilyRoot / LilyQualitySuffix — retired with
    // the ':' entry format: the SYMBOL is the entry now, so Symbol/SeventhSymbol/…
    // are both the display and the insert text.)

    /// <summary>Roman-numeral degree: uppercase major, lowercase minor/dim, with °/+.</summary>
    /// <remarks>
    /// ⚠️ THIS IS ANALYSIS NOTATION, NOT A LILY# ENTRY. It is shown to a reader (the
    /// completion's Detail line names the function of a chord it is offering by name);
    /// Lily#'s own degree spelling is <see cref="RomanSymbol"/>, which keeps the numeral
    /// uppercase and carries the quality as a suffix, exactly as
    /// <c>ChordStructure.ToRomanNumeral</c> prints it. Do not insert this one: the
    /// lowercase numeral is not the grammar and the ° does not even lex.
    /// </remarks>
    public string Roman
    {
        get
        {
            string r = new[] { "I", "II", "III", "IV", "V", "VI", "VII" }[Degree];
            if (Quality is "m" or "dim") r = r.ToLowerInvariant();
            return Quality switch { "dim" => r + "°", "aug" => r + "+", _ => r };
        }
    }

    /// <summary>The numeral alone, always uppercase — the head of a Lily# degree entry.</summary>
    public string Numeral => new[] { "I", "II", "III", "IV", "V", "VI", "VII" }[Degree];

    /// <summary>This degree written as a Lily# chord ENTRY: <c>I</c>, <c>IIm</c>,
    /// <c>VIIdim</c>. The numeral plus the SAME quality suffix the absolute
    /// <see cref="Symbol"/> carries.</summary>
    /// <remarks>
    /// ⚠️ No accidental prefix, and that is a property of being DIATONIC: the degree's root
    /// is the key's own note on that letter, so <c>ChordStructure.ToRomanNumeral</c> emits
    /// no ♭/♯ for it. A chromatic degree would need one, and would not come from here.
    /// ⚠️ The suffix is the ASCII one (<c>dim</c>, <c>m7-5</c>), not the printed roman
    /// symbols (° ø7) — MEASURED: the lexer refuses those characters outright, so offering
    /// them would hand the writer a spelling the compiler cannot read.
    /// </remarks>
    public string RomanSymbol => Numeral + Quality;

    /// <summary>The diatonic seventh as a Lily# degree entry: <c>Imaj7</c>, <c>IIm7</c>,
    /// <c>V7</c>, <c>VIIm7-5</c>.</summary>
    public string RomanSeventhSymbol => Numeral + SeventhQuality;
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

            char seventh = Letters[(rootStep + 6) % 7];
            int rPc = Pc(root, sharps), tPc = Pc(third, sharps), fPc = Pc(fifth, sharps);
            int t3 = (tPc - rPc + 12) % 12, t5 = (fPc - rPc + 12) % 12;
            int t7 = (Pc(seventh, sharps) - rPc + 12) % 12;
            string quality = (t3, t5) switch
            {
                (4, 7) => "",
                (3, 7) => "m",
                (3, 6) => "dim",
                (4, 8) => "aug",
                _ => "",
            };
            // Seventh-chord quality from the triad + the seventh's interval
            // (11 = major 7th, 10 = minor 7th, 9 = diminished 7th). Altered
            // tensions spell '+'/'-' — '#'/'b' belong to the root alone
            // (GRAMMAR_AUDIT 8.1), so the half-diminished is m7-5.
            string seventhQuality = (quality, t7) switch
            {
                ("", 11) => "maj7",
                ("", 10) => "7",
                ("m", 10) => "m7",
                ("m", 11) => "mmaj7",
                ("dim", 10) => "m7-5",
                ("dim", 9) => "dim7",
                ("aug", 11) => "maj7+5",
                ("aug", 10) => "7+5",
                _ => "7",
            };

            chords.Add(new DiatonicChord(
                deg, root, KeySpelling.Alteration(rootStep, sharps), quality, seventhQuality,
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
