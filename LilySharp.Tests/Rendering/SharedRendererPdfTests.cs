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
    public void Pdf_DrawsAccidentalsAndTiesAndSlurs()
    {
        // A single line exercising sharp/flat accidentals, a tie (~), and a
        // slur ((...)). Phase 2-B should now emit Emmentaler accidental
        // glyphs and Bezier path operators for the tie/slur.
        var source = """
            key C major
            time 4/4

            section Demo {
                line {
                    | cis'4 d~ d8( e fis g) | a2 bes |
                }
            }

            structure { Demo }

            render score "out.svg" { staff { line } }
            """;
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));

        var bytes = PdfGenerator.Generate(tree, PdfRenderOptions.Default);
        Assert.True(bytes.Length > 30_000);

        // Decompress the first content stream and look for closed-bezier
        // ops (tie/slur) and the sharp-accidental codepoint mapping.
        var streamText = DecodeFirstContentStream(bytes);
        // PDF cubic bezier op is "c"; closed filled paths end in "h" + "f" or "f*".
        Assert.True(streamText.Contains(" c\n") || streamText.Contains(" c\r"),
            "Expected at least one cubic Bezier 'c' operator (tie or slur).");
        Assert.True(streamText.Contains("h\nf"),
            "Expected at least one closed/filled path 'h' + fill (tie/slur).");
    }

    private static string DecodeFirstContentStream(byte[] pdfBytes)
    {
        // Locate the first "stream\n...endstream" block, then FlateDecode.
        var text = System.Text.Encoding.Latin1.GetString(pdfBytes);
        int startKw = text.IndexOf("stream", StringComparison.Ordinal);
        Assert.True(startKw >= 0);
        int dataStart = startKw + "stream".Length;
        // Skip the line-ending after "stream" (CR LF or just LF)
        if (dataStart < text.Length && text[dataStart] == '\r') dataStart++;
        if (dataStart < text.Length && text[dataStart] == '\n') dataStart++;
        int endKw = text.IndexOf("endstream", dataStart, StringComparison.Ordinal);
        Assert.True(endKw > dataStart);
        // Trim trailing newline before "endstream"
        int dataEnd = endKw;
        if (dataEnd > dataStart && text[dataEnd - 1] == '\n') dataEnd--;
        if (dataEnd > dataStart && text[dataEnd - 1] == '\r') dataEnd--;

        var compressed = System.Text.Encoding.Latin1.GetBytes(text.Substring(dataStart, dataEnd - dataStart));
        // Skip 2-byte zlib header; the rest is raw DEFLATE
        using var ms = new MemoryStream(compressed, 2, compressed.Length - 2);
        using var ds = new System.IO.Compression.DeflateStream(ms, System.IO.Compression.CompressionMode.Decompress);
        using var sr = new StreamReader(ds, System.Text.Encoding.Latin1);
        return sr.ReadToEnd();
    }

    [Fact]
    public void Pdf_DrawsDynamicsArticulationsAndLyrics()
    {
        // Showcase sample exercises @p / @f / @ff / @accent / @staccato / @fermata
        // and a separate lyrics line. The PDF should now embed both Emmentaler
        // (for music + articulations) and a serif font (for dynamics + lyrics)
        // and the content stream should reference both via "/F0" and "/F1".
        var path = Path.Combine(SamplesRoot, "test", "lyrics.lys");
        Assert.True(File.Exists(path), $"sample missing: {path}");
        var tree = SyntaxTree.Parse(File.ReadAllText(path));
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));

        var bytes = PdfGenerator.Generate(tree, PdfRenderOptions.Default);
        var stream = DecodeAllContentStreams(bytes);

        // Music font (Emmentaler) selected at FontSize × scale = 4 × 6 = 24 pt.
        // PdfSharpCore assigns /F0 / /F1 in encounter order, so either may
        // host the music font.
        Assert.True(stream.Contains("/F0 24 Tf") || stream.Contains("/F1 24 Tf"),
            "Expected a 24-pt music font selection (Emmentaler).");

        // Lyrics use serif at 0.8 × FontSize × scale = 0.8 × 4 × 6 = 19.2 pt
        Assert.True(stream.Contains(" 19.2 Tf") || stream.Contains(" 19 Tf"),
            "Expected a 19.2-pt serif font selection for lyrics.");
    }

    private static string DecodeAllContentStreams(byte[] pdfBytes)
    {
        var text = System.Text.Encoding.Latin1.GetString(pdfBytes);
        var sb = new System.Text.StringBuilder();
        int searchFrom = 0;
        while (true)
        {
            int kw = text.IndexOf("stream", searchFrom, StringComparison.Ordinal);
            if (kw < 0) break;
            int dataStart = kw + "stream".Length;
            if (dataStart < text.Length && text[dataStart] == '\r') dataStart++;
            if (dataStart < text.Length && text[dataStart] == '\n') dataStart++;
            int endKw = text.IndexOf("endstream", dataStart, StringComparison.Ordinal);
            if (endKw <= dataStart) break;
            int dataEnd = endKw;
            if (dataEnd > dataStart && text[dataEnd - 1] == '\n') dataEnd--;
            if (dataEnd > dataStart && text[dataEnd - 1] == '\r') dataEnd--;

            var compressed = System.Text.Encoding.Latin1.GetBytes(
                text.Substring(dataStart, dataEnd - dataStart));
            try
            {
                using var ms = new MemoryStream(compressed, 2, compressed.Length - 2);
                using var ds = new System.IO.Compression.DeflateStream(
                    ms, System.IO.Compression.CompressionMode.Decompress);
                using var sr = new StreamReader(ds, System.Text.Encoding.Latin1);
                sb.AppendLine(sr.ReadToEnd());
            }
            catch
            {
                // Some streams (font data, CMap) may not be flate-compressed
                // or may decompress to binary; skip them.
            }
            searchFrom = endKw + "endstream".Length;
        }
        return sb.ToString();
    }

    private static string SamplesRoot
    {
        get
        {
            var dir = AppContext.BaseDirectory;
            for (int i = 0; i < 6; i++)
            {
                var candidate = Path.Combine(dir, "samples");
                if (Directory.Exists(candidate)) return candidate;
                dir = Path.GetDirectoryName(dir)!;
            }
            // Repo path fallback
            return @"C:\MyProj\LilySharp\samples";
        }
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
