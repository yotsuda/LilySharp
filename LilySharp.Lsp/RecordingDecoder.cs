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

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using NAudio.Wave;

namespace LilySharp.Lsp;

/// <summary>
/// A recording the preview's webview cannot decode itself — an m4a / mp4 / aac (VS Code's Chromium
/// has no AAC decoder: MEASURED 2026-11-15 in VS Code 1.146, Lab sessions/p1283/stretch/vscode-e2e) —
/// decoded by Windows' Media Foundation into a float WAV the webview reads (docs/recording-design.md
/// §1 5, owner's decision 2026-11-15: Windows only; elsewhere such a file is not taken).
/// </summary>
/// <remarks>
/// The seconds must be a player's: an mp3 or AAC encoder puts silence in front and the file says how
/// much; a browser and ffmpeg start after it, Media Foundation does not — and Dettato's bar seconds
/// are a player's (C:\MyProj\Dettato\docs\SIDECAR.md). So the lead is cut as Dettato cuts it: the
/// reading below is Dettato's own (src/Dettato/Audio.cs, the same author, Apache-2.0), kept to its
/// channels instead of averaged.
/// </remarks>
internal static class RecordingDecoder
{
    /// <summary>The kinds of file this decodes, by extension.</summary>
    public static readonly string[] Inputs = ["m4a", "mp4", "aac", "wma"];

    /// <summary>Decodes <paramref name="input"/> into a 32-bit float WAV at <paramref name="output"/>,
    /// its own rate and channels, the encoder's lead cut. InvalidDataException says why not, in words
    /// for the user.</summary>
    [SupportedOSPlatform("windows")]
    public static (int Rate, int Channels, double Seconds) DecodeToWav(string input, string output)
    {
        try
        {
            using var reader = new MediaFoundationReader(input, new MediaFoundationReader.MediaFoundationReaderSettings { RequestFloatOutput = true });
            int channels = reader.WaveFormat.Channels, rate = reader.WaveFormat.SampleRate;
            var samples = reader.ToSampleProvider();
            int lead = Lead(input, rate);
            long skip = (long)lead * channels;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
            long frames = 0;
            using (var writer = new WaveFileWriter(output, WaveFormat.CreateIeeeFloatWaveFormat(rate, channels)))
            {
                var buffer = new float[channels * 16384];
                for (int got; (got = samples.Read(buffer, 0, buffer.Length)) > 0;)
                {
                    int from = 0;
                    if (skip > 0)
                    {
                        from = (int)Math.Min(skip, got);
                        skip -= from;
                    }
                    if (got > from)
                    {
                        writer.WriteSamples(buffer, from, got - from);
                        frames += (got - from) / channels;
                    }
                }
            }
            if (frames == 0)
                throw new InvalidDataException("no sound in it");
            return (rate, channels, frames / (double)rate);
        }
        // (Windows' errors may come without a message: their code then)
        catch (Exception e) when (e is COMException or ArgumentException or InvalidOperationException or NotSupportedException or EndOfStreamException)
        {
            throw new InvalidDataException($"not a recording this reads ({(string.IsNullOrWhiteSpace(e.Message) ? $"0x{e.HResult:X8}" : e.Message.Trim())})");
        }
    }

