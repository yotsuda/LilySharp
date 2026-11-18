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
using System.Text.RegularExpressions;
using LilySharp.Lsp;
using LilySharp.Lsp.Protocol;
using Xunit;

namespace LilySharp.Tests.Lsp;

/// <summary>
/// What a <c>using "..."</c> reads for an open document's requests (review of session 1291, P8:
/// several requests per keystroke each read every include whole, to find it unchanged): a file
/// that stands is read once, and one written again is read again.
/// </summary>
/// <remarks>
/// "Read once" is seen as the SAME string handed out twice — <c>File.ReadAllText</c> makes a
/// new one each time — so nothing is counted across the tests that run beside these.
/// </remarks>
public class IncludeFilesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lys-include-files-" + Guid.NewGuid().ToString("N"));

    public IncludeFilesTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* a scanner holds it: the temp folder's own cleaning takes it */ }
    }

    /// <summary>Writes <paramref name="name"/> as if <paramref name="ago"/> had passed since.</summary>
    private string Write(string name, string text, TimeSpan ago)
    {
        string path = Path.Combine(_dir, name);
        File.WriteAllText(path, text);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow - ago);
        return path;
    }

    private static readonly TimeSpan LongAgo = TimeSpan.FromMinutes(5);

    [Fact]
    public void AFileThatStands_IsReadOnce()
    {
        string path = Write("a.lys", "part m { clef treble }\n", LongAgo);
        string? first = IncludeFiles.Read(path);
        Assert.Equal("part m { clef treble }\n", first);
        Assert.Same(first, IncludeFiles.Read(path));
        Assert.Same(first, IncludeFiles.Read(path));
    }

    [Fact]
    public void AFileWrittenAgain_IsReadAgain_WhateverItsLength()
    {
        string path = Write("a.lys", "part m { clef treble }\n", LongAgo);
        Assert.Equal("part m { clef treble }\n", IncludeFiles.Read(path));
        // The same length, another time of writing.
        Write("a.lys", "part n { clef treble }\n", LongAgo - TimeSpan.FromMinutes(1));
        Assert.Equal("part n { clef treble }\n", IncludeFiles.Read(path));
        // Another length, the same time of writing.
        DateTime stamp = File.GetLastWriteTimeUtc(path);
        File.WriteAllText(path, "part longer { clef treble }\n");
        File.SetLastWriteTimeUtc(path, stamp);
        Assert.Equal("part longer { clef treble }\n", IncludeFiles.Read(path));
    }

    // A stamp says "not written since" only once it is old: a file written twice within its file
    // system's tick, to the same length, carries the same stamp with other text.
    [Fact]
    public void AFileJustWritten_IsReadEachTime_UntilItsStampIsOld()
    {
        string path = Path.Combine(_dir, "fresh.lys");
        File.WriteAllText(path, "part m { clef treble }\n");
        DateTime stamp = File.GetLastWriteTimeUtc(path);
        string? first = IncludeFiles.Read(path);
        // Written again to the same length, and the stamp put back: what a second write within
        // the tick looks like.
        File.WriteAllText(path, "part n { clef treble }\n");
        File.SetLastWriteTimeUtc(path, stamp);
        Assert.Equal("part n { clef treble }\n", IncludeFiles.Read(path));
        Assert.NotSame(first, IncludeFiles.Read(path));

        // The same file with its stamp just short of settled, and just past it.
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow - IncludeFiles.Settles + TimeSpan.FromSeconds(2));
        Assert.NotSame(IncludeFiles.Read(path), IncludeFiles.Read(path));
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow - IncludeFiles.Settles - TimeSpan.FromSeconds(1));
        Assert.Same(IncludeFiles.Read(path), IncludeFiles.Read(path));
        // …and a stamp in the future (another machine's clock) never settles.
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow + TimeSpan.FromHours(1));
        Assert.NotSame(IncludeFiles.Read(path), IncludeFiles.Read(path));
    }

    [Fact]
    public void AFileThatIsNotThere_IsNone_AndIsReadWhenItAppears_AndNoneAgainWhenItGoes()
    {
        string path = Path.Combine(_dir, "later.lys");
        Assert.Null(IncludeFiles.Read(path));
        Write("later.lys", "part m\n", LongAgo);
        Assert.Equal("part m\n", IncludeFiles.Read(path));
        File.Delete(path);
        Assert.Null(IncludeFiles.Read(path));
        // …and back with the stamp and the length it had: not the text that was kept.
        Write("later.lys", "part n\n", LongAgo);
        Assert.Equal("part n\n", IncludeFiles.Read(path));
    }

    /// <summary>The document's requests ask through it: a book whose include stands is one piece
    /// across the requests, and an include saved again — long after, as from another editor —
    /// is the new piece at the next request.</summary>
    [Fact]
    public void ABooksInclude_SavedAgain_ShowsAtTheNextRequest()
    {
        Write("intro.lys", "part m { clef treble }\nsection Intro { m { c'1 | d'1 | } }\n", LongAgo);
        string text = "using \"intro.lys\"\nsection A { m { e'4 f g a | } }\nform { Intro A }\nscore { staff m }\n";
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        var uri = new Uri(Path.Combine(_dir, "main.lys"));
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = uri, Text = text, Version = 1, LanguageId = "lilysharp" },
        });
        var ask = new PlaceAtParams { TextDocument = new TextDocumentIdentifier { Uri = uri }, Offset = text.IndexOf("f g a", StringComparison.Ordinal) };
        Assert.Equal(3, server.PlaceAt(ask).Measure);
        Assert.Equal(3, server.PlaceAt(ask).Measure);
        // A bar more, the stamp old but another: kept texts are not trusted past their stamp.
        Write("intro.lys", "part m { clef treble }\nsection Intro { m { c'1 | d'1 | e'1 | } }\n", LongAgo - TimeSpan.FromMinutes(1));
        Assert.Equal(4, server.PlaceAt(ask).Measure);
    }

    /// <summary>Every request that expands a document's <c>using</c>s reads through
    /// <c>IncludeFiles</c>: one that read the disk itself would pay the read on each arrival,
    /// and nothing else here would say so.</summary>
    [Fact]
    public void TheRequestsOfADocument_ReadTheirIncludesThroughIt()
    {
        string lsp = Path.Combine(RepoRoot(), "LilySharp.Lsp");
        var readers = Directory.EnumerateFiles(lsp, "*.cs")
            .Select(f => (Name: Path.GetFileName(f), Text: File.ReadAllText(f)))
            .ToList();
        // The one that reads for itself is the export of a file named by its path, not a document's request.
        var own = readers.Where(r => Regex.IsMatch(r.Text, @"=>\s*(System\.IO\.)?File\.Exists\(p\)\s*\?\s*(System\.IO\.)?File\.ReadAllText\(p\)"))
            .Select(r => r.Name).ToArray();
        Assert.Equal(["LilySharpLanguageServer.Export.cs"], own);
        Assert.Equal(6, readers.Sum(r => Regex.Matches(r.Text, @"\bIncludeFiles\.Read\b(?!\()").Count));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "LilySharp.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("the repo's root was not found");
    }
}
