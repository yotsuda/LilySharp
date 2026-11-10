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
/// A note on the other staff of a grand staff, both ways: <c>@cross</c> is a <c>&lt;staff&gt;</c>
/// that is not its voice's, and a voice belongs to the staff most of its notes are on.
/// </summary>
[Trait("Category", "Unit")]
public class MusicXmlCrossStaffTests
{
    private const string Book = """
        octave absolute
        time 4/4
        part rh { clef treble }
        part lh { clef bass }
        section A {
          rh { c''4 d'' <e'' g''> f'' | c''4 g@cross c''2 | }
          lh { c4 g <e' g'>@cross e' | c8 g@cross b@cross c' r2 | }
        }
        form { ~A }
        score { grandStaff { staff rh staff lh } }
        """;

    private static XElement[] Notes(string source)
    {
        var xml = new MusicXmlExporter().Export(SyntaxTree.Parse(source)).ToXml();
        return xml.Descendants("note").Where(n => n.Element("pitch") != null).ToArray();
    }

    private static string Of(XElement note, string child) => note.Element(child)?.Value ?? "";

    private static string Pitch(XElement note) =>
        note.Element("pitch")!.Element("step")!.Value + note.Element("pitch")!.Element("octave")!.Value;

    /// <param name="foreignLastStaff">The staff the left hand's last note is written on.</param>
    private static string Foreign(int foreignLastStaff) => $"""
        <score-partwise version="4.0">
          <part-list><score-part id="P1"><part-name>Piano</part-name></score-part></part-list>
          <part id="P1"><measure number="1">
            <attributes><divisions>2</divisions><time><beats>4</beats><beat-type>4</beat-type></time>
              <staves>2</staves>
              <clef number="1"><sign>G</sign><line>2</line></clef>
              <clef number="2"><sign>F</sign><line>4</line></clef></attributes>
            <note><pitch><step>C</step><octave>5</octave></pitch><duration>8</duration><voice>1</voice><type>whole</type><staff>1</staff></note>
            <backup><duration>8</duration></backup>
            <note><pitch><step>C</step><octave>3</octave></pitch><duration>4</duration><voice>5</voice><type>half</type><staff>2</staff></note>
            <note><pitch><step>G</step><octave>3</octave></pitch><duration>2</duration><voice>5</voice><type>quarter</type><staff>2</staff></note>
            <note><pitch><step>E</step><octave>4</octave></pitch><duration>2</duration><voice>5</voice><type>quarter</type><staff>{foreignLastStaff}</staff></note>
          </measure></part>
        </score-partwise>
        """;

    [Fact]
    public void AMovedNoteNamesTheOtherStaff_AndKeepsItsVoice()
    {
        var notes = Notes(Book);
        var moved = notes.Where(n => Of(n, "voice") == "1" && Of(n, "staff") == "2").Select(Pitch);
        Assert.Equal(["G3"], moved);
        var movedUp = notes.Where(n => Of(n, "voice") == "5" && Of(n, "staff") == "1").Select(Pitch);
        // The chord's two members both: a chord moves whole.
        Assert.Equal(["E4", "G4", "G3", "B3"], movedUp);
    }

    [Fact]
    public void ACrossStaffNoteComesBackAsWritten()
    {
        var xml = new MusicXmlExporter().Export(SyntaxTree.Parse(Book)).ToXml();
        var (lys, _) = new MusicXmlImporter().Import(xml.ToString());
        Assert.Contains("c''4 g4@cross c''2", lys);
        Assert.Contains("<e' g'>4@cross", lys);
        Assert.Contains("c8 g8@cross b8@cross c'8", lys);
        // Only those: the unmoved keep their part and draw no mark.
        Assert.Equal(4, System.Text.RegularExpressions.Regex.Matches(lys, "@cross").Count);
        Assert.Contains("grandStaff", lys);
    }

    [Fact]
    public void AVoiceSitsWhereMostOfItsNotesAre_NotWhereItsLastNoteIs()
    {
        var (lys, _) = new MusicXmlImporter().Import(Foreign(foreignLastStaff: 1));
        // It used to take the whole left hand up: one staff, a bass clef in the right hand's music.
        Assert.Contains("grandStaff", lys);
        Assert.Contains("clef bass", lys);
        Assert.Contains("c2 g4 e'4@cross", lys);
    }

