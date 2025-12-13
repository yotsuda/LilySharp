namespace LilySharp.Core.Svg.Renderer;

/// <summary>
/// Options for SVG rendering.
/// </summary>
public sealed class SvgRenderOptions
{
    /// <summary>
    /// If true, embed font data as Base64 in the SVG.
    /// If false, reference font files by URL.
    /// </summary>
    public bool EmbedFont { get; init; }
    
    /// <summary>
    /// Full path to the font file (used when EmbedFont is false).
    /// This should be an absolute path or URI that the viewer can access.
    /// </summary>
    public string FontPath { get; init; } = "emmentaler-20.woff2";
    
    /// <summary>
    /// Directory containing font files (used when EmbedFont is true).
    /// If not specified, searches in common locations.
    /// </summary>
    public string? FontDirectory { get; init; }
    
    /// <summary>
    /// Default options (relative font path, no embedding).
    /// </summary>
    public static SvgRenderOptions Default => new();
    
    /// <summary>
    /// Options for export with embedded font.
    /// </summary>
    public static SvgRenderOptions Export(string? fontDirectory = null) => new() 
    { 
        EmbedFont = true, 
        FontDirectory = fontDirectory 
    };
    
    /// <summary>
    /// Creates options for preview with a specific font path.
    /// </summary>
    public static SvgRenderOptions Preview(string fontPath) => new() 
    { 
        EmbedFont = false, 
        FontPath = fontPath 
    };
}