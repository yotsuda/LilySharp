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
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests.MusicXml;

/// <summary>
/// A clef written after a bar's first notes stands where it was written in the file, not at the
/// next bar's head: an &lt;attributes&gt; in the note stream.
/// </summary>
[Trait("Category", "Unit")]
public class MusicXmlMidBarClefTests
{
    private static XDocument Export(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        return new MusicXmlExporter().Export(tree).ToXml();
    }

    private static XDocument Music(string music) => Export(
        "octave absolute\ntime 4/4\npart p { clef treble }\nsection A { p { " + music + " } }\nform { A }\nscore { staff p }\n");

    /// <summary>Each clef as "bar/moment in quarters:sign", read along the file as a reader does.</summary>
    private static string[] Clefs(XDocument doc)
    {
        var rows = new List<string>();
        int bar = 0, divisions = 1;
        foreach (var measure in doc.Descendants("measure"))
        {
            bar++;
            int at = 0;
            foreach (var el in measure.Elements())
            {
                if (el.Name == "attributes")
                {
                    if (el.Element("divisions") is { } d)
                        divisions = int.Parse(d.Value);
                    foreach (var clef in el.Elements("clef"))
                        rows.Add($"{bar}/{(double)at / divisions:0.##}:{clef.Element("sign")!.Value}");
                }
                else if (el.Name == "backup")
                    at -= int.Parse(el.Element("duration")!.Value);
                else if (el.Name == "forward")
                    at += int.Parse(el.Element("duration")!.Value);
                else if (el.Name == "note" && el.Element("chord") == null && el.Element("grace") == null)
                    at += int.Parse(el.Element("duration")!.Value);
            }
        }
        return rows.ToArray();
    }

    [Fact]
    public void AClefInTheBar_StandsWhereItWasWritten_AndIsNotRestated()
        => Assert.Equal(new[] { "1/0:G", "1/2:F" }, Clefs(Music("c'2 clef bass c' | c'1 | c'1 |")));

    /// <summary>The next bar's attributes, written for a key, say nothing of a clef already said.</summary>
    [Theory]
    [InlineData("c'2 clef bass c' | key g major c'1 |", new[] { "1/0:G", "1/2:F" })]
    [InlineData("c'2 clef bass c' | clef bass c'1 |", new[] { "1/0:G", "1/2:F" })]
    [InlineData("c'2 clef! treble c' | key g major c'1 |", new[] { "1/0:G", "1/2:G" })]
    public void TheNextBar_DoesNotRestateIt(string music, string[] clefs)
        => Assert.Equal(clefs, Clefs(Music(music)));

    [Fact]
    public void AClefAtABarHead_StaysInTheBarsOwnAttributes()
    {
        var doc = Music("c'1 | clef bass c'1 |");
        Assert.Equal(new[] { "1/0:G", "2/0:F" }, Clefs(doc));
        Assert.Equal("attributes", doc.Descendants("measure").ElementAt(1).Elements().First().Name.LocalName);
    }

    [Fact]
    public void ARestatedClef_WritesNothing_UnlessForced()
    {
        Assert.Equal(new[] { "1/0:G" }, Clefs(Music("c'2 clef treble c' |")));
        Assert.Equal(new[] { "1/0:G", "1/2:G" }, Clefs(Music("c'2 clef! treble c' |")));
    }

    [Fact]
    public void AnOctaveClef_KeepsItsOctave()
    {
        var clef = Music("c'2 clef treble_8 c' |").Descendants("clef").Last();
        Assert.Equal("-1", clef.Element("clef-octave-change")!.Value);
        Assert.Equal("2", clef.Element("line")!.Value);
    }

    [Fact]
    public void ALowerVoicesClef_StandsAtItsMoment()
        => Assert.Equal(new[] { "1/0:G", "1/2:F" }, Clefs(Music("voice { c''1 | } { c'2 clef bass c' | }")));

    [Fact]
    public void AClefInATuplet_StandsAtItsMember()
    {
        var rows = Clefs(Music("tuplet 3/2 { c'4 clef bass c' c' } c'2 |"));
        Assert.Equal("1/0:G", rows[0]);
        Assert.StartsWith("1/0.67:F", rows[1]);
    }

