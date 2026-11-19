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
    /// (MusicMarkItem.HairpinEndsOnStaff), and two parts of the document, each read alone: a
    /// wedge stops at ITS OWN part's next level, not at the other part's the page ends the
    /// hairpin at (owner's decision 2026-11-15; from session 1240 until then it stopped where
    /// the other part's level was written).
    /// </summary>
    [Fact]
    public void OnACondensedStaff_AHairpinStopsAtItsOwnPartsLevel_NotAtTheOtherParts()
    {
        // The later part's mf on beat 2 ends the first part's crescendo on the page; in the
        // first part's own document it runs to its own f on beat 4.
        var later = Condensed("c''2.@cresc c''4@f | c''1 |", "e'4 e'4@mf e'2 | e'1 |");
        Assert.Equal(["bar 1 crescendo", "bar 1 +72 stop", "bar 1 +72 f"], Say(later, 0));
        Assert.Equal(["bar 1 +24 mf"], Say(later, 1));
        // Both parts write the f and the page prints the first part's: the second's crescendo
        // stops at its own copy.
        var earlier = Condensed("c''2@p c''2@f | c''1 |", "e'2@p@cresc e'2@f | e'1 |");
        Assert.Equal(["bar 1 p", "bar 1 +48 f"], Say(earlier, 0));
        Assert.Equal(["bar 1 p", "bar 1 crescendo", "bar 1 +48 stop", "bar 1 +48 f"], Say(earlier, 1));
    }

    /// <summary>…and its own part's next HAIRPIN and its own STOP end it as its level does:
    /// the other part's mf (bar 1) and p (bar 2), which the page ends the two at, end neither.</summary>
    [Fact]
    public void OnACondensedStaff_AHairpinStopsAtItsOwnPartsNextHairpinAndStop()
        => Assert.Equal(
            ["bar 1 crescendo", "bar 1 +72 stop", "bar 1 +72 diminuendo", "bar 2 +96 stop"],
            Say(Condensed("c''2.@cresc c''4@decresc | c''2 c''2@!decresc |", "e'4 e'4@mf e'2 | e'4@p e'2. |"), 0));

    /// <summary>With nothing in its own part to end it, the wedge stops at its bar's line — what
    /// the page draws of a hairpin nothing ends — though the other part's level (a phrase played
    /// in both bars) ends it on the page a beat in.</summary>
    [Fact]
    public void OnACondensedStaff_AHairpinNothingInItsPartEnds_StopsAtItsBarsLine()
    {
        const string head = "octave absolute\npart a { instrument violin }\npart b { instrument violin }\n"
            + "phrase lev { e'4 e'4@mf e'2 }\nsection S {\n";
        const string tail = "  b { lev | lev | }\n}\nform { ~S }\nscore { condensedStaff { a b } }\n";
        Assert.Equal(["bar 2 crescendo", "bar 2 +96 stop"],
            Say(Document(head + "  a { c''1 | c''1@cresc | }\n" + tail), 0));
        Assert.Equal(["bar 1 crescendo", "bar 1 +96 stop"],
            Say(Document(head + "  a { c''1@cresc | c''1 | }\n" + tail), 0));
    }

    /// <summary>A part with two voices on a condensed staff: the second voice's hairpin has no
    /// end in its own voice and stops where the FIRST voice of its part wrote its level — the
    /// other part's mf a beat in, which the page ends it at, does not end it.</summary>
    [Fact]
    public void OnACondensedStaff_AHairpinWithNoEndInItsVoice_StopsAtItsOwnPartsOtherVoice()
        => Assert.Equal(
            ["bar 1 +72 f", "bar 1 crescendo#3", "bar 1 +72 stop#3"],
            Say(Condensed("voice { c''2. c''4@f } { a'1@cresc } | c''1 |", "e'4 e'4@mf e'2 | e'1 |"), 0));

    /// <summary>Its own VOICE's end first: the second voice of its part writes a p under the
    /// first voice's crescendo — the page, where the staff's levels end a condensed staff's
    /// hairpins, ends it there — and the wedge runs on to its own voice's f.</summary>
    [Fact]
    public void OnACondensedStaff_AnotherVoiceOfItsPart_DoesNotEndAHairpinWithItsOwnEnd()
        => Assert.Equal(
            ["bar 1 crescendo", "bar 2 +48 p", "bar 3 stop", "bar 3 f"],
            Say(Condensed("c''1@cresc | voice { c''1 } { a'2 a'2@p } | c''1@f |", "e'1 | e'1 | e'1 |"), 0));

    /// <summary>A hairpin the carry rule cuts stops at its section's last bar line, as any
    /// part's does — not at its own part's f two sections on.</summary>
    [Fact]
    public void OnACondensedStaff_AHairpinCutAtItsSectionsEnd_StopsThere()
        => Assert.Equal(
            ["bar 1 crescendo", "bar 2 +96 stop", "bar 4 f"],
            Say(Document("""
                octave absolute
                part a { instrument violin }
                part b { instrument violin }
                section C {
                  a { c''1@cresc | c''1 || }
                  b { e'1 | e'1 || }
                }
                section D {
                  a { c''1 || }
                  b { e'1 || }
                }
                section E {
                  a { c''1@f | }
                  b { e'1 | }
                }
                form { C D E }
                score { condensedStaff { a b } }
                """), 0));

    /// <summary>
    /// The hairpin BOTH parts of a condensed staff write is printed once on the page — the
    /// later part's is left out of it (MeasureCollector.JunkCondensedStaffDuplicateDynamics) —
    /// and is each part's own wedge in the document. Until session 1266 the later part wrote
    /// its p and its f with no wedge between (Lab sessions/p1266/cs/c4, c7).
    /// </summary>
    [Fact]
    public void OnACondensedStaff_TheHairpinBothPartsWrite_IsEachPartsOwnWedge()
    {
        string[] own = ["bar 1 p", "bar 1 crescendo", "bar 1 +48 stop", "bar 1 +48 f"];
        var written = Condensed("c''2@p@cresc c''2@f | c''1 |", "e'2@p@cresc e'2@f | e'1 |");
        Assert.Equal(own, Say(written, 0));
        Assert.Equal(own, Say(written, 1));
        // One phrase played by both parts: one written mark, the earlier part's playing first.
        var phrase = Document("octave absolute\npart a { instrument violin }\npart b { instrument violin }\n"
            + "phrase swell { e'2@p@cresc e'2@f }\nsection S {\n  a { swell | e'1 | }\n  b { swell | e'1 | }\n}\n"
            + "form { ~S }\nscore { condensedStaff { a b } }\n");
        Assert.Equal(own, Say(phrase, 0));
        Assert.Equal(own, Say(phrase, 1));
    }

    /// <summary>…in every play of its section, and a hairpin carried into the next section
    /// (the diminuendo of T ends at S's p) as the earlier part's is.</summary>
    [Fact]
    public void OnACondensedStaff_TheHairpinBothPartsWrite_IsWrittenInEveryPlay()
    {
        var doc = Document("""
            octave absolute
            part a { instrument violin }
            part b { instrument violin }
            section S {
              a { c''2@p@cresc c''2@f | c''1 | }
              b { e'2@p@cresc e'2@f | e'1 | }
            }
            section T {
              a { c''2@decresc c''2 | c''1@p | }
              b { e'2@decresc e'2 | e'1@p | }
            }
            form { S T S }
            score { condensedStaff { a b } }
            """);
        string[] own = ["bar 1 p", "bar 1 crescendo", "bar 1 +48 stop", "bar 1 +48 f",
                        "bar 3 diminuendo", "bar 4 stop", "bar 4 p",
                        "bar 5 p", "bar 5 crescendo", "bar 5 +48 stop", "bar 5 +48 f"];
        Assert.Equal(own, Say(doc, 0));
        Assert.Equal(own, Say(doc, 1));
    }

    private static XDocument Combined(string fl1, string fl2)
        => Document("octave absolute\ntime 4/4\npart fl1 { clef treble }\npart fl2 { clef treble }\nsection A {\n"
            + "  fl1 { " + fl1 + " }\n  fl2 { " + fl2 + " }\n}\nform { ~A }\nscore { combinedStaff { fl1 fl2 } }\n");

    /// <summary>
    /// On a combined staff a unison's hairpin is printed once, the first part's — the second
    /// part's notes are engraved for nobody and its marks go with them
    /// (CombinedStaffReaddress.MoveMarks) — and is each part's own wedge in the document, the
    /// second part's hairpin before it stopping where its own next one starts. From session
    /// 1321, when the page began to drop those marks, to session 1325 the second part wrote no
    /// wedge for the unison's hairpin, and the stop of the one before it stood after every
    /// other direction of its bar (Lab sessions/p1325/xml s2, s3, h2, h3).
    /// </summary>
    [Theory]
    // Part two's crescendo where the parts play apart, then the unison's.
    [InlineData("c''2 d''2 | c'4 d'@cresc e' f'@!cresc | g'1 |", "e'4 f' e'@cresc f' | c'4 d'@cresc e' f'@!cresc | g'1 |",
        "bar 2 +24 crescendo, bar 2 +96 stop",
        "bar 1 +48 crescendo, bar 2 +24 stop, bar 2 +24 crescendo, bar 2 +96 stop")]
    // The unison's begins at its bar's head.
    [InlineData("c''2 d''2 | c'4@cresc d' e' f'@!cresc | g'1 |", "e'4 f' e'@cresc f' | c'4@cresc d' e' f'@!cresc | g'1 |",
        "bar 2 crescendo, bar 2 +96 stop",
        "bar 1 +48 crescendo, bar 2 stop, bar 2 crescendo, bar 2 +96 stop")]
    // The unison's is closed by a level both parts write.
    [InlineData("c''2 d''2 | c'4 d'@decresc e' f' | g'1@p |", "e'4@p f' e'@cresc f' | c'4 d'@decresc e' f' | g'1@p |",
        "bar 2 +24 diminuendo, bar 3 stop, bar 3 p",
        "bar 1 p, bar 1 +48 crescendo, bar 2 +24 stop, bar 2 +24 diminuendo, bar 3 stop, bar 3 p")]
    // A unison alone: both parts say it.
    [InlineData("c'4 d'@cresc e' f'@!cresc | g'1 |", "c'4 d'@cresc e' f'@!cresc | g'1 |",
        "bar 1 +24 crescendo, bar 1 +96 stop",
        "bar 1 +24 crescendo, bar 1 +96 stop")]
    // A start on a rest under the other part's solo, dropped from the page with its stop.
    [InlineData("c''1 | c''1 | c''1 | c''1 |", "r2 r2@cresc | r1 | e'1@!cresc | e'1 |",
        "",
        "bar 1 +48 crescendo, bar 3 +96 stop")]
    public void OnACombinedStaff_TheUnisonsHairpin_IsEachPartsOwnWedge(string fl1, string fl2, string first, string second)
    {
        var doc = Combined(fl1, fl2);
        Assert.Equal(first, string.Join(", ", Say(doc, 0)));
        Assert.Equal(second, string.Join(", ", Say(doc, 1)));
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
