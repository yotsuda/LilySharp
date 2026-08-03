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

    /// <summary>First-to-last text placement on the page, in staff spaces.</summary>
    private static double PdfPlacementSpanSpaces(string text)
    {
        const double PageWidth = 40, PageHeight = 8;
        using var doc = new LilySharp.Core.Rendering.Pdf.PdfDocumentContext(
            new LilySharp.Core.Rendering.Pdf.PdfDocumentOptions
            {
                PointsPerSpace = PdfPointsPerSpace,
                AutoSizePages = true,
            });
        var gc = doc.BeginPage(PageWidth, PageHeight);
        gc.DrawText(text, 1.0, 5.0, Em, "serif");
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
    private static double PngInkWidthSpaces(string text)
    {
        // Wide enough for the unkerned string too, so a regression cannot be clipped into
        // looking right.
        const double PageWidth = 40, PageHeight = 8;
        using var doc = new PngDocumentContext(new PngDocumentOptions { PixelsPerSpace = PixelsPerSpace });
        var gc = doc.BeginPage(PageWidth, PageHeight);
        gc.DrawText(text, 1.0, 5.0, Em, "serif");
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
