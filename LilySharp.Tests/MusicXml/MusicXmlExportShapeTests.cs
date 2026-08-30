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
/// Shape assertions on the exported MusicXML — the "roundtrip lite" guard for
/// the exporter gaps found in the grammar audit: multi-voice measures must
/// MERGE (voice numbers + backup), repeats must unfold to their played
/// length, drum notes serialize unpitched, and lyric elisions split inside
/// one lyric element.
/// </summary>
public class MusicXmlExportShapeTests
{
    private static XDocument Export(string source)
    {
        var tree = SyntaxTree.Parse(source);
        return new MusicXmlExporter().Export(tree).ToXml();
    }

    [Fact]
    public void MultiVoice_MergesMeasuresWithBackup()
    {
        var doc = Export("""
            part pno { clef treble }
            section A {
              pno {
                voice { c'4 d' e' f' | g'1 | }
                { c4 c c c | c1 | }
              }
            }
            form main { A }
            score main { staff pno }
            """);
        var measures = doc.Descendants("measure").ToList();
        Assert.Equal(2, measures.Count);               // NOT 4 (serialized voices)
        Assert.Equal(2, doc.Descendants("backup").Count());
        // Full-bar rewind: 4/4 at 24 divisions per quarter.
        Assert.All(doc.Descendants("backup"),
            b => Assert.Equal("96", b.Element("duration")!.Value));
        Assert.Equal(5, doc.Descendants("note").Count(n => n.Element("voice")?.Value == "1"));
        Assert.Equal(5, doc.Descendants("note").Count(n => n.Element("voice")?.Value == "2"));
    }

    [Fact]
    public void ThreeVoices_BackupRewindsToBarStartNotCumulatively()
    {
        // Regression: the third voice's <backup> summed EVERY forward note already
        // merged into the measure (voice1 + voice2), rewinding past the bar start.
        // Each backup must equal one bar (96 at 24 divisions per quarter, 4/4).
        var doc = Export("""
            part pno { clef treble }
            section A {
              pno {
                voice { c'4 d' e' f' | }
                { c4 d e f | }
                { e4 f g a | }
              }
            }
            form main { A }
            score main { staff pno }
            """);
        var backups = doc.Descendants("backup").ToList();
        Assert.Equal(2, backups.Count);
        Assert.All(backups, b => Assert.Equal("96", b.Element("duration")!.Value));
    }

    [Fact]
    public void PartMajorSection_ExportsItsInlineNotes_NotAnEmptyPart()
    {
        // A part-major `part m { section A { … } }` cell holds its music INLINE.
        // The inline notes used to hit ProcessNode's skip-declarations case, so the
        // part exported empty; now they emit under the enclosing part's name and clef.
        var doc = Export("""
            part m { clef bass
              section A { c d e f | }
            }
            form main { A }
            score main { staff m }
            """);
        Assert.Equal(4, doc.Descendants("note").Count());
        Assert.Equal("m", doc.Descendants("part-name").Single().Value);
        // The enclosing part's clef (bass = F/4) is applied.
        var clef = doc.Descendants("clef").First();
        Assert.Equal("F", clef.Element("sign")!.Value);
        Assert.Equal("4", clef.Element("line")!.Value);
    }

    [Fact]
    public void PartMajorPart_WithSeveralSections_ConcatenatesThem()
    {
        // Two sections of the same part flow into one continuous part.
        var doc = Export("""
            part m { clef treble
              section A { c d e f | }
              section B { g a b c' | }
            }
            form main { A B }
            score main { staff m }
            """);
        Assert.Equal(8, doc.Descendants("note").Count());
        Assert.Single(doc.Descendants("score-part"));
    }

    [Fact]
    public void TiedChord_TiesEveryChordMember()
    {
        // Regression: <c e g>~ <c e g> tied only the first note; all members tie.
        var doc = Export("""
            octave absolute
            time 4/4
            part m { clef treble }
            section A { m { <c' e' g'>2~ <c' e' g'>2 | } }
            form main { A }
            score main { staff m }
            """);
        var ties = doc.Descendants("tie").ToList();
        Assert.Equal(3, ties.Count(t => t.Attribute("type")!.Value == "start"));
        Assert.Equal(3, ties.Count(t => t.Attribute("type")!.Value == "stop"));
    }