    /// <summary>The samples (per channel) Windows gives before the recording's own first one: the
    /// encoder's silence the file says it put there. 0 where the file does not say.</summary>
    internal static int Lead(string path, int rate)
    {
        try
        {
            using var file = File.OpenRead(path);
            return Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".mp3" => Mp3Lead(file),
                ".m4a" or ".mp4" => Mp4Lead(file, rate),
                _ => 0,
            };
        }
        catch (Exception e) when (e is IOException or ArgumentException or IndexOutOfRangeException or OverflowException) { return 0; }
    }

    /// <summary>An mp3 whose first frame is the encoder's note and not sound: Windows gives that frame as silence
    /// (when it says "Info": a constant bit rate), then the encoder's delay (in LAME's part of the note), then one
    /// sample more. An mp3 with no note has the delay too, and nothing that says how long: it stays.</summary>
    internal static int Mp3Lead(Stream file)
    {
        var head = new byte[10];
        file.ReadExactly(head);
        long at = 0;
        if (head[0] == 'I' && head[1] == 'D' && head[2] == '3')     // (the tag's length: four bytes of seven bits)
            at = 10 + (head[6] << 21 | head[7] << 14 | head[8] << 7 | head[9]) + ((head[5] & 0x10) != 0 ? 10 : 0);
        file.Position = at;
        var b = new byte[8192];
        int got = file.ReadAtLeast(b, b.Length, false);
        for (int i = 0; i + 4 < got; i++)
        {
            if (b[i] != 0xFF || (b[i + 1] & 0xE0) != 0xE0) continue;
            int version = b[i + 1] >> 3 & 3, layer = b[i + 1] >> 1 & 3;      // 3: MPEG 1; 1: layer III
            if (version == 1 || layer != 1 || b[i + 2] >> 4 == 15 || (b[i + 2] >> 2 & 3) == 3) continue;
            bool mono = b[i + 3] >> 6 == 3, crc = (b[i + 1] & 1) == 0;
            int note = i + 4 + (crc ? 2 : 0) + (version == 3 ? (mono ? 17 : 32) : (mono ? 9 : 17));
            if (note + 8 > got) return 0;
            string id = Encoding.ASCII.GetString(b, note, 4);
            if (id is not ("Xing" or "Info")) return 0;         // the first frame is sound
            // (a note that says "Xing", as a file of varying bit rate has, Windows knows and passes over itself)
            int frame = id == "Xing" ? 0 : version == 3 ? 1152 : 576, flags = b[note + 7];
            int lame = note + 8 + ((flags & 1) != 0 ? 4 : 0) + ((flags & 2) != 0 ? 4 : 0) + ((flags & 4) != 0 ? 100 : 0) + ((flags & 8) != 0 ? 4 : 0);
            if (lame + 24 > got) return frame;
            int delay = b[lame + 21] << 4 | b[lame + 22] >> 4;
            return delay is > 0 and < 3000 ? frame + delay + 1 : frame;
        }
        return 0;
    }

    /// <summary>An MPEG-4 file whose sound track says where it begins (the first entry of its edit list): Windows
    /// gives the track from its very start.</summary>
    internal static int Mp4Lead(Stream file, int rate)
    {
        static uint U32(byte[] b, int at) => (uint)(b[at] << 24 | b[at + 1] << 16 | b[at + 2] << 8 | b[at + 3]);
        static long I64(byte[] b, int at) => (long)U32(b, at) << 32 | U32(b, at + 4);
        // the boxes inside b[from..to): (type, where its content begins, where it ends)
        static IEnumerable<(string Type, int From, int To)> Boxes(byte[] b, int from, int to)
        {
            while (from + 8 <= to)
            {
                long size = U32(b, from);
                int content = from + 8;
                if (size == 1) { size = I64(b, from + 8); content += 8; }
                else if (size == 0) size = to - from;
                if (size < content - from || from + size > to) yield break;
                yield return (Encoding.ASCII.GetString(b, from + 4, 4), content, (int)(from + size));
                from += (int)size;
            }
        }
        static (int From, int To)? Find(byte[] b, (int From, int To) inside, string type)
        {
            foreach (var box in Boxes(b, inside.From, inside.To)) if (box.Type == type) return (box.From, box.To);
            return null;
        }

        // the moov box, wherever among the file's boxes it is
        byte[]? moov = null;
        var top = new byte[16];
        for (long at = 0; at + 8 <= file.Length;)
        {
            file.Position = at;
            file.ReadExactly(top, 0, 8);
            long size = U32(top, 0);
            int header = 8;
            if (size == 1) { file.ReadExactly(top, 8, 8); size = I64(top, 8); header = 16; }
            else if (size == 0) size = file.Length - at;
            if (size < header) return 0;
            if (top[4] == 'm' && top[5] == 'o' && top[6] == 'o' && top[7] == 'v')
            {
                if (size - header > 1 << 26) return 0;
                moov = new byte[size - header];
                file.ReadExactly(moov);
                break;
            }
            at += size;
        }
        if (moov == null) return 0;
        foreach (var trak in Boxes(moov, 0, moov.Length))
        {
            if (trak.Type != "trak" || Find(moov, (trak.From, trak.To), "mdia") is not { } mdia) continue;
            if (Find(moov, mdia, "hdlr") is not { } hdlr || Encoding.ASCII.GetString(moov, hdlr.From + 8, 4) != "soun") continue;
            // the first sound track is the one Windows plays
            if (Find(moov, mdia, "mdhd") is not { } mdhd) return 0;
            if (Find(moov, (trak.From, trak.To), "edts") is not { } edts || Find(moov, edts, "elst") is not { } elst) return Priming(moov);
            long scale = U32(moov, mdhd.From + (moov[mdhd.From] == 1 ? 20 : 12));
            if (U32(moov, elst.From + 4) < 1 || scale == 0) return 0;
            long begins = moov[elst.From] == 1 ? I64(moov, elst.From + 16) : (int)U32(moov, elst.From + 12);
            return begins > 0 && begins < scale ? (int)Math.Round(begins * (double)rate / scale) : 0;      // (under a second: an encoder's, not a cut)
        }
        return 0;
    }

    /// <summary>With no edit list, iTunes (and the iPhone's Voice Memos) say the encoder's silence in their own note,
    /// "iTunSMPB": hex numbers, the second the samples in front (2112 for AAC). ffmpeg goes by it, Windows does not
    /// (MEASURED 2026-11-16 by Dettato on the owner's files, its README "A recording as it comes"; Dettato
    /// REQUESTS #5). 0 when there is none.</summary>
    internal static int Priming(byte[] moov)
    {
        int at = moov.AsSpan().IndexOf("iTunSMPB"u8);
        if (at < 0) return 0;
        int data = moov.AsSpan(at).IndexOf("data"u8);
        if (data < 0 || data > 64) return 0;
        int from = at + data + 12, to = from;      // ('data', then 4 bytes of kind and 4 of language, then the text)
        while (to < moov.Length && to < from + 200 && moov[to] is >= 0x20 and < 0x7F) to++;
        var fields = Encoding.ASCII.GetString(moov, from, to - from).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return fields.Length > 1 && int.TryParse(fields[1], System.Globalization.NumberStyles.HexNumber, null, out int n) && n is > 0 and < 10000 ? n : 0;
    }
}
