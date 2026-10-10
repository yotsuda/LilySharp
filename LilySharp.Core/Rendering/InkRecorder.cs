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

using System;
using System.Collections.Generic;
using LilySharp.Core.Svg.Layout;

namespace LilySharp.Core.Rendering;

/// <summary>
/// A decorator over a page's DEVICE context that forwards every primitive and remembers,
/// as a box in the page's Y-up frame (the margin group's: X right of the left margin, Y up
/// from the page's bottom), where ink was put — so the preview's <c>@todo</c> memos can be
/// set where nothing else is (<c>SharedRenderer.DrawTodoMarks</c>, owner's request
/// 2026-10-10: a memo over another mark cannot be read, red or not). The boxes are the
/// primitives' bounding boxes: a glyph's is its design box at the drawn size, a text's its
/// ink from the score's metrics, a curve's the hull of its control points, a line's its
/// end points widened by its pen. Nested groups' transforms (the margin, a system's own
/// frame, an ossia's scale) are tracked, so every box is where the page shows the ink.
/// </summary>
/// <remarks>
/// Wrapped in only when a page has memos to place and the backend shows the editor's marks
/// (the interactive SVG), and with the system fragment memo set aside for that render — a
/// replayed system writes text, not primitives, and would leave its ink unseen. An export
/// never wraps.
/// LILYSHARP-OWN: an editor's aid with no LilyPond counterpart — LilyPond has no marks to place.
/// </remarks>
internal sealed class InkRecorder : IDrawingContext
{
    private readonly IDrawingContext _inner;
    private readonly ScoreTextMetrics _fonts;
    private readonly double _pageHeight;
    private readonly double _marginLeft;
    private readonly List<(double Left, double Right, double Bottom, double Top)> _boxes = new();
    private readonly Stack<(double Tx, double Ty, double Sx, double Sy)> _transforms = new();
    private int _suspended;

    public InkRecorder(IDrawingContext inner, ScoreTextMetrics fonts, double pageHeight, double marginLeft)
    {
        _inner = inner;
        _fonts = fonts;
        _pageHeight = pageHeight;
        _marginLeft = marginLeft;
        _transforms.Push((0, 0, 1, 1));
    }

    /// <summary>How many boxes have been put down so far.</summary>
    public int Count => _boxes.Count;

    /// <summary>The page's height in staff spaces — the top of the page frame's Y-up range,
    /// past which a memo would be cut off.</summary>
    public double PageHeight => _pageHeight;

    /// <summary>Stops recording until the token is disposed — for the memos' own leaders,
    /// whose long slanted boxes would fence off the margin they cross.</summary>
    public IDisposable Suspend()
    {
        _suspended++;
        return new Resume(this);
    }

    private sealed class Resume(InkRecorder owner) : IDisposable
    {
        public void Dispose() => owner._suspended--;
    }

    /// <summary>Whether the page-frame box <c>[left, right] × [bottom, top]</c> (Y-up) is clear
    /// of every box put down so far.</summary>
    public bool IsClear(double left, double right, double bottom, double top)
    {
        foreach (var b in _boxes)
            if (b.Left < right && left < b.Right && b.Bottom < top && bottom < b.Top)
                return false;
        return true;
    }

    // ---- recording ----

    private (double X, double Y) Map(double x, double y)
    {
        var (tx, ty, sx, sy) = _transforms.Peek();
        return (tx + sx * x, ty + sy * y);
    }

    /// <summary>Records a device-frame box (Y down), converting to the page frame.</summary>
    private void Put(double x0, double y0, double x1, double y1)
    {
        if (_suspended > 0)
            return;
        var (ax, ay) = Map(x0, y0);
        var (bx, by) = Map(x1, y1);
        double left = Math.Min(ax, bx) - _marginLeft, right = Math.Max(ax, bx) - _marginLeft;
        double top = _pageHeight - Math.Min(ay, by), bottom = _pageHeight - Math.Max(ay, by);
        if (right - left < 1e-9 && top - bottom < 1e-9)
            return;
        _boxes.Add((left, right, bottom, top));
    }

    private void PutPoints(double pen, params (double X, double Y)[] points)
    {
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        foreach (var (x, y) in points)
        {
            x0 = Math.Min(x0, x); y0 = Math.Min(y0, y);
            x1 = Math.Max(x1, x); y1 = Math.Max(y1, y);
        }
        double half = pen / 2;
        Put(x0 - half, y0 - half, x1 + half, y1 + half);
    }

    private void PutGlyph(char glyph, double x, double y, double fontSize)
    {
        // The glyph's design box at the drawn size (the frame here is Y down, so the box's
        // top goes up the page as a smaller Y); an unknown character gets a token box.
        var font = MusicFont.Current;
        double scale = fontSize / SharedRenderer.FontSize;
        if (font.GlyphOf(glyph) is { } g)
        {
            var box = font.FullSize.Box(g);
            Put(x + box.Left * scale, y - box.Top * scale, x + box.Right * scale, y - box.Bottom * scale);
        }
        else
            Put(x, y - 0.5 * scale, x + 1.0 * scale, y + 0.5 * scale);
    }

    public void DrawLine(double x1, double y1, double x2, double y2,
        Color? stroke = null, double strokeWidth = 0.1,
        (double On, double Off)? dash = null, LineCap cap = LineCap.Butt)
    {
        PutPoints(strokeWidth, (x1, y1), (x2, y2));
        _inner.DrawLine(x1, y1, x2, y2, stroke, strokeWidth, dash, cap);
    }

