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
using LilySharp.Core.Svg.Model;
using LilySharp.Lsp;
using Xunit;
using CompletionContext = LilySharp.Lsp.Protocol.CompletionContext;
using CompletionParams = LilySharp.Lsp.Protocol.CompletionParams;
using DidOpenTextDocumentParams = LilySharp.Lsp.Protocol.DidOpenTextDocumentParams;
using Position = LilySharp.Lsp.Protocol.Position;
using TextDocumentIdentifier = LilySharp.Lsp.Protocol.TextDocumentIdentifier;
using TextDocumentItem = LilySharp.Lsp.Protocol.TextDocumentItem;
using CompletionItem = LilySharp.Lsp.Protocol.CompletionItem;

namespace LilySharp.Tests.Lsp;

/// <summary>
/// After <c>@!</c> the names a span end takes (owner's report 2026-10-28: nothing completed
/// there — the position fell through to the note names). The list is the compiler's own
/// (<see cref="MusicMarkItem.ParseSpanEndName"/>), the span open before the caret comes first,
/// and '!' triggers the list only after an '@'.
/// </summary>
[Trait("Category", "Unit")]
public class SpanEndCompletionTests
{
    private const string Head = "part p { clef treble }\nsection A { p { ";
    private const string Tail = " f' | } }\nform { A }\nscore { staff p }\n";

    private static CompletionItem[]? ItemsAt(string music, string? trigger)
    {
        string text = Head + music + Tail;
        int offset = text.IndexOf('▮');
        text = text.Remove(offset, 1);
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        var uri = new System.Uri("file:///span-end.lys");
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
        })?.Items;
    }

    [Fact]
    public void TheBangTrigger_OffersEveryNameTheCompilerEnds_AndNothingElse()
    {
        var items = ItemsAt("c'4@rit d'4 e'4@!▮", "!");
        Assert.NotNull(items);
        Assert.All(items!, i => Assert.NotNull(MusicMarkItem.ParseSpanEndName(i.Label)));
        foreach (var name in new[] { "rit", "accel", "textSpan", "ottava", "8va", "15mb", "sustain", "unaCorda", "phrasingSlur", "cresc" })
            Assert.Contains(items!, i => i.Label == name);
        Assert.DoesNotContain(items!, i => i.Label is "staccato" or "c" or "fermata");
    }

    [Fact]
    public void TheOpenSpanComesFirst_Selected()
    {
        var items = ItemsAt("c'4@sustain d'4@8va e'4@!8va f'4@!▮", "!")!;
        var first = items.OrderBy(i => i.SortText, System.StringComparer.Ordinal).First();
        Assert.Equal("sustain", first.Label);                  // the 8va was closed
        Assert.True(first.Preselect);
        Assert.Single(items, i => i.Preselect == true);
    }

    [Theory]
    [InlineData("c'4@!ri▮", "rit")]
    [InlineData("c'4@!8▮", "8vb")]
    [InlineData("c'4@!sus▮", "sustain")]
    public void APartialName_KeepsTheList(string music, string expected)
    {
        var items = ItemsAt(music, null);
        Assert.NotNull(items);
        Assert.Contains(items!, i => i.Label == expected);
        Assert.Equal("AfterAtBang", LilySharpLanguageServer.GetCompletionContext(
            (Head + music + Tail).Replace("▮", ""), (Head + music).IndexOf('▮')).ToString());
    }

    /// <summary>Right after '@' the list has a '!' row (owner's request 2026-10-28) that writes the
    /// '!' and opens the span ends; once a name is being typed it is gone.</summary>
    [Fact]
    public void RightAfterTheAt_TheListOffersTheBang()
    {
        var bang = Assert.Single(ItemsAt("c'4@rit d'4@▮", "@")!, i => i.Label == "!");
        Assert.Equal("!", bang.InsertText);
        Assert.Equal("editor.action.triggerSuggest", bang.Command?.CommandIdentifier);
        Assert.DoesNotContain(ItemsAt("c'4@st▮", null)!, i => i.Label == "!");
    }

    /// <summary>The '@' list is in plain alphabetical order, as VS Code sorts it (the sort texts
    /// lower-cased), '!' first (owner's decision 2026-10-28: the writer looks for a name, not a
    /// family); the '@!' list keeps the open span first and the rest alphabetical.</summary>
    [Fact]
    public void TheAtList_IsAlphabetical_AndTheSpanEndsAfterTheOpenOne()
    {
        static string[] Shown(CompletionItem[] items) => [.. items
            .OrderBy(i => (i.SortText ?? i.Label).ToLowerInvariant(), System.StringComparer.Ordinal)
            .Select(i => i.Label)];
        var at = Shown(ItemsAt("c'4@▮", "@")!);
        Assert.Equal("!", at[0]);
        Assert.Equal(at.Skip(1).OrderBy(l => l.ToLowerInvariant(), System.StringComparer.Ordinal), at.Skip(1));
        var ends = Shown(ItemsAt("c'4@sustain d'4@!▮", "!")!);
        Assert.Equal("sustain", ends[0]);
        Assert.Equal(ends.Skip(1).OrderBy(l => l.ToLowerInvariant(), System.StringComparer.Ordinal), ends.Skip(1));
    }

    /// <summary>A '!' that is not after an '@' opens nothing — the trigger is gated as '.' is.</summary>
    [Fact]
    public void ABangElsewhere_AnswersNothing()
    {
        Assert.Null(ItemsAt("c'4 d'4 \"Hi!▮\"", "!"));
    }
}
