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

    /// <summary>What the file says reads back: the notes keep their pitches and the clef is kept.</summary>
    [Fact]
    public void TheFileReadsBack()
    {
        var doc = Music("c'2 clef bass c' | c'1 |");
        var (lys, _) = new MusicXmlImporter().Import(doc.ToString());
        var tree = SyntaxTree.Parse(lys);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        Assert.Contains("clef bass", lys);
        Assert.Equal(
            doc.Descendants("note").Select(n => n.Element("pitch")!.Element("step")!.Value + n.Element("pitch")!.Element("octave")!.Value),
            new MusicXmlExporter().Export(tree).ToXml().Descendants("note").Select(n => n.Element("pitch")!.Element("step")!.Value + n.Element("pitch")!.Element("octave")!.Value));
    }
}
