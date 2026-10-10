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
using Xunit;
using CompletionContext = LilySharp.Lsp.Protocol.CompletionContext;
using CompletionParams = LilySharp.Lsp.Protocol.CompletionParams;
using DidOpenTextDocumentParams = LilySharp.Lsp.Protocol.DidOpenTextDocumentParams;
using Position = LilySharp.Lsp.Protocol.Position;
using TextDocumentIdentifier = LilySharp.Lsp.Protocol.TextDocumentIdentifier;
using TextDocumentItem = LilySharp.Lsp.Protocol.TextDocumentItem;

namespace LilySharp.Tests.Lsp;

/// <summary>
/// The <c>.up</c> / <c>.down</c> placement after a value annotation's argument —
/// <c>@todo("memo").down</c>, <c>@text("dolce").up</c>, <c>@diagram(…).down</c> — is offered
/// the way <c>@fermata.down</c> is: right after the ')', after its '.', and on a partial
/// word. Owner's request 2026-10-11: the <c>.down</c> of <c>@todo("comment").down</c> had to
/// be typed by hand, because the placement context only knew articulation names.
/// </summary>
[Trait("Category", "Unit")]
public class ValueAnnotationPlacementCompletionTests
{
    private static string Ctx(string text) =>
        LilySharpLanguageServer.GetCompletionContext(text, text.Length).ToString();

    [Theory]
    [InlineData("c4@todo(\"comment\")", "todo")]        // right after the ')'
    [InlineData("c4@todo(\"comment\").", "todo")]       // after the dot
    [InlineData("c4@todo(\"comment\").d", "todo")]      // partial 'down'
    [InlineData("c4@todo(\"comment\").down", "todo")]   // the whole word (replaced, as for @fermata.down)
    [InlineData("c4@todo(o1203 \"smudged\")", "todo")]  // a key and a memo
    [InlineData("c4@todo(\"see (b) @text(x)\").", "todo")] // ')' and '@' inside the memo
    [InlineData("c4@todo(\"say \\\"hi)\\\"\").", "todo")]  // an escaped quote, then ')'
    [InlineData("c4@text(\"dolce\").", "text")]
    [InlineData("c4@diagram(x32010).", "diagram")]
    public void AfterTheArgument_OffersPlacement(string text, string name)
    {
        Assert.Equal("AfterValueAnnotationPlacement", Ctx(text));
        Assert.Equal(name, LilySharpLanguageServer.ValueAnnotationBeforePlacement(text, text.Length));
    }

    [Theory]
    [InlineData("c4@todo(\"comment\") ")]   // trailing space → the user moved on
    [InlineData("c4@todo(\"comment\")d")]   // a word glued without a dot
    [InlineData("c4@mark(\"A\").")]         // a fixed-side value annotation
    [InlineData("c4@finger(3)")]
    [InlineData("c4@todo( d e)")]           // a slur that starts on the note, not an argument
    [InlineData("c4( d e)")]                // a plain slur
    [InlineData("c4@todo(\"memo")]          // still inside the argument
    public void ElsewhereNot(string text)
    {
        Assert.NotEqual("AfterValueAnnotationPlacement", Ctx(text));
        Assert.Null(LilySharpLanguageServer.ValueAnnotationBeforePlacement(text, text.Length));
    }

    [Fact]
    public void Todo_OffersDownFirst_WithTheDotWhenNoneIsTyped()
    {
        const string text = "c4@todo(\"comment\")";
        var items = LilySharpLanguageServer
            .GetArticulationPlacementCompletions(text, text.Length, null, "todo").Items;
        Assert.Equal([".down", ".up"], items.OrderBy(i => i.SortText, System.StringComparer.Ordinal)
            .Select(i => i.Label));
        Assert.Contains("BELOW", items.Single(i => i.Label == ".down").Detail);
    }

    [Fact]
    public void Text_OffersUpFirst_BareAfterTheDot()
    {
        const string text = "c4@text(\"dolce\").";
        var items = LilySharpLanguageServer
            .GetArticulationPlacementCompletions(text, text.Length, null, "text").Items;
        Assert.Equal(["up", "down"], items.OrderBy(i => i.SortText, System.StringComparer.Ordinal)
            .Select(i => i.Label));
    }

    /// <summary>What each offered item writes is what the parser takes as the placement.
    /// The control is a fixed-side annotation, where the same '.down' is an error — so
    /// "no error" is the parser taking it, not the parser not looking.</summary>
    [Theory]
    [InlineData("todo(\"comment\")", true)]
    [InlineData("text(\"dolce\")", true)]
    [InlineData("diagram(x32010)", true)]
    [InlineData("mark(\"A\")", false)]
    public void TheCompletedPlacementParses(string annotation, bool takesPlacement)
    {
        foreach (var word in new[] { "up", "down" })
        {
            var tree = MusicSource.Parse($"c4@{annotation}.{word} d4");
            bool clean = !tree.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
            Assert.True(clean == takesPlacement, $"@{annotation}.{word}: errors = {!clean}");
        }
    }

    // ---- through the server: the '.' trigger answers only the placement ----

    private static string[]? LabelsAt(string text, string? trigger)
    {
        int offset = text.IndexOf('▮');
        text = text.Remove(offset, 1);
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        var uri = new System.Uri("file:///todo.lys");
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            { Uri = uri, Text = text, LanguageId = "lilysharp", Version = 1 },
        });
        var (line, character) = LilySharpLanguageServer.GetLineAndCharacter(text, offset);
        var list = server.Completion(new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Position = new Position(line, character),
            Context = trigger == null
                ? new CompletionContext { TriggerKind = 1 }
                : new CompletionContext { TriggerKind = 2, TriggerCharacter = trigger },
        });
        return list?.Items.Select(i => i.Label).ToArray();
    }

    [Fact]
    public void TheDotTrigger_OpensThePlacementAfterATodo()
    {
        var labels = LabelsAt("part p { c'4@todo(\"comment\").▮ d'4 }", ".");
        Assert.NotNull(labels);
        Assert.Equal("down", labels![0]);
        Assert.Contains("up", labels);
    }

    [Fact]
    public void TheDotTrigger_OpensThePlacementAfterAnArticulation()
    {
        var labels = LabelsAt("part p { c'4@fermata.▮ d'4 }", ".");
        Assert.NotNull(labels);
        Assert.Contains("down", labels!);
    }

    /// <summary>A dotted note value is the common '.', and no list may pop up there; the
    /// same position asked for explicitly (Ctrl+Space) still answers, so it is the trigger
    /// gate that keeps it quiet.</summary>
    [Fact]
    public void TheDotTrigger_AnswersNothingAfterADottedNote()
    {
        const string text = "part p { c'4.▮ d'8 }";
        Assert.Null(LabelsAt(text, "."));
        Assert.NotNull(LabelsAt(text, null));
    }
}
