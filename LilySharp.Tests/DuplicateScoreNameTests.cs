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

using System.Linq;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
public sealed class DuplicateScoreNameTests
{
    private const string Head =
        "part bl { clef bass tuning bass }\nsection Main { bl { a4 b c d | } }\nstructure { Main }\n";

    [Fact]
    public void ScoreName_MayBeABareIdentifier()
    {
        // `score foo { ... }` — no quotes needed for a simple name.
        var tree = SyntaxTree.Parse(Head + "score foo { staff bl }\n");
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics));
        var spec = RenderSpecParser.FindFirst(tree)!;
        Assert.Equal("foo", spec.OutputFile);
    }

    [Fact]
    public void UnnamedScore_IsAllowed()
    {
        var tree = SyntaxTree.Parse(Head + "score { staff bl }\n");
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics));
    }

    [Fact]
    public void DuplicateName_IsAnError()
    {
        var v = new DuplicateScoreNameValidator();
        v.Validate(SyntaxTree.Parse(Head + "score foo { staff bl }\nscore foo { tab bl }\n"));
        var d = Assert.Single(v.Diagnostics);
        Assert.Equal(DiagnosticCodes.DuplicateScoreName, d.Code);
    }

    [Fact]
    public void DistinctNames_AndOneUnnamed_AreClean()
    {
        var v = new DuplicateScoreNameValidator();
        v.Validate(SyntaxTree.Parse(Head +
            "score a { staff bl }\nscore b { tab bl }\nscore { staff bl }\n"));
        Assert.Empty(v.Diagnostics);
    }
}
