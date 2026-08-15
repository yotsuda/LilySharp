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
/// input/regression/part-combine-relative.ly — "The pitches in <c>\partCombine</c> are unaffected
/// by an outer <c>\relative</c> ... The expected output of this test is three identical measures."
/// </summary>
/// <remarks>
/// Expected numbers are LilyPond 2.26.0's own grob dump of the book (audit/lpreg/pcrel.ly,
/// log pcrel.log) and of the two-measure control that gives the twin a matching frame
/// (pcrel-ctl.ly): all three measures are E4/F4 over C4/D4, staff-position -4/-6 then -3/-5,
/// with columns at 8.585 / 12.860 and the next measure 9.891 further on.
/// <para>
/// ⚠️ THE BOOK'S MIDDLE MEASURE IS OUT OF FRAME. In LilyPond <c>\relative</c> WRAPS music, so it
/// has an inside and an outside and the book's claim is that the combiner ignores the outside.
/// Lily#'s <c>octave</c> is a switch in the stream — a part is relative unless something
/// switches it — so no outer scope exists for anything to ignore. The half that can be written
/// is the half a reader of the page sees: the absolute spelling and the relative spelling must
/// print the same measure.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class PartCombineRelativeTests
{
    private static string Book(string one, string two) => $$"""
        octave absolute
        time 4/4
        part vone { clef treble }
        part vtwo { clef treble }
        section A {
          vone { {{one}} }
          vtwo { {{two}} }
        }
        form main { ~A }
        score main { combinedStaff { vone vtwo } }
        """ + "\n";

    private static string Svg(string source) => SvgGenerator.Generate(
        SyntaxTree.Parse(source),
        new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false });

    /// <summary>Note columns as (x, staff spaces above the centre line), clef and metre dropped.</summary>
    private static List<(double X, double[] Ys)> Columns(string svg) =>
        Regex.Matches(svg, "<text class=\"music\"[^>]*x=\"([-\\d.]+)\" y=\"([-\\d.]+)\"[^>]*data-pos=")
            .Select(m => (X: double.Parse(m.Groups[1].Value),
                          Y: Math.Round(11.69 - double.Parse(m.Groups[2].Value), 3)))
            .OrderBy(g => g.X)
            .Skip(2)
            .GroupBy(g => g.X)
            .OrderBy(g => g.Key)
            .Select(g => (X: g.Key, Ys: g.Select(h => h.Y).OrderBy(y => y).ToArray()))
            .ToList();

    [Fact]
    public void TheAbsoluteAndTheRelativeSpellingPrintTheSameMeasure()
    {
        // The claim, in the half of it Lily# has a frame for. LilyPond's staff positions -6/-4
        // and -5/-3 are staff spaces -3.0/-2.0 and -2.5/-1.5 above the centre line, and its two
        // measures start 9.891 apart.
        var columns = Columns(Svg(Book("e2 f | octave relative e2 f |",
                                       "c2 d | octave relative c2 d |")));

        Assert.Equal(4, columns.Count);
        Assert.Equal([-3.0, -2.0], columns[0].Ys);
        Assert.Equal([-2.5, -1.5], columns[1].Ys);

        // …and the second measure is the first one over again, which is what "three identical
        // measures" means for the two of them that can be written.
        Assert.Equal(columns[0].Ys, columns[2].Ys);
        Assert.Equal(columns[1].Ys, columns[3].Ys);
        Assert.Equal(8.585, columns[0].X, 0.006);
        Assert.Equal(12.860, columns[1].X, 0.006);
        Assert.Equal(18.476, columns[2].X, 0.006);
        Assert.Equal(22.751, columns[3].X, 0.006);
    }

    [Fact]
    public void TheOctaveSwitchIsNotANoOp()
    {
        // The positive control, and the test above cannot do without it: `e f` reads the same in
        // both modes — that is WHY the book's measures are identical — so an `octave relative`
        // that did nothing at all would pass it.
        // The discriminating step is a fifth: after D4, relative `a` is A3 (the fourth down is
        // nearer than the fifth up) at -4.0 ss, where absolute `a` is A4 at -0.5 ss.
        // ⚠️ Not every step discriminates, and two spellings were tried before this one: the
        // modes agree for every step of a fourth or less. Probe: pcrel-ctl-probe.lys.
        var columns = Columns(Svg(Book("e2 f | octave relative c2 c |",
                                       "c2 d | octave relative a2 a |")));

        Assert.Equal(-4.0, columns[2].Ys[0]);
        Assert.Equal(-4.0, columns[3].Ys[0]);
    }

    [Fact]
    public void TheTwoPartsAreKeptApartSoNoLabelIsPrinted()
    {
        // The frame check the book depends on: E4/F4 against C4/D4 is never a unison or a chord
        // the combiner merges, so both parts print and neither an "a2" nor a "Solo" appears.
        // LilyPond's dump of the book carries no CombineTextScript record at all. If this ever
        // starts printing a label, the measures above stopped being what the book is about.
        var svg = Svg(Book("e2 f | octave relative e2 f |",
                           "c2 d | octave relative c2 d |"));

        var labels = Regex.Matches(svg, "<text (?![^>]*class=\"music\")[^>]*>([^<]+)</text>")
            .Select(m => m.Groups[1].Value)
            .Where(t => t is "a2" or "Solo" or "Solo II")
            .ToList();

        Assert.Empty(labels);
    }
}
