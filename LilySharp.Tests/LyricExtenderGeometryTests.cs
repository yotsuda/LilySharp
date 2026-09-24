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
using System.Collections.Immutable;
using System.Linq;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The lyric extender (<c>__</c>) is LilyPond's Lyric_extender::print: it leaves the
/// syllable's ink right by the line's own thickness (0.08), reaches at least
/// <c>minimum-length</c> 1.5 past the syllable (capped at the system's right edge) and at
/// least to the melisma's last head, is capped by the next syllable's ink left less the
/// thickness, sits ON the baseline (the box 0 … 0.08 above it) and is dropped under 1.5
/// thicknesses of length. Until session 565 it left the syllable by 0.2, ended at the held
/// head alone, sat 0.7 BELOW the baseline, was drawn 0.1 thick and dropped under 0.5.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/lyric-extender.cc:57-133 Lyric_extender::print;
/// scm/define-grobs.scm:2138-2147 LyricExtender lyric-extender-interface — minimum-length
/// 1.5, thickness 0.8.
/// MEASURED on 2.26.0 (Lab sessions/p565/extender-lp2.log, a hand twin with \lyricsto since
/// the exporter's twin gives LilyPond no heads): `lo __ lo` under g2( a4) b — left 23.611 =
/// the syllable's right 23.531 + 0.08, right 27.198 = the a's ink right; `mi __` under
/// c'1( d1) — right 43.958 = the d's ink right; the box 0.08 tall on the baseline.
/// Poison: the old 0.2 / 0.7 / no-minimum reddens every fact but the drop.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class LyricExtenderGeometryTests
{
    private const double H = 0.8 * EngravingDefaults.LineThickness;   // 0.08

    private static LyricLayout Syllable(string text, double x, double width, LyricConnectorType type,
        int measureIndex = 0, int itemIndex = 0)
        => new(new LyricItem(text, measureIndex, itemIndex, type, 0, 1), x, -10, width);

    private static ImmutableArray<SystemLayout> System(double width)
        => ImmutableArray.Create(new SystemLayout(0, 0, 76, 0,
            ImmutableArray.Create(new MeasureLayout(0, 0, width, ImmutableArray<ItemLayout>.Empty))));

    private static LyricHyphenLayout Extender(LyricLayout from, LyricLayout to, double systemWidth = 60)
        => Assert.Single(new LyricHyphenEngraver().CalculateLayouts(new List<LyricLayout> { from, to }, System(systemWidth)));

    [Fact]
    public void LeavesTheSyllableByItsThickness_AndReachesMinimumLength()
    {
        // The syllable's ink right is 6 (x 5, width 2); the next syllable is far away, so
        // the end is left + 1.5.
        var e = Extender(Syllable("la", 5, 2, LyricConnectorType.Extender),
                         Syllable("la", 20, 2, LyricConnectorType.None, itemIndex: 3));
        Assert.Equal(6 + H, e.ExtenderStartX, 9);
        Assert.Equal(6 + 1.5, e.ExtenderEndX, 9);
    }

    [Fact]
    public void IsCappedByTheNextSyllable_LessTheThickness()
    {
        var e = Extender(Syllable("la", 5, 2, LyricConnectorType.Extender),
                         Syllable("la", 8, 2, LyricConnectorType.None, itemIndex: 3));
        // The next syllable's ink left is 7; the line ends 0.08 short of it.
        Assert.Equal(7 - H, e.ExtenderEndX, 9);
    }

    [Fact]
    public void SitsOnTheBaseline_ItsBoxAboveIt()
    {
        var e = Extender(Syllable("la", 5, 2, LyricConnectorType.Extender),
                         Syllable("la", 20, 2, LyricConnectorType.None, itemIndex: 3));
        // Device Y of the centre line: the baseline (10) less half the thickness.
        Assert.Equal(10 - H / 2, e.ExtenderY, 9);
    }

    [Fact]
    public void IsDropped_WhenShorterThanOneAndAHalfThicknesses()
    {
        // Ink right 6; the next syllable's ink left at 6.2 leaves 6.2 − 0.08 − 6.08 = 0.04.
        var layouts = new LyricHyphenEngraver().CalculateLayouts(new List<LyricLayout>
        {
            Syllable("la", 5, 2, LyricConnectorType.Extender),
            Syllable("la", 7.2, 2, LyricConnectorType.None, itemIndex: 3),
        }, System(60));
        Assert.Empty(layouts);
    }

    [Fact]
    public void TheMinimumLength_IsCappedAtTheSystemsRightEdge()
    {
        // The system ends at 7.0: minimum-length would reach 7.5, the cap holds it at 7.0.
        var e = Extender(Syllable("la", 5, 2, LyricConnectorType.Extender),
                         Syllable("la", 20, 2, LyricConnectorType.None, itemIndex: 3), systemWidth: 7.0);
        Assert.Equal(7.0, e.ExtenderEndX, 9);
    }
}
