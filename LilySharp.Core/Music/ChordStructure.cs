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

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace LilySharp.Core.Music;

/// <summary>
/// The set of named chord qualities Lily# understands in chord-name entry
/// (<c>c:maj7</c>, <c>a:m</c>, …). Each maps to a fixed set of chord tones and a
/// display suffix.
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/chord-entry.scm — default chord step construction; the
/// named modifiers in ly/chord-modifiers-init.ly (m, dim, aug, maj, sus). The
/// tone sets below are the standard jazz/pop spellings LilyPond produces.
/// </remarks>
public enum ChordQuality
{
    /// <summary>Major triad (root, major third, perfect fifth); no suffix.</summary>
    Major,
    /// <summary>Minor triad (root, minor third, perfect fifth); suffix <c>m</c>.</summary>
    Minor,
    /// <summary>Diminished triad (root, minor third, diminished fifth); suffix <c>dim</c>.</summary>
    Diminished,
    /// <summary>Augmented triad (root, major third, augmented fifth); suffix <c>aug</c>.</summary>
    Augmented,
    /// <summary>Dominant seventh: major triad plus a minor seventh; suffix <c>7</c>.</summary>
    Dominant7,
    /// <summary>Major seventh: major triad plus a major seventh; suffix <c>maj7</c>.</summary>
    Major7,
    /// <summary>Minor seventh: minor triad plus a minor seventh; suffix <c>m7</c>.</summary>
    Minor7,
    /// <summary>Minor-major seventh: minor triad plus a major seventh; suffix <c>m maj7</c>.</summary>
    MinorMajor7,
    /// <summary>Diminished seventh: diminished triad plus a diminished seventh; suffix <c>dim7</c>.</summary>
    Diminished7,
    /// <summary>Half-diminished seventh: diminished triad plus a minor seventh; suffix <c>m7♭5</c>.</summary>
    HalfDiminished7,
    /// <summary>Major sixth: major triad plus a major sixth; suffix <c>6</c>.</summary>
    Major6,
    /// <summary>Minor sixth: minor triad plus a major sixth; suffix <c>m6</c>.</summary>
    Minor6,
    /// <summary>Dominant ninth: dominant seventh plus a major ninth; suffix <c>9</c>.</summary>
    Dominant9,
    /// <summary>Major ninth: major seventh plus a major ninth; suffix <c>maj9</c>.</summary>
    Major9,
    /// <summary>Minor ninth: minor seventh plus a major ninth; suffix <c>m9</c>.</summary>
    Minor9,
    /// <summary>Suspended second (root, major second, perfect fifth); suffix <c>sus2</c>.</summary>
    Sus2,
    /// <summary>Suspended fourth (root, perfect fourth, perfect fifth); suffix <c>sus4</c>.</summary>
    Sus4,
    /// <summary>Dominant seventh with a suspended fourth (no third); suffix <c>7sus4</c>.</summary>
    Dominant7Sus4,
}

/// <summary>One chord tone: a diatonic step above the root (0=root, 2=third,
/// 4=fifth, 6=seventh, 8=ninth) and its semitone offset above the root. The step
/// gives the spelled LETTER; the semitone gives the actual pitch (hence the
/// accidental).</summary>
public readonly record struct ChordToneSpec(int DiatonicStep, int Semitone);

/// <summary>A spelled chord tone: a diatonic letter step (0=C..6=B), an accidental
/// alteration, and how many octaves above the root it sits.</summary>
public readonly record struct ChordTone(int Step, int Alter, int OctaveUp);

