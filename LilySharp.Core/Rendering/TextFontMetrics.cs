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
using SkiaSharp;

namespace LilySharp.Core.Rendering;

/// <summary>
/// Advance widths and INK extents of text, read from the bundled text fonts themselves.
/// </summary>
/// <remarks>
/// LilyPond measures text the same way — Pango over the FreeType outline
/// (<c>lily/modified-font-metric.cc:125-143</c> <c>Modified_font_metric::text_stencil</c>) —
/// so a text grob's extent IS the drawn glyphs' ink and no nominal constant can stand in
/// for it. That is the finding <c>26afa9fe</c> had to make for DynamicText and this class
/// generalises: the layout asks the font, per string, through one function.
/// <para>
/// THE OUTLINE, NOT <c>MeasureText</c>. <c>SKPaint.MeasureText(s, ref SKRect)</c> quantises
/// its rectangle in FONT UNITS — measured, it returns the same 0.718750 for a digit at
/// TextSize 1000 and at TextSize 1000000 — which is 0.027 staff spaces of granularity,
/// coarser than the residuals this engine is chasing. <see cref="SKPaint.GetTextPath"/>
/// carries no quantisation and reproduces LilyPond's own number for the tuplet digit to
/// 0.000042 staff spaces.
/// </para>
/// <para>
/// ⚠️ THERE IS NO SYSTEM-FONT FALLBACK, deliberately. <c>SKTypeface.FromFamilyName</c>
/// resolves "LilyPond Serif", "C059", "TeX Gyre Schola" and even the CSS generic "serif"
/// all to Segoe UI on a stock Windows box — a SANS face — because LilyPond's text fonts
/// live in its own share directory under a private fontconfig and are not system fonts at
/// all. Measuring by name would therefore measure whatever the machine happens to have,
/// and the same .lys would lay out differently on different machines. That is not a
/// hypothetical: it is exactly the bug <c>b69c73e6</c> removed from the LP-fidelity probe,
/// where four ledger values had been measured against fontconfig's pick. Faces come from
/// the bundled files or the call throws.
/// </para>
/// </remarks>
public static class TextFontMetrics
{
    /// <summary>The bundled serif family — TeX Gyre Schola, the GUST-licensed twin of the
    /// URW C059 that LilyPond's own <c>"LilyPond Serif"</c> alias prefers.</summary>
    /// <remarks>
    /// LILYPOND-REF: <c>share/lilypond/*/fonts/00-lilypond-fonts.conf</c> — the alias
    /// prefers C059, then Century Schoolbook URW/L, then TeX Gyre Schola. Measured on
    /// 2.26.0, C059 and TeX Gyre Schola agree on every advance and on the digit's ink to
    /// six digits, so this is LilyPond's text face; C059 itself is AGPL and Schola is
    /// LPPL/GUST, which is why the twin is the one bundled.
    /// </remarks>
    public const string SerifFamily = "TeX Gyre Schola";

    /// <summary>The bundled sans family — TeX Gyre Heros, likewise metric-identical to the
    /// Nimbus Sans that <c>"LilyPond Sans Serif"</c> prefers.</summary>
    public const string SansFamily = "TeX Gyre Heros";

    private static readonly ConcurrentDictionary<(bool Sans, FontStyle Style), SKTypeface> Faces = new();
    private static readonly ConcurrentDictionary<(bool Sans, FontStyle Style, string Text), (double Advance, double Bottom, double Top)>
        Cache = new();
    private static readonly ConcurrentDictionary<(bool Sans, FontStyle Style, string Text), SKPath> Paths = new();

    /// <summary>
    /// The string's glyph outlines as ONE path at 1000 units/em, baseline origin, in
    /// Skia's Y-DOWN frame — the same path <see cref="Measure"/> takes its ink box from,
    /// exposed so the outline-skyline builder reads the identical geometry (one producer,
    /// like <see cref="Typeface"/> for the renderers). Cached and shared: callers must
    /// not mutate or dispose the returned path.
    /// </summary>
    internal static SKPath OutlinePath(string text, bool sans, FontStyle style)
        => Paths.GetOrAdd((sans, style, text), static key =>
        {
            var typeface = Face(key.Sans, key.Style);
            using var paint = new SKPaint { Typeface = typeface, TextSize = 1000f };
            return paint.GetTextPath(key.Text, 0, 0);
        });

