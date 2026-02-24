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
/// Tests for tremolo beam width and slope calculations.
/// </summary>
/// <remarks>
/// LILYPOND-REF: stem-tremolo.cc:45-94 calc-width, calc-slope
/// </remarks>
[Trait("Category", "Unit")]
public class TremoloEngraverTests
{
    [Fact]
    public void GetBeamWidth_WithFlag_Returns1_0()
    {
        // LILYPOND-REF: stem-tremolo.cc:81-94 — flagged notes use width 1.0
        Assert.Equal(1.0, TremoloEngraver.GetBeamWidth(hasFlag: true));
    }

    [Fact]
    public void GetBeamWidth_WithoutFlag_Returns1_5()
    {
        // LILYPOND-REF: stem-tremolo.cc:81-94 — non-flagged notes use width 1.5
        Assert.Equal(1.5, TremoloEngraver.GetBeamWidth(hasFlag: false));
    }

    [Fact]
    public void GetBeamSlope_StemUp_Returns0_25()
    {
        // LILYPOND-REF: stem-tremolo.cc:45-79 — default slope is 0.25
        Assert.Equal(0.25, TremoloEngraver.GetBeamSlope(stemUp: true, hasFlag: false));
        Assert.Equal(0.25, TremoloEngraver.GetBeamSlope(stemUp: true, hasFlag: true));
    }

    [Fact]
    public void GetBeamSlope_StemDownWithFlag_Returns0_40()
    {
        // LILYPOND-REF: stem-tremolo.cc:45-79 — down stem with flag uses steeper slope 0.40
        Assert.Equal(0.40, TremoloEngraver.GetBeamSlope(stemUp: false, hasFlag: true));
    }

    [Fact]
    public void GetBeamSlope_StemDownWithoutFlag_Returns0_25()
    {
        // LILYPOND-REF: stem-tremolo.cc:45-79 — down stem without flag uses default 0.25
        Assert.Equal(0.25, TremoloEngraver.GetBeamSlope(stemUp: false, hasFlag: false));
    }

    [Fact]
    public void GetBeamThickness_Returns0_48()
    {
        // LILYPOND-REF: define-grobs.scm:2785 beam-thickness = 0.48
        Assert.Equal(0.48, TremoloEngraver.GetBeamThickness());
    }

    [Fact]
    public void GetBeamGap_Returns0_8()
    {
        // LILYPOND-REF: define-grobs.scm:2780 beam-gap = 0.8
        Assert.Equal(0.8, TremoloEngraver.GetBeamGap());
    }
}
