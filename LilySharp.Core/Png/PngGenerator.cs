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
using LilySharp.Core.Svg;
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
/// There is no longer an SVG-string entry point. The one that existed rendered
/// through Svg.Skia, whose Svg.Custom dependency is MS-PL — a free but
/// GPL-incompatible license, which no binary Lily# distributes may contain.
/// Its only caller was the test-only visual-diff harness, which now carries
/// that rasterizer itself (LilySharp.Tests/Svg/VisualDiffReport.cs).
/// </remarks>
public static class PngGenerator
{
    /// <summary>
    /// Generates a PNG image from a syntax tree.
    /// </summary>
    public static byte[] Generate(SyntaxTree tree, PngRenderOptions? options = null, string? renderName = null)
    {
        options ??= PngRenderOptions.Default;

        // The score a name picks: Choose's policy (a match, else the FIRST score), the
        // one every door resolves with. Until 2026-10-03 this and the PDF used FindByName,
        // which answered null for a stale name — the file's scoreless picture.
        var renderSpec = RenderSpecParser.Choose(RenderSpecParser.FindAll(tree), renderName);

        // ONE collection path for every output format: this used to be a
        // hand-copied subset of SvgGenerator.CollectScore and silently missed
        // its newer behaviours (score transpose, `with chords` attachment) —
        // the PNG of a score could differ from its SVG.
        MultiStaffScore multiScore = SvgGenerator.CollectScore(tree, renderSpec, settings: options.PaperOverrides);
        ScoreLayout layout = new LayoutEngine(multiScore.Paper).Layout(multiScore);
        LayoutWarnings.Report(layout, multiScore.Paper, options.LayoutWarning);

        var fontDir = options.FontDirectory ?? FontLocator.Find();
        var docOptions = new PngDocumentOptions
        {
            // PngRenderOptions.Scale is "× SVG-baseline DPI"; SharedRenderer
            // works in staff-spaces. Map: 10 px per staff-space at scale 1.0
            // (matches the existing SvgGenerator's PixelsPerSpace = 10).
            // The paper keeps its size on another staff space (`--set staffSpace=…`).
            PixelsPerSpace = options.Scale * 10.0 * multiScore.Paper.StaffSpaceScale,
            Quality = options.Quality,
            FontDirectory = fontDir,
        };

        using var doc = new PngDocumentContext(docOptions);
        using (var fallbacks = MusicFallbackLog.Open())
        {
            SharedRenderer.RenderTo(multiScore, layout, doc);
            fallbacks.Report(options.LayoutWarning);
        }
        doc.Dispose();
        return doc.GetBytes();
    }

    /// <summary>
    /// Generates one PNG per page. LilyPond's PNG backend emits a file per
    /// page (BASE-page%d.png, scm/ps-to-png.scm) rather than one tall image;
    /// callers name the files accordingly.
    /// </summary>
    public static IReadOnlyList<byte[]> GeneratePages(SyntaxTree tree, PngRenderOptions? options = null, string? renderName = null)
        => GenerateScorePages(tree, RenderSpecParser.Choose(RenderSpecParser.FindAll(tree), renderName), options);

    /// <summary>One PNG per page of one given score (null = a file with no <c>score</c>
    /// block) — <see cref="SvgGenerator.GenerateScore"/> says why a name is not always
    /// enough.</summary>
    public static IReadOnlyList<byte[]> GenerateScorePages(SyntaxTree tree, RenderSpec? renderSpec, PngRenderOptions? options = null)
    {
        options ??= PngRenderOptions.Default;

        MultiStaffScore multiScore = SvgGenerator.CollectScore(tree, renderSpec, settings: options.PaperOverrides);
        ScoreLayout layout = new LayoutEngine(multiScore.Paper).Layout(multiScore);
        LayoutWarnings.Report(layout, multiScore.Paper, options.LayoutWarning);

        var fontDir = options.FontDirectory ?? FontLocator.Find();
        var docOptions = new PngDocumentOptions
        {
            PixelsPerSpace = options.Scale * 10.0 * multiScore.Paper.StaffSpaceScale,
            Quality = options.Quality,
            FontDirectory = fontDir,
            SeparatePages = true,
        };

        using var doc = new PngDocumentContext(docOptions);
        using (var fallbacks = MusicFallbackLog.Open())
        {
            SharedRenderer.RenderTo(multiScore, layout, doc);
            fallbacks.Report(options.LayoutWarning);
        }
        doc.Dispose();
        return doc.GetPageBytes();
    }

    /// <summary>
    /// Trims a PNG to the bounding box of its non-background (non-near-white) pixels,
    /// plus a small margin, so a tiny snippet fills the frame instead of floating in a
    /// page-sized sea of white. Returns the original bytes if nothing (or everything)
    /// is background. (<c>lysc png --crop</c>; lived in the CLI until 2026-10-03.)
    /// </summary>
    public static byte[] CropToContent(byte[] png, int marginPx = 8)
    {
        using var bitmap = SkiaSharp.SKBitmap.Decode(png);
        if (bitmap == null) return png;
        int w = bitmap.Width, h = bitmap.Height;
        int minX = w, minY = h, maxX = -1, maxY = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var c = bitmap.GetPixel(x, y);
                // "Ink" = any pixel darker than near-white on any channel (ignores the
                // white/near-white page background and anti-aliasing fringe).
                if (c.Alpha > 16 && (c.Red < 240 || c.Green < 240 || c.Blue < 240))
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
        if (maxX < minX || maxY < minY) return png; // blank image

        minX = System.Math.Max(0, minX - marginPx);
        minY = System.Math.Max(0, minY - marginPx);
        maxX = System.Math.Min(w - 1, maxX + marginPx);
        maxY = System.Math.Min(h - 1, maxY + marginPx);
        int cw = maxX - minX + 1, ch = maxY - minY + 1;
        if (cw >= w && ch >= h) return png; // already tight

        using var cropped = new SkiaSharp.SKBitmap(cw, ch);
        using (var canvas = new SkiaSharp.SKCanvas(cropped))
        {
            canvas.Clear(SkiaSharp.SKColors.White);
            canvas.DrawBitmap(bitmap, new SkiaSharp.SKRect(minX, minY, maxX + 1, maxY + 1),
                new SkiaSharp.SKRect(0, 0, cw, ch));
        }
        using var img = SkiaSharp.SKImage.FromBitmap(cropped);
        using var data = img.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
