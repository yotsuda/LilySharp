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
/// <remarks>
/// ⚠️ EVERY PRIMITIVE IS APPENDED PIECEWISE, NOT FORMATTED. Until session 395 each call
/// built its line with <c>string.Format</c> — boxing every double, building the line, then
/// copying it into the builder — plus a string per source attribute, per fill attribute and
/// per escaped glyph: three to five transient strings for every notehead, beam and rest of a
/// live-drawn system, which is the preview's floor for every system the fragment memo cannot
/// replay. The numbers are formatted straight into the builder with the SAME format strings
/// (<c>F2</c> / <c>F3</c> / <c>F4</c>, invariant culture), so the text is byte for byte what
/// it was: the snapshot suite and the corpus hashes are the proof.
/// </remarks>
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

    // ---- number formatting straight into the builder (no intermediate string) ----

    private void Num(double value, string format)
    {
        Span<char> buffer = stackalloc char[32];
        if (value.TryFormat(buffer, out int written, format, Inv))
            _sb.Append(buffer[..written]);
        else
            _sb.Append(value.ToString(format, Inv)); // cannot happen for F2/F3/F4; kept honest
    }

    /// <summary>A coordinate, size or font size: two decimals.</summary>
    private void F2(double value) => Num(value, "F2");

    /// <summary>A stroke width: three decimals.</summary>
    private void F3(double value) => Num(value, "F3");

    /// <summary>A scale factor: four decimals.</summary>
    private void F4(double value) => Num(value, "F4");

    /// <summary>A named attribute with a two-decimal value: <c> name="1.23"</c>.</summary>
    private void Attr(string name, double value)
    {
        _sb.Append(' ').Append(name).Append("=\"");
        F2(value);
        _sb.Append('"');
    }

    /// <summary>A fill attribute for a glyph/shape whose default is black. SVG's initial
    /// <c>fill</c> is already black, so a black fill is redundant — omit it (this repeats
    /// across thousands of glyphs, beams and rests per score). Non-black colours emit
    /// normally; a null fill also defaults to black here (callers that need an UNFILLED
    /// shape use the <c>fill="none"</c> paths, not this helper).</summary>
    private void AppendFill(Color? fill)
    {
        if (fill is { } f && f != Color.Black)
            _sb.Append(" fill=\"").Append(f.ToHex()).Append('"');
    }

    private void AppendStroke(Color stroke, double strokeWidth)
    {
        _sb.Append(" stroke=\"").Append(stroke.ToHex()).Append("\" stroke-width=\"");
        F3(strokeWidth);
        _sb.Append('"');
    }

    private void AppendPoint((double X, double Y) p)
    {
        F2(p.X);
        _sb.Append(',');
        F2(p.Y);
    }

    public void DrawLine(double x1, double y1, double x2, double y2,
        Color? stroke = null, double strokeWidth = 0.1,
        (double On, double Off)? dash = null, LineCap cap = LineCap.Butt)
    {
        _sb.Append("  <line");
        Attr("x1", x1);
        Attr("y1", y1);
        Attr("x2", x2);
        Attr("y2", y2);
        AppendStroke(stroke ?? Color.Black, strokeWidth);
        if (dash is { } d)
        {
            _sb.Append(" stroke-dasharray=\"");
            F2(d.On);
            _sb.Append(' ');
            F2(d.Off);
            _sb.Append('"');
        }
        // SVG default linecap is butt; only emit the attribute when rounding.
        if (cap == LineCap.Round)
            _sb.Append(" stroke-linecap=\"round\"");
        AppendSource();
        _sb.Append("/>").AppendLine();
    }

    public void DrawRectangle(double x, double y, double width, double height,
        Color? fill = null, Color? stroke = null, double strokeWidth = 0)
    {
        _sb.Append("  <rect");
        Attr("x", x);
        Attr("y", y);
        Attr("width", width);
        Attr("height", height);
        if (fill is { } f)
            AppendFill(f);                   // black omitted (SVG default), non-black emitted
        else
            _sb.Append(" fill=\"none\"");    // an explicitly UNFILLED rect
        if (stroke is { } s)
            AppendStroke(s, strokeWidth);
        AppendSource();
        _sb.Append("/>").AppendLine();
    }

    public void DrawFilledQuad((double X, double Y) p0, (double X, double Y) p1,
        (double X, double Y) p2, (double X, double Y) p3, Color fill)
    {
        _sb.Append("  <polygon points=\"");
        AppendPoint(p0);
        _sb.Append(' ');
        AppendPoint(p1);
        _sb.Append(' ');
        AppendPoint(p2);
        _sb.Append(' ');
        AppendPoint(p3);
        _sb.Append('"');
        AppendFill(fill);
        AppendSource();
        _sb.Append("/>").AppendLine();
    }

    public void DrawEllipse(double cx, double cy, double rx, double ry,
        Color? fill = null, Color? stroke = null, double strokeWidth = 0)
    {
        _sb.Append("  <ellipse");
        Attr("cx", cx);
        Attr("cy", cy);
        Attr("rx", rx);
        Attr("ry", ry);
        if (fill is { } f)
            AppendFill(f);                   // black omitted (SVG default), non-black emitted
        else
            _sb.Append(" fill=\"none\"");
        if (stroke is { } s)
            AppendStroke(s, strokeWidth);
        AppendSource();
        _sb.Append("/>").AppendLine();
    }

    public void DrawCircle(double cx, double cy, double r, Color? fill = null)
    {
        _sb.Append("  <circle");
        Attr("cx", cx);
        Attr("cy", cy);
        Attr("r", r);
        AppendFill(fill);
        AppendSource();
        _sb.Append("/>").AppendLine();
    }

    public void DrawClosedBezier(
        (double X, double Y) p0, (double X, double Y) c1, (double X, double Y) c2,
        (double X, double Y) p1, (double X, double Y) c2Back, (double X, double Y) c1Back,
        Color? fill = null, double strokeWidth = 0)
    {
        _sb.Append("  <path d=\"M ");
        AppendPoint(p0);
        _sb.Append(" C ");
        AppendPoint(c1);
        _sb.Append(' ');
        AppendPoint(c2);
        _sb.Append(' ');
        AppendPoint(p1);
        _sb.Append(" C ");
        AppendPoint(c2Back);
        _sb.Append(' ');
        AppendPoint(c1Back);
        _sb.Append(' ');
        AppendPoint(p0);
        _sb.Append(" Z\"");
        AppendFill(fill);
        // A round-cap/round-join stroke in the fill colour rounds the tapered ends,
        // matching LilyPond's slur/tie stencil (fill + round stroke).
        if (strokeWidth > 0)
        {
            _sb.Append(" stroke=\"").Append((fill ?? Color.Black).ToHex()).Append("\" stroke-width=\"");
            F2(strokeWidth);
            _sb.Append("\" stroke-linecap=\"round\" stroke-linejoin=\"round\"");
        }
        AppendSource();
        _sb.Append("/>").AppendLine();
    }

    /// <summary>
    /// The face attribute a music glyph carries: nothing at the score's own size (the
    /// <c>.music</c> CSS class already names that family), and an explicit
    /// <c>font-family</c> — which overrides the class — for any other design. Also RECORDS
    /// the design, so the document embeds exactly the faces the score drew with.
    /// </summary>
    private void AppendMusicFace()
    {
        _usedDesigns?.Add(_musicDesign);
        DesignLog?.Add(_musicDesign);
        // The fallback chain matters: a viewer that has not been given this design's face
        // (the VS Code preview injects Emmentaler itself and omits @font-face entirely) then
        // draws the glyph from the default design instead of showing tofu. It is the wrong
        // OUTLINE by ~0.5% and the right glyph, which is the better of the two failures.
        if (_musicDesign != EmmentalerFaces.DefaultDesign)
            _sb.Append(" font-family=\"").Append(EmmentalerFaces.Family(_musicDesign))
               .Append(", Emmentaler, serif\"");
    }

    /// <summary>
    /// One music <c>&lt;text&gt;</c> element: <c>class="music"</c>, the face, an optional
    /// <c>pointer-events="none"</c> (interactive, non-clickable glyphs), the anchor, the
    /// font size, the fill, the source and the escaped glyph.
    /// </summary>
    private void MusicText(char glyph, double x, double y, double fontSize, Color? fill,
        bool pointerEventsNone)
    {
        _sb.Append("  <text class=\"music\"");
        AppendMusicFace();
        if (pointerEventsNone)
            _sb.Append(" pointer-events=\"none\"");
        Attr("x", x);
        Attr("y", y);
        Attr("font-size", fontSize);
        AppendFill(fill);
        AppendSource();
        _sb.Append('>');
        AppendEscaped(glyph);
        _sb.Append("</text>").AppendLine();
    }

    public void DrawGlyph(char glyph, double x, double y, double fontSize, Color? fill = null)
        => MusicText(glyph, x, y, fontSize, fill, pointerEventsNone: false);

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
        MusicText(glyph, x, y, fontSize, fill, pointerEventsNone: true);
        HitRect(x, y - inkHeight / 2, inkWidth, inkHeight);
    }

    private void HitRect(double x, double y, double width, double height)
    {
        _sb.Append("  <rect class=\"nh-hit\"");
        Attr("x", x);
        Attr("y", y);
        Attr("width", width);
        Attr("height", height);
        _sb.Append(" fill=\"none\" pointer-events=\"all\"");
        AppendSource();
        _sb.Append("/>").AppendLine();
    }

    public void DrawHitRect(double x, double y, double width, double height)
    {
        // Interactive preview only: a transparent click target (the nh-hit class
        // keeps it out of the webview's highlight recolor, like the notehead's).
        if (!_interactive) return;
        HitRect(x, y, width, height);
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
        MusicText(glyph, x, y, fontSize, fill, pointerEventsNone: true);
    }

    public void DrawText(string text, double x, double y, double fontSize,
        TextRole role, FontStyle style = FontStyle.Regular,
        TextAnchor anchor = TextAnchor.Start, Color? fill = null,
        VerticalAnchor verticalAnchor = VerticalAnchor.Baseline)
    {
        _sb.Append("  <text");
        Attr("x", x);
        Attr("y", y);
        Attr("font-size", fontSize);
        string? family = FamilyAttributeFor(role);
        // The document root names the bundled serif (SvgDocumentContext.WriteHeader), so a
        // role that resolves to it inherits and emits nothing — an element attribute
        // still overrides the inherited one where a role was bound to something else.
        if (family != null)
            _sb.Append(" font-family=\"").Append(EscapeAttr(family)).Append('"');
        if ((style & FontStyle.Bold) != 0)
            _sb.Append(" font-weight=\"bold\"");
        if ((style & FontStyle.Italic) != 0)
            _sb.Append(" font-style=\"italic\"");
        if (anchor != TextAnchor.Start)
            _sb.Append(anchor == TextAnchor.Middle ? " text-anchor=\"middle\"" : " text-anchor=\"end\"");
        if (verticalAnchor != VerticalAnchor.Baseline)
            _sb.Append(verticalAnchor == VerticalAnchor.Middle
                ? " dominant-baseline=\"central\""
                : " dominant-baseline=\"hanging\"");
        AppendFill(fill);
        AppendSource();
        _sb.Append('>').Append(EscapeText(text)).Append("</text>").AppendLine();
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
            _sb.Append("  <g transform=\"translate(");
            F2(transform.TranslateX);
            _sb.Append(',');
            F2(transform.TranslateY);
            _sb.Append(") scale(");
            F4(transform.ScaleX);
            _sb.Append(',');
            F4(transform.ScaleY);
            _sb.Append(")\">").AppendLine();
        }
        return new ScopeAction(() =>
        {
            _sb.AppendLine("  </g>");
        });
    }

    /// <summary>Interactive preview only (see <see cref="IDrawingContext.BeginLabeledGroup"/>):
    /// static export emits nothing, so exported files stay byte for byte what they were.</summary>
    public IDisposable BeginLabeledGroup(string label)
    {
        if (!_interactive)
            return NullScope.Instance;
        _sb.Append("  <g class=\"").Append(label).Append("\">").AppendLine();
        return new ScopeAction(() => _sb.AppendLine("  </g>"));
    }

    /// <summary>
    /// The source attribute(s) of the element being emitted: <c>data-pos</c>, and in
    /// interactive mode the <c>data-alt</c> aliases. Also feeds the capture log.
    /// </summary>
    private void AppendSource()
    {
        if (!_currentSourcePosition.HasValue)
            return;
        int pos = _currentSourcePosition.Value;
        _sb.Append(" data-pos=\"");
        AppendInt(pos);
        _sb.Append('"');
        if (SourceLog is { } capture)
        {
            capture.Add(pos);
            if (_currentAliases is { Count: > 0 })
                capture.AddRange(_currentAliases);
        }
        // data-alt lists the extra highlight offsets: a caret on any of them lights this
        // element too (the webview matches data-pos OR a data-alt member); the click still
        // uses data-pos. Only in interactive mode (aliases are null otherwise).
        if (_currentAliases is { Count: > 0 } aliases)
        {
            _sb.Append(" data-alt=\"");
            for (int i = 0; i < aliases.Count; i++)
            {
                if (i > 0)
                    _sb.Append(' ');
                AppendInt(aliases[i]);
            }
            _sb.Append('"');
        }
    }

    private void AppendInt(int value)
    {
        Span<char> buffer = stackalloc char[16];
        if (value.TryFormat(buffer, out int written, default, Inv))
            _sb.Append(buffer[..written]);
        else
            _sb.Append(value.ToString(Inv));
    }

    private void AppendEscaped(char c)
    {
        switch (c)
        {
            case '<': _sb.Append("&lt;"); break;
            case '>': _sb.Append("&gt;"); break;
            case '&': _sb.Append("&amp;"); break;
            default: _sb.Append(c); break;
        }
    }

    private static string EscapeText(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static string EscapeAttr(string s) => EscapeText(s).Replace("\"", "&quot;");
}
