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
using LilySharp.Core.Rendering.Png;
using LilySharp.Core.Rendering.Svg;
using Xunit;

namespace LilySharp.Tests.Rendering;

/// <summary>
/// Guards multi-page output in the SVG and PNG document contexts: pages must
/// stack vertically (each at its cumulative Y offset) rather than overlapping
/// (SVG) or being clipped past a fixed reserved height (PNG).
/// </summary>
public sealed class MultiPageDocumentTests
{
    [Fact]
    public void Svg_TwoPages_StackVerticallyWithoutOverlap()
    {
        using var doc = new SvgDocumentContext(new SvgDocumentOptions { OmitFontFace = true });
        for (int p = 0; p < 2; p++)
        {
            var gc = doc.BeginPage(80, 67);
            gc.DrawLine(0, 0, 80, 0, Color.Black, 0.1);
            doc.EndPage();
        }
        doc.Dispose();
        var svg = doc.ToSvg();

        // Root sized to BOTH pages (2 × 67 = 134), not just the first.
        Assert.Contains("viewBox=\"0 0 80.00 134.00\"", svg);
        // Each page wrapped at its own Y offset — second page is NOT at 0.
        Assert.Contains("translate(0, 0.00)", svg);
        Assert.Contains("translate(0, 67.00)", svg);
    }

    [Fact]
    public void Svg_SinglePage_HasNoGroupWrapper()
    {
        using var doc = new SvgDocumentContext(new SvgDocumentOptions { OmitFontFace = true });
        var gc = doc.BeginPage(80, 67);
        gc.DrawLine(0, 0, 80, 0, Color.Black, 0.1);
        doc.EndPage();
        doc.Dispose();
        var svg = doc.ToSvg();

        Assert.Contains("viewBox=\"0 0 80.00 67.00\"", svg);
        Assert.DoesNotContain("<g transform=\"translate(0,", svg);
    }

    [Fact]
    public void Png_FourPlusPages_AreAllIncluded_NotClipped()
    {
        const double pps = 20.0;
        using var doc = new PngDocumentContext(new PngDocumentOptions { PixelsPerSpace = pps });
        const int pageCount = 5;          // exceeds the old fixed ×4 reservation
        const double pageHeightSp = 10.0;
        for (int p = 0; p < pageCount; p++)
        {
            var gc = doc.BeginPage(10, pageHeightSp);
            gc.DrawLine(0, 0, 10, 0, Color.Black, 0.1);
            doc.EndPage();
        }
        doc.Dispose();
        var bytes = doc.GetBytes();

        int height = ReadPngHeight(bytes);
        int expected = (int)(pageCount * pageHeightSp * pps); // 5 × 10 × 20 = 1000
        Assert.Equal(expected, height);
    }

    // Reads the height field from a PNG's IHDR chunk (no SkiaSharp dependency).
    private static int ReadPngHeight(byte[] png)
    {
        Assert.True(png.Length >= 24, "not a PNG");
        Assert.Equal(0x89, png[0]);
        // IHDR height is the 4-byte big-endian value at offset 20.
        return (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
    }
}
