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
/// Backend-agnostic drawing surface for music notation. All coordinates,
/// thicknesses, and font sizes are expressed in <em>staff-spaces</em>
/// (the same unit Lily#'s layout engine uses). The implementation is
/// responsible for converting to its native output unit (SVG user units,
/// PDF points, etc.) at emit time.
/// </summary>
/// <remarks>
/// Origin is top-left, Y axis points downward (matches both SVG and PDF
/// page coordinates).
///
/// Color parameters accept <c>null</c> to mean "use renderer default"
/// (typically opaque black for fills, transparent for strokes).
/// </remarks>
public interface IDrawingContext
{
    void DrawLine(
        double x1, double y1, double x2, double y2,
        Color? stroke = null, double strokeWidth = 0.1,
        (double On, double Off)? dash = null);

    void DrawRectangle(
        double x, double y, double width, double height,
        Color? fill = null, Color? stroke = null, double strokeWidth = 0);

    void DrawEllipse(
        double cx, double cy, double rx, double ry,
        Color? fill = null, Color? stroke = null, double strokeWidth = 0);

    void DrawCircle(
        double cx, double cy, double r,
        Color? fill = null);

    /// <summary>
    /// Closed cubic Bézier path (used for ties and slurs). The path goes
    /// outward from <paramref name="p0"/> through control points
    /// <paramref name="c1"/>, <paramref name="c2"/> to <paramref name="p1"/>,
    /// then back through <paramref name="c2Back"/>, <paramref name="c1Back"/>
    /// to close.
    /// </summary>
    void DrawClosedBezier(
        (double X, double Y) p0,
        (double X, double Y) c1,
        (double X, double Y) c2,
        (double X, double Y) p1,
        (double X, double Y) c2Back,
        (double X, double Y) c1Back,
        Color? fill = null);

    /// <summary>
    /// Draws a music-font glyph (Emmentaler) at the given baseline anchor.
    /// </summary>
    /// <param name="glyph">SMuFL Unicode codepoint (e.g.
    /// <c>EmmentalerGlyphs.GClef</c>).</param>
    /// <param name="x">X position of the glyph anchor (staff-spaces).</param>
    /// <param name="y">Y position of the glyph anchor (staff-spaces).</param>
    /// <param name="fontSize">Font size in staff-spaces (typically 4.0).</param>
    void DrawGlyph(
        char glyph, double x, double y, double fontSize,
        Color? fill = null);

    /// <summary>Draws plain (non-music) text such as titles, dynamics, lyrics.</summary>
    void DrawText(
        string text, double x, double y, double fontSize,
        string fontFamily, FontStyle style = FontStyle.Regular,
        TextAnchor anchor = TextAnchor.Start, Color? fill = null);

    /// <summary>
    /// Tags subsequent draw operations (until <see cref="IDisposable.Dispose"/>)
    /// with a source-text position for click-to-source mapping. SVG backends
    /// emit <c>data-pos</c> attributes; backends that don't support metadata
    /// may ignore the call.
    /// </summary>
    IDisposable Source(int sourcePosition);

    /// <summary>
    /// Begins a transformed group scope. Subsequent draw operations have the
    /// transform applied (in document order, after any enclosing groups).
    /// Dispose the returned token to end the scope.
    /// </summary>
    IDisposable BeginGroup(DrawingTransform transform);
}
