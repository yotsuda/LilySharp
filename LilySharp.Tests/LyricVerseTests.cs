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
    public void LyricsRow_Standalone_EvenDistributesSyllables()
    {
        // `lyrics verse` as a score row with no melody: each bar's syllables spread
        // evenly, tagged as a lyrics row.
        var score = Collect(@"
time 4/4
section Main { lyrics verse { Aa bb cc dd | ee ff | } }
structure { Main }
score ""x"" { lyrics verse }
");
        var row = score.Lyrics.Where(l => l.IsLyricsRow).ToList();
        Assert.NotEmpty(row);
        Assert.All(row, l => Assert.True(l.IsLyricsRow));

        var bar0 = row.Where(l => l.MeasureIndex == 0).OrderBy(l => l.Timing).ToList();
        var bar1 = row.Where(l => l.MeasureIndex == 1).OrderBy(l => l.Timing).ToList();
        Assert.Equal(4, bar0.Count);                 // Aa bb cc dd
        Assert.Equal(2, bar1.Count);                 // ee ff
        Assert.Equal(LilySharp.Core.Semantics.Fraction.Zero, bar0[0].Timing);
        // Strictly increasing within a bar (spread out, not collapsed).
        static double Val(LilySharp.Core.Semantics.Fraction f) => f.Numerator / (double)f.Denominator;
        for (int i = 1; i < bar0.Count; i++)
            Assert.True(Val(bar0[i].Timing) > Val(bar0[i - 1].Timing));
    }

    [Fact]
    public void LyricsRow_OneBlock_AutoWrapsByMusicBarCount()
    {
        // A single lyrics-row block of 4 bars, with a 2-bar melody in the score,
        // auto-wraps into 2 stacked verses mapped back onto the same 2 bars.
        var score = Collect(@"
time 4/4
section Main {
  melody { c'4 d e f | g a b c'' | }
  lyrics verse { Aa bb cc dd | ee ff gg hh | Pp qq rr ss | tt uu vv ww | }
}
structure { Main }
score ""x"" { staff melody  lyrics verse }
");
        var row = score.Lyrics.Where(l => l.IsLyricsRow).ToList();
        var v1 = row.Where(l => l.VerseNumber == 1).ToList();
        var v2 = row.Where(l => l.VerseNumber == 2).ToList();

        Assert.NotEmpty(v1);
        Assert.NotEmpty(v2);
        Assert.Contains(v2, l => l.Text == "Pp");
        Assert.All(v2, l => Assert.InRange(l.MeasureIndex, 0, 1)); // wrapped onto bars 0,1
    }

    [Fact]
    public void MultiSection_WrapsEachBlockAtItsOwnSectionLength()
    {
        // Section A = 2 bars; its lyrics-ROW block of 4 bars wraps into 2 verses on
        // bars 0,1 (NOT overrunning into section B's bars 2,3). Section B's block
        // restarts at verse 1 on bars 2,3.
        var score = Collect(@"
time 4/4
section A {
  melody { c'4 d e f | g a b c'' | }
  lyrics verse { Aa bb cc dd | ee ff gg hh | Pp qq rr ss | tt uu vv ww | }
}
section B {
  melody { c'4 d e f | g a b c'' | }
  lyrics verse { Xx yy zz w1 | mm nn oo pp | }
}
structure { A B }
score ""x"" { staff melody  lyrics verse }
");
        var a = score.Lyrics.Where(l => l.Text == "Pp").ToList(); // verse 2 of section A
        Assert.NotEmpty(a);
        Assert.All(a, l => Assert.InRange(l.MeasureIndex, 0, 1)); // wrapped within A
        Assert.All(a, l => Assert.Equal(2, l.VerseNumber));

        var b = score.Lyrics.Where(l => l.Text == "Xx").ToList(); // section B
        Assert.NotEmpty(b);
        Assert.All(b, l => Assert.InRange(l.MeasureIndex, 2, 3)); // in B's bars
        Assert.All(b, l => Assert.Equal(1, l.VerseNumber));       // restarted at verse 1
    }

    [Fact]
    public void CompoundBarlines_AreNotSyllables_AndSplitMeasures()
    {
        // `||` and `|.` in a lyrics row are barlines, not words: they never appear as
        // syllables, and a mid-line `||` advances the bar (Aa/bb in bar 0, cc/dd bar 1).
        var score = Collect(@"
time 4/4
section Main { lyrics verse { Aa bb || cc dd |. } }
structure { Main }
score ""x"" { lyrics verse }
");
        Assert.NotEmpty(score.Lyrics);
        Assert.DoesNotContain(score.Lyrics, l => l.Text.Contains('|'));
        Assert.Equal(0, score.Lyrics.Single(l => l.Text == "Aa").MeasureIndex);
        Assert.Equal(0, score.Lyrics.Single(l => l.Text == "bb").MeasureIndex);
        Assert.Equal(1, score.Lyrics.Single(l => l.Text == "cc").MeasureIndex);
        Assert.Equal(1, score.Lyrics.Single(l => l.Text == "dd").MeasureIndex);
    }

    [Fact]
    public void TrailingHyphenWord_RendersAsCenteredHyphen_BothPaths()
    {
        // A word ending in a hyphen ("Mu-") is a sung syllable WITHOUT the dash that
        // carries a hyphen connector to the next syllable — identically for note-bound
        // lyrics and an independent lyrics row (no literal trailing dash on either).
        var noteBound = Collect(@"
time 4/4
section Main { melody { c'4 d e f | } lyrics melody { Mu- sic is here | } }
structure { Main }
score ""x"" { staff melody }
");
        var mu = noteBound.Lyrics.Single(l => l.Text.StartsWith("Mu"));
        Assert.Equal("Mu", mu.Text);
        Assert.Equal(LilySharp.Core.Svg.Model.LyricConnectorType.Hyphen, mu.ConnectorType);

        var row = Collect(@"
time 4/4
section Main { lyrics verse { Mu- sic is here | } }
structure { Main }
score ""x"" { lyrics verse }
");
        var muRow = row.Lyrics.Single(l => l.Text.StartsWith("Mu"));
        Assert.Equal("Mu", muRow.Text);
        Assert.Equal(LilySharp.Core.Svg.Model.LyricConnectorType.Hyphen, muRow.ConnectorType);
    }

    [Fact]
    public void MultiVerseRow_ReservesTallerBand()
    {
        // A 2-verse auto-wrapped lyrics row marks its staff with TextRowVerses=2 so
        // the layout reserves room for the second line.
        var score = Collect(@"
time 4/4
section Main {
  melody { c'4 d e f | g a b c'' | }
  lyrics verse { Aa bb cc dd | ee ff gg hh | Pp qq rr ss | tt uu vv ww | }
}
structure { Main }
score ""x"" { staff melody  lyrics verse }
");
        var rowStaff = score.StaffGroups.SelectMany(g => g.Staves)
            .Single(s => s.IsTextRow);
        Assert.Equal(2, rowStaff.TextRowVerses);
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
