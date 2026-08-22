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

using System;
using System.Linq;
using Xunit;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;

namespace LilySharp.Tests;

/// <summary>
/// The <c>paper</c> directive: the page's dimensions, from the source.
/// </summary>
/// <remarks>
/// The conversion cases assert the RULE the defaults were computed with — mm to staff
/// spaces through LilyPond's TeX point, rounded to six decimals — by demanding that a
/// book which STATES a default equals the default exactly, not to within an epsilon.
/// The end-to-end case reads the emitted SVG because the page's width is where the
/// answer becomes observable to a reader.
/// </remarks>
[Trait("Category", "Unit")]
public class PaperBlockTests
{
    // ================================================================================
    // Reading: keys, units, and the overlay
    // ================================================================================

    private static LayoutOptions Read(string paperBlock, out PaperPlanReader.Problem[] problems)
    {
        var tree = SyntaxTree.Parse(paperBlock + "\n" + Book);
        var paper = tree.GetRoot().DescendantNodes().OfType<PaperDeclarationSyntax>().Single();
        var options = PaperPlanReader.Read(paper, out var found);
        problems = [.. found];
        return options;
    }

    private static LayoutOptions ReadClean(string paperBlock)
    {
        var options = Read(paperBlock, out var problems);
        Assert.Empty(problems);
        return options;
    }

    [Fact]
    public void StatingTheDefaults_IsTheDefaults()
    {
        // ⚠️ EXACT equality, deliberately. Every LayoutOptions page default is
        // <millimetres> * 72.27 / 127 rounded to six decimals (LayoutOptions.cs), and the
        // reader rounds the same way — so `paperWidth 210mm` IS PageWidth 119.501575,
        // and a book that writes its defaults out lays out byte-identically to one that
        // writes nothing. An epsilon here would let the two spellings drift.
        var p = ReadClean("paper { paperWidth 210mm  paperHeight 297mm  leftMargin 15mm  "
            + "rightMargin 15mm  topMargin 10mm  bottomMargin 10mm }");
        Assert.Equal(LayoutOptions.Default, p);
    }

    [Fact]
    public void CmAndInchConvertThroughMm()
    {
        var p = ReadClean("paper { paperWidth 21cm  indent 1in }");
        Assert.Equal(LayoutOptions.Default.PageWidth, p.PageWidth); // 21cm = 210mm = the default
        Assert.Equal(14.454, p.Indent);                             // 25.4 * 72.27 / 127
    }

    [Fact]
    public void ABareNumberIsStaffSpaces()
    {
        var p = ReadClean("paper { paperWidth 100  spacingIncrement 1.5  indent -2.5 }");
        Assert.Equal(100, p.PageWidth);
        Assert.Equal(1.5, p.SpacingIncrement);
        Assert.Equal(-2.5, p.Indent);
    }

    [Fact]
    public void RaggedRightIsABareFlag()
    {
        Assert.False(LayoutOptions.Default.RaggedRight);
        Assert.True(ReadClean("paper { raggedRight }").RaggedRight);
    }

    [Fact]
    public void ASpacingBlockOverlaysOnlyTheLinesItWrites()
    {
        var p = ReadClean("paper { systemSystemSpacing { basicDistance 33 } }");
        Assert.Equal(33, p.VerticalSpacing.SystemSystem.BasicDistance);
        // The lines it does not write keep their LilyPond defaults…
        Assert.Equal(8, p.VerticalSpacing.SystemSystem.MinimumDistance);
        Assert.Equal(60, p.VerticalSpacing.SystemSystem.Stretchability);
        // …and the specs it does not name are untouched.
        Assert.Equal(LayoutOptions.Default.VerticalSpacing.ScoreSystem, p.VerticalSpacing.ScoreSystem);
        Assert.Equal(LayoutOptions.Default.StaffSpacing, p.StaffSpacing);
    }

    [Fact]
    public void TheStaffSpacingFamilyLivesHereToo()
    {
        // LilyPond keeps these on grobs (StaffGrouper.staff-staff-spacing); Lily# keeps
        // them in paper { } because they are applied score-wide in one pass and paper is
        // the spelling whose meaning IS score-wide (user decision 2026-08-23,
        // GRAMMAR_AUDIT §2.1/§2.2).
        var p = ReadClean("paper { staffStaffSpacing { padding 2.5 }  "
            + "staffGroupStaffSpacing { minimumDistance 9 } }");
        Assert.Equal(2.5, p.StaffSpacing.StaffStaff.Padding);
        Assert.Equal(9, p.StaffSpacing.StaffGroupStaff.MinimumDistance);
    }

