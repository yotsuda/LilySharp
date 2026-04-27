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

/// <summary>PNG implementation of <see cref="IDrawingContext"/>.</summary>
internal sealed class PngDrawingContext : IDrawingContext
{
    private readonly SKCanvas _canvas;
    private readonly double _scale;
    private readonly FontCache _fonts;

    public PngDrawingContext(SKCanvas canvas, double pixelsPerSpace, string? fontDirectory)
    {
        _canvas = canvas;
        _scale = pixelsPerSpace;
        _fonts = new FontCache(fontDirectory);
    }

    private float X(double s) => (float)(s * _scale);
    private float T(double s) => (float)(s * _scale);
    private static SKColor ToSKColor(Color? c)
    {
        var col = c ?? Color.Black;
        return new SKColor(col.R, col.G, col.B, col.A);
    }

    public void DrawLine(double x1, double y1, double x2, double y2,
        Color? stroke = null, double strokeWidth = 0.1,
        (double On, double Off)? dash = null)
    {
        using var paint = new SKPaint
        {
            Color = ToSKColor(stroke),
            StrokeWidth = T(strokeWidth),
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };
        if (dash is { } d)
            paint.PathEffect = SKPathEffect.CreateDash(
                new[] { (float)T(d.On), (float)T(d.Off) }, 0);
        _canvas.DrawLine(X(x1), X(y1), X(x2), X(y2), paint);
        paint.PathEffect?.Dispose();
    }

    public void DrawRectangle(double x, double y, double width, double height,
        Color? fill = null, Color? stroke = null, double strokeWidth = 0)
    {
        var rect = SKRect.Create(X(x), X(y), T(width), T(height));
        if (fill is { } f)
        {
            using var fp = new SKPaint { Color = ToSKColor(f), Style = SKPaintStyle.Fill, IsAntialias = true };
            _canvas.DrawRect(rect, fp);
        }
        if (stroke is { } s)
        {
            using var sp = new SKPaint { Color = ToSKColor(s), StrokeWidth = T(strokeWidth), Style = SKPaintStyle.Stroke, IsAntialias = true };
            _canvas.DrawRect(rect, sp);
        }
    }

    public void DrawEllipse(double cx, double cy, double rx, double ry,
        Color? fill = null, Color? stroke = null, double strokeWidth = 0)
    {
        if (fill is { } f)
        {
            using var fp = new SKPaint { Color = ToSKColor(f), Style = SKPaintStyle.Fill, IsAntialias = true };
            _canvas.DrawOval(X(cx), X(cy), T(rx), T(ry), fp);
        }
        if (stroke is { } s)
        {
            using var sp = new SKPaint { Color = ToSKColor(s), StrokeWidth = T(strokeWidth), Style = SKPaintStyle.Stroke, IsAntialias = true };
            _canvas.DrawOval(X(cx), X(cy), T(rx), T(ry), sp);
        }
    }

    public void DrawCircle(double cx, double cy, double r, Color? fill = null)
    {
        using var paint = new SKPaint
        {
            Color = ToSKColor(fill),
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };
        _canvas.DrawCircle(X(cx), X(cy), T(r), paint);
    }

    public void DrawClosedBezier(
        (double X, double Y) p0, (double X, double Y) c1, (double X, double Y) c2,
        (double X, double Y) p1, (double X, double Y) c2Back, (double X, double Y) c1Back,
        Color? fill = null)
    {
        using var path = new SKPath();
        path.MoveTo(X(p0.X), X(p0.Y));
        path.CubicTo(X(c1.X), X(c1.Y), X(c2.X), X(c2.Y), X(p1.X), X(p1.Y));
        path.CubicTo(X(c2Back.X), X(c2Back.Y), X(c1Back.X), X(c1Back.Y), X(p0.X), X(p0.Y));
        path.Close();
        using var paint = new SKPaint
        {
            Color = ToSKColor(fill),
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };
        _canvas.DrawPath(path, paint);
    }

    public void DrawGlyph(char glyph, double x, double y, double fontSize, Color? fill = null)
    {
        var font = _fonts.GetFont("Emmentaler", T(fontSize), FontStyle.Regular);
        using var paint = new SKPaint
        {
            Color = ToSKColor(fill),
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            SubpixelText = true,
        };
        _canvas.DrawText(glyph.ToString(), X(x), X(y), font, paint);
    }

