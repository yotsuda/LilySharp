using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using SkiaSharp;
using Svg.Skia;
using Svg.Skia.TypefaceProviders;

namespace LilySharp.Core.Png;

/// <summary>
/// Generates PNG images from syntax trees via SVG intermediate rendering.
/// Uses Svg.Skia (SkiaSharp) for SVG-to-raster conversion with custom
/// Emmentaler music font registration.
/// </summary>
public static class PngGenerator
{
    /// <summary>
    /// Generates a PNG image from a syntax tree.
    /// </summary>
    public static byte[] Generate(SyntaxTree tree, PngRenderOptions? options = null, string? renderName = null)
    {
        options ??= PngRenderOptions.Default;

        var fontDir = options.FontDirectory ?? FindFontDirectory();

        // Generate SVG with embedded fonts
        var svgOptions = new SvgRenderOptions
        {
            EmbedFont = true,
            FontDirectory = fontDir
        };
        var svgString = SvgGenerator.Generate(tree, svgOptions, renderName);

        return ConvertSvgToPng(svgString, options, fontDir);
    }

    /// <summary>
    /// Converts an SVG string to PNG bytes.
    /// </summary>
    public static byte[] ConvertSvgToPng(string svgString, PngRenderOptions? options = null, string? fontDirectory = null)
    {
        options ??= PngRenderOptions.Default;
        fontDirectory ??= FindFontDirectory();

        using var svg = new SKSvg();

        // Register Emmentaler fonts with Svg.Skia before rendering
        var providers = RegisterMusicFonts(svg, fontDirectory);

        try
        {
            svg.FromSvg(svgString);

            if (svg.Picture == null)
                throw new InvalidOperationException("Failed to parse SVG content");

            var bounds = svg.Picture.CullRect;
            int width = (int)Math.Ceiling(bounds.Width * options.Scale);
            int height = (int)Math.Ceiling(bounds.Height * options.Scale);

            if (width <= 0 || height <= 0)
                throw new InvalidOperationException($"Invalid SVG dimensions: {bounds.Width}x{bounds.Height}");

            using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.White);
            canvas.Scale(options.Scale);
            canvas.DrawPicture(svg.Picture);

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, options.Quality);

            return data.ToArray();
        }
        finally
        {
            foreach (var provider in providers)
                provider.Dispose();
        }
    }

    private static List<EmmentalerTypefaceProvider> RegisterMusicFonts(SKSvg svg, string? fontDirectory)
    {
        var providers = new List<EmmentalerTypefaceProvider>();

        if (fontDirectory == null)
            return providers;

        // Prefer OTF (uncompressed, SkiaSharp-compatible) over WOFF2/WOFF
        // SkiaSharp 2.88.x cannot load WOFF2/WOFF formats; OTF works reliably
        RegisterFont(svg, fontDirectory, "emmentaler-20.otf", "Emmentaler", providers);
        if (providers.Count == 0)
            RegisterFont(svg, fontDirectory, "emmentaler-20.woff2", "Emmentaler", providers);

        RegisterFont(svg, fontDirectory, "emmentaler-brace.otf", "Emmentaler-Brace", providers);
        if (providers.Count < 2)
            RegisterFont(svg, fontDirectory, "emmentaler-brace.woff", "Emmentaler-Brace", providers);

        return providers;
    }

    private static void RegisterFont(SKSvg svg, string fontDir, string fileName, string familyName,
        List<EmmentalerTypefaceProvider> providers)
    {
        var fontPath = Path.Combine(fontDir, fileName);
        if (!File.Exists(fontPath))
            return;

        // Load font data as bytes and create typeface via SKData
        var fontBytes = File.ReadAllBytes(fontPath);
        var skData = SKData.CreateCopy(fontBytes);
        var typeface = SKTypeface.FromData(skData);

        if (typeface == null)
        {
            skData.Dispose();
            return; // Font format not supported on this platform
        }

        var provider = new EmmentalerTypefaceProvider(familyName, typeface);

        svg.Settings.TypefaceProviders ??= new List<ITypefaceProvider>();
        svg.Settings.TypefaceProviders.Insert(0, provider);

        providers.Add(provider);
    }

    private static string? FindFontDirectory()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "fonts"),
            Path.Combine(AppContext.BaseDirectory, "Fonts"),
            Path.Combine(AppContext.BaseDirectory, "..", "fonts"),
            "fonts",
            "../fonts"
        };

        foreach (var candidate in candidates)
        {
            if (Directory.Exists(candidate) &&
                (File.Exists(Path.Combine(candidate, "emmentaler-20.otf")) ||
                 File.Exists(Path.Combine(candidate, "emmentaler-20.woff2"))))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }
}

/// <summary>
/// Custom ITypefaceProvider that maps a font family name to a pre-loaded SKTypeface.
/// Used to register Emmentaler music fonts with Svg.Skia's rendering pipeline.
/// </summary>
internal sealed class EmmentalerTypefaceProvider : ITypefaceProvider, IDisposable
{
    private readonly string _familyName;
    private readonly SKTypeface _typeface;

    public EmmentalerTypefaceProvider(string familyName, SKTypeface typeface)
    {
        _familyName = familyName;
        _typeface = typeface;
    }

    public SKTypeface? FromFamilyName(string fontFamily, SKFontStyleWeight fontWeight,
        SKFontStyleWidth fontWidth, SKFontStyleSlant fontStyle)
    {
        // Svg.Skia may pass the entire CSS font-family value as a single string,
        // e.g. '"Emmentaler", serif' or "'Emmentaler', serif".
        // Parse comma-separated entries and match each against our family name.
        var families = fontFamily.Split(',', StringSplitOptions.TrimEntries);
        foreach (var raw in families)
        {
            // Strip surrounding quotes (single or double)
            var name = raw.Trim('\'', '"');

            if (string.Equals(name, _familyName, StringComparison.OrdinalIgnoreCase))
                return _typeface;

            // Also match the actual font family name (e.g. "Emmentaler-20" for "Emmentaler")
            if (_typeface.FamilyName != null &&
                string.Equals(name, _typeface.FamilyName, StringComparison.OrdinalIgnoreCase))
                return _typeface;
        }

        return null;
    }

    public void Dispose()
    {
        _typeface.Dispose();
    }
}
