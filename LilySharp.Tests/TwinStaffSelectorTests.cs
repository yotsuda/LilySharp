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

using LilySharp.Core.LilyPond;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// HANDOFF §2 F-twinhk: a score item's <c>as removeEmpty V</c> and <c>as lines N</c> are
/// LilyPond context mods, and the twin writes them on the staff they belong to. It wrote
/// neither until 2026-10-06, so LilyPond drew every staff the page hides (and the brace beside
/// it — Lab sessions/p849/brace, test/hara-kiri) on five lines.
/// </summary>
public class TwinStaffSelectorTests
{
    private static string Twin(string scoreBody) => new LilyPondExporter().Export(SyntaxTree.Parse($$"""
        part rh { clef treble }
        part lh { clef bass }
        section A { rh { c'1 | } lh { r1 | } }
        form main { A }
        score main { {{scoreBody}} }
        """));

    [Theory]
    [InlineData("removeEmpty true", "\\RemoveEmptyStaves")]
    [InlineData("removeEmpty all", "\\RemoveAllEmptyStaves")]
    public void RemoveEmpty_IsLilyPondsContextMod_InAGrandStaff(string selector, string mod)
    {
        string twin = Twin($"grandStaff {{ staff rh staff lh as {selector} }}");
        Assert.Contains($"\\new Staff \\with {{ {mod} }} {{ \\clef \"bass\"", twin);
        // …on that staff only.
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(twin, @"\\Remove(All)?EmptyStaves"));
    }

    [Fact]
    public void LinesAndRemoveEmpty_OnAStaffOfItsOwn()
    {
        string twin = Twin("staff rh staff lh as lines 1 removeEmpty true");
        Assert.Contains("\\RemoveEmptyStaves \\override StaffSymbol.line-count = #1 } { \\clef \"bass\" \\lh }", twin);
        Assert.Contains("\\new Staff \\with { instrumentName = \"Rh\" } {", twin);
    }

    [Fact]
    public void NoSelector_WritesNoMod()
    {
        string twin = Twin("grandStaff { staff rh staff lh }");
        Assert.DoesNotContain("EmptyStaves", twin);
        Assert.DoesNotContain("line-count", twin);
    }
}
