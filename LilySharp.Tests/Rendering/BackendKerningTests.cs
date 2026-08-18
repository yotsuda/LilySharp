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
using System.Text.RegularExpressions;
using LilySharp.Core.Rendering;
using LilySharp.Core.Rendering.Png;
using SkiaSharp;
using Xunit;

namespace LilySharp.Tests.Rendering;

/// <summary>
/// What a backend DRAWS has to be the width the layout RESERVED for it.
/// </summary>
/// <remarks>
/// ⚠️ WHY THIS FILE EXISTS. The engine reserves text width through
/// <see cref="TextFontMetrics.Advance"/>, which shapes with HarfBuzz and therefore carries pair
/// kerning, because that is what LilyPond's Pango measurement does. The PNG and PDF backends
/// drew the string with no shaping at all, so the ink was the UNKERNED sum: on 2026-08-03 the
/// title "VAVAVAVAVA" drew 3.16 staff spaces (63 px) past what layout had reserved for it. The
/// three backends disagreed with each other too — SVG hands the whole string to the viewer,
/// which shapes it, so SVG alone matched.
/// <para>
/// It survived because NOTHING WATCHED IT: the snapshot corpus is 657 SVG files and no PNG or
/// PDF at all, and SVG is precisely the backend that was already right. These are the first
/// observers those two backends have.
/// </para>
/// <para>
/// ⚠️ THE PAIR IS THE POINT, not either width alone. The two strings are PERMUTATIONS of each
/// other and share their first and last glyph, so their side bearings are identical and the
/// difference of their INK widths is exactly the difference of their ADVANCE widths — an
/// equality that holds without knowing the face's side bearings, its kern values, or how the
/// rasteriser antialiases an edge. Without kerning the two are the same width, which is what
/// makes "no kerning" falsifiable rather than merely visible.
/// </para>
/// </remarks>
public sealed class BackendKerningTests
{
    // 5 x V-A and 4 x A-V pairs, against 1 x V-A. Same glyphs, same first, same last.
    private const string Kerned = "VAVAVAVAVA";
    private const string Plain = "VVVVVAAAAA";

    private const double Em = 3.0;             // font size in staff spaces
    private const float PixelsPerSpace = 40f;  // the page scale these are measured at

    [Fact]
    public void PngDrawsTheWidthTheLayoutReserved()
    {
        double reserved = TextFontMetrics.Advance(Kerned, Em) - TextFontMetrics.Advance(Plain, Em);
        double drawn = PngInkWidthSpaces(Kerned) - PngInkWidthSpaces(Plain);

        // The reserved difference is ~2.2 staff spaces here; a backend that does not shape puts
        // exactly 0 on the right-hand side.
        Assert.True(reserved < -1.0, $"the probe pair stopped being a probe: reserved diff {reserved}");
        Assert.True(Math.Abs(reserved - drawn) < 0.05,
            $"reserved {reserved:F6} but drew {drawn:F6} staff spaces of difference");
    }

    /// <summary>
    /// A NAMED face is drawn at the width the layout reserved for it, too.
    /// </summary>
    /// <remarks>
    /// ⚠️ WHY THE NAME IS THE BUNDLED FAMILY'S OWN. A test that named Georgia would pass or
    /// fail by what this machine happens to have installed, and the thing under test is not
    /// the font — it is whether the DRAWING side reaches the file the RESERVATION opened.
    /// Naming <c>TeX Gyre Schola</c> keeps that file the same on every box, because
    /// <see cref="TextFontMetrics"/> consults the bundle before the machine (LilyPond's own
    /// fontconfig ordering), while still taking the named branch in every backend:
    /// <c>ResolvedFace.IsBundled</c> is about whether a name was WRITTEN, not about which
    /// file it lands on.
    /// <para>
    /// ⚠️ The first assertion is what keeps this an observer. If the plan ever starts
    /// resolving as bundled, the rest of the method would quietly re-test the path that was
    /// already right; it says so instead of passing.
    /// </para>
    /// <para>
    /// ⚠️ WHAT THIS DOES NOT WATCH, said out loud. The defect had two halves: the drawing
    /// side did not SHAPE, and it reached its typeface by a second lookup (Skia, by family
    /// name) that need not land on the file the reservation opened. This watches the first
    /// half — poisoned on 2026-08-18 by sending a named face back down the unshaped path,
    /// and it was the only test in 5451 that went red. The second half cannot be watched
    /// deterministically: the two walks are PROVABLY equal for every face a test may name,
    /// because the bundled families resolve to the bundle on both sides and an absent name
    /// falls back to the bundled family on both sides. Only a third-party face that this
    /// machine happens to have installed separates them, which is exactly the test the
    /// bundle-first rule exists to make unnecessary. That half is closed by construction
    /// instead: the second lookup is gone, and there is one walk.
    /// </para>
    /// </remarks>
    [Fact]
    public void PngDrawsTheWidthTheLayoutReservedForANamedFace()
    {
        var plan = new TextFontPlan.Builder()
            .Role(TextRole.Title, [TextFontMetrics.SerifFamily])
            .Build();
        Assert.False(plan.Resolve(TextRole.Title).IsBundled,
            "the probe plan resolved as bundled, so this no longer watches the named path");

        var metrics = new ScoreTextMetrics(plan);
        double reserved = metrics.Advance(Kerned, Em, TextRole.Title)
                        - metrics.Advance(Plain, Em, TextRole.Title);
        double drawn = PngInkWidthSpaces(Kerned, plan) - PngInkWidthSpaces(Plain, plan);

        // Writing the bundled family's own name must not become a way to get a DIFFERENT
        // Schola: the reservation is the same number the default plan reserves.
        Assert.Equal(TextFontMetrics.Advance(Kerned, Em) - TextFontMetrics.Advance(Plain, Em),
            reserved, 9);
        Assert.True(reserved < -1.0, $"the probe pair stopped being a probe: reserved diff {reserved}");
        Assert.True(Math.Abs(reserved - drawn) < 0.05,
            $"reserved {reserved:F6} but drew {drawn:F6} staff spaces of difference");
    }

