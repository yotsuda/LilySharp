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
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using LilySharp.Core.LilyPond;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// <c>layout { tremoloBeams gapped | joined }</c> and <c>lysc --set tremoloBeams=joined</c> (Calamo
/// request 28, 2026-11-19; owner's decision): an old print's two-note tremolo, its beams reaching
/// both stems, against the default's beams kept 0.8 off them.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/beam.cc:402-427 Beam::get_gaps; :632-654 calc_beam_segments. The numbers are
/// LilyPond 2.26.0's under <c>\override Beam.gap = #0</c> (the twin's line), measured on the three
/// books of LilySharp-Lab sessions/p1310/trem.
/// </remarks>
[Trait("Category", "Unit")]
public class TremoloBeamsTests
{
    private const string Joined = "layout { tremoloBeams joined }";

    // The request's repro: a chord and a note, a dotted quarter's worth each, four beams.
    private static string Stemmed(string layout) => $$"""
        octave absolute
        time 6/8
        key b major
        {{layout}}
        part lh { clef bass }
        section A { lh { repeat tremolo 12 { <fis b>64 dis'64 } repeat tremolo 12 { <fis b>64 dis'64 } | } }
        score { staff lh }
        """;

    // Whole notes: no stem ink, and the second pair's chord carries two sharps.
    private static string Whole(string layout, string music) => $$"""
        octave absolute
        time 4/4
        {{layout}}
        part rh { clef treble }
        section A { rh { {{music}} | } }
        score { staff rh }
        """;

    private static string Svg(string book, params string[] settings) =>
        SvgGenerator.Generate(SyntaxTree.Parse(book), new SvgRenderOptions
        {
            EmbedFont = false,
            PaperOverrides = settings.Length == 0 ? null : PaperOverrides.Parse(settings, out _),
        });

    private static string Picture(string svg) => Regex.Replace(svg, "data-pos=\"[^\"]*\"", "");

    private static double D(string s) => double.Parse(s, CultureInfo.InvariantCulture);

    private static List<(double Left, double Right)> Beams(string svg) =>
        Regex.Matches(svg, "<polygon points=\"([-\\d.]+),[-\\d.]+ ([-\\d.]+),[-\\d.]+ [^\"]+\"")
            .Select(m => (D(m.Groups[1].Value), D(m.Groups[2].Value))).ToList();

    private static List<double> StemXs(string svg) =>
        Regex.Matches(svg, "<line x1=\"([-\\d.]+)\" y1=\"[-\\d.]+\" x2=\"([-\\d.]+)\" y2=\"[-\\d.]+\"[^>]*stroke-width=\"0.130\"")
            .Where(m => m.Groups[1].Value == m.Groups[2].Value)
            .Select(m => D(m.Groups[1].Value)).OrderBy(x => x).ToList();

    [Fact]
    public void TheDefault_IsThePictureOfABookThatWritesNoKey()
    {
        string plain = Svg(Stemmed(""));
        Assert.Equal(Picture(plain), Picture(Svg(Stemmed("layout { tremoloBeams gapped }"))));
        Assert.Equal(plain, Svg(Stemmed(""), "tremoloBeams=gapped"));
        Assert.NotEqual(plain, Svg(Stemmed(""), "tremoloBeams=joined"));
        Assert.Equal(plain, Svg(Stemmed("")));
        Assert.Equal(EngravingStyle.Default, PaperOverrides.Parse(["tremoloBeams=gapped"], out _)!
            .ApplyLayout(LayoutPlan.Default).EngravingStyle);
    }

    [Theory]
    [InlineData("", 0.8)]
    [InlineData(Joined, 0.0)]
    public void TheBeams_StopTheGapShortOfBothStems_AndJoinedIsNoGap(string layout, double gap)
    {
        string svg = Svg(Stemmed(layout));
        var stems = StemXs(svg);
        var beams = Beams(svg);
        Assert.Equal(4, stems.Count);
        Assert.Equal(8, beams.Count);
        // A beam's end stands on its stem's outer edge (half the stem's 0.13) when nothing is
        // taken off — where an ordinary beam's does.
        for (int pair = 0; pair < 2; pair++)
            foreach (var (left, right) in beams.Skip(pair * 4).Take(4))
            {
                // (The page writes two decimals: each side is within one rounding of the other.)
                Assert.Equal(stems[pair * 2] - 0.065 + gap, left, 0.011);
                Assert.Equal(stems[pair * 2 + 1] + 0.065 - gap, right, 0.011);
            }
    }

