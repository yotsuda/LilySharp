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
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests.MusicXml;

/// <summary>
/// The document's wedges are the page's hairpins (session 1240): each starts where the page
/// draws one and stops at what the page pairs it with — its voice's own end first, then its
/// staff's (HairpinEngraver.DetectHairpins). Until then the document paired them itself with
/// ONE open wedge for the whole file, which any level, any other hairpin and any stop closed,
/// whichever voice or part wrote it.
/// </summary>
/// <remarks>
/// Each wedge and level is said as "bar +offset what": the offset in divisions (24 a quarter,
/// omitted at the bar's head), a wedge's number after '#' where it writes one.
/// </remarks>
[Trait("Category", "Unit")]
public class MusicXmlWedgePairTests
{
    private const string Head = "octave absolute\ntime 4/4\npart m { clef treble }\nsection A { m {\n";
    private const string Tail = "\n} }\nform { A }\nscore { staff m }\n";

    private static XDocument Document(string src)
        => XDocument.Parse(new MusicXmlExporter().Export(SyntaxTree.Parse(src)).ToXml().ToString());

    /// <summary>The wedges and levels of one part, in the document's order.</summary>
    private static List<string> Say(XDocument doc, int part = 0)
    {
        var said = new List<string>();
        foreach (var measure in doc.Descendants("part").ElementAt(part).Elements("measure"))
            foreach (var direction in measure.Elements("direction"))
            {
                string at = "bar " + (string?)measure.Attribute("number")
                    + (direction.Element("offset") is { } offset ? " +" + offset.Value : "");
                foreach (var wedge in direction.Descendants("wedge"))
                    said.Add(at + " " + (string?)wedge.Attribute("type")
                        + (wedge.Attribute("number") is { } number ? "#" + number.Value : "")
                        + ((string?)wedge.Attribute("niente") == "yes" ? " niente" : ""));
                foreach (var level in direction.Descendants("dynamics").Elements())
                    said.Add(at + " " + level.Name.LocalName);
            }
        return said;
    }

    private static List<string> Bars(string bars) => Say(Document(Head + bars + Tail));

    /// <summary>The report (Lab sessions/p1239/xml/x1): the upper voice's crescendo runs to the
    /// next bar's f, and the lower voice's decrescendo — started and stopped under it — does
    /// not end it. It used to stop at the decrescendo's start, with no length.</summary>
    [Fact]
    public void AHairpinRunsThroughAnotherVoicesHairpin_ToItsOwnEnd()
        => Assert.Equal(
            ["bar 1 crescendo", "bar 1 diminuendo#3", "bar 1 +96 stop#3", "bar 2 stop", "bar 2 f"],
            Bars("voice { c''1@cresc } { c'2@decresc c'2@!decresc } | voice { c''1@f } { c'1 } |"));

    /// <summary>Two voices' hairpins at one time are two numbers: the first voice's is the
    /// reader's 1 (none written), the second's 3 — 2 is a grand staff's lower staff.</summary>
    [Fact]
    public void TwoVoicesHairpinsAtOneTime_AreNumberedApart_AndEachStopsAtItsOwnEnd()
        => Assert.Equal(
            // (The stop includes its note: the fourth eighth ends at 48.)
            ["bar 1 crescendo", "bar 1 +48 stop", "bar 1 diminuendo#3", "bar 1 +96 stop#3"],
            Bars("voice { c''8@cresc c'' c'' c''@!cresc c''2 } { c'2@decresc c'2@!decresc } |"));

    /// <summary>A level in the other voice does not end a hairpin that has its own end.</summary>
    [Fact]
    public void AnotherVoicesLevel_DoesNotEndAHairpinWithItsOwnEnd()
        => Assert.Equal(
            ["bar 1 crescendo", "bar 1 +96 stop", "bar 1 p", "bar 1 diminuendo#3", "bar 1 +48 stop#3", "bar 1 +48 pp"],
            Bars("voice { c''2@cresc c''2@!cresc } { c'2@p@decresc c'2@pp } |"));

