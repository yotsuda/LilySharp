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

using LilySharp.Core.Svg.Layout;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Where a system-start brace's right edge goes — against the SystemStartBar, as LilyPond
/// places it, not against the indent.
/// </summary>
/// <remarks>
/// Every number is LilyPond 2.26.0's, dumped with -dbackend=null from
/// scratch/p377/brace/brace-chain.ly (GrandStaff and PianoStaff, with and without a name) and
/// book 1 of audit/lp-geometry/probes/instrument-name-x.ly: all of them put the SystemStartBar
/// at 8.475827 .. 8.635827 and the brace's right edge at 8.175827 for the default indent.
/// <para>
/// ⚠️ THE OLD ANSWER WAS <c>indent - 0.3</c> = 8.235827, 0.06 right of LilyPond. That 0.06 is
/// exactly <c>indent - SystemStartBar left</c>: the brace clears the bar by its padding, and the
/// bar sits 0.06 left of the indent. The second test says the rule is a translation of the
/// indent, so an indent other than the default cannot hide a constant absorbed into it.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class SystemStartBracePlacementTests
{
    /// <summary>LilyPond's own default indent in staff spaces.</summary>
    private const double Indent = 8.535826771653543;

    [Fact]
    public void MatchesLilyPond()
        => Assert.Equal(8.175826771653544, MultiStaffLayouter.SystemStartBraceRightEdge(Indent), 9);

    [Fact]
    public void SitsThePaddingLeftOfLilyPondsSystemStartBar()
    {
        const double lilyPondBarLeft = 8.475826771653542;
        Assert.Equal(lilyPondBarLeft - 0.3, MultiStaffLayouter.SystemStartBraceRightEdge(Indent), 9);
        Assert.Equal(12.0 - 0.36, MultiStaffLayouter.SystemStartBraceRightEdge(12.0), 9);
    }

    /// <summary>
    /// A bracket's stroke clears the same bar by the bracket's 0.8, and its X extent is the
    /// stroke alone. LilyPond 2.26.0 (brace-chain.ly, StaffGroup and ChoirStaff):
    /// SystemStartBracket 7.225827 .. 7.675827, so the stroke Lily# draws on its centre is
    /// centred at 7.450827. Until session 376 Lily# centred it on indent - 0.8 = 7.735827.
    /// </summary>
    [Fact]
    public void BracketStroke_MatchesLilyPondsExtent()
    {
        const double lilyPondLeft = 7.225826771653543, lilyPondRight = 7.6758267716535435;
        double centre = MultiStaffLayouter.SystemStartBracketCentre(Indent);
        double half = LilySharp.Core.Svg.EngravingDefaults.SystemStartBracketThickness / 2.0;
        Assert.Equal(lilyPondLeft, centre - half, 9);
        Assert.Equal(lilyPondRight, centre + half, 9);
    }

    /// <summary>LilyPond's SystemStartBar, which every other delimiter chains from.</summary>
    [Fact]
    public void SystemStartBarLeftEdge_MatchesLilyPond()
        => Assert.Equal(8.475826771653542, MultiStaffLayouter.SystemStartBarLeftEdge(Indent), 9);
}
