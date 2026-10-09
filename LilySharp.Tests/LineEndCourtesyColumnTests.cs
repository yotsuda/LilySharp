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
using System.Text.RegularExpressions;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The END-of-line courtesy group is a column per break-align symbol across the staves
/// (session 830, SpacingRules.LineEndCourtesy): where the staves change key differently — a
/// transposed part over a concert one — each staff's cancellation, signature and meter stand in
/// the same column, as LilyPond's Score-level Break_align_engraver puts them. MEASURED against
/// LilyPond 2.26.0 on these two books' `lysc ly` twins (Lab sessions/p830/ck, a.ly and b.ly).
/// </summary>
public sealed class LineEndCourtesyColumnTests
{
    private static string Book(string fromKey, string toKey) => $$"""
        paper { raggedRight }
        octave absolute
        time 4/4
        key {{fromKey}}
        part upper { clef treble transpose d }
        part lower { clef bass }
        section A { upper { c''4 d'' e'' f'' | g''1 | } lower { c4 d e f | g1 | } }
        section B { upper { key {{toKey}} time 3/4 g''4 a'' b'' | c'''2. | } lower { key {{toKey}} time 3/4 g4 a b | c'2. | } }
        form { A break B }
        score { staff upper  staff lower }
        """;

    private static (char Glyph, double X, double Y)[] Glyphs(string svg) =>
        [.. Regex.Matches(svg, "<text class=\"music\"(?: font-family=\"[^\"]*\")? x=\"([-\\d.]+)\" y=\"([-\\d.]+)\"[^>]*>(.)</text>")
            .Select(m => (m.Groups[3].Value[0], double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)))];

    private static string Svg(string book) =>
        SvgGenerator.Generate(SyntaxTree.Parse(book), new SvgRenderOptions { EmbedFont = false });

    /// <summary>
    /// C→G under a transposed part: the upper staff's courtesy is three sharps (D→A), the lower's
    /// one — and both meters stand after the three, where LilyPond draws them (the lower one was
    /// 2.2 short), 5.78 right of the final bar line's left edge.
    /// </summary>
    [Fact]
    public void BothStavesMeters_StandInOneColumn_AfterTheWidestKey()
    {
        string svg = Svg(Book("c major", "g major"));
        var meters = Glyphs(svg).Where(g => g.Glyph == '3').OrderByDescending(g => g.X).Take(2).ToArray();
        Assert.NotEqual(meters[0].Y, meters[1].Y);                       // one on each staff
        Assert.InRange(meters[0].X - meters[1].X, -0.011, 0.011);
        double finalBar = Regex.Matches(svg, "<rect x=\"([-\\d.]+)\" y=\"[-\\d.]+\" width=\"0.19\"")
            .Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
            .Where(x => x < meters[0].X).Max();
        Assert.InRange(meters[0].X - finalBar - 5.78, -0.011, 0.011);   // LilyPond 54.22 − 48.44
    }

    /// <summary>
    /// A→G over G→F: the upper staff cancels two sharps, the lower one — the lower staff's natural
    /// stands under the upper's first (the cancellation column), and both meters again in one column.
    /// </summary>
    [Fact]
    public void BothStavesCancellations_OpenTheSameColumn()
    {
        var glyphs = Glyphs(Svg(Book("g major", "f major")));
        double lastMeter = glyphs.Where(g => g.Glyph == '3').Max(g => g.X);
        var meters = glyphs.Where(g => g.Glyph == '3' && g.X > lastMeter - 0.5).ToArray();
        Assert.Equal(2, meters.Length);
        Assert.InRange(meters[0].X - meters[1].X, -0.011, 0.011);
        // The line-end naturals: right of the last note, left of the meters.
        var naturals = glyphs.Where(g => g.Glyph == '' && g.X > lastMeter - 6 && g.X < lastMeter).ToArray();
        Assert.Equal(3, naturals.Length);                               // two above, one below
        double upperFirst = naturals.Where(g => g.Y < naturals.Max(n => n.Y) - 4).Min(g => g.X);
        double lowerFirst = naturals.Where(g => g.Y > naturals.Min(n => n.Y) + 4).Min(g => g.X);
        Assert.InRange(lowerFirst - upperFirst, -0.011, 0.011);
        // …and the lower staff's flat waits for the upper staff's second natural: it opens the KEY
        // column, 0.5 after the cancellation column's ink (it stood 0.5 after its own natural).
        double upperLast = naturals.Where(g => g.Y < naturals.Max(n => n.Y) - 4).Max(g => g.X);
        double lowerFlat = glyphs.Where(g => g.Glyph == '\uE021' && g.X > upperLast && g.X < lastMeter).Min(g => g.X);
        Assert.True(lowerFlat >= upperLast + 0.6666 + 0.5 - 0.011, $"flat at {lowerFlat}, naturals end {upperLast + 0.6666}");
    }
}