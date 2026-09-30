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
using LilySharp.Core.LilyPond;
using LilySharp.Core.Midi;
using LilySharp.Core.MusicXml;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A form ending's range or list — <c>[1-2. B]</c>, <c>[1,3. B]</c> — names the passes it plays
/// on, and every reader of the form plays the run by them (<see cref="RepeatPasses"/>, the one
/// spelling): the MIDI, the PLAYED order a tie is carried along, the neighbours a split bar is
/// judged by, and the LilyPond twin's <c>\volta</c>. Until 2026-09-29 (HANDOFF §1.1 第663 ⑾) the
/// form's readers played the i-th written ending on pass i and counted the endings, so
/// <c>|: A [1-2. B] :| [3. C]</c> sounded A B A C — while the same music written inline
/// (<see cref="MidiRepeatTests.InlineVoltas_RangeEndingMatchesEachPass"/>) sounded A B A B A C.
/// </summary>
[Trait("Category", "Unit")]
public sealed class FormEndingPassTests
{
    // One whole note a section, at a pitch that names it: A c' (72), B g' (79), C e'' (88), D a' (81).
    private static string Book(string form) => $$"""
        octave absolute
        part m {
          section A { c'1 | }
          section B { g'1 | }
          section C { e''1 | }
          section D { a'1 | }
        }
        form main { {{form}} }
        score main { staff m }
        """;

    private static List<MidiNote> MidiNotes(string src) =>
        new MidiExporter().Export(SyntaxTree.Parse(src)).Tracks.SelectMany(t => t.Notes)
            .OrderBy(n => n.StartTick).ToList();

    /// <summary>The sections the MIDI plays, in order, as their letters.</summary>
    private static string Played(string form) =>
        string.Concat(MidiNotes(Book(form))
            .Select(n => n.Pitch switch { 72 => 'A', 79 => 'B', 88 => 'C', 81 => 'D', _ => '?' }));

    [Theory]
    [InlineData("|: A [1-2. B] :| [3. C]", "ABABAC")]
    [InlineData("|: A [1,3. B] :| [2. C]", "ABACAB")]
    [InlineData("|: A [1. B] :| [2-3. C] D", "ABACACD")]
    [InlineData("|: A [1-2. B C] :| [3. D]", "ABCABCAD")]   // an ending of two sections, on both its passes
    [InlineData("|: A [1. B] :| [2. C]", "ABAC")]           // the plain spelling, as before
    [InlineData("|: A [1,3,5. B] :| [2,4. C]", "ABACABACAB")] // a list past two numbers (session 716)
    public void TheMidiPlaysEachEndingOnThePassesItNames(string form, string played)
        => Assert.Equal(played, Played(form));

    private static List<Diagnostic> Errors(string form, string code)
        => [.. SemanticValidation.Run(SyntaxTree.Parse(Book(form))).Where(d => d.Code == code)];

    /// <summary>Owner's decision 2026-09-30: the endings' numbers are the passes. A count beside
    /// them (LYS1042) and a pass no ending names or two name (LYS1043) are errors — until then
    /// `[1. B] :|*3 [2. C]` printed "1." "2." and played A B A C A C, where LilyPond reads
    /// "1. 2." "3.". A repeat without endings keeps its count.</summary>
    [Fact]
    public void TheNumbersAreThePasses_ACountBesideThemAndAGapAreErrors()
    {
        var count = Assert.Single(Errors("|: A [1. B] :|*3 [2. C]", DiagnosticCodes.RepeatCountWithEndings));
        Assert.Equal(DiagnosticSeverity.Error, count.Severity);
        Assert.Equal(2, count.Span.Length);                          // the "*3"
        Assert.Contains("'|: A [1-2. B] :| [3. C]'", count.Message);
        Assert.Empty(Errors("|: A :|*3", DiagnosticCodes.RepeatCountWithEndings));
        Assert.Empty(Errors("|: A [1-2. B] :| [3. C]", DiagnosticCodes.RepeatCountWithEndings));

        Assert.Contains("pass 2", Assert.Single(Errors("|: A [1. B] :| [3. C]", DiagnosticCodes.EndingPassNotNamedOnce)).Message);
        Assert.Contains("Pass 2 is already '[1-2.'",
            Assert.Single(Errors("|: A [1-2. B] :| [2. C]", DiagnosticCodes.EndingPassNotNamedOnce)).Message);
        // Every pass once: no error — ranges, lists, a first ending alone.
        foreach (string ok in new[] { "|: A [1-2. B] :| [3. C]", "|: A [1,3. B] :| [2. C]",
                     "|: A [1. B] :| [2. C] :| [3. D]", "|: A [1. B] :| C" })
            Assert.Empty(Errors(ok, DiagnosticCodes.EndingPassNotNamedOnce));
    }

