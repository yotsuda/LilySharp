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
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Guards against doc-vs-code drift: every fenced code example in the language
/// documentation must actually COMPILE, not merely parse. A public grammar spec that
/// claims forms the compiler refuses is worse than none, so this test keeps the docs
/// honest.
/// </summary>
/// <remarks>
/// ⚠️ For a long time this read ONE file — GRAMMAR_FOR_LLM.md — while SYNTAX_REFERENCE.md
/// (the canonical spec) and TUTORIAL.md had zero observers, and both had drifted. What
/// the day this net was widened (2026-08-15) found in the two unwatched files:
/// <list type="bullet">
/// <item>SYNTAX_REFERENCE.md wrote every example's comment with LilyPond's <c>%</c>,
///   which is not a Lily# token at all (LYS0018) — 33 of its 57 blocks.</item>
/// <item>its "Navigation Marks" block taught <c>c4@segno</c> and friends, all five
///   LYS1022 — and the engine engraves them anyway, so no output ever looked wrong.</item>
/// <item>its parallel-voices block held two <c>voice</c> spans in one block, i.e. the
///   LYS0019 that the paragraph beneath it explains.</item>
/// <item>TUTORIAL.md's only multi-staff example — the first thing a reader copies —
///   used the removed <c>clef:</c> form and <c>staff treble { rightHand }</c>.</item>
/// </list>
/// ⚠️ <b>GRAMMAR.md is still unobserved and cannot be added here:</b> it has ZERO plain
/// fenced blocks (measured). Its examples live inside EBNF <c>(* … *)</c> comments, so
/// reaching them needs a different extractor, not another entry in the list below.
/// </remarks>
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

    /// <summary>
    /// Every Lily# example a markdown file offers, in the two shapes the docs actually use.
    /// </summary>
    /// <remarks>
    /// ⚠️ Both shapes are OPT-IN by their own syntax, never by a heuristic that decides
    /// whether a run of text "looks like code" — a checker that guesses is the kind
    /// RULES §5.2.1⑦ forbids, and here it would be reading prose out of GRAMMAR.md.
    /// </remarks>
    private static IEnumerable<(int Index, string Code)> Examples(string markdown)
    {
        int blockIndex = 0;
        foreach (var code in FencedBlocks(markdown))
            yield return (blockIndex++, code);
        foreach (var code in EbnfExampleBlocks(markdown))
            yield return (blockIndex++, code);
    }

    /// <summary>Contents of every ```-fenced block that claims to be Lily# — either PLAIN
    /// (no info string) or tagged <c>lilysharp</c> / <c>lys</c>. Any other language tag
    /// (```text, ```bash) is NOT Lily# and is skipped — that is how a non-code listing
    /// (the reserved-word table, a shell transcript) opts out.</summary>
    /// <remarks>
    /// ⚠️ The tagged spelling is not decoration: GRAMMAR.md's ONLY fenced example is
    /// ```lilysharp, so before 2026-08-15 the "plain fence" rule alone read zero of that
    /// file — which was misreported as "GRAMMAR.md has no fenced examples".
    /// </remarks>
    private static IEnumerable<string> FencedBlocks(string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        int i = 0;
        while (i < lines.Length)
        {
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("```"))
            {
                var tag = trimmed.TrimEnd()[3..].Trim();
                bool isLilySharp = tag.Length == 0 || tag is "lilysharp" or "lys";
                var body = new List<string>();
                i++;
                while (i < lines.Length && !lines[i].TrimStart().StartsWith("```"))
                    body.Add(lines[i++]);
                i++; // closing fence
                if (isLilySharp)
                    yield return string.Join("\n", body);
            }
            else i++;
        }
    }

    /// <summary>
    /// Contents of every <c>(* Example…: … *)</c> block — how GRAMMAR.md illustrates the
    /// production it has just stated, since its EBNF is plain markdown rather than a fence.
    /// The header line (<c>Example:</c>, <c>Examples:</c>, or <c>Example (why this one):</c>)
    /// is dropped; everything up to <c>*)</c> is the code.
    /// </summary>
    /// <remarks>
    /// ⚠️ The rule is "an Example block is code, ALL of it" — deliberately, so that no
    /// heuristic has to tell code from prose. The one block that mixed the two (the
    /// parallel-voices example, three paragraphs of specification under two lines of
    /// code) was split rather than guessed at, which is what made the rule true before it
    /// was enforced. Keep it true: prose belongs in a comment of its own.
    /// </remarks>
    private static IEnumerable<string> EbnfExampleBlocks(string markdown)
    {
        var text = markdown.Replace("\r\n", "\n");
        foreach (Match m in Regex.Matches(text, @"\(\*\s*Examples?\b[^:]*:(.*?)\*\)", RegexOptions.Singleline))
            yield return m.Groups[1].Value;
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
    [Theory]
    [InlineData("docs/GRAMMAR_FOR_LLM.md")]
    [InlineData("docs/SYNTAX_REFERENCE.md")]
    [InlineData("docs/TUTORIAL.md")]
    [InlineData("docs/GRAMMAR.md")]
    public void DocExamples_AllCompile(string relative)
    {
        var path = RepoFile(relative);
        var md = File.ReadAllText(path);

        var failures = new List<string>();
        foreach (var (index, code) in Examples(md))
        {
            if (string.IsNullOrWhiteSpace(code))
                continue;

            var tree = SyntaxTree.Parse(code);
            if (!tree.HasErrors)
            {
                var semantic = SemanticErrors(tree);
                if (semantic == null)
                    continue;
                failures.Add($"block #{index} (SEMANTIC):\n{code}\n  -> {semantic}");
                continue;
            }

            bool isFragment = tree.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .All(d => d.Code == DiagnosticCodes.TopLevelMusic);
            if (isFragment)
            {
                var wrapped = SyntaxTree.Parse(MusicSource.Wrap(code));
                if (!wrapped.HasErrors)
                {
                    var semantic = SemanticErrors(wrapped);
                    if (semantic == null)
                        continue;
                    failures.Add($"block #{index} (as a section body, SEMANTIC):\n{code}\n  -> {semantic}");
                    continue;
                }
                var wrappedMsgs = string.Join(" | ", wrapped.Diagnostics.Select(d => d.Message));
                failures.Add($"block #{index} (as a section body):\n{code}\n  -> {wrappedMsgs}");
                continue;
            }

            var msgs = string.Join(" | ", tree.Diagnostics.Select(d => d.Message));
            failures.Add($"block #{index}:\n{code}\n  -> {msgs}");
        }

        Assert.True(failures.Count == 0,
            $"{relative} has code examples Lily# rejects:\n\n" +
            string.Join("\n\n", failures));
    }

    /// <summary>
    /// The semantic errors of an example that already PARSES, or null when there are none.
    /// </summary>
    /// <remarks>
    /// Parsing was never the whole bar. The spec's only <c>override</c> example wrote
    /// <c>Stem.length</c>, which parses perfectly and which the engine does not read — once
    /// LYS1029 gave that a voice, the canonical spec was teaching three lines the compiler
    /// refuses, and this test stayed green throughout because it stopped at the parser.
    /// A spec that claims forms the compiler rejects is the exact failure this file exists
    /// to prevent, so the bar is now "compiles", not "parses".
    ///
    /// ⚠️ ERRORS only. Warnings are deliberately allowed: an illustration of two notes has
    /// no reason to fill a 4/4 bar, and demanding it would push padding rests into examples
    /// whose whole job is to show one idea.
    /// </remarks>
    private static string? SemanticErrors(SyntaxTree tree)
    {
        var errors = LilySharp.Core.Semantics.SemanticValidation.Run(tree)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Where(d => !FragmentCodes.Contains(d.Code))
            .ToList();
        return errors.Count == 0 ? null : string.Join(" | ", errors.Select(d => d.Message));
    }

    /// <summary>
    /// Errors that mean "this example is an EXCERPT", not "this example is wrong. An
    /// illustration of the form syntax names sections it does not define, and one of a
    /// grand staff names parts declared elsewhere — spelling those out in full would bury
    /// what each example teaches, which is the same reason the parse side accepts a bare
    /// section body.
    /// </summary>
    /// <remarks>
    /// ⚠️ Kept as a NAMED, closed list rather than "ignore semantic errors": with it, the
    /// net still catches the two defects it was added for — the spec's first example wrote
    /// <c>partial</c> at the top level, which LYS1024 refuses and whose comment ("once for
    /// every part") is the very misconception that error corrects, and its only
    /// <c>override</c> example wrote <c>Stem.length</c>, which LYS1029 refuses. Both parsed
    /// perfectly, which is why this file stayed green while the spec drifted.
    ///
    /// ★ What the line costs, measured when the net was widened (2026-08-15) by removing
    /// this set and re-running: GRAMMAR_FOR_LLM.md 6 blocks fail, SYNTAX_REFERENCE.md 8,
    /// TUTORIAL.md 0. All 14 are genuine excerpts — a <c>form</c> naming sections defined
    /// elsewhere, a <c>score</c> naming parts declared elsewhere — so the exclusions buy
    /// exactly what they were meant to and nothing else. Anyone tempted to widen this set
    /// further should count first: that is how the 2026-08-15 pass knew the '@segno' block
    /// was a real defect rather than one more excerpt.
    /// </remarks>
    private static readonly HashSet<string> FragmentCodes = new()
    {
        DiagnosticCodes.UndefinedSection,
        DiagnosticCodes.UndefinedPart,
        DiagnosticCodes.UndefinedVariable,
        DiagnosticCodes.UndefinedPhrase,
        DiagnosticCodes.UnknownFormReference,
    };
}
