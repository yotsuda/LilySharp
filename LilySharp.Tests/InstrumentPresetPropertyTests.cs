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
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// LYS1050 and LYS1051 (owner's request 2026-10-24): a part's own <c>octave</c> or <c>tuning</c>
/// that slips against its <c>instrument</c> preset.
/// </summary>
public class InstrumentPresetPropertyTests
{
    private static Diagnostic[] Warnings(string header, string code)
    {
        string text = $"part b {{ {header} }}\nsection A {{ b {{ c4 d e f }} }}\nscore {{ staff b }}\n";
        var tree = SyntaxTree.Parse(text);
        return [.. SemanticValidation.Run(tree).Where(d => d.Code == code)];
    }

    [Fact]
    public void AnOctaveBelowABassPresets_IsWarned_WithBothPitches()
    {
        var w = Assert.Single(Warnings("instrument bass octave 2", DiagnosticCodes.OctaveBelowTransposingPreset));
        Assert.Equal(DiagnosticSeverity.Warning, w.Severity);
        Assert.Contains("octave 2 writes a bare c as C2, which sounds C1", w.Message);
        Assert.Contains("instrument bass already sounds 1 octave below the page", w.Message);
        Assert.Contains("written from octave 3 (a bare c is C3 and sounds C2)", w.Message);
    }

    [Theory]
    [InlineData("instrument bass octave 3")]   // the preset's own
    [InlineData("instrument bass octave 4")]   // higher: not the trap
    [InlineData("instrument bass")]            // no octave written
    [InlineData("instrument cello octave 2")]  // the cello sounds as written
    [InlineData("instrument bass octave 2 transposition 8va")]   // the part's own transposition moves it up
    public void NoTrap_NoWarning(string header)
        => Assert.Empty(Warnings(header, DiagnosticCodes.OctaveBelowTransposingPreset));

    [Fact]
    public void ATuningOfAnotherInstrument_IsWarned_WithBothStrings()
    {
        var w = Assert.Single(Warnings("instrument ukulele tuning guitar", DiagnosticCodes.TuningOfAnotherInstrument));
        Assert.Equal(DiagnosticSeverity.Warning, w.Severity);
        Assert.Contains("tuning guitar gives this part another instrument's strings (E2 A2 D3 G3 B3 E4)", w.Message);
        Assert.Contains("instrument ukulele is strung in ukulele (G4 C4 E4 A4)", w.Message);
    }

    [Theory]
    [InlineData("instrument guitar tuning guitarDropD")]   // an override within the family
    [InlineData("instrument bass tuning bass5")]
    [InlineData("instrument violin tuning mandolin")]      // the same strings
    [InlineData("instrument flute tuning guitar")]         // a preset without strings has none to slip from
    [InlineData("tuning guitar")]                          // no preset
    public void ATuningOfItsOwnFamily_IsNotWarned(string header)
        => Assert.Empty(Warnings(header, DiagnosticCodes.TuningOfAnotherInstrument));
}
