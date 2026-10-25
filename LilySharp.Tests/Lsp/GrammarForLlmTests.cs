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

using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LilySharp.Lsp;
using LilySharp.Lsp.Protocol;
using Xunit;

namespace LilySharp.Tests.Lsp;

/// <summary>
/// GRAMMAR_FOR_LLM.md is what the AI transform sends the model as the language (aiTransform.ts,
/// loadGrammar). Two things the general doc tests do not hold it to: its "Common requests"
/// answers are what the model copies, so each must be clean — no warnings either, judged by the
/// same check the AI's candidates get; and it must name every annotation the human reference
/// names, or the model never learns that one (2026-10-25: four were missing).
/// </summary>
[Trait("Category", "Unit")]
public class GrammarForLlmTests
{
    private static string Doc(string relative) =>
        File.ReadAllText(Path.Combine(CollectResumeTests.FindRepoRoot(), relative)).Replace("\r\n", "\n");

    [Fact]
    public void CommonRequests_AreWholeFilesTheCheckFindsNothingIn()
    {
        var md = Doc("docs/GRAMMAR_FOR_LLM.md");
        var section = Regex.Match(md, @"^## Common requests.*?(?=^## )", RegexOptions.Singleline | RegexOptions.Multiline);
        Assert.True(section.Success, "GRAMMAR_FOR_LLM.md has no \"## Common requests\" section");
        var blocks = Regex.Matches(section.Value, @"^```\n(.*?)^```", RegexOptions.Singleline | RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value).ToList();
        Assert.True(blocks.Count >= 5, $"expected the recipes' examples, found {blocks.Count}");

        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        var dirty = blocks
            .Select(code => (code, diags: server.CheckCandidate(new CheckCandidateParams { Text = code }).Diagnostics
                .Where(d => d.Severity is "error" or "warning" && !d.Message.Contains("is not installed on this system"))
                .ToList()))
            .Where(b => b.diags.Count > 0)
            .Select(b => b.code + "\n  -> " + string.Join(" | ", b.diags.Select(d => $"{d.Code}: {d.Message}")))
            .ToList();
        Assert.True(dirty.Count == 0, "Common requests examples the check complains about:\n\n" + string.Join("\n\n", dirty));
    }

    [Fact]
    public void NamesEveryAnnotationTheReferenceNames()
    {
        var llm = Doc("docs/GRAMMAR_FOR_LLM.md");
        // Lower-case initial: the reference also shows a wrong spelling, `@Staccato`, to refuse it.
        var missing = Regex.Matches(Doc("docs/SYNTAX_REFERENCE.md"), @"@[a-z][a-zA-Z]*")
            .Select(m => m.Value).Distinct()
            .Where(a => !Regex.IsMatch(llm, Regex.Escape(a) + "(?![a-zA-Z])"))
            .OrderBy(a => a)
            .ToList();
        Assert.True(missing.Count == 0, "GRAMMAR_FOR_LLM.md never names: " + string.Join(" ", missing));
    }
}
