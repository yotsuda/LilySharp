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

using System.Text.RegularExpressions;
using LilySharp.Core.LilyPond;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// LilyPond keeps grace time per voice, and engraves a bar line, a meter or a key at each voice's
/// FIRST moment there — the grace's on a staff that leads with one. A grace on one staff alone
/// therefore drew the other staff's meter twice and a stray bar line (MEASURED 2.26.0, Lab
/// sessions/p841/gl). The twin now gives every other voice a grace skip of the same length
/// (LilyPondExporter.CollectGraceSync — LilyPond's documented remedy), at every grace moment
/// (the owner's decision, session 842).
/// </summary>
public class LilyPondExporterGraceSyncTests
{
    private static string Twin(string source) => new LilyPondExporter().Export(SyntaxTree.Parse(source));

    private static string Body(string ly, string variable)
    {
        var m = Regex.Match(ly, "^" + variable + @" = \\\w+ [^{]*\{(.*?)^\}", RegexOptions.Multiline | RegexOptions.Singleline);
        Assert.True(m.Success, $"no variable '{variable}' in:\n{ly}");
        return m.Groups[1].Value;
    }

    private const string Header = """
        octave absolute
        time 4/4
        key c major
        part top { clef treble }
        part bot { clef bass }

        """;

    private const string TwoStaves = "score main { staff top staff bot }\n";

    [Fact]
    public void AGraceOnOneStaff_GivesTheOtherStaffASkipOfItsLength()
    {
        string ly = Twin(Header + """
            section A {
              top { c'4 d' e' f' | g'1 | }
              bot { grace { e16 f } g4 a b c' | grace { d'16 e' } f'2 g'2 | }
            }
            form main { A }

            """ + TwoStaves);
        string top = Body(ly, "top");
        // Two sixteenths of grace on the lower staff at each bar's start: an eighth's skip above,
        // and the note after it writes its own duration (the skip's would carry in LilyPond).
        Assert.Contains(@"\grace { s8 } c'4", top);
        Assert.Contains(@"\grace { s8 } g'1", top);
        Assert.DoesNotContain(@"\grace { s", Body(ly, "bot"));
    }

    [Fact]
    public void AShorterRunIsPaddedToTheLongest()
    {
        string ly = Twin(Header + """
            section A {
              top { grace { d'16 } c'1 | }
              bot { grace { e16 f g } g1 | }
            }
            form main { A }

            """ + TwoStaves);
        // 3/16 below, 1/16 above: the upper run is preceded by the difference.
        Assert.Contains(@"\grace { s8 } \grace { d'16 }", Body(ly, "top"));
        Assert.DoesNotContain(@"\grace { s", Body(ly, "bot"));
    }

    [Fact]
    public void ASectionPlayedTwice_OwesTheSkipOnEveryPlay()
    {
        string ly = Twin(Header + """
            section A {
              top { c'2 d'2 | }
              bot { grace { e16 } g1 | }
            }
            form main { A A }

            """ + TwoStaves);
        Assert.Equal(2, Regex.Matches(Body(ly, "top"), @"\\grace \{ s16 \} c'2").Count);
    }

    /// <summary>One written bar played twice, owing a skip on its SECOND play only: the skips
    /// are counted per play, so the first play writes none (an export keyed by position alone
    /// would write it on both, and LilyPond would then sync to a grace no staff plays).</summary>
    [Fact]
    public void APhraseUsedTwice_OwesTheSkipOnlyWhereTheOtherStaffHasAGrace()
    {
        string ly = Twin(Header + """
            phrase riff { c'2 d'2 | }
            section A {
              top { riff riff }
              bot { c1 | grace { e16 } g1 | }
            }
            form main { A }

            """ + TwoStaves);
        var plays = Regex.Matches(Body(ly, "top"), @"(\\grace \{ s16 \} )?c'2");
        Assert.Equal(2, plays.Count);
        Assert.False(plays[0].Groups[1].Success, "the first play owes no skip:\n" + ly);
        Assert.True(plays[1].Groups[1].Success, "the second play owes the skip:\n" + ly);
    }

    [Fact]
    public void TheOtherVoiceOfTheSameStaff_OwesItToo()
    {
        string ly = Twin(Header + """
            section A {
              top { voice { grace { a'16 } g'2 e'2 } { c'1 } | }
              bot { c1 | }
            }
            form main { A }

            """ + TwoStaves);
        Assert.Contains(@"\\ { \grace { s16 } c'1 }", Body(ly, "top"));
        Assert.Contains(@"\grace { s16 } c1", Body(ly, "bot"));
    }

    // A bar of silence is written as one spacer of its own (`s1 |`), with no event to carry the
    // skip: it goes in front of the spacer (session 845, Lab sessions/p845/gs — without it the
    // silent staff drew its section mark twice, and its `\time` twice under a header).
    [Theory]
    [InlineData("", "s1")]
    [InlineData("time 3/4", "s2.")]
    public void ASectionThePartDoesNotWrite_OwesTheSkipInItsSilentBars(string header, string bar)
    {
        string body = bar == "s1" ? "1" : "2.";
        string ly = Twin(Header + $$"""
            section A {
              top { c'1 | }
              bot { c1 | }
            }
            section B {
              {{header}}
              bot { grace { e16 f } g{{body}} | grace { e16 } g{{body}} | }
            }
            form main { A B }

            """ + TwoStaves);
        string top = Body(ly, "top");
        Assert.Contains(@"\grace { s8 } " + bar + " |", top);
        Assert.Contains(@"\grace { s16 } " + bar + " |", top);
        Assert.DoesNotContain(@"\grace { s", Body(ly, "bot"));
    }

    /// <summary>The silent bars are counted per play like the events: a section played twice
    /// owes the skip in its second bar on both plays, and never in its first.</summary>
    [Fact]
    public void SilentBars_AreCountedPerPlay()
    {
        string ly = Twin(Header + """
            section B {
              bot { g1 | grace { e16 } g1 | }
            }
            form main { B B }

            """ + TwoStaves);
        Assert.Equal(2, Regex.Matches(Body(ly, "top"), @"\bs1 \|\s*\\grace \{ s16 \} s1 \|").Count);
        Assert.Equal(2, Regex.Matches(Body(ly, "top"), @"\\grace \{ s16 \}").Count);
    }

    [Theory]
    // The part writes the section, but a bar short: the padding bar.
    [InlineData("top { c'1 | }", "bot { c1 | grace { e16 } g1 | }")]
    // The author's own empty bar, `| |`.
    [InlineData("top { c'1 | | }", "bot { c1 | grace { e16 } g1 | }")]
    public void APaddingBarOrAnEmptyBar_OwesTheSkipToo(string top, string bot)
    {
        string ly = Twin(Header + $$"""
            section A {
              {{top}}
              {{bot}}
            }
            form main { A }

            """ + TwoStaves);
        Assert.Contains(@"\grace { s16 } s1 |", Body(ly, "top"));
    }

    [Fact]
    public void WithoutAGrace_NothingIsWritten()
    {
        string ly = Twin(Header + """
            section A {
              top { c'1 | }
              bot { c1 | }
            }
            form main { A }

            """ + TwoStaves);
        Assert.DoesNotContain(@"\grace", ly);
    }
}
