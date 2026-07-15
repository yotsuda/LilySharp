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
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A rows-only score (chords / lyrics, no staff) never runs ProcessForm, so its section starts
/// are laid out from the row tracks. The bar counter only looked for chord blocks NESTED in a
/// section; a part-major chord TRACK (chords X { section A { … } }) keeps its bars on the section
/// itself, so every section counted zero bars and stacked at bar 0 — the whole chart collapsed
/// onto one section's width. These lock the correct per-section bar layout.
/// </summary>
[Trait("Category", "Unit")]
public class ChordRowSectionLayoutTests
{
    private static int[] SectionLabelMeasures(string src)
    {
        var tree = SyntaxTree.Parse(src);
        var spec = RenderSpecParser.FindFirst(tree)!;
        var measures = new MeasureCollector().CollectMultiStaff(tree, spec)
            .StaffGroups.SelectMany(g => g.Staves).SelectMany(s => s.Voices).First().Measures;
        return measures.Select((m, i) => (m.SectionLabel, i))
            .Where(x => x.SectionLabel != null).Select(x => x.i).ToArray();
    }

    private static int MeasureCount(string src)
    {
        var tree = SyntaxTree.Parse(src);
        var spec = RenderSpecParser.FindFirst(tree)!;
        return new MeasureCollector().CollectMultiStaff(tree, spec)
            .StaffGroups.SelectMany(g => g.Staves).SelectMany(s => s.Voices).First().Measures.Length;
    }

    [Fact]
    public void TwoSingleBarSections_ChordsOnly_KeepTwoMeasures()
        => Assert.Equal(2, MeasureCount("""
            time 4/4
            chords prog { section A { c1 } section B { g1 } }
            form main { A B }
            score main { chords prog }
            """));

    [Fact]
    public void TwoTwoBarSections_ChordsOnly_KeepFourMeasures()
        => Assert.Equal(4, MeasureCount("""
            time 4/4
            chords prog { section A { c1 | f1 } section B { g1 | c1 } }
            form main { A B }
            score main { chords prog }
            """));

    [Fact]
    public void SectionLabels_LandAtEachSectionStart_NotStackedAtBarZero()
        => Assert.Equal(new[] { 0, 2 }, SectionLabelMeasures("""
            time 4/4
            chords prog { section A { c1 | f1 } section B { g1 | c1 } }
            form main { A B }
            score main { chords prog }
            """));
}
