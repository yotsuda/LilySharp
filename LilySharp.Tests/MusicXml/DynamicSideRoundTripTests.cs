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
using System.Xml.Linq;
using LilySharp.Core.MusicXml;
using LilySharp.Core.MusicXmlImport;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests.MusicXml;

/// <summary>
/// The side a dynamic or a hairpin stands on (session 1260), in three places that had three
/// readings. The page read the two voices of a staff as ONE stream, so a lower voice's
/// <c>@p</c> rode up onto the upper voice's <c>@f.up</c> and a lower voice's <c>.down</c> broke
/// the upper voice's line; <c>lysc xml</c> read them in the order it writes the voices, another
/// answer again; and <c>lysc import</c> read no <c>placement</c> at all, so <c>@f.up</c> came
/// back as <c>@f</c>. Now a line is its voice's in all three, and the reader writes the side
/// wherever the placement is not what the mark would get unsaid.
/// MEASURED (LilyPond 2.26.0, Lab sessions/p1260/side/ly/s4.png): one line a Voice — the upper
/// voice's f and hairpin above, the lower voice's p and mf below.
/// </summary>
[Trait("Category", "Unit")]
public class DynamicSideRoundTripTests
{
    private static string Book(string bars, bool lyrics = false)
        => "octave absolute\ntime 4/4\npart m { clef treble }\nsection A {\n  m {\n" + bars + "\n  }\n"
           + (lyrics ? "  lyrics words { la la la la | la | }\n" : "")
           + "}\nform { A }\n" + (lyrics ? "score {\n  staff m\n  lyrics words\n}\n" : "score { staff m }\n");

    private const string TwoVoices =
        "voice { e''4@f.up f'' g'' a'' } { b'4@p c'' d'' e'' } | voice { e''2@cresc.up g''@ff } { b'2@pp.down c''@mf } |";

    private static XDocument Document(string src)
    {
        var tree = SyntaxTree.Parse(src);
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        return XDocument.Parse(new MusicXmlExporter().Export(tree).ToXml().ToString());
    }

    /// <summary>Every level and opening wedge of the document with the side its direction
    /// stands on, in document order: <c>f:above</c>, <c>crescendo:below</c>.</summary>
    private static List<string> Sides(XDocument doc)
        => doc.Descendants("direction")
            .Select(d => (Side: (string?)d.Attribute("placement"),
                Name: d.Descendants("dynamics").FirstOrDefault()?.Elements().First().Name.LocalName
                    ?? d.Descendants("wedge").Select(w => (string?)w.Attribute("type")).FirstOrDefault(t => t != "stop")))
            .Where(d => d.Name != null)
            .Select(d => d.Name + ":" + d.Side)
            .ToList();

    // (Without the section's own mark, which the reader writes on the first note.)
    private static string Import(XDocument doc)
        => new MusicXmlImporter().Import(doc.ToString()).Item1.Replace("\r", "").Replace("@mark(\"A\")", "");

