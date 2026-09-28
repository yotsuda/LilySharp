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
using LilySharp.Core.Midi;
using LilySharp.Core.MusicXml;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The SECTION CARRY RULE (owner's decisions, 2026-09-28): a slur, phrasing slur, tie or hairpin
/// open when a section ends is carried into the section the form plays next and must end there;
/// never over a repeat sign, volta edge or jump; never into a section the part does not play.
/// Checked per form, per part, per play (<see cref="SectionPlayCursor"/>, LYS4023), and every
/// reader draws, sounds and exports by the same rule.
/// </summary>
/// <remarks>
/// MEASURED when the rule landed (poisoned, then restored from a byte copy): with the
/// section-play stamp off (<c>MeasureBuilder.TakePendingSectionPlay</c> stamping nothing) the
/// five rows about the rule itself go red and the rest stay green; with the MIDI tie memory back
/// to one slot (<c>SyncTieSlot</c> keyed on nothing) the two-part tie row and the splitter's two
/// tie rows go red; with the MusicXML carry off the two tie-export rows go red. The tie over a
/// repeat (owner's second decision of the day): with the PLAYED order flattened to the printed
/// one (<c>PlayedOrder.Expand</c> ignoring the repeat roles), or with the repeat ties / hanging
/// ties not added (<c>SectionTieCarry.Apply</c> returning the voice unchanged), the rows about
/// the tie back to <c>|:</c> and into ending 2 go red.
/// </remarks>
[Trait("Category", "Unit")]
public class SectionCarryTests
{
    private static IReadOnlyList<Diagnostic> Check(string src)
    {
        var tree = SyntaxTree.Parse(src);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        return SemanticValidation.Run(tree);
    }

    private static List<Diagnostic> Carry(string src) =>
        Check(src).Where(d => d.Code == DiagnosticCodes.SpanAcrossSectionBoundary).ToList();

    /// <summary>The part <c>vn</c>'s staff.</summary>
    private static Score Collect(string src) => new MeasureCollector().Collect(SyntaxTree.Parse(src), "vn");

    private static int LineOf(string src, Diagnostic d) => src[..d.Span.Start].Count(c => c == '\n') + 1;

    private const string Linear = """
        part vn {
          section C { c''4 d e f( | g4@p a b c~ || }
          section D { c'''4) d@cresc e f@phrasingSlur || }
          section E { g4@f a b c@!phrasingSlur | }
        }
        form main { C D E }
        score main { staff vn }
        """;

    [Fact]
    public void EverySpanCarriedIntoTheNextSection_AndEndedThere_IsDrawnAndSaysNothing()
    {
        var diagnostics = Check(Linear);
        Assert.DoesNotContain(diagnostics, d => d.Code is DiagnosticCodes.SpanAcrossSectionBoundary
            or DiagnosticCodes.UnpairedSlur or DiagnosticCodes.UnpairedSpan or DiagnosticCodes.TieTargetMismatch);

        var score = Collect(Linear);
        var slurs = new SlurDetector().DetectSlurs(score);
        Assert.Single(slurs, s => !s.IsPhrasing && s.StartMeasureIndex == 0 && s.EndMeasureIndex == 2);
        Assert.Single(slurs, s => s.IsPhrasing && s.StartMeasureIndex == 2 && s.EndMeasureIndex == 3);
        Assert.Single(new TieDetector().DetectTies(score), t => t.StartMeasureIndex == 1 && t.EndMeasureIndex == 2);
        var hairpin = Assert.Single(HairpinEngraver.DetectHairpins(score.MusicMarks, score.Dynamics,
            SectionPlays.For(score)));
        Assert.Equal((2, 3), (hairpin.StartMeasureIndex, hairpin.EndMeasureIndex));
    }

    /// <summary>Played order, not written order: in <c>C D C E D</c> the second C's slur is
    /// carried into E (which does not close it), and the last D's <c>)</c> has nothing carried
    /// in from E. Until the rule, the page drew that slur from the second C to the last D, over
    /// the whole of E, and said nothing.</summary>
    [Fact]
    public void AReorderedForm_JudgesEachPlayAgainstTheSectionThatFollowsIt()
    {
        const string src = """
            part vn {
              section C { c''4 d e f( || }
              section D { g4) a b c || }
              section E { c4 d e f || }
            }
            form main { C D C E D }
            score main { staff vn }
            """;
        var carry = Carry(src);
        Assert.Equal(2, carry.Count);
        Assert.Equal(DiagnosticSeverity.Warning, carry[0].Severity);
        Assert.Equal(2, LineOf(src, carry[0]));
        Assert.StartsWith("a slur '(' is carried from section C into section E, which follows it in form 'main', "
            + "and is not closed there, so no slur is drawn", carry[0].Message);
        Assert.Equal(3, LineOf(src, carry[1]));
        Assert.StartsWith("this ')' closes nothing: no slur is open, and section E, played before section D in "
            + "form 'main', carries none into it", carry[1].Message);

        // The page draws the one slur the rule allows: C (play 1) into D (play 2).
        var slur = Assert.Single(new SlurDetector().DetectSlurs(Collect(src)));
        Assert.Equal((0, 1), (slur.StartMeasureIndex, slur.EndMeasureIndex));
    }

    /// <summary>A part with no music in the next section: its bars there are padding.</summary>
    [Fact]
    public void ASpanCarriedIntoASectionThePartDoesNotPlay_IsReported()
    {
        const string src = """
            part vn {
              section C { c''4 d e f( || }
              section E { g4) a b c | }
            }
            part vc {
              section C { c4 d e f || }
              section D { g4 a b c || }
              section E { g4 a b c | }
            }
            form main { C D E }
            score main { staff vn staff vc }
            """;
        var carry = Carry(src);
        Assert.Contains(carry, d => d.Message.StartsWith(
            "a slur '(' is carried from section C into section D, where this part plays nothing in form 'main'"));
        Assert.Contains(carry, d => d.Message.StartsWith("this ')' closes nothing"));
    }

    // ---- ties over repeat signs and endings: carried along the PLAYED order ----------------

    private static List<(int Pitch, int Start, int Length)> Midi(string src) =>
        new MidiExporter().Export(SyntaxTree.Parse(src)).Tracks.SelectMany(t => t.Notes)
            .Select(n => (n.Pitch, n.StartTick, n.DurationTicks)).ToList();

    private static string Xml(string src) => new MusicXmlExporter().Export(SyntaxTree.Parse(src)).ToXml().ToString();

    private static List<NoteItem> Notes(Score score) =>
        score.Voices[0].Measures.SelectMany(m => m.Items).OfType<NoteItem>().ToList();

    /// <summary>Into a repeat from the section before its <c>|:</c>: the body's first note is
    /// printed next and played next on the first pass — an ordinary arc; the second pass just
    /// starts, so it sounds a new note there.</summary>
    [Fact]
    public void ATieIntoARepeatFromBeforeIt_IsAnArc_TiedOnTheFirstPassOnly()
    {
        const string src = """
            part vn {
              section I { c''1~ || }
              section A { c''1 | d1 || }
            }
            form main { I |: A :| }
            score main { staff vn }
            """;
        Assert.DoesNotContain(Check(src), d => d.Code is DiagnosticCodes.SpanAcrossSectionBoundary
            or DiagnosticCodes.TieTargetMismatch);
        var score = Collect(src);
        Assert.Single(new TieDetector().DetectTies(score), t => (t.StartMeasureIndex, t.EndMeasureIndex) == (0, 1));
        Assert.DoesNotContain(Notes(score), n => n.HasRepeatTie || n.HasLaissezVibrer);
        // 1920 ticks a whole note: I + A's first bar as one note, then d; pass 2 re-attacks c.
        Assert.Equal([(84, 0, 3840), (86, 3840, 1920), (84, 5760, 1920), (86, 7680, 1920)], Midi(src));
        string xml = Xml(src);
        Assert.Equal(1, CountOf(xml, "<tie type=\"start\""));
        Assert.Equal(1, CountOf(xml, "<tie type=\"stop\""));
    }

    /// <summary>At the end of a repeat's body, back to its <c>|:</c>: the note the pass returns
    /// to is not printed next — the tied note gets a hanging tie, the body's first note a repeat
    /// tie (drawn once), and the MIDI sustains into the second pass.</summary>
    [Fact]
    public void ATieBackToTheRepeatStart_HangsAndGivesTheTargetARepeatTie()
    {
        const string src = """
            part vn {
              section A { c''1 | c1~ || }
            }
            form main { |: A :| }
            score main { staff vn }
            """;
        Assert.DoesNotContain(Check(src), d => d.Code is DiagnosticCodes.SpanAcrossSectionBoundary
            or DiagnosticCodes.TieTargetMismatch);
        var score = Collect(src);
        Assert.Empty(new TieDetector().DetectTies(score));
        var notes = Notes(score);
        Assert.True(notes[0].HasRepeatTie);
        Assert.True(notes[1].HasLaissezVibrer);
        Assert.Equal([(84, 0, 1920), (84, 1920, 3840), (84, 5760, 1920)], Midi(src));
        string xml = Xml(src);
        Assert.Equal(1, CountOf(xml, "<tie type=\"start\""));
        Assert.Equal(1, CountOf(xml, "<tie type=\"stop\""));
        Assert.Contains("\\repeatTie", Twin(src));
    }

    [Fact]
    public void ATieBackToTheRepeatStart_OnAnotherPitch_IsLys4007_AndDrawsNothing()
    {
        const string src = """
            part vn {
              section A { c''1 | e1~ || }
            }
            form main { |: A :| }
            score main { staff vn }
            """;
        var diagnostics = Check(src);
        Assert.Single(diagnostics, d => d.Code == DiagnosticCodes.TieTargetMismatch);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCodes.SpanAcrossSectionBoundary);
        Assert.DoesNotContain(Notes(Collect(src)), n => n.HasRepeatTie || n.HasLaissezVibrer);
    }

    /// <summary>From the body into the endings: ending 1 is printed next (an arc), ending 2 is
    /// reached on the next pass (a repeat tie); and out of the last ending into what follows the
    /// block (an arc).</summary>
    [Fact]
    public void ATieIntoTheEndings_ArcsIntoTheFirst_AndRepeatTiesTheSecond_AndLeavesTheLastByAnArc()
    {
        const string src = """
            part vn {
              section A { c''1 | c1~ || }
              section B { c''1 || }
              section C { c''1 | c1~ || }
              section D { c''1 | }
            }
            form main { |: A [1. B] :| [2. C] D }
            score main { staff vn }
            """;
        Assert.DoesNotContain(Check(src), d => d.Code is DiagnosticCodes.SpanAcrossSectionBoundary
            or DiagnosticCodes.TieTargetMismatch);
        var score = Collect(src);
        var arcs = new TieDetector().DetectTies(score).Select(t => (t.StartMeasureIndex, t.EndMeasureIndex)).ToList();
        Assert.Contains((1, 2), arcs);   // A into ending 1
        Assert.Contains((4, 5), arcs);   // ending 2 into D
        var notes = Notes(score);
        Assert.True(notes[3].HasRepeatTie);   // ending 2's first note
        Assert.False(notes[2].HasRepeatTie);  // ending 1's: the arc
        Assert.False(notes[1].HasLaissezVibrer);
        string twin = Twin(src);
        Assert.Equal(1, CountOf(twin, "\\repeatTie"));
        // Pass 1: A's c into B; pass 2: A's c into C; C's c into D.
        Assert.Equal([(84, 0, 1920), (84, 1920, 3840), (84, 5760, 1920), (84, 7680, 3840),
            (84, 11520, 3840)], Midi(src));
    }

    [Fact]
    public void ASlurIntoARepeatFromBeforeIt_IsStillAnError()
    {
        const string src = """
            part vn {
              section I { c''1( || }
              section A { d''1) | }
            }
            form main { I |: A :| }
            score main { staff vn }
            """;
        Assert.Contains(Carry(src), d => d.Severity == DiagnosticSeverity.Error
            && d.Message.StartsWith("a slur '(' would be carried from section I into section A over a repeat sign"));
    }

    private static string Twin(string src) =>
        new LilySharp.Core.LilyPond.LilyPondExporter().Export(SyntaxTree.Parse(src));

    [Fact]
    public void ASlurIntoAVoltaEnding_IsAnError()
    {
        const string src = """
            part vn {
              section A { c''4 d e f( || }
              section B { g4) a b c || }
              section C { c4 d e f | }
            }
            form main { |: A [1. B :| [2. C] }
            score main { staff vn }
            """;
        var carry = Carry(src);
        Assert.Contains(carry, d => d.Severity == DiagnosticSeverity.Error
            && d.Message.StartsWith("a slur '(' would be carried from section A into section B over a repeat sign, a volta ending or a jump"));
        Assert.Empty(new SlurDetector().DetectSlurs(Collect(src)));
    }

    [Fact]
    public void AHairpinNothingEndsInTheNextSection_IsCutAtItsOwnSectionsEnd()
    {
        const string src = """
            part vn {
              section C { c''4@p d e f@cresc || }
              section D { g4 a b c || }
              section E { c4@f d e f | }
            }
            form main { C D E }
            score main { staff vn }
            """;
        var carry = Assert.Single(Carry(src));
        Assert.StartsWith("a hairpin is carried from section C into section D, which follows it in form 'main', "
            + "and nothing ends it there, so it is cut at the end of section C", carry.Message);
        var score = Collect(src);
        var hairpin = Assert.Single(HairpinEngraver.DetectHairpins(score.MusicMarks, score.Dynamics, SectionPlays.For(score)));
        Assert.Equal((1, 0), (hairpin.EndMeasureIndex, hairpin.EndItemIndex));
    }

    /// <summary>Every form a score plays is checked — not only the first score's. In
    /// <c>form other { D C }</c> C is last and D first: the slur is lost there.</summary>
    [Fact]
    public void AnotherScoresForm_IsCheckedToo_AndItsMessagesNameTheForm()
    {
        const string src = """
            part vn {
              section C { c''4 d e f( || }
              section D { g4) a b c | }
            }
            form main { C D }
            form other { D C }
            score main { staff vn }
            score other "rev" { staff vn }
            """;
        var slurs = Check(src).Where(d => d.Code == DiagnosticCodes.UnpairedSlur).ToList();
        Assert.Equal(2, slurs.Count);
        Assert.All(slurs, d => Assert.EndsWith("(in form 'other')", d.Message));

        // Only the first score's form: nothing.
        Assert.DoesNotContain(Check(src.Replace("score other \"rev\" { staff vn }", "")),
            d => d.Code == DiagnosticCodes.UnpairedSlur);
    }

    /// <summary>A tie carried into the next section in a book of TWO parts sounds as one note:
    /// the MIDI's tie memory is kept per part (it was one slot, and the other part's block of the
    /// same section overwrote it), and the MusicXML carries the open tie into the part's next
    /// block and writes its stop (it was forgotten there, leaving a start with no stop).</summary>
    [Fact]
    public void ATieIntoTheNextSection_SoundsAsOneNote_AndIsExportedAsAPair_WithAnotherPartBetween()
    {
        const string src = """
            part vn {
              section C { c''4 d e c~ || }
              section D { c''4 a b c | }
            }
            part vc {
              section C { c4 d e f || }
              section D { g4 a b c | }
            }
            form main { C D }
            score main { staff vn staff vc }
            """;
        Assert.Empty(Carry(src));
        var vn = new MidiExporter().Export(SyntaxTree.Parse(src)).Tracks.First(t => t.Notes.Count > 0).Notes;
        Assert.Equal(7, vn.Count);
        Assert.Contains(vn, n => n.StartTick == 1440 && n.DurationTicks == 960);

        string xml = new MusicXmlExporter().Export(SyntaxTree.Parse(src)).ToXml().ToString();
        Assert.Equal(1, CountOf(xml, "<tie type=\"start\""));
        Assert.Equal(1, CountOf(xml, "<tie type=\"stop\""));
    }

    private static int CountOf(string text, string what)
    {
        int n = 0;
        for (int i = text.IndexOf(what, System.StringComparison.Ordinal); i >= 0;
             i = text.IndexOf(what, i + 1, System.StringComparison.Ordinal))
            n++;
        return n;
    }

    /// <summary>A span never carried keeps its family's own code — the rule changes nothing inside
    /// one section.</summary>
    [Fact]
    public void ASlurNeverClosedInTheLastSection_IsStillLys4010()
    {
        const string src = """
            part vn {
              section C { c''4 d e f || }
              section D { g4( a b c | }
            }
            form main { C D }
            score main { staff vn }
            """;
        Assert.Empty(Carry(src));
        Assert.Single(Check(src), d => d.Code == DiagnosticCodes.UnpairedSlur);
    }
}
