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

using LilySharp.Core.Png;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests.Rendering;

public sealed class SharedRendererPngTests
{
    [Fact]
    public void PngGenerator_DirectPath_ProducesValidPng()
    {
        // Direct PNG path: SyntaxTree → SharedRenderer → SKCanvas → PNG
        // (no SVG roundtrip via Svg.Skia).
        var source = """
            key C major
            time 4/4

            section Demo {
                line { | c'4 d e f | g2 e | c1 | }
            }

            form main { Demo }
            score main "out" { staff line }
            """;
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));

        var bytes = PngGenerator.Generate(tree, PngRenderOptions.Default);

        // PNG magic: 89 50 4E 47 0D 0A 1A 0A
        Assert.True(bytes.Length > 1_000, $"PNG too small: {bytes.Length} B");
        Assert.Equal(0x89, bytes[0]);
        Assert.Equal((byte)'P', bytes[1]);
        Assert.Equal((byte)'N', bytes[2]);
        Assert.Equal((byte)'G', bytes[3]);
    }

    [Fact]
    public void PngGenerator_OssiaSample_FitsBothStaves()
    {
        // ossia.lys exercises the BeginGroup transform path (ossia staff
        // scaled to 0.65× via SharedRenderer's group scope). The PNG should
        // be tall enough to hold both staves stacked vertically.
        var source = """
            key C major
            time 4/4

            section Main {
                melody { | c'4 d e f | g2 e | c1 | }
                ossia_melody { | c'4 e g e | a2 f | e1 | }
            }

            form main { Main }

            score main "ossia" {
                staff melody
                ossia ossia_melody
            }
            """;
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));

        var bytes = PngGenerator.Generate(tree, new PngRenderOptions { Scale = 1.0f });
        Assert.True(bytes.Length > 2_000);
        Assert.Equal(0x89, bytes[0]);
    }

    /// <summary>
    /// The PNG backend really loads the OTHER design's face when a music-face scope asks for
    /// it — the same glyph, the same size, drawn from the 14 and from the 20, must not come
    /// out as the same pixels.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE FAILURE THIS EXISTS FOR IS SILENT. Unlike PDF (whose resolver throws on an
    /// unknown face) SkiaSharp falls back to SKTypeface.Default when a family does not
    /// resolve, so a wrong or missing branch in PngDrawingContext.FontCache would draw
    /// something — the 20's outline, or tofu — and every other test would stay green.
    /// Emmentaler is optically sized, so identical bytes here mean one file answered twice.
    /// </remarks>
    [Fact]
    public void PngDrawsTheDesignTheMusicFaceScopeAsksFor()
    {
        static byte[] HeadDrawnFrom(int design)
        {
            using var doc = new LilySharp.Core.Rendering.Png.PngDocumentContext(
                new LilySharp.Core.Rendering.Png.PngDocumentOptions { PixelsPerSpace = 40 });
            var gc = doc.BeginPage(3, 3);
            using (gc.MusicFace(design))
                gc.DrawGlyph(LilySharp.Core.Svg.EmmentalerGlyphs.NoteheadBlack, 0.2, 2, 4);
            doc.EndPage();
            doc.Dispose();
            return doc.GetBytes();
        }

        Assert.NotEqual(HeadDrawnFrom(20), HeadDrawnFrom(14));
    }
}
