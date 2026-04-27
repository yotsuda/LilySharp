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

namespace LilySharp.Core.Rendering.Pdf;

/// <summary>PDF implementation of <see cref="IDrawingContext"/>.</summary>
internal sealed class PdfDrawingContext : IDrawingContext
{
    private readonly XGraphics _gfx;
    private readonly double _scale;  // points per staff-space

    public PdfDrawingContext(XGraphics gfx, double pointsPerSpace)
    {
        _gfx = gfx;
        _scale = pointsPerSpace;
    }

    private double X(double s) => s * _scale;
    private double T(double s) => s * _scale;
    private static XColor ToXColor(Color? c)
    {
        var col = c ?? Color.Black;
        return XColor.FromArgb(col.A, col.R, col.G, col.B);
    }

    public void DrawLine(double x1, double y1, double x2, double y2,
        Color? stroke = null, double strokeWidth = 0.1)
    {
        var pen = new XPen(ToXColor(stroke), T(strokeWidth));
        _gfx.DrawLine(pen, X(x1), X(y1), X(x2), X(y2));
    }

    public void DrawRectangle(double x, double y, double width, double height,
        Color? fill = null, Color? stroke = null, double strokeWidth = 0)
    {
        var rx = X(x);
        var ry = X(y);
        var rw = T(width);
        var rh = T(height);
        if (fill is { } f && stroke is { } s)
        {
            _gfx.DrawRectangle(new XPen(ToXColor(s), T(strokeWidth)),
                new XSolidBrush(ToXColor(f)), rx, ry, rw, rh);
        }
        else if (fill is { } f2)
        {
            _gfx.DrawRectangle(new XSolidBrush(ToXColor(f2)), rx, ry, rw, rh);
        }
        else if (stroke is { } s2)
        {
            _gfx.DrawRectangle(new XPen(ToXColor(s2), T(strokeWidth)), rx, ry, rw, rh);
        }
    }

    public void DrawEllipse(double cx, double cy, double rx, double ry,
        Color? fill = null, Color? stroke = null, double strokeWidth = 0)
    {
        var x = X(cx - rx);
        var y = X(cy - ry);
        var w = T(rx * 2);
        var h = T(ry * 2);
        if (fill is { } f && stroke is { } s)
        {
            _gfx.DrawEllipse(new XPen(ToXColor(s), T(strokeWidth)),
                new XSolidBrush(ToXColor(f)), x, y, w, h);
        }
        else if (fill is { } f2)
        {
            _gfx.DrawEllipse(new XSolidBrush(ToXColor(f2)), x, y, w, h);
        }
        else if (stroke is { } s2)
        {
            _gfx.DrawEllipse(new XPen(ToXColor(s2), T(strokeWidth)), x, y, w, h);
        }
    }

    public void DrawCircle(double cx, double cy, double r, Color? fill = null)
    {
        _gfx.DrawEllipse(new XSolidBrush(ToXColor(fill)),
            X(cx - r), X(cy - r), T(r * 2), T(r * 2));
    }

    public void DrawClosedBezier(
        (double X, double Y) p0, (double X, double Y) c1, (double X, double Y) c2,
        (double X, double Y) p1, (double X, double Y) c2Back, (double X, double Y) c1Back,
        Color? fill = null)
    {
        var path = new XGraphicsPath();
        path.AddBezier(
            X(p0.X), X(p0.Y),
            X(c1.X), X(c1.Y),
            X(c2.X), X(c2.Y),
            X(p1.X), X(p1.Y));
        path.AddBezier(
            X(p1.X), X(p1.Y),
            X(c2Back.X), X(c2Back.Y),
            X(c1Back.X), X(c1Back.Y),
            X(p0.X), X(p0.Y));
        path.CloseFigure();
        _gfx.DrawPath(new XSolidBrush(ToXColor(fill)), path);
    }

    public void DrawGlyph(char glyph, double x, double y, double fontSize, Color? fill = null)
    {
        var font = new XFont("Emmentaler", T(fontSize));
        // SVG <text y="..."> places the baseline at y. PdfSharpCore's
        // DrawString with TopLeft format places the top of the box at y;
        // BaseLineLeft was removed in PdfSharpCore. We approximate by
        // drawing at (x, y) using BaseLineLeft format if available, else
        // shift to compensate. PdfSharpCore exposes XStringFormats.BaseLineLeft.
        _gfx.DrawString(glyph.ToString(), font,
            new XSolidBrush(ToXColor(fill)),
            X(x), X(y), XStringFormats.BaseLineLeft);
    }

    public void DrawText(string text, double x, double y, double fontSize,
        string fontFamily, FontStyle style = FontStyle.Regular,
        TextAnchor anchor = TextAnchor.Start, Color? fill = null)
    {
        var pdfStyle = ((style & FontStyle.Bold) != 0, (style & FontStyle.Italic) != 0) switch
        {
            (true, true) => XFontStyle.BoldItalic,
            (true, false) => XFontStyle.Bold,
            (false, true) => XFontStyle.Italic,
            _ => XFontStyle.Regular,
        };
        var font = new XFont(fontFamily, T(fontSize), pdfStyle);
        var fmt = anchor switch
        {
            TextAnchor.Middle => XStringFormats.BaseLineCenter,
            TextAnchor.End => XStringFormats.BaseLineRight,
            _ => XStringFormats.BaseLineLeft,
        };
        _gfx.DrawString(text, font, new XSolidBrush(ToXColor(fill)),
            X(x), X(y), fmt);
    }

    public IDisposable Source(int sourcePosition)
    {
        // PDF backend ignores source-position metadata. A future enhancement
        // could emit PDF link annotations or named destinations.
        return NullScope.Instance;
    }

    public IDisposable BeginGroup(DrawingTransform transform)
    {
        var state = _gfx.Save();
        if (!transform.IsIdentity)
        {
            _gfx.TranslateTransform(T(transform.TranslateX), T(transform.TranslateY));
            _gfx.ScaleTransform(transform.ScaleX, transform.ScaleY);
        }
        return new ScopeAction(() => _gfx.Restore(state));
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
}
