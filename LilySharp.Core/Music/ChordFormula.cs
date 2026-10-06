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
using System.Text;

namespace LilySharp.Core.Music;

/// <summary>
/// A chord quality ASSEMBLED from its parts rather than looked up — the open chord entry
/// (HANDOFF §2 E ⑹, the owner's approval 2026-10-06): <c>C9sus4</c>, <c>Cm7-5-9</c>,
/// <c>Cmaj7+11</c>, <c>C7alt</c>, <c>C5</c>, <c>C69</c>, <c>Cadd2</c>, <c>C7omit3</c>.
/// </summary>
/// <remarks>
/// <para>
/// THE GRAMMAR, in this order (each part optional, the whole quality consumed):
/// a quality word <c>m</c> / <c>min</c> / <c>dim</c> / <c>aug</c>, then <c>maj</c> (alone, or
/// after <c>m</c> / <c>aug</c>); an extension <c>5</c> <c>6</c> <c>69</c> <c>7</c> <c>9</c>
/// <c>11</c> <c>13</c>; then, in any order and each at most once, the alterations
/// <c>-5 +5 -9 +9 +11 -13</c>, <c>add2 add4 add6 add9 add11 add13</c>, <c>omit3 omit5</c>,
/// <c>sus2 sus4 sus</c> and <c>alt</c>. The alterations are spelled <c>+</c>/<c>-</c> because
/// <c>#</c>/<c>b</c> after a letter belong to the root and the bass alone
/// (<see cref="ChordQualityRegistry"/>'s ByToken remark — what keeps <c>Bb5</c> unambiguous).
/// </para>
/// <para>
/// A spelling whose tones are a REGISTERED quality's resolves to that quality
/// (<see cref="ChordQualityRegistry.TryResolve(string?, out ChordQuality, out ChordFormula?)"/>),
/// so <c>C7-9</c> and its tables are unchanged and only a new tone set reaches this type.
/// </para>
/// <para>
/// THE NAME is LilyPond's, computed from the tones the way LilyPond computes it
/// (<see cref="Name"/>), and the twin hands the tones to LilyPond explicitly
/// (<see cref="LilyPondModifier"/>), so LilyPond prints the same name for the same chord.
/// </para>
/// </remarks>
public sealed class ChordFormula : System.IEquatable<ChordFormula>
{
    /// <summary>The quality as written (after the root), for messages and the source spelling.</summary>
    public string Source { get; }

    /// <summary>The chord tones, the root first, ordered by step.</summary>
    public ImmutableArray<ChordToneSpec> Tones { get; }

    private ChordFormula(string source, ImmutableArray<ChordToneSpec> tones)
    {
        Source = source;
        Tones = tones;
    }

    // The major scale above the root by 0-based diatonic step (1, 2, 3, … 13): what a step
    // is when nothing alters it. Steps 7 and up wrap an octave (9 = 2 + 12).
    private static readonly int[] MajorScale = [0, 2, 4, 5, 7, 9, 11];

    private static int Major(int step) => MajorScale[step % 7] + 12 * (step / 7);

