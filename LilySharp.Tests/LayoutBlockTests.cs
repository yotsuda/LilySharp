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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The <c>layout { }</c> block — the score-wide display switches, the third block of the
/// <c>fonts</c> / <c>paper</c> shape (user decision 2026-09-11): its reading, its
/// refusals, its name layer, and the <c>barNumbers</c> key on the page and in the twin.
/// The <c>marks</c> key's geometry is <see cref="MarkArrangementTests"/>.
/// </summary>
/// <remarks>
/// The bar-number policy is LilyPond's vocabulary ported: <c>lines</c> is the default
/// visibility function kept to line starts by the grob's break-visibility, <c>none</c> is
/// <c>\remove Bar_number_engraver</c>, <c>every N</c> is <c>every-nth-bar-number-visible</c>
/// with break-visibility opened — so under <c>every 2</c> a line-start bar 3 carries NO
/// number, which is what LilyPond prints and the first thing a reader would "fix".
/// </remarks>
[Trait("Category", "Unit")]
public class LayoutBlockTests
{
    private static readonly SvgRenderOptions Opt = new() { EmbedFont = false };

    private const string Body =
        "part m { clef treble }\nsection A { m { c4 d e f | } }\nform main { A }\nscore main { staff m }\n";

    private static LayoutPlan Read(string block, out LayoutPlanReader.Problem[] problems)
    {
        var tree = SyntaxTree.Parse(block + "\n" + Body);
        Assert.False(tree.HasErrors, string.Join(" | ", tree.Diagnostics.Select(d => d.Message)));
        var layout = tree.GetRoot().DescendantNodes().OfType<LayoutDeclarationSyntax>().Single();
        var plan = LayoutPlanReader.Read(layout, out var found);
        problems = [.. found];
        return plan;
    }

    private static LayoutPlan ReadClean(string block)
    {
        var plan = Read(block, out var problems);
        Assert.Empty(problems);
        return plan;
    }

    private static Diagnostic[] All(string source)
    {
        var tree = SyntaxTree.Parse(source);
        return [.. tree.Diagnostics.Concat(SemanticValidation.Run(tree))];
    }

    // ================================================================================
    // Reading
    // ================================================================================

    [Fact]
    public void StatingTheDefaults_IsTheDefault()
    {
        // A book that writes the defaults out lays out exactly as one that writes nothing.
        Assert.Equal(LayoutPlan.Default, ReadClean("layout { marks stacked  barNumbers lines }"));
        Assert.Equal(LayoutPlan.Default, ReadClean("layout { }"));
        Assert.False(LayoutPlan.Default.MarksBeside);
        Assert.Equal(BarNumberMode.Lines, LayoutPlan.Default.BarNumbers.Mode);
    }

    [Fact]
    public void EachKeyBindsItsOwnSwitch_AndLeavesTheOther()
    {
        var beside = ReadClean("layout { marks beside }");
        Assert.True(beside.MarksBeside);
        Assert.Equal(BarNumberPolicy.Lines, beside.BarNumbers);

        var none = ReadClean("layout { barNumbers none }");
        Assert.False(none.MarksBeside);
        Assert.Equal(BarNumberPolicy.None, none.BarNumbers);

        var every = ReadClean("layout { barNumbers every 4  marks beside }");
        Assert.True(every.MarksBeside);
        Assert.Equal(BarNumberPolicy.Every(4), every.BarNumbers);
        Assert.Equal(4, every.BarNumbers.Period);
    }

    [Fact]
    public void KeysAreCaseInsensitive_LikeAPaperKey_AndWordsAreNot()
    {
        Assert.True(ReadClean("layout { MARKS beside }").MarksBeside);
        Assert.Equal(BarNumberPolicy.None, ReadClean("layout { barnumbers none }").BarNumbers);
        // The value words are the language's closed vocabulary: canonical case only.
        Read("layout { marks Beside }", out var problems);
        var p = Assert.Single(problems);
        Assert.Equal(DiagnosticCodes.LayoutEntryBadValue, p.Code);
    }