/// <summary>
/// Maps chord-entry quality tokens (the text after the <c>:</c>) to a
/// <see cref="ChordQuality"/>, and each quality to its tone set + display suffix.
/// This single table is the foundation the chord NAME display, the editor's
/// note-expansion completion, and future fret diagrams all build on.
/// </summary>
public static class ChordQualityRegistry
{
    // Tones as (diatonic step above root, semitone above root). P1=0, m3=3, M3=4,
    // d5=6, P5=7, #5=8, M6=9, m7=10, M7=11, M9=14.
    private static readonly Dictionary<ChordQuality, ChordToneSpec[]> Tones = new()
    {
        [ChordQuality.Major] = [new(0, 0), new(2, 4), new(4, 7)],
        [ChordQuality.Minor] = [new(0, 0), new(2, 3), new(4, 7)],
        [ChordQuality.Diminished] = [new(0, 0), new(2, 3), new(4, 6)],
        [ChordQuality.Augmented] = [new(0, 0), new(2, 4), new(4, 8)],
        [ChordQuality.Dominant7] = [new(0, 0), new(2, 4), new(4, 7), new(6, 10)],
        [ChordQuality.Major7] = [new(0, 0), new(2, 4), new(4, 7), new(6, 11)],
        [ChordQuality.Minor7] = [new(0, 0), new(2, 3), new(4, 7), new(6, 10)],
        [ChordQuality.MinorMajor7] = [new(0, 0), new(2, 3), new(4, 7), new(6, 11)],
        [ChordQuality.Diminished7] = [new(0, 0), new(2, 3), new(4, 6), new(6, 9)],
        [ChordQuality.HalfDiminished7] = [new(0, 0), new(2, 3), new(4, 6), new(6, 10)],
        [ChordQuality.Major6] = [new(0, 0), new(2, 4), new(4, 7), new(5, 9)],
        [ChordQuality.Minor6] = [new(0, 0), new(2, 3), new(4, 7), new(5, 9)],
        [ChordQuality.Dominant9] = [new(0, 0), new(2, 4), new(4, 7), new(6, 10), new(8, 14)],
        [ChordQuality.Major9] = [new(0, 0), new(2, 4), new(4, 7), new(6, 11), new(8, 14)],
        [ChordQuality.Minor9] = [new(0, 0), new(2, 3), new(4, 7), new(6, 10), new(8, 14)],
        [ChordQuality.Sus2] = [new(0, 0), new(1, 2), new(4, 7)],
        [ChordQuality.Sus4] = [new(0, 0), new(3, 5), new(4, 7)],
        [ChordQuality.Dominant7Sus4] = [new(0, 0), new(3, 5), new(4, 7), new(6, 10)],
    };

    private static readonly Dictionary<ChordQuality, string> Suffix = new()
    {
        [ChordQuality.Major] = "",
        [ChordQuality.Minor] = "m",
        [ChordQuality.Diminished] = "dim",
        [ChordQuality.Augmented] = "aug",
        [ChordQuality.Dominant7] = "7",
        [ChordQuality.Major7] = "maj7",
        [ChordQuality.Minor7] = "m7",
        [ChordQuality.MinorMajor7] = "m maj7",
        [ChordQuality.Diminished7] = "dim7",
        [ChordQuality.HalfDiminished7] = "m7♭5",
        [ChordQuality.Major6] = "6",
        [ChordQuality.Minor6] = "m6",
        [ChordQuality.Dominant9] = "9",
        [ChordQuality.Major9] = "maj9",
        [ChordQuality.Minor9] = "m9",
        [ChordQuality.Sus2] = "sus2",
        [ChordQuality.Sus4] = "sus4",
        [ChordQuality.Dominant7Sus4] = "7sus4",
    };

    // Entry tokens (after the ':') that select each quality. Several spellings map
    // to the same quality (m / min, maj7 / maj, m7b5 / m7.5-).
    private static readonly Dictionary<string, ChordQuality> ByToken = new()
    {
        ["m"] = ChordQuality.Minor,
        ["min"] = ChordQuality.Minor,
        ["dim"] = ChordQuality.Diminished,
        ["aug"] = ChordQuality.Augmented,
        ["7"] = ChordQuality.Dominant7,
        ["maj7"] = ChordQuality.Major7,
        ["maj"] = ChordQuality.Major7,
        ["m7"] = ChordQuality.Minor7,
        ["min7"] = ChordQuality.Minor7,
        ["mmaj7"] = ChordQuality.MinorMajor7,
        ["dim7"] = ChordQuality.Diminished7,
        ["m7b5"] = ChordQuality.HalfDiminished7,
        ["m7.5-"] = ChordQuality.HalfDiminished7,
        ["6"] = ChordQuality.Major6,
        ["m6"] = ChordQuality.Minor6,
        ["min6"] = ChordQuality.Minor6,
        ["9"] = ChordQuality.Dominant9,
        ["maj9"] = ChordQuality.Major9,
        ["m9"] = ChordQuality.Minor9,
        ["min9"] = ChordQuality.Minor9,
        ["sus2"] = ChordQuality.Sus2,
        ["sus4"] = ChordQuality.Sus4,
        ["sus"] = ChordQuality.Sus4,
        ["7sus4"] = ChordQuality.Dominant7Sus4,
    };

    /// <summary>The tones (diatonic step + semitone above root) of a quality.</summary>
    public static IReadOnlyList<ChordToneSpec> GetTones(ChordQuality quality) => Tones[quality];

