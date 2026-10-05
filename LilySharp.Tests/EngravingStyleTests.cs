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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// <c>layout { lineThickness … StaffLine.thickness … Stem.thickness … }</c> and the same keys as
/// <c>lysc --set lineThickness=…</c>
/// (LilySharp-Omr's proposal of 2026-10-02 P4, session 822): LilyPond's line thicknesses and
/// stem length under its own names and units, held by <see cref="EngravingStyle"/>.
/// </summary>
[Trait("Category", "Unit")]
public class EngravingStyleTests
{
    private static EngravingStyle StyleOf(params string[] settings)
    {
        var parsed = PaperOverrides.Parse(settings, out var error);
        Assert.Null(error);
        return parsed!.ApplyLayout(LayoutPlan.Default).EngravingStyle;
    }

    [Fact]
    public void TheKeys_SetLilyPondsProperties()
    {
        var s = StyleOf("lineThickness=0.15", "StaffLine.thickness=1.2", "LedgerLine.thickness=2,0.05",
            "Stem.thickness=2", "Stem.lengthFraction=1.2", "Beam.thickness=0.6",
            "BarLine.thinThickness=3", "BarLine.thickThickness=7");
        Assert.Equal(0.15, s.LineThickness);
        Assert.Equal(1.2, s.StaffSymbolThickness);
        Assert.Equal(2, s.LedgerLineThicknessLines);
        Assert.Equal(0.05, s.LedgerLineThicknessSpaces);
        Assert.Equal(2, s.StemThickness);
        Assert.Equal(1.2, s.StemLengthFraction);
        Assert.Equal(0.6, s.BeamThickness);
        Assert.Equal(3, s.BarLineHairThickness);
        Assert.Equal(7, s.BarLineThickThickness);
        Assert.Equal(EngravingStyle.Default, StyleOf("spacingIncrement=1.6"));
    }