    /// <summary>With no end in its own voice a hairpin ends at its staff's next level, as the
    /// page draws it — whichever of the two voices is written out first.</summary>
    [Fact]
    public void AHairpinWithNoEndInItsVoice_StopsAtTheStaffsNextLevel()
    {
        Assert.Equal(
            ["bar 1 crescendo", "bar 1 +48 stop", "bar 1 +48 f"],
            Bars("voice { c''4@cresc c'' c'' c'' } { c'2 c'2@f } |"));
        // The level is the FIRST voice's, written before the hairpin's start is met: the stop
        // stands at the level's offset and after its start in the document.
        Assert.Equal(
            ["bar 1 +48 f", "bar 1 crescendo#3", "bar 1 +48 stop#3"],
            Bars("voice { c''2 c''2@f } { c'4@cresc c' c' c' } |"));
        // …and in a later bar, in that bar.
        Assert.Equal(
            ["bar 1 crescendo#3", "bar 2 f", "bar 2 stop#3"],
            Bars("voice { c''2 c''2 | c''1@f } { c'4@cresc c' c' c' | c'1 } |"));
        // A third voice's level ends the second voice's hairpin.
        Assert.Equal(
            ["bar 1 crescendo#3", "bar 1 +48 stop#3", "bar 1 +48 p"],
            Bars("voice { c''1 } { c'1@cresc } { a'2 a'2@p } |"));
    }

    /// <summary>…in a span that does not open the part: the bar the level was written in is
    /// the bar the later voice's stream is at, so the stop still follows its start.</summary>
    [Fact]
    public void TheFirstVoicesLevelInALaterBarOfThePart_IsFoundInItsOwnBar()
        => Assert.Equal(
            ["bar 2 +48 f", "bar 2 crescendo#3", "bar 2 +48 stop#3"],
            Bars("c''1 | voice { c''2 c''2@f } { c'4@cresc c' c' c' } |"));

    /// <summary>The three things that end a hairpin, each at its own place: the next hairpin's
    /// start, a stop on the hairpin's own note (it spans that note), a level.</summary>
    [Fact]
    public void AHairpinStopsAtTheNextHairpin_AtItsOwnNotesStop_AtALevel()
        => Assert.Equal(
            ["bar 1 diminuendo", "bar 1 +48 stop", "bar 1 +48 crescendo", "bar 2 stop", "bar 2 f",
             "bar 3 crescendo", "bar 3 +48 stop"],
            Bars("c'2@decresc d'2@cresc | c'1@f | c'2@cresc@!cresc d'2 |"));

    /// <summary>A phrase's hairpin played twice is two hairpins of the page, each with its own
    /// end: the first runs to the f, the second — nothing after it — to its bar's line.</summary>
    [Fact]
    public void APhrasePlayedTwice_GivesEachPlayingItsOwnEnd()
        => Assert.Equal(
            ["bar 1 crescendo", "bar 2 stop", "bar 2 f", "bar 3 crescendo", "bar 3 +96 stop"],
            Say(Document("octave absolute\ntime 4/4\npart m { clef treble }\nphrase riff { c'4@cresc d' e' f' }\n"
                + "section A { m { riff | c'1@f | riff | c'1 | } }\nform { A }\nscore { staff m }\n")));

    /// <summary>…and played twice bar after bar the first stops where the second starts, as the
    /// page ends it (session 1241; the two wedges overlapped under one number before).</summary>
    [Fact]
    public void APhrasePlayedTwiceBarAfterBar_StopsTheFirstWedgeWhereTheSecondStarts()
        => Assert.Equal(
            ["bar 1 crescendo", "bar 2 stop", "bar 2 crescendo", "bar 3 stop", "bar 3 f"],
            Say(Document("octave absolute\ntime 4/4\npart m { clef treble }\nphrase riff { c'4@cresc d' e' f' }\n"
                + "section A { m { riff | riff | c'1@f | } }\nform { A }\nscore { staff m }\n")));

