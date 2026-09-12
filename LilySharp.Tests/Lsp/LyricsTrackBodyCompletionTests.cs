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

using System.Linq;
using System.IO;
using LilySharp.Lsp.Protocol;
using LilySharp.Lsp;
using Xunit;

namespace LilySharp.Tests.Lsp;

/// <summary>
/// A lyrics track is the SAME track whichever way its header is spelled —
/// <c>lyrics w { }</c> or the bound form <c>lyrics w sings melody { }</c> (GRAMMAR
/// LyricsBlock) — so its body completes the same way: <c>section NAME { … }</c> cells at
/// the track level, the verse header inside a cell, and NEVER the music vocabulary.
/// </summary>
/// <remarks>
/// ⚠️ The bound spelling was unknown to two of the readers until 2026-09-12. A block frame
/// carries the TWO words before its <c>{</c>, so <c>lyrics w sings melody {</c> reads as
/// (Prefix=sings, Name=melody) and the <c>lyrics</c> keyword is out of reach;
/// <c>IsLyricsFrame</c> knew this and the two context predicates did not, so a bound
/// track's body fell through to the music completions — MEASURED at 98 items, opening
/// <c>c d e f g a b</c>, at every caret in the track. Same shape as the chords track's own
/// body (<see cref="ChordsTrackBodyCompletionTests"/>): one construct, several readers.
/// The two spellings are asserted SIDE BY SIDE here on purpose — that is what makes a
/// reader that learns only one of them fail.
/// </remarks>
[Trait("Category", "Unit")]
public class LyricsTrackBodyCompletionTests
{
    private static string[] CompletionLabelsAt(string text, int offset)
    {
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        var uri = new System.Uri("file:///lyrics.lys");
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
        });
        return list?.Items.Select(i => i.Label!).ToArray() ?? System.Array.Empty<string>();
    }

    private static (string Text, int Offset) At(string marked)
    {
        int i = marked.IndexOf('▮');
        return (marked.Remove(i, 1), i);
    }

    /// <summary>The music items that must never reach a lyrics track's body.</summary>
    private static void AssertNoMusic(string[] labels)
    {
        foreach (var pitch in new[] { "c", "d", "e", "f", "g", "a", "b" })
            Assert.DoesNotContain(pitch, labels);
        Assert.DoesNotContain("break", labels);
    }

    [Theory]
    [InlineData("lyrics w { ")]                    // plain track
    [InlineData("lyrics { ")]                      // nameless track
    [InlineData("lyrics w sings melody { ")]       // bound track — the spelling that regressed
    public void EverySpellingOfTheHeader_OpensTheSameTrackContext(string doc)
        => Assert.Equal(LilySharpLanguageServer.CompletionContext.LyricsBlock,
            LilySharpLanguageServer.GetCompletionContext(doc, doc.Length));

    [Theory]
    [InlineData("lyrics words")]
    [InlineData("lyrics words sings melody")]
    public void TrackBody_OffersItsSections_NeverMusic(string header)
    {
        var (text, offset) = At($$"""
            part melody { section A { c'4 d' e' f' | } section B { g'4 a' b' c'' | } }
            {{header}} {
              section A { Do re mi fa | }
              ▮
            }
            form main { A B }
            score main { staff melody  lyrics words }
            """);
        var labels = CompletionLabelsAt(text, offset);

        Assert.Contains("section B", labels);       // played, not yet written here
        Assert.DoesNotContain("section A", labels); // already written in this track
        AssertNoMusic(labels);
    }

    [Theory]
    [InlineData("lyrics words")]
    [InlineData("lyrics words sings melody")]
    public void AfterTheSectionKeyword_OffersTheSectionNames_NeverMusic(string header)
    {
        var (text, offset) = At($$"""
            part melody { section A { c'4 d' e' f' | } section B { g'4 a' b' c'' | } }
            {{header}} {
              section A { Do re mi fa | }
              section ▮
            }
            form main { A B }
            score main { staff melody  lyrics words }
            """);
        var labels = CompletionLabelsAt(text, offset);

        Assert.Contains("B", labels);
        Assert.DoesNotContain("A", labels);
        AssertNoMusic(labels);
    }

    [Theory]
    [InlineData("lyrics words")]
    [InlineData("lyrics words sings melody")]
    public void InsideACell_TheVerseHeaderIsWhatIsOffered(string header)
    {
        // Control: the CELL holds syllables — typed, not completed — and the one construct
        // worth offering is the verse header. Not music there either.
        var (text, offset) = At($$"""
            part melody { section A { c'4 d' e' f' | } }
            {{header}} {
              section A { Do re ▮ }
            }
            """);
        var labels = CompletionLabelsAt(text, offset);

        Assert.Contains("[1. ]", labels);
        AssertNoMusic(labels);
    }

    [Fact]
    public void ABoundTracksBody_DoesNotOfferTheRepeatKinds()
    {
        // Control on the third reader of the same spelling: `repeat` inside a lyrics body is
        // the English word, not the directive — the guard has always known the `sings`
        // clause and must keep knowing it now that it asks IsLyricsFrame.
        var (text, offset) = At("lyrics words sings melody { section A { repeat ▮ } }");
        var labels = CompletionLabelsAt(text, offset);

        Assert.DoesNotContain("unfold", labels);
        Assert.DoesNotContain("percent", labels);
    }
}