    /// <summary>
    /// The PDF places its clusters where the layout reserved them.
    /// </summary>
    /// <remarks>
    /// PDF has no rasteriser here, so this reads the page's own content stream instead of ink:
    /// the x of every text-placing operator. Both probe strings end on the same glyph, so the
    /// distance from the first placement to the last differs by exactly what the reservation
    /// differs by — the same cancellation the ink measurement leans on, one layer up.
    /// ⚠️ A backend that does not shape emits ONE placement for the whole string, so the
    /// left-hand side is 0 and this fails loudly rather than by a fraction.
    /// </remarks>
    [Fact]
    public void PdfPlacesTextWhereTheLayoutReservedIt()
    {
        double reserved = TextFontMetrics.Advance(Kerned, Em) - TextFontMetrics.Advance(Plain, Em);
        double a = PdfPlacementSpanSpaces(Kerned), b = PdfPlacementSpanSpaces(Plain);

        Assert.True(reserved < -1.0, $"the probe pair stopped being a probe: reserved diff {reserved}");
        Assert.True(System.Math.Abs(reserved - (a - b)) < 0.05,
            $"reserved {reserved:F6} but drawn {a:F6} - {b:F6} = {a - b:F6}");
    }

    /// <summary>
    /// A NAMED face's clusters land where the layout reserved them, too.
    /// </summary>
    /// <remarks>
    /// The named half of <see cref="PdfPlacesTextWhereTheLayoutReservedIt"/>, and it names the
    /// bundled family for the same reason the PNG one does — see
    /// <see cref="PngDrawsTheWidthTheLayoutReservedForANamedFace"/>.
    /// <para>
    /// ⚠️ WHAT THIS BACKEND MUST NOT DO, and what therefore is not watched. Without
    /// <c>embedded</c> a named face PdfSharpCore has no program for is served by the bundled
    /// STAND-IN, so the glyphs on the page are not the ones the box was measured from and
    /// putting them at the shaped positions would be worse, not better. That case still draws
    /// unshaped, and it cannot be tested deterministically: it needs a third-party face this
    /// machine happens to have installed, which is exactly the dependency the bundle-first
    /// rule exists to keep out of the suite. The divergence is named where it happens —
    /// <c>PdfDrawingContext.DrawText</c>'s <c>LILYSHARP-OWN</c> — and this paragraph is the
    /// "observed by: nothing" that block points at.
    /// </para>
    /// <para>
    /// ⚠️ The <c>embedded</c> arm is unwatchable for the same reason, and was MEASURED once
    /// instead, out of band: <c>scratch/p205/embed.lys</c> with <c>font "Georgia" embedded</c>
    /// on a machine that has Georgia went from 8 whole-string placements to 30 per-cluster
    /// ones, with <c>/BaseFont</c> confirming the page carried Georgia's own program.
    /// </para>
    /// </remarks>
    [Fact]
    public void PdfPlacesTextWhereTheLayoutReservedItForANamedFace()
    {
        var plan = new TextFontPlan.Builder()
            .Role(TextRole.Title, [TextFontMetrics.SerifFamily])
            .Build();
        Assert.False(plan.Resolve(TextRole.Title).IsBundled,
            "the probe plan resolved as bundled, so this no longer watches the named path");

        var metrics = new ScoreTextMetrics(plan);
        double reserved = metrics.Advance(Kerned, Em, TextRole.Title)
                        - metrics.Advance(Plain, Em, TextRole.Title);
        double a = PdfPlacementSpanSpaces(Kerned, plan), b = PdfPlacementSpanSpaces(Plain, plan);

        Assert.True(reserved < -1.0, $"the probe pair stopped being a probe: reserved diff {reserved}");
        Assert.True(Math.Abs(reserved - (a - b)) < 0.05,
            $"reserved {reserved:F6} but drawn {a:F6} - {b:F6} = {a - b:F6}");
    }

