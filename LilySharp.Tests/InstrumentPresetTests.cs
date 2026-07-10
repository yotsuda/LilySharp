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

using System.Linq;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The `instrument` preset role: it supplies a part's clef/octave AND (for fretted
/// instruments) a default tab tuning. `name` is the display label; explicit `clef`
/// and `tuning` override the preset.
/// </summary>
[Trait("Category", "Unit")]
public sealed class InstrumentPresetTests
{
    private static TuningType TabTuningOf(string source)
    {
        var spec = RenderSpecParser.FindFirst(SyntaxTree.Parse(source))!;
        return spec.Items.OfType<TabStaffSpec>().Single().Tuning;
    }

    [Fact]
    public void Instrument_SuppliesTabTuning()
    {
        // `instrument bass` implies the 4-string bass tuning for a tab, no explicit
        // `tuning` needed.
        var tuning = TabTuningOf(
            "part bl { instrument bass }\nsection A { bl { e,4 a, d g | } }\nform main { A }\nscore { tab bl }\n");
        Assert.Equal(TuningType.Bass, tuning);
    }

    [Fact]
    public void ExplicitTuning_OverridesInstrumentPreset()
    {
        // `tab guitar bl` pins guitar even though the instrument is a bass.
        var tuning = TabTuningOf(
            "part bl { instrument bass }\nsection A { bl { e,4 | } }\nform main { A }\nscore { tab guitar bl }\n");
        Assert.Equal(TuningType.Guitar, tuning);
    }

    [Fact]
    public void PartTuningProperty_OverridesInstrumentPreset()
    {
        var tuning = TabTuningOf(
            "part bl { instrument bass tuning bass5 }\nsection A { bl { e,4 | } }\nform main { A }\nscore { tab bl }\n");
        Assert.Equal(TuningType.Bass5, tuning);
    }

    [Fact]
    public void NoInstrumentNoTuning_DefaultsToGuitar()
    {
        // Unchanged baseline: a tab with no tuning hint is a guitar.
        var tuning = TabTuningOf(
            "part gtr { }\nsection A { gtr { c4 | } }\nform main { A }\nscore { tab gtr }\n");
        Assert.Equal(TuningType.Guitar, tuning);
    }

    [Theory]
    [InlineData("bass", "bass")]
    [InlineData("electric-bass", "bass")]
    [InlineData("guitar", "guitar")]
    [InlineData("ukulele", "ukulele")]
    [InlineData("violin", null)]   // bowed: not a tab instrument
    [InlineData(null, null)]
    public void GetTuning_MapsFrettedInstruments(string? instrument, string? expected)
    {
        Assert.Equal(expected, InstrumentDefaults.GetTuning(instrument));
    }
}
