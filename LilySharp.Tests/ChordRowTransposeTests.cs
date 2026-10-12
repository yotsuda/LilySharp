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
/// A chord row over a staff that prints a transposing part for its player names the chords that
/// player reads (owner's report 2026-10-12: a B♭ trumpet part in G major under F6 and B♭7). The
/// row is sounding pitch — it belongs to no instrument, and the MIDI plays it so — and the names
/// move as the staff does: the capo's pressed-name road (ChordStructure.Pressed), spelled in the
/// key the staff prints.
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
        score trumpet pitch transposed { chords prog  staff tp }
        score alto pitch transposed { chords prog  staff ax }
        score mixed { chords prog  staff tp as pitch transposed  staff pn }
        score conductor { chords prog  staff tp }
        score sheet pitch transposed { chords prog }
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
    // A row over a staff printed for its player inside a sounding score moves with that staff.
    [InlineData("mixed", "G6 C7 C♯°7 Am7 D7")]
    public void ARowOverATransposedStaff_NamesWhatThePlayerReads(string score, string names)
        => Assert.Equal(names, Names(score).Replace("dim7", "°7"));

    [Theory]
    // A sounding page — the piano's, the trumpet printed at concert pitch, a lead sheet over no
    // staff — names the chords as they are written.
    [InlineData("")]
    [InlineData("conductor")]
    [InlineData("sheet")]
    public void ARowOverASoundingStaff_OrOverNone_IsAsWritten(string score)
        => Assert.Equal("F6 B♭7 B°7 Gm7 C7", Names(score).Replace("dim7", "°7"));

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
