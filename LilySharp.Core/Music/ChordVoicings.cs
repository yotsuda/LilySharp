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

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace LilySharp.Core.Music;

/// <summary>
/// The ordered BASE VOICINGS of a chord on a fretted tuning — Lily#'s own list of a chord's
/// shapes. Owner's rules of 2026-09-27; since 2026-09-28 (HANDOFF §2 K0) the order is NOT
/// part of the language: a source writes a shape or nothing, never a place in this list. It
/// serves the chords LilyPond's predefined table lacks (<see cref="ChordShapes.Default"/>'s
/// fallback — the shape the editor's step writes first), the editor's shape stepping and the
/// hover.
/// </summary>
/// <remarks>
/// <para>
/// LILYSHARP-OWN, all of it: LilyPond has no voicing catalogue. Its predefined fret diagrams
/// (<c>\storePredefinedDiagram</c>, <see cref="PredefinedFretboards"/>) are a hand-written
/// table keyed by chord, one shape each; Lily# lists shapes by rule for every chord it can
/// name. Changing a rule moves only the diagrams of chords drawn from the fallback.
/// </para>
/// <para>
/// A shape gives each string a fret or mutes it. A shape is VALID when
/// <list type="bullet">
/// <item>V1 at least three strings sound, and every fret is 0..15;</item>
/// <item>V2 only chord tones sound (for X/Y, X's tones plus Y);</item>
/// <item>V3 every REQUIRED tone sounds — all of the chord's tones but the perfect fifth
///   (<c>DiatonicStep</c> 4, <c>Semitone</c> 7), which may be left out; an altered fifth is
///   required; for X/Y, Y is required;</item>
/// <item>V4 the LOWEST-PITCHED sounding note is the root (for X/Y, Y);</item>
/// <item>V5 among the fretted strings (fret &gt; 0) the highest fret less the lowest is at most
///   <see cref="NormalSpan"/> (3: four frets) — or, with STRETCH shapes allowed,
///   <see cref="StretchSpan"/> (4: five frets); open strings do not count;</item>
/// <item>V6 at most four fingers: one per fretted string, except that when the lowest fretted
///   fret is used on two or more strings and every string from its first use to its last is
///   fretted at or above it (none open or muted), those strings are ONE finger (a barre).</item>
/// </list>
/// A BASE is a MAXIMAL valid shape: no muted string of it can be given any fret 0..15 with the
/// result still valid. Every valid shape is then some base with strings muted — which is why
/// the owner chose maximality: the earlier "every string from the bass up sounds" rule missed
/// playable shapes such as Cm7's <c>x3x546</c>.
/// </para>
/// <para>
/// ORDER (index 0 first): ⒜ POSITION — the lowest fretted fret, 0 when nothing is fretted —
/// ascending; ⒝ FINGER COUNT (V6's count) ascending; ⒞ the frets as a tuple from the LOWEST
/// string to the highest, a muted string −1, lexicographic ascending.
/// </para>
/// <para>
/// TWO RULE SETS (owner's decision 2026-09-28): a STRETCH shape — fretted frets five apart,
/// highest − lowest = 4 — is hard to play, so by default it is not offered. The NORMAL rule
/// (V5 at 3) is what the compiler's fallback reads first and what the editor steps through; the
/// STRETCH-INCLUSIVE rule (V5 at 4, the owner's prototype) is the editor's when
/// <c>lilysharp.chordShapes.includeStretch</c> is on, and the fallback's only when the normal
/// rule finds nothing (<see cref="Fallback(IReadOnlyList{int}, ChordStructure)"/>). Each rule's
/// bases are MAXIMAL WITHIN ITS OWN valid set: a base of the normal rule can be a muted copy
/// of a stretch shape, so the normal bases are not the stretch-inclusive bases filtered.
/// </para>
/// <para>
/// Reference numbers (standard tuning), reproduced from the owner's prototype
/// (<c>maximal.py</c>) and pinned by <c>ChordVoicingTests</c> — the stretch-inclusive rule:
/// C 267 valid / 57 bases, Cm7 126 / 52, G7 321 / 89, D 255 / 64, F♯m7♭5 105 / 45, C9 156 / 68.
/// The normal rule's are pinned there too.
/// </para>
/// <para>
/// ⚠️ ON A KEYSTROKE PATH: the validator and the page both ask. The walk is a backtrack string
/// by string, pruned by the allowed pitch classes and by the span; maximality is a hash lookup
/// per muted string per fret. One (tuning, chord) is computed once per process
/// (<see cref="Cache"/>) — the answer depends on nothing else.
/// </para>
/// </remarks>
public static class ChordVoicings
{
    /// <summary>V1: the fewest strings that must sound.</summary>
    public const int MinSounding = 3;

