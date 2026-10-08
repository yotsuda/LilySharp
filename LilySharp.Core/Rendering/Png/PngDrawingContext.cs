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

using LilySharp.Core.Svg.Layout;
using SkiaSharp;

namespace LilySharp.Core.Rendering.Png;

/// <summary>PNG implementation of <see cref="IDrawingContext"/>.</summary>
internal sealed class PngDrawingContext : IDrawingContext, IDisposable
{
    private readonly SKCanvas _canvas;
    private readonly double _scale;
    private readonly FontCache _fonts;

    /// <summary>Releases the per-page font handles (typefaces loaded from disk and
    /// the SKFonts derived from them). Called by PngDocumentContext.EndPage.</summary>
    public void Dispose() => _fonts.Dispose();

    /// <summary>The Emmentaler design music glyphs are drawn from — see
    /// <see cref="IDrawingContext.MusicFace"/>.</summary>
    private int _musicDesign = MusicFont.Current.DefaultDesign;

    /// <summary>Which face each text role is drawn in — the score's <c>font</c>
    /// directive, resolved. The document hands its own down at <c>BeginPage</c>.</summary>
    private readonly TextFontPlan _plan;

    /// <summary>
    /// The SAME resolution the layout reserved through — role and style in, the font PROGRAM
    /// out.
    /// </summary>
    /// <remarks>
    /// ⚠️ Built from the plan rather than handed in, and that is safe because
    /// <see cref="ScoreTextMetrics.Face"/> is a pure function of the plan: two instances of
    /// the same plan answer identically, and the cache inside is a speed-up, not the answer.
    /// Sharing the layout's instance would be an extra parameter on every backend for no
    /// difference in what gets drawn.
    /// </remarks>
    private readonly ScoreTextMetrics _metrics;

    public PngDrawingContext(SKCanvas canvas, double pixelsPerSpace, string? fontDirectory,
        TextFontPlan? plan = null)
    {
        _canvas = canvas;
        _scale = pixelsPerSpace;
        _fonts = new FontCache(fontDirectory);
        _plan = plan ?? TextFontPlan.Default;
        _metrics = new ScoreTextMetrics(_plan);
    }

    private float X(double s) => (float)(s * _scale);
    private float T(double s) => (float)(s * _scale);
    private static SKColor ToSKColor(Color? c)
    {
        var col = c ?? Color.Black;
        return new SKColor(col.R, col.G, col.B, col.A);
    }

    public void DrawLine(double x1, double y1, double x2, double y2,
        Color? stroke = null, double strokeWidth = 0.1,
        (double On, double Off)? dash = null, LineCap cap = LineCap.Butt)
    {
        using var paint = new SKPaint
        {
            Color = ToSKColor(stroke),
            StrokeWidth = T(strokeWidth),
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            StrokeCap = cap == LineCap.Round ? SKStrokeCap.Round : SKStrokeCap.Butt,
        };
        if (dash is { } d)
            paint.PathEffect = SKPathEffect.CreateDash(
                new[] { (float)T(d.On), (float)T(d.Off) }, 0);
        _canvas.DrawLine(X(x1), X(y1), X(x2), X(y2), paint);
        paint.PathEffect?.Dispose();
    }

    public void DrawRectangle(double x, double y, double width, double height,
        Color? fill = null, Color? stroke = null, double strokeWidth = 0)
    {
        var rect = SKRect.Create(X(x), X(y), T(width), T(height));
        if (fill is { } f)
        {
            using var fp = new SKPaint { Color = ToSKColor(f), Style = SKPaintStyle.Fill, IsAntialias = true };
            _canvas.DrawRect(rect, fp);
        }
        if (stroke is { } s)
        {
            using var sp = new SKPaint { Color = ToSKColor(s), StrokeWidth = T(strokeWidth), Style = SKPaintStyle.Stroke, IsAntialias = true };
            _canvas.DrawRect(rect, sp);
        }
    }

