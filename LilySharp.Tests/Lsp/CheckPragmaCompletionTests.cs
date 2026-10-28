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
using LilySharp.Lsp;
using Xunit;
using CompletionContext = LilySharp.Lsp.Protocol.CompletionContext;
using CompletionParams = LilySharp.Lsp.Protocol.CompletionParams;
using DidOpenTextDocumentParams = LilySharp.Lsp.Protocol.DidOpenTextDocumentParams;
using Position = LilySharp.Lsp.Protocol.Position;
using TextDocumentIdentifier = LilySharp.Lsp.Protocol.TextDocumentIdentifier;
using TextDocumentItem = LilySharp.Lsp.Protocol.TextDocumentItem;

namespace LilySharp.Tests.Lsp;

/// <summary>
/// The <c>check</c> line in the editor (Semantics.CheckPragma, 2026-10-28): the keyword at the top
/// level, the checks after it, the parts after the check — the ones not named yet — and its hover.
/// </summary>
[Trait("Category", "Unit")]
public class CheckPragmaCompletionTests
{
    private const string Parts = "part s { instrument soprano }\npart a { instrument alto }\npart t { instrument tenor }\npart b { instrument voice-bass }\n";

    private static string[]? LabelsAt(string marked, string? trigger = " ")
    {
        int offset = marked.IndexOf('▮');
        string text = marked.Remove(offset, 1);
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        var uri = new System.Uri("file:///check.lys");
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = uri, Text = text, LanguageId = "lilysharp", Version = 1 },
        });
        var (line, character) = LilySharpLanguageServer.GetLineAndCharacter(text, offset);
        return server.Completion(new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Position = new Position(line, character),
            Context = trigger == null
                ? new CompletionContext { TriggerKind = 1 }
                : new CompletionContext { TriggerKind = 2, TriggerCharacter = trigger },
        })?.Items.Select(i => i.Label).ToArray();
    }

    [Fact]
    public void TheTopLevel_OffersCheck()
        => Assert.Contains("check", LabelsAt(Parts + "▮", null)!);

    [Fact]
    public void AfterCheck_TheChecks()
        => Assert.Equal(["voiceLeading"], LabelsAt(Parts + "check ▮"));

    [Fact]
    public void AfterTheCheck_TheParts_NotNamedYet()
    {
        Assert.Equal(["s", "a", "t", "b"], LabelsAt(Parts + "check voiceLeading ▮"));
        Assert.Equal(["t", "b"], LabelsAt(Parts + "check voiceLeading s a ▮"));
        // The name being typed stays offered.
        Assert.Contains("t", LabelsAt(Parts + "check voiceLeading s a t▮", null)!);
    }

    [Fact]
    public void OffTheTopLevel_CheckIsNothing()
    {
        // In a part's music `check` is a phrase reference or nothing; no checks are offered there.
        var labels = LabelsAt(Parts + "section A { s { c'4 check ▮ } }");
        Assert.DoesNotContain("voiceLeading", labels ?? []);
    }

    [Fact]
    public void TheHover_SaysWhatTheCheckDoes()
    {
        var tree = LilySharp.Core.Syntax.SyntaxTree.Parse(Parts + "check voiceLeading s a t b\n");
        var line = Assert.Single(LilySharp.Core.Semantics.CheckPragma.Lines(tree));
        string? hover = LanguageReference.Hover(line);
        Assert.NotNull(hover);
        Assert.Contains("parallel fifths", hover);
        Assert.Contains("changes nothing that is drawn", hover);
    }
}