    /// <summary>The page: each voice's marks ride that voice's line.</summary>
    [Fact]
    public void OnThePage_AVoicesSideSaysNothingOfTheOtherVoicesMarks()
    {
        var tree = SyntaxTree.Parse(Book(TwoVoices));
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        var layout = new LayoutEngine().Layout(SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree)));
        bool Above(string text) => layout.DynamicLayouts.Single(d => d.Text == text).IsAbove;
        Assert.True(Above("f"));
        Assert.False(Above("p"));     // beside the upper voice's @f.up: it used to ride up onto it
        Assert.True(Assert.Single(layout.HairpinLayouts).IsAbove);
        Assert.True(Above("ff"));     // the upper voice's line: the lower voice's .down used to break it
        Assert.False(Above("pp"));
        Assert.False(Above("mf"));
    }

    /// <summary>The upper voice's hairpin still runs where the lower voice's first mark stands,
    /// at the same moment: the lower voice starts a line of its own.</summary>
    [Fact]
    public void OnThePage_ALaterVoiceStartsALineOfItsOwn()
    {
        var tree = SyntaxTree.Parse(Book("voice { e''1@cresc.up } { b'1@p } |"));
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        var layout = new LayoutEngine().Layout(SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree)));
        Assert.True(Assert.Single(layout.HairpinLayouts).IsAbove);
        Assert.False(Assert.Single(layout.DynamicLayouts).IsAbove);
    }

    /// <summary>The document says the page's sides.</summary>
    [Fact]
    public void TheDocument_PlacesEachVoicesMarksOnItsOwnLine()
        => Assert.Equal(
            ["f:above", "p:below", "crescendo:above", "ff:above", "pp:below", "mf:below"],
            Sides(Document(Book(TwoVoices))));

    /// <summary>Out and back, a written side is still written, and one the line gives is not.</summary>
    [Theory]
    // Each moment a line of its own.
    [InlineData("b'4@f.up c''@p e''@mf.up f''@f | c''1 |", "b'4@f.up c''4@p e''4@mf.up f''4@f")]
    // The level that ends a hairpin follows the line its start's level wrote.
    [InlineData("c''4@f.up@decresc d'' e'' g''@p | c''1 |", "c''4@f.up@decresc d''4 e''4 g''4@p")]
    // A level that breaks off the hairpin's line says so.
    [InlineData("c''4@p@cresc.up d'' e''@f.down f'' | c''1@mf |", "c''4@p@cresc.up d''4 e''4@f.down f''4 | c''1@mf")]
    // The level that ends the hairpin ends the line: the next level is the staff's again.
    [InlineData("c''4@cresc.up d''@f e''@p f'' | c''1 |", "c''4@cresc.up d''4@f e''4@p f''4")]
    // Two voices, each its own line.
    [InlineData(TwoVoices, "e''2@cresc.up g''2@ff")]
    [InlineData(TwoVoices, "b'2@pp c''2@mf")]
    public void OutAndBack_TheSidesAreTheBooks(string bars, string expected)
    {
        var first = Document(Book(bars));
        string back = Import(first);
        Assert.Contains(expected, back);
        Assert.Equal(Sides(first), Sides(Document(back)));
    }

    /// <summary>A later voice's line runs on from one <c>voice { } { }</c> to the next while its
    /// hairpin does: the level that ends it there follows the side its start wrote.</summary>
    [Fact]
    public void ALaterVoicesLine_RunsOnIntoTheNextVoiceBlock()
    {
        var first = Document(Book("voice { e''1@mp } { b'2@cresc.up c''2 } | voice { e''1@mf } { b'1@f } |"));
        Assert.Equal(["mp:below", "crescendo:above", "mf:below", "f:above"], Sides(first));
        string back = Import(first);
        Assert.Contains("b'2@cresc.up c''2 | b'1@f", back);
        Assert.Equal(Sides(first), Sides(Document(back)));
    }

    /// <summary>Over lyrics the staff's own side is above: there <c>.down</c> is what is said.</summary>
    [Fact]
    public void OverLyrics_TheSideSaidIsBelow()
    {
        var first = Document(Book("c''4@f d''@p.down e''@decresc f'' | g''1@pp |", lyrics: true));
        Assert.Equal(["f:above", "p:below", "diminuendo:above", "pp:above"], Sides(first));
        string back = Import(first);
        Assert.Contains("c''4@f d''4@p.down e''4@decresc f''4 | g''1@pp", back);
        Assert.Equal(Sides(first), Sides(Document(back)));
    }

    private static string Foreign(string directions)
        => "<score-partwise version=\"4.0\"><part-list><score-part id=\"P1\"><part-name>P</part-name></score-part></part-list>"
           + "<part id=\"P1\"><measure number=\"1\"><attributes><divisions>1</divisions><time><beats>4</beats>"
           + "<beat-type>4</beat-type></time><clef><sign>G</sign><line>2</line></clef></attributes>" + directions
           + string.Concat(new[] { "C", "D", "E", "F" }.Select(s =>
               $"<note><pitch><step>{s}</step><octave>4</octave></pitch><duration>1</duration><voice>1</voice><type>quarter</type></note>"))
           + "</measure></part></score-partwise>";

    private static string Level(string name, string? placement, int offset = 0)
        => "<direction" + (placement != null ? $" placement=\"{placement}\"" : "") + $"><direction-type><dynamics><{name}/></dynamics>"
           + "</direction-type>" + (offset > 0 ? $"<offset>{offset}</offset>" : "") + "</direction>";

    /// <summary>Another program's file: a placement above is said, one below is the staff's
    /// own, and a direction with no placement says nothing.</summary>
    [Fact]
    public void AForeignFile_SaysOnlyTheSideThatIsNotTheStaffsOwn()
    {
        string lys = new MusicXmlImporter().Import(Foreign(
            Level("f", "above") + Level("p", "below", 1) + Level("mf", null, 2))).Item1;
        Assert.Contains("c'4@f.up d'4@p e'4@mf f'4", lys);
    }
}
