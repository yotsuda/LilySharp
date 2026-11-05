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

using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// One dot column a staff and moment, whichever voices its dots come from: every dot on one
/// configuration and at one X (lily/dot-column.cc:194-232). MEASURED, LilyPond 2.26.0: Lab
/// sessions/p1101/probe/dots.ps1 and dots2.ps1, against a grob dump of each book's twin.
/// </summary>
[Trait("Category", "Unit")]
public class DotColumnTests
{
    private static string Book(string music)
        => "octave absolute\ntime 4/4\nkey c major\npart p { clef treble }\nsection A { p { " + music + " } }\nform { A }\nscore { staff p }\n";

    private static MultiStaffScore Score(string music)
    {
        var tree = SyntaxTree.Parse(Book(music));
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        return SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
    }

    /// <summary>Every dot of the page, as (row against the middle line, x).</summary>
    private static List<(int Row, double X)> Dots(string music)
    {
        string svg = SvgGenerator.Generate(SyntaxTree.Parse(Book(music)),
            new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false });
        var lines = Regex.Matches(svg, "<line x1=\"([-\\d.]+)\" y1=\"([-\\d.]+)\" x2=\"([-\\d.]+)\" y2=\"([-\\d.]+)\"")
            .Where(m => m.Groups[2].Value == m.Groups[4].Value
                && double.Parse(m.Groups[3].Value) - double.Parse(m.Groups[1].Value) > 5)
            .Select(m => double.Parse(m.Groups[2].Value)).Distinct().OrderBy(v => v).ToList();
        double middle = lines[2];
        return Regex.Matches(svg, "<text class=\"music\" x=\"([-\\d.]+)\" y=\"([-\\d.]+)\"[^>]*>(.)</text>")
            .Where(m => m.Groups[3].Value[0] == EmmentalerGlyphs.AugmentationDot)
            .Select(m => ((int)System.Math.Round((middle - double.Parse(m.Groups[2].Value)) * 2), double.Parse(m.Groups[1].Value)))
            .OrderBy(d => d.Item1).ToList();
    }

    private static int[] Rows(string music) => Dots(music).Select(d => d.Row).ToArray();

    [Theory]
    [InlineData("voice { f''4. c''8 c''2 } { e''4. c'8 c'2 } { d''4. c''8 c''2 } |", new[] { 1, 3, 5 })]
    [InlineData("voice { e''4. c''8 c''2 } { d'4. c'8 c'2 } { c''4. c''8 c''2 } |", new[] { -5, 1, 3 })]
    [InlineData("voice { d''4. c''8 c''2 } { d''4. c'8 c'2 } { d''4. c''8 c''2 } |", new[] { 1, 3 })]
    [InlineData("voice { e''4. c''8 c''2 } { d''4. c'8 c'2 } { c''4. c''8 c''2 } { b'4. c'8 c'2 } |", new[] { -1, 1, 3, 5 })]
    [InlineData("voice { <e'' g''>4. c''8 c''2 } { <d'' f''>4. c'8 c'2 } { <c'' e''>4. c''8 c''2 } |", new[] { -3, -1, 1, 3, 5, 7 })]
    [InlineData("voice { f''4. c''8 c''2 } { e''4. c'8 c'2 } |", new[] { 3, 5 })]
    [InlineData("voice { <d'' f''>4. c''8 c''2 } { <c'' e''>4. c'8 c'2 } |", new[] { -1, 1, 3, 5 })]
    [InlineData("voice { g''4. c''8 c''2 } { r4. c'8 c'2 } { f''4. c''8 c''2 } |", new[] { -3, 3, 5 })]
    [InlineData("voice { f''4. c''8 c''2 } { e''4. c'8 c'2 } { r4. c''8 c''2 } |", new[] { 1, 5, 13 })]
    [InlineData("voice { r4. c''8 c''2 } { e''4. c'8 c'2 } { f''4. c''8 c''2 } |", new[] { 1, 3, 15 })]
    [InlineData("voice { c''1. } { r1. } |", new[] { -5, 1 })]
    [InlineData("voice { f''2. c''4 } { r2. c'4 } |", new[] { -3, 5 })]
    [InlineData("voice { g'8. c''16 c''4 c''2 } { c'8. c'16 c'4 c'2 } |", new[] { -7, -1 })]
    [InlineData("voice { c''4. c''8 c''2 } { <e' f'>4. c'8 c'2 } |", new[] { -5, -3, 1 })]
    [InlineData("voice { <e'' g''>4. c''8 c''2 } { <a' b'>4. c'8 c'2 } |", new[] { -1, 1, 3, 5 })]
    [InlineData("voice { f''2. c''4 } { f''4 f''4. c'8 c'4 } |", new[] { 3, 5 })]
    [InlineData("voice { e''4. c''8 c''2 } { e'4. c'8 c'2 } |", new[] { -5, 3 })]
    [InlineData("voice { e''4. c''8 c''2 } { f''4. c'8 c'2 } |", new[] { 3, 5 })]
    [InlineData("voice { b'4. c''8 c''2 } { c''4. c'8 c'2 } |", new[] { -1, 1 })]
    [InlineData("voice { g'8. r16 g'4 g'2 } { c'8. c'16 c'4 c'2 } |", new[] { -7, -1 })]
    [InlineData("voice { g'8. r16 g'4 g'2 } { r8. c'16 c'4 c'2 } |", new[] { -6, -1 })]
    [InlineData("voice { d''4. c''8 c''2 } { e''4. c'8 c'2 } |", new[] { 1, 3 })]
    [InlineData("voice { c''4. c''8 c''2 } { d''4. c'8 c'2 } |", new[] { 1, 3 })]
    [InlineData("voice { <a' b'>4. c''8 c''2 } { c'4. c'8 c'2 } |", new[] { -7, -1, 1 })]
    [InlineData("voice { g'8. r16 g'4 g'2 } { <c' e'>8. c'16 c'4 c'2 } |", new[] { -7, -5, -1 })]
    [InlineData("voice { f''2. c''4 } { c'1 } { r4 f''4. c''8 c''4 } |", new[] { 5, 5 })]
    public void EveryVoicesDots_StandOnOneConfiguration(string music, int[] rows)
        => Assert.Equal(rows, Rows(music));

    [Theory]
    [InlineData("voice { f''4. c''8 c''2 } { r4. c'8 c'2 } |")]
    [InlineData("voice { c''4. c''8 c''2 } { r4. c'8 c'2 } |")]
    [InlineData("voice { f''8. c''16 c''4 c''2 } { r8. c'16 c'4 c'2 } |")]
    [InlineData("voice { <e'' g''>4. c''8 c''2 } { r4. c'8 c'2 } |")]
    [InlineData("voice { g''4. c''8 c''2 } { r4. c'8 c'2 } { f''4. c''8 c''2 } |")]
    [InlineData("voice { f''4. c''8 c''2 } { e''4. c'8 c'2 } { r4. c''8 c''2 } |")]
    [InlineData("voice { e''4. c''8 c''2 } { d''4. c'8 c'2 } { c''4. c''8 c''2 } { b'4. c'8 c'2 } |")]
    [InlineData("voice { c''1. } { r1. } |")]
    [InlineData("voice { f''2. c''4 } { r2. c'4 } |")]
    [InlineData("voice { g'8. c''16 c''4 c''2 } { c'8. c'16 c'4 c'2 } |")]
    [InlineData("voice { c''4. c''8 c''2 } { <e' f'>4. c'8 c'2 } |")]
    [InlineData("voice { <e'' g''>4. c''8 c''2 } { <a' b'>4. c'8 c'2 } |")]
    [InlineData("voice { g'8. r16 g'4 g'2 } { c'8. c'16 c'4 c'2 } |")]
    [InlineData("voice { g'8. r16 g'4 g'2 } { r8. c'16 c'4 c'2 } |")]
    [InlineData("voice { <a' b'>4. c''8 c''2 } { c'4. c'8 c'2 } |")]
    [InlineData("voice { g'8. r16 g'4 g'2 } { <c' e'>8. c'16 c'4 c'2 } |")]
    [InlineData("voice { <f' g'>4. c''8 c''2 } { c'4. c'8 c'2 } |")]
    [InlineData("voice { <c'' d''>4. c''8 c''2 } { c'4. c'8 c'2 } |")]
    public void EveryVoicesDots_StandAtOneX(string music)
    {
        var xs = Dots(music).Select(d => d.X).ToArray();
        Assert.True(xs.Length >= 2);
        Assert.InRange(xs.Max() - xs.Min(), 0, 0.011);
    }

    [Fact]
    public void AColumnOverAHeldNote_IsAnother()
    {
        var dots = Dots("voice { c''2. c''4 } { c'2 r4. c'8 } |");
        Assert.Equal(2, dots.Count);
        Assert.True(System.Math.Abs(dots[0].X - dots[1].X) > 1);
    }

    // A flag pushes the upper voice's dot right of the head (2.52 from it, MEASURED: dots2.ps1),
    // and the lower voice's goes with it, and that is what its own reservation reads.
    [Theory]
    [InlineData("voice { g'8. r16 g'4 g'2 } { e'8. e'16 e'4 e'2 } |")]
    public void AShiftedDot_IsInTheColumnsReservation(string music)
    {
        var score = Score(music);
        var dotted = score.StaffGroups.Single().Staves.Single().Voices[1].Measures[0].Items.OfType<NoteItem>().First();
        Assert.True(dotted.DotColumnShift > 0);
        var (withShift, _) = LilySharp.Core.Svg.Layout.DotColumn.Reserved(dotted, 8, 1.3);
        var (without, _) = LilySharp.Core.Svg.Layout.DotColumn.Reserved(dotted with { DotColumnShift = 0 }, 8, 1.3);
        Assert.Equal(dotted.DotColumnShift, withShift - without, 6);
    }

    [Fact]
    public void AShiftedRestsDot_IsInItsReservation()
    {
        var rest = Score("voice { g'8. r16 g'4 g'2 } { r8. c'16 c'4 c'2 } |").StaffGroups.Single().Staves.Single()
            .Voices[1].Measures[0].Items.OfType<RestItem>().First();
        Assert.True(rest.DotColumnShift > 0);
        var (withShift, _) = LilySharp.Core.Svg.Layout.DotColumn.Reserved(rest, 8, 1.0);
        var (without, _) = LilySharp.Core.Svg.Layout.DotColumn.Reserved(rest with { DotColumnShift = 0 }, 8, 1.0);
        Assert.Equal(rest.DotColumnShift, withShift - without, 6);
    }

    [Fact]
    public void AGraceDot_IsNoMemberOfTheColumn()
    {
        // MEASURED: the main notes' 3 and 5, as with no grace; its own dot stands apart.
        var rows = Rows("voice { grace { g''16. } f''4. c''8 c''2 } { e''4. c'8 c'2 } |");
        Assert.Equal(new[] { 3, 5 }, rows.Where(p => p is 3 or 5).Distinct().OrderBy(p => p).ToArray());
        Assert.DoesNotContain(1, rows);
    }

    [Fact]
    public void TheRestsShift_IsStamped()
    {
        var rest = Score("voice { f''4. c''8 c''2 } { r4. c'8 c'2 } |").StaffGroups.Single().Staves.Single()
            .Voices[1].Measures[0].Items.OfType<RestItem>().First();
        Assert.True(rest.DotColumnShift > 0.1, $"{rest.DotColumnShift}");
    }
}