    [Fact]
    public void StructureOrder_EmitsSectionsInStructureOrderWithReplay()
    {
        // The structure gives the played ORDER; a replayed section reappears.
        // (Previously the export dumped sections in declaration order, so 'A B A'
        // collapsed to just A, B.)
        var doc = Export("""
            octave absolute
            part m { clef treble }
            section A { m { c'4 d' e' f' | } }
            section B { m { g'4 a' b' c'' | } }
            form main { A B A }
            score main { staff m }
            """);
        var measures = doc.Descendants("measure").ToList();
        Assert.Equal(3, measures.Count);   // A, B, A — not 2
        string First(XElement m) => m.Descendants("note").First(n => n.Element("pitch") != null)
            .Element("pitch")!.Element("step")!.Value;
        Assert.Equal(new[] { "C", "G", "C" }, measures.Select(First));
    }

    [Fact]
    public void StructureRepeat_BracketsSpanWithForwardAndBackwardBarlines()
    {
        var doc = Export("""
            octave absolute
            part m { clef treble }
            section A { m { c'4 d' e' f' | } }
            section B { m { g'4 a' b' c'' | d''4 c'' b' a' | } }
            form main { A |: B :| }
            score main { staff m }
            """);
        var measures = doc.Descendants("measure").ToList();
        Assert.Equal(3, measures.Count);   // A(1) + B(2)
        bool HasRepeat(XElement m, string loc, string dir) =>
            m.Elements("barline").Any(bl => bl.Attribute("location")?.Value == loc
                && bl.Element("repeat")?.Attribute("direction")?.Value == dir);
        Assert.True(HasRepeat(measures[1], "left", "forward"));    // B opens the repeat
        Assert.True(HasRepeat(measures[2], "right", "backward"));  // B closes it
        Assert.False(HasRepeat(measures[0], "left", "forward"));   // A is outside the repeat
    }

    [Fact]
    public void FiguredBass_ExportsFigureNumberWithAccidental()
    {
        var doc = Export("""
            octave absolute
            part m { clef treble }
            section A { m { c'4@fig(#6) d' e' f' | } }
            form main { A }
            score main { staff m }
            """);
        var figs = doc.Descendants("figured-bass").ToList();
        Assert.Single(figs);
        var figure = figs[0].Element("figure")!;
        Assert.Equal("6", figure.Element("figure-number")!.Value);
        Assert.Equal("sharp", figure.Element("suffix")!.Value);
    }

    [Fact]
    public void Tuplet_EmitsTupletBracketNotations()
    {
        var doc = Export("""
            octave absolute
            time 4/4
            part m { clef treble }
            section A { m { tuplet 3/2 { c'8 d' e' } r4 c'2 | } }
            form main { A }
            score main { staff m }
            """);
        var tuplets = doc.Descendants("tuplet").ToList();
        Assert.Equal(2, tuplets.Count);
        Assert.Equal("start", tuplets[0].Attribute("type")!.Value);
        Assert.Equal("stop", tuplets[1].Attribute("type")!.Value);
        // The bracket lives in <notations>; the duration math <time-modification> stays.
        Assert.All(tuplets, t => Assert.Equal("notations", t.Parent!.Name.LocalName));
        Assert.Equal(3, doc.Descendants("time-modification").Count());
    }

    [Fact]
    public void LeadingRepeatBar_DoesNotEmitEmptyMeasure()
    {
        var doc = Export("""
            octave absolute
            time 4/4
            part m { clef treble }
            section A { m { |: c'4 d' e' f' | g' a' b' c'' :| } }
            form main { A }
            score main { staff m }
            """);
        var measures = doc.Descendants("measure").ToList();
        Assert.Equal(2, measures.Count);   // NOT 3 — no spurious empty leading measure
        Assert.True(measures[0].Elements("barline").Any(b =>
            b.Element("repeat")?.Attribute("direction")?.Value == "forward"));
        Assert.Equal(4, measures[0].Elements("note").Count());   // the notes land in measure 1
    }

