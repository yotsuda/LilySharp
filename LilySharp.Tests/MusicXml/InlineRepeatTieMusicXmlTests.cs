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
/// A tie that ends a bar of a book whose MUSIC writes repeat signs reaches the first note of
/// every bar played after it — the score's sign column's answer (<c>Semantics.RepeatStream</c>,
/// docs/inline-repeat-design.md §7 stage 4) — in the MusicXML as on the page: a stop on each
/// such note, the start kept, and the hanging tie (<c>let-ring</c>) where no arc leaves the
/// tied note. MEASURED before (2026-11-15, Lab sessions/p1247/probe): the second ending's note
/// carried no stop, and a tie from the first ending back to the head was not written at all,
/// where the same music spelled with the form's signs wrote both. The expected texts are the
/// form spelling's (t-e-formtwin, u-g-firsttwin, u-i-twin).
/// </summary>
[Trait("Category", "Unit")]
public sealed class InlineRepeatTieMusicXmlTests
{
    private static XDocument Export(string m, string b, string form = "form { S }") => new MusicXmlExporter().Export(SyntaxTree.Parse($$"""
        octave absolute
        time 4/4
        part m { {{m}} }
        part b { {{b}} }
        {{form}}
        score { staff m staff b }
        """)).ToXml();

    private static XDocument OneSection(string m, string b)
        => Export($"section S {{ {m} }}", $"section S {{ {b} }}");

    /// <summary>The first part's ties, a note at a time: "2:d5:start 3:d5:stop 3:c5:let-ring".</summary>
    private static string Ties(XDocument doc) => string.Join(" ",
        doc.Descendants("part").First().Elements("measure").SelectMany(measure => measure.Elements("note")
            .SelectMany(note => note.Descendants("tied").Select(tied => measure.Attribute("number")!.Value + ":"
                + note.Element("pitch")!.Element("step")!.Value.ToLowerInvariant()
                + note.Element("pitch")!.Element("octave")!.Value + ":" + tied.Attribute("type")!.Value))));

    /// <summary>Out of the body into both endings: the first ending's note takes the arc's stop
    /// (it is printed next), the second ending's a stop of its own.</summary>
    [Theory]
    [InlineData("|: c''1 | d''1~ | [1. d''1 :| [2. d''1 ] | c''1 |", "c1 | d1 | e1 | f1 | g1 |")]
    // …whichever part writes the signs.
    [InlineData("c''1 | d''1~ | d''1 | d''1 | c''1 |", "|: c1 | d1 | [1. e1 :| [2. f1 ] | g1 |")]
    public void ATieOutOfTheBodyStopsOnBothEndings(string m, string b)
        => Assert.Equal("2:d5:start 3:d5:stop 4:d5:stop", Ties(OneSection(m, b)));

    [Fact]
    public void ATieOutOfTheBodyStopsOnEachOfThreeEndings()
        => Assert.Equal("2:d5:start 3:d5:stop 4:d5:stop 5:d5:stop",
            Ties(OneSection("|: c''1 | d''1~ | [1. d''1 :| [2. d''1 :| [3. d''1 ] | c''1 |", "c1 | d1 | e1 | f1 | g1 | a1 |")));

    /// <summary>Out of the first ending back to the head: the head's note takes the stop, and
    /// the tied note — no arc leaves it, the bar printed next is the second ending — the hanging tie.</summary>
    [Fact]
    public void ATieOutOfTheFirstEndingStopsOnTheHead()
        => Assert.Equal("1:c5:stop 3:c5:start 3:c5:let-ring",
            Ties(OneSection("|: c''1 | d''1 | [1. c''1~ :| [2. e''1 ] | c''1 |", "c1 | d1 | e1 | f1 | g1 |")));

    /// <summary>The second ending is printed after the first and never played after it: a tie
    /// out of the first ending does not reach it (the flow of the written notes joined them
    /// before the column was asked), and the head it does return to is another pitch — no tie.</summary>
    [Fact]
    public void TheFirstEndingsTieDoesNotReachTheSecondEnding()
        => Assert.Equal("",
            Ties(OneSection("|: c''1 | [1. d''1~ :| [2. d''1 ] | c''1 |", "c1 | d1 | e1 | g1 |")));

    /// <summary>A chord ties the heads the target repeats, bar by bar: the f is not in the
    /// second ending's chord.</summary>
    [Fact]
    public void AChordTiesTheHeadsEachEndingRepeats()
        => Assert.Equal("2:d5:start 2:f5:start 3:d5:stop 3:f5:stop 4:d5:stop",
            Ties(OneSection("|: c''1 | <d'' f''>1~ | [1. <d'' f''>1 :| [2. <d'' g''>1 ] | c''1 |", "c1 | d1 | e1 | f1 | g1 |")));

    /// <summary>A section boundary inside a repeat the music writes: the tie over it is the
    /// plain arc, as it was.</summary>
    [Fact]
    public void ATieOverASectionBoundaryInsideTheRepeatIsThePlainArc()
        => Assert.Equal("2:d5:start 3:d5:stop",
            Ties(Export("section A { |: c''1 | d''1~ } section B { d''1 :| e''1 | }",
                "section A { c1 | d1 | } section B { e1 | f1 | }", "form { A B }")));

    /// <summary>A plain repeat in the music, the tie at its end: back to the head (a stop
    /// there, where the pitch is the tied one) and on to the bar printed next (the arc).</summary>
    [Fact]
    public void ATieAtTheRepeatsEndReachesTheHeadAndTheNextBar()
        => Assert.Equal("1:d5:stop 2:d5:start 3:d5:stop",
            Ties(OneSection("|: d''1 | d''1~ :| d''1 | c''1 |", "c1 | d1 | e1 | f1 |")));

    /// <summary>A book with no form reads its sections in declaration order: the same column.</summary>
    [Fact]
    public void ABookWithNoFormIsReadTheSameWay()
        => Assert.Equal("2:d5:start 3:d5:stop 4:d5:stop",
            Ties(Export("section S { |: c''1 | d''1~ | [1. d''1 :| [2. d''1 ] | c''1 | }",
                "section S { c1 | d1 | e1 | f1 | g1 | }", form: "")));
}
