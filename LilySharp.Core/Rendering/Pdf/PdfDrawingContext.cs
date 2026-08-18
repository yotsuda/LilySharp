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

using System.Collections.Concurrent;
using PdfSharpCore.Drawing;
using SkiaSharp;

namespace LilySharp.Core.Rendering.Pdf;

/// <summary>PDF implementation of <see cref="IDrawingContext"/>.</summary>
internal sealed class PdfDrawingContext : IDrawingContext
{
    private readonly XGraphics _gfx;
    private readonly double _scale;  // points per staff-space
    private readonly double _originPt; // page-margin offset applied to positions
    // Where a covering system face gets registered so PdfSharpCore can embed it; null in
    // the tests that build a context directly, which then draw .notdef as before.
    private readonly EmmentalerFontResolver? _fontResolver;

    // XFont is immutable and reused across every glyph/text of the same face+size;
    // a full score draws thousands, so cache them instead of allocating per draw.
    private readonly Dictionary<(string Family, double Size, XFontStyle Style), XFont> _fontCache = new();

    /// <summary>The Emmentaler design music glyphs are drawn from — see
    /// <see cref="IDrawingContext.MusicFace"/>.</summary>
    private int _musicDesign = EmmentalerFaces.DefaultDesign;

    /// <summary>Which face each text role is drawn in — the score's <c>font</c>
    /// directive, resolved. The document hands its own down at <c>BeginPage</c>.</summary>
    private readonly TextFontPlan _plan;

    public PdfDrawingContext(XGraphics gfx, double pointsPerSpace, double originPt = 0,
        EmmentalerFontResolver? fontResolver = null, TextFontPlan? plan = null)
    {
        _gfx = gfx;
        _scale = pointsPerSpace;
        _originPt = originPt;
        _fontResolver = fontResolver;
        _plan = plan ?? TextFontPlan.Default;
    }

    // Positions are offset by the page margin; sizes (T) are not.
    private double X(double s) => s * _scale + _originPt;
    private double T(double s) => s * _scale;
    private static XColor ToXColor(Color? c)
    {
        var col = c ?? Color.Black;
        return XColor.FromArgb(col.A, col.R, col.G, col.B);
    }

    private XFont GetFont(string family, double size, XFontStyle style = XFontStyle.Regular)
    {
        var key = (family, size, style);
        if (!_fontCache.TryGetValue(key, out var font))
            _fontCache[key] = font = new XFont(family, size, style);
        return font;
    }

    public void DrawLine(double x1, double y1, double x2, double y2,
        Color? stroke = null, double strokeWidth = 0.1,
        (double On, double Off)? dash = null, LineCap cap = LineCap.Butt)
    {
        var pen = new XPen(ToXColor(stroke), T(strokeWidth));
        if (dash is { } d)
        {
            pen.DashStyle = XDashStyle.Custom;
            // PdfSharpCore's DashPattern is in multiples of the pen WIDTH, but d.On/d.Off
            // are in staff-spaces (like the SVG/PNG backends). Divide by the staff-space
            // stroke width so the dash lengths match the other backends.
            pen.DashPattern = strokeWidth > 0
                ? new[] { d.On / strokeWidth, d.Off / strokeWidth }
                : new[] { d.On, d.Off };
        }
        pen.LineCap = cap == LineCap.Round ? XLineCap.Round : XLineCap.Flat;
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

    public void DrawFilledQuad((double X, double Y) p0, (double X, double Y) p1,
        (double X, double Y) p2, (double X, double Y) p3, Color fill)
    {
        var pts = new[]
        {
            new XPoint(X(p0.X), X(p0.Y)), new XPoint(X(p1.X), X(p1.Y)),
            new XPoint(X(p2.X), X(p2.Y)), new XPoint(X(p3.X), X(p3.Y)),
        };
        _gfx.DrawPolygon(new XSolidBrush(ToXColor(fill)), pts, XFillMode.Alternate);
    }

    public void DrawClosedBezier(
        (double X, double Y) p0, (double X, double Y) c1, (double X, double Y) c2,
        (double X, double Y) p1, (double X, double Y) c2Back, (double X, double Y) c1Back,
        Color? fill = null, double strokeWidth = 0)
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
        var brush = new XSolidBrush(ToXColor(fill));
        _gfx.DrawPath(brush, path);
        // Round-cap/join stroke rounds the tapered ends (LilyPond slur/tie stencil).
        if (strokeWidth > 0)
        {
            var pen = new XPen(ToXColor(fill), T(strokeWidth))
            {
                LineCap = XLineCap.Round,
                LineJoin = XLineJoin.Round,
            };
            _gfx.DrawPath(pen, path);
        }
    }