    /// <summary>A hairpin the carry rule cuts stops where its section's play ends — the end of
    /// the section's LAST bar, not of the bar it started in, and not at the f two sections on.</summary>
    [Fact]
    public void AHairpinCutAtItsSectionsEnd_StopsAtTheSectionsLastBarLine()
        => Assert.Equal(
            ["bar 1 +24 crescendo", "bar 2 +96 stop", "bar 4 f"],
            Say(Document("""
                octave absolute
                part vn {
                  section C { c'4 d'@cresc e' f' | g'1 || }
                  section D { g'4 a' b' c'' || }
                  section E { c''4@f d'' e'' f'' | }
                }
                form { C D E }
                score { staff vn }
                """)));

    /// <summary>A hairpin nothing ends is drawn to its bar's line, and its wedge stops there:
    /// the file used to leave it open.</summary>
    [Fact]
    public void AHairpinNothingEnds_StopsAtItsBarsLine()
        => Assert.Equal(
            ["bar 1 +24 crescendo", "bar 1 +96 stop"],
            Bars("c'4 d'@cresc e' f' | c'1 |"));

    /// <summary>A phrase played in a later voice is that voice's: its hairpin takes the voice's
    /// number, not the first voice's.</summary>
    [Fact]
    public void APhrasesHairpinInALaterVoice_TakesThatVoicesNumber()
        => Assert.Equal(
            ["bar 1 p", "bar 1 crescendo#3", "bar 2 stop#3", "bar 2 ff"],
            Say(Document("octave absolute\ntime 4/4\npart m { clef treble }\nphrase riff { c'4@cresc d' e' f' }\n"
                + "section A { m { voice { c''1@p } { riff } | c'1@ff | } }\nform { A }\nscore { staff m }\n")));

    /// <summary>The niente at a hairpin's thin end is the wedge's own <c>niente</c> and no
    /// <c>n</c>, in either written order; a niente no tip stands for is the level.</summary>
    [Fact]
    public void ANienteIsTheCircleOfTheHairpinThePagePairsItWith()
        => Assert.Equal(
            ["bar 1 crescendo niente", "bar 2 stop", "bar 2 f", "bar 2 diminuendo", "bar 3 stop niente",
             "bar 4 crescendo niente", "bar 5 stop", "bar 5 mf", "bar 6 n"],
            Bars("c'1@niente@cresc | c'1@f@decresc | c'1@niente | c'1@cresc@niente | c'1@mf | c'1@niente |"));

    private const string TwoParts = """
        octave absolute
        time 4/4
        part m { clef treble }
        part n { clef bass }
        section A {
          m { c'1@cresc | c'1 | }
          n { c1@p | c1@decresc | }
        }
        section B {
          m { c'1 | c'1@f | }
          n { c1@pp | c1 | }
        }
        form { A B }
        score { staff m staff n }
        """;

    /// <summary>A hairpin carried into the next section and ended there stops there. The other
    /// part's music is written between the two blocks, and its level used to close it — the
    /// one open wedge was the file's, not the part's.</summary>
    [Fact]
    public void AnotherPartsLevel_DoesNotEndAHairpinCarriedIntoTheNextSection()
    {
        var doc = Document(TwoParts);
        Assert.Equal(["bar 1 crescendo", "bar 4 stop", "bar 4 f"], Say(doc, 0));
        Assert.Equal(["bar 1 p", "bar 2 diminuendo", "bar 3 stop", "bar 3 pp"], Say(doc, 1));
    }

    /// <summary>Two parts play one phrase, so the level that ends the first part's hairpin is
    /// written at the same source position in the second part — and written there FIRST, in
    /// the section the hairpin is carried into. It ends its own part's hairpins only.</summary>
    [Fact]
    public void TheSameWrittenLevelInAnotherPart_DoesNotEndThisPartsHairpin()
    {
        var doc = Document("""
            octave absolute
            time 4/4
            part m { clef treble }
            part n { clef treble }
            phrase top { c''1@f }
            section A {
              m { c'1@cresc | c'1 | }
              n { c'1 | c'1 | }
            }
            section B {
              n { top | c'1 | }
              m { c'1 | top | }
            }
            form { A B }
            score { staff m staff n }
            """);
        Assert.Equal(["bar 1 crescendo", "bar 4 stop", "bar 4 f"], Say(doc, 0));
        Assert.Equal(["bar 3 f"], Say(doc, 1));
    }

