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

using System.Collections.Generic;
using System.Linq;
using LilySharp.Core.Midi;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// rit./rall./accel. and the fermata move the time (docs/expressive-tempo-design.md; grammar
/// evaluation 5's B1, owner's decision 2026-11-23: the tempo ones first, a fermata held 1.5
/// times). MEASURED before: the tempo map held one tempo through `@rit … @!rit`, and a fermata
/// only rang its own note half again while the other parts went on.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ExpressiveTempoTests
{
    // At 60 quarters a minute a quarter is 1,000,000 µs.
    private const int Beat = 1_000_000;

    private static string Book(string music, string other = "") => $$"""
        octave absolute
        tempo 4 = 60
        part a { instrument flute }
        {{(other.Length > 0 ? "part b { instrument oboe }" : "")}}
        section S { a { {{music}} } {{(other.Length > 0 ? "b { " + other + " }" : "")}} }
        form { S }
        score { staff a {{(other.Length > 0 ? "staff b" : "")}} }
        """;

    private static MidiFile Export(string src) => new MidiExporter().Export(SyntaxTree.Parse(src));

    private static int TempoAt(MidiFile midi, int tick)
    {
        int tempo = 0;
        foreach (var change in midi.Tracks[0].TempoChanges)
            if (change.Tick <= tick) tempo = change.MicrosecondsPerBeat;
        return tempo;
    }

    [Fact]
    public void ARitSlowsToSeventyPercentAndTheTempoComesBack()
    {
        // Two bars of quarters under the line, then a bar after it.
        var midi = Export(Book("c''4@rit d'' e'' f'' | g''4 a'' b'' c'''4@!rit | c''1 |"));
        int q = midi.TicksPerQuarterNote;
        Assert.Equal(Beat, TempoAt(midi, 0));                                         // the tempo it began at
        Assert.InRange(TempoAt(midi, 8 * q - 1), Beat / 0.72, Beat / 0.68);           // ~70% at its end
        Assert.True(TempoAt(midi, 4 * q) > TempoAt(midi, 2 * q));                     // slowing all along
        Assert.Equal(Beat, TempoAt(midi, 8 * q));                                     // a tempo after it
    }

    [Fact]
    public void AnAccelSpeedsUp()
    {
        var midi = Export(Book("c''4@accel d'' e'' f''@!accel | c''1 |"));
        int q = midi.TicksPerQuarterNote;
        Assert.InRange(TempoAt(midi, 4 * q - 1), Beat / 1.32, Beat / 1.28);
        Assert.Equal(Beat, TempoAt(midi, 4 * q));
    }

    [Fact]
    public void ATempoWrittenWhereTheRitEndsStands()
    {
        var midi = Export(Book("c''4@rit d'' e'' f''@!rit | tempo 4 = 50 c''1 |"));
        Assert.Equal(1_200_000, TempoAt(midi, 4 * midi.TicksPerQuarterNote));
    }

    [Fact]
    public void AFermataHoldsTheTimeHalfAgain_AndItsNoteIsAsWritten()
    {
        var midi = Export(Book("c''4 d''2@fermata e''4 | c''1 |"));
        int q = midi.TicksPerQuarterNote;
        Assert.Equal(Beat, TempoAt(midi, 0));
        Assert.Equal(Beat * 3 / 2, TempoAt(midi, q));
        Assert.Equal(Beat * 3 / 2, TempoAt(midi, 3 * q - 1));
        Assert.Equal(Beat, TempoAt(midi, 3 * q));
        var d = midi.Tracks.SelectMany(t => t.Notes).Single(n => n.Pitch == 74);
        Assert.Equal(2 * q, d.DurationTicks);
    }

    [Fact]
    public void AFermataOnARestHoldsTheTimeToo()
    {
        var midi = Export(Book("c''4 r2@fermata e''4 |"));
        Assert.Equal(Beat * 3 / 2, TempoAt(midi, midi.TicksPerQuarterNote));
    }

    /// <summary>However the fermata reaches the onset — a chord member's, an empty chord's handed on.</summary>
    [Theory]
    [InlineData("c''4 <e'' g''@fermata>4 r2 |")]
    [InlineData("c''4 <>@fermata d''4 r2 |")]
    public void AFermataOnAMemberOrAnEmptyChordHoldsTheTime(string music)
        => Assert.Equal(Beat * 3 / 2, TempoAt(Export(Book(music)), Export(Book(music)).TicksPerQuarterNote));

    [Fact]
    public void TheSameFermataInTwoPartsHoldsOnce()
    {
        // One part holds a half note, the other a quarter: one fermata, as long as the longest note.
        var midi = Export(Book("c''4 d''2@fermata e''4 |", "e'4 f'4@fermata g'2 |"));
        int q = midi.TicksPerQuarterNote;
        Assert.Equal(Beat * 3 / 2, TempoAt(midi, q));
        Assert.Equal(Beat * 3 / 2, TempoAt(midi, 3 * q - 1));
        Assert.Equal(Beat, TempoAt(midi, 3 * q));
    }

    [Fact]
    public void ARitInARepeatIsPlayedOnEachPass()
    {
        var midi = Export(Book("|: c''4@rit d'' e'' f''@!rit :| c''1 |"));
        int q = midi.TicksPerQuarterNote;
        Assert.True(TempoAt(midi, 4 * q - 1) > Beat * 1.3);    // the first pass's end
        Assert.Equal(Beat, TempoAt(midi, 4 * q));               // the second pass starts a tempo
        Assert.True(TempoAt(midi, 8 * q - 1) > Beat * 1.3);    // and slows again
    }

    [Fact]
    public void AnUnclosedRitMovesNothing()
    {
        var midi = Export(Book("c''4@rit d'' e'' f'' | c''1 |"));
        Assert.All(midi.Tracks[0].TempoChanges, c => Assert.Equal(Beat, c.MicrosecondsPerBeat));
    }
}
