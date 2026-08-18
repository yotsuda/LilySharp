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
/// coarser than the residuals this engine is chasing.
/// <see cref="SKPaint.GetTextPath(string, float, float)"/>
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
    /// <para>
    /// ⚠️ "AGREE ON EVERY ADVANCE" IS TRUE AND IS NOT THE WHOLE STORY — they do NOT agree on
    /// KERNS, found 2026-08-02 when the shaping port left two entries open. Shaped through
    /// HarfBuzz, in font units: the A-A pair is +61 in C059 and 0 in Schola, V-A is −84
    /// against −95, A-V −83 against −90, v-a −20 in both. LilyPond's own stencil expression
    /// names the file it drew with (<c>ly:stencil-expr</c> prints
    /// <c>(glyph-string … C059-Italic … C059-Italic.otf …)</c>), and probe books TS1/TS2 pin
    /// the same LilyPond to THIS face and land on Lily#'s numbers exactly. So ledger
    /// <c>text.width.{aa,va}</c> are a FONT FILE difference and not an arithmetic one.
    /// DECIDED 2026-08-02 (user, with the size measured first): the twin stays, because the
    /// URW exemption covers embedding in a PostScript/PDF document and not shipping the font
    /// program. What that costs, measured: real strings run 0–4 pixels (0–0.137 ss) apart, and
    /// the ff/ffi LIGATURES differ in width too (605 against 686, 878 against 904), so the drawn
    /// page differs and not only the reservation. SWEPT ACROSS ALL FOUR FACES on 2026-08-03 —
    /// the 2026-08-02 reading was the italic face only, and it generalises with no face safer
    /// than another. Over the 52 letters (2704 ordered pairs), C059 kerns 765–774 pairs and
    /// Schola 273–287 — ABOUT A THIRD AS MANY — and 26.8–27.6% of all pairs land on a different
    /// width; adding digits and title/dynamic punctuation (77 characters, 5929 pairs) gives
    /// 1074–1085 against 344–357 and 17.5–17.8%. The spread across the four faces is under one
    /// point, so weight and slant do not change the story.
    /// ⚠️ The earlier note said "438 of 471 kerned pairs, 11.2%"; neither character set above
    /// reproduces those counts, so that sweep used a third alphabet nobody wrote down. Counts
    /// are meaningless without the set they were counted over — HANDOFF §0.
    /// ⚠️ Do not read a non-zero text-width entry as a defect without checking the string
    /// for a pair these two kern differently. HANDOFF §3 holds the decision.
    /// </para>
    /// </remarks>
    public const string SerifFamily = "TeX Gyre Schola";

    /// <summary>The bundled sans family — TeX Gyre Heros, likewise metric-identical to the
    /// Nimbus Sans that <c>"LilyPond Sans Serif"</c> prefers.</summary>
    public const string SansFamily = "TeX Gyre Heros";

    /// <summary>
    /// Pango's device-pixel quantum, in staff spaces — the grid every shaped advance is
    /// snapped to, and the one home for it.
    /// </summary>
    /// <remarks>
    /// LilyPond measures text through Pango over FreeType at <c>PANGO_RESOLUTION</c> 1200
    /// dpi, and a glyph's advance comes back hinted to a WHOLE DEVICE PIXEL. Pango scales a
    /// logical width by <c>scale_ = INCH_TO_BP / (PANGO_SCALE · PANGO_RESOLUTION ·
    /// output_scale)</c>, so one pixel measures <c>INCH_TO_BP / (PANGO_RESOLUTION ·
    /// output_scale)</c> staff spaces, with <c>output_scale = staff-space · MM_PER_INCH /
    /// INCH_TO_PT</c> over the default 5 pt staff space. DERIVED FROM LILYPOND'S OWN
    /// CONSTANTS, not fitted — and MEASURED: ledger <c>text.width.*</c> found every one of
    /// LilyPond's text widths to be a whole multiple of it (39 pixels for "n", 32 for "o",
    /// 156 for "nnnn"), and <c>GetTimeSigDigitWidth</c> found the same grid from the
    /// fetaText side before the text side knew about it.
    /// <para>
    /// ⚠️ SIZE-INDEPENDENT, which is why the quantising happens in staff spaces here rather
    /// than at a ppem: a device pixel is a device pixel whatever the font size, and
    /// <c>ppem × pixel = fontSize</c> makes the two spellings identical. A per-size ppem
    /// constant would be a second home for the same number.
    /// </para>
    /// </remarks>
    // LILYPOND-REF: lily/pango-font.cc:109-112 Pango_font::Pango_font (scale_);
    //   lily/include/pango-font.hh:75 PANGO_RESOLUTION; lily/include/dimensions.hh:27,31
    //   INCH_TO_PT, INCH_TO_BP.
    public const double PangoPixelStaffSpaces =
        72.0 * 72.27 / (1200.0 * 5.0 * 25.4); // INCH_TO_BP·INCH_TO_PT / (RES·staff_pt·mm_per_inch)

    /// <summary>Snaps a width in staff spaces to <see cref="PangoPixelStaffSpaces"/>, as
    /// LilyPond's hinted advances are.</summary>
    public static double QuantiseToPangoPixel(double widthStaffSpaces)
        => Math.Round(widthStaffSpaces / PangoPixelStaffSpaces, MidpointRounding.AwayFromZero)
           * PangoPixelStaffSpaces;

    private static readonly ConcurrentDictionary<TextFace, SKTypeface> Faces = new();
    private static readonly ConcurrentDictionary<(TextFace Face, string Text),
        (double Bottom, double Top)>
        Cache = new();
    private static readonly ConcurrentDictionary<(TextFace Face, string Text), SKPath> Paths = new();

    /// <summary>
    /// The string's glyph outlines as ONE path at 1000 units/em, baseline origin, in
    /// Skia's Y-DOWN frame — the same path <see cref="Measure"/> takes its ink box from,
    /// exposed so the outline-skyline builder reads the identical geometry (one producer,
    /// like <c>Typeface</c> for the renderers). Cached and shared: callers must
    /// not mutate or dispose the returned path.
    /// </summary>
    internal static SKPath OutlinePath(string text, bool sans, FontStyle style)
        => OutlinePath(text, TextFace.Bundled(sans, style));

    /// <inheritdoc cref="OutlinePath(string, bool, FontStyle)"/>
    internal static SKPath OutlinePath(string text, TextFace face)
        => Paths.GetOrAdd((face, text), static key =>
        {
            var typeface = Face(key.Face);
            using var paint = new SKPaint { Typeface = typeface, TextSize = 1000f };
            return paint.GetTextPath(key.Text, 0, 0);
        });

    /// <summary>Advance width of <paramref name="text"/> in staff spaces.</summary>
    /// <remarks>
    /// PER GLYPH, then snapped to <see cref="PangoPixelStaffSpaces"/> — the size multiply
    /// happens BEFORE the snap and the snap happens once per glyph, because that is what
    /// LilyPond's advances are: <c>round(advance × ppem)</c> device pixels each, summed.
    /// MEASURED (audit/lp-geometry/probes/text-advance.ly): "8va" is 36 + 33 + 37 = 106
    /// pixels of rounding, not <c>round</c> of the string's total (which would be 106.254
    /// → 106 here but 90 rather than 94 for "AA" either way) — the ladder rungs "n"/"nn"/
    /// "nnnn" reading 39/78/156 are what pin it to the glyph.
    /// <para>
    /// ⚠️ KERNING IS IN, and it is INSIDE this snap: <see cref="Run"/> takes each glyph's
    /// advance from the SHAPED run (HarfBuzz, the shaper Pango itself calls), pair
    /// adjustments already applied, and snaps THAT. Not the other order — a pair value is an
    /// adjustment to the glyph's own advance, so snapping first and adding the kern
    /// afterwards misses by a fraction of a pixel per pair; the dynamic labels were spelled
    /// that way until 2026-08-05 and their ledger points are what caught it
    /// (Svg/Layout/DynamicOutline.cs).
    /// ⚠️ This remark said "what this still does not do is kerning" until 2026-08-05 —
    /// EIGHTEEN sessions after the kerning port landed here on 2026-08-02, and it named
    /// three residuals (−4, +5, +1) of which two have been EXACT ever since. It was read as
    /// current and copied into a handoff. The live numbers are the ledger's:
    /// <c>text.width.{a1,v1,av,8va}</c> are 0, and <c>text.width.{aa,va}</c> keep −4 and −1
    /// pixels which are NOT a defect and NOT this snap — they are the bundled face's kern
    /// table differing from the C059 LilyPond resolves, measured and DECIDED (HANDOFF §3).
    /// Do not chase them, and do not re-derive them.
    /// </para>
    /// </remarks>
    public static double Advance(string text, double fontSize, bool sans = false,
        FontStyle style = FontStyle.Regular)
        => Advance(text, fontSize, TextFace.Bundled(sans, style));

    /// <inheritdoc cref="Advance(string, double, bool, FontStyle)"/>
    public static double Advance(string text, double fontSize, TextFace face)
        => string.IsNullOrEmpty(text) ? 0 : Run(text, fontSize, face).Total;

    /// <summary>
    /// One glyph of a shaped run: the face's OWN glyph id, and the pen position it sits at,
    /// in staff spaces from the run's origin.
    /// </summary>
    /// <remarks>
    /// <paramref name="MissingCodepoint"/> is the source code point when the face has no glyph
    /// for it (<see cref="MissingGlyphAdvance"/> decided its width); a backend that has a
    /// fallback face should draw THAT character rather than this .notdef glyph id.
    /// <para>
    /// <paramref name="Cluster"/> is the index in the source string this glyph came from, for a
    /// backend that cannot address glyphs and has to draw characters at these positions
    /// instead. ⚠️ A LIGATURE IS ONE GLYPH OVER SEVERAL CHARACTERS, so clusters repeat and skip;
    /// the run is authoritative about WIDTH, and a character-drawing backend is only promised
    /// that each cluster starts where the reservation put it.
    /// </para>
    /// </remarks>
    public readonly record struct ShapedGlyph(ushort GlyphId, double X, int? MissingCodepoint, int Cluster);

    /// <summary>
    /// The glyphs a backend must draw, at the positions it must draw them, for its ink to be
    /// the width <c>Advance</c> reserved.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/pango-font.cc:407-435 Pango_font::pango_item_string_stencil — one
    ///   glyph descriptor per SHAPED glyph (<c>pgs->num_glyphs</c>), each carrying its own box,
    ///   and lily/pango-font.cc:494-503 Pango_font::pango_item_string_stencil wraps the list as
    ///   the stencil's <c>glyph-string</c>. A LilyPond page is glyphs at positions; this is the
    ///   same list, which is why a backend can draw from it without laying anything out itself.
    /// ⚠️ THIS AND <c>Advance</c> ARE ONE COMPUTATION, deliberately — the advance is
    /// this run's total. They were two before 2026-08-03, and the two disagreed: layout
    /// reserved the shaped width while the PNG and PDF backends drew the string unshaped, so a
    /// title's ink ran 3.16 staff spaces past the box it was given. That is the
    /// reserve-versus-draw split this repo keeps finding, and the fix for it is not to make the
    /// second spelling agree but to delete it.
    /// </remarks>
    public static IReadOnlyList<ShapedGlyph> ShapeRun(string text, double fontSize,
        bool sans = false, FontStyle style = FontStyle.Regular)
        => ShapeRun(text, fontSize, TextFace.Bundled(sans, style));

    /// <inheritdoc cref="ShapeRun(string, double, bool, FontStyle)"/>
    public static IReadOnlyList<ShapedGlyph> ShapeRun(string text, double fontSize, TextFace face)
        => string.IsNullOrEmpty(text) ? [] : Run(text, fontSize, face).Glyphs;

    private static (ShapedGlyph[] Glyphs, double Total) Run(
        string text, double fontSize, TextFace face)
        => RunCache.GetOrAdd((face, text, fontSize), static key =>
        {
            var glyphs = new List<ShapedGlyph>();
            double pen = 0;
            foreach (var (glyphId, perEm, xOffsetPerEm, missingCodepoint, cluster)
                     in ShapedAdvancesPerEm(key.Text, key.Face))
            {
                double advance = missingCodepoint is int cp ? MissingGlyphAdvance(cp) : perEm;
                glyphs.Add(new ShapedGlyph(glyphId, pen + xOffsetPerEm * key.FontSize,
                                           missingCodepoint, cluster));
                // The snap is per glyph and AFTER the size multiply — see Advance's remarks.
                pen += QuantiseToPangoPixel(advance * key.FontSize);
            }
            return ([.. glyphs], pen);
        });

    /// <summary>
    /// The string SHAPED: one entry per output glyph, its advance per em with the pair
    /// adjustments already in it, and the source code point when the face has no glyph.
    /// </summary>
    /// <remarks>
    /// This is the whole of what HarfBuzz is here for. LilyPond measures text through Pango,
    /// which shapes with HarfBuzz and has the <c>kern</c> feature on, so its advances carry
    /// pair adjustments; <c>SKPaint.MeasureText</c> answers per code point and cannot —
    /// CHECKED before reaching for a dependency: MeasureText("8va") equals the sum of its
    /// three characters, so nothing was being lost in Skia, it was never being asked.
    /// MEASURED (audit/lp-geometry/probes/text-advance.ly): "AA" is 4 pixels wider than "A"
    /// twice, "AAA" 8 (per pair), "AV" and "VA" 5 narrower, "VV" exactly twice "V".
    /// <para>
    /// ⚠️ SHAPED IN FONT UNITS, quantised by the CALLER. The scale is set to units-per-em so
    /// what comes back is the design's own numbers, and each glyph's advance is snapped to a
    /// Pango pixel afterwards — the order LilyPond's own pipeline has (FreeType hints the
    /// advance, GPOS adjusts it, and the result is whole pixels either way; every width in
    /// the probe is an exact multiple of one).
    /// </para>
    /// <para>
    /// ⚠️ A MISSING GLYPH IS REPORTED, NOT MEASURED. The bundled Latin faces have no CJK, and
    /// a shaper returns .notdef for those with the .notdef advance — the 0.28 em that used to
    /// make a CJK lyric reserve a third of its width. The cluster maps back to the source
    /// code point so <see cref="MissingGlyphAdvance"/> keeps deciding those.
    /// </para>
    /// </remarks>
    private static IEnumerable<(ushort GlyphId, double PerEm, double XOffsetPerEm,
                                int? MissingCodepoint, int Cluster)>
        ShapedAdvancesPerEm(string text, TextFace face)
    {
        var (font, upem) = ShapingFont(face);
        using var buffer = new HarfBuzzSharp.Buffer();
        buffer.AddUtf16(text);
        buffer.GuessSegmentProperties();
        lock (font)
            font.Shape(buffer);
        var infos = buffer.GlyphInfos;
        var positions = buffer.GlyphPositions;
        var result = new List<(ushort, double, double, int?, int)>(infos.Length);
        for (int i = 0; i < infos.Length; i++)
        {
            int? missing = infos[i].Codepoint == 0
                ? char.ConvertToUtf32(text, (int)infos[i].Cluster)
                : null;
            result.Add(((ushort)infos[i].Codepoint,
                        positions[i].XAdvance / (double)upem,
                        positions[i].XOffset / (double)upem,
                        missing,
                        (int)infos[i].Cluster));
        }
        return result;
    }

    // One shaping font per face, built from the same bundled file the measuring and drawing
    // paths already use (FontLocator), and shared: building a Face parses the tables.
    // ⚠️ hb_font_t is not thread-safe for shaping, hence the lock above; the engine measures
    // from parallel layout passes.
    private static readonly ConcurrentDictionary<TextFace,
        (HarfBuzzSharp.Font Font, uint UnitsPerEm)> ShapingFonts = new();

    private static (HarfBuzzSharp.Font Font, uint UnitsPerEm) ShapingFont(TextFace face)
        => ShapingFonts.GetOrAdd(face, static key =>
        {
            var blob = ShapingBlob(key);
            blob.MakeImmutable();
            var hbFace = new HarfBuzzSharp.Face(blob, 0);
            uint upem = (uint)hbFace.UnitsPerEm;
            var font = new HarfBuzzSharp.Font(hbFace);
            font.SetScale((int)upem, (int)upem);
            font.SetFunctionsOpenType();
            return (font, upem);
        });

    // The shaper wants a font PROGRAM. A bundled face is a file on disk; a named one is
    // whatever the machine handed back, streamed out of the typeface (and lifted out of a
    // .ttc, which is how Yu Gothic / Meiryo / MS Gothic ship on Japanese Windows —
    // FontEmbedInfo.TryGetFontBytes(SKTypeface) is the same lift the PDF embedder needs).
    private static HarfBuzzSharp.Blob ShapingBlob(TextFace face)
    {
        if (face.IsBundled)
            return HarfBuzzSharp.Blob.FromFile(BundledPath(face));
        var bundled = BundledPathForName(face.Name!, face.Style);
        if (bundled != null)
            return HarfBuzzSharp.Blob.FromFile(bundled);
        var typeface = SystemFace(face.Name!, face.Style)
            ?? throw Unmeasurable(face);
        var bytes = FontEmbedInfo.TryGetFontBytes(typeface)
            ?? throw Unmeasurable(face);
        using var stream = new MemoryStream(bytes);
        return HarfBuzzSharp.Blob.FromStream(stream);
    }

    private static string BundledPath(TextFace face)
    {
        var file = FileName(face.Sans, face.Style);
        return FontLocator.ResolveFile(file)
            ?? throw new InvalidOperationException(
                $"Bundled text font '{file}' was not found. The engine cannot measure text " +
                "without the faces it ships.");
    }

    /// <summary>
    /// The bundled file a NAME refers to, when the name is one of the families this engine
    /// ships — otherwise null.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE BUNDLE IS CONSULTED BEFORE THE MACHINE, and that ordering is LilyPond's own
    /// rather than a Lily# preference: LilyPond builds its Fontconfig configuration by
    /// loading its OWN <c>00-lilypond-fonts.conf</c> first, the system default second, and
    /// its <c>99-lilypond-fonts.conf</c> last.
    /// <para>
    /// It is load-bearing twice over. It keeps <c>fonts { serif "TeX Gyre Schola" }</c>
    /// measuring the same file the default does — writing the bundled family's own name
    /// must not become a way to get a DIFFERENT Schola that happens to be installed. And
    /// because the bundled faces are present on every machine by construction, it is what
    /// makes a named-face test deterministic anywhere, which a test naming Georgia could
    /// never be.
    /// </para>
    /// </remarks>
    // LILYPOND-REF: lily/font-config.cc:43-78 make_font_config — the datadir's
    //   00-lilypond-fonts.conf is pushed ahead of FcConfigFilename's default conf.
    private static string? BundledPathForName(string name, FontStyle style)
        => TryBundledFamily(TextFace.Named(name, false, style), out bool sans)
            ? FontLocator.ResolveFile(FileName(sans, style))
            : null;

    /// <summary>
    /// Does <paramref name="face"/> come out of the BUNDLE, and if so from which family's
    /// file?
    /// </summary>
    /// <remarks>
    /// ⚠️ THE ANSWER IS ABOUT THE NAME, NOT THE BINDING. <c>fonts { sans "TeX Gyre Schola" }</c>
    /// measures the SERIF file, because a name refers to a face and not to the slot it was
    /// written in; <paramref name="sans"/> is the family of the FILE, which is why it can
    /// disagree with <see cref="TextFace.Sans"/> (that one is the fallback family).
    /// <para>
    /// It exists so a backend can ask the question rather than re-derive it. The PDF resolver
    /// derived it from the BINDING and so served <c>fonts { sans "TeX Gyre Schola" }</c> with
    /// TeX Gyre Heros while the layout had reserved TeX Gyre Schola — a whole face apart, and
    /// the reason this is one function and not two spellings.
    /// </para>
    /// <para>
    /// ⚠️ NO SECOND ADDRESS. The rule that makes this worth asking — the bundle is consulted
    /// before the machine — is LilyPond's, and its citation lives on
    /// <see cref="BundledPathForName"/>, the one caller that acts on the ordering. This
    /// method only says which family a name belongs to.
    /// </para>
    /// </remarks>
    internal static bool TryBundledFamily(TextFace face, out bool sans)
    {
        if (face.IsBundled) { sans = face.Sans; return true; }
        if (string.Equals(face.Name, SerifFamily, StringComparison.OrdinalIgnoreCase))
        { sans = false; return true; }
        if (string.Equals(face.Name, SansFamily, StringComparison.OrdinalIgnoreCase))
        { sans = true; return true; }
        sans = face.Sans;
        return false;
    }

    /// <summary>
    /// The machine's face for <paramref name="name"/> at <paramref name="style"/>, or null
    /// when this machine does not have it.
    /// </summary>
    /// <remarks>
    /// ⚠️ SKIA NEVER RETURNS NULL for an unknown family — it hands back a default face, and
    /// taking that would be the silent substitution this whole class exists to refuse. The
    /// resolved family name is compared against the request, exactly as
    /// <c>FontEmbedInfo.ClassifyUncached</c> does; a mismatch is "not here", not "close
    /// enough".
    /// <para>
    /// LILYSHARP-OWN: LilyPond has no equivalent refusal, and cannot — it resolves through
    /// Fontconfig, whose whole purpose is to always answer with SOMETHING (family
    /// substitution is the feature, not a fallback). Departing from
    /// <c>lily/font-select.cc:193-217 select_font</c> here is deliberate: a substituted
    /// face measured as if it were the requested one is a wrong page with no complaint
    /// attached, and this engine would rather say the face is missing. What that buys is
    /// the difference between "your title is in the wrong font" and "your title is in the
    /// wrong font AND spaced for a third one".
    /// </para>
    /// <para>
    /// ⚠️ WHEN IT GOES AWAY: never, unless Lily# grows a way for a score to declare the
    /// FILE it means (then a face is either present as declared or absent, and there is
    /// nothing to substitute). OBSERVED BY
    /// <c>TextFaceMetricsTests.MeasuringAFaceThisMachineDoesNotHave_Throws_RatherThanSubstituting</c>
    /// and <c>…CanMeasure_IsFalseForAFaceThisMachineDoesNotHave</c>.
    /// </para>
    /// </remarks>
    private static SKTypeface? SystemFace(string name, FontStyle style)
    {
        var skStyle = style switch
        {
            FontStyle.BoldItalic => SKFontStyle.BoldItalic,
            FontStyle.Bold => SKFontStyle.Bold,
            FontStyle.Italic => SKFontStyle.Italic,
            _ => SKFontStyle.Normal,
        };
        var typeface = SKTypeface.FromFamilyName(name, skStyle);
        return typeface != null
               && typeface.FamilyName.Equals(name, StringComparison.OrdinalIgnoreCase)
            ? typeface
            : null;
    }

    private static InvalidOperationException Unmeasurable(TextFace face) =>
        new($"Text face '{face.Name}' is not available on this machine, so its metrics "
            + "cannot be read. Ask CanMeasure first and decide the fallback out loud — "
            + "substituting silently is what makes one score lay out differently in two "
            + "places with nothing said about it.");

    /// <summary>
    /// Can this machine measure <paramref name="face"/>? Always true for a bundled face.
    /// </summary>
    /// <remarks>
    /// The question exists because the answer is a property of the MACHINE, and every
    /// caller that lets a score name a face has to have an answer for "no" that it can say
    /// out loud. LilyPond has the same exposure — <c>font-name</c> goes to fontconfig
    /// (LILYPOND-REF: lily/font-select.cc:193-217 select_font) — and reports a missing
    /// family through Pango rather than pretending; <c>DiagnosticCodes.FontNotFound</c> is
    /// this engine's version of saying so.
    /// </remarks>
    public static bool CanMeasure(TextFace face)
    {
        if (face.IsBundled)
            return true;
        if (BundledPathForName(face.Name!, face.Style) != null)
            return true;
        return SystemFace(face.Name!, face.Style) != null;
    }

    // Keyed by SIZE as well as string, because the snap makes the advance non-linear in the
    // size — one string at two sizes is two different pixel counts, not one number scaled.
    // Holds the glyphs beside the total so the measuring and the drawing paths cannot drift:
    // asking for either shapes the run once.
    private static readonly ConcurrentDictionary<
        (TextFace Face, string Text, double FontSize),
        (ShapedGlyph[] Glyphs, double Total)> RunCache = new();

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
        => Ink(text, fontSize, TextFace.Bundled(sans, style));

    /// <inheritdoc cref="Ink(string, double, bool, FontStyle)"/>
    public static (double Bottom, double Top) Ink(string text, double fontSize, TextFace face)
    {
        var m = Measure(text, face);
        return (m.Bottom * fontSize, m.Top * fontSize);
    }

    /// <summary>Ink height (<c>Top - Bottom</c>) of <paramref name="text"/> in staff spaces.</summary>
    public static double InkHeight(string text, double fontSize, bool sans = false,
        FontStyle style = FontStyle.Regular)
        => InkHeight(text, fontSize, TextFace.Bundled(sans, style));

    /// <inheritdoc cref="InkHeight(string, double, bool, FontStyle)"/>
    public static double InkHeight(string text, double fontSize, TextFace face)
    {
        var (bottom, top) = Ink(text, fontSize, face);
        return top - bottom;
    }

    // (There is no horizontal-ink accessor, and that absence is deliberate. An `InkX` lived
    // here for one day, 2026-08-02, put in to serve the ottava's
    // `text_size = text.extent (X_AXIS)[RIGHT] + 0.3` — but a text stencil's box takes X
    // from Pango's LOGICAL rectangle and only Y from the ink one
    // (LILYPOND-REF: lily/pango-font.cc:351-362 Pango_font::pango_item_string_stencil),
    // so what that port wanted was the ADVANCE.
    // Ledger text.width.* measures the same conclusion from the outside: LilyPond's widths
    // are whole 1200-dpi pixels, and the ottava's number is a whole one only as an advance.
    // Nothing else asked for horizontal ink — the outline walk reads the path itself
    // (TextOutlineSkylines) — so the accessor came back out rather than waiting to be
    // misread a second time.)

    /// <summary>Per-em INK of one string, cached.</summary>
    /// <remarks>
    /// Per em and NOT quantised, unlike <c>Advance</c>: the ink is what the outline
    /// gives, and LilyPond takes a text stencil's Y from Pango's INK rectangle. That
    /// rectangle is quantised too, by the same PANGO_RESOLUTION — the ledger's
    /// <c>-0.000076</c> on DynamicText's height is that — but a height wants the whole
    /// quantised OUTLINE rather than one snap, so it stays open and named rather than
    /// half-ported here (GetTimeSigDigitWidth's remark makes the same split).
    /// </remarks>
    private static (double Bottom, double Top) Measure(string text, TextFace face)
    {
        if (string.IsNullOrEmpty(text))
            return (0, 0);
        return Cache.GetOrAdd((face, text), static key =>
        {
            var path = OutlinePath(key.Text, key.Face);
            if (path.IsEmpty)
                return (0, 0);
            var b = path.Bounds;
            // Skia's path is Y-DOWN about the baseline: Top is negative above it. Reflect
            // to this engine's Y-up ink convention.
            return (-b.Bottom / 1000.0, -b.Top / 1000.0);
        });
    }

    // The bundled Emmentaler face, for LAYOUT-side glyph OUTLINE measurement (the
    // skyline walk over a music glyph). The drawing backends keep their own loaders —
    // those are drawing devices per backend; this is the one measurement home. Null
    // when no bundled font directory is found (callers fall back to the designed box).
    // ⚠️ ONE FACE PER DESIGN. Emmentaler is optically sized, so a grob at another font-size
    // walks ANOTHER FILE's outline, not this one scaled (EmmentalerFaces / GlyphMetrics.
    // AtFontSize). Cached per design because a score asks for a handful and asks per glyph.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, SKTypeface?>
        MusicFaces = new();

    private static SKTypeface? MusicFace(int design) =>
        MusicFaces.GetOrAdd(design, static d =>
        {
            var path = FontLocator.ResolveFile(EmmentalerFaces.OtfFile(d));
            return path != null ? SKTypeface.FromFile(path) : null;
        });

    /// <summary>
    /// Outline path of one Emmentaler glyph at 1000 units/em (the same frame
    /// <c>OutlinePath</c> serves for text), or null when the bundled music font
    /// cannot be located. <paramref name="design"/> is the Emmentaler design to walk —
    /// the score's own unless the grob states another <c>font-size</c>.
    /// </summary>
    internal static SKPath? MusicGlyphPath(char glyph, int design)
    {
        var face = MusicFace(design);
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
    internal static SKTypeface Typeface(bool sans, FontStyle style)
        => Face(TextFace.Bundled(sans, style));

    /// <inheritdoc cref="Typeface(bool, FontStyle)"/>
    internal static SKTypeface Typeface(TextFace face) => Face(face);

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
    /// Width, per em, of a character the bundled face cannot draw — what
    /// <c>Advance</c> spends for it before the pixel snap.
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

    private static SKTypeface Face(TextFace face)
        => Faces.GetOrAdd(face, static key =>
        {
            // The bundle first, the machine second — see BundledPathForName.
            var path = key.IsBundled ? BundledPath(key) : BundledPathForName(key.Name!, key.Style);
            if (path != null)
            {
                return SKTypeface.FromFile(path)
                    ?? throw new InvalidOperationException(
                        $"Bundled text font '{path}' failed to load.");
            }
            return SystemFace(key.Name!, key.Style) ?? throw Unmeasurable(key);
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
