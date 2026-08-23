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
using LilySharp.Core.MusicXmlImport;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests.MusicXml;

/// <summary>
/// The importer's <c>&lt;defaults&gt;&lt;page-layout&gt;</c> → <c>paper { }</c> mapping:
/// the source's page size and margins come across in millimetres, only the stated keys
/// are written, and everything the mapping cannot carry (no scaling, per-parity margins,
/// a staff of a different size) is surfaced in the report, never silently mangled.
/// </summary>
/// <remarks>
/// The fixtures use <c>scaling 7mm / 40 tenths</c> — 0.175mm per tenth — so the expected
/// millimetre values are exact short decimals (1200 tenths = 210mm), and the staff is
/// 19.84 DTP points, which is nominally 20pt and therefore silent. The semantic half of
/// the emitted spellings (that <c>paperWidth 210mm</c> IS the a4 default width, etc.) is
/// PaperBlockTests' claim; here the emitted source must parse AND validate clean, which
/// walks it through PaperValidator's full vocabulary.
/// </remarks>
public class MusicXmlPageLayoutImportTests
{
    /// <summary>A one-note score wrapped around the given &lt;defaults&gt; children.</summary>
    private static string Score(string defaults) => $"""
        <score-partwise version="4.0">
          <defaults>{defaults}</defaults>
          <part-list><score-part id="P1"><part-name>M</part-name></score-part></part-list>
          <part id="P1"><measure number="1">
            <attributes><divisions>1</divisions>
              <time><beats>4</beats><beat-type>4</beat-type></time>
              <clef><sign>G</sign><line>2</line></clef></attributes>
            <note><pitch><step>C</step><octave>4</octave></pitch><duration>4</duration><type>whole</type></note>
          </measure></part>
        </score-partwise>
        """;

    private const string Scaling20Pt =
        "<scaling><millimeters>7</millimeters><tenths>40</tenths></scaling>";

    private static (string Lys, ImportReport Report) Import(string xml)
        => new MusicXmlImporter().Import(xml);

    private static bool HasErrors(SyntaxTree tree)
        => tree.Diagnostics.Concat(SemanticValidation.Run(tree))
            .Any(d => d.Severity == DiagnosticSeverity.Error);

    [Fact]
    public void PageLayout_ImportsAsAPaperBlock_InMillimetres()
    {
        var (lys, report) = Import(Score(Scaling20Pt + """
            <page-layout>
              <page-height>1680</page-height>
              <page-width>1200</page-width>
              <page-margins type="both">
                <left-margin>80</left-margin><right-margin>80</right-margin>
                <top-margin>60</top-margin><bottom-margin>60</bottom-margin>
              </page-margins>
            </page-layout>
            """));

        Assert.Contains("paper {\n  paperWidth 210mm\n  paperHeight 294mm\n"
            + "  leftMargin 14mm\n  rightMargin 14mm\n"
            + "  topMargin 10.5mm\n  bottomMargin 10.5mm\n}\n", lys);
        Assert.False(report.HasWarnings, string.Join("; ", report.Warnings));
        // The emitted spellings must survive the paper block's own validator, not
        // just the parser — an unknown key or a spaced unit is a semantic ERROR.
        Assert.False(HasErrors(SyntaxTree.Parse(lys)), lys);
    }

    [Fact]
    public void NoDefaults_EmitsNoPaperBlock()
    {
        var (lys, report) = Import(Score(""));
        Assert.DoesNotContain("paper {", lys);
        Assert.False(report.HasWarnings, string.Join("; ", report.Warnings));
    }

    [Fact]
    public void PageLayoutWithoutScaling_IsDroppedWithAWarning()
    {
        // Tenths have no physical size without <scaling>; converting by a guessed
        // scale could be arbitrarily wrong, so the page is dropped and said so.
        var (lys, report) = Import(Score("""
            <page-layout><page-height>1680</page-height><page-width>1200</page-width></page-layout>
            """));
        Assert.DoesNotContain("paper {", lys);
        Assert.Contains(report.Warnings, w => w.Contains("<scaling>"));
    }

    [Fact]
    public void PartialPageLayout_EmitsOnlyTheStatedKeys()
    {
        var (lys, report) = Import(Score(Scaling20Pt + """
            <page-layout><page-height>1680</page-height><page-width>1200</page-width></page-layout>
            """));
        Assert.Contains("paperWidth 210mm", lys);
        Assert.Contains("paperHeight 294mm", lys);
        Assert.DoesNotContain("Margin", lys);   // absent keys keep the block's defaults
        Assert.False(report.HasWarnings, string.Join("; ", report.Warnings));
        Assert.False(HasErrors(SyntaxTree.Parse(lys)), lys);
    }

