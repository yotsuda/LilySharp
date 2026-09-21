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

using System;
using LilySharp.Core.Rendering;
using LilySharp.Core.Rendering.Svg;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests.Rendering;

/// <summary>
/// The session's page body buffers (<see cref="SvgPageBuffers"/>, session 456): a keystroke
/// draws its pages into the builders the previous render handed back, which is worth
/// 508,076 B per keystroke over the corpus and must be worth nothing at all to the picture.
/// </summary>
/// <remarks>
/// ⚠️ THE POOL CANNOT BE SEEN IN THE OUTPUT WHEN IT WORKS, which is the whole point and also
/// the reason these nets exist in two halves. The EQUALITY half (a pooled document draws what
/// an unpooled one draws) catches a buffer handed out dirty or handed to two pages at once —
/// it is the half that goes red if the pool ever becomes semantic. The LIVENESS half
/// (<see cref="IncrementalCompiler.PageBufferStats"/>) catches the opposite failure, a pool
/// that quietly stops serving: nothing in the picture would ever notice that, and the 508 KB
/// would drain away one refactoring at a time.
/// </remarks>
public sealed class SvgPageBufferTests
{
    private static readonly SvgRenderOptions Interactive =
        new() { EmbedFont = false, Interactive = true };

    /// <summary>Two pages with a mark whose Y is <paramref name="y"/> — so a page carrying
    /// an earlier document's text can be named, not merely inferred from an inequality.</summary>
    private static string Draw(double y, SvgPageBuffers? buffers)
    {
        var doc = new SvgDocumentContext(new SvgDocumentOptions { OmitFontFace = true }, buffers);
        for (int p = 0; p < 2; p++)
        {
            var gc = doc.BeginPage(80, 67);
            gc.DrawLine(0, y, 80, y, Color.Black, 0.1);
            doc.EndPage();
        }
        doc.Dispose();
        string svg = doc.ToSvg();
        doc.ReleaseBuffers();
        return svg;
    }

    [Fact]
    public void ADocumentDrawnIntoParkedBuffers_IsTheDocumentDrawnIntoFreshOnes()
    {
        var pool = new SvgPageBuffers();
        string first = Draw(13, pool);
        Assert.Equal((0, 2, 0), (pool.Stats.Served, pool.Stats.Fresh, 0));

        string second = Draw(41, pool);
        Assert.Equal(2, pool.Stats.Served);          // both pages came back from the pool
        Assert.Equal(2, pool.Stats.Fresh);           // and nothing new was built
        Assert.Equal(Draw(41, null), second);        // byte for byte, the unpooled document
        Assert.DoesNotContain("13.00", second, StringComparison.Ordinal);  // not one char of it stayed
        Assert.Contains("13.00", first, StringComparison.Ordinal);         // …which is a real claim
    }

    [Fact]
    public void AReleasedDocument_SaysSo_RatherThanAnsweringAnEmptyOne()
    {
        var pool = new SvgPageBuffers();
        var doc = new SvgDocumentContext(new SvgDocumentOptions { OmitFontFace = true }, pool);
        var gc = doc.BeginPage(80, 67);
        gc.DrawLine(0, 0, 80, 0, Color.Black, 0.1);
        doc.EndPage();
        doc.Dispose();

        string svg = doc.ToSvg();
        doc.ReleaseBuffers();
        Assert.Equal(svg, doc.ToSvg());   // the answer was taken before the buffers went back
        Assert.Throws<InvalidOperationException>(() => doc.ToPages(null, default));
    }

    [Fact]
    public void TheSession_DrawsEveryLaterPageIntoAParkedBuffer_AndTheSameBytes()
    {
        string src = SvgPageSetTests.MultiPageBook();
        var tree = SyntaxTree.Parse(src);
        var session = new IncrementalCompiler(tree, Interactive);

        var first = session.RenderIncrementalPages(tree, default);
        int pages = first.Pages.Length;
        Assert.True(pages >= 3, $"the book should span several pages, not {pages}");
        Assert.Equal((0, pages), (session.PageBufferStats.Served, session.PageBufferStats.Fresh));

        // Three keystrokes. Every page of every one of them is drawn into a parked buffer,
        // and every one of them is the full compile of its own text.
        string text = src;
        for (int k = 1; k <= 3; k++)
        {
            text = ReplaceFirstBar(text, k);
            var set = session.RenderIncrementalPages(SyntaxTree.Parse(text), default);
            Assert.Equal(pages * k, session.PageBufferStats.Served);
            Assert.Equal(pages, session.PageBufferStats.Fresh);
            Assert.Equal(
                SvgGenerator.Generate(SyntaxTree.Parse(text), Interactive).Replace("\r\n", "\n"),
                set.ToSvg().Replace("\r\n", "\n"));
        }
    }

    /// <summary>One note of the first bar, edited a different way each keystroke.</summary>
    private static string ReplaceFirstBar(string text, int k)
    {
        int at = text.IndexOf("c8( d e f)", StringComparison.Ordinal);
        Assert.True(at >= 0, "the book's first bar is not where this test expects it");
        string bar = k switch
        {
            1 => "c8( d e g)",
            2 => "c8( d e a)",
            _ => "c8( d e b)",
        };
        return text[..at] + bar + text[(at + "c8( d e f)".Length)..];
    }
}
