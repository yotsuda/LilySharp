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
using System.Text;
using MeltySynth;

namespace LilySharp.Core.Audio;

/// <summary>
/// Renders a score's MIDI to a WAV file through a SoundFont, offline — <c>lysc wav</c>.
/// </summary>
/// <remarks>
/// <para>
/// The synth is MeltySynth (pure C#, MIT), the road docs/playback-design.md §6 names: the
/// SAME MIDI the <c>.mid</c> export writes (<see cref="Midi.MidiExporter"/>) is played, so
/// what is heard is what a MIDI player would play from that file, through the font the user
/// names. No font is bundled (owner's decision 2026-10-12), so the user names one.
/// </para>
/// <para>
/// ⚠️ SoundFont 2 ONLY. MeltySynth reads <c>.sf2</c>; it reads neither the compressed
/// <c>.sf3</c> (MuseScore's defaults) nor Windows' <c>gm.dls</c>, which the preview's synth
/// does read — so a file of either kind is refused here with that sentence, before the synth
/// is asked. GeneralUser GS is an <c>.sf2</c> (the preview's recommendations,
/// <c>site/editor-body.html#soundfonts</c>).
/// </para>
/// <para>
/// The output is 16-bit PCM, stereo, at <see cref="DefaultSampleRate"/>, rendered and
/// written a block at a time (a long piece is never held whole in memory), with
/// <see cref="ReleaseTail"/> seconds after the last event so the final notes ring out.
/// </para>
/// </remarks>
public static class WavRenderer
{
    /// <summary>The sample rate the file is written at.</summary>
    public const int DefaultSampleRate = 44100;

    /// <summary>Seconds rendered after the MIDI's last event, for the final releases.</summary>
    public const double ReleaseTail = 2.0;

    private const int BlockFrames = 4096;

    /// <summary>
    /// Why the file at <paramref name="soundFontPath"/> cannot be used, or null when it reads
    /// as a SoundFont 2 — the check made before the synth is asked.
    /// </summary>
    public static string? SoundFontProblem(string soundFontPath)
    {
        if (!File.Exists(soundFontPath))
            return $"sound font not found: {soundFontPath}";
        var head = new byte[12];
        using (var f = File.OpenRead(soundFontPath))
            if (f.Read(head, 0, 12) < 12)
                return $"not a sound font: {soundFontPath}";
        string riff = Encoding.ASCII.GetString(head, 0, 4), form = Encoding.ASCII.GetString(head, 8, 4);
        if (riff != "RIFF")
            return $"not a sound font: {soundFontPath}";
        if (form == "DLS ")
            return $"{Path.GetFileName(soundFontPath)} is a DLS bank, which `lysc wav` cannot read — name a SoundFont 2 (.sf2)";
        if (form != "sfbk")
            return $"not a sound font: {soundFontPath}";
        if (string.Equals(Path.GetExtension(soundFontPath), ".sf3", StringComparison.OrdinalIgnoreCase))
            return $"{Path.GetFileName(soundFontPath)} is a compressed SoundFont 3, which `lysc wav` cannot read — name a SoundFont 2 (.sf2)";
        return null;
    }

    /// <summary>
    /// Plays <paramref name="midi"/> through the SoundFont at <paramref name="soundFontPath"/>
    /// and writes the sound to <paramref name="output"/> as a WAV file; answers how long the
    /// file plays.
    /// </summary>
    /// <exception cref="InvalidDataException">The font is not a SoundFont 2
    /// (<see cref="SoundFontProblem"/>), or MeltySynth could not read it.</exception>
    public static TimeSpan Render(Midi.MidiFile midi, string soundFontPath, Stream output,
        int sampleRate = DefaultSampleRate)
    {
        if (SoundFontProblem(soundFontPath) is { } problem)
            throw new InvalidDataException(problem);

        using var midiBytes = new MemoryStream();
        midi.WriteTo(midiBytes);
        midiBytes.Position = 0;
        var sequence = new MeltySynth.MidiFile(midiBytes);

        SoundFont font;
        try { font = new SoundFont(soundFontPath); }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            throw new InvalidDataException($"cannot read the sound font {Path.GetFileName(soundFontPath)}: {e.Message}", e);
        }

        var synth = new Synthesizer(font, new SynthesizerSettings(sampleRate));
        var sequencer = new MidiFileSequencer(synth);
        sequencer.Play(sequence, false);

        long frames = (long)Math.Ceiling((sequence.Length.TotalSeconds + ReleaseTail) * sampleRate);
        WriteHeader(output, sampleRate, frames);

        var left = new float[BlockFrames];
        var right = new float[BlockFrames];
        var pcm = new byte[BlockFrames * 4];
        for (long done = 0; done < frames; done += BlockFrames)
        {
            int n = (int)Math.Min(BlockFrames, frames - done);
            sequencer.Render(left.AsSpan(0, n), right.AsSpan(0, n));
            for (int i = 0; i < n; i++)
            {
                WriteSample(pcm, i * 4, left[i]);
                WriteSample(pcm, i * 4 + 2, right[i]);
            }
            output.Write(pcm, 0, n * 4);
        }
        return TimeSpan.FromSeconds((double)frames / sampleRate);
    }

    private static void WriteSample(byte[] buffer, int at, float value)
    {
        short s = (short)Math.Round(Math.Clamp(value, -1f, 1f) * short.MaxValue);
        buffer[at] = (byte)s;
        buffer[at + 1] = (byte)(s >> 8);
    }

    /// <summary>The 44-byte RIFF/WAVE header of a 16-bit stereo PCM file of
    /// <paramref name="frames"/> frames.</summary>
    private static void WriteHeader(Stream output, int sampleRate, long frames)
    {
        const short channels = 2, bits = 16;
        long data = frames * channels * (bits / 8);
        using var w = new BinaryWriter(output, Encoding.ASCII, leaveOpen: true);
        w.Write(Encoding.ASCII.GetBytes("RIFF"));
        w.Write((uint)(36 + data));
        w.Write(Encoding.ASCII.GetBytes("WAVE"));
        w.Write(Encoding.ASCII.GetBytes("fmt "));
        w.Write(16u);
        w.Write((short)1);                               // PCM
        w.Write(channels);
        w.Write(sampleRate);
        w.Write(sampleRate * channels * (bits / 8));     // byte rate
        w.Write((short)(channels * (bits / 8)));         // block align
        w.Write(bits);
        w.Write(Encoding.ASCII.GetBytes("data"));
        w.Write((uint)data);
    }
}