    /// <summary>The bracket prints its passes as LilyPond does (scm/output-lib.scm
    /// volta-bracket-interface::calc-text): a run under three each with its point, a longer
    /// one as a range with an en dash, the runs apart by a thin space. Until 2026-09-30 the
    /// page printed the written spelling, "1-2." where LilyPond prints "1. 2.".</summary>
    [Theory]
    [InlineData("|: A [1. B] :| [2. C]", "1.", "2.")]
    [InlineData("|: A [1-2. B] :| [3. C]", "1. 2.", "3.")]
    [InlineData("|: A [1-3. B] :| [4. C]", "1.–3.", "4.")]
    [InlineData("|: A [1,3,5. B] :| [2,4. C]", "1. 3. 5.", "2. 4.")]
    [InlineData("|: A [1,3. B] :| [2. C]", "1. 3.", "2.")]
    public void TheBracketPrintsItsPassesAsLilyPondDoes(string form, string first, string second)
        => Assert.Equal(new[] { first, second },
            Collect(Book(form)).VoltaBrackets.OrderBy(v => v.StartMeasureIndex).Select(v => v.VoltaText).ToArray());

    /// <summary>The last written ending returns when it plays a pass before the last, and ends
    /// in the ':|' no one wrote — on the page and in MusicXML, as LilyPond draws it
    /// (alternative-sequence-iterator.cc:104-107) and as the MIDI already played it. Until
    /// 2026-09-30 only the MIDI returned. A last ending on the last pass alone does not; a
    /// single ending before its written ':|' gets no second bar.</summary>
    [Theory]
    [InlineData("|: A [1,3. B] :| [2. C] D", true)]      // A B A C | A B D
    [InlineData("|: A [1. B] :| [2-3. C] D", true)]      // A B A C | A C D
    [InlineData("|: A [1. B] :| [2. C] D", false)]
    [InlineData("|: A [1-2. B] :| D", false)]
    public void AReturningLastEnding_EndsInARepeatBar(string form, bool returns)
    {
        var measures = Collect(Book(form)).Voices[0].Measures;
        // The written ':|' after the first ending, and the returning last ending's own.
        int returnsDrawn = returns ? 2 : 1;
        Assert.Equal(returnsDrawn, measures.Count(m => m.EndBarline is BarlineType.RepeatEnd or BarlineType.RepeatBoth));
        if (returns)
            Assert.Equal(BarlineType.RepeatEnd, measures[2].EndBarline);   // A B C D, one bar each: C
        Assert.Equal(returnsDrawn, CountOf(Xml(Book(form)), "<repeat direction=\"backward\""));
    }

    [Theory]
    [InlineData(new[] { 1, 2, 3, 5 }, "1.–3. 5.")]
    [InlineData(new[] { 2, 1 }, "1. 2.")]
    [InlineData(new[] { 4 }, "4.")]
    public void ThePrintedTextGroupsRuns(int[] passes, string printed)
        => Assert.Equal(printed, SyntaxFacts.VoltaPrintedText(passes));

    /// <summary>The form and the inline spelling of one piece sound the same: c d c e c d.</summary>
    [Fact]
    public void TheFormAndTheInlineSpelling_SoundTheSame()
    {
        var inline = MidiNotes("{ |: c4 [1,3. d4] :| [2. e4] }").Select(n => n.Pitch).ToArray();
        Assert.Equal(new[] { 60, 62, 60, 64, 60, 62 }, inline);
        Assert.Equal("ABACAB", Played("|: A [1,3. B] :| [2. C]"));
    }

