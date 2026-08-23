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
            form main { A }
            score main { staff m  chords prog{{display}} }
            """;
        var tree = SyntaxTree.Parse(src);
        Assert.False(tree.HasErrors, string.Join(" | ", tree.Diagnostics.Select(d => d.Message)));
        var spec = RenderSpecParser.FindFirst(tree);
        Assert.NotNull(spec);
        return new MeasureCollector().CollectMultiStaff(tree, spec!);
    }

    private static string[] Names(MultiStaffScore s)
        => s.ChordNames.OrderBy(c => c.MeasureIndex).Select(c => c.ChordText).ToArray();

    private static string?[] Degrees(MultiStaffScore s)
        => s.ChordNames.OrderBy(c => c.MeasureIndex).Select(c => c.RomanText).ToArray();

    [Fact]
    public void ADegreeResolvesToTheKeysChord()
    {
        Assert.Equal(new[] { "Cmaj7", "G7", "Dm7" },
            Names(Collect("c major", "Imaj7 | V7 | IIm7 |")));
    }

    /// <summary>
    /// The reason the feature exists: the SAME source in another key is another chord.
    /// </summary>
    [Fact]
    public void TheSameDegreesFollowTheKey()
    {
        const string entries = "Imaj7 | V7 | IIm7 | bVII |";
        Assert.Equal(new[] { "Cmaj7", "G7", "Dm7", "B♭" }, Names(Collect("c major", entries)));
        Assert.Equal(new[] { "E♭maj7", "B♭7", "Fm7", "D♭" }, Names(Collect("ees major", entries)));
    }

    [Fact]
    public void ADegreeChartStillPrintsNamesByDefault_AndDegreesUnderAsRoman()
    {
        // Written one way, shown either way — the display selector is unchanged by this.
        Assert.Equal(new[] { "Cmaj7", "G7" }, Names(Collect("c major", "Imaj7 | V7 |")));
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
    [InlineData("#IVm7-5 |", "F♯m7♭5")]   // chromatic degree, altered tension
    [InlineData("bVII |", "B♭")]           // flat degree, typed ASCII
    [InlineData("V7/VII |", "G7/B")]       // slash bass, itself a degree
    [InlineData("VIIdim |", "Bdim")]
    [InlineData("Vaug |", "Gaug")]
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
            form main { A }
            score main { staff m  chords prog }
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
            Assert.Equal(original.DisplayName, back.DisplayName);
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
        Assert.Equal("B♭", flat.DisplayName);

        var tree = SyntaxTree.Parse("""
            time 4/4
            key c major
            part m { clef treble section A { c4 d e f | } }
            chords prog { section A { ♭VII | } }
            form main { A }
            score main { staff m  chords prog }
            """);
        Assert.True(tree.HasErrors);
    }
}
