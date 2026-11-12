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

using System.IO;
using System.Linq;
using LilySharp.Core.Export;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using SkiaSharp;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The shape of a score video's picture (owner's report 2026-11-12: a short song's MP4 was the
/// whole sheet of paper, most of it white above and below the music). A score of one page takes
/// that page's own shape, cut under its music as the PNG export's is; a score of several pages
/// keeps the paper's.
/// </summary>
[Trait("Category", "Unit")]
public class ScoreVideoShapeTests
{
    private static string Book(int bars) => "octave relative\ntime 4/4\npart m { clef treble }\nsection A { m { "
        + string.Concat(Enumerable.Repeat("c'4 d e f | ", bars)) + "} }\nform { A }\nscore { staff m }\n";

    private static (int Width, int Height, int Pages) Picture(string source, ScoreVideoOptions? options = null)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics.Select(d => d.Message)));
        string dir = Path.Combine(Path.GetTempPath(), "lys-video-" + System.Guid.NewGuid().ToString("N"));
        try
        {
            var spec = RenderSpecParser.FindFirst(tree);
            ScoreVideo.WriteFrames(tree, spec, [new VideoNote(0, 0.5, -1, 0)], 1.0, dir, options);
            using var first = SKBitmap.Decode(Directory.GetFiles(dir, "*.png").OrderBy(f => f).First());
            return (first.Width, first.Height, LilySharp.Core.Png.PngGenerator.GenerateScorePages(tree, spec).Count);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void AScoreOfOnePage_IsItsMusic_NotTheSheet()
    {
        var (width, height, pages) = Picture(Book(4));
        Assert.Equal(1, pages);
        // A4 stands 1.41 tall to 1 wide; four bars on it are a strip far wider than tall.
        Assert.True(width > height * 2, $"{width} x {height}");
        // Even, as H.264 asks.
        Assert.Equal(0, width % 2);
        Assert.Equal(0, height % 2);
    }

    [Fact]
    public void AScoreOfSeveralPages_KeepsThePapersShape()
    {
        var (width, height, pages) = Picture(Book(140));
        Assert.True(pages > 1, $"{pages} page(s)");
        Assert.Equal(1.414, height / (double)width, 2);
    }

    // The owner's report of 2026-11-10 stands: a score that states its paper - a video's 16:9 -
    // has that picture, however little music is on it.
    [Fact]
    public void APaperTheScoreStates_KeepsItsShape_OnePageOrNot()
    {
        string source = Book(4).Replace("score { staff m }", "paper yt { paperWidth 256mm  paperHeight 144mm }\nscore { paper yt  staff m }");
        var (width, height, pages) = Picture(source);
        Assert.Equal(1, pages);
        Assert.Equal((1920, 1080), (width, height));
    }

    [Fact]
    public void ASizeThatIsAsked_IsTheSizeGiven()
    {
        var (width, height, _) = Picture(Book(4), new ScoreVideoOptions { Width = 1280, Height = 720 });
        Assert.Equal((1280, 720), (width, height));
    }
}