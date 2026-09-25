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

using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using LilySharp.Core.LilyPond;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A <c>grandStaff { … }</c> ONE level inside a <c>staffGroup</c> or <c>choirStaff</c> — the
/// piano inside the orchestra's bracket (user decision, session 376).
/// </summary>
/// <remarks>
/// The model stays flat: the bracket's members become leaf groups that all carry ONE
/// <see cref="OuterStaffGroup"/>. Every number asserted about the page is LilyPond 2.26.0's,
/// measured on the same shapes (scratch/p377/nest):
/// <list type="bullet">
/// <item>staff tops 10.5 apart across the nested group's boundary, 9 inside it;</item>
/// <item>the outer bracket where a bracket always stands, the nested brace 0.3 left of the
/// bracket's ink — indent − 1.61;</item>
/// <item>a staffGroup draws bar lines through the gaps on both sides of the nested group, a
/// choirStaff through the nested group's own gap only.</item>
/// </list>
/// </remarks>
[Trait("Category", "Unit")]
public class NestedGrandStaffTests
{
    private const string Body = "octave absolute\ntime 4/4\n" + """
        part vln { clef treble }
        part pr { clef treble }
        part pl { clef bass }
        part vc { clef bass }
        section A {
          vln { c''1 | c''1 | }
          pr { e'1 | e'1 | }
          pl { c1 | c1 | }
          vc { c1 | c1 | }
          lyrics words sings pl { la la | }
        }
        form main { ~A }
        """;

    private static string Book(string render) => Body + "\nscore main { " + render + " }\n";

    private const string InBracket = "staffGroup { staff vln  grandStaff { staff pr  staff pl }  staff vc }";
    private const string InChoir = "choirStaff { staff vln  grandStaff { staff pr  staff pl }  staff vc }";

    private static RenderSpec Spec(string render) => RenderSpecParser.FindFirst(TestPaper.ParseAtIndentZero(Book(render)))!;

    private static ImmutableArray<Voice> OneVoice(string name) =>
        ImmutableArray.Create(new Voice(name, ImmutableArray<Measure>.Empty));

