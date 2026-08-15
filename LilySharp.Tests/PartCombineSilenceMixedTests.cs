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
/// input/regression/part-combine-silence-mixed.ly — "Different kinds of silence are not merged
/// into the shared voice even if they begin and end simultaneously; however, when rests and
/// skips are present in the same part, the skips are ignored."
/// </summary>
/// <remarks>
/// Every number here was read off LilyPond 2.26.0's own grob dump of the book
/// (<c>audit/lpreg/pcsm.ly</c> with <c>pcdump.ily</c>, log <c>audit/lpreg/pcsm.log</c>),
/// never off this renderer.
/// <para>
/// ⚠️ POSITIONS ARE COMPARED AS DRAWN INK, not as LilyPond's <c>y=</c>. A multi-measure rest
/// and an ordinary semibreve rest in the same voice report positions 1.0 staff space apart and
/// DRAW IN THE SAME PLACE: lily/multi-measure-rest.cc:254-264 Multi_measure_rest::print takes
/// the ordinary rest's position and subtracts 2, then :284-292 picks the hanging glyph variant,
/// and the two cancel. Measured on 2.26.0 (audit/lpreg/mmr1.log): neutral R1 y=0.0 with ink
/// +0.375..+1.0, neutral r1 y=+1.0 with the same ink. Reading y= alone reports a defect that is
/// not there — this file's first draft did.
/// </para>
/// <para>
/// ⚠️ THE BOOK IS NOT CLOSED. Bar 2 (a skip against a multi-measure rest) is apart-silence in
/// LilyPond and the multi-measure rest is engraved in voice two, ink -2.625..-2.000; Lily#
/// draws it at +1.0 because a multi-measure rest is engraved once per STAFF here and takes no
/// voice direction (MultiMeasureRestEngraver.Calculate). Bar 1's dropped <c>R1^"R"</c> keeps
/// its label, because a label is a DynamicItem keyed by (measure, item) outside the stream the
/// combiner rewrites. Both are measured in HANDOFF §1; neither is asserted here, because a test
/// that nails a wrong number in place is worse than no test.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class PartCombineSilenceMixedTests
{
    private static string Combined(string partOne, string partTwo) => $$"""
        octave absolute
        time 4/4
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
    /// Every music glyph except the clef and the metre, as (x, staff spaces above the centre
    /// line), lowest x first.
    /// </summary>
    /// <remarks>
    /// ⚠️ <c>data-pos</c> is NOT required here, unlike <c>PartCombineSilenceTests.Glyphs</c>.
    /// A multi-measure rest has no source token of its own and so carries none — filtering on
    /// the attribute reports a bar that draws one as an empty bar, which is the failure this
    /// book would show last. The price is that augmentation dots would count too; this book
    /// has no dotted anything.
    /// </remarks>
    private static List<(double X, double Y)> Glyphs(string svg) =>
        Regex.Matches(svg, "<text class=\"music\"[^>]*x=\"([-\\d.]+)\" y=\"([-\\d.]+)\"")
            .Select(m => (X: double.Parse(m.Groups[1].Value),
                          Y: Math.Round(11.69 - double.Parse(m.Groups[2].Value), 3)))
            .OrderBy(g => g.X)
            .Skip(2)
            .ToList();

    [Fact]
    public void SkipsBesideARestAreIgnored()
    {
        // Bars 4 and 5 of the book. Each part writes THREE silences at once and the two parts
        // write them in the OPPOSITE ORDER — that mirroring is the book's own point, because
        // LilyPond's answer comes from silence-events (scm/part-combiner.scm:76-86), which
        // filters the rests out of the moment first and only looks at skips when there are
        // none. So both bars are one multi-measure rest against one multi-measure rest, and
        // one ordinary rest against one ordinary rest: unisilence, ONE silence printed.
        // LilyPond (pcsm.log): bar 4 one mmrest, ink +0.375..+1.000; bar 5 one rest at +1.0.
        var glyphs = Glyphs(Svg(Combined(
            "voice { R1 } { s1 } { s4 } | voice { r1 } { s2 } { s4 } |",
            "voice { s4 } { s1 } { R1 } | voice { s4 } { s2 } { r1 } |")));

        Assert.Equal(2, glyphs.Count);
        Assert.Equal(1.0, glyphs[0].Y);
        Assert.Equal(1.0, glyphs[1].Y);
    }

    [Fact]
    public void TheOrderTheSilencesAreWrittenInDoesNotChangeTheAnswer()
    {
        // The control, and the thing that failed before the rule was ported: with the two
        // parts' branches in the SAME order Lily# already printed one silence per bar, because
        // the combiner happened to be handed the rest. Mirroring them is what exposed that it
        // was reading the first branch rather than the moment. LilyPond cannot tell the two
        // apart; neither may this.
        var mirrored = Glyphs(Svg(Combined(
            "voice { R1 } { s1 } { s4 } | voice { r1 } { s2 } { s4 } |",
            "voice { s4 } { s1 } { R1 } | voice { s4 } { s2 } { r1 } |")));
        var aligned = Glyphs(Svg(Combined(
            "voice { R1 } { s1 } { s4 } | voice { r1 } { s2 } { s4 } |",
            "voice { R1 } { s1 } { s4 } | voice { r1 } { s2 } { s4 } |")));

        Assert.Equal(aligned.Count, mirrored.Count);
        for (int i = 0; i < aligned.Count; i++)
            Assert.Equal(aligned[i].Y, mirrored[i].Y);
    }

    [Fact]
    public void DifferentKindsOfSilenceAreNotMerged()
    {
        // The first half of the texidoc, and the positive control for the second: the rule is
        // "ignore the skips", NOT "merge whatever is silent". Bar 3 of the book is an ordinary
        // rest in part one against a skip in part two, which LilyPond keeps APART — the rest
        // is engraved in voice one at +2.0 (pcsm.log, REST dir=1), not in the shared voice at
        // +1.0 where a merged one would sit. If the port ever answered unisilence here, this
        // is the assertion that would fail while the two above stayed green.
        var glyphs = Glyphs(Svg(Combined("r1 |", "s1 |")));

        Assert.Single(glyphs);
        Assert.Equal(2.0, glyphs[0].Y);
    }

    [Fact]
    public void TheIgnoredSkipIsStillThere()
    {
        // ⚠️ NOT a claim of LilyPond's, a claim about HOW this is ported. LilyPond ignores the
        // skips by looking past them; a Lily# voice holds one item per moment, so the chosen
        // rest is SWAPPED into the branch the combiner reads and the skip takes its place in
        // the branch the rest came from. Nothing is deleted and no branch changes its length —
        // which is why a part that writes a note against its own rest still reaches the staff
        // whole. Two parts each writing r1 against a skip must therefore still print exactly
        // one rest and no stray second one, wherever the r1 was written.
        var glyphs = Glyphs(Svg(Combined("voice { s1 } { r1 } |", "voice { r1 } { s1 } |")));

        Assert.Single(glyphs);
        Assert.Equal(1.0, glyphs[0].Y);
    }
}
