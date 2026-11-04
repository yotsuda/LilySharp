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
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests.MusicXml;

/// <summary>
/// The lines a MusicXML file draws freely and a Lily# book must pair: a slur between two voices, a
/// wedge's stop, a pedal's <c>discontinue</c>, an una corda never released, and a line over a repeat.
/// </summary>
[Trait("Category", "Unit")]
public class MusicXmlImportSpanTests
{
    private static string Note(string step, int voice = 1, string? slur = null, int number = 1, int staff = 1) =>
        $"<note><pitch><step>{step}</step><octave>4</octave></pitch><duration>1</duration><voice>{voice}</voice>"
        + $"<type>quarter</type><staff>{staff}</staff>"
        + (slur == null ? "" : $"<notations><slur type=\"{slur}\" number=\"{number}\"/></notations>")
        + "</note>";

    private static string Direction(string inner, int? staff = null) =>
        $"<direction><direction-type>{inner}</direction-type>{(staff is { } s ? $"<staff>{s}</staff>" : "")}</direction>";

    private const string Back = "<backup><duration>4</duration></backup>";
    private const string Wedge = "<wedge type=\"crescendo\"/>";
    private const string WedgeStop = "<wedge type=\"stop\"/>";
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

    private static (string Lys, ImportReport Report) Import(string xml)
    {
        var (lys, report) = new MusicXmlImporter().Import(xml);
        return (lys, report);
    }