    private static string Svg(string render) => SvgGenerator.Generate(
        TestPaper.ParseAtIndentZero(Book(render)),
        new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false });

    /// <summary>The top line of each five-line staff, top to bottom (device Y, downward).</summary>
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

    /// <summary>Span-bar pieces: bar-line rects that start at one staff's bottom line.</summary>
    private static List<(double Y, double H)> BarRects(string svg) =>
        Regex.Matches(svg, "<rect x=\"[-\\d.]+\" y=\"([-\\d.]+)\" width=\"0\\.19\" height=\"([-\\d.]+)\"")
            .Select(m => (double.Parse(m.Groups[1].Value), double.Parse(m.Groups[2].Value)))
            .ToList();

    private static bool GapIsSpanned(string svg, double upperTop, double lowerTop) =>
        BarRects(svg).Any(r => Math.Abs(r.Y - (upperTop + 4)) < 0.02
                               && Math.Abs(r.H - (lowerTop - upperTop - 4)) < 0.02);

    [Fact]
    public void TheNestedGroupIsAMember_AndNotAlsoALooseItem()
    {
        var tree = TestPaper.ParseAtIndentZero(Book(InBracket));
        Assert.DoesNotContain(tree.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);

        var spec = RenderSpecParser.FindFirst(tree)!;
        var outer = Assert.IsType<GrandStaffRenderSpec>(Assert.Single(spec.Items)).GrandStaff;
        Assert.Equal(StaffGroupType.StaffGroup, outer.Type);
        Assert.Equal(4, outer.StaffCount);
        Assert.Collection(outer.Members,
            m => Assert.Equal("vln", Assert.IsType<SingleStaffSpec>(m).Staff.VoiceName),
            m => Assert.Equal(StaffGroupType.GrandStaff, Assert.IsType<GrandStaffRenderSpec>(m).GrandStaff.Type),
            m => Assert.Equal("vc", Assert.IsType<SingleStaffSpec>(m).Staff.VoiceName));
    }

    [Fact]
    public void TheBracketBecomesLeafGroups_SharingOneOuter()
    {
        var groups = Spec(InBracket).ToStaffGroups(OneVoice).ToList();

        Assert.Collection(groups,
            g => Assert.Equal(StaffGroupType.Single, g.Type),
            g => { Assert.Equal(StaffGroupType.GrandStaff, g.Type); Assert.Equal(2, g.StaffCount); },
            g => Assert.Equal(StaffGroupType.Single, g.Type));
        var outer = Assert.IsType<OuterStaffGroup>(groups[0].Outer);
        Assert.Equal(StaffGroupType.StaffGroup, outer.Type);
        Assert.All(groups, g => Assert.Same(outer, g.Outer));
    }

    [Fact]
    public void AFlatBracketCarriesNoOuter()
    {
        var group = Assert.Single(Spec("staffGroup { staff vln  staff vc }").ToStaffGroups(OneVoice));
        Assert.Null(group.Outer);
    }

    [Fact]
    public void TheBindingsKeepTheirStaffIndices()
    {
        int staffIndex = -1;
        var indexOf = new Dictionary<string, int>();
        foreach (var b in Spec(InBracket).GetVoiceBindings())
        {
            if (b.Slotting == VoiceSlotting.OwnStaff) staffIndex++;
            indexOf[b.VoiceName] = staffIndex;
        }
        Assert.Equal(0, indexOf["vln"]);
        Assert.Equal(1, indexOf["pr"]);
        Assert.Equal(2, indexOf["pl"]);
        Assert.Equal(3, indexOf["vc"]);
    }

    [Theory]
    [InlineData(InBracket)]
    [InlineData(InChoir)]
    public void AStaffCrossingTheNestedGroupsBoundary_SitsAtTheGroupDistance(string render)
    {
        var tops = StaffTops(Svg(render));

        Assert.Equal(4, tops.Count);
        Assert.Equal(10.5, tops[1] - tops[0], 2);   // vln → the nested piano
        Assert.Equal(9.0, tops[2] - tops[1], 2);    // inside the piano
        Assert.Equal(10.5, tops[3] - tops[2], 2);   // the nested piano → vc
    }

    [Fact]
    public void TheBraceStandsLeftOfTheBracket()
    {
        string svg = Svg(InBracket);

        // No instrument name, so the indent is 0: LilyPond's nested brace ends at indent − 1.61
        // and the bracket stroke is centred at indent − 1.085.
        var brace = Regex.Match(svg, "<text x=\"([-\\d.]+)\"[^>]*font-family=\"Emmentaler-Brace\"");
        Assert.True(brace.Success, "no brace was drawn");
        Assert.Equal(-1.61, double.Parse(brace.Groups[1].Value), 1);
        Assert.Equal(1, Regex.Matches(svg, "font-family=\"Emmentaler-Brace\"").Count);

        var stroke = Regex.Match(svg, "<line x1=\"([-\\d.]+)\"[^>]*stroke-width=\"0\\.450\"");
        Assert.True(stroke.Success, "no bracket stroke was drawn");
        Assert.InRange(double.Parse(stroke.Groups[1].Value), -1.096, -1.074);

        Assert.Equal(-1.61, LilySharp.Core.Svg.Layout.MultiStaffLayouter.SystemStartBraceRightEdgeAgainst(
            LilySharp.Core.Svg.Layout.MultiStaffLayouter.SystemStartBracketCentre(0.0) - 0.225), 9);
    }

    [Fact]
    public void AStaffGroupSpansTheGapsOnBothSidesOfTheNestedGroup()
    {
        string svg = Svg(InBracket);
        var tops = StaffTops(svg);

        Assert.True(GapIsSpanned(svg, tops[0], tops[1]), "vln → piano gap not spanned");
        Assert.True(GapIsSpanned(svg, tops[1], tops[2]), "the piano's own gap not spanned");
        Assert.True(GapIsSpanned(svg, tops[2], tops[3]), "piano → vc gap not spanned");
    }

    [Fact]
    public void AChoirStaffSpansOnlyTheNestedGroupsOwnGap()
    {
        string svg = Svg(InChoir);
        var tops = StaffTops(svg);

        Assert.False(GapIsSpanned(svg, tops[0], tops[1]));
        Assert.True(GapIsSpanned(svg, tops[1], tops[2]));
        Assert.False(GapIsSpanned(svg, tops[2], tops[3]));
    }

    /// <summary>
    /// ⚠️ THE TWIN'S DOUBLE-EMIT. LilyPondExporter.RenderRows walks descendants too; without its
    /// guard the grand staff is written inside the StaffGroup AND again as a loose row.
    /// </summary>
    [Fact]
    public void TheTwinWritesTheGrandStaffOnce_InsideTheStaffGroup()
    {
        string ly = new LilyPondExporter().Export(TestPaper.ParseAtIndentZero(Book(InBracket)));

        Assert.Equal(1, Regex.Matches(ly, @"\\new GrandStaff").Count);
        Assert.Equal(1, Regex.Matches(ly, @"\\new StaffGroup").Count);
        int group = ly.IndexOf(@"\new StaffGroup", StringComparison.Ordinal);
        int grand = ly.IndexOf(@"\new GrandStaff", StringComparison.Ordinal);
        Assert.True(grand > group, "the GrandStaff is not inside the StaffGroup");
    }

    [Fact]
    public void ARowAfterTheNestedGroup_IsRefused()
    {
        var diags = SemanticValidation.Run(TestPaper.ParseAtIndentZero(Book(
            "staffGroup { staff vln  grandStaff { staff pr  staff pl }  lyrics words  staff vc }")));

        var d = Assert.Single(diags, d => d.Code == DiagnosticCodes.GroupRowNotBoundToStaffAbove);
        Assert.Contains("nested group", d.Message);
    }

    [Fact]
    public void ARowInsideTheNestedGroup_StillFolds()
    {
        var spec = Spec("staffGroup { staff vln  grandStaff { staff pr  staff pl  lyrics words }  staff vc }");

        var outer = Assert.IsType<GrandStaffRenderSpec>(Assert.Single(spec.Items)).GrandStaff;
        var inner = Assert.IsType<GrandStaffRenderSpec>(outer.Members[1]).GrandStaff;
        Assert.Equal(new[] { "words" }, Assert.IsType<SingleStaffSpec>(inner.Members[1]).Staff.WithLyrics);
    }
}
