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
using LilySharp.Core.MusicXml;
using LilySharp.Core.MusicXmlImport;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests.MusicXml;

/// <summary>
/// The guitar lines come back from MusicXML (session 1261). <c>lysc xml</c> writes a palm mute,
/// a let ring and a barre as their words and a dashed <c>&lt;bracket&gt;</c>, and
/// <c>lysc import</c> did not read the bracket: <c>e4@palmMute … b@!palmMute</c> came back as
/// <c>e4@text("P.M.").up</c> with no line, and <c>@barre(7)</c> as <c>@text("CVII").up</c>
/// (Lab sessions/p1261/gt, back0).
/// </summary>
[Trait("Category", "Unit")]
public class MusicXmlImportGuitarLineTests
{
    private static string Book(string bars)
        => "octave absolute\ntime 4/4\npart g { instrument guitar }\nsection A {\n  g {\n" + bars + "\n  }\n}\nform { A }\nscore { staff g }\n";

    private static string Xml(string src)
    {
        var tree = SyntaxTree.Parse(src);
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        return new MusicXmlExporter().Export(tree).ToXml().ToString();
    }

    // (Without the section's own mark, which the reader writes on the first note.)
    private static string Import(string xml)
        => new MusicXmlImporter().Import(xml).Item1.Replace("\r", "").Replace("@mark(\"A\")", "");

    /// <summary>Out and back, each line is the line it was.</summary>
    [Theory]
    [InlineData("e4@palmMute g a b@!palmMute | c'1 |", "e4@palmMute g4 a4 b4@!palmMute")]
    [InlineData("c'4@letRing e' g' c''@!letRing | c'1 |", "c'4@letRing e'4 g'4 c''4@!letRing")]
    [InlineData("e4@barre(7) g a b@!barre | c'1 |", "e4@barre(7) g4 a4 b4@!barre")]
    [InlineData("c'4@barre(5 half) e' g' c''@!barre | c'1 |", "c'4@barre(5 half) e'4 g'4 c''4@!barre")]
    // A barre with no end is its label on one note.
    [InlineData("e1@barre(3) | c'1 |", "e1@barre(3) |")]
    // Two lines opened on one note, each ended where it ends.
    [InlineData("e4@palmMute@letRing g a@!palmMute b@!letRing | c'1 |", "e4@palmMute@letRing g4 a4@!palmMute b4@!letRing")]
    // The same line twice in a bar.
    [InlineData("e4@palmMute g@!palmMute a@palmMute b@!palmMute | c'1 |", "e4@palmMute g4@!palmMute a4@palmMute b4@!palmMute")]
    // Each voice its own.
    [InlineData("voice { e'4@palmMute g' a' b'@!palmMute } { e2@letRing g2@!letRing } | c'1 |", "e'4@palmMute g'4 a'4 b'4@!palmMute")]
    [InlineData("voice { e'4@palmMute g' a' b'@!palmMute } { e2@letRing g2@!letRing } | c'1 |", "e2@letRing g2@!letRing")]
    public void OutAndBack_TheLineIsTheLineItWas(string bars, string expected)
    {
        string first = Xml(Book(bars));
        string back = Import(first);
        Assert.Contains(expected, back);
        // …and the book that came back says the same brackets again.
        static string Lines(string xml) => string.Concat(System.Xml.Linq.XDocument.Parse(xml).Descendants("direction")
            .Where(d => d.Descendants("bracket").Any() || d.Descendants("words").Any())
            .Select(d => d.ToString(System.Xml.Linq.SaveOptions.DisableFormatting)));
        Assert.Equal(Lines(first), Lines(Xml(back)));
    }

    private static string Foreign(string directions)
        => "<score-partwise version=\"4.0\"><part-list><score-part id=\"P1\"><part-name>P</part-name></score-part></part-list>"
           + "<part id=\"P1\"><measure number=\"1\"><attributes><divisions>1</divisions><time><beats>4</beats>"
           + "<beat-type>4</beat-type></time><clef><sign>G</sign><line>2</line></clef></attributes>" + directions
           + string.Concat(new[] { "C", "D", "E", "F" }.Select(s =>
               $"<note><pitch><step>{s}</step><octave>4</octave></pitch><duration>1</duration><voice>1</voice><type>quarter</type></note>"))
           + "</measure></part></score-partwise>";

    private static string Start(string words, int number = 1)
        => $"<direction placement=\"above\"><direction-type><words>{words}</words></direction-type>"
           + $"<direction-type><bracket type=\"start\" number=\"{number}\" line-end=\"none\"/></direction-type></direction>";

    private static string Stop(int offset, int number = 1)
        => $"<direction placement=\"above\"><direction-type><bracket type=\"stop\" number=\"{number}\" line-end=\"down\"/>"
           + $"</direction-type><offset>{offset}</offset></direction>";

    /// <summary>A palm mute a file never closes stays the words: a Lily# palm mute with no end
    /// is an error and draws nothing.</summary>
    [Fact]
    public void APalmMuteNeverClosed_StaysTheWords()
        => Assert.Contains("c'4@text(\"P.M.\").up d'4 e'4 f'4", Import(Foreign(Start("P.M."))));

    /// <summary>A bracket from other words is no guitar line: the words stay text, and its stop
    /// ends nothing.</summary>
    [Fact]
    public void ABracketFromOtherWords_IsNoGuitarLine()
    {
        string lys = Import(Foreign(Start("rasg.") + Stop(3)));
        Assert.Contains("c'4@text(\"rasg.\").up d'4 e'4 f'4", lys);
        Assert.DoesNotContain("@!", lys);
    }

    /// <summary>The stop stands where its last note ends (offset 3 = after the third quarter):
    /// the line's end is written on that note.</summary>
    [Fact]
    public void AForeignLetRing_EndsOnTheNoteItsStopFollows()
        => Assert.Contains("c'4@letRing d'4 e'4@!letRing f'4", Import(Foreign(Start("let ring", 2) + Stop(3, 2))));

    /// <summary>A stop ends the line of ITS number.</summary>
    [Fact]
    public void AStop_EndsTheLineOfItsNumber()
        => Assert.Contains("c'4@palmMute@letRing d'4@!letRing e'4@!palmMute f'4",
            Import(Foreign(Start("P.M.") + Start("let ring", 2) + Stop(2, 2) + Stop(3))));
}
