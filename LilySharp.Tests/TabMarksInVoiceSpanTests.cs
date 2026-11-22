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
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The marks a numbers-only tab copies from its part — <c>@pluck</c>'s letter and a fretted
/// <c>@vibrato</c>'s wave — stand by the tab's own digits inside a <c>voice { } { }</c> span as
/// outside it (evaluation 5, guitar r-01, r-08). Until 2026-11-22 the letter reached for a stem
/// the numbers-only tab does not have (LilyPond gives its stems no length), and the wave read the
/// tab as a five-line staff of the notes' pitches; inside a span both stood up under the notation
/// staff, where they read as a second copy on the other voice.
/// </summary>
[Trait("Category", "Unit")]
public class TabMarksInVoiceSpanTests
{
    // Bar 1 the span (the lower voice on the bass strings), bar 2 the same upper notes alone.
    private const string Book =
        "octave absolute\npart gtr { instrument guitar }\nsection A {\n  gtr {\n" +
        "    voice { a'4.@vibrato b'8@pluck(i) a'2 | } { e,1 | }\n" +
        "    a'4.@vibrato b'8@pluck(i) a'2 |\n" +
        "  }\n}\nform { ~A }\nscore { staff gtr  tab gtr }\n";

    private static ScoreLayout Render()
    {
        var tree = SyntaxTree.Parse(Book);
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        return new LayoutEngine().Layout(score);
    }

    [Fact]
    public void ThePluckLetter_OnTheTab_StandsAsHighInTheSpanAsOutsideIt()
    {
        var letters = Render().ArticulationLayouts.Where(a => a.StaffIndex == 1).OrderBy(a => a.MeasureIndex).ToList();
        Assert.Equal(2, letters.Count);
        Assert.Equal(letters[1].YUp, letters[0].YUp, 6);
        Assert.True(letters[0].IsAbove);
    }

    [Fact]
    public void TheVibratoWave_OnTheTab_StandsAsHighInTheSpanAsOutsideIt()
    {
        var waves = Render().TrillSpannerLayouts.Where(t => t.StaffIndex == 1).OrderBy(t => t.StartMeasureIndex).ToList();
        Assert.Equal(2, waves.Count);
        Assert.Equal(waves[1].YUp, waves[0].YUp, 6);
    }
}
