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

/// <summary>A span bar running through a lyric line keeps the line's syllables off it, as the
/// SpanBarStub does in LilyPond (lily/span-bar-stub-engraver.cc).</summary>
public class LyricSpanBarStubTests
{
    private const string Music = """
        time 4/4
        part up { clef treble }
        part down { clef treble }
        section A { up { c''4 c c c | c c c c | c c c c | } down { c''4 c c c | c c c c | c c c c | } }
        form { A }

        """;

    private const string FirstWide = "lyrics w { a a a a | WWWWWW a a a | a a a a | }\n";
    private const string LastWide = "lyrics w { a a a WWWWWW | a a a a | a a a a | }\n";
    private const string Narrow = "lyrics w { a a a a | a a a a | a a a a | }\n";

    private static string Group(string kind) => $"score {{ {kind} {{ staff up  lyrics w  staff down }} }}\n";

    private static readonly Regex TextAt = new(@"<text[^>]*\bx=""(?<x>[-\d.]+)""[^>]*>(?<t>WWWWWW|a)</text>", RegexOptions.Compiled);

    /// <summary>The x of the wide syllable and of the first syllable after it (the column it pushes).</summary>
    private static double[] Xs(string lyrics, string kind)
    {
        string svg = SvgGenerator.Generate(SyntaxTree.Parse(Music + lyrics + Group(kind)));
        return [.. TextAt.Matches(svg).Select(m => double.Parse(m.Groups["x"].Value, CultureInfo.InvariantCulture))];
    }

    [Theory]
    [InlineData("staffGroup")]
    [InlineData("grandStaff")]
    public void AWideFirstSyllable_StandsFurtherRight_ThanUnderAChoirStaff(string kind)
    {
        var withBar = Xs(FirstWide, kind);
        var choir = Xs(FirstWide, "choirStaff");
        Assert.True(withBar[4] > choir[4] + 1, $"{kind}: {withBar[4]} against choir {choir[4]}");
    }

    [Theory]
    [InlineData("staffGroup")]
    [InlineData("grandStaff")]
    public void AWideLastSyllable_PushesTheNextBarOn(string kind)
    {
        var withBar = Xs(LastWide, kind);
        var choir = Xs(LastWide, "choirStaff");
        Assert.True(withBar[4] > choir[4] + 0.5, $"{kind}: {withBar[4]} against choir {choir[4]}");
    }

    private static readonly Regex BarAt = new(@"<(?:line|rect)[^>]*?(?:x1|x)=""(?<x>[\d.]+)""[^>]*?(?:stroke-width|width)=""0\.19\d*""", RegexOptions.Compiled);

    private static (double Word, double[] Bars) WideAndBars(string lyrics)
    {
        string svg = SvgGenerator.Generate(SyntaxTree.Parse(Music + lyrics + Group("staffGroup")));
        double word = double.Parse(TextAt.Matches(svg).First(m => m.Groups["t"].Value == "WWWWWW").Groups["x"].Value, CultureInfo.InvariantCulture);
        double[] bars = [.. BarAt.Matches(svg).Select(m => double.Parse(m.Groups["x"].Value, CultureInfo.InvariantCulture)).Distinct().Order()];
        return (word, bars);
    }

    // The same word stands at half its width from either side, so the two gaps' difference is
    // LilyPond's: 0.1 (stub) + 0.3 (Staff_spacing) on its left, 0.1 (stub) + 0.1 (rod) on its right.
    [Fact]
    public void TheGapsToTheBar_AreLilyPonds()
    {
        const double barWidth = 0.19;
        var (first, firstBars) = WideAndBars(FirstWide);
        var (last, lastBars) = WideAndBars(LastWide);
        double fromTheLeft = first - (firstBars[0] + barWidth);
        double fromTheRight = lastBars[0] - last;
        Assert.Equal(0.40 - 0.20, fromTheLeft - fromTheRight, 1);
    }

    [Fact]
    public void NarrowSyllables_DoNotMoveAColumn()
        => Assert.Equal(Xs(Narrow, "choirStaff"), Xs(Narrow, "staffGroup"));

    [Fact]
    public void TheLastStaffOfAGroup_IsNotCrossed()
    {
        // No bar runs below the last staff: its line overhangs the bar as a lone staff's does.
        string Below(string kind) => SvgGenerator.Generate(SyntaxTree.Parse(Music + FirstWide + $"score {{ {kind} {{ staff up  staff down  lyrics w }} }}\n"));
        static string Xonly(string svg) => string.Join(",", TextAt.Matches(svg).Select(m => m.Groups["x"].Value));
        Assert.Equal(Xonly(Below("choirStaff")), Xonly(Below("staffGroup")));
    }
}
