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

    // ---- `lysc wav --singer`: the sung parts' voices mixed in (docs/singing-design.md §5) ----

    // One bar of 4/4 at 120 (2 s), a syllable on each note; the score sings the part (a staff
    // with its words under it), and — `rows` — may play it on a second staff too.
    private static string SungBook(string rows = "staff melody  lyrics verse sings melody") =>
        "tempo 4 = 120\ntime 4/4\npart melody { clef treble }\n" +
        "section A { melody { c'4 e4 g4 c4 | } lyrics verse { か き く け | } }\nform { A }\n" +
        $"score {{ {rows} }}\n";

    private (byte[] Bytes, ExportResult Result) Export(string book, SingingEngine singer, string name = "sung.wav")
    {
        string font = TinySoundFont.Write(Path.Combine(_dir, "sine.sf2"));
        string wav = Path.Combine(_dir, name);
        var tree = SyntaxTree.Parse(book);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics.Select(d => d.Message)));
        var score = LilySharp.Core.Svg.Collector.RenderSpecParser.FindAllDeclared(tree).Single();
        var result = ScoreExport.Write(tree, "wav", wav, score, new ExportOptions { SoundFont = font, Singer = singer });
        return (File.ReadAllBytes(wav), result);
    }

    private static short Peak(byte[] bytes, double fromSeconds, double toSeconds)
    {
        int frames = BitConverter.ToInt32(bytes, 40) / 4;
        int from = (int)(fromSeconds * WavRenderer.DefaultSampleRate), to = Math.Min(frames, (int)(toSeconds * WavRenderer.DefaultSampleRate));
        short peak = 0;
        for (int f = from; f < to; f++)
            peak = Math.Max(peak, Math.Abs(BitConverter.ToInt16(bytes, 44 + f * 4)));
        return peak;
    }

    private static double Seconds(byte[] bytes) => BitConverter.ToInt32(bytes, 40) / 4.0 / WavRenderer.DefaultSampleRate;

    [Fact]
    public void ASungPart_IsSungFromTheScoresZero_InItsInstrumentsPlace()
    {
        // The engine's half-second voice at 48 kHz: resampled into the file from 0; the part's
        // notes, which the score only sings, leave the instruments — so from 1 s on nothing sounds.
        var singer = new FakeSinger { Seconds = 0.5, SampleRate = 48000, Amplitude = 0.5f };
        var (bytes, result) = Export(SungBook(), singer);

        // What the engine was given: Cantilena's score (its README), the syllables as kana.
        Assert.NotNull(singer.ScoreJson);
        using var json = System.Text.Json.JsonDocument.Parse(singer.ScoreJson);
        Assert.Equal(WavRenderer.DefaultSampleRate, json.RootElement.GetProperty("sampleRate").GetInt32());
        Assert.Equal("ja", json.RootElement.GetProperty("language").GetString());
        var notes = json.RootElement.GetProperty("notes").EnumerateArray().ToList();
        Assert.Equal(new[] { "か", "き", "く", "け" }, notes.Select(n => n.GetProperty("lyric").GetString()));
        // Moved later by the lead (SungScoreJson.Lead): the room for the first consonant before the beat.
        Assert.Equal(new[] { 0.3, 0.8, 1.3, 1.8 }, notes.Select(n => n.GetProperty("start").GetDouble()));
        Assert.Equal(new[] { 60, 64, 67, 72 }, notes.Select(n => n.GetProperty("pitch").GetInt32()));
        Assert.True(notes[3].GetProperty("breath").GetBoolean());
        Assert.Contains("か", singer.ScoreJson);                               // written as kana, not \u escapes

        Assert.True(Peak(bytes, 0.0, 0.5) > 12000, "the voice sounds from the score's zero");
        Assert.True(Peak(bytes, 1.0, 2.0) < 50, "the instrument is silent in the voice's place");
        Assert.Contains(result.Notes, n => n == "Voice: fake/voice sings melody (4 notes, 0.5 s)");
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void AConsonantBeforeTheBeat_IsHeardWhole_TheInstrumentsWaitingForIt()
    {
        // The engine sings the first syllable's consonant 0.1 s before the beat, in the lead
        // (owner's report 2026-11-08: さ at 0 sang as た when the lead was cut): the file begins
        // with it, and the instruments — the score's other staff — start 0.1 s later.
        var singer = new FakeSinger { Seconds = 0.5, SampleRate = 48000, Amplitude = 0.5f, Consonant = 0.1 };
        var (bytes, result) = Export(SungBook("staff melody  lyrics verse sings melody  staff melody"), singer);

        Assert.Contains(result.Notes, n => n == "Voice lead: 0.10 s (the first consonant before the beat; the instruments start after it)");
        Assert.True(Peak(bytes, 0.0, 0.08) > 12000, "the consonant sounds from the file's start");
        Assert.InRange(Seconds(bytes), 4.0, 4.2);                             // 0.1 + 2 s of notes + 2 s of tail
        // The instrument's staff comes in at 0.1 s: the voice alone is a 440 Hz sine of amplitude 0.5
        // (about 16,000); once the instrument joins, the peaks are higher.
        Assert.True(Peak(bytes, 0.0, 0.09) < 17500, "before the beat only the voice sounds");
        Assert.True(Peak(bytes, 0.12, 0.5) > 17500, "from the beat the instrument sounds too");
    }

    [Fact]
    public void AVoiceTheEngineCannotSing_LeavesTheInstrument_AndIsSaid()
    {
        var singer = new FakeSinger { Error = "no voice installed" };
        var (bytes, result) = Export(SungBook(), singer);

        Assert.True(Peak(bytes, 0.0, 1.0) > 1000, "the instrument stands in");
        Assert.Contains("melody is not sung (no voice installed); its instrument plays", result.Warnings);
        Assert.DoesNotContain(result.Notes, n => n.StartsWith("Voice:", StringComparison.Ordinal));
    }

    [Fact]
    public void APartTheScoreSingsAndPlays_SoundsBothWays()
    {
        // A silent voice, so what is heard is the instrument's second staff.
        var singer = new FakeSinger { Seconds = 0.1, Amplitude = 0f };
        var (bytes, result) = Export(SungBook("staff melody  lyrics verse sings melody  staff melody"), singer);

        Assert.True(Peak(bytes, 0.0, 1.0) > 1000, "the instrument plays on its own staff");
        Assert.Contains(result.Notes, n => n == "Voice: fake/voice sings melody (4 notes, 0.1 s), its instrument too");
    }

    [Fact]
    public void AVoiceLongerThanTheNotes_GrowsTheFile_WhateverItsRate()
    {
        var singer = new FakeSinger { Seconds = 5.0, SampleRate = 22050, Amplitude = 0.5f };
        var (bytes, _) = Export(SungBook(), singer);

        Assert.InRange(Seconds(bytes), 6.9, 7.2);                             // 5 s of voice, 2 s of tail
        Assert.True(Peak(bytes, 4.0, 4.5) > 12000, "the voice sounds past the notes' end");
        Assert.True(Peak(bytes, 6.5, 7.0) < 50, "then the tail");
    }

    [Fact]
    public void TheWavReader_AveragesChannels_AndReadsPcmAndFloat()
    {
        static byte[] Wav(short format, short channels, short bits, byte[] data)
        {
            var m = new MemoryStream();
            var w = new BinaryWriter(m);
            w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + data.Length); w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            w.Write(16); w.Write(format); w.Write(channels); w.Write(8000); w.Write(8000 * channels * bits / 8);
            w.Write((short)(channels * bits / 8)); w.Write(bits);
            w.Write(Encoding.ASCII.GetBytes("data")); w.Write(data.Length); w.Write(data);
            return m.ToArray();
        }
        var stereo = WavReader.Read(Wav(1, 2, 16, [.. BitConverter.GetBytes((short)16384), .. BitConverter.GetBytes((short)-16384),
            .. BitConverter.GetBytes((short)16384), .. BitConverter.GetBytes((short)16384)]));
        Assert.Equal(8000, stereo.SampleRate);
        Assert.Equal(new[] { 0f, 0.5f }, stereo.Samples);
        var floats = WavReader.Read(Wav(3, 1, 32, [.. BitConverter.GetBytes(0.25f), .. BitConverter.GetBytes(-1f)]));
        Assert.Equal(new[] { 0.25f, -1f }, floats.Samples);
        var bytes8 = WavReader.Read(Wav(1, 1, 8, [128, 255]));
        Assert.Equal(0f, bytes8.Samples[0]);
        Assert.InRange(bytes8.Samples[1], 0.99f, 1f);
        Assert.Throws<InvalidDataException>(() => WavReader.Read(Encoding.ASCII.GetBytes("not a wav file at all")));
    }

    [Fact]
    public void TheCantilenaProgram_IsRefusedBeforeAnyScoreIsRendered_WhenItsPathNamesNoFile()
    {
        Assert.Contains("not found", CantilenaProgram.Problem(Path.Combine(_dir, "none", "cantilena.exe")));
        Assert.Null(CantilenaProgram.Problem("cantilena"));                     // a bare name: the PATH's
        Assert.Equal("neutrino/MERROW", new CantilenaProgram("cantilena", "neutrino/MERROW").Name);
        Assert.Equal("cantilena", new CantilenaProgram(@"C:\x\cantilena.exe").Name);
    }
}

