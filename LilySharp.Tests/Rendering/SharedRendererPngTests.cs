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

            structure { Demo }
            render score "out.svg" { staff { line } }
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

            structure { Main }

            render score "ossia.svg" {
                staff { melody }
                ossia { ossia_melody }
            }
            """;
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));

        var bytes = PngGenerator.Generate(tree, new PngRenderOptions { Scale = 1.0f });
        Assert.True(bytes.Length > 2_000);
        Assert.Equal(0x89, bytes[0]);
    }
}
