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

using System;
using System.Linq;
using LilySharp.Core.Rendering.Boxes;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A section label at a line start stands as far over the staff whatever the clef, when only
/// whole rests stand under it — LilyPond 2.26.0 puts it 0.85 over the top line over a treble,
/// a bass and a percussion staff alike (Lab sessions/p871). Until session 871 the outside-staff
/// support seeded a PHANTOM treble clef under every clef but bass, alto and tenor
/// (OutsideStaffStacker.SeedClefInk), and over the narrower percussion clef the label met its
/// curl: 2.21 over the staff.
/// </summary>
[Trait("Category", "Unit")]
public sealed class LineStartLabelOverClefTests
{
    private static double LabelGap(string clef)
    {
        var tree = SyntaxTree.Parse(
            $"time 4/4\npart rh {{ clef {clef} }}\nsection A {{ rh {{ r1 | r1 | }} }}\nscore {{ staff rh }}\n");
        var symbols = BoxesGenerator.GenerateDocument(tree, RenderSpecParser.FindFirst(tree))
            .Pages.SelectMany(p => p.Symbols).ToList();
        double topLine = symbols.Where(s => s.Kind == "staffLine").Min(s => (s.Box[1] + s.Box[3]) / 2);
        var frame = symbols.First(s => s.Kind == "rect");
        return topLine - frame.Box[3];
    }

    [Theory]
    [InlineData("bass")]
    [InlineData("percussion")]
    [InlineData("soprano")]
    [InlineData("bass_8")]
    public void TheLabel_StandsAsOverAnyClef_AsOverATreble(string clef)
    {
        double treble = LabelGap("treble");
        Assert.Equal(treble, LabelGap(clef), 3);
    }

    /// <summary>The baritone C clef sits on the TOP line, so its upper half stands 2 over the
    /// staff and the label clears it: LilyPond 2.137 over the top line against 0.850 over a
    /// treble (Lab sessions/p871) — the one clef here whose own ink lifts the label.</summary>
    [Fact]
    public void TheLabel_ClearsABaritoneClef_ByLilyPondsLift()
        => Assert.Equal(2.137 - 0.850, LabelGap("baritone") - LabelGap("treble"), 3);
}
