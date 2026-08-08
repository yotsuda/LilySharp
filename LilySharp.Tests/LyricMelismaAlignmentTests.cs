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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A syllable held over following notes (a melisma) is LEFT-aligned on its note
/// column — its ink left lands on the column's alignment extent left edge — while
/// every other syllable stays centred (lyric-melisma-melisma.ly's claim).
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/lyric-engraver.cc:180-183 stop_translation_timestep — a
/// syllable on a melisma_busy voice takes self-alignment-X = lyricMelismaAlignment
/// (default LEFT). MEASURED (lyric-melisma-melisma twin, LilyPond 2.26.0): "looong"
/// ink left 26.9292 == the c16 head's ink left 26.9292, exactly; "ha"/"ho" centred.
/// </remarks>
[Trait("Category", "Unit")]
public class LyricMelismaAlignmentTests
{
    private const string Source = @"
time 4/4
part v { section A { c4 c c16( d e f) g4 | } }
lyrics w { section A { ha ha looong __ ~ ~ ho | } }
form main { A }
score main { staff v with lyrics w }
";

    [Fact]
    public void Collector_MarksTheHeldSyllable_AndOnlyIt()
    {
        var tree = SyntaxTree.Parse(Source);
        var spec = RenderSpecParser.FindFirst(tree);
        Assert.NotNull(spec);
        var score = new MeasureCollector().CollectMultiStaff(tree, spec!);

        Assert.True(score.Lyrics.Single(l => l.Text == "looong").MelismaAlignLeft,
            "the syllable held by __ / ~ markers is the melisma syllable");
        Assert.All(score.Lyrics.Where(l => l.Text != "looong"),
            l => Assert.False(l.MelismaAlignLeft));
    }

    [Fact]
    public void MelismaSyllableInkLeft_LandsOnItsHeadsInkLeft_OthersStayCentred()
    {
        var tree = SyntaxTree.Parse(Source);
        var spec = RenderSpecParser.FindFirst(tree);
        var score = SvgGenerator.CollectScore(tree, spec);
        var layout = new LayoutEngine().Layout(score);

        var lo = layout.LyricLayouts.Single(l => l.Item.Text == "looong");
        var ho = layout.LyricLayouts.Single(l => l.Item.Text == "ho");
        var ml = layout.Systems[0].Measures.Single(m => m.MeasureIndex == lo.Item.MeasureIndex);

        // The heads under the two syllables, from the music itself.
        var items = score.EnumerateStaves().First().Staff.PrimaryVoice.Measures[0].Items;
        MusicItem ItemAt(Fraction timing)
        {
            var onset = Fraction.Zero;
            foreach (var it in items)
            {
                if (onset == timing) return it;
                onset += it.Duration;
            }
            throw new Xunit.Sdk.XunitException($"no item at {timing}");
        }

        // Melisma syllable: ink LEFT == its column + the head's ink left (he.left).
        var loHead = GlyphMetrics.GetNoteheadBBox(GlyphMetrics.NoteValueOf(ItemAt(lo.Item.Timing)));
        double loColX = ml.X + ml.GetXForTiming(lo.Item.Timing);
        Assert.Equal(loColX + loHead.Left, lo.X - lo.Width / 2, 6);

        // Ordinary syllable: ink CENTRE == its column + the head's ink centre.
        var hoHead = GlyphMetrics.GetNoteheadBBox(GlyphMetrics.NoteValueOf(ItemAt(ho.Item.Timing)));
        double hoColX = ml.X + ml.GetXForTiming(ho.Item.Timing);
        Assert.Equal(hoColX + (hoHead.Left + hoHead.Right) / 2, ho.X, 6);
    }
}
