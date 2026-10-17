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

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LilySharp.Lsp.MidiOut;

/// <summary>
/// The language server's MIDI player (docs/playback-design.md §6, stage B): one open port
/// at a time — the one <c>lilysharp.playback.output</c> names, reopened when the name
/// changes — a playback thread that sends the note list's events as their time comes, and
/// the auditions' strikes. Windows plays through the Microsoft GS Wavetable Synth with no
/// setup; loopMIDI / an IAC bus carry the stream to a DAW or a VST host, where the user's
/// own instruments are. The webview keeps the follow-along highlight from the start time
/// this returns (Unix milliseconds — the same wall clock as the page's <c>Date.now()</c>).
/// </summary>
public sealed class MidiOutPlayer : IDisposable
{
    private readonly Func<string, IMidiOutPort> _open;
    private readonly object _gate = new();
    private IMidiOutPort? _port;
    private Playback? _playback;
    private readonly HashSet<int> _struck = [];

    public MidiOutPlayer() : this(MidiOutPorts.Open) { }

    /// <summary>With the port factory given — the nets hand one that records what is sent.</summary>
    public MidiOutPlayer(Func<string, IMidiOutPort> open)
    {
        _open = open;
    }

    /// <summary>The open port's name, or null.</summary>
    public string? PortName { get { lock (_gate) return _port?.Name; } }

    /// <summary>The port named, opened if it is not the open one (the open one is silenced
    /// and closed first). Throws when it cannot be opened.</summary>
    private IMidiOutPort PortNamed(string name)
    {
        if (_port != null && _port.Name == name) return _port;
        ClosePort();
        _port = _open(name);
        return _port;
    }

    private void ClosePort()
    {
        if (_port == null) return;
        try { _port.Dispose(); } catch { /* a port already gone */ }
        _port = null;
    }

    /// <summary>Plays <paramref name="notes"/> from <paramref name="offset"/> seconds into
    /// the piece on the port named, stopping what was playing. Returns when the playback's
    /// zero is on the wall clock (Unix ms) and when it ends (seconds into the piece).</summary>
    public (long StartedAtMs, double EndSeconds) Play(string portName, IReadOnlyList<PlaybackNote> notes, double offset)
    {
        var events = MidiOutEvents.From(notes, offset);
        double end = MidiOutEvents.EndOf(notes, offset);
        lock (_gate)
        {
            StopPlayback();
            var port = PortNamed(portName);
            var playback = new Playback(port, events);
            _playback = playback;
            playback.Start();
            return (playback.StartedAtMs, end);
        }
    }

    /// <summary>Stops the playback, if one is on, and silences its channels.</summary>
    public void Stop()
    {
        lock (_gate) StopPlayback();
    }

    private void StopPlayback()
    {
        var playback = _playback;
        _playback = null;
        playback?.Stop();
    }

    /// <summary>An audition: strikes <paramref name="pitches"/> (MIDI numbers, rounded)
    /// with a program (the part's, or -1 for the timbre family's) on the audition channel —
    /// the drum family (timbre 9) on the drum channel. Returns the channel struck, for
    /// <see cref="Release"/>.</summary>
    public int Strike(string portName, IReadOnlyList<double> pitches, int timbre, int program, int velocity = 100)
    {
        lock (_gate)
        {
            var port = PortNamed(portName);
            bool drums = timbre == 9;
            int c = drums ? MidiOutEvents.DrumChannel : MidiOutEvents.AuditionChannel;
            if (!drums)
                port.Send((byte)(MidiOutEvents.Program | c), (byte)MidiOutEvents.ProgramOf(program, timbre), 0);
            foreach (var p in pitches)
                port.Send((byte)(MidiOutEvents.NoteOn | c), Key(p), (byte)Math.Clamp(velocity, 1, 127));
            _struck.Add(c);
            return c;
        }
    }

    public void Release(int channel, IReadOnlyList<double> pitches)
    {
        lock (_gate)
        {
            if (_port == null || channel is < 0 or > 15) return;
            foreach (var p in pitches)
                _port.Send((byte)(MidiOutEvents.NoteOff | channel), Key(p), 0);
        }
    }

    private static byte Key(double pitch) => (byte)Math.Clamp((int)Math.Round(pitch), 0, 127);

    /// <summary>Silences everything and closes the port: the server is going down, or the
    /// setting turned the output off.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            StopPlayback();
            if (_port != null)
            {
                foreach (var c in _struck)
                    foreach (var e in MidiOutEvents.Silence(c))
                        try { _port.Send(e.Status, e.Data1, e.Data2); } catch { /* going down */ }
                _struck.Clear();
            }
            ClosePort();
        }
    }

    /// <summary>
    /// One playback: a thread that sleeps until each event's time and sends it. Sleeps to
    /// about 2 ms before the time and spins the rest (a timer's grain is 1–16 ms; a note 10
    /// ms late is heard); on Windows the timer period is raised to 1 ms for the playback's
    /// duration (<c>timeBeginPeriod</c>, as every MIDI player does). A stop wakes the thread
    /// at once and silences every channel the events touched.
    /// </summary>
    private sealed class Playback
    {
        private readonly IMidiOutPort _port;
        private readonly List<MidiOutEvent> _events;
        private readonly ManualResetEventSlim _stop = new(false);
        private readonly Stopwatch _clock = new();
        private readonly Thread _thread;
        public long StartedAtMs { get; private set; }
        public string? Error { get; private set; }

        public Playback(IMidiOutPort port, List<MidiOutEvent> events)
        {
            _port = port;
            _events = events;
            _thread = new Thread(Run) { IsBackground = true, Name = "Lily# MIDI out", Priority = ThreadPriority.AboveNormal };
        }

        public void Start()
        {
            StartedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _clock.Start();
            _thread.Start();
        }

        public void Stop()
        {
            _stop.Set();
            if (Thread.CurrentThread != _thread) _thread.Join(500);
            try
            {
                foreach (var c in MidiOutEvents.ChannelsOf(_events))
                    foreach (var e in MidiOutEvents.Silence(c))
                        _port.Send(e.Status, e.Data1, e.Data2);
            }
            catch (Exception ex) { Error ??= ex.Message; }
        }

        private void Run()
        {
            bool periodRaised = OperatingSystem.IsWindows() && timeBeginPeriod(1) == 0;
            try
            {
                foreach (var e in _events)
                {
                    if (!WaitUntil(e.Time)) return;
                    _port.Send(e.Status, e.Data1, e.Data2);
                }
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                Console.Error.WriteLine($"MIDI out: {ex.Message}");
            }
            finally
            {
                if (periodRaised) timeEndPeriod(1);
            }
        }

        /// <summary>False when stopped before the time came.</summary>
        private bool WaitUntil(double seconds)
        {
            while (true)
            {
                double remaining = seconds - _clock.Elapsed.TotalSeconds;
                if (remaining <= 0) return !_stop.IsSet;
                if (remaining > 0.003)
                {
                    if (_stop.Wait(TimeSpan.FromSeconds(remaining - 0.002))) return false;
                }
                else
                {
                    if (_stop.IsSet) return false;
                    Thread.SpinWait(100);
                }
            }
        }

        [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint ms);
        [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint ms);
    }
}
