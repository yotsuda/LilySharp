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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// LYS6008 — a volta ending that no repeat block opened. It engraves as its plain section
/// (see <see cref="FormVoltaWithoutRepeatTests"/>), so the number the author wrote prints
/// nothing, and this says so.
/// </summary>
/// <remarks>
/// A WARNING, not an error: LilyPond renders the same shape as plain music and says nothing
/// (measured on 2.26.0), so the output is right — it is merely less than what writing
/// <c>[1.</c> looks like it asks for. Breaking LP's silence is the house rule, the same call
/// made for LYS0028 (user decision, 2026-08-16).
/// </remarks>
[Trait("Category", "Unit")]
public sealed class VoltaEndingWithoutRepeatDiagnosticTests
{
    private const string Head =
        "part m { clef treble }\n" +
        "section A { m { c4 c c c | } }\n" +
        "section B { m { d4 d d d | } }\n";

    private const string Tail = "\nscore { staff m }\n";

    private static SyntaxTree Parse(string form) => SyntaxTree.Parse(Head + form + Tail);

    private static Diagnostic[] Warnings(string form)
        => SemanticValidation.Run(Parse(form))
            .Where(d => d.Code == DiagnosticCodes.VoltaEndingWithoutRepeat).ToArray();

    /// <summary>Each spelling the engraver drops through to a plain reference is warned about
    /// exactly once — including the one in a form that DOES have a repeat block, which the
    /// weaker "this form has no repeat" rule would have missed.</summary>
    [Theory]
    [InlineData("form main { [1. A] }")]
    [InlineData("form main { A [1. B] }")]
    [InlineData("form main { |: A :| B [1. B] }")]     // has a repeat block, ending still loose
    [InlineData("form main { [1. ~A] }")]
    [InlineData("form main { [1-3. A] }")]
    public void ARepeatlessEnding_Warns(string form)
    {
        var d = Assert.Single(Warnings(form));
        Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
    }

    /// <summary>A real pair of endings is silent: both are children of the repeat block, the
    /// one after the <c>:|</c> included.</summary>
    [Theory]
    [InlineData("form main { |: A [1. A] :| [2. B] }")]
    [InlineData("form main { |: A [1. A] :| }")]
    [InlineData("form main { A B }")]
    public void ALegitimateArrangement_IsSilent(string form) => Assert.Empty(Warnings(form));

    /// <summary>
    /// ⚠️ The empty-set trap (§5.4), in the form it actually takes here: the silence above
    /// would also hold if the parser produced no alternatives at all. Assert that the quiet
    /// book really does contain two of them.
    /// </summary>
    [Fact]
    public void TheSilentControlReallyContainsEndings()
        => Assert.Equal(2, Parse("form main { |: A [1. A] :| [2. B] }").GetRoot()
            .DescendantNodes().OfType<FormAlternativeSyntax>().Count());

    /// <summary>
    /// ⚠️ THE claim, and the reason this diagnostic does not re-spell the rule: the set of
    /// endings warned about is exactly the set of endings the engraver does NOT bracket.
    /// One book holds all three cases — <c>[1. A]</c> and <c>[2. B]</c> inside the repeat
    /// block, <c>[3. B]</c> loose after it — and the two sets partition it. If the validator
    /// and the collector ever answer differently, this is where it shows (HANDOFF §5.2.1②).
    /// </summary>
    [Fact]
    public void TheWarnedEndingsAreExactlyTheOnesTheEngraverDoesNotBracket()
    {
        const string form = "form main { |: A [1. A] :| [2. B] [3. B] }";
        var tree = Parse(form);

        var written = tree.GetRoot().DescendantNodes().OfType<FormAlternativeSyntax>()
            .Select(a => a.VoltaText).OrderBy(t => t).ToArray();
        var bracketed = new LayoutEngine()
            .Layout(new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!))
            .VoltaBracketLayouts.Select(v => v.VoltaText).OrderBy(t => t).ToArray();
        var warned = Warnings(form)
            .Select(d => tree.GetRoot().DescendantNodes().OfType<FormAlternativeSyntax>()
                .First(a => a.Span.Start == d.Span.Start).VoltaText)
            .OrderBy(t => t).ToArray();

