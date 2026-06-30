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
/// Proposal A: the '.up' / '.down' placement qualifier on '@' annotations
/// (e.g. '@staccato.up') forces an articulation above / below, overriding the
/// automatic (opposite-the-stem) side. It rides the existing '@name(qualifier)'
/// grammar, so the syntax already parsed — only the meaning is new.
/// </summary>
[Trait("Category", "Unit")]
public class ArticulationPlacementTests
{
    private static MultiStaffScore Collect(string src)
    {
        var tree = SyntaxTree.Parse(src);
        Assert.False(tree.HasErrors,
            string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        return SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
    }

    [Fact]
    public void UpDownQualifier_ForcesArticulationSide()
    {
        var score = Collect(
            "part m { clef treble }\n" +
            "section S { m { c'4@staccato.up d'4@staccato.down } }\n" +
            "structure { S }\n" +
            "score \"o\" { staff m }\n");

        var stac = score.Articulations
            .Where(a => a.Type == ArticulationType.Staccato)
            .OrderBy(a => a.ItemIndex)
            .ToList();

        Assert.Equal(2, stac.Count);
        Assert.True(stac[0].IsAbove, "@staccato.up should be placed above");
        Assert.False(stac[1].IsAbove, "@staccato.down should be placed below");
    }

    [Fact]
    public void QualifierFlipsTheAutomaticSide()
    {
        // The same note: plain '@staccato' takes the automatic side; '.up' / '.down'
        // force the two sides, so at least one of them differs from the automatic one.
        // (This proves the qualifier overrides the default rather than being ignored,
        // without depending on which side the default picks.)
        MultiStaffScore Side(string ann)
        {
            return Collect(
                "part m { clef treble }\n" +
                $"section S {{ m {{ c'4{ann} }} }}\n" +
                "structure { S }\n" +
                "score \"o\" { staff m }\n");
        }

        bool plain = Side("@staccato").Articulations.Single().IsAbove;
        bool up = Side("@staccato.up").Articulations.Single().IsAbove;
        bool down = Side("@staccato.down").Articulations.Single().IsAbove;

        Assert.True(up);
        Assert.False(down);
        Assert.True(plain == up || plain == down); // plain matches one forced side; the other is a real flip
        Assert.NotEqual(up, down);
    }
}
