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
using LilySharp.Core.Rendering;

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
form main { Main }
score main ""x"" { staff melody  lyrics melody }
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
form main { Main }
score main ""x"" { lyrics verse }
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
form main { Main }
score main ""x"" { staff melody  lyrics verse }
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
form main { A B }
score main ""x"" { staff melody  lyrics verse }
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
form main { Main }
score main ""x"" { lyrics verse }
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
form main { Main }
score main ""x"" { staff melody  lyrics melody }
");
        var mu = noteBound.Lyrics.Single(l => l.Text.StartsWith("Mu"));
        Assert.Equal("Mu", mu.Text);
        Assert.Equal(LilySharp.Core.Svg.Model.LyricConnectorType.Hyphen, mu.ConnectorType);

        var row = Collect(@"
time 4/4
section Main { lyrics verse { Mu- sic is here | } }
form main { Main }
score main ""x"" { lyrics verse }
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
form main { Main }
score main ""x"" { staff melody  lyrics verse }
");
        var rowStaff = score.StaffGroups.SelectMany(g => g.Staves)
            .Single(s => s.IsTextRow);
        Assert.Equal(2, rowStaff.TextRowVerses);
    }

    [Fact]
    public void RestBarBeforeLyrics_ExplicitEmptyPairSkipsTheRestBar()
    {
        // The melody opens with a whole-rest bar (r1); the lyrics skip it with an
        // EXPLICIT empty bar — the leading "| |" pair (the bare-barline rule: an
        // empty measure is always a visible pair). "Twin" lands on the first NOTE
        // bar (index 1); nothing lands on the rest bar.
        var score = Collect(@"
time 4/4
section Main {
  melody { r1 | c'4 d' e' f' | g'4 a' b' c'' | }
  lyrics melody { | | Twin- kle twin- kle | lit- tle star | }
}
form main { Main }
score main ""x"" { staff melody  lyrics melody }
");
        Assert.Equal(1, score.Lyrics.Single(l => l.Text == "Twin").MeasureIndex);
        Assert.Equal(2, score.Lyrics.Single(l => l.Text == "lit").MeasureIndex);
        // Nothing lands on the rest bar (index 0).
        Assert.DoesNotContain(score.Lyrics, l => l.MeasureIndex == 0);
    }

    [Fact]
    public void LeadingBareBarline_AnchorsOnly_NoEmptyBar()
    {
        // A lone leading '|' merely anchors the run's start — the music rule now
        // holds in lyrics too, so the fenced style `| きら | ひかる |` aligns with
        // the melody above instead of silently shifting the verse one bar over.
        var fenced = Collect(@"
time 4/4
section Main {
  melody { c'4 d' e' f' | g'4 a' b' c'' | }
  lyrics melody { | Twin- kle twin- kle | lit- tle star | }
}
form main { Main }
score main ""x"" { staff melody  lyrics melody }
");
        Assert.Equal(0, fenced.Lyrics.Single(l => l.Text == "Twin").MeasureIndex);
        Assert.Equal(1, fenced.Lyrics.Single(l => l.Text == "lit").MeasureIndex);

        // …and it is exactly equivalent to the unfenced spelling.
        var plain = Collect(@"
time 4/4
section Main {
  melody { c'4 d' e' f' | g'4 a' b' c'' | }
  lyrics melody { Twin- kle twin- kle | lit- tle star | }
}
form main { Main }
score main ""x"" { staff melody  lyrics melody }
");
        Assert.Equal(
            plain.Lyrics.Select(l => (l.Text, l.MeasureIndex, l.ItemIndex)),
            fenced.Lyrics.Select(l => (l.Text, l.MeasureIndex, l.ItemIndex)));
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
form main { Main }
score main ""x"" { staff melody  lyrics melody }
");
        Assert.NotEmpty(score.Lyrics);
        Assert.All(score.Lyrics, l => Assert.Equal(0, l.MeasureIndex));
    }

    /// <summary>
    /// Every verse gets its OWN up-skyline, built from its own syllables — not verse 1's
    /// repeated, and not one merged skyline for the whole line.
    /// </summary>
    /// <remarks>
    /// This is the stored value behind <c>lyrics.verse-step</c> (audit/lp-geometry, open at
    /// +0.400000). LilyPond spaces a second lyric line from the first by the UPPER line's
    /// <c>nonstaff-nonstaff-spacing</c> — <c>((basic-distance . 0) (minimum-distance . 2.8)
    /// (padding . 0.2))</c>, ly/engraver-init.ly:653-656, reached through
    /// page-layout-problem.cc:1315-1332 — and a zero ideal under a minimum makes the
    /// realized step <c>max(2.8, the two lines' ink + 0.2)</c>. Lily# steps by a flat
    /// <c>VerseSpacing</c>, so it cannot answer that; per-verse ink is the input it was
    /// missing.
    /// <para>
    /// ⚠️ ASSERTED BEFORE ANYTHING CONSUMES IT, on purpose (HANDOFF 2E's island order):
    /// nothing in the output moves yet, so without this test the storage change would be
    /// invisible and could rot before the placement change arrives. It asserts what a
    /// skyline is FOR — that a verse's height follows that verse's own text — rather than
    /// re-stating the em fractions, which would only compare the implementation with
    /// itself (HANDOFF 5.4).
    /// </para>
    /// </remarks>
    [Fact]
    public void EachVerseGetsItsOwnUpSkyline()
    {
        // Verse 1 is all x-height ("no"); verse 2 reaches the ascender line ("hi"). Same
        // system, same measure, same X — so the ONLY thing that can separate the two
        // skylines is which verse's text each was built from.
        static LilySharp.Core.Svg.Layout.LyricLayout Syllable(string text, int verse) =>
            new(new LilySharp.Core.Svg.Model.LyricItem(text, MeasureIndex: 0, ItemIndex: 0)
                { VerseNumber = verse },
                X: 3.0, YUp: -5.0, Width: 1.2);

        // The line key: these syllables are note-bound and share the block below the system,
        // which is the -1 the chain uses for it.
        var byVerse = LilySharp.Core.Svg.Layout.LyricEngraver.BuildVerseUpSkylines(ScoreTextMetrics.Bundled, 
            new[] { Syllable("no", 1), Syllable("hi", 2) },
            new Dictionary<int, int> { [0] = 0 },
            _ => -1);

        Assert.Equal(2, byVerse.Count);
        double verse1 = byVerse[(0, -1, 1)].MaxHeight();
        double verse2 = byVerse[(0, -1, 2)].MaxHeight();

        // "hi" has an ascender and "no" does not, so verse 2's ink is the taller of the
        // two. Before this, only verse 1 was built at all and verse 2 had no skyline.
        Assert.True(verse2 > verse1 + 0.1,
            $"verse 2's own ink must drive its own skyline: verse 1 {verse1:F6}, "
            + $"verse 2 {verse2:F6}");
    }

}