    [Fact]
    public void Volta_EmitsEndingBrackets()
    {
        var doc = Export("""
            octave absolute
            time 4/4
            part m { clef treble }
            section A { m { c'4 d' e' f' | } }
            section D { m { g'4 a' b' c'' | } }
            section O { m { e'4 f' g' a' | } }
            form main { |: A [1. D] :| [2. O] }
            score main { staff m }
            """);
        var measures = doc.Descendants("measure").ToList();
        Assert.Equal(3, measures.Count);   // A, 1st ending D, 2nd ending O

        bool HasEnding(XElement m, string num, string type) =>
            m.Elements("barline").Elements("ending").Any(e =>
                e.Attribute("number")?.Value == num && e.Attribute("type")?.Value == type);
        bool HasRepeat(XElement m, string dir) =>
            m.Elements("barline").Elements("repeat").Any(r => r.Attribute("direction")?.Value == dir);

        Assert.True(HasRepeat(measures[0], "forward"));        // A opens the repeat
        Assert.True(HasEnding(measures[1], "1", "start"));     // 1st ending brackets
        Assert.True(HasEnding(measures[1], "1", "stop"));
        Assert.True(HasRepeat(measures[1], "backward"));       // :| caps the 1st ending
        Assert.True(HasEnding(measures[2], "2", "start"));     // 2nd ending
        Assert.True(HasEnding(measures[2], "2", "discontinue"));
        Assert.False(HasRepeat(measures[2], "backward"));      // 2nd ending does not repeat
    }

    [Fact]
    public void NavMarks_EmitSignsAtTargetsAndJumpWordsAtEnds()
    {
        var doc = Export("""
            octave absolute
            time 4/4
            part m { clef treble }
            section A { m { c'4 d' e' f' | } }
            section B { m { g'4 a' b' c'' | } }
            section C { m { e'4 f' g' a' | } }
            form main { A segno B ds al fine C fine }
            score main { staff m }
            """);
        var measures = doc.Descendants("measure").ToList();
        Assert.Equal(3, measures.Count);

        // The segno SIGN opens its target section B (measure 2), not the piece.
        Assert.Single(doc.Descendants("segno"));
        Assert.Single(measures[1].Descendants("segno"));
        // Jump-from words sit at the END of the section just played.
        Assert.Contains(measures[1].Descendants("words"), w => w.Value == "D.S. al Fine"); // end of B
        Assert.Contains(measures[2].Descendants("words"), w => w.Value == "Fine");         // end of C
        // …with the matching <sound> playback attributes.
        Assert.Contains(doc.Descendants("sound"), s => s.Attribute("dalsegno")?.Value == "segno");
        Assert.Contains(doc.Descendants("sound"), s => s.Attribute("fine")?.Value == "yes");
    }

    /// <summary>
    /// A form-level <c>_"text"</c> reaches MusicXML as a bare <c>&lt;words&gt;</c> on the
    /// last measure of the section just played — the position MeasureCollector already
    /// gives it, and the one the jump-from navigation marks above use.
    /// </summary>
    /// <remarks>
    /// The exporter walked past this node while the mapping existed for every one of its
    /// siblings, and the remark above WalkForm said so — but that remark said the same about
    /// nav marks and volta endings, which had both shipped. So the assertions here pin the
    /// POSITION and the absence of <c>&lt;sound&gt;</c> rather than merely that a
    /// <c>&lt;words&gt;</c> appears somewhere: "it is exported" is the claim that rotted.
    /// <para>
    /// ⚠️ <c>placement</c> is asserted as "below" against the ENGINE, not against the
    /// exporter: CustomTextEngraver's baseline is below the staff middle while
    /// MusicMarkEngraver's is above it. The first draft of both this test and the method it
    /// covers said "above", copied from the nav marks — the test agreed with the code and
    /// they were wrong together, which is the failure mode a test written after the code is
    /// prone to. The number to check a placement against lives in the engraver.
    /// </para>
    /// </remarks>
    [Fact]
    public void CustomText_EmitsWordsAtEndOfTheSectionJustPlayed()
    {
        var doc = Export("""
            octave absolute
            time 4/4
            part m { clef treble }
            section A { m { c'4 d' e' f' | } }
            section B { m { g'4 a' b' c'' | } }
            form main { A _"rit." B fine }
            score main { staff m }
            """);
        var measures = doc.Descendants("measure").ToList();
        Assert.Equal(2, measures.Count);

        // END of the section just played — measure 1 (A), not the start of B.
        var words = doc.Descendants("words").Where(w => w.Value == "rit.").ToList();
        Assert.Single(words);
        Assert.Contains(measures[0].Descendants("words"), w => w.Value == "rit.");
        Assert.DoesNotContain(measures[1].Descendants("words"), w => w.Value == "rit.");

        // BELOW the staff — CustomTextEngraver's baseline (2.0 - 5.5 Y-up from the staff
        // middle) is under it. The Fine in the same form is a nav mark, whose engraver puts
        // it at 2.0 - (-2.0), over the staff: one document, the two sides opposed, so a later
        // edit cannot quietly collapse them onto one value.
        var dir = words[0].Ancestors("direction").Single();
        var fine = doc.Descendants("words").Single(w => w.Value == "Fine")
                      .Ancestors("direction").Single();
        Assert.Equal("below", dir.Attribute("placement")?.Value);
        Assert.Equal("above", fine.Attribute("placement")?.Value);

        // Free text carries no playback meaning: the <direction> holding it has no <sound>
        // — while Fine, which does, keeps its own.
        Assert.Empty(dir.Elements("sound"));
        Assert.NotEmpty(fine.Elements("sound"));
    }

