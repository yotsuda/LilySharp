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
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A SMuFL font's <c>engravingDefaults</c> set the line thicknesses the score did not write
/// (docs/smufl-design.md §1 "寸法の優先順": written ＞ the font's ＞ LilyPond's; §6 ② ⒡).
/// Bravura: staff line 0.13, stem 0.12, ledger 0.16 reaching 0.4 past a 1.18-wide head,
/// beam 0.5, bar lines 0.16 and 0.5.
/// </summary>
public class SmuflEngravingTests
{
    private const string Book =
        "part m { clef treble }\nsection A { m { c'8 d' e' f' g'2 } }\nform main { A }\nscore main { staff m }\n";

    private const double Eps = 1e-9;

    private static EngravingStyle StyleOf(string head, params string[] settings)
    {
        var tree = SyntaxTree.Parse(head + "\n" + Book);
        PaperOverrides? set = null;
        if (settings.Length > 0)
        {
            set = PaperOverrides.Parse(settings, out var error);
            Assert.Null(error);
        }
        return SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree), settings: set).Paper.Style;
    }

    [Fact]
    public void ABravuraScore_TakesBravurasThicknesses_InLilyPondsUnits()
    {
        var s = StyleOf("fonts { music \"Bravura\" }");
        Assert.Equal(0.13, s.StaffLineThickness, Eps);
        Assert.Equal(0.12, s.StemThickness * s.StaffLineThickness, Eps);
        Assert.Equal(0.16, s.LedgerLineThicknessLines * s.StaffLineThickness + s.LedgerLineThicknessSpaces, Eps);
        Assert.Equal(0.4 / 1.18, s.LedgerLengthFraction, Eps);
        Assert.Equal(0.5, s.BeamThickness, Eps);
        Assert.Equal(0.16, s.BarLineHairThickness * s.LineThickness, Eps);
        Assert.Equal(0.5, s.BarLineThickThickness * s.LineThickness, Eps);
        // What the font does not speak for stays LilyPond's.
        Assert.Equal(EngravingStyle.Default.LineThickness, s.LineThickness);
        Assert.Equal(EngravingStyle.Default.BeamDamping, s.BeamDamping);
    }

    [Fact]
    public void WhatTheScoreWrites_WinsOverTheFont_EvenAtLilyPondsOwnValue()
    {
        var s = StyleOf("fonts { music \"Bravura\" }\nlayout { Stem.thickness 1.3  Beam.thickness 0.48 }");
        Assert.Equal(1.3, s.StemThickness);
        Assert.Equal(0.48, s.BeamThickness);
        Assert.Equal(0.13, s.StaffLineThickness, Eps);   // the rest is still Bravura's
        var set = StyleOf("fonts { music \"Bravura\" }", "BarLine.thickThickness=6");
        Assert.Equal(6.0, set.BarLineThickThickness);
    }

    [Fact]
    public void AWrittenLineThickness_StillMovesTheFontsLinesTogether()
    {
        var s = StyleOf("fonts { music \"Bravura\" }\nlayout { lineThickness 0.2 }");
        Assert.Equal(0.26, s.StaffLineThickness, Eps);
        Assert.Equal(0.24, s.StemThickness * s.StaffLineThickness, Eps);
        Assert.Equal(1.0, s.BarLineThickThickness * s.LineThickness, Eps);
    }

    [Fact]
    public void AnEmmentalerScore_KeepsTheStyleItWrote()
    {
        Assert.Equal(EngravingStyle.Default, StyleOf(""));
        var s = StyleOf("layout { Stem.thickness 1.5 }");
        Assert.Equal(1.5, s.StemThickness);
        Assert.Equal(EngravingStyle.Default.StaffLineThickness, s.StaffLineThickness);
    }

    [Fact]
    public void AChain_IsEngravedByItsFirstFont()
    {
        // Leland's staff line is 0.11, Bravura's 0.13.
        Assert.Equal(0.11, StyleOf("fonts { music \"Leland\" \"Bravura\" }").StaffLineThickness, Eps);
    }
}
