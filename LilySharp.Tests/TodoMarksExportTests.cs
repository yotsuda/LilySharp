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
using System.Text.RegularExpressions;
using LilySharp.Core.Export;
using LilySharp.Core.Pdf;
using LilySharp.Core.Png;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using SkiaSharp;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// <c>lysc svg|png|pdf --todo</c> (owner's request 2026-10-12, for the site's lesson
/// pictures): an export that asks for them draws the preview's <c>@todo</c> marks — the red
/// rings and the memos, on the room the preview opens for them — and an export that does not
/// stays as it was.
/// </summary>
[Trait("Category", "Unit")]
public class TodoMarksExportTests
{
    private static SyntaxTree Book() => SyntaxTree.Parse(
        "octave absolute\npart m { clef treble }\nsection A { m { c'4@todo(\"why\") d'4 r4@todo e'4 | } }\nform { ~A }\nscore { staff m }\n");

    private static readonly Regex Ring = new("<path d=\"[^\"]*\"[^>]*fill=\"none\"[^>]*stroke=\"#E53935\"", RegexOptions.Compiled);
    private static readonly Regex Height = new("<svg[^>]*\\sheight=\"([0-9.]+)", RegexOptions.Compiled);

    [Fact]
    public void AnSvgAskedForTheMarks_DrawsThePreviewsMarks_OnThePreviewsRoom()
    {
        var tree = Book();
        string marked = SvgGenerator.Generate(tree, new SvgRenderOptions { EditorMarks = true });
        string preview = SvgGenerator.Generate(tree, SvgRenderOptions.Preview());
        string plain = SvgGenerator.Generate(tree, SvgRenderOptions.Default);

        // The same rings and memo as the preview — two marked items, eight paths a ring.
        Assert.Equal(16, Ring.Matches(marked).Count);
        Assert.Equal(Ring.Matches(preview).Count, Ring.Matches(marked).Count);
        Assert.Contains(">why<", marked);
        // On the room the preview opens for a memo (LayoutOptions.TodoBands), not on the
        // export's page — the memo would otherwise climb into the title.
        Assert.Equal(Height.Match(preview).Groups[1].Value, Height.Match(marked).Groups[1].Value);
        // The static page stays static: no click targets.
        Assert.DoesNotContain("nh-hit", marked);

        Assert.Empty(Ring.Matches(plain));
        Assert.DoesNotContain(">why<", plain);
    }

    [Fact]
    public void APngAskedForTheMarks_HasTheirRedInk_AndOneNotAsked_None()
    {
        var tree = Book();
        static int RedPixels(byte[] png)
        {
            using var bitmap = SKBitmap.Decode(png);
            int n = 0;
            for (int y = 0; y < bitmap.Height; y++)
                for (int x = 0; x < bitmap.Width; x++)
                {
                    var c = bitmap.GetPixel(x, y);
                    if (c.Red > 180 && c.Green < 110 && c.Blue < 110) n++;
                }
            return n;
        }
        Assert.True(RedPixels(PngGenerator.Generate(tree, new PngRenderOptions { Scale = 1, EditorMarks = true })) > 50);
        Assert.Equal(0, RedPixels(PngGenerator.Generate(tree, new PngRenderOptions { Scale = 1 })));
    }

    [Fact]
    public void APdfAskedForTheMarks_DiffersFromOneNotAsked()
    {
        var tree = Book();
        byte[] marked = PdfGenerator.Generate(tree, new PdfRenderOptions { EditorMarks = true });
        byte[] plain = PdfGenerator.Generate(tree, new PdfRenderOptions());
        Assert.NotEqual(plain.Length, marked.Length);
    }

    [Fact]
    public void TheExportsTodoSetting_ReachesTheFile()
    {
        var tree = Book();
        string path = Path.Combine(Path.GetTempPath(), $"todo-marks-{System.Guid.NewGuid():N}.svg");
        try
        {
            ScoreExport.Write(tree, "svg", path, null, new ExportOptions { TodoMarks = true });
            Assert.Equal(16, Ring.Matches(File.ReadAllText(path)).Count);
            ScoreExport.Write(tree, "svg", path, null);
            Assert.Empty(Ring.Matches(File.ReadAllText(path)));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
