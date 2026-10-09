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
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A tuplet bracket on a TAB staff is bounded by the tab stems, which stand at the fret
/// digits' centres, and reaches 0.3 past them — LilyPond 2.26.0 on the twin
/// (\tabFullNotation, Lab sessions/p690/probes): the bracket's X-positions start at the
/// stem's edge, centre − 0.065, and print draws the hook shorten-pair × ss = 0.2 × 1.5
/// further out. Lily# read the notation head's offsets there, so the hook stood 0.5–0.7 off
/// the digit at either end (user report 2026-09-29, bohemian-rhapsody.lys score tab2 bar 25).
/// </summary>
public class TabTupletBracketTests
{
    // The tuplet on the top string (stems down, bracket below) …
    private const string HighString = """
        part cb {
          instrument bass
          section A { tuplet 3/2 { ees8@f ees4 } ees4 r8 bes'\2 bes bes | }
        }
        form { A }
        score { tab cb }
        """;

    // … and on the bottom string (stems up, bracket above): the same x rule on either side.
    private const string LowString = """
        octave absolute
        part cb {
          instrument bass
          section A { tuplet 3/2 { bes,,8@f bes,,4 } bes,,4 r8 bes\2 bes bes | }
        }
        form { A }
        score { tab cb }
        """;

    // … and where the string and the notated pitch disagree: E2 on the A string (fret 7)
    // sits below the tab's middle (stem UP, bracket above) while the bass staff stems its
    // written E3 down — the side is the strings', as LilyPond's TabVoice reads it.
    private const string StringAgainstPitch = """
        octave absolute
        part cb {
          instrument bass
          section A { tuplet 3/2 { e,8\3@f e,4\3 } e,4\3 r8 bes\2 bes bes | }
        }
        form { A }
        score { tab cb }
        """;

    // Two eighths on the D string, stems DOWN, bracket below: LilyPond 2.26.0 stands the
    // bracket line 1.13 below the stem tips (padding 1.1 off the columns' reach); Lily# read
    // the tips in the notation frame and ran the line THROUGH the stems, 1.4 above their
    // tips (user report 2026-09-29, bohemian-rhapsody.lys score tab, bar 42).
    private const string DString = """
        octave absolute
        part cb {
          instrument bass
          section A { bes,4@mf tuplet 3/2 { g,8\2 g,4\2~ } g,4\2 f,4 | }
        }
        form { A }
        score { tab cb }
        """;

    /// <summary>Every tab stem (0.130 wide vertical line) as (x, top y, bottom y).</summary>
    private static List<(double X, double Y1, double Y2)> Stems(string svg) =>
        Regex.Matches(svg, "<line x1=\"([-\\d.]+)\" y1=\"([-\\d.]+)\" x2=\"([-\\d.]+)\" y2=\"([-\\d.]+)\" stroke=\"#000000\" stroke-width=\"0.130\"/>")
            .Where(m => m.Groups[1].Value == m.Groups[3].Value)
            .Select(m => (double.Parse(m.Groups[1].Value), double.Parse(m.Groups[2].Value), double.Parse(m.Groups[4].Value)))
            .ToList();

    [Theory]
    [InlineData(DString, "g,8\\2", false)]
    [InlineData(LowString, "bes,,8@f", true)]
    public void TheBracket_ClearsTheTabStems_ByLilyPondsPadding(string book, string first, bool bracketUp)
    {
        string svg = Render(book);
        int tupletPos = book.IndexOf("tuplet 3/2", System.StringComparison.Ordinal);
        double firstCentre = DigitCentres(svg)[book.IndexOf(first, System.StringComparison.Ordinal)];
        var hooks = Hooks(svg, tupletPos);
        Assert.Equal(2, hooks.Count);
        // The bracket line is the hooks' y on the staff side; the tuplet's first stem is the
        // one at the first digit's centre.
        double line = bracketUp ? System.Math.Min(hooks[0].Y1, hooks[0].Y2) : System.Math.Max(hooks[0].Y1, hooks[0].Y2);
        var stem = Stems(svg).Single(s => System.Math.Abs(s.X - firstCentre) < 0.01);
        double tip = bracketUp ? System.Math.Min(stem.Y1, stem.Y2) : System.Math.Max(stem.Y1, stem.Y2);
        // padding 1.1 past the stem's tip, on the bracket's side (device y grows down).
        double clearance = bracketUp ? tip - line : line - tip;
        Assert.InRange(clearance, 1.05, 1.25);
    }

    // ---- the three clauses ported on 2026-09-29 (nested tuplets, avoid-scripts, the beam
    // reads), each against LilyPond 2.26.0 on its twin (Lab sessions/p690/probes/tt-*.ly).

