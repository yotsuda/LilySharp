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
/// An accidental shortens its note's ledger lines on the left — only those within the
/// glyph's own <c>ledger-shortening-range</c> of the head, to midway between the DRAWN
/// accidental's right edge and the head — and a ledger two heads of a chord share is the
/// union of what each asks for. Until session 564 the range was ±3 positions for every
/// glyph, the edge a nominal gap from the head, and a chord asked only for its extreme head.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/ledger-line-spanner.cc:245-270 Ledger_line_spanner::print (the
/// accidental's extent and range), :359-369 (the shortening), :386-408 (the union).
/// MEASURED on 2.26.0 (Lab sessions/p564/ledger-acc-lp.svg): C♯6 — its own line 1.805, the
/// one below 1.956; D♭6 — both 1.956; A♯3 — both 1.805; a ♮ — 1.814. The page after this
/// change reads the same to the printed 2 decimals (ledger-acc-ls-after.svg). Poison: the
/// old ±3 range reddens the first two facts; the nominal edge the third; the extreme-head
/// chord the fourth.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class LedgerAccidentalShorteningTests
{
    private static string Book(string music)
        => "part m { clef treble }\nsection A { m { " + music + " } }\n"
           + "form main { ~A }\nscore main { staff m }\n";

    private static string Svg(string music)
        => SvgGenerator.Generate(SyntaxTree.Parse(Book(music)), new SvgRenderOptions { EmbedFont = false });

    /// <summary>Every ledger line as (x1, y, width), page Y-down, left to right then top down.</summary>
    private static (double X, double Y, double W)[] Ledgers(string svg)
        => Regex.Matches(svg, @"<line x1=""([\d.]+)"" y1=""([\d.]+)"" x2=""([\d.]+)"" y2=""\2"" stroke=""#000000"" stroke-width=""0\.200""")
            .Cast<Match>()
            .Select(m => (double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                          double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                          double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture)
                          - double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)))
            .OrderBy(l => l.Item1).ThenBy(l => l.Item2)
            .ToArray();

    private const double Full = 1.3042 * 1.5;   // the head's width widened by 0.25 each side

    [Fact]
    public void ASharpShortensItsOwnLine_NotTheOneBelowIt()
    {
        // C♯6 (position 8): ledgers at 8 (the head's own, in the sharp's −0.8 … 1 range)
        // and 6 (two positions below, outside it).
        var l = Ledgers(Svg("cis''4 r r r |"));
        Assert.Equal(2, l.Length);
        var own = l.OrderBy(x => x.Y).First();      // higher on the page = position 8
        var below = l.OrderBy(x => x.Y).Last();
        Assert.True(own.W < Full - 0.1, $"the head's own line must be shortened: {own.W}");
        Assert.Equal(Full, below.W, 2);
    }

    [Fact]
    public void AFlatShortensNothingBelowTheHead()
    {
        // D♭6 (position 9): ledgers at 8 and 6, both below the head; the flat's range is
        // 0 … 0.8 above it.
        var l = Ledgers(Svg("des''4 r r r |"));
        Assert.Equal(2, l.Length);
        Assert.All(l, x => Assert.Equal(Full, x.W, 2));
    }

    [Fact]
    public void TheEdgeIsTheGlyphs_ANaturalAndASharpDiffer()
    {
        // A♯3 then A♮3 (positions −8; relative to the default c', `ais` is the A below):
        // both shorten their two lines, to different lengths, because a natural's right
        // edge is not a sharp's.
        var l = Ledgers(Svg("ais4 a r r |"));
        Assert.Equal(4, l.Length);
        double sharp = l[0].W, natural = l[2].W;
        Assert.True(sharp < Full - 0.1 && natural < Full - 0.1, "both shortened");
        Assert.NotEqual(sharp, natural, 2);
    }

    [Fact]
    public void AChordsSharedLine_IsTheUnionOfItsHeads()
    {
        // <c'' dis''>: D♯6 (position 9) shortens its line at 8 — but C6 (position 8) shares
        // that line with no accidental, so the line keeps C's full extent (and, the two
        // being a second, spans both shifted heads).
        var l = Ledgers(Svg("<c'' dis''>4 r r r |"));
        var atEight = l.OrderBy(x => x.Y).First();
        Assert.True(atEight.W >= Full - 0.01, $"the shared line must not be shortened: {atEight.W}");
    }
}