    public void DrawFilledQuad((double X, double Y) p0, (double X, double Y) p1,
        (double X, double Y) p2, (double X, double Y) p3, Color fill)
    {
        using var path = new SKPath();
        path.MoveTo(X(p0.X), X(p0.Y));
        path.LineTo(X(p1.X), X(p1.Y));
        path.LineTo(X(p2.X), X(p2.Y));
        path.LineTo(X(p3.X), X(p3.Y));
        path.Close();
        using var paint = new SKPaint { Color = ToSKColor(fill), Style = SKPaintStyle.Fill, IsAntialias = true };
        _canvas.DrawPath(path, paint);
    }

    public void DrawEllipse(double cx, double cy, double rx, double ry,
        Color? fill = null, Color? stroke = null, double strokeWidth = 0)
    {
        if (fill is { } f)
        {
            using var fp = new SKPaint { Color = ToSKColor(f), Style = SKPaintStyle.Fill, IsAntialias = true };
            _canvas.DrawOval(X(cx), X(cy), T(rx), T(ry), fp);
        }
        if (stroke is { } s)
        {
            using var sp = new SKPaint { Color = ToSKColor(s), StrokeWidth = T(strokeWidth), Style = SKPaintStyle.Stroke, IsAntialias = true };
            _canvas.DrawOval(X(cx), X(cy), T(rx), T(ry), sp);
        }
    }

    public void DrawCircle(double cx, double cy, double r, Color? fill = null)
    {
        using var paint = new SKPaint
        {
            Color = ToSKColor(fill),
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };
        _canvas.DrawCircle(X(cx), X(cy), T(r), paint);
    }

    public void DrawBezier(
        (double X, double Y) p0, (double X, double Y) c1, (double X, double Y) c2,
        (double X, double Y) p1, Color? stroke = null, double strokeWidth = 0.1)
    {
        using var path = new SKPath();
        path.MoveTo(X(p0.X), X(p0.Y));
        path.CubicTo(X(c1.X), X(c1.Y), X(c2.X), X(c2.Y), X(p1.X), X(p1.Y));
        using var paint = new SKPaint
        {
            Color = ToSKColor(stroke),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = T(strokeWidth),
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            IsAntialias = true,
        };
        _canvas.DrawPath(path, paint);
    }

    public void DrawClosedBezier(
        (double X, double Y) p0, (double X, double Y) c1, (double X, double Y) c2,
        (double X, double Y) p1, (double X, double Y) c2Back, (double X, double Y) c1Back,
        Color? fill = null, double strokeWidth = 0)
    {
        using var path = new SKPath();
        path.MoveTo(X(p0.X), X(p0.Y));
        path.CubicTo(X(c1.X), X(c1.Y), X(c2.X), X(c2.Y), X(p1.X), X(p1.Y));
        path.CubicTo(X(c2Back.X), X(c2Back.Y), X(c1Back.X), X(c1Back.Y), X(p0.X), X(p0.Y));
        path.Close();
        using var paint = new SKPaint
        {
            Color = ToSKColor(fill),
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };
        _canvas.DrawPath(path, paint);
        // Round-cap/join stroke rounds the tapered ends (LilyPond slur/tie stencil).
        if (strokeWidth > 0)
        {
            using var strokePaint = new SKPaint
            {
                Color = ToSKColor(fill),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = T(strokeWidth),
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round,
                IsAntialias = true,
            };
            _canvas.DrawPath(path, strokePaint);
        }
    }

    public void DrawGlyph(char glyph, double x, double y, double fontSize, Color? fill = null)
    {
        // The FACE follows the music-face scope; the SIZE does not change with it — every
        // Emmentaler design's em is four of its own staff spaces (IDrawingContext.MusicFace).
        var font = _fonts.GetFont(MusicFont.Current.FaceFamily(_musicDesign), T(fontSize), FontStyle.Regular);
        using var paint = new SKPaint
        {
            Color = ToSKColor(fill),
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            SubpixelText = true,
        };
        _canvas.DrawText(glyph.ToString(), X(x), X(y), font, paint);
    }

