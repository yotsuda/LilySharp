using LilySharp.Core.Syntax;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Renderer;

namespace LilySharp.Core.Svg;

/// <summary>
/// Unified SVG generation from syntax tree.
/// Used by both CLI and VS Code preview to ensure identical rendering.
/// </summary>
public static class SvgGenerator
{
    /// <summary>
    /// Generates SVG from a syntax tree.
    /// </summary>
    /// <param name="tree">The parsed syntax tree</param>
    /// <param name="options">Render options (font embedding, etc.)</param>
    /// <param name="renderName">Optional render name to select specific render</param>
    /// <returns>SVG string</returns>
    public static string Generate(SyntaxTree tree, SvgRenderOptions? options = null, string? renderName = null)
    {
        options ??= SvgRenderOptions.Default;
        var renderer = new SvgRenderer(renderOptions: options);
        
        // Find render specification - by name if specified, otherwise first
        var renderSpec = string.IsNullOrEmpty(renderName) 
            ? RenderSpecParser.FindFirst(tree)
            : RenderSpecParser.FindByName(tree, renderName);
        
        if (renderSpec != null && renderSpec.IsMultiStaff)
        {
            // Multi-staff rendering (grandStaff, etc.)
            var collector = new MeasureCollector();
            var multiScore = collector.CollectMultiStaff(tree, renderSpec);
            
            var layoutEngine = new LayoutEngine();
            var layout = layoutEngine.Layout(multiScore);
            
            return renderer.Render(multiScore, layout);
        }
        else
        {
            // Single staff - get voiceName from renderSpec if available
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