    public void DrawGlyph(char glyph, double x, double y, double fontSize, Color? fill = null)
    {
        // The FACE follows the music-face scope; the SIZE does not change with it — every
        // Emmentaler design's em is four of its own staff spaces (IDrawingContext.MusicFace).
        var font = GetFont(EmmentalerFaces.Family(_musicDesign), T(fontSize));
        // SVG <text y="..."> places the baseline at y; PdfSharpCore's
        // XStringFormats.BaseLineLeft matches that, so we draw at (x, y).
        _gfx.DrawString(glyph.ToString(), font,
            new XSolidBrush(ToXColor(fill)),
            X(x), X(y), XStringFormats.BaseLineLeft);
    }

    public void DrawText(string text, double x, double y, double fontSize,
        TextRole role, FontStyle style = FontStyle.Regular,
        TextAnchor anchor = TextAnchor.Start, Color? fill = null,
        VerticalAnchor verticalAnchor = VerticalAnchor.Baseline)
    {
        // One face per run: PdfSharpCore's XFont is a single family, and the resolver
        // answers for whichever of the chain's names it was configured with. The FIRST
        // is asked for — a per-CHARACTER fallback still runs below for anything it
        // cannot draw, which is the same mechanism a CJK title already relies on.
        var face = _plan.Resolve(role);
        bool sans = face.Family == TextFontFamily.Sans;
        string fontFamily = face.IsBundled
            ? (sans ? "sans-serif" : "serif")
            : face.Names[0];
        var pdfStyle = ((style & FontStyle.Bold) != 0, (style & FontStyle.Italic) != 0) switch
        {
            (true, true) => XFontStyle.BoldItalic,
            (true, false) => XFontStyle.Bold,
            (false, true) => XFontStyle.Italic,
            _ => XFontStyle.Regular,
        };
        var font = GetFont(fontFamily, T(fontSize), pdfStyle);
        // SVG dominant-baseline parity: shift Y so the baseline sits where the
        // requested anchor would visually land. cap-height ≈ 0.7 × em, so
        // central baseline ≈ 0.35 × em below central, hanging ≈ 0.8 × em below top.
        double drawY = verticalAnchor switch
        {
            VerticalAnchor.Middle => y + fontSize * 0.35,
            VerticalAnchor.Hanging => y + fontSize * 0.8,
            _ => y,
        };
        var brush = new XSolidBrush(ToXColor(fill));

        // A BUNDLED face is drawn at the positions the reservation computed (DrawShaped).
        // A bound `font "X"` keeps PdfSharpCore's own layout, and the reason CHANGED on
        // 2026-08-18: it used to be that the engine never measured that file either
        // (HANDOFF §2F's gap, now closed — ScoreTextMetrics measures it). What is left is
        // this backend's: WITHOUT `embedded` a named face is served by the bundled
        // stand-in (EmmentalerFontResolver), so the glyphs on the page are not the ones the
        // box was measured from and shaping them at those positions would be worse, not
        // better. WITH `embedded` the real program is there and the shaped path is
        // reachable — that is the next step, and it needs the resolved TextFace threaded to
        // the drawing side.
        if (face.IsBundled)
        {
            double width = TextFontMetrics.Advance(text, fontSize, sans, style);
            double left = anchor switch
            {
                TextAnchor.Middle => x - width / 2,
                TextAnchor.End => x - width,
                _ => x,
            };
            DrawShaped(text, left, drawY, fontSize, sans, style, font, brush);
            return;
        }

        var fmt = anchor switch
        {
            TextAnchor.Middle => XStringFormats.BaseLineCenter,
            TextAnchor.End => XStringFormats.BaseLineRight,
            _ => XStringFormats.BaseLineLeft,
        };
        _gfx.DrawString(text, font, brush, X(x), X(drawY), fmt);
    }

