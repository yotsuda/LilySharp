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
using System.Linq;
using LilySharp.Core.Music;
using LilySharp.Core.Tablature;

namespace LilySharp.Core.Editing;

/// <summary>
/// The voicings of a chord a player can hold on a string instrument, in a register asked for — the
/// notes to write for "Cm7 on the guitar, round here" (owner's request 2026-10-30: a composing AI
/// wrote a guitar chord no hand could hold; a tool that answers what can be held, asked before
/// writing, rather than a rule that refuses it after).
/// </summary>
/// <remarks>
/// <para>
/// A fretted instrument's voicings are <see cref="ChordVoicings"/>'s, the chord diagrams' own list
/// (its rules V1-V6: every needed tone, the root lowest, four frets, four fingers), those wholly in
/// the register first, nearest its middle first. A bowed string (violin, viola, cello, double bass)
/// sounds two strings at once (owner's rule, the same day): its voicings are double stops on two
/// neighbouring strings, of two of the chord's tones, the third or the seventh among them, the
/// hand's two fingers no more than <see cref="BowedReach"/> semitones apart along the strings.
/// </para>
/// <para>
/// The notes are named as the chord spells them (Cm7's E-flat, not D-sharp), in Lily#'s absolute
/// octaves (<c>c'</c> is middle C) and as they sound — what a fretted or bowed part writes, its
/// clef's octave being the page's.
/// </para>
/// </remarks>
public static class ChordVoicingFinder
{
    /// <summary>One voicing: the fret (or the semitones up a bowed string) of each string, lowest
    /// string first, -1 for one not played; the pitches it sounds, lowest first; and the notes as
    /// Lily# writes them, lowest first.</summary>
    public sealed record Voicing(IReadOnlyList<int> Frets, IReadOnlyList<int> Pitches, IReadOnlyList<string> Notes)
    {
        /// <summary>The frets as a chord diagram writes them, lowest string first: <c>x35343</c>.</summary>
        public string Diagram => string.Concat(Frets.Select(f => f < 0 ? "x" : f < 10 ? f.ToString() : $"({f})"));

        /// <summary>The chord as Lily# writes it: <c>&lt;c ees' g' bes'&gt;</c>.</summary>
        public string Chord => $"<{string.Join(" ", Notes)}>";
    }

    /// <summary>The answer: the voicings, or why there are none, and the instrument's strings.</summary>
    public sealed record Answer(IReadOnlyList<Voicing> Voicings, string? Problem, bool Bowed, IReadOnlyList<string> Strings);

    /// <summary>The widest a bowed double stop's two fingers lie apart, in semitones along the strings.</summary>
    public const int BowedReach = 5;

    /// <summary>The highest a bowed double stop climbs a string, in semitones (about the 5th position).</summary>
    public const int BowedTop = 12;

    /// <summary>
    /// The voicings of <paramref name="symbol"/> (<c>Cm7</c>, <c>G7/B</c>, <c>F#m7-5</c>) on
    /// <paramref name="instrument"/> (an instrument preset or a tuning word: <c>guitar</c>,
    /// <c>bass</c>, <c>dropD</c>, <c>ukulele</c>, <c>violin</c>, <c>cello</c> …), its notes within
    /// <paramref name="low"/>..<paramref name="high"/> (note names, <c>C3</c> or <c>c</c>; either may
    /// be null) first, at most <paramref name="count"/>.
    /// </summary>
    public static Answer Find(string symbol, string instrument, string? low, string? high, int count = 6)
    {
        string word = InstrumentWord(instrument);
        bool bowed = word is "violin" or "viola" or "cello" or "doubleBass";
        if (!Tunings.Names.Contains(word))
            return new Answer([], $"'{instrument}' is no string instrument Lily# knows the strings of: guitar, bass, violin, viola, "
                + $"cello, doubleBass, ukulele, or a tuning word ({string.Join(", ", Tunings.Names)})", false, []);
        var type = Tunings.Parse(word);
        var strings = Tunings.GetTuning(type);
        var names = strings.Select(p => NoteName(p, null)).ToList();
        if (!ChordStructure.TryParseChordEntry(symbol.Trim(), out var chord, out var refusal) || !ChordVoicings.TryTones(chord, out int allowed, out int required, out int bassPc))
            return new Answer([], refusal ?? $"'{symbol}' is not a chord symbol Lily# reads (Cm7, G7/B, F#m7-5, Bbmaj7 ...)", bowed, names);
        int? lo = low is { Length: > 0 } ? PitchOf(low) : null;
        int? hi = high is { Length: > 0 } ? PitchOf(high) : null;
        if (low is { Length: > 0 } && lo == null || high is { Length: > 0 } && hi == null)
            return new Answer([], $"a register is two note names, as 'C3' and 'G4' or 'c' and 'g'''", bowed, names);

        var spelled = Spellings(chord);
        List<int[]> shapes = bowed
            ? DoubleStops(strings, allowed, required, chord, bassPc)
            : [.. ChordVoicings.For(strings, chord, includeStretch: false).Bases.Select(b => b.ToArray())];
        var voicings = shapes.Select(frets =>
        {
            var pitches = frets.Select((f, s) => f < 0 ? -1 : strings[s] + f).Where(p => p >= 0).OrderBy(p => p).ToList();
            return new Voicing(frets, pitches, [.. pitches.Select(p => NoteName(p, spelled))]);
        }).ToList();
        // In the register first, in the list's own order (the commonest shapes first); then the
        // rest, the nearest to the register first.
        bool Inside(Voicing v) => (lo is not int l || v.Pitches[0] >= l) && (hi is not int h || v.Pitches[^1] <= h);
        int Distance(Voicing v) => System.Math.Max(lo is int l ? l - v.Pitches[0] : 0, 0) + System.Math.Max(hi is int h ? v.Pitches[^1] - h : 0, 0);
        var ordered = voicings.Select((v, i) => (v, i))
            .OrderBy(x => Inside(x.v) ? 0 : 1)
            .ThenBy(x => Inside(x.v) ? 0 : Distance(x.v))
            .ThenBy(x => x.i)
            .Select(x => x.v).ToList();
        if (ordered.Count == 0)
            return new Answer([], bowed
                ? "no double stop of its tones lies on two neighbouring strings within the hand's reach"
                : "no voicing of it within the diagrams' rules (every needed tone, the root lowest, four frets, four fingers)", bowed, names);
        string? outside = ordered.Any(Inside) ? null : "none lies wholly in the register asked: the nearest are given";
        return new Answer([.. ordered.Take(System.Math.Max(1, count))], outside, bowed, names);
    }

