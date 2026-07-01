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
using System.Collections.Immutable;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using Xunit;

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
public class OttavaTransposerTests
{
    private static NoteItem Note(int staffPos, int midi = 60) =>
        new NoteItem(staffPos, Fraction.Quarter, 0, null, false, 0) { Midi = midi };

    private static Measure Meas(params MusicItem[] items) =>
        new Measure(ImmutableArray.Create(items), BarlineType.None, BarlineType.None, null, 0, 0);

    private static Voice TwoMeasures(int p0, int p1) =>
        new Voice("v", ImmutableArray.Create(Meas(Note(p0)), Meas(Note(p1))));

    private static OttavaBracketItem Bracket(OttavaType type, int start, int end) =>
        new OttavaBracketItem(type, start, end, 0, StaffIndex: 0);

    private static int Pos(Voice v, int measure, int item) =>
        ((NoteItem)v.Measures[measure].Items[item]).StaffPosition;

    [Fact]
    public void Transpose_NoBrackets_ReturnsSameReference()
    {
        var voice = TwoMeasures(4, 4);
        var result = OttavaTransposer.Transpose(voice, Array.Empty<OttavaBracketItem>());
        Assert.Same(voice, result); // byte-identical for non-ottava scores
    }

    [Fact]
    public void Transpose_8va_ShiftsCoveredMeasureDownOneOctave_LeavesOthers()
    {
        // 8va covers measure 0 only; measure 1 (loco) is untouched.
        var voice = TwoMeasures(10, 10);
        var result = OttavaTransposer.Transpose(voice, new[] { Bracket(OttavaType.Ottava8va, 0, 0) });

        Assert.Equal(3, Pos(result, 0, 0));   // 10 - 7 (one octave lower on the page)
        Assert.Equal(10, Pos(result, 1, 0));  // outside the span: unchanged
    }

    [Fact]
    public void Transpose_8vb_ShiftsUpOneOctave()
    {
        var voice = TwoMeasures(-3, -3);
        var result = OttavaTransposer.Transpose(voice, new[] { Bracket(OttavaType.Ottava8vb, 0, 0) });
        Assert.Equal(4, Pos(result, 0, 0));   // -3 + 7
    }

    [Fact]
    public void Transpose_15ma_ShiftsDownTwoOctaves()
    {
        var voice = TwoMeasures(14, 14);
        var result = OttavaTransposer.Transpose(voice, new[] { Bracket(OttavaType.Quindicesima15ma, 0, 0) });
        Assert.Equal(0, Pos(result, 0, 0));   // 14 - 14
    }

    [Fact]
    public void Transpose_LeavesMidiUntouched()
    {
        // The whole point: DISPLAY shifts, SOUND does not. Midi is preserved so
        // MIDI/MusicXML export is unaffected.
        var voice = new Voice("v", ImmutableArray.Create(Meas(Note(10, midi: 84))));
        var result = OttavaTransposer.Transpose(voice, new[] { Bracket(OttavaType.Ottava8va, 0, 0) });

        var note = (NoteItem)result.Measures[0].Items[0];
        Assert.Equal(3, note.StaffPosition); // display moved
        Assert.Equal(84, note.Midi);         // sound unchanged
    }

    [Fact]
    public void Transpose_Chord_ShiftsEveryNote()
    {
        var notes = ImmutableArray.Create(
            new ChordNoteInfo(0, null, false),
            new ChordNoteInfo(4, null, false));
        var chord = new ChordItem(notes, Fraction.Quarter, 0, 0);
        var voice = new Voice("v", ImmutableArray.Create(Meas(chord)));

        var result = OttavaTransposer.Transpose(voice, new[] { Bracket(OttavaType.Ottava8va, 0, 0) });

        var shifted = (ChordItem)result.Measures[0].Items[0];
        Assert.Equal(-7, shifted.Notes[0].StaffPosition); // 0 - 7
        Assert.Equal(-3, shifted.Notes[1].StaffPosition); // 4 - 7
    }

    [Fact]
    public void Transpose_SpanningTwoMeasures_ShiftsBoth()
    {
        var voice = TwoMeasures(10, 12);
        var result = OttavaTransposer.Transpose(voice, new[] { Bracket(OttavaType.Ottava8va, 0, 1) });
        Assert.Equal(3, Pos(result, 0, 0));  // 10 - 7
        Assert.Equal(5, Pos(result, 1, 0));  // 12 - 7
    }
}