    [Fact]
    public void Joined_MovesNothingButTheBeams()
    {
        static string NoBeams(string svg) => Regex.Replace(Picture(svg), "<polygon[^>]*>", "");
        Assert.Equal(NoBeams(Svg(Stemmed(""), "tremoloBeams=gapped")), NoBeams(Svg(Stemmed(""), "tremoloBeams=joined")));
    }

    [Theory]
    // LilyPond 2.26.0: 2.058 − 0.04 + its 0.08 outline, and 2.858 − 0.04 + 0.08 under Beam.gap 0
    // — the heads' inner edges, half a gap off each.
    [InlineData("", 2.098)]
    [InlineData(Joined, 2.898)]
    public void AWholeNotePair_RunsBetweenTheHeads(string layout, double length)
    {
        var beams = Beams(Svg(Whole(layout, "repeat tremolo 32 { g''64 a' }")));
        Assert.Equal(4, beams.Count);
        Assert.All(beams, b => Assert.Equal(length, b.Right - b.Left, 2));
    }

    [Theory]
    // LilyPond 2.26.0: 0.50 and 1.70. The right end keeps off the chord's two sharps — their
    // united extent (2.32, the sharps staggered 1.285) and the accidental-padding 1.0 — under
    // either word; one sharp's width in its place drew both 1.29 longer (found 2026-11-19).
    [InlineData("", 0.50)]
    [InlineData(Joined, 1.70)]
    public void AWholeNotePair_KeepsOffTheLastChordsAccidentals(string layout, double length)
    {
        var beams = Beams(Svg(Whole(layout, "repeat tremolo 16 { c''32 <dis'' fis''> }")));
        Assert.Equal(3, beams.Count);
        Assert.All(beams, b => Assert.Equal(length, b.Right - b.Left, 2));
    }

    [Fact]
    public void TheSetting_IsTheLayoutKey()
    {
        var parsed = PaperOverrides.Parse(["tremoloBeams=joined"], out var error);
        Assert.Null(error);
        Assert.True(parsed!.ApplyLayout(LayoutPlan.Default).EngravingStyle.TremoloBeamsJoined);
        Assert.Equal(Picture(Svg(Stemmed(Joined))), Picture(Svg(Stemmed(""), "tremoloBeams=joined")));
        // ...and wins over the file's.
        Assert.Equal(Picture(Svg(Stemmed("layout { tremoloBeams gapped }"))),
            Picture(Svg(Stemmed(Joined), "tremoloBeams=gapped")));
        Assert.Null(PaperOverrides.Parse(["tremoloBeams=touching"], out error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void AnotherWord_IsAProblemInTheBlock()
    {
        var tree = SyntaxTree.Parse("layout { tremoloBeams touching }");
        LayoutPlanReader.Read(tree.GetRoot().DescendantNodes().OfType<LayoutDeclarationSyntax>().Single(), out var found);
        var problem = Assert.Single(found);
        Assert.True(problem.IsError);
        Assert.Contains("gapped or joined", problem.Message);
    }

    [Fact]
    public void TheTwin_WritesLilyPondsGap_OnlyForJoined()
    {
        string twin = new LilyPondExporter().Export(SyntaxTree.Parse(Stemmed(Joined)));
        Assert.Contains("\\override Beam.gap = #0\n", twin);
        int at = twin.IndexOf("\\override Beam.gap", System.StringComparison.Ordinal);
        Assert.True(twin.LastIndexOf("\\Score", at, System.StringComparison.Ordinal) > twin.LastIndexOf("\\layout", at, System.StringComparison.Ordinal));
        Assert.DoesNotContain("Beam.gap", new LilyPondExporter().Export(SyntaxTree.Parse(Stemmed(""))));
        Assert.DoesNotContain("Beam.gap", new LilyPondExporter().Export(SyntaxTree.Parse(Stemmed("layout { tremoloBeams gapped }"))));
    }
}
