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
using LilySharp.Core.Export;
using LilySharp.Core.Syntax;
using LilySharp.Lsp;
using LilySharp.Lsp.Protocol;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The three doors to an export — <c>lysc &lt;format&gt; --score NAME</c>, the preview's
/// Export button (<c>lilysharp/export</c> with a RenderName) and the Explorer's batch
/// export (<c>All</c>) — write ONE file for one score, byte for byte, because all three
/// write through <see cref="ScoreExport"/>.
/// </summary>
/// <remarks>
/// ★ THE CLAIM WORTH GUARDING is the agreement, not that files appear. Each door used
/// to carry its own switch over the formats, and they drifted: the button's MIDI began
/// with a section the score's form never plays (2026-10-03, owner report), its PDF and
/// PNG resolved a stale score name to the file's scoreless picture, its SVG dropped the
/// font when the folder was not found. The book here has two scores on one staff whose
/// forms differ (<c>sub</c> plays the section twice), so a door that took the wrong
/// score, or the primary form, writes different bytes.
/// <para>
/// ⚠️ SPAWNS THE REAL CLI (as <c>dotnet lysc.dll</c>), the way CliBatchTests does and for
/// its reason: the command line's door is the process, options parsing included.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class ScoreExportTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("lysc-doors-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private const string TwoScores = """
        title "Two"
        time 4/4
        key c major
        part melody { clef treble }
        part bass { clef bass }
        phrase mel { c4 d e f | g4 a b c | }
        section Main { melody { mel } bass { c,4 e g c | f,4 a c f | } }
        form main { Main }
        form sub { Main Main }
        score main { staff melody staff bass }
        score sub { staff melody }
        """;

    private static readonly string Muxer = Path.Combine(
        Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..")),
        OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");

    private static (int Exit, string Stdout, string Stderr) Lysc(params string[] args)
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
        psi.ArgumentList.Add(dll);
        foreach (string a in args) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi)!;
        string outText = p.StandardOutput.ReadToEnd();
        string errText = p.StandardError.ReadToEnd();
        p.WaitForExit(120_000);
        return (p.ExitCode, outText, errText);
    }

    private static LilySharpLanguageServer Server() => new(Stream.Null, Stream.Null);

    [Theory]
    [InlineData("svg", ".svg")]
    [InlineData("png", ".png")]
    [InlineData("pdf", ".pdf")]
    [InlineData("midi", ".mid")]
    [InlineData("musicxml", ".xml")]
    [InlineData("ly", ".ly")]
    public void TheCommandLine_TheExportButton_AndTheBatchExport_WriteOneFile(string format, string ext)
    {
        string book = Path.Combine(_dir, "two.lys");
        File.WriteAllText(book, TwoScores);
        string cliDir = Path.Combine(_dir, "cli");
        string buttonDir = Path.Combine(_dir, "button");
        string batchDir = Path.Combine(_dir, "batch");
        Directory.CreateDirectory(buttonDir);

        // Door 1: the command line (`xml` is the command for musicxml).
        var (exit, stdout, stderr) = Lysc(format == "musicxml" ? "xml" : format, "--score", "sub", "-d", cliDir, book);
        Assert.True(exit == 0, $"lysc exit {exit}\n{stdout}\n{stderr}");

        // Door 2: the preview's Export button — one score, named.
        var button = Server().Export(new ExportParams
        {
            Path = book, Format = format, OutputPath = Path.Combine(buttonDir, "two-sub" + ext), RenderName = "sub",
        });
        Assert.True(button.Success, button.Error);

        // Door 3: the Explorer's batch export — every score, under the CLI's names.
        var batch = Server().Export(new ExportParams { Path = book, Format = format, All = true, OutputDirectory = batchDir });
        Assert.True(batch.Success, batch.Error);

        string cli = Comparable(Path.Combine(cliDir, "two-sub" + ext), format);
        Assert.Equal(cli, Comparable(Path.Combine(buttonDir, "two-sub" + ext), format));
        Assert.Equal(cli, Comparable(Path.Combine(batchDir, "two-sub" + ext), format));

        // And `sub` is not `main`: the doors agreed on the score, not on a default.
        Assert.NotEqual(cli, Comparable(Path.Combine(batchDir, "two" + ext), format));
    }

    // The file's bytes as text, byte for byte — except a PDF's, where PDFsharp writes the
    // clock (/CreationDate), a document /ID and a random six-letter tag on each subset
    // font name (/RFYOHX+Emmentaler-20): the one format that is not reproducible from
    // one process to the next. Those are masked; everything drawn stays compared.
    private static string Comparable(string path, string format)
    {
        string text = System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(path));
        return format == "pdf"
            ? System.Text.RegularExpressions.Regex.Replace(text,
                @"/[A-Z]{6}\+|/(CreationDate|ModDate) \(D:[^)]*\)|/ID ?\[[^\]]*\]", "X")
            : text;
    }

    // The home itself, without a door: a file with no `score` block is the null score —
    // its one picture, the primary form, every part — and still writes.
    [Theory]
    [InlineData("svg")]
    [InlineData("midi")]
    [InlineData("musicxml")]
    [InlineData("ly")]
    public void ANullScore_IsTheFilesOnePicture(string format)
    {
        var tree = SyntaxTree.Parse("""
            time 4/4
            key c major
            part melody { clef treble }
            section Main { melody { c4 d e f | g1 | } }
            form main { Main }
            """);
        string path = Path.Combine(_dir, "plain" + ScoreExport.Extensions[format]);

        var result = ScoreExport.Write(tree, format, path, score: null);

        Assert.Equal([path], result.Files);
        Assert.True(new FileInfo(path).Length > 0);
    }

    [Fact]
    public void SeveralPngPages_AreNamedAsLilyPondNamesThem()
    {
        Assert.Equal([Path.Combine("out", "song.png")], ScoreExport.PngPagePaths(Path.Combine("out", "song.png"), 1));
        Assert.Equal(
            [Path.Combine("out", "song-page1.png"), Path.Combine("out", "song-page2.png")],
            ScoreExport.PngPagePaths(Path.Combine("out", "song.png"), 2));
    }

    [Fact]
    public void AnUnknownFormat_IsRefused()
    {
        var tree = SyntaxTree.Parse("part m { clef treble } section A { m { c4 } } form main { A }");
        Assert.Throws<ArgumentException>(() => ScoreExport.Write(tree, "docx", Path.Combine(_dir, "x.docx"), null));
    }
}
