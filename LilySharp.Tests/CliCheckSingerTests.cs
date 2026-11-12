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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// <c>lysc check --singer</c> (owner's decisions 2026-11-13): the check asks a cantoria program
/// who would sing each <c>singer</c> line on this machine and says, AT THE LINE, which nobody
/// installed can — a warning in the compiler's own shape, the exit code the check's own. The
/// program here is a script that prints a plan (<see cref="SingerPlanTests.FakeCantoria"/>).
/// </summary>
/// <remarks>
/// Spawns the real CLI as <c>dotnet lysc.dll</c>, for the reason <c>CliBestEffortOutputTests</c>
/// gives; the harness is copied rather than shared, as <c>CliBatchTests</c> does.
/// </remarks>
[Trait("Category", "Integration")]
public class CliCheckSingerTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("lysc-check-singer-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private static readonly string Muxer = Path.Combine(
        Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..")),
        OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");

    private static (int Exit, string[] Out) Lysc(params string[] args)
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
        // The machine's own singer is not this test's.
        psi.Environment.Remove("LILYSHARP_SINGER");
        psi.ArgumentList.Add(dll);
        foreach (string a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var err = p.StandardError.ReadToEndAsync();
        string output = p.StandardOutput.ReadToEnd();
        p.WaitForExit(120_000);
        _ = err.GetAwaiter().GetResult();
        return (p.ExitCode, output.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToArray());
    }

    /// <summary>A melody in Japanese and an alto in Latin: the singer lines are lines 9 and 10.</summary>
    private string Song()
    {
        string path = Path.Combine(_dir, "song.lys");
        File.WriteAllText(path,
            "time 4/4\n" +
            "part melody { clef treble }\n" +
            "part alto { clef treble }\n" +
            "section A { melody { c'2 d | } alto { a2 b | } }\n" +
            "lyrics ja { あ い | }\n" +
            "lyrics la language latin { no -- bis | }\n" +
            "form { A }\n" +
            "\n" +
            "singer \"neutrino/MERROW\" sings ja on melody\n" +
            "singer female sings la on alto\n" +
            "score { staff melody  lyrics ja  staff alto  lyrics la }\n" +
            "score short { staff melody  lyrics ja  staff alto  lyrics la }\n");
        return path;
    }

    // What `cantoria plan --json` answers for one score of Song(), and for its two.
    private const string Melody = """{"part":"melody","voice":"neutrino/MERROW","reason":null}""";
    private const string Alto = """{"part":"alto","voice":null,"reason":"no installed voice sings Latin (la); installed voices sing ja, en, zh, ko"}""";

    private static string Plan(params string[] singers) => "{\"ready\":false,\"singers\":[" + string.Join(",", singers) + "]}";

    [Fact]
    public void ALineNobodyInstalledCanSing_IsAWarningAtTheLine_AndTheExitCodeIsTheChecks()
    {
        string song = Song();
        string cantoria = SingerPlanTests.FakeCantoria(_dir, Plan(Melody, Alto, Melody, Alto), 3);

        var (exit, lines) = Lysc("check", "--singer", cantoria, song);

        Assert.Equal(0, exit);
        // Two scores sing the two lines: each is said once.
        Assert.Equal(
        [
            "No errors found.",
            $"{song}(10,1): warning: No installed voice can sing this line: no installed voice sings Latin (la); installed voices sing ja, en, zh, ko. The part's instrument plays instead.",
            "Singers:",
            "  line 9: singer \"neutrino/MERROW\" sings ja on melody -> neutrino/MERROW",
        ], lines);
    }

    [Fact]
    public void WithScore_OnlyThatScoresSingersAreAskedAfter()
    {
        string song = Song();
        // A plan for two voices: asked for four, it would be refused as not the answer.
        string cantoria = SingerPlanTests.FakeCantoria(_dir, Plan(Melody, Melody), 0);

        var (exit, lines) = Lysc("check", "--singer", cantoria, "--score", "short", song);

        Assert.Equal(0, exit);
        Assert.Equal(["No errors found.", "Singers:", "  line 9: singer \"neutrino/MERROW\" sings ja on melody -> neutrino/MERROW",
            "  line 10: singer female sings la on alto -> neutrino/MERROW"], lines);
    }

    [Fact]
    public void WithoutSinger_TheCheckIsAsItWas()
    {
        var (exit, lines) = Lysc("check", Song());
        Assert.Equal(0, exit);
        Assert.Equal(["No errors found."], lines);
    }

    [Fact]
    public void AProgramThatCannotAnswer_IsOneLine_AndTheChecksOwnResultStands()
    {
        string song = Song();
        // An older cantoria: exit code 2, its usage.
        var (exit, lines) = Lysc("check", "--singer", SingerPlanTests.FakeCantoria(_dir, "", 2), song);
        Assert.Equal(0, exit);
        Assert.Equal("No errors found.", lines[0]);
        Assert.StartsWith("Singers: not asked (", Assert.Single(lines.Skip(1)));
        Assert.Contains("does not know `plan`", lines[1]);

        // No such program.
        (exit, lines) = Lysc("check", "--singer", Path.Combine(_dir, "none", "cantoria.exe"), song);
        Assert.Equal(0, exit);
        Assert.StartsWith("Singers: not asked (cantoria.exe could not be asked who would sing: ", Assert.Single(lines.Skip(1)));
    }
}
