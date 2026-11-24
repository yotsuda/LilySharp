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
using System.Linq;
using System.Text.RegularExpressions;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using LilySharp.Tests.LpFidelity;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A <c>|:</c> in the middle of a line is ONE bar line, the <c>.|:</c>: the plain bar line
/// ending the bar before it yields (the pen never drew it) and takes no room either, so the
/// <c>.|:</c> stands where a plain <c>|</c> would and only its own width widens the bar
/// (SpacingRules.EndBarYieldsToRepeatStart).
/// LILYPOND-REF: scm/bar-line.scm define-bar-line ".|:" — mid-line glyph ".|:", end-of-line piece "|".
/// </summary>
/// <remarks>
/// MEASURED, LilyPond 2.26.0 (Lab sessions/p1373/wish — probe.ps1 writes each book, its
/// <c>lysc ly</c> twin and the head x of both sides; raggedRight): the distance from the last
/// head of bar 1 to the first of bar 2. The <c>.|:</c>'s left edge is 28.893 after down-stem
/// e'' quarters, as a plain <c>|</c>'s. Until session 1373 the layout kept the yielded
/// <c>|</c>'s 0.19 before the <c>.|:</c>: 5.88 and 6.17 where LilyPond has 5.695 and 5.979.
/// </remarks>
[Trait("Category", "Unit")]
public class RepeatStartYieldTests
{
    private const string Bar = "e''4 e'' e'' e''";

    [Theory]
    // Down stems, then up stems after the bar line (the up stems earn no correction).
    [InlineData("rh { " + Bar + " |: " + Bar + " :| }", "part rh { }", "staff rh", 5.695)]
    [InlineData("rh { e'4 e' e' e' |: e'4 e' e' e' :| }", "part rh { }", "staff rh", 5.979)]
    // Two staves: the lower one sounding, then silent (its share of the correction gone).
    [InlineData("rh { " + Bar + " |: " + Bar + " :| }\n  lh { c1 |: c1 :| }", "part rh { }\npart lh { clef bass }", "staff rh staff lh", 5.695)]
    [InlineData("rh { " + Bar + " |: " + Bar + " :| }\n  lh { s1 |: s1 :| }", "part rh { }\npart lh { clef bass }", "staff rh staff lh", 5.600)]
    // The control: a plain bar line, unchanged.
    [InlineData("rh { " + Bar + " | " + Bar + " | }", "part rh { }", "staff rh", 4.045)]
    public void AMidLineRepeatStart_StandsWhereAPlainBarLineWould(string music, string parts, string score, double crossing)
    {
        string src = "time 4/4\noctave absolute\npaper { raggedRight }\n" + parts
            + "\nsection A {\n  " + music + "\n}\nform { A }\nscore { " + score + " }\n";
        var heads = Regex.Matches(TwinBowSweep.Dump(src), @"^HEAD \S+ 0 x=([-\d.]+)", RegexOptions.Multiline)
            .Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
            .OrderBy(x => x).ToList();
        Assert.Equal(8, heads.Count);
        // 0.006: the figures are LilyPond's to three places; Lily#'s rows read 5.690, 5.980,
        // 5.690, 5.600, 4.040.
        Assert.Equal(crossing, heads[4] - heads[3], 0.006);
    }

