using LilySharp.Core.Pdf;
using LilySharp.Core.Pdf.Renderer;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

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
    public void PdfRenderer_SingleStaff_ViaRenderer()
    {
        var source = "c4 d e f";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);
        var layoutEngine = new LayoutEngine();
        var layout = layoutEngine.Layout(score);

        var renderer = new PdfRenderer();
        var bytes = renderer.Render(score, layout);

        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);
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
