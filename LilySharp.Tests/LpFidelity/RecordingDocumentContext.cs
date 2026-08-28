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

using LilySharp.Core.Rendering;

namespace LilySharp.Tests.LpFidelity;

/// <summary>A music-font glyph as the renderer actually placed it.</summary>
internal readonly record struct DrawnGlyph(char Glyph, double X, double Y, double FontSize);

/// <summary>A filled rectangle as the renderer actually placed it — bar lines are these.</summary>
internal readonly record struct DrawnRect(double X, double Y, double Width, double Height);

/// <summary>A straight line — staff lines, stems, ledger lines.</summary>
internal readonly record struct DrawnLine(
    double X1, double Y1, double X2, double Y2, double StrokeWidth,
    // Whether the rule was drawn DASHED. Kept because thickness and span alone do not tell
    // a staff line from an accel./rit. spanner's rule: the rule is StaffLineThickness thick
    // and reaches across the system, so RenderedGeometry.StaffLineYs counted it as a sixth
    // staff line and every reading on such a page threw "found 6 staff lines" (2026-08-28,
    // books TSCR/TSLR). A dashed rule is never a staff line.
    bool IsDashed = false);

/// <summary>
/// A filled quadrilateral — a BEAM segment, drawn corner by corner because it is sloped.
/// Corners are in the order the renderer passes them: top-left, top-right, bottom-right,
/// bottom-left.
/// </summary>
/// <remarks>
/// A bounding box cannot stand in for it. LilyPond's <c>Beam.positions</c> is a PAIR — the
/// centre line's height at each end — and a sloped beam's box only knows their average, so
/// a beam that is too steep and one that is too high would read the same.
/// </remarks>
internal readonly record struct DrawnQuad(
    double X0, double Y0, double X1, double Y1, double X2, double Y2, double X3, double Y3);

/// <summary>
/// A tapered bow — a tie or a slur — as the renderer actually placed it. The renderer emits
/// the "bezier sandwich" (lily/lookup.cc Lookup::slur): one curve out along
/// <c>P0 → C1 C2 → P1</c> and one back along <c>P1 → C2Back C1Back → P0</c>, the two middle
/// controls offset by ±half the mid-thickness PERPENDICULAR to the chord. The bow's own
/// control point — the one <c>TieFormattingProblem</c>/<c>SlurScoringProblem</c> solved — is
/// the MIDPOINT of each pair, which is why both halves are kept rather than one.
/// </summary>
internal readonly record struct DrawnBezier(
    (double X, double Y) P0, (double X, double Y) C1, (double X, double Y) C2,
    (double X, double Y) P1, (double X, double Y) C2Back, (double X, double Y) C1Back)
{
    /// <summary>The solved first control point: the sandwich's two halves averaged.</summary>
    public (double X, double Y) Centreline1
        => ((C1.X + C1Back.X) / 2, (C1.Y + C1Back.Y) / 2);
}

/// <summary>
/// Plain text — titles, lyrics, chord symbols, dynamics. The font and the anchor are kept
/// because the string and position alone cannot say WHICH of those a text is, and a
/// measurement that picked up a title instead of a chord symbol would look plausible.
/// <see cref="Anchor"/> also records what <see cref="X"/> MEANS: <c>Middle</c> makes it the
/// ink centre and <c>Start</c> the ink left. A chord symbol takes <c>Start</c>, which is
/// LilyPond's ChordName reference point (scm/define-grobs.scm:837-855 — no X-offset, no
/// self-alignment-interface); a lyric syllable is still centred, which is not LyricText's
/// placement and is the next thing to port.
/// </summary>
internal readonly record struct DrawnText(
    string Text, double X, double Y, double FontSize, TextRole Role, TextAnchor Anchor);

