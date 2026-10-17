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

using LilySharp.Lsp.MidiOut;
using StreamJsonRpc;

namespace LilySharp.Lsp;

/// <summary>
/// MIDI out (docs/playback-design.md §6, stage B; owner's decision 2026-10-18): the preview's
/// Play, Alt+P / Alt+M and the auditions sound through a MIDI output port of this machine
/// when <c>lilysharp.playback.output</c> names one — the Microsoft GS Wavetable Synth, a
/// loopMIDI or IAC bus into a DAW or a VST host, an interface. The webview still decides
/// what plays and keeps the follow-along highlight; the server holds the port and the clock
/// (<see cref="MidiOutPlayer"/>). Every error is an answer, never an exception: a port that
/// is not there makes the preview play its own synth, with the sentence in the status bar.
/// </summary>
public sealed partial class LilySharpLanguageServer
{
    private readonly MidiOutPlayer _midiOut = new();

    /// <summary>The player, for the nets.</summary>
    internal MidiOutPlayer MidiOut => _midiOut;

    [JsonRpcMethod("lilysharp/midiOut/ports", UseSingleObjectParameterDeserialization = true)]
    public Task<MidiOutPortsResponse> MidiOutPortsAsync(MidiOutPortsParams? @params, CancellationToken token)
        => OffDispatch(MidiOutPortList, token);

    public MidiOutPortsResponse MidiOutPortList()
    {
        try
        {
            return new MidiOutPortsResponse { Ports = [.. MidiOutPorts.Names()] };
        }
        catch (Exception ex)
        {
            // DllNotFoundException on a Linux without libasound2, and whatever else the OS says.
            return new MidiOutPortsResponse { Error = ex.Message };
        }
    }

    [JsonRpcMethod("lilysharp/midiOut/play", UseSingleObjectParameterDeserialization = true)]
    public Task<MidiOutPlayResponse> MidiOutPlayAsync(MidiOutPlayParams @params, CancellationToken token)
        => OffDispatch(() => MidiOutPlay(@params), token);

    public MidiOutPlayResponse MidiOutPlay(MidiOutPlayParams @params)
    {
        try
        {
            var (startedAt, end) = _midiOut.Play(@params.Port, @params.Notes, @params.Offset);
            return new MidiOutPlayResponse { Started = true, StartedAtMs = startedAt, EndSeconds = end };
        }
        catch (Exception ex)
        {
            return new MidiOutPlayResponse { Error = ex.Message };
        }
    }

    [JsonRpcMethod("lilysharp/midiOut/stop", UseSingleObjectParameterDeserialization = true)]
    public Task<MidiOutResponse> MidiOutStopAsync(MidiOutStopParams? @params, CancellationToken token)
        => OffDispatch(MidiOutStop, token);

    public MidiOutResponse MidiOutStop()
    {
        try
        {
            _midiOut.Stop();
            return new MidiOutResponse();
        }
        catch (Exception ex)
        {
            return new MidiOutResponse { Error = ex.Message };
        }
    }

    [JsonRpcMethod("lilysharp/midiOut/strike", UseSingleObjectParameterDeserialization = true)]
    public Task<MidiOutResponse> MidiOutStrikeAsync(MidiOutStrikeParams @params, CancellationToken token)
        => OffDispatch(() => MidiOutStrike(@params), token);

    public MidiOutResponse MidiOutStrike(MidiOutStrikeParams @params)
    {
        try
        {
            return new MidiOutResponse { Channel = _midiOut.Strike(@params.Port, @params.Pitches, @params.Timbre, @params.Program) };
        }
        catch (Exception ex)
        {
            return new MidiOutResponse { Error = ex.Message };
        }
    }

    [JsonRpcMethod("lilysharp/midiOut/release", UseSingleObjectParameterDeserialization = true)]
    public Task<MidiOutResponse> MidiOutReleaseAsync(MidiOutReleaseParams @params, CancellationToken token)
        => OffDispatch(() => MidiOutRelease(@params), token);

    public MidiOutResponse MidiOutRelease(MidiOutReleaseParams @params)
    {
        try
        {
            _midiOut.Release(@params.Channel, @params.Pitches);
            return new MidiOutResponse { Channel = @params.Channel };
        }
        catch (Exception ex)
        {
            return new MidiOutResponse { Error = ex.Message };
        }
    }

    /// <summary>Silences and closes the MIDI output: the server is going down (Program.cs),
    /// so no note is left ringing on an external synth.</summary>
    public void DisposeMidiOut() => _midiOut.Dispose();
}