    [Fact]
    public void OnAGrandStaff_TheClefNamesItsStaff()
    {
        var doc = Export("octave absolute\ntime 4/4\npart rh { clef treble }\npart lh { clef bass }\n"
            + "section A { rh { c''2 clef bass c' | } lh { c2 clef treble c' | } }\nform { A }\nscore { grandStaff { staff rh\n staff lh } }\n");
        var inStream = doc.Descendants("measure").First().Elements("attributes").Skip(1).SelectMany(a => a.Elements("clef"))
            .Select(c => c.Attribute("number")!.Value + ":" + c.Element("sign")!.Value).ToArray();
        Assert.Equal(new[] { "1:F", "2:G" }, inStream);
    }

    [Fact]
    public void OnATabOnlyStaff_NoNotationClefIsWritten()
    {
        var doc = Export("octave absolute\ntime 4/4\npart g { clef treble }\nsection A { g { e'2 clef bass e' | } }\nform { A }\nscore { tab g }\n");
        Assert.DoesNotContain(doc.Descendants("clef"), c => c.Element("sign")!.Value == "F");
    }

    [Fact]
    public void BesideATabStaff_TheClefIsStaffOnes()
    {
        var doc = Export("octave absolute\ntime 4/4\npart g { clef treble }\nsection A { g { e'2 clef bass e' | } }\nform { A }\nscore { staff g\n tab g }\n");
        var clef = doc.Descendants("clef").Single(c => c.Element("sign")!.Value == "F");
        Assert.Equal("1", clef.Attribute("number")!.Value);
    }

    private static string Import(string xml)
    {
        var (lys, _) = new MusicXmlImporter().Import(xml);
        Assert.False(SyntaxTree.Parse(lys).HasErrors, lys);
        return lys;
    }

    private static string Book(params string[] measures)
        => "<score-partwise version=\"4.0\"><part-list><score-part id=\"P1\"><part-name>P</part-name></score-part></part-list><part id=\"P1\">"
           + string.Concat(measures.Select((m, i) => $"<measure number=\"{i + 1}\">" + m + "</measure>"))
           + "</part></score-partwise>";

    private const string OneStaff = "<attributes><divisions>1</divisions><time><beats>4</beats><beat-type>4</beat-type></time><clef><sign>G</sign><line>2</line></clef></attributes>";
    private const string TwoStaves = "<attributes><divisions>1</divisions><time><beats>4</beats><beat-type>4</beat-type></time><staves>2</staves>"
        + "<clef number=\"1\"><sign>G</sign><line>2</line></clef><clef number=\"2\"><sign>F</sign><line>4</line></clef></attributes>";
    private const string ToBass = "<attributes><clef><sign>F</sign><line>4</line></clef></attributes>";
    private const string ToTreble = "<attributes><clef><sign>G</sign><line>2</line></clef></attributes>";

    private static string N(string step, int staff = 0, int voice = 1)
        => $"<note><pitch><step>{step}</step><octave>4</octave></pitch><duration>2</duration><voice>{voice}</voice><type>half</type>"
           + (staff > 0 ? $"<staff>{staff}</staff>" : "") + "</note>";

    [Fact]
    public void ReadingBack_AClefInTheBar_StandsBeforeTheNoteAtItsMoment()
        => Assert.Contains("c'2 clef bass d'2 |", Import(Book(OneStaff + N("C") + ToBass + N("D"))));

    [Fact]
    public void ReadingBack_TheWholeRoundTrip_KeepsEveryClefsMoment()
    {
        var doc = Music("voice { c''2 d'' | c''1 | } { c'4 clef bass c' c'2 | clef treble c'1 | }");
        var (lys, _) = new MusicXmlImporter().Import(doc.ToString());
        var again = new MusicXmlExporter().Export(SyntaxTree.Parse(lys)).ToXml();
        Assert.Equal(Clefs(doc), Clefs(again));
    }

    [Fact]
    public void ReadingBack_AClefAfterTheLastNote_OpensTheNextBar()
        => Assert.Contains("clef bass e'1", Import(Book(
            OneStaff + N("C") + N("D") + ToBass,
            "<note><pitch><step>E</step><octave>4</octave></pitch><duration>4</duration><voice>1</voice><type>whole</type></note>")));