    public void DrawText(string text, double x, double y, double fontSize,
        TextRole role, FontStyle style = FontStyle.Regular,
        TextAnchor anchor = TextAnchor.Start, Color? fill = null,
        VerticalAnchor verticalAnchor = VerticalAnchor.Baseline)
    {
        using var paint = new SKPaint
        {
            Color = ToSKColor(fill),
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            SubpixelText = true,
        };
        var placed = PlaceText(text, x, y, fontSize, role, style, anchor, verticalAnchor, paint);
        float cx = placed.X;
        foreach (var (segment, typeface) in placed.Runs)
        {
            paint.Typeface = typeface;
            using var runFont = new SKFont(typeface, T(fontSize)) { Edging = SKFontEdging.SubpixelAntialias };
            if (ReferenceEquals(typeface, placed.Reserved) && placed.Measured is TextFace shaped)
                DrawShaped(segment, cx, placed.Y, fontSize, shaped, runFont, paint);
            else
                _canvas.DrawText(segment, cx, placed.Y, runFont, paint);
            cx += RunWidth(segment, typeface, placed.Reserved, placed.Measured, fontSize, paint);
        }
    }

    /// <summary>
    /// The ink box of a <see cref="DrawText"/> call, in this context's pixels — the glyphs at
    /// the very positions <see cref="DrawText"/> draws them (one placement, <see cref="PlaceText"/>).
    /// Empty when the text has no ink. For <c>lysc boxes</c> (BoxesDrawingContext).
    /// </summary>
    internal SKRect MeasureTextInk(string text, double x, double y, double fontSize,
        TextRole role, FontStyle style, TextAnchor anchor, VerticalAnchor verticalAnchor)
    {
        using var paint = new SKPaint { SubpixelText = true };
        var placed = PlaceText(text, x, y, fontSize, role, style, anchor, verticalAnchor, paint);
        var ink = SKRect.Empty;
        float cx = placed.X;
        foreach (var (segment, typeface) in placed.Runs)
        {
            paint.Typeface = typeface;
            using var runFont = new SKFont(typeface, T(fontSize));
            if (ReferenceEquals(typeface, placed.Reserved) && placed.Measured is TextFace shaped)
            {
                var glyphs = TextFontMetrics.ShapeRun(segment, fontSize, shaped);
                if (glyphs.Count > 0)
                {
                    for (int i = 0; i < glyphs.Count; i++)
                        ink = Union(ink, Offset(OutlineBounds(runFont, glyphs[i].GlyphId), cx + T(glyphs[i].X), placed.Y));
                }
            }
            else
            {
                // Unshaped, as DrawText draws it: each glyph at the sum of the advances before it.
                var ids = GlyphIds(runFont, segment);
                var advances = new float[ids.Length];
                runFont.GetGlyphWidths(ids, advances, null);
                float pen = cx;
                for (int i = 0; i < ids.Length; i++)
                {
                    ink = Union(ink, Offset(OutlineBounds(runFont, ids[i]), pen, placed.Y));
                    pen += advances[i];
                }
            }
            cx += RunWidth(segment, typeface, placed.Reserved, placed.Measured, fontSize, paint);
        }
        return ink;
    }

    /// <summary>The ink box of a <see cref="DrawGlyph"/> call, in this context's pixels, from
    /// the face <see cref="DrawGlyph"/> draws it in. For <c>lysc boxes</c>.</summary>
    internal SKRect MeasureGlyphInk(char glyph, double x, double y, double fontSize)
    {
        var font = _fonts.GetFont(MusicFont.Current.FaceFamily(_musicDesign), T(fontSize), FontStyle.Regular);
        var ids = GlyphIds(font, glyph.ToString());
        if (ids.Length == 0)
            return SKRect.Empty;
        var ink = SKRect.Empty;
        foreach (var id in ids)
            ink = Union(ink, Offset(OutlineBounds(font, id), X(x), X(y)));
        return ink;
    }

    /// <summary>A glyph's OUTLINE box at the origin — tight. ⚠️ Not GetGlyphWidths' bounds:
    /// those are padded (measured 2026-10-05: noteheads.s0 2.0625 × 1.125 staff spaces against
    /// an outline of 1.962 × 1.09, which is also what the PNG's pixels show).</summary>
    private static SKRect OutlineBounds(SKFont font, ushort id)
    {
        using var path = font.GetGlyphPath(id);
        return path is null || path.IsEmpty ? SKRect.Empty : path.TightBounds;
    }

    private static ushort[] GlyphIds(SKFont font, string text)
    {
        var ids = new ushort[font.CountGlyphs(text)];
        font.GetGlyphs(text, ids);
        return ids;
    }