        Assert.Equal(new[] { "1.", "2.", "3." }, written);
        Assert.Equal(new[] { "1.", "2." }, bracketed);
        Assert.Equal(new[] { "3." }, warned);
        Assert.Empty(bracketed.Intersect(warned));                  // disjoint
        Assert.Equal(written, bracketed.Concat(warned).OrderBy(t => t).ToArray());  // exhaustive
    }

    /// <summary>Every loose ending is reported on its own, so two of them do not hide behind
    /// each other (the rule LYS6002 and LYS6007 already follow).</summary>
    [Fact]
    public void EveryLooseEndingIsReported()
        => Assert.Equal(2, Warnings("form main { [1. A] [2. B] }").Length);

    /// <summary>
    /// The squiggle sits on the ending itself — the thing to delete or to open a repeat in
    /// front of — not on the form's braces or its keyword, and not one character wider than
    /// what was written.
    /// </summary>
    /// <remarks>
    /// ⚠️ That last clause is the reason this test is exact rather than a Contains: the
    /// node's own <c>Span</c> keeps its last token's TRAILING trivia, so the obvious spelling
    /// squiggles <c>"[1. B] "</c> — the space after the ending included. The validator takes
    /// the ink end from the last child token instead (FormDeclarationValidator.InkSpan).
    /// </remarks>
    [Theory]
    [InlineData("form main { A [1. B] }", "[1. B]")]
    [InlineData("form main { A [1. B }", "[1. B")]        // no ']' — ends on the section name
    public void TheWarningMarksExactlyTheEndingAsWritten(string form, string ink)
    {
        var d = Assert.Single(Warnings(form));
        string src = Head + form + Tail;
        Assert.Equal(src.IndexOf(ink), d.Span.Start);
        Assert.Equal(src.IndexOf(ink) + ink.Length, d.Span.End);
    }

    /// <summary>
    /// The message names what printed nothing, what was engraved instead, and both ways out.
    /// </summary>
    /// <remarks>
    /// ⚠️ Pinned because nothing else observes a message, which is how the reconstructed-text
    /// defect on WarnUnknown survived (HANDOFF §5.0). This message nearly repeated it: the
    /// first draft offered "drop the '[1.]'", a bracket-plus-number with the section cut out
    /// of the middle — not a string the author typed and not one the language accepts.
    /// </remarks>
    [Fact]
    public void TheMessageNamesTheNumberTheSectionAndBothWaysOut()
    {
        var d = Assert.Single(Warnings("form main { A [1. B] }"));
        Assert.Contains("'1.' prints nothing", d.Message);
        Assert.Contains("'B' is engraved as an ordinary section reference", d.Message);
        Assert.Contains("|: … [1. B] :|", d.Message);   // open a repeat
        Assert.Contains("write 'B' on its own", d.Message);
    }

    /// <summary>
    /// ⚠️ Every spelling the message QUOTES is one the author could have typed. The suggested
    /// <c>|: … :|</c> is exempt — a candidate has no source to quote (HANDOFF §5.0) — so it
    /// is excluded by name rather than by being overlooked.
    /// </summary>
    [Theory]
    [InlineData("form main { A [1. B] }")]
    [InlineData("form main { A [1-3. B] }")]
    [InlineData("form main { A [1,3. B] }")]
    public void EveryQuotedSpellingIsOneTheAuthorWrote(string form)
    {
        string src = Head + form + Tail;
        string message = Assert.Single(Warnings(form)).Message;
        // The candidate clause is the one rebuilt string; drop it, then every remaining
        // '…' quote has to be findable in the file.
        string quoted = message.Substring(0, message.IndexOf("Open a repeat"))
                      + message.Substring(message.IndexOf("or remove the brackets"));
        var quotes = System.Text.RegularExpressions.Regex.Matches(quoted, @"'([^']+)'")
            .Select(m => m.Groups[1].Value).ToArray();
        Assert.NotEmpty(quotes);
        Assert.All(quotes, q => Assert.Contains(q, src));
    }

    /// <summary>A ranged ending keeps its written form in the message rather than being
    /// re-spelt from its parts — <c>[1-3. A]</c> reports "1-3.", not "1.".</summary>
    [Fact]
    public void ARangedEndingReportsItsWrittenNumber()
        => Assert.Contains("'1-3.' prints nothing",
            Assert.Single(Warnings("form main { [1-3. A] }")).Message);

    /// <summary>
    /// LYS6007 and LYS6008 answer different questions and must not double up: a form holding
    /// only a loose ending DOES name a section, so it is not called empty.
    /// </summary>
    [Fact]
    public void ALooseEndingIsNotAlsoCalledAnEmptyForm()
        => Assert.DoesNotContain(SemanticValidation.Run(Parse("form main { [1. A] }")),
            d => d.Code == DiagnosticCodes.EmptyForm);
}
