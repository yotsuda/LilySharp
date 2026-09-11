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
using LilySharp.Core.LilyPond;
using LilySharp.Core.Parser;
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
/// <c>layout { marks stacked | beside }</c> — the display switch that arranges a boxed
/// section label and the tempo mark standing at the same bar (user decision 2026-09-02,
/// HANDOFF §3; built 2026-09-09 as a bare directive, moved into the <c>layout</c> block
/// 2026-09-11). The words, their two tiers, and the geometry each produces.
/// </summary>
/// <remarks>
/// <para>
/// The stacked default is LilyPond's and is what every book on disk printed before the
/// option existed, so the first net is BYTE IDENTITY: writing <c>marks stacked</c> changes
/// nothing, and writing nothing is the same page. The beside arrangement is Lily#-own
/// (LilyPond has no chart pair), so its net is the arrangement's own geometry — the label
/// at the line-start edge, the tempo's ink left one gap past the box, its baseline on the
/// label text's — read from the layout through the same helpers the engraver places by.
/// </para>
/// <para>
/// The book is the F2 positive control (<c>scratch/p321/fx/fx4-mark-tempo.lys</c>, session
/// 324): a key signature AND a tempo, which is exactly the shape where the stacked and the
/// beside arrangements differ in both X (key right against indent + 0.3) and Y (two
/// lines against one). The block's own reading — keys, refusals, the name layer, the
/// <c>barNumbers</c> key — is <see cref="LayoutBlockTests"/>.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class MarkArrangementTests
{
    private static readonly SvgRenderOptions Opt = new() { EmbedFont = false };

    private const string Beside = "layout { marks beside }\n";
    private const string Stacked = "layout { marks stacked }\n";

    /// <summary>fx4: bass, D major, 4/4, tempo 117, one labelled section.</summary>
    private static string Book(string top = "", string scoreItems = "")
        => top + """
        octave absolute
        tempo 117
        key d major
        time 4/4
        part bl { clef bass }
        section Intro { bl { r1 | r1 | } }
        form main { Intro }
        score main {
        """ + scoreItems + " staff bl }\n";

    private static (MultiStaffScore Score, ScoreLayout Layout) Lay(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join(" | ", tree.Diagnostics.Select(d => d.Message)));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        return (score, new LayoutEngine(score.Paper).Layout(score));
    }

    private static (MusicMarkLayout Label, MusicMarkLayout Tempo) Pair(ScoreLayout layout)
    {
        var label = Assert.Single(layout.MusicMarkLayouts, m => m.MarkType == MusicMarkType.SectionLabel);
        var tempo = Assert.Single(layout.MusicMarkLayouts, m => m.MarkType == MusicMarkType.Tempo);
        return (label, tempo);
    }

    // ---------------------------------------------------------------- the words

    [Fact]
    public void TheTwoWords_AreTheCompilers_TheDefaultFirst()
    {
        Assert.Equal(new[] { "stacked", "beside" }, MarkArrangement.Modes);
        Assert.Equal(MarkArrangement.Modes, LanguageVocabulary.MarkArrangements);
        // The block's word is reserved; the key and its values are the block's own words.
        Assert.NotEqual(SyntaxKind.Identifier, new Lexer("layout").ScanAllTokens().First().Kind);
        Assert.Equal(SyntaxKind.Identifier, new Lexer("marks").ScanAllTokens().First().Kind);
    }

    [Theory]
    [InlineData(Beside, "", true)]
    [InlineData(Stacked, "", false)]
    // A named block, referenced by the score.
    [InlineData("layout chart { marks beside }\n", "layout chart", true)]
    [InlineData("layout chart { marks stacked }\n", "layout chart", false)]
    // The score's reference REPLACES the file's unnamed default, in both directions…
    [InlineData(Beside + "layout chart { marks stacked }\n", "layout chart", false)]
    [InlineData(Stacked + "layout chart { marks beside }\n", "layout chart", true)]
    // …and replaces it whole: a referenced block that says nothing about marks is the
    // stacked default, not the file's beside (no hidden three-layer chain).
    [InlineData(Beside + "layout chart { barNumbers none }\n", "layout chart", false)]
    // An override block on the reference reads as if written at the named block's end.
    [InlineData("layout chart { marks stacked }\n", "layout chart { marks beside }", true)]
    // Nothing written: the stacked default.
    [InlineData("", "", false)]
    public void BothTiers_ReadIntoTheScore_AndTheScoresReferenceWins(string top, string item, bool beside)
    {
        var tree = SyntaxTree.Parse(Book(top, item));
        Assert.False(tree.HasErrors, string.Join(" | ", tree.Diagnostics.Select(d => d.Message)));
        Assert.Empty(SemanticValidation.Run(tree).Where(d => d.Severity == DiagnosticSeverity.Error));

        var spec = RenderSpecParser.FindFirst(tree)!;
        Assert.Equal(item.Length > 0, spec.LayoutRef != null);

        var score = SvgGenerator.CollectScore(tree, spec);
        Assert.Equal(beside, score.MarksBeside);
        Assert.Equal(beside, score.LayoutPlan.MarksBeside);
        // The twin's reader resolves to the same plan the page collected.
        var render = tree.GetRoot().DescendantNodes().OfType<RenderDeclarationSyntax>().First();
        Assert.Equal(score.LayoutPlan, LayoutPlanReader.Resolve(tree.GetRoot(), render));
    }

    [Fact]
    public void AThirdWord_IsRefusedAtTheWord_AndNamesTheTwo()
    {
        string src = Book("layout { marks sideways }\n");
        var tree = SyntaxTree.Parse(src);
        Assert.False(tree.HasErrors, "the parser keeps the word; the reader refuses it");
        var d = Assert.Single(SemanticValidation.Run(tree), x => x.Severity == DiagnosticSeverity.Error);
        Assert.Equal(DiagnosticCodes.LayoutEntryBadValue, d.Code);
        Assert.Contains("sideways", d.Message, StringComparison.Ordinal);
        Assert.Contains("stacked or beside", d.Message, StringComparison.Ordinal);
        // The error stands ON the word (the doc machine reads refusal off the span).
        int at = src.IndexOf("sideways", StringComparison.Ordinal);
        Assert.True(d.Span.Start <= at && at < d.Span.Start + d.Span.Length,
            $"the error should stand on the word at {at}, not at {d.Span.Start}");
        // And the file still reads as the stacked default: an unknown word binds nothing.
        Assert.False(LayoutPlanReader.Resolve(tree.GetRoot(), null).MarksBeside);
    }

    [Fact]
    public void ASecondTopLevelLayout_WarnsLikeEveryRepeatedGlobal()
    {
        var tree = SyntaxTree.Parse(Book(Stacked + Beside));
        var warn = SemanticValidation.Run(tree)
            .Where(d => d.Code == DiagnosticCodes.DuplicateGlobalSetting).ToList();
        Assert.Single(warn);
        Assert.Contains("'layout'", warn[0].Message, StringComparison.Ordinal);
        // The LAST wins, like every repeated global — on the page and in the twin's reader.
        Assert.True(SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree)).MarksBeside);
        Assert.True(LayoutPlanReader.Resolve(tree.GetRoot(), null).MarksBeside);
    }

    // ---------------------------------------------------------------- the page

    [Fact]
    public void WritingStacked_IsTheSamePageAsWritingNothing()
    {
        string bare = SvgGenerator.Generate(SyntaxTree.Parse(Book()), Opt);
        string top = SvgGenerator.Generate(SyntaxTree.Parse(Book(Stacked)), Opt);
        string item = SvgGenerator.Generate(
            SyntaxTree.Parse(Book("layout s { marks stacked }\n", "layout s")), Opt);
        // data-pos moves with the added text; the ink must not.
        Assert.Equal(MaskDataPos(bare), MaskDataPos(top));
        Assert.Equal(MaskDataPos(bare), MaskDataPos(item));
    }

    [Fact]
    public void Beside_PutsTheLabelAtTheLineStartEdge_AndTheTempoOnItsBaselineToItsRight()
    {
        var (score, layout) = Lay(Book(Beside));
        var fonts = score.TextMetrics;
        var (label, tempo) = Pair(layout);
        var item = new MusicMarkItem(MusicMarkType.SectionLabel, label.Text, label.MeasureIndex, 0);
        double halfW = MusicMarkEngraver.LabelBoxHalfWidth(fonts, MusicMarkType.SectionLabel, label.Text);

        // The label's LEFT edge is the line-start edge (indent + 0.3) — the placement
        // session 324 retired from the default and this option brings back.
        double indent = layout.Systems[0].Indent;
        Assert.Equal(indent + 0.3 + halfW, label.X, 9);

        // The tempo's ink left is one gap past the box, its baseline on the label text's.
        Assert.Equal(MusicMarkEngraver.BesideTempoX(fonts, item, label.X), tempo.X, 9);
        Assert.Equal(label.X + halfW + MusicMarkEngraver.BesideTempoGap, tempo.X, 9);
        Assert.Equal(MusicMarkEngraver.BesideTempoBaselineUp(fonts, item, label.YUp), tempo.YUp, 9);
        Assert.True(tempo.YUp < label.YUp, "the digits stand on the label's baseline, below its centre");

        // The pair is one union for the outside-staff pass: the tempo names its label.
        Assert.Equal(label.SourceIndex, tempo.BesideOfSourceIndex);
        Assert.Equal(-1, label.BesideOfSourceIndex);
    }

    [Fact]
    public void Stacked_KeepsLilyPondsAnchors_WhichAreNotTheBesideOnes()
    {
        var (score, stacked) = Lay(Book());
        var (_, beside) = Lay(Book(Beside));
        var fonts = score.TextMetrics;
        var (sLabel, sTempo) = Pair(stacked);
        var (bLabel, bTempo) = Pair(beside);

        // Stacked: the label's left edge is the KEY's right (F2, session 324), which on a
        // D major book stands right of the line-start edge the beside arm uses.
        Assert.True(sLabel.X > bLabel.X + 1.0,
            $"stacked label {sLabel.X:F3} should stand right of the beside label {bLabel.X:F3}");
        // Stacked: the tempo self-aligns on the METER column, not on the label's box, and
        // the two are on different lines (label over tempo) rather than one.
        double halfW = MusicMarkEngraver.LabelBoxHalfWidth(fonts, MusicMarkType.SectionLabel, sLabel.Text);
        Assert.NotEqual(sLabel.X + halfW + MusicMarkEngraver.BesideTempoGap, sTempo.X, 6);
        Assert.Equal(-1, sTempo.BesideOfSourceIndex);
        Assert.True(sLabel.YUp > sTempo.YUp + 1.0,
            $"stacked: the label ({sLabel.YUp:F3}) stacks over the tempo ({sTempo.YUp:F3})");
        // Beside: one line — the pair's top is the tempo's note, the label's centre is lower
        // than the stacked label's.
        Assert.True(bLabel.YUp < sLabel.YUp,
            $"beside label {bLabel.YUp:F3} should sit lower than the stacked label {sLabel.YUp:F3}");
    }

    [Fact]
    public void AMidMeasureTempo_KeepsItsNoteColumn_UnderBeside()
    {
        // A tempo change inside the bar is anchored to its note under either arrangement
        // (CalculateXPosition's note-column arm); only a measure-start tempo pairs.
        string book = """
        layout { marks beside }
        octave absolute
        key d major
        time 4/4
        part bl { clef bass }
        section Intro { bl { d4 tempo 90 e f g | a1 | } }
        form main { Intro }
        score main { staff bl }
        """;
        var (_, layout) = Lay(book);
        var label = Assert.Single(layout.MusicMarkLayouts, m => m.MarkType == MusicMarkType.SectionLabel);
        var tempo = Assert.Single(layout.MusicMarkLayouts, m => m.MarkType == MusicMarkType.Tempo);
        Assert.Equal(-1, tempo.BesideOfSourceIndex);
        Assert.True(tempo.X > label.X + 4.0,
            $"a mid-measure tempo ({tempo.X:F3}) stays over its note, not beside the label ({label.X:F3})");
    }

    [Fact]
    public void ANoteUnderEitherMember_LiftsThePairTogether()
    {
        // High ink under the TEMPO's column only (the label's column holds a rest): the
        // outside-staff pass must lift both, keeping the baseline offset, or the label
        // would stay low with the tempo floating off its line.
        string quiet = Book(Beside);
        string high = quiet.Replace("bl { r1 | r1 | }", "bl { r4 g'2. | r1 | }");
        var (score, qLay) = Lay(quiet);
        var (_, hLay) = Lay(high);
        var fonts = score.TextMetrics;
        var (qLabel, qTempo) = Pair(qLay);
        var (hLabel, hTempo) = Pair(hLay);
        var item = new MusicMarkItem(MusicMarkType.SectionLabel, hLabel.Text, hLabel.MeasureIndex, 0);

        Assert.True(hTempo.YUp > qTempo.YUp + 0.5,
            $"the high note should lift the tempo ({qTempo.YUp:F3} -> {hTempo.YUp:F3})");
        Assert.Equal(hLabel.YUp - qLabel.YUp, hTempo.YUp - qTempo.YUp, 9);
        Assert.Equal(MusicMarkEngraver.BesideTempoBaselineUp(fonts, item, hLabel.YUp), hTempo.YUp, 9);
    }

    // ---------------------------------------------------------------- the readers

    [Fact]
    public void TheTwin_WarnsThatBesideHasNoLilyPondSpelling_AndStaysQuietForStacked()
    {
        var beside = new LilyPondExporter();
        beside.Export(SyntaxTree.Parse(Book(Beside)));
        Assert.Contains(beside.Warnings, w => w.Contains("marks beside", StringComparison.Ordinal));

        var perScore = new LilyPondExporter();
        perScore.Export(SyntaxTree.Parse(Book("layout chart { marks beside }\n", "layout chart")));
        Assert.Contains(perScore.Warnings, w => w.Contains("marks beside", StringComparison.Ordinal));

        var stacked = new LilyPondExporter();
        string ly = stacked.Export(SyntaxTree.Parse(Book(Stacked)));
        Assert.DoesNotContain(stacked.Warnings, w => w.Contains("marks", StringComparison.Ordinal));
        // ...and the twin's text is the same twin: the option is a display option.
        Assert.Equal(new LilyPondExporter().Export(SyntaxTree.Parse(Book())), ly);
    }

    [Fact]
    public void AnArrangementEdit_RendersIdenticalToFullRecompile_BothWays()
    {
        // The arrangement lives outside every per-measure reuse key (a label's X and the
        // tempo's line move, no measure's content does), so the session sheds its caches
        // when it changes — the paper guard's shape (PaperEditIncrementalTests).
        string stacked = Book();
        string beside = Book(Beside);
        var compiler = new IncrementalCompiler(SyntaxTree.Parse(stacked), Opt);
        compiler.Render();
        foreach (var (text, label) in new[] { (beside, "to beside"), (stacked, "back to stacked") })
        {
            string incremental = compiler.RenderIncremental(SyntaxTree.Parse(text));
            string full = SvgGenerator.Generate(SyntaxTree.Parse(text), Opt);
            Assert.True(full == incremental, $"{label}: incremental != full");
        }
    }

    private static string MaskDataPos(string svg)
        => System.Text.RegularExpressions.Regex.Replace(svg, @"data-pos=""\d+""", "data-pos=\"\"");
}