    /// <summary>
    /// A name bound under one family, whose FILE belongs to the other, is drawn from its file.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE TWO SIDES USED DIFFERENT RULES. <c>TextFontMetrics</c> resolves a NAME to the
    /// bundled file of that name — <c>TeX Gyre Schola</c> is the serif file whatever family it
    /// was bound under. The PDF resolver served a name it had no program for with the bundled
    /// stand-in of the family it was BOUND under, so <c>font { sans "TeX Gyre Schola" }</c>
    /// reserved Schola and drew Heros: not a shaping difference but a different face, and the
    /// widest kind of divergence this file exists to catch.
    /// <para>
    /// It is deterministic anywhere, because both faces are bundled — the point of the probe
    /// is that the two rules disagree about WHICH bundled file, not whether a machine has one.
    /// </para>
    /// </remarks>
    [Fact]
    public void PdfDrawsTheFileTheReservationMeasured_WhenANameCrossesFamilies()
    {
        var plan = new TextFontPlan.Builder()
            .Role(TextRole.Title, TextFontFamily.Sans)
            .Family(TextFontFamily.Sans, [TextFontMetrics.SerifFamily])
            .Build();
        var resolved = plan.Resolve(TextRole.Title);
        Assert.False(resolved.IsBundled, "the probe plan stopped naming a face");
        Assert.Equal(TextFontFamily.Sans, resolved.Family);

        // The reservation measures the file the NAME owns — the serif one — so the reserved
        // difference is the serif face's kerning, not the sans face's.
        var metrics = new ScoreTextMetrics(plan);
        double reserved = metrics.Advance(Kerned, Em, TextRole.Title)
                        - metrics.Advance(Plain, Em, TextRole.Title);
        Assert.Equal(TextFontMetrics.Advance(Kerned, Em) - TextFontMetrics.Advance(Plain, Em),
            reserved, 9);

        double a = PdfPlacementSpanSpaces(Kerned, plan), b = PdfPlacementSpanSpaces(Plain, plan);
        Assert.True(Math.Abs(reserved - (a - b)) < 0.05,
            $"reserved {reserved:F6} but drawn {a:F6} - {b:F6} = {a - b:F6}");

        // ⚠️ THE PLACEMENTS ALONE DO NOT SETTLE THIS. They come from ShapeRun, which is given
        // the measured face whatever gets drawn — poisoned on 2026-08-18 by taking the family
        // from the BINDING, and the span assertion above stayed green while the page carried
        // the wrong face. The page has to be asked which font it actually used.
        var fonts = PdfBaseFonts(Kerned, plan);
        Assert.Contains(fonts, f => f.Contains("Schola", StringComparison.Ordinal));
        Assert.DoesNotContain(fonts, f => f.Contains("Heros", StringComparison.Ordinal));
    }

    /// <summary>The <c>/BaseFont</c> names a one-string page ends up carrying.</summary>
    /// <remarks>
    /// <c>#20</c> is a PDF name-object escape for a space, so "TeX Gyre Schola" appears as
    /// <c>TeX#20Gyre#20Schola</c> and a subset prefix sits in front of it; the assertions
    /// look for the family word rather than the whole name for both reasons.
    /// </remarks>
    private static IReadOnlyList<string> PdfBaseFonts(string text, TextFontPlan? plan = null)
    {
        const double PageWidth = 40, PageHeight = 8;
        using var doc = new LilySharp.Core.Rendering.Pdf.PdfDocumentContext(
            new LilySharp.Core.Rendering.Pdf.PdfDocumentOptions
            {
                PointsPerSpace = PdfPointsPerSpace,
                AutoSizePages = true,
            });
        if (plan != null) doc.Fonts = plan;
        var gc = doc.BeginPage(PageWidth, PageHeight);
        gc.DrawText(text, 1.0, 5.0, Em, TextRole.Title);
        doc.EndPage();
        doc.Dispose();

        string raw = System.Text.Encoding.Latin1.GetString(doc.GetBytes());
        var names = Regex.Matches(raw, @"/BaseFont\s*/([A-Za-z0-9+\-,#]+)")
            .Select(m => m.Groups[1].Value).Distinct().ToList();
        Assert.NotEmpty(names);
        return names;
    }

