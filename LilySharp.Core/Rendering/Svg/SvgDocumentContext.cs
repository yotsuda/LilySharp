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

using System.Globalization;
using System.Text;

namespace LilySharp.Core.Rendering.Svg;

/// <summary>Configuration for <see cref="SvgDocumentContext"/>.</summary>
internal sealed class SvgDocumentOptions
{
    /// <summary>Pixels per staff-space when emitting outer width/height.</summary>
    public double PixelsPerSpace { get; init; } = 10.0;

    /// <summary>If true, embed Emmentaler as base64 WOFF2; otherwise reference by name.</summary>
    public bool EmbedFont { get; init; } = true;

    /// <summary>
    /// If true, skip the <c>@font-face</c> rule entirely. Used by the VS Code
    /// preview path where the host page injects Emmentaler from a known URL.
    /// </summary>
    public bool OmitFontFace { get; init; }

    /// <summary>Optional override for the font directory.</summary>
    public string? FontDirectory { get; init; }

    /// <summary>
    /// If true, pages emit interactive click targets (tight per-notehead hit
    /// rectangles). Used by the VS Code preview; off for static export.
    /// </summary>
    public bool Interactive { get; init; }
}

/// <summary>
/// SVG output. A single-page document is emitted as one <c>&lt;svg&gt;</c>.
/// Multi-page documents are stacked vertically inside one root <c>&lt;svg&gt;</c>
/// sized to hold every page: each page's content is wrapped in a
/// <c>&lt;g transform="translate(0, y)"&gt;</c> at its cumulative Y offset, so
/// pages no longer overlap and nothing is clipped by the first page's height.
/// </summary>
internal sealed class SvgDocumentContext : IDocumentContext
{
    private readonly SvgDocumentOptions _options;
    private readonly List<(StringBuilder Content, double Width, double Height)> _pages = new();
    private StringBuilder? _currentContent;
    private double _currentWidth, _currentHeight;
    private SvgDrawingContext? _currentPage;
    private string? _result;
    private bool _disposed;

    public SvgDocumentContext(SvgDocumentOptions? options = null)
    {
        _options = options ?? new SvgDocumentOptions();
    }

    public IDrawingContext BeginPage(double widthSpaces, double heightSpaces)
    {
        if (_currentPage != null)
            throw new InvalidOperationException("Previous page not ended.");
        // Buffer each page separately so we can size the root <svg> to all pages
        // and offset them at Dispose (we don't know the total height up front).
        _currentContent = new StringBuilder();
        _currentWidth = widthSpaces;
        _currentHeight = heightSpaces;
        _currentPage = new SvgDrawingContext(_currentContent, _options.Interactive);
        return _currentPage;
    }

    public void EndPage()
    {
        if (_currentPage == null || _currentContent == null)
            throw new InvalidOperationException("No page to end.");
        _pages.Add((_currentContent, _currentWidth, _currentHeight));
        _currentPage = null;
        _currentContent = null;
    }

    /// <summary>Returns the assembled SVG text. Call after <see cref="Dispose"/>.</summary>
    public string ToSvg()
    {
        if (!_disposed)
            throw new InvalidOperationException("Dispose the document before reading SVG.");
        return _result ?? "";
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (_currentPage != null) EndPage();
        _result = Assemble();
        _disposed = true;
    }

