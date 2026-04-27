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
public sealed class PdfDocumentOptions
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

    /// <summary>Optional font directory override.</summary>
    public string? FontDirectory { get; init; }
}

/// <summary>
/// PDF implementation of <see cref="IDocumentContext"/> backed by
/// PdfSharpCore. Each <see cref="BeginPage"/> creates one PDF page; the
/// Emmentaler music font is registered globally on first use so glyphs
/// from <c>EmmentalerGlyphs</c> can be drawn directly via
/// <see cref="IDrawingContext.DrawGlyph"/>.
/// </summary>
public sealed class PdfDocumentContext : IDocumentContext
{
    private static readonly object _resolverLock = new();
    private static bool _resolverInstalled;

    private readonly PdfDocumentOptions _options;
    private readonly PdfDocument _document;
    private PdfDrawingContext? _currentPage;
    private XGraphics? _currentGfx;
    private bool _disposed;

    public PdfDocumentContext(PdfDocumentOptions? options = null)
    {
        _options = options ?? new PdfDocumentOptions();
        EnsureFontResolver(_options.FontDirectory);
        _document = new PdfDocument();
    }

    public IDrawingContext BeginPage(double widthSpaces, double heightSpaces)
    {
        if (_currentPage != null)
            throw new InvalidOperationException("Previous page not ended.");

        var page = _document.AddPage();
        if (_options.AutoSizePages)
        {
            page.Width = widthSpaces * _options.PointsPerSpace;
            page.Height = heightSpaces * _options.PointsPerSpace;
        }
        else
        {
            page.Width = _options.PageWidthPt;
            page.Height = _options.PageHeightPt;
        }

        _currentGfx = XGraphics.FromPdfPage(page);
        _currentPage = new PdfDrawingContext(_currentGfx, _options.PointsPerSpace);
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

    /// <summary>Returns the document bytes. Call after <see cref="Dispose"/>.</summary>
    public byte[] ToBytes()
    {
        if (!_disposed)
            throw new InvalidOperationException("Dispose the document before reading bytes.");
        using var ms = new MemoryStream();
        _document.Save(ms);
        return ms.ToArray();
    }

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

    private static void EnsureFontResolver(string? fontDirectory)
    {
        lock (_resolverLock)
        {
            if (_resolverInstalled) return;
            // PdfSharpCore allows the resolver to be set only once per process.
            // We compose: ours first (Emmentaler), fallback to the existing
            // resolver (PdfSharpCore's default handles common system fonts
            // like "Serif" used by SharedRenderer for titles/lyrics/dynamics).
            var existing = GlobalFontSettings.FontResolver;
            GlobalFontSettings.FontResolver = new EmmentalerFontResolver(fontDirectory, existing);
            _resolverInstalled = true;
        }
    }
}
