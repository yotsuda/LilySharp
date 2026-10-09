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

using System.IO;
using System.Linq;
using LilySharp.Core.Syntax;
using LilySharp.Lsp;
using LilySharp.Lsp.Protocol;
using Xunit;

namespace LilySharp.Tests.Lsp;

/// <summary>
/// The quick fix for LYS0038 (a score's name written as a quoted string, 2026-10-09,
/// docs/anonymous-blocks-design.md §7): the string becomes the bare word the error names, or —
/// beside a bare name — goes, and the book then parses clean.
/// </summary>
public class ScoreNameQuickFixTests
{
    private const string Head = "part melody { section A { c'4 d' e' f' } }\nform { A }\n";

    private static CodeAction? FixAt(string text, string caretAt)
    {
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        var uri = new System.Uri("file:///quoted.lys");
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            { Uri = uri, Text = text, LanguageId = "lilysharp", Version = 1 },
        });
        // Inside the squiggle, not on its first character: where a caret usually is.
        var (line, character) = LilySharpLanguageServer.GetLineAndCharacter(
            text, text.IndexOf(caretAt, System.StringComparison.Ordinal) + 2);
        var at = new Position(line, character);
        return (server.GetCodeActions(new CodeActionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Range = new LilySharp.Lsp.Protocol.Range { Start = at, End = at },
        }) ?? []).SingleOrDefault(a => a.Diagnostics?.Any(d => d.Code as string == DiagnosticCodes.ScoreNameQuoted) == true);
    }

    private static string Apply(string text, CodeAction action)
    {
        var edit = action.Edit!.Changes!.Values.Single().Single();
        int start = OffsetOf(text, edit.Range.Start);
        int end = OffsetOf(text, edit.Range.End);
        return text.Substring(0, start) + edit.NewText + text.Substring(end);
    }

    private static int OffsetOf(string text, Position p)
    {
        int offset = 0;
        for (int line = 0; line < p.Line; line++)
            offset = text.IndexOf('\n', offset) + 1;
        return offset + p.Character;
    }

    [Theory]
    [InlineData("score \"tab\" { staff melody }", "score tab { staff melody }")]
    [InlineData("score \"guitar-chart\" { staff melody }", "score guitarChart { staff melody }")]
    [InlineData("score tab \"both\" { staff melody }", "score tab { staff melody }")]
    [InlineData("score \"tab\" transpose d { staff melody }", "score tab transpose d { staff melody }")]
    public void TheQuotedName_BecomesTheBareWord_AndTheBookParses(string written, string fixedScore)
    {
        string text = Head + written + "\n";
        var fix = FixAt(text, "\"");
        Assert.NotNull(fix);

        string after = Apply(text, fix!);

        Assert.Equal(Head + fixedScore + "\n", after);
        Assert.Empty(SyntaxTree.Parse(after).Diagnostics);
    }
}
