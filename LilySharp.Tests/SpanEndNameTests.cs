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

using System;
using System.IO;
using System.Linq;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using LilySharp.Lsp;
using LilySharp.Lsp.Protocol;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A span is closed by the name it was opened with (owner's decision 2026-10-28, LYS4032):
/// <c>@8va … @!8va</c>, <c>@rit … @!rit</c>, <c>@ottava(bassa) … @!ottava</c>; a mismatch is an
/// error whose quick fix writes the start's name.
/// </summary>
[Trait("Category", "Unit")]
public class SpanEndNameTests
{
    private static string Book(string music)
        => $"time 4/4\npart p {{ clef treble }}\nsection A {{\n  p {{\n    {music}\n  }}\n}}\nform {{ A }}\nscore {{ staff p }}\n";

    private static LilySharp.Core.Syntax.Diagnostic[] Mismatches(string music)
        => [.. SemanticValidation.Run(SyntaxTree.Parse(Book(music))).Where(d => d.Code == DiagnosticCodes.SpanEndName)];

    [Theory]
    [InlineData("c''4@8va d'' e'' f''@!8va | g'1 |")]
    [InlineData("c''4@ottava d'' e'' f''@!ottava | g'1 |")]
    [InlineData("c4@ottava(bassa) d e f@!ottava | g'1 |")]
    [InlineData("c''4@quindicesima d'' e'' f''@!quindicesima | g'1 |")]
    [InlineData("c'4@rit d' e' f'@!rit | g'1 |")]
    [InlineData("c'4@textSpan(\"poco rit.\") d' e' f'@!textSpan | g'1 |")]
    [InlineData("c''4@8va d''@15ma e'' f''@!15ma | g'1 |")]   // a change: the end closes the last start
    public void TheOpeningName_ClosesIt(string music)
        => Assert.Empty(Mismatches(music));

    [Theory]
    [InlineData("c''4@8va d'' e'' f''@!15ma | g'1 |", "'@!15ma' closes the '@8va'", "8va")]
    [InlineData("c''4@quindicesima d'' e'' f''@!ottava | g'1 |", "'@!ottava' closes the '@quindicesima'", "quindicesima")]
    [InlineData("c'4@rit d' e' f'@!accel | g'1 |", "'@!accel' closes the '@rit'", "rit")]
    [InlineData("c''4@8va d''@15ma e'' f''@!8va | g'1 |", "'@!8va' closes the '@15ma'", "15ma")]
    public void AnotherName_IsAnError_AndTheQuickFixWritesTheOpeningOne(string music, string says, string fixedName)
    {
        var d = Assert.Single(Mismatches(music));
        Assert.Equal(LilySharp.Core.Syntax.DiagnosticSeverity.Error, d.Severity);
        Assert.Contains(says, d.Message);

        string text = Book(music);
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        var uri = new Uri("file:///span-end-name.lys");
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = uri, Text = text, LanguageId = "lilysharp", Version = 1 },
        });
        var (line, character) = LilySharpLanguageServer.GetLineAndCharacter(text, d.Span.Start + 1);
        var actions = server.GetCodeActions(new CodeActionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Range = new LilySharp.Lsp.Protocol.Range { Start = new Position(line, character), End = new Position(line, character) },
        }) ?? [];
        var fix = Assert.Single(actions, a => a.Title == $"Write '@!{fixedName}'");
        var edit = fix.Edit!.Changes!.Values.Single().Single();
        var lines = text.Split('\n');
        int start = lines.Take(edit.Range.Start.Line).Sum(l => l.Length + 1) + edit.Range.Start.Character;
        int end = lines.Take(edit.Range.End.Line).Sum(l => l.Length + 1) + edit.Range.End.Character;
        string after = text[..start] + edit.NewText + text[end..];
        Assert.DoesNotContain(SemanticValidation.Run(SyntaxTree.Parse(after)), x => x.Code == DiagnosticCodes.SpanEndName);
    }

    /// <summary>An unclosed span is told to write the end by its own name.</summary>
    [Theory]
    [InlineData("c''4@8va d'' e'' f'' | g'1 |", "'@!8va'")]
    [InlineData("c''4@quindicesima d'' e'' f'' | g'1 |", "'@!quindicesima'")]
    [InlineData("c'4@accel d' e' f' | g'1 |", "'@!accel'")]
    public void AnUnclosedSpan_IsToldItsOwnEnd(string music, string end)
    {
        var unclosed = SemanticValidation.Run(SyntaxTree.Parse(Book(music))).Single(d => d.Code == DiagnosticCodes.UnpairedSpan);
        Assert.Contains("write " + end, unclosed.Message);
    }

    /// <summary>The squiggle is under the whole mark, not its '@' alone (owner's request 2026-10-28).</summary>
    [Theory]
    [InlineData("c'4@accel d' e' f' | g'1 |", "@accel")]
    [InlineData("c'4@textSpan(\"poco rit.\") d' e' f' | g'1 |", "@textSpan(\"poco rit.\")")]
    [InlineData("c'4 d' e' f'@!rit | g'1 |", "@!rit")]
    public void AnUnpairedMark_IsUnderlinedWhole(string music, string underlined)
    {
        string text = Book(music);
        var d = SemanticValidation.Run(SyntaxTree.Parse(text)).Single(x => x.Code == DiagnosticCodes.UnpairedSpan);
        Assert.Equal(underlined, text.Substring(d.Span.Start, d.Span.Length));
    }

    /// <summary>The trill span is opened and closed by one name too, '@trillSpan' … '@!trillSpan'
    /// (owner's decision 2026-10-28; until then LilyPond's '@startTrillSpan' / '@stopTrillSpan').</summary>
    [Fact]
    public void TheTrillSpan_IsTrillSpanAndItsEnd()
    {
        var all = SemanticValidation.Run(SyntaxTree.Parse(Book("c''4@trillSpan d'' e'' f''@!trillSpan | g'1 |")));
        Assert.DoesNotContain(all, d => d.Code == DiagnosticCodes.UnknownAnnotation);

        foreach (string old in new[] { "startTrillSpan", "stopTrillSpan" })
        {
            var retired = Assert.Single(SemanticValidation.Run(SyntaxTree.Parse(Book($"c''4@{old} d'' e'' f'' | g'1 |"))),
                d => d.Code == DiagnosticCodes.UnknownAnnotation);
            Assert.Contains("'@trillSpan' … '@!trillSpan'", retired.Message);
        }

        string text = "c''4@trillSpan d'' e''@!";
        Assert.Equal("trillSpan", LilySharpLanguageServer.OpenSpanBefore(text, text.Length));
        var ends = LilySharpLanguageServer.GetSpanEndCompletions(text, text.Length).Items;
        Assert.True(Assert.Single(ends, i => i.Label == "trillSpan").Preselect);
    }

    [Fact]
    public void EachVoiceIsItsOwnLine()
    {
        // The second voice's end answers to its own start, not to the first voice's.
        Assert.Empty(Mismatches("voice { c''4@8va d'' e'' f''@!8va | } { c'4@15mb d' e' f'@!15mb | } g'1 |"));
    }
}
