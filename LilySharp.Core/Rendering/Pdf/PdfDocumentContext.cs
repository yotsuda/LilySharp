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

using PdfSharpCore.Drawing;
using PdfSharpCore.Fonts;
using PdfSharpCore.Pdf;

namespace LilySharp.Core.Rendering.Pdf;

/// <summary>Configuration for <see cref="PdfDocumentContext"/>.</summary>
internal sealed class PdfDocumentOptions
{
    /// <summary>PDF points per staff-space (sets the overall engraving size).</summary>
    public double PointsPerSpace { get; init; } = 6.0;

    /// <summary>If <c>true</c>, lay out the page using the first page's
    /// requested dimensions plus a small margin. If <c>false</c>, every
    /// PDF page uses the explicit <see cref="PageWidthPt"/> /
    /// <see cref="PageHeightPt"/> regardless of the BeginPage size.</summary>
    public bool AutoSizePages { get; init; } = true;

    /// <summary>Fixed page width in points (used when AutoSizePages = false).</summary>
    public double PageWidthPt { get; init; } = 595.28;  // A4

    /// <summary>Fixed page height in points (used when AutoSizePages = false).</summary>
    public double PageHeightPt { get; init; } = 841.89;

    /// <summary>
    /// Extra margin (points) added on every side of an auto-sized page, on top
    /// of the engraving's own small left/top margins. Content is shifted by this
    /// amount so the page keeps a symmetric border instead of the music running
    /// to the right/bottom edge. Ignored when AutoSizePages = false.
    /// </summary>
    public double AutoSizeMarginPt { get; init; } = 18.0;  // ~6.4 mm

    /// <summary>Optional font directory override.</summary>
    public string? FontDirectory { get; init; }

    /// <summary>The document's configured text font (<c>font "X"</c>), or null.</summary>
    public string? TextFontFamily { get; init; }

    /// <summary>Whether to subset-embed <see cref="TextFontFamily"/> (<c>embedded</c>).</summary>
    public bool EmbedTextFont { get; init; }
}

/// <summary>
/// PDF implementation of <see cref="IDocumentContext"/> backed by
/// PdfSharpCore. Each <see cref="BeginPage"/> creates one PDF page; the
/// Emmentaler music font is registered globally on first use so glyphs
/// from <c>EmmentalerGlyphs</c> can be drawn directly via
/// <see cref="IDrawingContext.DrawGlyph"/>.
/// </summary>
internal sealed class PdfDocumentContext : IDocumentContext
{
    private static readonly object _resolverLock = new();
    private static bool _resolverInstalled;
    private static EmmentalerFontResolver? _installedResolver;

    private readonly PdfDocumentOptions _options;
    private readonly EmmentalerFontResolver _resolver;
    private readonly PdfDocument _document;
    private PdfDrawingContext? _currentPage;
    private XGraphics? _currentGfx;
    private bool _disposed;

    public PdfDocumentContext(PdfDocumentOptions? options = null)
    {
        _options = options ?? new PdfDocumentOptions();
        _resolver = EnsureFontResolver(_options.FontDirectory);
        // Per-document text font + embed intent (font "X" [embedded]). The resolver
        // is a process global set once, but this target is mutable and refreshed per
        // document.
        _resolver.SetTextFont(_options.TextFontFamily, _options.EmbedTextFont);
        _document = new PdfDocument();
    }

    public IDrawingContext BeginPage(double widthSpaces, double heightSpaces)
    {
        if (_currentPage != null)
            throw new InvalidOperationException("Previous page not ended.");

        var page = _document.AddPage();
        double originPt = 0;
        if (_options.AutoSizePages)
        {
            // Pad the content box on every side so the music doesn't touch the
            // page edge; shift the drawing origin by the same amount to keep the
            // left/right (and top/bottom) borders symmetric.
            originPt = _options.AutoSizeMarginPt;
            page.Width = widthSpaces * _options.PointsPerSpace + 2 * originPt;
            page.Height = heightSpaces * _options.PointsPerSpace + 2 * originPt;
        }
        else
        {
            page.Width = _options.PageWidthPt;
            page.Height = _options.PageHeightPt;
        }

        _currentGfx = XGraphics.FromPdfPage(page);
        _currentPage = new PdfDrawingContext(_currentGfx, _options.PointsPerSpace, originPt, _resolver);
        return _currentPage;
    }

    public void EndPage()
    {
        if (_currentGfx == null)
            throw new InvalidOperationException("No page to end.");
        _currentGfx.Dispose();
        _currentGfx = null;
        _currentPage = null;
    }

    /// <summary>
    /// Returns the document bytes. Call after <see cref="Dispose"/>.
    /// Alias for <see cref="GetBytes"/>: the bytes are captured during
    /// <see cref="Dispose"/> (the underlying document is disposed there, so we
    /// cannot re-save it afterwards).
    /// </summary>
    public byte[] ToBytes() => GetBytes();

    private byte[]? _savedBytes;

    public void Dispose()
    {
        if (_disposed) return;
        if (_currentPage != null) EndPage();
        using (var ms = new MemoryStream())
        {
            _document.Save(ms);
            _savedBytes = ms.ToArray();
        }
        _document.Dispose();
        _disposed = true;
    }

    /// <summary>Returns the saved bytes (post-Dispose).</summary>
    public byte[] GetBytes() =>
        _savedBytes ?? throw new InvalidOperationException("Dispose first.");

    /// <summary>
    /// Installs the Emmentaler font resolver (composed over PdfSharpCore's default)
    /// exactly once per process.
    /// </summary>
    /// <remarks>
    /// LIMITATION: PdfSharpCore's <c>GlobalFontSettings.FontResolver</c> is a
    /// process-global with no reset API, so the FIRST render's
    /// <paramref name="fontDirectory"/> wins for the whole process lifetime — a
    /// later <see cref="PdfDocumentContext"/> created with a DIFFERENT
    /// FontDirectory is silently ignored. Harmless for the one-shot CLI (a fresh
    /// process per invocation); latent for a long-lived host (e.g. an LSP server
    /// rendering multiple documents with differing font directories).
    /// </remarks>
    private static EmmentalerFontResolver EnsureFontResolver(string? fontDirectory)
    {
        lock (_resolverLock)
        {
            if (!_resolverInstalled)
            {
                // PdfSharpCore allows the resolver to be set only once per process.
                // We compose: ours first (Emmentaler), fallback to the existing
                // resolver (PdfSharpCore's default handles common system fonts
                // like "Serif" used by SharedRenderer for titles/lyrics/dynamics).
                var existing = GlobalFontSettings.FontResolver;
                _installedResolver = new EmmentalerFontResolver(fontDirectory, existing);
                GlobalFontSettings.FontResolver = _installedResolver;
                _resolverInstalled = true;
            }
            return _installedResolver!;
        }
    }
}
