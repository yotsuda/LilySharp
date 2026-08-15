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
/// input/regression/part-combine-tuplet-end.ly — "End tuplets events are sent to the starting
/// context, so even after a switch, a tuplet ends correctly" — and the horizontal spacing rule
/// the same book turned out to measure.
/// </summary>
/// <remarks>
/// Every expected number was read off LilyPond 2.26.0's own grob dump of this book
/// (<c>audit/lpreg/pctend.ly</c> with <c>pcdump.ily</c>, log <c>pctend.log</c>) and its
/// no-combiner control (<c>pctend-ctl.ly</c>), never off this renderer.
/// <para>
/// The switch the book is about: bar 1 is part one alone against part two's <c>R1</c> (Solo),
/// bar 2 has both parts on one <c>g1</c> (a2). The second tuplet ends exactly on that boundary.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class PartCombineTupletEndTests
{
    private const string Defaults = "octave absolute\ntime 4/4\n";

    private const string PartOne =
        "r2 tuplet 3/2 { g8[ g g] } tuplet 3/2 { g[ g g] } | g1 |";
    private const string PartTwo = "R1 | g1 |";

    private static string Book(string score) => Defaults + $$"""
        part vone { clef treble }
        part vtwo { clef treble }
        section A {
          vone { {{PartOne}} }
          vtwo { {{PartTwo}} }
        }
        form main { ~A }
        score main { {{score}} }
        """ + "\n";

    /// <summary>
    /// How far a GAP may sit from LilyPond's: the SVG prints coordinates to two decimals, so
    /// each end of a gap carries ±0.005 and their difference twice that. Positions are compared
    /// at two decimals directly; only differences need this.
    /// </summary>
    private const double GapTolerance = 0.011;

    private static string Svg(string source) => SvgGenerator.Generate(
        SyntaxTree.Parse(source),
        new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false });

    /// <summary>
    /// The x of every note column, left to right — music glyphs with a source position, minus
    /// the two leftmost (the clef and the metre, neither of which changes in this book).
    /// </summary>
    private static double[] ColumnXs(string svg) =>
        Regex.Matches(svg, "<text class=\"music\"[^>]*x=\"([-\\d.]+)\"[^>]*data-pos=")
            .Select(m => double.Parse(m.Groups[1].Value))
            .Distinct()
            .OrderBy(x => x)
            .Skip(2)
            .ToArray();

    /// <summary>The tuplet numbers, as (text, x, staff spaces above the centre line).</summary>
    private static List<(string Text, double X, double Y)> TupletNumbers(string svg) =>
        Regex.Matches(svg, "<text (?![^>]*class=\"music\")[^>]*x=\"([-\\d.]+)\" y=\"([-\\d.]+)\"[^>]*>([^<]+)</text>")
            .Select(m => (Text: m.Groups[3].Value,
                          X: double.Parse(m.Groups[1].Value),
                          Y: Math.Round(11.69 - double.Parse(m.Groups[2].Value), 3)))
            .Where(t => t.Text is "3")
            .OrderBy(t => t.X)
            .ToList();

    [Fact]
    public void BothTupletsCloseAndNeitherReachesTheBarWhereTheVoicesSwitch()
    {
        // The claim. LilyPond engraves TWO tuplets, each a beamed triplet — so the bracket is
        // suppressed (bracket-visibility = if-no-beam, and the dump shows both TupletBrackets
        // with an EMPTY x extent) and only the number is drawn, over the middle stem:
        //   "3" at x 17.830355, y +3.34      "3" at x 25.342955, y +3.34
        // and the two beams span 15.261155-20.399555 and 22.773755-27.912155.
        // If the tuplet's end event went to the context the combiner switches TO, rather than
        // to the one it started in, the second tuplet could not close on that boundary.
        var svg = Svg(Book("combinedStaff { vone vtwo }"));

        Assert.Equal(2, Regex.Matches(svg, "<polygon").Count);   // two beams, and only two

        var numbers = TupletNumbers(svg);
        Assert.Equal(2, numbers.Count);
        Assert.Equal(17.830, numbers[0].X, 2);
        Assert.Equal(25.343, numbers[1].X, 2);
        Assert.All(numbers, n => Assert.Equal(3.34, n.Y, 2));

        // …and the switch itself happened, or the book would be measuring nothing: bar 1 is
        // labelled Solo and bar 2 a2.
        var labels = Regex.Matches(svg, "<text (?![^>]*class=\"music\")[^>]*>([^<]+)</text>")
            .Select(m => m.Groups[1].Value)
            .Where(t => t is "a2" or "Solo" or "Solo II")
            .ToList();
        Assert.Equal(["Solo", "a2"], labels);
    }

    [Fact]
    public void TheStepFromTheSharedRestIntoTheSoloIsNotRefinedByTheRestsWidth()
    {
        // LilyPond puts the half rest in the SHARED voice (both parts are silent at moment 0)
        // and the tuplet in SOLO, and its Note_spacing wishes are chained per Voice CONTEXT —
        // so the shared voice's wish at the rest names the note it engraves NEXT (the g1 of
        // bar 2, x 31.403) and not the solo note that follows it. With no wish spanning the
        // pair, note-spacing.cc:77's left-head refinement never runs on it:
        //   LilyPond, combined  8.585 -> 14.086955   = 5.501955   (the bare duration ideal)
        //   LilyPond, no combiner                    = 5.801955   (= the same + 1.5 - 1.2,
        //                                              the half rest's right edge traded for
        //                                              spacing-increment)
        // MEASURED: audit/lpreg/pctend.log WISH records against pctend-ctl.log.
        var xs = ColumnXs(Svg(Book("combinedStaff { vone vtwo }")));

        Assert.Equal(5.501955, xs[1] - xs[0], GapTolerance);
    }

    [Fact]
    public void TheSameMusicWithoutTheCombinerKeepsTheRefinement()
    {
        // The positive control, and the reason the test above is about a VOICE boundary rather
        // than about rests: part one's music alone, on a plain staff, is one voice throughout,
        // so its wish does span the pair and the gap is the wider 5.801955 — LilyPond's own
        // number for exactly this control (pctend-ctl.ly). Every other column of the two
        // scores is a rigid translation of the other, which is what says this single gap is
        // the whole of the difference.
        var xs = ColumnXs(Svg(Book("staff vone")));

        Assert.Equal(5.801955, xs[1] - xs[0], GapTolerance);
    }

    [Fact]
    public void AVoiceChangeAcrossABarLineCostsNothing()
    {
        // The other half of the rule, and a nail against fixing the gap above by declaring
        // every context change a boundary. The solo run ends and the a2 begins in the SHARED
        // voice, so bar 1 -> bar 2 is a context change too — but a bar line is one column for
        // every voice (Note_spacing_engraver adds currentCommandColumn to the running wish at
        // every timestep), so the refinement does run there. LilyPond's last-note-to-g1
        // distance is 4.795186 in BOTH scores, combined and control.
        var combined = ColumnXs(Svg(Book("combinedStaff { vone vtwo }")));
        var plain = ColumnXs(Svg(Book("staff vone")));

        Assert.Equal(4.795186, combined[^1] - combined[^2], GapTolerance);
        Assert.Equal(plain[^1] - plain[^2], combined[^1] - combined[^2], GapTolerance);
    }

    [Fact]
    public void StepsInsideOneContextAreStillRefined()
    {
        // The negative control for the boundary rule: the six tuplet eighths are all in the
        // solo voice, so every step between them keeps the left-head refinement and measures
        // LilyPond's 2.504200 — including the step from the first tuplet to the second, which
        // crosses a tuplet edge but no context.
        var xs = ColumnXs(Svg(Book("combinedStaff { vone vtwo }")));

        foreach (var i in Enumerable.Range(2, 5))
            Assert.Equal(2.504200, xs[i] - xs[i - 1], GapTolerance);
    }
}