    /// <summary>The playback highlight: a ranged ending is ONE printed copy, lit on each of its
    /// passes. When the ending replays the body's section, the ending's copy is the second
    /// printed one (ordinal 1) on every pass it plays — the MIDI advanced the ordinal by the
    /// PASS until 2026-09-29, lighting a third copy that is not printed.</summary>
    [Theory]
    [InlineData("|: A [1-2. A] :| [3. B]", new[] { 0, 1, 0, 1, 0 })]
    [InlineData("|: A [1,3. A] :| [2. B]", new[] { 0, 1, 0, 0, 1 })]
    public void TheHighlightLightsTheEndingsOwnPrintedCopy_OnEveryPassItPlays(string form, int[] ordinals)
        => Assert.Equal(ordinals, MidiNotes(Book(form)).Where(n => n.Pitch == 72).Select(n => n.SourceOrdinal).ToArray());

    // ---- the page: the played order a tie is carried along ------------------------------

    private static Score Collect(string src) => new MeasureCollector().Collect(SyntaxTree.Parse(src), "vn");

    private static List<NoteItem> Notes(Score score) =>
        score.Voices[0].Measures.SelectMany(m => m.Items).OfType<NoteItem>().ToList();

    private static string Xml(string src) => new MusicXmlExporter().Export(SyntaxTree.Parse(src)).ToXml().ToString();

    private static string Twin(string src) => new LilyPondExporter().Export(SyntaxTree.Parse(src));

    private static int CountOf(string text, string what)
    {
        int n = 0;
        for (int i = text.IndexOf(what, System.StringComparison.Ordinal); i >= 0;
             i = text.IndexOf(what, i + 1, System.StringComparison.Ordinal))
            n++;
        return n;
    }

    /// <summary>Played A B A C A B D: the tie out of <c>[2. C]</c> goes back to A (pass 3), never
    /// into D, which is printed after C but is played after B. So C's tied note hangs, A's first
    /// note takes a repeat tie, and the MIDI sustains C into A — and A's own tie reaches B by an
    /// arc (pass 1) and C by a repeat tie (pass 2), as with plain endings.</summary>
    [Fact]
    public void ATieOutOfAListedEnding_IsCarriedToThePlayThatFollowsItOnItsPass()
    {
        const string src = """
            part vn {
              section A { c''1 | c1~ || }
              section B { c''1 || }
              section C { c''1 | c1~ || }
              section D { c''1 | }
            }
            form main { |: A [1,3. B] :| [2. C] D }
            score main { staff vn }
            """;
        var tree = SyntaxTree.Parse(src);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        Assert.DoesNotContain(SemanticValidation.Run(tree), d => d.Code is DiagnosticCodes.SpanAcrossSectionBoundary
            or DiagnosticCodes.TieTargetMismatch);
        var score = Collect(src);
        var arcs = new TieDetector().DetectTies(score).Select(t => (t.StartMeasureIndex, t.EndMeasureIndex)).ToList();
        Assert.Equal([(1, 2)], arcs);          // A into ending 1 only; C's tie leaves by no arc
        var notes = Notes(score);
        Assert.True(notes[0].HasRepeatTie);    // A's first note: C's tie, on pass 3
        Assert.True(notes[3].HasRepeatTie);    // C's first note: A's tie, on pass 2
        Assert.True(notes[4].HasLaissezVibrer); // C's tied note hangs
        Assert.False(notes[1].HasLaissezVibrer);
        Assert.False(notes[5].HasRepeatTie);   // D: nothing is carried into it
        // 1920 ticks a whole note. Pass 1: A c, A c~ + B c; pass 2: A c, A c~ + C c, C c~ + (pass 3)
        // A c; A c~ + B c; then D.
        var midi = MidiNotes(src).Select(n => (n.Pitch, n.StartTick, n.DurationTicks)).ToList();
        Assert.Equal([(84, 0, 1920), (84, 1920, 3840), (84, 5760, 1920), (84, 7680, 3840),
            (84, 11520, 3840), (84, 15360, 3840), (84, 19200, 1920)], midi);
        // MusicXML: two starts; stops on B, on C and on A (C's tie back); C's start hangs.
        string xml = Xml(src);
        Assert.Equal(2, CountOf(xml, "<tie type=\"start\""));
        Assert.Equal(3, CountOf(xml, "<tie type=\"stop\""));
        Assert.Equal(1, CountOf(xml, "<tied type=\"let-ring\""));
        // The twin: both repeat ties, and the endings carry their volte.
        string twin = Twin(src);
        Assert.Equal(2, CountOf(twin, "\\repeatTie"));
        Assert.Contains("\\volta 1,3 {", twin);
        Assert.Contains("\\volta 2 {", twin);
    }

