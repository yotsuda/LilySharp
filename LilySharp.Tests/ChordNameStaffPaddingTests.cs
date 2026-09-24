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
using LilySharp.Core.Rendering;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Layout;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A chord line over the system's top staff sits where LilyPond's minimum translation puts
/// the ChordNames context against the staff under it: the line's deepest ink bottom clears
/// the staff's skyline by nonstaff-relatedstaff-spacing's padding 0.5. Over a bare staff that
/// skyline is the top line's ink edge (half a line thickness above its centre), so the
/// baseline stands 0.5 + 0.05 − (the deepest symbol's ink bottom) above the centre. Until
/// session 567 it stood a flat 0.6 there, fitted to one measurement.
/// </summary>
/// <remarks>
/// LILYPOND-REF: ly/engraver-init.ly:703-723 ChordNames — nonstaff-relatedstaff-spacing padding 0.5
/// LILYPOND-REF: lily/align-interface.cc:228-238 internal_get_minimum_translations — skyline distance plus padding
/// MEASURED on 2.26.0 (Lab sessions/p567/chord-bare-lp.log): C Am | F G7 over c d e f | c d e f
/// — the baseline 0.537 above the top line's ink = 0.5 + the G7's ink bottom 0.037; with an
/// a'' under the G7 the baseline sits 0.537 above that head's top.
/// Poison: the old flat 0.6 reddens the bare-staff fact (0.6 ≠ 0.587).
/// </remarks>
[Trait("Category", "Unit")]
public sealed class ChordNameStaffPaddingTests
{
    private const double Tolerance = 0.006;

    // Note-attached @chord symbols: the ATTACHED chord line, placed by the engraver's own
    // floor (a `chords` track above the staff is an independent ROW, placed by the loose-line
    // walk — LilyPond's alignment — which already reads the same padding and ink).
    private static string Render(string melody) => LiveRender.SvgFromRenderSpec($$"""
        key c major
        part m { clef treble }
        section Main {
          m { time 4/4 {{melody}} }
        }
        form main { Main }
        score main { staff m }
        """);

    private const string BareLine = "c4@chord(C) d e@chord(Am) f | c@chord(F) d e@chord(G7) f |";

    private static double D(string s) => double.Parse(s, CultureInfo.InvariantCulture);

    /// <summary>The top staff line's device Y: the topmost full-width rule.</summary>
    private static double TopLineY(string svg) => Regex.Matches(svg,
            "<line x1=\"0\\.05\" y1=\"([-\\d.]+)\" x2=\"[-\\d.]+\" y2=\"\\1\" stroke=\"#000000\" stroke-width=\"0\\.100\"/>")
        .Select(m => D(m.Groups[1].Value)).Min();

    /// <summary>The chord symbols' ROOT-SIZED runs (the superscripts are smaller and sit
    /// higher): their one baseline, their em, and their texts.</summary>
    private static (double Y, double Em, string[] Runs) RootRuns(string svg)
    {
        var runs = Regex.Matches(svg,
                "<text x=\"[-\\d.]+\" y=\"([-\\d.]+)\" font-size=\"([\\d.]+)\" font-family=\"TeX Gyre Heros, sans-serif\"[^>]*>([^<]+)</text>")
            .Select(m => (Y: D(m.Groups[1].Value), Em: D(m.Groups[2].Value), Text: m.Groups[3].Value)).ToList();
        double em = runs.Max(r => r.Em);
        var roots = runs.Where(r => r.Em == em).ToList();
        Assert.Equal(4, roots.Count);
        Assert.Single(roots.Select(r => r.Y).Distinct());
        return (roots[0].Y, em, roots.Select(r => r.Text).ToArray());
    }

    /// <summary>The deepest ink bottom among the root runs, measured the way the engraver
    /// measures a symbol (the chord face's own style — regular; ChordName declares no series).</summary>
    private static double DeepestInkBottom((double Y, double Em, string[] Runs) roots)
        => roots.Runs.Min(r => TextFontMetrics.Ink(r, roots.Em, sans: true,
            ChordNameGlyphRun.Style(ScoreTextMetrics.Bundled)).Bottom);

    private static double ExpectedAboveTopLine(double skylineAboveCentre, double deepestInkBottom)
        => skylineAboveCentre + ChordNameEngraver.RelatedStaffPadding - deepestInkBottom;

    [Fact]
    public void OverABareStaff_TheDeepestSymbolsInkClearsTheTopLinesEdge_ByThePadding()
    {
        var svg = Render(BareLine);
        var roots = RootRuns(svg);
        double expected = ExpectedAboveTopLine(
            EngravingDefaults.StaffLineThickness / 2.0, DeepestInkBottom(roots));
        double actual = TopLineY(svg) - roots.Y;
        Assert.InRange(actual, expected - Tolerance, expected + Tolerance);
    }

    [Fact]
    public void ALineOfShallowerSymbols_SitsLower_ByTheirInkDifference()
    {
        // Every symbol's ink bottom joins the floor: a line of "F"s (a flat foot, no
        // overshoot) stands lower than one with "C" and "G" (round bowls dipping under the
        // baseline) by exactly the ink difference. A flat padding over the top line would
        // place both alike.
        var g = Render(BareLine);
        var c = Render("c4@chord(F) d e@chord(F) f | c@chord(F) d e@chord(F) f |");
        var gRoots = RootRuns(g);
        var cRoots = RootRuns(c);
        double gDeepest = DeepestInkBottom(gRoots);
        double cDeepest = DeepestInkBottom(cRoots);
        Assert.True(cDeepest > gDeepest + 0.01,
            $"the probe needs symbols of different depth: G-line {gDeepest}, C-line {cDeepest}");
        double expected = ExpectedAboveTopLine(EngravingDefaults.StaffLineThickness / 2.0, cDeepest);
        double actual = TopLineY(c) - cRoots.Y;
        Assert.InRange(actual, expected - Tolerance, expected + Tolerance);
        Assert.True(actual < TopLineY(g) - gRoots.Y - 0.01, "the shallower symbols must sit lower");
    }

    [Fact]
    public void ANoteUnderASymbol_LiftsTheWholeLine_ToItsInkPlusThePadding()
    {
        // The a'' (one ledger line up) stands under the G7; the line, rigid, rides the
        // staff's skyline there — never lower than that head's top plus the padding.
        var bare = Render(BareLine);
        var high = Render("c4@chord(C) d e@chord(Am) f | c@chord(F) d a''@chord(G7) c |");
        double bareUp = TopLineY(bare) - RootRuns(bare).Y;
        double highUp = TopLineY(high) - RootRuns(high).Y;
        // The head's centre is 1.0 above the top line; its ink top half a head higher.
        double headTop = 1.0 + GlyphMetrics.GetNoteheadBBox(4).Top;
        Assert.True(highUp >= bareUp + 0.5, $"bare {bareUp} high {highUp}");
        Assert.True(highUp >= headTop + ChordNameEngraver.RelatedStaffPadding - Tolerance,
            $"the line must clear the a'' head's top {headTop} by the padding; it stands at {highUp}");
    }
}
