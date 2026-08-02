// Lily# — a music notation language and engraver.
// Copyright (C) 2026 yotsuda
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
using LilySharp.Core.Svg.Layout;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The per-design metric tables: that there really are eight different drawings behind them,
/// that the twenty is the one the flat constants have always been, and that a grace's head
/// comes out of the FOURTEEN.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/font-select.cc:41-70 best_rounded_design_size picks the file,
///   lily/open-type-font.cc:390-408 get_indexed_char_dimensions reads that file's own table.
/// Nothing in the engraver reads the new tables yet — the scaled paths (grace, ossia, cue)
/// are wired to them in the next step of the island. These are the observers that say the
/// data is real and says what it claims to.
/// </remarks>
public sealed class EmmentalerDesignMetricsTests
{
    /// <summary>magstep, LilyPond's own font-size scale: <c>2^(s/6)</c>.</summary>
    /// <remarks>LILYPOND-REF: scm/lily-library.scm magstep.</remarks>
    private static double Magstep(double step) => Math.Pow(2.0, step / 6.0);

    /// <summary>
    /// Emmentaler is OPTICALLY sized: the small designs are drawn wider, not scaled down. The
    /// black notehead's right edge, in each design's own staff spaces, is the number the whole
    /// island turns on.
    /// </summary>
    /// <remarks>
    /// MEASURED from the LILC tables of LilyPond 2.26.0's own font files — the same eight
    /// files Lily# bundles. If these ever come out equal, the tables have collapsed onto one
    /// design and the optical sizing is gone.
    /// </remarks>
    [Theory]
    [InlineData(11, 1.289478)]
    [InlineData(13, 1.294282)]
    [InlineData(14, 1.298161)]
    [InlineData(16, 1.300819)]
    [InlineData(18, 1.302806)]
    [InlineData(20, 1.304200)]
    [InlineData(23, 1.305122)]
    [InlineData(26, 1.305873)]
    public void EachDesignDrawsItsOwnNotehead(int rounded, double headRight)
        => Assert.Equal(headRight, GlyphMetrics.ForDesign(rounded).NoteheadBlack.Right, 6);

    /// <summary>
    /// The twenty's table is not a copy of the flat constants — it IS them, so the score's own
    /// size cannot drift away from the table that claims to hold it.
    /// </summary>
    [Fact]
    public void TheTwentyTableIsTheFlatConstants()
    {
        var twenty = GlyphMetrics.ForDesign(20);

        Assert.Equal(GlyphMetrics.NoteheadBlack, twenty.NoteheadBlack);
        Assert.Equal(GlyphMetrics.NoteheadBlackOutline, twenty.NoteheadBlackOutline);
        Assert.Equal(GlyphMetrics.NoteheadBlackAdvance, twenty.NoteheadBlackAdvance);
        Assert.Equal(GlyphMetrics.NoteheadBlackStemAttachment, twenty.NoteheadBlackStemAttachment);
        Assert.Equal(GlyphMetrics.ClefG, twenty.ClefG);
        Assert.Equal(GlyphMetrics.GClefAdvance, twenty.GClefAdvance);
        Assert.Equal(GlyphMetrics.DynamicLetterF, twenty.DynamicLetterF);
    }

    /// <summary>
    /// The design size each file really carries, read from the font, against the mapping
    /// ported from LilyPond's Scheme — two sources for one number.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/lily-library.scm:1702-1710 feta-design-size-mapping is the ported
    /// side (<see cref="EmmentalerDesignSize.Designs"/>); the tables' <c>DesignSize</c> comes
    /// out of each font's own LILY table. A font swapped for one of a different size, or a
    /// mistyped digit in the port, breaks this and nothing else would.
    /// </remarks>
    [Fact]
    public void EveryTableAgreesWithThePortedDesignSizeMapping()
    {
        Assert.Equal(EmmentalerDesignSize.Designs.Length, GlyphMetrics.AllDesigns.Length);

        foreach (var table in GlyphMetrics.AllDesigns)
        {
            var ported = EmmentalerDesignSize.Designs.Single(d => d.Rounded == table.Rounded);
            Assert.Equal(ported.Actual, table.DesignSize, 6);
        }
    }

    /// <summary>
    /// A grace head is the FOURTEEN design's head at magstep(−3) — LilyPond's own drawn value.
    /// Scaling the twenty instead misses it by 0.004270, which is the residual twelve
    /// <c>grace.column</c> ledger entries carry.
    /// </summary>
    /// <remarks>
    /// MEASURED (LilyPond 2.26.0, <c>\grace c'8 c'1</c>, NoteHead X-extent printed from
    /// after-line-breaking): the grace head ends at <b>0.9179386191980385</b> page staff
    /// spaces and the full-size whole head at 1.962, so the extents are page staff spaces and
    /// the grace's is not the twenty design scaled.
    /// <para>
    /// ⚠️ The tables are rounded to six decimals (the generator's own convention), so the
    /// composition reproduces LilyPond to 2e-7 rather than to the last digit — hence five
    /// places here. The gap this closes is 0.004270, four orders of magnitude larger.
    /// </para>
    /// <para>
    /// ⚠️ This says the DATA is right, not that the engraver reads it: the drawn size still
    /// comes off the flat constants until the scaled paths are wired to the tables.
    /// </para>
    /// </remarks>
    [Fact]
    public void AGraceHeadIsTheFourteenDesign()
    {
        const double lilyPondsOwn = 0.9179386191980385;

        double fromTheDesignItLandsOn =
            GlyphMetrics.ForFontSizeStep(-3).NoteheadBlack.Right * Magstep(-3);
        double fromScalingTheTwenty = GlyphMetrics.NoteheadBlack.Right * Magstep(-3);

        Assert.Equal(14, EmmentalerDesignSize.ForFontSizeStep(-3).Rounded);
        Assert.Equal(lilyPondsOwn, fromTheDesignItLandsOn, 5);
        Assert.Equal(0.004270, fromScalingTheTwenty - lilyPondsOwn, 6);
    }

    /// <summary>There are eight designs and nothing between them: 12 is not a rounded size.</summary>
    /// <remarks>
    /// The caller is meant to arrive with what <see cref="EmmentalerDesignSize.BestRounded"/>
    /// returned, so a number that is not a design means the selection was skipped — that is
    /// worth a throw rather than a nearest-neighbour guess.
    /// </remarks>
    [Fact]
    public void ForDesign_KnowsOnlyTheEightDesigns()
        => Assert.Throws<ArgumentOutOfRangeException>(() => GlyphMetrics.ForDesign(12));
}
