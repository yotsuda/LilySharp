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
using LilySharp.Core.Svg.Model;
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
/// The GRACE path reads them since 2026-08-02, metrics and drawn face together
/// (<see cref="AGraceIsMeasuredAndDrawnFromOneDesign"/>); ossia and cue are still the 20
/// scaled. These are the observers that say the data is real and says what it claims to.
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
    /// ⚠️ This says the DATA is right. That the ENGRAVER reads it is
    /// <see cref="AGraceIsMeasuredAndDrawnFromOneDesign"/>, and what LilyPond says about the
    /// result is the <c>grace.column.*</c> ledger island.
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

        // ...and the sized font does that multiply itself, so a reader never does.
        Assert.Equal(lilyPondsOwn, GlyphMetrics.AtFontSize(-3).NoteheadBlack.Right, 5);
    }

    /// <summary>
    /// A sized font is the design's table with the magstep already applied — the whole of
    /// LilyPond's Modified_font_metric — so full size is the flat constants untouched.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/modified-font-metric.cc:62-68 Modified_font_metric::get_indexed_char_dimensions
    ///   — the whole of it is <c>b.scale (magnification_)</c>.
    /// The identity at step 0 is what says a reader can be moved onto
    /// <see cref="GlyphMetrics.AtFontSize"/> without changing what it reads.
    /// </remarks>
    [Fact]
    public void AtFontSizeZeroChangesNothing()
    {
        var full = GlyphMetrics.AtFontSize(0);

        Assert.Equal(20, full.Rounded);
        Assert.Equal(GlyphMetrics.NoteheadBlack, full.NoteheadBlack);
        Assert.Equal(GlyphMetrics.NoteheadBlackAdvance, full.NoteheadBlackAdvance);
        Assert.Equal(GlyphMetrics.ClefG, full.ClefG);
        Assert.Equal(GlyphMetrics.NoteheadBlackStemAttachment, full.NoteheadBlackStemAttachment);
        // ...and a sized font is its own design's table scaled, on BOTH axes — the vertical
        // one has no other observer, and a scale applied to X alone would pass everything else.
        var fourteen = GlyphMetrics.ForDesign(14);
        var grace = GlyphMetrics.AtFontSize(-3);
        Assert.Equal(fourteen.NoteheadBlack.Top * Magstep(-3), grace.NoteheadBlack.Top, 9);
        Assert.Equal(fourteen.NoteheadBlack.Right * Magstep(-3), grace.NoteheadBlack.Right, 9);
        Assert.Equal(fourteen.NoteheadBlackStemAttachment.Y * Magstep(-3),
            grace.NoteheadBlackStemAttachment.Y, 9);
    }

    /// <summary>
    /// Every design a table exists for is also a face that can be DRAWN with — both the
    /// .otf the metrics were read from and the .woff2 an SVG embeds.
    /// </summary>
    /// <remarks>
    /// ⚠️ ONE DECISION, TWO READERS: the design a size lands on is what the metrics come out
    /// of AND what the glyph is drawn from. A design whose face is missing would make the box
    /// a column reserves stop being the box the glyph fills, and it would fail at RENDER time
    /// in someone's score rather than here. The .woff2 files are built by
    /// audit/scripts/Convert-EmmentalerWoff2.py.
    /// </remarks>
    [Fact]
    public void EveryDesignHasABundledFaceToDrawWith()
    {
        foreach (var (rounded, _) in EmmentalerDesignSize.Designs)
        {
            Assert.NotNull(LilySharp.Core.Rendering.FontLocator.ResolveFile($"emmentaler-{rounded}.otf"));
            Assert.NotNull(LilySharp.Core.Rendering.FontLocator.ResolveFile($"emmentaler-{rounded}.woff2"));
        }
    }

    /// <summary>
    /// ONE DECISION, TWO READERS: the design a grace is MEASURED with is the design it is
    /// DRAWN from — asserted on the drawn document, which is the only place the two meet.
    /// </summary>
    /// <remarks>
    /// This is the invariant the island exists for, and neither half can observe it alone: the
    /// <c>grace.column.*</c> ledger points see the METRICS (they measure notehead anchors) and
    /// would stay exact if the renderer went on drawing the 20's outlines, while a snapshot
    /// sees the DRAWN bytes and would stay green if the layout went back to scaling the 20.
    /// Splitting them is how a box stops being the box its glyph fills.
    /// <para>
    /// The full-size assertion is the other half: a score's ordinary glyphs must keep the bare
    /// <c>.music</c> family, or every existing SVG would have changed for nothing.
    /// </para>
    /// </remarks>
    [Fact]
    public void AGraceIsMeasuredAndDrawnFromOneDesign()
    {
        // The two spellings a caller reaches for — the metric font and the face — are one
        // decision taken once, from the font-size ly/grace-init.ly states.
        Assert.Equal(GraceNoteItem.DesignSize, GraceNoteItem.Font.Rounded);
        Assert.Equal(14, GraceNoteItem.DesignSize);

        var svg = LiveRender.Svg("grace { d16 e } f4 g2 r4");
        var family = "Emmentaler-" + GraceNoteItem.DesignSize;

        // The grace's own glyphs name that design's face…
        Assert.Contains($"font-family=\"{family},", svg);
        // …the document declares it (embedded or local, per SvgDocumentOptions.EmbedFont)…
        Assert.Contains($"font-family: '{family}'", svg);
        // …and the full-size glyphs are untouched: the bare class, no per-element override.
        Assert.Contains("<text class=\"music\" x=", svg);
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
