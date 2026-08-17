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
internal sealed class PngDocumentOptions
{
    /// <summary>
    /// Pixels per staff-space (sets engraving scale + image resolution).
    /// E.g. 10 = 96-DPI baseline, 20 = 2× retina, 30 = 3× print quality.
    /// </summary>
    public double PixelsPerSpace { get; init; } = 20.0;

    /// <summary>Encode each page as its own PNG (see GetPageBytes) instead
    /// of stitching pages into one tall image.</summary>
    public bool SeparatePages { get; init; }

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
/// Each page is rendered to its own surface; at <see cref="Dispose"/> the pages
/// are composited into a final image sized to hold them all (height = sum of
/// page heights, width = widest page). This handles any number of pages and
/// pages of differing sizes without clipping — unlike a fixed pre-reserved
/// surface, which dropped pages beyond a guessed height.
/// </remarks>
internal sealed class PngDocumentContext : IDocumentContext
{
    private readonly PngDocumentOptions _options;
    private readonly List<(SKImage Image, int WidthPx, int HeightPx)> _pages = new();
    private SKSurface? _pageSurface;
    private SKCanvas? _pageCanvas;
    private int _pageWidthPx, _pageHeightPx;
    private PngDrawingContext? _currentPage;
    private bool _disposed;
    private byte[]? _bytes;
    private List<byte[]>? _pageBytes;

    public PngDocumentContext(PngDocumentOptions? options = null)
    {
        _options = options ?? new PngDocumentOptions();
    }

    /// <inheritdoc/>
    public TextFontPlan Fonts { get; set; } = TextFontPlan.Default;

    public IDrawingContext BeginPage(double widthSpaces, double heightSpaces)
    {
        if (_currentPage != null)
            throw new InvalidOperationException("Previous page not ended.");

        _pageWidthPx = Math.Max(1, (int)Math.Ceiling(widthSpaces * _options.PixelsPerSpace));
        _pageHeightPx = Math.Max(1, (int)Math.Ceiling(heightSpaces * _options.PixelsPerSpace));
        _pageSurface = SKSurface.Create(new SKImageInfo(_pageWidthPx, _pageHeightPx,
            SKColorType.Rgba8888, SKAlphaType.Premul))
            ?? throw new InvalidOperationException("Failed to allocate SKSurface.");
        _pageCanvas = _pageSurface.Canvas;
        var bg = _options.Background;
        _pageCanvas.Clear(new SKColor(bg.R, bg.G, bg.B, bg.A));
        _currentPage = new PngDrawingContext(_pageCanvas, _options.PixelsPerSpace,
            _options.FontDirectory, Fonts);
        return _currentPage;
    }

    public void EndPage()
    {
        if (_pageSurface == null || _currentPage == null)
            throw new InvalidOperationException("No page to end.");
        _pages.Add((_pageSurface.Snapshot(), _pageWidthPx, _pageHeightPx));
        _pageSurface.Dispose();
        _pageSurface = null;
        _pageCanvas = null;
        _currentPage.Dispose(); // release this page's font handles (typefaces + SKFonts)
        _currentPage = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (_currentPage != null) EndPage();

        if (_pages.Count > 0 && _options.SeparatePages)
        {
            // One PNG per page (LilyPond's multi-page PNG model: ps-to-png
            // emits BASE-page%d.png instead of one tall image).
            _pageBytes = new List<byte[]>(_pages.Count);
            foreach (var (image, _, _) in _pages)
            {
                using var data = image.Encode(SKEncodedImageFormat.Png, _options.Quality);
                _pageBytes.Add(data.ToArray());
            }
            _bytes = _pageBytes[0];
            foreach (var (image, _, _) in _pages)
                image.Dispose();
            _pages.Clear();
        }
        else if (_pages.Count > 0)
        {
            int totalH = 0, maxW = 0;
            foreach (var (_, w, h) in _pages)
            {
                totalH += h;
                if (w > maxW) maxW = w;
            }

            using (var final = SKSurface.Create(new SKImageInfo(maxW, totalH,
                       SKColorType.Rgba8888, SKAlphaType.Premul))
                   ?? throw new InvalidOperationException("Failed to allocate SKSurface."))
            {
                var canvas = final.Canvas;
                var bg = _options.Background;
                canvas.Clear(new SKColor(bg.R, bg.G, bg.B, bg.A));
                float y = 0;
                foreach (var (image, _, h) in _pages)
                {
                    canvas.DrawImage(image, 0, y);
                    y += h;
                }
                using var snapshot = final.Snapshot();
                using var data = snapshot.Encode(SKEncodedImageFormat.Png, _options.Quality);
                _bytes = data.ToArray();
            }

            foreach (var (image, _, _) in _pages)
                image.Dispose();
            _pages.Clear();
        }
        else
        {
            // No page was produced (e.g. a score that laid out to zero systems).
            // Emit a 1x1 blank instead of leaving _bytes null, so GetBytes() can't
            // throw "Dispose first." on an empty render.
            using var blank = SKSurface.Create(new SKImageInfo(1, 1,
                SKColorType.Rgba8888, SKAlphaType.Premul));
            if (blank != null)
            {
                var bg = _options.Background;
                blank.Canvas.Clear(new SKColor(bg.R, bg.G, bg.B, bg.A));
                using var snapshot = blank.Snapshot();
                using var data = snapshot.Encode(SKEncodedImageFormat.Png, _options.Quality);
                _bytes = data.ToArray();
            }
            else
            {
                _bytes = System.Array.Empty<byte>();
            }
        }
        _disposed = true;
    }

    /// <summary>Returns the saved PNG bytes (post-Dispose).</summary>
    public byte[] GetBytes() =>
        _bytes ?? throw new InvalidOperationException("Dispose first.");

    /// <summary>Per-page PNG encodings (post-Dispose); requires
    /// <see cref="PngDocumentOptions.SeparatePages"/>.</summary>
    public IReadOnlyList<byte[]> GetPageBytes() =>
        _pageBytes ?? throw new InvalidOperationException(
            "Dispose first (with SeparatePages set).");
}
