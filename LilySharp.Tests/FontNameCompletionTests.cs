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
/// Completing an installed, embeddable font family — inside a <c>fonts { … }</c> binding,
/// which is the only place a face name belongs. Context detection and the item-building
/// are covered without touching the real system font set: detection is text-only, and the
/// item shape is exercised through the synthetic-list helper so the assertions are
/// environment-independent.
/// </summary>
[Trait("Category", "Unit")]
public class FontNameCompletionTests
{
    private static LilySharpLanguageServer.CompletionContext Ctx(string text)
        => LilySharpLanguageServer.GetCompletionContext(text, text.Length);

    [Theory]
    [InlineData("fonts \"")]
    [InlineData("fonts \"Noto Se")]
    [InlineData("font \"Noto Se")]
    public void ABareStringAfterTheKeyword_OffersNoFace(string text)
    {
        // A face name belongs inside a BINDING, and a bare string after `fonts` is not one
        // — it is the missing-block error (LYS8008). Completing a face into it would help
        // the writer finish a spelling the parser refuses: the editor would be leading them
        // into the error it is about to underline.
        //
        // ⚠️ The third case is `font`, which is not a keyword at all any more — so it is
        // just a word, and the popup must not treat it as the directive it never was.
        Assert.NotEqual(LilySharpLanguageServer.CompletionContext.AfterFontName, Ctx(text));
    }

    [Fact]
    public void AfterTheKeyword_NoBlockYet_IsAfterFontKeyword()
    {
        // `fonts ` with the caret after it: Ctrl+Space offers the block forms.
        Assert.Equal(LilySharpLanguageServer.CompletionContext.AfterFontKeyword,
            Ctx("fonts "));
        // …and the singular is not the keyword, so it gets no directive context of its own.
        Assert.NotEqual(LilySharpLanguageServer.CompletionContext.AfterFontKeyword,
            Ctx("font "));
    }

    [Fact]
    public void AfterTheKeyword_OffersOnlyBlocks()
    {
        // The declaration takes a block and nothing else, so every item inserts one. An
        // item that inserted a bare quote would complete straight to a diagnostic.
        var items = LilySharpLanguageServer.GetFontDeclarationCompletions().Items;

        Assert.DoesNotContain("\"…\"", items.Select(i => i.Label));
        Assert.All(items, i => Assert.StartsWith("{", i.InsertText!, StringComparison.Ordinal));
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

    [Fact]
    public void BuildHelper_ExcludesInstalledFamiliesTheBundleShadows()
    {
        // ⚠️ THE RED ONLY SOME MACHINES CAN SEE, pinned with a synthetic list so EVERY
        // machine can see it. A box that also INSTALLS TeX Gyre (this repo's WSL gets it as
        // a LilyPond build dependency; GitHub's ubuntu-latest and this Windows do not, so
        // both gates stayed green) enumerated the bundled names a second time from the
        // system, and the system row carried the classification and the sort — the popup
        // recommended "the machine's TeX Gyre Heros" while the engine, which consults the
        // bundle before the machine for these names, would never use it. Same name, so the
        // writer cannot tell. The shadowed rows are dropped; the bundled entries
        // (BundledFaceCompletions, sort "!…") are the one spelling of these names offered.
        var synthetic = new (string, FontEmbedInfo.FontEmbedClass, bool)[]
        {
            (TextFontMetrics.SerifFamily, FontEmbedInfo.FontEmbedClass.Free, false),
            (TextFontMetrics.SansFamily, FontEmbedInfo.FontEmbedClass.Gray, false),
            ("Liberation Serif", FontEmbedInfo.FontEmbedClass.Free, false),
        };

        var labels = LilySharpLanguageServer.BuildFontNameCompletions(synthetic)
            .Items.Select(i => i.Label).ToArray();

        Assert.DoesNotContain(TextFontMetrics.SerifFamily, labels);
        Assert.DoesNotContain(TextFontMetrics.SansFamily, labels);
        Assert.Contains("Liberation Serif", labels);
    }
}
