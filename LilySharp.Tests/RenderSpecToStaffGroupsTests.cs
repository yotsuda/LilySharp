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

using System.Collections.Immutable;
using System.Linq;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// RenderSpec.ToStaffGroups must not throw when a single-voice row (tab/ossia/
/// chord/lyrics) names a part that resolves to no voices — the tab and ossia
/// branches previously indexed [0] with no empty guard, unlike chord/lyrics.
/// </summary>
[Trait("Category", "Unit")]
public class RenderSpecToStaffGroupsTests
{
    [Fact]
    public void ToStaffGroups_TabPartWithNoVoices_DoesNotThrow()
    {
        var tree = SyntaxTree.Parse("""
            part m { clef treble }
            section A { m { c'4 d' e' f' | } }
            structure { A }
            score x { tab m }
            """);
        var spec = RenderSpecParser.FindFirst(tree);
        Assert.NotNull(spec);

        // getVoices yields nothing for the tab part; the branch must fall back to an
        // empty voice rather than throwing IndexOutOfRange.
        var groups = spec!.ToStaffGroups(_ => ImmutableArray<Voice>.Empty).ToList();
        Assert.NotEmpty(groups);
    }
}
