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
/// Verifies that the glyph-shape-aware accidental skyline produces tighter
/// packing than the naive BBox approximation, while still preventing
/// collisions of the actual ink.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/accidental-placement.cc:254-301 — set_ape_skylines
/// </remarks>
[Trait("Category", "Unit")]
public class AccidentalGlyphSkylineTests
{
    [Fact]
    public void Build_Sharp_FallsBackToBoundingBox()
    {
        // Sharp's silhouette is essentially rectangular; the helper returns the BBox.
        var sky = AccidentalGlyphSkyline.Build("sharp", -1.4, 1.4, 0.0, 1.0, Skyline.Direction.Left);
        var bbox = Skyline.FromBox(-1.4, 1.4, 0.0, 1.0, Skyline.Direction.Left);

        // At the BBox edges they should agree exactly.
        Assert.Equal(bbox.QueryXInRange(-1.4, 1.4), sky.QueryXInRange(-1.4, 1.4), precision: 6);
    }

    [Fact]
    public void Build_Flat_BowlExtendsRight_StemDoesNot()
    {
        // Use the live Emmentaler-derived BBox so this test follows the font.
        var flat = LilySharp.Core.Svg.Layout.GlyphMetrics.AccidentalFlat;
        double xLeft = flat.Left;
        double xRight = flat.Right;
        double yBottom = flat.Bottom;
        double yTop = flat.Top;

        var sky = AccidentalGlyphSkyline.Build("flat", yBottom, yTop, xLeft, xRight, Skyline.Direction.Right);

        // Query at the bowl height (just below top): should be full xRight.
        double bowlX = sky.QueryXInRange(yTop - 0.5, yTop - 0.1);
        Assert.Equal(xRight, bowlX, precision: 4);

        // Query at the stem height (well below the bowl split): should be tighter than full width.
        double stemX = sky.QueryXInRange(yBottom + 0.05, yBottom + 0.15);
        Assert.True(stemX < xRight - 0.3,
            $"Stem skyline should be much narrower than bowl. Got stemX={stemX}, expected < {xRight - 0.3}");
        Assert.True(stemX > xLeft, $"Stem skyline must extend right of xLeft. Got stemX={stemX}");
    }

    [Fact]
    public void Build_Flat_TwoFlatsStackedCanPackCloser_ThanBoundingBoxes()
    {
        // Stack two flats vertically: upper flat at staff position +6, lower at -6.
        // BBox approach: each flat is a 0.904-wide rectangle. The skyline packing
        // does not benefit from the bowl/stem distinction.
        // Glyph approach: the lower flat's bowl reaches into the upper flat's stem
        // region (where the upper flat is just a narrow strip), so they can sit
        // closer in X.
        const double upperShift = 3.0;   // staff space units
        const double lowerShift = -3.0;
        var flat = LilySharp.Core.Svg.Layout.GlyphMetrics.AccidentalFlat;
        double width = flat.Width;
        double yB = flat.Bottom;
        double yT = flat.Top;

        // Glyph skylines for both flats (Right-direction = right edge of accidental).
        var upperGlyph = AccidentalGlyphSkyline.Build("flat",
            yB + upperShift, yT + upperShift, 0, width, Skyline.Direction.Right);
        var lowerGlyph = AccidentalGlyphSkyline.Build("flat",
            yB + lowerShift, yT + lowerShift, 0, width, Skyline.Direction.Right);

        // BBox skylines for comparison.
        var upperBBox = Skyline.FromBox(yB + upperShift, yT + upperShift, 0, width, Skyline.Direction.Right);
        var lowerBBox = Skyline.FromBox(yB + lowerShift, yT + lowerShift, 0, width, Skyline.Direction.Right);

        // Probe Y range of the upper flat's stem (just above its bottom).
        double stemY = yB + upperShift + 0.1;
        double glyphRight = upperGlyph.QueryXInRange(stemY - 0.05, stemY + 0.05);
        double bboxRight  = upperBBox .QueryXInRange(stemY - 0.05, stemY + 0.05);

        Assert.True(glyphRight < bboxRight,
            $"Glyph skyline should reveal the stem is narrower than the BBox. Got glyph={glyphRight}, bbox={bboxRight}");
    }

    [Fact]
    public void Build_DoubleFlat_HasTwoBowlsAndTwoStems()
    {
        // Double flat BBox: width 1.644, so two flats with the second starting at ~0.74.
        var sky = AccidentalGlyphSkyline.Build("doubleFlat", -0.7, 1.748, 0, 1.644, Skyline.Direction.Right);

        // At the bowl level (top), the right edge should be the full xRight (second bowl).
        double bowlRight = sky.QueryXInRange(1.0, 1.5);
        Assert.Equal(1.644, bowlRight, precision: 4);

        // At the stem level (bottom), the right edge is the second stem's right edge.
        // Second stem starts at ~0.74 with width ~0.25, so right edge ≈ 0.74 + 0.137 ≈ 0.88.
        double stemRight = sky.QueryXInRange(-0.6, -0.4);
        Assert.True(stemRight < 1.0,
            $"Double-flat stem skyline should be narrower than the full BBox. Got {stemRight}");
        Assert.True(stemRight > 0.5,
            $"Double-flat stem skyline must include the second stem. Got {stemRight}");
    }

    [Fact]
    public void Build_Natural_FallsBackToBoundingBox()
    {
        // Natural's silhouette is nearly rectangular; we use BBox.
        var sky = AccidentalGlyphSkyline.Build("natural", -1.34, 1.364, 0, 0.672, Skyline.Direction.Right);
        Assert.Equal(0.672, sky.QueryXInRange(-1.34, 1.364), precision: 6);
    }
}
