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

    [Fact]
    public void ANoteOnItsVoicesOwnStaffIsNotMarked()
    {
        var (lys, _) = new MusicXmlImporter().Import(Foreign(foreignLastStaff: 2));
        Assert.Contains("grandStaff", lys);
        Assert.DoesNotContain("@cross", lys);
    }
}