    private static SKRect Offset(SKRect r, float dx, float dy)
        => r.IsEmpty ? r : new SKRect(r.Left + dx, r.Top + dy, r.Right + dx, r.Bottom + dy);

    private static SKRect Union(SKRect a, SKRect b)
        => a.IsEmpty ? b : b.IsEmpty ? a
            : new SKRect(Math.Min(a.Left, b.Left), Math.Min(a.Top, b.Top), Math.Max(a.Right, b.Right), Math.Max(a.Bottom, b.Bottom));

    /// <summary>Where <see cref="DrawText"/> puts a string: its typeface runs, the reserved
    /// face and the measured one, and the pen's start (anchor and vertical anchor applied).</summary>
    private readonly record struct PlacedText(
        List<(string Segment, SKTypeface Typeface)> Runs, SKTypeface? Reserved, TextFace? Measured,
        float X, float Y);

    /// <summary>The placement half of <see cref="DrawText"/> — ONE home for it, so the box
    /// <see cref="MeasureTextInk"/> reports is the ink <see cref="DrawText"/> lays down.
    /// <paramref name="paint"/> carries the text size the fallback runs are measured at.</summary>
    private PlacedText PlaceText(string text, double x, double y, double fontSize,
        TextRole role, FontStyle style, TextAnchor anchor, VerticalAnchor verticalAnchor, SKPaint paint)
    {
        // WHICH FILE, not which family name. The reservation resolved a role and a style to
        // one font PROGRAM (ScoreTextMetrics.Face, walking the score's chain and taking the
        // first face this machine can read); this asks it the same question and draws from
        // the answer. It used to ask Skia for a FAMILY NAME instead — a second lookup that
        // could land on another file, which is why a bound face had to be drawn unshaped.
        // ⚠️ THE BRACE IS NOT A TEXT FACE. TextRole.SystemBrace names an Emmentaler file that
        // TextFontMetrics does not measure and cannot open, so asking it would fall back to
        // the bundled serif and silently draw a serif "{". No score can bind it either —
        // TextFontPlan.Resolve answers that role before any binding is consulted.
        SKFont font;
        TextFace? measured;
        if (role == TextRole.SystemBrace)
        {
            measured = null;
            font = _fonts.GetFont(TextFontPlan.BraceFaceName, T(fontSize), style);
        }
        else
        {
            var face = _metrics.Face(role, style);
            measured = face;
            font = _fonts.GetFont(face, T(fontSize));
        }

        // Glyph fallback: the reserved face (bundled or named) has no CJK coverage, so
        // Japanese titles/section labels rendered as tofu. Split the text into typeface
        // runs, resolving missing codepoints via SKFontManager.MatchCharacter — the same
        // per-character fallback SVG consumers get from the OS font stack.
        var baseTypeface = font.Typeface ?? SKTypeface.Default;
        var runs = _fonts.SegmentByTypeface(text, baseTypeface, style);

        // The one face the RESERVATION knows — a run still on it is drawn shaped, at the
        // positions layout paid for; a run that fell through to a system fallback is a face
        // TextFontMetrics never opened, so its glyph ids and its widths are its own. Null
        // for the brace, which no reservation measures.
        SKTypeface? reserved = measured is null ? null : baseTypeface;

        paint.TextSize = T(fontSize);

        float tx = X(x);
        if (anchor != TextAnchor.Start)
        {
            float width = 0;
            foreach (var (segment, typeface) in runs)
                width += RunWidth(segment, typeface, reserved, measured, fontSize, paint);
            if (anchor == TextAnchor.Middle) tx -= width / 2f;
            else /* End */ tx -= width;
        }
        // SVG dominant-baseline parity: shift Y so SkiaSharp's baseline lands
        // where the requested anchor visually is. Use SKFont.Metrics for
        // accurate ascent/descent (ascent is negative in Skia).
        float ty = X(y);
        if (verticalAnchor != VerticalAnchor.Baseline)
        {
            var metrics = font.Metrics;
            ty += verticalAnchor switch
            {
                VerticalAnchor.Middle => -(metrics.Ascent + metrics.Descent) / 2f,
                VerticalAnchor.Hanging => -metrics.Ascent,
                _ => 0,
            };
        }
        return new PlacedText(runs, reserved, measured, tx, ty);
    }

