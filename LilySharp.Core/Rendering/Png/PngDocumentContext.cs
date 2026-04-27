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

using SkiaSharp;

namespace LilySharp.Core.Rendering.Png;

/// <summary>Configuration for <see cref="PngDocumentContext"/>.</summary>
public sealed class PngDocumentOptions
{
    /// <summary>
    /// Pixels per staff-space (sets engraving scale + image resolution).
    /// E.g. 10 = 96-DPI baseline, 20 = 2× retina, 30 = 3× print quality.
    /// </summary>
    public double PixelsPerSpace { get; init; } = 20.0;

    /// <summary>PNG encoder quality (0-100). 100 = lossless.</summary>
    public int Quality { get; init; } = 100;

    /// <summary>Background color. Defaults to white.</summary>
    public Color Background { get; init; } = Color.White;

    /// <summary>Optional font directory override.</summary>
    public string? FontDirectory { get; init; }
}

/// <summary>
/// PNG implementation of <see cref="IDocumentContext"/> backed by SkiaSharp.
/// Multi-page documents are stacked vertically in a single PNG (each page
/// begins at <c>y = sum(previous heights)</c>).
/// </summary>
/// <remarks>
/// Unlike PdfDocumentContext, the PNG backend pre-allocates a single
/// surface large enough to hold all pages. <see cref="BeginPage"/> sets a
/// translation so the page-local <c>(0, 0)</c> maps to the next free row.
/// </remarks>
public sealed class PngDocumentContext : IDocumentContext
{
    private readonly PngDocumentOptions _options;
    private readonly List<(double WidthSp, double HeightSp)> _pendingPages = new();
    private SKSurface? _surface;
    private SKCanvas? _canvas;
    private PngDrawingContext? _currentPage;
    private double _accumulatedY;
    private bool _disposed;
    private byte[]? _bytes;

    public PngDocumentContext(PngDocumentOptions? options = null)
    {
        _options = options ?? new PngDocumentOptions();
    }

    public IDrawingContext BeginPage(double widthSpaces, double heightSpaces)
    {
        if (_currentPage != null)
            throw new InvalidOperationException("Previous page not ended.");

        // Lazy-allocate surface on the first page using its dimensions.
        // Multi-page PNG isn't a real format; we vertically stack instead.
        // The first BeginPage sets the canvas width to the first page's width
        // (assumed largest in practice). Subsequent pages are clipped to that
        // width if they exceed it.
        if (_surface == null)
        {
            int w = (int)Math.Ceiling(widthSpaces * _options.PixelsPerSpace);
            int h = (int)Math.Ceiling(heightSpaces * _options.PixelsPerSpace);
            // Reserve room for likely future pages (×4); we'll truncate at Dispose.
            int reservedH = Math.Max(h, h * 4);
            _surface = SKSurface.Create(new SKImageInfo(w, reservedH,
                SKColorType.Rgba8888, SKAlphaType.Premul))
                ?? throw new InvalidOperationException("Failed to allocate SKSurface.");
            _canvas = _surface.Canvas;
            var bg = _options.Background;
            _canvas.Clear(new SKColor(bg.R, bg.G, bg.B, bg.A));
        }

        _canvas!.Save();
        _canvas.Translate(0, (float)(_accumulatedY * _options.PixelsPerSpace));
        _currentPage = new PngDrawingContext(_canvas, _options.PixelsPerSpace,
            _options.FontDirectory);
        _pendingPages.Add((widthSpaces, heightSpaces));
        return _currentPage;
    }

    public void EndPage()
    {
        if (_canvas == null || _currentPage == null)
            throw new InvalidOperationException("No page to end.");
        _canvas.Restore();
        var (_, h) = _pendingPages[^1];
        _accumulatedY += h;
        _currentPage = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (_currentPage != null) EndPage();

        if (_surface != null)
        {
            // Snapshot only the used portion of the surface (crop to actual content).
            int finalH = Math.Max(1, (int)Math.Ceiling(_accumulatedY * _options.PixelsPerSpace));
            int width = _surface.Canvas.LocalClipBounds.Width > 0
                ? (int)_surface.Canvas.LocalClipBounds.Width
                : (int)Math.Ceiling(_pendingPages[0].WidthSp * _options.PixelsPerSpace);

            using var image = _surface.Snapshot(new SKRectI(0, 0, width, finalH));
            using var data = image.Encode(SKEncodedImageFormat.Png, _options.Quality);
            _bytes = data.ToArray();
            _surface.Dispose();
            _surface = null;
        }
        _disposed = true;
    }

    /// <summary>Returns the saved PNG bytes (post-Dispose).</summary>
    public byte[] GetBytes() =>
        _bytes ?? throw new InvalidOperationException("Dispose first.");
}
