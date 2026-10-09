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
/// Where <c>lysc</c> writes: every score of a book, each named for the book —
/// <c>song</c> for the main score and <c>song-&lt;alias&gt;</c> for every other — into the
/// folder <c>-d</c> names (the input's own by default). No option names a file (user decision
/// 2026-09-26: a typed name could say anything; this one says which book and which score).
/// </summary>
/// <remarks>
/// Spawns the real CLI as <c>dotnet lysc.dll</c>, for the reason <c>CliBestEffortOutputTests</c>
/// gives; the harness is copied rather than shared, as <c>CliBatchTests</c> does.
/// </remarks>
[Trait("Category", "Integration")]
public class CliOutputNamingTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("lysc-naming-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private static readonly string Muxer = Path.Combine(
        Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..")),
        OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");

    private static (int Exit, string Stderr) Lysc(params string[] args)
    {
        string dll = Path.Combine(AppContext.BaseDirectory, "lysc.dll");
        Assert.True(File.Exists(dll), $"lysc.dll not beside the tests: {dll}");
        var psi = new ProcessStartInfo(Muxer)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add(dll);
        foreach (string a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        p.StandardOutput.ReadToEnd();
        string err = p.StandardError.ReadToEnd();
        p.WaitForExit(120_000);
        return (p.ExitCode, err);
    }

    /// <summary>A book with three scores over one form — the shape 82 of the owner's bass
    /// books have: the main one, and two more with their own aliases.</summary>
    private string Book(string scores = "score { staff m }\nscore both { staff m  tab m }\n"
                                        + "score tab { tab m }\n")
    {
        string path = Path.Combine(_dir, "song.lys");
        File.WriteAllText(path, "part m { instrument bass }\nsection A { m { c4 d e f | } }\n"
            + "form { A }\n" + scores);
        return path;
    }

    private string[] Written(string folder)
        => Directory.Exists(folder)
            ? [.. Directory.GetFiles(folder).Select(Path.GetFileName).Where(n => !n!.EndsWith(".lys")).Order()!]
            : [];

    [Theory]
    [InlineData("svg", ".svg")]
    [InlineData("pdf", ".pdf")]
    [InlineData("midi", ".mid")]
    [InlineData("xml", ".xml")]
    [InlineData("ly", ".ly")]
    public void EveryScoreIsWritten_NamedForTheBookAndItsAlias(string command, string ext)
    {
        var r = Lysc(command, Book());
        Assert.Equal(0, r.Exit);
        Assert.Equal(new[] { $"song{ext}", $"song-both{ext}", $"song-tab{ext}" }.Order(), Written(_dir));
    }

    [Fact]
    public void TheFolderIsChosen_AndMadeWhenItIsMissing()
    {
        string folder = Path.Combine(_dir, "out", "nested");
        Assert.Equal(0, Lysc("svg", "-n", "-d", folder, Book()).Exit);
        Assert.Equal(new[] { "song-both.svg", "song-tab.svg", "song.svg" }, Written(folder));
    }

    [Fact]
    public void ScorePicksOne_UnderTheSameFixedName()
    {
        Assert.Equal(0, Lysc("svg", "-n", "--score", "tab", Book()).Exit);
        Assert.Equal(new[] { "song-tab.svg" }, Written(_dir));
    }

    [Theory]
    [InlineData("-o", "x.svg")]
    [InlineData("x.svg")]
    [InlineData("--all")]
    public void ANameOrAnAllIsRefused_AndNothingIsWritten(params string[] extra)
    {
        var r = Lysc(["svg", "-n", Book(), .. extra]);
        Assert.NotEqual(0, r.Exit);
        Assert.Empty(Written(_dir));
        Assert.Contains(extra[0] == "--all" ? "every score is written" : "choose the folder with -d", r.Stderr);
    }

    [Fact]
    public void TwoScoresWithOneName_AreRefusedRatherThanOverwritten()
    {
        var r = Lysc("svg", "-n", Book("score { staff m }\nscore { tab m }\n"));
        Assert.NotEqual(0, r.Exit);
        Assert.Contains("give each its own name", r.Stderr);
        Assert.Empty(Written(_dir));
    }
}
