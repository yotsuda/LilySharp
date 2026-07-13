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
using LilySharp.Core.Rendering;
using LilySharp.Lsp;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Completing an installed, embeddable font family inside a <c>font "…"</c> string.
/// Context detection and the item-building are covered without touching the real
/// system font set: detection is text-only, and the item shape is exercised through
/// the synthetic-list helper so the assertions are environment-independent.
/// </summary>
[Trait("Category", "Unit")]
public class FontNameCompletionTests
{
    private static LilySharpLanguageServer.CompletionContext Ctx(string text)
        => LilySharpLanguageServer.GetCompletionContext(text, text.Length);

    [Fact]
    public void CursorInsideFontString_IsAfterFontName()
    {
        // The caret sits just inside the opening quote of a `font "…"` directive.
        Assert.Equal(LilySharpLanguageServer.CompletionContext.AfterFontName,
            Ctx("font \""));
    }

    [Fact]
    public void PartwayThroughAFontName_KeepsTheList()
    {
        // A partial family name (spaces and all) still resolves to the font context.
        Assert.Equal(LilySharpLanguageServer.CompletionContext.AfterFontName,
            Ctx("font \"Noto Se"));
    }

    [Fact]
    public void AfterFontKeyword_NoQuotesYet_IsAfterFontKeyword()
    {
        // `font ` with the caret after it (no quotes typed): Ctrl+Space should complete
        // the whole quoted name.
        Assert.Equal(LilySharpLanguageServer.CompletionContext.AfterFontKeyword,
            Ctx("font "));
    }

    [Fact]
    public void AfterFontKeyword_InsertsEmptyQuotesAndRetriggers()
    {
        // At `font |` the single item inserts the empty pair with the caret between the
        // quotes and re-triggers, so the font-name list shows inside the string.
        var item = Assert.Single(LilySharpLanguageServer.GetFontQuoteInsertCompletion().Items);
        Assert.Equal("\"$0\"", item.InsertText);
        Assert.Equal("editor.action.triggerSuggest", item.Command?.CommandIdentifier);
    }

    [Fact]
    public void CursorInsideTitleString_StillAfterTitleText_NoRegression()
    {
        // The mirrored detection must not steal the title/composer string.
        Assert.Equal(LilySharpLanguageServer.CompletionContext.AfterTitleText,
            Ctx("title \""));
        Assert.Equal(LilySharpLanguageServer.CompletionContext.AfterTitleText,
            Ctx("composer \""));
    }

    [Fact]
    public void BuildHelper_ExcludesForbidden_KeepsEmbeddable_AnnotatesLicenseAndCjk()
    {
        var synthetic = new (string, FontEmbedInfo.FontEmbedClass, bool)[]
        {
            ("Noto Serif CJK JP", FontEmbedInfo.FontEmbedClass.Free, true),
            ("meiryo", FontEmbedInfo.FontEmbedClass.Gray, false),
            ("Some Font", FontEmbedInfo.FontEmbedClass.Forbidden, false),
        };

        var items = LilySharpLanguageServer.BuildFontNameCompletions(synthetic).Items;

        // The Forbidden family is never offered.
        Assert.DoesNotContain(items, i => i.Label == "Some Font");

        // The Free family is present, marked as an OFL/libre embeddable, and — since it
        // covers Japanese — its detail mentions CJK.
        var free = Assert.Single(items, i => i.Label == "Noto Serif CJK JP");
        Assert.Contains("OFL", free.Detail);
        Assert.Contains("CJK", free.Detail);

        // The Gray family is present and flagged as license-unverified (no CJK note).
        var gray = Assert.Single(items, i => i.Label == "meiryo");
        Assert.Contains("unverified", gray.Detail);
        Assert.DoesNotContain("CJK", gray.Detail);
    }

    [Fact]
    public void BuildHelper_AlsoExcludesNotFound()
    {
        var synthetic = new (string, FontEmbedInfo.FontEmbedClass, bool)[]
        {
            ("Ghost Font", FontEmbedInfo.FontEmbedClass.NotFound, false),
            ("Liberation Serif", FontEmbedInfo.FontEmbedClass.Free, false),
        };

        var labels = LilySharpLanguageServer.BuildFontNameCompletions(synthetic)
            .Items.Select(i => i.Label).ToArray();

        Assert.DoesNotContain("Ghost Font", labels);
        Assert.Contains("Liberation Serif", labels);
    }
}
