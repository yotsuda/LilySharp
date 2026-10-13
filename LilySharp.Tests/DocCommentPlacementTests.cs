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
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// No member is inserted between another member's doc comment and its declaration.
/// </summary>
/// <remarks>
/// The shape it leaves is one doc comment closing and the next one opening on the very next
/// line: C# joins the two, the first member loses its documentation, and the second reads
/// someone else's. It happened again and again in this repository — 109 sites when it was
/// first counted (2026-10-13), the latest three in one session — so the check is mechanical.
/// The fix is to move the stranded comment back down onto its own member.
/// </remarks>
[Trait("Category", "Unit")]
public class DocCommentPlacementTests
{
    private static readonly Regex Closes = new(@"^\s*///.*</(summary|remarks)>\s*$", RegexOptions.Compiled);
    private static readonly Regex Opens = new(@"^\s*///\s*<summary>", RegexOptions.Compiled);

    /// <summary>The 1-based lines where a doc comment opens right under another one's close.</summary>
    internal static IEnumerable<int> StrandedComments(IReadOnlyList<string> lines)
    {
        for (int i = 1; i < lines.Count; i++)
            if (Closes.IsMatch(lines[i - 1]) && Opens.IsMatch(lines[i]))
                yield return i + 1;
    }

    [Fact]
    public void NoDocCommentIsStrandedAboveAnotherMembersComment()
    {
        string root = CollectResumeTests.FindRepoRoot();
        var found = new List<string>();
        foreach (string project in new[] { "LilySharp.Core", "LilySharp.Lsp", "LilySharp.Cli", "LilySharp.Tests" })
        {
            string dir = Path.Combine(root, project);
            if (!Directory.Exists(dir))
                continue;
            foreach (string file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(root, file);
                if (relative.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    || relative.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                    continue;
                foreach (int line in StrandedComments(File.ReadAllLines(file)))
                    found.Add($"{relative}:{line}");
            }
        }
        Assert.True(found.Count == 0,
            "a member was inserted between another member's doc comment and its declaration — move the "
            + "stranded comment down onto its own member:\n" + string.Join("\n", found));
    }

    [Fact]
    public void TheCheck_SeesTheShape()
    {
        string[] stranded =
        [
            "    /// <summary>The first member's comment.</summary>",
            "    /// <summary>The inserted member's comment.</summary>",
            "    private static int Inserted() => 0;",
            "",
            "    private static int First() => 1;",
        ];
        Assert.Equal([2], StrandedComments(stranded));
        string[] fine =
        [
            "    /// <summary>One.</summary>",
            "    /// <remarks>Its remarks.</remarks>",
            "    private static int One() => 1;",
        ];
        Assert.Empty(StrandedComments(fine));
    }
}