    /// <summary>V1: the highest fret a voicing may use.</summary>
    public const int MaxFret = 15;

    /// <summary>V5, the normal rule: the widest span of fretted frets (highest − lowest) — four
    /// frets.</summary>
    public const int NormalSpan = 3;

    /// <summary>V5 with stretch shapes allowed — five frets (the owner's prototype).</summary>
    public const int StretchSpan = 4;

    /// <summary>V6: the fingers a voicing may use.</summary>
    public const int MaxFingers = 4;

    /// <summary>The bases of one chord on one tuning, in the order, and how many VALID
    /// shapes they were chosen from (the count the tests pin).</summary>
    public sealed record VoicingSet(ImmutableArray<ImmutableArray<int>> Bases, int ValidCount);

    private static readonly ConcurrentDictionary<(string Tuning, int Allowed, int Required, int Bass, int Span), VoicingSet>
        Cache = new();

    /// <summary>V5's span for a rule set: <see cref="StretchSpan"/> with stretch shapes,
    /// else <see cref="NormalSpan"/>.</summary>
    public static int SpanOf(bool includeStretch) => includeStretch ? StretchSpan : NormalSpan;

    /// <summary>A STRETCH shape: its fretted frets lie further apart than the normal rule
    /// allows (highest − lowest &gt; <see cref="NormalSpan"/>).</summary>
    public static bool IsStretch(IReadOnlyList<int> frets)
    {
        int lo = int.MaxValue, hi = int.MinValue;
        foreach (int f in frets)
            if (f > 0)
            {
                lo = System.Math.Min(lo, f);
                hi = System.Math.Max(hi, f);
            }
        return hi - lo > NormalSpan;
    }

    /// <summary>
    /// Whether Lily# lists shapes on a tuning at all: its open strings rise STRICTLY from the lowest
    /// string to the highest (a guitar in standard, drop or open tuning, a seven-string, a bass).
    /// A re-entrant tuning — a ukulele's G4 C4 E4 A4, a banjo's drone — does not qualify.
    /// </summary>
    /// <remarks>
    /// Owner's decision (2026-09-27): the rules were written for, and checked on, the
    /// guitar. On a re-entrant tuning "the lowest string" is not the lowest note, and the rules
    /// would number shapes a player of that instrument would not recognise; the position-string
    /// form still works there.
    /// </remarks>
    public static bool IsGuitarType(IReadOnlyList<int> tuning)
    {
        for (int i = 1; i < tuning.Count; i++)
            if (tuning[i] <= tuning[i - 1])
                return false;
        return tuning.Count > 0;
    }

    /// <summary>
    /// The pitch classes a chord may sound (V2), must sound (V3), and must have lowest (V4), as
    /// 12-bit masks and a pitch class; false for a chord with no registered tone set.
    /// </summary>
    public static bool TryTones(ChordStructure chord, out int allowed, out int required, out int bassPc)
    {
        allowed = required = 0;
        bassPc = 0;
        if (chord.RawSuffix != null)
            return false;
        int rootPc = Mod12(Semantics.RelativeOctave.StepSemitoneOf(Mod7(chord.RootStep)) + chord.RootAlter);
        foreach (var tone in ChordQualityRegistry.GetTones(chord.Quality))
        {
            int bit = 1 << Mod12(rootPc + tone.Semitone);
            allowed |= bit;
            // V3: the PERFECT fifth alone may be left out. An altered fifth (dim, aug, m7-5,
            // 7-5, 7+5) has another semitone and stays required.
            if (!IsPerfectFifth(tone))
                required |= bit;
        }
        bassPc = rootPc;
        if (chord.BassStep is int bs)
        {
            bassPc = Mod12(Semantics.RelativeOctave.StepSemitoneOf(Mod7(bs)) + (chord.BassAlter ?? 0));
            allowed |= 1 << bassPc;
            required |= 1 << bassPc;
        }
        return true;
    }

    /// <summary>V3's one exemption: the tone at diatonic step 4, seven semitones up.</summary>
    public static bool IsPerfectFifth(ChordToneSpec tone) => tone.DiatonicStep == 4 && tone.Semitone == 7;

    /// <summary>The bases of <paramref name="chord"/> on <paramref name="tuning"/> (open
    /// strings as MIDI numbers, LOW string first) under the normal rule or, with
    /// <paramref name="includeStretch"/>, the stretch-inclusive one, in the order; empty for a
    /// chord with no tone set.</summary>
    public static VoicingSet For(IReadOnlyList<int> tuning, ChordStructure chord, bool includeStretch)
        => TryTones(chord, out int allowed, out int required, out int bass)
            ? For(tuning, allowed, required, bass, includeStretch)
            : new VoicingSet([], 0);

