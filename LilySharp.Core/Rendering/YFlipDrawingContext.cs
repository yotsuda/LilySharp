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

using System.Collections.Generic;

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
///
/// A width/height (<c>DrawRectangle</c>, <c>DrawHitRect</c>) or an ink extent
/// (<c>DrawNotehead</c>) is a SIZE, not a coordinate, so it passes through
/// unflipped; only the anchor Y flips. Sizes/anchors that are forwarded to the
/// inner context keep the inner's backend behaviour (e.g. interactive SVG hit
/// rects), which a bare <c>DrawGlyph</c> fallback would lose.
///
/// This is WIRED: <see cref="SharedRenderer"/> wraps each page's
/// <see cref="IDrawingContext"/> in it (see <c>RenderTo</c>), so every primitive
/// Y the renderer emits is page Y-up and this decorator performs the one device
/// flip. The internal-Y-up migration is complete for grob geometry and the render
/// path; the remaining device islands — the staff/system stacking origins and the
/// shared collision/skyline substrate — are being folded into this same flip.
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

    public void DrawFilledQuad(
        (double X, double Y) p0, (double X, double Y) p1,
        (double X, double Y) p2, (double X, double Y) p3,
        Color fill)
        => _inner.DrawFilledQuad(
            (p0.X, F(p0.Y)), (p1.X, F(p1.Y)), (p2.X, F(p2.Y)), (p3.X, F(p3.Y)), fill);

    public void DrawEllipse(double cx, double cy, double rx, double ry,
        Color? fill = null, Color? stroke = null, double strokeWidth = 0)
        => _inner.DrawEllipse(cx, F(cy), rx, ry, fill, stroke, strokeWidth);

    public void DrawCircle(double cx, double cy, double r, Color? fill = null)
        => _inner.DrawCircle(cx, F(cy), r, fill);

    public void DrawClosedBezier(
        (double X, double Y) p0, (double X, double Y) c1, (double X, double Y) c2,
        (double X, double Y) p1, (double X, double Y) c2Back, (double X, double Y) c1Back,
        Color? fill = null, double strokeWidth = 0)
        => _inner.DrawClosedBezier(
            (p0.X, F(p0.Y)), (c1.X, F(c1.Y)), (c2.X, F(c2.Y)),
            (p1.X, F(p1.Y)), (c2Back.X, F(c2Back.Y)), (c1Back.X, F(c1Back.Y)), fill, strokeWidth);

    public void DrawGlyph(char glyph, double x, double y, double fontSize, Color? fill = null)
        => _inner.DrawGlyph(glyph, x, F(y), fontSize, fill);

    public void DrawNotehead(char glyph, double x, double y, double fontSize, Color? fill,
        double inkWidth, double inkHeight)
        // inkWidth/inkHeight are sizes (the hit rect stays centred on the anchor),
        // so only the anchor Y flips; forward to keep the inner's hit-rect behaviour.
        => _inner.DrawNotehead(glyph, x, F(y), fontSize, fill, inkWidth, inkHeight);

    public void DrawAttachedGlyph(char glyph, double x, double y, double fontSize, Color? fill = null)
        => _inner.DrawAttachedGlyph(glyph, x, F(y), fontSize, fill);

    public void DrawText(string text, double x, double y, double fontSize,
        TextRole role, FontStyle style = FontStyle.Regular,
        TextAnchor anchor = TextAnchor.Start, Color? fill = null,
        VerticalAnchor verticalAnchor = VerticalAnchor.Baseline)
        => _inner.DrawText(text, x, F(y), fontSize, role, style, anchor, fill, verticalAnchor);

    public IDisposable Source(int sourcePosition) => _inner.Source(sourcePosition);

    public IDisposable Source(int sourcePosition, IReadOnlyList<int> aliases)
        => _inner.Source(sourcePosition, aliases);

    // Forwarded, NOT left to the interface default: the default is a no-op, so a decorated
    // backend would silently keep drawing graces from the 20 face.
    public IDisposable MusicFace(int rounded) => _inner.MusicFace(rounded);

    public void DrawHitRect(double x, double y, double width, double height)
        // y is the Y-up top edge; height is a size (extends downward), unchanged.
        => _inner.DrawHitRect(x, F(y), width, height);

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
