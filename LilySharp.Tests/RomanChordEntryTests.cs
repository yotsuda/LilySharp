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

using System.Linq;
using LilySharp.Core.Music;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A chord track's entries may be written as ROMAN DEGREES of the key —
/// <c>chords prog { section A { Imaj7 | V7 } }</c> — as well as absolute names. The degree
/// resolves against the key in force at that bar, so one written progression follows the
/// key instead of being respelled by hand.
/// </summary>
/// <remarks>
/// The WRITTEN form and the DISPLAYED form are independent, and that is the point of
/// resolving to a <see cref="ChordStructure"/> rather than keeping the text: a chart
/// written in degrees prints absolute names by default and degrees under
/// <c>as roman</c>, and a chart written in names does the same. The degree vocabulary
/// itself lives once, as <c>ChordStructure.ToRomanNumeral</c> and its exact inverse
/// <c>TryParseRomanEntry</c>.
/// </remarks>
[Trait("Category", "Unit")]
public class RomanChordEntryTests
{
    private static MultiStaffScore Collect(string key, string entries, string display = "")
    {
        var src = $$"""
            time 4/4
            key {{key}}
            part m { clef treble
              section A { c4 d e f | g a b c' | c'4 b a g | f e d c | }
            }
            chords prog { section A { {{entries}} } }
            form { A }
            score { staff m  chords prog{{display}} }
            """;
        var tree = SyntaxTree.Parse(src);
        Assert.False(tree.HasErrors, string.Join(" | ", tree.Diagnostics.Select(d => d.Message)));
        var spec = RenderSpecParser.FindFirst(tree);
        Assert.NotNull(spec);
        return new MeasureCollector().CollectMultiStaff(tree, spec!);
    }

    /// <summary>The names a score PRINTS, in bar order. ⚠️ These are the drawn symbols, and
    /// since 2026-09-12 the default vocabulary is LilyPond's — a major seventh is its
    /// triangle, a diminished triad its degree sign — so <c>Imaj7</c> resolves to a chord
    /// that prints <c>C△</c>. What these cases are about is WHICH CHORD a degree names; the
    /// vocabulary is <c>LayoutBlockTests</c>'.</summary>
    private static string[] Names(MultiStaffScore s)
        => s.ChordNames.OrderBy(c => c.MeasureIndex).Select(c => c.ChordText).ToArray();

    private static string?[] Degrees(MultiStaffScore s)
        => s.ChordNames.OrderBy(c => c.MeasureIndex).Select(c => c.RomanText).ToArray();

    [Fact]
    public void ADegreeResolvesToTheKeysChord()
    {
        Assert.Equal(new[] { "C△", "G7", "Dm7" },
            Names(Collect("c major", "Imaj7 | V7 | IIm7 |")));
    }

    /// <summary>
    /// The reason the feature exists: the SAME source in another key is another chord.
    /// </summary>
    [Fact]
    public void TheSameDegreesFollowTheKey()
    {
        const string entries = "Imaj7 | V7 | IIm7 | bVII |";
        Assert.Equal(new[] { "C△", "G7", "Dm7", "B♭" }, Names(Collect("c major", entries)));
        Assert.Equal(new[] { "E♭△", "B♭7", "Fm7", "D♭" }, Names(Collect("ees major", entries)));
    }

    [Fact]
    public void ADegreeChartStillPrintsNamesByDefault_AndDegreesUnderAsRoman()
    {
        // Written one way, shown either way — the display selector is unchanged by this.
        Assert.Equal(new[] { "C△", "G7" }, Names(Collect("c major", "Imaj7 | V7 |")));
        Assert.Equal(new[] { "Imaj7", "V7" },
            Degrees(Collect("c major", "Imaj7 | V7 |", " as roman")));
    }

    [Fact]
    public void AbsoluteAndDegreeEntriesResolveToTheSameStructure()
    {
        // The two spellings are one chord: same printed name, same degree, either way in.
        var written = Collect("c major", "Imaj7 | V7 |");
        var spelled = Collect("c major", "Cmaj7 | G7 |");

        Assert.Equal(Names(spelled), Names(written));
        Assert.Equal(Degrees(spelled), Degrees(written));
    }

    [Theory]
    [InlineData("#IVm7-5 |", "F♯ø")]      // chromatic degree, altered tension
    [InlineData("bVII |", "B♭")]           // flat degree, typed ASCII
    [InlineData("V7/7 |", "G7/B")]         // slash bass, the key's degree as a digit
    [InlineData("IV/b7 |", "F/B♭")]        // a flat bass digit
    [InlineData("V7/V |", "D7")]           // secondary dominant: V7 of the major key on V
    [InlineData("IIm7/IV |", "Gm7")]       // the 2 of IV's 2-5
    [InlineData("VIIdim7/V |", "F♯°7")]    // the leading-tone chord of V
    [InlineData("V7/bVII |", "F7")]        // a flat target
    [InlineData("V7/V/#4 |", "D7/F♯")]     // a secondary with its own bass
    [InlineData("VIIdim |", "B°")]
    [InlineData("Vaug |", "G+")]
    public void TheDegreeGrammarCoversTheOrdinaryShapes(string entry, string expected)
        => Assert.Equal(new[] { expected }, Names(Collect("c major", entry)));

