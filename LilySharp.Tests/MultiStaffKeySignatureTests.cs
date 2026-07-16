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
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A section that opens with its OWN key collapses into the opening signature (no
/// redundant change is drawn). In a MULTI-staff score each staff must keep ITS key: the
/// opening key is recorded per voice, not into the shared score key, so two parts stating
/// different opening keys do not overwrite each other (the regression this guards).
/// </summary>
[Trait("Category", "Unit")]
public class MultiStaffKeySignatureTests
{
    // Effective opening sharps per staff: its own signature if set, else the score key.
    private static IReadOnlyList<int> OpeningSharpsPerStaff(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.Empty(tree.Diagnostics);
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        return score.EnumerateStaves()
            .Select(t => (t.Staff.PerStaffKeySignature ?? score.KeySignature).Sharps)
            .ToList();
    }

    [Fact]
    public void EachStaffKeepsItsOwnOpeningKey()
    {
        var sharps = OpeningSharpsPerStaff(
            "part melody { section A { key c major c1 } } " +
            "part melody2 { section A { key a major c1 } } " +
            "form main { A } score main { staff melody staff melody2 }");
        // Staff 1 opens in C major (0 sharps), staff 2 in A major (3) — no leak.
        Assert.Equal(new[] { 0, 3 }, sharps);
    }

    [Fact]
    public void StaffWithoutItsOwnKeyFallsBackToTheScoreKey()
    {
        // A top-level `key d major` (2 sharps) governs the staff that states no key of its
        // own; the other staff overrides to A major (3).
        var sharps = OpeningSharpsPerStaff(
            "key d major " +
            "part melody { section A { c1 } } " +
            "part melody2 { section A { key a major c1 } } " +
            "form main { A } score main { staff melody staff melody2 }");
        Assert.Equal(new[] { 2, 3 }, sharps);
    }
}
