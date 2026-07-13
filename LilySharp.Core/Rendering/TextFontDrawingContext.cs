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

namespace LilySharp.Core.Rendering;

/// <summary>
/// Remaps the text font-family of every non-music <see cref="DrawText"/> call to a
/// configured font, implementing the score's <c>font "NAME"</c> header directive.
/// Wraps an inner <see cref="IDrawingContext"/> and delegates everything UNCHANGED
/// except <see cref="DrawText"/>, where a GENERIC family ("serif", "sans-serif",
/// "sans", "monospace", "mono") is swapped for the configured name; a family that is
/// already specific passes through untouched.
/// </summary>
/// <remarks>
/// Music glyphs (Emmentaler) are drawn via <see cref="DrawGlyph"/> /
/// <see cref="DrawNotehead"/> / <see cref="DrawAttachedGlyph"/> — NOT text — so they
/// are delegated straight through and keep the music font. This decorator only
/// references the font as a plain family name; embedding is a later phase.
/// </remarks>
internal sealed class TextFontDrawingContext : IDrawingContext
{
    private readonly IDrawingContext _inner;
    private readonly string _fontName;

    public TextFontDrawingContext(IDrawingContext inner, string fontName)
    {
        _inner = inner;
        _fontName = fontName;
    }

    // Generic CSS/SVG families that a `font "NAME"` directive replaces with the
    // configured face; a caller-supplied specific family is left as-is.
    private static bool IsGenericFamily(string family) => family switch
    {
        _ when family.Equals("serif", StringComparison.OrdinalIgnoreCase) => true,
        _ when family.Equals("sans-serif", StringComparison.OrdinalIgnoreCase) => true,
        _ when family.Equals("sans", StringComparison.OrdinalIgnoreCase) => true,
        _ when family.Equals("monospace", StringComparison.OrdinalIgnoreCase) => true,
        _ when family.Equals("mono", StringComparison.OrdinalIgnoreCase) => true,
        _ => false,
    };

    public void DrawLine(
        double x1, double y1, double x2, double y2,
        Color? stroke = null, double strokeWidth = 0.1,
        (double On, double Off)? dash = null,
        LineCap cap = LineCap.Butt)
        => _inner.DrawLine(x1, y1, x2, y2, stroke, strokeWidth, dash, cap);

    public void DrawRectangle(
        double x, double y, double width, double height,
        Color? fill = null, Color? stroke = null, double strokeWidth = 0)
        => _inner.DrawRectangle(x, y, width, height, fill, stroke, strokeWidth);

    public void DrawFilledQuad((double X, double Y) p0, (double X, double Y) p1,
        (double X, double Y) p2, (double X, double Y) p3, Color fill)
        => _inner.DrawFilledQuad(p0, p1, p2, p3, fill);

    public void DrawEllipse(
        double cx, double cy, double rx, double ry,
        Color? fill = null, Color? stroke = null, double strokeWidth = 0)
        => _inner.DrawEllipse(cx, cy, rx, ry, fill, stroke, strokeWidth);

    public void DrawCircle(double cx, double cy, double r, Color? fill = null)
        => _inner.DrawCircle(cx, cy, r, fill);

    public void DrawClosedBezier(
        (double X, double Y) p0,
        (double X, double Y) c1,
        (double X, double Y) c2,
        (double X, double Y) p1,
        (double X, double Y) c2Back,
        (double X, double Y) c1Back,
        Color? fill = null,
        double strokeWidth = 0)
        => _inner.DrawClosedBezier(p0, c1, c2, p1, c2Back, c1Back, fill, strokeWidth);

    // Music glyphs (Emmentaler) — not text, delegated unchanged. Explicit overrides
    // (not the interface defaults) so the inner backend's own DrawNotehead /
    // DrawAttachedGlyph behaviour — e.g. interactive SVG click targets — is preserved.
    public void DrawGlyph(char glyph, double x, double y, double fontSize, Color? fill = null)
        => _inner.DrawGlyph(glyph, x, y, fontSize, fill);

    public void DrawNotehead(
        char glyph, double x, double y, double fontSize, Color? fill,
        double inkWidth, double inkHeight)
        => _inner.DrawNotehead(glyph, x, y, fontSize, fill, inkWidth, inkHeight);

    public void DrawAttachedGlyph(char glyph, double x, double y, double fontSize, Color? fill = null)
        => _inner.DrawAttachedGlyph(glyph, x, y, fontSize, fill);

    public void DrawText(
        string text, double x, double y, double fontSize,
        string fontFamily, FontStyle style = FontStyle.Regular,
        TextAnchor anchor = TextAnchor.Start, Color? fill = null,
        VerticalAnchor verticalAnchor = VerticalAnchor.Baseline)
        => _inner.DrawText(text, x, y, fontSize,
            IsGenericFamily(fontFamily) ? _fontName : fontFamily,
            style, anchor, fill, verticalAnchor);

    public IDisposable Source(int sourcePosition) => _inner.Source(sourcePosition);

    public IDisposable BeginGroup(DrawingTransform transform) => _inner.BeginGroup(transform);
}