    [Fact]
    public void TheVocabularyIsPublished_AndTheReaderReadsIt()
    {
        Assert.Equal(new[] { "marks", "barNumbers" }, LanguageVocabulary.LayoutKeys);
        Assert.Equal(LanguageVocabulary.LayoutKeys, LayoutPlanReader.AllKeySpellings());
        Assert.Equal(new[] { "lines", "none", "every" }, LanguageVocabulary.BarNumberPolicies);
        Assert.Equal(LanguageVocabulary.MarkArrangements, LayoutPlanReader.ValueWords("marks"));
        Assert.Equal(LanguageVocabulary.BarNumberPolicies, LayoutPlanReader.ValueWords("barNumbers"));
    }

    // ================================================================================
    // Refusals — each on its own span, the rest of the block still binding
    // ================================================================================

    [Fact]
    public void AnUnknownKeyIsAnError_AndTheRestStillBinds()
    {
        var plan = Read("layout { bogus 3  marks beside }", out var problems);
        var problem = Assert.Single(problems);
        Assert.Equal(DiagnosticCodes.UnknownLayoutKey, problem.Code);
        Assert.True(problem.IsError);
        Assert.Contains("barNumbers", problem.Message, StringComparison.Ordinal); // names the vocabulary
        Assert.True(plan.MarksBeside);
    }

