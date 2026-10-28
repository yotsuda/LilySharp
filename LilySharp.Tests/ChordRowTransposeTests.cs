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
using LilySharp.Core.LilyPond;
using LilySharp.Core.MusicXml;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A chord row that is <c>for</c> a transposing part names the chords that part's player reads
/// on the page (owner's report 2026-10-12: a B♭ trumpet part in G major under F6 and B♭7). The
/// track is sounding pitch — the MIDI plays it so — and the names move as the part does on this
/// page: the capo's pressed-name road (ChordStructure.Pressed), spelled in the key it prints.
/// Since 2026-10-28 (owner's decision, Semantics.ChordTrackPitch) the row says so with `for`;
/// the staff it stands above decides nothing, and a row with no `for` prints as written.
/// </summary>
[Trait("Category", "Unit")]
public class ChordRowTransposeTests
{
    private const string Book = """
        pitch concert
        key f major
        part tp { instrument trumpet }
        part ax { instrument alto-sax }
        part pn { instrument piano-right }
        section A {
          chords prog { F6 | Bb7 | Bdim7 | Gm7 C7 | }
          tp { a'1 | aes'1 | b'1 | bes'2 e'2 | }
          ax { f'1 | f'1 | f'1 | f'2 e'2 | }
          pn { c''1 | d''1 | d''1 | d''2 c''2 | }
        }
        form { ~A }
        score { chords prog  staff pn }
        score trumpet pitch transposed { chords prog for tp  staff tp }
        score alto pitch transposed { chords prog for ax  staff ax }
        score mixed { chords prog for tp  staff tp as pitch transposed  staff pn }
        score conductor { chords prog for tp  staff tp }
        score sheet pitch transposed { chords prog }
        score plain pitch transposed { chords prog  staff tp }
        score apart pitch transposed { staff pn  chords prog for tp }
        """;

    private static string Names(string score)
    {
        var tree = SyntaxTree.Parse(Book);
        var spec = RenderSpecParser.FindByName(tree, score) ?? RenderSpecParser.FindFirst(tree)!;
        return string.Join(" ", SvgGenerator.CollectScore(tree, spec).ChordNames
            .OrderBy(c => c.MeasureIndex).ThenBy(c => c.Timing.ToDouble()).Select(c => c.ChordText));
    }

    [Theory]
    // The B♭ trumpet reads a major second up, in G: B°7 becomes C♯°7, not D♭°7.
    [InlineData("trumpet", "G6 C7 C♯°7 Am7 D7")]
    // The E♭ alto saxophone reads a major sixth up, in D.
    [InlineData("alto", "D6 G7 G♯°7 Em7 A7")]
    // A row for a staff printed for its player inside a sounding score moves with that staff.
    [InlineData("mixed", "G6 C7 C♯°7 Am7 D7")]
    // The row says whose reading it prints wherever it stands: here over no staff of the trumpet.
    [InlineData("apart", "G6 C7 C♯°7 Am7 D7")]
    public void ARowForATransposedPart_NamesWhatThePlayerReads(string score, string names)
        => Assert.Equal(names, Names(score).Replace("dim7", "°7"));

    [Theory]
    // A sounding page — the piano's, the trumpet printed at concert pitch, a lead sheet over no
    // staff — names the chords as they are written.
    [InlineData("")]
    [InlineData("conductor")]
    [InlineData("sheet")]
    // With no `for`, the row is the band's chords at concert pitch, even right above the trumpet's
    // staff printed for its player (until 2026-10-28 the staff below moved it).
    [InlineData("plain")]
    public void ARowWithNoReader_OrForASoundingPart_IsAsWritten(string score)
        => Assert.Equal("F6 B♭7 B°7 Gm7 C7", Names(score).Replace("dim7", "°7"));

    /// <summary>A track typed in a trumpet's letters (`for tp`, the file typing the trumpet as
    /// its player reads it): the trumpet's page prints it as written, a concert page — and the
    /// MIDI — a major second lower, and no `pitch` of the file changes what it means.</summary>
    private const string Typed = """
        pitch written
        key f major
        part tp { instrument trumpet key g major }
        part pn { instrument piano-right }
        chords tpc for tp { section A { G6 | C7 | } }
        section A {
          tp { b'1 | b'1 | }
          pn { c''1 | d''1 | }
        }
        form { ~A }
        score trumpet { chords tpc  staff tp }
        score full pitch concert { chords tpc  staff tp  staff pn }
        score band pitch concert { chords tpc for pn  staff pn }
        """;

    private static string NamesOf(string book, string score)
    {
        var tree = SyntaxTree.Parse(book);
        var spec = RenderSpecParser.FindByName(tree, score)!;
        return string.Join(" ", SvgGenerator.CollectScore(tree, spec).ChordNames
            .OrderBy(c => c.MeasureIndex).ThenBy(c => c.Timing.ToDouble()).Select(c => c.ChordText));
    }

