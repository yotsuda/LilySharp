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
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A drum staff's hairpins are one drummer's (session 1320, HairpinEngraver.AsOneDrummers), as
/// its levels are (DynamicEngraver.DrumDoubles): where both lines of a chart write the level,
/// one is printed, and the hairpin between two such levels stands between the printed ones —
/// whichever line wrote it, and once where both did.
/// </summary>
/// <remarks>
/// LILYSHARP-OWN, as the levels' rule is. Until this session the wedge of the line whose p and
/// f were the dropped copies found no text at its ends: it ran from note column to note column
/// on a line of its own under the printed p and f, and where both lines wrote the hairpin both
/// wedges were drawn (Lab sessions/p1320/probe d1 and d2).
/// </remarks>
[Trait("Category", "Unit")]
public class DrumChartHairpinTests
{
    private const string Head = "time 4/4\npaper { raggedRight }\npart d { clef percussion }\nsection A { d {\n";
    private const string Tail = "\n} }\nform { A }\nscore { staff d }\n";

    private static (string Wedges, string Levels) Say(string bar)
    {
        var tree = SyntaxTree.Parse(Head + bar + Tail);
        Assert.DoesNotContain(tree.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        var layout = new LayoutEngine(score.Paper).Layout(score);
        return (string.Join(" ", layout.HairpinLayouts.OrderBy(h => h.StartX).ThenByDescending(h => h.YUp)
                .Select(h => $"{h.StartX:F2}..{h.EndX:F2} at {h.YUp + EngravingDefaults.StaffMiddle:F2}")),
            string.Join(" ", layout.DynamicLayouts.OrderBy(d => d.X)
                .Select(d => $"{d.Text} {d.X:F2} at {d.YUp + EngravingDefaults.StaffMiddle:F2}")));
    }

    /// <summary>
    /// Both lines write the levels, and one of them or both the hairpin: one wedge, from the
    /// printed p's padded right edge to the printed f's left, the three on one line — the
    /// picture of the first line writing it all alone, which was right before.
    /// </summary>
    [Theory]
    // The first line writes it all, the second nothing: the picture.
    [InlineData("drums { { hh4@p@cresc hh hh hh@f } { bd4 bd bd bd } } |")]
    // The lower line writes the hairpin: its p and f are the dropped copies.
    [InlineData("drums { { hh4@p hh hh hh@f } { bd4@p@cresc bd bd bd@f } } |")]
    // The upper line does.
    [InlineData("drums { { hh4@p@cresc hh hh hh@f } { bd4@p bd bd bd@f } } |")]
    // Both do: one wedge.
    [InlineData("drums { { hh4@p@cresc hh hh hh@f } { bd4@p@cresc bd bd bd@f } } |")]
    // The same as two voices of the staff.
    [InlineData("voice { hh4@p hh hh hh@f } { bd4@p@cresc bd bd bd@f } |")]
    [InlineData("voice { hh4@p@cresc hh hh hh@f } { bd4@p@cresc bd bd bd@f } |")]
    public void TheHairpinBetweenTwoLevelsBothLinesWrite_StandsBetweenThePrintedOnes(string bar)
    {
        var (wedges, levels) = Say(bar);
        Assert.Equal("18.27..23.91 at -5.32", wedges);
        Assert.Equal("p 16.54 at -3.92 f 25.54 at -3.92", levels);
    }

    /// <summary>The same hairpin over the same beats in both lines, with no level at either
    /// end, is one wedge too; two different ones, or the same over other beats, both stand.</summary>
    [Theory]
    [InlineData("drums { { hh4@cresc hh hh hh@!cresc } { bd4@cresc bd bd bd@!cresc } } |", 1)]
    [InlineData("drums { { hh4@cresc hh hh hh@!cresc } { bd4 bd bd bd } } |", 1)]
    [InlineData("drums { { hh4@cresc hh hh hh@!cresc } { bd4@decresc bd bd bd@!decresc } } |", 2)]
    [InlineData("drums { { hh4@cresc hh hh@!cresc hh } { bd4@cresc bd bd bd@!cresc } } |", 2)]
    [InlineData("drums { { hh4@cresc hh hh hh@!cresc } { bd4 bd@cresc bd bd@!cresc } } |", 2)]
    public void TheSameHairpinInBothLines_IsOneWedge(string bar, int wedges)
        => Assert.Equal(wedges, Say(bar).Wedges.Split(" at ").Length - 1);

    /// <summary>A staff that is no drum staff engraves each voice's, as LilyPond does
    /// (HairpinVoiceTests.TheSameHairpinInBothVoices_IsDrawnForEach).</summary>
    [Fact]
    public void OnAPitchedStaff_EachVoicesHairpinStands()
    {
        var tree = SyntaxTree.Parse("octave absolute\ntime 4/4\npaper { raggedRight }\npart m { clef treble }\nsection A { m {\n"
            + "voice { c''2@p@cresc c''2@f } { c'2@p@cresc c'2@f } |" + "\n} }\nform { A }\nscore { staff m }\n");
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        Assert.Equal(2, new LayoutEngine(score.Paper).Layout(score).HairpinLayouts.Length);
    }

    /// <summary>The room a system keeps under the drum staff is the drawn wedge's: the table
    /// the staff skylines read (ScoreSideTables.HairpinsByStaff) holds the one wedge.</summary>
    [Fact]
    public void TheRoomUnderTheStaff_IsTheOneWedges()
    {
        var tree = SyntaxTree.Parse("time 4/4\npart d { clef percussion }\npart b { clef bass }\nsection A {\n"
            + " d { drums { { hh4@p@cresc hh hh hh@f } { bd4@p@cresc bd bd bd@f } } | }\n b { c1 | }\n}\nform { A }\nscore { staff d staff b }\n");
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        var pin = Assert.Single(ScoreSideTables.HairpinsByStaff(score).At(0));
        Assert.Equal(0, pin.VoiceIndex);
    }

    /// <summary>…and the wedge that stands is the first line's; a pitched staff beside the drum
    /// staff keeps each voice's.</summary>
    [Fact]
    public void TheFirstLinesWedgeStands_AndAPitchedStaffBesideItKeepsBoth()
    {
        var tree = SyntaxTree.Parse("octave absolute\ntime 4/4\npart d { clef percussion }\npart m { clef treble }\nsection A {\n"
            + " d { drums { { hh4@cresc hh hh hh@!cresc } { bd4@cresc bd bd bd@!cresc } } | }\n"
            + " m { voice { c''2@cresc c''2@!cresc } { c'2@cresc c'2@!cresc } | }\n}\nform { A }\nscore { staff d staff m }\n");
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        var table = ScoreSideTables.HairpinsByStaff(score);
        Assert.Equal(0, Assert.Single(table.At(0)).VoiceIndex);
        Assert.Equal(2, table.At(1).Length);
        Assert.Equal(3, new LayoutEngine(score.Paper).Layout(score).HairpinLayouts.Length);
    }
}
