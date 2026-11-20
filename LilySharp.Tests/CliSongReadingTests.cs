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
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using LilySharp.Core.Export;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// <c>lysc timeline</c>, <c>lysc notes</c> and the <c>--order</c>/<c>--tempo</c> they share with
/// <c>midi</c> and <c>wav</c> (Shosa's request 1, 2026-11-20; <see cref="SongReading"/>). Their JSON is
/// an agreement with programs that read it — Shosa dances to it — so these pin its SHAPE, key by key:
/// a shape that changes in silence breaks them. A change here is a change of
/// <see cref="SongReading.Protocol"/> unless it only adds a field.
/// </summary>
/// <remarks>Spawns the real CLI as <c>dotnet lysc.dll</c>, the harness of <c>CliShapeTests</c>.</remarks>
[Trait("Category", "Integration")]
public sealed class CliSongReadingTests : IDisposable
{
    private static readonly string Muxer = Path.Combine(
        Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..")),
        OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");

    // An upbeat, a change of tempo inside a section, a change of time between sections, two forms,
    // two scores: every field of the timeline says something.
    private const string Song = """
        title "Probe"
        tempo 120
        time 4/4
        part melody { clef treble }
        section Intro {
          partial 4
          melody { g'4 | c'4 d e f | tempo 90 g1 | }
        }
        section Waltz {
          melody { time 3/4 b'4 d d | a2. | }
        }
        form { Intro Waltz }
        form short { Waltz }
        score { staff melody }
        score practice { form short  staff melody }

        """;

    private readonly string _dir = Directory.CreateTempSubdirectory("lys-song-reading-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string Write(string name, string text)
    {
        string path = Path.Combine(_dir, name);
        File.WriteAllText(path, text.ReplaceLineEndings("\n"));
        return path;
    }

    private static (int Exit, string Out, string Err) Lysc(params string[] args)
    {
        string dll = Path.Combine(AppContext.BaseDirectory, "lysc.dll");
        Assert.True(File.Exists(dll), $"lysc.dll not beside the tests: {dll}");
        var psi = new ProcessStartInfo(Muxer)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
        };
        psi.ArgumentList.Add(dll);
        foreach (string a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var err = p.StandardError.ReadToEndAsync();
        string output = p.StandardOutput.ReadToEnd();
        p.WaitForExit(120_000);
        return (p.ExitCode, output.TrimEnd(), err.GetAwaiter().GetResult());
    }

    private static string[] Keys(JsonElement e) => e.EnumerateObject().Select(p => p.Name).ToArray();

    [Fact]
    public void Timeline_HasTheAgreedShape_KeyByKey()
    {
        var (exit, output, _) = Lysc("timeline", Write("probe.lys", Song));
        Assert.Equal(0, exit);
        using var json = JsonDocument.Parse(output);
        var root = json.RootElement;
        Assert.Equal(new[] { "error", "tempo", "beatsPerBar", "beatUnit", "sections", "plays", "scores", "tempos", "meters" }, Keys(root));
        Assert.All(root.GetProperty("plays").EnumerateArray(), p => Assert.Equal(new[] { "name", "beats", "pickup" }, Keys(p)));
        Assert.All(root.GetProperty("tempos").EnumerateArray(), t => Assert.Equal(new[] { "beat", "bpm" }, Keys(t)));
        Assert.All(root.GetProperty("meters").EnumerateArray(), m => Assert.Equal(new[] { "beat", "beatsPerBar", "beatUnit" }, Keys(m)));
        // And what each says, all of it: the upbeat is a beat of Intro's nine, the tempo changes
        // in Intro's third bar (beat 5), the waltz's time from its first beat (9). "" is the unnamed score.
        Assert.Equal(
            """{"error":null,"tempo":120,"beatsPerBar":4,"beatUnit":4,"sections":["Intro","Waltz"],"plays":[{"name":"Intro","beats":9,"pickup":1},{"name":"Waltz","beats":6,"pickup":0}],"scores":["","practice"],"tempos":[{"beat":0,"bpm":120},{"beat":5,"bpm":90}],"meters":[{"beat":0,"beatsPerBar":4,"beatUnit":4},{"beat":9,"beatsPerBar":3,"beatUnit":4}]}""",
            output);
    }

    [Fact]
    public void Notes_AreSevenNumbersEach_InTheTimelinesBeats()
    {
        var (exit, output, _) = Lysc("notes", Write("probe.lys", Song));
        Assert.Equal(0, exit);
        // [beat, beats, pitch, velocity, timbre, program, channel], in the order they start.
        Assert.Equal(
            """{"error":null,"notes":[[0,1,67,90,0,0,0],[1,1,84,90,0,0,0],[2,1,86,90,0,0,0],[3,1,88,90,0,0,0],[4,1,89,90,0,0,0],[5,4,91,90,0,0,0],[9,1,71,90,0,0,0],[10,1,74,90,0,0,0],[11,1,74,90,0,0,0],[12,3,69,90,0,0,0]]}""",
            output);
    }

    [Fact]
    public void Score_PlaysItsOwnForm()
    {
        using var json = JsonDocument.Parse(Lysc("timeline", Write("probe.lys", Song), "--score", "practice").Out);
        var plays = json.RootElement.GetProperty("plays").EnumerateArray().Select(p => p.GetProperty("name").GetString()).ToArray();
        Assert.Equal(new[] { "Waltz" }, plays);
        Assert.Equal(3, json.RootElement.GetProperty("beatsPerBar").GetInt32());
    }

    [Fact]
    public void Order_PlaysTheSectionsAsked_LeavingOutNamesTheSongHasNot()
    {
        string song = Write("probe.lys", Song);
        using var json = JsonDocument.Parse(Lysc("timeline", song, "--order", "Waltz Nope Intro").Out);
        var root = json.RootElement;
        Assert.Equal(JsonValueKind.Null, root.GetProperty("error").ValueKind);
        Assert.Equal(new[] { "Waltz", "Intro" }, root.GetProperty("plays").EnumerateArray().Select(p => p.GetProperty("name").GetString()).ToArray());
        // Intro now starts at beat 6, its tempo change five beats later.
        Assert.Equal("""[{"beat":0,"bpm":120},{"beat":11,"bpm":90}]""", root.GetProperty("tempos").GetRawText());

        // None of them: an error, still JSON, still exit 0 — and notes say the same.
        var (exit, output, _) = Lysc("timeline", song, "--order", "Nope Never");
        Assert.Equal(0, exit);
        using var none = JsonDocument.Parse(output);
        Assert.Contains("None of the sections asked for", none.RootElement.GetProperty("error").GetString());
        using var noNotes = JsonDocument.Parse(Lysc("notes", song, "--order", "Nope").Out);
        Assert.Equal(0, noNotes.RootElement.GetProperty("notes").GetArrayLength());
        Assert.NotNull(noNotes.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public void Tempo_ScalesEveryTempo_AndMovesNoBeat()
    {
        string song = Write("probe.lys", Song);
        using var json = JsonDocument.Parse(Lysc("timeline", song, "--tempo", "60").Out);
        Assert.Equal(60, json.RootElement.GetProperty("tempo").GetDouble());
        Assert.Equal("""[{"beat":0,"bpm":60},{"beat":5,"bpm":45}]""", json.RootElement.GetProperty("tempos").GetRawText());
        Assert.Equal(Lysc("notes", song).Out, Lysc("notes", song, "--tempo", "60").Out);
    }

    [Fact]
    public void ASongThatCannotBeRead_IsAnErrorInTheJson_WithExitZero()
    {
        foreach (var (args, says) in new (string[], string)[]
        {
            (["timeline", Path.Combine(_dir, "none.lys")], "There is no file none.lys"),
            (["notes", Path.Combine(_dir, "none.lys")], "There is no file none.lys"),
            (["timeline", Write("broken.lys", "part p { clef treble }\nsection A { p { c'4 d e | } }\nform { A B C\n")], "has errors"),
            (["timeline", Write("empty.lys", "part p { clef treble }\n")], "has no section"),
        })
        {
            var (exit, output, _) = Lysc(args);
            Assert.Equal(0, exit);
            using var json = JsonDocument.Parse(output);
            Assert.Contains(says, json.RootElement.GetProperty("error").GetString());
        }
    }

    [Fact]
    public void AWrongCommandLine_IsExitOne()
    {
        Assert.Equal(1, Lysc("timeline").Exit);
        Assert.Equal(1, Lysc("timeline", Write("probe.lys", Song), "--tempo", "fast").Exit);
        Assert.Equal(1, Lysc("notes", Write("probe.lys", Song), "--tempo", "0").Exit);
        Assert.Equal(1, Lysc("midi", Write("probe.lys", Song), "--tempo", "-3").Exit);
    }

    [Fact]
    public void VersionJson_SaysTheProtocol()
    {
        var (exit, output, _) = Lysc("--version", "--json");
        Assert.Equal(0, exit);
        using var json = JsonDocument.Parse(output);
        Assert.Equal(new[] { "version", "protocol" }, Keys(json.RootElement));
        // Raised only when a field changes its meaning or goes away: then this line changes with it.
        Assert.Equal(1, SongReading.Protocol);
        Assert.Equal(SongReading.Protocol, json.RootElement.GetProperty("protocol").GetInt32());
        string plain = Lysc("--version").Out.Split('\n')[0].Trim();
        Assert.Equal(plain, "lysc " + json.RootElement.GetProperty("version").GetString());
    }

    [Fact]
    public void MidiOrder_IsTheSameFileAsTheOrderWrittenAsTheForm()
    {
        string asked = Path.Combine(_dir, "asked"), written = Path.Combine(_dir, "written");
        Assert.Equal(0, Lysc("midi", Write("probe.lys", Song), "--score", "", "--order", "Waltz Intro", "-d", asked).Exit);
        Assert.Equal(0, Lysc("midi", Write("reordered.lys", Song.Replace("form { Intro Waltz }", "form { Waltz Intro }")), "--score", "", "-d", written).Exit);
        Assert.Equal(File.ReadAllBytes(Path.Combine(written, "reordered.mid")), File.ReadAllBytes(Path.Combine(asked, "probe.mid")));

        var (exit, _, err) = Lysc("midi", Write("probe.lys", Song), "--order", "Nope", "-d", asked);
        Assert.Equal(1, exit);
        Assert.Contains("None of the sections asked for", err);
    }

    [Fact]
    public void MidiTempo_ScalesTheFilesTempos()
    {
        string asked = Path.Combine(_dir, "asked"), written = Path.Combine(_dir, "written");
        Assert.Equal(0, Lysc("midi", Write("probe.lys", Song), "--score", "", "--tempo", "60", "-d", asked).Exit);
        Assert.Equal(0, Lysc("midi", Write("slower.lys", Song.Replace("tempo 120", "tempo 60").Replace("tempo 90", "tempo 45")), "--score", "", "-d", written).Exit);
        var scaled = Tempos(File.ReadAllBytes(Path.Combine(asked, "probe.mid")));
        var own = Tempos(File.ReadAllBytes(Path.Combine(written, "slower.mid")));
        Assert.Equal(own.Count, scaled.Count);
        Assert.Equal(2, scaled.Count);
        // A microsecond apart at most: 60e6/90 is rounded once before it is scaled.
        Assert.All(own.Zip(scaled), p => Assert.InRange(p.Second, p.First - 2, p.First + 2));
    }

    /// <summary>The microseconds a quarter of every Set Tempo event (FF 51 03 tt tt tt) in a .mid.</summary>
    private static List<int> Tempos(byte[] mid)
    {
        var found = new List<int>();
        for (int i = 0; i + 5 < mid.Length; i++)
        {
            if (mid[i] == 0xFF && mid[i + 1] == 0x51 && mid[i + 2] == 0x03)
                found.Add(mid[i + 3] << 16 | mid[i + 4] << 8 | mid[i + 5]);
        }
        return found;
    }
}
