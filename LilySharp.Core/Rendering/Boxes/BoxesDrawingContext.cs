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

using System.Reflection;
using LilySharp.Core.Rendering.Png;
using LilySharp.Core.Svg;
using SkiaSharp;

namespace LilySharp.Core.Rendering.Boxes;

/// <summary>One drawn symbol: what it is, the box its ink fills on the page, and where in the
/// source it comes from.</summary>
/// <param name="Kind">What it is — <c>notehead</c>, <c>stem</c>, <c>staffLine</c>, <c>lyric</c>…
/// (<see cref="BoxesDrawingContext"/> says where each comes from).</param>
/// <param name="Glyph">A music glyph's name (Lily#'s <c>EmmentalerGlyphs</c> constant, or
/// <c>U+XXXX</c> for one it does not name); null for anything else.</param>
/// <param name="Codepoint">A music glyph's code point in the bundled Emmentaler; null otherwise.</param>
/// <param name="Text">A text's string; null otherwise.</param>
/// <param name="Box">The ink box, page coordinates in staff spaces, Y down: x0, y0, x1, y1.</param>
/// <param name="Pos">The source offset it was drawn under (<c>data-pos</c>), −1 for none.</param>
/// <param name="Staff">The staff (<c>EnumerateStaves</c> index) it was drawn on, −1 for none.</param>
/// <param name="Todo">The <c>@todo</c> key of the item it belongs to, null for none.</param>
/// <param name="Ends">A tie's or slur's two ends (x0, y0, x1, y1), null for anything else.</param>
public sealed record BoxSymbol(
    string Kind, string? Glyph, int? Codepoint, string? Text, double[] Box,
    int Pos, int Staff, string? Todo, double[]? Ends);

/// <summary>One bar of one system as the page prints it: its number and its box (from the
/// system's top staff line to its bottom one).</summary>
public sealed record BoxBar(int Bar, double[] Box);

/// <summary>One page: its size in staff spaces, its symbols in drawing order, its bars.</summary>
public sealed record BoxPage(int Page, double Width, double Height,
    IReadOnlyList<BoxSymbol> Symbols, IReadOnlyList<BoxBar> Bars);

/// <summary>
/// A backend that draws nothing and records, for every primitive, the box its ink fills —
/// <c>lysc boxes</c> (LilySharp-Omr's proposal of 2026-10-02, P5: an OMR reader's symbol-level
/// training truth, read from Lily# rather than guessed back out of its SVG).
/// </summary>
/// <remarks>
/// <para>
/// It sits where every backend sits, after the Y flip, so its coordinates are the SVG's and the
/// PNG's: page staff spaces, origin top-left, Y down. Group transforms are composed as the
/// backends compose them (translate, then scale).
/// </para>
/// <para>
/// ⚠️ THE GLYPH AND TEXT BOXES ARE THE PNG BACKEND'S OWN MEASURE: a <see cref="PngDrawingContext"/>
/// that never paints answers them (<see cref="PngDrawingContext.MeasureGlyphInk"/>,
/// <see cref="PngDrawingContext.MeasureTextInk"/>), from the face and the placement its
/// <c>DrawGlyph</c> / <c>DrawText</c> use — so a box is where the PNG's ink is, not a second
/// opinion of it. Lines, rectangles, beams and curves are measured from their geometry (a
/// stroke's half-width included; a curve sampled).
/// </para>
/// <para>
/// THE KIND: a music glyph's from its name (<see cref="GlyphKind"/>), a text's from its
/// <see cref="TextRole"/>, and anything else from the innermost <see cref="IDrawingContext.Kind"/>
/// scope the renderer opened — or the primitive's own name (<c>line</c>, <c>rect</c>,
/// <c>curve</c>…) where it opened none. An enclosing kind scope wins over a glyph's or a text's
/// own kind, so a fret number drawn as text inside a <c>tabFret</c> scope says so.
/// </para>
/// </remarks>
internal sealed class BoxesDrawingContext : IDrawingContext, IDisposable
{
    // Measured at 100 px a staff space: Skia's glyph bounds are floats in pixels, and a font
    // four pixels tall would round them to the pixel grid.
    private const double Scale = 100;

    private readonly SKBitmap _bitmap = new(1, 1);
    private readonly SKCanvas _canvas;
    private readonly PngDrawingContext _measure;
    private readonly List<BoxSymbol> _symbols = new();
    private readonly List<BoxBar> _bars = new();

