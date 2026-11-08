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
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Which parts a score sings and which it plays (owner's decision 2026-11-08, docs/singing-design.md
/// §3): the words belong to the staff they are printed under — a staff with a lyrics row under it
/// that sings its part is sung, a staff without is played, and a part on two staves is both.
/// </summary>
[Trait("Category", "Unit")]
public class SungStavesTests
{
    private static (string Sung, string Played) Of(string scoreBody)
    {
        var source = $$"""
            part melody { clef treble }
            part piano { clef treble }
            part horn
            section A {
              melody { c'4 d e f | }
              piano { c4 d e f | }
              horn { c'4 d e f | }
              lyrics verse { a b c d | }
            }
            form { A }
            score { {{scoreBody}} }
            """;
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics.Select(d => d.Message)));
        var spec = RenderSpecParser.FindAll(tree).First();
        var sounds = SungStaves.Of(tree.GetRoot(), spec);
        return (string.Join(" ", sounds.Sung), string.Join(" ", sounds.Played));
    }

    [Fact]
    public void TheOwnersExample_IsSungOnTheStaffWithTheWords_AndPlayedOnTheOthers()
        => Assert.Equal(("melody", "melody"), Of("staffGroup { staff melody  staff melody  lyrics verse sings melody  staff melody }"));

    [Fact]
    public void AStaffWithItsWordsUnderIt_IsSungAlone()
        => Assert.Equal(("melody", ""), Of("staff melody  lyrics verse sings melody"));

    [Fact]
    public void AStaffWithNoWords_IsPlayed()
        => Assert.Equal(("", "melody"), Of("staff melody"));

    [Fact]
    public void TheAccompaniment_IsPlayed_WhileTheSingerIsSung()
        => Assert.Equal(("melody", "piano"), Of("staff melody  lyrics verse sings melody  staff piano"));

    [Fact]
    public void WordsOfAPartNoStaffShows_GiveNoVoice_TheStaffAboveIsPlayed()
        => Assert.Equal(("", "horn"), Of("staff horn  lyrics verse sings melody"));

    [Fact]
    public void WordsAboveTheirStaff_BelongToTheFirstStaffOfThatPartBelow()
        => Assert.Equal(("melody", ""), Of("lyrics verse sings melody  staff melody"));

    [Fact]
    public void AnUnboundRow_SingsTheStaffAboveIt()
        => Assert.Equal(("melody", "piano"), Of("staff piano  staff melody  lyrics verse"));

    [Fact]
    public void WordsUnderAnotherPartsStaff_BelongToTheNearestStaffOfTheirPartAbove()
        => Assert.Equal(("melody", "piano"), Of("staff melody  staff piano  lyrics verse sings melody"));
}
