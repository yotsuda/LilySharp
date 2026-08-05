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

using LilySharp.Core.Rendering;
using LilySharp.Core.Svg.Layout;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A dynamic label's X-extent — the DynamicText grob's logical rect, which is where its
/// outline profile is centred (<see cref="DynamicEngraver.LabelSkylines"/>). The rule is
/// one snapped device advance per glyph WITH the GPOS kern inside the snap; these are the
/// observers of that rule, and the ledger's dynamic.* points are its consequences.
/// </summary>
/// <remarks>
/// MEASURED (audit/lp-geometry/probes/dynamic-text-x.ly, book DXM): LilyPond's own
/// <c>ly:grob-extent</c> for all twenty labels, re-run 2026-08-05. The engine is NOT given
/// these numbers — it composes from the font's hmtx advances and GPOS kerns
/// (<see cref="GlyphMetrics.DynamicLetterAdvance"/>, <see cref="GlyphMetrics.DynamicLetterKern"/>)
/// and LilyPond's own derived pixel <see cref="TextFontMetrics.PangoPixelStaffSpaces"/> —
/// so agreeing here is a reproduction, not a fit (HANDOFF §5.2). Same shape as
/// <c>TextFontMetricsTests.Advance_MatchesLilyPondsOwnWidths</c> on the text side.
/// <para>
/// ⚠️ THE KERN IS INSIDE THE SNAP, and that is the whole content of this file. Snapping the
/// advance and then adding the raw kern reproduces every UNKERNED label and misses every
/// kerned one by a whole pixel per pair — 0.015427 ss for <c>ff</c>, twice that for
/// <c>fff</c>. Session 93 tried exactly that spelling and its <c>\fff</c> book
/// (ledger <c>staff.staff.dynamic-stem-binding</c>) is what refused it.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class DynamicLabelWidthTests
{
    [Theory]
    // singles — the rounding alone
    [InlineData("f", 1.263302362205)]
    [InlineData("p", 1.468162204724)]
    [InlineData("m", 1.741308661417)]
    [InlineData("n", 1.297445669291)]
    [InlineData("r", 0.887725984252)]
    [InlineData("s", 0.819439370079)]
    [InlineData("z", 1.126729133858)]
    // unkerned runs — additive, so the snap is per glyph and not on the total
    [InlineData("pp", 2.936324409449)]
    [InlineData("ppp", 4.404486614173)]
    [InlineData("fp", 2.731464566929)]
    [InlineData("pf", 2.731464566929)]
    [InlineData("sf", 2.082741732283)]
    [InlineData("sfz", 3.209470866142)]
    // kerned runs — both signs of pair, one and two pairs
    [InlineData("ff", 2.390031496063)]
    [InlineData("fff", 3.516760629921)]
    [InlineData("mf", 2.902181102362)]
    [InlineData("mp", 3.448474015748)]
    [InlineData("sff", 3.209470866142)]
    [InlineData("rfz", 3.380187401575)]
    [InlineData("spz", 3.755763779528)]
    public void Width_IsLilyPondsOwnExtent(string label, double lilypond)
        => Assert.Equal(lilypond, DynamicOutline.AdvanceWidth(label)!.Value, 9);

    /// <summary>
    /// The composition is the font's numbers and LilyPond's pixel, not a table of the
    /// widths above: every label is a whole number of device pixels, and a label's width
    /// is the sum of its glyphs' snapped kerned advances.
    /// </summary>
    [Fact]
    public void Width_IsSnappedAdvancePlusKern_PerGlyph()
    {
        double f = GlyphMetrics.DynamicLetterAdvance('f')!.Value;
        double p = GlyphMetrics.DynamicLetterAdvance('p')!.Value;
        double kff = GlyphMetrics.DynamicLetterKern('f', 'f');

        // pp does not kern: two snapped p advances.
        Assert.Equal(2 * TextFontMetrics.QuantiseToPangoPixel(p),
            DynamicOutline.AdvanceWidth("pp")!.Value, 9);
        // fff does, twice: the kern rides INSIDE the first two glyphs' snaps.
        Assert.Equal(2 * TextFontMetrics.QuantiseToPangoPixel(f + kff)
                     + TextFontMetrics.QuantiseToPangoPixel(f),
            DynamicOutline.AdvanceWidth("fff")!.Value, 9);

        foreach (string label in new[] { "f", "ff", "fff", "mp", "mf", "spz" })
        {
            double pixels = DynamicOutline.AdvanceWidth(label)!.Value
                            / TextFontMetrics.PangoPixelStaffSpaces;
            Assert.Equal(System.Math.Round(pixels), pixels, 6);
        }

        // A label not spelled from the seven fetaText letters has no feta outline —
        // the caller falls back to its serif box (free @text, cresc./dim. words).
        Assert.Null(DynamicOutline.AdvanceWidth("dolce"));
    }

    /// <summary>
    /// The positive control: the two spellings this rule replaced are BOTH wrong, and
    /// wrong by the amounts named in <see cref="DynamicOutline"/>'s remarks — so the
    /// theory above is not passing on a coincidence that any composition would satisfy.
    /// </summary>
    [Fact]
    public void RawAdvances_AndSnapAfterKern_BothMissTheKernedLabels()
    {
        double f = GlyphMetrics.DynamicLetterAdvance('f')!.Value;
        double kff = GlyphMetrics.DynamicLetterKern('f', 'f');
        const double lpFff = 3.516760629921;

        double raw = 3 * f + 2 * kff;                                    // before any snap
        double snapThenKern = 3 * TextFontMetrics.QuantiseToPangoPixel(f) + 2 * kff;

        Assert.Equal(0.019239370079, raw - lpFff, 9);                    // and not 0
        Assert.Equal(-0.030853543307, snapThenKern - lpFff, 9);

        // ⚠️ NOT a whole number of pixels, because that spelling snaps one part of the
        // advance and leaves the kern unsnapped: the miss is TWICE the per-pair term,
        // once for each kerned glyph.
        double perPair = TextFontMetrics.QuantiseToPangoPixel(f + kff)
                         - (TextFontMetrics.QuantiseToPangoPixel(f) + kff);
        Assert.Equal(0.015426771654, perPair, 9);
        Assert.Equal(-2 * perPair, snapThenKern - lpFff, 9);
    }
}
