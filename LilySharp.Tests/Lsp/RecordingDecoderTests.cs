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

    // An MPEG-4 box: size, type, content.
    private static byte[] Box(string type, params byte[][] content)
    {
        int size = 8;
        foreach (var c in content) size += c.Length;
        var b = new byte[size];
        b[0] = (byte)(size >> 24); b[1] = (byte)(size >> 16); b[2] = (byte)(size >> 8); b[3] = (byte)size;
        System.Text.Encoding.ASCII.GetBytes(type, 0, 4, b, 4);
        int at = 8;
        foreach (var c in content) { c.CopyTo(b, at); at += c.Length; }
        return b;
    }

    private static byte[] Be32(uint v) => [(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v];
    private static byte[] Ascii(string s) => System.Text.Encoding.ASCII.GetBytes(s);

    // A file as iTunes 11 and the iPhone's Voice Memos write it: a sound track with no edit list (or with
    // one, beginning at `editBegins` in a 44100 time scale) and the iTunes note "iTunSMPB" under udta.
    private static MemoryStream ITunesFile(uint? editBegins)
    {
        var hdlr = Box("hdlr", new byte[8], Ascii("soun"), new byte[12]);
        var mdhd = Box("mdhd", new byte[12], Be32(44100), Be32(441000), new byte[4]);
        var trakParts = new System.Collections.Generic.List<byte[]>();
        if (editBegins is { } begins)
            trakParts.Add(Box("edts", Box("elst", new byte[4], Be32(1), Be32(441000), Be32(begins), Be32(0x00010000))));
        trakParts.Add(Box("mdia", mdhd, hdlr));
        var note = Box("----",
            Box("mean", new byte[4], Ascii("com.apple.iTunes")),
            Box("name", new byte[4], Ascii("iTunSMPB")),
            Box("data", Be32(1), new byte[4], Ascii(" 00000000 00000840 000002D8 00000000000AC6E8 00000000 00000000")));
        var moov = Box("moov", Box("trak", trakParts.ToArray()), Box("udta", Box("meta", new byte[4], Box("ilst", note))));
        var ftyp = Box("ftyp", Ascii("M4A "), new byte[4]);
        var file = new MemoryStream();
        file.Write(ftyp); file.Write(moov); file.Write(Box("mdat", new byte[16]));
        file.Position = 0;
        return file;
    }

    [Fact]
    public void AnM4aWithNoEditList_IsCutByItsITunesNote()
    {
        // (Dettato REQUESTS #5: without it the bar seconds Dettato writes are 48 ms before the sound Lily# plays)
        Assert.Equal(0x840, RecordingDecoder.Mp4Lead(ITunesFile(null), 44100));
    }

    [Fact]
    public void AnM4aWithAnEditList_GoesByItsEditList()
    {
        Assert.Equal(1024, RecordingDecoder.Mp4Lead(ITunesFile(1024), 44100));
    }

    [Fact]
    public void AFileThatIsNotThere_SaysSo()
    {
        var response = LilySharpLanguageServer.DecodeRecording(new DecodeRecordingParams { Input = Fixture("missing.m4a"), Output = Path.GetTempFileName() });
        Assert.False(response.Ok);
        Assert.NotNull(response.Error);
    }
}
