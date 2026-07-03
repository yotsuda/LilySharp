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
using LilySharp.Lsp;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Completion after <c>key</c> offers tonic pitches, and after
/// <c>key TONIC</c> only the modes — not lyrics/tempo/every keyword.
/// </summary>
[Trait("Category", "Unit")]
public class KeyCompletionTests
{
    private static LilySharpLanguageServer.CompletionContext ContextOf(string text)
        => LilySharpLanguageServer.GetCompletionContext(text, text.Length);

    [Theory]
    [InlineData("key ")]                    // top level
    [InlineData("part m { key ")]           // part header
    [InlineData("section A { m { key ")]    // mid-music
    public void AfterKey_OffersTonics(string text)
    {
        Assert.Equal(LilySharpLanguageServer.CompletionContext.AfterKey, ContextOf(text));
    }

    [Theory]
    [InlineData("key a ")]
    [InlineData("key a m")]                 // partial mode word
    [InlineData("key fis ")]                // accidental tonic
    [InlineData("part m { key bes d")]
    public void AfterKeyTonic_OffersModes(string text)
    {
        Assert.Equal(LilySharpLanguageServer.CompletionContext.AfterKeyTonic, ContextOf(text));
    }

    [Theory]
    [InlineData("tempo ")]                  // unrelated keyword
    [InlineData("key a major ")]            // key already complete
    public void ElsewhereIsNotAKeyContext(string text)
    {
        var ctx = ContextOf(text);
        Assert.NotEqual(LilySharpLanguageServer.CompletionContext.AfterKey, ctx);
        Assert.NotEqual(LilySharpLanguageServer.CompletionContext.AfterKeyTonic, ctx);
    }

    [Fact]
    public void ModeCompletions_AreExactlyTheNineModes()
    {
        var labels = LilySharpLanguageServer.GetKeyModeCompletions().Items
            .Select(i => i.Label).ToArray();
        Assert.Equal(
            new[] { "major", "minor", "ionian", "dorian", "phrygian", "lydian",
                    "mixolydian", "aeolian", "locrian" },
            labels);
    }

    [Fact]
    public void TopLevelCompletions_CarryTheScoreTemplates()
    {
        // The score templates moved from static VS Code snippets (which the
        // editor merged into EVERY completion popup, even after `key a`) into
        // the LSP's top-level items, where the context dispatch scopes them.
        var items = LilySharpLanguageServer.GetTopLevelCompletions().Items;
        Assert.Contains(items, i => i.Label == "newscore");
        Assert.Contains(items, i => i.Label == "grandstaff");
        var modeItems = LilySharpLanguageServer.GetKeyModeCompletions().Items;
        Assert.DoesNotContain(modeItems, i => i.Label == "newscore" || i.Label == "grandstaff");
    }

    [Fact]
    public void TonicCompletions_CoverTheCircleOfFifths()
    {
        var labels = LilySharpLanguageServer.GetKeyTonicCompletions().Items
            .Select(i => i.Label).ToArray();
        Assert.Equal(15, labels.Length);
        Assert.Contains("c", labels);
        Assert.Contains("fis", labels);
        Assert.Contains("ces", labels);
    }
}
