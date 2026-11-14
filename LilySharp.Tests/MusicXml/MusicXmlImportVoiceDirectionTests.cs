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
using LilySharp.Core.MusicXml;
using LilySharp.Core.MusicXmlImport;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests.MusicXml;

/// <summary>
/// A direction is its voice's (session 1259). <c>lysc xml</c> writes every direction at its
/// bar's head with an offset, before the first voice's notes, so a later voice's hairpin or
/// dynamic came back on the FIRST voice's note at its beat (Lab sessions/p1240/xml/x1:
/// <c>c''1@cresc@decresc</c>), and a wedge's stop closed the latest wedge open on its staff
/// whatever its number. Now the writer says a later voice's direction's <c>&lt;voice&gt;</c>,
/// the reader marks a note of that voice, and a stop ends the wedge of its number.
/// </summary>
[Trait("Category", "Unit")]
public class MusicXmlImportVoiceDirectionTests
{
    private static string Note(string step, int voice = 1) =>
        $"<note><pitch><step>{step}</step><octave>4</octave></pitch><duration>1</duration><voice>{voice}</voice>"
        + "<type>quarter</type></note>";

    private static string Direction(string inner, int offset = 0, int? voice = null) =>
        $"<direction><direction-type>{inner}</direction-type>"
        + (offset > 0 ? $"<offset>{offset}</offset>" : "")
        + (voice is { } v ? $"<voice>{v}</voice>" : "") + "</direction>";

