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

using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Tests for the configurable <c>MinItemGap</c> (LP <c>skyline-horizontal-padding</c>).
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/define-grobs.scm — skyline-horizontal-padding (LP default 0.1)
/// LILYPOND-REF: lily/separation-item.cc:49-68 — set_distance(padding)
/// </remarks>
[Trait("Category", "Unit")]
public class SeparatingPaddingTests
{
    private static NoteItem MakeNote(int staffPos = 0) =>
        new(staffPosition: staffPos,
            baseDuration: new Fraction(1, 4),
            dots: 0,
            accidental: null,
            needsLedgerLines: false,
            sourcePosition: 0);

    [Fact]
    public void DefaultMinItemGap_MatchesGlyphMetricsConstant()
    {
        Assert.Equal(GlyphMetrics.MinItemGap, NoteSpacingParameters.Default.MinItemGap);
    }

    [Fact]
    public void Skyline_DefaultPadding_AppliesGlyphMetricsValue()
    {
        var prev = MakeNote(0);
        var next = MakeNote(0);
        double dist = SpacingRules.CalculateSkylineDistance(prev, next, staffY: 0);
        Assert.True(dist >= NoteSpacingParameters.Default.MinItemGap,
            $"Skyline distance must be at least the default MinItemGap. Got {dist}.");
    }

    [Fact]
    public void Skyline_TightPadding_ProducesShorterDistance()
    {
        // LP-style tight padding (0.1) vs LilySharp default (0.4) should narrow the spacing.
        var prev = MakeNote(0);
        var next = MakeNote(0);
        var defaultParams = NoteSpacingParameters.Default;
        var tightParams = defaultParams with { MinItemGap = 0.1 };

        double defaultDist = SpacingRules.CalculateSkylineDistance(prev, next, staffY: 0, defaultParams);
        double tightDist = SpacingRules.CalculateSkylineDistance(prev, next, staffY: 0, tightParams);

        Assert.True(tightDist < defaultDist,
            $"Tight LP-style padding should yield shorter distance. default={defaultDist:F2}, tight={tightDist:F2}");
    }

    [Fact]
    public void Skyline_NullParams_UsesGlyphMetricsDefault()
    {
        // Passing null preserves the historical (non-parameter) behaviour.
        var prev = MakeNote(0);
        var next = MakeNote(0);
        double withNull = SpacingRules.CalculateSkylineDistance(prev, next, staffY: 0, noteParams: null);
        double withDefault = SpacingRules.CalculateSkylineDistance(prev, next, staffY: 0, noteParams: NoteSpacingParameters.Default);

        Assert.Equal(withNull, withDefault, precision: 4);
    }

    [Fact]
    public void Params_LpStylePadding_HasValueZeroPointOne()
    {
        // Sanity: confirm the LP default value can be expressed verbatim.
        var lpParams = NoteSpacingParameters.Default with { MinItemGap = 0.1 };
        Assert.Equal(0.1, lpParams.MinItemGap);
    }
}