    /// <summary>Advance width of <paramref name="text"/> in staff spaces.</summary>
    public static double Advance(string text, double fontSize, bool sans = false,
        FontStyle style = FontStyle.Regular)
        => Measure(text, sans, style).Advance * fontSize;

    // The three faces the engine RESERVES for, named so a call site reads as the face it
    // draws in. They are the whole of what the old hand-typed tables offered, which is why
    // the migration off those is an identifier substitution rather than 37 edited argument
    // lists — each one a chance to attach the wrong weight to the wrong string.

    /// <summary>Advance of regular serif text (lyrics, parenthesised marks).</summary>
    public static double Serif(string text, double fontSize) => Advance(text, fontSize);

    /// <summary>Advance of BOLD serif text (marks, tempo text, bar numbers, volta labels).</summary>
    public static double SerifBold(string text, double fontSize)
        => Advance(text, fontSize, sans: false, FontStyle.Bold);

    /// <summary>Advance of BOLD sans text (chord symbols).</summary>
    public static double SansBold(string text, double fontSize)
        => Advance(text, fontSize, sans: true, FontStyle.Bold);

    /// <summary>
    /// The INK extent of <paramref name="text"/> in staff spaces, relative to its baseline
    /// and up-positive: <c>Bottom</c> is negative for a descender, <c>Top</c> positive for
    /// the part above the baseline.
    /// </summary>
    public static (double Bottom, double Top) Ink(string text, double fontSize,
        bool sans = false, FontStyle style = FontStyle.Regular)
    {
        var m = Measure(text, sans, style);
        return (m.Bottom * fontSize, m.Top * fontSize);
    }

    /// <summary>Ink height (<c>Top - Bottom</c>) of <paramref name="text"/> in staff spaces.</summary>
    public static double InkHeight(string text, double fontSize, bool sans = false,
        FontStyle style = FontStyle.Regular)
    {
        var (bottom, top) = Ink(text, fontSize, sans, style);
        return top - bottom;
    }

    /// <summary>Per-em metrics of one string, cached.</summary>
    private static (double Advance, double Bottom, double Top) Measure(
        string text, bool sans, FontStyle style)
    {
        if (string.IsNullOrEmpty(text))
            return (0, 0, 0);
        return Cache.GetOrAdd((sans, style, text), static key =>
        {
            var typeface = Face(key.Sans, key.Style);
            // Measured at 1000 units/em and divided back, so the caller's font size
            // multiplies a pure ratio and no size-dependent hinting enters.
            using var paint = new SKPaint { Typeface = typeface, TextSize = 1000f };
            double advance = AdvancePerEm(key.Text, typeface, paint);
            var path = OutlinePath(key.Text, key.Sans, key.Style);
            if (path.IsEmpty)
                return (advance, 0, 0);
            var b = path.Bounds;
            // Skia's path is Y-DOWN about the baseline: Top is negative above it. Reflect
            // to this engine's Y-up ink convention.
            return (advance, -b.Bottom / 1000.0, -b.Top / 1000.0);
        });
    }

    // The bundled Emmentaler face, for LAYOUT-side glyph OUTLINE measurement (the
    // skyline walk over a music glyph). The drawing backends keep their own loaders —
    // those are drawing devices per backend; this is the one measurement home. Null
    // when no bundled font directory is found (callers fall back to the designed box).
    private static readonly Lazy<SKTypeface?> MusicFace = new(static () =>
    {
        var path = FontLocator.ResolveFile("emmentaler-20.otf");
        return path != null ? SKTypeface.FromFile(path) : null;
    });

    /// <summary>
    /// Outline path of one Emmentaler glyph at 1000 units/em (the same frame
    /// <see cref="OutlinePath"/> serves for text), or null when the bundled music font
    /// cannot be located.
    /// </summary>
    internal static SKPath? MusicGlyphPath(char glyph)
    {
        var face = MusicFace.Value;
        if (face == null)
            return null;
        using var paint = new SKPaint { Typeface = face, TextSize = 1000f };
        return paint.GetTextPath(glyph.ToString(), 0, 0);
    }

