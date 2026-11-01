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

using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The preview's page carries one invisible box per bar per staff (rect.bar-staff) with where
/// that bar is written (data-start): a click on the bar in the preview takes the editor there
/// (owner request 2026-10-14). The place is the bar's first note, rest or chord — not the
/// barline before it, and in a section's first bar not the section's own start.
/// </summary>
[Trait("Category", "Unit")]
public class BarStaffBoxTests
{
    private const string Book = """
        octave absolute
        time 4/4
        part rh { }
        part lh { clef bass }
        section A {
          partial 4
          rh { g''4 | c'''1 | d'''2 e''' | }
          lh { r4 | c'1 | d'1 | }
        }
        section B {
          rh { f'''1 | }
          lh {
            f2 g | }
        }
        form { A B }
        score { staff rh staff lh }
        """;

    private static readonly Regex Box = new(
        @"<rect class=""bar-staff"" data-start=""(\d+)""(?: data-span=""\d+ \d+"")? x=""([^""]+)"" y=""([^""]+)"" width=""([^""]+)"" height=""([^""]+)""");

    [Fact]
    public void EachBarOfEachStaff_GoesToItsFirstNote()
    {
        string preview = SvgGenerator.Generate(SyntaxTree.Parse(Book), SvgRenderOptions.Preview());
        var boxes = Box.Matches(preview).Cast<Match>().ToList();
        Assert.Equal(8, boxes.Count);   // four bars, two staves

        // The upper staff stands higher on the page (a smaller device y).
        double top = boxes.Min(b => Num(b, 3));
        string Written(Match b) => Book[int.Parse(b.Groups[1].Value)..].TrimStart().Split(' ', '\n', '\r')[0];
        Assert.Equal(new[] { "g''4", "c'''1", "d'''2", "f'''1" },
            boxes.Where(b => Num(b, 3) == top).Select(Written));
        Assert.Equal(new[] { "r4", "c'1", "d'1", "f2" },
            boxes.Where(b => Num(b, 3) != top).Select(Written));

        // A staff tall, and as wide as the bar's own box.
        Assert.All(boxes, b => Assert.Equal(4, Num(b, 5), 3));
        var bars = Regex.Matches(preview, @"<rect class=""bar-box"" data-bar=""\d+"" x=""([^""]+)"" y=""[^""]+"" width=""([^""]+)""");
        Assert.Equal(bars.Select(m => m.Groups[1].Value).OrderBy(x => x),
            boxes.Where(b => Num(b, 3) == top).Select(b => b.Groups[2].Value).OrderBy(x => x));
    }

    /// <summary>Each bar's written stretch (data-span, the caret in it lights the bar): from its
    /// first note to the bar line closing it — the bar's own text, never the section's start nor
    /// another part's text before a section's first bar (Measure.SourceStart has both).</summary>
    [Fact]
    public void EachBarOfEachStaff_SpansItsOwnText()
    {
        string preview = SvgGenerator.Generate(SyntaxTree.Parse(Book), SvgRenderOptions.Preview());
        var spans = Regex.Matches(preview, @"data-start=""(\d+)"" data-span=""(\d+) (\d+)""").Cast<Match>()
            .Select(m => (S: int.Parse(m.Groups[1].Value), F: int.Parse(m.Groups[2].Value), T: int.Parse(m.Groups[3].Value)))
            .ToList();
        Assert.Equal(8, spans.Count);
        Assert.All(spans, x => Assert.Equal(x.S, x.F));
        Assert.Equal(new[] { "g''4 |", "r4 |", "c'''1 |", "c'1 |", "d'''2 e''' |", "d'1 |", "f'''1 |", "f2 g |" },
            spans.Select(x => Book[x.F..(x.T + 1)]));
    }

    /// <summary>A drum chart written in phrase words (owner's report 2026-11-01: a kit of
    /// <c>drums { hats back kick } |</c> bars stayed lit in the preview whatever the caret did). The
    /// chart's hits carry the phrase bodies' positions, so each bar's stretch ran from the phrases
    /// at the top of the file down to its own bar line, and every Dr bar held the caret. A bar is
    /// written where its chart is: its stretch is the chart's text, and a click on it goes there.</summary>
    [Fact]
    public void ADrumChartOfPhrases_SpansItsChart_NotThePhraseBodies()
    {
        const string book = "time 4/4\npart kit { clef percussion }\nphrase hats { hh8 hh hh hh hh hh hh hh | }\nphrase kick { bd4 r bd r | }\n"
            + "section A {\n  kit { drums { hats kick } | drums { hats kick } | }\n}\nform { A }\nscore { staff kit }\n";
        string preview = SvgGenerator.Generate(SyntaxTree.Parse(book), SvgRenderOptions.Preview());
        var spans = Regex.Matches(preview, @"data-start=""(\d+)"" data-span=""(\d+) (\d+)""").Cast<Match>()
            .Select(m => (S: int.Parse(m.Groups[1].Value), F: int.Parse(m.Groups[2].Value), T: int.Parse(m.Groups[3].Value)))
            .ToList();
        Assert.Equal(new[] { "drums { hats kick } |", "drums { hats kick } |" }, spans.Select(x => book[x.F..(x.T + 1)]));
        Assert.All(spans, x => Assert.Equal(x.S, x.F));
        // The hits themselves still point into the phrase bodies (a click on a note goes there).
        Assert.Contains($"data-pos=\"{book.IndexOf("hh8")}\"", preview);
        // A chart written out keeps its own text too — the chart's start, not its first hit's.
        string inline = book.Replace("drums { hats kick }", "drums { { hh8 hh hh hh hh hh hh hh } { bd4 r bd r } }");
        var own = Regex.Matches(SvgGenerator.Generate(SyntaxTree.Parse(inline), SvgRenderOptions.Preview()), @"data-span=""(\d+) (\d+)""").Cast<Match>()
            .Select(m => inline[int.Parse(m.Groups[1].Value)..(int.Parse(m.Groups[2].Value) + 1)]).ToList();
        Assert.Equal(2, own.Count);
        Assert.All(own, s => Assert.Equal("drums { { hh8 hh hh hh hh hh hh hh } { bd4 r bd r } } |", s));
    }

    [Fact]
    public void AnExportedPage_HasNone()
        => Assert.DoesNotContain("bar-staff", SvgGenerator.Generate(SyntaxTree.Parse(Book), SvgRenderOptions.Default));

    private static double Num(Match m, int g) => double.Parse(m.Groups[g].Value, CultureInfo.InvariantCulture);
}