    /// <summary>The answer as the composing AI reads it: each voicing as Lily# writes the chord, with
    /// its diagram (fretted) or its strings (bowed), and how to read them.</summary>
    public static string Describe(string symbol, string instrument, string? low, string? high, int count = 6)
    {
        var answer = Find(symbol, instrument, low, high, count);
        if (answer.Voicings.Count == 0)
            return $"No voicing: {answer.Problem}.";
        string register = low is { Length: > 0 } || high is { Length: > 0 } ? $", in {low ?? "..."}-{high ?? "..."}" : "";
        var text = new System.Text.StringBuilder();
        text.Append($"{symbol} on the {instrument} (strings {string.Join(" ", answer.Strings)}){register} — ")
            .Append(answer.Bowed
                ? "a bowed string sounds two strings at once: double stops on neighbouring strings, the lowest positions first:\n"
                : "voicings a player can hold, the commonest shapes first:\n");
        for (int i = 0; i < answer.Voicings.Count; i++)
        {
            var v = answer.Voicings[i];
            string how = answer.Bowed
                ? string.Join(", ", v.Frets.Select((f, s) => (f, s)).Where(x => x.f >= 0)
                    .Select(x => $"{answer.Strings[x.s]} string {(x.f == 0 ? "open" : x.f == 1 ? "1 semitone up" : $"{x.f} semitones up")}"))
                : $"diagram {v.Diagram}, lowest string first, x not played";
            text.Append($"{i + 1}. {v.Chord} — {how}\n");
        }
        if (answer.Problem != null)
            text.Append($"({answer.Problem}.)\n");
        text.Append("The notes are as they sound, in absolute octaves (c' is middle C): write them so under `octave absolute`, or the "
            + "same notes in relative octaves.");
        if (InstrumentWord(instrument) is "bass" or "bass4" or "bass5" or "bass6" or "bassDropD" or "doubleBass")
            text.Append(" A bass part reads an octave above it sounds: without `pitch concert`, write each an octave higher.");
        return text.ToString();
    }

    /// <summary>The two-note stops of a chord on a bowed string's neighbouring strings: two tones of
    /// it, the third or the seventh among them (the colour of the chord), lowest positions first.</summary>
    private static List<int[]> DoubleStops(int[] strings, int allowed, int required, ChordStructure chord, int bassPc)
    {
        int rootPc = bassPc;
        var colour = chord.ToneSpecs.Where(t => t.DiatonicStep is 2 or 6).Select(t => (rootPc + t.Semitone) % 12).ToHashSet();
        var found = new List<(int[] Frets, int Cost)>();
        for (int s = 0; s + 1 < strings.Length; s++)
            for (int a = 0; a <= BowedTop; a++)
                for (int b = 0; b <= BowedTop; b++)
                {
                    int p = strings[s] + a, q = strings[s + 1] + b;
                    if (q <= p || (allowed >> (p % 12) & 1) == 0 || (allowed >> (q % 12) & 1) == 0 || p % 12 == q % 12)
                        continue;
                    // Open strings ring freely; the stopped notes are one hand's.
                    if (a > 0 && b > 0 && System.Math.Abs(a - b) > BowedReach)
                        continue;
                    if (colour.Count > 0 && !colour.Contains(p % 12) && !colour.Contains(q % 12))
                        continue;
                    var frets = Enumerable.Repeat(-1, strings.Length).ToArray();
                    frets[s] = a;
                    frets[s + 1] = b;
                    found.Add((frets, a + b));
                }
        return [.. found.OrderBy(f => f.Cost).Select(f => f.Frets)];
    }