    /// <summary>
    /// Draw one run GLYPH BY GLYPH at the positions the layout reserved for them.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/pango-font.cc:407-435 Pango_font::pango_item_string_stencil — the
    ///   loop that builds <c>glyph_exprs</c> emits ONE descriptor per SHAPED glyph
    ///   (<c>pgs->num_glyphs</c>, <c>pgs->glyphs[i]</c>), and lily/pango-font.cc:494-503
    ///   Pango_font::pango_item_string_stencil hands that list to the backend as a
    ///   <c>glyph-string</c>. LilyPond's page is therefore glyphs at positions, never a string
    ///   the backend is left to lay out — which is the whole of what this method does.
    /// ⚠️ <c>SKCanvas.DrawText(string, …)</c> maps code points to glyphs one at a time and
    /// steps by the face's raw advances: no pair kerning and no ligatures, because nothing in
    /// that path shapes. The engine RESERVES the shaped width
    /// (<c>TextFontMetrics.Advance</c>, which is HarfBuzz through Pango's rules, the
    /// way LilyPond measures), so drawing that way put the ink and its box at different
    /// widths — measured 2026-08-03 at 3.16 staff spaces on a ten-letter title, and invisible
    /// because the snapshot corpus holds no PNG at all.
    /// <para>
    /// Positions come from <c>TextFontMetrics.ShapeRun</c>, which is the SAME
    /// computation the reservation is the total of — not a second one that agrees.
    /// </para>
    /// <para>
    /// ⚠️ ONLY FOR THE RESERVED FACE — bundled or named, whichever one the layout opened.
    /// The glyph ids are that file's own, and the fallback typeface a CJK run lands on is a
    /// different file where they would mean other glyphs. Those runs keep the unshaped path
    /// (and the reservation keeps deciding their width by <c>MissingGlyphAdvance</c>, which
    /// is a divergence of its own and not this one).
    /// </para>
    /// </remarks>
    private void DrawShaped(string segment, float originX, float baselineY, double fontSize,
        TextFace face, SKFont runFont, SKPaint paint)
    {
        var glyphs = TextFontMetrics.ShapeRun(segment, fontSize, face);
        if (glyphs.Count == 0)
            return;

        using var builder = new SKTextBlobBuilder();
        var run = builder.AllocatePositionedRun(runFont, glyphs.Count);
        var ids = run.GetGlyphSpan();
        var positions = run.GetPositionSpan();
        for (int i = 0; i < glyphs.Count; i++)
        {
            ids[i] = glyphs[i].GlyphId;
            positions[i] = new SKPoint(originX + T(glyphs[i].X), baselineY);
        }
        using var blob = builder.Build();
        _canvas.DrawText(blob, 0, 0, paint);
    }

    /// <summary>
    /// How far the pen moves over one run — the reserved width for a bundled face, and the
    /// face's own measure for a fallback the reservation never saw.
    /// </summary>
    private float RunWidth(string segment, SKTypeface typeface, SKTypeface? reserved,
        TextFace? measured, double fontSize, SKPaint paint)
    {
        if (ReferenceEquals(typeface, reserved) && measured is TextFace face)
            return T(TextFontMetrics.Advance(segment, fontSize, face));
        // SKPaint.MeasureText takes a string overload where SKFont.MeasureText wants glyph ids.
        var previous = paint.Typeface;
        paint.Typeface = typeface;
        float width = paint.MeasureText(segment);
        paint.Typeface = previous;
        return width;
    }

    public IDisposable Source(int sourcePosition)
    {
        // PNG output has no source-position metadata channel.
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
        int saveCount = _canvas.Save();
        if (!transform.IsIdentity)
        {
            _canvas.Translate(T(transform.TranslateX), T(transform.TranslateY));
            _canvas.Scale((float)transform.ScaleX, (float)transform.ScaleY);
        }
        return new ScopeAction(() => _canvas.RestoreToCount(saveCount));
    }

