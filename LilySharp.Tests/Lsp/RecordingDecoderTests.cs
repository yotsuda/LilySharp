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
using System.IO;
using LilySharp.Lsp;
using NAudio.Wave;
using Xunit;

namespace LilySharp.Tests.Lsp;

/// <summary>
/// lilysharp/decodeRecording: an m4a (which VS Code's webview cannot decode) is decoded by
/// Windows' Media Foundation into a float WAV whose seconds are a player's — the encoder's silence
/// in front cut, as a browser and ffmpeg cut it, so the bar seconds Dettato writes fall where the
/// sound is (docs/recording-design.md §1 5).
/// </summary>
/// <remarks>
/// The fixtures (Fixtures/recording, made by ffmpeg 9.0.2: a quiet 220 Hz tone with a 2 ms click
/// at exactly 0.5 s, stereo 44.1 kHz) put the click where ffmpeg plays it. Media Foundation alone
/// puts it an AAC priming (2112 samples, 48 ms) or an mp3 frame and LAME's delay later. On
/// another system than Windows the decoder says it reads such a file on Windows only.
/// </remarks>
public class RecordingDecoderTests
{
    private static string Fixture(string name)
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "LilySharp.Tests", "Fixtures")))
            dir = Path.GetDirectoryName(dir);
        return Path.Combine(dir!, "LilySharp.Tests", "Fixtures", "recording", name);
    }

    [Theory]
    [InlineData("click.m4a")]
    [InlineData("click.mp3")]
    public void TheClick_IsWhereAPlayerPutsIt(string name)
    {
        var output = Path.Combine(Path.GetTempPath(), $"lys-decode-{Guid.NewGuid():N}.wav");
        var response = LilySharpLanguageServer.DecodeRecording(new DecodeRecordingParams { Input = Fixture(name), Output = output });
        if (!OperatingSystem.IsWindows())
        {
            Assert.False(response.Ok);
            Assert.Contains("Windows only", response.Error);
            return;
        }
        try
        {
            Assert.True(response.Ok, response.Error);
            Assert.Equal(44100, response.Rate);
            Assert.Equal(2, response.Channels);
            using var reader = new WaveFileReader(output);
            Assert.Equal(WaveFormatEncoding.IeeeFloat, reader.WaveFormat.Encoding);
            var samples = reader.ToSampleProvider();
            var buffer = new float[2 * 4096];
            long frame = 0, peakAt = -1;
            float peak = 0;
            for (int got; (got = samples.Read(buffer, 0, buffer.Length)) > 0;)
            {
                for (int i = 0; i < got; i += 2, frame++)
                    if (Math.Abs(buffer[i]) > peak)
                        (peak, peakAt) = (Math.Abs(buffer[i]), frame);
            }
            // The click at 0.5 s: within 3 ms (the codec smears a transient a little); without the
            // lead cut it is 48 ms (AAC) or 39 ms (mp3) late.
            Assert.InRange(peakAt / 44100.0, 0.497, 0.503);
            Assert.InRange(response.Seconds, 1.15, 1.30);
        }
        finally
        {
            File.Delete(output);
        }
    }

    [Fact]
    public void AFileThatIsNotThere_SaysSo()
    {
        var response = LilySharpLanguageServer.DecodeRecording(new DecodeRecordingParams { Input = Fixture("missing.m4a"), Output = Path.GetTempFileName() });
        Assert.False(response.Ok);
        Assert.NotNull(response.Error);
    }
}
