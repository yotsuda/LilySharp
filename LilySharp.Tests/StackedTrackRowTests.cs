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

using System.Collections.Generic;
using System.Linq;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// ONE track, placed as SEVERAL rows in the same score — <c>chords prog as roman</c> above
/// <c>chords prog as names</c>. Each placement is its own band, in written order, with its
/// own display mode; the track itself is written once.
/// </summary>
/// <remarks>
/// This shape worked before anything asserted it, which is not the same as being supported
/// (user question, session 240 — asked before removing <c>as both</c> in its favour). It
/// works because <c>RenderSpec.GetVoiceBindings</c> yields one binding per ITEM, so two
/// placements of one name carry their own modes and their own staff indices.
/// ⚠️ One structure does NOT distinguish them: <c>MeasureCollector.CollectMultiStaff</c>
/// keys <c>staffVoices</c> by track NAME, so the second placement overwrites the first's
/// entry and both bands end up reading one Voice. That is harmless only because a row's
/// BAR GRID does not depend on its display mode — the two placements want the same bars.
/// It is written down here because it is the seam a future per-row difference would break,
/// and nothing else in the tree says it.
/// </remarks>
[Trait("Category", "Unit")]
public class StackedTrackRowTests
{
    private const string Doc = """
        octave absolute
        time 4/4
        key c major
        part m { clef treble
          section A { c'4 d' e' f' | g' a' b' c'' | }
          section B { c''4 b' a' g' | }
        }
        chords prog {
          section A { C | G7 | }
          section B { F | }
        }
        lyrics v {
          section A { one two | three four | }
          section B { five six | }
        }
        form main { A B }
        """;

    private static MultiStaffScore Collect(string scoreBody)
    {
        var tree = SyntaxTree.Parse($"{Doc}\nscore main {{\n  staff m\n{scoreBody}\n}}\n");
        Assert.False(tree.HasErrors, string.Join(" | ", tree.Diagnostics.Select(d => d.Message)));
        var spec = RenderSpecParser.FindFirst(tree);
        Assert.NotNull(spec);
        return new MeasureCollector().CollectMultiStaff(tree, spec!);
    }

    // (band, mode) for each chord symbol, bands in top-to-bottom order.
    private static List<(int Band, ChordDisplayMode Mode)> ChordBands(MultiStaffScore s)
        => s.ChordNames.OrderBy(c => c.StaffIndex).ThenBy(c => c.MeasureIndex)
            .Select(c => (c.StaffIndex, c.DisplayMode)).Distinct().ToList();

    [Fact]
    public void TwoPlacementsOfOneTrack_AreTwoBandsWithTheirOwnModes()
    {
        var s = Collect("  chords prog as roman\n  chords prog as names");

        // Two distinct bands, roman first because it is written first.
        Assert.Equal(2, ChordBands(s).Count);
        Assert.Equal(ChordDisplayMode.Roman, ChordBands(s)[0].Mode);
        Assert.Equal(ChordDisplayMode.Names, ChordBands(s)[1].Mode);
        // …and the roman band sits ABOVE the names band (a lower staff index is higher).
        Assert.True(ChordBands(s)[0].Band < ChordBands(s)[1].Band);
    }

    [Fact]
    public void TheOrderIsTheWrittenOrder_NotAFixedOne()
    {
        var s = Collect("  chords prog as names\n  chords prog as roman");

        Assert.Equal(ChordDisplayMode.Names, ChordBands(s)[0].Mode);
        Assert.Equal(ChordDisplayMode.Roman, ChordBands(s)[1].Mode);
    }

    [Fact]
    public void EveryBandCarriesTheWholeTrack_SectionsIncluded()
    {
        // The track spans two sections and the form plays A then B, so each band holds all
        // three symbols — a placement is a view of the track, not a slice of it.
        var s = Collect("  chords prog as roman\n  chords prog as names");

        foreach (var band in s.ChordNames.Select(c => c.StaffIndex).Distinct())
            Assert.Equal(new[] { "C", "G7", "F" },
                s.ChordNames.Where(c => c.StaffIndex == band)
                    .OrderBy(c => c.MeasureIndex).Select(c => c.ChordText).ToArray());
    }

    [Fact]
    public void TheSameModeTwice_IsTwoBandsToo()
    {
        // Nothing dedupes placements: two identical rows are two identical bands. Written
        // down because "the same row twice" is the shape a dedupe would silently collapse.
        var s = Collect("  chords prog as names\n  chords prog as names");

        Assert.Equal(2, s.ChordNames.Select(c => c.StaffIndex).Distinct().Count());
    }

    [Fact]
    public void ThreePlacements_AreThreeBands()
    {
        var s = Collect("  chords prog as roman\n  chords prog as names\n  chords prog as roman");

        Assert.Equal(3, s.ChordNames.Select(c => c.StaffIndex).Distinct().Count());
    }

    [Fact]
    public void ALyricsTrackStacksTheSameWay()
    {
        var s = Collect("  lyrics v\n  lyrics v");

        var bands = s.Lyrics.Where(l => l.IsLyricsRow).Select(l => l.StaffIndex).Distinct().ToList();
        Assert.Equal(2, bands.Count);
        foreach (var band in bands)
            Assert.Equal(new[] { "one", "two", "three", "four", "five", "six" },
                s.Lyrics.Where(l => l.IsLyricsRow && l.StaffIndex == band)
                    .OrderBy(l => l.MeasureIndex).ThenBy(l => l.ItemIndex)
                    .Select(l => l.Text).ToArray());
    }

    [Fact]
    public void PlacementsOfDifferentTracksInterleave()
    {
        // A row of another kind between the two placements keeps the bands in written
        // order — the ordering is the score's item list, not a per-track grouping.
        var s = Collect("  chords prog as roman\n  lyrics v\n  chords prog as names");

        var roman = s.ChordNames.First(c => c.DisplayMode == ChordDisplayMode.Roman).StaffIndex;
        var names = s.ChordNames.First(c => c.DisplayMode == ChordDisplayMode.Names).StaffIndex;
        var lyric = s.Lyrics.First(l => l.IsLyricsRow).StaffIndex;

        Assert.True(roman < lyric && lyric < names);
    }
}