    [Fact]
    public void ATypoInTheKey_IsRefusedAsTheKey_NotAsAValue()
    {
        // `mark beside`: the unknown word opens the entry (nothing else could), and its
        // would-be value rides along — one error, on the key.
        Read("layout { mark beside }", out var problems);
        var problem = Assert.Single(problems);
        Assert.Equal(DiagnosticCodes.UnknownLayoutKey, problem.Code);
        Assert.Contains("'mark'", problem.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("layout { marks }", "marks")]                    // no word at all
    [InlineData("layout { marks sideways }", "sideways")]        // not one of the two
    [InlineData("layout { marks beside stacked }", "stacked")]   // one word, not two
    [InlineData("layout { barNumbers }", "barNumbers")]
    [InlineData("layout { barNumbers weekly }", "weekly")]
    [InlineData("layout { barNumbers every }", "every")]         // no count
    [InlineData("layout { barNumbers every 0 }", "0")]           // below 1
    [InlineData("layout { barNumbers every x }", "x")]           // not a number
    [InlineData("layout { barNumbers lines 3 }", "3")]           // a count on a word that takes none
    [InlineData("layout { barNumbers every 4 5 }", "5")]         // a second count
    public void ABadValue_IsAnErrorOnTheOffendingToken(string block, string at)
    {
        var plan = Read(block, out var problems);
        var problem = Assert.Single(problems);
        Assert.Equal(DiagnosticCodes.LayoutEntryBadValue, problem.Code);
        Assert.True(problem.IsError);
        string src = block + "\n" + Body;
        int expected = src.IndexOf(at, "layout {".Length, StringComparison.Ordinal);
        Assert.True(problem.Span.Start <= expected && expected < problem.Span.Start + problem.Span.Length,
            $"the error should stand on '{at}' at {expected}, not at {problem.Span.Start}: {problem.Message}");
        Assert.Equal(LayoutPlan.Default, plan); // the entry bound nothing
    }

    [Fact]
    public void AKeySetTwice_Warns_AndTheLastWins()
    {
        var plan = Read("layout { marks beside  marks stacked }", out var problems);
        var problem = Assert.Single(problems);
        Assert.Equal(DiagnosticCodes.DuplicateLayoutKey, problem.Code);
        Assert.False(problem.IsError);
        Assert.False(plan.MarksBeside);
    }

    [Fact]
    public void ANestedBrace_IsRefusedWhereItStands_AndTheBlockStillCloses()
    {
        var diags = All("layout { marks { beside } }\n" + Body);
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.LayoutEntryBadValue);
        // The rest of the file parses: the score is still there.
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCodes.StrayItemToken);
        Assert.Single(SyntaxTree.Parse("layout { marks { beside } }\n" + Body)
            .GetRoot().DescendantNodes().OfType<RenderDeclarationSyntax>());
    }

    [Theory]
    // A named declaration takes a block. ⚠️ A bare `layout` on its own line reads the NEXT
    // word as the name, so this is also what `layout` followed by a declaration reports —
    // the paper directive behaves the same way, and the message names the block to write.
    [InlineData("layout beside\n", "LYS9108")]
    // A value where a block belongs: no name to take, so the blockless message stands.
    [InlineData("layout 7\n", "LYS9104")]
    public void TheBlocklessForms_AreRefusedByTheParser(string top, string code)
    {
        var diags = All(top + Body);
        Assert.Contains(diags, d => d.Code == code && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void TheNameLayer_MirrorsThePaperOne()
    {
        static string Doc(string top, string item) =>
            top + "part m { clef treble }\nsection A { m { c4 d e f | } }\nform main { A }\n"
            + "score main { " + item + " staff m }\n";

        // An unnamed block inside a score: the reference form is what belongs there.
        Assert.Contains(All(Doc("", "layout { marks beside }")),
            d => d.Code == DiagnosticCodes.ScoreLayoutNeedsAName);
        // A reference to nothing.
        Assert.Contains(All(Doc("", "layout chart")),
            d => d.Code == DiagnosticCodes.UnknownLayoutBlockName && d.Message.Contains("'chart'", StringComparison.Ordinal));
        // Two declarations, one name.
        Assert.Contains(All(Doc("layout a { marks beside }\nlayout a { marks stacked }\n", "layout a")),
            d => d.Code == DiagnosticCodes.DuplicateLayoutBlockName);
        // A declaration nobody references warns.
        Assert.Contains(All(Doc("layout a { marks beside }\n", "")),
            d => d.Code == DiagnosticCodes.UnreferencedNamedLayout && d.Severity == DiagnosticSeverity.Warning);
        // Two references in one score: the earlier one is named, the last wins.
        var diags = All(Doc("layout a { marks beside }\nlayout b { marks stacked }\n", "layout a  layout b"));
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.DuplicateLayoutReference);
        var tree = SyntaxTree.Parse(Doc("layout a { marks beside }\nlayout b { marks stacked }\n", "layout a  layout b"));
        Assert.False(SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree)).MarksBeside);
        // The clean shape: no diagnostic at all.
        Assert.Empty(All(Doc("layout a { marks beside  barNumbers every 2 }\n", "layout a { barNumbers none }")));
    }

    // ================================================================================
    // barNumbers on the page
    // ================================================================================

    /// <summary>Eight whole-note bars, a forced break every two: four systems of two bars.</summary>
    private static string EightBars(string top) =>
        top + "part m { clef treble }\n"
        + "section A { m { c1 | c1 | break c1 | c1 | break c1 | c1 | break c1 | c1 | } }\n"
        + "form main { A }\nscore main { staff m }\n";

    private static ScoreLayout Lay(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join(" | ", tree.Diagnostics.Select(d => d.Message)));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        return new LayoutEngine(score.Paper).Layout(score);
    }

    [Fact]
    public void Lines_NumbersTheFirstBarOfEveryLineAfterTheFirst()
    {
        var layout = Lay(EightBars(""));
        Assert.Equal(4, layout.Systems.Length);
        Assert.Equal(new[] { "3", "5", "7" }, layout.BarNumberLayouts.Select(b => b.Text));
        // Writing the default is the same page.
        Assert.Equal(layout.BarNumberLayouts.Select(b => (b.Text, b.X, b.YUp)),
            Lay(EightBars("layout { barNumbers lines }\n")).BarNumberLayouts.Select(b => (b.Text, b.X, b.YUp)));
    }

    [Fact]
    public void None_NumbersNothing()
    {
        Assert.Empty(Lay(EightBars("layout { barNumbers none }\n")).BarNumberLayouts);
    }

    [Fact]
    public void EveryN_NumbersTheMultiples_WhereverTheyStand_AndNothingElse()
    {
        // every 2: bars 2, 4, 6, 8 — each the SECOND bar of its line — and NOT the
        // line-start bars 3, 5, 7 (LilyPond's every-nth-bar-number-visible answers the
        // visibility question first; a line start that is not a multiple gets no grob).
        var every2 = Lay(EightBars("layout { barNumbers every 2 }\n"));
        Assert.Equal(new[] { "2", "4", "6", "8" }, every2.BarNumberLayouts.Select(b => b.Text));
        Assert.All(every2.BarNumberLayouts, b => Assert.False(b.RightAligned, "a mid-line number left-aligns"));

        // every 3: 3 and 6 — one at a line start (right-aligned into the margin), one mid-line.
        var every3 = Lay(EightBars("layout { barNumbers every 3 }\n"));
        Assert.Equal(new[] { "3", "6" }, every3.BarNumberLayouts.Select(b => b.Text));
        Assert.True(every3.BarNumberLayouts[0].RightAligned);
        Assert.False(every3.BarNumberLayouts[1].RightAligned);

        // every 1: every bar, the first included (1 is a multiple of 1).
        Assert.Equal(Enumerable.Range(1, 8).Select(n => n.ToString()),
            Lay(EightBars("layout { barNumbers every 1 }\n")).BarNumberLayouts.Select(b => b.Text));
    }

    [Fact]
    public void EveryN_CountsTheDisplayedNumber_NotTheMeasureIndex()
    {
        // A pickup is bar 0 (numberOffset −1): under every 2 the numbered bars are the even
        // DISPLAYED numbers, so the third measure (displayed 2) is numbered and the second
        // (displayed 1) is not.
        // ⚠️ AND THE PICKUP ITSELF IS, because 0 is a multiple of 2 — LilyPond's function is
        // `(= 0 (modulo barnum n))` with no guard on the first bar (the guard belongs to the
        // DEFAULT function, first-bar-number-invisible-…, which every-nth replaces). The
        // literal port prints the 0; suppressing it would be a Lily#-own rule.
        string book = "part m { clef treble }\n"
            + "section A { partial 4  m { c4 | c1 | c1 | c1 | } }\n"
            + "form main { A }\nscore main { staff m }\n";
        var layout = Lay("layout { barNumbers every 2 }\n" + book);
        var numbered = layout.BarNumberLayouts.Select(b => (b.MeasureIndex, b.Text)).ToArray();
        Assert.Equal(new[] { (0, "0"), (2, "2") }, numbered);
    }

    [Fact]
    public void ABarNumberEdit_RendersIdenticalToFullRecompile()
    {
        // The policy lives outside every per-measure reuse key (which bars carry a number
        // is not a measure's content), so the session sheds its caches when it changes —
        // the paper guard's shape (PaperEditIncrementalTests).
        string lines = EightBars("");
        string every = EightBars("layout { barNumbers every 2 }\n");
        var compiler = new IncrementalCompiler(SyntaxTree.Parse(lines), Opt);
        compiler.Render();
        foreach (var (text, label) in new[] { (every, "to every 2"), (lines, "back to lines") })
        {
            string incremental = compiler.RenderIncremental(SyntaxTree.Parse(text));
            string full = SvgGenerator.Generate(SyntaxTree.Parse(text), Opt);
            Assert.True(full == incremental, $"{label}: incremental != full");
        }
    }

    // ================================================================================
    // The twin
    // ================================================================================

    [Fact]
    public void TheTwin_SpellsThePolicyInLilyPondsWords_AndNothingForTheDefault()
    {
        static (string Ly, IReadOnlyList<string> Warnings) Twin(string top)
        {
            var exporter = new LilyPondExporter();
            string ly = exporter.Export(SyntaxTree.Parse(EightBars(top)));
            return (ly, exporter.Warnings);
        }

        var (lines, _) = Twin("");
        Assert.DoesNotContain("Bar_number_engraver", lines, StringComparison.Ordinal);
        Assert.DoesNotContain("barNumberVisibility", lines, StringComparison.Ordinal);
        // Writing the default is the same twin.
        Assert.Equal(lines, Twin("layout { barNumbers lines }\n").Ly);

        var (none, noneWarnings) = Twin("layout { barNumbers none }\n");
        Assert.Contains("\\remove Bar_number_engraver", none, StringComparison.Ordinal);
        Assert.DoesNotContain(noneWarnings, w => w.Contains("layout", StringComparison.Ordinal));

        var (every, _) = Twin("layout { barNumbers every 4 }\n");
        Assert.Contains("barNumberVisibility = #(every-nth-bar-number-visible 4)", every, StringComparison.Ordinal);
        Assert.Contains("\\override BarNumber.break-visibility = #end-of-line-invisible", every, StringComparison.Ordinal);
        // …inside the \Score context of the \layout block, where the fonts overrides go.
        int at = every.IndexOf("barNumberVisibility", StringComparison.Ordinal);
        int score = every.LastIndexOf("\\Score", at, StringComparison.Ordinal);
        int layout = every.LastIndexOf("\\layout", at, StringComparison.Ordinal);
        Assert.True(layout >= 0 && score > layout, "the policy lines stand in \\layout { \\context { \\Score … } }");
    }
}