    /// <summary>Each pitch class of the chord named as the chord spells it (letter and alteration).</summary>
    private static Dictionary<int, (int Step, int Alter)> Spellings(ChordStructure chord)
    {
        var names = new Dictionary<int, (int, int)>();
        int rootStep = ((chord.RootStep % 7) + 7) % 7;
        int rootPc = Natural(rootStep) + chord.RootAlter;
        void Add(int step, int pc)
        {
            step = ((step % 7) + 7) % 7;
            pc = ((pc % 12) + 12) % 12;
            int alter = ((pc - Natural(step)) % 12 + 18) % 12 - 6;
            names.TryAdd(pc, (step, alter));
        }
        foreach (var tone in chord.ToneSpecs)
            Add(rootStep + tone.DiatonicStep, rootPc + tone.Semitone);
        if (chord.BassStep is int bs)
            Add(bs, Natural(((bs % 7) + 7) % 7) + (chord.BassAlter ?? 0));
        return names;
    }

    private static int Natural(int step) => step switch { 0 => 0, 1 => 2, 2 => 4, 3 => 5, 4 => 7, 5 => 9, _ => 11 };

    /// <summary>A pitch in Lily#'s absolute octaves (<c>c'</c> = C4), as <paramref name="spelled"/> names
    /// its class, else with a sharp or a flat as the common name.</summary>
    internal static string NoteName(int midi, IReadOnlyDictionary<int, (int Step, int Alter)>? spelled)
    {
        int pc = ((midi % 12) + 12) % 12;
        var (step, alter) = spelled != null && spelled.TryGetValue(pc, out var s) ? s
            : pc switch { 1 => (0, 1), 3 => (2, -1), 6 => (3, 1), 8 => (5, -1), 10 => (6, -1), _ => (Enumerable.Range(0, 7).First(i => Natural(i) == pc), 0) };
        // The octave of the LETTER: B-sharp 3 sounds C4, C-flat 4 sounds B3.
        int octave = (midi - Natural(step) - alter) / 12 - 1;
        string name = "cdefgab"[step] + alter switch { -2 => "eses", -1 => "es", 1 => "is", 2 => "isis", _ => "" };
        return name + (octave >= 4 ? new string('\'', octave - 3) : new string(',', 3 - octave));
    }

    /// <summary>A note name as a pitch: <c>C3</c>, <c>Eb4</c>, <c>F#2</c> (scientific), or Lily#'s
    /// absolute <c>c</c>, <c>ees'</c>, <c>fis,</c>; null for neither.</summary>
    internal static int? PitchOf(string name)
    {
        name = name.Trim();
        var sci = System.Text.RegularExpressions.Regex.Match(name, @"^([A-Ga-g])(#|b|♯|♭|x|bb)?(-?\d)$");
        if (sci.Success)
        {
            int step = "cdefgab".IndexOf(char.ToLowerInvariant(sci.Groups[1].Value[0]));
            int alter = sci.Groups[2].Value switch { "#" or "♯" => 1, "x" => 2, "b" or "♭" => -1, "bb" => -2, _ => 0 };
            return (int.Parse(sci.Groups[3].Value) + 1) * 12 + Natural(step) + alter;
        }
        var lys = System.Text.RegularExpressions.Regex.Match(name, @"^([a-g])((?:is|es|s)*)('*|,*)$");
        if (!lys.Success)
            return null;
        int st = "cdefgab".IndexOf(lys.Groups[1].Value[0]);
        string acc = lys.Groups[2].Value;
        int alt = System.Text.RegularExpressions.Regex.Matches(acc, "is").Count - System.Text.RegularExpressions.Regex.Matches(acc.Replace("is", ""), "e?s").Count;
        string marks = lys.Groups[3].Value;
        int oct = 3 + (marks.StartsWith('\'') ? marks.Length : -marks.Length);
        return (oct + 1) * 12 + Natural(st) + alt;
    }

    /// <summary>An instrument as the tuning word it plays: a preset's (<c>electric guitar</c> is the
    /// guitar), else the word itself.</summary>
    private static string InstrumentWord(string instrument)
    {
        string w = instrument.Trim().ToLowerInvariant().Replace(" ", "").Replace("-", "");
        return w switch
        {
            "" or "guitar" or "electricguitar" or "acousticguitar" or "classicalguitar" => "guitar",
            "bass" or "electricbass" or "bassguitar" => "bass",
            "doublebass" or "contrabass" => "doubleBass",
            "dropd" => "guitarDropD",
            _ => Tunings.Names.FirstOrDefault(n => n.Equals(instrument.Trim(), System.StringComparison.OrdinalIgnoreCase)) ?? w,
        };
    }
}
