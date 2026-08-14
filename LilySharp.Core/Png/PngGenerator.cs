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

        // Find render specification - by name if specified, otherwise first
        var renderSpec = string.IsNullOrEmpty(renderName)
            ? RenderSpecParser.FindFirst(tree)
            : RenderSpecParser.FindByName(tree, renderName);

        // ONE collection path for every output format: this used to be a
        // hand-copied subset of SvgGenerator.CollectScore and silently missed
        // its newer behaviours (score transpose, `with chords` attachment) —
        // the PNG of a score could differ from its SVG.
        MultiStaffScore multiScore = SvgGenerator.CollectScore(tree, renderSpec);
        ScoreLayout layout = new LayoutEngine().Layout(multiScore);

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
    /// Generates one PNG per page. LilyPond's PNG backend emits a file per
    /// page (BASE-page%d.png, scm/ps-to-png.scm) rather than one tall image;
    /// callers name the files accordingly.
    /// </summary>
    public static IReadOnlyList<byte[]> GeneratePages(SyntaxTree tree, PngRenderOptions? options = null, string? renderName = null)
    {
        options ??= PngRenderOptions.Default;

        var renderSpec = string.IsNullOrEmpty(renderName)
            ? RenderSpecParser.FindFirst(tree)
            : RenderSpecParser.FindByName(tree, renderName);

        MultiStaffScore multiScore = SvgGenerator.CollectScore(tree, renderSpec);
        ScoreLayout layout = new LayoutEngine().Layout(multiScore);

        var fontDir = options.FontDirectory ?? FontLocator.Find();
        var docOptions = new PngDocumentOptions
        {
            PixelsPerSpace = options.Scale * 10.0,
            Quality = options.Quality,
            FontDirectory = fontDir,
            SeparatePages = true,
        };

        using var doc = new PngDocumentContext(docOptions);
        SharedRenderer.RenderTo(multiScore, layout, doc);
        doc.Dispose();
        return doc.GetPageBytes();
    }

}
