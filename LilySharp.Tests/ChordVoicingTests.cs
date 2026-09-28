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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using LilySharp.Core.Music;
using LilySharp.Core.Syntax;
using LilySharp.Core.Tablature;
using Xunit;
using Xunit.Abstractions;

namespace LilySharp.Tests;

/// <summary>
/// The ordered base voicings of a chord (owner's rules of 2026-09-27). Since 2026-09-28 no
/// source writes a place in the order (HANDOFF §2 K0) — it is the fallback shape of a chord
/// LilyPond's table lacks and the editor's stepping order — but every number here is still a
/// net: the counts and places are the owner's prototype's (<c>maximal.py</c>, run with
/// LilyPond 2.26's python), reproduced to the shape.
/// </summary>
/// <remarks>
/// Each frozen rule has a net that turns red when it changes (poisoned one by one on
/// 2026-09-28, the results in the session's report): the counts move under every rule, and
/// the named shapes say WHICH rule moved them — <c>x3x546</c> needs maximality (it has an
/// inner mute), <c>8-10-8-8-8-8</c> needs the barre rule (six fretted strings), <c>x35553</c>
/// needs a span of 4 (frets 3 to 5 with the barre… and 8-10-10-9-8-8 a fret above 9), and
/// the first-twelve lists hold the ORDER.
/// <para>
/// Since the owner's decision of 2026-09-28 there are TWO rule sets: the prototype's numbers
/// are the STRETCH-INCLUSIVE rule's (V5 at max − min ≤ 4, <c>includeStretch: true</c>), and
/// the NORMAL rule (≤ 3 — no stretch shape; the fallback's and the editor's default) has its
/// own nets below, computed by the same walk and hand-checked shape by shape at the head of
/// each list.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class ChordVoicingTests
{
    private readonly ITestOutputHelper _out;

    public ChordVoicingTests(ITestOutputHelper output) => _out = output;

    private static readonly int[] Standard = Tunings.Guitar;

    private static ChordStructure Chord(string symbol)
    {
        Assert.True(ChordStructure.TryParseChordEntry(symbol, out var s), symbol);
        return s;
    }

    /// <summary>"x35343", "8-10-10-9-8-8" or the writer's compact "8-10-10-988" → frets, LOW
    /// string first, −1 muted (the shape reader, <see cref="ChordShapes.TryRead"/>: the step's
    /// spellings are compact since 2026-09-28).</summary>
    internal static int[] Shape(string s)
    {
        Assert.True(ChordShapes.TryRead(s, out var frets, out var problem), problem);
        return [.. frets];
    }

    private static int IndexOf(ChordVoicings.VoicingSet set, string shape)
    {
        var want = Shape(shape);
        for (int i = 0; i < set.Bases.Length; i++)
            if (set.Bases[i].SequenceEqual(want))
                return i;
        return -1;
    }

    // ---------------------------------------------------------------- the reference numbers

    [Theory]
    [InlineData("C", 267, 57)]
    [InlineData("Cm7", 126, 52)]
    [InlineData("G7", 321, 89)]
    [InlineData("D", 255, 64)]
    [InlineData("F#m7-5", 105, 45)]
    [InlineData("C9", 156, 68)]
    public void TheCounts_AreThePrototypes(string symbol, int valid, int bases)
    {
        var set = ChordVoicings.For(Standard, Chord(symbol), includeStretch: true);
        Assert.Equal(valid, set.ValidCount);
        Assert.Equal(bases, set.Bases.Length);
    }

    [Theory]
    [InlineData("C", "x32010", 0)]
    [InlineData("C", "x35553", 12)]
    [InlineData("C", "8-10-10-9-8-8", 47)]
    [InlineData("Cm7", "x35343", 2)]
    [InlineData("Cm7", "x3x546", 3)]
    [InlineData("Cm7", "8-10-8-8-8-8", 31)]
    [InlineData("G7", "320001", 0)]
    [InlineData("G7", "353433", 21)]
    [InlineData("G7", "xx5767", 41)]
    [InlineData("D", "x50232", 0)]
    [InlineData("D", "x54232", 2)]
    [InlineData("D", "x57775", 9)]
    [InlineData("D", "10-12-12-11-10-10", 47)]
    [InlineData("F#m7-5", "202210", 2)]
    [InlineData("F#m7-5", "x-9-10-9-10-0", 26)]
    [InlineData("C9", "x32330", 2)]
    public void TheFamiliarShapes_StandAtThePrototypesIndices(string symbol, string shape, int index)
        => Assert.Equal(index, IndexOf(ChordVoicings.For(Standard, Chord(symbol), includeStretch: true), shape));

    /// <summary>The ORDER, beyond the named shapes: the prototype's first twelve bases.</summary>
    [Theory]
    [InlineData("C", "x32010 x35010 x32013 x32510 x35510 x32050 x32053 x325x3 x32550 x35050 x35053 x35550")]
    [InlineData("Cm7", "x31313 x3134x x35343 x3x546 x35046 x35346 8xx546 8xx846 8x5046 8x8046 8x8048 8x854x")]
    [InlineData("G7", "320001 350001 xx5431 3x0431 3x3401 3x5401 320031 320401 323001 325001 350401 353001")]
    [InlineData("D", "x50232 x542x5 x54232 x54x35 x5473x x54x75 x547x5 x5477x x50775 x57775 x90775")]
    [InlineData("F#m7-5", "202x12 202x15 202210 202510 204210 204510 23x210 232250 232252 20x552 20x555 2025x2")]
    [InlineData("C9", "x30310 x32036 x32330 x35330 x30056 x30330 x30350 x30353 x30356 x30556 x30756 855556")]
    public void TheFirstBases_AreInThePrototypesOrder(string symbol, string first)
    {
        var set = ChordVoicings.For(Standard, Chord(symbol), includeStretch: true);
        var want = first.Split(' ');
        for (int i = 0; i < want.Length; i++)
            Assert.True(set.Bases[i].SequenceEqual(Shape(want[i])),
                $"{symbol} #{i}: {ChordVoicings.Spell(set.Bases[i])}, the prototype has {want[i]}");
    }

    // ---------------------------------------------------------------- the normal rule (no stretch)

    /// <summary>
    /// The NORMAL rule's numbers (owner's decision 2026-09-28: a stretch shape — fretted frets
    /// five apart — is not offered by default). Computed 2026-09-28 by this walk with V5 at 3;
    /// the heads of the lists checked by hand: C's <c>x35010</c> and <c>x32510</c> (frets 1 to
    /// 5) are gone, so after <c>x32010</c> (position 1, three fingers) comes <c>x32013</c>
    /// (position 1, four), then position 2's <c>x32050</c> (three fingers) before
    /// <c>x32053</c> / <c>x325x3</c> / <c>x32550</c> (four each, lexicographic, −1 first); G7's
    /// <c>350001</c> and <c>xx5431</c> (frets 1 to 5) are gone the same way.
    /// </summary>
    [Theory]
    [InlineData("C", 207, 38)]
    [InlineData("Cm7", 85, 33)]
    [InlineData("G7", 215, 59)]
    [InlineData("D", 169, 36)]
    public void TheNormalCounts_ArePinned(string symbol, int valid, int bases)
    {
        var set = ChordVoicings.For(Standard, Chord(symbol), includeStretch: false);
        Assert.Equal(valid, set.ValidCount);
        Assert.Equal(bases, set.Bases.Length);
    }

    [Theory]
    [InlineData("C", "x32010", 0)]
    [InlineData("C", "x35553", 9)]
    [InlineData("C", "8-10-10-9-8-8", 30)]
    [InlineData("Cm7", "x35343", 2)]
    [InlineData("Cm7", "x3x546", 3)]
    [InlineData("Cm7", "8-10-8-8-8-8", 18)]
    [InlineData("G7", "320001", 0)]
    [InlineData("G7", "353433", 10)]
    [InlineData("G7", "xx5767", 20)]
    [InlineData("D", "x50232", 0)]
    [InlineData("D", "x57775", 8)]
    [InlineData("D", "10-12-12-11-10-10", 27)]
    public void TheNormalShapes_StandAtTheirPinnedIndices(string symbol, string shape, int index)
        => Assert.Equal(index, IndexOf(ChordVoicings.For(Standard, Chord(symbol), includeStretch: false), shape));

    [Theory]
    [InlineData("C", "x32010 x32013 x32050 x32053 x325x3 x32550 x35050 x35053 x35550 x35553")]
    [InlineData("Cm7", "x31313 x3134x x35343 x3x546 x35046 x35346 8x58x6 8650x6")]
    [InlineData("G7", "320001 3x0431 3x3401 320031 320401 323001 323003 32303x 32340x 35340x")]
    [InlineData("D", "x50232 x542x5 x54232 x54x35 x54x75 x547x5 x5477x x50775 x57775")]
    public void TheFirstNormalBases_AreInOrder(string symbol, string first)
    {
        var set = ChordVoicings.For(Standard, Chord(symbol), includeStretch: false);
        var want = first.Split(' ');
        for (int i = 0; i < want.Length; i++)
            Assert.True(set.Bases[i].SequenceEqual(Shape(want[i])),
                $"{symbol} #{i}: {ChordVoicings.Spell(set.Bases[i])}, pinned {want[i]}");
    }

    /// <summary>No base of the normal rule stretches; the stretch-inclusive rule has some.</summary>
    [Theory]
    [InlineData("C")]
    [InlineData("Cm7")]
    [InlineData("G7")]
    [InlineData("D")]
    public void TheNormalRule_HasNoStretchShape(string symbol)
    {
        Assert.DoesNotContain(ChordVoicings.For(Standard, Chord(symbol), includeStretch: false).Bases,
            b => ChordVoicings.IsStretch(b));
        Assert.Contains(ChordVoicings.For(Standard, Chord(symbol), includeStretch: true).Bases,
            b => ChordVoicings.IsStretch(b));
    }

    /// <summary>
    /// MAXIMAL WITHIN ITS OWN RULE: G7's <c>35340x</c> is a normal base — its top string takes
    /// no fret without a stretch or a fifth finger — but with stretch shapes allowed it grows
    /// into <c>353407</c> (frets 3 to 7, the 3s a barre over the 5: four fingers), so it is no
    /// base there. Filtering the stretch-inclusive bases would have lost it.
    /// </summary>
    [Fact]
    public void Maximality_IsComputedWithinTheRuleEnumerated()
    {
        var normal = ChordVoicings.For(Standard, Chord("G7"), includeStretch: false);
        var stretch = ChordVoicings.For(Standard, Chord("G7"), includeStretch: true);
        Assert.True(IndexOf(normal, "35340x") >= 0);
        Assert.Equal(-1, IndexOf(stretch, "35340x"));
        Assert.True(IndexOf(stretch, "353407") >= 0);
    }

    [Theory]
    [InlineData("C")]
    [InlineData("Cm7")]
    [InlineData("G7")]
    [InlineData("D")]
    public void EveryNormalValidShape_IsANormalBaseWithStringsMuted(string symbol)
    {
        Assert.True(ChordVoicings.TryTones(Chord(symbol), out int allowed, out int required, out int bass));
        var valid = ChordVoicings.ValidShapes(Standard, allowed, required, bass, ChordVoicings.NormalSpan, out _);
        var bases = ChordVoicings.For(Standard, allowed, required, bass, includeStretch: false).Bases;
        Assert.NotEmpty(valid);
        foreach (var v in valid)
            Assert.True(bases.Any(b => Enumerable.Range(0, v.Length).All(i => v[i] < 0 || b[i] == v[i])),
                $"{symbol}: {ChordVoicings.Spell(v)} is no normal base with strings muted");
    }

    // ---------------------------------------------------------------- the fallback

    /// <summary>
    /// The compiler's fallback prefers a shape without a stretch: on the standard tuning, for
    /// every chord below, the fallback is the NORMAL order's first — and the net bites only if
    /// some of them would have drawn a stretch shape from the stretch-inclusive order.
    /// </summary>
    [Fact]
    public void TheFallback_IsTheFirstNormalShape()
    {
        int wouldStretch = 0;
        foreach (var root in new[] { "C", "D", "E", "F", "G", "A", "B", "Eb", "Bb", "F#" })
            foreach (var quality in new[] { "", "m", "7", "m7", "maj7", "9", "m9", "maj9", "6", "7-5", "dim7", "add9" })
            {
                var chord = Chord(root + quality);
                var normal = ChordVoicings.For(Standard, chord, includeStretch: false).Bases;
                var wide = ChordVoicings.For(Standard, chord, includeStretch: true).Bases;
                if (normal.IsEmpty)
                    continue;
                Assert.True(ChordVoicings.Fallback(Standard, chord)!.Value.SequenceEqual(normal[0]), root + quality);
                if (ChordVoicings.IsStretch(wide[0]))
                    wouldStretch++;
            }
        _out.WriteLine($"{wouldStretch} chords would have drawn a stretch shape");
        Assert.True(wouldStretch > 0, "no chord's stretch-inclusive first stretches: the net does not bite");
    }

    /// <summary>
    /// A chord whose every shape stretches still draws: F11 in open G (D2 G2 D3 G3 B3 D4) has no
    /// normal shape, so the fallback is the stretch-inclusive first, <c>333047</c> — checked by
    /// hand: F2 B♭2 F3 G3 E♭4 A4 (the fifth, C, left out), F lowest, frets 3 to 7 a stretch, the
    /// 3s a barre over three strings (three fingers).
    /// </summary>
    [Fact]
    public void AChordWithOnlyStretchShapes_FallsBackToTheFirstStretchShape()
    {
        var openG = Tunings.GetTuning(Tunings.Parse("guitaropeng"));
        var f11 = Chord("F11");
        Assert.Empty(ChordVoicings.For(openG, f11, includeStretch: false).Bases);
        Assert.Equal("333047", ChordVoicings.Spell(ChordVoicings.Fallback(openG, f11)!.Value));
        var chosen = ChordShapes.Default(Tunings.Parse("guitaropeng"), f11);
        Assert.Equal("333047", chosen!.Spelled);
        Assert.Equal(ShapeSource.FirstOfOrder, chosen.Source);
    }

    /// <summary>
    /// COVERAGE — the property maximality was chosen for: every valid shape is some base with
    /// strings muted, so <c>mute</c> reaches every playable shape.
    /// </summary>
    [Theory]
    [InlineData("C")]
    [InlineData("Cm7")]
    [InlineData("G7")]
    [InlineData("D")]
    [InlineData("F#m7-5")]
    [InlineData("C9")]
    public void EveryValidShape_IsABaseWithStringsMuted(string symbol)
    {
        Assert.True(ChordVoicings.TryTones(Chord(symbol), out int allowed, out int required, out int bass));
        var valid = ChordVoicings.ValidShapes(Standard, allowed, required, bass, ChordVoicings.StretchSpan, out _);
        var bases = ChordVoicings.For(Standard, allowed, required, bass, includeStretch: true).Bases;
        Assert.NotEmpty(valid);
        foreach (var v in valid)
            Assert.True(bases.Any(b => Enumerable.Range(0, v.Length).All(i => v[i] < 0 || b[i] == v[i])),
                $"{symbol}: {ChordVoicings.Spell(v)} is no base with strings muted");
    }

    /// <summary>
    /// V3's required tones, from the registry: every tone but the PERFECT fifth — the sets the
    /// prototype hard-codes (C {C,E}, Cm7 {C,E♭,B♭}, G7 {G,B,F}, D {D,F♯}, F♯m7♭5 all four,
    /// C9 {C,E,B♭,D}).
    /// </summary>
    [Theory]
    [InlineData("C", new[] { 0, 4 }, new[] { 0, 4, 7 })]
    [InlineData("Cm7", new[] { 0, 3, 10 }, new[] { 0, 3, 7, 10 })]
    [InlineData("G7", new[] { 7, 11, 5 }, new[] { 7, 11, 2, 5 })]
    [InlineData("D", new[] { 2, 6 }, new[] { 2, 6, 9 })]
    [InlineData("F#m7-5", new[] { 6, 9, 0, 4 }, new[] { 6, 9, 0, 4 })]
    [InlineData("C9", new[] { 0, 4, 10, 2 }, new[] { 0, 4, 7, 10, 2 })]
    [InlineData("C/G", new[] { 0, 4, 7 }, new[] { 0, 4, 7 })]
    [InlineData("Am/F#", new[] { 9, 0, 6 }, new[] { 9, 0, 4, 6 })]
    public void TheRequiredTones_AreAllButThePerfectFifth(string symbol, int[] required, int[] allowed)
    {
        Assert.True(ChordVoicings.TryTones(Chord(symbol), out int a, out int r, out _));
        Assert.Equal(required.Aggregate(0, (m, pc) => m | 1 << pc), r);
        Assert.Equal(allowed.Aggregate(0, (m, pc) => m | 1 << pc), a);
    }

    [Fact]
    public void ASlashChord_HasItsBassLowest()
    {
        // C/G: every base sounds G lowest (V4), and G may not be left out (V3).
        var set = ChordVoicings.For(Standard, Chord("C/G"), includeStretch: true);
        Assert.NotEmpty(set.Bases);
        foreach (var b in set.Bases)
        {
            int lowest = Enumerable.Range(0, 6).Where(i => b[i] >= 0).Min(i => Standard[i] + b[i]);
            Assert.Equal(7, lowest % 12);
        }
        // The familiar 3x2010 (G on the sixth string, then C E G C E).
        Assert.Contains(set.Bases, b => b.SequenceEqual(Shape("332010")));
    }

    // ---------------------------------------------------------------- the rules one by one

    [Fact]
    public void TheFingerCount_TakesABarreAsOneFinger_OnlyWhenNothingBreaksIt()
    {
        Assert.Equal(1 + 3, ChordVoicings.Fingers(Shape("8-10-10-9-8-8")));  // barre at 8
        Assert.Equal(1 + 1, ChordVoicings.Fingers(Shape("8-10-8-8-8-8")));   // barre + one
        Assert.Equal(3, ChordVoicings.Fingers(Shape("x32010")));             // no fret twice
        Assert.Equal(4, ChordVoicings.Fingers(Shape("x3x546")));             // 3 and 5 4 6
        // 1 on two strings with an OPEN string between them: no barre.
        Assert.Equal(3, ChordVoicings.Fingers(Shape("x1012x")));
        // …and with a MUTED string between them: no barre either.
        Assert.Equal(3, ChordVoicings.Fingers(Shape("1x12xx")));
        Assert.Equal(0, ChordVoicings.Fingers(Shape("x0000x")));
    }

    [Fact]
    public void ThePosition_IsTheLowestFrettedFret_OpenStringsNotCounted()
    {
        Assert.Equal(1, ChordVoicings.Position(Shape("x32010")));
        Assert.Equal(8, ChordVoicings.Position(Shape("8-10-10-9-8-8")));
        Assert.Equal(0, ChordVoicings.Position(Shape("x0000x")));
    }

    [Fact]
    public void AReentrantTuning_IsNotGuitarType_AndDropDAndSevenStringAre()
    {
        Assert.True(ChordVoicings.IsGuitarType(Tunings.Guitar));
        Assert.True(ChordVoicings.IsGuitarType(Tunings.GetTuning(TuningType.GuitarDropD)));
        Assert.True(ChordVoicings.IsGuitarType(Tunings.GetTuning(TuningType.Guitar7)));
        Assert.True(ChordVoicings.IsGuitarType(Tunings.GetTuning(TuningType.TenorUkulele)));
        Assert.False(ChordVoicings.IsGuitarType(Tunings.GetTuning(TuningType.Ukulele)));
        Assert.False(ChordVoicings.IsGuitarType(Tunings.GetTuning(TuningType.BanjoOpenG)));
    }

    // ---------------------------------------------------------------- other tunings

    /// <summary>
    /// Drop D (D2 A2 D3 G3 B3 E4): the open-D shape rings the sixth string too. Pinned after
    /// checking by hand: <c>000232</c> is D A D A D F♯ — only D and F♯ and A, D lowest, frets 2
    /// 3 2 are three fingers — and it is #0 because nothing sits lower (position 2, three
    /// fingers, and every earlier-sorting tuple at position 2 fails a rule).
    /// </summary>
    [Fact]
    public void DropD_PinnedByHand()
    {
        var set = ChordVoicings.For(Tunings.GetTuning(TuningType.GuitarDropD), Chord("D"), includeStretch: true);
        _out.WriteLine($"drop-D D: {set.ValidCount} valid, {set.Bases.Length} bases; first: "
            + string.Join(" ", set.Bases.Take(8).Select(b => ChordVoicings.Spell(b))));
        Assert.Equal(DropDValid, set.ValidCount);
        Assert.Equal(DropDBases, set.Bases.Length);
        Assert.Equal(0, IndexOf(set, "000232"));
    }

    // Checked 2026-09-28 against the prototype's own logic generalised to any tuning (the
    // same maximal.py rules, run with LilyPond 2.26's python): 522 valid, 76 bases, the same
    // first eight — 000232 004232 050232 004235 0542x5 054232 004735 054x35. By hand: no fret 1
    // on any string sounds D, F♯ or A, and an all-open shape has no F♯, so position 2 is the
    // lowest; 000232 counts two fingers (the 2s barre over the 3); and x00232, which would sort
    // first, is not maximal (its sixth string takes the open D).
    private const int DropDValid = 522;
    private const int DropDBases = 76;

    /// <summary>
    /// Seven strings (B1 E2 A2 D3 G3 B3 E4). #0 is <c>xx32013</c> (C3 E3 G3 C4 G4): the
    /// familiar <c>xx32010</c> is NOT a base here, because the seventh string can add the low
    /// C2 at fret 1 (<c>1x32010</c> is valid — four fingers, the two 1s split by a muted
    /// string), and every base it grows into starts with a sounding seventh string, which sorts
    /// after <c>xx32013</c>'s muted one (−1 first). Checked by hand and against the generalised prototype: 805 valid, 146
    /// bases, first eight xx32013 xx32510 xx35510 10xx513 10xx553 10x2013 10x2053 10x25x3.
    /// </summary>
    [Fact]
    public void SevenString_PinnedByHand()
    {
        var set = ChordVoicings.For(Tunings.GetTuning(TuningType.Guitar7), Chord("C"), includeStretch: true);
        _out.WriteLine($"7-string C: {set.ValidCount} valid, {set.Bases.Length} bases; first: "
            + string.Join(" ", set.Bases.Take(8).Select(b => ChordVoicings.Spell(b))));
        Assert.Equal(SevenValid, set.ValidCount);
        Assert.Equal(SevenBases, set.Bases.Length);
        Assert.Equal(0, IndexOf(set, SevenFirst));
    }

    private const int SevenValid = 805;
    private const int SevenBases = 146;
    private const string SevenFirst = "xx32013";

    // ---------------------------------------------------------------- the price

    /// <summary>
    /// The walk runs on keystrokes (the validator and the page both ask), so its price is
    /// written down: every chord of the reference table, uncached, on six and seven strings.
    /// A generous ceiling, so this catches an exponential regression, not a slow machine.
    /// </summary>
    [Fact]
    public void TheWalk_IsCheapEnoughForAKeystroke()
    {
        foreach (var tuning in new[] { Tunings.Guitar, Tunings.GetTuning(TuningType.Guitar7) })
            foreach (var symbol in new[] { "C", "Cm7", "G7", "D", "F#m7-5", "C9", "C13", "Cm11" })
            {
                Assert.True(ChordVoicings.TryTones(Chord(symbol), out int a, out int r, out int b));
                ChordVoicings.Enumerate(tuning, a, r, b, ChordVoicings.StretchSpan);   // warm the JIT
                var sw = Stopwatch.StartNew();
                const int runs = 5;
                for (int k = 0; k < runs; k++)
                    ChordVoicings.Enumerate(tuning, a, r, b, ChordVoicings.StretchSpan);
                double ms = sw.Elapsed.TotalMilliseconds / runs;
                _out.WriteLine($"{tuning.Length} strings {symbol}: {ms:0.00} ms");
                Assert.True(ms < 250, $"{symbol} on {tuning.Length} strings took {ms:0.0} ms");
            }
    }
}