    [Fact]
    public void PercentRepeat_ExportsMeasureRepeatSign()
    {
        // A one-measure percent body exports the SIGN: repeated measures keep
        // their REAL notes under measure-style measure-repeat (hidden behind
        // the % by importers, full bars for strict ones).
        var doc = Export("""
            part m { clef treble }
            section A { m { repeat percent 2 { c'4 d' e' f' | } } }
            form main { A }
            score main { staff m }
            """);
        Assert.Equal(2, doc.Descendants("measure").Count());
        Assert.Equal(8, doc.Descendants("note").Count());
        Assert.Single(doc.Descendants("measure-repeat")
            .Where(m => (string?)m.Attribute("type") == "start"));
    }

    [Fact]
    public void DrumNotes_SerializeUnpitchedWithNotehead()
    {
        var doc = Export("""
            part kit { clef percussion }
            section A { kit { bd4 sn hh hh | } }
            form main { A }
            score main { staff kit }
            """);
        Assert.Equal(4, doc.Descendants("unpitched").Count());
        Assert.Equal("percussion", doc.Descendants("clef").First().Element("sign")!.Value);
        // hh carries the cross head; bd/sn have none.
        Assert.Equal(2, doc.Descendants("notehead").Count(n => n.Value == "x"));
    }

    [Fact]
    public void DirectionFamily_ExportsWedgePedalOttavaRepeatHarmony()
    {
        var doc = Export("""
            octave absolute
            part pno { clef treble }
            section A { pno {
              |: c'4@sustain@chord(Dm7) d'@cresc e' f'@!sustain | g'1@f :|
              a'4@ottava b' a'@!ottava g' | c'1@chord(G7/B) |
            } }
            form main { A }
            score main { staff pno }
            """);
        Assert.Single(doc.Descendants("repeat").Where(r => (string?)r.Attribute("direction") == "forward"));
        Assert.Single(doc.Descendants("repeat").Where(r => (string?)r.Attribute("direction") == "backward"));
        Assert.Single(doc.Descendants("wedge").Where(w => (string?)w.Attribute("type") == "crescendo"));
        Assert.Single(doc.Descendants("wedge").Where(w => (string?)w.Attribute("type") == "stop"));
        Assert.Single(doc.Descendants("pedal").Where(p => (string?)p.Attribute("type") == "start"));
        Assert.Single(doc.Descendants("pedal").Where(p => (string?)p.Attribute("type") == "stop"));
        Assert.Single(doc.Descendants("octave-shift").Where(o => (string?)o.Attribute("type") == "down"));
        Assert.Single(doc.Descendants("octave-shift").Where(o => (string?)o.Attribute("type") == "stop"));
        var kinds = doc.Descendants("harmony").Select(h => h.Element("kind")!.Value).ToList();
        Assert.Equal(new[] { "minor-seventh", "dominant" }, kinds);
        Assert.Equal("B", doc.Descendants("bass-step").Single().Value);
    }

    [Fact]
    public void LyricElision_SplitsInsideOneLyric()
    {
        var doc = Export("""
            part v { clef treble }
            section A {
              v { c'4 d' | }
              lyrics { glo ri~a | }
            }
            form main { A }
            score main { staff v }
            """);
        var lyric = doc.Descendants("lyric")
            .First(l => l.Elements("elision").Any());
        Assert.Equal(new[] { "ri", "a" }, lyric.Elements("text").Select(t => t.Value));
    }

