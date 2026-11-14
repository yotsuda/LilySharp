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
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests.MusicXml;

/// <summary>
/// A repeat sign or an ending written in one part's music is the score's, and the MusicXML
/// writes it in EVERY part's same measure — with the ending's bracket and the <c>:|*N</c> count
/// (docs/inline-repeat-design.md §3.2). MEASURED before (2026-11-14, Lab
/// sessions/p1243/stage0.md): only the writing part carried the repeat, and no ending and no
/// count was written at all.
/// </summary>
[Trait("Category", "Unit")]
public sealed class InlineRepeatMusicXmlTests
{
    private static XDocument Export(string m, string b) => new MusicXmlExporter().Export(SyntaxTree.Parse($$"""
        octave absolute
        time 4/4
        part m { section S { {{m}} } }
        part b { section S { {{b}} } }
        form { S }
        score { staff m staff b }
        """)).ToXml();

    /// <summary>Each part's repeats and endings, a measure at a time: "2:forward 3:1.start 3:1.stop 3:backward".</summary>
    private static string[] Signs(XDocument doc) => [.. doc.Descendants("part").Select(part => string.Join(" ",
        part.Elements("measure").SelectMany(measure => measure.Descendants("barline").SelectMany(bar =>
            bar.Elements().Where(e => e.Name.LocalName is "repeat" or "ending").Select(e => measure.Attribute("number")!.Value + ":"
                + (e.Name.LocalName == "repeat"
                    ? e.Attribute("direction")!.Value + (e.Attribute("times") is { } times ? "*" + times.Value : "")
                    : e.Attribute("number")!.Value + "." + e.Attribute("type")!.Value))))))];

    [Fact]
    public void EveryPartCarriesTheRepeatOnePartWrites()
    {
        var signs = Signs(Export("c''1 |: d''1 | e''1 :| f''1 |", "c1 | d1 | e1 | f1 |"));
        Assert.Equal("2:forward 3:backward", signs[0]);
        Assert.Equal(signs[0], signs[1]);
    }

    [Fact]
    public void TheEndingsAndTheCountAreWritten_InEveryPart()
    {
        var signs = Signs(Export("|: c''1 | [1. d''1 :| [2. e''1 ] | f''1 |", "c1 | d1 | e1 | f1 |"));
        Assert.Equal("1:forward 2:1.start 2:1.stop 2:backward 3:2.start 3:2.stop", signs[0]);
        Assert.Equal(signs[0], signs[1]);

        var counted = Signs(Export("|: c''1 :|*3 d''1 |", "c1 | d1 |"));
        Assert.Equal("1:forward 1:backward*3", counted[0]);
        Assert.Equal(counted[0], counted[1]);
    }

    /// <summary>A chords row's signs are the score's too. MEASURED before (2026-11-14, Lab
    /// sessions/p1244/probe/f-chords, i-chordvolta, u-rowpart): none of them was written.</summary>
    [Fact]
    public void ARowsRepeatsAndEndingsAreWritten_InEveryPart()
    {
        var signs = Signs(new MusicXmlExporter().Export(SyntaxTree.Parse("""
            octave absolute
            time 4/4
            part m { section S { c''1 | d''1 | e''1 | f''1 | } }
            part b { section S { c1 | d1 | e1 | f1 | } }
            chords prog { section S { |: C | [1. G | Dm :|*3 [2. G ] | } }
            form { S }
            score { chords prog staff m staff b }
            """)).ToXml());
        Assert.Equal("1:forward 2:1.start 3:1.stop 3:backward*3 4:2.start 4:2.stop", signs[0]);
        Assert.Equal(signs[0], signs[1]);

        // A lead sheet: the row is its own part, and carries its own signs.
        var lead = Signs(new MusicXmlExporter().Export(SyntaxTree.Parse("""
            time 4/4
            chords prog { section S { C |: G | Dm :| G | } }
            form { S }
            score { chords prog }
            """)).ToXml());
        Assert.Equal("2:forward 3:backward", Assert.Single(lead));
    }

    /// <summary>A navigation mark written in one part's music is written in every part: a
    /// target at the head of the bar after it, a jump or a fine at the end of the bar before
    /// it. MEASURED before (2026-11-14, Lab sessions/p1244/probe/e-nav): none was written.</summary>
    [Fact]
    public void ANavigationMarkInTheMusicIsWritten_InEveryPart()
    {
        var doc = Export("c''1 | segno d''1 | to coda e''1 | f''1 ds al coda | coda g''1 |", "c1 | d1 | e1 | f1 | g1 |");
        string[] marks = [.. doc.Descendants("part").Select(part => string.Join(" ",
            part.Elements("measure").SelectMany(measure => measure.Elements("direction")
                .Where(d => d.Element("sound") is { } sound && sound.Attribute("tempo") == null)
                .Select(d => measure.Attribute("number")!.Value
                    + (d.ElementsBeforeSelf("note").Any() ? ":end:" : ":head:")
                    + d.Element("sound")!.Attributes().First().Name.LocalName))))];
        Assert.Equal("2:head:segno 2:end:tocoda 4:end:dalsegno 5:head:coda", marks[0]);
        Assert.Equal(marks[0], marks[1]);
    }

    /// <summary>An ending the next one follows with no <c>:|</c> between them goes back all the
    /// same (session 1257; <c>SyntaxFacts.EndingGoesBackUnsaid</c>): its last measure carries
    /// the backward repeat, in every part — the document of <c>[2. e1 | ] [3. …</c> is the
    /// document of <c>[2. e1 :| [3. …</c>.</summary>
    [Fact]
    public void AnEndingFollowedByAnotherWritesItsBackwardRepeat()
    {
        const string B = "c1 | d1 | e1 | f1 | g1 |";
        static string Signs(XDocument doc) => string.Join(" ", doc.Descendants("part").Select(p =>
            string.Join(",", p.Elements("measure").Select((m, i) => (m, i))
                .Where(x => x.m.Descendants("repeat").Any(r => (string?)r.Attribute("direction") == "backward"))
                .Select(x => x.i + 1))));
        string unsaid = Signs(Export("|: c''1 | [1. d''1 :| [2. e''1 | ] [3. f''1 | ] g''1 |", B));
        Assert.Equal("2,3 2,3", unsaid);
        Assert.Equal(Signs(Export("|: c''1 | [1. d''1 :| [2. e''1 :| [3. f''1 | ] g''1 |", B)), unsaid);
    }
}
