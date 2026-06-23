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
/// The single conversion from Lily#'s internal <b>Y-up</b> layout frame
/// (origin at the page bottom, Y growing upward — matching LilyPond, so
/// <c>lily/*.cc</c> formulas port sign-for-sign) to the device <b>Y-down</b>
/// frame the backends emit (origin top-left). This is the stand-in for
/// LilyPond's stencil-time flip.
/// </summary>
/// <remarks>
/// Unlike a <c>scale(1, -1)</c> group transform — which would vertically
/// mirror every glyph and text run drawn inside it — this decorator performs
/// the flip by <em>coordinate arithmetic</em> (<c>y_device = Height - y_up</c>)
/// at every primitive, so glyphs stay upright. It is the one chokepoint that
/// converts the whole internal Y-up space to device coordinates; wrapping
/// the page's <see cref="IDrawingContext"/> in it is the only place the flip
/// happens.
///
/// Group transforms are <b>conjugated</b> so they compose correctly under the
/// flip. For a Y-up transform <c>(tx, ty, sx, sy)</c> the device-equivalent is
/// <c>(tx, Height - ty - sy*Height, sx, sy)</c> — derived from
/// <c>flip ∘ T_up = T_dev ∘ flip</c>. Note <c>ScaleY</c> stays positive (no
/// mirror), and conjugation composes across nested groups.
/// </remarks>
internal sealed class YFlipDrawingContext : IDrawingContext
{
    private readonly IDrawingContext _inner;
    private readonly double _height;

    public YFlipDrawingContext(IDrawingContext inner, double height)
    {
        _inner = inner;
        _height = height;
    }

    private double F(double y) => _height - y;

    public void DrawLine(double x1, double y1, double x2, double y2,
        Color? stroke = null, double strokeWidth = 0.1,
        (double On, double Off)? dash = null, LineCap cap = LineCap.Butt)
        => _inner.DrawLine(x1, F(y1), x2, F(y2), stroke, strokeWidth, dash, cap);

    public void DrawRectangle(double x, double y, double width, double height,
        Color? fill = null, Color? stroke = null, double strokeWidth = 0)
        // y is the Y-up coordinate of the rectangle's (visually) top edge; the
        // device top edge is Height - y and the rect still extends downward by
        // height, so the device-space rectangle is unchanged in size.
        => _inner.DrawRectangle(x, F(y), width, height, fill, stroke, strokeWidth);

    public void DrawEllipse(double cx, double cy, double rx, double ry,
        Color? fill = null, Color? stroke = null, double strokeWidth = 0)
        => _inner.DrawEllipse(cx, F(cy), rx, ry, fill, stroke, strokeWidth);

    public void DrawCircle(double cx, double cy, double r, Color? fill = null)
        => _inner.DrawCircle(cx, F(cy), r, fill);

    public void DrawClosedBezier(
        (double X, double Y) p0, (double X, double Y) c1, (double X, double Y) c2,
        (double X, double Y) p1, (double X, double Y) c2Back, (double X, double Y) c1Back,
        Color? fill = null)
        => _inner.DrawClosedBezier(
            (p0.X, F(p0.Y)), (c1.X, F(c1.Y)), (c2.X, F(c2.Y)),
            (p1.X, F(p1.Y)), (c2Back.X, F(c2Back.Y)), (c1Back.X, F(c1Back.Y)), fill);

    public void DrawGlyph(char glyph, double x, double y, double fontSize, Color? fill = null)
        => _inner.DrawGlyph(glyph, x, F(y), fontSize, fill);

    public void DrawText(string text, double x, double y, double fontSize,
        string fontFamily, FontStyle style = FontStyle.Regular,
        TextAnchor anchor = TextAnchor.Start, Color? fill = null,
        VerticalAnchor verticalAnchor = VerticalAnchor.Baseline)
        => _inner.DrawText(text, x, F(y), fontSize, fontFamily, style, anchor, fill, verticalAnchor);

    public IDisposable Source(int sourcePosition) => _inner.Source(sourcePosition);

    public IDisposable BeginGroup(DrawingTransform transform)
    {
        // Conjugate the Y-up transform into its device-space equivalent so the
        // backend (which composes in device space) reproduces flip ∘ T_up.
        var deviceTransform = new DrawingTransform(
            TranslateX: transform.TranslateX,
            TranslateY: _height - transform.TranslateY - transform.ScaleY * _height,
            ScaleX: transform.ScaleX,
            ScaleY: transform.ScaleY);
        return _inner.BeginGroup(deviceTransform);
    }
}