/// <summary>A singing engine for the tests: like the real one it is handed a score moved later by
/// <see cref="SungScoreJson.Lead"/>, and writes silence for that lead, then a sine of its own
/// length and rate (a <see cref="Consonant"/> starts the sine that much before the beat, inside
/// the lead); keeps the score; or fails with its sentence.</summary>
internal sealed class FakeSinger : SingingEngine
{
    public double Seconds { get; init; } = 1.0;
    public int SampleRate { get; init; } = 48000;
    public float Amplitude { get; init; } = 0.5f;
    /// <summary>Seconds of sound before the beat (the first syllable's consonant), within the lead.</summary>
    public double Consonant { get; init; }
    public string? Error { get; init; }
    public string? ScoreJson { get; private set; }

    public override string Name => "fake/voice";

    public override string? Sing(string scoreJsonPath, string outputWavPath)
    {
        ScoreJson = File.ReadAllText(scoreJsonPath);
        if (Error != null)
            return Error;
        int silent = (int)((SungScoreJson.Lead - Consonant) * SampleRate);
        int n = (int)((Seconds + Consonant) * SampleRate);
        using var f = File.Create(outputWavPath);
        var w = new BinaryWriter(f);
        w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + (silent + n) * 2); w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(SampleRate); w.Write(SampleRate * 2); w.Write((short)2); w.Write((short)16);
        w.Write(Encoding.ASCII.GetBytes("data")); w.Write((silent + n) * 2);
        for (int i = 0; i < silent; i++)
            w.Write((short)0);
        for (int i = 0; i < n; i++)
            w.Write((short)(Math.Sin(2 * Math.PI * 440 * i / SampleRate) * Amplitude * 32767));
        return null;
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
