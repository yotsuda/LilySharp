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
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// One part on two staves under two clefs — <c>staff melody</c> and <c>staff bass melody</c>:
/// each staff places the notes under its own clef. The part's notes were placed once, under the
/// clef the first staff that writes one names, and the other staff drew those places under its
/// own, so the treble staff's C5 stood two octaves up (owner's report 2026-10-25, stepping the
/// second staff's clef with Ctrl+Shift+Up).
/// </summary>
/// <remarks>Poison: skip MeasureCollector.PlaceUnderAnotherClef ⇒ the first two go red.</remarks>
[Trait("Category", "Unit")]
public sealed class StaffClefPerStaffTests
{
    // Positions from the middle line: C5 is 1 on a treble staff (B4), 13 on a bass staff (D3).
    private static int[][] Positions(string rows, string music = "c'4 d e f |")
    {
        var tree = SyntaxTree.Parse("part melody { clef treble }\nsection A { melody { " + music + " } }\nform { ~A }\nscore { " + rows + " }\n");
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        return score.EnumerateStaves()
            .Select(s => s.Staff.Voices[0].Measures.SelectMany(m => m.Items).OfType<NoteItem>().Select(n => n.StaffPosition).ToArray())
            .ToArray();
    }

    [Fact]
    public void TheClefWrittenOnTheSecondStaff_MovesNothingOnTheFirst()
    {
        var p = Positions("staff melody  staff bass melody");
        Assert.Equal(1, p[0][0]);
        Assert.Equal(13, p[1][0]);
    }

    [Fact]
    public void TheClefWrittenOnTheFirstStaff_MovesNothingOnTheSecond()
    {
        var p = Positions("staff bass melody  staff melody");
        Assert.Equal(13, p[0][0]);
        Assert.Equal(1, p[1][0]);
    }

    [Fact]
    public void AnOctaveClef_PlacesAsItsParentDoes()
    {
        var p = Positions("staff melody  staff treble_8 melody");
        Assert.Equal(p[0], p[1]);
    }

    [Fact]
    public void AfterThePartsOwnClefChange_EveryStaffStandsInIt()
    {
        // The part changes to the alto clef in bar 2: from there both staves are in it, so
        // its notes stand alike on both; before it each staff has its own clef.
        var p = Positions("staff melody  staff bass melody", "c'4 d e f | clef alto c4 d e f |");
        Assert.Equal(1, p[0][0]);
        Assert.Equal(13, p[1][0]);
        Assert.Equal(p[0].Skip(4), p[1].Skip(4));
    }
}
