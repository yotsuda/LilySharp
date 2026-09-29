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

using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Each staff's Staff_spacing wish off a bar line runs from ITS OWN last break-aligned grob
/// with a non-empty extent. A numbers tab under a staff shares the staff's voice — so its
/// measures carry the staff's meter change — but blanks the meter
/// (<c>TimeSignature.stencil ##f</c>) and has no key signature, so its wish runs off its bar
/// line; the two wishes are then averaged (merge_springs).
/// LILYPOND-REF: lily/staff-spacing.cc:118-221 Staff_spacing::get_spacing;
/// LILYPOND-REF: lily/spring.cc:104-129 merge_springs.
/// </summary>
[Trait("Category", "Unit")]
public class StaffSpacingWishTests
{
    /// <summary>MEASURED, LilyPond 2.26.0 (Lab sessions/p692/ends, the <c>lysc ly</c> twins of
    /// these two sources, the BarLine extents): the ink right edges of the bar lines after bars 1
    /// and 2, the second one before a 2/4 bar with a key. On the staff alone 47.171 / 74.644;
    /// with the numbers tab under it 46.973 / 74.145. Until session 692 the tab's wish read the
    /// shared meter change as its own and Lily# drew 47.16 / 74.55 with the tab.</summary>
    [Theory]
    [InlineData("staff bass", 47.171, 74.644)]
    [InlineData("staff bass\n  tab bass", 46.973, 74.145)]
    public void ANumbersTab_WishesOffItsBarLine_NotTheStaffsMeter(string items, double bar1, double bar2)
    {
        string src = $$"""
            time 4/4
            key aes major
            octave absolute
            part bass { clef bass
              tuning bass }
            section A { bass { aes,1 | aes,1 | } }
            section E1 { bass { time 2/4 des4 ees | break time 4/4 aes,1 | aes,1 | } }
            section E2 { bass { time 4/4 ees1 | } }
            form main { |: A [1. ~E1] :| [2. ~E2] }
            score main {
              {{items}}
            }
            """;
        var tree = SyntaxTree.Parse(src);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
        var layout = new LayoutEngine().Layout(
            new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!));
        var m = layout.Systems[0].Measures;
        Assert.Equal(bar1, m[0].X + m[0].Width, 0.01);
        Assert.Equal(bar2, m[1].X + m[1].Width, 0.01);
    }
}
