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
    public void AnOttavasStopOnAVoiceThatSoundsEarlierStillEndsTheLine()
    {
        // Voice 2's one note sounds before the line opens, but is the staff's last in the stream.
        const string half = "<note><pitch><step>G</step><octave>4</octave></pitch><duration>4</duration><voice>2</voice><type>whole</type><staff>1</staff></note>";
        var (lys, _) = Import(Book(
            Note("C") + Note("D") + Direction("<octave-shift type=\"down\" size=\"8\"/>") + Note("E") + Note("F") + Back + half
            + Direction("<octave-shift type=\"stop\" size=\"8\"/>"),
            Note("C") + Note("D") + Note("E") + Note("F") + Back + half));
        Assert.Contains("e'4@8va f'4@!8va", lys);
        Assert.Empty(Errors(lys));
    }

    [Fact]
    public void AnOttavasStopOnTheOtherStaffStillEndsTheLine()
    {
        var (lys, _) = Import(Book(
            Direction("<octave-shift type=\"down\" size=\"8\"/>", 1) + Note("C") + Note("D") + Note("E") + Note("F") + Back
            + Note("C", voice: 2, staff: 2) + Note("D", voice: 2, staff: 2) + Note("E", voice: 2, staff: 2) + Note("F", voice: 2, staff: 2)
            + Direction("<octave-shift type=\"stop\" size=\"8\"/>", 2)));
        Assert.Contains("c'4@8va d'4 e'4 f'4@!8va", lys);
    }

    [Fact]
    public void AnOttavasStopEndsItsOwnHandsLineThoughTheOtherOpenedLater()
    {
        string up(int staff) => Direction("<octave-shift type=\"down\" size=\"8\"/>", staff);
        string end(int staff) => Direction("<octave-shift type=\"stop\" size=\"8\"/>", staff);
        var (lys, _) = Import(Book(
            up(1) + Note("C") + Note("D") + Note("E") + Note("F") + end(1) + Back
            + Note("C", voice: 2, staff: 2) + up(2) + Note("D", voice: 2, staff: 2) + Note("E", voice: 2, staff: 2) + Note("F", voice: 2, staff: 2),
            Note("C") + Note("D") + Note("E") + Note("F") + Back
            + Note("C", voice: 2, staff: 2) + end(2) + Note("D", voice: 2, staff: 2) + Note("E", voice: 2, staff: 2) + Note("F", voice: 2, staff: 2)));
        Assert.Contains("c'4@8va d'4 e'4 f'4@!8va", lys);
        Assert.Contains("c'4 d'4@8va e'4 f'4 | c'4@!8va d'4", lys.Replace("\r", "").Replace("\n", " ").Replace("  ", " "));
        Assert.Empty(Errors(lys));
    }

    [Fact]
    public void AnOttavasEndWithNoStartIsDroppedAndReported()
    {
        var (lys, report) = Import(Book(Note("C") + Note("D") + Direction("<octave-shift type=\"stop\" size=\"8\"/>") + Note("E") + Note("F")));
        Assert.DoesNotContain("8va", lys);
        Assert.Contains(report.Warnings, w => w.Contains("octave line's end"));
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

    private static string Tied(string step, string tie, int voice = 1, bool chord = false, string alter = "") =>
        $"<note>{(chord ? "<chord/>" : "")}<pitch><step>{step}</step>{alter}<octave>4</octave></pitch><duration>1</duration>"
        + string.Concat(tie.Split(',', System.StringSplitOptions.RemoveEmptyEntries).Select(t => $"<tie type=\"{t}\"/>"))
        + $"<voice>{voice}</voice><type>quarter</type><staff>1</staff></note>";

    [Fact]
    public void ATieInOneVoiceStays_ChainedToo()
    {
        var (lys, report) = Import(Book(Tied("C", "start") + Tied("C", "stop,start") + Tied("C", "stop") + Tied("D", "")));
        Assert.Contains("c'4~ c'4~ c'4 d'4", lys);
        Assert.DoesNotContain(report.Warnings, w => w.Contains("tie"));
    }

    [Fact]
    public void ATieBetweenTwoVoicesIsDroppedAndReported()
    {
        var (lys, report) = Import(Book(
            Tied("C", "") + Tied("D", "") + Tied("E", "") + Tied("F", "start") + Back
            + Tied("G", "", voice: 2) + Tied("A", "", voice: 2) + Tied("B", "", voice: 2) + Tied("C", "", voice: 2),
            Tied("G", "") + Tied("A", "") + Tied("B", "") + Tied("C", "") + Back
            + Tied("F", "stop", voice: 2) + Tied("A", "", voice: 2) + Tied("B", "", voice: 2) + Tied("C", "", voice: 2)));
        Assert.DoesNotContain("4~", lys);
        Assert.Contains(report.Warnings, w => w.Contains("tie runs from voice 1 to voice 2"));
    }

    [Fact]
    public void ATieInItsOwnVoiceIsNotTakenByAnother()
    {
        // Voice 1's F is held into the next bar in voice 1; voice 2's F starts later and goes nowhere.
        var (lys, _) = Import(Book(
            Tied("C", "") + Tied("D", "") + Tied("E", "") + Tied("F", "start") + Back
            + Tied("G", "", voice: 2) + Tied("A", "", voice: 2) + Tied("B", "", voice: 2) + Tied("F", "start", voice: 2),
            Tied("F", "stop") + Tied("A", "") + Tied("B", "") + Tied("C", "")));
        Assert.Equal(1, lys.Split("4~").Length - 1);
        Assert.Contains("f'4~ |", lys);
    }

    [Fact]
    public void ATiesStopTakesTheStartInItsOwnVoiceThoughAnotherOpenedFirst()
    {
        var (lys, _) = Import(Book(
            Tied("C", "") + Tied("D", "") + Tied("F", "start") + Tied("E", "") + Back
            + Tied("G", "", voice: 2) + Tied("A", "", voice: 2) + Tied("B", "", voice: 2) + Tied("F", "start", voice: 2),
            Tied("C", "") + Tied("D", "") + Tied("E", "") + Tied("F", "") + Back
            + Tied("F", "stop", voice: 2) + Tied("A", "", voice: 2) + Tied("B", "", voice: 2) + Tied("C", "", voice: 2)));
        Assert.Contains("b'4 f'4~ |", lys);
        Assert.Equal(1, lys.Split("4~").Length - 1);
    }

    [Fact]
    public void ATieOnAChordsFirstHeadAloneTiesThatHead()
    {
        var (lys, _) = Import(Book(
            Tied("C", "start") + Tied("E", "", chord: true) + Tied("C", "stop") + Tied("E", "", chord: true) + Tied("F", "") + Tied("G", "")));
        Assert.Contains("<c'~ e'>4 <c' e'>4", lys);
        Assert.Empty(Errors(lys));
    }

    [Fact]
    public void ATieWithNoEndIsDroppedAndReported()
    {
        var (lys, report) = Import(Book(Tied("C", "start") + Tied("D", "") + Tied("E", "") + Tied("F", "")));
        Assert.DoesNotContain("4~", lys);
        Assert.Contains(report.Warnings, w => w.Contains("tie has no end"));
    }

    [Fact]
    public void ATieOnOneHeadOfAChordIsWrittenOnThatHead()
    {
        var (lys, _) = Import(Book(
            Tied("C", "") + Tied("E", "start", chord: true) + Tied("C", "") + Tied("E", "stop", chord: true) + Tied("F", "") + Tied("G", "")));
        Assert.Contains("<c' e'~>4 <c' e'>4", lys);
        Assert.Empty(Errors(lys));
    }

    [Fact]
    public void ATieOnEveryHeadOfAChordIsWrittenAfterIt()
    {
        var (lys, _) = Import(Book(
            Tied("C", "start") + Tied("E", "start", chord: true) + Tied("C", "stop") + Tied("E", "stop", chord: true) + Tied("F", "") + Tied("G", "")));
        Assert.Contains("<c' e'>4~ <c' e'>4", lys);
    }

    [Fact]
    public void ATieBetweenTwoSpellingsOfOnePitchIsDroppedAndReported()
    {
        const string sharp = "<alter>1</alter>", flat = "<alter>-1</alter>";
        var (lys, report) = Import(Book(Tied("A", "start", alter: sharp) + Tied("B", "stop", alter: flat) + Tied("E", "") + Tied("F", "")));
        Assert.Contains("ais'4 bes'4", lys);
        Assert.Contains(report.Warnings, w => w.Contains("two spellings"));
    }

    [Fact]
    public void ATieOverAnotherNoteOfItsVoiceIsDroppedAndReported()
    {
        var (lys, report) = Import(Book(Tied("E", "start") + Tied("C", "") + Tied("E", "stop") + Tied("F", "")));
        Assert.Contains("e'4 c'4 e'4", lys);
        Assert.Contains(report.Warnings, w => w.Contains("passes over"));
    }

    private static string Grace(string step, bool slur = false) => $"<note><grace/><pitch><step>{step}</step><octave>4</octave></pitch><voice>1</voice><type>16th</type>"
        + "<staff>1</staff>" + (slur ? "<notations><slur type=\"start\" number=\"1\"/></notations>" : "") + "</note>";

    [Fact]
    public void GraceNotesAtABarsEndLeadToTheNextBarsNote()
    {
        var (lys, _) = Import(Book(
            Note("C") + Note("D") + Note("E") + Note("F") + Grace("A"),
            Note("G") + Note("A") + Note("B") + Note("C")));
        Assert.Contains("f'4 | grace { a'16 } g'4", lys.Replace("\n", " ").Replace("\r", "").Replace("  ", " "));
    }

    [Fact]
    public void GraceNotesAfterThePartsLastNoteAreReported()
    {
        var (_, report) = Import(Book(Note("C") + Note("D") + Note("E") + Note("F") + Grace("A")));
        Assert.Contains(report.Warnings, w => w.Contains("grace notes after"));
    }

    [Fact]
    public void AGraceGroupsSlurIntoItsMainNoteOpensOnItsLastNote()
    {
        var (lys, _) = Import(Book(Grace("A", true) + Grace("B", false) + Note("C", slur: "stop") + Note("D") + Note("E") + Note("F")));
        Assert.Contains("b'16( }", lys);
        Assert.Contains("c'4)", lys);
    }

    private static string Sung(string step, string text, string syllabic = "single", int verse = 1, int voice = 1, string slur = "", string extra = "")
    {
        string notations = slur.Length == 0 ? "" : $"<notations><slur type=\"{slur}\" number=\"1\"/></notations>";
        string lyric = text.Length == 0 ? "" : $"<lyric number=\"{verse}\"><syllabic>{syllabic}</syllabic><text>{text}</text>{extra}</lyric>";
        return $"<note><pitch><step>{step}</step><octave>4</octave></pitch><duration>1</duration><voice>{voice}</voice><type>quarter</type><staff>1</staff>{notations}{lyric}</note>";
    }

    private static string LyricsLine(string lys) =>
        lys.Replace("\r", "").Split('\n').First(l => l.TrimStart().StartsWith("lyrics words", System.StringComparison.Ordinal) && l.Contains('{'));

    [Fact]
    public void ANoteWithNoSyllableIsHeldSoTheNextSyllableStaysOnItsNote()
    {
        var (lys, _) = Import(Book(Sung("C", "la") + Sung("D", "") + Sung("E", "li") + Sung("F", "")));
        Assert.Contains("{ la ~ li | }", LyricsLine(lys));
    }

    [Fact]
    public void ABarLineResyncsSoASilentBarEndWritesNoHold()
    {
        var (lys, _) = Import(Book(
            Sung("C", "la") + Sung("D", "") + Sung("E", "") + Sung("F", ""),
            Sung("G", "li") + Sung("A", "") + Sung("B", "") + Sung("C", "")));
        Assert.Contains("{ la | li | }", LyricsLine(lys));
    }

    [Fact]
    public void ASlurOverOneSyllableStaysAMelisma()
    {
        var (lys, _) = Import(Book(Sung("C", "la", slur: "start") + Sung("D", "", slur: "stop") + Sung("E", "li") + Sung("F", "lo")));
        Assert.Contains("c'4( d'4)", lys);
        Assert.Contains("{ la li lo | }", LyricsLine(lys));
    }

    [Fact]
    public void ASlurOverNotesWithTheirOwnSyllablesIsAPhrasingSlur()
    {
        var (lys, _) = Import(Book(Sung("C", "la", slur: "start") + Sung("D", "li", slur: "stop") + Sung("E", "lo") + Sung("F", "lu")));
        Assert.Contains("c'4@phrasingSlur d'4@!phrasingSlur", lys);
        Assert.DoesNotContain("4(", lys);
    }

    [Fact]
    public void ASlurOverSyllablesWhereAPhrasingSlurRunsIsDroppedAndReported()
    {
        const string phrase = "<slur type=\"start\" number=\"2\"/>";
        var first = Sung("C", "la", slur: "start").Replace("<slur type=\"start\" number=\"1\"/>", "<slur type=\"start\" number=\"1\"/>" + phrase);
        var last = Sung("E", "lo").Replace("</staff>", "</staff><notations><slur type=\"stop\" number=\"2\"/></notations>");
        var (lys, report) = Import(Book(first + Sung("D", "li", slur: "stop") + last + Sung("F", "lu")));
        Assert.DoesNotContain("4(", lys);
        Assert.Contains("e'4@!phrasingSlur", lys);
        Assert.Contains(report.Warnings, w => w.Contains("already runs"));
    }

    [Fact]
    public void ASyllableOfManyWordsIsQuoted()
    {
        var (lys, _) = Import(Book(Sung("C", "1. Rau", "begin") + Sung("D", "sche", "end") + Sung("E", "- bas,") + Sung("F", "12")));
        Assert.Contains("{ \"1. Rau\" -- sche \"- bas,\" \"12\" | }", LyricsLine(lys));
        Assert.Empty(Errors(lys));
    }

    [Fact]
    public void AVersesNumberInItsSyllableIsDroppedWhereTheRowsPrintOne()
    {
        var two = Sung("C", "1. Rau").Replace("</lyric>", "</lyric><lyric number=\"2\"><syllabic>single</syllabic><text>2. Horch</text></lyric>");
        var (lys, _) = Import(Book(two + Sung("D", "") + Sung("E", "") + Sung("F", "")));
        Assert.Contains("{ Rau | }", lys);
        Assert.Contains("{ Horch | }", lys);
    }

    [Fact]
    public void AVersesNumberOfAnotherVerseStays()
    {
        var two = Sung("C", "3. Rau").Replace("</lyric>", "</lyric><lyric number=\"2\"><syllabic>single</syllabic><text>x</text></lyric>");
        var (lys, _) = Import(Book(two + Sung("D", "") + Sung("E", "") + Sung("F", "")));
        Assert.Contains("\"3. Rau\"", lys);
    }

    [Fact]
    public void AnExtendedSyllableDrawsItsLineOverNotesItHolds()
    {
        const string extend = "<extend/>";
        var (lys, _) = Import(Book(
            Sung("C", "la", extra: extend, slur: "start") + Sung("D", "", slur: "stop") + Sung("E", "li", extra: extend) + Sung("F", "lo")));
        Assert.Contains("{ la __ li lo | }", LyricsLine(lys));
    }

    [Fact]
    public void AnElisionStaysInItsSyllable()
    {
        const string written = "<note><pitch><step>C</step><octave>4</octave></pitch><duration>1</duration><voice>1</voice><type>quarter</type><staff>1</staff>"
            + "<lyric number=\"1\"><syllabic>single</syllabic><text>E</text><elision>‿</elision><text>o</text></lyric></note>";
        const string glyph = "<note><pitch><step>D</step><octave>4</octave></pitch><duration>1</duration><voice>1</voice><type>quarter</type><staff>1</staff>"
            + "<lyric number=\"1\"><syllabic>single</syllabic><text>e</text><text>\uE551</text><text>o</text></lyric></note>";
        var (lys, _) = Import(Book(written + glyph + Sung("E", "") + Sung("F", "")));
        Assert.Contains("{ E~o e~o | }", LyricsLine(lys));
    }

    [Fact]
    public void ASyllableOnATiedNoteIsDroppedAndReported()
    {
        var tied = Tied("C", "stop").Replace("</note>", "<lyric number=\"1\"><syllabic>single</syllabic><text>li</text></lyric></note>");
        var first = Tied("C", "start").Replace("</note>", "<lyric number=\"1\"><syllabic>single</syllabic><text>la</text></lyric></note>");
        var (lys, report) = Import(Book(first + tied + Sung("E", "lo") + Sung("F", "lu")));
        Assert.Contains("{ la lo lu | }", LyricsLine(lys));
        Assert.Contains(report.Warnings, w => w.Contains("'li'"));
    }

    [Fact]
    public void ASyllableOnALowerVoiceStandsOnTheFirstVoiceAtItsMoment()
    {
        var (lys, report) = Import(Book(
            Sung("C", "") + Sung("D", "") + Sung("E", "") + Sung("F", "") + Back
            + Sung("G", "la", voice: 2) + Sung("A", "", voice: 2) + Sung("B", "li", voice: 2) + Sung("C", "", voice: 2)));
        Assert.Contains("{ la ~ li | }", LyricsLine(lys));
        Assert.Contains(report.Warnings, w => w.Contains("lower voice stand"));
    }

    [Fact]
    public void ASyllableOnALowerVoiceWithNoNoteAboveIsDroppedAndReported()
    {
        const string rest = "<note><rest/><duration>1</duration><voice>1</voice><type>quarter</type><staff>1</staff></note>";
        var (_, report) = Import(Book(
            rest + Sung("D", "") + Sung("E", "") + Sung("F", "") + Back
            + Sung("G", "la", voice: 2) + Sung("A", "", voice: 2) + Sung("B", "", voice: 2) + Sung("C", "", voice: 2)));
        Assert.Contains(report.Warnings, w => w.Contains("is dropped") && w.Contains("lower voice"));
    }

    private const string TwoSongs = """
        <score-partwise version="4.0"><part-list><score-part id="P1"><part-name>S</part-name></score-part><score-part id="P2"><part-name>T</part-name></score-part></part-list>
        <part id="P1"><measure number="1"><attributes><divisions>1</divisions><time><beats>2</beats><beat-type>4</beat-type></time><clef><sign>G</sign><line>2</line></clef></attributes>ONE</measure></part>
        <part id="P2"><measure number="1"><attributes><divisions>1</divisions><time><beats>2</beats><beat-type>4</beat-type></time><clef><sign>G</sign><line>2</line></clef></attributes>TWO</measure></part>
        </score-partwise>
        """;

    [Fact]
    public void EveryPartsWordsAreWrittenAndGoBackOnThatPart()
    {
        var (lys, _) = Import(TwoSongs.Replace("ONE", Sung("C", "so") + Sung("D", "pra")).Replace("TWO", Sung("E", "te") + Sung("F", "nor")));
        Assert.Contains("so pra", lys);
        Assert.Contains("te nor", lys);
        Assert.Empty(Errors(lys));
        Assert.Contains("lyrics words_", lys[lys.IndexOf("score {", System.StringComparison.Ordinal)..]);
        var parts = new MusicXmlExporter().Export(SyntaxTree.Parse(lys)).ToXml().Descendants("part").ToArray();
        Assert.Equal(new[] { "so", "pra" }, parts[0].Descendants("lyric").Select(l => l.Element("text")!.Value));
        Assert.Equal(new[] { "te", "nor" }, parts[1].Descendants("lyric").Select(l => l.Element("text")!.Value));
    }

    private static (string Text, string Step)[] ExportedSyllables(string source) =>
        new MusicXmlExporter().Export(SyntaxTree.Parse(source)).ToXml().Descendants("note")
            .Where(n => n.Element("lyric") != null)
            .Select(n => (n.Element("lyric")!.Element("text")!.Value, n.Element("pitch")!.Element("step")!.Value)).ToArray();

    [Fact]
    public void ExportPutsASyllableWhereThePageDoes_PastASlur()
    {
        const string source = """
            octave absolute
            time 4/4
            part a { clef treble }
            section A {
              a { c'4( d' e') f' | }
              lyrics one { la li | }
            }
            form { ~A }
            score { staff a lyrics one on a }
            """;
        Assert.Equal(new[] { ("la", "C"), ("li", "F") }, ExportedSyllables(source));
    }

    [Fact]
    public void ExportPutsSyllablesOnTheFirstVoiceOnly()
    {
        const string source = """
            octave absolute
            time 2/4
            part a { clef treble }
            section A {
              a { voice { c''4 d'' | } { e'4 f' | } }
              lyrics one { la li lo | }
            }
            form { ~A }
            score { staff a lyrics one on a }
            """;
        // `lo` has no note of the first voice left in its bar; the second voice's e' is not one.
        Assert.Equal(new[] { ("la", "C"), ("li", "D") }, ExportedSyllables(source));
    }

    [Fact]
    public void ATiedNoteOverASectionEdgeTakesNoSyllableInExport()
    {
        const string source = """
            octave absolute
            time 2/4
            part a { clef treble }
            section A {
              a { c'2~ | }
              lyrics one { la | }
            }
            section B {
              a { c'4 d' | }
              lyrics one { li | }
            }
            form { ~A ~B }
            score { staff a lyrics one on a }
            """;
        Assert.Equal(new[] { ("la", "C"), ("li", "D") }, ExportedSyllables(source));
    }

    [Fact]
    public void AVerseKeepsItsNumberInASectionThatLacksTheFirst()
    {
        const string source = """
            octave absolute
            time 2/4
            part a { clef treble }
            section A {
              a { c'2 | }
              lyrics one { la | }
              lyrics two { li | }
            }
            section B {
              a { d'2 | }
              lyrics two { lo | }
            }
            form { ~A ~B }
            score { staff a lyrics one on a lyrics two on a }
            """;
        var lyric = new MusicXmlExporter().Export(SyntaxTree.Parse(source)).ToXml().Descendants("lyric").Last();
        Assert.Equal("lo", lyric.Element("text")!.Value);
        Assert.Equal("2", lyric.Attribute("number")!.Value);
    }

    private static string Implicit(string book, int number) =>
        book.Replace($"<measure number=\"{number}\">", $"<measure number=\"{number}\" implicit=\"yes\">");

    private static string[] Warnings(string lys) =>
        SemanticValidation.Run(SyntaxTree.Parse(lys)).Select(d => d.Message).ToArray();

    [Fact]
    public void AnImplicitFirstBarIsAPickup()
    {
        var (lys, _) = Import(Implicit(Book(Note("C"), Note("D") + Note("E") + Note("F") + Note("G")), 1));
        Assert.Contains("partial 4\n", lys);
        Assert.Empty(Warnings(lys));
    }

    [Fact]
    public void AShortFirstBarIsAPickupThoughNotMarked()
    {
        var (lys, _) = Import(Book(Note("C") + Note("D") + Note("E"), Note("D") + Note("E") + Note("F") + Note("G")));
        Assert.Contains("partial 2.\n", lys);
        Assert.Empty(Warnings(lys));
    }

    [Fact]
    public void AFullImplicitFirstBarIsNoPickup()
    {
        var (lys, _) = Import(Implicit(Book(Note("C") + Note("D") + Note("E") + Note("F")), 1));
        Assert.DoesNotContain("partial", lys);
    }

    [Fact]
    public void APickupComesBackFromLyscXmlAndStaysPickup()
    {
        const string source = """
            octave absolute
            time 4/4
            part a { clef treble }
            section A {
              partial 4
              a { c'4 | d'1 | }
            }
            form { ~A }
            score { staff a }
            """;
        var xml = new MusicXmlExporter().Export(SyntaxTree.Parse(source)).ToXml();
        var (lys, _) = Import(xml.ToString());
        Assert.Contains("partial 4\n", lys);
    }

    [Fact]
    public void ABarSplitByAnImplicitBarStartsASectionThere()
    {
        var (lys, _) = Import(Implicit(Book(
            Note("C") + Note("D") + Note("E") + Note("F"),
            Note("C") + Note("D") + Note("E"),
            Note("F"),
            Note("C") + Note("D") + Note("E") + Note("F")), 3));
        Assert.Contains("partial 4\n", lys);
        Assert.Equal(2, lys.Split("\nsection ").Length - 1);
        Assert.Empty(Warnings(lys));
    }

    [Fact]
    public void AShortBarMakingUpABarWithTheOneBeforeIsAPickup()
    {
        var (lys, _) = Import(Book(
            Note("C") + Note("D") + Note("E") + Note("F"),
            Note("C") + Note("D") + Note("E"),
            Note("F"),
            Note("C") + Note("D") + Note("E") + Note("F")));
        Assert.Contains("partial 4\n", lys);
        Assert.Empty(Warnings(lys));
    }

    [Fact]
    public void AnImplicitShortBarIsAPickupThoughNoBarBeforeItWaitsForIt()
    {
        var (lys, _) = Import(Implicit(Book(
            Note("C") + Note("D") + Note("E") + Note("F"),
            Note("F"),
            Note("C") + Note("D") + Note("E") + Note("F")), 2));
        Assert.Contains("partial 4\n", lys);
    }

    [Fact]
    public void ASlurOverTwoPickupsStillLetsThemStartSections()
    {
        var (lys, _) = Import(Implicit(Implicit(Book(
            Note("C") + Note("D") + Note("E") + Note("F"),
            Note("C", slur: "start") + Note("D") + Note("E"),
            Note("F"),
            Note("C") + Note("D") + Note("E"),
            Note("F"),
            Note("C") + Note("D", slur: "stop") + Note("E") + Note("F")), 3), 5));
        Assert.Equal(2, lys.Split("partial 4\n").Length - 1);
        Assert.Empty(Errors(lys));
    }

    [Fact]
    public void AShortLaterVoiceLeavesNoPickupPendingAfterTheSpan()
    {
        const string source = """
            octave absolute
            time 4/4
            part a { clef treble }
            section A {
              partial 4
              a { voice { c''4 } { e'8 } d''2 e''2 | f''1 | }
            }
            form { ~A }
            score { staff a }
            """;
        var measures = new MusicXmlExporter().Export(SyntaxTree.Parse(source)).ToXml().Descendants("measure").ToArray();
        Assert.Equal(3, measures.Length);
        Assert.Equal(2, measures[1].Elements("note").Count());
    }

    [Fact]
    public void TheBarAfterAPickupIsNoPickupThoughTheyMakeUpABar()
    {
        var (lys, _) = Import(Book(Note("C"), Note("C") + Note("D") + Note("E"), Note("C") + Note("D") + Note("E") + Note("F")));
        Assert.Equal(1, lys.Split("partial").Length - 1);
    }

    [Fact]
    public void ALaterShortBarNotMarkedImplicitIsNoPickup()
    {
        var (lys, _) = Import(Book(Note("C") + Note("D") + Note("E") + Note("F"), Note("C"), Note("C") + Note("D") + Note("E") + Note("F")));
        Assert.DoesNotContain("partial", lys);
    }

    [Fact]
    public void APickupNoOneValueSpells_IsACount()
    {
        const string sixteenth = "<note><pitch><step>D</step><octave>4</octave></pitch><duration>1</duration><voice>1</voice><type>16th</type><staff>1</staff></note>";
        var book = Book(Note("C") + "@@", Note("C") + Note("D") + Note("E") + Note("F"))
            .Replace("<divisions>1</divisions>", "<divisions>4</divisions>")
            .Replace("<duration>1</duration>", "<duration>4</duration>")
            .Replace("@@", sixteenth);
        var (lys, _) = Import(book);
        Assert.Contains("d'16", lys);
        Assert.Contains("partial 16*5", lys);
    }

    [Fact]
    public void ATripletPickupStaysOneBarInMusicXml()
    {
        const string source = """
            octave absolute
            time 4/4
            part a { clef treble }
            section A {
              partial 4
              a { tuplet 3/2 { d'8 d' d' } | c'1 | }
            }
            form { ~A }
            score { staff a }
            """;
        var measures = new MusicXmlExporter().Export(SyntaxTree.Parse(source)).ToXml().Descendants("measure").ToArray();
        Assert.Equal(2, measures.Length);
        Assert.Equal(3, measures[0].Elements("note").Count());
    }

    [Fact]
    public void AVoiceSilentThroughThePickupRestsForThePickupInMusicXml()
    {
        const string source = """
            octave absolute
            time 4/4
            part a { clef treble }
            section A {
              partial 4
              a { voice { c''4 | d''1 | e''1 | } { | g'1 | a'1 | } }
            }
            form { ~A }
            score { staff a }
            """;
        Assert.DoesNotContain(Warnings(source), m => m.Contains("this voice runs"));
        var xml = new MusicXmlExporter().Export(SyntaxTree.Parse(source)).ToXml();
        var first = xml.Descendants("measure").First();
        int divisions = int.Parse(xml.Descendants("divisions").First().Value);
        var rest = first.Elements("note").Single(n => n.Element("rest") != null);
        Assert.Equal(divisions, int.Parse(rest.Element("duration")!.Value));
        Assert.Equal(3, xml.Descendants("measure").Count());
    }

    [Fact]
    public void AVoiceRunningPastTheFirstIsStillCountedPastAPickup()
    {
        const string source = """
            octave absolute
            time 4/4
            part a { clef treble }
            section A {
              partial 4
              a { voice { c''4 | d''1 | } { | g'1 | a'1 | } }
            }
            form { ~A }
            score { staff a }
            """;
        Assert.Contains(Warnings(source), m => m.Contains("this voice runs 2 bars, 1 more"));
    }

    private const string PickupThenClose = """
        octave absolute
        time 6/8
        part a { clef treble }
        section P {
          partial 8
          a { c'8 | }
        }
        section B {
          a { d'2. | }
        }
        section C {
          a { d'2. | e'2 LAST }
        }
        form { ~P |: ~B :| ~C }
        score { staff a }
        """;

    [Theory]
    [InlineData("r8 |", true)]     // 5/8 + the 1/8 pickup is one bar.
    [InlineData("|", false)]       // 4/8 is not.
    public void APiecesLastBarCompletesThePickupInAnotherSection(string last, bool clean)
    {
        var messages = Warnings(PickupThenClose.Replace("LAST", last));
        Assert.Equal(clean, !messages.Any(m => m.Contains("less than time signature")));
    }

    [Fact]
    public void ALastBarInASectionThatGoesOnCompletesNoPickup()
    {
        var book = PickupThenClose.Replace("LAST", "r8 |").Replace("form { ~P |: ~B :| ~C }", "form { ~P ~C ~B }");
        Assert.Contains(Warnings(book), m => m.Contains("less than time signature"));
    }

    [Fact]
    public void AnImplicitBarStartsASectionThoughALineRunsOverIt()
    {
        var (lys, _) = Import(Implicit(Book(
            Note("C") + Note("D") + Note("E") + Note("F"),
            Note("C", slur: "start") + Note("D") + Note("E"),
            Note("F"),
            Note("C") + Note("D") + Note("E", slur: "stop") + Note("F"),
            Note("C") + Note("D") + Note("E") + Note("F")), 3));
        Assert.Contains("partial 4\n", lys);
        Assert.Empty(Errors(lys));
    }

    [Fact]
    public void AVoiceOpeningOnBarLinesHasPassedThePickup()
    {
        const string source = """
            octave absolute
            time 4/4
            part a { clef treble }
            section A {
              partial 4
              a { voice { c''4 | d''1 | e''1 | } { | | g'1 | } }
            }
            form { ~A }
            score { staff a }
            """;
        Assert.Empty(Errors(source));
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
        var (lys, report) = Import(Book(Note("C") + member + Note("D", slur: "stop") + Note("E") + Note("F")));
        Assert.DoesNotContain(report.Warnings, w => w.Contains("slur"));
        Assert.Contains("<c' g'>4( d'4)", lys);
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

    /// <summary>Two levels with no note of their staff between them: a note prints one, and the
    /// later is what stands when it sounds.</summary>
    [Fact]
    public void LevelsPiledOnOneNote_KeepTheLast_AndSaySo()
    {
        var (lys, report) = Import(Book(
            Direction(Forte, 1) + Note("C", staff: 2) + Direction("<dynamics><p/></dynamics>", 1) + Note("D", staff: 2)
            + Note("E", staff: 1) + Note("F", staff: 1)));
        Assert.DoesNotContain("@f", lys);
        Assert.Contains("@p", lys);
        Assert.Contains(report.Warnings, w => w.Contains("the dynamic f has no note of its staff"));
    }

    /// <summary>A mark set for a staff with no note in the bar stands on the note at its moment,
    /// not back on that staff's last note, a bar earlier.</summary>
    [Fact]
    public void ADynamicForAStaffSilentThroughTheBar_StandsOnTheNoteAtItsMoment()
    {
        var (lys, _) = Import(Book(
            Note("C", staff: 1) + Note("D", staff: 1) + Note("E", staff: 1) + Note("F", staff: 1),
            Direction(Forte, 1) + Note("G", staff: 2) + Note("A", staff: 2) + Note("B", staff: 2) + Note("C", staff: 2)));
        Assert.DoesNotContain("f'4@f", lys);
        Assert.Contains("g'4@f", lys);
    }

    /// <summary>At one moment they are both written, for the check to name.</summary>
    [Fact]
    public void LevelsAtOneMoment_AreBothWritten()
    {
        var (lys, _) = Import(Book(
            Direction("<dynamics><pp/></dynamics>") + Direction(Forte) + Note("C") + Note("D") + Note("E") + Note("F")));
        Assert.Contains("@pp@f", lys);
    }
}
