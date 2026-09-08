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
using Xunit;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;

namespace LilySharp.Tests;

/// <summary>
/// A chord or lyrics ROW's bar is as long as the music's bar at the same index. The row
/// grids its slots on the score meter, so until session 350 a pickup bar (and a bar under
/// a mid-piece meter change) carried a whole meter of row spacer and was priced for it —
/// amazing-grace's one-beat pickup stood 6.34 staff spaces wider with its chords row than
/// without. LILYPOND-REF: ly/engraver-init.ly:756-759 Timing_translator (\alias Timing) —
/// one Timing per Score, so every context's bar is the staff's bar.
/// </summary>
[Trait("Category", "Unit")]
public class RowBarLengthTests
{
    private static MultiStaffScore Collect(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join(" | ", tree.Diagnostics.Select(d => d.Message)));
        var spec = RenderSpecParser.FindFirst(tree);
        return new MeasureCollector().CollectMultiStaff(tree, spec!);
    }

    private static Fraction BarLength(MultiStaffScore score, string rowName, int measure)
    {
        var row = score.EnumerateStaves().Single(t => t.Staff.PrimaryVoice.Name == rowName).Staff;
        var sum = Fraction.Zero;
        foreach (var it in row.PrimaryVoice.Measures[measure].Items)
            sum += it.Duration;
        return sum;
    }

    private const string Pickup = """
        time 3/4
        key g major
        part m { clef treble }
        section A {
          partial 4
          m { d'4 | g'2 b'8 g'8 | g'2 d'4 | }
          chords prog { | G | G | }
        }
        form main { A }
        score main { chords prog  staff m }
        """;

    [Fact]
    public void AChordRowsPickupBar_IsAsLongAsTheMusicsPickup()
    {
        var score = Collect(Pickup);
        Assert.Equal(new Fraction(1, 4), BarLength(score, "prog", 0));
        // The full bars keep the meter.
        Assert.Equal(new Fraction(3, 4), BarLength(score, "prog", 1));
    }

    [Fact]
    public void ARowsBar_UnderAMeterChangeTheGridNeverSaw_IsAsLongAsTheMusics()
    {
        var score = Collect("""
            time 4/4
            key c major
            part m { clef treble }
            section A { m { c'1 | } chords prog { C | } }
            section B { time 3/4  m { d'2. | } chords prog { D | } }
            form main { A B }
            score main { chords prog  staff m }
            """);
        Assert.Equal(new Fraction(1, 1), BarLength(score, "prog", 0));
        Assert.Equal(new Fraction(3, 4), BarLength(score, "prog", 1));
    }

    [Fact]
    public void AnEvenSpreadLyricsRow_ScalesItsSlotsToThePickup_ShareForShare()
    {
        var score = Collect("""
            time 4/4
            key c major
            part m { clef treble }
            section A {
              partial 4
              m { c'4 | d'4 e' f' g' | }
              lyrics words { one | two three four five | }
            }
            form main { A }
            score main { staff m  lyrics words }
            """);
        Assert.Equal(new Fraction(1, 4), BarLength(score, "words", 0));
        Assert.Equal(new Fraction(1, 1), BarLength(score, "words", 1));
    }

    /// <summary>The page: the pickup bar's width does not change when the row is placed.</summary>
    [Fact]
    public void ThePage_APickupBar_IsNoWiderWithAChordRowThanWithout()
    {
        double With = FirstBarWidth(Pickup);
        double Without = FirstBarWidth(Pickup.Replace("chords prog  staff m", "staff m"));
        Assert.Equal(Without, With, 2);
    }

    private static double FirstBarWidth(string source)
    {
        var tree = SyntaxTree.Parse(source);
        string svg = SvgGenerator.Generate(tree, new SvgRenderOptions { EmbedFont = false });
        var bars = Regex.Matches(svg,
                "<rect x=\"([0-9.-]+)\" y=\"([0-9.-]+)\" width=\"([0-9.-]+)\" height=\"([0-9.-]+)\"")
            .Select(m => (X: double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                          Y: double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                          W: double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture),
                          H: double.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture)))
            .Where(r => r.W < 0.5 && r.H > 3)
            .ToList();
        Assert.True(bars.Count > 0, "no barlines drawn");
        // The STAFF's barlines are the lowest band; the first of them ends the pickup bar.
        double staffY = bars.Max(b => b.Y);
        return bars.Where(b => System.Math.Abs(b.Y - staffY) < 0.1).Min(b => b.X);
    }
}