    /// <summary>
    /// Draw the string CLUSTER BY CLUSTER at the positions the layout reserved for them.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/pango-font.cc:407-435 Pango_font::pango_item_string_stencil — the
    ///   loop that builds <c>glyph_exprs</c> emits ONE descriptor per SHAPED glyph, and
    ///   lily/pango-font.cc:494-503 Pango_font::pango_item_string_stencil hands the list to the
    ///   backend as a <c>glyph-string</c>. LilyPond's page is glyphs at positions.
    /// ⚠️ PdfSharpCore emits the whole string as one <c>Tj</c> with no positioning array, so a
    /// PDF viewer advances by the font's <c>/Widths</c> and nothing kerns. The engine reserves
    /// the SHAPED width (<c>TextFontMetrics.Advance</c> — HarfBuzz, the way LilyPond
    /// measures through Pango), so the ink and its box were different widths: measured
    /// 2026-08-03 at 3.16 staff spaces on a ten-letter title. Nothing caught it because the
    /// snapshot corpus holds no PDF at all.
    /// <para>
    /// ⚠️ NOT PORTED — backend-blocked: CHARACTERS AT THE CLUSTERS' POSITIONS, not glyphs at
    /// their own. LP has the counterpart (glyphs at positions, the REF above), so this is a
    /// knowing divergence and not a Lily#-own quantity (§5.2 audit, session 158);
    /// PdfSharpCore has no glyph-level API to hand a glyph id to, so each cluster's SOURCE TEXT
    /// is drawn where the shaped run put that cluster.
    ///   departs from: :411-426, one <c>glyph_desc</c> per <c>pgs->glyphs[i]</c>. Between
    ///     clusters this agrees with LilyPond exactly — every cluster starts at its reserved
    ///     position, so pair kerning is carried and the run ends where its box ends. INSIDE a
    ///     cluster it does not: a ligature is one shaped glyph over several characters, and
    ///     those characters are drawn side by side within the ligature's advance.
    ///   goes away when: this backend can be handed a glyph id — a PdfSharpCore that exposes
    ///     one, or another PDF writer. Nothing smaller fixes it: the alternative inside
    ///     PdfSharpCore is to lay the string out by its own widths, which is a SECOND idea of
    ///     the width and the defect this method exists to remove.
    ///   observed by: BackendKerningTests.PdfPlacesTextWhereTheLayoutReservedIt, which reads
    ///     the content stream's placements — so it pins the between-cluster half and is blind
    ///     to the inside-cluster half. ⚠️ Nothing observes the ligature interior, and this
    ///     backend drew every PAIR that way until 2026-08-03, so the change loses nothing that
    ///     was ever right.
    /// </para>
    /// </remarks>
    private void DrawShaped(string text, double left, double baselineY, double fontSize,
        bool sans, FontStyle style, XFont font, XBrush brush)
    {
        var glyphs = TextFontMetrics.ShapeRun(text, fontSize, sans, style);
        for (int i = 0; i < glyphs.Count; i++)
        {
            // The source characters this glyph stands for: from its cluster to the next one's.
            int start = glyphs[i].Cluster;
            int end = i + 1 < glyphs.Count ? glyphs[i + 1].Cluster : text.Length;
            if (end <= start)
                continue;   // a mark or a second glyph inside one cluster — already drawn
            // A character the bundled face cannot draw is shaped to .notdef, and .notdef in a
            // PDF is BLANK — a Japanese title left no ink at all. ShapeRun reports the source
            // code point for exactly this: draw it in a face that has the glyph.
            var fallback = glyphs[i].MissingCodepoint is int cp
                ? FallbackFont(cp, fontSize, style)
                : default;
            var clusterFont = fallback.Font ?? font;
            double px = X(left + glyphs[i].X);
            double py = X(baselineY);
            if (fallback.Oblique)
                DrawObliqued(text[start..end], clusterFont, brush, px, py);
            else
                _gfx.DrawString(text[start..end], clusterFont, brush, px, py,
                    XStringFormats.BaseLineLeft);
        }
    }

