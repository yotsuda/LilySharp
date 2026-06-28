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
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Lyrics written flat in ONE block auto-wrap into stacked verses by the section's
/// bar count, and empty measures (<c>| |</c>) skip a bar without a syllable.
/// </summary>
[Trait("Category", "Unit")]
public class LyricVerseTests
{
    private static LilySharp.Core.Svg.Model.MultiStaffScore Collect(string source)
    {
        var tree = SyntaxTree.Parse(source);
        var spec = RenderSpecParser.FindFirst(tree);
        Assert.NotNull(spec);
        return new MeasureCollector().CollectMultiStaff(tree, spec!);
    }

    [Fact]
    public void OneBlock_WrapsIntoStackedVerses_ByBarCount()
    {
        // Melody = 2 bars; a single lyrics block of 4 bars wraps into 2 verses,
        // verse 2 mapped back onto the same 2 measures.
        var score = Collect(@"
time 4/4
section Main {
  melody { c'4 d e f | g a b c'' | }
  lyrics melody { Aa bb cc dd | ee ff gg hh | Pp qq rr ss | tt uu vv ww | }
}
structure { Main }
score ""x"" { staff melody }
");
        var v1 = score.Lyrics.Where(l => l.VerseNumber == 1).ToList();
        var v2 = score.Lyrics.Where(l => l.VerseNumber == 2).ToList();

        Assert.NotEmpty(v1);
        Assert.NotEmpty(v2);
        // Verse 2 is the second half of the block, wrapped onto the same bars (0,1).
        Assert.Contains(v2, l => l.Text == "Pp");
        Assert.All(v2, l => Assert.InRange(l.MeasureIndex, 0, 1));
        Assert.Equal(v1.Select(l => l.MeasureIndex).Distinct().OrderBy(x => x),
                     v2.Select(l => l.MeasureIndex).Distinct().OrderBy(x => x));
    }

    [Fact]
    public void EmptyMeasure_SkipsBar_NoSyllableThere()
    {
        // Bar 2 of the lyric line is empty (`| |`), so no syllable lands in measure 1.
        var score = Collect(@"
time 4/4
section Main {
  melody { c'4 d e f | g a b c'' | }
  lyrics melody { Aa bb cc dd | | }
}
structure { Main }
score ""x"" { staff melody }
");
        Assert.NotEmpty(score.Lyrics);
        Assert.All(score.Lyrics, l => Assert.Equal(0, l.MeasureIndex));
    }
}
