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

using LilySharp.Core.Rendering;
using LilySharp.Core.Rendering.Png;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using SkiaSharp;

namespace LilySharp.Core.Png;

/// <summary>
/// Generates PNG images from syntax trees using <see cref="SharedRenderer"/>
/// driving a <see cref="PngDocumentContext"/> backed by SkiaSharp.
/// </summary>
/// <remarks>
/// This is the direct path: <c>SyntaxTree → ScoreLayout → SKCanvas → PNG</c>,
/// with no SVG intermediate. The Emmentaler music font is loaded directly
/// from the .otf file via <see cref="SKTypeface.FromFile"/>, sidestepping
/// the WOFF/WOFF2 limitations that affect SVG-based rasterization.
///
/// The legacy <see cref="ConvertSvgToPng"/> entry point (SVG string → PNG)
/// is preserved as a public utility for callers that already have an SVG
/// in hand.
/// </remarks>
public static class PngGenerator
{
    /// <summary>
    /// Generates a PNG image from a syntax tree.
    /// </summary>
    public static byte[] Generate(SyntaxTree tree, PngRenderOptions? options = null, string? renderName = null)
    {
        options ??= PngRenderOptions.Default;

        // Find render specification - by name if specified, otherwise first
        var renderSpec = string.IsNullOrEmpty(renderName)
            ? RenderSpecParser.FindFirst(tree)
            : RenderSpecParser.FindByName(tree, renderName);

        // Promote single-staff to MultiStaffScore so SharedRenderer drives both.
        MultiStaffScore multiScore;
        ScoreLayout layout;
        if (renderSpec != null && renderSpec.IsMultiStaff)
        {
            var collector = new MeasureCollector();
            multiScore = collector.CollectMultiStaff(tree, renderSpec);
            layout = new LayoutEngine().Layout(multiScore);
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
            var score = collector.Collect(tree, voiceName, renderSpec?.LocalStructure);
            multiScore = MultiStaffScore.FromScore(score);
            layout = new LayoutEngine().Layout(multiScore);
        }

        var fontDir = options.FontDirectory ?? FontLocator.Find();
        var docOptions = new PngDocumentOptions
        {
            // PngRenderOptions.Scale is "× SVG-baseline DPI"; SharedRenderer
            // works in staff-spaces. Map: 10 px per staff-space at scale 1.0
            // (matches the existing SvgGenerator's PixelsPerSpace = 10).
            PixelsPerSpace = options.Scale * 10.0,
            Quality = options.Quality,
            FontDirectory = fontDir,
        };

        using var doc = new PngDocumentContext(docOptions);
        SharedRenderer.RenderTo(multiScore, layout, doc);
        doc.Dispose();
        return doc.GetBytes();
    }

    /// <summary>
    /// Converts an SVG string to PNG bytes using SkiaSharp's SVG rasterizer.
    /// Kept for callers that already hold an SVG document; new code should
    /// prefer <see cref="Generate"/> for the direct (no-roundtrip) path.
    /// </summary>
    public static byte[] ConvertSvgToPng(string svgString, PngRenderOptions? options = null, string? fontDirectory = null)
    {
        options ??= PngRenderOptions.Default;
        fontDirectory ??= FontLocator.Find();

        using var svg = new global::Svg.Skia.SKSvg();
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

            using var surface = SKSurface.Create(new SKImageInfo(width, height,
                SKColorType.Rgba8888, SKAlphaType.Premul));
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

    private static List<EmmentalerTypefaceProvider> RegisterMusicFonts(
        global::Svg.Skia.SKSvg svg, string? fontDirectory)
    {
        var providers = new List<EmmentalerTypefaceProvider>();
        if (fontDirectory == null) return providers;

        RegisterFont(svg, fontDirectory, "emmentaler-20.otf", "Emmentaler", providers);
        if (providers.Count == 0)
            RegisterFont(svg, fontDirectory, "emmentaler-20.woff2", "Emmentaler", providers);

        RegisterFont(svg, fontDirectory, "emmentaler-brace.otf", "Emmentaler-Brace", providers);
        if (providers.Count < 2)
            RegisterFont(svg, fontDirectory, "emmentaler-brace.woff", "Emmentaler-Brace", providers);
        return providers;
    }

    private static void RegisterFont(global::Svg.Skia.SKSvg svg, string fontDir, string fileName,
        string familyName, List<EmmentalerTypefaceProvider> providers)
    {
        var fontPath = Path.Combine(fontDir, fileName);
        if (!File.Exists(fontPath)) return;

        var fontBytes = File.ReadAllBytes(fontPath);
        var skData = SKData.CreateCopy(fontBytes);
        var typeface = SKTypeface.FromData(skData);
        if (typeface == null) { skData.Dispose(); return; }

        var provider = new EmmentalerTypefaceProvider(familyName, typeface);
        svg.Settings.TypefaceProviders ??= new List<global::Svg.Skia.TypefaceProviders.ITypefaceProvider>();
        svg.Settings.TypefaceProviders.Insert(0, provider);
        providers.Add(provider);
    }

}

/// <summary>Custom typeface provider for Svg.Skia (legacy SVG path only).</summary>
internal sealed class EmmentalerTypefaceProvider : global::Svg.Skia.TypefaceProviders.ITypefaceProvider, IDisposable
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
        var families = fontFamily.Split(',', StringSplitOptions.TrimEntries);
        foreach (var raw in families)
        {
            var name = raw.Trim('\'', '"');
            if (string.Equals(name, _familyName, StringComparison.OrdinalIgnoreCase))
                return _typeface;
            if (_typeface.FamilyName != null &&
                string.Equals(name, _typeface.FamilyName, StringComparison.OrdinalIgnoreCase))
                return _typeface;
        }
        return null;
    }

    public void Dispose() => _typeface.Dispose();
}