    private static string[] Errors(string lys) =>
        SemanticValidation.Run(SyntaxTree.Parse(lys)).Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.Message).ToArray();

    private static string RepeatAt(bool end) => end
        ? "<barline location=\"right\"><repeat direction=\"backward\"/></barline>"
        : "<barline location=\"left\"><repeat direction=\"forward\"/></barline>";

    [Fact]
    public void ADiscontinuedPedalIsReleased()
    {
        var (lys, _) = Import(Book(
            Direction("<pedal type=\"start\" line=\"yes\" sign=\"yes\"/>") + Note("C") + Note("D")
            + Direction("<pedal type=\"discontinue\" line=\"yes\" sign=\"no\"/>") + Note("E") + Note("F")));
        Assert.Contains("c'4@sustain d'4 e'4@!sustain f'4", lys.Replace("\r", ""));
        Assert.Empty(Errors(lys));
    }

    [Fact]
    public void AnUnaCordaNeverReleasedStaysTheWordsOnThePage()
    {
        var (lys, _) = Import(Book(
            Direction("<words>una corda</words>") + Note("C") + Note("D") + Note("E") + Note("F"),
            Direction("<words>una corda</words>") + Note("C") + Note("D") + Note("E") + Note("F")));
        Assert.DoesNotContain("@unaCorda", lys);
        Assert.Equal(2, lys.Split("@text(\"una corda\")").Length - 1);
        Assert.Empty(Errors(lys));
    }

    [Fact]
    public void AnUnaCordaWithItsReleaseStaysThePedal()
    {
        var (lys, _) = Import(Book(
            Direction("<words>una corda</words>") + Note("C") + Note("D")
            + Direction("<words>tre corde</words>") + Note("E") + Note("F")));
        Assert.Contains("@unaCorda", lys);
        Assert.Contains("@treCorde", lys);
        Assert.DoesNotContain("@text", lys);
    }

    [Fact]
    public void AWedgeStopEndsTheHairpinOnTheLastNoteBeforeIt()
    {
        var (lys, _) = Import(Book(
            Direction(Wedge) + Note("C") + Note("D") + Direction(WedgeStop) + Note("E") + Note("F"),
            Note("G") + Note("A") + Note("B") + Direction(Forte) + Note("C")));
        Assert.Contains("c'4@cresc d'4@!cresc e'4", lys);
    }

    [Fact]
    public void AWedgeStopAtTheBarsEndIncludesTheBarsLastNote()
    {
        var (lys, _) = Import(Book(
            Direction(Wedge) + Note("C") + Note("D") + Note("E") + Note("F") + Direction(WedgeStop),
            Note("G") + Note("A") + Note("B") + Note("C")));
        Assert.Contains("f'4@!cresc |", lys);
    }

    [Fact]
    public void AWedgeStoppingOnADynamicNeedsNoEndOfItsOwn()
    {
        var (lys, _) = Import(Book(
            Direction(Wedge) + Note("C") + Note("D") + Direction(WedgeStop) + Direction(Forte) + Note("E") + Note("F")));
        Assert.DoesNotContain("@!cresc", lys);
        Assert.Contains("e'4@f", lys);
    }

    [Fact]
    public void AWedgeADynamicHasAlreadyEndedWritesNoStrayEnd()
    {
        var (lys, _) = Import(Book(
            Direction(Wedge) + Note("C") + Direction(Forte) + Note("D") + Note("E") + Direction(WedgeStop) + Note("F")));
        Assert.DoesNotContain("@!cresc", lys);
    }

    [Fact]
    public void AWedgeUnderOneNoteIsOneNotesHairpin()
    {
        var (lys, _) = Import(Book(
            Direction(Wedge) + Note("C") + Direction(WedgeStop) + Note("D") + Note("E") + Note("F")));
        Assert.Contains("c'4@cresc@!cresc d'4", lys);
    }

    [Fact]
    public void AnEndedHairpinComesBackFromLyscXmlOnItsNote()
    {
        const string source = """
            octave absolute
            time 4/4
            part a { clef treble }
            section A {
              a { c'4@cresc d' e' f'@!cresc | g'1 | }
            }
            form { ~A }
            score { staff a }
            """;
        var xml = new MusicXmlExporter().Export(SyntaxTree.Parse(source)).ToXml();
        var (lys, _) = Import(xml.ToString());
        Assert.Contains("f'4@!cresc |", lys);
    }

    [Fact]
    public void ASlurBetweenTwoVoicesIsDroppedAndReported()
    {
        // Voice 2 is written after voice 1, but its start sounds before voice 1's stop.
        var (lys, report) = Import(Book(
            Note("C") + Note("D", slur: "stop") + Note("E") + Note("F") + Back
            + Note("G", voice: 2, slur: "start") + Note("A", voice: 2) + Note("B", voice: 2) + Note("C", voice: 2)));
        Assert.DoesNotContain("4(", lys);
        Assert.DoesNotContain("4)", lys);
        Assert.Contains(report.Warnings, w => w.Contains("from voice 2 to voice 1"));
    }

    [Fact]
    public void ASlurBetweenTwoVoicesDoesNotPairWithAnUnrelatedEnd()
    {
        // The stray start in voice 1 must not swallow voice 1's own later slur.
        var (lys, _) = Import(Book(
            Note("C", slur: "start") + Note("D") + Note("E") + Note("F") + Back
            + Note("G", voice: 2) + Note("A", voice: 2, slur: "stop") + Note("B", voice: 2) + Note("C", voice: 2),
            Note("C", slur: "start") + Note("D", slur: "stop") + Note("E") + Note("F")));
        Assert.Contains("c'4 d'4 e'4 f'4 | c'4( d'4)", lys);
    }

    [Fact]
    public void ASlurInOneVoiceStaysAndASecondNumberStaysAPhrasingSlur()
    {
        var (lys, report) = Import(Book(
            Note("C", slur: "start") + Note("D", slur: "stop") + Note("E", slur: "start", number: 2) + Note("F", slur: "stop", number: 2)));
        Assert.Contains("c'4( d'4) e'4@phrasingSlur f'4@!phrasingSlur", lys);
        Assert.DoesNotContain(report.Warnings, w => w.Contains("slur"));
    }

    [Fact]
    public void AnOttavaOverARepeatEndsBeforeItAndStartsAgainAfter()
    {
        var (lys, report) = Import(Book(
            Direction("<octave-shift type=\"down\" size=\"8\"/>") + Note("C") + Note("D") + Note("E") + Note("F"),
            RepeatAt(end: false) + Note("G") + Note("A") + Direction("<octave-shift type=\"stop\" size=\"8\"/>") + Note("B") + Note("C") + RepeatAt(end: true)));
        Assert.Contains("f'4@!8va", lys);
        Assert.Contains("g'4@8va", lys);
        Assert.Contains("a'4@!8va", lys);
        Assert.Empty(Errors(lys));
        Assert.Contains(report.Warnings, w => w.Contains("section edge"));
    }

    [Fact]
    public void APedalReleasedOnTheFirstNoteAfterARepeatIsReleasedBeforeIt()
    {
        var (lys, _) = Import(Book(
            Direction("<pedal type=\"start\" sign=\"yes\"/>") + Note("C") + Note("D") + Note("E") + Note("F"),
            RepeatAt(end: false) + Direction("<pedal type=\"stop\" sign=\"yes\"/>") + Note("G") + Note("A") + Note("B") + Note("C") + RepeatAt(end: true)));
        Assert.Contains("f'4@!sustain", lys);
        Assert.DoesNotContain("g'4@", lys);
        Assert.Empty(Errors(lys));
    }

    [Fact]
    public void AHairpinOpenedOnTheNoteBeforeARepeatStartsAfterIt()
    {
        var (lys, _) = Import(Book(
            Note("C") + Note("D") + Note("E") + Direction(Wedge) + Note("F"),
            RepeatAt(end: false) + Note("G") + Direction(WedgeStop) + Note("A") + Note("B") + Note("C") + RepeatAt(end: true)));
        Assert.Contains("e'4 f'4", lys);
        Assert.DoesNotContain("f'4@cresc", lys);
        Assert.Empty(Errors(lys));
    }

    [Fact]
    public void AnOttavaEndsInTheVoiceItOpenedIn()
    {
        var (lys, _) = Import(Book(
            Direction("<octave-shift type=\"down\" size=\"8\"/>") + Note("C") + Note("D") + Note("E") + Note("F") + Back
            + Note("C", voice: 2) + Note("D", voice: 2) + Note("E", voice: 2) + Note("F", voice: 2)
            + Direction("<octave-shift type=\"stop\" size=\"8\"/>")));
        Assert.Contains("c'4@8va d'4 e'4 f'4@!8va", lys);
        Assert.Empty(Errors(lys));
    }

    [Fact]
    public void ASlurOverARepeatEndsOnTheLastPitchedNoteAndStartsAgain()
    {
        var (lys, _) = Import(Book(
            Note("C") + Note("D", slur: "start") + Note("E") + "<note><rest/><duration>1</duration><voice>1</voice><type>quarter</type><staff>1</staff></note>",
            RepeatAt(end: false) + "<note><rest/><duration>1</duration><voice>1</voice><type>quarter</type><staff>1</staff></note>"
            + Note("G") + Note("A", slur: "stop") + Note("B") + RepeatAt(end: true)));
        Assert.Contains("d'4( e'4) r4", lys);
        Assert.Contains("r4 g'4( a'4) b'4", lys);
        Assert.Empty(Errors(lys));
    }

    [Fact]
    public void ALineMayCrossOnePlainSectionEdgeButNotRunThroughTheNextSection()
    {
        var (lys, _) = Import(Book(
            Direction("<pedal type=\"start\" sign=\"yes\"/>") + Note("C") + Note("D") + Note("E") + Note("F"),
            Direction("<rehearsal>B</rehearsal>") + Note("G") + Note("A") + Note("B") + Note("C"),
            Direction("<rehearsal>C</rehearsal>") + Note("D") + Note("E") + Direction("<pedal type=\"stop\" sign=\"yes\"/>") + Note("F") + Note("G")));
        // Over the first mark it is carried; it may not run through that whole section.
        Assert.Contains("c'4@sustain d'4 e'4 f'4 |", lys);
        Assert.Contains("c'4@!sustain", lys);
        Assert.Contains("@sustain e'4", lys);
        Assert.Contains("f'4@!sustain", lys);
        Assert.Empty(SemanticValidation.Run(SyntaxTree.Parse(lys)).Where(d => d.Message.Contains("pedal")));
    }

    [Fact]
    public void AnEndedHairpinLetsARehearsalMarkStartASection()
    {
        var (lys, _) = Import(Book(
            Direction(Wedge) + Note("C") + Note("D") + Direction(WedgeStop) + Note("E") + Note("F"),
            Direction("<rehearsal>B</rehearsal>") + Note("G") + Note("A") + Note("B") + Note("C"),
            Direction("<rehearsal>C</rehearsal>") + Note("D") + Note("E") + Note("F") + Note("G")));
        Assert.Contains("section B", lys);
    }

    [Fact]
    public void ANoteEndingOneSlurAndStartingTheNextReadsBothHoweverTheyAreWritten()
    {
        const string both = "<note><pitch><step>D</step><octave>4</octave></pitch><duration>1</duration><voice>1</voice><type>quarter</type>"
            + "<staff>1</staff><notations><slur type=\"start\" number=\"1\"/><slur type=\"stop\" number=\"1\"/></notations></note>";
        var (lys, report) = Import(Book(Note("C", slur: "start") + both + Note("E", slur: "stop") + Note("F")));
        Assert.Contains("c'4( d'4)( e'4)", lys);
        Assert.DoesNotContain(report.Warnings, w => w.Contains("slur"));
    }

    [Fact]
    public void AStopOnANoteEndsTheSlurBeforeItNotTheOneThatNoteStarts()
    {
        const string both = "<note><pitch><step>D</step><octave>4</octave></pitch><duration>1</duration><voice>1</voice><type>quarter</type>"
            + "<staff>1</staff><notations><slur type=\"start\" number=\"1\"/><slur type=\"stop\" number=\"1\"/></notations></note>";
        var (lys, _) = Import(Book(Note("C", slur: "start") + both + Note("E") + Note("F")));
        Assert.Contains("c'4( d'4) e'4", lys);
    }

    [Fact]
    public void AStopTakesTheStartInItsOwnVoiceBeforeAnotherVoices()
    {
        // Voice 1 opens first, and its slur goes on to another voice; voice 2's own slur stays.
        var (lys, _) = Import(Book(
            Note("C", slur: "start") + Note("D") + Note("E") + Note("F") + Back
            + Note("G", voice: 2, slur: "start") + Note("A", voice: 2, slur: "stop") + Note("B", voice: 2, slur: "stop") + Note("C", voice: 2)));
        Assert.Contains("g'4( a'4)", lys);
    }

    [Fact]
    public void EachHandsOttavaEndsInThatHand()
    {
        static string Staffed(string s, int voice, int staff) => Note(s, voice: voice, staff: staff);
        string Bar(int staff, int voice) => Staffed("C", voice, staff) + Staffed("D", voice, staff) + Staffed("E", voice, staff) + Staffed("F", voice, staff);
        string up(int staff) => Direction("<octave-shift type=\"down\" size=\"8\"/>", staff);
        string end(int staff) => Direction("<octave-shift type=\"stop\" size=\"8\"/>", staff);
        var (lys, _) = Import(Book(
            up(1) + Bar(1, 1) + end(1) + Back + up(2) + Bar(2, 2) + end(2),
            Bar(1, 1) + Back + Bar(2, 2)));
        Assert.Equal(2, lys.Split("c'4@8va d'4 e'4 f'4@!8va").Length - 1);
        Assert.Empty(Errors(lys));
    }

    [Fact]
    public void AWedgeBesideAWordInOneDirectionIsRead()
    {
        var (lys, _) = Import(Book(
            "<direction><direction-type><words>dim.</words></direction-type><direction-type><wedge type=\"diminuendo\"/></direction-type></direction>"
            + Note("C") + Note("D")
            + "<direction><direction-type><words>a</words></direction-type><direction-type><wedge type=\"stop\"/></direction-type></direction>"
            + Note("E") + Note("F")));
        Assert.Contains("@decresc", lys);
        Assert.Contains("d'4@!decresc", lys);
    }

    [Fact]
    public void AHairpinTheNextOneEndsStopsInTheFile()
    {
        const string source = """
            octave absolute
            time 4/4
            part a { clef treble }
            section A {
              a { c'4@cresc d' e'@decresc f' | g'1@p | }
            }
            form { ~A }
            score { staff a }
            """;
        var wedges = new MusicXmlExporter().Export(SyntaxTree.Parse(source)).ToXml().Descendants("wedge")
            .Select(w => (string?)w.Attribute("type")).ToArray();
        Assert.Equal(new[] { "crescendo", "stop", "diminuendo", "stop" }, wedges);
    }

    [Fact]
    public void ALaterVoicesMarksAreWritten()
    {
        const string source = """
            octave absolute
            time 4/4
            part a { clef treble }
            section A {
              a {
                voice { c''4 d'' e'' f'' | g''1 | a''1 | }
                { c'4@p d'@cresc e' f'@!cresc | g'1@sustain@8va@text("x") | a'1@!sustain@!8va | }
              }
            }
            form { ~A }
            score { staff a }
            """;
        var xml = new MusicXmlExporter().Export(SyntaxTree.Parse(source)).ToXml();
        Assert.Single(xml.Descendants("dynamics"));
        Assert.Equal(2, xml.Descendants("wedge").Count());
        Assert.Equal(2, xml.Descendants("pedal").Count());
        Assert.Equal(2, xml.Descendants("octave-shift").Count());
        Assert.Contains(xml.Descendants("words"), w => w.Value == "x");
        // The wedge's stop stands at the later voice's f, on the bar's last beat.
        var (lys, _) = Import(xml.ToString());
        Assert.Empty(Errors(lys));
        Assert.Contains("@!cresc", lys);
    }

    [Fact]
    public void ATremolosMarksComeBackOnceOnTheirNotes()
    {
        const string source = """
            octave absolute
            time 4/4
            part a { clef treble }
            section A {
              a { repeat tremolo 4 { c'16@sustain e'16@cresc } g'2@!sustain@f | g'1 | }
            }
            form { ~A }
            score { staff a }
            """;
        var xml = new MusicXmlExporter().Export(SyntaxTree.Parse(source)).ToXml();
        Assert.Single(xml.Descendants("pedal").Where(p => (string?)p.Attribute("type") == "start"));
        var wedge = xml.Descendants("wedge").Single(w => (string?)w.Attribute("type") == "crescendo");
        // On the body's second note, which now stands halfway through the span of two quarters.
        int divisions = int.Parse(xml.Descendants("divisions").First().Value);
        Assert.Equal(divisions, int.Parse(wedge.Parent!.Parent!.Element("offset")!.Value));
        var (lys, _) = Import(xml.ToString());
        Assert.Contains("repeat tremolo 4 { c'16@sustain e'16@cresc }", lys);
    }

    [Fact]
    public void ASlurOnAChordMemberStartsWithItsChord()
    {
        const string member = "<note><chord/><pitch><step>G</step><octave>4</octave></pitch><duration>1</duration><voice>1</voice>"
            + "<type>quarter</type><staff>1</staff><notations><slur type=\"start\" number=\"1\"/></notations></note>";
        var (_, report) = Import(Book(Note("C") + member + Note("D", slur: "stop") + Note("E") + Note("F")));
        Assert.DoesNotContain(report.Warnings, w => w.Contains("slur"));
    }

    [Fact]
    public void AGraceNotesSlurEndsOnItsMainNote()
    {
        const string grace = "<note><grace/><pitch><step>B</step><octave>4</octave></pitch><voice>1</voice><type>16th</type>"
            + "<staff>1</staff><notations><slur type=\"start\" number=\"1\"/></notations></note>";
        var (lys, _) = Import(Book(grace + Note("C", slur: "stop") + Note("D") + Note("E") + Note("F")));
        Assert.Contains("b'16( }", lys);
        Assert.Contains("c'4)", lys);
    }
}
