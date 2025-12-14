namespace LilySharp.Core.Svg.Renderer;

/// <summary>
/// Options for SVG rendering.
/// </summary>
public sealed class SvgRenderOptions
{
    /// <summary>
    /// If true, embed font data as Base64 in the SVG.
    /// If false, reference font by name using local().
    /// </summary>
    public bool EmbedFont { get; init; }
    
    /// <summary>
    /// If true, skip @font-face in SVG (for external font loading).
    /// </summary>
    public bool SkipFontFace { get; init; }
    
    /// <summary>
    /// Directory containing font files (used when EmbedFont is true).
    /// If not specified, searches in common locations.
    /// </summary>
    public string? FontDirectory { get; init; }
    
    /// <summary>
    /// Default options (system font reference, no embedding).
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
    /// Creates options for preview (no @font-face in SVG, font loaded externally).
    /// </summary>
    public static SvgRenderOptions Preview() => new() 
    { 
        EmbedFont = false, 
        SkipFontFace = true 
    };
}
