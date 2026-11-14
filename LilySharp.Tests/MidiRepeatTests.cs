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
        Assert.Equal(new[] { 60, 62, 60, 62 }, Pitches("{ |: c'4 d4 :| }"));
    }

    /// <summary>
    /// A replayed bar sounds as it is printed at its place — whichever sign sends the piece
    /// back, and wherever it is written (session 1255, when the form's own walk was dropped and
    /// every book came to be played along its timeline). MEASURED before (Lab
    /// sessions/p1255/probe/p7-dyn): a form's <c>|: A :|</c> carried the level its body ended
    /// on back to the head — the second c ff — where the same form's D.C. and one-sided
    /// <c>:|</c>, and the same repeat written in the music, played the head as printed.
    /// </summary>
    [Theory]
    [InlineData("section A { m { c'1 | d'1@ff | } }", "form { |: A :| }")]   // the form's repeat
    [InlineData("section A { m { c'1 | d'1@ff | } }", "form { A :| }")]      // its one-sided ':|'
    [InlineData("section A { m { c'1 | d'1@ff | } }", "form { A dc }")]      // its D.C.
    [InlineData("section A { m { |: c'1 | d'1@ff :| } }", "form { A }")]     // the music's repeat
    public void AReplayedBarSoundsAsItIsPrinted(string section, string form)
    {
        string src = "octave absolute\ntime 4/4\npart m { }\n" + section + "\n" + form + "\nscore { staff m }\n";
        var notes = new MidiExporter().Export(SyntaxTree.Parse(src)).Tracks[1].Notes.OrderBy(n => n.StartTick).ToList();
        Assert.Equal(new[] { 60, 62, 60, 62 }, notes.Select(n => n.Pitch));
        Assert.Equal(notes[0].Velocity, notes[2].Velocity);
        Assert.Equal(notes[1].Velocity, notes[3].Velocity);
        Assert.True(notes[1].Velocity > notes[0].Velocity);
    }

    /// <summary>A pedal the piece ends under is lifted at its end, in a book with a repeat too:
    /// the timeline's last stretch dropped what stood at its very end (measured the same
    /// session: Lab sessions/p1255/probe/p4-pedal, the first run).</summary>
    [Fact]
    public void APedalThePieceEndsUnderIsLifted_AfterARepeatToo()
    {
        const string Src = "octave absolute\ntime 4/4\npart m { }\nsection A { m { c'1 | } }\nsection B { m { d'1@sustain | e'1 | } }\nform { |: A :| B }\nscore { staff m }\n";
        var pedals = new MidiExporter().Export(SyntaxTree.Parse(Src)).Tracks.SelectMany(t => t.Pedals).OrderBy(p => p.Tick).ToList();
        Assert.NotEmpty(pedals);
        Assert.False(pedals[^1].Down);
    }

    /// <summary>⚠️ TWO RULES, KEPT AS THEY WERE (session 1255; the owner has not chosen): a third
    /// ending with no <c>:|</c> before it. In a FORM it is an ending no repeat opens — two
    /// passes, then its section once (LYS6008 says so, and the page reads the form that way);
    /// in the MUSIC it joins the run — three passes (<see cref="InlineVoltas_CountInferredFromHighestNumber"/>).
    /// The form's rule lived in the form's own MIDI walk until that walk was dropped; it is
    /// <c>RepeatStream.Elements</c>' now. 11 books of the owner's corpus write the form's shape
    /// (their LilyPond originals are <c>\repeat volta 3</c> with three alternatives).</summary>
    [Fact]
    public void AFormsThirdEndingWithNoRepeatSignBeforeIt_IsPlayedOnce()
    {
        const string Src = "octave absolute\ntime 4/4\npart m { }\n"
            + "section A { m { c'1 | } }\nsection B { m { d'1 | } }\nsection C { m { e'1 | } }\nsection D { m { f'1 | } }\n"
            + "form { |: A [1. B] :| [2. C] [3. D] }\nscore { staff m }\n";
        Assert.Equal(new[] { 60, 62, 60, 64, 65 }, Pitches(Src));
    }

    [Fact]
    public void NoRepeat_PlaysOnce()
    {
        Assert.Equal(new[] { 60, 62 }, Pitches("{ c'4 d4 }"));
    }

    [Fact]
    public void RepeatRestartsRelativeOctaveContext()
    {
        // Each pass restarts from the same context: |: c g :| -> c g c g
        // (g resolves to G3 from c both times, not drifting on the 2nd pass).
        Assert.Equal(new[] { 60, 55, 60, 55 }, Pitches("{ |: c'4 g4 :| }"));
    }

    [Fact]
    public void NestedRepeats_Expand()
    {
        // |: a |: b :| :|  ->  a (b b) a (b b)  = a b b a b b
        // a4=A3(57), b4... use c/d to keep it simple: |: c |: d :| :|
        // outer x2 of [ c, (d x2) ] = c d d c d d
        Assert.Equal(new[] { 60, 62, 62, 60, 62, 62 }, Pitches("{ |: c'4 |: d4 :| :| }"));
    }

    [Fact]
    public void ExplicitRepeatCount_PlaysSpanNTimes()
    {
        // |: c d :|*3 -> c d c d c d (played three times).
        Assert.Equal(new[] { 60, 62, 60, 62, 60, 62 }, Pitches("{ |: c'4 d4 :|*3 }"));
    }

    [Fact]
    public void ExplicitRepeatCount_One_PlaysSpanOnce()
    {
        // |: c d :|*1 -> c d (an explicit single pass).
        Assert.Equal(new[] { 60, 62 }, Pitches("{ |: c'4 d4 :|*1 }"));
    }

    [Fact]
    public void ExplicitRepeatCount_OuterAndInner()
    {
        // |: c |: d :|*3 :|*2 -> outer x2 of [ c, (d x3) ] = c d d d  c d d d
        Assert.Equal(new[] { 60, 62, 62, 62, 60, 62, 62, 62 },
            Pitches("{ |: c'4 |: d4 :|*3 :|*2 }"));
    }

    [Fact]
    public void InlineVoltas_PlayCommonBodyThenIthEnding()
    {
        // |: c [1. e] :| [2. f]  -> pass1: c e ; pass2: c f.
        // Each pass restarts the octave context from c, so e=E4(64), f=F4(65).
        Assert.Equal(new[] { 60, 64, 60, 65 }, Pitches("{ |: c'4 [1. e4] :| [2. f4] }"));
    }

    [Fact]
    public void InlineVoltas_CountInferredFromHighestNumber()
    {
        // Three endings -> body plays 3 times, i-th ending per pass.
        Assert.Equal(new[] { 60, 62, 60, 64, 60, 65 },
            Pitches("{ |: c'4 [1. d4] :| [2. e4] [3. f4] }"));
    }

    [Fact]
    public void InlineVoltas_RangeEndingMatchesEachPass()
    {
        // [1-2. d] covers passes 1 and 2; [3. e] covers pass 3. Count = 3.
        Assert.Equal(new[] { 60, 62, 60, 62, 60, 64 },
            Pitches("{ |: c'4 [1-2. d4] :| [3. e4] }"));
    }

    [Fact]
    public void InlineVoltas_ExplicitCountClampsToLastEnding()
    {
        // :|*3 forces 3 passes but only two endings -> pass 3 reuses the last ending. The
        // spelling is an error since 2026-09-30 (LYS1042, in a form
        // and in the music); this pins what the MIDI still does with it.
        Assert.Equal(new[] { 60, 62, 60, 64, 60, 64 },
            Pitches("{ |: c'4 [1. d4] :|*3 [2. e4] }"));
    }

    [Fact]
    public void InlineVoltas_DoNotBreakBeams()
    {
        // '[' before a note is still a beam marker, not a volta: [c d] plays once.
        Assert.Equal(new[] { 60, 62 }, Pitches("{ [c'8 d8] }"));
    }

    // SourceOrdinal per note = which printed copy the playback highlight lights.
    private static int[] Ordinals(string source, int pitch) =>
        new MidiExporter().Export(SyntaxTree.Parse(source))
            .Tracks[1].Notes.Where(n => n.Pitch == pitch)
            .OrderBy(n => n.StartTick).Select(n => n.SourceOrdinal).ToArray();

    [Fact]
    public void VoltaEndingsReusingBodySection_HighlightDistinctPrintedCopies()
    {
        // form { A |: B | [1. B "x"] :| [2. B "y"] | A }: both endings reuse
        // section B, printed as body (copy 0), ending 1 (copy 1), ending 2 (copy 2).
        // The playback highlight picks the printed copy by SourceOrdinal, so B's
        // four onsets (body, ending 1, body, ending 2) must carry ordinals
        // 0,1,0,2 -- NOT 0,1,0,1, which re-lit ending 1 on both passes and never
        // ending 2. Intro/outro section A must keep its own 0,1 (undisturbed).
        const string src =
            "octave absolute\n" +
            "part mel\n" +
            "phrase pa { c''4 }\n" +
            "phrase pb { g''4 }\n" +
            "section A { mel { pa } }\n" +
            "section B { mel { pb } }\n" +
            "form { A |: B | [1. B \"x\"] :| [2. B \"y\"] | A }\n" +
            "score s { staff mel }\n";
        Assert.Equal(new[] { 0, 1, 0, 2 }, Ordinals(src, 79)); // section B (g'' = 79)
        Assert.Equal(new[] { 0, 1 }, Ordinals(src, 72));       // intro/outro A (c'' = 72)
    }
}