    private static string PhraseBook(string phrases, string music)
        => "octave absolute\npart m { clef treble }\n" + phrases
           + "\nsection A { m {\n" + music + "\n} }\n"
           + "form main { A }\nscore main { staff m }\n";

    private static string[] GraceNotes(string book)
        => Export(book).Descendants("note")
            .Where(n => n.Element("grace") != null)
            .Select(n => n.Element("pitch")!.Element("step")!.Value
                         + n.Element("pitch")!.Element("octave")!.Value
                         + "/" + n.Element("type")!.Value)
            .ToArray();

    /// <summary>
    /// A phrase named in a <c>grace { }</c> body is EXPORTED — the same notes, in the same
    /// order, as writing them in the body.
    /// </summary>
    /// <remarks>
    /// ⚠️ THIS WALK WAS THE FOURTH READER OF ONE STATEMENT. What a grace body carries is
    /// stated once in <c>Semantics.GraceBodySupport</c>, whose remarks said "read twice";
    /// session 300 taught the page and LYS4020 that a phrase reference is a container, and
    /// <c>MusicXmlExporter.ProcessGraceNotes</c> was still walking <c>grace.Body.Items</c>
    /// itself. MEASURED 2026-08-30 (session 301, scratch/p301/ab): the book exported NO
    /// <c>&lt;grace/&gt;</c> at all where the page engraved two grace notes.
    /// ⚠️ The first assert is what makes the second one say something: two EMPTY exports are
    /// also "equal", and empty is exactly what the defect produced.
    /// </remarks>
    [Theory]
    [InlineData("phrase G { d'16 e' }", "grace { G } c'1 |", "grace { d'16 e' } c'1 |")]
    [InlineData("phrase I { d'16 e' }\nphrase O { I f'16 }",
                "grace { O } c'1 |", "grace { d'16 e' f'16 } c'1 |")]
    [InlineData("phrase G { d'16 e' }",
                "grace { c'16 G a'16 } c'1 |", "grace { c'16 d'16 e' a'16 } c'1 |")]
    public void APhraseReferenceInAGraceBody_ExportsWhatThePhraseHolds(
        string phrases, string written, string control)
    {
        Assert.NotEmpty(GraceNotes(PhraseBook("", control)));
        Assert.Equal(GraceNotes(PhraseBook("", control)),
                     GraceNotes(PhraseBook(phrases, written)));
    }

    /// <summary>
    /// A grace group keeps its OWN duration memory, opening at an eighth and threading a
    /// written value forward, and it never touches the main stream's.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE EIGHTH IS THE LAYOUT'S RULE (<c>MeasureCollector.CollectGraceNotes</c>'
    /// <c>graceDefaultDuration</c>; LilyPond has no grace-specific default at all), and this
    /// walker used to be a FOURTH answer to it: it shared the exporter's main-stream default,
    /// so <c>grace { c' } d'4</c> exported a QUARTER where the page, the MIDI and the
    /// <c>.ly</c> twin all say an eighth (MEASURED 2026-08-30, session 301). 2026-08-01 found
    /// the same question answered three ways and made the page its one home; this row is the
    /// fourth reader arriving there.
    /// <para>
    /// ⚠️ THE THIRD ASSERT IS THE ONE THAT MOVED A TRACKED BOOK. Sharing the main stream's
    /// memory meant a grace also LEAKED its value outward: <c>Fixtures/test/ossia-beams.lys</c>
    /// writes <c>d4@glissando grace { d8 } c</c> in 4/4, and the exported bar summed to 3.5
    /// quarters because the <c>c</c> inherited the grace's eighth instead of the <c>d4</c>'s
    /// quarter (MEASURED on the corpus sweep, scratch/p301/sweep.json — the only tracked book
    /// this session moved). The page and the MIDI had it right the whole time.
    /// </para>
    /// </remarks>
    [Fact]
    public void AGraceGroup_OpensAtAnEighthAndLeavesTheMainStreamAlone()
    {
        static string[] Types(string music)
            => Export(PhraseBook("", music)).Descendants("note")
                .Select(n => (n.Element("grace") != null ? "grace:" : "main:")
                             + n.Element("type")!.Value)
                .ToArray();

        // The undurated grace opens at the group's eighth, not at the main stream's quarter…
        Assert.Equal(new[] { "grace:eighth", "main:quarter" }, Types("grace { c' } d'4 |"));
        // …a written value threads to the next grace item…
        Assert.Equal(new[] { "grace:16th", "grace:16th", "main:quarter" },
            Types("grace { c'16 d' } e'4 |"));
        // …and none of it reaches the note after the grace, which is still the quarter it
        // inherits from the main stream.
        Assert.Equal(new[] { "main:quarter", "grace:16th", "main:quarter" },
            Types("c'4 grace { d'16 } e' |"));
    }