    /// <summary>Reads an assembled quality; false when <paramref name="quality"/> is not one.</summary>
    public static bool TryParse(string quality, out ChordFormula formula)
    {
        formula = null!;
        if (string.IsNullOrEmpty(quality))
            return false;
        int i = 0;
        bool Take(string word)
        {
            if (string.CompareOrdinal(quality, i, word, 0, word.Length) != 0)
                return false;
            i += word.Length;
            return true;
        }

        // The quality word, then `maj`.
        bool minor = false, dim = false, aug = false, maj = false;
        if (Take("maj"))
            maj = true;
        else if (Take("min") || Take("m"))
            minor = true;
        else if (Take("dim"))
            dim = true;
        else if (Take("aug"))
            aug = true;
        if (!maj && (minor || aug) && Take("maj"))
            maj = true;

        // The extension — the longest first, so `69` is not `6` then `9`.
        int ext = 0;
        foreach (var (word, value) in new[] { ("69", 69), ("13", 13), ("11", 11), ("9", 9), ("7", 7), ("6", 6), ("5", 5) })
            if (Take(word))
            {
                ext = value;
                break;
            }

        // The modifiers, in any order, each once.
        var seen = new HashSet<string>(System.StringComparer.Ordinal);
        string[] modifiers =
        [
            "-5", "+5", "-9", "+9", "+11", "-13",
            "add13", "add11", "add2", "add4", "add6", "add9",
            "omit3", "omit5", "sus2", "sus4", "sus", "alt",
        ];
        while (i < quality.Length)
        {
            string? hit = null;
            foreach (var m in modifiers)
                if (Take(m))
                {
                    hit = m == "sus" ? "sus4" : m;
                    break;
                }
            if (hit == null || !seen.Add(hit))
                return false;
        }

        // A power chord is the bare `5`; `alt` is the altered dominant, `7alt` or `alt`.
        if (ext == 5)
        {
            if (minor || dim || aug || maj || seen.Count > 0)
                return false;
            formula = new ChordFormula(quality, [new(0, 0), new(4, 7)]);
            return true;
        }
        if (seen.Contains("alt"))
        {
            if (minor || dim || aug || maj || (ext != 0 && ext != 7) || seen.Count > 1)
                return false;
            // LILYPOND-REF: ly/chord-modifiers-init.ly ignatzekExceptionMusic (lines 54-55) —
            //   <c e g bes des' ees' fis' aes'> is the altered chord (super-Locrian). Its raised
            //   ninth is spelled a MINOR TENTH (ees'), and LilyPond matches an exception by the
            //   spelled pitches, so the tone is the tenth here too: written as `.9+` it also
            //   replaced the `.9-` (replace-step) and LilyPond named C7♯9♯11♭13 (p846).
            formula = new ChordFormula(quality,
                [new(0, 0), new(2, 4), new(4, 7), new(6, 10), new(8, 13), new(9, 15), new(10, 18), new(12, 20)]);
            return true;
        }
        bool sus2 = seen.Contains("sus2"), sus4 = seen.Contains("sus4");
        if ((sus2 || sus4) && (minor || dim))
            return false;
        if (dim && maj)
            return false;

        // The steps, by 0-based diatonic step: the semitone each holds.
        var steps = new SortedDictionary<int, int>();
        void Set(int step, int semitone) => steps[step] = semitone;

        Set(0, 0);
        Set(2, minor || dim ? 3 : 4);
        Set(4, dim ? 6 : aug ? 8 : 7);
        int seventh = maj ? 11 : dim ? 9 : 10;
        switch (ext)
        {
            case 0:
                // `maj` alone is the major seventh, as the registered `maj` has always been.
                if (maj)
                    Set(6, seventh);
                break;
            case 6: Set(5, 9); break;
            case 69: Set(5, 9); Set(8, 14); break;
            case 7: Set(6, seventh); break;
            case 9: Set(6, seventh); Set(8, 14); break;
            case 11: Set(6, seventh); Set(8, 14); Set(10, 17); break;
            // The thirteenth without the eleventh — Lily#'s rule for every 13
            // (ChordQuality's remark on the plain extensions).
            case 13: Set(6, seventh); Set(8, 14); Set(12, 21); break;
        }

        // The alterations replace the step they name. Both ninths at once is `alt`'s: LilyPond
        // holds one pitch per step (replace-step), so `-9+9` would have no twin to hand over.
        bool flatNine = seen.Contains("-9"), sharpNine = seen.Contains("+9");
        if ((seen.Contains("-5") && seen.Contains("+5")) || (flatNine && sharpNine))
            return false;
        if (seen.Contains("-5")) Set(4, 6);
        if (seen.Contains("+5")) Set(4, 8);
        if (flatNine) Set(8, 13);
        if (sharpNine) Set(8, 15);
        if (seen.Contains("+11")) Set(10, 18);
        if (seen.Contains("-13")) Set(12, 20);

        foreach (var (word, step) in new[] { ("add2", 1), ("add4", 3), ("add6", 5), ("add9", 8), ("add11", 10), ("add13", 12) })
            if (seen.Contains(word))
            {
                if (steps.ContainsKey(step))
                    return false;
                Set(step, Major(step));
            }
        if (sus2 || sus4)
        {
            steps.Remove(2);
            if (sus2) Set(1, 2);
            if (sus4) Set(3, 5);
        }
        if (seen.Contains("omit3"))
        {
            if (!steps.Remove(2))
                return false;
        }
        if (seen.Contains("omit5"))
            steps.Remove(4);

        var tones = ImmutableArray.CreateBuilder<ChordToneSpec>();
        foreach (var (step, semitone) in steps)
            tones.Add(new ChordToneSpec(step, semitone));
        formula = new ChordFormula(quality, tones.ToImmutable());
        return true;
    }

