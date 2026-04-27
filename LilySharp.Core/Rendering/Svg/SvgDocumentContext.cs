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

using System.Text;

namespace LilySharp.Core.Rendering.Svg;

/// <summary>Configuration for <see cref="SvgDocumentContext"/>.</summary>
public sealed class SvgDocumentOptions
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
}

/// <summary>
/// Single-document, single-page SVG output. Multi-page documents are
/// emitted as multiple <c>&lt;svg&gt;</c> elements stacked vertically in
/// the same root document (each page begins at <c>(0, accumulatedY)</c>).
/// </summary>
public sealed class SvgDocumentContext : IDocumentContext
{
    private readonly SvgDocumentOptions _options;
    private readonly StringBuilder _sb = new();
    private SvgDrawingContext? _currentPage;
    private bool _headerWritten;
    private bool _disposed;

    public SvgDocumentContext(SvgDocumentOptions? options = null)
    {
        _options = options ?? new SvgDocumentOptions();
    }

    public IDrawingContext BeginPage(double widthSpaces, double heightSpaces)
    {
        if (_currentPage != null)
            throw new InvalidOperationException("Previous page not ended.");
        if (!_headerWritten)
        {
            WriteHeader(widthSpaces, heightSpaces);
            _headerWritten = true;
        }
        _currentPage = new SvgDrawingContext(_sb);
        return _currentPage;
    }

    public void EndPage()
    {
        if (_currentPage == null)
            throw new InvalidOperationException("No page to end.");
        _currentPage = null;
    }

    /// <summary>Returns the accumulated SVG text. Call after <see cref="Dispose"/>.</summary>
    public string ToSvg()
    {
        if (!_disposed)
            throw new InvalidOperationException("Dispose the document before reading SVG.");
        return _sb.ToString();
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (_currentPage != null) EndPage();
        if (_headerWritten)
            _sb.AppendLine("</svg>");
        _disposed = true;
    }

    private void WriteHeader(double widthSpaces, double heightSpaces)
    {
        double widthPx = widthSpaces * _options.PixelsPerSpace;
        double heightPx = heightSpaces * _options.PixelsPerSpace;

        _sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        _sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{widthPx:F1}\" height=\"{heightPx:F1}\" viewBox=\"0 0 {widthSpaces:F2} {heightSpaces:F2}\">");
        _sb.AppendLine("<style>");
        var fontFaceRule = GetFontFaceRule();
        if (!string.IsNullOrEmpty(fontFaceRule))
            _sb.AppendLine("  " + fontFaceRule);
        _sb.AppendLine("  .music { font-family: 'Emmentaler', serif; }");
        _sb.AppendLine("</style>");
    }

    private string GetFontFaceRule()
    {
        // Preview mode: host page (VS Code webview, browser) injects Emmentaler.
        if (_options.OmitFontFace)
            return "";
        if (_options.EmbedFont)
        {
            var path = ResolveFontPath("emmentaler-20.woff2");
            if (path != null && File.Exists(path))
            {
                var bytes = File.ReadAllBytes(path);
                var b64 = Convert.ToBase64String(bytes);
                return $"@font-face {{ font-family: 'Emmentaler'; src: url('data:font/woff2;base64,{b64}') format('woff2'); }}";
            }
        }
        return "@font-face { font-family: 'Emmentaler'; src: local('Emmentaler'); }";
    }

    private string? ResolveFontPath(string fileName)
    {
        if (!string.IsNullOrEmpty(_options.FontDirectory))
        {
            var p = Path.Combine(_options.FontDirectory, fileName);
            if (File.Exists(p)) return p;
        }
        var baseDir = AppContext.BaseDirectory;
        foreach (var sub in new[] { "Fonts", "fonts", "" })
        {
            var p = Path.Combine(baseDir, sub, fileName);
            if (File.Exists(p)) return p;
        }
        return null;
    }
}
