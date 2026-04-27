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

using LilySharp.Core.Pdf;
using LilySharp.Core.Pdf.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests.Rendering;

public sealed class SharedRendererPdfTests
{
    [Fact]
    public void Ossia_Pdf_EmbedsMusicFontAndExceedsStubSize()
    {
        // Regression: prior to the SharedRenderer migration, ossia.lys
        // produced a 2.3 KB PDF with no music glyphs (just ellipse heads
        // and line stems). The new path embeds Emmentaler and draws clefs,
        // time signature, and proper noteheads, which inflates the file
        // to several tens of KB even for a 3-measure × 2-staff sample.
        var source = """
            key C major
            time 4/4

            section Main {
                melody {
                    | c'4 d e f | g2 e | c1 |
                }
                ossia_melody {
                    | c'4 e g e | a2 f | e1 |
                }
            }

            structure { Main }

            render score "ossia.svg" {
                staff { melody }
                ossia { ossia_melody }
            }
            """;
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));

        var bytes = PdfGenerator.Generate(tree, PdfRenderOptions.Default);

        Assert.True(bytes.Length > 20_000,
            $"Expected ossia.lys PDF > 20 KB (font subset embedded), got {bytes.Length} B.");
        Assert.Equal((byte)'%', bytes[0]);
        Assert.Equal((byte)'P', bytes[1]);
    }

    [Fact]
    public void SingleStaffScore_PromotedToMultiStaff_StillRenders()
    {
        var source = """
            key G major
            time 3/4

            section Hello {
                tune {
                    | g'4 a b | c'2. |
                }
            }

            structure { Hello }

            render score "out.pdf" { staff { tune } }
            """;
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));

        var bytes = PdfGenerator.Generate(tree, PdfRenderOptions.Default);
        Assert.True(bytes.Length > 20_000);
    }
}
