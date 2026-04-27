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

using LilySharp.Core.Pdf;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

[Trait("Category", "Integration")]
public class PdfRendererTests
{
    [Fact]
    public void PdfGenerator_SimpleNotes_ProducesValidPdf()
    {
        var source = "c4 d e f";
        var tree = SyntaxTree.Parse(source);
        var bytes = PdfGenerator.Generate(tree);

        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);
        // PDF files start with %PDF-
        Assert.Equal((byte)'%', bytes[0]);
        Assert.Equal((byte)'P', bytes[1]);
        Assert.Equal((byte)'D', bytes[2]);
        Assert.Equal((byte)'F', bytes[3]);
    }

    [Fact]
    public void PdfGenerator_EighthNotes_WithBeams()
    {
        var source = "c8 d e f g a b c'";
        var tree = SyntaxTree.Parse(source);
        var bytes = PdfGenerator.Generate(tree);

        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 100);
    }

    [Fact]
    public void PdfGenerator_Chords()
    {
        var source = "<c e g>4 <d f a> <e g b> <f a c'>";
        var tree = SyntaxTree.Parse(source);
        var bytes = PdfGenerator.Generate(tree);

        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 100);
    }

    [Fact]
    public void PdfGenerator_WithRests()
    {
        var source = "c4 r d r";
        var tree = SyntaxTree.Parse(source);
        var bytes = PdfGenerator.Generate(tree);

        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 100);
    }

    [Fact]
    public void PdfGenerator_DottedNotes()
    {
        var source = "c4. d8 e4. f8";
        var tree = SyntaxTree.Parse(source);
        var bytes = PdfGenerator.Generate(tree);

        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 100);
    }

    [Fact]
    public void PdfGenerator_MultipleMeasures()
    {
        var source = "c4 d e f | g a b c'";
        var tree = SyntaxTree.Parse(source);
        var bytes = PdfGenerator.Generate(tree);

        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 100);
    }

    [Fact]
    public void PdfGenerator_LetterSize()
    {
        var source = "c4 d e f";
        var tree = SyntaxTree.Parse(source);
        var bytes = PdfGenerator.Generate(tree, PdfRenderOptions.Letter);

        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 100);
    }

    [Fact]
    public void PdfGenerator_CustomOptions()
    {
        var source = "c4 d e f";
        var tree = SyntaxTree.Parse(source);
        var options = new PdfRenderOptions
        {
            StaffSpacePt = 8.0,
            PageWidthPt = 612,
            PageHeightPt = 792
        };
        var bytes = PdfGenerator.Generate(tree, options);

        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 100);
    }

    [Fact]
    public void PdfGenerator_LedgerLines()
    {
        // Notes above and below the staff
        var source = "c''4 d'' c, d,";
        var tree = SyntaxTree.Parse(source);
        var bytes = PdfGenerator.Generate(tree);

        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 100);
    }

    [Fact]
    public void PdfRenderOptions_DefaultIsA4()
    {
        var options = PdfRenderOptions.Default;
        Assert.Equal(595.28, options.PageWidthPt);
        Assert.Equal(841.89, options.PageHeightPt);
        Assert.Equal(6.0, options.StaffSpacePt);
        Assert.True(options.EmbedFont);
    }

    [Fact]
    public void PdfRenderOptions_Letter()
    {
        var options = PdfRenderOptions.Letter;
        Assert.Equal(612, options.PageWidthPt);
        Assert.Equal(792, options.PageHeightPt);
    }
}
