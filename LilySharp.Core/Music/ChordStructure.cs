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
        // The half-diminished spelling follows the symbol grammar's alteration
        // rule — EVERY altered tension is '+'/'-' (never '#'/'b', which belong
        // to the root and bass alone, so Bb9 stays unambiguous), and LilyPond's
        // m7.5- went with the ':' entry format (GRAMMAR_AUDIT 8.1).
        ["m7-5"] = ChordQuality.HalfDiminished7,
        // '+' is the jazz spelling of the augmented triad (C+). One canonical
        // DISPLAY ("Caug") for both entries, like min/m.
        ["+"] = ChordQuality.Augmented,
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

    // Reverse of Tones: each quality's pitch-class set (semitones-above-root mod 12)
    // -> the quality, for recognizing a chord's notes. Every registered quality has
    // a distinct set, so a match is unambiguous once the root is fixed.
    private static readonly Dictionary<string, ChordQuality> ByPitchClasses = BuildPitchClassIndex();

    private static Dictionary<string, ChordQuality> BuildPitchClassIndex()
    {
        var map = new Dictionary<string, ChordQuality>();
        foreach (var (quality, tones) in Tones)
            map[PitchClassKey(tones.Select(t => t.Semitone))] = quality;
        // Fifthless voicings: the PERFECT 5TH is the most omittable chord tone, so a
        // chord keeps its name without it. <1 3 7> in C = C-E-B names as Cmaj7 (no G);
        // the two-note root+3rd <c e> / <c ees>, whose 3rd still fixes major vs minor,
        // names as C / Cm — a bare @chord means the writer wants a symbol. Each
        // dropped-fifth set is distinct from every full quality, so recognition stays
        // unambiguous (a rootless-5th dyad like <c g> has no 3rd and is left unnamed).
        foreach (var q in new[]
        {
            ChordQuality.Major, ChordQuality.Minor,
            ChordQuality.Major7, ChordQuality.Dominant7,
            ChordQuality.Minor7, ChordQuality.MinorMajor7,
        })
            map.TryAdd(
                PitchClassKey(Tones[q].Where(t => t.Semitone != 7).Select(t => t.Semitone)), q);
        return map;
    }

    private static string PitchClassKey(IEnumerable<int> semitones) =>
        string.Join(",", semitones.Select(s => ((s % 12) + 12) % 12).Distinct().OrderBy(x => x));

    /// <summary>
    /// Recognizes a chord QUALITY from the set of semitone intervals above its root
    /// (each reduced to a pitch class; 0 = the root itself). Returns false when the
    /// interval set matches no registered quality. The root is supplied separately
    /// by the caller (the chord's first member), so C6 and Am7 — which share a
    /// pitch-class set — are told apart by their root.
    /// </summary>
    public static bool TryRecognize(IEnumerable<int> intervalsFromRoot, out ChordQuality quality)
        => ByPitchClasses.TryGetValue(PitchClassKey(intervalsFromRoot), out quality);
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
    int? BassAlter = null,
    // A verbatim quality suffix for a chord whose token is NOT in the registry
    // (e.g. "M7", "7#9"): the interval set is unknown (Tones stays empty, so no
    // note expansion), but the root still yields a Roman degree — so `c:M7`
    // shows "CM7" / "IM7" instead of falling back to the literal name.
    string? RawSuffix = null,
    // True for the /+bass ADDED-bass entry form. LP realizes /bass as an
    // inversion (a chord member dropped to the bottom) and /+bass as an extra
    // note below, but PRINTS the same name for both — so DisplayName ignores
    // this; it records entry intent for a future realization.
    // LILYPOND-REF: scm/chord-entry.scm:91-115 process-inversion — a non-member
    // slash pitch degrades to a plain bass.
    // LILYPOND-REF: scm/chord-entry.scm:46-50 interpret-bass — the /+FOO part
    // always sets a plain bass (never an inversion).
    bool BassIsAdded = false)
{
    // Diatonic-step semitones come from RelativeOctave.StepSemitoneOf (single source).

    /// <summary>Semitone offsets of the chord tones above the root (the pitch set).</summary>
    public ImmutableArray<int> Intervals =>
        RawSuffix != null ? ImmutableArray<int>.Empty
        : [.. ChordQualityRegistry.GetTones(Quality).Select(t => t.Semitone)];

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
            if (RawSuffix != null)
                return ImmutableArray<ChordTone>.Empty;
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
            sb.Append(RawSuffix ?? ChordQualityRegistry.GetSuffix(Quality));
            if (BassStep is int bs)
            {
                sb.Append('/');
                sb.Append(SpellPitch(bs, BassAlter ?? 0));
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Recognizes a chord from its ROOT (first member) and the pitch classes of all
    /// its members. Returns false when the members match no registered quality. Used
    /// by the auto <c>@chord</c> annotation — the root is the chord's first note, so
    /// C6 and Am7 (same pitch-class set) are told apart by it.
    /// </summary>
    public static bool TryRecognize(int rootStep, int rootAlter,
        IEnumerable<int> memberPitchClasses, out ChordStructure? structure)
    {
        structure = null;
        int rootPc = Mod12(Semantics.RelativeOctave.StepSemitoneOf(rootStep) + rootAlter);
        var intervals = memberPitchClasses.Select(pc => Mod12(pc - rootPc));
        if (!ChordQualityRegistry.TryRecognize(intervals, out var quality))
            return false;
        structure = new ChordStructure(rootStep, rootAlter, quality);
        return true;
    }

    private static int Mod12(int a) => ((a % 12) + 12) % 12;

    /// <summary>
    /// This chord as a Roman-numeral scale degree in the given key, jazz style: an
    /// UPPERCASE numeral for the root's scale degree plus the SAME quality suffix as
    /// the printed name (e.g. <c>Imaj7</c>, <c>IIm7</c>, <c>V7</c>, <c>VIm</c>). A
    /// chromatic root gets a ♯/♭ prefix (<c>♭III</c>, <c>♯IV</c>); a slash bass shows
    /// as its own degree (<c>V7/VII</c>).
    /// </summary>
    /// <param name="tonicStep">The key tonic's diatonic step (0=C .. 6=B) — the actual
    /// tonic, so a minor key is measured from its own tonic (A minor: Am = I).</param>
    /// <param name="keySharps">The signature: +sharps / -flats.</param>
    public string ToRomanNumeral(int tonicStep, int keySharps)
    {
        static string Degree(int step, int alter, int tonicStep, int keySharps)
        {
            int degree = ((step - tonicStep) % 7 + 7) % 7;
            string numeral = new[] { "I", "II", "III", "IV", "V", "VI", "VII" }[degree];
            // Accidental = how far the root sits from the scale's own note on that
            // letter (0 = diatonic, +/- = chromatic).
            int acc = alter - KeySpelling.Alteration(step, keySharps);
            string prefix = acc > 0 ? new string('♯', acc)    // ♯
                : acc < 0 ? new string('♭', -acc)             // ♭
                : "";
            return prefix + numeral;
        }

        var sb = new StringBuilder(Degree(RootStep, RootAlter, tonicStep, keySharps));
        sb.Append(RawSuffix ?? RomanSuffix(Quality));
        if (BassStep is int bs)
            sb.Append('/').Append(Degree(bs, BassAlter ?? 0, tonicStep, keySharps));
        return sb.ToString();
    }

    /// <summary>The quality suffix in Roman-numeral style: the triad symbols
    /// diminished ° / augmented + / half-diminished ø are more idiomatic there than
    /// the name-style "dim"/"aug"/"m7♭5"; every other quality keeps the printed
    /// suffix (m, maj7, m7, 7, …), so IIm7 / V7 / Imaj7 read as expected.</summary>
    private static string RomanSuffix(ChordQuality quality) => quality switch
    {
        ChordQuality.Diminished => "°",       // °
        ChordQuality.Diminished7 => "°7",     // °7
        ChordQuality.HalfDiminished7 => "ø7",  // ø7
        ChordQuality.Augmented => "+",
        _ => ChordQualityRegistry.GetSuffix(quality),
    };

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
    /// the SYMBOL as it prints (GRAMMAR_AUDIT 8.1, decided 2026-08-21). An
    /// UPPERCASE root <c>A</c>–<c>G</c>, an optional <c>#</c>/<c>b</c> accidental,
    /// a bare quality (empty = major), and an optional <c>/</c> slash bass spelled
    /// the same way. So "C", "Am", "G7", "F#m", "Bb7", "Cmaj7/E" parse; the
    /// retired <c>:</c> entry form ("a:m", "g:7") and an unknown quality ("C7-9")
    /// do not — an unknown quality still DISPLAYS verbatim in a chords row (the
    /// collector's raw-suffix fallback), this strict parse is what @chord and the
    /// completion build on. Shared by the <c>chords { }</c> block and @chord
    /// annotations, so the two stay in one format.
    /// </summary>
    /// <remarks>
    /// The <c>/+</c> added-bass entry went with the <c>:</c> format: '+' now
    /// spells altered tensions and the augmented triad inside the quality, and
    /// LilyPond PRINTS /bass and /+bass identically anyway (the flag recorded
    /// entry intent only — <see cref="BassIsAdded"/> stays on the model for the
    /// MusicXML importer's sake, but no Lily# spelling sets it any more).
    /// </remarks>
    public static bool TryParseChordEntry(string s, out ChordStructure result)
    {
        result = default!;
        if (string.IsNullOrEmpty(s))
            return false;

        int slash = s.IndexOf('/');
        string main = slash >= 0 ? s.Substring(0, slash) : s;
        string? bass = slash >= 0 ? s.Substring(slash + 1) : null;

        if (!TryParseSymbolPitch(main, out int step, out int alter, out string qualStr))
            return false;
        if (!ChordQualityRegistry.TryResolve(qualStr, out var quality))
            return false;

        int? bassStep = null, bassAlter = null;
        if (bass != null)
        {
            if (!TryParseSymbolPitch(bass, out int bs, out int ba, out string rest)
                || rest.Length != 0)
                return false;
            bassStep = bs;
            bassAlter = ba;
        }

        result = new ChordStructure(step, alter, quality, bassStep, bassAlter);
        return true;
    }

    /// <summary>A symbol-format pitch: uppercase letter + optional '#'/'b',
    /// returning whatever follows (the quality for a root, which must be empty
    /// for a bass). Lowercase letters do not parse — 'b' is an accidental here,
    /// so the case IS the grammar (GRAMMAR_AUDIT 8.1: "Bb5" is B♭'s power chord
    /// precisely because a flat can only follow a root).</summary>
    public static bool TryParseSymbolPitch(string s, out int step, out int alter, out string rest)
    {
        step = -1; alter = 0; rest = "";
        if (string.IsNullOrEmpty(s))
            return false;
        step = "CDEFGAB".IndexOf(s[0]); // display order == the model's 0=C..6=B
        if (step < 0)
            return false;
        int i = 1;
        if (s.Length > 1 && (s[1] == '#' || s[1] == 'b'))
        {
            alter = s[1] == '#' ? 1 : -1;
            i = 2;
        }
        rest = s.Substring(i);
        return true;
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
