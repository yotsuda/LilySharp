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
using LilySharp.Core.Semantics;
using LilySharp.Lsp;
using LilySharp.Lsp.Protocol;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Completing a <c>paper { … }</c> body: every key the reader validates against, from the
/// reader's own table.
/// </summary>
/// <remarks>
/// ⚠️ Until 2026-09-10 the flag <c>raggedRight</c> was the ONE paper spelling the completion
/// listed by hand instead of reading from <see cref="LanguageVocabulary"/>, so
/// <c>raggedBottom</c> — added to <c>PaperPlanReader</c> that day — would have parsed,
/// validated and laid out while the popup never offered it: the failure mode
/// <see cref="FontBlockCompletionTests.TheKeyListIsTheReadersVocabulary_NotACopyOfIt"/>
/// guards the fonts block against, and this file guards the paper block against.
/// </remarks>
[Trait("Category", "Unit")]
public class PaperBlockCompletionTests
{
    private static LilySharpLanguageServer.CompletionContext Ctx(string text)
        => LilySharpLanguageServer.GetCompletionContext(text, text.Length);

    [Theory]
    [InlineData("paper {")]
    [InlineData("paper { ")]
    [InlineData("paper { rag")]
    [InlineData("paper { raggedRight ")]
    [InlineData("paper { paperWidth 210mm\n  ")]
    [InlineData("paper wide { ")]
    public void InsideTheBlock_OffersKeys(string text)
        => Assert.Equal(LilySharpLanguageServer.CompletionContext.PaperBlock, Ctx(text));

    [Fact]
    public void TheKeyListIsTheReadersVocabulary_NotACopyOfIt()
    {
        var labels = LilySharpLanguageServer.GetPaperBlockCompletions()
            .Items.Select(i => i.Label).ToArray();
        foreach (string key in PaperPlanReader.AllKeySpellings())
            Assert.Contains(key, labels);
    }

    [Fact]
    public void BothFlags_AreOffered_AsBareWords()
    {
        var items = LilySharpLanguageServer.GetPaperBlockCompletions().Items;
        foreach (string flag in new[] { "raggedRight", "raggedBottom" })
        {
            var item = Assert.Single(items, i => i.Label == flag);
            Assert.Equal(CompletionItemKind.Keyword, item.Kind);
            // A flag takes nothing, so it inserts nothing but itself — no `$0` value slot.
            Assert.Null(item.InsertText);
            Assert.False(string.IsNullOrEmpty(item.Detail), flag + " has no help line");
        }
        Assert.Equal(LanguageVocabulary.PaperFlagKeys.Count,
            items.Count(i => i.Kind == CompletionItemKind.Keyword));
    }
}
