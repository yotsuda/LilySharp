namespace LilySharp.Core.Svg.Renderer;

/// <summary>
/// Options for SVG rendering.
/// </summary>
public sealed class SvgRenderOptions
{
    /// <summary>
    /// If true, embed font data as Base64 in the SVG.
    /// </summary>
    public bool EmbedFont { get; init; }
    
    /// <summary>
    /// If true, omit @font-face from SVG (font defined externally in HTML).
    /// </summary>
    public bool OmitFontFace { get; init; }
    
    /// <summary>
    /// Directory containing font files (used when EmbedFont is true).
    /// </summary>
    public string? FontDirectory { get; init; }
    
    /// <summary>
    /// Default options (reference font by name, requires font installed on system).
    /// </summary>
    public static SvgRenderOptions Default => new();
    
    /// <summary>
    /// Export mode: embed font as Base64 for standalone SVG.
    /// </summary>
    public static SvgRenderOptions Export(string? fontDirectory = null) => new() 
    { 
        EmbedFont = true, 
        FontDirectory = fontDirectory 
    };
    
    /// <summary>
    /// Preview mode: omit @font-face (font defined externally in HTML).
    /// </summary>
    public static SvgRenderOptions Preview() => new() 
    { 
        OmitFontFace = true
    };
}
