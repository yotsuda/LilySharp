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
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using LilySharp.Core.Tablature;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// On a full-notation tab an eighth group's stems take a sixteenth group's length, so the
/// eighth pair and the sixteenth group over one string share a beam height — and nothing else
/// about the beam changes: stems keep their own heads, beams still slope across the strings.
/// </summary>
/// <remarks>
/// LILYSHARP-OWN, owner's decision 2026-09-24 (session 569, on The Final Countdown's tab
/// score; BeamScoringProblem's <c>uniformBeamedLength</c>). LilyPond's
/// <c>\tabFullNotation</c> stood the two groups of one bar on one string a quant apart,
/// because the middle-line clamp that lines them up on a notation staff never binds on a tab.
/// A first attempt lifted every beam of a direction to one floor; the owner found its stems too
/// long and its beams all flat — the second and third facts below are that finding's net.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class TabBeamLengthTests
{
    // Bar 1 the E string (fret 2) and bar 2 the A string (fret 5), both stem UP; bar 3 the D
    // string (fret 2) and bar 4 the G string (fret 2), both stem DOWN. Each bar holds the
    // sixteenth group (8 16 16) and the eighth pair, twice.
    private const string Book = """
        octave absolute
        time 4/4
        part bl {
          clef bass
          tuning bass
          section A {
            fis,,8\4 fis,,16\4 fis,,\4 fis,,8\4 fis,,\4 fis,,8\4 fis,,16\4 fis,,\4 fis,,8\4 fis,,\4 |
            d,8\3 d,16\3 d,\3 d,8\3 d,\3 d,8\3 d,16\3 d,\3 d,8\3 d,\3 |
            e,8\2 e,16\2 e,\2 e,8\2 e,\2 e,8\2 e,16\2 e,\2 e,8\2 e,\2 |
            a,8\1 a,16\1 a,\1 a,8\1 a,\1 a,8\1 a,16\1 a,\1 a,8\1 a,\1 |
            e,,8\4 fis,,\4 a,,\3 b,,\3 e,,8\4 fis,,\4 a,,\3 b,,\3 |
          }
        }
        form main { A }
        score main { tab bl as full }
        """;

    /// <summary>Every beam's bar and outer edge at both ends (Y-up about the tab's middle).</summary>
    private static (int Bar, double Left, double Right)[] Beams()
    {
        var tree = SyntaxTree.Parse(Book);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        var layout = new LayoutEngine().Layout(score);
        var staff = score.StaffGroups[0].Staves[0];
        int strings = Tunings.GetStringCount(staff.Tuning!.Value);
        double tabHeight = (strings - 1) * EngravingDefaults.TabStringSpace(strings);
        var geom = new TabStaffGeometry(score.TextMetrics, staff.Tuning.Value, -tabHeight / 2.0,
            staff.TabSourceClef, staff.Transposition);
        return layout.BeamLayouts
            .Select(b => (b.Group.MeasureIndex,
                          -ArticulationEngraver.TabBeamOuterEdgeY(b, geom, b.LeftX),
                          -ArticulationEngraver.TabBeamOuterEdgeY(b, geom, b.RightX)))
            .ToArray();
    }

    [Fact]
    public void OverOneString_TheEighthPairAndTheSixteenthGroupShareAHeight()
    {
        var beams = Beams();
        for (int bar = 0; bar < 4; bar++)
        {
            var inBar = beams.Where(b => b.Bar == bar).ToArray();
            Assert.Equal(4, inBar.Length);    // 8 16 16 | 8 8 | 8 16 16 | 8 8
            foreach (var b in inBar)
            {
                Assert.Equal(inBar[0].Left, b.Left, 6);
                Assert.Equal(inBar[0].Left, b.Right, 6);
            }
        }
    }

    [Fact]
    public void AnotherString_IsAnotherHeight_TheStemsKeepTheirOwnLength()
    {
        // The A string's up-beams stand above the E string's, and the D string's down-beams
        // below the G string's: each stem is measured from its own digit, so the beam moves
        // with the string, one string gap at a time.
        var beams = Beams();
        double Height(int bar) => beams.First(b => b.Bar == bar).Left;
        Assert.True(Height(1) > Height(0) + 0.5, $"A {Height(1):F3} over E {Height(0):F3}");
        Assert.True(Height(2) < Height(3) - 0.5, $"D {Height(2):F3} under G {Height(3):F3}");
    }

    [Fact]
    public void AGroupAcrossTheStrings_StillSlopes()
    {
        // E string then A string, rising: the beam rises to the right, as it does in LilyPond.
        // (An E-A-E-A zigzag would not — LilyPond flattens a concave group, and so does Lily#.)
        var pairs = Beams().Where(b => b.Bar == 4).ToArray();
        Assert.NotEmpty(pairs);
        Assert.All(pairs, b => Assert.True(b.Right > b.Left + 0.1, $"{b.Left:F3} → {b.Right:F3}"));
    }
}
