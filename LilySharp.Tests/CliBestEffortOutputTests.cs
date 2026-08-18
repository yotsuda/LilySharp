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
using System.Runtime.InteropServices;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// <c>lysc</c> renders BEST EFFORT: an error is reported and sets the exit code, but it no
/// longer withholds the output. Severity and "may this produce a file" are different
/// questions, and most diagnostics answer only the first — an unsupported <c>override</c>,
/// a stray token in a part header, a duplicate property all leave a score that engraves.
/// </summary>
/// <remarks>
/// <para>
/// Driven as a PROCESS because that is where the policy lives: <c>RunOutputCommand</c> is a
/// local function of a top-level program, so there is nothing to call. <c>lysc.dll</c> is in
/// the test output directory already (LilySharp.Cli is a ProjectReference), fonts included.
/// </para>
/// <para>
/// The process is started as <c>dotnet lysc.dll</c> and NOT as the <c>lysc</c> apphost. Both
/// halves of the apphost were a liability, and each one had already cost a red suite
/// (2026-08-19, session 212):
/// </para>
/// <list type="bullet">
///   <item><description>
///     Its NAME is per-platform. Hard-coding <c>lysc.exe</c> made these seven tests fail on
///     BOTH ubuntu legs of CI — for every push at least back to 2026-08-15 — with
///     "lysc.exe not beside the tests". Nobody was reading the CI gate, so the handoff kept
///     saying the suite was green: it was, on Windows.
///   </description></item>
///   <item><description>
///     Its CONTENT is per-commit. The SDK bakes <c>InformationalVersion</c>
///     (<c>0.3.0+&lt;git sha&gt;</c>) into the apphost's Win32 version resource, so every
///     commit hands Windows a binary it has never seen. With Smart App Control enforcing
///     (<c>VerifiedAndReputablePolicyState = 1</c> — this dev machine), <c>Process.Start</c>
///     then throws a Win32Exception saying the file was blocked by an application control
///     policy, until the cloud reputation for that hash resolves. RULES records the same
///     family blocking BenchmarkDotNet's generated assemblies and LilySharp.Probe; the answer
///     there was also to stop producing the unknown binary, not to weaken the machine.
///   </description></item>
/// </list>
/// <para>
/// The managed dll is the same top-level program either way, so nothing asserted below is
/// weakened — only the launcher changed. What is no longer covered is "the apphost starts",
/// which no test covered on Linux anyway and which belongs to the release, not to this class.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public class CliBestEffortOutputTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(),
        "lys-cli-besteffort-" + Guid.NewGuid().ToString("N"));

    public CliBestEffortOutputTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private static string Source(string parts) =>
        "octave absolute\n" + parts + "\n"
        + "section Main { m { c4 d4 e4 f4 | } }\nform main { ~Main }\nscore main { staff m }\n";

    /// <summary>
    /// The muxer running THIS test, derived from the runtime that loaded it so that it cannot
    /// disagree with the framework <c>lysc.dll</c> will resolve against.
    /// </summary>
    /// <remarks>
    /// <c>DOTNET_HOST_PATH</c> names the same file here (measured, session 212), but MSBuild is
    /// what sets it, so it is absent when the test dll is driven directly rather than by
    /// <c>dotnet test</c>. <c>GetRuntimeDirectory()</c> is always
    /// <c>&lt;root&gt;/shared/Microsoft.NETCore.App/&lt;version&gt;/</c> for a
    /// framework-dependent run, which is what these tests are.
    /// </remarks>
    private static readonly string Muxer = Path.Combine(
        Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..")),
        OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");

    /// <summary>Runs <c>lysc svg -n &lt;input&gt; &lt;output&gt;</c> to completion.</summary>
    private static (int Exit, string Stderr) RunLysc(string input, string output)
    {
        string dll = Path.Combine(AppContext.BaseDirectory, "lysc.dll");
        Assert.True(File.Exists(dll), $"lysc.dll not beside the tests: {dll}");
        Assert.True(File.Exists(Muxer), $"no dotnet host where the runtime says one is: {Muxer}");

        var psi = new ProcessStartInfo(Muxer)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        // ArgumentList, not Arguments: the temp paths are quoted for us, on both platforms.
        // -n: no embedded font, so the run stays quick and the file small.
        foreach (string arg in new[] { dll, "svg", "-n", input, output }) psi.ArgumentList.Add(arg);

        using var p = Process.Start(psi)!;
        string stderr = p.StandardError.ReadToEnd();
        p.StandardOutput.ReadToEnd();
        p.WaitForExit(60_000);
        return (p.ExitCode, stderr);
    }

    private (int Exit, string Stderr, bool Wrote) RunSvg(string parts)
    {
        string input = Path.Combine(_dir, "in.lys");
        string output = Path.Combine(_dir, "out.svg");
        File.WriteAllText(input, Source(parts));
        if (File.Exists(output)) File.Delete(output);

        var (exit, stderr) = RunLysc(input, output);
        return (exit, stderr, File.Exists(output) && new FileInfo(output).Length > 0);
    }

    [Fact]
    public void ACleanFileRendersAndSucceeds()
    {
        var r = RunSvg("part m { clef treble }");
        Assert.Equal(0, r.Exit);
        Assert.True(r.Wrote);
        Assert.DoesNotContain("written anyway", r.Stderr);
    }

    [Theory]
    // Each of these is an ERROR that leaves a perfectly engravable score. Before the
    // best-effort policy, every one of them produced no file at all.
    [InlineData("part m { clef treble  override Beam.thickness = 9 }")]  // LYS1029
    [InlineData("part m { bass }")]                                      // LYS0025
    [InlineData("part m { clef }")]                                      // LYS0026
    [InlineData("part m { clef bass clef treble }")]                     // LYS7003
    public void AnErrorIsReportedButTheOutputIsStillWritten(string parts)
    {
        var r = RunSvg(parts);
        // The exit code is unchanged, so scripts and CI still fail on an error...
        Assert.Equal(1, r.Exit);
        // ...but the file is there.
        Assert.True(r.Wrote, "lysc withheld the output for an error that does not prevent engraving");
        // and the reader is told not to trust it blindly
        Assert.Contains("written anyway", r.Stderr);
    }

    [Fact]
    public void EvenABrokenParseRendersWhatSurvivedRecovery()
    {
        // The parser drops the tokens it cannot place; what DID parse is what gets drawn.
        // This is the LSP preview's long-standing policy, now shared by the CLI.
        var r = RunSvg("part m { clef treble");
        Assert.Equal(1, r.Exit);
        Assert.True(r.Wrote);
    }

    [Fact]
    public void AWarningNeitherStopsTheOutputNorFailsTheRun()
    {
        // An unclosed manual beam (LYS4016) is a warning: the run is clean.
        string input = Path.Combine(_dir, "warn.lys");
        string output = Path.Combine(_dir, "warn.svg");
        File.WriteAllText(input,
            "octave absolute\ntime 4/4\npart m { clef treble }\n"
            + "section Main { m { c8[ d8 e8 f8 g8 a8 b8 c8 | } }\nform main { ~Main }\n"
            + "score main { staff m }\n");

        var (exit, stderr) = RunLysc(input, output);

        Assert.Equal(0, exit);
        Assert.True(File.Exists(output));
        Assert.Contains("manual beam", stderr);
        Assert.DoesNotContain("written anyway", stderr);
    }
}