    [Theory]
    // The first note is the odd one out: the count decides, not the order.
    [InlineData(new[] { 1, 2, 2 }, "e'2@cross g4 c'4")]
    // Equal counts: the first staff the voice was written on.
    [InlineData(new[] { 2, 1 }, "e'2 g2@cross")]
    public void TheStaffCountDecides_AndTheFirstOfEquals(int[] staves, string expected)
    {
        (string Pitch, string Type, int Dur)[] notes = staves.Length == 3
            ? [("<step>E</step><octave>4</octave>", "half", 4), ("<step>G</step><octave>3</octave>", "quarter", 2), ("<step>C</step><octave>4</octave>", "quarter", 2)]
            : [("<step>E</step><octave>4</octave>", "half", 4), ("<step>G</step><octave>3</octave>", "half", 4)];
        string lh = string.Concat(notes.Select((n, i) =>
            $"<note><pitch>{n.Pitch}</pitch><duration>{n.Dur}</duration><voice>5</voice><type>{n.Type}</type><staff>{staves[i]}</staff></note>"));
        string xml = $"""
            <score-partwise version="4.0">
              <part-list><score-part id="P1"><part-name>Piano</part-name></score-part></part-list>
              <part id="P1"><measure number="1">
                <attributes><divisions>2</divisions><time><beats>4</beats><beat-type>4</beat-type></time>
                  <staves>2</staves>
                  <clef number="1"><sign>G</sign><line>2</line></clef>
                  <clef number="2"><sign>F</sign><line>4</line></clef></attributes>
                <note><pitch><step>C</step><octave>5</octave></pitch><duration>8</duration><voice>1</voice><type>whole</type><staff>1</staff></note>
                <backup><duration>8</duration></backup>
                {lh}
              </measure></part>
            </score-partwise>
            """;
        var (lys, _) = new MusicXmlImporter().Import(xml);
        Assert.Contains(expected, lys);
    }

    /// <summary>One bar of 2/4 (divisions 8), the voices as Dichterliebe 1's bar 6 writes them.</summary>
    private static string GapBar(string music) => $"""
        <score-partwise version="4.0">
          <part-list><score-part id="P1"><part-name>Piano</part-name></score-part></part-list>
          <part id="P1"><measure number="1">
            <attributes><divisions>8</divisions><time><beats>2</beats><beat-type>4</beat-type></time>
              <clef><sign>G</sign><line>2</line></clef></attributes>
            {music}
          </measure></part>
        </score-partwise>
        """;

    private static string N(string step, int octave, int voice, int duration, string type) =>
        $"<note><pitch><step>{step}</step><octave>{octave}</octave></pitch><duration>{duration}</duration><voice>{voice}</voice><type>{type}</type></note>";

    private static string Forward(int duration) => $"<forward><duration>{duration}</duration></forward>";

    private static string Backup(int duration) => $"<backup><duration>{duration}</duration></backup>";

    [Fact]
    public void AVoicelessForwardAfterABackupBelongsToTheNextVoice()
    {
        var (lys, _) = new MusicXmlImporter().Import(GapBar(
            N("C", 5, 1, 8, "quarter") + N("D", 5, 1, 8, "quarter")
            + Backup(16) + Forward(8) + Forward(4)
            + N("F", 4, 2, 2, "16th") + N("D", 4, 2, 2, "16th")));
        // The finished voice takes no rest past its bar; the next one stands on its beat.
        Assert.Contains("c''4 d''4 |", lys);
        Assert.Contains("r4 r8 f'16 d'16 |", lys);
    }

    [Fact]
    public void AVoicelessForwardAfterAVoicesOwnNoteIsThatVoicesGap()
    {
        var (lys, _) = new MusicXmlImporter().Import(GapBar(
            N("C", 5, 1, 8, "quarter") + Forward(8)));
        Assert.Contains("c''4 r4 |", lys);
    }

    [Fact]
    public void AForwardNamingItsVoiceIsThatVoicesGap()
    {
        var (lys, _) = new MusicXmlImporter().Import(GapBar(
            N("C", 5, 1, 16, "half") + Backup(16)
            + "<forward><duration>8</duration><voice>2</voice></forward>"
            + N("F", 4, 2, 8, "quarter")));
        Assert.Contains("r4 f'4 |", lys);
    }