    /// <summary>The printed suffix after the root (e.g. "m7", "maj7", "").</summary>
    public static string GetSuffix(ChordQuality quality) => Suffix[quality];

    /// <summary>
    /// Resolves a quality token (text after the <c>:</c>, e.g. "m7"); returns false
    /// for an unknown token. An empty/absent token is a plain major triad.
    /// </summary>
    public static bool TryResolve(string? token, out ChordQuality quality)
    {
        if (string.IsNullOrEmpty(token))
        {
            quality = ChordQuality.Major;
            return true;
        }
        return ByToken.TryGetValue(token, out quality);
    }

    /// <summary>All recognized quality tokens (for tooling / completion).</summary>
    public static IReadOnlyCollection<string> Tokens => ByToken.Keys;
}

/// <summary>
/// A fully resolved chord: a root, a quality (hence a tone set), and an optional
/// slash bass. The display name and the spelled note chord are both derived from
/// the structure — so this one model drives the chord-name display, the editor's
/// note-expansion completion, and (later) staff notes and fret diagrams.
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/chord-ignatzek-names.scm — root + quality → printed name.
/// Lily# renders the name as PLAIN TEXT (e.g. "Cmaj7"), not LilyPond's
/// superscript/triangle typography — a deliberate Phase-1 simplification.
/// </remarks>
public sealed record ChordStructure(
    int RootStep,            // 0=C, 1=D, … 6=B (diatonic step)
    int RootAlter,           // accidental: -2..+2 (semitone alteration)
    ChordQuality Quality,
    int? BassStep = null,    // slash bass diatonic step (null = no bass)
    int? BassAlter = null)
{
    // Diatonic-step semitones come from RelativeOctave.StepSemitoneOf (single source).

    /// <summary>Semitone offsets of the chord tones above the root (the pitch set).</summary>
    public ImmutableArray<int> Intervals =>
        [.. ChordQualityRegistry.GetTones(Quality).Select(t => t.Semitone)];

    /// <summary>
    /// The spelled chord tones (letter step, accidental, octave above the root).
    /// Each tone's letter comes from its diatonic degree and its accidental from
    /// the actual semitone, so a minor third spells e♭ (not d♯) and a Bb7 seventh
    /// spells a♭ (not g♯).
    /// </summary>
    public ImmutableArray<ChordTone> Tones
    {
        get
        {
            var b = ImmutableArray.CreateBuilder<ChordTone>();
            foreach (var spec in ChordQualityRegistry.GetTones(Quality))
            {
                int absStep = RootStep + spec.DiatonicStep;
                int letterStep = ((absStep % 7) + 7) % 7;
                int octaveUp = absStep / 7;
                // Semitone span from the root LETTER (natural) to the tone LETTER.
                int naturalSpan = Semantics.RelativeOctave.StepSemitoneOf(letterStep)
                                  - Semantics.RelativeOctave.StepSemitoneOf(RootStep) + 12 * octaveUp;
                int alter = spec.Semitone - naturalSpan + RootAlter;
                b.Add(new ChordTone(letterStep, alter, octaveUp));
            }
            return b.ToImmutable();
        }
    }

    /// <summary>The printed chord symbol, e.g. "C", "Am7", "G7", "B♭maj7", "C/G".</summary>
    public string DisplayName
    {
        get
        {
            var sb = new StringBuilder();
            sb.Append(SpellPitch(RootStep, RootAlter));
            sb.Append(ChordQualityRegistry.GetSuffix(Quality));
            if (BassStep is int bs)
            {
                sb.Append('/');
                sb.Append(SpellPitch(bs, BassAlter ?? 0));
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// The chord as a Lily# note chord, e.g. "&lt;c e g b&gt;" (Cmaj7),
    /// "&lt;c ees g&gt;" (Cm). The notes are bare (no octave marks): in relative
    /// mode each successive tone resolves to the nearest pitch above, voicing the
    /// chord ascending from the root. A slash bass is prepended below the root.
    /// </summary>
    public string ToNoteChord()
    {
        var notes = new List<string>();
        if (BassStep is int bs)
            notes.Add(SpellLilyPitch(bs, BassAlter ?? 0) + ","); // bass an octave down
        foreach (var t in Tones)
            notes.Add(SpellLilyPitch(t.Step, t.Alter));
        return "<" + string.Join(" ", notes) + ">";
    }

    /// <summary>
    /// Parses a chord symbol written as a single word (root letter + Dutch
    /// accidental + quality token), e.g. "cmaj7", "am", "g7", "besm7". Requires a
    /// recognized non-empty quality so plain note letters do not parse as chords.
    /// Used by the editor's note-expansion completion.
    /// </summary>
    public static bool TryParseSymbol(string word, out ChordStructure result)
    {
        result = default!;
        if (string.IsNullOrEmpty(word))
            return false;

        int step = "cdefgab".IndexOf(char.ToLower(word[0]));
        if (step < 0)
            return false;

        string rest = word.Substring(1);
        int alter = 0;
        foreach (var (suffix, a) in new[] { ("isis", 2), ("eses", -2), ("is", 1), ("es", -1) })
        {
            if (rest.StartsWith(suffix, System.StringComparison.Ordinal))
            {
                alter = a;
                rest = rest.Substring(suffix.Length);
                break;
            }
        }

        // Require an explicit, recognized quality (so "c" or "ees" alone do not
        // expand — only "cm", "g7", "besmaj7" do).
        if (rest.Length == 0 || !ChordQualityRegistry.TryResolve(rest, out var quality))
            return false;

        result = new ChordStructure(step, alter, quality);
        return true;
    }

    /// <summary>
    /// Parses a <c>chords { }</c> chord entry — the ONE chord format Lily# accepts:
    /// a Lily# (Dutch) root pitch (<c>c</c>, <c>cis</c>, <c>bes</c>), an optional
    /// <c>:</c> then a quality token (empty = major), and an optional <c>/</c> slash
    /// bass. So "c", "c:m7", "cis:m7", "bes:7", and "g:7/b" parse; a chord symbol
    /// ("Cm7", "C#m7") and an unknown quality ("c:7#9") do not. Shared by the
    /// <c>chords { }</c> block and @chord annotations, so the two stay in one format.
    /// </summary>
    public static bool TryParseChordEntry(string s, out ChordStructure result)
    {
        result = default!;
        if (string.IsNullOrEmpty(s))
            return false;

        int slash = s.IndexOf('/');
        string main = slash >= 0 ? s.Substring(0, slash) : s;
        string? bass = slash >= 0 ? s.Substring(slash + 1) : null;

        int colon = main.IndexOf(':');
        string pitchStr = colon >= 0 ? main.Substring(0, colon) : main;
        string qualStr = colon >= 0 ? main.Substring(colon + 1) : "";

        if (!TryParseDutchPitch(pitchStr, out int step, out int alter))
            return false;
        if (!ChordQualityRegistry.TryResolve(qualStr, out var quality))
            return false;

        int? bassStep = null, bassAlter = null;
        if (bass != null)
        {
            if (!TryParseDutchPitch(bass, out int bs, out int ba))
                return false;
            bassStep = bs;
            bassAlter = ba;
        }

        result = new ChordStructure(step, alter, quality, bassStep, bassAlter);
        return true;
    }

    // A Lily# (Dutch) root pitch: lowercase letter + optional is/isis/es/eses
    // accidental, nothing else (an uppercase root or a '#'/'b' does not parse).
    private static bool TryParseDutchPitch(string s, out int step, out int alter)
    {
        step = -1; alter = 0;
        if (string.IsNullOrEmpty(s))
            return false;
        step = "cdefgab".IndexOf(s[0]);
        if (step < 0)
            return false;
        string rest = s.Substring(1);
        if (rest.Length == 0)
            return true;
        foreach (var (suffix, a) in new[] { ("isis", 2), ("eses", -2), ("is", 1), ("es", -1) })
            if (rest == suffix) { alter = a; return true; }
        return false;
    }

    /// <summary>Spells a diatonic step + alteration as a note name with a Unicode
    /// accidental (e.g. 0/+1 → "C♯", 6/-1 → "B♭"). Shared by the chord-name fallback.</summary>
    public static string SpellPitch(int step, int alter)
    {
        char letter = "CDEFGAB"[((step % 7) + 7) % 7];
        string acc = alter switch
        {
            -2 => "♭♭",
            -1 => "♭",
            0 => "",
            1 => "♯",
            2 => "♯♯",
            _ => "",
        };
        return letter + acc;
    }

    // A Lily# (Dutch) pitch token: letter + is/isis/es/eses accidental.
    private static string SpellLilyPitch(int step, int alter)
    {
        char letter = "cdefgab"[((step % 7) + 7) % 7];
        string acc = alter switch
        {
            2 => "isis",
            1 => "is",
            0 => "",
            -1 => "es",
            -2 => "eses",
            _ => "",
        };
        return letter + acc;
    }
}
