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
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A section the form does not play prints no chords: until session 1364 its chords row was
/// placed at the first bar, so `form { A B }` with a section C kept for later printed C's chords
/// crammed into A's bars, and an excerpt score (`score practice { form practice … }`) printed
/// every other section's chords over its own (Lab sessions/p1362/eval5 jpop r-07, r-07b).
/// </summary>
public class ChordRowUnplayedSectionTests
{
    private const string Sections = "octave absolute\npart vo { instrument soprano }\n"
        + "section A { chords harmony { C | F | } vo { c'1 | f'1 | } }\n"
        + "section B { chords harmony { G | Am | } vo { g'1 | a'1 | } }\n"
        + "section C { chords harmony { Dm7 | E7 | } vo { d'1 | e'1 | } }\n";

    private static string Chords(string source, string? score = null)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join(" | ", tree.Diagnostics.Select(d => d.Message)));
        var spec = score == null ? RenderSpecParser.FindFirst(tree) : RenderSpecParser.FindAll(tree).Single(s => s.Name == score);
        var collected = SvgGenerator.CollectScore(tree, spec);
        return string.Join(" ", collected.ChordNames.OrderBy(c => c.MeasureIndex).Select(c => $"{c.MeasureIndex}:{c.ChordText}"));
    }

    [Fact]
    public void ASectionTheFormLeavesOut_PrintsNoChords()
        => Assert.Equal("0:C 1:F 2:G 3:Am",
            Chords(Sections + "form { A B }\nscore { chords harmony  staff vo }\n"));

    [Fact]
    public void AnExcerptScore_PrintsItsOwnSectionsChords()
    {
        string source = Sections + "form { A B C }\nform practice { B }\n"
            + "score { chords harmony  staff vo }\nscore practice { form practice  chords harmony  staff vo }\n";
        Assert.Equal("0:C 1:F 2:G 3:Am 4:Dm7 5:E7", Chords(source));
        Assert.Equal("0:G 1:Am", Chords(source, "practice"));
    }

    /// <summary>A score of rows alone places its sections off the form (EnsureSectionStartsForRows)
    /// and a row read with no section at all keeps the first bar.</summary>
    [Fact]
    public void RowsAlone_KeepTheirPlaces()
    {
        Assert.Equal("0:G 1:Am 2:C 3:F", Chords("octave absolute\nchords harmony {\n  section A { C | F | }\n  section B { G | Am | }\n}\n"
            + "form { B A }\nscore { chords harmony }\n"));
    }
}
