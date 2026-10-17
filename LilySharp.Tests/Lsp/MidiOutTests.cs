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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using LilySharp.Lsp;
using LilySharp.Lsp.MidiOut;
using Xunit;

namespace LilySharp.Tests.Lsp;

/// <summary>
/// MIDI out (docs/playback-design.md §6, stage B): the note list the preview plays becomes
/// the channel messages a port is sent — the same events the webview's <c>toEvents</c> makes
/// (test/playbackCore.test.ts, mirrored here case for case) — and the player sends them as
/// their time comes, silences on a stop, and answers every failure as an error, never an
/// exception. The port is a recorder: no device is needed, and none is on the CI machines.
/// </summary>
public class MidiOutTests
{
    private static PlaybackNote Note(double t, double d, double p, int c = 0, int g = 0, int i = 0, int v = 90)
        => new() { T = t, D = d, P = p, V = v, S = (int)(10 * t), O = 0, I = i, C = c, G = g };

    private static string Kind(MidiOutEvent e) => (e.Status & 0xF0) switch
    {
        MidiOutEvents.NoteOn => "on",
        MidiOutEvents.NoteOff => "off",
        MidiOutEvents.Program => "program",
        MidiOutEvents.PitchBend => "bend",
        MidiOutEvents.Control => "control",
        _ => "?",
    };

    private static int BendValue(MidiOutEvent e) => e.Data1 | (e.Data2 << 7);

    // ---- the events (MidiOutEvents.From) ----

    [Fact]
    public void EachPitchedChannelsProgram_IsSetOnceFirst_AndDrumsGetNone()
    {
        var events = MidiOutEvents.From(
        [
            Note(0, 1, 60, c: 0, g: 40),
            Note(0, 1, 36, c: MidiOutEvents.DrumChannel, i: 9),
            Note(1, 1, 64, c: 1, g: 73),
            Note(2, 1, 62, c: 0, g: 40),
        ], 0);
        var programs = events.Where(e => Kind(e) == "program").Select(e => (e.Channel, (int)e.Data1)).ToList();
        Assert.Equal([(0, 40), (1, 73)], programs);
        Assert.Equal(["program", "program"], events.Take(2).Select(Kind));
        Assert.DoesNotContain(events, e => Kind(e) == "program" && e.Channel == MidiOutEvents.DrumChannel);
    }

    [Fact]
    public void ANotesOff_ComesBeforeItsReStrike_AtTheSameInstant()
    {
        var events = MidiOutEvents.From([Note(0, 1, 60), Note(1, 1, 60)], 0);
        Assert.Equal(["off", "on"], events.Where(e => e.Time == 1).Select(Kind));
    }

    [Fact]
    public void NotesBeforeTheOffset_AreDroppedWhole_AndTheRestTimedFromIt()
    {
        var notes = new[] { Note(0, 1, 60), Note(1, 1, 62), Note(2, 1, 64) };
        var ons = MidiOutEvents.From(notes, 1).Where(e => Kind(e) == "on").Select(e => (e.Time, (int)e.Data1)).ToList();
        Assert.Equal([(0.0, 62), (1.0, 64)], ons);
        Assert.Equal(3, MidiOutEvents.EndOf(notes, 1));
    }

    [Fact]
    public void AQuarterTone_BendsTheWheelUpFromTheKeyBelow_AndCentresItAfter()
    {
        var events = MidiOutEvents.From([Note(0, 1, 60.5)], 0);
        var bends = events.Where(e => Kind(e) == "bend").Select(e => (e.Time, BendValue(e))).ToList();
        Assert.Equal([(0.0, MidiOutEvents.BendCentre + 2048), (1.0, MidiOutEvents.BendCentre)], bends);
        var kinds = events.Select(Kind).ToList();
        Assert.True(kinds.IndexOf("bend") < kinds.IndexOf("on"));
        Assert.Equal(60, events.First(e => Kind(e) == "on").Data1);
    }

    [Fact]
    public void AnUnnamedChannel_TakesTheTimbresProgram_AndTheDrumFamilyTheDrumChannel()
    {
        Assert.Equal(MidiOutEvents.DrumChannel, MidiOutEvents.ChannelOf(-1, 9));
        Assert.Equal(0, MidiOutEvents.ChannelOf(-1, 3));
        Assert.Equal(48, MidiOutEvents.ProgramOf(-1, 3));
        Assert.Equal(40, MidiOutEvents.ProgramOf(40, 3));
        // The pitched channels in order, the drums on the drum channel; what a stop silences.
        var events = MidiOutEvents.From([Note(0, 1, 60, c: 2), Note(0, 1, 36, c: -1, i: 9)], 0);
        Assert.Equal([2, 9], MidiOutEvents.ChannelsOf(events));
    }

