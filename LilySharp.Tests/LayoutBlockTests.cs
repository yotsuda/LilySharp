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
using LilySharp.Core.Music;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using LilySharp.Lsp;
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

    /// <summary>
    /// Every key the language PUBLISHES is one the reader actually binds, and one the
    /// editor serves its own words for.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE CHECK BESIDE THIS ONE CANNOT SEE THAT.
    /// <see cref="TheVocabularyIsPublished_AndTheReaderReadsIt"/> compares the vocabulary
    /// with <c>LayoutPlanReader.AllKeySpellings()</c>, which RETURNS that same vocabulary —
    /// it is an identity, and it stays green for a key that has no arm in
    /// <c>ReadEntriesInto</c>'s switch. Such a key parses, passes the unknown-key test,
    /// falls to <c>_ =&gt; plan</c> and binds nothing, in silence: "a switch nobody reads
    /// looks exactly like one that works", which is the sentence the unknown-key ERROR
    /// exists for, one level in.
    /// <para>
    /// The question is asked of BEHAVIOUR rather than of a list: for each published key,
    /// at least one of its own words must MOVE the plan away from the default. Derived,
    /// not recalled (RULES §5.0) — a key added to the vocabulary is asked the same
    /// question the day it appears.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryPublishedKey_IsBoundByTheReader_AndServedByTheEditor()
    {
        Assert.NotEmpty(LanguageVocabulary.LayoutKeys);
        foreach (string key in LanguageVocabulary.LayoutKeys)
        {
            var words = LayoutPlanReader.ValueWords(key);
            Assert.True(words.Count > 0,
                $"'{key}' is published as a layout key and the reader offers no words for it");

            // A word may need an argument (`every N`); try the bare form, then the counted
            // one, so the question stays generic over the keys.
            bool moved = false;
            foreach (string word in words)
                foreach (string written in new[] { word, word + " 4" })
                {
                    var plan = Read($"layout {{ {key} {written} }}", out var problems);
                    if (problems.Length == 0 && plan != LayoutPlan.Default)
                        moved = true;
                }
            Assert.True(moved,
                $"'{key}' is published as a layout key and NO value of it changes the plan — "
                + "its arm in LayoutPlanReader.ReadEntriesInto is missing, so the key binds "
                + "nothing and says nothing.");

            // …and the editor serves that key's own words rather than the key list again.
            Assert.NotEqual(LilySharpLanguageServer.CompletionContext.LayoutBlock,
                LilySharpLanguageServer.GetCompletionContext($"layout {{ {key} ", $"layout {{ {key} ".Length));
        }

        // The pre-filled block the top level offers names every key, so accepting it and
        // changing nothing shows the whole switchboard (and still writes the defaults).
        string snippet = LilySharpLanguageServer.GetTopLevelCompletions().Items
            .Single(i => i.Label == "layout").InsertText!;
        foreach (string key in LanguageVocabulary.LayoutKeys)
            Assert.Contains(key, snippet, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCheckThatEveryKeyBinds_CanFail()
    {
        // ★ The ratchet above is today's behaviour written down, so it was green the moment
        // it was written — which says nothing (RULES §5.4). It was POISONED to see it bite
        // (2026-09-11): a `poisonKey` added to SyntaxFacts.LayoutKeyVocabulary with words in
        // LayoutPlanReader.ValueWords and no arm in ReadEntriesInto — exactly the shape a
        // half-added key has — turned it red, naming the key and the missing arm:
        //   'poisonKey' is published as a layout key and NO value of it changes the plan
        // What stays here is the half the test can carry on its own: a key the vocabulary
        // does NOT publish offers no words and binds nothing, so the arithmetic the ratchet
        // runs on is real in both directions.
        Assert.Empty(LayoutPlanReader.ValueWords("bogus"));
        var plan = Read("layout { bogus modern }", out var problems);
        Assert.Equal(LayoutPlan.Default, plan);
        Assert.Contains(problems, p => p.Code == DiagnosticCodes.UnknownLayoutKey);
    }

    [Fact]
    public void TheVocabularyIsPublished_AndTheReaderReadsIt()
    {
        Assert.Equal(
            new[] { "marks", "barNumbers", "accidentals", "sectionLabels", "partCombineText",
                    "chordQualities", "minorChords" },
            LanguageVocabulary.LayoutKeys);
        Assert.Equal(LanguageVocabulary.LayoutKeys, LayoutPlanReader.AllKeySpellings());
        Assert.Equal(new[] { "lines", "none", "every" }, LanguageVocabulary.BarNumberPolicies);
        Assert.Equal(LanguageVocabulary.MarkArrangements, LayoutPlanReader.ValueWords("marks"));
        Assert.Equal(LanguageVocabulary.BarNumberPolicies, LayoutPlanReader.ValueWords("barNumbers"));
        Assert.Equal(LanguageVocabulary.AccidentalStyleWords, LayoutPlanReader.ValueWords("accidentals"));
        Assert.Equal(new[] { "boxed", "plain", "none" }, LanguageVocabulary.SectionLabelStyles);
        Assert.Equal(new[] { "on", "off" }, LanguageVocabulary.PartCombineTextWords);
        Assert.Equal(new[] { "words", "symbols" }, LanguageVocabulary.ChordQualityStyleWords);
        Assert.Equal(new[] { "upper", "lower" }, LanguageVocabulary.MinorChordWords);
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
    // sectionLabels — the name above the staff
    // ================================================================================

    /// <summary>A two-section book whose form names both, so both print a label.</summary>
    private const string TwoSections =
        "part m { clef treble }\n"
        + "section A { m { c1 | } }\nsection B { m { c1 | } }\n"
        + "form main { A B }\nscore main { staff m }\n";

    private static string MaskDataPos(string svg)
        => System.Text.RegularExpressions.Regex.Replace(svg, @"data-pos=""\d+""", "data-pos=\"\"");

    private static string?[] Labels(string top)
    {
        var tree = SyntaxTree.Parse(top + TwoSections);
        Assert.False(tree.HasErrors, string.Join(" | ", tree.Diagnostics.Select(d => d.Message)));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        var layout = new LayoutEngine(score.Paper).Layout(score);
        return [.. layout.MusicMarkLayouts
            .Where(m => m.MarkType == MusicMarkType.SectionLabel)
            .Select(m => (string?)m.Text)];
    }

    [Fact]
    public void SectionLabels_Boxed_IsTheDefault_AndNoneEngravesNothing()
    {
        Assert.Equal(new string?[] { "A", "B" }, Labels(""));
        Assert.Equal(new string?[] { "A", "B" }, Labels("layout { sectionLabels boxed }\n"));
        Assert.Empty(Labels("layout { sectionLabels none }\n"));

        // …and writing the default is the same page, byte for byte.
        Assert.Equal(
            MaskDataPos(SvgGenerator.Generate(SyntaxTree.Parse(TwoSections), Opt)),
            MaskDataPos(SvgGenerator.Generate(
                SyntaxTree.Parse("layout { sectionLabels boxed }\n" + TwoSections), Opt)));
    }

    [Fact]
    public void SectionLabels_None_TakesTheLabelOutOfTheTwinToo_AndLeavesTheMusicAlone()
    {
        // The page engraves no name, so the twin writes none: the two pictures are the same
        // picture or the twin is not one.
        string ly = new LilyPondExporter().Export(
            SyntaxTree.Parse("layout { sectionLabels none }\n" + TwoSections));
        Assert.DoesNotContain("\\mark", ly, StringComparison.Ordinal);
        Assert.Contains("\\mark \\markup \\box \"A\"",
            new LilyPondExporter().Export(SyntaxTree.Parse(TwoSections)), StringComparison.Ordinal);

        // It is a DISPLAY switch: the form still plays both sections, so the bar count holds.
        Assert.Equal(
            Labels("").Length > 0 ? 2 : 0,
            SvgGenerator.CollectScore(SyntaxTree.Parse(TwoSections),
                RenderSpecParser.FindFirst(SyntaxTree.Parse(TwoSections)))
                .StaffGroups[0].Staves[0].Voices[0].Measures.Length);
        var hidden = SyntaxTree.Parse("layout { sectionLabels none }\n" + TwoSections);
        Assert.Equal(2, SvgGenerator.CollectScore(hidden, RenderSpecParser.FindFirst(hidden))
            .StaffGroups[0].Staves[0].Voices[0].Measures.Length);
    }

    /// <summary>The SectionLabel layouts of a book, each with the frame bit it was placed
    /// with — the flag the draw and every reservation read.</summary>
    private static MusicMarkLayout[] LabelLayouts(string top)
    {
        var tree = SyntaxTree.Parse(top + TwoSections);
        Assert.False(tree.HasErrors, string.Join(" | ", tree.Diagnostics.Select(d => d.Message)));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        var layout = new LayoutEngine(score.Paper).Layout(score);
        return [.. layout.MusicMarkLayouts.Where(m => m.MarkType == MusicMarkType.SectionLabel)];
    }

    [Fact]
    public void SectionLabels_Plain_KeepsTheNameAndDropsTheFrame_EverywhereItIsPriced()
    {
        var boxed = LabelLayouts("layout { sectionLabels boxed }\n");
        var plain = LabelLayouts("layout { sectionLabels plain }\n");

        // The names are still engraved — `plain` is the FRAME's switch, not the label's.
        Assert.Equal(new[] { "A", "B" }, plain.Select(m => m.Text));
        Assert.All(boxed, m => Assert.True(m.Boxed));
        Assert.All(plain, m => Assert.False(m.Boxed));

        // ⚠️ The bit reaches the pricing, not just the draw: the label's extent narrows by
        // exactly the frame's two margins, and its height by the same on each side. This is
        // the assertion that fails if a site keeps reading the boxed geometry.
        var fonts = SvgGenerator.CollectScore(
            SyntaxTree.Parse(TwoSections),
            RenderSpecParser.FindFirst(SyntaxTree.Parse(TwoSections))).TextMetrics;
        double margin = MusicMarkEngraver.LabelBoxMargin(fonts, MusicMarkType.SectionLabel, boxed: true);
        Assert.True(margin > 0.0, "the boxed margin should be a real width");
        Assert.Equal(0.0, MusicMarkEngraver.LabelBoxMargin(fonts, MusicMarkType.SectionLabel, boxed: false), 9);
        foreach (string text in new[] { "A", "B" })
        {
            Assert.Equal(
                MusicMarkEngraver.LabelBoxHalfWidth(fonts, MusicMarkType.SectionLabel, text, boxed: true) - margin,
                MusicMarkEngraver.LabelBoxHalfWidth(fonts, MusicMarkType.SectionLabel, text, boxed: false), 9);
            Assert.Equal(
                MusicMarkEngraver.LabelBoxHalfHeight(fonts, MusicMarkType.SectionLabel, text, boxed: true) - margin,
                MusicMarkEngraver.LabelBoxHalfHeight(fonts, MusicMarkType.SectionLabel, text, boxed: false), 9);
            // The baseline keeps its place INSIDE the ink, so the text does not move on the
            // page when the frame goes away — only the frame's own air is gone.
            Assert.Equal(
                MusicMarkEngraver.LabelBaselineBelowCentre(fonts, MusicMarkType.SectionLabel, text, boxed: true),
                MusicMarkEngraver.LabelBaselineBelowCentre(fonts, MusicMarkType.SectionLabel, text, boxed: false), 9);
        }

        // ⚠️ A rehearsal mark is NOT a section label: the key says nothing about it, so its
        // frame survives `plain` — which is also LilyPond's own picture for RehearsalMark.
        Assert.True(MusicMarkEngraver.IsBoxDrawn(MusicMarkType.Rehearsal, SectionLabelStyle.Plain));
        Assert.False(MusicMarkEngraver.IsBoxDrawn(MusicMarkType.SectionLabel, SectionLabelStyle.Plain));
        Assert.True(MusicMarkEngraver.IsBoxDrawn(MusicMarkType.SectionLabel, SectionLabelStyle.Boxed));
    }

    [Fact]
    public void SectionLabels_Plain_DrawsTheNameWithNoRectangle_AndTheTwinDropsTheBox()
    {
        // The page: the two names are still there, the two frames are not. Drawn once —
        // this is the picture, counted rather than described.
        string boxedSvg = SvgGenerator.Generate(
            SyntaxTree.Parse("layout { sectionLabels boxed }\n" + TwoSections), Opt);
        string plainSvg = SvgGenerator.Generate(
            SyntaxTree.Parse("layout { sectionLabels plain }\n" + TwoSections), Opt);
        static int Count(string svg, string tag)
            => System.Text.RegularExpressions.Regex.Matches(svg, "<" + tag + "\\b").Count;
        Assert.Equal(Count(boxedSvg, "text"), Count(plainSvg, "text"));
        Assert.Equal(Count(boxedSvg, "rect") - 2, Count(plainSvg, "rect"));
        Assert.Contains(">A<", plainSvg, StringComparison.Ordinal);

        // The twin writes the same picture: the bare markup, with no `\box`.
        string ly = new LilyPondExporter().Export(
            SyntaxTree.Parse("layout { sectionLabels plain }\n" + TwoSections));
        Assert.Contains("\\mark \\markup \"A\"", ly, StringComparison.Ordinal);
        Assert.DoesNotContain("\\box", ly, StringComparison.Ordinal);
    }

    // ================================================================================
    // partCombineText — the a2 / Solo words
    // ================================================================================

    /// <summary>Two parts on one staff: bar 1 in unison ("a2"), bar 2 only part one
    /// ("Solo").</summary>
    private const string Combined =
        "part one { clef treble }\npart two { clef treble }\n"
        + "section A { one { c'1 | d'1 | } two { c'1 | r1 | } }\n"
        + "form main { A }\nscore main { combinedStaff { one two } }\n";

    private static string[] CombineWords(string top)
    {
        var tree = SyntaxTree.Parse(top + Combined);
        Assert.False(tree.HasErrors, string.Join(" | ", tree.Diagnostics.Select(d => d.Message)));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        var layout = new LayoutEngine(score.Paper).Layout(score);
        return [.. layout.PartCombineLayouts.Select(p => p.Text)];
    }

    [Fact]
    public void PartCombineText_On_IsTheDefault_AndOffPrintsNoWords()
    {
        var words = CombineWords("");
        Assert.NotEmpty(words);
        Assert.Equal(words, CombineWords("layout { partCombineText on }\n"));
        Assert.Empty(CombineWords("layout { partCombineText off }\n"));

        // Writing the default is the same page.
        Assert.Equal(
            MaskDataPos(SvgGenerator.Generate(SyntaxTree.Parse(Combined), Opt)),
            MaskDataPos(SvgGenerator.Generate(
                SyntaxTree.Parse("layout { partCombineText on }\n" + Combined), Opt)));
    }

    [Fact]
    public void PartCombineText_Off_IsLilyPondsOwnPropertyInTheTwin()
    {
        string off = new LilyPondExporter().Export(
            SyntaxTree.Parse("layout { partCombineText off }\n" + Combined));
        Assert.Contains("\\set Staff.printPartCombineTexts = ##f", off, StringComparison.Ordinal);
        Assert.DoesNotContain("printPartCombineTexts",
            new LilyPondExporter().Export(SyntaxTree.Parse(Combined)), StringComparison.Ordinal);
    }

    // ================================================================================
    // chordQualities / minorChords — how a chord symbol is spelled
    // ================================================================================

    /// <summary>A chord row of the four qualities LilyPond names with a symbol, plus a
    /// minor seventh with a slash bass (so the bass's own case is observed).</summary>
    private const string Chords =
        "time 4/4\npart m { clef treble }\n"
        + "section A { m { c4 d e f | c4 d e f | c4 d e f | c4 d e f | c4 d e f | }\n"
        + "  chords prog { Cdim | Caug | Cm7-5 | Cdim7 | Am7/C | } }\n"
        + "form main { ~A }\nscore main { chords prog  staff m }\n";

    private static string[] ChordTexts(string top)
    {
        var tree = SyntaxTree.Parse(top + Chords);
        Assert.False(tree.HasErrors, string.Join(" | ", tree.Diagnostics.Select(d => d.Message)));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        return [.. score.ChordNames.Select(c => c.ChordText)];
    }

    [Fact]
    public void ChordNames_Words_IsTheDefault_AndSymbolsSpellsLilyPondsFour()
    {
        var words = new[] { "Cdim", "Caug", "Cm7♭5", "Cdim7", "Am7/C" };
        Assert.Equal(words, ChordTexts(""));
        Assert.Equal(words, ChordTexts("layout { chordQualities words }\n"));

        // LilyPond's ignatzekExceptionMusic, in the characters it names the four with.
        // Every other quality is spelled the same in both — the minor seventh is here to
        // say so rather than to be assumed.
        Assert.Equal(
            new[] { "C°", "C+", "Cø", "C°7", "Am7/C" },
            ChordTexts("layout { chordQualities symbols }\n"));
    }

    [Fact]
    public void MinorChords_Lower_LowercasesTheRoot_DropsTheM_AndLeavesTheBassAlone()
    {
        var lower = ChordTexts("layout { minorChords lower }\n");

        // The minor seventh: root down, the m gone with it, the BASS still a capital —
        // LilyPond calls chordNoteNamer with lowercase? = #f (MinorChords' remark).
        Assert.Equal("a7/C", lower[4]);
        // A diminished chord has a minor third too, so LilyPond lowercases it as well; its
        // quality word carries no leading "m" to drop.
        Assert.Equal("cdim", lower[0]);
        // The AUGMENTED triad's third is MAJOR: never lowercased, under either vocabulary.
        Assert.Equal("Caug", lower[1]);
    }

    /// <summary>
    /// The two switches together reach LilyPond's own chord names for these chords — the
    /// one claim worth making about the pair, and the reason they are separate keys rather
    /// than one bundled style.
    /// </summary>
    /// <remarks>
    /// ★ MEASURED against LilyPond 2.26.0, not argued (scratch/p372/probe-symbols.ly, whose
    /// twin carries <c>chordNameLowercaseMinor = ##t</c>): LilyPond's own SVG spells the same
    /// five chords <c>c°</c>, <c>C+</c>, <c>cø</c>, <c>c°7</c>, <c>a7/C</c> — character for
    /// character what this asserts. The same probe with neither switch shows the divergence
    /// the keys exist for: LilyPond prints <c>C°</c> / <c>C+</c> / <c>Cø</c> / <c>C°7</c>
    /// there too, because ITS exception table is always on, while Lily# prints the words.
    /// <para>
    /// ⚠️ NOT a claim about the whole vocabulary: LilyPond raises everything after the root
    /// and draws a major seventh as a triangle, neither of which Lily# has (see
    /// <see cref="LilySharp.Core.Semantics.ChordQualityStyle"/>). What agrees here is the
    /// SPELLING — which characters stand for which quality, and which case the root is in.
    /// </para>
    /// </remarks>
    [Fact]
    public void BothSwitchesTogether_SpellTheseChordsAsLilyPondDoes()
    {
        Assert.Equal(
            new[] { "c°", "C+", "cø", "c°7", "a7/C" },
            ChordTexts("layout { chordQualities symbols  minorChords lower }\n"));
    }

    [Fact]
    public void WritingTheChordDefaults_IsTheSamePageAsWritingNothing()
    {
        Assert.Equal(
            MaskDataPos(SvgGenerator.Generate(SyntaxTree.Parse(Chords), Opt)),
            MaskDataPos(SvgGenerator.Generate(
                SyntaxTree.Parse("layout { chordQualities words  minorChords upper }\n" + Chords), Opt)));
    }

    /// <summary>
    /// ★ THE ONE WAY <c>minorChords lower</c> CAN CORRUPT A SYMBOL: it drops the suffix's
    /// leading <c>m</c> as the minor modifier, so a minor-third quality whose suffix opened
    /// with <c>ma…</c> would silently lose the <c>m</c> of <c>maj</c> and print a major
    /// seventh as a minor one. Asked of the whole registry, in both vocabularies, so a
    /// quality registered tomorrow is asked the same day — and asked of the SUFFIX rather
    /// than of a list of qualities, which is what makes it a question and not a restatement.
    /// </summary>
    [Fact]
    public void NoMinorThirdQuality_SpellsItsSuffixSoTheMinorModifierEatsAWord()
    {
        int asked = 0;
        foreach (ChordQuality q in System.Enum.GetValues<ChordQuality>())
        {
            if (!ChordQualityRegistry.HasMinorThird(q))
                continue;
            foreach (var style in System.Enum.GetValues<ChordQualityStyle>())
            {
                asked++;
                string suffix = ChordQualityRegistry.GetSuffix(q, style);
                Assert.False(suffix.StartsWith("ma", System.StringComparison.Ordinal),
                    $"{q} spells its minor-third suffix '{suffix}': dropping the minor "
                    + "modifier would eat the head of a word.");
            }
        }
        Assert.True(asked >= 10, $"only {asked} minor-third spellings were reached");
    }

    /// <summary>
    /// A Roman degree does not move with <c>chordQualities</c>, and every quality the
    /// vocabulary DOES move is accounted for: either the roman table already replaced the
    /// word for it, or what the vocabulary gives is the major-seventh TRIANGLE, which a
    /// degree cannot carry.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ THE SECOND ARM IS NOT AN EXCUSE, it is the geometry: the triangle is a polygon
    /// sized and placed by <c>\super</c> (<c>ChordNameGlyphRun.TriangleBase</c> takes the
    /// step three notches under the raised one), and a Roman degree is drawn on ONE
    /// baseline — <c>ChordNameEngraver.DisplaySymbol</c> gives it no superscript, because
    /// LilyPond has no degrees and there is nothing to follow. A baseline triangle would be
    /// a size with no source. So <c>Imaj7</c> stays <c>Imaj7</c> under both words, exactly
    /// as <c>IIm7</c> does.
    /// </para>
    /// <para>
    /// Derived in both directions — which qualities move comes from the two tables, and what
    /// roman prints comes from <c>ToRomanNumeral</c> — so neither half is a list this file
    /// keeps up to date. The COUNT is asserted so a quality quietly leaving either table is
    /// a failure rather than a smaller sweep.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheRomanDegreeCannotFollowTheChordVocabulary()
    {
        int overridden = 0, triangles = 0;
        foreach (ChordQuality q in System.Enum.GetValues<ChordQuality>())
        {
            string words = ChordQualityRegistry.GetSuffix(q, ChordQualityStyle.Words);
            string symbols = ChordQualityRegistry.GetSuffix(q, ChordQualityStyle.Symbols);
            if (words == symbols)
                continue;   // the two vocabularies agree: nothing for roman to follow
            // Degree I of C major, so whatever follows the "I" is the roman suffix.
            string roman = new ChordStructure(0, 0, q).ToRomanNumeral(0, 0)["I".Length..];
            if (symbols.Contains(ChordNameGlyphRun.TriangleCarrier))
            {
                triangles++;
                // The degree keeps the WORD — it has no superscript to put a triangle in.
                Assert.Equal(words, roman);
            }
            else
            {
                overridden++;
                Assert.NotEqual(words, roman);
            }
        }
        // LilyPond's exception table names four of Lily#'s qualities, and majorSevenSymbol
        // reaches the four whose seventh is a natural major one.
        Assert.Equal(4, overridden);
        Assert.Equal(4, triangles);
    }

    /// <summary>
    /// ★ AND THE TWO AXES COMPOSE ON THE PAGE. <c>chords NAME as names|roman</c> says WHICH
    /// QUANTITY a row shows; <c>layout { chordQualities }</c> says how the quality of
    /// whatever it shows is SPELLED. They are separate settings at separate scopes — the
    /// clause is per ROW because one score writes both at once, which a score-wide key could
    /// not carry — and the identity above means a roman row cannot move under the
    /// vocabulary. Asked here of a score that writes BOTH rows, which is the arrangement
    /// that replaced the retired <c>as both</c>: the names row changes, the degrees row is
    /// byte-identical, on the one page.
    /// </summary>
    [Fact]
    public void TheDegreesRowIsUnmovedByTheVocabulary_WhileTheNamesRowBesideItChanges()
    {
        const string TwoRows =
            "time 4/4\nkey c major\npart m { clef treble }\n"
            + "section A { m { c4 d e f | } chords prog { Cdim | } }\n"
            + "form main { ~A }\n"
            + "score main { chords prog as roman  chords prog as names  staff m }\n";

        static string[] Shown(string top)
        {
            var tree = SyntaxTree.Parse(top + TwoRows);
            Assert.False(tree.HasErrors, string.Join(" | ", tree.Diagnostics.Select(d => d.Message)));
            var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
            // What each band actually engraves: a roman band prints its degree, a names band
            // its symbol (ChordNameEngraver's DisplayMode arm).
            return [.. score.ChordNames.Select(c =>
                c.DisplayMode == ChordDisplayMode.Roman ? c.RomanText ?? c.ChordText : c.ChordText)];
        }

        Assert.Equal(new[] { "I°", "Cdim" }, Shown(""));
        Assert.Equal(new[] { "I°", "C°" }, Shown("layout { chordQualities symbols }\n"));
    }

    /// <summary>
    /// ★ THE OTHER TWO NAMERS ON THE PAGE. A chord symbol is spelled in four places, and
    /// the two above cover one of them (a `chords` ROW). These are the other two: an inline
    /// <c>@chord</c> on a note, which the main music walk collects, and a chord part
    /// ATTACHED to a staff, which the row's collector reaches by a different door. A namer
    /// that missed the switch would print the other vocabulary beside the one the score
    /// asked for, on the same page — the failure the required argument exists to stop, asked
    /// here of the finished page rather than of the compiler's types.
    /// </summary>
    /// <remarks>
    /// ★ POISONED to see it bite (2026-09-11), since it was green the hour it was written
    /// and that says nothing (RULES §5.4): <c>CollectChordAnnotations</c> was made to pass
    /// <c>ChordSpelling.Default</c> instead of the score's — exactly the shape a namer that
    /// missed the switch has — and it turned red naming the odd one out:
    ///   Assert.All() Failure: 1 out of 3 items … Expected: "c°"  Actual: "Cdim"
    /// </remarks>
    [Fact]
    public void TheInlineChordAndTheAttachedTrack_SpellItTheSameWayTheRowDoes()
    {
        const string Inline =
            "time 4/4\npart m { clef treble }\n"
            + "section A { m { c4@chord(Cdim) d e f | } chords prog { Cdim | } }\n"
            + "form main { ~A }\nscore main { chords prog  staff m with chords prog }\n";

        static string[] Texts(string source)
        {
            var tree = SyntaxTree.Parse(source);
            Assert.False(tree.HasErrors, string.Join(" | ", tree.Diagnostics.Select(d => d.Message)));
            var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
            return [.. score.ChordNames.Select(c => c.ChordText)];
        }

        // The row, the attached track and the inline mark are three symbols for the one
        // chord, and under each spelling all three read the same.
        var words = Texts(Inline);
        Assert.Equal(3, words.Length);
        Assert.All(words, t => Assert.Equal("Cdim", t));

        var symbols = Texts("layout { chordQualities symbols  minorChords lower }\n" + Inline);
        Assert.Equal(3, symbols.Length);
        Assert.All(symbols, t => Assert.Equal("c°", t));
    }

    /// <summary>
    /// An edit that changes the spelling renders exactly what a full recompile renders, in
    /// both directions — the gate every score-wide switch has to pass, because the spelling
    /// lives outside each measure's own reuse key and only the session-level comparison of
    /// the resolved plan can shed the caches (the accidental style's guard, same shape).
    /// </summary>
    /// <remarks>
    /// What carries it, named so the guard is not mistaken for luck: the spelling is a field
    /// OF <c>LayoutPlan</c>, which is a record, and the resume gate compares the resolved
    /// plan by value (<c>MeasureCollector.MetaMatchesShifted</c>). A spelling kept anywhere
    /// else — on the collector, on the chord collector alone — would resume stale, and that
    /// is the shape this asks about. NOT poisoned: the poison for a spelling that does not
    /// reach a namer is on the test above, and there is no half-state of "in the record but
    /// not compared" to introduce here without inventing one.
    /// </remarks>
    [Fact]
    public void ASpellingEdit_RendersIdenticalToFullRecompile_BothWays()
    {
        string plain = Chords;
        string spelled = "layout { chordQualities symbols  minorChords lower }\n" + Chords;
        var compiler = new IncrementalCompiler(SyntaxTree.Parse(plain), Opt);
        compiler.Render();
        foreach (var (text, label) in new[] { (spelled, "to symbols"), (plain, "back to words") })
        {
            string incremental = compiler.RenderIncremental(SyntaxTree.Parse(text));
            string full = SvgGenerator.Generate(SyntaxTree.Parse(text), Opt);
            Assert.True(full == incremental, $"{label}: incremental != full");
        }
    }

    /// <summary>
    /// ★ THE DRAWN SYMBOL, piece by piece, against LilyPond's own numbers — which size each
    /// piece is set at and whether it stands on the root's baseline.
    /// </summary>
    /// <remarks>
    /// ⚠️ THIS IS THE OBSERVER A DEFECT LIVED WITHOUT. Under <c>chordQualities symbols</c>
    /// with <c>minorChords lower</c> the degree sign went into the SUPERSCRIPT — drawn
    /// 0.707× and lifted — from the day the vocabulary shipped until 2026-09-12, and nothing
    /// saw it: no tracked book writes <c>symbols</c>, so no snapshot covered it, and the
    /// text-level tests above compare STRINGS, which were right all along. A picture nobody
    /// draws is a picture nobody checks.
    /// <para>
    /// MEASURED on LilyPond 2.26.0 (scratch/p372/lpnames.svg, lp-words.svg): a default
    /// ChordName sets its root at font-size 2.6165 and the raised pieces at 1.8500, lifted
    /// 1.1892; the degree sign sits on the ROOT's baseline at 3.2965, which is
    /// <c>whiteCircleMarkup</c>'s <c>\fontsize #2</c>. The tolerance is three decimals
    /// because LilyPond's SVG quotes four and the last one is its own rounding.
    /// </para>
    /// <para>
    /// ★ POISONED with the defect it was written for (2026-09-12): <c>PrintedSymbol</c>'s
    /// baseline count was put back to zero whenever the root is lowercased — the line that
    /// carried the defect — and the two degree-sign cases went red naming the size the
    /// circle lost:
    ///   Assert.Equal() … Expected: 3.2965  Actual: 2.331
    /// 2.331 is the em at the superscript's font-size plus the circle's own +2, i.e. the
    /// circle drawn inside the <c>\super</c> it must never enter.
    /// </para>
    /// </remarks>
    /// <param name="entry">The chord as the row writes it.</param>
    /// <param name="expected">The pieces the page must draw, left to right, as
    /// <c>text:em:up</c> triples — the em being the font-size LilyPond sets that piece at
    /// and <c>up</c> whether it stands in the superscript.</param>
    [Theory]
    // symbols + lower: LilyPond's own picture for these chords.
    [InlineData("Cdim", "c:2.6165:down|°:3.2965:down")]
    [InlineData("Cm7-5", "c:2.6165:down|ø:1.8500:up")]
    [InlineData("Cdim7", "c:2.6165:down|°:3.2965:down|7:1.8500:up")]
    // …the augmented triad's third is MAJOR, so the root keeps its capital and the whole
    // symbol stands on the baseline (LilyPond prints "C+" flat).
    [InlineData("Caug", "C+:2.6165:down")]
    // …and a minor seventh, where the modifier goes with the lowercase root and only the
    // digit is raised.
    [InlineData("Am7", "a:2.6165:down|7:1.8500:up")]
    public void TheDrawnPieces_AreSetTheSizeLilyPondSetsThem(string entry, string expected)
    {
        string book = "layout { chordQualities symbols  minorChords lower }\n"
            + "time 4/4\npart m { clef treble }\n"
            + "section A { m { c4 d e f | } chords prog { " + entry + " | } }\n"
            + "form main { ~A }\nscore main { chords prog  staff m }\n";
        var tree = SyntaxTree.Parse(book);
        Assert.False(tree.HasErrors, string.Join(" | ", tree.Diagnostics.Select(d => d.Message)));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        var item = Assert.Single(score.ChordNames);

        var pieces = ChordNameGlyphRun.Pieces(score.TextMetrics, item.ChordText, item.SuperFrom);
        var want = expected.Split('|');
        Assert.Equal(want.Length, pieces.Length);

        double superRaise = ChordNameGlyphRun.SuperRaise(score.TextMetrics);
        for (int k = 0; k < want.Length; k++)
        {
            var parts = want[k].Split(':');
            Assert.Equal(parts[0], pieces[k].Text);
            Assert.Equal(
                double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture),
                ChordNameGlyphRun.EmAt(pieces[k].FontSize), 3);
            // Raised, or on the ROOT's baseline — the question the defect got wrong.
            Assert.Equal(parts[2] == "up" ? superRaise : 0.0, pieces[k].Raise, 9);
        }
    }

    [Fact]
    public void MinorChords_Lower_IsLilyPondsOwnPropertyInTheTwin()
    {
        string lower = new LilyPondExporter().Export(
            SyntaxTree.Parse("layout { minorChords lower }\n" + Chords));
        Assert.Contains("\\new ChordNames \\with { chordNameLowercaseMinor = ##t }",
            lower, StringComparison.Ordinal);

        // A book that writes nothing gets no \with at all, so every existing twin stands.
        Assert.DoesNotContain("chordNameLowercaseMinor",
            new LilyPondExporter().Export(SyntaxTree.Parse(Chords)), StringComparison.Ordinal);
        // …and the VOCABULARY key reaches the twin not at all: LilyPond names the chords it
        // realizes by its own rules (the 2026-09-08 owner decision).
        Assert.Equal(
            new LilyPondExporter().Export(SyntaxTree.Parse(Chords)),
            new LilyPondExporter().Export(
                SyntaxTree.Parse("layout { chordQualities symbols }\n" + Chords)));
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