    public void DrawRectangle(double x, double y, double width, double height,
        Color? fill = null, Color? stroke = null, double strokeWidth = 0)
    {
        Put(x, y, x + width, y + height);
        _inner.DrawRectangle(x, y, width, height, fill, stroke, strokeWidth);
    }

    public void DrawFilledQuad((double X, double Y) p0, (double X, double Y) p1,
        (double X, double Y) p2, (double X, double Y) p3, Color fill)
    {
        PutPoints(0, p0, p1, p2, p3);
        _inner.DrawFilledQuad(p0, p1, p2, p3, fill);
    }

    public void DrawEllipse(double cx, double cy, double rx, double ry,
        Color? fill = null, Color? stroke = null, double strokeWidth = 0)
    {
        Put(cx - rx - strokeWidth / 2, cy - ry - strokeWidth / 2, cx + rx + strokeWidth / 2, cy + ry + strokeWidth / 2);
        _inner.DrawEllipse(cx, cy, rx, ry, fill, stroke, strokeWidth);
    }

    public void DrawCircle(double cx, double cy, double r, Color? fill = null)
    {
        Put(cx - r, cy - r, cx + r, cy + r);
        _inner.DrawCircle(cx, cy, r, fill);
    }

    public void DrawClosedBezier((double X, double Y) p0, (double X, double Y) c1, (double X, double Y) c2,
        (double X, double Y) p1, (double X, double Y) c2Back, (double X, double Y) c1Back,
        Color? fill = null, double strokeWidth = 0)
    {
        PutPoints(strokeWidth, p0, c1, c2, p1, c2Back, c1Back);
        _inner.DrawClosedBezier(p0, c1, c2, p1, c2Back, c1Back, fill, strokeWidth);
    }

    public void DrawBezier((double X, double Y) p0, (double X, double Y) c1, (double X, double Y) c2,
        (double X, double Y) p1, Color? stroke = null, double strokeWidth = 0.1)
    {
        PutPoints(strokeWidth, p0, c1, c2, p1);
        _inner.DrawBezier(p0, c1, c2, p1, stroke, strokeWidth);
    }

    public void DrawGlyph(char glyph, double x, double y, double fontSize, Color? fill = null)
    {
        PutGlyph(glyph, x, y, fontSize);
        _inner.DrawGlyph(glyph, x, y, fontSize, fill);
    }

    public void DrawGlyphTurned(char glyph, double x, double y, double fontSize, Color? fill = null)
    {
        PutGlyph(glyph, x, y, fontSize);
        _inner.DrawGlyphTurned(glyph, x, y, fontSize, fill);
    }

    public void DrawNotehead(char glyph, double x, double y, double fontSize, Color? fill,
        double inkWidth, double inkHeight)
    {
        Put(x, y - inkHeight / 2, x + inkWidth, y + inkHeight / 2);
        _inner.DrawNotehead(glyph, x, y, fontSize, fill, inkWidth, inkHeight);
    }

    public void DrawAttachedGlyph(char glyph, double x, double y, double fontSize, Color? fill = null)
    {
        PutGlyph(glyph, x, y, fontSize);
        _inner.DrawAttachedGlyph(glyph, x, y, fontSize, fill);
    }

    public void DrawText(string text, double x, double y, double fontSize,
        TextRole role, FontStyle style = FontStyle.Regular,
        TextAnchor anchor = TextAnchor.Start, Color? fill = null,
        VerticalAnchor verticalAnchor = VerticalAnchor.Baseline)
    {
        if (text.Length > 0)
        {
            double advance = _fonts.Advance(text, fontSize, role, style);
            double left = anchor switch
            {
                TextAnchor.Middle => x - advance / 2,
                TextAnchor.End => x - advance,
                _ => x,
            };
            var (bottom, top) = _fonts.Ink(text, fontSize, role, style);
            // The frame is Y down: ink above the baseline is a smaller Y. A text anchored
            // other than at its baseline is read as if it were — a rough box is all the
            // memos need.
            Put(left, y - top, left + advance, y - bottom);
        }
        _inner.DrawText(text, x, y, fontSize, role, style, anchor, fill, verticalAnchor);
    }

    public IDisposable Source(int sourcePosition) => _inner.Source(sourcePosition);

    public IDisposable Source(int sourcePosition, IReadOnlyList<int> aliases) => _inner.Source(sourcePosition, aliases);

    public IDisposable Todo(string key) => _inner.Todo(key);

    public bool DrawsEditorMarks => _inner.DrawsEditorMarks;

    public IDisposable Kind(string kind) => _inner.Kind(kind);

    public IDisposable Staff(int staffIndex) => _inner.Staff(staffIndex);

    public IDisposable MusicFace(int rounded) => _inner.MusicFace(rounded);

    public void DrawHitRect(double x, double y, double width, double height) => _inner.DrawHitRect(x, y, width, height);

    public void DrawBarBox(int barNumber, double x, double y, double width, double height)
        => _inner.DrawBarBox(barNumber, x, y, width, height);

    public IDisposable BeginGroup(DrawingTransform transform)
    {
        var (tx, ty, sx, sy) = _transforms.Peek();
        _transforms.Push((tx + sx * transform.TranslateX, ty + sy * transform.TranslateY,
            sx * transform.ScaleX, sy * transform.ScaleY));
        return new GroupEnd(this, _inner.BeginGroup(transform));
    }

    private sealed class GroupEnd(InkRecorder owner, IDisposable inner) : IDisposable
    {
        public void Dispose()
        {
            owner._transforms.Pop();
            inner.Dispose();
        }
    }

    public IDisposable BeginLabeledGroup(string label) => _inner.BeginLabeledGroup(label);
}