    /// <summary>
    /// The bundled face itself, for the RENDERERS — so the engine draws the same font it
    /// reserved for.
    /// </summary>
    /// <remarks>
    /// One loader, shared with the measuring path above. Two loaders is how
    /// <c>ClefGlyphXOffset</c> ended up with a second home and how the renderer's tuplet
    /// thickness drifted from <c>EngravingDefaults</c>: whenever drawing and spacing read
    /// the same quantity from different code, one of them eventually moves.
    /// </remarks>
    internal static SKTypeface Typeface(bool sans, FontStyle style) => Face(sans, style);

    /// <summary>Is <paramref name="family"/> one of the generic families the engine asks
    /// for by name, and which of the two bundled faces does it mean?</summary>
    internal static bool IsGenericTextFamily(string family, out bool sans)
    {
        switch (family.ToLowerInvariant())
        {
            case "serif": sans = false; return true;
            case "sans": case "sans-serif": sans = true; return true;
            default: sans = false; return false;
        }
    }

    /// <summary>
    /// The string's advance, per em, with a defined width for every character the bundled
    /// face has no glyph for.
    /// </summary>
    /// <remarks>
    /// ⚠️ WITHOUT THIS, CJK COLLAPSES. TeX Gyre Schola is a Latin face (LilyPond's own
    /// fontconfig calls it "Latin glyphs only"), so Skia hands back the .notdef advance —
    /// measured, 0.28 em — for every CJK character. A "これ" label reported 0.56 em where
    /// the truth is 2.0: three and a half times too narrow, i.e. reserved space a CJK lyric
    /// would overflow. The renderer does NOT draw .notdef there; it resolves a per-codepoint
    /// system fallback (PngDrawingContext.SegmentByTypeface), so the width that lands on the
    /// page is the fallback face's.
    /// <para>
    /// The convention is the one the hand-typed table this class replaced already used, and
    /// it is a convention rather than a guess: East Asian wide characters occupy exactly one
    /// em in essentially every CJK face, and half-width forms half of one. Anything else the
    /// face lacks takes the same 0.5 em median the old table used, because the fallback face
    /// is not known here. Deterministic on purpose — asking the system font manager would
    /// make the same score lay out differently on different machines, which is the whole
    /// reason this class refuses a system-font fallback.
    /// </para>
    /// </remarks>
    private static double AdvancePerEm(string text, SKTypeface typeface, SKPaint paint)
    {
        double total = 0;
        for (int i = 0; i < text.Length;)
        {
            int cp = char.ConvertToUtf32(text, i);
            int len = char.IsSurrogatePair(text, i) ? 2 : 1;
            total += typeface.GetGlyph(cp) != 0
                ? paint.MeasureText(text.Substring(i, len)) / 1000.0
                : MissingGlyphAdvance(cp);
            i += len;
        }
        return total;
    }

    /// <summary>Width, per em, of a character the bundled face cannot draw.</summary>
    private static double MissingGlyphAdvance(int cp) => cp switch
    {
        >= 0x1100 and <= 0x115F => 1.0,   // Hangul Jamo
        >= 0x2E80 and <= 0xA4CF => 1.0,   // CJK radicals … kana … ideographs … Yi
        >= 0xAC00 and <= 0xD7A3 => 1.0,   // Hangul syllables
        >= 0xF900 and <= 0xFAFF => 1.0,   // CJK compatibility ideographs
        >= 0xFF00 and <= 0xFF60 => 1.0,   // full-width forms
        >= 0x20000 and <= 0x3FFFD => 1.0, // CJK extension planes
        _ => 0.5,                          // median, as the table this replaced used
    };

    private static SKTypeface Face(bool sans, FontStyle style)
        => Faces.GetOrAdd((sans, style), static key =>
        {
            var file = FileName(key.Sans, key.Style);
            var path = FontLocator.ResolveFile(file)
                ?? throw new InvalidOperationException(
                    $"Bundled text font '{file}' was not found. Text metrics come from the " +
                    "bundled faces only — falling back to a system font would make the same " +
                    "score lay out differently on different machines.");
            return SKTypeface.FromFile(path)
                ?? throw new InvalidOperationException($"Bundled text font '{path}' failed to load.");
        });

    private static string FileName(bool sans, FontStyle style)
    {
        string stem = sans ? "texgyreheros" : "texgyreschola";
        string face = style switch
        {
            FontStyle.BoldItalic => "bolditalic",
            FontStyle.Bold => "bold",
            FontStyle.Italic => "italic",
            _ => "regular",
        };
        return $"{stem}-{face}.otf";
    }
}