    [Fact]
    public void AShortNote_LastsThirtyMilliseconds_AndTheVelocityIsAtLeastOne()
    {
        var events = MidiOutEvents.From([Note(0, 0.001, 60, v: 0)], 0);
        Assert.Equal(0.03, events.First(e => Kind(e) == "off").Time);
        Assert.Equal(1, events.First(e => Kind(e) == "on").Data2);
    }

    // ---- the player (MidiOutPlayer), on a recording port ----

    private sealed class Recorder : IMidiOutPort
    {
        public readonly List<(double At, byte Status, byte D1, byte D2)> Sent = [];
        public readonly Stopwatch Clock = Stopwatch.StartNew();
        public bool Disposed;
        public string Name { get; }
        public Recorder(string name) { Name = name; }
        public void Send(byte status, byte data1, byte data2)
        {
            lock (Sent) Sent.Add((Clock.Elapsed.TotalSeconds, status, data1, data2));
        }
        public void Dispose() => Disposed = true;
    }

    private static (MidiOutPlayer Player, List<Recorder> Opened) PlayerWithRecorder()
    {
        var opened = new List<Recorder>();
        var player = new MidiOutPlayer(name =>
        {
            if (name == "gone") throw new InvalidOperationException("MIDI output \"gone\" is not there");
            var r = new Recorder(name);
            opened.Add(r);
            return r;
        });
        return (player, opened);
    }

    [Fact]
    public void Play_SendsTheProgramThenTheNotes_InOrder_NeverEarly_AndTheEndIsTheLastOff()
    {
        var (player, opened) = PlayerWithRecorder();
        var (startedAt, end) = player.Play("Recorder", [Note(0, 0.05, 60, g: 40), Note(0.08, 0.05, 62, g: 40)], 0);
        Assert.Equal(0.13, end, 6);
        Assert.InRange(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - startedAt, 0, 2000);
        var port = Assert.Single(opened);
        SpinWait.SpinUntil(() => { lock (port.Sent) return port.Sent.Count >= 5; }, 3000);
        List<(double At, byte Status, byte D1, byte D2)> sent;
        lock (port.Sent) sent = [.. port.Sent];
        Assert.Equal(5, sent.Count);
        Assert.Equal((byte)0xC0, sent[0].Status);
        Assert.Equal(40, sent[0].D1);
        Assert.Equal([(0x90, 60), (0x80, 60), (0x90, 62), (0x80, 62)],
            sent.Skip(1).Select(s => ((int)s.Status, (int)s.D1)));
        // The clock: each event no earlier than its time (the recorder's clock started a
        // hair before the player's), and the whole within a generous second.
        var scheduled = new[] { 0, 0, 0.05, 0.08, 0.13 };
        for (int i = 0; i < scheduled.Length; i++)
            Assert.True(sent[i].At >= scheduled[i] - 0.002 && sent[i].At < scheduled[i] + 1.0,
                $"event {i} at {sent[i].At:F4}s for {scheduled[i]}s");
        player.Dispose();
    }

    [Fact]
    public void Stop_SilencesEveryChannelTouched_AndWakesTheThreadAtOnce()
    {
        var (player, opened) = PlayerWithRecorder();
        player.Play("Recorder", [Note(0, 0.02, 60, c: 3), Note(5, 1, 62, c: 3), Note(5, 1, 36, c: 9, i: 9)], 0);
        var port = opened[0];
        SpinWait.SpinUntil(() => { lock (port.Sent) return port.Sent.Count >= 3; }, 3000);
        var stopping = Stopwatch.StartNew();
        player.Stop();
        Assert.True(stopping.ElapsedMilliseconds < 1000, "the stop waited for the thread's sleep");
        List<(double At, byte Status, byte D1, byte D2)> sent;
        lock (port.Sent) sent = [.. port.Sent];
        // The program, the first note on and off; then the silence on 3 and 9 and nothing of the 5 s notes.
        var silence = sent.Skip(3).ToList();
        Assert.Equal([0xB3, 0xB3, 0xE3, 0xB9, 0xB9, 0xE9], silence.Select(s => (int)s.Status));
        Assert.Equal([123, 120], silence.Take(2).Select(s => (int)s.D1));
        Assert.Equal(MidiOutEvents.BendCentre, silence[2].D1 | (silence[2].D2 << 7));
        Assert.DoesNotContain(sent, s => s.Status == 0x93 && s.D1 == 62);
        player.Dispose();
    }