    [Fact]
    public void DifferingEvenMargins_UseTheOddSetAndWarn()
    {
        var (lys, report) = Import(Score(Scaling20Pt + """
            <page-layout>
              <page-margins type="odd">
                <left-margin>80</left-margin><right-margin>80</right-margin>
                <top-margin>60</top-margin><bottom-margin>60</bottom-margin>
              </page-margins>
              <page-margins type="even">
                <left-margin>40</left-margin><right-margin>120</right-margin>
                <top-margin>60</top-margin><bottom-margin>60</bottom-margin>
              </page-margins>
            </page-layout>
            """));
        Assert.Contains("leftMargin 14mm", lys);   // the odd 80, not the even 40
        Assert.Contains("rightMargin 14mm", lys);
        Assert.Contains(report.Warnings, w => w.Contains("even-page"));
    }

    [Fact]
    public void MatchingEvenMargins_AreSilent()
    {
        const string margins = """
            <left-margin>80</left-margin><right-margin>80</right-margin>
            <top-margin>60</top-margin><bottom-margin>60</bottom-margin>
            """;
        var (lys, report) = Import(Score(Scaling20Pt
            + $"""
            <page-layout>
              <page-margins type="odd">{margins}</page-margins>
              <page-margins type="even">{margins}</page-margins>
            </page-layout>
            """));
        Assert.Contains("leftMargin 14mm", lys);
        Assert.False(report.HasWarnings, string.Join("; ", report.Warnings));
    }

    [Fact]
    public void OddOnlyMarginsWithUnequalSides_WarnAboutTheMirroredEvenPages()
    {
        // A lone type="odd" set means even pages MIRROR it. Equal sides make the
        // mirror the identity (silent); unequal sides cannot be represented.
        var (lys, report) = Import(Score(Scaling20Pt + """
            <page-layout>
              <page-margins type="odd">
                <left-margin>80</left-margin><right-margin>40</right-margin>
              </page-margins>
            </page-layout>
            """));
        Assert.Contains("leftMargin 14mm", lys);
        Assert.Contains("rightMargin 7mm", lys);
        Assert.Contains(report.Warnings, w => w.Contains("odd pages only"));
    }

    [Fact]
    public void ANonTwentyPointStaff_IsSaidOutLoud_AndThePageIsKept()
    {
        // 8.4667mm per 40 tenths = a 24pt staff. Lily#'s staff is not a knob, so the
        // page comes across as stated and the size difference is reported. The control
        // is every other test here: 7mm per 40 tenths is 19.84 DTP points, nominally
        // the same 20pt staff Lily# engraves (the 0.4% is TeX-vs-DTP point spelling),
        // and stays silent.
        var (lys, report) = Import(Score("""
            <scaling><millimeters>8.4667</millimeters><tenths>40</tenths></scaling>
            <page-layout><page-height>1403</page-height><page-width>992</page-width></page-layout>
            """));
        Assert.Contains("paperWidth 209.97mm", lys);
        Assert.Contains(report.Warnings, w => w.Contains("24pt"));
    }

    [Fact]
    public void ImportedPaperBlock_ReadsBackAsThePageItStates()
    {
        // End to end through the engine's own reader: 1680 x 1200 tenths at 0.175mm
        // = 294mm x 210mm. The width lands ON the a4 default (210mm = 119.501575,
        // PaperBlockTests' "a book that states a default IS the default"), and the
        // height lands OFF it — the moved value proves the block was read, not skipped.
        var (lys, _) = Import(Score(Scaling20Pt + """
            <page-layout><page-height>1680</page-height><page-width>1200</page-width></page-layout>
            """));
        var tree = SyntaxTree.Parse(lys);
        var paper = tree.GetRoot().DescendantNodes()
            .OfType<PaperDeclarationSyntax>().Single();
        var options = PaperPlanReader.Read(paper, out var problems);
        Assert.Empty(problems);
        Assert.Equal(119.501575, options.PageWidth);    // 210mm, the default, exactly
        Assert.Equal(167.302205, options.PageHeight);   // 294mm x 72.27/127, 6 decimals
    }
}
