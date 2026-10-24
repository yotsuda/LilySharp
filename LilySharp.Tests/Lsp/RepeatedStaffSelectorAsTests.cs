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
/// LYS0042 (owner's decision 2026-10-24): a second <c>as</c> in a staff row —
/// <c>staff melody as pitch transposed as lines 1</c> — is ONE error on that word, the selectors
/// after it still read, and the quick fix deletes it.
/// </summary>
[Trait("Category", "Unit")]
public class RepeatedStaffSelectorAsTests
{
    private const string Head = "part melody { instrument alto-sax }\nsection A { melody { c'4 d' e' f' | } }\nform { A }\n";

    [Theory]
    [InlineData("staff melody as pitch transposed as lines 1")]
    [InlineData("staff melody as lines 1 as removeEmpty true")]
    [InlineData("staff melody ossia melody as lines 1 as removeEmpty all")]
    public void ASecondAs_IsOneError_OnThatWord(string rows)
    {
        string text = Head + "score { " + rows + " }\n";
        var errors = SyntaxTree.Parse(text).Diagnostics.Where(d => d.Severity == LilySharp.Core.Syntax.DiagnosticSeverity.Error).ToList();
        var only = Assert.Single(errors);
        Assert.Equal(DiagnosticCodes.RepeatedStaffSelectorAs, only.Code);
        int second = text.IndexOf(" as ", text.IndexOf(" as ", System.StringComparison.Ordinal) + 1, System.StringComparison.Ordinal) + 1;
        Assert.Equal(second, only.Span.Start);
    }

    [Fact]
    public void OneAs_StaysClean()
    {
        string text = Head + "score { staff melody as pitch transposed lines 1 }\n";
        Assert.DoesNotContain(SyntaxTree.Parse(text).Diagnostics, d => d.Severity == LilySharp.Core.Syntax.DiagnosticSeverity.Error);
    }

    [Fact]
    public void TheQuickFix_DeletesTheExtraAs()
    {
        string text = Head + "score { staff melody as pitch transposed as lines 1 }\n";
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        var uri = new System.Uri("file:///as.lys");
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = uri, Text = text, LanguageId = "lilysharp", Version = 1 },
        });
        int second = text.LastIndexOf(" as ", System.StringComparison.Ordinal) + 2;
        var (line, character) = LilySharpLanguageServer.GetLineAndCharacter(text, second);
        var at = new Position(line, character);
        var fix = (server.GetCodeActions(new CodeActionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Range = new LilySharp.Lsp.Protocol.Range { Start = at, End = at },
        }) ?? []).Single(a => a.Diagnostics?.Any(d => d.Code as string == DiagnosticCodes.RepeatedStaffSelectorAs) == true);
        var edit = fix.Edit!.Changes!.Values.Single().Single();
        int start = OffsetOf(text, edit.Range.Start), end = OffsetOf(text, edit.Range.End);
        string after = text[..start] + edit.NewText + text[end..];
        Assert.Equal(Head + "score { staff melody as pitch transposed lines 1 }\n", after);
        Assert.DoesNotContain(SyntaxTree.Parse(after).Diagnostics, d => d.Severity == LilySharp.Core.Syntax.DiagnosticSeverity.Error);
    }

    private static int OffsetOf(string text, Position p)
    {
        int offset = 0;
        for (int line = 0; line < p.Line; line++)
            offset = text.IndexOf('\n', offset) + 1;
        return offset + p.Character;
    }
}
