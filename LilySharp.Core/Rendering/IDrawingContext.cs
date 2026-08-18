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
/// How the ends of a stroked line are shaped.
/// </summary>
public enum LineCap
{
    /// <summary>Squared off exactly at the endpoint (SVG default).</summary>
    Butt,
    /// <summary>Rounded end of radius = half the stroke width.</summary>
    Round,
}

/// <summary>
/// Backend-agnostic drawing surface for music notation. All coordinates,
/// thicknesses, and font sizes are expressed in <em>staff-spaces</em>
/// (the same unit Lily#'s layout engine uses). The implementation is
/// responsible for converting to its native output unit (SVG user units,
/// PDF points, etc.) at emit time.
/// </summary>
/// <remarks>
/// ⚠️ THIS TYPE CARRIES TWO Y FRAMES, and which one you are in depends on where you sit
/// relative to <see cref="YFlipDrawingContext"/>:
/// <list type="bullet">
/// <item><b>Before the flip</b> — everything the renderer draws through the context
/// <c>SharedRenderer</c> hands out (SharedRenderer.cs:99 wraps each page) is in <b>page
/// Y-up</b>: origin at the page's BOTTOM-left, Y growing upward, the frame the layout
/// engine works in.</item>
/// <item><b>After the flip</b> — what <c>YFlipDrawingContext</c> passes to the backend
/// (SVG, PDF, Skia) is <b>device Y-down</b>: origin top-left, Y growing downward, which is
/// what both SVG and PDF page coordinates want.</item>
/// </list>
/// One type spanning two frames is a sign-error smell, and it is deliberate rather than
/// accidental: the flip exists in exactly ONE place so that no other code has to know
/// about it (docs/COORDINATE_AUDIT.md §3.G, §4.4). An implementor of this interface is a
/// BACKEND and therefore lives after the flip; a caller of it is a renderer and lives
/// before it. Anything that both implements and calls — a decorator like
/// <c>UnscaledXDrawingContext</c> — stays in whichever frame it was handed and must not
/// convert.
///
/// X is unaffected: origin at the left edge, growing rightward, in both frames.
///
/// Color parameters accept <c>null</c> to mean "use renderer default"
/// (typically opaque black for fills, transparent for strokes).
/// </remarks>
public interface IDrawingContext
{
    /// <summary>Draws a straight (optionally dashed) line between two points.</summary>
    void DrawLine(
        double x1, double y1, double x2, double y2,
        Color? stroke = null, double strokeWidth = 0.1,
        (double On, double Off)? dash = null,
        LineCap cap = LineCap.Butt);

    /// <summary>Draws an axis-aligned rectangle with optional fill and stroke.</summary>
    void DrawRectangle(
        double x, double y, double width, double height,
        Color? fill = null, Color? stroke = null, double strokeWidth = 0);

    /// <summary>Fills the quadrilateral p0→p1→p2→p3 (four straight edges). Used for a
    /// beam, whose ends are VERTICAL even when the body slopes — a plain thick line
    /// would cap its ends perpendicular to the slope and leave triangles past the
    /// stems. LILYPOND-REF: lily/lookup.cc Lookup::beam.</summary>
    void DrawFilledQuad(
        (double X, double Y) p0, (double X, double Y) p1,
        (double X, double Y) p2, (double X, double Y) p3,
        Color fill);

    /// <summary>Draws an axis-aligned ellipse with optional fill and stroke.</summary>
    void DrawEllipse(
        double cx, double cy, double rx, double ry,
        Color? fill = null, Color? stroke = null, double strokeWidth = 0);

    /// <summary>Draws a filled circle of the given radius.</summary>
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
    /// <param name="strokeWidth">When &gt; 0, the filled path is also stroked with a
    /// ROUND cap/join pen of this width in the fill colour — LilyPond strokes its
    /// slur/tie stencil this way so the tapered ends read as rounded, not sharp
    /// points.</param>
    void DrawClosedBezier(
        (double X, double Y) p0,
        (double X, double Y) c1,
        (double X, double Y) c2,
        (double X, double Y) p1,
        (double X, double Y) c2Back,
        (double X, double Y) c1Back,
        Color? fill = null,
        double strokeWidth = 0);

    /// <summary>
    /// Draws a music-font glyph (Emmentaler) at the given baseline anchor.
    /// </summary>
    /// <param name="glyph">SMuFL Unicode codepoint (e.g.
    /// <c>EmmentalerGlyphs.GClef</c>).</param>
    /// <param name="x">X position of the glyph anchor (staff-spaces).</param>
    /// <param name="y">Y position of the glyph anchor (staff-spaces).</param>
    /// <param name="fontSize">Font size in staff-spaces (typically 4.0).</param>
    /// <param name="fill">Glyph color; null uses the renderer default (opaque black).</param>
    void DrawGlyph(
        char glyph, double x, double y, double fontSize,
        Color? fill = null);

    /// <summary>
    /// Draws a notehead glyph and, in interactive SVG output, a tight
    /// transparent click target the exact size of the notehead ink
    /// (<paramref name="inkWidth"/> × <paramref name="inkHeight"/> staff-spaces,
    /// centred vertically on <paramref name="y"/>) so only the head — not the
    /// whole glyph em-box — is clickable. The default (used by static SVG,
    /// PDF and PNG backends) just draws the glyph like <see cref="DrawGlyph"/>.
    /// </summary>
    void DrawNotehead(
        char glyph, double x, double y, double fontSize, Color? fill,
        double inkWidth, double inkHeight)
        => DrawGlyph(glyph, x, y, fontSize, fill);

