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
  chords riff { C | G7 C | }
}
form main { Main }
score main ""x"" { chords riff  staff melody }
";
        var svg = Render(source);
        _output.WriteLine(svg);

        // ⚠️ TWO text runs, not one: the root stands on the baseline and the 7 is RAISED and
        // reduced, the way LilyPond builds a chord name (ChordNameGlyphRun.SuperRaise). A
        // symbol asserted as ONE run is asserting the picture Lily# drew before the
        // superscript was ported.
        Assert.Contains(">G</text>", svg);
        Assert.Contains(">7</text>", svg);
        // Two C chords (m1 whole + m2 second half); the G7's pieces are G and 7.
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(svg, ">C</text>").Count);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(svg, ">7</text>"));
    }

    [Fact]
    public void ChordPart_Standalone_NoStaff_SpreadsAcrossBars()
    {
        // A chords-only lead sheet (no music staff) renders the symbols spread
        // across bars by their rhythm — not collapsed at one X.
        var source = @"
time 4/4
section Main {
  chords riff { C | Am | F | G7 | }
}
form main { Main }
score main ""x"" { chords riff }
";
        var svg = Render(source);
        _output.WriteLine(svg);

        Assert.Contains(">Am</text>", svg);
        // The 7 is a raised run of its own (see ChordPart_RendersChordSymbols).
        Assert.Contains(">G</text>", svg);
        Assert.Contains(">7</text>", svg);

        double X(string text)
        {
            var m = System.Text.RegularExpressions.Regex.Match(
                svg, "<text[^>]*\\bx=\"([0-9.]+)\"[^>]*>" + text + "</text>");
            Assert.True(m.Success, $"chord '{text}' not found");
            return double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        }
        // C (bar 1) < Am (bar 2) < F (bar 3) < G7 (bar 4): the bars have real width. The
        // G7's ROOT carries its X — the raised 7 stands to the right of it, so either piece
        // answers the question, and the root is the symbol's own reference point.
        Assert.True(X("C") < X("Am") && X("Am") < X("F") && X("F") < X("G"),
            "chords collapsed instead of spreading across bars");
    }

    [Fact]
    public void ChordPart_EmptyBar_SkipsAndCountsTheBar()
    {
        // A bare "|" is an empty chord bar: it is skipped (no chord) but still
        // counts, so the following chords land in the right measures.
        var source = @"
time 4/4
section Main { chords riff { C | | F | G7 | } }
form main { Main }
score main ""x"" { chords riff }
";
        var tree = SyntaxTree.Parse(source);
        var spec = RenderSpecParser.FindFirst(tree);
        var score = new MeasureCollector().CollectMultiStaff(tree, spec!);

        var byText = score.ChordNames.ToDictionary(c => c.ChordText, c => c.MeasureIndex);
        Assert.Equal(0, byText["C"]);
        Assert.Equal(2, byText["F"]);   // bar 1 (index 1) was empty -> F is bar 3
        Assert.Equal(3, byText["G7"]);
    }

    [Fact]
    public void ChordPart_QualityAndDuration_RootSuffixForm()
    {
        // g4:m7 = Gm7, quarter (duration after the root, before the colon).
        var source = @"
time 4/4
section Main {
  melody { c'4 d e f | }
  chords riff { Gm7 C Am Dm | }
}
form main { Main }
score main ""x"" { chords riff  staff melody }
";
        var svg = Render(source);
        _output.WriteLine(svg);

        // `Gm` on the baseline (the minor modifier stays down) and `7` raised.
        Assert.Contains(">Gm</text>", svg);
        Assert.Contains(">7</text>", svg);
        Assert.Contains(">Am</text>", svg);
        Assert.Contains(">Dm</text>", svg);
    }
}