    /// <summary>First-to-last text placement on the page, in staff spaces.</summary>
    private static double PdfPlacementSpanSpaces(string text, TextFontPlan? plan = null)
    {
        const double PageWidth = 40, PageHeight = 8;
        using var doc = new LilySharp.Core.Rendering.Pdf.PdfDocumentContext(
            new LilySharp.Core.Rendering.Pdf.PdfDocumentOptions
            {
                PointsPerSpace = PdfPointsPerSpace,
                AutoSizePages = true,
            });
        if (plan != null) doc.Fonts = plan;
        var gc = doc.BeginPage(PageWidth, PageHeight);
        gc.DrawText(text, 1.0, 5.0, Em, TextRole.Title);
        doc.EndPage();
        doc.Dispose();

        var xs = TextPlacementXs(doc.GetBytes());
        Assert.True(xs.Count > 1,
            $"\"{text}\": {xs.Count} placement(s) [{string.Join(", ", xs)}] — the backend emitted "
            + "one run for the whole string, so nothing shaped it");
        // PDF user units are points; the page context scales staff spaces by PointsPerSpace.
        double points = xs.Max() - xs.Min();
        return points / PdfPointsPerSpace;
    }

    /// <summary>Every x a text-placing operator (<c>Td</c> / <c>Tm</c>) sets, in PDF units.</summary>
    private static List<double> TextPlacementXs(byte[] pdf)
    {
        var result = new List<double>();
        string raw = System.Text.Encoding.Latin1.GetString(pdf);
        for (int i = 0; (i = raw.IndexOf("stream", i, StringComparison.Ordinal)) >= 0;)
        {
            int start = i + "stream".Length;
            if (start < pdf.Length && pdf[start] == '\r') start++;
            if (start < pdf.Length && pdf[start] == '\n') start++;
            int end = raw.IndexOf("endstream", start, StringComparison.Ordinal);
            if (end < 0) break;
            i = end;

            // PdfSharpCore leaves page content UNCOMPRESSED here; other objects (embedded font
            // programs) are Flate. Read both — taking only the inflatable ones silently skipped
            // the very stream this test is about and left it measuring a font's internals.
            string content;
            try
            {
                using var ms = new MemoryStream(pdf, start, end - start);
                using var zip = new System.IO.Compression.ZLibStream(ms, System.IO.Compression.CompressionMode.Decompress);
                using var reader = new StreamReader(zip);
                content = reader.ReadToEnd();
            }
            catch
            {
                content = raw[start..end];
            }
            if (!content.Contains("Tj", StringComparison.Ordinal)
                && !content.Contains("TJ", StringComparison.Ordinal))
                continue;   // not a content stream

            // ⚠️ Td IS RELATIVE to the previous line's start, not absolute — reading its
            // operand as a position made every placement look like a small offset and the two
            // probe strings come out identical. Tm sets the matrix outright; BT resets it.
            double pen = 0;
            foreach (Match m in Regex.Matches(content,
                @"\bBT\b"
                + @"|([-\d.]+)\s+([-\d.]+)\s+Td\b"
                + @"|([-\d.]+)\s+([-\d.]+)\s+([-\d.]+)\s+([-\d.]+)\s+([-\d.]+)\s+([-\d.]+)\s+Tm\b"))
            {
                if (m.Groups[1].Success
                    && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double dx))
                    result.Add(pen += dx);
                else if (m.Groups[7].Success
                    && double.TryParse(m.Groups[7].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double tx))
                    result.Add(pen = tx);
                else
                    pen = 0;   // BT
            }
        }
        return result;
    }

    /// <summary>The page context's staff space in PDF points.</summary>
    private const double PdfPointsPerSpace = 10.0;

    /// <summary>The ink width of one string drawn on its own page, in staff spaces.</summary>
    private static double PngInkWidthSpaces(string text, TextFontPlan? plan = null)
    {
        // Wide enough for the unkerned string too, so a regression cannot be clipped into
        // looking right.
        const double PageWidth = 40, PageHeight = 8;
        using var doc = new PngDocumentContext(new PngDocumentOptions { PixelsPerSpace = PixelsPerSpace });
        if (plan != null) doc.Fonts = plan;
        var gc = doc.BeginPage(PageWidth, PageHeight);
        gc.DrawText(text, 1.0, 5.0, Em, TextRole.Title);
        doc.EndPage();
        doc.Dispose();

        using var bitmap = SKBitmap.Decode(doc.GetBytes());
        int left = int.MaxValue, right = -1;
        for (int y = 0; y < bitmap.Height; y++)
            for (int x = 0; x < bitmap.Width; x++)
                if (bitmap.GetPixel(x, y).Red < 128)
                {
                    if (x < left) left = x;
                    if (x > right) right = x;
                }
        Assert.True(right >= 0, $"nothing was drawn for \"{text}\"");
        return (right - left + 1) / (double)PixelsPerSpace;
    }
}