    public void DrawText(string text, double x, double y, double fontSize,
        string fontFamily, FontStyle style = FontStyle.Regular,
        TextAnchor anchor = TextAnchor.Start, Color? fill = null,
        VerticalAnchor verticalAnchor = VerticalAnchor.Baseline)
    {
        var font = _fonts.GetFont(fontFamily, T(fontSize), style);
        using var paint = new SKPaint
        {
            Color = ToSKColor(fill),
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            SubpixelText = true,
        };
        float tx = X(x);
        if (anchor != TextAnchor.Start)
        {
            // SKPaint.MeasureText accepts a string overload (SKFont.MeasureText
            // requires glyph IDs in 2.88.x); use SKPaint as the measurer.
            paint.TextSize = T(fontSize);
            paint.Typeface = font.Typeface;
            float width = paint.MeasureText(text);
            if (anchor == TextAnchor.Middle) tx -= width / 2f;
            else /* End */ tx -= width;
        }
        // SVG dominant-baseline parity: shift Y so SkiaSharp's baseline lands
        // where the requested anchor visually is. Use SKFont.Metrics for
        // accurate ascent/descent (ascent is negative in Skia).
        float ty = X(y);
        if (verticalAnchor != VerticalAnchor.Baseline)
        {
            var metrics = font.Metrics;
            ty += verticalAnchor switch
            {
                VerticalAnchor.Middle => -(metrics.Ascent + metrics.Descent) / 2f,
                VerticalAnchor.Hanging => -metrics.Ascent,
                _ => 0,
            };
        }
        _canvas.DrawText(text, tx, ty, font, paint);
    }

    public IDisposable Source(int sourcePosition)
    {
        // PNG output has no source-position metadata channel.
        return NullScope.Instance;
    }

    public IDisposable BeginGroup(DrawingTransform transform)
    {
        int saveCount = _canvas.Save();
        if (!transform.IsIdentity)
        {
            _canvas.Translate(T(transform.TranslateX), T(transform.TranslateY));
            _canvas.Scale((float)transform.ScaleX, (float)transform.ScaleY);
        }
        return new ScopeAction(() => _canvas.RestoreToCount(saveCount));
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }

    private sealed class ScopeAction : IDisposable
    {
        private Action? _action;
        public ScopeAction(Action a) { _action = a; }
        public void Dispose() { _action?.Invoke(); _action = null; }
    }

    /// <summary>
    /// Caches SKTypeface and SKFont instances per family/style. Loads the
    /// Emmentaler music fonts from disk; falls back to system typefaces for
    /// generic families like "serif" and "sans-serif".
    /// </summary>
    private sealed class FontCache : IDisposable
    {
        private readonly string? _fontDirectory;
        private readonly Dictionary<string, SKTypeface> _typefaces = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<SKFont> _fonts = new();

        public FontCache(string? fontDirectory)
        {
            _fontDirectory = fontDirectory;
        }

        public SKFont GetFont(string family, float pixelSize, FontStyle style)
        {
            var typeface = GetTypeface(family, style);
            // SKFont allocations are cheap but disposable; cache for reuse.
            var font = new SKFont(typeface, pixelSize) { Edging = SKFontEdging.SubpixelAntialias };
            _fonts.Add(font);
            return font;
        }

        private SKTypeface GetTypeface(string family, FontStyle style)
        {
            string key = $"{family.ToLowerInvariant()}|{style}";
            if (_typefaces.TryGetValue(key, out var cached))
                return cached;

            SKTypeface? tf = TryLoadEmmentaler(family) ?? TryLoadSystem(family, style);
            tf ??= SKTypeface.Default;
            _typefaces[key] = tf;
            return tf;
        }

        private SKTypeface? TryLoadEmmentaler(string family)
        {
            string? fileName = family.ToLowerInvariant() switch
            {
                "emmentaler" or "emmentaler-20" => "emmentaler-20.otf",
                "emmentaler-brace" => "emmentaler-brace.otf",
                _ => null
            };
            if (fileName == null) return null;
            string? path = ResolveFontPath(fileName);
            return path != null ? SKTypeface.FromFile(path) : null;
        }

        private static SKTypeface? TryLoadSystem(string family, FontStyle style)
        {
            var weight = (style & FontStyle.Bold) != 0 ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal;
            var slant = (style & FontStyle.Italic) != 0 ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright;
            // Map CSS generic family names to a reasonable system font.
            string actual = family.ToLowerInvariant() switch
            {
                "serif" => "Times New Roman",
                "sans-serif" or "sans" => "Arial",
                "monospace" or "mono" => "Courier New",
                _ => family,
            };
            return SKTypeface.FromFamilyName(actual, weight, SKFontStyleWidth.Normal, slant);
        }

        private string? ResolveFontPath(string fileName)
        {
            if (!string.IsNullOrEmpty(_fontDirectory))
            {
                var p = Path.Combine(_fontDirectory, fileName);
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

        public void Dispose()
        {
            foreach (var f in _fonts) f.Dispose();
            foreach (var t in _typefaces.Values) t.Dispose();
        }
    }
}
