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
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using LilySharp.Tests.LpFidelity;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A staffless lead sheet sets its <c>form</c> section names ABOVE the row, in their own
/// band, as LilyPond sets a mark over a ChordNames line — and the row's symbols stand where
/// they would with no label at all.
/// </summary>
/// <remarks>
/// LILYPOND-REF: probes/mark-chord-row.ly books MKT (SectionLabel), MKS (RehearsalMark) and
/// MKV (taller symbols) all read the row's ink top plus <c>outside-staff-padding</c>
/// 0.460000 — one number for two grobs and two symbol heights.
/// <para>
/// ⚠️ THIS REVERSES A DECISION. From 2026-08-24 to session 784 the owner had the label set
/// ON the chord line, level with the symbols (a printed-chart convention), with the overlap
/// resolved in X: a window that moved the symbols clear of the box, a spring floor that
/// widened the first bar for it, and — once the grid engraved its meters on that line — a
/// box pushed past the meter (sessions 781, 783). Looking at that last shape the owner
/// reversed it for every staffless sheet (2026-10-04: "the section mark reads better in
/// the row above"), and the X half went with the convention (HANDOFF 5.3). The controls
/// below are the old file's, read the other way round.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class RowsOnlySectionLabelTests
{
    private static string RowsOnly(bool withChords) => $$"""
        octave absolute
        key c major

        part melody {
          section A { c'4 d' e' f' | g' a' b' c'' | }
          section B { c'4 d' e' f' | g' a' b' c'' | }
        }
        {{(withChords ? """
        chords harm {
          section A { C | G | }
          section B { C | G | }
        }
        """ : "")}}
        lyrics verse {
          section A { one two three four | five six sev- en | }
          section B { one two three four | five six sev- en | }
        }

        form { A B }

        score {{{(withChords ? "\n          chords harm as names" : "")}}
          lyrics verse sings melody
        }
        """;

    /// <summary>On a staffless chord sheet every label stands ABOVE the chord line, clear of
    /// the symbols by a real margin (device Y grows downward, so "above" is a smaller Y).</summary>
    [Fact]
    public void SectionLabel_OnAStafflessChordSheet_StandsAboveTheSymbols()
    {
        var g = RenderedGeometry.Render(RowsOnly(withChords: true));
        var labels = g.MusicMarkLabels;
        Assert.Equal(2, labels.Count);          // one per section, one per system
        foreach (var label in labels)
        {
            double nearestChord = g.ChordSymbols
                .Select(c => c.Y)
                .OrderBy(y => System.Math.Abs(y - label.Y))
                .First();
            Assert.True(nearestChord - label.Y > 1.0,
                $"'{label.Text}' at {label.Y:F6} must stand above the chord line at {nearestChord:F6}, "
                + "not level with the symbols (the 2026-08-24 convention, reversed 2026-10-04).");
        }
    }

    /// <summary>...and the symbols owe the label nothing: hidden (<c>~A</c>) or shown, every
    /// chord symbol and every bar line stands at the same X.</summary>
    [Fact]
    public void SectionLabel_OnAStafflessChordSheet_LeavesTheSymbolsWhereTheyStand()
    {
        var shown = RenderedGeometry.Render(RowsOnly(withChords: true));
        var hidden = RenderedGeometry.Render(RowsOnly(withChords: true).Replace("form { A B }", "form { ~A ~B }"));
        Assert.Empty(hidden.MusicMarkLabels);
        Assert.Equal(hidden.ChordSymbols.Select(c => (c.Text, System.Math.Round(c.X, 6))).ToList(),
                     shown.ChordSymbols.Select(c => (c.Text, System.Math.Round(c.X, 6))).ToList());
        Assert.Equal(hidden.Barlines.Select(b => System.Math.Round(b.X, 6)).ToList(),
                     shown.Barlines.Select(b => System.Math.Round(b.X, 6)).ToList());
    }

    /// <summary>A lyrics-only sheet keeps its label above the row too.</summary>
    [Fact]
    public void SectionLabel_OnAStafflessLyricsSheet_StaysAboveTheRow()
    {
        var g = RenderedGeometry.Render(RowsOnly(withChords: false));
        Assert.Empty(g.ChordSymbols);
        var label = g.MusicMarkLabels[0];
        double firstSyllable = g.LyricSyllables.Where(s => s.Y > label.Y).Min(s => s.Y);
        Assert.True(firstSyllable - label.Y > 1.0,
            $"the label sits {firstSyllable - label.Y:F6} above the first syllable it "
            + "overlaps; it must keep its own band.");
    }

    /// <summary>CONTROL — the same book WITH a staff: the label keeps its band above the
    /// staff, which the owner confirmed on 2026-08-24 and which the reversal leaves alone.</summary>
    [Fact]
    public void SectionLabel_WithAStaff_KeepsItsBandAboveTheChordRow()
    {
        const string withStaff = """
            octave absolute
            key c major

            part melody {
              section A { c'4 d' e' f' | g' a' b' c'' | }
              section B { c'4 d' e' f' | g' a' b' c'' | }
            }
            chords harm {
              section A { C | G | }
              section B { C | G | }
            }

            form { A B }

            score {
              chords harm
              staff melody
            }
            """;
        var g = RenderedGeometry.Render(withStaff);
        var label = g.MusicMarkLabels[0];
        double nearestChord = g.ChordSymbols
            .Select(c => c.Y)
            .OrderBy(y => System.Math.Abs(y - label.Y))
            .First();
        Assert.True(nearestChord - label.Y > 1.0,
            $"with a staff present the label must stay {nearestChord - label.Y:F6} > 1.0 above "
            + "the chord line.");
    }

    // ----- the line-start X of a staffless label: the same anchors a staff's takes -----

    private static string Chart(string top, string form) => top + $$"""
        tempo 117
        time 4/4
        chords prog { section Intro { C | G | Am | F | } }
        form { {{form}} }
        score { chords prog }
        """ + "\n";

    private static (MultiStaffScore Score, ScoreLayout Layout) Lay(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join(" | ", tree.Diagnostics.Select(d => d.Message)));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        return (score, new LayoutEngine(score.Paper).Layout(score));
    }

    private static double BoxLeft(MultiStaffScore score, ScoreLayout layout)
    {
        var box = layout.MusicMarkLayouts.Single(m => m.MarkType == MusicMarkType.SectionLabel);
        return box.X - MusicMarkEngraver.LabelBoxHalfWidth(score.TextMetrics, box.MarkType, box.Text, box.Boxed);
    }

    /// <summary>At a line start the box keeps the line-start edge — over the grid's meter,
    /// which stands on the row below it now — under both arrangements; a line opening on a
    /// drawn <c>|:</c> puts the box on that bar, as on a staff.</summary>
    [Theory]
    [InlineData("", "Intro", false)]
    [InlineData("layout { markTempo beside }\n", "Intro", false)]
    [InlineData("", "|: Intro :|", true)]
    [InlineData("layout { markTempo beside }\n", "|: Intro :|", true)]
    public void TheBox_TakesAStaffsLineStartAnchor(string top, string form, bool openingRepeat)
    {
        var (score, layout) = Lay(Chart(top, form));
        double left = BoxLeft(score, layout);
        double indent = layout.Systems[0].Indent;
        if (openingRepeat)
            Assert.True(left > indent + 0.3 + 1.0, $"the box's left edge ({left:F2}) should stand on the drawn `|:`, past the edge");
        else
            Assert.Equal(indent + 0.3, left, 6);
    }

    /// <summary>...and a label hidden or shown leaves the first bar's width alone: no spring
    /// floor is paid for a box that stands above the row.</summary>
    [Fact]
    public void TheBox_WidensNoBar()
    {
        var (_, shown) = Lay(Chart("", "Intro"));
        var (_, hidden) = Lay(Chart("", "~Intro"));
        Assert.Equal(hidden.Systems[0].Measures[0].Width, shown.Systems[0].Measures[0].Width, 6);
        Assert.Equal(hidden.Systems[0].Measures[1].Width, shown.Systems[0].Measures[1].Width, 6);
    }
}
