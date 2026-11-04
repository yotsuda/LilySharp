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
using LilySharp.Core.Midi;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests.Midi;

/// <summary>
/// A tuplet whose notes are not a whole number of ticks must still end where it was written to:
/// 7 16ths in 480 ticks are 68.57 each, and the group is 480 however they are rounded.
/// </summary>
[Trait("Category", "Unit")]
public class MidiTupletClockTests
{
    private static string Bars(string group, int groupsPerBar, int bars)
        => string.Join(" ", Enumerable.Repeat(
            string.Join(" ", Enumerable.Repeat(group, groupsPerBar)) + " |", bars));

    private static int[] Onsets(string music)
    {
        var tree = SyntaxTree.Parse($$"""
            octave absolute
            time 4/4
            part a
            section A { a { {{music}} } }
            form { ~A }
            score { staff a }
            """);
        return new MidiExporter().Export(tree).Tracks.SelectMany(t => t.Notes)
            .Select(n => n.StartTick).OrderBy(t => t).ToArray();
    }

    [Theory]
    [InlineData("tuplet 7/4 { c'16 d' e' f' g' a' b' }", 7)]   // 68.57: rounds up, runs late
    [InlineData("tuplet 11/8 { c'32 d' e' f' g' a' b' c' d' e' f' }", 11)]   // 43.6: rounds up
    [InlineData("tuplet 13/8 { c'32 d' e' f' g' a' b' c' d' e' f' g' a' }", 13)]   // 36.9: up
    [InlineData("tuplet 9/8 { c'32 d' e' f' g' a' b' c' d' }", 9)]   // 53.3: rounds down, runs early
    [InlineData("tuplet 3/2 { tuplet 5/4 { c'32 d' e' f' g' } c'8 c'8 }", 7)]   // nested
    [InlineData("tuplet 7/4 { <c' e'>16 <d' f'> <e' g'> <f' a'> <g' b'> <a' c''> <b' d''> }", 14)]
    [InlineData("tuplet 7/4 { <c' e'>16 q q q q q q }", 14)]
    [InlineData("tuplet 7/4 { c'16 16 16 16 16 16 16 }", 7)]
    [InlineData("tuplet 7/4 { c'16 r r r r r r }", 1)]
    [InlineData("tuplet 7/4 { c'16 /16 / / / / / }", 1)]
    [InlineData("<< c' d' e' f' g' a' b' >>4", 7)]
    [InlineData("<< c' 2 3 4 5 6 7 >>4", 7)]
    public void AGroupsNextBeatStandsWhereItWasWritten(string group, int notes)
    {
        var onsets = Onsets(Bars(group, 4, 8));
        // Every group begins on its beat, in the last bar as in the first.
        for (int i = 0; i < onsets.Length / notes; i++)
            Assert.Equal(i * 480, onsets[i * notes]);
    }

    [Fact]
    public void ADrumGroupEndsOnItsBeatToo()
    {
        var tree = SyntaxTree.Parse($$"""
            time 4/4
            part kit { clef percussion }
            section A { kit { {{Bars("tuplet 7/4 { bd16 sn bd sn bd sn bd }", 4, 2)}} } }
            form { ~A }
            score { staff kit }
            """);
        var onsets = new MidiExporter().Export(tree).Tracks.SelectMany(t => t.Notes)
            .Select(n => n.StartTick).OrderBy(t => t).ToArray();
        for (int i = 0; i < onsets.Length / 7; i++)
            Assert.Equal(i * 480, onsets[i * 7]);
    }

    private const string Whole = "tuplet 7/4 { c'16 d' e' f' g' a' b' }";

    [Fact]
    public void TheMusicAfterVoicesOwesThemNothing()
    {
        var tree = SyntaxTree.Parse($$"""
            octave absolute
            time 4/4
            part a
            section A { a { voice { tuplet 7/4 { c'16 d' e' } } { tuplet 7/4 { e'16 f' g' } } {{Whole}} } }
            form { ~A }
            score { staff a }
            """);
        var last = new MidiExporter().Export(tree).Tracks.SelectMany(t => t.Notes)
            .Select(n => n.StartTick).OrderBy(t => t).TakeLast(7).ToArray();
        int spanEnd = last[0];
        Assert.Equal(Onsets(Whole + " r2."), last.Select(t => t - spanEnd));
    }

    [Fact]
    public void APartsCopyOfASectionOwesNothingToTheCopyBeforeIt()
    {
        var tree = SyntaxTree.Parse($$"""
            octave absolute
            time 4/4
            part a { section A { tuplet 7/4 { c'16 d' e' } r2. } }
            part b { section A { {{Whole}} r2. } }
            form { ~A }
            score { staff a staff b }
            """);
        var second = new MidiExporter().Export(tree).Tracks.Last(t => t.Notes.Count > 0).Notes
            .Select(n => n.StartTick).OrderBy(t => t);
        Assert.Equal(Onsets(Whole + " r2."), second);
    }

    [Fact]
    public void AVoiceOwesNothingToTheVoiceBeforeIt()
    {
        // The first voice stops part way through a group, with a fraction of a tick unspent.
        var tree = SyntaxTree.Parse($$"""
            octave absolute
            time 4/4
            part a
            section A { a { voice { tuplet 7/4 { c'16 d' e' } r2. } { {{Whole}} r2. } } }
            form { ~A }
            score { staff a }
            """);
        var second = new MidiExporter().Export(tree).Tracks.SelectMany(t => t.Notes)
            .Where(n => n.Voice == 1).Select(n => n.StartTick).OrderBy(t => t);
        Assert.Equal(Onsets(Whole + " r2."), second);
    }

    [Fact]
    public void APartOwesNothingToThePartBeforeIt()
    {
        var tree = SyntaxTree.Parse($$"""
            octave absolute
            time 4/4
            part a
            part b
            section A { a { tuplet 7/4 { c'16 d' e' } r2. } b { {{Whole}} r2. } }
            form { ~A }
            score { staff a staff b }
            """);
        var second = new MidiExporter().Export(tree).Tracks.Last(t => t.Notes.Count > 0).Notes
            .Select(n => n.StartTick).OrderBy(t => t);
        Assert.Equal(Onsets(Whole + " r2."), second);
    }

    [Fact]
    public void ANoteInsideAGroupStandsAtItsNearestTick()
    {
        var onsets = Onsets(Bars("tuplet 7/4 { c'16 d' e' f' g' a' b' }", 4, 1));
        for (int i = 0; i < 7; i++)
            Assert.InRange(onsets[i], (int)System.Math.Floor(i * 480 / 7.0), (int)System.Math.Ceiling(i * 480 / 7.0));
    }
}
