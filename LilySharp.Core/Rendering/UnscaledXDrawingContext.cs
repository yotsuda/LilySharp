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
/// Pre-divides every horizontal coordinate by <c>scaleX</c> so that an
/// ENCLOSING uniform scale group leaves X positions unchanged: glyphs and
/// vertical geometry come out scaled (the group's transform), while X lands
/// exactly where the caller said.
/// </summary>
/// <remarks>
/// This is how an ossia/magnified staff stays aligned with the score's shared
/// spacing columns: in LilyPond one paper column per moment spans EVERY staff
/// and the spacing is solved score-wide, so a magnified staff's notes sit at
/// the same X as the full-size staff below — only the notation shrinks.
/// LILYPOND-REF: lily/paper-column.cc, lily/spacing-spanner.cc (score-wide
/// columns); ly/music-functions-init.ly magnifyStaff (fontSize + staff-space —
/// per-staff SIZE, not per-staff horizontal spacing).
/// </remarks>
internal sealed class UnscaledXDrawingContext : IDrawingContext
{
    private readonly IDrawingContext _inner;
    private readonly double _invScaleX;

    public UnscaledXDrawingContext(IDrawingContext inner, double scaleX)
    {
        _inner = inner;
        _invScaleX = 1.0 / scaleX;
    }

    private double X(double x) => x * _invScaleX;
    private (double X, double Y) P((double X, double Y) p) => (p.X * _invScaleX, p.Y);

    public void DrawLine(
        double x1, double y1, double x2, double y2,
        Color? stroke = null, double strokeWidth = 0.1,
        (double On, double Off)? dash = null,
        LineCap cap = LineCap.Butt)
        => _inner.DrawLine(X(x1), y1, X(x2), y2, stroke, strokeWidth, dash, cap);

    public void DrawRectangle(
        double x, double y, double width, double height,
        Color? fill = null, Color? stroke = null, double strokeWidth = 0)
        => _inner.DrawRectangle(X(x), y, width * _invScaleX, height, fill, stroke, strokeWidth);

    public void DrawEllipse(
        double cx, double cy, double rx, double ry,
        Color? fill = null, Color? stroke = null, double strokeWidth = 0)
        => _inner.DrawEllipse(X(cx), cy, rx * _invScaleX, ry, fill, stroke, strokeWidth);

    // A circle's RADIUS stays as given (the enclosing uniform scale shrinks it
    // like a glyph); only its centre X is position-compensated.
    public void DrawCircle(double cx, double cy, double r, Color? fill = null)
        => _inner.DrawCircle(X(cx), cy, r, fill);

    public void DrawClosedBezier(
        (double X, double Y) p0,
        (double X, double Y) c1,
        (double X, double Y) c2,
        (double X, double Y) p1,
        (double X, double Y) c2Back,
        (double X, double Y) c1Back,
        Color? fill = null,
        double strokeWidth = 0)
        => _inner.DrawClosedBezier(P(p0), P(c1), P(c2), P(p1), P(c2Back), P(c1Back), fill, strokeWidth);

    public void DrawGlyph(char glyph, double x, double y, double fontSize, Color? fill = null)
        => _inner.DrawGlyph(glyph, X(x), y, fontSize, fill);

    public void DrawText(
        string text, double x, double y, double fontSize,
        string fontFamily, FontStyle style = FontStyle.Regular,
        TextAnchor anchor = TextAnchor.Start, Color? fill = null,
        VerticalAnchor verticalAnchor = VerticalAnchor.Baseline)
        => _inner.DrawText(text, X(x), y, fontSize, fontFamily, style, anchor, fill, verticalAnchor);

    public IDisposable Source(int sourcePosition) => _inner.Source(sourcePosition);

    // A nested group's X translation is a POSITION and gets the same
    // compensation; its own scale factors pass through untouched.
    public IDisposable BeginGroup(DrawingTransform transform)
        => _inner.BeginGroup(transform with { TranslateX = X(transform.TranslateX) });
}