    /// <summary>Every exported pitch as step+octave, in order — RELATIVE mode, where the
    /// frame a phrase opens and the anchor it hands back are the things being watched.</summary>
    private static string[] RelativePitches(string phrases, string music)
        => Export("part m { clef treble }\n" + phrases
                  + "\nsection A { m {\n" + music + "\n} }\n"
                  + "form main { A }\nscore main { staff m }\n")
            .Descendants("pitch")
            .Select(p => p.Element("step")!.Value + p.Element("octave")!.Value)
            .ToArray();

    /// <summary>
    /// A phrase body is EXPORTED in a fresh relative frame inside a grace: the same
    /// reference writes the same pitches wherever the grace starts from.
    /// </summary>
    /// <remarks>
    /// ⚠️ THIS ROW EXISTS BECAUSE A POISON FOUND NOTHING. Session 301 poisoned the fresh
    /// frame in this walker (<c>h_xmlnoframe</c>, scratch/p301/poison.py) and the whole suite
    /// stayed green: the grace rows above are all written <c>octave absolute</c>, where there
    /// is no running frame for the rule to touch. A poison that turns nothing red is the
    /// report that the net is missing (RULES §5.4). The MIDI twin of this row was red on the
    /// first run, which is what said the hole was in the NET rather than in the rule.
    /// ⚠️ The second half is not decoration: "both books agree" is also what a walker that
    /// had stopped resolving relative octaves would say, so the INLINE spelling of the same
    /// two notes is asserted to DISAGREE across the same variation.
    /// </remarks>
    [Fact]
    public void APhraseInAGraceBody_IsExportedInAFreshFrame()
    {
        const string G = "phrase G { d16 e }";
        Assert.Equal(
            RelativePitches(G, "c'2 grace { G } c'2 | e'1 |")[1..3],
            RelativePitches(G, "c,,2 grace { G } c'2 | e'1 |")[1..3]);
        Assert.NotEqual(
            RelativePitches("", "c'2 grace { d16 e } c'2 | e'1 |")[1..3],
            RelativePitches("", "c,,2 grace { d16 e } c'2 | e'1 |")[1..3]);
    }

    /// <summary>
    /// A reference inside a grace hands the exported chain back at the phrase's ANCHOR — the
    /// chord rule, so the phrase's interior never leaks into the note written after it.
    /// </summary>
    /// <remarks>
    /// ⚠️ THIS ROW ALSO EXISTS BECAUSE A POISON FOUND NOTHING (<c>i_xmlnoanchor</c>), and it
    /// is a SEPARATE row from the fresh frame above for the reason §5.4 asks the red sets to
    /// differ: the two poisons name two sentences, and one test covering both would have said
    /// they were one. ⚠️ The pair is the point — equality with the main-stream reference
    /// alone would also hold for a walker that had stopped moving the frame at all, so the
    /// inline spelling is asserted to leave it somewhere ELSE (<c>grace { d16 d' }</c> ends
    /// an octave up and hands THAT over).
    /// </remarks>
    [Fact]
    public void APhraseInAGraceBody_HandsTheExportedChainBackAtItsAnchor()
    {
        const string H = "phrase H { d16 d' }";
        Assert.Equal(
            RelativePitches(H, "H c2 c2 | e'1 |")[2],
            RelativePitches(H, "grace { H } c2 c2 | e'1 |")[2]);
        Assert.NotEqual(
            RelativePitches("", "grace { d16 d' } c2 c2 | e'1 |")[2],
            RelativePitches(H, "grace { H } c2 c2 | e'1 |")[2]);
    }
}