    /// <summary>The same, from the masks <see cref="TryTones"/> gives.</summary>
    public static VoicingSet For(IReadOnlyList<int> tuning, int allowed, int required, int bassPc,
        bool includeStretch)
    {
        int span = SpanOf(includeStretch);
        var key = (TuningKey(tuning), allowed, required, bassPc, span);
        return Cache.GetOrAdd(key, _ => Enumerate(tuning, allowed, required, bassPc, span));
    }

    /// <summary>
    /// The default shape of a chord LilyPond's table lacks (<see cref="ChordShapes.Default"/>,
    /// what the editor's step writes first): the first of the NORMAL order; only when the normal
    /// rule finds no shape, the first of the stretch-inclusive order; null when neither does.
    /// </summary>
    /// <remarks>Owner's decision (2026-09-28): stretch shapes are hard to play, so the default
    /// prefers a shape without one — a chord whose every shape stretches still gets one.</remarks>
    public static ImmutableArray<int>? Fallback(IReadOnlyList<int> tuning, ChordStructure chord)
        => TryTones(chord, out int allowed, out int required, out int bass)
            ? Fallback(tuning, allowed, required, bass)
            : null;

    /// <summary>The same, from the masks <see cref="TryTones"/> gives.</summary>
    public static ImmutableArray<int>? Fallback(IReadOnlyList<int> tuning, int allowed, int required, int bassPc)
    {
        if (For(tuning, allowed, required, bassPc, includeStretch: false).Bases is [var normal, ..])
            return normal;
        if (For(tuning, allowed, required, bassPc, includeStretch: true).Bases is [var stretch, ..])
            return stretch;
        return null;
    }

    private static string TuningKey(IReadOnlyList<int> tuning)
    {
        var sb = new StringBuilder(tuning.Count * 3);
        foreach (int t in tuning)
            sb.Append(t).Append(',');
        return sb.ToString();
    }

    /// <summary>The walk itself — uncached, so a test can time it. <paramref name="maxSpan"/>
    /// is V5's (<see cref="SpanOf"/>); the bases are maximal among the valid shapes of THAT
    /// rule.</summary>
    internal static VoicingSet Enumerate(IReadOnlyList<int> tuning, int allowed, int required, int bassPc,
        int maxSpan)
    {
        var valid = ValidShapes(tuning, allowed, required, bassPc, maxSpan, out var options);
        var bases = Maximal(valid, options);
        bases.Sort((a, b) => CompareInOrder(a, b));
        var result = ImmutableArray.CreateBuilder<ImmutableArray<int>>(bases.Count);
        foreach (var b in bases)
            result.Add([.. b]);
        return new VoicingSet(result.MoveToImmutable(), valid.Count);
    }

    /// <summary>Every VALID shape (V1–V6), in the walk's order — the set the bases are
    /// chosen from, which the coverage net compares them against.</summary>
    internal static List<int[]> ValidShapes(IReadOnlyList<int> tuning, int allowed, int required,
        int bassPc, int maxSpan, out int[][] options)
    {
        int n = tuning.Count;
        // Per string, the frets that sound an allowed pitch class (V1, V2). A muted string is
        // tried first by the walk, then these.
        options = new int[n][];
        for (int i = 0; i < n; i++)
        {
            var list = new List<int>();
            for (int f = 0; f <= MaxFret; f++)
                if ((allowed & (1 << Mod12(tuning[i] + f))) != 0)
                    list.Add(f);
            options[i] = [.. list];
        }

        var valid = new List<int[]>();
        var frets = new int[n];
        var opts = options;

        void Walk(int i, int minFretted, int maxFretted, int sounding, int pcs, int lowest)
        {
            if (sounding + (n - i) < MinSounding)
                return;   // V1 cannot be met any more
            if (i == n)
            {
                if ((pcs & required) == required                    // V3
                    && Mod12(lowest) == bassPc                      // V4
                    && Fingers(frets) <= MaxFingers)                // V6
                    valid.Add((int[])frets.Clone());
                return;
            }
            frets[i] = -1;
            Walk(i + 1, minFretted, maxFretted, sounding, pcs, lowest);
            foreach (int f in opts[i])
            {
                int lo = minFretted, hi = maxFretted;
                if (f > 0)
                {
                    lo = System.Math.Min(lo, f);
                    hi = System.Math.Max(hi, f);
                    if (hi - lo > maxSpan)
                        continue;   // V5
                }
                int pitch = tuning[i] + f;
                frets[i] = f;
                Walk(i + 1, lo, hi, sounding + 1, pcs | (1 << Mod12(pitch)),
                     System.Math.Min(lowest, pitch));
            }
            frets[i] = -1;
        }
        Walk(0, int.MaxValue, int.MinValue, 0, 0, int.MaxValue);
        return valid;
    }

