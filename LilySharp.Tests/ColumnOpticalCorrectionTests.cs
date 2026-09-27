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
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using LilySharp.Tests.LpFidelity;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The bar line → first note optical correction is read per STAFF: each staff's
/// Staff_spacing wish reads every staff's first column against its OWN bar, and the wishes
/// are averaged.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/separating-line-group-engraver.cc:147-150 Separating_line_group_engraver::stop_translation_timestep — right-items = currentMusicalColumn
/// LILYPOND-REF: lily/staff-spacing.cc:95-110 Staff_spacing::next_notes_correction.
/// </remarks>
public class ColumnOpticalCorrectionTests
{
    /// <summary>
    /// Beside a four-string tab (bar ±1.5) a staff's down stem earns less on the tab's wish
    /// than on its own (bar ±2), and the mean is LilyPond's.
    /// </summary>
    /// <remarks>
    /// MEASURED (2.26.0, LilySharp-Lab sessions/p661/merge/m2.ly, staff + numbers-only bass
    /// tab, first column off the bar against an up/up bar): d' down over the tab's up stub
    /// 0.100000 = (0.114286 + 0.085714) / 2; g down over the tab's down stub 0.175074 =
    /// (0.189360 + 0.160788) / 2. Until session 661 both staves took the column's max against
    /// their own voices' bars: 0.114286 / 0.189360.
    /// </remarks>
    [Fact]
    public void AStaffAndATab_AverageEachStaffsReadingOfTheColumn()
    {
        var tree = SyntaxTree.Parse("""
            octave absolute
            time 4/4
            part bassline {
              clef bass
              tuning bass
              section S { a,,4\4 a,,\4 a,,\4 a,,\4 | d4\3 a,,\4 a,,\4 a,,\4 | g,4\1 a,,\4 a,,\4 a,,\4 | a,,4\4 a,,\4 a,,\4 a,,\4 | }
            }
            form main { S }
            score main { staff bassline  tab bassline }
            """);
        var multi = SvgGenerator.CollectScore(tree, RenderSpecParser.FindAll(tree).First());
        var data = SystemBreaker.ComputeMultiStaffSpringData(
            multi, SpacingRules.CalculateCommonShortestDuration(multi));
        double reference = data[3].Springs[0].IdealDistance;
        Assert.Equal(0.100000, data[1].Springs[0].IdealDistance - reference, precision: 5);
        Assert.Equal(0.175074, data[2].Springs[0].IdealDistance - reference, precision: 5);
    }

    /// <summary>
    /// A system opening on <c>.|:</c> takes the lower staff's down stem on the upper staff's
    /// wish too — the whole correction, not half of it.
    /// </summary>
    /// <remarks>
    /// MEASURED (2.26.0, LilySharp-Lab sessions/p661/merge/ls5.ly, two loose staves — the
    /// twin's plain simultaneity): the first column 0.132217 further off than an up/up
    /// opening. Until session 661 each staff's line-start wish read only its own voices, so
    /// the mean halved it.
    /// ⚠️ Inside a PianoStaff LilyPond answers 0.133646 (ls2.ly): its bar lines reach 0.05
    /// toward the connected staff ((−2.05, 2) above, (−2, 2.05) below — ls4.ly), which the
    /// upper wish's reading of the lower stem meets. That extension is not ported.
    /// </remarks>
    [Fact]
    public void ALineStartRepeatBar_ReadsTheOtherStaffsDownStem()
    {
        var g = RenderedGeometry.Render("""
            octave absolute
            time 4/4
            part up { }
            part lo { clef bass }
            section A { up { c4 c c c | break } lo { g,,4 g,, g,, g,, | break } }
            section B { up { c4 c c c | break } lo { e,4 g,, g,, g,, | break } }
            section C { up { c4 c c c | } lo { g,,4 g,, g,, g,, | } }
            form main { A |: B :| |: C :| }
            score main { staff up  staff lo }
            """);
        // The upper staff's four c's, one row a system, in page order.
        var firsts = g.Noteheads
            .GroupBy(n => Math.Round(n.Y, 1))
            .Where(row => row.Count() == 4)
            .OrderBy(row => row.Key)
            .Select(row => row.Min(n => n.X))
            .ToArray();
        Assert.Equal(5, firsts.Length); // systems 1 and 3 give two rows, system 2 one
        Assert.Equal(0.132217, firsts[2] - firsts[3], 5);
    }
}
