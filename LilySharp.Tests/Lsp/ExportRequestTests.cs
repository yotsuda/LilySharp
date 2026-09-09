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
using LilySharp.Lsp;
using LilySharp.Lsp.Protocol;
using Xunit;

namespace LilySharp.Tests.Lsp;

/// <summary>
/// The <c>lilysharp/export</c> request's batch face (HANDOFF §2F F-export): a file
/// named by path rather than by open document, and <c>All</c> writing every score
/// under the CLI's <c>--all</c> names. The generators are tested elsewhere; these
/// guard the request wiring — which text is read, what each score's file is called,
/// what is left out and said so.
/// </summary>
public sealed class ExportRequestTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "lilysharp-export-" + Guid.NewGuid().ToString("N"));

    public ExportRequestTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private static LilySharpLanguageServer Server() => new(Stream.Null, Stream.Null);

    // Two scores on the SAME staff, differing only in form: `main` plays the section
    // once, `sub` twice — so a difference between the two outputs of a form-driven
    // format (the twin, MIDI) can only come from the form the request chose.
    private const string TwoScores = """
        title "Two"
        time 4/4
        key c major
        part melody { clef treble }
        phrase mel { c4 d e f | g4 a b c | }
        section Main { melody { mel } }
        form main { Main }
        form sub { Main Main }
        score main { staff melody }
        score sub { staff melody }
        """;

    // Parts, a section and a form, but no `score` block — the renderer's default
    // picture, and one file to write.
    private const string NoScoreBlock = """
        time 4/4
        key c major
        part melody { clef treble }
        section Main { melody { c4 d e f | g1 | } }
        form main { Main }
        """;

    private string Book(string name, string text)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, text);
        return path;
    }

    private string Out => Path.Combine(_dir, "out");

    [Theory]
    [InlineData("svg", ".svg")]
    [InlineData("pdf", ".pdf")]
    [InlineData("png", ".png")]
    [InlineData("ly", ".ly")]
    [InlineData("musicxml", ".xml")]
    [InlineData("midi", ".mid")]
    public void All_WritesEveryScore_UnderTheCliNames(string format, string ext)
    {
        var book = Book("two.lys", TwoScores);

        var response = Server().Export(new ExportParams
        {
            Path = book, Format = format, All = true, OutputDirectory = Out,
        });

        Assert.True(response.Success, response.Error);
        Assert.Equal(
            new[] { Path.Combine(Out, "two" + ext), Path.Combine(Out, "two-sub" + ext) },
            response.OutputPaths);
        Assert.All(response.OutputPaths!, p => Assert.True(new FileInfo(p).Length > 0, p));
        Assert.Empty(response.Warnings!);
        Assert.Equal(Out, response.OutputPath);
    }

    [Fact]
    public void All_GivesAFormDrivenFormat_EachScoresOwnForm()
    {
        var book = Book("two.lys", TwoScores);

        var response = Server().Export(new ExportParams
        {
            Path = book, Format = "ly", All = true, OutputDirectory = Out,
        });

        Assert.True(response.Success, response.Error);
        // Same staff, so the only thing that can differ is the form — and it must,
        // or `sub` was written with the primary form the one-score call uses.
        Assert.NotEqual(
            File.ReadAllText(Path.Combine(Out, "two.ly")),
            File.ReadAllText(Path.Combine(Out, "two-sub.ly")));
    }

    [Fact]
    public void All_Vsqx_WritesTheFirstScore_AndSaysWhatItLeftOut()
    {
        var book = Book("two.lys", TwoScores);

        var response = Server().Export(new ExportParams
        {
            Path = book, Format = "vsqx", All = true, OutputDirectory = Out,
        });

        Assert.True(response.Success, response.Error);
        Assert.Equal(new[] { Path.Combine(Out, "two.vsqx") }, response.OutputPaths);
        var warning = Assert.Single(response.Warnings!);
        Assert.Contains("sub", warning);
    }

    [Fact]
    public void All_NoScoreBlock_WritesOneFile_UnderTheFilesName()
    {
        var book = Book("plain.lys", NoScoreBlock);

        var response = Server().Export(new ExportParams
        {
            Path = book, Format = "svg", All = true, OutputDirectory = Out,
        });

        Assert.True(response.Success, response.Error);
        Assert.Equal(new[] { Path.Combine(Out, "plain.svg") }, response.OutputPaths);
        Assert.True(File.Exists(Path.Combine(Out, "plain.svg")));
    }

    [Fact]
    public void AnOpenDocument_Wins_OverTheFileOnDisk()
    {
        var book = Book("two.lys", TwoScores.Replace("\"Two\"", "\"Disk\""));
        var uri = new Uri(book);
        var server = Server();
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = uri, Text = TwoScores.Replace("\"Two\"", "\"Open\""),
                LanguageId = "lilysharp", Version = 1,
            },
        });

        var response = server.Export(new ExportParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Path = book, Format = "svg", All = true, OutputDirectory = Out,
        });

        Assert.True(response.Success, response.Error);
        var svg = File.ReadAllText(Path.Combine(Out, "two.svg"));
        Assert.Contains("Open", svg);
        Assert.DoesNotContain("Disk", svg);
    }

    [Fact]
    public void OneScore_IsUnchanged_AndNamesTheOneFile()
    {
        var book = Book("two.lys", TwoScores);
        var target = Path.Combine(_dir, "chosen.svg");

        var response = Server().Export(new ExportParams
        {
            Path = book, Format = "svg", OutputPath = target, RenderName = "sub",
        });

        Assert.True(response.Success, response.Error);
        Assert.Equal(target, response.OutputPath);
        Assert.Equal(new[] { target }, response.OutputPaths);
        Assert.True(File.Exists(target));
    }

    [Fact]
    public void ASyntaxError_ReturnsIt_AndWritesNothing()
    {
        var book = Book("broken.lys", "part m { section A { c4 d }");

        var response = Server().Export(new ExportParams
        {
            Path = book, Format = "pdf", All = true, OutputDirectory = Out,
        });

        Assert.False(response.Success);
        Assert.NotNull(response.Error);
        Assert.False(Directory.Exists(Out));
    }

    [Fact]
    public void AMissingFile_ReturnsAnError()
    {
        var response = Server().Export(new ExportParams
        {
            Path = Path.Combine(_dir, "no-such.lys"), Format = "pdf", All = true, OutputDirectory = Out,
        });

        Assert.False(response.Success);
        Assert.Contains("no-such.lys", response.Error);
    }

    [Fact]
    public void NeitherDocumentNorPath_ReturnsAnError()
    {
        var response = Server().Export(new ExportParams { Format = "pdf" });

        Assert.False(response.Success);
        Assert.NotNull(response.Error);
    }

    [Fact]
    public void All_WithoutAnOutputDirectory_ReturnsAnError()
    {
        var book = Book("two.lys", TwoScores);

        var response = Server().Export(new ExportParams { Path = book, Format = "pdf", All = true });

        Assert.False(response.Success);
        Assert.NotNull(response.Error);
    }

    [Fact]
    public void AnUnknownFormat_ReturnsAnError()
    {
        var book = Book("two.lys", TwoScores);

        var response = Server().Export(new ExportParams
        {
            Path = book, Format = "docx", All = true, OutputDirectory = Out,
        });

        Assert.False(response.Success);
        Assert.Contains("docx", response.Error);
    }
}