    private string Assemble()
    {
        if (_pages.Count == 0)
            return "";

        var sb = new StringBuilder();

        // Single page: emit exactly as a one-page document (no <g> wrapper) so the
        // output is byte-identical to the common case.
        if (_pages.Count == 1)
        {
            var (content, w, h) = _pages[0];
            WriteHeader(sb, w, h);
            sb.Append(content);
            sb.AppendLine("</svg>");
            return sb.ToString();
        }

        double totalHeight = 0, maxWidth = 0;
        foreach (var (_, w, h) in _pages)
        {
            totalHeight += h;
            if (w > maxWidth) maxWidth = w;
        }

        WriteHeader(sb, maxWidth, totalHeight);
        double yOffset = 0;
        foreach (var (content, _, h) in _pages)
        {
            // class/data attributes let a viewer (the VS Code preview toolbar)
            // find the page boundaries for fit-page zoom and page navigation.
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "<g class=\"page\" data-page-top=\"{0:F2}\" data-page-height=\"{1:F2}\" transform=\"translate(0, {0:F2})\">",
                yOffset, h));
            sb.Append(content);
            sb.AppendLine("</g>");
            yOffset += h;
        }
        sb.AppendLine("</svg>");
        return sb.ToString();
    }

    private void WriteHeader(StringBuilder sb, double widthSpaces, double heightSpaces)
    {
        double widthPx = widthSpaces * _options.PixelsPerSpace;
        double heightPx = heightSpaces * _options.PixelsPerSpace;

        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        // The root font-family is inherited by every text: non-music text (title, lyrics,
        // fret digits, …) omits its own attribute and picks this up, while the ".music"
        // CSS class overrides it for glyphs and a custom `font "NAME"` overrides it per
        // element. Saves repeating it on hundreds of texts.
        //
        // It NAMES THE BUNDLED FACE, with the generic left as the fallback. The engine
        // reserves space with TextFontMetrics, which measures TeX Gyre Schola — LilyPond's
        // own text face by metrics — so a viewer holding that font now draws exactly what
        // was spaced for. ⚠️ The bare generic used to be a silent mismatch: measured on a
        // stock Windows box the CSS generic "serif" resolves to Segoe UI, a SANS face.
        // ⚠️ REMAINING GAP: unlike Emmentaler the text faces are NOT embedded here, so a
        // viewer without Schola still falls back. Embedding them would add ~185 KB of
        // base64 PER FACE to every SVG, which needs the used-face tracking this context
        // does not have (the header is written before any text is drawn). PNG and PDF are
        // exact today; SVG records the intent and degrades to the generic.
        sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{0:F1}\" height=\"{1:F1}\" viewBox=\"0 0 {2:F2} {3:F2}\" font-family=\"{4}, serif\">",
            widthPx, heightPx, widthSpaces, heightSpaces, TextFontMetrics.SerifFamily));
        sb.AppendLine("<style>");
        var fontFaceRule = GetFontFaceRule();
        if (!string.IsNullOrEmpty(fontFaceRule))
            sb.AppendLine("  " + fontFaceRule);
        sb.AppendLine("  .music { font-family: 'Emmentaler', serif; }");
        sb.AppendLine("</style>");
    }

    private string GetFontFaceRule()
    {
        // Preview mode: host page (VS Code webview, browser) injects the fonts.
        if (_options.OmitFontFace)
            return "";
        if (_options.EmbedFont)
        {
            // Embed BOTH the main notation font and the SEPARATE brace font
            // (Emmentaler-Brace, used for grand-staff/group braces). Without the
            // brace face the brace glyph renders blank in any viewer that lacks
            // the font installed (the bug that hid it in the VS Code preview).
            var faces = new StringBuilder();
            AppendEmbeddedFontFace(faces, "Emmentaler", "emmentaler-20.woff2", "woff2");
            AppendEmbeddedFontFace(faces, "Emmentaler-Brace", "emmentaler-brace.woff", "woff");
            if (faces.Length > 0)
                return faces.ToString().TrimEnd();
        }
        return "@font-face { font-family: 'Emmentaler'; src: local('Emmentaler'); }";
    }

    private void AppendEmbeddedFontFace(StringBuilder sb, string family, string fileName, string format)
    {
        var path = ResolveFontPath(fileName);
        if (path == null || !File.Exists(path))
            return;
        var b64 = Convert.ToBase64String(File.ReadAllBytes(path));
        sb.AppendLine(
            $"@font-face {{ font-family: '{family}'; src: url('data:font/{format};base64,{b64}') format('{format}'); }}");
    }

    private string? ResolveFontPath(string fileName) =>
        FontLocator.ResolveFile(fileName, _options.FontDirectory);
}
