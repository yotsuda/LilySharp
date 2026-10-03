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
using System.Text.RegularExpressions;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// CHANGELOG.md is the GitHub Release body (release.yml copies the topmost section
/// verbatim), and GitHub reads a bare <c>@word</c> in a release body as a USER MENTION:
/// the v0.10.0 page listed the GitHub users <c>finger</c>, <c>sf</c> and <c>chord</c> as
/// the release's contributors, from <c>write @finger(3) and @chord(c)</c> and
/// <c>'@sf'</c> quoted inside an italic message (owner report, 2026-10-03; v0.7.0 listed
/// <c>feather</c> and <c>chord</c> the same way). An annotation name is written in a code
/// span — <c>`@chord(c)`</c> — which GitHub leaves alone.
/// </summary>
public class ChangelogMentionTests
{
    // A `@name` outside a code span: at the line start or after a non-word character, as
    // GitHub's own mention rule reads it (`c4@ho` inside a code span is stripped first;
    // an e-mail-like `x@y` is not a mention and is not flagged).
    private static readonly Regex BareMention = new(@"(^|[^\w`])@[A-Za-z][\w-]*", RegexOptions.Compiled);
    private static readonly Regex CodeSpan = new("`[^`]*`", RegexOptions.Compiled);

    [Fact]
    public void NoAnnotationNameIsWrittenWhereGitHubReadsAMention()
    {
        var path = Path.Combine(CollectResumeTests.FindRepoRoot(), "CHANGELOG.md");
        var offenders = new List<string>();
        bool inFence = false;
        int number = 0;
        foreach (var line in File.ReadLines(path))
        {
            number++;
            if (line.TrimStart().StartsWith("```"))
            {
                inFence = !inFence;
                continue;
            }
            if (inFence)
                continue;
            if (BareMention.IsMatch(CodeSpan.Replace(line, "")))
                offenders.Add($"{number}: {line.Trim()}");
        }
        Assert.True(offenders.Count == 0,
            "CHANGELOG.md writes an @name outside a code span; GitHub shows it as a release contributor. "
            + "Wrap it in backticks:\n  " + string.Join("\n  ", offenders));
    }
}