    [Fact]
    public void APlay_OnAnotherPort_ClosesTheFirst_AndOnTheSame_KeepsIt()
    {
        var (player, opened) = PlayerWithRecorder();
        player.Play("A", [Note(0, 0.01, 60)], 0);
        player.Play("A", [Note(0, 0.01, 60)], 0);
        Assert.Single(opened);
        player.Play("B", [Note(0, 0.01, 60)], 0);
        Assert.Equal(2, opened.Count);
        Assert.True(opened[0].Disposed);
        Assert.Equal("B", player.PortName);
        player.Dispose();
        Assert.True(opened[1].Disposed);
        Assert.Null(player.PortName);
    }

    [Fact]
    public void AStrike_SetsTheProgramOnTheAuditionChannel_AndTheReleaseLetsGo_DrumsOnTheirChannel()
    {
        var (player, opened) = PlayerWithRecorder();
        int c = player.Strike("Recorder", [60.0, 64.4], timbre: 3, program: -1);
        Assert.Equal(MidiOutEvents.AuditionChannel, c);
        player.Release(c, [60.0, 64.4]);
        int d = player.Strike("Recorder", [36.0], timbre: 9, program: -1);
        Assert.Equal(MidiOutEvents.DrumChannel, d);
        var sent = opened[0].Sent.Select(s => ((int)s.Status, (int)s.D1, (int)s.D2)).ToList();
        Assert.Equal([(0xCF, 48, 0), (0x9F, 60, 100), (0x9F, 64, 100), (0x8F, 60, 0), (0x8F, 64, 0), (0x99, 36, 100)], sent);
        player.Dispose();
    }

    // ---- the machine's own port, where there is one ----

    /// <summary>
    /// The one net that touches the OS: on a Windows machine the Microsoft GS Wavetable Synth
    /// is always listed, so the winmm P/Invoke (WinMmPort) is exercised for real — opened,
    /// sent a program, three short notes, a strike and its release, silenced and closed — in
    /// under half a second. Elsewhere, or without it, the net passes on nothing: the
    /// CoreMIDI and ALSA ports are not reachable from a CI runner.
    /// </summary>
    [Fact]
    public void TheMachinesOwnSynth_WhereThereIsOne_OpensPlaysAndCloses()
    {
        if (!OperatingSystem.IsWindows()) return;
        var names = MidiOutPorts.Names();
        const string gs = "Microsoft GS Wavetable Synth";
        if (!names.Contains(gs)) return;
        using var player = new MidiOutPlayer();
        var (startedAt, end) = player.Play(gs, [Note(0, 0.05, 60, g: 0), Note(0.06, 0.05, 64), Note(0.12, 0.05, 67)], 0);
        Assert.Equal(0.17, end, 6);
        Assert.Equal(gs, player.PortName);
        Thread.Sleep(220);
        Assert.Equal(MidiOutEvents.AuditionChannel, player.Strike(gs, [72.0], timbre: 0, program: 0));
        player.Release(MidiOutEvents.AuditionChannel, [72.0]);
        player.Stop();
        // A port that is not there, on this OS's own list — the sentence names what is.
        var ex = Assert.Throws<InvalidOperationException>(() => MidiOutPorts.Open("no such port — lily# test"));
        Assert.Contains(gs, ex.Message);
    }

    // ---- the requests ----

    private static LilySharpLanguageServer Server() => new(Stream.Null, Stream.Null);

    [Fact]
    public void ThePortList_IsAnAnswer_WhateverTheMachineHas()
    {
        var response = Server().MidiOutPortList();
        // On a machine with no MIDI API in reach the error is the answer; otherwise a list,
        // possibly empty (a CI runner). Never a throw.
        Assert.True(response.Error != null || response.Ports != null);
    }

    [Fact]
    public void APortThatIsNotThere_IsAnError_NotAnException_AndNothingIsStarted()
    {
        var server = Server();
        var play = server.MidiOutPlay(new MidiOutPlayParams
        {
            Port = "no such port — lily# test", Notes = [Note(0, 1, 60)], Offset = 0,
        });
        Assert.False(play.Started);
        Assert.Contains("no such port", play.Error);
        Assert.Null(server.MidiOut.PortName);
        var strike = server.MidiOutStrike(new MidiOutStrikeParams { Port = "no such port — lily# test", Pitches = [60] });
        Assert.Equal(-1, strike.Channel);
        Assert.NotNull(strike.Error);
        Assert.Null(server.MidiOutStop().Error);
        Assert.Null(server.MidiOutRelease(new MidiOutReleaseParams { Channel = 15, Pitches = [60] }).Error);
        server.DisposeMidiOut();
    }
}
