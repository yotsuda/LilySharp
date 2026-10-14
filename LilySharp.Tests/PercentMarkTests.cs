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
using LilySharp.Core.Editing;
using LilySharp.Core.Midi;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using LilySharp.Lsp;
using LilySharp.Lsp.Protocol;
using Xunit;
using Diagnostic = LilySharp.Core.Syntax.Diagnostic;
using DiagnosticSeverity = LilySharp.Core.Syntax.DiagnosticSeverity;

namespace LilySharp.Tests;

/// <summary>
/// The percent repeat written where it is drawn (docs/percent-repeat-design.md, owner's
/// decision 2026-10-14): the bars once, then <c>%</c> / <c>%N</c> — one repetition of the N
/// bars before it — alone between bar lines. The parser groups a run of marks with its bars;
/// a mark it cannot group is LYS1048, bars that are not whole are LYS1049, and the old
/// <c>repeat percent</c> / <c>repeat unfold</c> is LYS1047 with <c>lysc percent</c> to rewrite it.
/// </summary>
[Trait("Category", "Unit")]
public sealed class PercentMarkTests
{
    private static string Book(string music, string time = "4/4") => $$"""
        time {{time}}
        part m { clef treble }
        section A { m { {{music}} } }
        form { A }
        score { staff m }
        """;

    private static IReadOnlyList<Diagnostic> Diagnose(string source) =>
        SemanticValidation.Run(SyntaxTree.Parse(source)).ToList();

    private static List<string> Codes(string source) =>
        Diagnose(source).Select(d => d.Code).ToList();

    // --- the tree ---

    [Fact]
    public void ARunOfMarks_IsGroupedWithTheBarsItRepeats()
    {
        string src = Book("c4 d e f | g1 | a2 b | c'2 d' | %2 | %2 |");
        var tree = SyntaxTree.Parse(src);
        Assert.Equal(src, tree.GetRoot().ToFullString());
        var group = Assert.Single(tree.GetRoot().DescendantNodes().OfType<PercentRepeatSyntax>());
        Assert.Equal(2, group.Bars);
        Assert.Equal(3, group.Count);
        Assert.Equal(2, group.Marks.Count());
        // The bars repeated are the two before the first mark, their bar line between them
        // included; the bars before them stay outside.
        Assert.Equal("a2 b | c'2 d'", string.Join(" ", group.BodyItems.Select(i => i.ToFullString().Trim())));
    }

    [Fact]
    public void TheDirectivesAtTheBarsHead_AndALineBreakBesideAMark_StayInPlace()
    {
        var tree = SyntaxTree.Parse(Book("c1 | break key g major d4 e f g | break % | % break |"));
        var group = Assert.Single(tree.GetRoot().DescendantNodes().OfType<PercentRepeatSyntax>());
        Assert.Equal(3, group.Count);
        Assert.IsType<NoteSyntax>(group.BodyItems.First());
        Assert.Empty(Codes(Book("c1 | break key g major d4 e f g | break % | % break |"))
            .Where(c => c is DiagnosticCodes.PercentMarkUngrouped or DiagnosticCodes.PercentBodyNotWholeBars));
    }

    // --- what cannot be grouped (LYS1048) ---

    [Theory]
    [InlineData("c1 | c4 % |", "only thing in its bar")]        // not alone
    [InlineData("c1 | %2 |", "only 1 bar")]                     // too few bars
    [InlineData("% | c1 |", "only 0 bars")]                     // nothing before it
    [InlineData("c1 | % | %2 |", "a repetition")]               // after marks of another length
    [InlineData("c1 | %0 |", "no number of bars")]
    public void AMarkThatRepeatsNothing_IsReportedAndSaysWhy(string music, string why)
    {
        var d = Assert.Single(Diagnose(Book(music)), d => d.Code == DiagnosticCodes.PercentMarkUngrouped);
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
        Assert.Contains(why, d.Message);
    }

    // --- the bars must be whole (LYS1049) ---

    [Theory]
    [InlineData("c2 | % |")]
    [InlineData("c1 | d2. | %2 |")]
    public void BarsThatAreNotWholeBars_AreReported(string music)
    {
        var codes = Codes(Book(music));
        Assert.Contains(DiagnosticCodes.PercentBodyNotWholeBars, codes);
        // Reported once, in place of the bar's own fill check.
        Assert.DoesNotContain(DiagnosticCodes.MeasureIncomplete, codes);
    }

    [Fact]
    public void WholeBars_AreClean_AndAMarkCountsItsBarsAcrossParts()
    {
        string src = """
            time 4/4
            section Main {
              rh { c4 c c c | d8 d d d d d d d | % | e1 | f1 | %2 | }
              lh { c1 | c1 | c1 | c1 | c1 | c1 | c1 | }
            }
            form { Main }
            """;
        var validator = new MeasureValidator();
        validator.Validate(SyntaxTree.Parse(src));
        Assert.Empty(validator.Diagnostics);
    }

    // --- the old spelling (LYS1047) ---

