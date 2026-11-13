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
/// THE NAME is the common jazz spelling, built from the tones by rule (<see cref="NameOf"/>
/// — the owner's decision 2026-11-13, which overrides the 2026-10-06 approval of LilyPond's
/// Ignatzek names recorded here until then). The twin hands the tones to LilyPond explicitly
/// (<see cref="LilyPondModifier"/>) and LilyPond names them by ITS rules, so the twin's
/// names are LilyPond's and differ from the page's wherever the two rule sets do.
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

    /// <summary>
    /// The printed suffix (after the root) and how many of its leading characters stand on
    /// the root's baseline (the rest is the superscript), in <paramref name="style"/>.
    /// </summary>
    public (string Suffix, int Baseline) Name(Semantics.ChordQualityStyle style) => NameOf(Tones, style);

    /// <summary>
    /// A tone set's printed suffix in THE COMMON JAZZ SPELLING, and how many of its leading
    /// characters stand on the root's baseline — the one name builder, for an assembled
    /// quality and for every registered one (<see cref="ChordQualityRegistry.GetSuffix"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// OWNER'S DECISION 2026-11-13 (grammar evaluation 4; the owner's words: 「コード記号は、
    /// ジャズの一般的な綴りにして」— "spell the chord symbols the way jazz commonly does"). It
    /// OVERRIDES the approval of 2026-10-06 recorded on this type until today ("THE NAME is
    /// LilyPond's, computed from the tones the way LilyPond computes it"): LilyPond's Ignatzek
    /// rules named a chart's <c>C7(#9, b13)</c> "C7♯9add♭13", its <c>Bbmaj7(#11)</c>
    /// "B♭△add♯11", its <c>F6(9)</c> "F6add9" and its <c>C13(b9)</c> "C7♭9add13" (the big
    /// band writer's bigband/r-08), which no chart prints. THE RULES, in the order the name is
    /// written:
    /// </para>
    /// <para>
    /// ⑴ The minor third's <c>m</c>. ⑵ With a seventh, THE HIGHEST UNALTERED EXTENSION names
    /// the chord — 7, 9, 11, 13 — whatever stands under it (a 13 over a ♭9 is still a 13:
    /// <c>C13(♭9)</c>; a 7 with a 13 and no ninth is <c>C13</c>, since the number implies what
    /// is below it and "add" is never said of a seventh chord); a MAJOR seventh keeps its
    /// sign and takes the number after it: <c>C△7</c>, <c>C△9</c>, <c>C△13</c> (<c>maj7</c>,
    /// <c>maj9</c> under <c>chordQualities words</c>). Without a seventh the sixth is
    /// <c>6</c>, and with a ninth beside it the 6/9 chord, <c>F6/9</c>. ⑶ The suspension
    /// (<c>sus2</c>, <c>sus4</c>). ⑷ THE ALTERATIONS — ♭5 ♯5 ♭9 ♯9 ♯11 ♭13 — in ascending
    /// order in ONE pair of parentheses: <c>C7(♯9♭13)</c>, <c>B♭△7(♯11)</c>. ⑸ <c>add</c>
    /// only for a tone the chain does not explain — a ninth, eleventh or thirteenth over a
    /// chord with NO seventh, a second or fourth beside the third, a sixth beside a seventh:
    /// <c>Cadd9</c> straight after the root, in the parentheses anywhere else
    /// (<c>Cm(add9)</c>, <c>C6(add11)</c>).
    /// </para>
    /// <para>
    /// WHAT STAYS (the same decision: "keep what the page prints today unless it contradicts
    /// the rules"): the five tone sets with a sign or word of their own — <c>+</c>, <c>°</c>,
    /// <c>ø</c>, <c>°7</c>, <c>alt</c> — and the power chord's <c>5</c>, matched exactly as
    /// LilyPond's exception table matches them; the sus names; and an OMITTED third or fifth
    /// prints nothing (<c>C7omit3</c> is <c>C7</c>), as LilyPond prints it. A natural eleventh
    /// under a thirteenth is not printed either (<c>Cm13</c> with the eleventh is
    /// <c>Cm13</c>, as before).
    /// </para>
    /// <para>
    /// THE 6/9 IS WRITTEN WITH A SLASH and cannot be read for a slash chord: a slash BASS is a
    /// note letter on the root's baseline at the root's size, the <c>6/9</c> is two digits in
    /// the raised run, and every reader of a printed name finds the bass by that difference
    /// (<see cref="ChordSymbolText.BassSlash"/>).
    /// LILYPOND-REF: ly/chord-modifiers-init.ly ignatzekExceptionMusic (lines 47-59) — +, °,
    ///   ø, °7, alt, and the power chord's 5, the exact-match table kept from LilyPond.
    /// LILYSHARP-OWN: everything else here. LilyPond's own names (scm/chord-ignatzek-names.scm
    /// ignatzek-chord-names) are what the LilyPond twin still prints — LilyPond names the
    /// twin's chords itself. Departs from LilyPond in: the number a seventh chord is named by,
    /// the parentheses, the number after the triangle, 6/9, where "add" is said. Goes away
    /// for: a triad, a plain 6, 7, 9, 11, 13, m7, sus chord and the six exception sets.
    /// Observed by <c>ChordFormulaTests</c> (the names).
    /// </para>
    /// <para>
    /// ⚠️ The separator LilyPond puts between the raised pieces (chordNameSeparator, half a
    /// space) is not drawn, as it never was. Under <c>words</c> the triangle is the word
    /// <c>maj</c> (after a minor <c>m</c>, a space and the word: <c>Cm maj7</c>, the registered
    /// quality's spelling since before the formula) and the exception signs are the words
    /// dim, aug, m7(♭5), dim7.
    /// </para>
    /// </remarks>
    internal static (string Suffix, int Baseline) NameOf(
        IReadOnlyList<ChordToneSpec> tones, Semantics.ChordQualityStyle style)
    {
        bool words = style == Semantics.ChordQualityStyle.Words;
        var by = new SortedDictionary<int, int>();
        foreach (var t in tones)
            if (t.DiatonicStep != 0)
                by[t.DiatonicStep] = t.Semitone;

        switch (string.Join(",", by.Select(p => p.Key + ":" + p.Value)))
        {
            case "": return ("", 0);
            case "2:4,4:8": return words ? ("aug", 0) : ("+", 1);
            case "2:3,4:6": return words ? ("dim", 0) : ("°", 1);
            case "2:3,4:6,6:10": return words ? ("m7(♭5)", 1) : ("ø", 0);
            case "2:3,4:6,6:9": return words ? ("dim7", 0) : ("°7", 1);
            case "2:4,4:7,6:10,8:13,9:15,10:18,12:20": return ("alt", 0);
            case "4:7": return ("5", 0);
        }

        int? At(int step) => by.TryGetValue(step, out int s) ? s : null;
        static string Spelled(int step, int semitone)
        {
            int a = semitone - Major(step);
            return (a > 0 ? new string('♯', a) : a < 0 ? new string('♭', -a) : "") + (step + 1);
        }

        int? third = At(2), fifth = At(4), sixth = At(5), seventh = At(6);
        bool minor = third == 3;
        // A diminished seventh over its own triad carries the sign with a higher number
        // (C°9); the exact four-note set was the exception above.
        bool diminished = minor && fifth == 6 && seventh == 9;

        var main = new StringBuilder();
        var altered = new List<string>();
        var added = new List<string>();

        // ⑷ the fifth, when it is not the perfect one (the diminished sign already says its own).
        if (fifth is int f && f != 7 && !diminished)
            altered.Add(Spelled(4, f));
        // A second or a fourth beside the third is an added tone; without one it is the
        // suspension, below.
        if (third != null)
            foreach (int step in new[] { 1, 3 })
                if (At(step) is int beside)
                    added.Add("add" + Spelled(step, beside));

        if (seventh is int sev)
        {
            // ⑵ the highest unaltered extension; the altered ones are ⑷'s.
            int top = 7;
            foreach (var (step, semitone) in by)
            {
                if (step <= 6)
                    continue;
                if (semitone == Major(step) && step is 8 or 10 or 12)
                    top = step + 1;
                else if (semitone != Major(step))
                    altered.Add(Spelled(step, semitone));
            }
            if (sev == 11)
                main.Append(words ? (minor ? " maj" : "maj") : "△");
            else if (sev != 10 && !diminished)
                altered.Insert(altered.Count > 0 && altered[0].EndsWith('5') ? 1 : 0, Spelled(6, sev));
            main.Append(top);
            if (sixth is int six)
                added.Add("add" + Spelled(5, six));
        }
        else
        {
            bool nineBeside = sixth == 9 && At(8) == 14;
            if (sixth is int six)
                main.Append(six == 9 ? (nineBeside ? "6/9" : "6") : Spelled(5, six));
            foreach (var (step, semitone) in by)
                if (step > 6 && !(nineBeside && step == 8))
                    added.Add("add" + Spelled(step, semitone));
        }

        // ⑶ the suspension: no third, and the second or the fourth in its place.
        if (third == null)
            foreach (int step in new[] { 3, 1 })
                if (At(step) is int sus)
                    main.Append("sus").Append(Spelled(step, sus));

        string prefix = diminished ? (words ? "dim" : "°") : minor ? "m" : "";
        int baseline = diminished ? (words ? 0 : 1) : minor ? 1 : 0;
        var name = new StringBuilder(prefix).Append(main);
        // ⑸ `add` straight after the root, in the parentheses anywhere else.
        if (name.Length == 0 && altered.Count == 0)
            foreach (var a in added)
                name.Append(a);
        else if (altered.Count + added.Count > 0)
        {
            name.Append('(');
            foreach (var a in altered)
                name.Append(a);
            foreach (var a in added)
                name.Append(a);
            name.Append(')');
        }
        return (name.ToString(), baseline);
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
