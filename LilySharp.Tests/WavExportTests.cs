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
using System.Linq;
using System.Text;
using LilySharp.Core.Audio;
using LilySharp.Core.Export;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// <c>lysc wav</c>: the score's MIDI played through a SoundFont 2 (MeltySynth) into a WAV.
/// The font is a one-sample sine written here (<see cref="TinySoundFont"/>), so the test needs
/// no font on the machine — none is bundled.
/// </summary>
[Trait("Category", "Unit")]
public sealed class WavExportTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lilysharp-wav-" + Guid.NewGuid().ToString("N"));

    public WavExportTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private const string Book =
        "octave absolute\ntempo 4 = 120\ntime 4/4\npart p { clef treble }\n" +
        "section S { p { c'4 e'4 g'4 c''4 | } }\nform { S }\nscore { staff p }\n";

    [Fact]
    public void AScore_RendersToAWavOfItsLengthPlusTheTail()
    {
        string font = TinySoundFont.Write(Path.Combine(_dir, "sine.sf2"));
        string wav = Path.Combine(_dir, "out.wav");
        var tree = SyntaxTree.Parse(Book);
        var score = LilySharp.Core.Svg.Collector.RenderSpecParser.FindAllDeclared(tree).Single();
        var result = ScoreExport.Write(tree, "wav", wav, score, new ExportOptions { SoundFont = font });

        Assert.Equal([wav], result.Files);
        var bytes = File.ReadAllBytes(wav);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Equal("WAVE", Encoding.ASCII.GetString(bytes, 8, 4));
        Assert.Equal(2, BitConverter.ToInt16(bytes, 22));                       // stereo
        Assert.Equal(WavRenderer.DefaultSampleRate, BitConverter.ToInt32(bytes, 24));
        Assert.Equal(16, BitConverter.ToInt16(bytes, 34));
        int data = BitConverter.ToInt32(bytes, 40);
        Assert.Equal(bytes.Length - 44, data);
        // One bar of 4/4 at 120 = 2 s, plus the 2 s tail.
        double seconds = data / 4.0 / WavRenderer.DefaultSampleRate;
        Assert.InRange(seconds, 3.9, 4.2);
        // The notes sound: the first second holds a loud sample, the last half second is
        // quiet again once the releases have rung out.
        short Peak(int fromFrame, int toFrame)
        {
            short peak = 0;
            for (int f = fromFrame; f < toFrame; f++)
                peak = Math.Max(peak, Math.Abs(BitConverter.ToInt16(bytes, 44 + f * 4)));
            return peak;
        }
        Assert.True(Peak(0, WavRenderer.DefaultSampleRate) > 1000, "the first second sounds");
        int frames = data / 4;
        Assert.True(Peak(frames - WavRenderer.DefaultSampleRate / 4, frames) < 200, "the tail rings out");
    }

    [Fact]
    public void ADlsBank_AnSf3_OrAMissingFile_IsRefusedBeforeTheSynthIsAsked()
    {
        string dls = Path.Combine(_dir, "gm.dls");
        File.WriteAllBytes(dls, [.. Encoding.ASCII.GetBytes("RIFF"), 0, 0, 0, 0, .. Encoding.ASCII.GetBytes("DLS ")]);
        Assert.Contains("DLS", WavRenderer.SoundFontProblem(dls));

        string sf3 = Path.Combine(_dir, "font.sf3");
        File.Copy(TinySoundFont.Write(Path.Combine(_dir, "a.sf2")), sf3);
        Assert.Contains("SoundFont 3", WavRenderer.SoundFontProblem(sf3));

        Assert.Contains("not found", WavRenderer.SoundFontProblem(Path.Combine(_dir, "none.sf2")));
        Assert.Null(WavRenderer.SoundFontProblem(TinySoundFont.Write(Path.Combine(_dir, "b.sf2"))));

        // …and the export leaves no file behind when it refuses.
        string wav = Path.Combine(_dir, "refused.wav");
        var tree = SyntaxTree.Parse(Book);
        Assert.Throws<InvalidDataException>(() => ScoreExport.Write(tree, "wav", wav, null,
            new ExportOptions { SoundFont = dls }));
        Assert.False(File.Exists(wav));
    }
}

