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
/// Where a document is on the disk, read off the URI the editor names it by. VS Code writes a
/// Windows drive's colon encoded — <c>file:///c%3A/Users/me/song.lys</c> — and .NET's
/// <see cref="Uri.LocalPath"/> reads that as <c>/c:/Users/me/song.lys</c>, a path that is
/// nowhere: what stands beside the document (the files its <c>using</c>s name, the scan's side
/// file) was not found, and every <c>using</c> in the editor was LYS0028 "cannot read".
/// </summary>
/// <remarks>
/// Found in a real VS Code in session 1299 (Lab sessions/p1299/vscode-e2e, run2.js): every test
/// before it opened its document by <c>new Uri(path)</c>, the form the editor does not send.
/// </remarks>
public class DocumentFilePathTests
{
    /// <summary>The URI as VS Code writes it: the drive's colon encoded.</summary>
    private static Uri AsTheEditorNamesIt(string path)
        => new("file:///" + path.Replace('\\', '/').TrimStart('/').Replace(":", "%3A"));

    [Fact]
    public void ABookOpenedAsTheEditorNamesIt_ReadsWhatItsUsingNames()
    {
        string dir = Path.Combine(Path.GetTempPath(), "lys-uri-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "intro.lys"), "part m { clef treble }\nsection Intro { m { c'1 | d'1 | } }\n");
            string text = "using \"intro.lys\"\nsection A { m { e'4 f g a | } }\nform { Intro A }\nscore { staff m }\n";
            var uri = AsTheEditorNamesIt(Path.Combine(dir, "main.lys"));
            if (OperatingSystem.IsWindows())
                Assert.Contains("%3A", uri.OriginalString);       // (the form under test: else this is the plain one again)
            var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
            server.DidOpen(new DidOpenTextDocumentParams
            {
                TextDocument = new TextDocumentItem { Uri = uri, Text = text, Version = 1, LanguageId = "lilysharp" },
            });
            // The part is the include's, and the Intro's two bars are before the caret's on the
            // page: with the include unread the caret stood in bar 1 of a part nobody declared.
            var placed = server.PlaceAt(new PlaceAtParams { TextDocument = new TextDocumentIdentifier { Uri = uri }, Offset = text.IndexOf("f g a", StringComparison.Ordinal) });
            Assert.Equal(3, placed.Measure);
            Assert.Equal("m", placed.Part);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { /* the temp folder's own cleaning takes it */ }
        }
    }

    [Fact]
    public void ADocumentsPath_IsTheSame_HoweverItsDriveIsWritten()
    {
        Assert.Equal(string.Empty, LilySharpLanguageServer.FilePathOf(new Uri("untitled:Untitled-1")));
        Assert.Equal(string.Empty, LilySharpLanguageServer.FilePathOf(null));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal("/home/me/a song.lys", LilySharpLanguageServer.FilePathOf(new Uri("file:///home/me/a%20song.lys")));
            return;
        }
        const string want = @"c:\Users\me\a song.lys";
        Assert.Equal(want, LilySharpLanguageServer.FilePathOf(new Uri("file:///c%3A/Users/me/a%20song.lys")));    // VS Code
        Assert.Equal(want, LilySharpLanguageServer.FilePathOf(new Uri("file:///c:/Users/me/a%20song.lys")));
        Assert.Equal(want, LilySharpLanguageServer.FilePathOf(new Uri(want)));
        Assert.Equal(@"C:\Users\me\a song.lys", LilySharpLanguageServer.FilePathOf(new Uri("file:///C%3A/Users/me/a%20song.lys")));
        // A share keeps its host.
        Assert.Equal(@"\\server\share\a.lys", LilySharpLanguageServer.FilePathOf(new Uri("file://server/share/a.lys")));
    }

    /// <summary>Nothing in the server reads <see cref="Uri.LocalPath"/> for itself: a new reader
    /// that did would be right in every test (they name documents by <c>new Uri(path)</c>) and
    /// wrong in the editor.</summary>
    [Fact]
    public void TheServer_ReadsADocumentsPathInOnePlace()
    {
        string lsp = Path.Combine(RepoRoot(), "LilySharp.Lsp");
        var readers = Directory.EnumerateFiles(lsp, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"\.LocalPath\b").Select(_ => Path.GetFileName(f)))
            .Distinct().ToArray();
        Assert.Equal(["LilySharpLanguageServer.FilePath.cs"], readers);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "LilySharp.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("the repo's root was not found");
    }
}