    private static string Tup(string kind) => $"<notations><tuplet type=\"{kind}\"/></notations>";

    [Fact]
    public void ATupletBracketWithoutATimeModificationWritesNoGroup()
    {
        // kinkel19-1: plain 16ths under a bracket. The start read as nothing, the stop as `}`.
        string music = string.Concat(
            "<note><rest/><duration>2</duration><voice>1</voice><type>16th</type>", Tup("start"), "</note>",
            N("A", 4, 1, 2, "16th"), N("C", 5, 1, 2, "16th"),
            "<note><pitch><step>F</step><octave>5</octave></pitch><duration>2</duration><voice>1</voice><type>16th</type>", Tup("stop"), "</note>",
            N("A", 4, 1, 8, "quarter"));
        var (lys, _) = new MusicXmlImporter().Import(GapBar(music));
        Assert.Contains("r16 a'16 c''16 f''16 a'4 |", lys);
        Assert.DoesNotContain("tuplet", lys);
        Assert.Empty(SyntaxTree.Parse(lys).Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void ARealTupletStillWritesItsGroup()
    {
        const string Tm = "<time-modification><actual-notes>3</actual-notes><normal-notes>2</normal-notes></time-modification>";
        string Note(string step, string extra) =>
            $"<note><pitch><step>{step}</step><octave>5</octave></pitch><duration>2</duration><voice>1</voice><type>eighth</type>{Tm}{extra}</note>";
        // 3 × (8 ÷ 3) is not a whole tick at divisions 8: the group is read off the marks.
        string music = Note("C", Tup("start")) + Note("D", "") + Note("E", Tup("stop")) + N("F", 5, 1, 8, "quarter");
        var (lys, _) = new MusicXmlImporter().Import(GapBar(music));
        Assert.Contains("tuplet 3/2 {", lys);
        Assert.Contains("} f''4", lys);
    }

    private static string TremNote(string step, int octave, int duration, string type, bool dot, string kind, int beams, bool chord = false) =>
        $"<note>{(chord ? "<chord/>" : "")}<pitch><step>{step}</step><octave>{octave}</octave></pitch><duration>{duration}</duration><voice>1</voice><type>{type}</type>{(dot ? "<dot/>" : "")}"
        + $"<notations><ornaments><tremolo type=\"{kind}\">{beams}</tremolo></ornaments></notations></note>";

    private static string TremBar(int beats, int beatType, string music) => $"""
        <score-partwise version="4.0">
          <part-list><score-part id="P1"><part-name>Piano</part-name></score-part></part-list>
          <part id="P1"><measure number="1">
            <attributes><divisions>8</divisions><time><beats>{beats}</beats><beat-type>{beatType}</beat-type></time>
              <clef><sign>G</sign><line>2</line></clef></attributes>
            {music}
          </measure></part>
        </score-partwise>
        """;

    [Theory]
    // kinkel10-4 bar 5: 6/8, each note half the bar and written as the whole bar's dotted half.
    [InlineData(6, 8, 12, "half", true, 2, "repeat tremolo 6 { c''16 f'16 } |")]
    [InlineData(4, 4, 16, "whole", false, 3, "repeat tremolo 16 { c''32 f'32 } |")]
    [InlineData(4, 4, 16, "whole", false, 1, "repeat tremolo 4 { c''8 f'8 } |")]
    public void ATwoNoteTremoloIsWrittenAsItsAlternation(int beats, int beatType, int each, string type, bool dot, int marks, string expected)
    {
        var (lys, _) = new MusicXmlImporter().Import(TremBar(beats, beatType,
            TremNote("C", 5, each, type, dot, "start", marks) + TremNote("F", 4, each, type, dot, "stop", marks)));
        Assert.Contains(expected, lys);
        Assert.Empty(LilySharp.Core.Semantics.SemanticValidation.Run(SyntaxTree.Parse(lys))
            .Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void ATremoloPairKeepsTheBarsOtherNotesAndItsChord()
    {
        var (lys, _) = new MusicXmlImporter().Import(TremBar(4, 4,
            N("G", 4, 1, 16, "half")
            + TremNote("C", 5, 8, "half", false, "start", 2) + TremNote("E", 5, 8, "half", false, "start", 2, chord: true)
            + TremNote("F", 4, 8, "half", false, "stop", 2)));
        Assert.Contains("g'2 repeat tremolo 4 { <c'' e''>16 f'16 } |", lys);
        Assert.Empty(LilySharp.Core.Semantics.SemanticValidation.Run(SyntaxTree.Parse(lys))
            .Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void AnotherVoiceIsFilledToThePairsWholeLength()
    {
        // The pair lasts the bar though the items hold its two notes once (LysWriter.LengthOf).
        var (lys, _) = new MusicXmlImporter().Import(TremBar(4, 4,
            TremNote("C", 5, 16, "whole", false, "start", 2) + TremNote("F", 4, 16, "whole", false, "stop", 2)
            + Backup(32) + N("G", 4, 2, 8, "quarter")));
        Assert.Contains("repeat tremolo 8 { c''16 f'16 } |", lys);
        Assert.Contains("g'4 s2. |", lys);
    }

    [Fact]
    public void ABackupShortOfTheBarsHeadPutsTheNextVoiceOnItsBeat()
    {
        // Dichterliebe 13 bar 30: the stream rewinds to beat 2 and the next voice writes there.
        var (lys, _) = new MusicXmlImporter().Import(GapBar(
            N("C", 5, 1, 16, "half") + Backup(8) + N("F", 4, 2, 8, "quarter")));
        Assert.Contains("r4 f'4 |", lys);
    }

    [Fact]
    public void AGapAVoiceOwesCanBeASixtyFourth()
    {
        // boulanger-clairieres02 bar 22: a 64th's gap (divisions 8: one tick is a 32nd, so 16).
        string music = N("C", 5, 1, 32, "half") + Backup(31) + N("F", 4, 2, 1, "64th");
        var (lys, report) = new MusicXmlImporter().Import(GapBar(music).Replace("<divisions>8<", "<divisions>16<"));
        Assert.Contains("r64 ", lys);
        Assert.DoesNotContain(report.Warnings, w => w.Contains("no plain note value"));
    }

    [Fact]
    public void AGapNoPlainValueSpellsIsReported()
    {
        // divisions 5: one tick is a fifth of a beat, no plain value's length.
        string music = N("C", 5, 1, 10, "half") + Backup(8) + N("F", 4, 2, 2, "16th");
        var (_, report) = new MusicXmlImporter().Import(GapBar(music).Replace("<divisions>8<", "<divisions>5<"));
        Assert.Contains(report.Warnings, w => w.Contains("no plain note value"));
    }

    [Fact]
    public void AVoiceBlankBeforeATypedBarLineKeepsItsBars()
    {
        // lehmann-flapper5: voice 2 rests through bars 1 and 2, bar 2 closing `||`, and sings in 3.
        string Bar(int no, string music, string? style = null) =>
            $"<measure number=\"{no}\">"
            + (no == 1 ? "<attributes><divisions>2</divisions><time><beats>4</beats><beat-type>4</beat-type></time><clef><sign>G</sign><line>2</line></clef></attributes>" : "")
            + music
            + (style is null ? "" : $"<barline location=\"right\"><bar-style>{style}</bar-style></barline>")
            + "</measure>";
        string V1(string step) => N(step, 5, 1, 8, "whole");
        string xml = $"""
            <score-partwise version="4.0">
              <part-list><score-part id="P1"><part-name>M</part-name></score-part></part-list>
              <part id="P1">
                {Bar(1, V1("C"))}
                {Bar(2, V1("D"), "light-light")}
                {Bar(3, V1("E") + Backup(8) + N("G", 4, 2, 8, "whole"))}
                {Bar(4, V1("F"), "light-heavy")}
              </part>
            </score-partwise>
            """;
        var (lys, _) = new MusicXmlImporter().Import(xml);
        Assert.Contains("|  | ||", lys.Replace("\r\n", " ").Replace("\n", " "));
        // Round trip: G4 back in bar 3, where the source has it.
        var measures = new MusicXmlExporter().Export(SyntaxTree.Parse(lys)).ToXml().Descendants("measure").ToArray();
        Assert.Equal(4, measures.Length);
        bool HasG4(XElement m) => m.Elements("note").Any(n => n.Element("pitch") is { } p
            && p.Element("step")!.Value == "G" && p.Element("octave")!.Value == "4");
        Assert.Equal([false, false, true, false], measures.Select(HasG4));
    }

    private static string Pedal(string type, int staff, string attributes = "line=\"no\" sign=\"yes\"") =>
        $"<direction placement=\"below\"><direction-type><pedal type=\"{type}\" {attributes}/></direction-type><staff>{staff}</staff></direction>";

    private static string Hand(string step, int octave, int voice, int staff, int duration, string type) =>
        $"<note><pitch><step>{step}</step><octave>{octave}</octave></pitch><duration>{duration}</duration><voice>{voice}</voice><type>{type}</type><staff>{staff}</staff></note>";

    /// <summary>Two bars of a grand staff in 2/4 (divisions 2): each hand writes what it is given.</summary>
    private static string PianoBars(string bar1, string bar2) => $"""
        <score-partwise version="4.0">
          <part-list><score-part id="P1"><part-name>Piano</part-name></score-part></part-list>
          <part id="P1">
            <measure number="1"><attributes><divisions>2</divisions><time><beats>2</beats><beat-type>4</beat-type></time>
              <staves>2</staves><clef number="1"><sign>G</sign><line>2</line></clef><clef number="2"><sign>F</sign><line>4</line></clef></attributes>{bar1}</measure>
            <measure number="2">{bar2}</measure>
          </part>
        </score-partwise>
        """;

    [Fact]
    public void APedalReleaseStaysWithTheHandThatPressedIt()
    {
        // Dichterliebe 12: the release names staff 2, and the note written there first is the
        // right hand's, come down @cross. It belongs on the left hand's own note.
        string bar1 = Pedal("start", 2)
            + Hand("C", 5, 1, 1, 4, "half") + Backup(4) + Hand("C", 3, 5, 2, 4, "half");
        string bar2 = Pedal("stop", 2)
            + Hand("G", 3, 1, 2, 2, "quarter") + Hand("D", 5, 1, 1, 2, "quarter") + Backup(4)
            + Hand("D", 3, 5, 2, 4, "half");
        var (lys, _) = new MusicXmlImporter().Import(PianoBars(bar1, bar2));
        Assert.Contains("g4@cross d''4", lys);
        Assert.Contains("c2@sustain | d2@!sustain", lys);
        Assert.Empty(LilySharp.Core.Semantics.SemanticValidation.Run(SyntaxTree.Parse(lys))
            .Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Theory]
    [InlineData("line=\"no\" sign=\"yes\"", " pedal text")]
    [InlineData("line=\"no\"", " pedal text")]
    [InlineData("line=\"yes\" sign=\"yes\"", " pedal mixed")]
    [InlineData("line=\"yes\" sign=\"no\"", " pedal bracket")]
    [InlineData("line=\"yes\"", " pedal bracket")]
    [InlineData("", "")]   // `lysc xml` says nothing: the book's default comes back as itself
    public void APedalsDrawnFormComesThrough(string attributes, string expected)
    {
        string bar = Pedal("start", 2, attributes) + Hand("C", 5, 1, 1, 4, "half") + Backup(4) + Hand("C", 3, 5, 2, 4, "half");
        string end = Pedal("stop", 2, attributes) + Hand("D", 5, 1, 1, 4, "half") + Backup(4) + Hand("D", 3, 5, 2, 4, "half");
        var (lys, _) = new MusicXmlImporter().Import(PianoBars(bar, end));
        if (expected.Length > 0)
            Assert.Contains($"{{ clef bass{expected}", lys);
        else
            Assert.DoesNotContain(" pedal ", lys);
    }

    private static string TremoloBook(string music, string header = "") => $$"""
        octave absolute
        time 6/8
        part m { clef treble {{header}}}
        section A { m { {{music}} } }
        form { ~A }
        score { staff m }
        """;

    private static XElement[] Exported(string book) =>
        new MusicXmlExporter().Export(SyntaxTree.Parse(book)).ToXml().Descendants("note").ToArray();

    private static string Tremolo(XElement note) => note.Descendants("tremolo").Select(t =>
        t.Attribute("type")!.Value + ":" + t.Value).SingleOrDefault() ?? "";

    [Theory]
    // Mid-bar and opening a bar, a chord in the pair, a span that crosses into the next bar's notes.
    [InlineData("repeat tremolo 6 { c''16 f'16 } |", "half", true, 36, "start:2", "stop:2", 2)]
    [InlineData("repeat tremolo 3 { c''8 f'8 } |", "half", true, 36, "start:1", "stop:1", 2)]
    [InlineData("c''4. repeat tremolo 3 { g'16 b'16 } |", "quarter", true, 18, "start:2", "stop:2", 3)]
    public void ATremoloPairIsWrittenOnce(string music, string type, bool dot, int duration, string first, string second, int notes)
    {
        var all = Exported(TremoloBook(music));
        Assert.Equal(notes, all.Length);
        var a = all[^2];
        var b = all[^1];
        Assert.Equal(type, a.Element("type")!.Value);
        Assert.Equal(dot, a.Element("dot") != null);
        Assert.Equal(duration, int.Parse(a.Element("duration")!.Value));
        Assert.Equal(first, Tremolo(a));
        Assert.Equal(second, Tremolo(b));
        Assert.Equal("2", a.Element("time-modification")!.Element("actual-notes")!.Value);
    }

    [Theory]
    [InlineData("repeat tremolo 6 { c''16 f'16 } |")]
    [InlineData("repeat tremolo 3 { <c'' e''>8 f'8 } |")]
    [InlineData("c''4. repeat tremolo 3 { g'16 b'16 } |")]
    [InlineData("repeat tremolo 12 { c''16 } |")]
    public void ATremoloComesBackAsWritten(string music)
    {
        string xml = new MusicXmlExporter().Export(SyntaxTree.Parse(TremoloBook(music))).ToXml().ToString();
        var (lys, _) = new MusicXmlImporter().Import(xml);
        if (music.Contains("{ c''16 }"))
            Assert.Contains("c''2.:16 |", lys);   // the one note's spelling of the same thing
        else
            Assert.Contains(music, lys);
    }

    [Fact]
    public void ATremoloOpeningALaterBarIsWrittenOnce()
    {
        var all = Exported(TremoloBook("c''2. | repeat tremolo 6 { c''16 f'16 } | d''2. |"));
        Assert.Equal(4, all.Length);
        Assert.Equal("start:2", Tremolo(all[1]));
        Assert.Equal("stop:2", Tremolo(all[2]));
    }

    [Fact]
    public void AOneNoteTremoloOnAShortSpanCountsOnlyWhatItAdds()
    {
        // An eighth's span in 32nds: three beams, one of them the eighth's own.
        var all = Exported(TremoloBook("repeat tremolo 4 { c''32 } r8 r2 |"));
        Assert.Equal("single:2", Tremolo(all[0]));
    }

    [Fact]
    public void AChordInATremoloPairMovesWhole()
    {
        var all = Exported(TremoloBook("repeat tremolo 3 { <c'' e''>8 f'8 } |"));
        Assert.Equal(3, all.Length);
        Assert.Equal(36, int.Parse(all[1].Element("duration")!.Value));   // the member too
        Assert.Equal("half", all[1].Element("type")!.Value);
    }

    [Fact]
    public void ATieIntoATremoloStaysAndATieOutOfItStays()
    {
        var all = Exported(TremoloBook("c''4.~ repeat tremolo 3 { c''16 b'16~ } | b'2. |"));
        Assert.Contains(all[1].Elements("tie"), t => t.Attribute("type")!.Value == "stop");
        Assert.Contains(all[2].Elements("tie"), t => t.Attribute("type")!.Value == "start");
        Assert.Contains(all[3].Elements("tie"), t => t.Attribute("type")!.Value == "stop");
    }

    [Theory]
    [InlineData("c''2.:16 |", "single:2")]
    [InlineData("c''2.:8 |", "single:1")]
    [InlineData("c''8:16 r8 r4. |", "single:1")]   // the note's own beam is not a mark
    [InlineData("<c'' e''>2.:16 |", "single:2")]
    public void AStemTremoloIsWrittenAndComesBack(string music, string expected)
    {
        var all = Exported(TremoloBook(music));
        Assert.Equal(expected, Tremolo(all[0]));
        string xml = new MusicXmlExporter().Export(SyntaxTree.Parse(TremoloBook(music))).ToXml().ToString();
        Assert.Contains(music.Split(' ')[0], new MusicXmlImporter().Import(xml).Item1);
    }

    [Theory]
    [InlineData("", "yes", "no")]              // Lily#'s default, a bracket
    [InlineData("pedal bracket ", "yes", "no")]
    [InlineData("pedal text ", "no", "yes")]
    [InlineData("pedal mixed ", "yes", "yes")]
    public void APedalSaysHowItIsDrawn(string header, string line, string sign)
    {
        string book = TremoloBook("c''2.@sustain | d''2.@!sustain |", header);
        var pedals = new MusicXmlExporter().Export(SyntaxTree.Parse(book)).ToXml().Descendants("pedal").ToArray();
        Assert.Equal(2, pedals.Length);
        Assert.All(pedals, p =>
        {
            Assert.Equal(line, p.Attribute("line")!.Value);
            Assert.Equal(sign, p.Attribute("sign")!.Value);
        });
        if (header.Length > 0)
        {
            string xml = new MusicXmlExporter().Export(SyntaxTree.Parse(book)).ToXml().ToString();
            Assert.Contains($"clef treble {header.TrimEnd()}", new MusicXmlImporter().Import(xml).Item1);
        }
    }

    private static string DivisionsBook(string music) => $$"""
        octave absolute
        time 4/4
        part m { clef treble }
        section A { m { {{music}} } }
        form { ~A }
        score { staff m }
        """;

    private static (int Divisions, int[] Durations) Counted(string music)
    {
        var xml = new MusicXmlExporter().Export(SyntaxTree.Parse(DivisionsBook(music))).ToXml();
        return (int.Parse(xml.Descendants("divisions").First().Value),
            xml.Descendants("note").Select(n => int.Parse(n.Element("duration")!.Value)).ToArray());
    }

    [Fact]
    public void AScoreOf24sThirdsStaysAt24()
    {
        var (divisions, durations) = Counted("tuplet 3/2 { c''8 d'' e'' } c''4 c''32 r32 r16 r8 r4 |");
        Assert.Equal(24, divisions);
        Assert.Equal([8, 8, 8, 24, 3, 3, 6, 12, 24], durations);
    }

    [Theory]
    // Dichterliebe 8 bar 36: an eighth in a quintuplet of sixteenths is 0.4 of a beat.
    [InlineData("tuplet 5/4 { c''8 d''16 e'' f'' } r2. |", 120, new[] { 48, 24, 24, 24 })]
    [InlineData("c''64 r64 r32 r16 r8 r4 r2 |", 48, new[] { 3 })]
    [InlineData("tuplet 7/4 { c''16 d'' e'' f'' g'' a'' b'' } r2. |", 168, new[] { 24 })]
    public void DivisionsRiseToSayADurationExactly(string music, int expected, int[] first)
    {
        var (divisions, durations) = Counted(music);
        Assert.Equal(expected, divisions);
        Assert.Equal(first, durations.Take(first.Length));
        // The bar still adds up: to four quarters, in its own divisions.
        Assert.Equal(4 * divisions, durations.Sum());
    }

    [Fact]
    public void EveryMeasureStatesTheOneDivisions()
    {
        var xml = new MusicXmlExporter().Export(SyntaxTree.Parse(
            DivisionsBook("tuplet 5/4 { c''16 d'' e'' f'' g'' } r2. | time 3/4 c''2. |"))).ToXml();
        Assert.All(xml.Descendants("divisions"), d => Assert.Equal("120", d.Value));
    }

    [Fact]
    public void AQuintupletComesBackAsWritten()
    {
        const string Music = "tuplet 5/4 { c''8 d''16 e''16 f''16 } r4 r2 |";
        var xml = new MusicXmlExporter().Export(SyntaxTree.Parse(DivisionsBook(Music))).ToXml();
        Assert.Contains(Music, new MusicXmlImporter().Import(xml.ToString()).Item1);
    }

    [Fact]
    public void RaisingTheDivisionsKeepsTheScoreChosen()
    {
        // The second build is a fresh exporter: it has to be told which score it is writing.
        var tree = SyntaxTree.Parse("""
            octave absolute
            time 4/4
            part a { clef treble }
            part b { clef bass }
            section A { a { tuplet 5/4 { c''16 d'' e'' f'' g'' } r2. | } b { c1 | } }
            form { ~A }
            score one { staff a }
            score two { staff b }
            """);
        var specs = LilySharp.Core.Svg.Collector.RenderSpecParser.FindAll(tree);
        var xml = new MusicXmlExporter { Score = specs[1] }.Export(tree).ToXml();
        Assert.Equal("F", xml.Descendants("sign").First().Value);
        Assert.Equal("C3", string.Concat(xml.Descendants("pitch").First().Elements().Select(e => e.Value)).Replace("0", ""));
    }

    [Fact]
    public void EachScoresFileHoldsThatScore()
    {
        // `lysc xml` of a book of two scores wrote the same two parts into both files.
        var tree = SyntaxTree.Parse("""
            octave absolute
            time 4/4
            part a { clef treble }
            part b { clef bass }
            section A { a { c''1 | } b { c1 | } }
            form { ~A }
            score one { staff a }
            score two { staff b }
            """);
        string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lys-xml-" + System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        try
        {
            var scores = tree.GetRoot().DescendantNodes().OfType<RenderDeclarationSyntax>().ToArray();
            string Written(int i, string name)
            {
                string path = System.IO.Path.Combine(dir, name + ".xml");
                var spec = LilySharp.Core.Svg.Collector.RenderSpecParser.Parse(scores[i])!;
                LilySharp.Core.Export.ScoreExport.Write(tree, "musicxml", path, (scores[i], spec));
                var xml = XDocument.Load(path);
                Assert.Single(xml.Root!.Elements("part"));
                return xml.Descendants("sign").First().Value;
            }
            Assert.Equal("G", Written(0, "one"));
            Assert.Equal("F", Written(1, "two"));
        }
        finally
        {
            System.IO.Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ATremoloFoldsAtTheRaisedDivisions()
    {
        // Another bar's quintuplet raises the score's divisions: the pair's span is read from them.
        var all = Exported(TremoloBook("repeat tremolo 6 { c''16 f'16 } | tuplet 5/4 { c''16 d'' e'' f'' g'' } r4. |"));
        Assert.Equal("start:2", Tremolo(all[0]));
        Assert.Equal("half", all[0].Element("type")!.Value);
    }

    [Fact]
    public void ASongsTimingIsItsOwnWhateverTheDivisions()
    {
        // VsqxExporter reads the document's durations: a quintuplet must not rescale the bar.
        string Book(string first) => $$"""
            time 4/4
            part v { clef treble }
            section A { v { {{first}} | c''1 | } lyrics words { la la la la la la la la | } }
            form { ~A }
            score { staff v }
            """;
        int LastNoteAt(string first)
        {
            var doc = new LilySharp.Core.Vocaloid.VsqxExporter().Export(SyntaxTree.Parse(Book(first)));
            return doc.Descendants().Where(e => e.Name.LocalName == "note")
                .Select(n => int.Parse(n.Elements().First(e => e.Name.LocalName == "t").Value)).Last();
        }
        Assert.Equal(LastNoteAt("c''4 c'' c'' c''"), LastNoteAt("tuplet 5/4 { c''16 d'' e'' f'' g'' } c''4 r2"));
    }

    [Fact]
    public void ASpanTheValueCannotDivideKeepsItsNotes()
    {
        // 7 ticks at divisions 8 is no whole number of 16th pairs: no block is guessed.
        var (lys, _) = new MusicXmlImporter().Import(TremBar(4, 4,
            TremNote("C", 5, 7, "half", false, "start", 2) + TremNote("F", 4, 8, "half", false, "stop", 2)));
        Assert.DoesNotContain("repeat tremolo", lys);
        Assert.Contains("c''2 f'2", lys);
    }

    [Fact]
    public void ATremoloStartWithNoStopKeepsItsNote()
    {
        var (lys, _) = new MusicXmlImporter().Import(TremBar(4, 4,
            TremNote("C", 5, 16, "half", false, "start", 2) + N("F", 4, 1, 16, "half")));
        Assert.DoesNotContain("repeat tremolo", lys);
        Assert.Contains("c''2 f'2 |", lys);
    }

    [Fact]
    public void ANoteOnItsVoicesOwnStaffIsNotMarked()
    {
        var (lys, _) = new MusicXmlImporter().Import(Foreign(foreignLastStaff: 2));
        Assert.Contains("grandStaff", lys);
        Assert.DoesNotContain("@cross", lys);
    }
}
