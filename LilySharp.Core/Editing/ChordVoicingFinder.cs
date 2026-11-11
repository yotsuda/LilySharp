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
        /// <summary>The frets as a chord diagram writes them, lowest string first: <c>x35343</c>, and
        /// with a fret of two digits <c>xx-12-10-x-10</c> — the spelling <c>@diagram( )</c> reads
        /// (<see cref="ChordVoicings.Spell"/>; owner's correction 2026-11-11: the answer wrote
        /// <c>xx(12)(10)x(10)</c>, which nothing reads).</summary>
        public string Diagram => ChordVoicings.Spell(Frets);

        /// <summary>The chord as Lily# writes it: <c>&lt;c ees' g' bes'&gt;</c>.</summary>
        public string Chord => $"<{string.Join(" ", Notes)}>";
    }

    /// <summary>The answer: the voicings, or why there are none, and the instrument's strings;
    /// whether the sound asked for distorts, and whether the chord has power chords (two or three
    /// notes) that then lead the list.</summary>
    public sealed record Answer(IReadOnlyList<Voicing> Voicings, string? Problem, bool Bowed, IReadOnlyList<string> Strings,
        bool Distorted = false, bool Powered = false);

    /// <summary>A sound that distorts — an overdriven or distortion guitar, a fuzz — on which a full
    /// chord blurs and two or three notes ring clear (owner's report 2026-11-01: an overdriven guitar
    /// struck `&lt;e, e b e' b'&gt;`, this tool's first E5 shape, from start to end).</summary>
    public static bool IsDistorted(string? sound)
        => sound != null && System.Text.RegularExpressions.Regex.IsMatch(sound, "overdriv|distort|fuzz",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>The widest a bowed double stop's two fingers lie apart, in semitones along the strings.</summary>
    public const int BowedReach = 5;

    /// <summary>The highest a bowed double stop climbs a string, in semitones (about the 5th position).</summary>
    public const int BowedTop = 12;

    /// <summary>
    /// The voicings of <paramref name="symbol"/> (<c>Cm7</c>, <c>G7/B</c>, <c>F#m7-5</c>) on
    /// <paramref name="instrument"/> (an instrument preset or a tuning word: <c>guitar</c>,
    /// <c>bass</c>, <c>dropD</c>, <c>ukulele</c>, <c>violin</c>, <c>cello</c> …), its notes within
    /// <paramref name="low"/>..<paramref name="high"/> (note names, <c>C3</c> or <c>c</c>; either may
    /// be null) first, at most <paramref name="count"/>. On a distorted <paramref name="sound"/> (the
    /// part's <c>midiInstrument</c>) the chord's power chords — the root and its fifth, the octave
    /// above — lead the list, the full shapes after them. On <paramref name="onStrings"/> (a fretted
    /// instrument's strings by number, 1 the highest: <c>1-4</c>, <c>1,2,3</c>) only shapes on those
    /// strings are answered, and the root need not be lowest, as a band's bass plays it (owner's
    /// request 2026-11-10: a band's clean guitar on the upper strings; the diagrams' list, its root
    /// lowest and its shapes maximal, had no A minor on the top four strings).
    /// </summary>
    /// <param name="omitRoot">A fretted instrument's shapes WITHOUT the chord's root, which the
    /// band's bass plays: the third, the fifth and the tones above it only (owner's request
    /// 2026-11-11: "high chords with the root left out" took the composing chat four minutes of
    /// sifting six rooted shapes a chord by hand).</param>
    /// <param name="fromFret">The lowest fret a fretted instrument's shape may press, no open
    /// string then: 5 answers the shapes from the fifth position up. 0 for any.</param>
    public static Answer Find(string symbol, string instrument, string? low, string? high, int count = 6, string? sound = null,
        string? onStrings = null, bool omitRoot = false, int fromFret = 0)
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
        bool[]? use = null;
        if (onStrings is { Length: > 0 })
        {
            if (bowed)
                return new Answer([], "strings picks a fretted instrument's strings; a bowed string's double stops lie on two neighbouring strings already", bowed, names);
            if (StringsNamed(onStrings, strings.Length) is not { } picked)
                return new Answer([], $"strings are numbered from 1, the highest, to {strings.Length}, the lowest, as \"1-4\" or \"1,2,3\"", bowed, names);
            if (picked.Contains(false))
                use = picked;
        }

        var spelled = Spellings(chord);
        bool distorted = !bowed && IsDistorted(sound);
        // Without the root, or from a fret up, the shapes are searched on the strings (all of
        // them where none are named): the diagrams' list holds the root and starts at the nut.
        if (!bowed && (omitRoot || fromFret > 0))
            use ??= [.. Enumerable.Repeat(true, strings.Length)];
        // A power chord IS its root and fifth: none without the root.
        List<int[]> power = distorted && !omitRoot ? PowerChords(strings, chord, bassPc) : [];
        if (fromFret > 0)
            power = [.. power.Where(p => p.All(f => f < 0 || f >= fromFret))];
        List<int[]> shapes = bowed
            ? DoubleStops(strings, allowed, required, chord, bassPc)
            : use != null
            // The shapes on neighbouring strings first: one with a string muted BETWEEN its sounding
            // strings (xx-12-10-x-10) is strummed through a string a finger has to damp, and is no
            // common grip on the upper strings (owner's question 2026-11-11) — it stays, after the others.
            ? [.. power.Where(p => p.Select((f, s) => f < 0 || use[s]).All(on => on)),
                .. OnStrings(strings, use, chord, allowed, required, bassPc, omitRoot, fromFret).OrderBy(fr => MutedBetween(fr) ? 1 : 0)]
            : [.. power, .. ChordVoicings.For(strings, chord, includeStretch: false).Bases.Select(b => b.ToArray())];
        var voicings = shapes.Select(frets =>
        {
            var pitches = frets.Select((f, s) => f < 0 ? -1 : strings[s] + f).Where(p => p >= 0).OrderBy(p => p).ToList();
            return new Voicing(frets, pitches, [.. pitches.Select(p => NoteName(p, spelled))]);
        })
        // No two strings on one pitch (owner's request 2026-11-01: a composed guitar part held
        // `<g, d g g d'>` — the diagrams' G shape 35503x, its fourth string at the fifth fret and
        // its open third string both G3 — and the doubled note reads as a mistake on the page). A
        // diagram may double a note; a chord written in notes should not, so the tool leaves such
        // shapes out and the AI never sees them.
        .Where(v => v.Pitches.Distinct().Count() == v.Pitches.Count).ToList();
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
                : omitRoot || fromFret > 0
                ? "no shape of it there that a hand can hold (four frets, four fingers, no pitch on two strings)"
                  + (omitRoot ? " without its root" : "") + (fromFret > 0 ? $" from fret {fromFret} up" : "")
                : "no voicing of it within the diagrams' rules (every needed tone, the root lowest, four frets, four fingers) that "
                  + "sounds no pitch on two strings", bowed, names);
        string? outside = ordered.Any(Inside) ? null : "none lies wholly in the register asked: the nearest are given";
        return new Answer([.. ordered.Take(System.Math.Max(1, count))], outside, bowed, names, distorted, power.Count > 0);
    }

    /// <summary>Whether a shape mutes a string between two it sounds.</summary>
    internal static bool MutedBetween(IReadOnlyList<int> frets)
    {
        int first = -1, last = -1;
        for (int s = 0; s < frets.Count; s++)
            if (frets[s] >= 0)
            {
                if (first < 0)
                    first = s;
                last = s;
            }
        for (int s = first + 1; s < last; s++)
            if (frets[s] < 0)
                return true;
        return false;
    }

    /// <summary>The answer as the composing AI reads it: each voicing as Lily# writes the chord, with
    /// its diagram (fretted) or its strings (bowed), and how to read them.</summary>
    public static string Describe(string symbol, string instrument, string? low, string? high, int count = 6, string? sound = null,
        string? onStrings = null, bool omitRoot = false, int fromFret = 0)
    {
        var answer = Find(symbol, instrument, low, high, count, sound, onStrings, omitRoot, fromFret);
        if (answer.Voicings.Count == 0)
            return $"No voicing: {answer.Problem}.";
        string register = low is { Length: > 0 } || high is { Length: > 0 } ? $", in {low ?? "..."}-{high ?? "..."}" : "";
        if (onStrings is { Length: > 0 })
            register += $", on strings {onStrings}";
        if (fromFret > 0 && !answer.Bowed)
            register += $", from fret {fromFret} up";
        bool rootless = omitRoot && !answer.Bowed;
        bool searched = !answer.Bowed && (onStrings is { Length: > 0 } || omitRoot || fromFret > 0);
        var text = new System.Text.StringBuilder();
        // The tones the symbol was read as stand first: `Bbmaj` is B-flat major SEVENTH here, and
        // an answer that did not say so was asked again as `Bb` (the same report).
        text.Append($"{symbol} — its tones {TonesOf(symbol)}{(rootless ? ", the root left to the bass" : "")} — on the {instrument} (strings {string.Join(" ", answer.Strings)}){register} — ")
            .Append(answer.Bowed
                ? "a bowed string sounds two strings at once: double stops on neighbouring strings, the lowest positions first:\n"
                : answer.Distorted && answer.Powered
                ? $"on a distorted sound ({sound}) a full chord blurs and two or three notes ring clear: its power chords first, "
                  + "the root and its fifth with the octave above, lowest first, then the full shapes:\n"
                : answer.Distorted
                ? $"on a distorted sound ({sound}) a full chord blurs, but this chord has no perfect fifth over its root, so no power "
                  + "chord: these are its full shapes — strike two or three of their notes, or leave the chord to a clean sound:\n"
                : rootless
                ? "voicings a player can hold without the root, the lowest positions first:\n"
                : searched
                ? "voicings a player can hold there, the lowest positions first; the root need not be lowest, as a band's bass plays it:\n"
                : "voicings a player can hold, the commonest shapes first:\n");
        for (int i = 0; i < answer.Voicings.Count; i++)
        {
            var v = answer.Voicings[i];
            string how = answer.Bowed
                ? string.Join(", ", v.Frets.Select((f, s) => (f, s)).Where(x => x.f >= 0)
                    .Select(x => $"{answer.Strings[x.s]} string {(x.f == 0 ? "open" : x.f == 1 ? "1 semitone up" : $"{x.f} semitones up")}"))
                : $"diagram({v.Diagram})";
            text.Append($"{i + 1}. {v.Chord} — {how}{(!answer.Bowed && MutedBetween(v.Frets) ? " (a string muted between the sounding ones: harder to strum)" : "")}\n");
        }
        if (answer.Problem != null)
            text.Append($"({answer.Problem}.)\n");
        if (!answer.Bowed)
            text.Append("A diagram is its frets, lowest string first, x not played, as `@diagram( )` writes it. ");
        text.Append("The notes are as they sound, in absolute octaves (c' is middle C): write them so under `octave absolute`, or the "
            + "same notes in relative octaves.");
        if (InstrumentWord(instrument) is "bass" or "bass4" or "bass5" or "bass6" or "bassDropD" or "doubleBass")
            text.Append(" A bass part reads an octave above it sounds: without `pitch concert`, write each an octave higher.");
        return text.ToString();
    }

    /// <summary>The chord's tones as Lily# names them, root first, with no octave: <c>bes d f a</c>
    /// for <c>Bbmaj</c> — what the symbol was read as. Empty for a symbol not read.</summary>
    internal static string TonesOf(string symbol)
    {
        if (!ChordStructure.TryParseChordEntry(symbol.Trim(), out var chord, out _))
            return "";
        var spelled = Spellings(chord);
        int rootPc = Natural(((chord.RootStep % 7) + 7) % 7) + chord.RootAlter;
        return string.Join(" ", chord.ToneSpecs
            .Select(t => NoteName(60 + ((rootPc + t.Semitone) % 12 + 12) % 12, spelled).TrimEnd('\'', ','))
            .Distinct());
    }

    /// <summary>
    /// One voicing for each chord of a progression, chosen so that the hand moves little from one
    /// to the next: among each chord's voicings (<see cref="Find"/>, the same options) the path
    /// whose top and bottom notes travel least, the earlier shapes of each list preferred.
    /// </summary>
    /// <remarks>
    /// LILYSHARP-OWN (owner's request 2026-11-11): the composing chat asked eight chords one by
    /// one and chose among forty-eight shapes by hand. The cost of a step is the top notes'
    /// distance, half the bottom notes', one for each note more or fewer and one for each fret the
    /// its own list adds three quarters of a semitone, so that among equal paths the commoner,
    /// lower shape stands.
    /// </remarks>
    public static string DescribeProgression(IReadOnlyList<string> symbols, string instrument, string? low, string? high,
        string? sound = null, string? onStrings = null, bool omitRoot = false, int fromFret = 0)
    {
        const int Candidates = 12;
        static int PositionOf(Voicing v) => v.Frets.Where(f => f > 0).DefaultIfEmpty(0).Min();
        var answers = symbols.Select(s => Find(s, instrument, low, high, Candidates, sound, onStrings, omitRoot, fromFret)).ToList();
        var text = new System.Text.StringBuilder();
        string where = (onStrings is { Length: > 0 } ? $", on strings {onStrings}" : "")
            + (fromFret > 0 ? $", from fret {fromFret} up" : "") + (omitRoot ? ", the root left to the bass" : "");
        text.Append($"{symbols.Count} chords on the {instrument}{where} — one voicing each, chosen so that the hand moves "
            + "little from chord to chord:\n");
        // The cheapest path over the chords that have voicings; one without any breaks the path.
        var best = new double[answers.Count][];
        var from = new int[answers.Count][];
        int previous = -1;
        for (int i = 0; i < answers.Count; i++)
        {
            int n = answers[i].Voicings.Count;
            best[i] = new double[n];
            from[i] = new int[n];
            for (int k = 0; k < n; k++)
            {
                var v = answers[i].Voicings[k];
                // A string muted between sounding ones costs as much as a long shift of the hand.
                double own = 0.75 * k + (MutedBetween(v.Frets) ? 8 : 0);
                best[i][k] = own;
                from[i][k] = -1;
                if (previous < 0)
                    continue;
                double least = double.MaxValue;
                for (int j = 0; j < answers[previous].Voicings.Count; j++)
                {
                    var u = answers[previous].Voicings[j];
                    double step = System.Math.Abs(u.Pitches[^1] - v.Pitches[^1]) + 0.5 * System.Math.Abs(u.Pitches[0] - v.Pitches[0])
                        + System.Math.Abs(u.Pitches.Count - v.Pitches.Count) + System.Math.Abs(PositionOf(u) - PositionOf(v));
                    if (best[previous][j] + step < least)
                    {
                        least = best[previous][j] + step;
                        from[i][k] = j;
                    }
                }
                best[i][k] = own + least;
            }
            if (n > 0)
                previous = i;
        }
        var chosen = new int[answers.Count];
        System.Array.Fill(chosen, -1);
        int at = -1;
        for (int i = answers.Count - 1; i >= 0; i--)
        {
            if (answers[i].Voicings.Count == 0)
                continue;
            chosen[i] = at >= 0 ? at : System.Array.IndexOf(best[i], best[i].Min());
            at = from[i][chosen[i]];
        }
        for (int i = 0; i < answers.Count; i++)
        {
            string tones = TonesOf(symbols[i]);
            if (chosen[i] < 0)
            {
                text.Append($"{i + 1}. {symbols[i]} — no voicing: {answers[i].Problem}\n");
                continue;
            }
            var v = answers[i].Voicings[chosen[i]];
            string how = answers[i].Bowed
                ? string.Join(", ", v.Frets.Select((f, s) => (f, s)).Where(x => x.f >= 0)
                    .Select(x => $"{answers[i].Strings[x.s]} string {(x.f == 0 ? "open" : $"{x.f} up")}"))
                : $"diagram({v.Diagram})";
            text.Append($"{i + 1}. {symbols[i]} ({tones}) — {v.Chord} — {how}\n");
        }
        text.Append("A diagram is its frets, lowest string first, x not played, as `@diagram( )` writes it. The notes are as they sound, in absolute octaves (c' is middle C): "
            + "write them so under `octave absolute`. For a chord's other voicings, ask it alone (`chord`).");
        if (InstrumentWord(instrument) is "bass" or "bass4" or "bass5" or "bass6" or "bassDropD" or "doubleBass")
            text.Append(" A bass part reads an octave above it sounds: without `pitch concert`, write each an octave higher.");
        return text.ToString();
    }

    /// <summary>The strings <paramref name="text"/> names (<c>1-4</c>, <c>1,2,3</c>; 1 is the highest),
    /// lowest string first as a tuning lists them; null when it names none, or one the instrument has not.</summary>
    private static bool[]? StringsNamed(string text, int count)
    {
        var use = new bool[count];
        foreach (var part in text.Split(',', System.StringSplitOptions.TrimEntries | System.StringSplitOptions.RemoveEmptyEntries))
        {
            var ends = part.Split('-', System.StringSplitOptions.TrimEntries);
            if (ends.Length > 2 || !int.TryParse(ends[0], out int a) || !int.TryParse(ends[^1], out int b))
                return null;
            if (a > b)
                (a, b) = (b, a);
            if (a < 1 || b > count)
                return null;
            for (int n = a; n <= b; n++)
                use[count - n] = true;
        }
        return use.Contains(true) ? use : null;
    }

    /// <summary>The shapes of a chord on the strings <paramref name="use"/> picks: the diagrams' rules but the
    /// root lowest (a slash chord's bass still is), each shape maximal on those strings — a muted one of them
    /// takes no fret it could — lowest position first, then the fewest fingers, the root lowest, the frets.
    /// The editor's step offers those of a guitar's four highest strings too (<see cref="NoteStepper.ShapeOrder"/>).</summary>
    /// <param name="rootless">The chord's root is on no string (the bass plays it), and a slash
    /// chord's bass need not be lowest either.</param>
    /// <param name="fromFret">No fret below it, and no open string, when above 0.</param>
    internal static List<int[]> OnStrings(int[] tuning, bool[] use, ChordStructure chord, int allowed, int required, int bassPc,
        bool rootless = false, int fromFret = 0)
    {
        int rootPc = ((Natural(((chord.RootStep % 7) + 7) % 7) + chord.RootAlter) % 12 + 12) % 12;
        bool slash = bassPc != rootPc && !rootless;
        if (rootless)
        {
            allowed &= ~(1 << rootPc);
            required &= ~(1 << rootPc);
        }
        var chosen = Enumerable.Range(0, tuning.Length).Where(s => use[s]).ToList();
        var valid = new List<int[]>();
        var frets = Enumerable.Repeat(-1, tuning.Length).ToArray();
        void Walk(int k)
        {
            if (k == chosen.Count)
            {
                if (Valid(frets))
                    valid.Add((int[])frets.Clone());
                return;
            }
            int s = chosen[k];
            frets[s] = -1;
            Walk(k + 1);
            for (int f = fromFret; f <= ChordVoicings.MaxFret; f++)
            {
                if ((allowed & (1 << Pc(tuning[s] + f))) == 0)
                    continue;
                frets[s] = f;
                if (Spanned(frets))
                    Walk(k + 1);
            }
            frets[s] = -1;
        }
        Walk(0);
        var keys = valid.Select(Key).ToHashSet();
        bool Maximal(int[] fr) => !chosen.Any(s => fr[s] < 0 && Enumerable.Range(fromFret, ChordVoicings.MaxFret + 1 - fromFret).Any(f =>
        {
            var more = (int[])fr.Clone();
            more[s] = f;
            // Without the root, a shape on neighbouring strings stands though a note more could be
            // had across a muted string: two tones side by side are the commoner grip there.
            return keys.Contains(Key(more)) && (!rootless || !MutedBetween(more) || MutedBetween(fr));
        }));
        bool RootLowest(int[] fr) => Pc(fr.Select((f, s) => f < 0 ? int.MaxValue : tuning[s] + f).Min()) == rootPc;
        return valid.Where(Maximal)
            .OrderBy(fr => ChordVoicings.Position(fr)).ThenBy(fr => ChordVoicings.Fingers(fr)).ThenBy(fr => RootLowest(fr) ? 0 : 1)
            .ThenBy(fr => string.Concat(fr.Select(f => (f + 1).ToString("D2"))), System.StringComparer.Ordinal)
            .ToList();

        bool Valid(int[] fr)
        {
            var pitches = fr.Select((f, s) => f < 0 ? -1 : tuning[s] + f).Where(p => p >= 0).OrderBy(p => p).ToList();
            // Without its root a triad is two tones: two notes are a shape then, where no third lies in reach.
            if (pitches.Count < (rootless ? 2 : ChordVoicings.MinSounding) || pitches.Distinct().Count() != pitches.Count)
                return false;
            int sounding = 0;
            foreach (int p in pitches)
                sounding |= 1 << Pc(p);
            // …and they are two TONES of it, not one tone and its octave.
            if (rootless && System.Numerics.BitOperations.PopCount((uint)sounding) < 2)
                return false;
            return (sounding & required) == required && (!slash || Pc(pitches[0]) == bassPc) && ChordVoicings.Fingers(fr) <= ChordVoicings.MaxFingers;
        }
        static bool Spanned(int[] fr)
        {
            int lowest = int.MaxValue, highest = -1;
            foreach (int f in fr)
                if (f > 0)
                {
                    lowest = System.Math.Min(lowest, f);
                    highest = System.Math.Max(highest, f);
                }
            return highest < 0 || highest - lowest <= ChordVoicings.NormalSpan;
        }
        static string Key(int[] fr) => string.Join(",", fr);
        static int Pc(int p) => (p % 12 + 12) % 12;
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

    /// <summary>The power chords of a chord with a perfect fifth over its root and no other bass: the
    /// root and its fifth on neighbouring strings, and those with the octave on the string above —
    /// the deepest root first, then the lowest position, the two notes before the three. None for a
    /// chord without one (dim, aug) or over another bass.</summary>
    private static List<int[]> PowerChords(int[] strings, ChordStructure chord, int bassPc)
    {
        int rootPc = ((Natural(((chord.RootStep % 7) + 7) % 7) + chord.RootAlter) % 12 + 12) % 12;
        if (bassPc != rootPc || !chord.ToneSpecs.Any(ChordVoicings.IsPerfectFifth))
            return [];
        // Fretted frets no further apart than the normal rule's span; open strings lie anywhere.
        static bool Near(int a, int b) => a == 0 || b == 0 || System.Math.Abs(a - b) <= ChordVoicings.NormalSpan;
        static int Position(int[] frets) => frets.Where(f => f > 0).DefaultIfEmpty(0).Min();
        var found = new List<(int[] Frets, int Root, int Position, int Notes)>();
        for (int s = 0; s + 1 < strings.Length; s++)
            for (int f = 0; f <= ChordVoicings.MaxFret; f++)
            {
                int root = strings[s] + f;
                int g = root + 7 - strings[s + 1];
                if (((root % 12) + 12) % 12 != rootPc || g < 0 || g > ChordVoicings.MaxFret || !Near(f, g))
                    continue;
                var two = Enumerable.Repeat(-1, strings.Length).ToArray();
                two[s] = f;
                two[s + 1] = g;
                found.Add((two, root, Position(two), 2));
                if (s + 2 >= strings.Length)
                    continue;
                int h = root + 12 - strings[s + 2];
                if (h < 0 || h > ChordVoicings.MaxFret || !Near(f, h) || !Near(g, h))
                    continue;
                var three = (int[])two.Clone();
                three[s + 2] = h;
                found.Add((three, root, Position(three), 3));
            }
        return [.. found.OrderBy(x => x.Root).ThenBy(x => x.Position).ThenBy(x => x.Notes).Select(x => x.Frets)];
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
