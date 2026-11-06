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
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A tie or slur is scored in its STAFF'S OWN frame (session 1116, BowLayout's frame remark):
/// its numbers do not depend on where in the system the staff stands, and the readers — the
/// drawing, the scripts, the page's extents — add the staff's place themselves.
/// </summary>
/// <remarks>
/// The net is the staff ORDER of a grand staff: the same music laid as the lower staff and as
/// the upper one must give the same bow to the bit, and the drawn bow must stand the staves'
/// distance apart. Until this session a bow baked its staff's within-system offset into its
/// Y, and the carry from the preliminary pass had to pay that offset's change back
/// (LayoutEngine.Prelim's history).
/// </remarks>
[Trait("Category", "Unit")]
public class BowStaffFrameTests
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>The bass part's music as the lower staff (<paramref name="bassBelow"/>) or the
    /// upper one of a grand staff; the treble part rests, so the spacing is the same either way.</summary>
    private static string Book(string bass, bool bassBelow, string treble = "r1 |") => $$"""
        octave absolute
        time 4/4
        part rh { clef treble }
        part lh { clef bass }
        section A { rh { {{treble}} } lh { {{bass}} } }
        form { ~A }
        score { grandStaff { {{(bassBelow ? "staff rh staff lh" : "staff lh staff rh")}} } }
        """;

    private static ScoreLayout Layout(string book)
    {
        var tree = SyntaxTree.Parse(book);
        var score = new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!);
        return new LayoutEngine().Layout(score);
    }

    private static void AssertSameBow(BowLayout below, BowLayout above)
    {
        Assert.Equal(above.StartX, below.StartX);
        Assert.Equal(above.EndX, below.EndX);
        Assert.Equal(above.StartYUp, below.StartYUp);
        Assert.Equal(above.EndYUp, below.EndYUp);
        Assert.Equal(above.Control1, below.Control1);
        Assert.Equal(above.Control2, below.Control2);
    }

    [Fact]
    public void ATieOfTheLowerStaffIsTheTieOfTheUpperOne()
    {
        const string bass = "c4~ c4 r2 |";
        var below = Layout(Book(bass, bassBelow: true)).TieLayouts.Single();
        var above = Layout(Book(bass, bassBelow: false)).TieLayouts.Single();
        Assert.Equal(1, below.StaffIndex);
        Assert.Equal(0, above.StaffIndex);
        AssertSameBow(below, above);
    }

    [Fact]
    public void ASlurOfTheLowerStaffIsTheSlurOfTheUpperOne()
    {
        const string bass = "c4( e g) r4 |";
        var below = Layout(Book(bass, bassBelow: true)).SlurLayouts.Single();
        var above = Layout(Book(bass, bassBelow: false)).SlurLayouts.Single();
        Assert.Equal(1, below.StaffIndex);
        AssertSameBow(below, above);
    }

    [Fact]
    public void AScriptAvoidingTheLowerStaffsSlurReadsItWhereItIs()
    {
        // Stems down, so the slur is above, and a trill stands OUTSIDE a slur (avoid-slur
        // outside): its height reads the bow's. Read a staff's distance away from where the
        // bow is scored, the bow would be out of the trill's way, or further in it.
        const string bass = "c'4( d'@trill e') r4 |";
        var below = Layout(Book(bass, bassBelow: true)).ArticulationLayouts.Single();
        var above = Layout(Book(bass, bassBelow: false)).ArticulationLayouts.Single();
        Assert.Equal(1, below.StaffIndex);
        Assert.Equal(above.YUp, below.YUp);
        // …and the slur did move the trill: without it the trill stands lower.
        var noSlur = Layout(Book("c'4 d'@trill e' r4 |", bassBelow: true)).ArticulationLayouts.Single();
        Assert.True(below.YUp > noSlur.YUp + 0.5, $"trill {below.YUp} over slur, {noSlur.YUp} without");
    }

    [Fact]
    public void AScriptRidingTheLowerStaffsTieReadsItWhereItIs()
    {
        // Stems down, so the tie curves up, under an accent above the head: the accent stands
        // on the tie's profile (Script_engraver::acknowledge_tie), which is read in the
        // staff's frame too.
        const string bass = "c'4~@accent c' r2 |";
        var below = Layout(Book(bass, bassBelow: true)).ArticulationLayouts.Single();
        var above = Layout(Book(bass, bassBelow: false)).ArticulationLayouts.Single();
        Assert.Equal(1, below.StaffIndex);
        Assert.Equal(above.YUp, below.YUp);
        var noTie = Layout(Book("c'4@accent c' r2 |", bassBelow: true)).ArticulationLayouts.Single();
        Assert.True(below.YUp > noTie.YUp + 0.2, $"accent {below.YUp} over the tie, {noTie.YUp} without");
    }

    [Fact]
    public void TheDrawnTieStandsTheStavesDistanceLower()
    {
        const string bass = "c4~ c4 r2 |";
        string belowSvg = LilySharp.Core.Svg.SvgGenerator.Generate(SyntaxTree.Parse(Book(bass, bassBelow: true)));
        string aboveSvg = LilySharp.Core.Svg.SvgGenerator.Generate(SyntaxTree.Parse(Book(bass, bassBelow: false)));
        // The staff lines of each system, top to bottom: the bass staff is lines 5..9 when it is
        // the lower staff and 0..4 when it is the upper.
        double[] Lines(string svg) => Regex
            .Matches(svg, "<line x1=\"([-\\d.]+)\" y1=\"([-\\d.]+)\" x2=\"([-\\d.]+)\" y2=\"([-\\d.]+)\"")
            .Where(l => l.Groups[2].Value == l.Groups[4].Value
                && double.Parse(l.Groups[3].Value, Inv) - double.Parse(l.Groups[1].Value, Inv) > 5)
            .Select(l => double.Parse(l.Groups[2].Value, Inv)).Distinct().OrderBy(y => y).ToArray();
        double TieStartY(string svg) => double.Parse(Regex.Match(svg,
            "d=\"M ([-\\d.]+),([-\\d.]+) C ").Groups[2].Value, Inv);
        double belowTop = Lines(belowSvg)[5], aboveTop = Lines(aboveSvg)[0];
        Assert.True(belowTop > aboveTop + 5);
        // The tie hangs the same distance under its staff's top line either way.
        Assert.InRange(TieStartY(belowSvg) - belowTop, TieStartY(aboveSvg) - aboveTop - 0.011,
            TieStartY(aboveSvg) - aboveTop + 0.011);
    }
}
