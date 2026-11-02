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

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Syntax;
using LilySharp.Lsp;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The path Copilot's chat teaches Lily# from (docs/LEARN_FOR_LLM.md; owner's request 2026-11-02): what it
/// tells a person must be true, so every example compiles clean and draws, as a lesson must, and every
/// lesson it names is one the editor offers.
/// </summary>
[Trait("Category", "Unit")]
public class LearnGuideTests
{
    private static readonly string Guide = File.ReadAllText(Path.Combine(CollectResumeTests.FindRepoRoot(), "docs", "LEARN_FOR_LLM.md")).Replace("\r\n", "\n");

    public static IEnumerable<object[]> Examples() =>
        Regex.Matches(Guide, "```lys\n(.*?)```", RegexOptions.Singleline).Select((m, i) => new object[] { i + 1, m.Groups[1].Value });

    [Theory]
    [MemberData(nameof(Examples))]
    public void AnExample_CompilesWithNoErrorAndNoWarning_AndDraws(int number, string text)
    {
        var tree = SyntaxTree.Parse(text);
        var said = tree.Diagnostics.Concat(SemanticValidation.Run(tree)).ToList();
        Assert.True(said.Count == 0, $"example {number}:\n" + string.Join("\n", said.Select(d => $"  {d.Code}: {d.Message}")));
        Assert.All(SvgGenerator.GenerateAll(tree), o => Assert.False(string.IsNullOrWhiteSpace(o.Svg)));
    }

    [Fact]
    public void ItHasItsExamples() => Assert.True(Examples().Count() >= 12);

    [Fact]
    public void TheStages_AreNumberedInOrder_AndTheListAnswersFromThem()
    {
        var stages = Regex.Matches(Guide, @"^### Stage (\d+) — ", RegexOptions.Multiline).Select(m => int.Parse(m.Groups[1].Value)).ToList();
        Assert.Equal(Enumerable.Range(1, stages.Count), stages);
        var list = Guide[Guide.IndexOf("## Which stage answers it", System.StringComparison.Ordinal)..];
        foreach (Match row in Regex.Matches(list, @"\| (\d+)(?:, then the grammar)? \|$", RegexOptions.Multiline))
            Assert.Contains(int.Parse(row.Groups[1].Value), stages);
        Assert.Equal(stages, Regex.Matches(list, @"\| (\d+)(?:, then the grammar)? \|$", RegexOptions.Multiline).Select(m => int.Parse(m.Groups[1].Value)));
    }

    [Fact]
    public void EveryLesson_IsNamed_InOrder_AndNoOther()
    {
        var named = Regex.Matches(Guide, @"\*\*Lesson:\*\* `(lesson-[\w-]+)`").Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(LilySharpLanguageServer.Lessons.Select(l => l.Label), named);
    }

    // What it says of the editor is the manifest's: a renamed command falsifies it.
    [Fact]
    public void TheCommandsItNames_AreTheExtensions()
    {
        string manifest = File.ReadAllText(Path.Combine(CollectResumeTests.FindRepoRoot(), "editors", "vscode", "package.json"));
        foreach (Match m in Regex.Matches(Guide, @"\*\*Lily#: ([^*]+?)\*\*"))
            Assert.Contains($"\"title\": \"{m.Groups[1].Value}\"", manifest);
        Assert.Contains("Lily#: New Score", Guide);
        Assert.Contains("Lily#: Export as PDF", Guide);
    }
}