    /// <summary>
    /// The shear a SYNTHESISED oblique applies: how far x moves per unit of height above
    /// the baseline.
    /// </summary>
    /// <remarks>
    /// MEASURED, not picked. The PNG backend's CJK slant comes from the face
    /// <c>SKFontManager.MatchCharacter</c> returns for an italic request, which on Windows
    /// is a DirectWrite OBLIQUE SIMULATION. Drawing 山田太郎 at 1000 units through the
    /// upright and the italic face and differencing the ink widths gives
    /// (4060.5 − 3802.7) / 901.4 = 0.2859 — 16.0°. This backend uses that number so the two
    /// RASTERISING backends agree.
    /// <para>
    /// ⚠️ It cannot agree with everything, and does not claim to: the SVG backend writes
    /// <c>font-style="italic"</c> and leaves the synthesis to the VIEWER, and another
    /// platform's font manager would choose its own angle. What is pinned here is PDF
    /// against PNG.
    /// </para>
    /// </remarks>
    private const double ObliqueShear = 0.2859;

    /// <summary>The same slant as an ANGLE IN DEGREES, which is what
    /// <see cref="XGraphics.ShearTransform(double, double)"/> takes.</summary>
    /// <remarks>
    /// ⚠️ It takes DEGREES, not a shear factor — WPF's Skew semantics. Handing it the
    /// factor 0.2859 emitted <c>1 -0 0.0049899 1 … cm</c>, i.e. tan(0.2859°): a slant of
    /// one two-hundredth, invisible on the page and indistinguishable from the upright
    /// text it was meant to replace. Read off the content stream, not guessed.
    /// </remarks>
    private static readonly double ObliqueDegrees = Math.Atan(ObliqueShear) * 180.0 / Math.PI;

    /// <summary>
    /// Draws one run with a synthesised oblique — the slant a face without a real italic
    /// gets. The TEXT is sheared by the page transform; the embedded FONT PROGRAM is not
    /// touched, which is what every PDF producer does for a fake italic.
    /// </summary>
    /// <remarks>
    /// Sheared about the run's own baseline origin (translate there first), or the shear
    /// would displace the text by an amount proportional to its distance from the page
    /// origin. The sign is negative because this frame is Y-DOWN: above the baseline y is
    /// negative, and the top has to lean RIGHT.
    /// </remarks>
    private void DrawObliqued(string text, XFont font, XBrush brush, double xPt, double yPt)
    {
        var state = _gfx.Save();
        _gfx.TranslateTransform(xPt, yPt);
        _gfx.ShearTransform(-ObliqueDegrees, 0);
        _gfx.DrawString(text, font, brush, 0, 0, XStringFormats.BaseLineLeft);
        _gfx.Restore(state);
    }

    /// <summary>The covering family for one codepoint, and whether its run has to be
    /// sheared because the bytes being embedded carry no slant of their own.</summary>
    private readonly record struct FallbackChoice(string? Family, bool Oblique);

    // Per (codepoint, style). Cached because MatchCharacter is a system font-manager query
    // and a CJK lyric asks per character.
    private static readonly ConcurrentDictionary<(int Cp, FontStyle Style), FallbackChoice>
        FallbackFamilies = new();