    /// <summary>
    /// At a line END the plain bar line does not yield: the end-of-line piece of <c>.|:</c> is
    /// that <c>|</c>, and the next line opens with the <c>.|:</c>. The ending line keeps its
    /// bar line's room — the last head stands as far from the line's right edge as before a
    /// plain line end.
    /// </summary>
    [Fact]
    public void AtALineEnd_ThePlainBarLineKeepsItsRoom()
    {
        (double Ending, int Systems) FirstBar(string second)
        {
            string src = "time 4/4\noctave absolute\npaper { raggedRight }\npart rh { }\nsection A {\n  rh { "
                + Bar + " break " + second + " }\n}\nform { A }\nscore { staff rh }\n";
            var tree = SyntaxTree.Parse(src);
            var layout = new LayoutEngine().Layout(SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree)));
            return (layout.Systems[0].Measures[^1].Width, layout.Systems.Length);
        }
        var plain = FirstBar("| " + Bar + " |");
        var repeat = FirstBar("|: " + Bar + " :|");
        Assert.Equal(2, plain.Systems);
        Assert.Equal(2, repeat.Systems);
        // The ending line's bar is the same bar either way, its | included; only what opens
        // the second line differs.
        Assert.Equal(plain.Ending, repeat.Ending, 6);
    }

    /// <summary>The yielded bar line's room, read off the layout: the bar before a mid-line
    /// <c>|:</c> is a plain bar line's width narrower than the same bar before a <c>|</c>.</summary>
    [Fact]
    public void MidLine_TheBarBeforeTheRepeatStart_IsAPlainBarLineNarrower()
    {
        double FirstBar(string rest)
        {
            string src = "time 4/4\noctave absolute\npaper { raggedRight }\npart rh { }\nsection A {\n  rh { "
                + Bar + " " + rest + " }\n}\nform { A }\nscore { staff rh }\n";
            var tree = SyntaxTree.Parse(src);
            var layout = new LayoutEngine().Layout(SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree)));
            Assert.Single(layout.Systems);
            return layout.Systems[0].Measures[0].Width;
        }
        Assert.Equal(EngravingDefaults.ThinBarlineThickness,
            FirstBar("| " + Bar + " |") - FirstBar("|: " + Bar + " :|"), 6);
    }

    /// <summary>
    /// The break gate prices the bar as the layout sets it: the yielded <c>|</c> is out of the
    /// bar's rigid width, and a candidate line ENDING before the <c>|:</c> pays it back (the
    /// end-of-line piece), through the next bar's line-end width.
    /// </summary>
    [Fact]
    public void TheBreakGate_PricesTheYieldedBarLine_AsTheLayoutDoes()
    {
        string src = "time 4/4\noctave absolute\npart rh { }\nsection A {\n  rh { "
            + Bar + " |: " + Bar + " :| " + Bar + " | }\n}\nform { A }\nscore { staff rh }\n";
        var tree = SyntaxTree.Parse(src);
        var score = new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!);
        var data = SystemBreaker.ComputeMultiStaffSpringData(score, null);
        double thin = EngravingDefaults.ThinBarlineThickness;

        // Bar 1: no start bar line, and its | yields to bar 2's |:.
        Assert.Equal(0.0, data[0].RigidWidth, 9);
        Assert.Equal(thin, data[1].LineEndCourtesyWidth, 9);
        // Bar 2: its own .|: and :|. — a :|. yields to nothing.
        Assert.Equal(SpacingRules.GetBarlineWidth(BarlineType.RepeatStart)
            + SpacingRules.GetBarlineWidth(BarlineType.RepeatEnd), data[1].RigidWidth, 9);
        Assert.Equal(0.0, data[2].LineEndCourtesyWidth, 9);
    }

    /// <summary>
    /// A trill stopping on the head of a bar binds to the bar line before it (to-barline), at
    /// that bar line's left edge. Before a mid-line <c>|:</c> that bar line is the <c>.|:</c>,
    /// drawn from the next bar's X — the yielded <c>|</c> is not there to end short of.
    /// MEASURED (2.26.0, Lab sessions/p1376/trill t0/t1): the left edge is 39.8667 under both a
    /// plain <c>|</c> and the <c>.|:</c>. Until session 1376 the line ended 0.19 short of the
    /// <c>.|:</c>.
    /// </summary>
    [Theory]
    [InlineData("|: e''1 :|", false)]
    [InlineData("| e''1 |", true)]
    [InlineData("break |: e''1 :|", true)]
    public void ATrillStoppingOnABarHead_EndsAtTheBarLineDrawnThere(string rest, bool plainBar)
    {
        string src = "time 4/4\noctave absolute\npaper { raggedRight }\npart rh { }\nsection A {\n  rh { "
            + "c''1@trillSpan | d''1@!trillSpan " + rest + " f''1 | }\n}\nform { A }\nscore { staff rh }\n";
        var tree = SyntaxTree.Parse(src);
        var layout = new LayoutEngine().Layout(SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree)));
        var trill = Assert.Single(layout.TrillSpannerLayouts);
        var bar2 = layout.Systems[0].Measures.Single(m => m.MeasureIndex == 1);
        double edge = bar2.X + bar2.Width - (plainBar ? EngravingDefaults.ThinBarlineThickness : 0.0);
        Assert.Equal(edge, trill.LineEndX, 6);
    }
}