    /// <summary>The BASES among <paramref name="valid"/>: the shapes none of whose muted
    /// strings can sound a fret with the shape still valid. Unordered.</summary>
    internal static List<int[]> Maximal(List<int[]> valid, int[][] options)
    {
        int n = options.Length;
        var keys = new HashSet<long>(valid.Count);
        foreach (var v in valid)
            keys.Add(Key(v));
        var bases = new List<int[]>();
        foreach (var v in valid)
        {
            bool extendable = false;
            for (int i = 0; i < n && !extendable; i++)
            {
                if (v[i] >= 0)
                    continue;
                foreach (int f in options[i])
                {
                    v[i] = f;
                    bool hit = keys.Contains(Key(v));
                    v[i] = -1;
                    if (hit)
                    {
                        extendable = true;
                        break;
                    }
                }
            }
            if (!extendable)
                bases.Add(v);
        }
        return bases;
    }

    /// <summary>The ORDER: position, then fingers, then the frets low string first.</summary>
    public static int CompareInOrder(IReadOnlyList<int> a, IReadOnlyList<int> b)
    {
        int c = Position(a).CompareTo(Position(b));
        if (c != 0)
            return c;
        c = Fingers(a).CompareTo(Fingers(b));
        if (c != 0)
            return c;
        for (int i = 0; i < a.Count && i < b.Count; i++)
        {
            c = a[i].CompareTo(b[i]);
            if (c != 0)
                return c;
        }
        return 0;
    }

    /// <summary>Order ⒜: the lowest fretted fret, 0 when nothing is fretted.</summary>
    public static int Position(IReadOnlyList<int> frets)
    {
        int min = int.MaxValue;
        foreach (int f in frets)
            if (f > 0 && f < min)
                min = f;
        return min == int.MaxValue ? 0 : min;
    }

    /// <summary>V6's finger count: one per fretted string, a qualifying barre at the lowest
    /// fretted fret counting once.</summary>
    public static int Fingers(IReadOnlyList<int> frets)
    {
        int fretted = 0, low = int.MaxValue;
        foreach (int f in frets)
            if (f > 0)
            {
                fretted++;
                if (f < low)
                    low = f;
            }
        if (fretted == 0)
            return 0;
        int first = -1, last = -1, atLow = 0;
        for (int i = 0; i < frets.Count; i++)
            if (frets[i] == low)
            {
                if (first < 0)
                    first = i;
                last = i;
                atLow++;
            }
        if (atLow < 2)
            return fretted;
        // The barre lies across every string from its first use to its last: an open or a
        // muted string (anything below `low`) in between breaks it.
        for (int k = first; k <= last; k++)
            if (frets[k] < low)
                return fretted;
        return 1 + (fretted - atLow);
    }

    /// <summary>
    /// A shape as a player writes it: one character per string, low string first —
    /// <c>x35343</c> — or, when a fret needs two digits, the frets joined by '-':
    /// <c>8-10-10-9-8-8</c>.
    /// </summary>
    public static string Spell(IReadOnlyList<int> frets)
    {
        bool wide = false;
        foreach (int f in frets)
            if (f > 9)
                wide = true;
        var sb = new StringBuilder();
        for (int i = 0; i < frets.Count; i++)
        {
            if (wide && i > 0)
                sb.Append('-');
            sb.Append(frets[i] < 0 ? "x" : frets[i].ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    /// <summary>
    /// A shape as the page's diagram spec: one character per string, low string first,
    /// <c>x</c> muted, a digit, and <c>a</c>–<c>f</c> for frets 10–15 (the internal alphabet
    /// <c>Svg.Layout.FretFrameGeometry.FretAt</c> reads; a writer never types it).
    /// </summary>
    public static string ToFrameSpec(IReadOnlyList<int> frets)
    {
        var sb = new StringBuilder(frets.Count);
        foreach (int f in frets)
            sb.Append(f switch
            {
                < 0 => 'x',
                <= 9 => (char)('0' + f),
                _ => (char)('a' + f - 10),
            });
        return sb.ToString();
    }

    private static long Key(int[] frets)
    {
        long k = 0;
        foreach (int f in frets)
            k = (k << 5) | (long)(f + 1);
        return k;
    }

    private static int Mod12(int a) => ((a % 12) + 12) % 12;
    private static int Mod7(int a) => ((a % 7) + 7) % 7;
}