    /// <summary>
    /// A PdfSharpCore font for <paramref name="codepoint"/> in a system face that has the
    /// glyph, registered with the resolver so its bytes are subset-embedded. Null when no
    /// installed face covers the character or its licence forbids embedding.
    /// </summary>
    /// <remarks>
    /// The face is picked with <c>SKFontManager.MatchCharacter</c> — the same query
    /// <c>PngDrawingContext.ResolveFace</c> makes, so the two backends land on the same font
    /// rather than each choosing its own idea of "a Japanese face".
    /// <para>
    /// ⚠️ EMBEDDING IS NOT OPTIONAL HERE, unlike <c>font "X" [embedded]</c>. That switch
    /// exists so a NAMED font is never embedded without the author asking; this path is
    /// reached only when the alternative is dropping the author's text on the floor, and
    /// PdfSharpCore has no way to reference a face it has no bytes for. A face whose fsType
    /// RESTRICTS embedding is still refused — that one is a licence prohibition, not a
    /// default — and its characters go back to being blank.
    /// </para>
    /// <para>
    /// ⚠️ THE POSITION IS THE RESERVED ONE, not this face's own advance.
    /// <c>TextFontMetrics.MissingGlyphAdvance</c> already spent a full em on each CJK
    /// character, which is what a CJK face actually advances, so the cluster positions the
    /// layout paid for are the right ones to draw at.
    /// </para>
    /// </remarks>
    private (XFont? Font, bool Oblique) FallbackFont(int codepoint, double fontSize, FontStyle style)
    {
        if (_fontResolver == null)
            return default;
        var choice = FallbackFamilies.GetOrAdd((codepoint, style), key =>
        {
            bool bold = (key.Style & FontStyle.Bold) != 0;
            bool italic = (key.Style & FontStyle.Italic) != 0;
            var matched = SKFontManager.Default.MatchCharacter(
                null,
                new SKFontStyle(
                    bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
                    SKFontStyleWidth.Normal,
                    italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright),
                null, key.Cp);
            if (matched == null)
                return default;
            if (FontEmbedInfo.Classify(matched) == FontEmbedInfo.FontEmbedClass.Forbidden)
                return default;
            var bytes = FontEmbedInfo.TryGetFontBytes(matched);
            if (bytes == null)
                return default;
            _fontResolver.RegisterFallback(matched.FamilyName, bold, italic, bytes,
                simulateBold: bold && !matched.IsBold,
                simulateItalic: italic && !matched.IsItalic);
            // Whether the run needs a synthesised slant is decided by the BYTES, not by the
            // face the matcher handed back.
            // ⚠️ The two disagree, and that is the whole reason a CJK composer used to come
            // out upright. Asked for an italic, the Windows matcher returns a face reporting
            // slant=Oblique — but that is a DirectWrite SIMULATION, applied at draw time and
            // absent from the font FILE. OpenStream therefore yields the upright outlines,
            // which is what gets embedded, so `matched.IsItalic` says "already slanted" about
            // a face whose bytes are not. Reading the extracted bytes back answers the only
            // question that matters: does what we are embedding lean?
            return new FallbackChoice(matched.FamilyName, italic && !BytesAreItalic(bytes));
        });
        if (choice.Family == null)
            return default;
        var pdfStyle = ((style & FontStyle.Bold) != 0, (style & FontStyle.Italic) != 0) switch
        {
            (true, true) => XFontStyle.BoldItalic,
            (true, false) => XFontStyle.Bold,
            (false, true) => XFontStyle.Italic,
            _ => XFontStyle.Regular,
        };
        return (GetFont(choice.Family, T(fontSize), pdfStyle), choice.Oblique);
    }

    /// <summary>Does this font program carry a slant of its own? Read from the extracted
    /// bytes, so a simulation applied by the platform's font manager cannot answer for
    /// them. False when the bytes cannot be read back at all — a run then leans, which is
    /// the recoverable side of the guess.</summary>
    private static bool BytesAreItalic(byte[] bytes)
    {
        try
        {
            using var data = SKData.CreateCopy(bytes);
            using var face = SKTypeface.FromData(data);
            return face?.IsItalic ?? false;
        }
        catch
        {
            return false;
        }
    }

    public IDisposable Source(int sourcePosition)
    {
        // PDF backend ignores source-position metadata. A future enhancement
        // could emit PDF link annotations or named destinations.
        return NullScope.Instance;
    }

    public IDisposable MusicFace(int rounded)
    {
        var prev = _musicDesign;
        _musicDesign = rounded;
        return new ScopeAction(() => _musicDesign = prev);
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
}