    /// <summary>
    /// Caches SKTypeface and SKFont instances. Text is addressed by
    /// <see cref="TextFace"/> — the file the reservation opened — and music by FAMILY NAME,
    /// which is how an Emmentaler design and the brace are named.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE TWO KEYS ARE NOT INTERCHANGEABLE and the family-name door is the lossy one: it
    /// ends at <see cref="TryLoadSystem"/>, which substitutes. Nothing routes text through it
    /// today — that was <c>FirstAvailable</c>, a second walk of the score's face chain, and
    /// it is gone because the reservation's walk (<see cref="ScoreTextMetrics.Face"/>) is the
    /// one that decides. <see cref="TryLoadBundledText"/> stays ahead of the system lookup so
    /// that a generic family arriving here in future lands on the file the layout measured
    /// rather than on Times New Roman.
    /// </remarks>
    private sealed class FontCache : IDisposable
    {
        private readonly string? _fontDirectory;
        private readonly Dictionary<string, SKTypeface> _typefaces = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Faces owned by <see cref="TextFontMetrics"/>'s shared cache — held here
        /// but NOT disposed with this page's own handles.</summary>
        private readonly HashSet<SKTypeface> _borrowed = new();
        private readonly Dictionary<string, SKFont> _fonts = new();
        private readonly Dictionary<(TextFace Face, float PixelSize), SKFont> _faceFonts = new();

        public FontCache(string? fontDirectory)
        {
            _fontDirectory = fontDirectory;
        }

        /// <summary>
        /// An SKFont over the very file <see cref="TextFontMetrics"/> measured from.
        /// </summary>
        /// <remarks>
        /// ⚠️ THE TYPEFACE IS BORROWED, never owned. TextFontMetrics caches one instance per
        /// face and measures with it, so it is deliberately NOT put in <c>_typefaces</c>:
        /// there is then nothing for <see cref="Dispose"/> to skip, and no way for this
        /// page's teardown to leave the shared cache holding a dead handle. Only the SKFont
        /// built over it belongs to this page.
        /// </remarks>
        public SKFont GetFont(TextFace face, float pixelSize)
        {
            var key = (face, pixelSize);
            if (_faceFonts.TryGetValue(key, out var cached))
                return cached;
            var made = new SKFont(TextFontMetrics.Typeface(face), pixelSize)
            {
                Edging = SKFontEdging.SubpixelAntialias,
            };
            _faceFonts[key] = made;
            return made;
        }

        public SKFont GetFont(string family, float pixelSize, FontStyle style)
        {
            // Cache SKFont by (family, size, style). DrawGlyph/DrawText call
            // this per glyph/run, so allocating a fresh SKFont every time and
            // only disposing at teardown accumulated thousands per document.
            string key = $"{family.ToLowerInvariant()}|{pixelSize}|{style}";
            if (_fonts.TryGetValue(key, out var cached))
                return cached;

            var typeface = GetTypeface(family, style);
            var font = new SKFont(typeface, pixelSize) { Edging = SKFontEdging.SubpixelAntialias };
            _fonts[key] = font;
            return font;
        }

        private SKTypeface GetTypeface(string family, FontStyle style)
        {
            string key = $"{family.ToLowerInvariant()}|{style}";
            if (_typefaces.TryGetValue(key, out var cached))
                return cached;

            // The bundled text faces come BEFORE the system lookup: the engine reserves
            // space with TextFontMetrics, which reads those files, so drawing anything else
            // would print a font the layout never measured. TryLoadSystem stays for an
            // explicitly named family the document asked for by `font "X"`.
            SKTypeface? tf = TryLoadEmmentaler(family);
            if (tf == null && (tf = TryLoadBundledText(family, style)) != null)
                // BORROWED, not owned: TextFontMetrics caches one instance per face and
                // measures with it. Disposing it here would leave the shared cache holding
                // a dead handle and the NEXT page would draw with it.
                _borrowed.Add(tf);
            tf ??= TryLoadSystem(family, style);
            tf ??= SKTypeface.Default;
            _typefaces[key] = tf;
            return tf;
        }

