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
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using LilySharp.Core.Svg.Layout;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Lily#'s OWN gestures — the scoop and the plop (an approach curve INTO the note) and the
/// guitar bend-up (a curve rising off the note to an arrowhead and an amount label) — pinned
/// to the values they draw today. LilyPond has no scoop or plop at all, and its guitar bend
/// is the unported BendSpanner, so no ledger point can hold these: these are IDENTITY nets
/// (RULES §5.0), the only observers the three devices have. Every number here is the
/// product's own (ArticulationEngraver's BendApproachXOffset 1.55 / BendHeadPadding 0.15 /
/// BendTabXOffset 0.5; SharedRenderer.DrawBendBefore's 1.25 × 1.7 polyline in eight
/// segments; DrawGuitarBend's 1.6 × 2.6 in ten, the 0.32 × 0.55 arrowhead, the label 0.35
/// over the tip at BendLabelEm) — a change to any of them is meant to land here.
/// </summary>
/// <remarks>
/// Measured off the SVG (two decimals) rather than RenderedGeometry, as BendAfterGeometryTests
/// does: the gestures are drawn as plain lines and a text, which the SVG keys to the mark's
/// source position (data-pos) and the recorder does not.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class OwnGestureGeometryTests
{
    private const double Tolerance = 0.006;   // two decimals in the SVG, either side

    // Scoop / plop (ArticulationEngraver.BendApproachXOffset, SharedRenderer.DrawBendBefore).
    private const double ApproachXOffset = 1.55;
    private const double ApproachLength = 1.25;
    private const double ApproachDepth = 1.7;
    private const int ApproachSegments = 8;

    // Bend-up (ArticulationEngraver.BendHeadPadding / BendTabXOffset, SharedRenderer.DrawGuitarBend).
    private const double BendHeadPadding = 0.15;
    private const double BendTabXOffset = 0.5;
    private const double BendReach = 1.6;
    private const double BendRise = 2.6;
    private const int BendSegments = 10;
    private const double ArrowHalfWidth = 0.32;
    private const double ArrowHeight = 0.55;
    private const double LabelOverTip = 0.35;

    // octave 5: c d e f stand in the treble staff's upper half (BendAfterGeometryTests).
    private static string Render(string sectionA) => LiveRender.SvgFromRenderSpec($$"""
        part v { octave 5 }
        section A { {{sectionA}} }
        form { A }
        score { staff v }
        """);

    private static string RenderTab(string sectionA) => LiveRender.SvgFromRenderSpec($$"""
        part gtr { tuning guitar }
        section A { gtr { {{sectionA}} } }
        form { A }
        score { tab gtr }
        """);

    private readonly record struct Seg(int Pos, double X1, double Y1, double X2, double Y2, double Width);

    /// <summary>Every stroked line that carries a source position — the gestures' segments
    /// (stems and staff lines carry none).</summary>
    private static List<Seg> Segments(string svg) => Regex.Matches(svg,
            "<line x1=\"([-\\d.]+)\" y1=\"([-\\d.]+)\" x2=\"([-\\d.]+)\" y2=\"([-\\d.]+)\" stroke=\"#000000\" "
            + "stroke-width=\"([\\d.]+)\" stroke-linecap=\"round\" data-pos=\"(\\d+)\"/>")
        .Select(m => new Seg(int.Parse(m.Groups[6].Value, CultureInfo.InvariantCulture),
            D(m.Groups[1]), D(m.Groups[2]), D(m.Groups[3]), D(m.Groups[4]), D(m.Groups[5])))
        .ToList();

    private static double D(Group g) => double.Parse(g.Value, CultureInfo.InvariantCulture);

    /// <summary>The note heads (notation) or fret digits (tab) with their source positions.</summary>
    private static List<(int Pos, double X, double Y)> Heads(string svg) => Regex.Matches(svg,
            "<text class=\"music\" x=\"([-\\d.]+)\" y=\"([-\\d.]+)\" font-size=\"[\\d.]+\" data-pos=\"(\\d+)\">(.)</text>")
        .Where(m => m.Groups[4].Value[0] is LilySharp.Core.Svg.EmmentalerGlyphs.NoteheadBlack)
        .Select(m => (int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture), D(m.Groups[1]), D(m.Groups[2])))
        .ToList();

    private static List<(int Pos, double X, double Y)> Digits(string svg) => Regex.Matches(svg,
            "<text x=\"([-\\d.]+)\" y=\"([-\\d.]+)\" font-size=\"[\\d.]+\" text-anchor=\"middle\" data-pos=\"(\\d+)\">\\d+</text>")
        .Select(m => (int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture), D(m.Groups[1]), D(m.Groups[2])))
        .ToList();

    /// <summary>The gesture's own note: the head (or digit) of the greatest source position
    /// before the mark's.</summary>
    private static (double X, double Y) OwnerOf(List<(int Pos, double X, double Y)> anchors, int markPos)
    {
        var own = anchors.Where(a => a.Pos < markPos).OrderByDescending(a => a.Pos).First();
        return (own.X, own.Y);
    }

    /// <summary>The segments of one gesture, in draw order, grouped by the mark they came from.</summary>
    private static List<List<Seg>> Gestures(string svg) =>
        Segments(svg).GroupBy(s => s.Pos).OrderBy(g => g.Key).Select(g => g.ToList()).ToList();

    private static void AssertPoint((double X, double Y) expected, double x, double y, string what)
    {
        Assert.InRange(x, expected.X - Tolerance, expected.X + Tolerance);
        Assert.InRange(y, expected.Y - Tolerance, expected.Y + Tolerance);
        _ = what;
    }

    [Theory]
    [InlineData("c4@scoop d e f |", +1)]   // the scoop rises INTO the head: it starts BELOW (device +)
    [InlineData("c4@plop d e f |", -1)]    // the plop falls into it: it starts above
    public void TheApproachCurve_ArrivesAtTheHeadFromTheLeft(string music, int startSide)
    {
        var svg = Render(music);
        var gesture = Assert.Single(Gestures(svg));
        var head = OwnerOf(Heads(svg), gesture[0].Pos);
        Assert.Equal(ApproachSegments, gesture.Count);
        Assert.All(gesture, s => Assert.Equal(0.13, s.Width, 3));
        var end = (head.X - ApproachXOffset, head.Y);
        AssertPoint(end, gesture[^1].X2, gesture[^1].Y2, "the curve's end");
        AssertPoint((end.Item1 - ApproachLength, head.Y + startSide * ApproachDepth),
            gesture[0].X1, gesture[0].Y1, "the curve's start");
        // One polyline: every segment starts where the previous ended, and the X advances.
        for (int i = 1; i < gesture.Count; i++)
        {
            Assert.Equal(gesture[i - 1].X2, gesture[i].X1, 6);
            Assert.Equal(gesture[i - 1].Y2, gesture[i].Y1, 6);
            Assert.True(gesture[i].X2 > gesture[i].X1);
        }
    }

    [Fact]
    public void TheApproachCurve_OnATabStaff_ArrivesAtTheDigitOnItsString()
    {
        var svg = RenderTab("c4@scoop d e f |");
        var gesture = Assert.Single(Gestures(svg));
        var digit = OwnerOf(Digits(svg), gesture[0].Pos);
        // The row is the string line the digit sits on: a staff line (0.100 thick) within a
        // string space of the digit's baseline.
        double row = StaffLineNearest(svg, digit.Y);
        AssertPoint((digit.X - ApproachXOffset, row), gesture[^1].X2, gesture[^1].Y2, "the curve's end");
        AssertPoint((digit.X - ApproachXOffset - ApproachLength, row + ApproachDepth),
            gesture[0].X1, gesture[0].Y1, "the curve's start");
    }

    [Theory]
    [InlineData("c4 e@bend(half) g a |", "½")]
    [InlineData("c4 e@bend(full) g a |", "full")]
    [InlineData("c4 e@bend(3) g a |", "1½")]
    [InlineData("c4 e@bend(4) g a |", "2")]
    public void TheBendUp_LeavesTheHeadsRight_AndLabelsTheTip(string music, string label)
    {
        var svg = Render(music);
        var all = Segments(svg);
        int pos = all.Select(s => s.Pos).Distinct().Single();
        var head = OwnerOf(Heads(svg), pos);
        var curve = all.Where(s => Math.Abs(s.Width - 0.13) < 1e-9).ToList();
        var arrow = all.Where(s => Math.Abs(s.Width - 0.16) < 1e-9).ToList();
        Assert.Equal(BendSegments, curve.Count);
        Assert.Equal(2, arrow.Count);

        var start = (head.X + GlyphMetrics.GetNoteheadBBox(4).Width + BendHeadPadding, head.Y);
        AssertPoint(start, curve[0].X1, curve[0].Y1, "the curve's start");
        var tip = (start.Item1 + BendReach, head.Y - BendRise);   // device Y grows downward
        AssertPoint(tip, curve[^1].X2, curve[^1].Y2, "the curve's tip");
        // The arrowhead: two strokes into the tip from ArrowHalfWidth either side, ArrowHeight below it.
        foreach (var a in arrow)
            AssertPoint(tip, a.X2, a.Y2, "an arrow stroke's end");
        Assert.Equal(new[] { tip.Item1 - ArrowHalfWidth, tip.Item1 + ArrowHalfWidth },
            arrow.Select(a => Math.Round(a.X1, 2)).OrderBy(x => x).Select(x => Math.Round(x, 2)),
            new RoundedComparer());
        Assert.All(arrow, a => Assert.InRange(a.Y1, tip.Item2 + ArrowHeight - Tolerance, tip.Item2 + ArrowHeight + Tolerance));
        // The label: centred on the tip, LabelOverTip above it, italic at BendLabelEm.
        var m = Regex.Match(svg,
            "<text x=\"([-\\d.]+)\" y=\"([-\\d.]+)\" font-size=\"([\\d.]+)\" font-style=\"italic\" "
            + "text-anchor=\"middle\" data-pos=\"" + pos + "\">([^<]*)</text>");
        Assert.True(m.Success, "the amount label is drawn with the mark's source position");
        Assert.Equal(label, m.Groups[4].Value);
        AssertPoint((tip.Item1, tip.Item2 - LabelOverTip), D(m.Groups[1]), D(m.Groups[2]), "the label");
        Assert.Equal(LilySharp.Core.Rendering.SharedRenderer.BendLabelEm, D(m.Groups[3]), 2);
    }

    [Fact]
    public void TheBendUp_OnATabStaff_LeavesTheDigitOnItsString()
    {
        var svg = RenderTab("c4 e@bend(full) g a |");
        var all = Segments(svg);
        int pos = all.Select(s => s.Pos).Distinct().Single();
        var digit = OwnerOf(Digits(svg), pos);
        var curve = all.Where(s => Math.Abs(s.Width - 0.13) < 1e-9).ToList();
        double row = StaffLineNearest(svg, digit.Y);
        AssertPoint((digit.X + BendTabXOffset, row), curve[0].X1, curve[0].Y1, "the curve's start");
        AssertPoint((digit.X + BendTabXOffset + BendReach, row - BendRise), curve[^1].X2, curve[^1].Y2, "the tip");
    }

    /// <summary>The Y of the string line a fret digit sits on: the digit is centred ON its
    /// line, so its baseline <paramref name="baselineY"/> hangs under it by less than a string
    /// space — the nearest drawn staff line (0.100 thick) ABOVE the baseline.</summary>
    private static double StaffLineNearest(string svg, double baselineY) => Regex.Matches(svg,
            "<line x1=\"[-\\d.]+\" y1=\"([-\\d.]+)\" x2=\"[-\\d.]+\" y2=\"([-\\d.]+)\" stroke=\"#000000\" stroke-width=\"0.100\"/>")
        .Select(m => D(m.Groups[1]))
        .Where(ly => ly < baselineY)
        .OrderByDescending(ly => ly)
        .First();

    private sealed class RoundedComparer : IEqualityComparer<double>
    {
        public bool Equals(double a, double b) => Math.Abs(a - b) <= Tolerance;
        public int GetHashCode(double d) => 0;
    }
}