    /// <summary>True when the third is minor (LilyPond's lowercase-root test, see
    /// <see cref="ChordQualityRegistry.HasMinorThird(ChordQuality)"/>).</summary>
    public bool HasMinorThird => Tones.Any(t => t.DiatonicStep == 2 && t.Semitone == 3);

    // A tone's alteration as LilyPond's pitch has it: semitones from the major scale's step.
    private static int Alteration(ChordToneSpec t) => t.Semitone - Major(t.DiatonicStep);

    // LILYPOND-REF: scm/chord-name.scm natural-chord-alteration — FLAT for step 7, else 0.
    private static int NaturalAlteration(ChordToneSpec t) => t.DiatonicStep == 6 ? -1 : 0;

    private static bool IsNatural(ChordToneSpec t) => Alteration(t) == NaturalAlteration(t);

    /// <summary>
    /// The printed suffix (after the root) and how many of its leading characters stand on
    /// the root's baseline (the rest is the superscript), in <paramref name="style"/>.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/chord-ignatzek-names.scm:227-301 ignatzek-chord-names — exceptions
    ///   first; else sus2/sus4 suffixes (a third beside them becomes an addition), the minor
    ///   third's prefix, the main name (the highest of 7 6 5 4 3), the stacked-thirds run from
    ///   the fifth up split into alterations and additions, and a natural seventh with only
    ///   natural extensions named by its top.
    /// LILYPOND-REF: scm/chord-ignatzek-names.scm:103-209 ignatzek-format-chord-name — the
    ///   raised group is main + alterations + suffixes + additions; filter-main-name drops a
    ///   natural 5 or 3, filter-alterations keeps the altered ones and the highest natural one
    ///   above the fifth, name-step draws majorSevenSymbol for a natural 7 (alteration 0).
    /// LILYPOND-REF: ly/engraver-init.ly minorChordModifier, additionalPitchPrefix and
    ///   majorSevenSymbol (lines 945-947) — "m", "add", the triangle.
    /// LILYPOND-REF: ly/chord-modifiers-init.ly ignatzekExceptionMusic (lines 47-59) — +, °,
    ///   ø, °7, alt, and the power chord's 5.
    /// ⚠️ The separator LilyPond puts between the raised pieces (chordNameSeparator, half a
    /// space) is not drawn — the registered qualities' strings have never carried it either.
    /// Under <c>words</c> the triangle is the word <c>maj</c> and the exception symbols the
    /// words of the registered qualities (dim, aug, m7♭5, dim7): LILYSHARP-OWN, as for them.
    /// </remarks>
    public (string Suffix, int Baseline) Name(Semantics.ChordQualityStyle style)
    {
        bool words = style == Semantics.ChordQualityStyle.Words;
        var pitches = Tones.Where(t => t.DiatonicStep != 0).ToList();
        // LilyPond's get-step: the first pitch on that (1-based) step, or none.
        ChordToneSpec? Get(int step1)
        {
            foreach (var p in pitches)
                if (p.DiatonicStep == step1 - 1)
                    return p;
            return null;
        }
        string Key() => string.Join(",", pitches.Select(p => p.DiatonicStep + ":" + p.Semitone));

        switch (Key())
        {
            case "2:4,4:8": return words ? ("aug", 0) : ("+", 1);
            case "2:3,4:6": return words ? ("dim", 0) : ("°", 1);
            case "2:3,4:6,6:10": return words ? ("m7♭5", 0) : ("ø", 0);
            case "2:3,4:6,6:9": return words ? ("dim7", 0) : ("°7", 1);
            case "2:4,4:7,6:10,8:13,9:15,10:18,12:20": return ("alt", 0);
            case "4:7": return ("5", 0);
        }

        var adds = new List<ChordToneSpec>();
        var suffixes = new List<ChordToneSpec>();
        foreach (int j in new[] { 2, 4 })
            if (Get(j) is { } sj)
            {
                if (Get(3) is { } third)
                {
                    adds.Insert(0, third);
                    pitches.Remove(third);
                }
                suffixes.Insert(0, sj);
            }
        bool minorPrefix = Get(3) is { } m3 && Alteration(m3) == -1;
        ChordToneSpec? main = Get(7) ?? Get(6) ?? Get(5) ?? Get(4) ?? Get(3);

        // From the fifth up: the leading run of stacked thirds, and what follows it.
        var upper = pitches.Where(p => p.DiatonicStep >= 4).ToList();
        int split = upper.Count == 0 ? 0 : 1;
        while (split < upper.Count && upper[split].DiatonicStep - upper[split - 1].DiatonicStep == 2)
            split++;
        var alterations = upper.Take(split).Where(p => p != main).ToList();
        adds.AddRange(upper.Skip(split).Where(p => p != main));

        if (main is { DiatonicStep: 6 } seventh && IsNatural(seventh)
            && alterations.Any(a => a.DiatonicStep > 6) && alterations.All(IsNatural))
        {
            main = alterations[^1];
            alterations.Clear();
        }

        string Step(ChordToneSpec p)
        {
            if (Alteration(p) == 0 && p.DiatonicStep == 6)
                return words ? "maj7" : "△";
            int a = Alteration(p) - NaturalAlteration(p);
            return (a > 0 ? new string('♯', a) : a < 0 ? new string('♭', -a) : "") + (p.DiatonicStep + 1);
        }

        var raised = new StringBuilder();
        if (main is { } mn && !(IsNatural(mn) && mn.DiatonicStep is 4 or 2))
            raised.Append(Step(mn));
        var filtered = alterations.Where(a => !IsNatural(a)).ToList();
        if (alterations.Count > 0 && IsNatural(alterations[^1]) && alterations[^1].DiatonicStep > 4)
            filtered.Add(alterations[^1]);
        foreach (var a in filtered)
            raised.Append(Step(a));
        foreach (var s in suffixes)
            raised.Append("sus").Append(Step(s));
        foreach (var a in adds)
            raised.Append("add").Append(Step(a));

        string text = raised.ToString();
        // Under words the triangle's word stands before a number LilyPond's symbol carried:
        // `△9` is `maj9`, as the registered maj9 prints.
        if (words && text.StartsWith("maj7", System.StringComparison.Ordinal) && text.Length > 4 && char.IsDigit(text[4]))
            text = "maj" + text.Substring(4);
        return minorPrefix ? ("m" + text, 1) : (text, 0);
    }

