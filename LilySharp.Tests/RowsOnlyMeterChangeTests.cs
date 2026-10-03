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
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Tests.LpFidelity;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A rows-only sheet (chords or lyrics, no staff) engraves its section headers' meter
/// changes on the grid, where a staff would: after the bar line where the change falls,
/// hoisted into the prefix of a line it opens, and as the courtesy at the end of the line
/// before (session 783). Until then the grid drew the meter once, at the top of the first
/// line, and a header's <c>time 3/2</c> widened the bars and showed nothing.
/// </summary>
/// <remarks>
/// DIFFERENTIAL against the same book WITH a staff: the staff's own meter glyphs — the
/// ones LilyPond's Time_signature_engraver draws — are the oracle, and the grid must draw
/// the same glyphs in the same left-to-right order. The grid's placement is LILYSHARP-OWN
/// (LilyPond engraves no meter on a ChordNames line; HANDOFF §3, 2026-08-20), so the X is
/// pinned against the row's own bar lines and symbols, not against a LilyPond number.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class RowsOnlyMeterChangeTests
{
    private static string Book(string form, string render, string headerA = "", string headerB = "time 3/2") => $$"""
        octave absolute
        time 4/4
        key c major
        part melody { clef treble }
        section A {
          {{headerA}}
          melody { c'1 | d'1 | }
          chords prog { C | G | }
        }
        section B {
          {{headerB}}
          melody { e'1. | f'1. | }
          chords prog { Am F | G C | }
        }
        form main { {{form}} }
        score main { {{render}} }
        """;

    private static bool IsMeterGlyph(char g) =>
        g is EmmentalerGlyphs.TimeSigCommon or EmmentalerGlyphs.TimeSigCutCommon
        || Enumerable.Range(0, 10).Any(d => EmmentalerGlyphs.GetTimeSigDigit(d) == g);

    /// <summary>The meter glyphs drawn, left to right (a numerator over its denominator
    /// shares an X, so the glyph breaks the tie).</summary>
    private static List<char> MeterGlyphs(RenderedGeometry g) =>
        g.Glyphs.Where(x => IsMeterGlyph(x.Glyph))
            .OrderBy(x => x.X).ThenBy(x => x.Glyph).Select(x => x.Glyph).ToList();

    [Fact]
    public void AHeadersMeterChange_IsEngravedOnTheGrid_MidLine()
    {
        var staffless = RenderedGeometry.Render(Book("A B", "chords prog"));
        var staffful = RenderedGeometry.Render(Book("A B", "chords prog staff melody"));
        var glyphs = MeterGlyphs(staffless);
        Assert.Equal(MeterGlyphs(staffful), glyphs);
        Assert.Equal(3, glyphs.Count);   // the opening C, then the 3 over the 2

        // …after the bar line that opens bar 3 — between bar 2's symbol and bar 3's first,
        // on the symbols' own line (the grid row's band).
        var meter = staffless.Glyphs.Where(x => IsMeterGlyph(x.Glyph)).OrderBy(x => x.X).ToList();
        double changeX = meter[1].X;
        var symbols = staffless.ChordSymbols.OrderBy(c => c.X).ToList();
        Assert.True(symbols[1].X < changeX && changeX < symbols[2].X,
            $"the 3/2 at {changeX:F2} should stand between `G' ({symbols[1].X:F2}) and `Am' ({symbols[2].X:F2})");
    }

    /// <summary>
    /// A form `break` reaches a rows-only sheet (session 783): the bar before it ends a
    /// line, as it does with a staff. A rows-only sheet has no music walk, which is where a
    /// staff's break was written, so the form's breaks were ignored — and the hoisted and
    /// courtesy meters below had no line start to stand on.
    /// </summary>
    [Fact]
    public void AFormBreak_EndsTheLine_OnARowsOnlySheet()
    {
        static LilySharp.Core.Svg.Layout.ScoreLayout Lay(string source)
        {
            var tree = LilySharp.Core.Syntax.SyntaxTree.Parse(source);
            var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
            return new LilySharp.Core.Svg.Layout.LayoutEngine(score.Paper).Layout(score);
        }
        var staffless = Lay(Book("A break B", "chords prog"));
        var staffful = Lay(Book("A break B", "chords prog staff melody"));
        Assert.Equal(2, staffful.Systems.Length);
        Assert.Equal(2, staffless.Systems.Length);
        Assert.Equal(2, staffless.Systems[1].Measures[0].MeasureIndex);
        // …and `noBreak` keeps the four bars on one line, which they fit on.
        Assert.Single(Lay(Book("A noBreak B", "chords prog")).Systems);
    }

    /// <summary>
    /// A change that opens a LINE: line 1 ends with the courtesy 3/2 and line 2 opens with
    /// it in the prefix (the hoisted copy is drawn once) — the glyphs a staff draws there.
    /// </summary>
    [Fact]
    public void AMeterChangeAtALineStart_IsHoistedIntoThePrefix_AndCourtesied()
    {
        var g = RenderedGeometry.Render(Book("A break B", "chords prog"));
        var staffful = RenderedGeometry.Render(Book("A break B", "chords prog staff melody"));
        Assert.Equal(MeterGlyphs(staffful), MeterGlyphs(g));
        var meter = g.Glyphs.Where(x => IsMeterGlyph(x.Glyph)).ToList();
        // C; the courtesy 3/2 at line 1's end; the prefix 3/2 at line 2's start.
        Assert.Equal(5, meter.Count);
        var digits = meter.Where(x => x.Glyph != EmmentalerGlyphs.TimeSigCommon).ToList();
        // Line 2's pair (a numerator two spaces over its denominator) stands at the line
        // start; line 1's pair stands right of line 1's last bar line, a system higher.
        double line2Y = digits.Max(x => x.Y);
        var line2 = digits.Where(x => System.Math.Abs(x.Y - line2Y) < 3.0).ToList();
        var line1 = digits.Where(x => System.Math.Abs(x.Y - line2Y) >= 3.0).ToList();
        Assert.Equal(2, line2.Count);
        Assert.Equal(2, line1.Count);
        Assert.True(line1.Min(x => x.X) > line2.Max(x => x.X) + 20.0,
            "the courtesy pair stands at the END of line 1, the prefix pair at the START of line 2");
    }

    [Fact]
    public void AnOpeningSectionsHeader_SetsTheFirstLinesMeter_WithNoChange()
    {
        // A opens the piece with `time 3/4` on a 4/4 book: the first line shows 3/4, as the
        // staff does (the header states the opening meter), and no change is drawn at bar 0.
        var staffless = RenderedGeometry.Render(Book("A B", "chords prog", headerA: "time 3/4"));
        var staffful = RenderedGeometry.Render(Book("A B", "chords prog staff melody", headerA: "time 3/4"));
        var glyphs = MeterGlyphs(staffless);
        Assert.Equal(MeterGlyphs(staffful), glyphs);
        Assert.Equal(4, glyphs.Count);   // 3 over 4, then 3 over 2
        Assert.DoesNotContain(EmmentalerGlyphs.TimeSigCommon, glyphs);
    }

    [Fact]
    public void OneMeterThroughout_DrawsItOnce()
    {
        var staffless = RenderedGeometry.Render(Book("A B", "chords prog", headerB: ""));
        Assert.Equal(new List<char> { EmmentalerGlyphs.TimeSigCommon }, MeterGlyphs(staffless));
    }
}
