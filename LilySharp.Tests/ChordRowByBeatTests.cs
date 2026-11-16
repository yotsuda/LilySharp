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
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// On a score of a chords row and a lyrics row only, a chord stands at its beat's share of the
/// bar — beat 3 of 4 in the middle — not on the column an evenly spread syllable widened
/// (owner's decision 2026-11-16: the chords must not follow the words).
/// </summary>
/// <remarks>
/// Until then <c>G C | D G</c> over <c>Supercalifragilistic is | so nice</c> stood G under the
/// middle of the long word and C two thirds into the bar.
/// </remarks>
public class ChordRowByBeatTests
{
    private static ChordNameLayout[] Chords(string score)
    {
        var tree = SyntaxTree.Parse(
            "time 4/4\nsection A {\n  chords harmony { G C | D G | }\n"
          + "  lyrics word { Supercalifragilistic is | so nice | }\n}\nform { A }\n" + score);
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        var collected = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        return [.. new LayoutEngine().Layout(collected).ChordNameLayouts.OrderBy(c => c.X)];
    }

    [Fact]
    public void Beat3_StandsHalfwayToTheNextBar_WhateverTheWords()
    {
        var c = Chords("score {\n  chords harmony\n  lyrics word\n}\n");
        Assert.Equal(["G", "C", "D", "G"], c.Select(x => x.ChordText));
        double toC = c[1].X - c[0].X, toD = c[2].X - c[0].X;
        // Beat 1 → beat 3 is half of beat 1 → the next bar's beat 1 (less the next bar's lead).
        Assert.InRange(toC / toD, 0.44, 0.52);
    }

    [Fact]
    public void Beat1_IsNotPushedRightByALongFirstSyllable()
    {
        var c = Chords("score {\n  chords harmony\n  lyrics word\n}\n");
        var alone = Chords("score {\n  chords harmony\n}\n");
        Assert.Equal(alone[0].X, c[0].X, 2);
    }
}
