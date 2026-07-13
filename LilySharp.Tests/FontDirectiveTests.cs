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
using Xunit;
using LilySharp.Core.Syntax;

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
public class FontDirectiveTests
{
    [Fact]
    public void FontDirective_WithEmbedded_ParsesNameAndEmbeddedFlag()
    {
        var tree = SyntaxTree.Parse("font \"meiryo\" embedded");

        var fonts = tree.GetRoot().DescendantNodes().OfType<FontDeclarationSyntax>().ToList();
        Assert.Single(fonts);
        Assert.Equal("meiryo", fonts[0].FontName);
        Assert.True(fonts[0].Embedded);
        Assert.False(tree.HasErrors);
    }

    [Fact]
    public void FontDirective_WithoutEmbedded_ParsesNameAndNoEmbeddedFlag()
    {
        var tree = SyntaxTree.Parse("font \"Noto Serif CJK JP\"");

        var fonts = tree.GetRoot().DescendantNodes().OfType<FontDeclarationSyntax>().ToList();
        Assert.Single(fonts);
        Assert.Equal("Noto Serif CJK JP", fonts[0].FontName);
        Assert.False(fonts[0].Embedded);
        Assert.False(tree.HasErrors);
    }

    [Fact]
    public void FontDirective_InFullDocumentHeader_ParsesClean()
    {
        var source =
            "font \"meiryo\" embedded\n" +
            "time 4/4\n" +
            "part m { clef treble section A { c4 d e f | } }\n" +
            "form main { A }\n" +
            "score main { staff m }";
        var tree = SyntaxTree.Parse(source);

        var fonts = tree.GetRoot().DescendantNodes().OfType<FontDeclarationSyntax>().ToList();
        Assert.Single(fonts);
        Assert.Equal("meiryo", fonts[0].FontName);
        Assert.True(fonts[0].Embedded);
        Assert.False(tree.HasErrors);
    }
}
