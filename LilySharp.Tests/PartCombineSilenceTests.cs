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
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using LilySharp.Core.Svg;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// What <c>combinedStaff</c> does with SILENCE — input/regression/part-combine-silence.ly,
/// "Rests must begin and end simultaneously to be merged into the shared voice."
/// </summary>
/// <remarks>
/// Every expected number below was read off LilyPond 2.26.0's own grob dump of that book
/// (<c>scratch/lpreg/pcsil-{a,b,c}.ly</c> with <c>pcdump.ily</c>), not off this renderer.
/// <para>
/// ⚠️ Read the rest's POSITION, never its <c>Y-offset</c>. Rest_collision chains
/// <c>force_shift_callback_rest</c> onto a rest's Y offset, and that callback applies the
/// incoming offset and then reports the property as 0.0 — so in LilyPond every rest that
/// shares a moment with another rest reads back as Y-offset 0 wherever it is actually drawn.
/// The dump was wrong in exactly that way for a whole session; the positions here are
/// system-relative, minus the staff's own.
/// LILYPOND-REF: lily/rest-collision.cc:46-66 force_shift_callback_rest.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class PartCombineSilenceTests
{
    private const string Defaults = "octave absolute\ntime 4/4\n";

    private static string Combined(string partOne, string partTwo) => Defaults + $$"""
        part vone { clef treble }
        part vtwo { clef treble }
        section A {
          vone { {{partOne}} }
          vtwo { {{partTwo}} }
        }
        form main { ~A }
        score main { combinedStaff { vone vtwo } }
        """ + "\n";

    private static string Svg(string source) => SvgGenerator.Generate(
        SyntaxTree.Parse(source),
        new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false });

    /// <summary>
    /// Every music glyph except the clef and the time signature, as (x, staff position in
    /// staff spaces above the centre line) — the frame the LilyPond dump reports in.
    /// </summary>
    /// <remarks>
    /// The clef and the metre are the two leftmost glyphs on these pages: none of these bars
    /// changes key or metre, so dropping the two smallest x values leaves the music.
    /// ⚠️ <c>data-pos</c> is required, and that is what keeps AUGMENTATION DOTS out. A dot is
    /// drawn as a music glyph of its own with no source position, at its note's y and a
    /// larger x — so without this filter a dotted note reads as a second note column at the
    /// same height, and <c>f2.</c> silently doubles.
    /// </remarks>
    private static List<(double X, double Y)> Glyphs(string svg) =>
        Regex.Matches(svg, "<text class=\"music\"[^>]*x=\"([-\\d.]+)\" y=\"([-\\d.]+)\"[^>]*data-pos=")
            .Select(m => (X: double.Parse(m.Groups[1].Value),
                          Y: Math.Round(11.69 - double.Parse(m.Groups[2].Value), 3)))
            .OrderBy(g => g.X)
            .Skip(2)
            .ToList();

    /// <summary>The glyphs grouped into note columns, each column's positions high to low.</summary>
    private static List<double[]> Columns(string svg) =>
        Glyphs(svg)
            .GroupBy(g => g.X)
            .OrderBy(g => g.Key)
            .Select(g => g.OrderByDescending(h => h.Y).Select(h => h.Y).ToArray())
            .ToList();

    private static List<string> Labels(string svg) =>
        Regex.Matches(svg, "<text (?![^>]*class=\"music\")[^>]*>([^<]+)</text>")
            .Select(m => m.Groups[1].Value)
            .Where(t => t is "a2" or "Solo" or "Solo II")
            .ToList();

    /// <summary>Score 1 of the book: rests only, ending in a bar of unisilence.</summary>
    private static string ScoreOne() =>
        Combined("r4 r2 r8 r8 | r1 |", "r8 r8 r2 r4 | r1 |");

    [Fact]
    public void RestsAreMergedOnlyWhenTheyBeginAndEndTogether()
    {
        // LilyPond's own six columns for this bar and a half:
        //   moment 0    r4 against r8  -> kept apart, BOTH printed, +2 / -2
        //   moment 1/8  part two alone                              -2
        //   moment 1/4  r2 against r2  -> MERGED, one rest in the shared voice, 0
        //   moment 3/4  r8 against r4  -> kept apart, BOTH printed, +2 / -2
        //   moment 7/8  part one alone                              +2
        //   bar 2       r1 against r1  -> MERGED, one rest, +1 (a semibreve hangs from the
        //                                 4th line, which the shared voice's neutral
        //                                 direction puts it on)
        // The two merges are the claim; the two non-merges are its positive control, and
        // they are the same PAIR of durations in the opposite order, so nothing here can be
        // explained by "the longer one wins".
        List<double[]> expected =
            [[2, -2], [-2], [0], [2, -2], [2], [1]];

        var columns = Columns(Svg(ScoreOne()));

        Assert.Equal(expected.Count, columns.Count);
        for (int i = 0; i < expected.Count; i++)
            Assert.Equal(expected[i], columns[i]);
    }

    [Fact]
    public void ASilencedPartStillSpendsItsTime()
    {
        // The regression this book found. Part two's r2 is dropped — it is the merged half
        // of the unisilence — and part two's NEXT rest, the r4, must still begin at 3/4.
        // Routing the dropped item to LilyPond's NullVoice keeps the part's clock running;
        // deleting the item outright pulled the r4 forward into the 1/4 column, where
        // LilyPond has no lower rest at all.
        var columns = Columns(Svg(ScoreOne()));

        Assert.Equal([0], columns[2]);        // the 1/4 column: the merged half rest, alone
        Assert.Equal([2, -2], columns[3]);    // the 3/4 column: part two's r4 is HERE
    }

    [Fact]
    public void AnOrdinaryRestIsPreferredToAMultiMeasureRest()
    {
        // Score 3 of the book. A whole-bar R1 in one part against r4 in the other is not a
        // merge and not two voices: LilyPond's synced apart-silence picks the ORDINARY rest
        // and sends the multi-measure rest to the null voice, so NO multi-measure rest is
        // engraved anywhere in this score. The surviving rest is in the shared voice, so it
        // carries no voice direction and sits on the centre line.
        //   bar 1: r4 (part one, 0.0) then f2. "Solo"      -1.5
        //   bar 2: r4 (part two, 0.0) then d2. "Solo II"   -2.5
        var svg = Svg(Combined("r4 f2. | R1 |", "R1 | r4 d2. |"));

        Assert.Equal([[0], [-1.5], [0], [-2.5]], Columns(svg));
        Assert.Equal(["Solo", "Solo II"], Labels(svg));
    }

    [Fact]
    public void ASoloThatStartsInsideTheOtherPartsRestIsStillPlacedLate()
    {
        // ⚠️ A NAIL, not a claim of correctness. Score 2 of the book, bar 1: part one holds
        // r4 in the "one" voice while part two's solo starts an eighth later, so LilyPond has
        // two voices sounding at once — and one Lily# voice cannot hold two overlapping
        // items, so the solo is appended after the rest instead of at its own moment.
        // LilyPond's three columns are 2.400 apart; this tree's are 4.600 then 2.750.
        // Materialise says so in its remarks; without this the residual is silent, and a
        // silent wrong position is the thing the corpus keeps getting caught by.
        // When the solo/shared stream can move to the other slot, this test should FAIL and
        // be replaced by LilyPond's numbers.
        var columns = Glyphs(Svg(Combined("r4 f2. | r8 f e2. |", "r8 d f2. | r4 e2. |")))
            .Select(g => g.X)
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        Assert.True(columns[1] - columns[0] > 3.0,
            $"the first gap is {columns[1] - columns[0]:F2}; LilyPond's is 2.400 — if this is "
            + "now near 2.4 the overlap has been fixed and the expectation must change");
    }

    [Fact]
    public void NotCombiningKeepsBothHalfRests()
    {
        // The control for the merge: the same music on a condensed staff — two voices on one
        // staff, no combining — keeps BOTH half rests, so the single rest in the combined
        // score is the combiner's doing and not something the renderer would have done anyway.
        // ⚠️ Counts only. A condensed staff draws both of them ON THE CENTRE LINE, one on top
        // of the other, where LilyPond's \voiceOne / \voiceTwo control (scratch/lpreg/
        // pcsil-ctl.ly) puts them at +2 / -2: nothing stamps a voice direction on the rests of
        // a staff whose voices came from separate parts. That is a condensedStaff defect,
        // filed in HANDOFF §1, and asserting the broken y here would nail it in place.
        var svg = Svg(Defaults + """
            part vone { clef treble }
            part vtwo { clef treble }
            section A {
              vone { r4 r2 r8 r8 | r1 | }
              vtwo { r8 r8 r2 r4 | r1 | }
            }
            form main { ~A }
            score main { condensedStaff { vone vtwo } }
            """ + "\n");

        var columns = Columns(svg);
        Assert.Equal(2, columns[2].Length);   // the 1/4 column keeps both half rests
        Assert.Equal(2, columns[5].Length);   // and bar 2 keeps both semibreve rests

        // …while the combined score has one of each, which is the claim of the book.
        var combined = Columns(Svg(ScoreOne()));
        Assert.Equal([0], combined[2]);
        Assert.Equal([1], combined[5]);
    }
}