    /// <summary>An inner triplet whose own beam hides its bracket: its number stands one
    /// padding off the beam (LilyPond's follow_beam arm, number centred on the invisible
    /// bracket — 1.34 off the stem rect's end, i.e. 1.10 off the beam's outer edge), and the
    /// outer bracket clears that number (the enclosed tuplets' points, :646-680) —
    /// LilyPond's gap from the inner number's centre to the outer line is 1.70.</summary>
    [Fact]
    public void ANestedTuplet_TheOuterBracketClearsTheInnerNumber_WhichClearsItsBeam()
    {
        const string book = """
            octave absolute
            part cb {
              instrument bass
              section A { tuplet 3/2 { tuplet 3/2 { bes,,8 bes,, bes,, } bes,,4 bes,, } r2 | }
            }
            form { A }
            score { tab cb }
            """;
        string svg = Render(book);
        int outer = book.IndexOf("tuplet 3/2", System.StringComparison.Ordinal);
        int inner = book.IndexOf("tuplet 3/2", outer + 1, System.StringComparison.Ordinal);
        var outerHooks = Hooks(svg, outer);
        Assert.Equal(2, outerHooks.Count);
        double outerLine = System.Math.Min(outerHooks[0].Y1, outerHooks[0].Y2);
        var number = NumberCentre(svg, inner);
        Assert.InRange(number.Y - outerLine, 1.60, 1.85);
        // The inner triplet's first stem ends on its beam; the number stands above it.
        var stem = Stems(svg).OrderBy(s => System.Math.Abs(s.X - number.X)).First();
        double tip = System.Math.Min(stem.Y1, stem.Y2);
        Assert.InRange(tip - number.Y, 1.25, 1.45);
    }

    /// <summary>A script of the tuplet's notes with no outside-staff-priority (a turn) is a
    /// point the bracket clears (:682-706): LilyPond's line stands 1.63 above the turn's
    /// origin, and Lily#'s too.</summary>
    [Fact]
    public void AScriptUnderTheBracket_IsCleared()
    {
        const string book = """
            octave absolute
            part cb {
              instrument bass
              section A { tuplet 3/2 { bes,,4@turn bes,,8@turn } r4 r2 | }
            }
            form { A }
            score { tab cb }
            """;
        string svg = Render(book);
        var hooks = Hooks(svg, book.IndexOf("tuplet 3/2", System.StringComparison.Ordinal));
        double line = System.Math.Min(hooks[0].Y1, hooks[0].Y2);
        double turn = Regex.Matches(svg, "<text class=\"music\" x=\"[-\\d.]+\" y=\"([-\\d.]+)\"[^>]*>(.)</text>")
            .Where(m => m.Groups[2].Value[0] == LilySharp.Core.Svg.EmmentalerGlyphs.OrnTurn)
            .Select(m => double.Parse(m.Groups[1].Value)).Min();
        Assert.InRange(turn - line, 1.55, 1.72);
    }

    /// <summary>A stem-down quarter on the D string and a beamed stem-up eighth on the A
    /// string tie the stems' vote; LilyPond's extremal-positions rule puts the bracket UP,
    /// and the beamed eighth's bound is ITS BEAM's direction.</summary>
    [Fact]
    public void ATieOfStems_GoesWhereTheExtremalPositionsSay()
    {
        const string book = """
            octave absolute
            part cb {
              instrument bass
              section A { tuplet 3/2 { f,4 bes,,8[ } bes,,8 bes,,] r4 r4 | }
            }
            form { A }
            score { tab cb }
            """;
        string svg = Render(book);
        var hooks = Hooks(svg, book.IndexOf("tuplet 3/2", System.StringComparison.Ordinal));
        double topLine = Regex.Matches(svg, "<line x1=\"[-\\d.]+\" y1=\"([-\\d.]+)\" x2=\"[-\\d.]+\" y2=\"\\1\" stroke=\"#000000\" stroke-width=\"0.100\"/>")
            .Select(m => double.Parse(m.Groups[1].Value)).Min();
        Assert.True(hooks[0].Y1 < topLine, "the bracket stands above the tab");
    }

    /// <summary>An A-string eighth beamed DOWN with the next one is a stem-down bound under a
    /// bracket below: the bracket starts at that stem's edge, 0.365 out (LilyPond 9.153 −
    /// 0.365 = 8.79), where reading the string's own direction (up) put it at the digit's
    /// edge, 0.75 further left. The 0.3 of that is the hook's reach past the bound, which LilyPond takes ALONG the sloped bracket (lily/bracket.cc:54-55 make_bracket; its twin draws this hook at −0.2746 = −0.3 × dx/length, session 694).</summary>
    [Fact]
    public void ABeamedBound_IsItsBeamsDirection()
    {
        const string book = """
            octave absolute
            part cb {
              instrument bass
              section A { tuplet 3/2 { a,,8 d,8 g,8[ } g,8] r4 r2 | }
            }
            form { A }
            score { tab cb }
            """;
        string svg = Render(book);
        var hooks = Hooks(svg, book.IndexOf("tuplet 3/2", System.StringComparison.Ordinal));
        double firstStem = Stems(svg).Min(s => s.X);
        // The reach past the bound along the bracket: 0.2 x the tab's 1.5, times dx/length
        // of the line the two hooks stand on.
        double dx = hooks[^1].X - hooks[0].X, dy = hooks[^1].Y1 - hooks[0].Y1;
        double reach = LilySharp.Core.Svg.Layout.TupletBracketLayout.BracketOutwardReach * 1.5
                       * dx / System.Math.Sqrt(dx * dx + dy * dy);
        Assert.Equal(firstStem - 0.065 - reach, hooks[0].X, 2);
    }