    private readonly Stack<(double Tx, double Ty, double Sx, double Sy)> _transforms = new();
    private (double Tx, double Ty, double Sx, double Sy) _t = (0, 0, 1, 1);
    private int _pos = -1;
    private int _staff = -1;
    private string? _kind;
    private string? _todo;

    public BoxesDrawingContext(double width, double height, string? fontDirectory, TextFontPlan plan)
    {
        Width = width;
        Height = height;
        _canvas = new SKCanvas(_bitmap);
        _measure = new PngDrawingContext(_canvas, Scale, fontDirectory, plan);
    }

    public double Width { get; }
    public double Height { get; }
    public IReadOnlyList<BoxSymbol> Symbols => _symbols;
    public IReadOnlyList<BoxBar> Bars => _bars;

    public void Dispose()
    {
        _measure.Dispose();
        _canvas.Dispose();
        _bitmap.Dispose();
    }

    // ---- recording ----

    private double[] Page(double x0, double y0, double x1, double y1)
    {
        double ax = _t.Tx + _t.Sx * x0, bx = _t.Tx + _t.Sx * x1;
        double ay = _t.Ty + _t.Sy * y0, by = _t.Ty + _t.Sy * y1;
        return [R(Math.Min(ax, bx)), R(Math.Min(ay, by)), R(Math.Max(ax, bx)), R(Math.Max(ay, by))];
    }

    private double[] PagePoints(double x0, double y0, double x1, double y1)
        => [R(_t.Tx + _t.Sx * x0), R(_t.Ty + _t.Sy * y0), R(_t.Tx + _t.Sx * x1), R(_t.Ty + _t.Sy * y1)];

    private static double R(double v) => Math.Round(v, 4);

    private void Add(string ownKind, double x0, double y0, double x1, double y1,
        string? glyph = null, int? codepoint = null, string? text = null, double[]? ends = null)
        => _symbols.Add(new BoxSymbol(_kind ?? ownKind, glyph, codepoint, text,
            Page(x0, y0, x1, y1), _pos, _staff, _todo, ends));

    private void AddPoints(string ownKind, IEnumerable<(double X, double Y)> points, double pad,
        double[]? ends = null)
    {
        double x0 = double.PositiveInfinity, y0 = double.PositiveInfinity;
        double x1 = double.NegativeInfinity, y1 = double.NegativeInfinity;
        foreach (var (x, y) in points)
        {
            x0 = Math.Min(x0, x); y0 = Math.Min(y0, y);
            x1 = Math.Max(x1, x); y1 = Math.Max(y1, y);
        }
        if (double.IsInfinity(x0))
            return;
        Add(ownKind, x0 - pad, y0 - pad, x1 + pad, y1 + pad, ends: ends);
    }

    private static IEnumerable<(double X, double Y)> Cubic(
        (double X, double Y) p0, (double X, double Y) c1, (double X, double Y) c2, (double X, double Y) p1)
    {
        const int samples = 32;
        for (int s = 0; s <= samples; s++)
        {
            double t = s / (double)samples, u = 1 - t;
            double b0 = u * u * u, b1 = 3 * u * u * t, b2 = 3 * u * t * t, b3 = t * t * t;
            yield return (b0 * p0.X + b1 * c1.X + b2 * c2.X + b3 * p1.X,
                          b0 * p0.Y + b1 * c1.Y + b2 * c2.Y + b3 * p1.Y);
        }
    }

    // ---- primitives ----

    public void DrawLine(double x1, double y1, double x2, double y2,
        Color? stroke = null, double strokeWidth = 0.1,
        (double On, double Off)? dash = null, LineCap cap = LineCap.Butt)
    {
        // The stroked outline: half the width either side of the segment, and past each end
        // by half the width for a round cap. Its box is the box of those corners.
        double dx = x2 - x1, dy = y2 - y1, len = Math.Sqrt(dx * dx + dy * dy), h = strokeWidth / 2;
        if (len == 0)
        {
            Add("line", x1 - h, y1 - h, x1 + h, y1 + h);
            return;
        }
        double nx = -dy / len * h, ny = dx / len * h;
        double ex = cap == LineCap.Round ? dx / len * h : 0, ey = cap == LineCap.Round ? dy / len * h : 0;
        AddPoints("line",
        [
            (x1 - ex + nx, y1 - ey + ny), (x1 - ex - nx, y1 - ey - ny),
            (x2 + ex + nx, y2 + ey + ny), (x2 + ex - nx, y2 + ey - ny),
        ], 0);
    }

