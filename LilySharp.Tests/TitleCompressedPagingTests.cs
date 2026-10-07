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
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The book title and the first system are ONE line to LilyPond's page breaker: the title
/// carries no page-break permission, so compress_lines merges the two (PageBreaker.CompressLines)
/// and the merged line is priced by its piggybacked shape and padding, not by the
/// markup-system spring between two lines.
/// MEASURED, LilyPond 2.26.0 on these books' twins (Lab sessions/p851/pg1 and ws): the
/// systems per page below. With the title kept apart Lily# put one system less on page 1.
/// </summary>
[Trait("Category", "Unit")]
public sealed class TitleCompressedPagingTests
{
    [Theory]
    [InlineData("automatic.lys", new[] { 13, 9 })]
    public void TheTitledFirstPage_HoldsWhatLilyPondsDoes(string book, int[] lilyPond)
    {
        var path = Path.Combine(CollectResumeTests.FindRepoRoot(), "audit", "tabfingering", book);
        var tree = SyntaxTree.Parse(File.ReadAllText(path));
        var spec = RenderSpecParser.FindAll(tree).First(s => string.IsNullOrEmpty(s.OutputFile));
        var score = SvgGenerator.CollectScore(tree, spec);
        var layout = new LayoutEngine(score.Paper).Layout(score);
        Assert.Equal(lilyPond, layout.Pages.Select(p => p.Systems.Length).ToArray());
    }
}
