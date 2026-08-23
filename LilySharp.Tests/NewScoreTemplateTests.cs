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
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The file "File ▸ New File… ▸ Lily# Score" produces — the extension's
/// <c>NEW_SCORE_TEMPLATE</c>, the very first Lily# most writers ever see.
/// </summary>
/// <remarks>
/// ⚠️ It lives in TypeScript, so nothing in this suite compiled it and nothing in this
/// suite could: it was written once, in a language this repo does not test, and read only
/// by whoever opened a new file. This net reads it out of the source and puts it through
/// the same bar every example in the docs has to clear (<c>DocExamplesParseTests</c>) —
/// with no fragment allowances, because this one IS a whole file.
/// <para>
/// ⚠️ The extraction is deliberately brittle. If the constant stops being a plain template
/// literal the regex stops matching and this test goes RED rather than quietly checking
/// nothing — the empty-set trap RULES §5.4 names, which for a net that reads another
/// language's source is the likely failure, not a hypothetical one.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class NewScoreTemplateTests
{
    /// <summary>The template literal's text, with the .ts file's CRLFs normalized — a
    /// JavaScript template literal normalizes its own line terminators to LF, so the string
    /// the editor inserts is LF whatever the checkout did to the source.</summary>
    private static string NewScoreTemplate()
    {
        var ts = File.ReadAllText(RepoFile("editors/vscode/src/extension.ts"));
        var match = Regex.Match(ts, @"const NEW_SCORE_TEMPLATE = `([^`]*)`", RegexOptions.Singleline);

        Assert.True(match.Success,
            "NEW_SCORE_TEMPLATE is no longer a plain template literal in extension.ts — this "
          + "test reads it as text and cannot see whatever it became.");

        return match.Groups[1].Value.Replace("\r\n", "\n");
    }

    [Fact]
    public void TheNewFileTemplate_IsACompleteScoreOnItsOwn()
    {
        var template = NewScoreTemplate();

        var tree = SyntaxTree.Parse(template);
        Assert.False(tree.HasErrors,
            $"the new-file template does not parse: {string.Join(" | ", tree.Diagnostics.Select(d => d.Message))}");

        var semantic = LilySharp.Core.Semantics.SemanticValidation.Run(tree)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.True(semantic.Count == 0,
            $"the new-file template does not compile: {string.Join(" | ", semantic.Select(d => d.Message))}");
    }

    [Fact]
    public void TheNewFileTemplate_IndentsLikeTheCorpusDoes()
    {
        // Same rule as the `template-…` completions: two spaces, no tabs — 545 of the 548
        // tracked .lys files that indent at all, and no file at all uses a tab. The two
        // templates are handed to the writer by the same editor and must not disagree
        // about what a Lily# file looks like.
        var template = NewScoreTemplate();

        Assert.DoesNotContain("\t", template);
        foreach (var line in template.Split('\n').Where(l => l.StartsWith(' ')))
        {
            int indent = line.Length - line.TrimStart(' ').Length;
            Assert.True(indent % 2 == 0,
                $"the new-file template indents a line by {indent} spaces, not a level of two: {line}");
        }
    }

    [Fact]
    public void TheNewFileTemplate_StaysABlankPageRatherThanAWorkedExample()
    {
        // ⚠️ A ratchet on the SIZE, which is the whole point of this template and the one
        // property no other test here can see. It was the full Twinkle — lyrics, verses, a
        // |: :| repeat and six paragraphs of teaching comments — and the first act on a new
        // file was deleting 40 lines. The worked examples belong to the `template-…`
        // completions, which replace the file on request; this one is what a writer starts
        // typing over.
        //
        // ⚠️ Raising the ceiling to make this pass is the move it exists to stop. If a new
        // line genuinely belongs here, something else has to leave.
        var lines = NewScoreTemplate().TrimEnd('\n').Split('\n').Length;

        Assert.True(lines <= 20,
            $"the new-file template has grown to {lines} lines. It is meant to be the "
          + "smallest complete piece — a blank page, not a lesson.");
    }

    [Fact]
    public void TheNewFileTemplate_OffersExactlyOneSelectableTitlePlaceholder()
    {
        // The command selects the word inside `title "…"` so the first keystroke names the
        // piece. That is a promise about the TEMPLATE, and nothing else in this suite makes
        // it: with no title line, or with `title ""`, the selection quietly does not happen
        // and the writer is simply left at the top of the file — a failure with no symptom.
        //
        // ⚠️ The regex here is deliberately the same shape as the extension's. Two of them
        // is one too many, but the alternative is executing TypeScript, and the pair is
        // pinned by this test failing the moment the template stops matching either.
        var matches = Regex.Matches(NewScoreTemplate(), @"(?m)^title ""([^""]*)""");

        Assert.True(matches.Count == 1,
            $"the new-file template has {matches.Count} top-level `title \"…\"` lines; the "
          + "command selects the first, so it wants exactly one.");
        Assert.True(matches[0].Groups[1].Value.Length > 0,
            "the new-file template's title is empty, so there is nothing to select — a "
          + "placeholder the writer types over is the point of it.");
    }

    [Fact]
    public void TheExtension_StillSelectsThatPlaceholder()
    {
        // The seam, same as the one ScoreTemplateCompletionTests keeps on the command name:
        // the test above guards what the template offers, and this guards that anything is
        // still reaching for it. Deleting the selection would otherwise leave every test
        // here green.
        var extension = File.ReadAllText(RepoFile("editors/vscode/src/extension.ts"));

        Assert.Contains("newScoreTitleRange(doc)", extension);
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