    [Theory]
    [InlineData("lineThickness=0")]
    [InlineData("lineThickness=0.1mm")]
    [InlineData("Stem.thickness=-1")]
    [InlineData("Stem.lengthFraction=x")]
    [InlineData("LedgerLine.thickness=1")]
    [InlineData("LedgerLine.thickness=0,0")]
    [InlineData("LedgerLine.thickness=1,-0.1")]
    [InlineData("Stem.length=3.5")]
    public void AValueThatDoesNotRead_IsRefused(string setting)
    {
        Assert.Null(PaperOverrides.Parse([setting], out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    /// <summary>The scope holds a style and gives the last one back; outside every scope the
    /// constants are LilyPond's.</summary>
    [Fact]
    public void TheStyle_IsHeldForItsScopeOnly()
    {
        Assert.Equal(0.1, EngravingDefaults.LineThickness);
        Assert.Equal(0.13, EngravingDefaults.StemThickness, 12);
        using (EngravingStyle.Use(EngravingStyle.Default with { LineThickness = 0.2, StemLengthFraction = 1.5 }))
        {
            Assert.Equal(0.2, EngravingDefaults.LineThickness);
            Assert.Equal(1.5, Core.Svg.Layout.StemDetails.Default.LengthFraction);
            using (EngravingStyle.Use(EngravingStyle.Default))
                Assert.Same(EngravingStyle.DefaultStemDetails, Core.Svg.Layout.StemDetails.Default);
            Assert.Equal(0.2, EngravingDefaults.LineThickness);
        }
        Assert.Equal(0.1, EngravingDefaults.LineThickness);
        Assert.Same(EngravingStyle.DefaultStemDetails, Core.Svg.Layout.StemDetails.Default);
    }

    private const string Book = """
        octave absolute
        part m { clef treble
          section A { c'8 d' e' f' g'4 a'' | b16 c' d' e' f'8 g' a'2 | c'''4 b'' a'' g'' | e2 c''2 | }
        }
        form main { A }
        score main { staff m }
        """;

    private static string Svg(string book, params string[] settings) =>
        SvgGenerator.Generate(SyntaxTree.Parse(book), new SvgRenderOptions
        {
            EmbedFont = false,
            PaperOverrides = settings.Length == 0 ? null : PaperOverrides.Parse(settings, out _),
        });

    private static string[] StrokeWidths(string svg) =>
        [.. Regex.Matches(svg, "stroke-width=\"([\\d.]+)\"").Select(m => m.Groups[1].Value).Distinct().Order()];

    private static string[] BarWidths(string svg) =>
        [.. Regex.Matches(svg, "<rect x=\"[\\d.]+\" y=\"[\\d.]+\" width=\"([\\d.]+)\" height=\"4.00\"").Select(m => m.Groups[1].Value).Distinct()];

    /// <summary>
    /// The thicknesses LilyPond 2.26.0 draws the same book with under the same overrides (Lab
    /// sessions/p822/p4, the <c>lysc ly --pin-fonts</c> twin with <c>line-thickness = 0.75\pt</c>,
    /// <c>ledger-line-thickness = #'(2 . 0.1)</c>, <c>BarLine.hair-thickness = 3</c>,
    /// <c>Stem.thickness = 2</c>): staff lines 0.15, stems 0.30, ledger lines 2 × 0.15 + 0.1,
    /// thin bar lines 3 × 0.15.
    /// </summary>
    [Fact]
    public void TheThicknesses_AreLilyPonds()
    {
        string plain = Svg(Book);
        Assert.Equal(["0.100", "0.130", "0.200"], StrokeWidths(plain));   // staff, stem, ledger
        Assert.Equal(["0.19"], BarWidths(plain));

        string set = Svg(Book, "lineThickness=0.15", "Stem.thickness=2", "BarLine.thinThickness=3",
            "LedgerLine.thickness=2,0.1");
        Assert.Equal(["0.150", "0.300", "0.400"], StrokeWidths(set));
        Assert.Equal(["0.45"], BarWidths(set));
    }

    /// <summary>
    /// StaffSymbol.thickness is the staff's line thickness, which nearly every line is stated
    /// in — the stems follow it — but the bar lines read the paper's line-thickness, as
    /// LilyPond's do (scm/bar-line.scm make-simple-bar-line).
    /// </summary>
    [Fact]
    public void TheStaffsThickness_MovesTheStemsAndNotTheBarLines()
    {
        string set = Svg(Book, "StaffLine.thickness=2");
        Assert.Equal(["0.200", "0.260", "0.300"], StrokeWidths(set));      // staff, 1.3 × 0.2, 0.2 + 0.1
        Assert.Equal(["0.19"], BarWidths(set));
    }

    private const string Beamed = """
        octave absolute
        part m { clef treble
          section A { c'8 d' e' f' g' a' b' c'' | b16 c' d' e' f' g' a' b' e''8 d'' c'' b' | a'8 g' f' e' d''16 e'' f'' g'' a''8 g'' | }
        }
        form main { A }
        score main { staff m }
        """;

    private static double[] StemLengths(string svg, string width) =>
        [.. Regex.Matches(svg, "<line x1=\"([-\\d.]+)\" y1=\"([-\\d.]+)\" x2=\"([-\\d.]+)\" y2=\"([-\\d.]+)\"[^>]*stroke-width=\"" + Regex.Escape(width) + "\"")
            .Select(m => Math.Abs(double.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture)
                                  - double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)))
            .Order()];

    /// <summary>
    /// The 30 beamed stems' lengths LilyPond 2.26.0 draws (sorted; Lab sessions/p822/p4, the
    /// twin, stem rects) at its defaults and under <c>line-thickness = 0.75\pt</c>,
    /// <c>Stem.thickness = 2</c>, <c>Stem.length-fraction = 1.2</c>, <c>Beam.beam-thickness = 0.6</c>.
    /// Lily#'s stem is drawn 0.03 longer than LilyPond's rect at the default — the drawing's
    /// convention, measured 0.027 … 0.036 over all 30 — and stays so under the settings: the
    /// beams were placed where LilyPond places them.
    /// </summary>
    [Fact]
    public void TheBeamedStems_AreLilyPondsUnderTheSettings()
    {
        double[] lilyPondDefault =
        [
            2.4970, 2.4970, 2.5025, 2.6160, 2.6160, 2.6682, 2.6682, 2.8394, 2.8394, 2.8512,
            2.8512, 2.9400, 3.0106, 3.0106, 3.0864, 3.0864, 3.3138, 3.3216, 3.3216, 3.3776,
            3.8138, 3.8151, 4.3138, 4.3138, 4.8138, 4.8138, 5.3138, 5.8138, 6.0000, 6.3176,
        ];
        double[] lilyPondSet =
        [
            3.0768, 3.2983, 3.2983, 3.3265, 3.4753, 3.4753, 3.5353, 3.5672, 3.5672, 3.5761,
            3.6523, 3.6523, 3.7483, 3.7483, 3.8258, 3.8293, 3.8293, 3.9293, 3.9293, 3.9626,
            4.1104, 4.1104, 4.3138, 4.3900, 4.8138, 4.8173, 5.3138, 5.8138, 6.0289, 6.3237,
        ];
        var plain = StemLengths(Svg(Beamed), "0.130");
        var set = StemLengths(Svg(Beamed, "lineThickness=0.15", "Stem.thickness=2", "Stem.lengthFraction=1.2",
            "Beam.thickness=0.6"), "0.300");
        Assert.Equal(30, plain.Length);
        Assert.Equal(30, set.Length);
        for (int i = 0; i < 30; i++)
        {
            Assert.InRange(plain[i] - lilyPondDefault[i], 0.02, 0.045);
            Assert.InRange(set[i] - lilyPondSet[i], 0.02, 0.045);
        }
    }

    /// <summary>
    /// The keys are the language's (owner's decision 2026-10-05): the file writes them in
    /// <c>layout { }</c>, a score overrides them through a named block, and <c>--set</c> wins
    /// over both — and each reaches the page.
    /// </summary>
    [Fact]
    public void TheFile_WritesTheStyle_AScoreOverridesIt_AndTheSettingWins()
    {
        const string music = """
            octave absolute
            part m { clef treble
              section A { c'8 d' e' f' g'4 a'' | b16 c' d' e' f'8 g' a'2 | }
            }
            form main { A }
            """;
        string file = "layout { lineThickness 0.15  Stem.thickness 2 }\n" + music + "score main { staff m }\n";
        Assert.Equal(["0.150", "0.250", "0.300"], StrokeWidths(Svg(file)));                // ledger 0.15 + 0.1
        Assert.Equal(["0.150", "0.250", "0.450"], StrokeWidths(Svg(file, "Stem.thickness=3")));

        string scored = "layout thin { lineThickness 0.08 }\n" + music
            + "score main { layout thin { Stem.thickness 1.0 }  staff m }\n";
        Assert.Equal(["0.080", "0.180"], StrokeWidths(Svg(scored)));                        // stem 1.0 × 0.08 joins the staff

        var tree = SyntaxTree.Parse(file);
        var layout = tree.GetRoot().DescendantNodes<LayoutDeclarationSyntax>().First();
        LayoutPlanReader.Read(layout, out var problems);
        Assert.Empty(problems);
    }

    /// <summary>The <c>lysc ly</c> twin writes the style in LilyPond's words — and nothing at the
    /// defaults, so a book that writes none twins as before.</summary>
    [Fact]
    public void TheTwin_SpellsTheStyleInLilyPond()
    {
        string Twin(string layout) => new Core.LilyPond.LilyPondExporter().Export(SyntaxTree.Parse(
            layout + "part m { }\nsection A { m { c'1 | } }\nform main { A }\nscore main { staff m }\n"));
        string styled = Twin("layout { lineThickness 0.15  StaffLine.thickness 1.2  LedgerLine.thickness 2 0.1  "
            + "Stem.thickness 2  Stem.lengthFraction 1.2  Beam.thickness 0.6  BarLine.thinThickness 3  BarLine.thickThickness 7 }\n");
        foreach (string line in new[]
        {
            "line-thickness = 0.75\\pt", "\\override StaffSymbol.thickness = #1.2",
            "\\override StaffSymbol.ledger-line-thickness = #'(2 . 0.1)", "\\override Stem.thickness = #2",
            "\\override Stem.length-fraction = #1.2", "\\override Beam.beam-thickness = #0.6",
            "\\override BarLine.hair-thickness = #3", "\\override BarLine.thick-thickness = #7",
        })
            Assert.Contains(line, styled, StringComparison.Ordinal);
        string plain = Twin("");
        Assert.DoesNotContain("line-thickness", plain, StringComparison.Ordinal);
        Assert.Equal(plain, Twin("layout { Stem.thickness 1.3  lineThickness 0.1 }\n"));
    }

    /// <summary>A setting reaches the PNG and the PDF as well: the same layout, drawn.</summary>
    [Fact]
    public void TheStyle_ChangesEveryPicture_AndNothingWithoutIt()
    {
        var tree = SyntaxTree.Parse(Book);
        var set = PaperOverrides.Parse(["lineThickness=0.2"], out _);
        Assert.NotEqual(Core.Png.PngGenerator.Generate(tree),
            Core.Png.PngGenerator.Generate(tree, new Core.Png.PngRenderOptions { PaperOverrides = set }));
        Assert.Equal(Svg(Book), Svg(Book, "spacingIncrement=1.2"));
    }
}
