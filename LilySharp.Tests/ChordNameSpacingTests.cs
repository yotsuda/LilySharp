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
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
public class ChordNameSpacingTests
{
    private static ScoreLayout BuildLayout(string source)
    {
        var tree = SyntaxTree.Parse(source);
        var spec = RenderSpecParser.FindFirst(tree)!;
        var score = SvgGenerator.CollectScore(tree, spec);
        return new LayoutEngine(new LayoutOptions()).Layout(score);
    }

    [Fact]
    public void ChordOnBeatInsideLongerNote_GetsDistinctX()
    {
        // The melody `a4 a g2` has note columns only at beats 0, 1, 2; the Em chord
        // falls on beat 3 — INSIDE the g2 half-note. Its X must interpolate across
        // g2's span (toward the barline), not snap onto the beat-2 column where Dm
        // sits, which previously placed Dm and Em at the same X (they overlapped).
        var layout = BuildLayout("""
            time 4/4
            key c major
            part m { clef treble section A { a4 a g2 | } }
            chords h { section A { f4 g d:m e:m | } }
            form main { A }
            score main { chords h  staff m }
            """);

        var xs = layout.ChordNameLayouts.Select(c => c.X).OrderBy(x => x).ToList();
        Assert.Equal(4, xs.Count);
        // Adjacent (centre-anchored) names must clear each other. The bug placed
        // Dm and Em at the same X (gap 0); a proportional-only fix still left them
        // ~2.1 ss apart — too tight for the ~4 ss-wide "Dm"/"Em" boxes.
        for (int i = 1; i < xs.Count; i++)
            Assert.True(xs[i] - xs[i - 1] >= 2.5,
                $"adjacent chord names overlap (gap < 2.5 ss): "
                + $"[{string.Join(", ", xs.Select(x => x.ToString("F1")))}]");
    }

    [Fact]
    public void ChordOnBeatInsideLongerNote_DoesNotOverhangNextMeasure()
    {
        // The beat-3 chord (Em, inside the g2 half-note) gets its own timing column,
        // so the measure widens to fit it — it must not spill across the barline and
        // crowd the next measure's first chord.
        var layout = BuildLayout("""
            time 4/4
            key c major
            part m { clef treble section A { a4 a g2 | c'4 c c c | } }
            chords h { section A { f4 g d:m e:m | f g c d | } }
            form main { A }
            score main { chords h  staff m }
            """);

        var m0 = layout.ChordNameLayouts.Where(c => c.MeasureIndex == 0).Select(c => c.X).OrderBy(x => x).ToList();
        var m1 = layout.ChordNameLayouts.Where(c => c.MeasureIndex == 1).Select(c => c.X).OrderBy(x => x).ToList();
        Assert.Equal(4, m0.Count);
        Assert.Equal(4, m1.Count);
        Assert.True(m1[0] - m0[^1] >= 3.0,
            $"beat-3 chord overhangs into the next measure: m0 last={m0[^1]:F1}, m1 first={m1[0]:F1}");
    }
}