    public void DrawRectangle(double x, double y, double width, double height,
        Color? fill = null, Color? stroke = null, double strokeWidth = 0)
    {
        if (fill is null && stroke is null)
            return;   // nothing inked (a transparent target)
        double h = stroke is null ? 0 : strokeWidth / 2;
        Add("rect", x - h, y - h, x + width + h, y + height + h);
    }

    public void DrawFilledQuad((double X, double Y) p0, (double X, double Y) p1,
        (double X, double Y) p2, (double X, double Y) p3, Color fill)
        => AddPoints("quad", [p0, p1, p2, p3], 0);

    public void DrawEllipse(double cx, double cy, double rx, double ry,
        Color? fill = null, Color? stroke = null, double strokeWidth = 0)
    {
        double h = stroke is null ? 0 : strokeWidth / 2;
        Add("ellipse", cx - rx - h, cy - ry - h, cx + rx + h, cy + ry + h);
    }

    public void DrawCircle(double cx, double cy, double r, Color? fill = null)
        => Add("circle", cx - r, cy - r, cx + r, cy + r);

    public void DrawClosedBezier((double X, double Y) p0, (double X, double Y) c1, (double X, double Y) c2,
        (double X, double Y) p1, (double X, double Y) c2Back, (double X, double Y) c1Back,
        Color? fill = null, double strokeWidth = 0)
        => AddPoints("curve", Cubic(p0, c1, c2, p1).Concat(Cubic(p1, c2Back, c1Back, p0)), strokeWidth / 2,
            ends: PagePoints(p0.X, p0.Y, p1.X, p1.Y));

    public void DrawBezier((double X, double Y) p0, (double X, double Y) c1, (double X, double Y) c2,
        (double X, double Y) p1, Color? stroke = null, double strokeWidth = 0.1)
        => AddPoints("curve", Cubic(p0, c1, c2, p1), strokeWidth / 2, ends: PagePoints(p0.X, p0.Y, p1.X, p1.Y));

    public void DrawGlyph(char glyph, double x, double y, double fontSize, Color? fill = null)
    {
        var ink = _measure.MeasureGlyphInk(glyph, x, y, fontSize);
        if (ink.IsEmpty)
            return;
        string name = GlyphName(glyph);
        Add(GlyphKind(name), ink.Left / Scale, ink.Top / Scale, ink.Right / Scale, ink.Bottom / Scale,
            glyph: name, codepoint: glyph);
    }

    public void DrawText(string text, double x, double y, double fontSize,
        TextRole role, FontStyle style = FontStyle.Regular,
        TextAnchor anchor = TextAnchor.Start, Color? fill = null,
        VerticalAnchor verticalAnchor = VerticalAnchor.Baseline)
    {
        var ink = _measure.MeasureTextInk(text, x, y, fontSize, role, style, anchor, verticalAnchor);
        if (ink.IsEmpty)
            return;
        Add(TextKind(role), ink.Left / Scale, ink.Top / Scale, ink.Right / Scale, ink.Bottom / Scale, text: text);
    }

    public void DrawBarBox(int barNumber, double x, double y, double width, double height)
        => _bars.Add(new BoxBar(barNumber, Page(x, y, x + width, y + height)));

    // ---- scopes ----

    public IDisposable Source(int sourcePosition)
    {
        int saved = _pos;
        _pos = sourcePosition;
        return new ScopeAction(() => _pos = saved);
    }

    public IDisposable Todo(string key)
    {
        string? saved = _todo;
        _todo = key;
        return new ScopeAction(() => _todo = saved);
    }

    public IDisposable Kind(string kind)
    {
        string? saved = _kind;
        _kind = kind;
        return new ScopeAction(() => _kind = saved);
    }

    public IDisposable Staff(int staffIndex)
    {
        int saved = _staff;
        _staff = staffIndex;
        return new ScopeAction(() => _staff = saved);
    }

    public IDisposable MusicFace(int rounded) => _measure.MusicFace(rounded);

    public IDisposable BeginGroup(DrawingTransform transform)
    {
        _transforms.Push(_t);
        if (!transform.IsIdentity)
            _t = (_t.Tx + _t.Sx * transform.TranslateX, _t.Ty + _t.Sy * transform.TranslateY,
                  _t.Sx * transform.ScaleX, _t.Sy * transform.ScaleY);
        return new ScopeAction(() => _t = _transforms.Pop());
    }

