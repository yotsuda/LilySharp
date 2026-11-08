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
using System.Buffers.Binary;
using System.IO;

namespace LilySharp.Core.Audio;

/// <summary>A sound read from a WAV file: mono samples in −1..1 at <see cref="SampleRate"/>.</summary>
public sealed record MonoAudio(float[] Samples, int SampleRate)
{
    /// <summary>How long the sound plays.</summary>
    public double Seconds => (double)Samples.Length / SampleRate;

    /// <summary>Where sound begins within the first <paramref name="within"/> seconds: the first
    /// sample past <paramref name="threshold"/>, in seconds; null when nothing sounds that long.</summary>
    public double? SoundStart(double within, float threshold = 0.02f)
    {
        int n = (int)Math.Min(Samples.Length, Math.Ceiling(within * SampleRate));
        for (int i = 0; i < n; i++)
            if (Math.Abs(Samples[i]) > threshold)
                return (double)i / SampleRate;
        return null;
    }
}

/// <summary>
/// Reads a RIFF WAVE file as mono floats — what a singing engine's WAV becomes before
/// <see cref="WavRenderer"/> mixes it in. PCM 8, 16, 24 and 32-bit and IEEE float 32 and 64,
/// any channel count, averaged to one (the editor's <c>audioRenderCore.decodeWav</c> reads the
/// same set).
/// </summary>
public static class WavReader
{
    /// <exception cref="InvalidDataException">Not a WAVE file, or a format not handled.</exception>
    public static MonoAudio Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 12 || !bytes[..4].SequenceEqual("RIFF"u8) || !bytes[8..12].SequenceEqual("WAVE"u8))
            throw new InvalidDataException("not a WAV file");
        int format = 0, channels = 0, rate = 0, bits = 0;
        ReadOnlySpan<byte> data = default;
        int pos = 12;
        while (pos + 8 <= bytes.Length)
        {
            var id = bytes.Slice(pos, 4);
            int size = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(pos + 4, 4));
            int body = pos + 8;
            if (size < 0 || body + size > bytes.Length)
                size = bytes.Length - body;              // a truncated last chunk: what is there
            if (id.SequenceEqual("fmt "u8) && size >= 16)
            {
                var f = bytes.Slice(body, size);
                format = BinaryPrimitives.ReadUInt16LittleEndian(f);
                channels = BinaryPrimitives.ReadUInt16LittleEndian(f[2..]);
                rate = BinaryPrimitives.ReadInt32LittleEndian(f[4..]);
                bits = BinaryPrimitives.ReadUInt16LittleEndian(f[14..]);
                if (format == 0xFFFE && size >= 26)      // WAVE_FORMAT_EXTENSIBLE: the real format leads the GUID
                    format = BinaryPrimitives.ReadUInt16LittleEndian(f[24..]);
            }
            else if (id.SequenceEqual("data"u8))
            {
                data = bytes.Slice(body, size);
            }
            pos = body + size + (size & 1);
        }
        if (channels == 0 || rate == 0)
            throw new InvalidDataException("the WAV file has no fmt chunk");
        int bytesPer = bits / 8;
        if (bytesPer == 0)
            throw new InvalidDataException($"unhandled WAV bit depth {bits}");
        int frames = data.Length / (bytesPer * channels);
        var mono = new float[frames];
        for (int i = 0; i < frames; i++)
        {
            float sum = 0;
            for (int c = 0; c < channels; c++)
            {
                var s = data.Slice((i * channels + c) * bytesPer, bytesPer);
                sum += (format, bits) switch
                {
                    (1, 8) => (s[0] - 128) / 128f,
                    (1, 16) => BinaryPrimitives.ReadInt16LittleEndian(s) / 32768f,
                    (1, 24) => ((s[0] << 8 | s[1] << 16 | s[2] << 24) >> 8) / 8388608f,
                    (1, 32) => BinaryPrimitives.ReadInt32LittleEndian(s) / 2147483648f,
                    (3, 32) => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(s)),
                    (3, 64) => (float)BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(s)),
                    _ => throw new InvalidDataException($"unhandled WAV format {format} at {bits} bits"),
                };
            }
            mono[i] = sum / channels;
        }
        return new MonoAudio(mono, rate);
    }
}
