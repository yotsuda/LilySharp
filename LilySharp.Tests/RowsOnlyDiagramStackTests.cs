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
using LilySharp.Core.Rendering;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using LilySharp.Tests.LpFidelity;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A rows-only sheet (chords and lyrics, or chords alone — no staff) stacks its rows the way
/// LilyPond stacks a staffless system: the first row is made SPACEABLE
/// (lily/page-layout-problem.cc:728-730, <c>MeasureCollector</c>) and every row under it is a
/// loose line spaced by its own skyline and specs. Owner's report 2026-10-31
/// (Downloads/lyrics.lys): a <c>Dm7(xx0211)</c> on a chords+lyrics sheet drew its diagram
/// through the words, because the two rows were stacked by a flat band gap that never read
/// the chord row's ink; and on the chords-only grid the diagram hung out of its cell. The
/// owner's decisions: no floor — the LilyPond chain (①) — and the cell encloses the diagram (②A).
/// </summary>
/// <remarks>
/// MEASURED (audit/lp-geometry/probes/staffless-fretboards.ly, 2.26.0): book SFL1 (ChordNames
/// over Lyrics) reads 5.500000 refpoint to refpoint — the Lyrics context's
/// nonstaff-relatedstaff-spacing basic-distance, the step a Lyrics line takes under a spaceable
/// line; SFL2 (a FretBoards line between) puts the lyric ink 0.500000 under the diagram's ink.
/// Lily# draws the grid's bar lines on the lyric row, and those are ink too
/// (<c>MultiStaffLayouter.LyricGridBand</c>): the 0.5 is kept off the bar TOPS.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class RowsOnlyDiagramStackTests
{
    private static string Book(string chords, string render, string words = "あいうえお | ほえほえ | あー |") => $$"""
        tempo 100
        time 4/4
        key c major
        chords prog { section A { {{chords}} } }
        lyrics verse { section A { {{words}} } }
        form { A }
        score { {{render}} }
        """;

    private const string Plain = "Bm7-5 | Dm7 Dm7 | Dm Fmaj7";
    private const string WithDiagram = "Bm7-5 | Dm7(xx0211) Dm7 | Dm Fmaj7";

    /// <summary>The chord line's baseline: its full-size runs (a raised 7 or ø is drawn
    /// smaller, above it).</summary>
    private static double ChordBaseline(RenderedGeometry g)
    {
        double em = g.ChordSymbolRuns.Max(t => t.FontSize);
        return g.ChordSymbolRuns.Where(t => Math.Abs(t.FontSize - em) < 1e-9).Max(t => t.Y);
    }

    /// <summary>
    /// The diagram's DEEPEST ink on the page (page Y, growing downward): a written shape draws
    /// no finger digits, so its feet are the strings' overhang past the last fret — the lines
    /// drawn under the X/O marks' columns (<c>SharedRenderer.DrawFretFrame</c>).
    /// </summary>
    private static double DiagramFeet(RenderedGeometry g)
    {
        var marks = g.Texts.Where(t => t.Role == TextRole.FretFrame).ToList();
        Assert.NotEmpty(marks);
        double left = marks.Min(t => t.X) - 1.0, right = marks.Max(t => t.X) + 2.0;
        var lines = g.Lines.Where(l => l.X1 >= left && l.X2 <= right).ToList();
        Assert.NotEmpty(lines);
        return lines.Max(l => Math.Max(l.Y1, l.Y2));
    }

    /// <summary>The grid's bar lines are drawn as rects whose Y is the TOP in page Y.</summary>
    private static (double Top, double Bottom) Span(DrawnRect bar) => (bar.Y, bar.Y + bar.Height);

    [Fact]
    public void ChordsOverLyrics_WithoutADiagram_TheLyricLineIsTheLyricsSpecsIdealUnderTheChordLine()
    {
        var g = RenderedGeometry.Render(Book(Plain, "chords prog  lyrics verse"));
        double chord = ChordBaseline(g);
        double lyric = g.LyricSyllables.Min(t => t.Y);
        // SFL1: 5.500000 refpoint to refpoint (the names' ink bottom + 0.5 + the bar tops at
        // 2.6 over the syllables is 3.1, under the ideal — so the ideal places the row).
        Assert.Equal(5.5, Math.Abs(lyric - chord), 3);
    }

    [Fact]
    public void ChordsOverLyrics_WithADiagram_TheGridsBarsStandHalfASpaceUnderTheDiagram()
    {
        var g = RenderedGeometry.Render(Book(WithDiagram, "chords prog  lyrics verse"));
        var bars = g.Barlines;
        Assert.NotEmpty(bars);
        double syllable = g.LyricSyllables.Min(t => t.Y);
        double feet = DiagramFeet(g);

        // The bar lines are the LYRIC row's: the syllables stand inside their span, and the
        // diagram's feet above it — the words and the grid are clear of the diagram.
        var (top, bottom) = Span(bars[0]);
        Assert.True(top < syllable && syllable < bottom,
            $"the syllable at {syllable:F3} is not inside the bar line's span [{top:F3}, {bottom:F3}]");
        Assert.True(feet < top, $"the diagram's feet at {feet:F3} reach into the bars' span from {top:F3}");

        // ...and the bar tops stand 0.5 — the Lyrics line's nonstaff-relatedstaff padding —
        // under the diagram's feet (SFL2's 0.500000, measured to the bar TOPS here because Lily#
        // draws them and LilyPond does not). The strings are drawn to their centre line; the
        // reserved box ends half a string thickness further.
        Assert.InRange(top - feet, 0.45, 0.6);
    }

    [Fact]
    public void ChordsOnlyGrid_WithADiagram_TheCellEnclosesIt()
    {
        var g = RenderedGeometry.Render(Book(WithDiagram, "chords prog"));
        var control = RenderedGeometry.Render(Book(Plain, "chords prog"));

        // The control's cell is the nominal staff: four spaces tall, the names centred in it.
        var plainBar = control.Barlines[0];
        Assert.Equal(4.0, plainBar.Height, 6);

        // With a diagram the cell grows to enclose it (ChordNameEngraver.GridCellHeight): the
        // cell's top stays the band top with the names 2.9 under it, and the room under the
        // deepest diagram's feet is the room over the tallest name's ink (owner's request
        // 2026-11-01: the same margin above and below).
        var bar = g.Barlines[0];
        double chord = ChordBaseline(g);
        double feet = DiagramFeet(g);
        double depth = feet - chord;
        Assert.True(depth > 4.0, $"the diagram's feet should hang well under the names; depth {depth:F3}");
        // Every bar line of the line is the same cell.
        foreach (var b in g.Barlines)
            Assert.Equal(bar.Height, b.Height, 6);
        // The name stands where it stood: 2.9 under the cell's top on both sheets.
        double plainChord = ChordBaseline(control);
        Assert.Equal(plainChord - plainBar.Y, chord - bar.Y, 6);
        // ...and the margins match: the line's tallest ink (a raised 7 here) to the bar top,
        // the diagram's feet to the bar bottom. The strings are drawn to their centre line and
        // the reserved box ends half a string thickness further, hence the tolerance.
        var tree = SyntaxTree.Parse(Book(WithDiagram, "chords prog"));
        var score = new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!);
        double inkTop = score.ChordNames.Max(c => ChordNameEngraver.ItemInkTop(score.TextMetrics, c));
        var (top, bottom) = Span(bar);
        double roomAbove = (chord - inkTop) - top;
        double roomBelow = bottom - feet;
        Assert.InRange(roomBelow - roomAbove, -0.06, 0.06);

        // The meter stands centred in the cell on both sheets — the same offset from the
        // cell's middle with the deepened cell as with the plain one.
        double meterOffset = g.Glyphs.Min(gl => gl.X) is var mx
            ? g.Glyphs.First(gl => gl.X == mx).Y - (top + bottom) / 2 : 0;
        var (pt, pb) = Span(plainBar);
        double plainMx = control.Glyphs.Min(gl => gl.X);
        double plainOffset = control.Glyphs.First(gl => gl.X == plainMx).Y - (pt + pb) / 2;
        Assert.Equal(plainOffset, meterOffset, 6);
    }

    /// <summary>The words of a lyrics grid stand centred between the bar ends: the same room
    /// over the row's tallest ink as under its deepest (owner's report 2026-11-01 — kana sat
    /// high in their cells). The band's two pieces are split by the row's own ink
    /// (<c>MultiStaffLayouter.LyricGridBand</c>, <c>LyricEngraver.RowInkExtent</c>).</summary>
    [Theory]
    [InlineData("あいうえお | ほえほえ | あー |")]                      // kana: 0.88 em up, 0.12 down
    [InlineData("one two three four | five six sev- en | nine ten |")]  // Latin, descenders
    [InlineData("one two | one two | one two |")]                       // Latin, none
    public void ChordsOverLyrics_TheWordsStandCentredBetweenTheBarEnds(string words)
    {
        string book = Book(Plain, "chords prog  lyrics verse", words);
        var tree = SyntaxTree.Parse(book);
        var score = new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!);
        int row = score.GridBarlineRowIndex;
        var (top, depth) = LyricEngraver.RowInkExtent(score.TextMetrics, score.Lyrics, row);
        Assert.True(top > 0, "the row's ink top should be measured");

        var g = RenderedGeometry.Render(book);
        var bar = g.Barlines[0];
        double baseline = g.LyricSyllables.Min(t => t.Y);
        var (barTop, barBottom) = Span(bar);
        // Page Y grows downward: the ink spans [baseline − top, baseline + depth].
        double roomAbove = (baseline - top) - barTop;
        double roomBelow = barBottom - (baseline + depth);
        Assert.Equal(roomAbove, roomBelow, 6);
        Assert.Equal(4.0, bar.Height, 6);
        Assert.True(roomAbove > 0.5, $"the ink should keep air inside the cell; {roomAbove:F3}");
    }

    /// <summary>A track placed twice on a chords-only sheet (<c>chords prog</c> over
    /// <c>chords prog as roman</c>): every row is a cell with its own bar lines, the meter
    /// stands on the first alone, and the diagram draws once, on the LAST row — name, name,
    /// diagram (owner's requests 2026-11-01); the same track on the same tuning is the same
    /// diagram.</summary>
    [Fact]
    public void ChordsOnlyGrid_ATrackPlacedTwice_EveryRowIsACell_AndTheDiagramDrawsOnceOnTheLastRow()
    {
        var g = RenderedGeometry.Render(Book(WithDiagram, "chords prog  chords prog as roman"));
        var cells = g.Barlines.GroupBy(b => Math.Round(b.Y, 3)).OrderBy(c => c.Key).ToList();
        Assert.Equal(2, cells.Count);
        var first = cells[0].First();
        var second = cells[1].First();
        // The first cell is the bare four-space cell; the second encloses the diagram.
        Assert.Equal(4.0, first.Height, 6);
        Assert.True(second.Height > 4.0 + 4.0, $"the second cell ({second.Height:F3}) should enclose the diagram");
        Assert.True(second.Y >= first.Y + first.Height - 1e-6,
            $"the second cell starts at {second.Y:F3}, inside the first's [{first.Y:F3}, {first.Y + first.Height:F3}]");
        // One diagram, under the roman row: its x x o marks once, below the second row's names.
        var marks = g.Texts.Where(t => t.Role == TextRole.FretFrame).ToList();
        Assert.Equal(3, marks.Count);
        double roman = g.ChordSymbolRuns.Max(t => t.Y);
        Assert.True(marks.All(m => m.Y > roman), "the diagram should hang under the roman row's names");
        // One meter (the staff-size glyph; the header tempo's note is the smaller one).
        Assert.Single(g.Glyphs.Where(gl => Math.Abs(gl.FontSize - 4.0) < 1e-9));
        // ...and the second row's names stand inside its own cell.
        Assert.True(second.Y < roman && roman < second.Y + second.Height,
            $"the roman row's baseline {roman:F3} is outside its cell [{second.Y:F3}, {second.Y + second.Height:F3}]");
    }

    [Fact]
    public void ChordsOnlyGrid_ADeepenedCellOnOneLine_IsClearedByTheNextLine()
    {
        // Enough bars to break: line 1 carries the diagram, line 2 is a plain cell.
        string chords = string.Join(" | ", Enumerable.Repeat("Dm7(xx0211) G7", 6)) + " | "
                        + string.Join(" | ", Enumerable.Repeat("C G", 8));
        string words = string.Join(" | ", Enumerable.Repeat("あ い", 14)) + " |";
        var g = RenderedGeometry.Render(Book(chords, "chords prog", words));
        var byLine = g.Barlines.GroupBy(b => Math.Round(b.Y, 3)).OrderBy(grp => grp.Key).ToList();
        Assert.True(byLine.Count >= 2, $"expected at least two lines of bars, found {byLine.Count}");
        var first = byLine[0].First();
        var second = byLine[1].First();
        Assert.True(first.Height > second.Height,
            $"the diagram line's cell ({first.Height:F3}) should be deeper than the plain one's ({second.Height:F3})");
        // The cells do not overlap: the next line's bars start past the deepened cell's feet.
        Assert.True(second.Y >= first.Y + first.Height - 1e-6,
            $"line 2's cell starts at {second.Y:F3}, inside line 1's [{first.Y:F3}, {first.Y + first.Height:F3}]");
    }
}
