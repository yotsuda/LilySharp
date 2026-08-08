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

using System.Globalization;
using System.Text.RegularExpressions;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// repeat-tremolo-chord-rep.ly: tremolos work with chord repetitions (q) — a
/// single-body tremolo prints ONE chord at the combined duration with the
/// subdivision's slashes, and a q inside a two-note tremolo takes the pair's
/// total display duration and beam join like a written chord.
/// LILYPOND-REF: lily/chord-tremolo-engraver.cc — the measured-tremolo engraver.
/// LILYPOND-REF: scm/music-functions.scm:854-920 copy-repeat-chord — q expands
/// before the iterators see it.
/// </summary>
[Trait("Category", "Unit")]
public class RepeatTremoloChordRepTests
{
    private const string BookTwin = """
        octave absolute
        time 4/4
        part v { }
        section Main {
          v {
            <c e g>1 |
            repeat tremolo 4 { q16 }
            repeat tremolo 4 { q16 }
            repeat tremolo 4 { c16 q16 } |
          }
        }
        form main { ~Main }
        score main { staff ~v }
        """;

    [Fact]
    public void SingleBodyQ_CombinesDurationAndCarriesSlashes()
    {
        var tree = SyntaxTree.Parse(BookTwin);
        var score = new MeasureCollector().Collect(tree);
        var items = score.Voice.Measures[1].Items;

        // `repeat tremolo 4 { q16 }` = ONE quarter chord with the 16th's two
        // slashes — the q takes the same combine as a written chord (the slashes
        // used to silently drop). LP prints black heads + 2 slash beams
        // (qtrem twin: heads M217/136 at X 15.73, slashes at X 16.22).
        var single = Assert.IsType<ChordItem>(items[0]);
        Assert.Equal(3, single.Notes.Length);
        Assert.Equal(new Fraction(1, 4), single.BaseDuration);
        Assert.Equal(2, single.TremoloBeams);
    }

    [Fact]
    public void PairBodyQ_TakesPairDisplayAndBeamJoin()
    {
        var tree = SyntaxTree.Parse(BookTwin);
        var score = new MeasureCollector().Collect(tree);
        var items = score.Voice.Measures[1].Items;

        // `repeat tremolo 4 { c16 q16 }`: both members print at the pair's total
        // duration (a half — the measured-tremolo convention) and sound half of
        // it; the beams join first→second. The q member used to skip ALL of
        // this and rendered a bare 16th chord with a flag.
        // LILYPOND-REF: lily/chord-tremolo-engraver.cc — Stem::duration_log from
        //   the total; qtrem twin: both sides print half heads (M303/37 / E0FD).
        var first = Assert.IsType<NoteItem>(items[2]);
        var second = Assert.IsType<ChordItem>(items[3]);
        Assert.Equal(new Fraction(1, 2), first.BaseDuration);
        Assert.Equal(new Fraction(1, 2), second.BaseDuration);
        Assert.Equal(new Fraction(1, 2), second.TimeScale);
        Assert.Equal(2, second.TremoloPairBeams);
        Assert.True(first.HasBeamStart);
        Assert.True(second.HasBeamEnd);
    }

    [Fact]
    public void ChordSlashes_DrawAtTheStem_LikeTheNoteForm()
    {
        // Renderer end: the two single-tremolo chords draw 2 slashes each — the
        // chord path used to skip DrawTremolo entirely (the mirror of its
        // once-missing flag branch). Slash left edge = stem X − width/2, which
        // lands on LP's own slash X (qtrem twin: 16.22 = 16.97 − 0.75).
        var svg = LiveRender.SvgFromRenderSpec(BookTwin);
        var slashes = new List<(double X1, double Y1)>();
        foreach (Match m in Regex.Matches(svg,
            "<line x1=\"([-\\d.]+)\" y1=\"([-\\d.]+)\" x2=\"([-\\d.]+)\" y2=\"([-\\d.]+)\""))
        {
            double x1 = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            double y1 = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            double x2 = double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
            double y2 = double.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture);
            if (x1 != x2 && y1 != y2)
                slashes.Add((x1, y1));
        }
        // 2 slashes on each of the two single-body tremolos; the pair draws
        // beams (polygons), not slashes.
        Assert.Equal(4, slashes.Count);
    }
}