    [Fact]
    public void ReadingBack_AClefAfterThePiecesLastNote_IsNoMark()
        => Assert.DoesNotContain("@clef", Import(Book(OneStaff + N("C") + N("D") + ToBass)));

    [Fact]
    public void ReadingBack_ARestatedClef_WritesNothing()
        => Assert.DoesNotContain("clef treble c", Import(Book(OneStaff + N("C") + ToTreble + N("D"))).Replace("{ clef treble }", ""));

    [Fact]
    public void ReadingBack_AClefOfOneStaff_StandsOnThatStaffsPart()
    {
        const string toTrebleBelow = "<attributes><clef number=\"2\"><sign>G</sign><line>2</line></clef></attributes>";
        var lys = Import(Book(
            TwoStaves + N("C", 1, 1) + N("D", 1, 1) + "<backup><duration>4</duration></backup>" + N("E", 2, 5) + toTrebleBelow + N("F", 2, 5),
            toTrebleBelow + N("C", 1, 1) + N("D", 1, 1) + "<backup><duration>4</duration></backup>" + N("E", 2, 5) + N("F", 2, 5)));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(lys, @"clef treble [a-g]"));
        Assert.Contains("e'2 clef treble f'2", lys);
    }

    [Fact]
    public void ReadingBack_AStaffsChangeAtABarHead_IsKeptToo()
    {
        const string toTrebleBelow = "<attributes><clef number=\"2\"><sign>G</sign><line>2</line></clef></attributes>";
        var lys = Import(Book(
            TwoStaves + N("C", 1, 1) + N("D", 1, 1) + "<backup><duration>4</duration></backup>" + N("E", 2, 5) + N("F", 2, 5),
            toTrebleBelow + N("C", 1, 1) + N("D", 1, 1) + "<backup><duration>4</duration></backup>" + N("E", 2, 5) + N("F", 2, 5)));
        Assert.Contains("clef treble e'2", lys);
        Assert.DoesNotContain("clef treble c'2", lys);
    }

    [Fact]
    public void ReadingBack_AClefTheMusicCannotSpell_IsReported_NotAnError()
    {
        const string soprano = "<attributes><clef><sign>C</sign><line>1</line></clef></attributes>";
        var (lys, report) = new MusicXmlImporter().Import(Book(OneStaff + N("C") + soprano + N("D")));
        Assert.False(SyntaxTree.Parse(lys).HasErrors, lys);
        Assert.DoesNotContain("clef soprano", lys);
        Assert.Contains(report.Warnings, w => w.Contains("soprano"));
    }

    [Fact]
    public void ReadingBack_BarRestsDoNotFoldOverAClef()
    {
        const string bar = "<note><rest measure=\"yes\"/><duration>4</duration><voice>1</voice></note>";
        string first = OneStaff.Replace("</attributes>", "<measure-style><multiple-rest>3</multiple-rest></measure-style></attributes>") + bar;
        Assert.Contains("R*3", Import(Book(first, bar, bar)));
        // After the first bar's rest, so it stands at the second's.
        var withClef = Import(Book(first + ToBass, bar, bar));
        Assert.DoesNotContain("R*3", withClef);
        Assert.Contains("clef bass R", withClef);
    }

    /// <summary>What the file says reads back: the notes keep their pitches and the clef is kept.</summary>
    [Fact]
    public void TheFileReadsBack()
    {
        var doc = Music("c'2 clef bass c' | c'1 |");
        var (lys, _) = new MusicXmlImporter().Import(doc.ToString());
        var tree = SyntaxTree.Parse(lys);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        Assert.Contains("clef bass c'2", lys);
        Assert.Equal(
            doc.Descendants("note").Select(n => n.Element("pitch")!.Element("step")!.Value + n.Element("pitch")!.Element("octave")!.Value),
            new MusicXmlExporter().Export(tree).ToXml().Descendants("note").Select(n => n.Element("pitch")!.Element("step")!.Value + n.Element("pitch")!.Element("octave")!.Value));
    }
}
