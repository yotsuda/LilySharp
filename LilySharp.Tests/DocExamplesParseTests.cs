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
using System.IO;
using System.Linq;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Guards against doc-vs-code drift: every fenced code example in the LLM-facing
/// language spec must actually parse with NO errors. This is the regression net for
/// the exact failure found while writing the spec — SYNTAX_REFERENCE.md shipped three
/// examples the parser rejects. A frozen public grammar spec that claims forms the
/// parser refuses is worse than none, so this test keeps the spec honest.
/// </summary>
[Trait("Category", "Unit")]
public class DocExamplesParseTests
{
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

    /// <summary>Extracts the contents of every PLAIN ```-fenced block (no info string)
    /// in a markdown file. Blocks with a language tag (e.g. ```text) are NOT Lily# code
    /// and are skipped — that is how a non-code listing (the reserved-word table) opts out.</summary>
    private static IEnumerable<(int Index, string Code)> FencedBlocks(string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        int i = 0, blockIndex = 0;
        while (i < lines.Length)
        {
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("```"))
            {
                bool isPlain = trimmed.TrimEnd() == "```"; // no language/info string
                var body = new List<string>();
                i++;
                while (i < lines.Length && !lines[i].TrimStart().StartsWith("```"))
                    body.Add(lines[i++]);
                i++; // closing fence
                if (isPlain)
                    yield return (blockIndex++, string.Join("\n", body));
            }
            else i++;
        }
    }

    /// <summary>
    /// Every example must be EITHER a complete document, OR music that is valid once placed
    /// in a part. Most of the spec's blocks are the second kind — showing four lines of
    /// part/section/form/score around every two-note illustration would bury what each one
    /// teaches — so the doc says once that its music blocks are section bodies.
    ///
    /// The two kinds are told apart by the parser rather than by a keyword heuristic: a block
    /// whose ONLY complaint is LYS0020 (music at the top level) is a fragment, and is then
    /// required to parse clean inside <see cref="MusicSource.Wrap"/>. Any other diagnostic —
    /// or a diagnostic that survives the wrapping — is still a failure, so the test keeps its
    /// original teeth: it cannot be satisfied by a block that is merely broken in a new way.
    /// </summary>
    [Fact]
    public void GrammarForLlm_AllExamplesParse()
    {
        var path = RepoFile("docs/GRAMMAR_FOR_LLM.md");
        var md = File.ReadAllText(path);

        var failures = new List<string>();
        foreach (var (index, code) in FencedBlocks(md))
        {
            if (string.IsNullOrWhiteSpace(code))
                continue;

            var tree = SyntaxTree.Parse(code);
            if (!tree.HasErrors)
                continue;

            bool isFragment = tree.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .All(d => d.Code == DiagnosticCodes.TopLevelMusic);
            if (isFragment)
            {
                var wrapped = SyntaxTree.Parse(MusicSource.Wrap(code));
                if (!wrapped.HasErrors)
                    continue;
                var wrappedMsgs = string.Join(" | ", wrapped.Diagnostics.Select(d => d.Message));
                failures.Add($"block #{index} (as a section body):\n{code}\n  -> {wrappedMsgs}");
                continue;
            }

            var msgs = string.Join(" | ", tree.Diagnostics.Select(d => d.Message));
            failures.Add($"block #{index}:\n{code}\n  -> {msgs}");
        }

        Assert.True(failures.Count == 0,
            "GRAMMAR_FOR_LLM.md has code examples the parser rejects:\n\n" +
            string.Join("\n\n", failures));
    }
}