        private SKTypeface? TryLoadEmmentaler(string family)
        {
            // Every music-font DESIGN is its own file — Emmentaler is optically sized, so a
            // grace's 14 is not the 20 scaled (MusicFont.FaceFile).
            var music = MusicFont.Current;
            string? fileName =
                music.TryParseFamily(family, out int design)
                    ? music.FaceFile(design)
                    : family.ToLowerInvariant() switch
                    {
                        "emmentaler-brace" => "emmentaler-brace.otf",
                        _ => null
                    };
            if (fileName == null) return null;
            string? path = ResolveFontPath(fileName);
            return path != null ? SKTypeface.FromFile(path) : null;
        }

        /// <summary>
        /// The generic families (<c>serif</c> / <c>sans</c>) resolved to the BUNDLED
        /// TeX Gyre faces — LilyPond's own text fonts by metrics — through the very loader
        /// <see cref="TextFontMetrics"/> measures with.
        /// </summary>
        private static SKTypeface? TryLoadBundledText(string family, FontStyle style)
            => TextFontMetrics.IsGenericTextFamily(family, out bool sans)
                ? TextFontMetrics.Typeface(sans, style)
                : null;

        private static SKTypeface? TryLoadSystem(string family, FontStyle style)
        {
            var weight = (style & FontStyle.Bold) != 0 ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal;
            var slant = (style & FontStyle.Italic) != 0 ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright;
            // Map CSS generic family names to a reasonable system font.
            string actual = family.ToLowerInvariant() switch
            {
                "serif" => "Times New Roman",
                "sans-serif" or "sans" => "Arial",
                "monospace" or "mono" => "Courier New",
                _ => family,
            };
            return SKTypeface.FromFamilyName(actual, weight, SKFontStyleWidth.Normal, slant);
        }

        // Per-codepoint fallback typefaces resolved via the system font
        // manager (cached — CJK runs hit the same face repeatedly).
        private readonly Dictionary<int, SKTypeface?> _fallbacks = new();

        /// <summary>
        /// Splits <paramref name="text"/> into runs renderable by one typeface
        /// each: codepoints the base face covers stay on it; for the rest the
        /// system font manager picks a face that has the glyph (e.g. a CJK
        /// font for Japanese section labels). Unresolvable codepoints stay on
        /// the base face (tofu — nothing better exists on this system).
        /// </summary>
        public List<(string Segment, SKTypeface Typeface)> SegmentByTypeface(
            string text, SKTypeface baseTypeface, FontStyle style)
        {
            var runs = new List<(string, SKTypeface)>();
            if (string.IsNullOrEmpty(text))
                return runs;

            var sb = new System.Text.StringBuilder();
            SKTypeface? runFace = null;

            for (int i = 0; i < text.Length;)
            {
                int cp = char.ConvertToUtf32(text, i);
                int len = char.IsSurrogatePair(text, i) ? 2 : 1;
                var face = ResolveFace(cp, baseTypeface, style);

                if (runFace != null && !ReferenceEquals(face, runFace))
                {
                    runs.Add((sb.ToString(), runFace));
                    sb.Clear();
                }
                runFace = face;
                sb.Append(text, i, len);
                i += len;
            }
            if (sb.Length > 0 && runFace != null)
                runs.Add((sb.ToString(), runFace));
            return runs;
        }

        private SKTypeface ResolveFace(int codepoint, SKTypeface baseTypeface, FontStyle style)
        {
            // Fast path: the base face has the glyph.
            if (baseTypeface.GetGlyph(codepoint) != 0)
                return baseTypeface;

            if (!_fallbacks.TryGetValue(codepoint, out var fallback))
            {
                var weight = (style & FontStyle.Bold) != 0 ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal;
                var slant = (style & FontStyle.Italic) != 0 ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright;
                fallback = SKFontManager.Default.MatchCharacter(
                    null, new SKFontStyle(weight, SKFontStyleWidth.Normal, slant), null, codepoint);
                _fallbacks[codepoint] = fallback;
            }
            return fallback ?? baseTypeface;
        }

        private string? ResolveFontPath(string fileName) =>
            FontLocator.ResolveFile(fileName, _fontDirectory);

        public void Dispose()
        {
            foreach (var f in _fonts.Values) f.Dispose();
            foreach (var f in _faceFonts.Values) f.Dispose();
            foreach (var t in _typefaces.Values)
                if (!_borrowed.Contains(t)) t.Dispose();
        }
    }
}