    /// <summary>
    /// The quality as a <c>\chordmode</c> modifier that names every tone: <c>:1</c> and then each
    /// tone's step, with <c>+</c>/<c>-</c> for its distance from the step LilyPond adds by
    /// default (a major or perfect step, the minor seventh) — <c>c:1.4.5.7.9</c> is C9sus4.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/chord-entry.scm:67-80 construct-chord-elements' interpret-additions —
    ///   a '.'-joined step with its '+' / '-'; Documentation/en/notation/chords.itely — the
    ///   power chord is <c>c:1.5</c>, the stack of thirds built up to 1 is the root alone.
    /// Explicit steps, so LilyPond's 13-without-11 rule (which fires only when the eleventh is
    /// "not given explicitly") never touches what the formula wrote.
    /// </remarks>
    public string LilyPondModifier()
    {
        var sb = new StringBuilder(":1");
        foreach (var t in Tones)
        {
            if (t.DiatonicStep == 0)
                continue;
            int byDefault = t.DiatonicStep == 6 ? 10 : Major(t.DiatonicStep);
            int d = t.Semitone - byDefault;
            sb.Append('.').Append(t.DiatonicStep + 1);
            sb.Append(d > 0 ? new string('+', d) : d < 0 ? new string('-', -d) : "");
        }
        return sb.ToString();
    }

    /// <summary>Equal when the tones are: two spellings of one chord are one chord.</summary>
    public bool Equals(ChordFormula? other) => other is not null && Tones.SequenceEqual(other.Tones);

    public override bool Equals(object? obj) => Equals(obj as ChordFormula);

    public override int GetHashCode()
    {
        var h = new System.HashCode();
        foreach (var t in Tones)
            h.Add(t);
        return h.ToHashCode();
    }
}