    /// <summary>
    /// The numerals are matched LONGEST FIRST. Written ascending, "I" would match the head
    /// of "IV" and the "V" would fall into the quality — so this is the test that would
    /// fail if that array were ever re-sorted.
    /// </summary>
    [Fact]
    public void CompoundNumeralsAreNotReadAsTheirFirstLetter()
    {
        // In C: IV = F, VI = A, VII = B, III = E. Read first-letter-first, IV/VI/VII would
        // all come out as C or G with a nonsense quality — and fail to resolve at all.
        Assert.Equal(new[] { "F", "A", "B", "E" },
            Names(Collect("c major", "IV | VI | VII | III |")));
    }

    [Fact]
    public void AMidPieceKeyChangeRebasesTheDegreesAfterIt()
    {
        var src = """
            time 4/4
            key c major
            part m { clef treble
              section A { c'4 c' c' c' | key g major d' d' d' d' | }
            }
            chords prog { section A { I | I | } }
            form { A }
            score { staff m  chords prog }
            """;
        var tree = SyntaxTree.Parse(src);
        Assert.False(tree.HasErrors, string.Join(" | ", tree.Diagnostics.Select(d => d.Message)));
        var score = new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!);

        // Same degree, two keys: I is C then G.
        Assert.Equal(new[] { "C", "G" }, Names(score));
    }

    // ---- the string parser, directly ----

    [Fact]
    public void TryParseRomanEntry_IsTheInverseOfToRomanNumeral()
    {
        // Round trip through the pivot, for every degree of C major: print the degree,
        // read it back, and the structure is the one that printed it.
        for (int deg = 0; deg < 7; deg++)
        {
            var original = new ChordStructure(deg, KeySpelling.Alteration(deg, 0), ChordQuality.Dominant7);
            string roman = original.ToRomanNumeral(tonicStep: 0, keySharps: 0);

            Assert.True(ChordStructure.TryParseRomanEntry(roman, tonicStep: 0, keySharps: 0, out var back),
                $"'{roman}' did not read back");
            Assert.Equal(original.DisplayName(ChordSpelling.Default),
                back.DisplayName(ChordSpelling.Default));
        }
    }

    [Fact]
    public void AnAbsoluteSymbolIsNotADegree_AndViceVersa()
    {
        // The two spellings cannot collide — a root is A-G, a numeral is I or V — which is
        // what lets the parser choose an arm on the first letter alone.
        Assert.False(ChordStructure.TryParseRomanEntry("Cmaj7", 0, 0, out _));
        Assert.False(ChordStructure.TryParseChordEntry("Imaj7", out _));
    }

    /// <summary>
    /// ⚠️ MEASURED, and the reason the grammar documents only the ASCII spellings: the
    /// LEXER refuses ♭ ♯ ° ø outright, so a degree cannot be pasted back from the score it
    /// printed. TryParseRomanEntry itself accepts them — this pins the split, so the day
    /// the lexer admits them nothing else has to change.
    /// </summary>
    [Fact]
    public void ThePrintedGlyphsParseAsStrings_ButDoNotLex()
    {
        Assert.True(ChordStructure.TryParseRomanEntry("♭VII", 0, 0, out var flat));
        Assert.Equal("B♭", flat.DisplayName(ChordSpelling.Default));

        var tree = SyntaxTree.Parse("""
            time 4/4
            key c major
            part m { clef treble section A { c4 d e f | } }
            chords prog { section A { ♭VII | } }
            form { A }
            score { staff m  chords prog }
            """);
        Assert.True(tree.HasErrors);
    }

    [Fact]
    public void ASecondaryFunctionPrintsAsWritten_AndABassAsADigit()
    {
        // `/` + a numeral is the target of a secondary function, `/` + a digit the bass
        // (owner's decision 2026-10-14, session 905). The degree row gives the function back
        // — a D7 written V7/V prints V7/V, not II7 — and the bass as the Nashville digit.
        Assert.Equal(new[] { "V7/V", "V7", "VII°7/V", "IV/♭7" },
            Degrees(Collect("c major", "V7/V | V7 | VIIdim7/V | IV/b7 |", " as roman")));
        // The same functions in F: the target is read in the key at the bar.
        Assert.Equal(new[] { "G7", "C7", "B°7", "B♭/E♭" },
            Names(Collect("f major", "V7/V | V7 | VIIdim7/V | IV/b7 |")));
    }

    [Fact]
    public void ASectionAfterAModulatedOneReadsItsDegreesInTheHomeKey()
    {
        // A section boundary reverts the key to the home key (the staff draws the naturals),
        // and that revert is a key change for the degrees too. Until session 905 only a
        // WRITTEN `key` re-based them, so C2 below read C F as V I (in F) — the owner met it
        // in a lesson sheet that set out each progression in C, then in F.
        var tree = SyntaxTree.Parse("""
            part m { clef treble }
            section C1 { chords prog { C F | }  m { c'2 f' | } }
            section F1 { chords prog { F Bb | }  m { key f major f'2 bes' | } }
            section C2 { chords prog { C F | }  m { c'2 f' | } }
            form { ~C1 ~F1 ~C2 }
            score { chords prog as roman  staff m }
            """);
        Assert.False(tree.HasErrors, string.Join(" | ", tree.Diagnostics.Select(d => d.Message)));
        var spec = RenderSpecParser.FindFirst(tree);
        Assert.NotNull(spec);
        var score = new MeasureCollector().CollectMultiStaff(tree, spec!);
        Assert.Equal(new[] { "I", "IV", "I", "IV", "I", "IV" },
            score.ChordNames.OrderBy(c => c.MeasureIndex).ThenBy(c => c.Timing)
                .Select(c => c.RomanText).ToArray());
    }
}
