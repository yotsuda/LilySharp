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
using System.Linq;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Layout;
using Xunit;

namespace LilySharp.Tests.Svg;

/// <summary>
/// What a script's padded profile is now: a function of the GLYPH, not of where on the page
/// the script happens to sit.
/// </summary>
/// <remarks>
/// ⚠️ THIS IS THE PROPERTY THE PADDING ORDER WAS CHOSEN FOR (user decision, 2026-08-17), and
/// nothing else in the repo can see it. LilyPond pads a grob's skyline grob-local
/// (LILYPOND-REF: lily/stencil-integral.cc:881-893 vertical_skylines_from_stencil — it pads
/// <c>skylines_from_stencil</c>'s answer, i.e. the grob's own frame), and the CONSUMER's half of
/// the padding — the copy <c>Skyline::distance(other, horizon_padding)</c> builds — was
/// applied to the PLACED skyline instead. Because the resolve's epsilons are absolute, that
/// made the decomposition depend on the placement: MEASURED, one fermata with one padding,
/// placed at x = 0 / 0.5 / 1 / 17.5 / 100 / 1000, resolved to 35 / 37 / 37 / 33 / 39 / 33
/// buildings. The same mark, the same padding, six different skylines.
/// <para>
/// ⚠️ THE CORPUS IS NOT AN INSTRUMENT FOR THIS. Both orders gave a byte-identical 566-book
/// SVG A/B, an unmoved 217-snapshot suite and an unmoved 516-point LP geometry ledger — the
/// difference is below every observer the repo has, which is exactly why it needs a test that
/// looks at the skyline itself rather than at anything drawn.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class ScriptProfilePaddingTests
{
    /// <summary>ScriptHorizonPadding — the consumer's, folded into the profile.</summary>
    private const double ExtraPad = 0.25;

    private static ArticulationLayout ScriptAt(string glyph, double declaredPad, double x)
        => new(MeasureIndex: 0, ItemIndex: 0, X: x, YUp: 3.25,
               Glyph: glyph, IsAbove: true, SourcePosition: 0,
               SkylineHorizontalPadding: declaredPad);

    /// <summary>
    /// ⚠️ THE FERMATA IS THE ONE THAT BITES — do not drop it as "one more glyph". Poisoning
    /// the order back (pad the placed copy) turns exactly ONE of these three red: the dot and
    /// the wedge are few enough buildings to survive either way, and a test built only from
    /// them would pass with the property gone. The other two are here to say that the
    /// property is about the ORDER and not about that one glyph.
    /// </summary>
    public static IEnumerable<object[]> Glyphs()
    {
        yield return [EmmentalerGlyphs.ArticStaccatoAbove.ToString(), 0.10];  // dot, declares padding
        yield return [EmmentalerGlyphs.ArticMarcatoAbove.ToString(), 0.0];    // wedge, sloped roofs
        yield return [EmmentalerGlyphs.FermataAbove.ToString(), 0.0];         // curve, 45 buildings
    }

    /// <summary>
    /// The same script padded the same way is the same skyline wherever it is placed —
    /// building for building, shifted by exactly the placement.
    /// </summary>
    [Theory]
    [MemberData(nameof(Glyphs))]
    public void ThePaddedProfileIsTheSameSkylineWhereverTheScriptSits(
        string glyph, double declaredPad)
    {
        var atOrigin = ArticulationEngraver.ScriptSkylines(
            ScriptAt(glyph, declaredPad, 0.0), 3.25, extraPad: ExtraPad).Up;

        // Spread over four decades: if the epsilons were still deciding, x = 1000 is where
        // it showed (the fermata lost six buildings there under the old order).
        foreach (double x in new[] { 0.5, 1.0, 17.5, 100.0, 1000.0 })
        {
            var placed = ArticulationEngraver.ScriptSkylines(
                ScriptAt(glyph, declaredPad, x), 3.25, extraPad: ExtraPad).Up;

            Assert.True(atOrigin.Buildings.Count == placed.Buildings.Count,
                $"x={x}: the padded profile resolved to {placed.Buildings.Count} buildings "
                + $"against {atOrigin.Buildings.Count} at the origin — the decomposition is "
                + "depending on the placement again, so the padding has moved back after it");

            for (int i = 0; i < atOrigin.Buildings.Count; i++)
            {
                var o = atOrigin.Buildings[i];
                var p = placed.Buildings[i];
                Assert.Equal(o.Start + x, p.Start, 9);
                Assert.Equal(o.End + x, p.End, 9);
                Assert.Equal(o.ValueAt(o.Start), p.ValueAt(p.Start), 9);
            }
        }
    }

    /// <summary>
    /// The positive control: the consumer's padding is really being applied. Without this a
    /// fold that silently dropped <c>extraPad</c> would pass the theory above perfectly —
    /// an unpadded profile is just as placement-independent.
    /// </summary>
    [Theory]
    [MemberData(nameof(Glyphs))]
    public void TheConsumersPaddingIsActuallyInTheProfile(string glyph, double declaredPad)
    {
        var script = ScriptAt(glyph, declaredPad, 17.5);
        var bare = ArticulationEngraver.ScriptSkylines(script, 3.25).Up;
        var padded = ArticulationEngraver.ScriptSkylines(script, 3.25, extraPad: ExtraPad).Up;

        double bareLeft = bare.Buildings.Min(b => b.Start);
        double bareRight = bare.Buildings.Max(b => b.End);
        double paddedLeft = padded.Buildings.Min(b => b.Start);
        double paddedRight = padded.Buildings.Max(b => b.End);

        // LILYPOND-REF: lily/skyline.cc:558-615 Skyline::padded (horizon_padding) — flat for
        // the padding, then a 45° run-out, so the profile reaches at least that much further
        // each way.
        Assert.True(bareLeft - paddedLeft >= ExtraPad - 1e-9,
            $"the padded profile only reaches {bareLeft - paddedLeft:F6} further left, "
            + $"not the declared {ExtraPad}");
        Assert.True(paddedRight - bareRight >= ExtraPad - 1e-9,
            $"the padded profile only reaches {paddedRight - bareRight:F6} further right, "
            + $"not the declared {ExtraPad}");
    }
}
