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

        // With a diagram the cell grows by the diagram's ink depth below the names' baseline
        // (ChordNameEngraver.GridCellHeight): the diagram's feet then keep the same room over
        // the bars' feet that a bare name's baseline keeps — 4.0 − 2.9 (GridChordBaseline).
        var bar = g.Barlines[0];
        double chord = ChordBaseline(g);
        double feet = DiagramFeet(g);
        double depth = feet - chord;
        Assert.True(depth > 4.0, $"the diagram's feet should hang well under the names; depth {depth:F3}");
        Assert.InRange(bar.Height, 4.0 + depth - 0.1, 4.0 + depth + 0.1);
        // Every bar line of the line is the same cell.
        foreach (var b in g.Barlines)
            Assert.Equal(bar.Height, b.Height, 6);
        // The name stands where it stood: 2.9 under the cell's top on both sheets.
        double plainChord = ChordBaseline(control);
        Assert.Equal(plainChord - plainBar.Y, chord - bar.Y, 6);
        // ...and the diagram's feet are inside the cell, with the bare name's own room under them.
        var (_, bottom) = Span(bar);
        Assert.InRange(bottom - feet, 1.1 - 0.1, 1.1 + 0.1);
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