    [Fact]
    public void AnUnwrittenStretchability_StaysAbsent()
    {
        // markup-markup-spacing declares NO stretchability in LilyPond, and absence is
        // a different quantity from any number (VerticalSpacingSpec.Stretchability's
        // remark). Overlaying its padding must not invent one.
        var p = ReadClean("paper { markupMarkupSpacing { padding 0.7 } }");
        Assert.Equal(0.7, p.VerticalSpacing.MarkupMarkup.Padding);
        Assert.Null(p.VerticalSpacing.MarkupMarkup.Stretchability);
        Assert.Equal(3.0,
            ReadClean("paper { markupMarkupSpacing { stretchability 3 } }")
                .VerticalSpacing.MarkupMarkup.Stretchability);
    }

    // ================================================================================
    // Refusals
    // ================================================================================

    [Fact]
    public void AnUnknownKeyIsAnError_AndTheRestStillBinds()
    {
        var p = Read("paper { bogus 3  paperWidth 105mm }", out var problems);
        var problem = Assert.Single(problems);
        Assert.Equal(DiagnosticCodes.UnknownPaperKey, problem.Code);
        Assert.True(problem.IsError);
        Assert.Contains("paperWidth", problem.Message, StringComparison.Ordinal); // names the vocabulary
        Assert.Equal(59.750787, p.PageWidth); // 105 * 72.27 / 127, rounded like the defaults
    }

