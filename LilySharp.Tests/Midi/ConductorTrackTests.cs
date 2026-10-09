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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LilySharp.Core.Midi;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests.Midi;

/// <summary>
/// The conductor track's hygiene: one meter and one tempo event per moment, and every
/// track's end-of-track at the piece's end — what LilyPond's performers and walker write
/// (time-signature-performer.cc, tempo-performer.cc, midi-walker.cc Midi_walker::finalize).
/// </summary>
/// <remarks>
/// Measured 2026-10-03 on the owner's five-part book: every meter change stood five times
/// at one tick (each part's walk through a section added the section's meter), the file
/// opened with two tempo events at tick 0 (the exporter's default 120 and the book's 72),
/// and the conductor track ended 30,720 ticks before the music did. Harmless to a player,
/// untrue as a file.
/// </remarks>
public class ConductorTrackTests
{
    private const string FiveParts = """
        tempo 4 = 72
        time 4/4
        key c major
        part a { clef treble }
        part b { clef treble }
        part c { clef alto }
        part d { clef bass }
        part e { clef bass }
        section A { time 9/8 }
        section A {
          a { c'4. d' e' | time 4/4 f'1 | }
          b { e'4. f' g' | time 4/4 a'1 | }
          c { g4. a b | time 4/4 c'1 | }
          d { c4. d e | time 4/4 f1 | }
          e { c,4. d, e, | time 4/4 f,1 | }
        }
        section B {
          a { g'1 | g'1 | }
          b { e'1 | e'1 | }
          c { c'1 | c'1 | }
          d { c1 | c1 | }
          e { c,1 | r1 | }
        }
        form { A B }
        score { staff a staff b staff c staff d staff e }
        """;

    private static MidiFile Export(string source) => new MidiExporter().Export(SyntaxTree.Parse(source));

    [Fact]
    public void AMeterChange_IsOneEvent_HoweverManyPartsWalkIt()
    {
        var conductor = Export(FiveParts).Tracks[0];

        var meters = conductor.TimeSignatures.Select(t => (t.Tick, t.Numerator, t.Denominator)).ToList();
        // 9/8 at the head, 4/4 at bar 2 (one bar of 9/8 = 9 × 240 ticks) — once each.
        Assert.Equal([(0, 9, 8), (2160, 4, 4)], meters);
    }

    [Fact]
    public void TheBooksTempo_ReplacesTheDefault_AtTickZero()
    {
        var conductor = Export(FiveParts).Tracks[0];

        var tempo = Assert.Single(conductor.TempoChanges);
        Assert.Equal((0, 833333), (tempo.Tick, tempo.MicrosecondsPerBeat)); // 72 bpm, not 120 and 72
    }

    [Fact]
    public void ATempo_EqualToTheOneInForce_WritesNothing()
    {
        // LilyPond's Tempo_performer announces an Audio_tempo only when the tempo changes.
        var conductor = Export("""
            tempo 4 = 100
            time 4/4
            part m { clef treble }
            section A { m { c'4 d' e' f' | tempo 4 = 100 g'1 | tempo 4 = 120 c'1 | } }
            form { A }
            score { staff m }
            """).Tracks[0];

        Assert.Equal([(0, 600000), (3840, 500000)],
            conductor.TempoChanges.Select(t => (t.Tick, t.MicrosecondsPerBeat)).ToList());
    }

    [Fact]
    public void EveryTrack_EndsWhereThePieceEnds()
    {
        var midi = Export(FiveParts);
        // 1 bar of 9/8 + 1 + 2 bars of 4/4 = 2160 + 3 × 1920.
        Assert.Equal(7920, midi.EndTick);

        using var stream = new MemoryStream();
        midi.WriteTo(stream);
        var ends = EndOfTrackDeltas(stream.ToArray());

        Assert.Equal(midi.Tracks.Count, ends.Count);
        // The conductor track's last event is the meter change at 2160: its end-of-track
        // stands 7920 − 2160 later. Part e rests through the last bar, so its last
        // note-off is a bar early; the others sound to the end.
        Assert.Equal(7920 - 2160, ends[0]);
        Assert.Equal(1920, ends[5]);
        Assert.All(ends.Skip(1).Take(4), delta => Assert.Equal(0, delta));
    }

    // The delta before each track's end-of-track (FF 2F 00), read off the chunk's tail.
    private static List<int> EndOfTrackDeltas(byte[] smf)
    {
        var deltas = new List<int>();
        int pos = 14; // MThd + 6-byte header
        while (pos + 8 <= smf.Length)
        {
            Assert.Equal("MTrk", System.Text.Encoding.ASCII.GetString(smf, pos, 4));
            int length = (smf[pos + 4] << 24) | (smf[pos + 5] << 16) | (smf[pos + 6] << 8) | smf[pos + 7];
            int end = pos + 8 + length;
            Assert.Equal(new byte[] { 0xFF, 0x2F, 0x00 }, smf[(end - 3)..end]);
            // The variable-length delta ends in the byte before FF; its earlier bytes have bit 7 set.
            int first = end - 4;
            while (first - 1 >= pos + 8 && (smf[first - 1] & 0x80) != 0)
                first--;
            int delta = 0;
            for (int i = first; i < end - 3; i++)
                delta = (delta << 7) | (smf[i] & 0x7F);
            deltas.Add(delta);
            pos = end;
        }
        return deltas;
    }
}
