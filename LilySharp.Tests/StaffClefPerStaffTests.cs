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
/// each staff places the notes under its own clef. The part is collected once per staff, and
/// each collection took the clef the first staff that writes one names, so the treble staff's
/// C5 stood two octaves up (owner's report 2026-10-25, stepping the second staff's clef with
/// Ctrl+Shift+Up), and a `clef` change that is none under the bass clef left the treble staff too.
/// </summary>
/// <remarks>Poison: collect every binding under RenderSpec.WrittenClefOf again ⇒ all but the
/// octave-clef case go red.</remarks>
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
    public void AChangeToTheOtherStaffsClef_IsDrawnWhereItChangesSomething()
    {
        // `clef bass` in bar 2 changes nothing on the bass staff and everything on the treble
        // one: the treble staff draws it, the bass staff does not.
        var tree = SyntaxTree.Parse("part melody { clef treble }\nsection A { melody { c''4 d e f | clef bass c,4 d e f | } }\n"
            + "form { ~A }\nscore { staff bass melody  staff melody }\n");
        var staves = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree)).EnumerateStaves().ToArray();
        int Changes(int s) => staves[s].Staff.Voices[0].Measures.SelectMany(m => m.Items).OfType<ClefChangeItem>().Count();
        Assert.Equal(0, Changes(0));
        Assert.Equal(1, Changes(1));
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