    private static (double X, double Y) NumberCentre(string svg, int tupletPos)
    {
        var m = Regex.Match(svg, "<text x=\"([-\\d.]+)\" y=\"([-\\d.]+)\"[^>]*font-style=\"italic\"[^>]*data-pos=\"" + tupletPos + "\">");
        Assert.True(m.Success, "no number for the tuplet at " + tupletPos);
        return (double.Parse(m.Groups[1].Value), double.Parse(m.Groups[2].Value));
    }

    private static string Render(string lys)
        => SvgGenerator.Generate(SyntaxTree.Parse(lys), new SvgRenderOptions { EmbedFont = false });

    /// <summary>Every fret digit's centre x, by its source offset (text-anchor middle).</summary>
    private static Dictionary<int, double> DigitCentres(string svg) =>
        // The fret face is regular since session 780 (bold only when `fonts { tab bold }` says so).
        Regex.Matches(svg, "<text x=\"([-\\d.]+)\" y=\"[-\\d.]+\" font-size=\"[\\d.]+\" (?:font-weight=\"bold\" )?text-anchor=\"middle\" data-pos=\"(\\d+)\">\\d+</text>")
            .ToDictionary(m => int.Parse(m.Groups[2].Value), m => double.Parse(m.Groups[1].Value));

    /// <summary>The bracket's vertical hooks (x1 == x2) tagged with the tuplet's source offset,
    /// as (x, top y, bottom y).</summary>
    private static List<(double X, double Y1, double Y2)> Hooks(string svg, int tupletPos) =>
        Regex.Matches(svg, "<line x1=\"([-\\d.]+)\" y1=\"([-\\d.]+)\" x2=\"([-\\d.]+)\" y2=\"([-\\d.]+)\"[^>]*data-pos=\"" + tupletPos + "\"")
            .Where(m => m.Groups[1].Value == m.Groups[3].Value)
            .Select(m => (double.Parse(m.Groups[1].Value), double.Parse(m.Groups[2].Value), double.Parse(m.Groups[4].Value)))
            .OrderBy(h => h.Item1).ToList();

    [Theory]
    [InlineData(HighString, "ees8@f", "ees4 }", false)]
    [InlineData(LowString, "bes,,8@f", "bes,,4 }", true)]
    [InlineData(StringAgainstPitch, "e,8\\3@f", "e,4\\3 }", true)]
    public void TheBracket_ReachesFromTheFirstStemToTheLast_OnTheStemsSide(
        string book, string first, string last, bool bracketUp)
    {
        string svg = Render(book);
        int tupletPos = book.IndexOf("tuplet 3/2", System.StringComparison.Ordinal);
        var digits = DigitCentres(svg);
        double firstCentre = digits[book.IndexOf(first, System.StringComparison.Ordinal)];
        double lastCentre = digits[book.IndexOf(last, System.StringComparison.Ordinal)];

        var hooks = Hooks(svg, tupletPos);
        Assert.Equal(2, hooks.Count);
        // The stem's edge (half a stem thickness off the digit's centre), then 0.2 × the
        // tab's 1.5 line spacing further out.
        const double reach = 0.065 + 0.3;
        Assert.Equal(firstCentre - reach, hooks[0].X, 2);
        Assert.Equal(lastCentre + reach, hooks[1].X, 2);
        // The hook is 0.7 × 1.5 tall, pointing at the staff.
        Assert.Equal(1.05, System.Math.Abs(hooks[0].Y2 - hooks[0].Y1), 2);
        // Above the tab when the strings stem up, below when they stem down (device y grows down).
        // The tab's own strings: the thin (0.100) horizontal lines, not the bracket's run.
        double topLine = Regex.Matches(svg, "<line x1=\"[-\\d.]+\" y1=\"([-\\d.]+)\" x2=\"[-\\d.]+\" y2=\"\\1\" stroke=\"#000000\" stroke-width=\"0.100\"/>")
            .Select(m => double.Parse(m.Groups[1].Value)).Min();
        Assert.Equal(bracketUp, hooks[0].Y1 < topLine);
    }
}
