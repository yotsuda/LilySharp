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
using System.Linq;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// '@stemUp' / '@stemDown' force a note's stem direction, overriding the automatic
/// (staff-position) default. They feed the existing NoteItem.StemUpOverride slot, so
/// the whole layout/render already honours them. (A beamed note follows its beam's
/// shared direction, since the beam writes the same slot afterwards.)
/// </summary>
[Trait("Category", "Unit")]
public class StemDirectionAnnotationTests
{
    private static List<NoteItem> Notes(string body)
    {
        var src =
            "part m { clef treble }\n" +
            $"section S {{ m {{ {body} }} }}\n" +
            "form main { S }\n" +
            "score main \"o\" { staff m }\n";
        var tree = SyntaxTree.Parse(src);
        Assert.False(tree.HasErrors,
            string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        return score.PrimaryContentStaff.PrimaryVoice.Measures[0].Items
            .OfType<NoteItem>().ToList();
    }

    [Fact]
    public void StemUpDown_ForcesStemDirection()
    {
        // c'' (high) auto-stems DOWN; force each way and check it sticks.
        var notes = Notes("c''4@stemUp c''4@stemDown c''4");
        Assert.Equal(3, notes.Count);
        Assert.True(notes[0].StemUp, "@stemUp should force the stem up");
        Assert.False(notes[1].StemUp, "@stemDown should force the stem down");
        // The third (plain) note keeps the automatic direction.
        Assert.Equal(notes[2].StaffPosition < 0, notes[2].StemUp);
    }

    [Fact]
    public void NoAnnotation_KeepsAutomaticDirection()
    {
        // No annotation -> StemUpOverride is null -> automatic from staff position.
        var notes = Notes("c''4 c4");
        Assert.All(notes, n => Assert.Equal(n.StaffPosition < 0, n.StemUp));
    }
}
