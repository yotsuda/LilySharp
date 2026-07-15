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

using System.Linq;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Navigation marks are score-level (every part shares the bar grid), so a segno written in the
/// piano part must still print on a chords-only / lyrics-only chart that omits the piano staff.
/// </summary>
[Trait("Category", "Unit")]
public class UnrenderedPartStructureMarkTests
{
    // Segno lives in part piano; the score draws only the chord + lyric rows.
    private const string Source = """
        time 4/4
        part piano { clef treble  section Main { segno c4 d e f | g a b c } }
        chords prog { section Main { c1 | g1 } }
        lyrics words { section Main { Twin- kle lit- tle | star how I } }
        form main { Main }
        score main { chords prog  lyrics words }
        """;

    private static MultiStaffScore Collect(string src)
    {
        var tree = SyntaxTree.Parse(src);
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        var spec = RenderSpecParser.FindFirst(tree);
        Assert.NotNull(spec);
        return new MeasureCollector().CollectMultiStaff(tree, spec!);
    }

    [Fact]
    public void SegnoInOmittedPart_SurfacesOnChordsLyricsScore()
    {
        var marks = Collect(Source).MusicMarks;
        // Exactly one segno — harvested from piano, not duplicated.
        Assert.Equal(1, marks.Count(m => m.Type == MusicMarkType.Segno));
    }

    [Fact]
    public void RenderingThePartItself_StillHasExactlyOneSegno()
    {
        // When the carrying part IS drawn the harvest must not double it (the dedup guard).
        var src = Source.Replace("score main { chords prog  lyrics words }",
            "score main { chords prog  staff piano }");
        Assert.Equal(1, Collect(src).MusicMarks.Count(m => m.Type == MusicMarkType.Segno));
    }

    [Fact]
    public void NoNavigationAnywhere_NoStructureMarks()
    {
        var src = Source.Replace("segno c4 d e f", "c4 d e f");
        Assert.DoesNotContain(Collect(src).MusicMarks, m => m.Type == MusicMarkType.Segno);
    }
}