    /// <summary>The played order itself, off the stamps: body, ending {1,2}, ending {3}, after.</summary>
    [Fact]
    public void ThePlayedOrder_FollowsTheNumbers()
    {
        var plays = new List<PrintedPlay>
        {
            new(SectionRepeatRole.Body, RunStart: true, Count: 0, Rewinds: false),
            new(SectionRepeatRole.Ending, false, 0, false, PassSet.Of([1, 2])),
            new(SectionRepeatRole.Ending, false, 0, false, PassSet.Of([3])),
            new(SectionRepeatRole.None, false, 0, false),
        };
        Assert.Equal([0, 1, 0, 1, 0, 2, 3], PlayedOrder.Expand(plays));
        // A pass past every number replays the last ending (the inline MIDI's rule).
        plays[0] = new(SectionRepeatRole.Body, RunStart: true, Count: 4, Rewinds: false);
        Assert.Equal([0, 1, 0, 1, 0, 2, 0, 2, 3], PlayedOrder.Expand(plays));
    }

    // ---- the split-bar exemption: the neighbours are the plays the numbers give ------------

    private const string Head = """
        time 4/4
        key c major
        part m { clef bass }
        section A { m { c4 d e f | g4 a | } }
        section E1 { m { b4 c' | d'4 e' f' g' | } }
        section E2 { m { c'4 d' | e'1 | f'4 | } }
        section D { m { g'4 a' b' | c''1 | } }
        """;

    private static string[] BarCodes(string book)
    {
        var validator = new MeasureValidator();
        validator.Validate(SyntaxTree.Parse(book));
        return validator.Diagnostics.Where(d => d.Code is "LYS2001" or "LYS2006").Select(d => d.Code).OrderBy(c => c).ToArray();
    }

    /// <summary>E2 ends on a quarter bar and D opens with the other three quarters — but E2
    /// (pass 2) is followed by A (pass 3), which opens full, and D follows E1, which ends full:
    /// both pieces are short bars. With plain endings E2 is followed by D and the two are one
    /// bar. (E2's quarter is not its own half-bar pickup's complement, so the anacrusis
    /// exemption does not cover it either.)</summary>
    [Fact]
    public void ASplitBarIsJudgedByTheNeighboursThePassesGive()
    {
        Assert.Equal(new[] { "LYS2001", "LYS2006" },
            BarCodes(Head + "form main { |: A [1,3. E1] :| [2. E2] D }\nscore main { staff m }\n"));
        Assert.Empty(BarCodes(Head + "form main { |: A [1. E1] :| [2. E2] D }\nscore main { staff m }\n"));
    }

    // ---- the twin ---------------------------------------------------------------------------

    /// <summary>A plain <c>[1.] [2.]</c> run is written as before — LilyPond's own fill reads it
    /// that way — and a ranged or listed one names its volte on every alternative.</summary>
    [Fact]
    public void TheTwinWritesTheVolteOfARangedOrListedEnding()
    {
        string plain = Twin(Book("|: A [1. B] :| [2. C]"));
        Assert.DoesNotContain("\\volta", plain);
        Assert.Contains("\\repeat volta 2", plain);

        string ranged = Twin(Book("|: A [1-2. B] :| [3. C]"));
        Assert.Contains("\\repeat volta 3", ranged);
        Assert.Contains("\\volta 1,2 {", ranged);
        Assert.Contains("\\volta 3 {", ranged);
    }

