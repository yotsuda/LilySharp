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
using System.Text.RegularExpressions;
using LilySharp.Core;
using LilySharp.Core.LilyPond;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A part that rests through a section whose last bar is short — the first half of a bar the
/// next play's pickup completes — rests through a bar that long (<see cref="SectionShortEnds"/>):
/// on the page, so the split bar is one bar in the conductor's score as it is on a part's
/// sheet, and in the LilyPond twin, whose staves share one clock. And a bar a part writes empty
/// under <c>time none</c> is as long as the part that fills it makes it.
/// </summary>
/// <remarks>
/// Grammar evaluation 4 (2026-11-13), a choral score whose soloist sings one section and a
/// chamber score's senza misura. Measured on LilyPond 2.26.0 (Lab sessions/p1227/x/twin): the
/// choral twin went from 24 warnings ("bar check failed at: 3/4", "mid-measure time signature
/// without \partial" at every later meter change) to none of them, the chamber twin from 15
/// to one.
/// </remarks>
public class SectionShortEndTests
{
    private const string Book = """
        octave absolute
        time 4/4
        part solo { clef treble }
        part m { clef treble }
        section Intro  { partial 4  m { r4 | R*2 | r2. | } }
        section Verse  { partial 4  m { d'4 | d'1 | d'2. | }  lyrics w { la | la | la | } }
        section Chorus { partial 4  m { e'4 | e'1 | e'1 | } }
        section Last   { time 6/8  solo { f'2. | f'2. |. }  m { f'2. | f'2. |. } }
        form { ~Intro |: Verse "A" :| Chorus "B" Last "C" }
        score { staff solo  staff m  lyrics w }
        score sheet { staff m  lyrics w }
        """;

    private static MultiStaffScore Collect(string source, string? score = null)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join(" | ", tree.Diagnostics.Select(d => d.Message)));
        var spec = score == null
            ? RenderSpecParser.FindFirst(tree)
            : RenderSpecParser.FindAll(tree).Single(s => s.Name == score);
        return SvgGenerator.CollectScore(tree, spec);
    }

    private static string Twin(string source) => new LilyPondExporter().Export(SyntaxTree.Parse(source));

    private static string Variable(string ly, string name)
    {
        var m = Regex.Match(ly, "^" + Regex.Escape(name) + @" = .*?^\}", RegexOptions.Multiline | RegexOptions.Singleline);
        Assert.True(m.Success, "no variable " + name);
        return m.Value;
    }

    [Fact]
    public void ThePage_PadsTheShortBar_AndTheScoreCountsTheBarsItsSheetCounts()
    {
        var full = Collect(Book);
        var solo = full.AllVoices.First(v => v.Name == "solo").Measures;
        var sheet = Collect(Book, "sheet").AllVoices.First(v => v.Name == "m").Measures;
        Assert.Equal(sheet.Length, solo.Length);
        // The silent part's bars are as long as the bars it rests through: the pickup, two
        // full bars, the three beats the Verse's pickup completes — a full bar there until
        // 2026-11-14.
        Assert.Equal(
            new[] { new Fraction(1, 4), new Fraction(1), new Fraction(1), new Fraction(3, 4) },
            solo.Take(4).Select(m => m.TotalDuration));
        // …so the score's first staff says what the sheet says: which measures continue a bar.
        var continued = Enumerable.Range(0, sheet.Length).Where(i => sheet[i].ContinuesBar).ToList();
        Assert.Equal(new[] { 4, 7 }, continued);
        foreach (var voice in full.AllVoices)
            Assert.Equal(continued, Enumerable.Range(0, voice.Measures.Length).Where(i => voice.Measures[i].ContinuesBar));
    }

    [Fact]
    public void TheTwin_WritesTheHalfBar_WithNoBarCheckAfterIt()
    {
        string ly = Twin(Book);
        // The silent staff: a spacer of the half bar's length, then the pickup.
        string solo = Variable(ly, "solo");
        Assert.Equal(2, Regex.Matches(solo, @"s1\*3/4\r?\n").Count);
        Assert.DoesNotContain("s1*3/4 |", solo);
        // The part that writes the bar: its `|` after the first half is no bar check.
        string m = Variable(ly, "m");
        Assert.Matches(@"r2\.\r?\n\s*\\repeat volta 2", m);
        Assert.Matches(@"d'2\.\r?\n\s*\}", m);
        Assert.DoesNotMatch(@"(r|d')2\. \|", m);
        // A full last bar keeps its check.
        Assert.Contains("e'1 |\n", m.Replace("\r\n", "\n"));
        // The lyric line under it, read off the page's bars: the same.
        string words = Variable(ly, "mLyricsOne");
        Assert.Matches(@"\\skip 2\.\r?\n\s*la4 \|", words);
        Assert.Matches(@"la2\.\r?\n\s*\\skip 4 \|", words);
    }

    [Fact]
    public void AFullLastBar_IsPaddedAsBefore()
    {
        // No section ends short: nothing is cut, and every bar keeps its check.
        string ly = Twin("""
            octave absolute
            time 4/4
            part solo { clef treble }
            part m { clef treble }
            section A { partial 4  m { c'4 | c'1 | c'1 | } }
            section B { solo { d'1 | } m { d'1 | } }
            form { A B }
            score { staff solo  staff m }
            """);
        string solo = Variable(ly, "solo");
        Assert.DoesNotContain("s1*", solo);
        Assert.Equal(2, Regex.Matches(solo, @"s1 \|").Count);
    }

    [Fact]
    public void AnEmptyBarUnderTimeNone_IsAsLongAsThePartThatFillsIt()
    {
        string ly = Twin("""
            octave absolute
            time 4/4
            part up { clef treble }
            part low { clef treble }
            section A { up { c'1 | } low { c'1 | } }
            section B { time none  up { c'4 d' e' f' g' | r1 r1 | } low { | c'1 d'1 | } }
            section C { time 3/4  up { c'2. |. } low { c'2. |. } }
            form { A B C }
            score { staff up  staff low }
            """);
        // The lower part's first bar of B is written `|` alone: five quarters, as the upper
        // part's is — `s1` until 2026-11-14, a quarter short of the staff above under a clock
        // that stands still.
        Assert.Contains(@"\cadenzaOn s1*5/4 \bar ""|""", Variable(ly, "low"));
    }
}