    /// <summary>
    /// Draws a music glyph that is ATTACHED to a note and shares its
    /// <c>data-pos</c> — an accidental or its parentheses. In interactive SVG the
    /// glyph stays highlightable (keeps its data-pos) but is NOT clickable, so a
    /// note's only click target is its notehead hit rect (see
    /// <see cref="DrawNotehead"/>), not the accidental's loose glyph box. The
    /// default (static SVG, PDF, PNG) just draws the glyph like
    /// <see cref="DrawGlyph"/>.
    /// </summary>
    void DrawAttachedGlyph(
        char glyph, double x, double y, double fontSize, Color? fill = null)
        => DrawGlyph(glyph, x, y, fontSize, fill);

    /// <summary>Draws plain (non-music) text such as titles, dynamics, lyrics.</summary>
    /// <param name="text">The string to draw.</param>
    /// <param name="x">Anchor X in staff-spaces.</param>
    /// <param name="y">Baseline (or <paramref name="verticalAnchor"/>) Y in staff-spaces.</param>
    /// <param name="fontSize">Font size in staff-spaces.</param>
    /// <param name="role">WHAT this string is — a title, a lyric, a fret number. The
    /// backend turns it into a face through the score's
    /// <see cref="TextFontPlan"/>.</param>
    /// <param name="style">Weight and slant, which the engraving decides and a
    /// <c>font</c> directive does not.</param>
    /// <param name="anchor">Horizontal alignment about <paramref name="x"/>.</param>
    /// <param name="fill">Ink colour; null is the renderer default.</param>
    /// <param name="verticalAnchor">Vertical alignment about <paramref name="y"/>.</param>
    /// <remarks>
    /// ⚠️ A ROLE, NOT A FAMILY — changed 2026-08-18. Every caller but one passed the
    /// literal <c>"serif"</c>, so the page could say which of two bundled faces it wanted
    /// and could not say what the string WAS; a <c>fonts { }</c> directive that gives
    /// lyrics one face and chord symbols another had nothing to bind to, and a decorator
    /// that rewrote families after the fact could not tell a fret number from a title
    /// (it restyled both). The face is now decided in one place —
    /// <see cref="TextFontPlan.Resolve"/> — and this parameter is the question it answers.
    /// </remarks>
    void DrawText(
        string text, double x, double y, double fontSize,
        TextRole role, FontStyle style = FontStyle.Regular,
        TextAnchor anchor = TextAnchor.Start, Color? fill = null,
        VerticalAnchor verticalAnchor = VerticalAnchor.Baseline);

    /// <summary>
    /// Tags subsequent draw operations (until <see cref="IDisposable.Dispose"/>)
    /// with a source-text position for click-to-source mapping. SVG backends
    /// emit <c>data-pos</c> attributes; backends that don't support metadata
    /// may ignore the call.
    /// </summary>
    IDisposable Source(int sourcePosition);

    /// <summary>
    /// Like <see cref="Source(int)"/>, but the emitted elements ALSO carry the given
    /// alias offsets (interactive SVG: a <c>data-alt</c> list), so a caret on any of them
    /// highlights the element too — used where one drawn barline collapses several written
    /// ones (a phrase <c>:|</c> plus the section <c>|</c>/<c>:|:</c> that confirms it).
    /// The primary stays the click target. Non-interactive backends ignore the aliases.
    /// </summary>
    IDisposable Source(int sourcePosition, System.Collections.Generic.IReadOnlyList<int> aliases)
        => Source(sourcePosition);

    /// <summary>
    /// Draws subsequent MUSIC glyphs (until <see cref="IDisposable.Dispose"/>) from another
    /// Emmentaler DESIGN — <paramref name="rounded"/> is the size in the file name, 20 being
    /// the score's own. Text is unaffected.
    /// </summary>
    /// <remarks>
    /// Emmentaler is optically sized: a grace is not the 20 design drawn small, it is the 14
    /// design drawn at <c>magstep(-3)</c>, and its outlines differ (a head's right edge is
    /// 1.298161 of its own staff spaces against the 20's 1.304200). The glyph's FONT SIZE
    /// does not change with the face — every design's em is four of ITS OWN staff spaces, so
    /// the caller passes the same <c>fontSize</c> it already computed and only the file
    /// changes.
    /// <para>
    /// ⚠️ ONE DECISION, TWO READERS: the design opened here must be the one the LAYOUT
    /// measured with (<c>GlyphMetrics.AtFontSize</c> of the same font-size), or the box a
    /// column reserved stops being the box the glyph fills.
    /// </para>
    /// The default ignores the scope: a backend that has only the 20 face keeps drawing from
    /// it, which is what every caller got before this existed.
    /// </remarks>
    IDisposable MusicFace(int rounded) => NullScope.Instance;

    /// <summary>
    /// In interactive SVG, draws a transparent click target over the given
    /// rectangle, carrying the current <see cref="Source(int)"/> position — used to
    /// give thin ink (a barline) a comfortably clickable area. Static backends
    /// (exported SVG, PDF, PNG) draw nothing.
    /// </summary>
    void DrawHitRect(double x, double y, double width, double height) { }

    /// <summary>
    /// Begins a transformed group scope. Subsequent draw operations have the
    /// transform applied (in document order, after any enclosing groups).
    /// Dispose the returned token to end the scope.
    /// </summary>
    IDisposable BeginGroup(DrawingTransform transform);
}
