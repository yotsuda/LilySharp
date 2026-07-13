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

using LilySharp.Core.Rendering.Pdf;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The PDF font resolver routes families to bundled/embedded faces. These cases are
/// environment-independent (they use a font name no system has, so it never installs
/// or embeds — exercising the fallback-to-bundled-serif routing).
/// </summary>
[Trait("Category", "Unit")]
public class EmmentalerFontResolverTests
{
    private const string BogusFont = "ZzNoSuchFont1234567890";

    [Fact]
    public void Serif_ResolvesToBundledLiberation()
    {
        var r = new EmmentalerFontResolver();
        Assert.Equal("LiberationSerif#", r.ResolveTypeface("serif", false, false)?.FaceName);
        Assert.Equal("LiberationSerif-Bold#", r.ResolveTypeface("serif", true, false)?.FaceName);
    }

    [Fact]
    public void Emmentaler_ResolvesToMusicFace()
        => Assert.Equal("Emmentaler#", new EmmentalerFontResolver().ResolveTypeface("Emmentaler", false, false)?.FaceName);

    [Fact]
    public void ConfiguredFont_NotEmbedded_ResolvesToBundledSerif()
    {
        // font "X" (no `embedded`) must NOT embed a system font — it maps to the
        // bundled serif so nothing proprietary is embedded without asking.
        var r = new EmmentalerFontResolver();
        r.SetTextFont(BogusFont, embed: false);
        Assert.Equal("LiberationSerif#", r.ResolveTypeface(BogusFont, false, false)?.FaceName);
    }

    [Fact]
    public void ConfiguredFont_EmbedButNotInstalled_FallsBackToSerif()
    {
        // `embedded` on a font this machine doesn't have: nothing to embed, so it
        // still resolves to the bundled serif (never "LysEmbed#").
        var r = new EmmentalerFontResolver();
        r.SetTextFont(BogusFont, embed: true);
        Assert.Equal("LiberationSerif#", r.ResolveTypeface(BogusFont, false, false)?.FaceName);
    }
}
