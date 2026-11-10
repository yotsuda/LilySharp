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
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>A dynamic in the gap a span bar runs through keeps off it: ly/engraver-init.ly:475-477
/// switches DynamicText's extra-spacing-width off in a StaffGroup and what inherits from it.</summary>
public class DynamicSpanBarTests
{
    private const string Plain = "c''4 c c c | c c c c | c c c c |";
    private const string SecondBarOpens = "c''4 c c c | c@p.subito c c c | c c c c |";
    private const string FirstBarCloses = "c''4 c c c8 c16 c32 c32@fff | c4 c c c | c c c c |";
    private const string FirstBarClosesPlain = "c''4 c c c8 c16 c32 c32 | c4 c c c | c c c c |";

    private static string Book(string up, string down, string group) => $$"""
        time 4/4
        part up { clef treble }
        part down { clef treble }
        section A { up { {{up}} } down { {{down}} } }
        form { A }
        score { {{group}} { staff up  staff down } }

        """;

    private static readonly Regex BarAt = new(@"<(?:line|rect)[^>]*?(?:x1|x)=""(?<x>[\d.]+)""[^>]*?(?:stroke-width|width)=""0\.19\d*""", RegexOptions.Compiled);

    private static double[] Bars(string up, string down, string group)
        => [.. BarAt.Matches(SvgGenerator.Generate(SyntaxTree.Parse(Book(up, down, group))))
            .Select(m => double.Parse(m.Groups["x"].Value, CultureInfo.InvariantCulture)).Distinct().Order()];

    private static double Width(double[] bars, int bar) => bars[bar + 1] - bars[bar];

    [Theory]
    [InlineData("staffGroup")]
    [InlineData("grandStaff")]
    public void AWideDynamicOpeningABar_PushesItsColumnOffTheBar(string group)
        => Assert.True(Width(Bars(SecondBarOpens, Plain, group), 0) > Width(Bars(Plain, Plain, group), 0) + 0.5);

    [Theory]
    [InlineData("staffGroup")]
    [InlineData("grandStaff")]
    public void AWideDynamicClosingABar_PushesTheBarOn(string group)
        => Assert.True(Bars(FirstBarCloses, Plain, group)[0] > Bars(FirstBarClosesPlain, Plain, group)[0] + 0.5);

    // LilyPond 2.26.0's own bar widths for these books (the `.ly` twins, Lab sessions/p1222/haru/probe/dyn-lp.ps1).
    [Theory]
    [InlineData("c@fff", 0.57)]
    [InlineData("c@sfz", 0.41)]
    [InlineData("c@mf", 0.26)]
    public void TheBarWidens_ByWhatLilyPondWidensIt(string note, double lilyPond)
    {
        string book = $"c''4 c c c | {note} c c c | c c c c |";
        Assert.Equal(lilyPond, Width(Bars(book, Plain, "staffGroup"), 0) - Width(Bars(Plain, Plain, "staffGroup"), 0), 1);
    }

    [Fact]
    public void UnderAChoirStaff_NoBarRunsThere_SoNothingMoves()
    {
        Assert.Equal(Bars(Plain, Plain, "choirStaff"), Bars(SecondBarOpens, Plain, "choirStaff"));
        Assert.Equal(Bars(FirstBarClosesPlain, Plain, "choirStaff"), Bars(FirstBarCloses, Plain, "choirStaff"));
    }

    [Fact]
    public void UnderTheLastStaff_NoBarRunsThere_SoNothingMoves()
        => Assert.Equal(Bars(Plain, Plain, "staffGroup"), Bars(Plain, SecondBarOpens, "staffGroup"));

    [Fact]
    public void AboveTheFirstStaff_NoBarRunsThere_SoNothingMoves()
    {
        string above = "c''4 c c c | c@fff.up c c c | c c c c |";
        Assert.Equal(Bars(Plain, Plain, "staffGroup"), Bars(above, Plain, "staffGroup"));
    }

    [Fact]
    public void AboveALowerStaff_ABarRunsThere()
    {
        string above = "c''4 c c c | c@fff.up c c c | c c c c |";
        Assert.True(Width(Bars(Plain, above, "staffGroup"), 0) > Width(Bars(Plain, Plain, "staffGroup"), 0) + 0.2);
    }

    [Fact]
    public void AFreeText_IsATextScript_AndTakesNoPart()
    {
        string text = "c''4 c c c | c@text(\"dolcissimo assai\") c c c | c c c c |";
        Assert.Equal(Bars(Plain, Plain, "staffGroup"), Bars(text, Plain, "staffGroup"));
    }

    [Fact]
    public void ANarrowDynamic_DoesNotBind()
    {
        string p = "c''4 c c c | c@p c c c | c c c c |";
        Assert.Equal(Bars(Plain, Plain, "staffGroup"), Bars(p, Plain, "staffGroup"));
    }
}
