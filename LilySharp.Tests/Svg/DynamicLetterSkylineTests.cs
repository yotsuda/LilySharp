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
using Xunit.Abstractions;

namespace LilySharp.Tests.Svg;

/// <summary>
/// The baked fetaText dynamic-letter outlines (GlyphSkylinesGenerated, session 36):
/// cross-checked against the letter BOXES the OTHER generator bakes
/// (Extract-EmmentalerMetrics.py's DynamicLetter* via <see cref="GlyphMetrics.TryGetDynamicInk"/>)
/// — two independent walks over one font must agree at the profile extremes.
/// </summary>
/// <remarks>
/// These letters are DATA today: nothing consumes them until the dynamic-support port
/// builds the label's own profile (my_dim) from them (HANDOFF session 36; ledger
/// staff.staff.dynamic-* whys carry the port design). The net exists so the bake cannot
/// silently rot before that port lands — a wrong glyph, axis, sign or scale misses the
/// box by half an em, far past the flattening tolerance asserted here.
/// </remarks>
[Trait("Category", "Unit")]
public class DynamicLetterSkylineTests
{
    private readonly ITestOutputHelper _output;

    public DynamicLetterSkylineTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// The resolved profiles' extremes are the letter's ink box, within the outline
    /// flattening's sagitta: LilyPond flattens cubics to max(2, len/0.2) segments
    /// (lily/freetype.cc:128-150), so a curve apex between segment endpoints may sit
    /// UNDER the true box top by up to ~len²·κ/8, and can never sit over it.
    /// </summary>
    [Theory]
    [InlineData('f')]
    [InlineData('m')]
    [InlineData('n')]
    [InlineData('p')]
    [InlineData('r')]
    [InlineData('s')]
    [InlineData('z')]
    public void ProfileExtremes_AreTheLetterBox_WithinTheFlatteningSagitta(char c)
    {
        var (downQuads, upQuads) = GlyphMetrics.DynamicLetterVerticalSkylineQuads(c);
        Assert.True(GlyphMetrics.TryGetDynamicInk(c.ToString(), out double bottom, out double top));

        var up = VerticalSkyline.FromGlyphOutline(
            VerticalDirection.Up, upQuads, StaffSize.FullSize, x: 0, y: 0);
        var down = VerticalSkyline.FromGlyphOutline(
            VerticalDirection.Down, downQuads, StaffSize.FullSize, x: 0, y: 0);

        _output.WriteLine($"'{c}': up.Max={up.MaxHeight():F6} boxTop={top:F6} "
            + $"down.Max={down.MaxHeight():F6} boxBottom={bottom:F6}");

        // MaxHeight answers in REAL Y for both directions (skyline.cc:667-680): the UP
        // extreme is the box top, the DOWN extreme the box bottom. Flattening can only
        // pull an extreme INWARD (toward the ink), never past the box.
        const double sagitta = 0.01;
        Assert.InRange(up.MaxHeight(), top - sagitta, top + 1e-6);
        Assert.InRange(down.MaxHeight(), bottom - 1e-6, bottom + sagitta);
    }

    /// <summary>A char outside the seven-letter encoding answers default, the same
    /// "caller falls back" contract <see cref="GlyphMetrics.TryGetDynamicInk"/> has.</summary>
    [Fact]
    public void NonDynamicLetter_AnswersDefault()
    {
        Assert.Equal(default, GlyphMetrics.DynamicLetterVerticalSkylineQuads('q'));
        Assert.Equal(0.0, GlyphMetrics.DynamicLetterKern('f', 'q'));
    }
}
