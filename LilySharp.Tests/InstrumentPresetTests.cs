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

    [Fact]
    public void TranspositionMarkers_AreExactlyWhatTheSwitchReads()
    {
        // ★ The published list and the switch beside it are two spellings of one vocabulary, and
        // a list that nobody checks drifts. Published 2026-08-19 so the editor's grammar could
        // colour these — `transposition 8vb` was plain, key and value both — and this is what
        // stops the grammar from being taught a marker the language does not read.
        foreach (string marker in InstrumentDefaults.TranspositionMarkers)
        {
            Assert.True(InstrumentDefaults.ParseTranspositionSemitones(marker) is not null,
                $"`{marker}` is published as a transposition marker and the parser returns null "
                + "for it, so a book that writes it is silently untransposed");
        }

        // ⚠️ And the other direction, as far as a switch permits: a marker the list omits would
        // be readable and uncoloured, which is exactly the half-coloured state this closed. These
        // four are the whole switch; a fifth arriving without a line here is the drift.
        Assert.Equal(4, InstrumentDefaults.TranspositionMarkers.Count);
        Assert.Null(InstrumentDefaults.ParseTranspositionSemitones("8vc"));

        // ⚠️ Case-insensitive, alone among the part-header vocabularies — recorded because the
        // grammar deliberately colours the lower-case spellings only.
        Assert.Equal(InstrumentDefaults.ParseTranspositionSemitones("8vb"),
                     InstrumentDefaults.ParseTranspositionSemitones("8VB"));
    }
}
