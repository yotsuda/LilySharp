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

using StreamJsonRpc;

namespace LilySharp.Lsp;

/// <summary>Parameters for lilysharp/decodeRecording: the recording beside a chart, and where the
/// editor wants its decoded samples (its own storage — never beside the chart,
/// docs/recording-design.md §1 5).</summary>
public class DecodeRecordingParams
{
    public string Input { get; set; } = "";
    public string Output { get; set; } = "";
}

/// <summary>What lilysharp/decodeRecording did: the float WAV's rate, channels and length, or why
/// it could not (<see cref="Error"/>, in words for the user).</summary>
public class DecodeRecordingResponse
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public int Rate { get; set; }
    public int Channels { get; set; }
    public double Seconds { get; set; }
}

public sealed partial class LilySharpLanguageServer
{
    /// <summary>
    /// Decodes a recording the preview's webview cannot (an m4a: VS Code's Chromium has no AAC)
    /// with Windows' Media Foundation (<see cref="RecordingDecoder"/>). Windows only — elsewhere it
    /// says so (owner's decision 2026-11-15, docs/recording-design.md §1 5).
    /// </summary>
    [JsonRpcMethod("lilysharp/decodeRecording", UseSingleObjectParameterDeserialization = true)]
    public Task<DecodeRecordingResponse> DecodeRecordingAsync(DecodeRecordingParams @params, CancellationToken token)
        => OffDispatch(() => DecodeRecording(@params), token);

    public static DecodeRecordingResponse DecodeRecording(DecodeRecordingParams @params)
    {
        if (!OperatingSystem.IsWindows())
            return new DecodeRecordingResponse { Error = "this kind of recording is read on Windows only — use a wav, mp3, flac or ogg" };
        if (!File.Exists(@params.Input))
            return new DecodeRecordingResponse { Error = $"no such file: {@params.Input}" };
        if (string.IsNullOrEmpty(@params.Output))
            return new DecodeRecordingResponse { Error = "no place to write the decoded recording" };
        try
        {
            var (rate, channels, seconds) = RecordingDecoder.DecodeToWav(@params.Input, @params.Output);
            return new DecodeRecordingResponse { Ok = true, Rate = rate, Channels = channels, Seconds = seconds };
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return new DecodeRecordingResponse { Error = e.Message };
        }
    }
}
