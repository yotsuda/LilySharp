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
using LilySharp.Core.Music;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// <c>lysc shape</c> (Orecchiante's request 9, owner's request 2026-11-16): a chord name's default
/// diagram, the shape the editor's step writes first (<see cref="ChordShapes.Default"/>), so a
/// chart written by another program carries it without a second table of shapes.
/// </summary>
/// <remarks>Spawns the real CLI as <c>dotnet lysc.dll</c>, the harness copied from
/// <c>CliCheckSingerTests</c>.</remarks>
[Trait("Category", "Integration")]
public class CliShapeTests
{
    private static readonly string Muxer = Path.Combine(
        Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..")),
        OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");

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
        return (p.ExitCode, output, err.GetAwaiter().GetResult());
    }

    [Fact]
    public void EachName_ItsDefaultShape_ALineEach_AndAnUnreadableNameAnEmptyOne()
    {
        var (exit, output, _) = Lysc("shape", "D", "Am7", "D/F#", "Bm7(b5)", "Xyz");
        Assert.Equal(0, exit);
        Assert.Equal("D\txx0232\nAm7\tx02010\nD/F#\txx4232\nBm7(b5)\tx20201\nXyz\t\n", output);
    }

    [Fact]
    public void TheTuning_IsAChordTuningsName_AndAnUnknownOneIsRefused()
    {
        Assert.Equal("C\t0003\n", Lysc("shape", "C", "--tuning", "ukulele").Out);
        var (exit, _, err) = Lysc("shape", "C", "--tuning", "banjoX");
        Assert.Equal(1, exit);
        Assert.Contains("unknown tuning", err);
        Assert.Equal(1, Lysc("shape").Exit);
    }

    [Fact]
    public void EveryChordOrecchianteWrites_HasAShape_TheEditorsFirst()
    {
        // Orecchiante writes 12 roots × 8 kinds × (no bass or 12): every one has a guitar shape, and
        // it is the editor's first (the same ChordShapes.Default; checked here for the plain ones).
        string[] roots = ["C", "C#", "D", "Eb", "E", "F", "F#", "G", "Ab", "A", "Bb", "B"];
        string[] kinds = ["", "m", "7", "maj7", "m7", "sus4", "dim", "m7(b5)"];
        var names = roots.SelectMany(r => kinds.Select(k => r + k)).ToArray();
        var lines = Lysc(["shape", .. names]).Out.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(names.Length, lines.Length);
        foreach (string line in lines)
        {
            var (name, shape) = (line.Split('\t')[0], line.Split('\t')[1]);
            Assert.True(shape.Length > 0, $"{name} has no shape");
            Assert.True(ChordStructure.TryParseChordEntry(name, out var chord));
            Assert.Equal(ChordVoicings.Spell(ChordShapes.Default(TuningType.Guitar, chord)!.Frets), shape);
        }
    }
}
