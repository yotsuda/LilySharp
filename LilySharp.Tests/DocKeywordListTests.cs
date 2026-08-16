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
using System.Text.RegularExpressions;
using LilySharp.Core.Parser;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The three documents that publish a list of RESERVED WORDS, against the one table that
/// decides them (<c>Lexer.GetKeywordKind</c>). Both directions: nothing listed may be free,
/// and nothing reserved may be missing.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ These lists had no observer at all until 2026-08-16, and all three had rotted. Measured
/// that day, word by word, by asking whether each can name a part: GRAMMAR.md listed
/// <c>structure</c>, <c>use</c> and <c>let</c>, SYNTAX_REFERENCE.md listed those plus
/// <c>include</c>, <c>chordnames</c> and <c>tabStaff</c>, and GRAMMAR_FOR_LLM.md listed
/// <c>structure</c> and <c>include</c> — none of which is reserved; a part may be called any
/// of them. Sixteen words that ARE reserved were missing from every list.
/// </para>
/// <para>
/// ⚠️ <c>structure</c> and <c>render</c> stopped being keywords when they became
/// <c>form</c> and <c>score</c>. <c>DocExamplesParseTests</c> could not see this: a keyword
/// LIST is neither an Example block nor a fenced example, and a production is not code.
/// That is the third shape of doc rot this repo has met — after examples that no longer
/// compile and productions that disagree with the parser — and the first with a machine.
/// </para>
/// <para>
/// ⚠️ The reverse direction is the one that found the sixteen. It is expressible because
/// every <c>*Keyword</c> kind but one comes from that single table; <see cref="Stitched"/>
/// names the exception and why.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class DocKeywordListTests
{
    /// <summary>The one <c>*Keyword</c> kind no bare word produces: <c>treble^8</c> is
    /// stitched in <c>Lexer.ScanWord</c> because <c>^</c> stops the word scan (its
    /// underscore twin <c>treble_8</c> needs no stitching).</summary>
    private static readonly SyntaxKind[] Stitched = [SyntaxKind.Treble8UpKeyword];

    private static string Doc(string relative) =>
        File.ReadAllText(Path.Combine(CollectResumeTests.FindRepoRoot(), relative));

    /// <summary>The words a document publishes as reserved.</summary>
    private static string[] Listed(string relative) => relative switch
    {
        // The EBNF production: every quoted terminal of `Keyword = … ;`.
        "docs/GRAMMAR.md" => Words(
            Regex.Match(Doc(relative), @"^Keyword = (.*?);", RegexOptions.Singleline | RegexOptions.Multiline)
                .Groups[1].Value, @"'([^']+)'"),
        // The ```text fence under "Keywords:" — bare words, with the parenthetical note dropped.
        "docs/GRAMMAR_FOR_LLM.md" => Words(
            Regex.Replace(
                Regex.Match(Doc(relative), @"Keywords:\s*```text\r?\n(.*?)```", RegexOptions.Singleline)
                    .Groups[1].Value,
                @"\([^)]*\)", " "),
            @"([A-Za-z_][A-Za-z0-9_]*)"),
        // The table: every backticked word in the rows of the keyword table.
        "docs/SYNTAX_REFERENCE.md" => Words(
            Regex.Match(Doc(relative), @"\| Group \| Words \|(.*?)\r?\n\r?\n", RegexOptions.Singleline)
                .Groups[1].Value, "`([^`]+)`"),
        _ => throw new ArgumentOutOfRangeException(nameof(relative)),
    };

    private static string[] Words(string text, string pattern)
    {
        Assert.False(string.IsNullOrWhiteSpace(text),
            "the keyword list did not extract — the document's shape moved, and an empty "
            + "list would make every assertion below vacuously true");
        return Regex.Matches(text, pattern)
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static SyntaxKind KindOf(string word) =>
        new Lexer(word).ScanAllTokens().First().Kind;

    [Theory]
    [InlineData("docs/GRAMMAR.md")]
    [InlineData("docs/GRAMMAR_FOR_LLM.md")]
    [InlineData("docs/SYNTAX_REFERENCE.md")]
    public void EveryWordTheDocCallsReserved_IsReserved(string relative)
    {
        var listed = Listed(relative);
        Assert.True(listed.Length > 40, $"{relative}: only {listed.Length} words extracted");

        var free = listed.Where(w => KindOf(w) == SyntaxKind.Identifier).ToArray();
        Assert.True(free.Length == 0,
            $"{relative} calls these reserved, and they are not — a part may be named any of "
            + $"them: {string.Join(", ", free)}");
    }

    [Theory]
    [InlineData("docs/GRAMMAR.md")]
    [InlineData("docs/GRAMMAR_FOR_LLM.md")]
    [InlineData("docs/SYNTAX_REFERENCE.md")]
    public void EveryReservedWord_IsInTheDoc(string relative)
    {
        // Every *Keyword kind the lexer can hand back from a bare word must have a spelling
        // in the list; the one that cannot is named above.
        var covered = Listed(relative).Select(KindOf).ToHashSet();
        var missing = Enum.GetValues<SyntaxKind>()
            .Where(k => k.ToString().EndsWith("Keyword", StringComparison.Ordinal))
            .Where(k => !Stitched.Contains(k))
            .Where(k => !covered.Contains(k))
            .ToArray();

        Assert.True(missing.Length == 0,
            $"{relative} is missing a spelling for these reserved kinds: "
            + string.Join(", ", missing));
    }

    [Fact]
    public void TheCheckCanFail()
    {
        // ★ The lists are today's behaviour written down, so they were green the moment they
        // were corrected — which says nothing (RULES §5.4). Both directions are shown to
        // bite: a free word smuggled into a list, and a reserved kind left out of one.
        Assert.Equal(SyntaxKind.Identifier, KindOf("chordnames"));   // was listed, is free
        Assert.NotEqual(SyntaxKind.Identifier, KindOf("form"));      // was missing, is reserved
        Assert.Equal(SyntaxKind.Identifier, KindOf("structure"));    // the rename, in one line
    }
}