/// <summary>
/// The smallest SoundFont 2 MeltySynth reads: one looped sine sample, one instrument over
/// every key, one preset (bank 0, program 0) — the chunk layout of the SoundFont 2.01
/// specification, each list ending in its terminal record.
/// </summary>
internal static class TinySoundFont
{
    public static string Write(string path)
    {
        const int rate = 44100, length = 4410;          // 0.1 s, 44 cycles of 440 Hz
        var smpl = new MemoryStream();
        var sw = new BinaryWriter(smpl);
        for (int i = 0; i < length; i++)
            sw.Write((short)(Math.Sin(2 * Math.PI * 440 * i / rate) * 20000));
        for (int i = 0; i < 46; i++)
            sw.Write((short)0);                            // the 46 zero points the format asks for

        static byte[] Name(string s) => Encoding.ASCII.GetBytes(s.PadRight(20, '\0'));
        byte[] Chunk(string id, byte[] body)
        {
            var m = new MemoryStream();
            var w = new BinaryWriter(m);
            w.Write(Encoding.ASCII.GetBytes(id));
            w.Write(body.Length);
            w.Write(body);
            if (body.Length % 2 == 1) w.Write((byte)0);
            return m.ToArray();
        }
        byte[] List(string type, params byte[][] chunks)
            => Chunk("LIST", [.. Encoding.ASCII.GetBytes(type), .. chunks.SelectMany(c => c)]);
        byte[] Records(Action<BinaryWriter> write)
        {
            var m = new MemoryStream();
            write(new BinaryWriter(m));
            return m.ToArray();
        }

        var info = List("INFO",
            Chunk("ifil", Records(w => { w.Write((short)2); w.Write((short)1); })),
            Chunk("isng", Encoding.ASCII.GetBytes("EMU8000\0")),
            Chunk("INAM", Encoding.ASCII.GetBytes("Sine\0\0")));
        var sdta = List("sdta", Chunk("smpl", smpl.ToArray()));
        var pdta = List("pdta",
            Chunk("phdr", Records(w =>
            {
                foreach (var (name, bag) in new[] { ("Sine", (short)0), ("EOP", (short)1) })
                {
                    w.Write(Name(name)); w.Write((short)0); w.Write((short)0); w.Write(bag);
                    w.Write(0); w.Write(0); w.Write(0);
                }
            })),
            Chunk("pbag", Records(w => { w.Write((short)0); w.Write((short)0); w.Write((short)1); w.Write((short)0); })),
            Chunk("pmod", new byte[10]),
            Chunk("pgen", Records(w => { w.Write((short)41); w.Write((short)0); w.Write(0); })),   // instrument 0; terminal
            Chunk("inst", Records(w =>
            {
                w.Write(Name("Sine")); w.Write((short)0);
                w.Write(Name("EOI")); w.Write((short)1);
            })),
            Chunk("ibag", Records(w => { w.Write((short)0); w.Write((short)0); w.Write((short)2); w.Write((short)0); })),
            Chunk("imod", new byte[10]),
            Chunk("igen", Records(w =>
            {
                w.Write((short)54); w.Write((short)1);     // sampleModes: loop
                w.Write((short)53); w.Write((short)0);     // sampleID 0 (last)
                w.Write(0);                                // terminal
            })),
            Chunk("shdr", Records(w =>
            {
                w.Write(Name("Sine")); w.Write(0); w.Write(length); w.Write(0); w.Write(length);
                w.Write(rate); w.Write((byte)69); w.Write((sbyte)0); w.Write((short)0); w.Write((short)1);
                w.Write(Name("EOS")); w.Write(new byte[26]);
            })));

        var body = new byte[][] { Encoding.ASCII.GetBytes("sfbk"), info, sdta, pdta }.SelectMany(b => b).ToArray();
        using (var f = File.Create(path))
        {
            var w = new BinaryWriter(f);
            w.Write(Encoding.ASCII.GetBytes("RIFF"));
            w.Write(body.Length);
            w.Write(body);
        }
        return path;
    }
}