    private static string Wedge(string type, int? number = null) =>
        $"<wedge type=\"{type}\"{(number is { } n ? $" number=\"{n}\"" : "")}/>";

    private const string Back = "<backup><duration>4</duration></backup>";
    private const string Forte = "<dynamics><f/></dynamics>";

    private static string Book(params string[] measures)
    {
        var body = string.Concat(measures.Select((m, i) => $"<measure number=\"{i + 1}\">"
            + (i == 0 ? "<attributes><divisions>1</divisions><time><beats>4</beats><beat-type>4</beat-type></time>"
                + "<clef><sign>G</sign><line>2</line></clef></attributes>" : "")
            + m + "</measure>"));
        return "<score-partwise version=\"4.0\"><part-list><score-part id=\"P1\"><part-name>P</part-name></score-part></part-list>"
            + $"<part id=\"P1\">{body}</part></score-partwise>";
    }

    private static string Import(string xml) => new MusicXmlImporter().Import(xml).Item1.Replace("\r", "");

    private static string Upper => Note("C") + Note("D") + Note("E") + Note("F");
    private static string Lower => Note("G", 2) + Note("A", 2) + Note("B", 2) + Note("C", 2);

    /// <summary>Every direction at the bar's head, as <c>lysc xml</c> writes them: the second
    /// voice's hairpin and its stop are the second voice's, and the first voice's crescendo runs
    /// on to the next bar's f.</summary>
    [Fact]
    public void ADirectionsVoice_IsTheVoiceWhoseNoteItMarks()
    {
        var lys = Import(Book(
            Direction(Wedge("crescendo")) + Direction(Wedge("diminuendo", 3), voice: 2)
                + Direction(Wedge("stop", 3), offset: 4, voice: 2) + Upper + Back + Lower,
            Direction(Wedge("stop")) + Direction(Forte) + Upper + Back + Lower));
        Assert.Contains("c'4@cresc d'4 e'4 f'4 | c'4@f", lys);
        Assert.Contains("g'4@decresc a'4 b'4 c'4@!decresc |", lys);
    }

    /// <summary>A level on the second voice's third beat, written at the bar's head: the first
    /// voice has a note on that beat too, and took it.</summary>
    [Fact]
    public void ALevelOfALaterVoice_DoesNotLandOnTheFirstVoicesNoteAtItsBeat()
    {
        var lys = Import(Book(Direction(Forte, offset: 2, voice: 2) + Upper + Back + Lower));
        Assert.Contains("c'4 d'4 e'4 f'4", lys);
        Assert.Contains("g'4 a'4 b'4@f c'4", lys);
    }

    /// <summary>A voice with no note in the bar says nothing: the direction marks the note at
    /// its moment, as one with no voice does.</summary>
    [Fact]
    public void ADirectionWhoseVoiceHasNoNoteInTheBar_MarksTheNoteAtItsMoment()
        => Assert.Contains("c'4 d'4 e'4@f f'4", Import(Book(Direction(Forte, offset: 2, voice: 2) + Upper)));

    /// <summary>The stop's number names its wedge. Each wedge stands before its own voice's note
    /// here, as other programs write them; the first voice's stop used to close the second
    /// voice's diminuendo — the latest one open — and left the crescendo with no end.</summary>
    [Fact]
    public void AWedgesStop_EndsTheWedgeOfItsNumber()
    {
        var lys = Import(Book(
            Direction(Wedge("crescendo", 1)) + Note("C") + Note("D") + Direction(Wedge("stop", 1)) + Note("E") + Note("F")
                + Back + Direction(Wedge("diminuendo", 2)) + Lower + Direction(Wedge("stop", 2)),
            Upper + Back + Lower));
        Assert.Contains("c'4@cresc d'4@!cresc e'4 f'4", lys);
        Assert.Contains("g'4@decresc a'4 b'4 c'4@!decresc", lys);
    }

    /// <summary>A stop whose number no open wedge carries still ends the latest one open, as
    /// every stop did.</summary>
    [Fact]
    public void AStopUnderANumberNoOpenWedgeCarries_EndsTheLatestOneOpen()
        => Assert.Contains("c'4@cresc d'4@!cresc e'4", Import(Book(
            Direction(Wedge("crescendo")) + Note("C") + Note("D") + Direction(Wedge("stop", 4)) + Note("E") + Note("F"),
            Upper)));

    private const string Head = "octave absolute\ntime 4/4\n";

    private static XDocument Document(string src)
        => XDocument.Parse(new MusicXmlExporter().Export(SyntaxTree.Parse(src)).ToXml().ToString());

    private static string OneStaff(string bars)
        => Head + "part m { clef treble }\nsection A { m {\n" + bars + "\n} }\nform { A }\nscore { staff m }\n";

    /// <summary>What <c>lysc xml</c> says: a later voice's direction names its voice, numbered
    /// as that voice's notes are, and the first voice's names none.</summary>
    [Fact]
    public void TheDocument_NamesTheVoiceOfALaterVoicesDirections()
    {
        var doc = Document(OneStaff(
            "voice { c''1@cresc } { c'2@decresc c'2@!decresc } { e'2 e'2@p } | voice { c''1@f } { c'1 } { e'1 } |"));
        var said = doc.Descendants("direction")
            .Where(d => d.Descendants("wedge").Any() || d.Descendants("dynamics").Any())
            .Select(d => (d.Descendants("wedge").FirstOrDefault() is { } w
                    ? (string?)w.Attribute("type") : d.Descendants("dynamics").Elements().First().Name.LocalName)
                + ":" + ((string?)d.Element("voice") ?? "-"));
        Assert.Equal(["crescendo:-", "diminuendo:2", "stop:2", "p:3", "stop:-", "f:-"], said);
        // Schema order: offset, voice, staff.
        var stop = doc.Descendants("direction").First(d => d.Element("offset") != null && d.Element("voice") != null);
        Assert.Equal(["direction-type", "offset", "voice"], stop.Elements().Select(e => e.Name.LocalName));
    }

    /// <summary>A stop is its START's voice's, wherever it is written: the first voice's
    /// crescendo ends at the second voice's f, and its stop — written as that f is met, in the
    /// second voice's stream — names no voice, as its start does.</summary>
    [Fact]
    public void TheDocument_AStopWrittenInAnotherVoicesStream_IsItsStartsVoices()
    {
        var doc = Document(OneStaff("voice { c''4@cresc c'' c'' c'' } { c'2 c'2@f } |"));
        Assert.Equal(["crescendo:-", "stop:-"], doc.Descendants("direction").Where(d => d.Descendants("wedge").Any())
            .Select(d => (string?)d.Descendants("wedge").First().Attribute("type") + ":" + ((string?)d.Element("voice") ?? "-")));
        Assert.Equal("2", (string?)doc.Descendants("direction").Single(d => d.Descendants("dynamics").Any()).Element("voice"));
    }

    /// <summary>A grand staff's lower staff: its voices count on from 5, and so do its
    /// directions' (the second voice of the left hand is 6).</summary>
    [Fact]
    public void TheDocument_CountsTheLowerStaffsVoicesOnFromFive()
    {
        var doc = Document(Head + "part rh { clef treble }\npart lh { clef bass }\nsection A {\n rh { c''1 | }\n"
            + " lh { voice { e1 } { c,2@cresc c,2@!cresc } | }\n}\nform { A }\nscore { grandStaff { staff rh staff lh } }\n");
        Assert.Equal(["6", "6"], doc.Descendants("direction").Where(d => d.Descendants("wedge").Any())
            .Select(d => (string?)d.Element("voice")));
        Assert.Contains("6", doc.Descendants("note").Select(n => (string?)n.Element("voice")));
    }

    /// <summary>The way out and back: each voice's hairpins and levels come back in its voice.</summary>
    [Theory]
    // The report: Lab sessions/p1240/xml/x1 (c''1@cresc@decresc before).
    [InlineData("voice { c''1@cresc } { c'2@decresc c'2@!decresc } | voice { c''1@f } { c'1 } |",
        "c''1@cresc | c''1@f |", "c'2@decresc c'2@!decresc | c'1 |")]
    [InlineData("voice { c''2 c''2 } { c'2 c'2@f } |", "c''2 c''2 |", "c'2 c'2@f |")]
    [InlineData("voice { c''2 c''2@cresc } { c'4@decresc c'4 c'4@!decresc c'4 } | voice { c''1@!cresc } { c'1 } |",
        "c''2 c''2@cresc | c''1@!cresc |", "c'4@decresc c'4 c'4@!decresc c'4 | c'1 |")]
    public void OutAndBack_EachVoiceKeepsItsOwn(string bars, string first, string second)
    {
        var lys = Import(Document(OneStaff(bars)).ToString());
        Assert.Contains(first, lys.Replace("@mark(\"A\")", ""));
        Assert.Contains(second, lys);
    }

    [Fact]
    public void OutAndBack_TheLeftHandsSecondVoiceKeepsItsOwn()
    {
        var lys = Import(Document(Head + "part rh { clef treble }\npart lh { clef bass }\nsection A {\n"
            + " rh { c''1@cresc | c''1@f | }\n"
            + " lh { voice { e1@decresc } { c,2@cresc c,2@!cresc } | voice { e1@p } { c,1 } | }\n}\n"
            + "form { A }\nscore { grandStaff { staff rh staff lh } }\n").ToString());
        Assert.Contains("c''1@cresc | c''1@f |", lys.Replace("@mark(\"A\")", ""));
        Assert.Contains("e1@decresc | e1@p |", lys);
        Assert.Contains("c,2@cresc c,2@!cresc | c,1 |", lys);
    }
}
