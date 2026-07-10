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
        "part bl { clef bass tuning bass }\nsection Main { bl { a4 b c d | } }\n";

    [Fact]
    public void MainForm_WritesToInputStem()
    {
        var tree = SyntaxTree.Parse(Head + "form main { Main }\nscore main { staff bl }\n");
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics));
        var spec = RenderSpecParser.FindFirst(tree)!;
        Assert.Equal("", spec.OutputFile);   // `main` → derive the name from the input file
    }

    [Fact]
    public void NonMainForm_NamesTheOutputFile()
    {
        var tree = SyntaxTree.Parse(Head + "form verse { Main }\nscore verse { staff bl }\n");
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics));
        var spec = RenderSpecParser.FindFirst(tree)!;
        Assert.Equal("verse", spec.OutputFile);   // any non-`main` form name becomes the file name
    }

    [Fact]
    public void ExplicitBasename_Wins()
    {
        var tree = SyntaxTree.Parse(Head + "form main { Main }\nscore main \"clean\" { staff bl }\n");
        var spec = RenderSpecParser.FindFirst(tree)!;
        Assert.Equal("clean", spec.OutputFile);
    }

    [Fact]
    public void DuplicateOutput_IsAnError()
    {
        // Two `main` scores with no basename both write the input stem — a collision.
        var v = new DuplicateScoreNameValidator();
        v.Validate(SyntaxTree.Parse(Head + "form main { Main }\nscore main { staff bl }\nscore main { tab bl }\n"));
        var d = Assert.Single(v.Diagnostics);
        Assert.Equal(DiagnosticCodes.DuplicateScoreName, d.Code);
    }

    [Fact]
    public void DistinctOutputs_AreClean()
    {
        var v = new DuplicateScoreNameValidator();
        v.Validate(SyntaxTree.Parse(Head +
            "form main { Main }\nform verse { Main }\n"
            + "score main { staff bl }\nscore verse { tab bl }\nscore main \"extra\" { staff bl }\n"));
        Assert.Empty(v.Diagnostics);
    }
}
