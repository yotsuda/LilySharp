using LilySharp.Core.Pdf.Renderer;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Pdf;

/// <summary>
/// Unified PDF generation from syntax tree.
/// Mirrors SvgGenerator but outputs PDF bytes.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/cairo.cc - Cairo-based PDF backend
/// </remarks>
public static class PdfGenerator
{
    /// <summary>
    /// Generates PDF from a syntax tree.
    /// </summary>
    /// <param name="tree">The parsed syntax tree</param>
    /// <param name="options">PDF render options (page size, staff scale, etc.)</param>
    /// <param name="renderName">Optional render name to select specific render</param>
    /// <returns>PDF document as byte array</returns>
    public static byte[] Generate(SyntaxTree tree, PdfRenderOptions? options = null, string? renderName = null)
    {
        options ??= PdfRenderOptions.Default;
        var renderer = new PdfRenderer(options: options);

        // Find render specification - by name if specified, otherwise first
        var renderSpec = string.IsNullOrEmpty(renderName)
            ? RenderSpecParser.FindFirst(tree)
            : RenderSpecParser.FindByName(tree, renderName);

        if (renderSpec != null && renderSpec.IsMultiStaff)
        {
            var collector = new MeasureCollector();
            var multiScore = collector.CollectMultiStaff(tree, renderSpec);

            var layoutEngine = new LayoutEngine();
            var layout = layoutEngine.Layout(multiScore);

            return renderer.Render(multiScore, layout);
        }
        else
        {
            string? voiceName = null;
            if (renderSpec != null && renderSpec.Items.Length == 1 &&
                renderSpec.Items[0] is SingleStaffSpec single)
            {
                voiceName = single.Staff.VoiceName;
            }

            var collector = new MeasureCollector();
            var score = collector.Collect(tree, voiceName);

            var layoutEngine = new LayoutEngine();
            var layout = layoutEngine.Layout(score);

            return renderer.Render(score, layout);
        }
    }
}
