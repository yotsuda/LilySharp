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
using System.Xml.Linq;
using LilySharp.Core.Music;
using LilySharp.Core.MusicXml;
using LilySharp.Core.MusicXmlImport;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The printed spellings of a chord symbol (owner's decisions 2026-10-23, session 967,
/// HANDOFF §2 E ⑹ "a+e"): the chart's glyphs and letters, the parenthesized alteration
/// list with spaces and commas inside, the bare number in it, the slash bass as an
/// inversion or an added note, and the MusicXML degrees of an assembled quality.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ChordSymbolSpellingTests
{
    private static ChordStructure Parse(string symbol)
    {
        Assert.True(ChordStructure.TryParseChordEntry(symbol, out var chord, out var why), $"{symbol} should parse: {why}");
        return chord;
    }

    private static string Printed(string symbol) => Parse(symbol).PrintedSymbol(ChordSpelling.Default).Text;

    /// <summary>Every spelling of one tone set is that chord: the registered quality (its
    /// table name) or the same assembled formula — and prints the canonical name
    /// (decision ⑶), the glyphs of LilyPond's default vocabulary.</summary>
    [Theory]
    // (4) the glyphs
    [InlineData("CΔ", "Cmaj7", "C△")]
    [InlineData("CΔ7", "Cmaj7", "C△")]
    [InlineData("C△7", "Cmaj7", "C△")]
    [InlineData("CΔ9", "Cmaj9", null)]
    [InlineData("Cø", "Cm7-5", "Cø")]
    [InlineData("Cø7", "Cm7-5", "Cø")]
    [InlineData("C°", "Cdim", "C°")]
    [InlineData("C°7", "Cdim7", "C°7")]
    [InlineData("C+", "Caug", "C+")]
    [InlineData("C+7", "Caug7", "C7♯5")]
    [InlineData("C+maj7", "Caugmaj7", null)]
    // (5) '-' after the root is the minor third
    [InlineData("C-", "Cm", "Cm")]
    [InlineData("C-7", "Cm7", "Cm7")]
    [InlineData("C-9", "Cm9", "Cm9")]
    [InlineData("C-Δ7", "Cmmaj7", "Cm△")]
    [InlineData("C-maj7", "Cmmaj7", "Cm△")]
    // (6a) the letter aliases
    [InlineData("CM7", "Cmaj7", "C△")]
    [InlineData("CMa7", "Cmaj7", "C△")]
    [InlineData("CMaj7", "Cmaj7", "C△")]
    [InlineData("CM9", "Cmaj9", null)]
    [InlineData("Cmi7", "Cm7", "Cm7")]
    [InlineData("CMi7", "Cm7", "Cm7")]
    [InlineData("CMI7", "Cm7", "Cm7")]
    [InlineData("Cmi", "Cm", "Cm")]
    [InlineData("CmM7", "Cmmaj7", "Cm△")]
    [InlineData("Co", "Cdim", "C°")]
    [InlineData("Co7", "Cdim7", "C°7")]
    [InlineData("C07", "Cm7-5", "Cø")]
    [InlineData("CØ7", "Cm7-5", "Cø")]
    [InlineData("C7no3", "C7omit3", "C7")]
    [InlineData("Cno5", "Comit5", null)]
    // (6d) the accidental glyphs
    [InlineData("B♭7", "Bb7", "B♭7")]
    [InlineData("F♯m7", "F#m7", "F♯m7")]
    [InlineData("C/E♭", "C/Eb", "C/E♭")]
    // The parenthesized list
    [InlineData("C7(b9)", "C7-9", "C7♭9")]
    [InlineData("C7(♭9)", "C7-9", "C7♭9")]
    [InlineData("C7(#11)", "C7+11", "C7♯11")]
    [InlineData("C7(b9,#11)", "C7-9+11", "C7♭9♯11")]
    [InlineData("C7(b9, #11)", "C7-9+11", "C7♭9♯11")]
    [InlineData("C7(b9 #11)", "C7-9+11", "C7♭9♯11")]
    [InlineData("Cm7(b5)", "Cm7-5", "Cø")]
    [InlineData("C(#5)", "Caug", "C+")]
    [InlineData("C(add9)", "Cadd9", "Cadd9")]
    [InlineData("C7(omit3)", "C7omit3", "C7")]
    [InlineData("C7(no3)", "C7omit3", "C7")]
    [InlineData("C7(sus4)", "C7sus4", "C7sus4")]
    [InlineData("Cm(maj7)", "Cmmaj7", "Cm△")]
    [InlineData("Cm(M7)", "Cmmaj7", "Cm△")]
    [InlineData("C(maj9)", "Cmaj9", null)]
    [InlineData("C7(alt)", "C7alt", "Calt")]
    [InlineData("C7(b9)/E", "C7-9/E", "C7♭9/E")]
    [InlineData("C/E(9)", "Cadd9/E", "Cadd9/E")]
    // (2) a bare number: the extension when a seventh is there, else an added tone
    [InlineData("C7(9)", "C9", "C9")]
    [InlineData("C7(13)", "C13", "C13")]
    [InlineData("C7(9,13)", "C13", "C13")]
    [InlineData("Cmaj7(9)", "Cmaj9", null)]
    [InlineData("Cm7(9)", "Cm9", "Cm9")]
    [InlineData("Cm7(11)", "Cm11", "Cm11")]
    [InlineData("C(9)", "Cadd9", "Cadd9")]
    [InlineData("C6(9)", "C69", "C6add9")]
    [InlineData("Cm6(9)", "Cm69", "Cm6add9")]
    [InlineData("C(6)", "C6", "C6")]
    [InlineData("C(7)", "C7", "C7")]
    [InlineData("Cm(7)", "Cm7", "Cm7")]
    [InlineData("C(2)", "Cadd2", "Csus2add3")]
    public void ASpelling_IsTheCanonicalChord_AndPrintsItsName(string spelling, string canonical, string? printed)
    {
        var chord = Parse(spelling);
        var expected = Parse(canonical);
        Assert.Equal(expected.Quality, chord.Quality);
        Assert.Equal(expected.Intervals, chord.Intervals);
        Assert.Equal(expected.BassStep, chord.BassStep);
        if (printed != null)
            Assert.Equal(printed, chord.PrintedSymbol(ChordSpelling.Default).Text);
        Assert.Equal(expected, chord);
    }

    [Theory]
    [InlineData("C-5", "C(b5)")]             // (5): a minor fifth is nobody's reading
    [InlineData("C7b9", "C7(b9)")]           // 'b' after a letter is the root's and the bass's
    [InlineData("C7#9", "C7(#11)")]
    [InlineData("C7(x)", "not an alteration")]
    [InlineData("C7(b9", "not closed")]
    [InlineData("C7(b9)(#11)", "one parenthesized list")]
    [InlineData("C7(b9)x", "only a '/' bass")]
    public void ARefusedSpelling_NamesTheFix(string spelling, string hint)
    {
        Assert.False(ChordStructure.TryParseChordEntry(spelling, out _, out var why));
        Assert.Contains(hint, why);
    }

    /// <summary>The older readings stay: '+' before a digit is the altered tension.</summary>
    [Theory]
    [InlineData("C+5", "Caug")]
    [InlineData("C+9", "C+9")]
    [InlineData("C7+9", "C7+9")]
    public void PlusBeforeADigit_KeepsItsReading(string spelling, string canonical)
        => Assert.Equal(Parse(canonical), Parse(spelling));

    /// <summary>The flat five on a plain triad has the parentheses as its ONLY spelling
    /// (<c>C-5</c> is refused, decision ⑸).</summary>
    [Fact]
    public void TheFlatFive_IsSpelledInParentheses()
    {
        var chord = Parse("C(b5)");
        Assert.Equal(ChordQuality.Composed, chord.Quality);
        Assert.Equal("0 4 6", string.Join(" ", chord.Intervals));
        Assert.Equal("-5", chord.Formula!.Source);
    }

    [Fact]
    public void TheCanonicalSpellingOfAnAssembledQuality_IsItsFormulaSource()
    {
        var chord = Parse("C7(b9, #11)");
        Assert.Equal(ChordQuality.Composed, chord.Quality);
        Assert.Equal("7-9+11", chord.Formula!.Source);
        Assert.Equal("c1:1.3.5.7.9-.11+", chord.ToChordMode("1"));
    }

    // ---- (a) ③ the slash bass: inversion or added note --------------------------------

    [Theory]
    [InlineData("C/E", false)]
    [InlineData("C/G", false)]
    [InlineData("C7/Bb", false)]
    [InlineData("Fmaj7/E", false)]
    [InlineData("C/D", true)]
    [InlineData("G/F", true)]
    [InlineData("Am7/G", false)]
    [InlineData("C7(b9)/Db", false)]
    public void ASlashBass_IsAnInversionWhenItIsAChordTone_ElseAdded(string spelling, bool added)
    {
        var chord = Parse(spelling);
        Assert.Equal(added, chord.BassIsAdded);
        // The twin writes LilyPond's own spelling of each: '/' for the inversion, '/+' for
        // the added bass (both print the same name there).
        Assert.Contains(added ? "/+" : "/", chord.ToChordMode("1"));
        Assert.Equal(added, chord.ToChordMode("1").Contains("/+"));
    }

    [Fact]
    public void TheWindowVoicing_MovesAnInvertedToneBelow_AndAddsABassUnderTheWholeChord()
    {
        // The window is G3 (55) up to F♯4. C/E: the E sounds once, below (LilyPond's
        // process-inversion) — E3 G3 C4.
        Assert.Equal(new[] { 52, 55, 60 }, ChordVoicing.Window(Parse("C/E")).Select(t => t.Midi));
        // C/D: D is no chord tone, so the whole chord stands over it — D3 G3 C4 E4.
        Assert.Equal(new[] { 50, 55, 60, 64 }, ChordVoicing.Window(Parse("C/D")).Select(t => t.Midi));
        // G7/B: B2 G3 D4 F4.
        Assert.Equal(new[] { 47, 55, 62, 65 }, ChordVoicing.Window(Parse("G7/B")).Select(t => t.Midi));
    }

    [Fact]
    public void ARomanDegree_ReadsTheSameSpellings()
    {
        Assert.True(ChordStructure.TryParseRomanEntry("V7(b9)", 0, 0, out var five));
        Assert.Equal(Parse("G7-9"), five);
        Assert.True(ChordStructure.TryParseRomanEntry("IIø7", 0, 0, out var two));
        Assert.Equal(Parse("Dm7-5"), two);
        Assert.True(ChordStructure.TryParseRomanEntry("IΔ7/3", 0, 0, out var one));
        Assert.Equal(Parse("Cmaj7/E"), one);
        Assert.False(one.BassIsAdded);
        Assert.True(ChordStructure.TryParseRomanEntry("I/2", 0, 0, out var over));
        Assert.True(over.BassIsAdded);
    }

    // ---- the row and the annotation: the list with spaces inside -------------------------

    private static SyntaxTree Book(string row, string music = "c'1 |")
        => SyntaxTree.Parse($$"""
            octave absolute
            part m { clef treble }
            section A {
              chords prog { {{row}} }
              m { {{music}} }
            }
            form { A }
            score { staff m  chords prog }
            """);

    [Theory]
    [InlineData("C7(b9, #11) |", "C7(b9,#11)", "")]
    [InlineData("C7(b9 #11) |", "C7(b9,#11)", "")]
    [InlineData("C7(b9)(x32310) |", "C7(b9)", "x32310")]
    [InlineData("C7(b9, #11)(x32310 guitar x3231x) |", "C7(b9,#11)", "x32310 guitar x3231x")]
    [InlineData("C7(x32310) |", "C7", "x32310")]
    [InlineData("C(0003) |", "C", "0003")]
    [InlineData("C(9) |", "C(9)", "")]
    [InlineData("Cm7(b5)/Gb |", "Cm7(b5)/Gb", "")]
    [InlineData("B♭Δ7 C°7 |", "B♭Δ7", "")]
    public void AChordsRowEntry_KeepsItsAlterationListInTheSymbol_AndItsShapesApart(
        string row, string symbol, string shapes)
    {
        var tree = Book(row);
        Assert.False(tree.HasErrors, string.Join(" | ", tree.Diagnostics.Select(d => d.Message)));
        var entry = tree.GetRoot().DescendantNodes<ChordEntrySyntax>().First();
        Assert.Equal(symbol, entry.SymbolText);
        Assert.Equal(shapes, string.Join(" ", entry.ShapeWords.Select(w => w.Text)));
        Assert.True(ChordStructure.TryParseChordEntry(entry.SymbolText, out _));
    }

    [Fact]
    public void TheGlyphs_AreNoUnexpectedCharacters_InARowOrAnAnnotation()
    {
        var tree = Book("B♭Δ7 | C°7 | F♯ø7 | C△ |", "c'1@chord(B♭°7) | c'1@chord(C△7) | c'1@chord(C7(♭9, ♯11)) | c'1 |");
        Assert.DoesNotContain(tree.Diagnostics, d => d.Code == DiagnosticCodes.UnexpectedCharacter);
        Assert.False(tree.HasErrors, string.Join(" | ", tree.Diagnostics.Select(d => d.Message)));
    }

    [Theory]
    [InlineData("@chord(C7(b9, #11))", "C7(b9,#11)", "")]
    [InlineData("@chord(C7(b9 #11) x32310)", "C7(b9,#11)", "x32310")]
    [InlineData("@chord(Cm7(b5) x3x343)", "Cm7(b5)", "x3x343")]
    [InlineData("@chord(C7(9) x32310 ukulele 0001)", "C7(9)", "x32310 ukulele 0001")]
    public void AChordAnnotation_ReadsTheListAsOneWord(string mark, string symbol, string shapes)
    {
        var tree = Book("C |", $"c'1{mark} |");
        Assert.False(tree.HasErrors, string.Join(" | ", tree.Diagnostics.Select(d => d.Message)));
        var node = tree.GetRoot().DescendantNodes<MusicMarkSyntax>().First(n => n.Name == "chord");
        var words = ChordAnnotation.Of(node)!;
        Assert.Equal(symbol, words.Symbol);
        Assert.NotNull(words.Structure);
        Assert.Empty(words.Problems);
        Assert.Equal(shapes, string.Join(" ", words.Shapes.Select(s => (s.TuningName is { } t ? t + " " : "") + s.Shape)));
        // The spans follow the same words.
        var spans = ChordAnnotation.WordSpans(node);
        Assert.Equal(words.Shapes.Length + (words.Shapes.Any(s => s.TuningName != null) ? 1 : 0) + 1, spans.Count);
        Assert.Equal(mark.IndexOf('(') + 1 + node.SourceStart, spans[0].Start);
    }

    [Fact]
    public void AShapeChordItem_ReadsTheListToo()
    {
        var tree = Book("C |", "chord(C7(b9) x32310)1 |");
        Assert.False(tree.HasErrors, string.Join(" | ", tree.Diagnostics.Select(d => d.Message)));
        var item = tree.GetRoot().DescendantNodes<ChordSyntax>().First(c => c.IsShapeChord);
        Assert.Equal(new[] { "C7(b9)", "x32310" }, item.ShapeArguments.Select(a => a.Text));
        var words = ShapeChords.Words(item);
        Assert.Equal(Parse("C7-9"), words.Structure);
    }

    [Fact]
    public void TheRowPrintsTheCanonicalName_AndPlaysTheChord()
    {
        var tree = Book("C7(b9, #11) | Cø7 | B♭Δ7/D |");
        var spec = LilySharp.Core.Svg.Collector.RenderSpecParser.FindFirst(tree)!;
        var score = new LilySharp.Core.Svg.Collector.MeasureCollector().CollectMultiStaff(tree, spec);
        var names = score.ChordNames.OrderBy(c => c.MeasureIndex).Select(c => c.ChordText).ToArray();
        Assert.Equal(new[] { "C7♭9♯11", "Cø", "B♭△/D" }, names);
        Assert.All(score.ChordNames, c => Assert.NotNull(c.Structure));
    }

    // ---- (a) ④ MusicXML: the degrees of an assembled quality -------------------------------

    private static XDocument Export(string row)
    {
        var tree = Book(row);
        Assert.False(tree.HasErrors, string.Join(" | ", tree.Diagnostics.Select(d => d.Message)));
        return new MusicXmlExporter().Export(tree).ToXml();
    }

    private static string Degrees(XElement harmony)
        => string.Join(" ", harmony.Elements("degree").Select(d =>
            $"{d.Element("degree-value")!.Value}{(int.Parse(d.Element("degree-alter")!.Value) switch { < 0 => "b", > 0 => "#", _ => "" })}{d.Element("degree-type")!.Value[0]}"));

    [Theory]
    [InlineData("C7(b9) |", "dominant", "9ba")]
    [InlineData("C7(b9, #11) |", "dominant", "9ba 11#a")]
    [InlineData("C9sus4 |", "suspended-fourth", "7a 9a")]
    [InlineData("C7omit3 |", "dominant", "3s")]
    [InlineData("Cm7(b5) |", "half-diminished", "")]
    [InlineData("C7(#5) |", "augmented-seventh", "")]
    [InlineData("C(b5) |", "major", "5ba")]
    [InlineData("C69 |", "major", "6a 9a")]
    [InlineData("C5 |", "power", "")]
    [InlineData("Cmaj7+11 |", "major-seventh", "11#a")]
    [InlineData("C7 |", "dominant", "")]
    public void AnAssembledQuality_IsABaseKindAndItsDegrees(string row, string kind, string degrees)
    {
        var harmony = Export(row).Descendants("harmony").First();
        Assert.Equal(kind, harmony.Element("kind")!.Value);
        Assert.Equal(degrees, Degrees(harmony));
        if (degrees.Length > 0 || kind is "half-diminished" or "augmented-seventh" or "power")
            Assert.NotNull(harmony.Element("kind")!.Attribute("text"));
    }

    [Fact]
    public void TheDegrees_ComeBackAsTheAlterationList()
    {
        var tree = Book("C7(b9, #11) | Cm7(b5) | C(add9) | C7omit3 |", "c'1 | c'1 | c'1 | c'1 |");
        Assert.False(tree.HasErrors, string.Join(" | ", tree.Diagnostics.Select(d => d.Message)));
        string xml = new MusicXmlExporter().Export(tree).ToXml().ToString();
        var (lys, report) = new MusicXmlImporter().Import(xml);
        foreach (var expected in new[] { "@chord(C7(b9,#11))", "@chord(Cm7-5)", "@chord(C(add9))", "@chord(C7(omit3))" })
            Assert.True(lys.Contains(expected), $"{expected} not in:\n{lys}\n{xml}");
        Assert.DoesNotContain("has no Lily# entry spelling", string.Join("; ", report.Warnings));
    }
}
