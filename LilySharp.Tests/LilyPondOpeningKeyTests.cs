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

using System.Text.RegularExpressions;
using LilySharp.Core.LilyPond;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The twin opens on ONE key: LilyPond keeps the first key event of a moment and discards the
/// rest, so a file key written before the part header's or the first section's own opening key
/// printed in place of it (test/key-opening-override's twin: D major where the page shows A).
/// </summary>
[Trait("Category", "Unit")]
public class LilyPondOpeningKeyTests
{
    private static string[] Keys(string lys) =>
        [.. Regex.Matches(new LilyPondExporter().Export(SyntaxTree.Parse(lys)), @"\\key \w+ \\\w+").Select(m => m.Value)];

    [Fact]
    public void ASectionThatOpensOnItsOwnKey_IsTheOnlyKey()
        => Assert.Equal(["\\key a \\major"],
            Keys("key d major\npart melody {\n  section A { key a major a'1 }\n}\nform { A }\nscore { staff melody }\n"));

    [Fact]
    public void APartHeadersKey_IsTheOnlyKey()
        => Assert.Equal(["\\key bes \\major"],
            Keys("key d major\npart melody { key bes major }\nsection A { melody { c1 } }\nform { A }\nscore { staff melody }\n"));

    [Fact]
    public void ASectionHeadersKey_IsTheOnlyKey()
        => Assert.Equal(["\\key e \\minor"],
            Keys("key d major\npart melody\nsection A { key e minor  melody { c1 } }\nform { A }\nscore { staff melody }\n"));

    [Fact]
    public void AKeyAfterTheFirstNote_LeavesTheFilesKey()
        => Assert.Equal(["\\key d \\major", "\\key a \\major"],
            Keys("key d major\npart melody {\n  section A { c1 | key a major c1 }\n}\nform { A }\nscore { staff melody }\n"));
}
