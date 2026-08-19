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
    private readonly bool _interactive;
    /// <summary>The designs whose glyphs this page actually drew — the document embeds a
    /// face for each. Shared with the document context, which writes the header after every
    /// page has been drawn.</summary>
    private readonly HashSet<int>? _usedDesigns;
    private int? _currentSourcePosition;
    private IReadOnlyList<int>? _currentAliases;
    private int _musicDesign = EmmentalerFaces.DefaultDesign;

    /// <summary>Capture hook for <see cref="SvgSystemFragmentCache"/>: while set, every
    /// source value this context EMITS (each data-pos, then each data-alt member, in
    /// text order) is appended here, so the capture can verify its scan of the emitted
    /// text against ground truth. Null outside a capture (the common case).</summary>
    internal List<int>? SourceLog { get; set; }

    /// <summary>Capture hook, same lifetime as <see cref="SourceLog"/>: the Emmentaler
    /// designs drawn during the capture, so a replayed fragment can re-record them into
    /// the document's used-design set (the @font-face side channel).</summary>
    internal HashSet<int>? DesignLog { get; set; }

    /// <summary>Which face each text role is drawn in — the score's <c>font</c>
    /// directive, resolved. The document hands its own down at <c>BeginPage</c>.</summary>
    private readonly TextFontPlan _fonts;

    public SvgDrawingContext(StringBuilder sb, bool interactive = false,
        HashSet<int>? usedDesigns = null, TextFontPlan? fonts = null)
    {
        _sb = sb;
        _interactive = interactive;
        _usedDesigns = usedDesigns;
        _fonts = fonts ?? TextFontPlan.Default;
    }

    /// <summary>A fill attribute for a glyph/shape whose default is black. SVG's initial
    /// <c>fill</c> is already black, so a black fill is redundant — omit it (this repeats
    /// across thousands of glyphs, beams and rests per score). Non-black colours emit
    /// normally; a null fill also defaults to black here (callers that need an UNFILLED
    /// shape use the <c>fill="none"</c> paths, not this helper).</summary>
    private static string FillAttr(Color? fill) =>
        fill is { } f && f != Color.Black ? string.Format(Inv, " fill=\"{0}\"", f.ToHex()) : "";

    public void DrawLine(double x1, double y1, double x2, double y2,
        Color? stroke = null, double strokeWidth = 0.1,
        (double On, double Off)? dash = null, LineCap cap = LineCap.Butt)
    {
        var color = (stroke ?? Color.Black).ToHex();
        var dashAttr = dash is { } d
            ? string.Format(Inv, " stroke-dasharray=\"{0:F2} {1:F2}\"", d.On, d.Off)
            : "";
        // SVG default linecap is butt; only emit the attribute when rounding.
        var capAttr = cap == LineCap.Round ? " stroke-linecap=\"round\"" : "";
        _sb.AppendLine(string.Format(Inv,
            "  <line x1=\"{0:F2}\" y1=\"{1:F2}\" x2=\"{2:F2}\" y2=\"{3:F2}\" stroke=\"{4}\" stroke-width=\"{5:F3}\"{6}{7}{8}/>",
            x1, y1, x2, y2, color, strokeWidth, dashAttr, capAttr, SourceAttr()));
    }

    public void DrawRectangle(double x, double y, double width, double height,
        Color? fill = null, Color? stroke = null, double strokeWidth = 0)
    {
        var attrs = new StringBuilder(96);
        attrs.AppendFormat(Inv, " x=\"{0:F2}\" y=\"{1:F2}\" width=\"{2:F2}\" height=\"{3:F2}\"", x, y, width, height);
        if (fill is { } f)
            attrs.Append(FillAttr(f));       // black omitted (SVG default), non-black emitted
        else
            attrs.Append(" fill=\"none\"");   // an explicitly UNFILLED rect
        if (stroke is { } s)
            attrs.AppendFormat(Inv, " stroke=\"{0}\" stroke-width=\"{1:F3}\"", s.ToHex(), strokeWidth);
        attrs.Append(SourceAttr());
        _sb.AppendLine($"  <rect{attrs}/>");
    }

    public void DrawFilledQuad((double X, double Y) p0, (double X, double Y) p1,
        (double X, double Y) p2, (double X, double Y) p3, Color fill)
    {
        _sb.AppendLine(string.Format(Inv,
            "  <polygon points=\"{0:F2},{1:F2} {2:F2},{3:F2} {4:F2},{5:F2} {6:F2},{7:F2}\"{8}{9}/>",
            p0.X, p0.Y, p1.X, p1.Y, p2.X, p2.Y, p3.X, p3.Y, FillAttr(fill), SourceAttr()));
    }

    public void DrawEllipse(double cx, double cy, double rx, double ry,
        Color? fill = null, Color? stroke = null, double strokeWidth = 0)
    {
        var attrs = new StringBuilder(96);
        attrs.AppendFormat(Inv, " cx=\"{0:F2}\" cy=\"{1:F2}\" rx=\"{2:F2}\" ry=\"{3:F2}\"", cx, cy, rx, ry);
        if (fill is { } f)
            attrs.Append(FillAttr(f));       // black omitted (SVG default), non-black emitted
        else
            attrs.Append(" fill=\"none\"");
        if (stroke is { } s)
            attrs.AppendFormat(Inv, " stroke=\"{0}\" stroke-width=\"{1:F3}\"", s.ToHex(), strokeWidth);
        attrs.Append(SourceAttr());
        _sb.AppendLine($"  <ellipse{attrs}/>");
    }

    public void DrawCircle(double cx, double cy, double r, Color? fill = null)
    {
        _sb.AppendLine(string.Format(Inv,
            "  <circle cx=\"{0:F2}\" cy=\"{1:F2}\" r=\"{2:F2}\"{3}{4}/>",
            cx, cy, r, FillAttr(fill), SourceAttr()));
    }

    public void DrawClosedBezier(
        (double X, double Y) p0, (double X, double Y) c1, (double X, double Y) c2,
        (double X, double Y) p1, (double X, double Y) c2Back, (double X, double Y) c1Back,
        Color? fill = null, double strokeWidth = 0)
    {
        var color = (fill ?? Color.Black).ToHex();
        var d = string.Format(Inv,
            "M {0:F2},{1:F2} C {2:F2},{3:F2} {4:F2},{5:F2} {6:F2},{7:F2} C {8:F2},{9:F2} {10:F2},{11:F2} {0:F2},{1:F2} Z",
            p0.X, p0.Y, c1.X, c1.Y, c2.X, c2.Y, p1.X, p1.Y, c2Back.X, c2Back.Y, c1Back.X, c1Back.Y);
        // A round-cap/round-join stroke in the fill colour rounds the tapered ends,
        // matching LilyPond's slur/tie stencil (fill + round stroke).
        var strokeAttr = strokeWidth > 0
            ? string.Format(Inv,
                " stroke=\"{0}\" stroke-width=\"{1:F2}\" stroke-linecap=\"round\" stroke-linejoin=\"round\"",
                color, strokeWidth)
            : "";
        _sb.AppendLine($"  <path d=\"{d}\"{FillAttr(fill)}{strokeAttr}{SourceAttr()}/>");
    }

    /// <summary>
    /// The face attribute a music glyph carries: nothing at the score's own size (the
    /// <c>.music</c> CSS class already names that family), and an explicit
    /// <c>font-family</c> — which overrides the class — for any other design. Also RECORDS
    /// the design, so the document embeds exactly the faces the score drew with.
    /// </summary>
    private string MusicFaceAttr()
    {
        _usedDesigns?.Add(_musicDesign);
        DesignLog?.Add(_musicDesign);
        // The fallback chain matters: a viewer that has not been given this design's face
        // (the VS Code preview injects Emmentaler itself and omits @font-face entirely) then
        // draws the glyph from the default design instead of showing tofu. It is the wrong
        // OUTLINE by ~0.5% and the right glyph, which is the better of the two failures.
        return _musicDesign == EmmentalerFaces.DefaultDesign
            ? ""
            : string.Format(Inv, " font-family=\"{0}, Emmentaler, serif\"",
                            EmmentalerFaces.Family(_musicDesign));
    }

    public void DrawGlyph(char glyph, double x, double y, double fontSize, Color? fill = null)
    {
        var fillAttr = FillAttr(fill);
        _sb.AppendLine(string.Format(Inv,
            "  <text class=\"music\"{6} x=\"{0:F2}\" y=\"{1:F2}\" font-size=\"{2:F2}\"{3}{4}>{5}</text>",
            x, y, fontSize, fillAttr, SourceAttr(), Escape(glyph), MusicFaceAttr()));
    }

    public void DrawNotehead(char glyph, double x, double y, double fontSize,
        Color? fill, double inkWidth, double inkHeight)
    {
        // Static output: identical to DrawGlyph (keeps exported SVG / snapshots
        // byte-for-byte unchanged).
        if (!_interactive)
        {
            DrawGlyph(glyph, x, y, fontSize, fill);
            return;
        }

        // Interactive preview: the notehead's own <text> hit area is the glyph
        // em-box (~2× the head, tall and wide). Make the glyph non-interactive
        // and lay a transparent hit rectangle the exact size of the head ink
        // (inkWidth × inkHeight, centred on the note Y) over it, so only the
        // head is clickable. Both carry the same data-pos (the glyph for
        // highlight, the rect for the click); the webview skips the .nh-hit rect
        // when it recolors highlights so the transparent box never shows.
        var fillAttr = FillAttr(fill);
        _sb.AppendLine(string.Format(Inv,
            "  <text class=\"music\"{6} pointer-events=\"none\" x=\"{0:F2}\" y=\"{1:F2}\" font-size=\"{2:F2}\"{3}{4}>{5}</text>",
            x, y, fontSize, fillAttr, SourceAttr(), Escape(glyph), MusicFaceAttr()));
        _sb.AppendLine(string.Format(Inv,
            "  <rect class=\"nh-hit\" x=\"{0:F2}\" y=\"{1:F2}\" width=\"{2:F2}\" height=\"{3:F2}\" fill=\"none\" pointer-events=\"all\"{4}/>",
            x, y - inkHeight / 2, inkWidth, inkHeight, SourceAttr()));
    }

    public void DrawHitRect(double x, double y, double width, double height)
    {
        // Interactive preview only: a transparent click target (the nh-hit class
        // keeps it out of the webview's highlight recolor, like the notehead's).
        if (!_interactive) return;
        _sb.AppendLine(string.Format(Inv,
            "  <rect class=\"nh-hit\" x=\"{0:F2}\" y=\"{1:F2}\" width=\"{2:F2}\" height=\"{3:F2}\" fill=\"none\" pointer-events=\"all\"{4}/>",
            x, y, width, height, SourceAttr()));
    }

    public void DrawAttachedGlyph(char glyph, double x, double y, double fontSize, Color? fill = null)
    {
        // Static output: identical to DrawGlyph.
        if (!_interactive)
        {
            DrawGlyph(glyph, x, y, fontSize, fill);
            return;
        }
        // Interactive preview: an accidental shares its note's data-pos, so it
        // must stay highlightable — but it must NOT be a click target, or the
        // note's clickable area would spill left onto the (loose) accidental box.
        // pointer-events="none" keeps the highlight (fill recolor) while the
        // notehead's nh-hit rect owns the click.
        var fillAttr = FillAttr(fill);
        _sb.AppendLine(string.Format(Inv,
            "  <text class=\"music\"{6} pointer-events=\"none\" x=\"{0:F2}\" y=\"{1:F2}\" font-size=\"{2:F2}\"{3}{4}>{5}</text>",
            x, y, fontSize, fillAttr, SourceAttr(), Escape(glyph), MusicFaceAttr()));
    }

    public void DrawText(string text, double x, double y, double fontSize,
        TextRole role, FontStyle style = FontStyle.Regular,
        TextAnchor anchor = TextAnchor.Start, Color? fill = null,
        VerticalAnchor verticalAnchor = VerticalAnchor.Baseline)
    {
        var attrs = new StringBuilder(128);
        attrs.AppendFormat(Inv, " x=\"{0:F2}\" y=\"{1:F2}\" font-size=\"{2:F2}\"", x, y, fontSize);
        string? family = FamilyAttributeFor(role);
        // The document root names the bundled serif (SvgDocumentContext.WriteHeader), so a
        // role that resolves to it inherits and emits nothing — an element attribute
        // still overrides the inherited one where a role was bound to something else.
        if (family != null)
            attrs.AppendFormat(Inv, " font-family=\"{0}\"", EscapeAttr(family));
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
        attrs.Append(FillAttr(fill));
        attrs.Append(SourceAttr());
        _sb.AppendLine($"  <text{attrs}>{EscapeText(text)}</text>");
    }

    /// <summary>
    /// The <c>font-family</c> this role needs on its own element, or null when the root's
    /// inherited family already says it.
    /// </summary>
    /// <remarks>
    /// A BUNDLED SANS ROLE NAMES THE BUNDLED FACE, generic last — the same shape the
    /// document root gives the serif (SvgDocumentContext.WriteHeader): the layout reserves
    /// chord symbols against the bundled TeX Gyre Heros, so a viewer holding that font
    /// draws exactly what was spaced for, and one without it degrades to its own sans —
    /// which is all the bare <c>sans-serif</c> this used to emit ever said. The serif case
    /// stays null because the root's inherited attribute already names it.
    /// </remarks>
    private string? FamilyAttributeFor(TextRole role)
    {
        var face = _fonts.Resolve(role);
        if (!face.IsBundled)
            return face.FamilyAttribute;
        return face.Family == TextFontFamily.Sans
            ? TextFontMetrics.SansFamily + ", sans-serif"
            : null;
    }

    public IDisposable Source(int sourcePosition)
    {
        var prev = _currentSourcePosition;
        _currentSourcePosition = sourcePosition;
        return new ScopeAction(() => _currentSourcePosition = prev);
    }

    public IDisposable Source(int sourcePosition, IReadOnlyList<int> aliases)
    {
        var prevPos = _currentSourcePosition;
        var prevAliases = _currentAliases;
        _currentSourcePosition = sourcePosition;
        _currentAliases = _interactive && aliases.Count > 0 ? aliases : null;
        return new ScopeAction(() =>
        {
            _currentSourcePosition = prevPos;
            _currentAliases = prevAliases;
        });
    }

    public IDisposable MusicFace(int rounded)
    {
        var prev = _musicDesign;
        _musicDesign = rounded;
        return new ScopeAction(() => _musicDesign = prev);
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
        return new ScopeAction(() =>
        {
            _sb.AppendLine("  </g>");
        });
    }

    private string SourceAttr()
    {
        if (!_currentSourcePosition.HasValue)
            return "";
        var s = string.Format(Inv, " data-pos=\"{0}\"", _currentSourcePosition.Value);
        if (SourceLog is { } capture)
        {
            capture.Add(_currentSourcePosition.Value);
            if (_currentAliases is { Count: > 0 })
                capture.AddRange(_currentAliases);
        }
        // data-alt lists the extra highlight offsets: a caret on any of them lights this
        // element too (the webview matches data-pos OR a data-alt member); the click still
        // uses data-pos. Only in interactive mode (aliases are null otherwise).
        return _currentAliases is { Count: > 0 }
            ? s + string.Format(Inv, " data-alt=\"{0}\"", string.Join(" ", _currentAliases))
            : s;
    }

    private static string Escape(char c) => c switch
    {
        '<' => "&lt;", '>' => "&gt;", '&' => "&amp;", _ => c.ToString()
    };

    private static string EscapeText(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static string EscapeAttr(string s) => EscapeText(s).Replace("\"", "&quot;");
}
