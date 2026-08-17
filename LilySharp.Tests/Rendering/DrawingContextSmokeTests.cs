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
using LilySharp.Core.Rendering.Pdf;
using LilySharp.Core.Rendering.Svg;
using LilySharp.Core.Svg;
using Xunit;

namespace LilySharp.Tests.Rendering;

public sealed class DrawingContextSmokeTests
{
    /// <summary>Exercises every <see cref="IDrawingContext"/> primitive.</summary>
    private static void DrawSamplePage(IDrawingContext gc)
    {
        // Staff lines
        for (int i = 0; i < 5; i++)
            gc.DrawLine(0, 5 + i, 30, 5 + i, Color.Black, 0.13);

        // Barline
        gc.DrawRectangle(30, 5, 0.16, 4, fill: Color.Black);

        // Treble clef
        gc.DrawGlyph(EmmentalerGlyphs.GClef, 1, 8, 4);

        // Time signature 4/4 (two stacked digits at same x)
        gc.DrawGlyph(EmmentalerGlyphs.TimeSig4, 4, 7, 4);
        gc.DrawGlyph(EmmentalerGlyphs.TimeSig4, 4, 9, 4);

        // Notehead + stem
        gc.DrawGlyph(EmmentalerGlyphs.NoteheadBlack, 7, 8, 4);
        gc.DrawLine(7.6, 8, 7.6, 4, Color.Black, 0.1);

        // Filled ellipse (alternate notehead approach)
        gc.DrawEllipse(10, 8, 0.6, 0.42, fill: Color.Black);

        // Slur (closed bezier)
        gc.DrawClosedBezier(
            (7, 7.5), (8, 6.8), (9, 6.8), (10, 7.5),
            (9, 7.0), (8, 7.0),
            fill: Color.Black);

        // Title
        gc.DrawText("Sample", 15, 2, 1.2, TextRole.Title, FontStyle.Bold, TextAnchor.Middle);

        // Source-position scope (SVG: data-pos=42, PDF: ignored)
        using (gc.Source(42))
            gc.DrawCircle(20, 8, 0.3, Color.Black);

        // A glyph out of ANOTHER Emmentaler design (a grace's 14) — every backend has to
        // resolve that face, not just SVG: PdfSharpCore asks its font resolver by family
        // name and SkiaSharp loads a typeface by file, and a missing branch there throws at
        // render time rather than at layout time.
        using (gc.MusicFace(14))
            gc.DrawGlyph(EmmentalerGlyphs.NoteheadBlack, 22, 8, 2.83);

        // Group with scale (ossia)
        using (gc.BeginGroup(new DrawingTransform(25, 5, 0.65, 0.65)))
            gc.DrawGlyph(EmmentalerGlyphs.NoteheadBlack, 0, 2, 4);
    }

    [Fact]
    public void Svg_AllPrimitives_ProduceWellFormedSvg()
    {
        string svg;
        using (var doc = new SvgDocumentContext(new SvgDocumentOptions { EmbedFont = false }))
        {
            var gc = doc.BeginPage(40, 12);
            DrawSamplePage(gc);
            doc.EndPage();
            doc.Dispose();
            svg = doc.ToSvg();
        }

        Assert.Contains("<?xml version=\"1.0\"", svg);
        Assert.Contains("<svg ", svg);
        Assert.Contains("viewBox=\"0 0 40.00 12.00\"", svg);
        Assert.Contains("<line ", svg);
        Assert.Contains("<rect ", svg);
        Assert.Contains("<circle ", svg);
        Assert.Contains("<ellipse ", svg);
        Assert.Contains("<path ", svg);
        Assert.Contains("<text ", svg);
        Assert.Contains("class=\"music\"", svg);
        // The other design names its own face and falls back to the default one, so a viewer
        // without it draws the right glyph from the wrong design instead of tofu.
        Assert.Contains("font-family=\"Emmentaler-14, Emmentaler, serif\"", svg);
        Assert.Contains("data-pos=\"42\"", svg);
        Assert.Contains("transform=\"translate(", svg);
        Assert.EndsWith("</svg>" + Environment.NewLine, svg);
    }

    [Fact]
    public void Pdf_AllPrimitives_ProduceMultiByteDocument()
    {
        byte[] bytes;
        using (var doc = new PdfDocumentContext(new PdfDocumentOptions { PointsPerSpace = 6 }))
        {
            var gc = doc.BeginPage(40, 12);
            DrawSamplePage(gc);
            doc.EndPage();
            doc.Dispose();
            bytes = doc.GetBytes();
        }

        // PDF magic and large enough to contain the embedded font subset
        Assert.True(bytes.Length > 5_000,
            $"PDF should embed Emmentaler subset and be > 5 KB, was {bytes.Length} B.");
        Assert.Equal((byte)'%', bytes[0]);
        Assert.Equal((byte)'P', bytes[1]);
        Assert.Equal((byte)'D', bytes[2]);
        Assert.Equal((byte)'F', bytes[3]);
    }
}
