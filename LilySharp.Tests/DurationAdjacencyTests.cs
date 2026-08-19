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
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The adjacency rule: a duration is GLUED to what it lengthens (<c>c4</c>,
/// <c>&lt;c e g&gt;4</c>). A glued number on a chord/arpeggio MEMBER is therefore a
/// misplaced duration (LYS0015 — members share one, written after the bracket),
/// while a spaced number outside brackets is a detached duration (LYS0016) and a
/// spaced number inside brackets stays a scale degree. This is what keeps
/// <c>&lt;c e g2&gt;</c> from silently reading as C-E-G plus a degree-2 D.
/// </summary>
[Trait("Category", "Unit")]
public class DurationAdjacencyTests
{
    private static bool Has(string source, string code) =>
        SyntaxTree.Parse(source).Diagnostics.Any(d => d.Code == code);

    [Theory]
    [InlineData("{ <c e g2> }")]
    [InlineData("{ <c2 e g> }")]     // on any member, not just the last
    [InlineData("{ <c e g2.> }")]    // glued dots are swallowed with it
    [InlineData("{ << c8 e g >> }")] // arpeggio members carry no durations either
    [InlineData("{ << c e8 g >> }")]
    public void GluedNumberOnAMember_IsADurationError(string source)
        => Assert.True(Has(source, DiagnosticCodes.DurationInsideChord));

    // ===== A spaced number outside brackets (bare duration, 2026-08-19) =====

    /// <summary>
    /// A spaced number OUTSIDE brackets stopped being an error when the bare
    /// duration landed: it repeats the previous note/chord/slash
    /// (LILYPOND-REF: lily/parser.yy music_embedded). LYS0016 survives for the
    /// one shape that still means nothing - a bare duration with no event
    /// before it to repeat (reported by the semantic validator, since the
    /// parser now builds a node either way).
    /// </summary>
    [Theory]
    [InlineData("{ c 4 }")]           // repeats c as a quarter
    [InlineData("{ <c e g> 2 }")]     // repeats the chord as a half
    [InlineData("{ c4 r4 4 }")]       // rests are transparent to the run
    public void SpacedNumberAfterAnEvent_IsARepeat_NotAnError(string source)
    {
        Assert.False(Has(source, DiagnosticCodes.DetachedDuration), source);
        var tree = SyntaxTree.Parse(source);
        Assert.DoesNotContain(SemanticValidation.Run(tree),
            d => d.Code == DiagnosticCodes.DetachedDuration);
    }

    [Theory]
    [InlineData("{ 4 c d }")]         // nothing before it in the body
    [InlineData("{ r4 4 }")]          // a rest alone is not a repeatable event
    [InlineData("{ << c e g >>4 4 }")] // an arpeggio breaks the run (no single answer)
    public void BareDurationWithNothingToRepeat_IsStillLYS0016(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.Contains(SemanticValidation.Run(tree),
            d => d.Code == DiagnosticCodes.DetachedDuration);
    }

    [Theory]
    [InlineData("{ c4 d4. e2 }")]     // glued durations, the normal form
    [InlineData("{ <c e g>2 }")]
    [InlineData("{ <c e g>'4 }")]     // glued through the postfix octave mark
    [InlineData("{ << c e g >>2 }")]
    [InlineData("{ <c 3 5>4 }")]      // spaced numbers inside brackets are degrees
    [InlineData("{ <c e g 2> }")]
    [InlineData("{ <1 3 5>2 }")]      // a first-member number is the degree anchor
    [InlineData("{ << c 3 5 >> }")]
    [InlineData("{ r2. R1*4 }")]
    public void GluedDurationsAndSpacedDegrees_StayClean(string source)
    {
        Assert.False(Has(source, DiagnosticCodes.DurationInsideChord), source);
        Assert.False(Has(source, DiagnosticCodes.DetachedDuration), source);
    }

    // ===== A '.' that no rule claimed (LYS0023) =====

    /// <summary>
    /// The other half of the adjacency rule: a duration is a NUMBER lengthened by
    /// dots, so a dot that no number claimed is not a duration.
    /// LILYPOND-REF: lily/parser.yy steno_duration — UNSIGNED dots. MEASURED on
    /// 2.26.0: <c>c'4 g'.</c> is <c>syntax error, unexpected '.'</c> and the file is
    /// refused, so any reading Lily# invented here would be a divergence from the
    /// twin it exports to.
    /// </summary>
    [Theory]
    [InlineData("{ c4 g. a4 }")]
    [InlineData("{ c4. g. }")]      // after a DOTTED note, where the reading is ambiguous
    [InlineData("{ g. }")]
    [InlineData("{ <c e g>4 <d f a>. }")]
    public void ADotWithNoNumber_IsADurationError(string source)
        => Assert.True(Has(source, DiagnosticCodes.UnclaimedDot), source);

    /// <summary>
    /// The SECOND cause, and the commoner one on disk: the legacy dotted spelling of
    /// an annotation whose argument now goes in parentheses. Its dot reaches no rule
    /// either — the mark parser takes only the dots it owns — so it used to disappear
    /// with the rest of the spelling, leaving a file that compiled and drew the wrong
    /// thing. Both causes are in the message for this reason.
    /// </summary>
    [Theory]
    [InlineData("{ c4@finger.3 }")]
    [InlineData("{ c4@chord.C }")]
    [InlineData("{ c4@mark.A }")]
    [InlineData("{ c4@bend.half }")]
    [InlineData("{ c4@notehead.x }")]
    public void TheLegacyDottedAnnotationSpelling_IsAnUnclaimedDot(string source)
        => Assert.True(Has(source, DiagnosticCodes.UnclaimedDot), source);

    /// <summary>One mistake, written twice, is reported once.</summary>
    [Fact]
    public void ARunOfBareDots_IsReportedOnce()
        => Assert.Equal(1, SyntaxTree.Parse("{ c4 g.. a4 }").Diagnostics
            .Count(d => d.Code == DiagnosticCodes.UnclaimedDot));

    /// <summary>
    /// ★ The body of the fix, not the diagnostic: the dot STAYS in the tree. It used
    /// to reach no rule, so the music loop's skip recovery dropped it and the book
    /// spelled itself back out as <c>c4 ga4</c> — a different piece of music, and
    /// every node after it standing one character early (HANDOFF §1 第168 ⑴).
    /// </summary>
    [Theory]
    [InlineData("{ c4 g. a4 }")]
    [InlineData("{ c4 g.. a4 }")]
    [InlineData("{ c4. g. a4 }")]
    public void ABareDot_StaysInTheTree(string source)
        => Assert.Equal(source, SyntaxTree.Parse(source).GetRoot().ToFullString());

    /// <summary>
    /// ★ This was the positive control for the case above, and it asserted the OPPOSITE:
    /// "a stray token with no rule of its own — here a bare <c>?</c> — is STILL DROPPED by
    /// the same skip recovery, so the round trip above is a statement about the dot."
    /// </summary>
    /// <remarks>
    /// ⚠️ That control pinned the defect it was standing next to. On 2026-08-16 the drop
    /// became a report-and-keep everywhere (LYS0030 and its siblings), so a stray token is
    /// no longer the thing the dot is being contrasted WITH — the round trip is now a
    /// parser invariant and the dot is one instance of it. The control that survives the
    /// change is the one the dot always needed: the token is kept AND the mistake is named,
    /// so a reader is not left to notice a silent nothing.
    /// </remarks>
    [Fact]
    public void AStrayTokenWithNoRule_IsReportedAndKept()
    {
        const string src = "{ c4 g? a4 }";
        var tree = SyntaxTree.Parse(src);
        Assert.Equal(src, tree.GetRoot().ToFullString());
        Assert.Contains(tree.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    /// <summary>
    /// The spellings that DO carry dots keep working, and — the point that makes the
    /// diagnostic's advice true — a note with no duration inherits the previous one
    /// WITH its dots, so nothing needs a bare dot to be sayable.
    /// </summary>
    [Theory]
    [InlineData("{ c4 g4. a4 }")]
    [InlineData("{ c4. g a4 }")]           // inheritance carries the dot
    [InlineData("{ r2. R1*4 }")]
    [InlineData("{ c4@staccato.up d4 }")]  // a dotted PLACEMENT qualifier is not a duration
    [InlineData("{ c4@ds.al.fine d4 }")]   // nor is a dotted annotation name
    [InlineData("{ c4@text(\"a\").down }")]
    [InlineData("{ c4 ds al fine d4 }")]   // the bare navigation mark takes no dots at all
    public void DotsThatBelongToSomething_StayClean(string source)
        => Assert.False(Has(source, DiagnosticCodes.UnclaimedDot), source);

    /// <summary>
    /// The measured claim the diagnostic's text makes: <c>c4. g</c> is two DOTTED
    /// quarters. If inheritance ever stopped carrying the dots, the advice "you do
    /// not need a bare dot" would become false, and this net says so.
    /// </summary>
    [Fact]
    public void ANoteWithNoDuration_InheritsTheDotsToo()
    {
        var notes = new MeasureCollector()
            .Collect(SyntaxTree.Parse("{ c4. g a4 }")).Voice.Measures
            .SelectMany(m => m.Items).OfType<NoteItem>().ToArray();
        Assert.Equal(1, notes[0].Dots);
        Assert.Equal(1, notes[1].Dots);                                  // inherited
        Assert.Equal(notes[0].BaseDuration, notes[1].BaseDuration);
        Assert.Equal(0, notes[2].Dots);                                  // written a4
    }

    [Fact]
    public void GluedMemberDuration_IsSwallowed_NotReadAsADegree()
    {
        // Best-effort recovery: <c e g2> stays a three-note chord — the old
        // behavior silently ADDED a degree-2 note (a D) to it.
        var chord = new MeasureCollector()
            .Collect(SyntaxTree.Parse("{ <c e g2> }")).Voice.Measures
            .SelectMany(m => m.Items).OfType<ChordItem>().First();
        Assert.Equal(new[] { 60, 64, 67 }, chord.Notes.Select(n => n.Midi).ToArray());
    }

    [Fact]
    public void ErroneousSource_StillRendersBestEffort()
    {
        // The preview's contract: a file with parse errors renders whatever DID
        // parse (the CLI, by contrast, gates on errors and writes nothing). The
        // erroneous chord keeps its real notes and the following music survives.
        var src = "part m { clef treble }\n"
                + "section A { m { <c e g2>2 d 4 e2 } }\n"
                + "form main { A }\nscore main { staff m }";
        var tree = SyntaxTree.Parse(src);
        Assert.True(tree.HasErrors); // LYS0015 + LYS0016 are both in there
        var svg = LilySharp.Core.Svg.SvgGenerator.Generate(tree);
        Assert.Contains("data-pos", svg); // real engraved content, not a blank page
    }
}