    // ---- the value ----------------------------------------------------------------------------

    [Fact]
    public void APassSetIsAValue()
    {
        Assert.Equal(PassSet.Of([1, 3]), PassSet.Of([3, 1, 1]));
        Assert.NotEqual(PassSet.Of([1, 3]), PassSet.Of([1, 2]));
        Assert.True(PassSet.None.IsEmpty);
        Assert.Equal(PassSet.None, PassSet.Of([]));
        Assert.Equal(3, PassSet.Of([1, 3]).Max);
        Assert.True(PassSet.Of([1, 3]).Contains(3));
        Assert.False(PassSet.Of([1, 3]).Contains(2));
        Assert.Equal("1,3", PassSet.Of([1, 3]).ToString());

        // The rule: the count and the ending of a pass.
        var endings = new[] { PassSet.Of([1, 2]), PassSet.Of([3]) };
        Assert.Equal(3, RepeatPasses.Count(null, endings));
        Assert.Equal(5, RepeatPasses.Count(5, endings));
        Assert.Equal(2, RepeatPasses.Count(null, new[] { PassSet.Of([1]) }));
        Assert.Equal(2, RepeatPasses.Count(null, System.Array.Empty<PassSet>()));
        Assert.Equal(0, RepeatPasses.EndingFor(2, endings));
        Assert.Equal(1, RepeatPasses.EndingFor(3, endings));
        Assert.Equal(1, RepeatPasses.EndingFor(4, endings));  // past every number: the last
        Assert.Equal(-1, RepeatPasses.EndingFor(1, System.Array.Empty<PassSet>()));
    }

    /// <summary>
    /// The passes written with the points the bracket prints — <c>[1.3. B]</c> for
    /// <c>[1,3. B]</c> — are ONE error naming both spellings (LYS0037), in the form and in the
    /// music alike. The lexer reads <c>1.3</c> as a decimal: until session 715 that drew five
    /// "Expected" errors an ending, and the pass reader then threw on the empty number the
    /// recovery left, so <c>lysc check</c> printed "The input string '' was not in a correct
    /// format." and no diagnostic at all (the owner wrote
    /// <c>form main { A |: [1.3. B] :| [2.4. C] :| }</c>).
    /// </summary>
    [Theory]
    [InlineData("|: A [1.3. B] :| [2.4. C]", 2)]
    [InlineData("|: A [1.3. B] :| [2. C]", 1)]
    public void PassesWrittenWithPoints_AreOneErrorAnEnding(string form, int endings)
    {
        var tree = SyntaxTree.Parse(Book(form));
        var all = tree.Diagnostics.Concat(SemanticValidation.Run(tree)).ToList();
        Assert.Equal(endings, all.Count);
        Assert.All(all, d => Assert.Equal(DiagnosticCodes.VoltaPassesWithPoints, d.Code));
        Assert.Contains("'[1,3.'", all[0].Message);
        Assert.Contains("'[1-3.'", all[0].Message);
    }

    [Fact]
    public void PassesWrittenWithPoints_InTheMusic_AreTheSameError()
    {
        const string src = """
            octave absolute
            part m { section A { c'1 |: d'1 [1.3. e'1] :| [2. f'1] } }
            form main { A }
            score main { staff m }
            """;
        var tree = SyntaxTree.Parse(src);
        var parse = Assert.Single(tree.Diagnostics);
        Assert.Equal(DiagnosticCodes.VoltaPassesWithPoints, parse.Code);
        // The music's own complaint about an ending in the music still stands beside it.
        Assert.Contains(SemanticValidation.Run(tree), d => d.Code == DiagnosticCodes.RepeatStructureOutsideForm);
    }

