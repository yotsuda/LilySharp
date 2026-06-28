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

using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;
using Xunit.Abstractions;

namespace LilySharp.Tests;

/// <summary>
/// Independent chord part: <c>chords name { … }</c> renders chord symbols, spread
/// across each measure by the default rhythm table (ChordRhythm).
/// </summary>
[Trait("Category", "Unit")]
public class ChordPartTests
{
    private readonly ITestOutputHelper _output;
    public ChordPartTests(ITestOutputHelper output) => _output = output;

    private static string Render(string source) =>
        SvgGenerator.Generate(SyntaxTree.Parse(source), new SvgRenderOptions { EmbedFont = false });

    [Fact]
    public void ChordPart_RendersChordSymbols()
    {
        var source = @"
time 4/4
section Main {
  melody { c'4 d e f | g a b c'' | }
  chords riff { c | g:7 c | }
}
structure { Main }
score ""x"" { chords riff  staff melody }
";
        var svg = Render(source);
        _output.WriteLine(svg);

        Assert.Contains(">G7</text>", svg);
        // Two C chords (m1 whole + m2 second half) + the G7.
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(svg, ">C</text>").Count);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(svg, ">G7</text>"));
    }

    [Fact]
    public void ChordPart_QualityAndDuration_RootSuffixForm()
    {
        // g4:m7 = Gm7, quarter (duration after the root, before the colon).
        var source = @"
time 4/4
section Main {
  melody { c'4 d e f | }
  chords riff { g4:m7 c4 a:m d:m | }
}
structure { Main }
score ""x"" { chords riff  staff melody }
";
        var svg = Render(source);
        _output.WriteLine(svg);

        Assert.Contains(">Gm7</text>", svg);
        Assert.Contains(">Am</text>", svg);
        Assert.Contains(">Dm</text>", svg);
    }
}