/// <summary>
/// An <see cref="IDocumentContext"/> that records what was drawn instead of writing a file,
/// at FULL double precision.
/// </summary>
/// <remarks>
/// <para>
/// This exists so LP-fidelity measurements are not taken from SVG text. SvgGenerator formats
/// every coordinate with <c>F2</c> (SvgGenerator.cs:229), which quantises to 0.01 and so
/// blurs any residual under 0.005 — useless when the LilyPond values being matched are
/// quoted to six decimals (0.189365 and 0.142857 differ from each other, and from 0.19, well
/// inside that noise).
/// </para>
/// <para>
/// It records through the SAME <c>SharedRenderer.RenderTo</c> path the SVG, PDF and PNG
/// backends use, so what it measures is what the product draws — not a parallel
/// reimplementation that could drift.
/// </para>
/// <para>
/// Coordinates are resolved to absolute page space: <see cref="BeginGroup"/> transforms are
/// composed onto a stack and applied as each primitive is recorded, exactly as a real backend
/// would (see PdfDrawingContext.BeginGroup — translate, then scale).
/// </para>
/// </remarks>
internal sealed class RecordingDocumentContext : IDocumentContext
{
    private readonly List<RecordingDrawingContext> _pages = new();

    public IReadOnlyList<RecordingDrawingContext> Pages => _pages;

    /// <summary>The first page — the only one most probes need.</summary>
    public RecordingDrawingContext Page => _pages[0];

    public TextFontPlan Fonts { get; set; } = TextFontPlan.Default;

        public IDrawingContext BeginPage(double widthSpaces, double heightSpaces)
    {
        var page = new RecordingDrawingContext(widthSpaces, heightSpaces);
        _pages.Add(page);
        return page;
    }

    public void EndPage() { }

    public void Dispose() { }
}

/// <summary>Records the primitives drawn on one page. See <see cref="RecordingDocumentContext"/>.</summary>
internal sealed class RecordingDrawingContext : IDrawingContext
{
    private readonly List<DrawnGlyph> _glyphs = new();
    private readonly List<DrawnRect> _rects = new();
    private readonly List<DrawnLine> _lines = new();
    private readonly List<DrawnText> _texts = new();
    private readonly List<DrawnQuad> _quads = new();
    private readonly List<DrawnBezier> _beziers = new();

    // Cumulative transform: a point p maps to (TranslateX + ScaleX * p.X, ...).
    //
    // Seeded from DrawingTransform.Identity, which is a real identity since 2026-08-19.
    // It was not before — the property was `new()`, which on a record struct zero-initialises
    // every field instead of applying the primary constructor's defaults, so seeding an
    // accumulator with it collapsed every recorded coordinate to 0. This context and
    // SharedRendererBeamTests' both hand-wrote `new(0, 0, 1, 1)` to dodge that, which made
    // two more spellings of one quantity; the property was fixed instead and they read it.
    private DrawingTransform _current = DrawingTransform.Identity;
    private readonly Stack<DrawingTransform> _stack = new();

    public RecordingDrawingContext(double widthSpaces, double heightSpaces)
    {
        WidthSpaces = widthSpaces;
        HeightSpaces = heightSpaces;
    }

    public double WidthSpaces { get; }
    public double HeightSpaces { get; }

    public IReadOnlyList<DrawnGlyph> Glyphs => _glyphs;
    public IReadOnlyList<DrawnRect> Rects => _rects;
    public IReadOnlyList<DrawnLine> Lines => _lines;
    public IReadOnlyList<DrawnText> Texts => _texts;
    public IReadOnlyList<DrawnQuad> Quads => _quads;
    public IReadOnlyList<DrawnBezier> Beziers => _beziers;

    private double Tx(double x) => _current.TranslateX + _current.ScaleX * x;
    private double Ty(double y) => _current.TranslateY + _current.ScaleY * y;
    private double Sx(double w) => _current.ScaleX * w;
    private double Sy(double h) => _current.ScaleY * h;

    public void DrawLine(double x1, double y1, double x2, double y2,
                         Color? stroke = null, double strokeWidth = 0.1,
                         (double On, double Off)? dash = null, LineCap cap = LineCap.Butt)
        => _lines.Add(new DrawnLine(
            Tx(x1), Ty(y1), Tx(x2), Ty(y2), Sx(strokeWidth), dash.HasValue));

    public void DrawRectangle(double x, double y, double width, double height,
                              Color? fill = null, Color? stroke = null, double strokeWidth = 0)
        => _rects.Add(new DrawnRect(Tx(x), Ty(y), Sx(width), Sy(height)));