    private static XDocument Condensed(string a, string b)
        => Document("octave absolute\npart a { instrument violin }\npart b { instrument violin }\n"
            + "section S {\n  a { " + a + " }\n  b { " + b + " }\n}\nform { ~S }\nscore { condensedStaff { a b } }\n");

    /// <summary>
    /// Two parts on a condensed staff are one staff of the page, whose levels end its hairpins
    /// (MusicMarkItem.HairpinEndsOnStaff), and two parts of the document: a hairpin the page
    /// ends at the OTHER part's level never meets that level in its own part. Its stop stands
    /// in its own part where the level was written — whichever of the two parts is written
    /// first — and no wedge is left open.
    /// </summary>
    [Fact]
    public void OnACondensedStaff_AHairpinEndedByTheOtherPartsLevel_StopsThereInItsOwnPart()
    {
        // The later part's mf on beat 2 ends the first part's crescendo on the page.
        var later = Condensed("c''2.@cresc c''4@f | c''1 |", "e'4 e'4@mf e'2 | e'1 |");
        Assert.Equal(["bar 1 crescendo", "bar 1 +72 f", "bar 1 +24 stop"], Say(later, 0));
        Assert.Equal(["bar 1 +24 mf"], Say(later, 1));
        // Both parts write the f and the page prints the first part's: it ends the second's.
        var earlier = Condensed("c''2@p c''2@f | c''1 |", "e'2@p@cresc e'2@f | e'1 |");
        Assert.Equal(["bar 1 p", "bar 1 +48 f"], Say(earlier, 0));
        Assert.Equal(["bar 1 p", "bar 1 crescendo", "bar 1 +48 f", "bar 1 +48 stop"], Say(earlier, 1));
    }

    /// <summary>…and where the other part plays that level more than once (a phrase played in
    /// both bars), at its first writing after the hairpin's start: not one before the start,
    /// not a later one.</summary>
    [Fact]
    public void OnACondensedStaff_TheOtherPartsLevelPlayedTwice_EndsTheHairpinAtTheFirstAfterItsStart()
    {
        const string head = "octave absolute\npart a { instrument violin }\npart b { instrument violin }\n"
            + "phrase lev { e'4 e'4@mf e'2 }\nsection S {\n";
        const string tail = "  b { lev | lev | }\n}\nform { ~S }\nscore { condensedStaff { a b } }\n";
        Assert.Equal(["bar 2 crescendo", "bar 2 +24 stop"],
            Say(Document(head + "  a { c''1 | c''1@cresc | }\n" + tail), 0));
        Assert.Equal(["bar 1 crescendo", "bar 1 +24 stop"],
            Say(Document(head + "  a { c''1@cresc | c''1 | }\n" + tail), 0));
    }

    private const string GrandStaff = """
        octave absolute
        time 4/4
        part rh { clef treble }
        part lh { clef bass }
        section A {
          rh { voice { c''1@cresc | c''1@f } { e'2@decresc e'2@!decresc | e'1 } | }
          lh { voice { c1@decresc | c1@p } { c,2 c,2@cresc | c,1@mf } | }
        }
        form { A }
        score { grandStaff { staff rh staff lh } }
        """;

    /// <summary>A grand staff's four lines: the upper staff's voices are 1 (none written) and
    /// 3, the lower staff's 2 and 4, each stop under its start's number.</summary>
    [Fact]
    public void AGrandStaffsVoices_NumberTheirWedgesApart()
        => Assert.Equal(
            ["bar 1 crescendo", "bar 1 diminuendo#3", "bar 1 +96 stop#3", "bar 1 diminuendo#2", "bar 1 +48 crescendo#4",
             "bar 2 stop", "bar 2 f", "bar 2 stop#2", "bar 2 p", "bar 2 stop#4", "bar 2 mf"],
            Say(Document(GrandStaff)));
}