    [Theory]
    [InlineData("repeat percent 2 { c4 d e f } |")]
    [InlineData("repeat unfold 2 { c4 d e f } |")]
    public void TheOldSpelling_IsAnError(string music)
    {
        var d = Assert.Single(Diagnose(Book(music)), d => d.Code == DiagnosticCodes.RepeatPercentRemoved);
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
        Assert.Equal("repeat " + music.Split(' ')[1] + " 2", Book(music).Substring(d.Span.Start, d.Span.Length));
    }

    // --- the playback: a repetition sounds the bars again ---

    [Fact]
    public void ARepetition_SoundsExactlyTheBarsWrittenOut()
    {
        static List<(int Pitch, int Start, int Length)> Notes(string music) =>
            new MidiExporter().Export(SyntaxTree.Parse(Book(music))).Tracks
                .SelectMany(t => t.Notes).Select(n => (Pitch: n.Pitch, Start: n.StartTick, Length: n.DurationTicks))
                .OrderBy(n => n.Start).ToList();

        Assert.Equal(
            Notes("octave absolute a2 b | c'2 d' | a2 b | c'2 d' |"),
            Notes("octave absolute a2 b | c'2 d' | %2 |"));

        // A relative body that climbs: each repetition is the written copy, not one an
        // octave up (the page draws one copy), and starts where the bars' last note ends.
        var climb = Notes("g''8 a b c d e f g | % | % |");
        Assert.Equal(24, climb.Count);
        Assert.Equal(climb.Take(8).Select(n => n.Pitch), climb.Skip(8).Take(8).Select(n => n.Pitch));
        Assert.Equal(climb.Take(8).Select(n => n.Pitch), climb.Skip(16).Select(n => n.Pitch));
        Assert.Equal(climb[7].Start + climb[7].Length, climb[8].Start);
    }

    // --- the converter (lysc percent) ---

    [Theory]
    [InlineData("repeat percent 4 { c4 d e f } break", "c4 d e f | % | % | % break")]
    [InlineData("repeat percent 2 { a2 b | c' d' | }", "a2 b | c' d' | %2")]
    [InlineData("repeat percent 3 { g1 | g1 | break } |", "g1 | g1 | break %2 | %2 |")]
    [InlineData("repeat percent 2 { c4 d e f } g1 |", "c4 d e f | % | g1 |")]
    // A body shorter than a bar, and an unfold: written out.
    [InlineData("repeat percent 2 { c8 d e f } |", "c8 d e f c8 d e f |")]
    [InlineData("repeat unfold 2 { e1 | }", "e1 | e1 |")]
    [InlineData("repeat unfold 2 { c1 } |", "c1 | c1 |")]
    // Music before it in its bar: the bars would not be the body's, so written out.
    [InlineData("r2 repeat percent 2 { c2 } |", "r2 c2 c2 |")]
    public void TheConverter_RewritesTheOldSpelling(string old, string now)
    {
        var result = PercentRepeatConverter.Convert(Book(old));
        Assert.Equal(Book(now), result.NewText);
    }

    [Fact]
    public void TheQuickFix_RewritesTheRepeatItStandsOn()
    {
        string src = Book("repeat percent 2 { c1 } | repeat percent 2 { d1 } |");
        int at = src.IndexOf("repeat percent 2 { d1 }", System.StringComparison.Ordinal) + 3;
        Assert.Equal(Book("repeat percent 2 { c1 } | d1 | % |"), PercentRepeatConverter.ConvertAt(src, at));
        Assert.Null(PercentRepeatConverter.ConvertAt(src, 0));
    }

    [Fact]
    public void TheEditorsQuickFix_RewritesTheItemAndTheBookIsClean()
    {
        string text = Book("repeat percent 3 { c4 d e f } |");
        var server = new LilySharpLanguageServer(System.IO.Stream.Null, System.IO.Stream.Null);
        var uri = new System.Uri("file:///percent.lys");
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = uri, Text = text, LanguageId = "lilysharp", Version = 1 },
        });
        int at = text.IndexOf("percent", System.StringComparison.Ordinal);
        var (line, character) = LilySharpLanguageServer.GetLineAndCharacter(text, at);
        var fix = Assert.Single(server.GetCodeActions(new CodeActionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Range = new LilySharp.Lsp.Protocol.Range { Start = new Position(line, character), End = new Position(line, character) },
        }) ?? [], a => a.Title.Contains("% marks", System.StringComparison.Ordinal));

        var edit = fix.Edit!.Changes!.Values.Single().Single();
        int start = Offset(text, edit.Range.Start), end = Offset(text, edit.Range.End);
        string after = text[..start] + edit.NewText + text[end..];
        Assert.Equal(Book("c4 d e f | % | % |"), after);
        Assert.Empty(Diagnose(after).Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    private static int Offset(string text, Position p)
    {
        int offset = 0;
        for (int l = 0; l < p.Line; l++)
            offset = text.IndexOf('\n', offset) + 1;
        return offset + p.Character;
    }
}