    public void DrawFilledQuad((double X, double Y) p0, (double X, double Y) p1,
                               (double X, double Y) p2, (double X, double Y) p3, Color fill)
    {
        // The corners, for a beam's own height and slope...
        _quads.Add(new DrawnQuad(Tx(p0.X), Ty(p0.Y), Tx(p1.X), Ty(p1.Y),
                                 Tx(p2.X), Ty(p2.Y), Tx(p3.X), Ty(p3.Y)));

        // ...and its bounding box, which the probes that only need "there is ink here"
        // already read as a rect.
        double minX = Math.Min(Math.Min(p0.X, p1.X), Math.Min(p2.X, p3.X));
        double maxX = Math.Max(Math.Max(p0.X, p1.X), Math.Max(p2.X, p3.X));
        double minY = Math.Min(Math.Min(p0.Y, p1.Y), Math.Min(p2.Y, p3.Y));
        double maxY = Math.Max(Math.Max(p0.Y, p1.Y), Math.Max(p2.Y, p3.Y));
        _rects.Add(new DrawnRect(Tx(minX), Ty(minY), Sx(maxX - minX), Sy(maxY - minY)));
    }

    public void DrawEllipse(double cx, double cy, double rx, double ry,
                            Color? fill = null, Color? stroke = null, double strokeWidth = 0)
        => _rects.Add(new DrawnRect(Tx(cx - rx), Ty(cy - ry), Sx(2 * rx), Sy(2 * ry)));

    public void DrawCircle(double cx, double cy, double r, Color? fill = null)
        => _rects.Add(new DrawnRect(Tx(cx - r), Ty(cy - r), Sx(2 * r), Sy(2 * r)));

    public void DrawClosedBezier((double X, double Y) p0, (double X, double Y) c1,
                                 (double X, double Y) c2, (double X, double Y) p1,
                                 (double X, double Y) c2Back, (double X, double Y) c1Back,
                                 Color? fill = null, double strokeWidth = 0)
    {
        // The endpoints as a line, which the probes that only need "there is a bow here"
        // already read...
        _lines.Add(new DrawnLine(Tx(p0.X), Ty(p0.Y), Tx(p1.X), Ty(p1.Y), Sx(strokeWidth)));

        // ...and the whole sandwich, because a bow's DIRECTION lives in its control points
        // and not in its endpoints: a tie curving up and one curving down share the two
        // ends exactly (TieFormattingProblem.CreateLayout puts both at the same attachment
        // Y) and differ only in which side the controls sit.
        _beziers.Add(new DrawnBezier(
            (Tx(p0.X), Ty(p0.Y)), (Tx(c1.X), Ty(c1.Y)), (Tx(c2.X), Ty(c2.Y)),
            (Tx(p1.X), Ty(p1.Y)), (Tx(c2Back.X), Ty(c2Back.Y)), (Tx(c1Back.X), Ty(c1Back.Y))));
    }

    public void DrawGlyph(char glyph, double x, double y, double fontSize, Color? fill = null)
        => _glyphs.Add(new DrawnGlyph(glyph, Tx(x), Ty(y), Sy(fontSize)));

    public void DrawText(string text, double x, double y, double fontSize, TextRole role,
                         FontStyle style = FontStyle.Regular, TextAnchor anchor = TextAnchor.Start,
                         Color? fill = null, VerticalAnchor verticalAnchor = VerticalAnchor.Baseline)
        => _texts.Add(new DrawnText(text, Tx(x), Ty(y), Sy(fontSize), role, anchor));

    public IDisposable Source(int sourcePosition) => NullScope.Instance;

    public IDisposable BeginGroup(DrawingTransform transform)
    {
        _stack.Push(_current);
        // Compose: parent(child(p)). Child translates then scales in the parent's frame.
        _current = new DrawingTransform(
            _current.TranslateX + _current.ScaleX * transform.TranslateX,
            _current.TranslateY + _current.ScaleY * transform.TranslateY,
            _current.ScaleX * transform.ScaleX,
            _current.ScaleY * transform.ScaleY);
        return new PopScope(this);
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }

    private sealed class PopScope : IDisposable
    {
        private readonly RecordingDrawingContext _owner;
        private bool _done;
        public PopScope(RecordingDrawingContext owner) => _owner = owner;
        public void Dispose()
        {
            if (_done) return;
            _done = true;
            _owner._current = _owner._stack.Pop();
        }
    }
}
