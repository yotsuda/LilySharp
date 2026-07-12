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
internal sealed class PngDrawingContext : IDrawingContext, IDisposable
{
    private readonly SKCanvas _canvas;
    private readonly double _scale;
    private readonly FontCache _fonts;

    /// <summary>Releases the per-page font handles (typefaces loaded from disk and
    /// the SKFonts derived from them). Called by PngDocumentContext.EndPage.</summary>
    public void Dispose() => _fonts.Dispose();

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
        (double On, double Off)? dash = null, LineCap cap = LineCap.Butt)
    {
        using var paint = new SKPaint
        {
            Color = ToSKColor(stroke),
            StrokeWidth = T(strokeWidth),
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            StrokeCap = cap == LineCap.Round ? SKStrokeCap.Round : SKStrokeCap.Butt,
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

    public void DrawFilledQuad((double X, double Y) p0, (double X, double Y) p1,
        (double X, double Y) p2, (double X, double Y) p3, Color fill)
    {
        using var path = new SKPath();
        path.MoveTo(X(p0.X), X(p0.Y));
        path.LineTo(X(p1.X), X(p1.Y));
        path.LineTo(X(p2.X), X(p2.Y));
        path.LineTo(X(p3.X), X(p3.Y));
        path.Close();
        using var paint = new SKPaint { Color = ToSKColor(fill), Style = SKPaintStyle.Fill, IsAntialias = true };
        _canvas.DrawPath(path, paint);
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
        Color? fill = null, double strokeWidth = 0)
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
        // Round-cap/join stroke rounds the tapered ends (LilyPond slur/tie stencil).
        if (strokeWidth > 0)
        {
            using var strokePaint = new SKPaint
            {
                Color = ToSKColor(fill),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = T(strokeWidth),
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round,
                IsAntialias = true,
            };
            _canvas.DrawPath(path, strokePaint);
        }
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

        // Glyph fallback: the mapped system face (e.g. Times New Roman for
        // "serif") has no CJK coverage, so Japanese titles/section labels
        // rendered as tofu. Split the text into typeface runs, resolving
        // missing codepoints via SKFontManager.MatchCharacter — the same
        // per-character fallback SVG consumers get from the OS font stack.
        var runs = _fonts.SegmentByTypeface(text, font.Typeface ?? SKTypeface.Default, style);

        using var paint = new SKPaint
        {
            Color = ToSKColor(fill),
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            SubpixelText = true,
        };
        paint.TextSize = T(fontSize);

        float tx = X(x);
        if (anchor != TextAnchor.Start)
        {
            // SKPaint.MeasureText accepts a string overload (SKFont.MeasureText
            // requires glyph IDs in 2.88.x); use SKPaint as the measurer,
            // summing run widths with each run's own typeface.
            float width = 0;
            foreach (var (segment, typeface) in runs)
            {
                paint.Typeface = typeface;
                width += paint.MeasureText(segment);
            }
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

        float cx = tx;
        foreach (var (segment, typeface) in runs)
        {
            paint.Typeface = typeface;
            using var runFont = new SKFont(typeface, T(fontSize)) { Edging = SKFontEdging.SubpixelAntialias };
            _canvas.DrawText(segment, cx, ty, runFont, paint);
            cx += paint.MeasureText(segment);
        }
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

    /// <summary>
    /// Caches SKTypeface and SKFont instances per family/style. Loads the
    /// Emmentaler music fonts from disk; falls back to system typefaces for
    /// generic families like "serif" and "sans-serif".
    /// </summary>
    private sealed class FontCache : IDisposable
    {
        private readonly string? _fontDirectory;
        private readonly Dictionary<string, SKTypeface> _typefaces = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, SKFont> _fonts = new();

        public FontCache(string? fontDirectory)
        {
            _fontDirectory = fontDirectory;
        }

        public SKFont GetFont(string family, float pixelSize, FontStyle style)
        {
            // Cache SKFont by (family, size, style). DrawGlyph/DrawText call
            // this per glyph/run, so allocating a fresh SKFont every time and
            // only disposing at teardown accumulated thousands per document.
            string key = $"{family.ToLowerInvariant()}|{pixelSize}|{style}";
            if (_fonts.TryGetValue(key, out var cached))
                return cached;

            var typeface = GetTypeface(family, style);
            var font = new SKFont(typeface, pixelSize) { Edging = SKFontEdging.SubpixelAntialias };
            _fonts[key] = font;
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

        // Per-codepoint fallback typefaces resolved via the system font
        // manager (cached — CJK runs hit the same face repeatedly).
        private readonly Dictionary<int, SKTypeface?> _fallbacks = new();

        /// <summary>
        /// Splits <paramref name="text"/> into runs renderable by one typeface
        /// each: codepoints the base face covers stay on it; for the rest the
        /// system font manager picks a face that has the glyph (e.g. a CJK
        /// font for Japanese section labels). Unresolvable codepoints stay on
        /// the base face (tofu — nothing better exists on this system).
        /// </summary>
        public List<(string Segment, SKTypeface Typeface)> SegmentByTypeface(
            string text, SKTypeface baseTypeface, FontStyle style)
        {
            var runs = new List<(string, SKTypeface)>();
            if (string.IsNullOrEmpty(text))
                return runs;

            var sb = new System.Text.StringBuilder();
            SKTypeface? runFace = null;

            for (int i = 0; i < text.Length;)
            {
                int cp = char.ConvertToUtf32(text, i);
                int len = char.IsSurrogatePair(text, i) ? 2 : 1;
                var face = ResolveFace(cp, baseTypeface, style);

                if (runFace != null && !ReferenceEquals(face, runFace))
                {
                    runs.Add((sb.ToString(), runFace));
                    sb.Clear();
                }
                runFace = face;
                sb.Append(text, i, len);
                i += len;
            }
            if (sb.Length > 0 && runFace != null)
                runs.Add((sb.ToString(), runFace));
            return runs;
        }

        private SKTypeface ResolveFace(int codepoint, SKTypeface baseTypeface, FontStyle style)
        {
            // Fast path: the base face has the glyph.
            if (baseTypeface.GetGlyph(codepoint) != 0)
                return baseTypeface;

            if (!_fallbacks.TryGetValue(codepoint, out var fallback))
            {
                var weight = (style & FontStyle.Bold) != 0 ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal;
                var slant = (style & FontStyle.Italic) != 0 ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright;
                fallback = SKFontManager.Default.MatchCharacter(
                    null, new SKFontStyle(weight, SKFontStyleWidth.Normal, slant), null, codepoint);
                _fallbacks[codepoint] = fallback;
            }
            return fallback ?? baseTypeface;
        }

        private string? ResolveFontPath(string fileName) =>
            FontLocator.ResolveFile(fileName, _fontDirectory);

        public void Dispose()
        {
            foreach (var f in _fonts.Values) f.Dispose();
            foreach (var t in _typefaces.Values) t.Dispose();
        }
    }
}
