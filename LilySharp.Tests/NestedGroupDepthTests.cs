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
using LilySharp.Core.LilyPond;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Any group inside any group, at any depth, drawn AS WRITTEN (user decision, session 376:
/// "respect the .lys the user wrote").
/// </summary>
/// <remarks>
/// Every number is LilyPond 2.26.0's, measured on the same shapes at indent 0
/// (scratch/p377/nest/deep.ly for positions and distances, sd*.ly with -dbackend=svg for the
/// span bars):
/// <list type="bullet">
/// <item>each delimiter stands against its PARENT's ink — a bracket 0.8 left of it, a brace
/// 0.3 — and a top-level one against the SystemStartBar; siblings are not column-aligned;</item>
/// <item>9 between two staves of one innermost group, 10.5 across a group boundary;</item>
/// <item>a gap is spanned when ANY group holding both staves is a grandStaff or staffGroup.</item>
/// </list>
/// </remarks>
[Trait("Category", "Unit")]
public class NestedGroupDepthTests
{
    private const string Body = "octave absolute\ntime 4/4\n" + """
        part pa { clef treble }
        part pb { clef treble }
        part pc { clef treble }
        part pd { clef bass }
        part pe { clef treble }
        section A {
          pa { c''1 | c''1 | }
          pb { e'1 | e'1 | }
          pc { g'1 | g'1 | }
          pd { c1 | c1 | }
          pe { c''1 | c''1 | }
        }
        form main { ~A }
        """;

    private static string Book(string render) => Body + "\nscore main { " + render + " }\n";

    private static string Svg(string render) => SvgGenerator.Generate(
        SyntaxTree.Parse(Book(render)),
        new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false });

    private static List<double> StaffTops(string svg)
    {
        var ys = Regex.Matches(svg, "<line x1=\"[-\\d.]+\" y1=\"([-\\d.]+)\" x2=\"[-\\d.]+\" y2=\"\\1\" stroke=\"#000000\" stroke-width=\"0.100\"/>")
            .Select(m => double.Parse(m.Groups[1].Value))
            .Distinct().OrderBy(y => y).ToList();
        var tops = new List<double>();
        for (int i = 0; i < ys.Count; i++)
            if (i == 0 || ys[i] - ys[i - 1] > 1.5)
                tops.Add(ys[i]);
        return tops;
    }

    /// <summary>The right edge of every brace drawn, sorted.</summary>
    private static double[] BraceRightEdges(string svg) =>
        Regex.Matches(svg, "<text x=\"([-\\d.]+)\"[^>]*font-family=\"Emmentaler-Brace\"")
            .Select(m => double.Parse(m.Groups[1].Value)).OrderBy(x => x).ToArray();

    /// <summary>The centre of every bracket stroke drawn, sorted.</summary>
    private static double[] BracketCentres(string svg) =>
        Regex.Matches(svg, "<line x1=\"([-\\d.]+)\"[^>]*stroke-width=\"0\\.450\"")
            .Select(m => double.Parse(m.Groups[1].Value)).OrderBy(x => x).ToArray();

    private static bool GapIsSpanned(string svg, double upperTop, double lowerTop) =>
        Regex.Matches(svg, "<rect x=\"[-\\d.]+\" y=\"([-\\d.]+)\" width=\"0\\.19\" height=\"([-\\d.]+)\"")
            .Any(m => Math.Abs(double.Parse(m.Groups[1].Value) - (upperTop + 4)) < 0.02
                      && Math.Abs(double.Parse(m.Groups[2].Value) - (lowerTop - upperTop - 4)) < 0.02);

    private static void AssertGaps(string svg, params double[] expected)
    {
        var tops = StaffTops(svg);
        Assert.Equal(expected.Length + 1, tops.Count);
        for (int i = 0; i < expected.Length; i++)
            Assert.Equal(expected[i], tops[i + 1] - tops[i], 2);
    }

    private static void AssertSpans(string svg, params bool[] expected)
    {
        var tops = StaffTops(svg);
        for (int i = 0; i < expected.Length; i++)
            Assert.True(expected[i] == GapIsSpanned(svg, tops[i], tops[i + 1]),
                $"gap {i}: expected spanned={expected[i]}");
    }

    private static void AssertNear(double[] lp, double[] actual, double tolerance = 0.02)
    {
        Assert.Equal(lp.Length, actual.Length);
        for (int i = 0; i < lp.Length; i++)
            Assert.InRange(actual[i], lp[i] - tolerance, lp[i] + tolerance);
    }

    [Theory]
    [InlineData("grandStaff { staff pa grandStaff { staff pb staff pd} }")]
    [InlineData("grandStaff { staffGroup { staff pa staff pb}  staff pd}")]
    [InlineData("choirStaff { staffGroup { staff pa staff pb}  staff pc}")]
    [InlineData("staffGroup { staffGroup { grandStaff { staff pa staff pd}  staff pb}  staff pc}")]
    [InlineData("staffGroup { grandStaff { staff pa staff pd} }")]
    public void EveryNesting_ParsesAndValidatesClean(string render)
    {
        var tree = SyntaxTree.Parse(Book(render));
        Assert.DoesNotContain(tree.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(SemanticValidation.Run(tree), d => d.Severity == DiagnosticSeverity.Error);
    }

    /// <summary>D1: StaffGroup { GrandStaff{a d}  StaffGroup{ b  GrandStaff{c d} } } — siblings at
    /// different depths, each against its own parent.</summary>
    [Fact]
    public void MixedDepthSiblings_EachStandAgainstItsOwnParent()
    {
        string svg = Svg("staffGroup { grandStaff { staff pa staff pd}  staffGroup { staff pb grandStaff { staff pc staff pd} } }");

        AssertNear(new[] { -2.86, -1.61 }, BraceRightEdges(svg));
        AssertNear(new[] { -2.335, -1.085 }, BracketCentres(svg));
        AssertGaps(svg, 9, 10.5, 10.5, 9);
        AssertSpans(svg, true, true, true, true);
    }

    /// <summary>D2: three levels — StaffGroup { StaffGroup { GrandStaff{a d}  b }  c }.</summary>
    [Fact]
    public void ThreeLevels_ChainOutward()
    {
        string svg = Svg("staffGroup { staffGroup { grandStaff { staff pa staff pd}  staff pb}  staff pc}");

        AssertNear(new[] { -2.86 }, BraceRightEdges(svg));
        AssertNear(new[] { -2.335, -1.085 }, BracketCentres(svg));
        AssertGaps(svg, 9, 10.5, 10.5);
    }

    /// <summary>D4: GrandStaff { StaffGroup{a b}  d } — the order written is the order drawn: the
    /// bracket stands OUTSIDE the brace, against the brace's ink.</summary>
    [Fact]
    public void ABracketInsideABrace_StandsLeftOfTheBrace()
    {
        string svg = Svg("grandStaff { staffGroup { staff pa staff pb}  staff pd}");

        AssertNear(new[] { -0.36 }, BraceRightEdges(svg));
        AssertNear(new[] { -2.515 }, BracketCentres(svg));
        AssertGaps(svg, 9, 10.5);
        AssertSpans(svg, true, true);
    }

    /// <summary>D10: GrandStaff { a  GrandStaff{b d} } — a brace in a brace.</summary>
    [Fact]
    public void ABraceInsideABrace_StandsAgainstTheOuterBrace()
    {
        string svg = Svg("grandStaff { staff pa grandStaff { staff pb staff pd} }");

        AssertNear(new[] { -1.79, -0.36 }, BraceRightEdges(svg));
        AssertGaps(svg, 10.5, 9);
    }

    /// <summary>D5: StaffGroup { GrandStaff{a d} } — both delimiters over the same staves are
    /// still both drawn, the child outside, as written.</summary>
    [Fact]
    public void AGroupHoldingOnlyOneGroup_DrawsBoth()
    {
        string svg = Svg("staffGroup { grandStaff { staff pa staff pd} }");

        AssertNear(new[] { -1.61 }, BraceRightEdges(svg));
        AssertNear(new[] { -1.085 }, BracketCentres(svg));
        AssertGaps(svg, 9);
    }

    /// <summary>D9: ChoirStaff { a  GrandStaff{ StaffGroup{b c}  d }  e } — a bracket two
    /// levels down, outside a tall brace; only the grand staff's gaps are spanned.</summary>
    [Fact]
    public void ABracketOutsideATallBrace_InsideAChoir()
    {
        string svg = Svg("choirStaff { staff pa grandStaff { staffGroup { staff pb staff pc}  staff pd}  staff pe}");

        AssertNear(new[] { -1.61 }, BraceRightEdges(svg));
        AssertNear(new[] { -3.765, -1.085 }, BracketCentres(svg));
        AssertGaps(svg, 10.5, 9, 10.5, 10.5);
        AssertSpans(svg, false, true, true, false);
    }

    /// <summary>sd6: GrandStaff { ChoirStaff{a b}  d } — the grand staff spans the choir's gap.</summary>
    [Fact]
    public void AChoirInsideAGrandStaff_HasItsGapSpannedByTheGrandStaff()
    {
        string svg = Svg("grandStaff { choirStaff { staff pa staff pb}  staff pd}");

        AssertGaps(svg, 9, 10.5);
        AssertSpans(svg, true, true);
    }

    /// <summary>sd7: StaffGroup { ChoirStaff{ GrandStaff{a d}  b }  c } — every gap spanned.</summary>
    [Fact]
    public void AChoirInsideAStaffGroup_HasEveryGapSpanned()
    {
        string svg = Svg("staffGroup { choirStaff { grandStaff { staff pa staff pd}  staff pb}  staff pc}");

        AssertGaps(svg, 9, 10.5, 10.5);
        AssertSpans(svg, true, true, true);
    }

    /// <summary>sd3: ChoirStaff { StaffGroup{a b}  c } — only the inner staffGroup's gap.</summary>
    [Fact]
    public void AStaffGroupInsideAChoir_SpansOnlyItsOwnGap()
    {
        string svg = Svg("choirStaff { staffGroup { staff pa staff pb}  staff pc}");

        AssertGaps(svg, 9, 10.5);
        AssertSpans(svg, true, false);
    }

    /// <summary>D8: StaffGroup { a  StaffGroup{b c}  e } — the violins' sub-bracket.</summary>
    [Fact]
    public void ASubBracket_StandsOutsideTheBracket()
    {
        string svg = Svg("staffGroup { staff pa staffGroup { staff pb staff pc}  staff pe}");

        AssertNear(new[] { -2.335, -1.085 }, BracketCentres(svg));
        AssertGaps(svg, 10.5, 9, 10.5);
        AssertSpans(svg, true, true, true);
    }

    [Fact]
    public void TheTwinNestsTheContextsAsWritten()
    {
        string ly = new LilyPondExporter().Export(SyntaxTree.Parse(Book(
            "grandStaff { staffGroup { staff pa staff pb}  staff pd}")));

        Assert.Equal(1, Regex.Matches(ly, @"\\new GrandStaff").Count);
        Assert.Equal(1, Regex.Matches(ly, @"\\new StaffGroup").Count);
        Assert.True(ly.IndexOf(@"\new StaffGroup", StringComparison.Ordinal)
                    > ly.IndexOf(@"\new GrandStaff", StringComparison.Ordinal),
            "the StaffGroup is not inside the GrandStaff");
    }
}
