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
/// LYS1054 — a member of a <c>&lt;…&gt;</c> chord that is not a tone of the <c>@chord</c> written
/// on it, as information (owner's decisions 2026-10-27).
/// </summary>
[Trait("Category", "Unit")]
public class ChordToneValidatorTests
{
    private static string Score(string music)
        => $"time 4/4\noctave absolute\npart p1 {{ clef treble }}\nsection A {{\n  p1 {{\n    {music}\n  }}\n}}\nform {{ A }}\nscore {{ staff p1 }}\n";

    private static LilySharp.Core.Syntax.Diagnostic[] Notes(string text)
        => [.. SemanticValidation.Run(SyntaxTree.Parse(text)).Where(d => d.Code == DiagnosticCodes.ChordToneOutsideSymbol)];

    [Fact]
    public void AMemberOutsideTheName_IsInformation_AtThatMember_NamingWhatTheNotesSpell()
    {
        string text = Score("<c' f' g'>1@chord(C) |");
        var d = Assert.Single(Notes(text));
        Assert.Equal(LilySharp.Core.Syntax.DiagnosticSeverity.Info, d.Severity);
        Assert.Equal("f'", text.Substring(d.Span.Start, d.Span.Length));
        Assert.Contains("F is not a tone of C", d.Message);
        Assert.Equal("Csus4", ChordTones.ProposedName(d.Message));
    }

    [Theory]
    [InlineData("<c' e' e'>1@chord(C) |")]            // the owner's example: E doubled
    [InlineData("<c' g'>1@chord(C) |")]               // a third left out: a voicing, not a slip
    [InlineData("<g e' c''>1@chord(C/G) |")]          // the slash bass is a tone
    [InlineData("e'1@chord(C) |")]                    // a melody note's chord: not checked
    [InlineData("<c' f' g'>1@chord(\"N.C.\") |")]     // free text
    [InlineData("<c' e' g'>1@chord |")]               // named from its notes
    public void NothingIsSaid_WhereTheNameAndTheNotesAgree_OrThereIsNoNameToCheck(string music)
    {
        Assert.Empty(Notes(Score(music)));
    }

    [Fact]
    public void EachMemberOutside_IsNamed()
    {
        string text = Score("<c' d' e' g' a'>1@chord(C) |");
        Assert.Equal(["d'", "a'"], Notes(text).Select(d => text.Substring(d.Span.Start, d.Span.Length)));
    }

    [Fact]
    public void TheQuickFix_RenamesTheSymbol_AndTheNoteIsGone()
    {
        string text = Score("<c' f' g'>1@chord(C) |");
        var d = Assert.Single(Notes(text));
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        var uri = new Uri("file:///chord-tones.lys");
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
        var fix = Assert.Single(actions, a => a.Title == "Name the chord Csus4");
        var edit = fix.Edit!.Changes!.Values.Single().Single();
        int start = DocumentManager.GetOffset(text, edit.Range.Start), end = DocumentManager.GetOffset(text, edit.Range.End);
        string fixedText = text[..start] + edit.NewText + text[end..];
        Assert.Contains("@chord(Csus4)", fixedText);
        Assert.Empty(Notes(fixedText));
    }
}
