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
/// The published lists of what the language holds, against the code that decides it. Both
/// directions, for both lists: the RESERVED WORDS the three documents print, against
/// <c>Lexer.GetKeywordKind</c>; and the SCORE ITEMS GRAMMAR.md's <c>ScoreItem</c> production
/// and the parser's own stray-item message print, against <c>Parser.ParseRenderItem</c>.
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

    // ───────────────────────────────────────────────────────────────────────────────
    // The OTHER list these documents publish: what a `score { }` body may hold.
    //
    // ⚠️ The reserved-word checks above cannot see this one. `staffGroup` and `choirStaff`
    // ARE reserved and ARE in all three word lists, so those tests were green while the
    // ScoreItem production, the stray-item message and every worked example omitted them —
    // for 62 sessions nothing in the product said what either keyword does. That is the
    // second shape of doc rot this file's own remarks name ("productions that disagree with
    // the parser"), and it had no machine until now.
    // ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Every reserved word, asked one at a time whether <c>ParseRenderItem</c> gives it a
    /// branch of its own. Machine-derived on purpose: a hand-written candidate list is the
    /// thing that rotted, and <see cref="EveryReservedWord_IsInTheDoc"/> already proves the
    /// table this reads covers every keyword kind (RULES §5.0 — enumerate, do not recall).
    /// </summary>
    /// <remarks>
    /// Words that can NAME A PART are excluded: they are accepted inside a score, but by the
    /// bare-part-name branch (a MIDI-only item), not by a branch of their own. That branch is
    /// checked separately by <see cref="TheBarePartNameItem_IsInTheProduction"/> — keeping the
    /// two apart is what makes "the parser accepts it" mean one thing here.
    /// </remarks>
    private static string[] KeywordScoreItems() =>
        Listed("docs/SYNTAX_REFERENCE.md")
            .Where(w => KindOf(w) != SyntaxKind.Identifier)
            .Where(w => !SyntaxFacts.IsPartNameKind(KindOf(w)))
            .Where(w => !IsStrayInsideAScore(w))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(w => w, StringComparer.Ordinal)
            .ToArray();

    private const string ScorePrefix = "score main { ";

    /// <summary>
    /// Does a score body refuse this word — is there an error ON THE WORD ITSELF?
    /// </summary>
    /// <remarks>
    /// ⚠️ Read as "not LYS0030" this question answers wrongly, and the first draft did:
    /// <c>using</c> has a branch in <c>ParseRenderItem</c> whose whole purpose is to REFUSE it
    /// (LYS0029, "a 'using' cannot go inside a score"), so a stray-code test called it
    /// accepted. Anchoring on the span separates the two: a word that reached a real branch
    /// and merely lacks its ARGUMENTS reports on what is missing — <c>score main { staff }</c>
    /// lands on the '}' — while a refused word is reported where it stands.
    /// </remarks>
    private static bool IsStrayInsideAScore(string word)
    {
        var span = new { Start = ScorePrefix.Length, End = ScorePrefix.Length + word.Length };
        return SyntaxTree.Parse($"{ScorePrefix}{word} }}").Diagnostics
            .Any(d => d.Severity == DiagnosticSeverity.Error
                   && d.Span.Start < span.End && d.Span.Start + d.Span.Length > span.Start);
    }

    /// <summary>The spellings GRAMMAR.md's <c>ScoreItem</c> production publishes, resolved one
    /// level: an alternative is either a quoted terminal or the name of another production,
    /// and a named production contributes every terminal it quotes.</summary>
    private static string[] ProductionSpellings()
    {
        string doc = Doc("docs/GRAMMAR.md");
        string block = Regex.Match(doc, @"^ScoreItem\s*=(.*?);",
            RegexOptions.Singleline | RegexOptions.Multiline).Groups[1].Value;
        Assert.False(string.IsNullOrWhiteSpace(block),
            "the ScoreItem production did not extract — the document's shape moved, and an "
            + "empty list would make every assertion below vacuously true");

        var found = new List<string>();
        foreach (string alternative in Strip(block).Split('|'))
        {
            // The FIRST quoted terminal of the alternative is the word that opens the item.
            // ⚠️ Every terminal would over-collect: StaffRender quotes 'with' in its tail,
            // and a first draft published `with` as a score item because of it.
            var terminal = Regex.Match(alternative, @"'([A-Za-z][A-Za-z0-9_]*)'");
            if (terminal.Success) { found.Add(terminal.Groups[1].Value); continue; }

            // An alternative that leads with a NON-TERMINAL names it instead; resolve one
            // level and take that production's own opening terminal (StaffRender -> staff).
            // A production with no terminal at all (PartRef = Identifier) adds nothing, which
            // is right: the bare-name item has no keyword to publish.
            string name = Regex.Match(alternative.Trim(), @"^([A-Za-z][A-Za-z0-9]*)").Groups[1].Value;
            if (name.Length == 0) continue;
            string body = Regex.Match(doc, $@"^{name}\s*=(.*?);",
                RegexOptions.Singleline | RegexOptions.Multiline).Groups[1].Value;
            var resolved = Regex.Match(Strip(body), @"'([A-Za-z][A-Za-z0-9_]*)'");
            if (resolved.Success) found.Add(resolved.Groups[1].Value);
        }
        return found.Distinct(StringComparer.Ordinal).OrderBy(w => w, StringComparer.Ordinal).ToArray();
    }

    /// <summary>(* … *) is commentary, not grammar — a spelling quoted inside one is prose.</summary>
    private static string Strip(string ebnf) =>
        Regex.Replace(ebnf, @"\(\*.*?\*\)", " ", RegexOptions.Singleline);

    /// <summary>The vocabulary the parser itself recites when it refuses an item — the ONE
    /// list a writer actually reads, since it arrives at the moment of the mistake.</summary>
    /// <remarks>
    /// ⚠️ The trigger has to be a RESERVED word with no score branch. An invented name like
    /// <c>zzz</c> does not work: it lexes as an Identifier, so the bare-part-name branch takes
    /// it and no stray item is ever reported (the first draft asserted on a null).
    /// </remarks>
    private static string StrayItemVocabulary()
    {
        var d = SyntaxTree.Parse($"{ScorePrefix}voice }}").Diagnostics
            .FirstOrDefault(x => x.Code == DiagnosticCodes.StrayItemToken);
        Assert.NotNull(d);
        return d!.Message;
    }

    [Fact]
    public void ScoreItemProduction_NamesEveryItemTheParserAccepts()
    {
        string[] accepted = KeywordScoreItems();
        Assert.True(accepted.Length > 8, $"only {accepted.Length} score items measured");

        string[] published = ProductionSpellings();
        string[] missing = accepted.Except(published, StringComparer.Ordinal).ToArray();

        Assert.True(missing.Length == 0,
            "docs/GRAMMAR.md ScoreItem does not publish these, and a score body takes them: "
            + string.Join(", ", missing)
            + $"\n  measured:  {string.Join(" ", accepted)}"
            + $"\n  published: {string.Join(" ", published)}");
    }

    [Fact]
    public void ScoreItemProduction_PublishesNothingTheParserRefuses()
    {
        string[] published = ProductionSpellings();
        Assert.True(published.Length > 8, $"only {published.Length} spellings extracted");

        string[] refused = published.Where(IsStrayInsideAScore).ToArray();
        Assert.True(refused.Length == 0,
            "docs/GRAMMAR.md ScoreItem publishes these, and a score body refuses them: "
            + string.Join(", ", refused));
    }

    [Fact]
    public void TheStrayItemMessage_NamesEveryItemTheParserAccepts()
    {
        string vocabulary = StrayItemVocabulary();
        string[] missing = KeywordScoreItems()
            .Where(w => !Regex.IsMatch(vocabulary, $@"'{Regex.Escape(w)}\b"))
            .ToArray();

        Assert.True(missing.Length == 0,
            "the message a writer gets for a stray score item does not name these, and a "
            + "score body takes them: " + string.Join(", ", missing)
            + $"\n  message: {vocabulary}");
    }

    [Fact]
    public void TheBarePartNameItem_IsInTheProduction()
    {
        // A bare part name renders that part to MIDI only, so a score of nothing but bare
        // names has nothing to engrave — the item is real, and the production has to say so.
        Assert.False(
            SyntaxTree.Parse(
                "part m { clef treble }\nsection A { m { c4 } }\nform main { A }\n"
                + "score main { staff m\n  m }").HasErrors,
            "a bare part name beside a staff is a score item and must parse");

        // ⚠️ A bare `Contains("PartRef")` passes on the tab/ossia/chords alternatives, which
        // all take a PartRef as an ARGUMENT — the claim here is that PartRef is an
        // alternative in its own right, so match the alternative.
        string block = Regex.Match(Doc("docs/GRAMMAR.md"), @"^ScoreItem\s*=(.*?);",
            RegexOptions.Singleline | RegexOptions.Multiline).Groups[1].Value;
        Assert.Matches(@"\|\s*PartRef\b", Strip(block));
    }

    [Fact]
    public void TheScoreItemCheckCanFail()
    {
        // ★ Both directions are shown to bite, on the words that were actually wrong
        // (RULES §5.4 — a ratchet written after the fix is green for free).
        //
        // ⑴ The parser really does give these two a branch: they were absent from the
        //    production, the stray-item message and every example, and nothing was red.
        Assert.False(IsStrayInsideAScore("staffGroup"));
        Assert.False(IsStrayInsideAScore("choirStaff"));
        // ⑵ …and the check can go the other way: a spelling no score body takes.
        Assert.True(IsStrayInsideAScore("voice"));
        Assert.True(IsStrayInsideAScore("section"));
        // ⑶ The extractors are not vacuous — an empty set would pass every assertion above.
        Assert.NotEmpty(ProductionSpellings());
        Assert.NotEmpty(KeywordScoreItems());
    }
}
