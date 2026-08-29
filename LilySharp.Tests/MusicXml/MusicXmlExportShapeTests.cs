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
}
