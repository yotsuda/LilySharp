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

using System.Globalization;
using System.Text;

namespace LilySharp.Core.Rendering.Svg;

/// <summary>SVG implementation of <see cref="IDrawingContext"/>.</summary>
internal sealed class SvgDrawingContext : IDrawingContext
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private readonly StringBuilder _sb;
    private int? _currentSourcePosition;
    private int _groupDepth;

    public SvgDrawingContext(StringBuilder sb)
    {
        _sb = sb;
    }

    public void DrawLine(double x1, double y1, double x2, double y2,
        Color? stroke = null, double strokeWidth = 0.1,
        (double On, double Off)? dash = null)
    {
        var color = (stroke ?? Color.Black).ToHex();
        var dashAttr = dash is { } d
            ? string.Format(Inv, " stroke-dasharray=\"{0:F2} {1:F2}\"", d.On, d.Off)
            : "";
        _sb.AppendLine(string.Format(Inv,
            "  <line x1=\"{0:F2}\" y1=\"{1:F2}\" x2=\"{2:F2}\" y2=\"{3:F2}\" stroke=\"{4}\" stroke-width=\"{5:F3}\"{6}{7}/>",
            x1, y1, x2, y2, color, strokeWidth, dashAttr, SourceAttr()));
    }

    public void DrawRectangle(double x, double y, double width, double height,
        Color? fill = null, Color? stroke = null, double strokeWidth = 0)
    {
        var attrs = new StringBuilder(96);
        attrs.AppendFormat(Inv, " x=\"{0:F2}\" y=\"{1:F2}\" width=\"{2:F2}\" height=\"{3:F2}\"", x, y, width, height);
        if (fill is { } f)
            attrs.AppendFormat(Inv, " fill=\"{0}\"", f.ToHex());
        else
            attrs.Append(" fill=\"none\"");
        if (stroke is { } s)
            attrs.AppendFormat(Inv, " stroke=\"{0}\" stroke-width=\"{1:F3}\"", s.ToHex(), strokeWidth);
        attrs.Append(SourceAttr());
        _sb.AppendLine($"  <rect{attrs}/>");
    }

    public void DrawEllipse(double cx, double cy, double rx, double ry,
        Color? fill = null, Color? stroke = null, double strokeWidth = 0)
    {
        var attrs = new StringBuilder(96);
        attrs.AppendFormat(Inv, " cx=\"{0:F2}\" cy=\"{1:F2}\" rx=\"{2:F2}\" ry=\"{3:F2}\"", cx, cy, rx, ry);
        if (fill is { } f)
            attrs.AppendFormat(Inv, " fill=\"{0}\"", f.ToHex());
        else
            attrs.Append(" fill=\"none\"");
        if (stroke is { } s)
            attrs.AppendFormat(Inv, " stroke=\"{0}\" stroke-width=\"{1:F3}\"", s.ToHex(), strokeWidth);
        attrs.Append(SourceAttr());
        _sb.AppendLine($"  <ellipse{attrs}/>");
    }

    public void DrawCircle(double cx, double cy, double r, Color? fill = null)
    {
        var color = (fill ?? Color.Black).ToHex();
        _sb.AppendLine(string.Format(Inv,
            "  <circle cx=\"{0:F2}\" cy=\"{1:F2}\" r=\"{2:F2}\" fill=\"{3}\"{4}/>",
            cx, cy, r, color, SourceAttr()));
    }

    public void DrawClosedBezier(
        (double X, double Y) p0, (double X, double Y) c1, (double X, double Y) c2,
        (double X, double Y) p1, (double X, double Y) c2Back, (double X, double Y) c1Back,
        Color? fill = null)
    {
        var color = (fill ?? Color.Black).ToHex();
        var d = string.Format(Inv,
            "M {0:F2},{1:F2} C {2:F2},{3:F2} {4:F2},{5:F2} {6:F2},{7:F2} C {8:F2},{9:F2} {10:F2},{11:F2} {0:F2},{1:F2} Z",
            p0.X, p0.Y, c1.X, c1.Y, c2.X, c2.Y, p1.X, p1.Y, c2Back.X, c2Back.Y, c1Back.X, c1Back.Y);
        _sb.AppendLine($"  <path d=\"{d}\" fill=\"{color}\"{SourceAttr()}/>");
    }

    public void DrawGlyph(char glyph, double x, double y, double fontSize, Color? fill = null)
    {
        var fillAttr = fill is { } f ? string.Format(Inv, " fill=\"{0}\"", f.ToHex()) : "";
        _sb.AppendLine(string.Format(Inv,
            "  <text class=\"music\" x=\"{0:F2}\" y=\"{1:F2}\" font-size=\"{2:F2}\"{3}{4}>{5}</text>",
            x, y, fontSize, fillAttr, SourceAttr(), Escape(glyph)));
    }

    public void DrawText(string text, double x, double y, double fontSize,
        string fontFamily, FontStyle style = FontStyle.Regular,
        TextAnchor anchor = TextAnchor.Start, Color? fill = null,
        VerticalAnchor verticalAnchor = VerticalAnchor.Baseline)
    {
        var attrs = new StringBuilder(128);
        attrs.AppendFormat(Inv, " x=\"{0:F2}\" y=\"{1:F2}\" font-size=\"{2:F2}\" font-family=\"{3}\"",
            x, y, fontSize, EscapeAttr(fontFamily));
        if ((style & FontStyle.Bold) != 0)
            attrs.Append(" font-weight=\"bold\"");
        if ((style & FontStyle.Italic) != 0)
            attrs.Append(" font-style=\"italic\"");
        if (anchor != TextAnchor.Start)
            attrs.Append(anchor == TextAnchor.Middle ? " text-anchor=\"middle\"" : " text-anchor=\"end\"");
        if (verticalAnchor != VerticalAnchor.Baseline)
            attrs.Append(verticalAnchor == VerticalAnchor.Middle
                ? " dominant-baseline=\"central\""
                : " dominant-baseline=\"hanging\"");
        if (fill is { } f)
            attrs.AppendFormat(Inv, " fill=\"{0}\"", f.ToHex());
        attrs.Append(SourceAttr());
        _sb.AppendLine($"  <text{attrs}>{EscapeText(text)}</text>");
    }

    public IDisposable Source(int sourcePosition)
    {
        var prev = _currentSourcePosition;
        _currentSourcePosition = sourcePosition;
        return new ScopeAction(() => _currentSourcePosition = prev);
    }

    public IDisposable BeginGroup(DrawingTransform transform)
    {
        if (transform.IsIdentity)
        {
            _sb.AppendLine("  <g>");
        }
        else
        {
            var ts = string.Format(Inv,
                "translate({0:F2},{1:F2}) scale({2:F4},{3:F4})",
                transform.TranslateX, transform.TranslateY, transform.ScaleX, transform.ScaleY);
            _sb.AppendLine($"  <g transform=\"{ts}\">");
        }
        _groupDepth++;
        return new ScopeAction(() =>
        {
            _sb.AppendLine("  </g>");
            _groupDepth--;
        });
    }

    private string SourceAttr() =>
        _currentSourcePosition.HasValue
            ? string.Format(Inv, " data-pos=\"{0}\"", _currentSourcePosition.Value)
            : "";

    private static string Escape(char c) => c switch
    {
        '<' => "&lt;", '>' => "&gt;", '&' => "&amp;", _ => c.ToString()
    };

    private static string EscapeText(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static string EscapeAttr(string s) => EscapeText(s).Replace("\"", "&quot;");

    private sealed class ScopeAction : IDisposable
    {
        private Action? _action;
        public ScopeAction(Action a) { _action = a; }
        public void Dispose() { _action?.Invoke(); _action = null; }
    }
}
