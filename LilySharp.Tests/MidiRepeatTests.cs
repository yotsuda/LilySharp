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

namespace LilySharp.Tests;

/// <summary>
/// Inline symbolic repeats (<c>|: … :|</c>) must actually repeat in MIDI playback,
/// not just draw repeat barlines. (Previously the barlines were ignored by the
/// MIDI exporter, so the music played once.)
/// </summary>
public sealed class MidiRepeatTests
{
    private static int[] Pitches(string source) =>
        new MidiExporter().Export(SyntaxTree.Parse(source))
            .Tracks[1].Notes.OrderBy(n => n.StartTick).Select(n => n.Pitch).ToArray();

    [Fact]
    public void InlineRepeat_PlaysSpanTwice()
    {
        // |: c d :| -> c d c d (played twice).
        Assert.Equal(new[] { 60, 62, 60, 62 }, Pitches("{ |: c4 d4 :| }"));
    }

    [Fact]
    public void NoRepeat_PlaysOnce()
    {
        Assert.Equal(new[] { 60, 62 }, Pitches("{ c4 d4 }"));
    }

    [Fact]
    public void RepeatRestartsRelativeOctaveContext()
    {
        // Each pass restarts from the same context: |: c g :| -> c g c g
        // (g resolves to G3 from c both times, not drifting on the 2nd pass).
        Assert.Equal(new[] { 60, 55, 60, 55 }, Pitches("{ |: c4 g4 :| }"));
    }

    [Fact]
    public void NestedRepeats_Expand()
    {
        // |: a |: b :| :|  ->  a (b b) a (b b)  = a b b a b b
        // a4=A3(57), b4... use c/d to keep it simple: |: c |: d :| :|
        // outer x2 of [ c, (d x2) ] = c d d c d d
        Assert.Equal(new[] { 60, 62, 62, 60, 62, 62 }, Pitches("{ |: c4 |: d4 :| :| }"));
    }

    [Fact]
    public void ExplicitRepeatCount_PlaysSpanNTimes()
    {
        // |: c d :|*3 -> c d c d c d (played three times).
        Assert.Equal(new[] { 60, 62, 60, 62, 60, 62 }, Pitches("{ |: c4 d4 :|*3 }"));
    }

    [Fact]
    public void ExplicitRepeatCount_One_PlaysSpanOnce()
    {
        // |: c d :|*1 -> c d (an explicit single pass).
        Assert.Equal(new[] { 60, 62 }, Pitches("{ |: c4 d4 :|*1 }"));
    }

    [Fact]
    public void ExplicitRepeatCount_OuterAndInner()
    {
        // |: c |: d :|*3 :|*2 -> outer x2 of [ c, (d x3) ] = c d d d  c d d d
        Assert.Equal(new[] { 60, 62, 62, 62, 60, 62, 62, 62 },
            Pitches("{ |: c4 |: d4 :|*3 :|*2 }"));
    }

    [Fact]
    public void InlineVoltas_PlayCommonBodyThenIthEnding()
    {
        // |: c [1. e] :| [2. f]  -> pass1: c e ; pass2: c f.
        // Each pass restarts the octave context from c, so e=E4(64), f=F4(65).
        Assert.Equal(new[] { 60, 64, 60, 65 }, Pitches("{ |: c4 [1. e4] :| [2. f4] }"));
    }

    [Fact]
    public void InlineVoltas_CountInferredFromHighestNumber()
    {
        // Three endings -> body plays 3 times, i-th ending per pass.
        Assert.Equal(new[] { 60, 62, 60, 64, 60, 65 },
            Pitches("{ |: c4 [1. d4] :| [2. e4] [3. f4] }"));
    }

    [Fact]
    public void InlineVoltas_RangeEndingMatchesEachPass()
    {
        // [1-2. d] covers passes 1 and 2; [3. e] covers pass 3. Count = 3.
        Assert.Equal(new[] { 60, 62, 60, 62, 60, 64 },
            Pitches("{ |: c4 [1-2. d4] :| [3. e4] }"));
    }

    [Fact]
    public void InlineVoltas_ExplicitCountClampsToLastEnding()
    {
        // :|*3 forces 3 passes but only two endings -> pass 3 reuses the last ending.
        Assert.Equal(new[] { 60, 62, 60, 64, 60, 64 },
            Pitches("{ |: c4 [1. d4] :|*3 [2. e4] }"));
    }

    [Fact]
    public void InlineVoltas_DoNotBreakBeams()
    {
        // '[' before a note is still a beam marker, not a volta: [c d] plays once.
        Assert.Equal(new[] { 60, 62 }, Pitches("{ [c8 d8] }"));
    }
}
