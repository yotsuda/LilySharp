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
using Microsoft.VisualStudio.LanguageServer.Protocol;
using LilySharp.Lsp;
using Xunit;

namespace LilySharp.Tests.Lsp;

/// <summary>
/// textDocument/definition must resolve every kind of symbol reference to its
/// declaration — not just the legacy <c>name = …</c> variable the handler
/// originally covered. A bare name in a music block is a phrase reference; inside
/// a <c>score { … }</c> the names are the form and the staff/tab/ossia parts; a
/// <c>form { … }</c> body plays sections. Each must jump to where the name is
/// bound. The reference/declaration model mirrors SymbolReferenceValidator so
/// go-to-definition and the undefined-symbol diagnostics agree.
/// </summary>
public class GoToDefinitionTests
{
    // A document exercising all four namespaces: a phrase, a part (header +
    // section-body block + score reference), a section (declared + form reference),
    // and a form (declared + score reference).
    private const string Source =
        "part melody\n" +          // part header — the canonical `melody` definition
        "phrase intro {\n" +       // phrase declaration — the `intro` definition
        "  c4 d e f\n" +
        "}\n" +
        "section Main {\n" +       // section declaration — the `Main` definition
        "  melody { intro }\n" +   // part block (defines melody's music) + phrase ref
        "}\n" +
        "form main { Main }\n" +   // form declaration `main` + section ref `Main`
        "score main \"out\" {\n" + // score references form `main`
        "  staff melody\n" +       // score references part `melody`
        "}\n";

    private static Location? DefinitionAt(string text, int offset)
    {
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        var uri = new System.Uri("file:///def.lys");
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            { Uri = uri, Text = text, LanguageId = "lilysharp", Version = 1 },
        });
        var (line, character) = LilySharpLanguageServer.GetLineAndCharacter(text, offset);
        return server.Definition(new TextDocumentPositionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Position = new Position(line, character),
        });
    }

    // Asserts that go-to-definition from the caret at `referenceOffset` lands on the
    // identifier that starts at `expectedDeclOffset` (its bare-name span).
    private static void AssertJumps(string text, int referenceOffset, int expectedDeclOffset, int nameLength)
    {
        var loc = DefinitionAt(text, referenceOffset);
        Assert.NotNull(loc);
        var (line, character) = LilySharpLanguageServer.GetLineAndCharacter(text, expectedDeclOffset);
        Assert.Equal(line, loc!.Range.Start.Line);
        Assert.Equal(character, loc.Range.Start.Character);
        var (endLine, endChar) = LilySharpLanguageServer.GetLineAndCharacter(text, expectedDeclOffset + nameLength);
        Assert.Equal(endLine, loc.Range.End.Line);
        Assert.Equal(endChar, loc.Range.End.Character);
    }

    [Fact]
    public void PhraseReferenceInSection_JumpsToPhraseDeclaration()
    {
        int declName = Source.IndexOf("intro");                 // phrase intro
        int reference = Source.IndexOf("intro", declName + 1);  // melody { intro }
        AssertJumps(Source, reference, declName, "intro".Length);
    }

    [Fact]
    public void FormNameInScore_JumpsToFormDeclaration()
    {
        int declName = Source.IndexOf("main");                 // form main
        int reference = Source.IndexOf("main", declName + 1);  // score main
        AssertJumps(Source, reference, declName, "main".Length);
    }

    [Fact]
    public void PartNameInScore_JumpsToPartDeclaration()
    {
        int declName = Source.IndexOf("melody");        // part melody (header)
        int reference = Source.LastIndexOf("melody");   // staff melody
        AssertJumps(Source, reference, declName, "melody".Length);
    }

    [Fact]
    public void SectionNameInForm_JumpsToSectionDeclaration()
    {
        int declName = Source.IndexOf("Main");                 // section Main
        int reference = Source.IndexOf("Main", declName + 1);  // form main { Main }
        AssertJumps(Source, reference, declName, "Main".Length);
    }

    [Fact]
    public void UndefinedReference_ResolvesToNull()
    {
        // `staff nope` names no part — no declaration to jump to, and it must not
        // fall through to some other namespace's symbol of a similar name.
        var text = Source.Replace("staff melody", "staff nope");
        int reference = text.IndexOf("nope");
        Assert.Null(DefinitionAt(text, reference));
    }

    [Fact]
    public void CaretNotOnASymbol_ResolvesToNull()
    {
        int inNote = Source.IndexOf("c4");   // a pitch, not a symbol reference
        Assert.Null(DefinitionAt(Source, inNote));
    }
}