    /// <summary>
    /// A list runs past two numbers — <c>[1,3,5. B]</c>, LilyPond's <c>\volta 1,3,5</c> — for
    /// every reader: the written text keeps its spelling (and the tree every character), the
    /// passes are 1, 3, 5, the twin writes <c>\volta 1,3,5</c>, MusicXML
    /// <c>number="1,3,5"</c>, and the inline spelling in the music reads the same. A range is
    /// still two numbers. Until session 716 <c>[1,3,5.</c> was eleven errors.
    /// </summary>
    [Fact]
    public void AListEnding_NamesEveryPassItLists()
    {
        string src = Book("|: A [1,3,5. B] :| [2,4. C]");
        var tree = SyntaxTree.Parse(src);
        Assert.Empty(tree.Diagnostics);
        Assert.Empty(SemanticValidation.Run(tree).Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Equal(src, tree.GetRoot().ToFullString());
        var ending = tree.GetRoot().DescendantNodesOfKinds(SyntaxKind.FormAlternative)
            .Cast<FormAlternativeSyntax>().First();
        Assert.Equal("1,3,5.", ending.VoltaText);
        Assert.Equal(new[] { 1, 3, 5 }, ending.Numbers.ToArray());
        Assert.Equal(new[] { "B" }, ending.Sections.Select(s => s.ToFullString().Trim()).ToArray());
        Assert.Contains("\\volta 1,3,5 ", Twin(src));
        Assert.Contains("number=\"1,3,5\"", Xml(src));

        // (The bare-braces harness is LYS0020's top-level music; the ending adds nothing.)
        var inline = SyntaxTree.Parse("{ |: c4 [1,3,5. d4] :| [2,4. e4] }");
        Assert.Equal(DiagnosticCodes.TopLevelMusic, Assert.Single(inline.Diagnostics).Code);
        Assert.Equal(new[] { 60, 62, 60, 64, 60, 62, 60, 64, 60, 62 },
            MidiNotes("{ |: c4 [1,3,5. d4] :| [2,4. e4] }").Select(n => n.Pitch).ToArray());

        // A range stays two numbers: a comma after it is the parser's error, as before.
        Assert.Contains(SyntaxTree.Parse(Book("|: A [1-3,5. B] :| [4. C]")).Diagnostics,
            d => d.Code == DiagnosticCodes.ExpectedToken);
    }

    /// <summary>
    /// An ending whose passes the parser could not read — <c>[. B]</c>, <c>[1-. B]</c>, a state
    /// the editor sees on the way to <c>[1. B]</c> — is the parser's one error: the validators
    /// and the preview run on it without throwing, and no pass error is guessed on top. And a gap
    /// INSIDE a list (<c>|: A [1,3. B] :|</c>, pass 2) is LYS1043 on that ending — it threw
    /// "Sequence contains no matching element" out of the validators until session 715.
    /// </summary>
    [Theory]
    [InlineData("|: A [. B] :| [2. C]")]
    [InlineData("|: A [1-. B] :| [2. C]")]
    public void AnUnreadableEnding_IsTheParsersErrorAlone(string form)
    {
        var tree = SyntaxTree.Parse(Book(form));
        Assert.Equal(DiagnosticCodes.ExpectedToken, Assert.Single(tree.Diagnostics).Code);
        Assert.Empty(SemanticValidation.Run(tree).Where(d => d.Severity == DiagnosticSeverity.Error));
        // Every reader of the passes runs on it: the page (the collector's highest pass), the
        // MusicXML (its own), the MIDI and the twin.
        Assert.NotEmpty(LilySharp.Core.Svg.SvgGenerator.Generate(tree,
            new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false }));
        Assert.NotEmpty(Xml(Book(form)));
        Assert.NotNull(MidiNotes(Book(form)));
        Assert.NotEmpty(Twin(Book(form)));
    }

    [Fact]
    public void AGapInsideAList_IsReportedOnThatEnding()
    {
        var gap = Assert.Single(Errors("|: A [1,3. B] :|", DiagnosticCodes.EndingPassNotNamedOnce));
        Assert.Contains("No ending plays pass 2", gap.Message);
    }
}