    [Theory]
    [InlineData("trumpet", "G6 C7")]
    [InlineData("full", "F6 B♭7")]
    // The row names the piano as its reader: what sounds.
    [InlineData("band", "F6 B♭7")]
    public void ATrackTypedForATransposingPart_IsReadInItsLetters(string score, string names)
        => Assert.Equal(names, NamesOf(Typed, score));

    // One track placed twice is two rows, each with its own `for` (the first row's must not
    // reach the second: found rendering a part sheet, 2026-10-28).
    [Fact]
    public void ATrackPlacedTwice_EachRowPrintsItsOwnReading()
    {
        string book = Book + "\nscore twice pitch transposed { chords prog for tp  chords prog  staff tp }\n";
        var names = NamesOf(book, "twice").Split(' ');
        Assert.Single(names, n => n == "G6");   // the trumpeter's reading
        Assert.Single(names, n => n == "F6");   // the band's, as written
    }

    [Fact]
    public void ATrackTypedForATransposingPart_SoundsWhatItsLettersMean()
    {
        // F6 = F A C D, B♭7 = B♭ D F A♭: the MIDI plays the concert chords.
        var midi = new LilySharp.Core.Midi.MidiExporter().Export(SyntaxTree.Parse(Typed));
        var classes = midi.Tracks.Where(t => t.Name == "tpc (chords)").SelectMany(t => t.Notes)
            .GroupBy(n => n.StartTick).OrderBy(g => g.Key)
            .Select(g => string.Join(",", g.Select(n => n.Pitch % 12).Distinct().Order()))
            .ToArray();
        Assert.Equal(["0,2,5,9", "2,5,8,10"], classes);
    }

    private static IReadOnlyList<LilySharp.Core.Syntax.Diagnostic> Validate(string src)
        => LilySharp.Core.Semantics.SemanticValidation.Run(SyntaxTree.Parse(src));

    [Fact]
    public void ARowWithAReaderThatIsNoPart_IsReported()
        => Assert.Contains(Validate(Typed.Replace("chords tpc for pn", "chords tpc for nobody")),
            d => d.Code == DiagnosticCodes.ChordForUnknown);

    [Fact]
    public void TwoBlocksOfATrackForDifferentParts_AreAConflict()
    {
        Assert.Contains(Validate(Typed.Replace("form { ~A }", "chords tpc for pn { section A { C | C | } }\nform { ~A }")),
            d => d.Code == DiagnosticCodes.ChordForConflict);
        Assert.DoesNotContain(Validate(Typed), d => d.Code is DiagnosticCodes.ChordForUnknown or DiagnosticCodes.ChordForConflict);
    }

    [Fact]
    public void TheUnnamedTrack_TakesFor()
    {
        string book = Typed.Replace("chords tpc for tp", "chords for tp").Replace("chords tpc", "chords");
        Assert.Equal("tp", LilySharp.Core.Semantics.ChordTrackPitch.TargetOf(SyntaxTree.Parse(book).GetRoot(), SyntaxFacts.UnnamedChordsName));
        Assert.Equal("F6 B♭7", NamesOf(book, "full"));
        Assert.Equal("G6 C7", NamesOf(book, "trumpet"));
    }

    [Theory]
    // The LilyPond twin moves the row with the same interval its part's music takes.
    [InlineData("trumpet", "\\new ChordNames \\transpose c d \\progChords")]
    [InlineData("alto", "\\new ChordNames \\transpose c a \\progChords")]
    [InlineData("mixed", "\\new ChordNames \\transpose c d \\progChords")]
    [InlineData("conductor", "\\new ChordNames \\progChords")]
    public void TheTwin_TransposesTheRowAsThePageDoes(string score, string row)
    {
        var tree = SyntaxTree.Parse(Book);
        var render = RenderSpecParser.FindDeclaredByName(tree, score)!.Value.Declaration;
        Assert.Contains(row, new LilyPondExporter { Score = render }.Export(tree));
    }

    /// <summary>MusicXML writes a transposing part in WRITTEN pitch with a <c>&lt;transpose&gt;</c>, and
    /// a reader adds that to the harmonies too: the row's harmonies on the trumpet's part are the
    /// chords it reads (G C C♯ A D), on a sounding part as written.</summary>
    [Theory]
    [InlineData("staff tp", "G C C1 A D")]
    [InlineData("staff pn", "F B-1 B G C")]
    public void MusicXml_WritesAPartsHarmoniesInItsWrittenPitch(string staff, string roots)
    {
        string book = Book.Replace("score { chords prog  staff pn }", "score { chords prog  " + staff + " }");
        string xml = new MusicXmlExporter().Export(SyntaxTree.Parse(book)).ToXml().ToString();
        Assert.Equal(roots, string.Join(" ", System.Text.RegularExpressions.Regex.Matches(xml,
                @"<root-step>(\w)</root-step>(?:\s*<root-alter>(-?\d)</root-alter>)?")
            .Select(m => m.Groups[1].Value + m.Groups[2].Value)));
    }
}