    [Fact]
    public void ASpacedUnitIsNamedAsTheGluedSpelling()
    {
        // `210 mm` reads as a key named mm — the trap gets the fix, not the key list.
        Read("paper { paperWidth 210 mm }", out var problems);
        var problem = Assert.Single(problems);
        Assert.Equal(DiagnosticCodes.UnknownPaperKey, problem.Code);
        Assert.Contains("glued", problem.Message, StringComparison.Ordinal);
        Assert.Contains("210mm", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownUnitIsAnError()
    {
        var p = Read("paper { paperWidth 210px }", out var problems);
        var problem = Assert.Single(problems);
        Assert.Equal(DiagnosticCodes.UnknownPaperUnit, problem.Code);
        Assert.Equal(LayoutOptions.Default.PageWidth, p.PageWidth); // the entry bound nothing
    }

    [Fact]
    public void AUnitOnStretchability_IsRefused()
    {
        Read("paper { systemSystemSpacing { stretchability 3mm } }", out var problems);
        var problem = Assert.Single(problems);
        Assert.Equal(DiagnosticCodes.PaperUnitOnUnitless, problem.Code);
    }

    [Fact]
    public void ADuplicateKeyWarns_AndTheLastOneWins()
    {
        var p = Read("paper { paperWidth 100  paperWidth 90 }", out var problems);
        var problem = Assert.Single(problems);
        Assert.Equal(DiagnosticCodes.DuplicatePaperKey, problem.Code);
        Assert.False(problem.IsError);
        Assert.Equal(90, p.PageWidth);
    }

    [Theory]
    [InlineData("paper { systemSystemSpacing 3 }")]      // a spec key wants a block
    [InlineData("paper { paperWidth { } }")]             // a scalar key wants a number
    [InlineData("paper { raggedRight 1 }")]              // a flag takes nothing
    [InlineData("paper { paperWidth }")]                 // a scalar key with nothing after it
    public void AWrongShape_IsLYS9003(string block)
    {
        Read(block, out var problems);
        Assert.Contains(problems, x => x.Code == DiagnosticCodes.PaperEntryMissingValue && x.IsError);
    }

    [Fact]
    public void AnUnknownSubKeyIsAnError()
    {
        Read("paper { systemSystemSpacing { basic 12 } }", out var problems);
        var problem = Assert.Single(problems);
        Assert.Equal(DiagnosticCodes.UnknownPaperKey, problem.Code);
        Assert.Contains("basicDistance", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PaperWithABareValue_IsAnsweredWithTheBlockToWrite()
    {
        var d = Assert.Single(SyntaxTree.Parse("paper 210\n" + Book).Diagnostics,
            x => x.Code == DiagnosticCodes.PaperNeedsABlock);
        Assert.Contains("paper { paperWidth 210mm", d.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARefusedPaperDeclaration_KeepsEveryToken()
    {
        // Same claim as the refused fonts one-liner: a dropped token slides every later
        // data-pos and diagnostic column (RULES §5.1). Both halves — total width, and
        // each node's own span.
        const string src = "paper 210\nform main { A }\n";
        var root = SyntaxTree.Parse(src).GetRoot();

        Assert.Equal(src, root.ToFullString());
        foreach (var n in root.DescendantNodes())
            Assert.Equal(n.ToFullString(), src.Substring(n.Position, n.FullWidth));
    }

    [Fact]
    public void TheFullVocabulary_RoundTripsAndIsClean()
    {
        string src = "paper {\n"
            + "  paperWidth 210mm  paperHeight 29.7cm\n"
            + "  leftMargin 15mm  rightMargin 15mm  topMargin 10mm  bottomMargin 10mm\n"
            + "  indent 8.535827  shortIndent 0\n"
            + "  topSystemPadding 1  spacingIncrement 1.2\n"
            + "  raggedRight\n"
            + "  systemSystemSpacing { basicDistance 12  minimumDistance 8  padding 1  stretchability 60 }\n"
            + "  scoreSystemSpacing { basicDistance 14 }\n"
            + "  markupSystemSpacing { basicDistance 5 }\n"
            + "  scoreMarkupSpacing { basicDistance 12 }\n"
            + "  markupMarkupSpacing { basicDistance 1 }\n"
            + "  topSystemSpacing { basicDistance 6 }\n"
            + "  lastBottomSpacing { basicDistance 1 }\n"
            + "  staffStaffSpacing { basicDistance 9 }\n"
            + "  staffGroupStaffSpacing { basicDistance 10.5 }\n"
            + "  defaultStaffStaffSpacing { basicDistance 9 }\n"
            + "  nonStaffRelatedStaffSpacing { padding 0.5 }\n"
            + "  nonStaffUnrelatedStaffSpacing { padding 1.5 }\n"
            + "  nonStaffNonStaffSpacing { padding 0.5 }\n"
            + "}\n" + Book;
        var tree = SyntaxTree.Parse(src);

        Assert.Equal(src, tree.GetRoot().ToFullString());
        Diagnostic[] all = [.. tree.Diagnostics, .. SemanticValidation.Run(tree)];
        Assert.Empty(all);
    }

    [Fact]
    public void TwoPaperBlocks_AreNamedAsDuplicates()
    {
        var tree = SyntaxTree.Parse("paper { paperWidth 100 }\npaper { paperWidth 90 }\n" + Book);
        Assert.Contains(SemanticValidation.Run(tree),
            x => x.Code == DiagnosticCodes.DuplicateGlobalSetting
                 && x.Message.Contains("'paper'", StringComparison.Ordinal));
    }

    // ================================================================================
    // End to end: the directive reaches the page
    // ================================================================================

    [Fact]
    public void PaperReachesTheRenderedPage()
    {
        // A narrower paper is a narrower page — and the control beside it: an EMPTY
        // paper block, and one that merely states the defaults, leave the picture
        // untouched. ⚠️ Every comparison here strips data-pos FIRST: prepending a
        // directive shifts each later token's source offset, so the raw bytes differ
        // in both directions and would prove nothing about the geometry (the
        // rebaseline lesson of 2026-08-22 — SVG carries source positions).
        string noDirective = Geometry(Svg(Book));
        Assert.NotEqual(noDirective, Geometry(Svg("paper { paperWidth 105mm }\n" + Book)));
        Assert.Equal(noDirective, Geometry(Svg("paper { }\n" + Book)));
        Assert.Equal(noDirective, Geometry(Svg("paper { paperWidth 210mm  topMargin 10mm }\n" + Book)));
    }

    /// <summary>The SVG with its <c>data-pos</c> attributes removed: the picture,
    /// without the source offsets that ride along with it.</summary>
    private static string Geometry(string svg) =>
        System.Text.RegularExpressions.Regex.Replace(svg, " data-pos=\"[0-9]+\"", "");

    [Fact]
    public void PaperTravelsOnTheScore()
    {
        // The overlay rides Score.Paper into `new LayoutEngine(score.Paper)` at every
        // generator; this pins the collector half of that road.
        var tree = SyntaxTree.Parse("paper { paperWidth 100 }\n" + Book);
        var score = SvgGenerator.CollectScore(tree, null);
        Assert.Equal(100, score.Paper.PageWidth);
    }

    private static string Svg(string source) =>
        SvgGenerator.Generate(SyntaxTree.Parse(source), new SvgRenderOptions { EmbedFont = false });

    // A small book with real music, so the page has systems to lay out.
    private const string Book = """
        section Main {
          melody { c'4 d e f | g a b c' | }
        }
        form main { Main }
        score main { staff melody }
        """;
}
