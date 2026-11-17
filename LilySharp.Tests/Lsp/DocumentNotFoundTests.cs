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
/// What the server answers about a document it does not hold
/// (<see cref="LilySharpLanguageServer.DocumentNotFound"/>) is ONE text, and the editor's
/// preview reads it: a file just opened is not registered yet, and the preview asks again on
/// exactly that answer. The server wrote the text 23 times, five of them with a full stop
/// (session 1295, review F5) — none of those five was the one the editor compares, by luck.
/// </summary>
public class DocumentNotFoundTests
{
    /// <summary>The editor's comparison is the server's text, letter for letter.</summary>
    [Fact]
    public void ThePreviewsRetry_ComparesTheServersOwnText()
    {
        var ts = File.ReadAllText(RepoFile("editors/vscode/src/extension.ts"));
        var compared = Regex.Matches(ts, @"\.Error\s*===\s*'([^']*not found[^']*)'")
            .Select(m => m.Groups[1].Value).ToArray();
        Assert.NotEmpty(compared);      // (the retry is still written this way: else this test reads nothing)
        Assert.All(compared, text => Assert.Equal(LilySharpLanguageServer.DocumentNotFound, text));
    }

    /// <summary>The requests answer it — the preview's, and a command's that used to end it
    /// with a full stop.</summary>
    [Fact]
    public void ARequestForADocumentTheServerDoesNotHold_AnswersThatText()
    {
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        var nowhere = new TextDocumentIdentifier { Uri = new Uri("file:///nowhere/absent.lys") };

        Assert.Equal(LilySharpLanguageServer.DocumentNotFound, server.GetSvg(new SvgParams { TextDocument = nowhere }).Error);
        Assert.Equal(LilySharpLanguageServer.DocumentNotFound, server.GetPlayback(new PlaybackParams { TextDocument = nowhere }).Error);
        Assert.Equal(LilySharpLanguageServer.DocumentNotFound,
            server.GuessSectionKey(new GuessSectionKeyParams { TextDocument = nowhere }).Error);
        Assert.Equal(LilySharpLanguageServer.DocumentNotFound, server.Regroup(new RegroupParams { TextDocument = nowhere }).Error);
    }

    /// <summary>No source of the server spells the text itself: a second spelling is how the
    /// five full stops came to be.</summary>
    [Fact]
    public void TheServerSpellsTheTextOnce()
    {
        var dir = Path.GetDirectoryName(RepoFile("LilySharp.Lsp/LilySharpLanguageServer.cs"))!;
        var spelled = Directory.GetFiles(dir, "*.cs")
            .SelectMany(f => File.ReadLines(f)
                .Where(line => line.Contains("\"Document not found", StringComparison.OrdinalIgnoreCase))
                .Select(line => $"{Path.GetFileName(f)}: {line.Trim()}"))
            .ToArray();
        var only = Assert.Single(spelled);
        Assert.Contains("const string DocumentNotFound", only);
    }

    private static string RepoFile(string relative)
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new FileNotFoundException($"Cannot find {relative} walking up from {AppContext.BaseDirectory}");
    }
}
