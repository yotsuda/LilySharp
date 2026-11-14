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
/// LYS1070 — two parts writing different repeat signs on one bar line — offers the three ways
/// out as quick fixes (owner's request 2026-11-14: the other part's bar line may stand many
/// lines away): the other part's sign here, this sign there, or a plain <c>|</c> here. Each
/// leaves a book that no longer reports it.
/// </summary>
public class RepeatSignQuickFixTests
{
    private const string Book =
        "octave absolute\ntime 4/4\n"
        + "part m { section S { c''1 |: d''1 | e''1 | } }\n"
        + "part b { section S { c1 :|*3 d1 | e1 | } }\n"
        + "form { S }\nscore { staff m staff b }\n";

    private static CodeAction[] Fixes(string text)
    {
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        var uri = new System.Uri("file:///signs.lys");
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            { Uri = uri, Text = text, LanguageId = "lilysharp", Version = 1 },
        });
        var (line, character) = LilySharpLanguageServer.GetLineAndCharacter(
            text, text.IndexOf(":|*3", System.StringComparison.Ordinal) + 1);
        var at = new Position(line, character);
        return [.. (server.GetCodeActions(new CodeActionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Range = new LilySharp.Lsp.Protocol.Range { Start = at, End = at },
        }) ?? []).Where(a => a.Diagnostics?.Any(d => d.Code as string == DiagnosticCodes.RepeatSignsDisagree) == true)];
    }

    private static string Apply(string text, CodeAction action)
    {
        var edit = action.Edit!.Changes!.Values.Single().Single();
        return text.Substring(0, OffsetOf(text, edit.Range.Start)) + edit.NewText + text.Substring(OffsetOf(text, edit.Range.End));
    }

    private static int OffsetOf(string text, Position p)
    {
        int offset = 0;
        for (int line = 0; line < p.Line; line++)
            offset = text.IndexOf('\n', offset) + 1;
        return offset + p.Character;
    }

    [Theory]
    [InlineData(0, "Write '|:' here, as part 'm' does", "c''1 |: d''1 | e''1 |", "c1 |: d1 | e1 |")]
    [InlineData(1, "Write ':|*3' in part 'm' too", "c''1 :|*3 d''1 | e''1 |", "c1 :|*3 d1 | e1 |")]
    [InlineData(2, "Write a plain '|' here - it follows the '|:' of part 'm'", "c''1 |: d''1 | e''1 |", "c1 | d1 | e1 |")]
    public void EachFixWritesWhatItSays_AndTheSignsAgree(int which, string title, string m, string b)
    {
        var fixes = Fixes(Book);
        Assert.Equal(3, fixes.Length);
        Assert.Equal(title, fixes[which].Title);

        string after = Apply(Book, fixes[which]);

        Assert.Contains("part m { section S { " + m + " } }", after);
        Assert.Contains("part b { section S { " + b + " } }", after);
        Assert.DoesNotContain(LilySharp.Core.Semantics.SemanticValidation.Run(SyntaxTree.Parse(after)),
            d => d.Code == DiagnosticCodes.RepeatSignsDisagree);
    }
}