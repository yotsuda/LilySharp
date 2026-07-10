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

using LilySharp.Core.Svg;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Selecting a render by a name that no longer exists (e.g. a stale preview
/// selection after a score was renamed) must fall back to the first score, not
/// crash the layout with an empty score.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SvgGeneratorFallbackTests
{
    private const string TwoScores =
        "part bl { clef bass tuning bass }\n" +
        "section A { bl { c4 d e f | } }\n" +
        "form main { A }\n" +
        "score main { staff bl  tab bl }\n" +
        "score main { tab bl }\n";

    [Fact]
    public void UnknownRenderName_FallsBackToFirstScore()
    {
        var tree = SyntaxTree.Parse(TwoScores);
        var svg = SvgGenerator.Generate(tree, null, "doesNotExist");
        Assert.False(string.IsNullOrWhiteSpace(svg));
    }

    [Fact]
    public void KnownRenderName_StillRenders()
    {
        var tree = SyntaxTree.Parse(TwoScores);
        Assert.False(string.IsNullOrWhiteSpace(SvgGenerator.Generate(tree, null, "tab2")));
    }
}
