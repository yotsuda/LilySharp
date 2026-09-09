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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A bar the SECTION BOUNDARY splits — a section ending on a short bar, followed in the
/// form by a section opening with the rest of it — is one bar of the music, so neither
/// half is a short bar: no LYS2001 on the last, no LYS2006 on the first. Reported by the
/// owner on Disco Inferno (2026-09-09): section A ends on the half bar and every volta
/// ending opens with the other half, so the repeat sign stands mid-bar and four
/// "first measure is shorter than the meter" warnings were wrong. The exemption is asked
/// of the form's play order per part (<see cref="SectionBoundaryBars"/>) and holds only when
/// EVERY neighbour completes the bar exactly — one odd neighbour and both warnings stand.
/// </summary>
[Trait("Category", "Unit")]
public class SectionBoundarySplitBarTests
{
    private static IReadOnlyList<Diagnostic> Diagnose(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join(" | ", tree.Diagnostics.Select(d => d.Message)));
        var validator = new MeasureValidator();
        validator.Validate(tree);
        return validator.Diagnostics;
    }

    private static string[] BarCodes(IReadOnlyList<Diagnostic> diags)
        => diags.Where(d => d.Code is "LYS2001" or "LYS2006").Select(d => d.Code).OrderBy(c => c).ToArray();

    private const string Head = """
        time 4/4
        key c major
        part m { clef bass }
        """;

    [Fact]
    public void AVoltaWhoseEndingsOpenWithTheRestOfTheBar_IsSilent()
    {
        // A ends on the half bar; both endings open with the other half; the repeat sign
        // stands mid-bar. Disco Inferno's shape.
        string book = Head + """
            section A { m { c4 d e f | g4 a | } }
            section E1 { m { b4 c' | d'4 e' f' g' | } }
            section E2 { m { c'4 d' | e'1 | } }
            form main { |: A [1. E1] :| [2. E2] }
            score main { staff m }
            """;
        Assert.Empty(BarCodes(Diagnose(book)));
    }

    [Fact]
    public void OneEndingThatDoesNotCompleteTheBar_KeepsBothWarnings()
    {
        // E2 opens with three quarters: neither half completes anything, so A's last bar
        // is a short bar again (LYS2001) and E2's first bar a bare pickup (LYS2006); E1
        // still completes A, but A's exemption asks EVERY successor.
        string book = Head + """
            section A { m { c4 d e f | g4 a | } }
            section E1 { m { b4 c' | d'4 e' f' g' | } }
            section E2 { m { c'4 d' e' | f'1 | } }
            form main { |: A [1. E1] :| [2. E2] }
            score main { staff m }
            """;
        var codes = BarCodes(Diagnose(book));
        Assert.Contains("LYS2001", codes);
        Assert.Contains("LYS2006", codes);
        // …and only E2's first bar is the pickup: E1's is still exempt.
        Assert.Single(codes, c => c == "LYS2006");
    }

    [Fact]
    public void APlainSequence_SplitsABarTheSameWay_AndTheSectionAloneStillWarns()
    {
        const string sections = """
            section A { m { c4 d e f | g4 a | } }
            section B { m { b4 c' | d'1 | } }
            """;
        Assert.Empty(BarCodes(Diagnose(Head + sections + "form main { A B }\nscore main { staff m }\n")));

        // Positive control: played on its own, B's first bar is a bare pickup and A's last
        // bar is short.
        var alone = BarCodes(Diagnose(Head + sections + "form main { B A }\nscore main { staff m }\n"));
        Assert.Equal(new[] { "LYS2001", "LYS2006" }, alone);
    }

    [Fact]
    public void ABreakAfterTheClosingBarLine_DoesNotHideTheLastBar()
    {
        // `| break` leaves a chunk holding the break alone after A's half bar; the half bar
        // is still the section's LAST sounding bar, and the endings complete it. Disco
        // Inferno's exact shape, with the structural `~` on the endings' declarations.
        string book = """
            time 4/4
            part m {
              clef bass
              section A { c4 d e f | g4 a | break }
              section ~E1 { b4 c' | d'4 e' f' g' | break }
              section ~E2 { c'4 d' | e'1 | }
            }
            form main { |: A [1. E1] :| [2. E2] }
            score main { staff m }
            """;
        Assert.Empty(BarCodes(Diagnose(book)));
    }

    /// <summary>
    /// The page counts the two halves as ONE bar (owner's request 2026-09-09, matching
    /// LilyPond's currentBarNumber, which a mid-bar repeat sign does not advance): the
    /// ending's first measure ContinuesBar, the numbers run 1 2 2 3 …, the system that
    /// opens with the second half carries no number, and the layout report counts bars.
    /// The SECOND ending also continues the body's half bar (ContinuedFromMeasure names
    /// it) — so it is no short bar and its system carries no number either — but it takes
    /// a new number, as LilyPond's count continues through alternatives.
    /// </summary>
    [Fact]
    public void ThePage_CountsTheTwoHalvesAsOneBar()
    {
        string book = """
            time 4/4
            part m {
              clef bass
              section A { c4 d e f | g4 a | break }
              section ~E1 { b4 c' | d'4 e' f' g' | break }
              section ~E2 { c'4 d' | e'1 | }
            }
            form main { |: A [1. E1] :| [2. E2] }
            score main { staff m }
            """;
        var tree = SyntaxTree.Parse(book);
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        var measures = score.PrimaryContentStaff.PrimaryVoice.Measures;
        // The page engraves the body once: A(2) E1(2) E2(2) = 6 model measures. BOTH
        // endings' first measures continue A's last — E2's across E1, the way the play
        // puts the body before every ending (LilyPond's alternativeRestores).
        Assert.Equal(6, measures.Length);
        Assert.True(measures[2].ContinuesBar);
        Assert.Equal(-1, measures[2].ContinuedFromMeasure);
        Assert.True(measures[4].ContinuesBar);
        Assert.Equal(1, measures[4].ContinuedFromMeasure);
        Assert.False(measures[1].BreaksMidBar);                 // the author's bar line stays
        Assert.Equal(BarlineType.Single, measures[1].EndBarline);
        // 1 2 | 2 3 | 4 5 — E1's half completes bar 2 (no new number); E2's half follows
        // E1's closing bar line, and LilyPond's count continues through alternatives
        // (alternativeRestores restores measurePosition, not currentBarNumber), so it is
        // bar 4 even though it opens mid-bar.
        Assert.Equal(new[] { 1, 2, 2, 3, 4, 5 }, BarNumberEngraver.NumberMeasures(measures, 0));

        var svg = SvgGenerator.Generate(tree, new SvgRenderOptions { EmbedFont = false });
        var numbers = Regex.Matches(svg, "<text[^>]*font-weight=\"bold\"[^>]*>(\\d+)</text>")
            .Select(x => x.Groups[1].Value).ToList();
        // Systems open with E1's and E2's first measures (the breaks): no number on them.
        Assert.Empty(numbers);
        // …and the report counts five bars: E1's system holds one bar (2-3, the half it
        // completes and its own), E2's two (4-5).
        var report = LayoutReport.Generate(tree);
        Assert.Contains("3 systems, 5 bars", report);
        Assert.Contains("system 2: bars 2-3     (1 bar)", report);
        Assert.Contains("system 3: bars 4-5     (2 bars)", report);

        // Positive control: an ending that does not complete the bar is its own bar.
        string plain = book.Replace("section ~E2 { c'4 d' | e'1 | }", "section ~E2 { c'4 d' e' | f'1 | }");
        var control = SvgGenerator.CollectScore(SyntaxTree.Parse(plain), RenderSpecParser.FindFirst(SyntaxTree.Parse(plain)))
            .PrimaryContentStaff.PrimaryVoice.Measures;
        Assert.True(control[2].ContinuesBar);
        Assert.False(control[4].ContinuesBar);
        Assert.Equal(new[] { 1, 2, 2, 3, 4, 5 }, BarNumberEngraver.NumberMeasures(control, 0));
    }

    [Fact]
    public void ThePartMajorSpelling_AndASilentReference_AreReadTheSame()
    {
        string book = """
            time 4/4
            part m {
              clef bass
              section A { c4 d e f | g4 a | }
              section B { b4 c' | d'1 | }
            }
            form main { A ~B }
            score main { staff m }
            """;
        Assert.Empty(BarCodes(Diagnose(book)));
    }

    [Fact]
    public void ThePartThatHasNoCellInThePredecessor_IsNotExempt()
    {
        // The bass has no cell in A (the collector pads it with a whole bar), so its short
        // first bar in B completes nothing and stays a pickup nudge.
        string book = Head + """
            part low { clef bass }
            section A { m { c4 d e f | g4 a | } }
            section B { m { b4 c' | d'1 | }  low { c4 d | e1 | } }
            form main { A B }
            score main { staff m staff low }
            """;
        var diags = Diagnose(book);
        Assert.Single(diags, d => d.Code == "LYS2006");
    }
}