    // ---- kinds ----

    /// <summary>Code point → the <c>EmmentalerGlyphs</c> constant naming it (the first declared,
    /// where two constants share a glyph), read once.</summary>
    private static readonly Dictionary<char, string> GlyphNames = BuildGlyphNames();

    private static Dictionary<char, string> BuildGlyphNames()
    {
        var names = new Dictionary<char, string>();
        foreach (var field in typeof(EmmentalerGlyphs).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            if (field.IsLiteral && field.FieldType == typeof(char) && field.GetRawConstantValue() is char c)
                names.TryAdd(c, field.Name);
        return names;
    }

    internal static string GlyphName(char glyph)
        => GlyphNames.TryGetValue(glyph, out var name) ? name : $"U+{(int)glyph:X4}";

    /// <summary>A music glyph's kind, from its name's leading word.</summary>
    internal static string GlyphKind(string name) => name switch
    {
        _ when name.StartsWith("Notehead", StringComparison.Ordinal) => "notehead",
        _ when name.StartsWith("Rest", StringComparison.Ordinal) => "rest",
        _ when name.StartsWith("Accidental", StringComparison.Ordinal) => "accidental",
        _ when name.StartsWith("Flag", StringComparison.Ordinal) => "flag",
        _ when name.Contains("Clef", StringComparison.Ordinal) => "clef",
        _ when name.StartsWith("Time", StringComparison.Ordinal) => "timeSignature",
        _ when name.StartsWith("Artic", StringComparison.Ordinal) || name.StartsWith("Script", StringComparison.Ordinal) => "articulation",
        _ when name.StartsWith("Fermata", StringComparison.Ordinal) => "fermata",
        _ when name.StartsWith("Orn", StringComparison.Ordinal) => "ornament",
        _ when name.StartsWith("Dynamic", StringComparison.Ordinal) => "dynamic",
        _ when name.StartsWith("Fingering", StringComparison.Ordinal) => "fingering",
        _ when name.StartsWith("Pedal", StringComparison.Ordinal) => "pedal",
        _ when name.StartsWith("Fig", StringComparison.Ordinal) => "figuredBass",
        _ when name.StartsWith("Met", StringComparison.Ordinal) => "metronome",
        _ when name.StartsWith("Augmentation", StringComparison.Ordinal) => "dot",
        _ when name.StartsWith("Bracket", StringComparison.Ordinal) => "bracket",
        _ when name.StartsWith("Arpeggio", StringComparison.Ordinal) => "arpeggio",
        _ when name.StartsWith("Breath", StringComparison.Ordinal) => "breath",
        _ when name.StartsWith("Caesura", StringComparison.Ordinal) => "caesura",
        _ when name.StartsWith("Repeat", StringComparison.Ordinal) => "repeatSign",
        _ when name.StartsWith("Mark", StringComparison.Ordinal) => "mark",
        _ => "glyph",
    };

    /// <summary>A text's kind: its role, in camel case (<c>TextRole.Lyric</c> → <c>lyric</c>).</summary>
    internal static string TextKind(TextRole role)
    {
        string s = role.ToString();
        return char.ToLowerInvariant(s[0]) + s[1..];
    }
}

/// <summary>The document side of <see cref="BoxesDrawingContext"/>: one per page.</summary>
internal sealed class BoxesDocumentContext : IDocumentContext
{
    private readonly string? _fontDirectory;
    private readonly List<BoxesDrawingContext> _pages = new();

    public BoxesDocumentContext(string? fontDirectory) => _fontDirectory = fontDirectory;

    public TextFontPlan Fonts { get; set; } = TextFontPlan.Default;

    public IDrawingContext BeginPage(double widthSpaces, double heightSpaces)
    {
        var page = new BoxesDrawingContext(widthSpaces, heightSpaces, _fontDirectory, Fonts);
        _pages.Add(page);
        return page;
    }

    public void EndPage() { }

    /// <summary>The pages recorded so far.</summary>
    public IReadOnlyList<BoxPage> Pages
        => [.. _pages.Select((p, i) => new BoxPage(i + 1, R(p.Width), R(p.Height), p.Symbols, p.Bars))];

    private static double R(double v) => Math.Round(v, 4);

    public void Dispose()
    {
        foreach (var page in _pages)
            page.Dispose();
    }
}
