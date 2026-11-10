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
/// Which parts a score sings and which it plays (docs/lyrics-singer-design.md §5): a singer line
/// makes its part a voice wherever a staff shows it, unless the staff says otherwise with
/// <c>sounds</c>; a staff with no singer plays; a part with no staff does not sound.
/// </summary>
[Trait("Category", "Unit")]
public class SungStavesTests
{
    private const string OnMelody = "singer sings verse on melody";

    private static SungStaves.Sounds Sounds(string scoreBody, string singers, string melody = "part melody { clef treble  instrument soprano }", string music = "c'4 d e f |")
    {
        var tree = SyntaxTree.Parse($$"""
            {{melody}}
            part piano { clef treble }
            part horn
            section A {
              melody { {{music}} }
              piano { c4 d e f | }
              horn { c'4 d e f | }
              lyrics verse { a b c d | }
            }
            {{singers}}
            form { A }
            score { {{scoreBody}} }
            """);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics.Select(d => d.Message)));
        return SungStaves.Of(tree.GetRoot(), RenderSpecParser.FindAll(tree).First());
    }

    private static (string Sung, string Played) Of(string scoreBody, string singers = OnMelody)
    {
        var sounds = Sounds(scoreBody, singers);
        return (string.Join(" ", sounds.Sung), string.Join(" ", sounds.Played));
    }

    [Theory]
    [InlineData("staff melody  lyrics verse")]
    [InlineData("lyrics verse  staff melody")]
    [InlineData("staff melody")]                        // words on the page are the score's to choose
    public void ASingersPart_IsSung_AndNotPlayed(string score)
        => Assert.Equal(("melody", ""), Of(score));

    [Fact]
    public void NoSinger_IsPlayed_WithItsWordsPrinted()
    {
        var sounds = Sounds("staff melody  lyrics verse", "");
        Assert.Empty(sounds.Sung);
        Assert.Equal(new[] { "melody" }, sounds.Played);
        Assert.Equal(new[] { "melody" }, sounds.PrintedWords);
    }

    [Fact]
    public void AVoicePreset_AsksForNothing()
        => Assert.Equal(("", "melody"), Of("staff melody", ""));

    [Fact]
    public void ASingerLine_TakesTheWordsOffThePrintedList()
        => Assert.Empty(Sounds("staff melody  lyrics verse", OnMelody).PrintedWords!);

    [Fact]
    public void TheAccompaniment_IsPlayed_WhileTheSingerIsSung()
        => Assert.Equal(("melody", "piano"), Of("staff melody  lyrics verse  staff piano"));

    [Theory]
    [InlineData("staff melody sounds voice", "melody", "")]
    [InlineData("staff melody sounds instrument", "", "melody")]
    [InlineData("staff melody sounds both", "melody", "melody")]
    [InlineData("staff melody sounds instrument  staff melody sounds voice", "melody", "melody")]     // two staves add up
    public void SoundsSays_WhatTheStaffIsHeardAs(string score, string sung, string played)
        => Assert.Equal((sung, played), Of(score));

    [Fact]
    public void APartNoStaffShows_DoesNotSound()
        => Assert.Equal(("", "horn"), Of("staff horn  lyrics verse on melody"));

    [Fact]
    public void AHiddenStaff_SoundsWithoutBeingDrawn()
    {
        Assert.Equal(("melody", "piano"), Of("staff melody hidden  staff piano"));
        Assert.Equal(("melody", "piano"), Of("staff melody  staff piano hidden"));
    }

    [Fact]
    public void AHiddenSinger_IsUnseen()
    {
        var tree = SyntaxTree.Parse("""
            part melody { clef treble }
            part piano { clef treble }
            section A {
              melody { c'4 d e f | }
              piano { c4 d e f | }
              lyrics verse { a b c d | }
            }
            singer sings verse on melody
            form { A }
            score { staff melody hidden  staff piano }
            """);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics.Select(d => d.Message)));
        Assert.Equal(new[] { "melody" }, SungStaves.UnseenSungParts(tree.GetRoot(), RenderSpecParser.FindAll(tree).First()));
    }

    [Fact]
    public void ALineThatSingsAVoiceOfAnEngravedPart_IsNotAnUnseenSinger()
    {
        var source = """
            part melody { clef treble  instrument soprano }
            section A {
              melody { voice alt { c'4 d e f | } { c4 d e f | } }
              lyrics verse { a b c d | }
            }
            singer sings verse on alt
            form { A }
            score { staff melody  lyrics verse on alt }
            """;
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics.Select(d => d.Message)));
        var spec = RenderSpecParser.FindAll(tree).First();
        Assert.Empty(SungStaves.UnseenSungParts(tree.GetRoot(), spec));
    }
}
