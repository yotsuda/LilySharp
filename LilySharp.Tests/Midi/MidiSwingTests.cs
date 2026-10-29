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

using LilySharp.Core.Midi;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests.Midi;

/// <summary>
/// A tempo mark's swing equation is played, not only printed (owner's request 2026-10-29: a jazz
/// trio's `tempo "Medium Swing" 4 = 146 swing` played straight eighths): a note on the second of a
/// pair of the swung value moves to the third of a triplet over the pair, as the equation reads.
/// </summary>
public class MidiSwingTests
{
    private static List<MidiNote> Notes(string music, string tempo = "tempo 120 swing", string time = "4/4")
    {
        var src = $"octave absolute\ntime {time}\n{tempo}\npart p {{ clef treble }}\nsection A {{ p {{ {music} }} }}\nform {{ A }}";
        var midi = new MidiExporter().Export(SyntaxTree.Parse(src));
        return midi.Tracks.SelectMany(t => t.Notes).OrderBy(n => n.StartTick).ToList();
    }

    private static int Q => new MidiExporter().Export(SyntaxTree.Parse("part p { clef treble }\nsection A { p { c'4 | } }\nform { A }")).TicksPerQuarterNote;

    [Fact]
    public void SwungEighths_SoundLongShort_AsATripletOverTheBeat()
    {
        var notes = Notes("c'8 d' e' f' g'2 |");
        Assert.Equal(new[] { 0, Q * 2 / 3, Q, Q + Q * 2 / 3, 2 * Q }, notes.Select(n => n.StartTick));
        // The first of a pair lasts to the second's new place, the second to the beat.
        Assert.Equal(Q * 2 / 3, notes[0].DurationTicks);
        Assert.Equal(Q, notes[1].StartTick + notes[1].DurationTicks);
    }

    [Fact]
    public void WithoutTheMark_EighthsStayStraight()
        => Assert.Equal(new[] { 0, Q / 2, Q, Q + Q / 2, 2 * Q }, Notes("c'8 d' e' f' g'2 |", "tempo 120").Select(n => n.StartTick));

    [Fact]
    public void ATripletAndTheBeat_KeepTheirTime()
    {
        var notes = Notes("tuplet 3/2 { c'8 d' e' } f'4 g'2 |");
        Assert.Equal(new[] { 0, Q / 3, Q * 2 / 3, Q, 2 * Q }, notes.Select(n => n.StartTick));
    }

    [Fact]
    public void Swing16_SwingsTheSixteenths_NotTheEighths()
    {
        var notes = Notes("c'16 d' e' f' g'8 a' b'2 |", "tempo 80 swing 16");
        int e = Q / 2;
        Assert.Equal(new[] { 0, e * 2 / 3, e, e + e * 2 / 3, Q, Q + e, 2 * Q }, notes.Select(n => n.StartTick));
    }

    [Fact]
    public void APickup_CountsItsPairsFromTheBarLine()
    {
        // An eighth pickup: the bar's first note is on the beat, its second an eighth after.
        var notes = Notes("partial 8 g'8 | c'8 d' e'4 f'2 |");
        int bar = notes[1].StartTick;
        Assert.Equal(new[] { bar, bar + Q * 2 / 3, bar + Q }, notes.Skip(1).Take(3).Select(n => n.StartTick));
    }
}
