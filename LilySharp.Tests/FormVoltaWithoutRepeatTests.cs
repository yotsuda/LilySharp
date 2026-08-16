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
using LilySharp.Core.LilyPond;
using LilySharp.Core.Midi;
using LilySharp.Core.MusicXml;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A volta ending that NO repeat block opened — <c>form main { A [1. B] }</c> — is its
/// plain section and nothing else: no bracket, no number, played once.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ The rule is LilyPond's, and it was MEASURED rather than reasoned about (2.26.0,
/// 2026-08-16): <c>\alternative { \volta 1 { … } }</c> with no <c>\repeat volta</c> in
/// front of it renders BYTE-IDENTICALLY to writing the music plainly, and LP says nothing
/// at all. The control with the <c>\repeat</c> restored hashes differently, so the
/// comparison is live. (An alternative written with NO volta number does draw a warning
/// from LP — "missing volta specification" — but that is about the missing number, not
/// about the missing repeat; measuring only that shape would have given the opposite
/// answer to the question actually being asked.)
/// </para>
/// <para>
/// ⚠️ The reason this file exists is that FOUR walks answer this question and only two of
/// them agreed. <see cref="LilyPondExporter"/> and <see cref="MusicXmlExporter"/> already
/// read a repeat-less ending as its section — LilyPondExporter even carries the rule in a
/// comment — while <c>MeasureCollector.ProcessForm</c> and <c>MidiExporter.PlayForm</c>
/// had no arm for the shape and dropped the section on the floor. The same file was
/// therefore two different pieces of music depending on which output you asked for. The
/// tests below assert the four together, so the next walk to drift is caught by name.
/// </para>
/// <para>
/// ⚠️ The predicate is the ENGRAVER's, not "the form has no repeat block": the ticket
/// proposed the latter and it is too weak. <c>|: A :| B [1. B]</c> has a repeat block and
/// its ending was dropped just the same, because the ending is a child of the FORM
/// (measured on the tree). A legitimate <c>|: A [1. D] :| [2. O]</c> keeps BOTH endings
/// inside the repeat block — the one after the <c>:|</c> fills ParseFormRepeatBlock's
/// finalAlternative slot — so it is never accused.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class FormVoltaWithoutRepeatTests
{
    private const string Head =
        "part m { clef treble }\n" +
        "section A { m { c4 c c c | } }\n" +
        "section B { m { d4 d d d | } }\n";

    private const string Tail = "\nscore { staff m }\n";

    private static SyntaxTree Parse(string form) => SyntaxTree.Parse(Head + form + Tail);

    private static string Page(string form) => SvgGenerator.Generate(Parse(form));

    private static int[] MidiPitches(string form)
        => new MidiExporter().Export(Parse(form)).Tracks[1].Notes.Select(n => n.Pitch).ToArray();

    private static string Twin(string form) => new LilyPondExporter().Export(Parse(form));

    private static string Xml(string form) => new MusicXmlExporter().Export(Parse(form)).ToString();

    /// <summary>
    /// The whole claim in one table: for every spelling of a repeat-less ending, all FOUR
    /// outputs are what the equivalent plain reference produces. Byte equality against a
    /// control, not a shape assertion — a bracket, a number or a dropped section would all
    /// break it, and none of them has to be enumerated here.
    /// </summary>
    [Theory]
    [InlineData("form main { A [1. B] }", "form main { A B }")]
    [InlineData("form main { [1. A] }", "form main { A }")]
    [InlineData("form main { |: A :| B [1. B] }", "form main { |: A :| B B }")]
    public void ARepeatlessEnding_IsItsPlainReference_InAllFourOutputs(string written, string plain)
    {
        Assert.Equal(Page(plain), Page(written));
        Assert.Equal(MidiPitches(plain), MidiPitches(written));
        Assert.Equal(Twin(plain), Twin(written));
        Assert.Equal(Xml(plain), Xml(written));
    }

    /// <summary>
    /// The premise of the test above, asserted so it cannot pass by being vacuous: the
    /// controls really are three DIFFERENT pieces, and none of them is the empty page that
    /// <c>form main { [1. A] }</c> used to produce (§5.4's empty-set trap — a comparison
    /// between two zero-byte strings would satisfy every Equal above).
    /// </summary>
    [Fact]
    public void TheControlsAreThreeDifferentNonEmptyPieces()
    {
        var pages = new[] { Page("form main { A B }"), Page("form main { A }"),
                            Page("form main { |: A :| B B }") };
        Assert.All(pages, p => Assert.NotEqual("", p));
        Assert.Equal(3, pages.Distinct().Count());
    }

    /// <summary>
    /// No bracket is engraved — the half of LP's answer that byte equality proves but does
    /// not NAME. Stated separately because it is the surprising half: the author wrote
    /// <c>[1.</c> and the number prints nothing — which is why saying so is the other half
    /// of this repair (FormDeclarationValidator).
    /// </summary>
    [Fact]
    public void ARepeatlessEnding_EngravesNoVoltaBracket()
    {
        var tree = Parse("form main { A [1. B] }");
        var layout = new LayoutEngine().Layout(
            new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!));
        Assert.Empty(layout.VoltaBracketLayouts);
    }

    /// <summary>
    /// The control for the test above, and the guard on the predicate: a REAL pair of
    /// endings still draws both brackets. Without this, deleting the volta engraver would
    /// leave the test above green.
    /// </summary>
    [Fact]
    public void ARealPairOfEndings_StillDrawsBothBrackets()
    {
        var tree = Parse("form main { |: A [1. A] :| [2. B] }");
        var layout = new LayoutEngine().Layout(
            new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!));
        Assert.Equal(new[] { "1.", "2." },
            layout.VoltaBracketLayouts.Select(v => v.VoltaText).OrderBy(t => t).ToArray());
    }

    /// <summary>
    /// The <c>!IsInsideRepeatBlock</c> guard on the new arm, pinned.
    /// </summary>
    /// <remarks>
    /// ⚠️ This test exists because the guard's poison came back GREEN across every test in
    /// the FormVolta family — the arm's other tests all look at shapes a double pass does
    /// not change (a second pass adds no bracket, and the repeat-less spellings have no
    /// ending inside a block to double). The guard is nonetheless load-bearing: with it
    /// removed, <c>ProcessForm</c>'s walk over <c>DescendantNodes()</c> engraves each ending
    /// a second time on top of ProcessRepeatBlock's, and the page really does move
    /// (<c>|: A [1. A] :| [2. B]</c> ink 86F66A44 → 63559F68, measured). Counting the
    /// SECTIONS is what sees it; the bracket layouts do not.
    /// </remarks>
    [Fact]
    public void AnEndingInsideARepeatBlock_IsEngravedByTheBlockAndNotAlsoByThisArm()
    {
        var labels = new MeasureCollector()
            .Collect(Parse("form main { |: A [1. A] :| [2. B] }"), "m")
            .Voice.Measures.Select(m => m.SectionLabel).ToArray();
        Assert.Equal(new[] { "A", "A", "B" }, labels);   // not A A A B B
    }

    /// <summary>
    /// The section is played ONCE, not twice — the ending is not an extra pass over
    /// anything. Pitch counts say it directly (A is four c's, B four d's).
    /// </summary>
    [Fact]
    public void ARepeatlessEnding_PlaysItsSectionExactlyOnce()
        => Assert.Equal(8, MidiPitches("form main { A [1. B] }").Length);

    /// <summary>
    /// The label follows the plain reference's rule, which is the one LilyPondExporter
    /// already wrote for this shape: the display label if given, otherwise the name — and
    /// <c>[1. ~B]</c> hides it exactly as <c>~B</c> does.
    /// </summary>
    [Theory]
    [InlineData("[1. B]", "B")]
    [InlineData("[1. B \"reprise\"]", "reprise")]
    [InlineData("[1. ~B]", null)]
    public void TheLabelIsThePlainReferences(string ending, string? expected)
    {
        var measures = new MeasureCollector()
            .Collect(Parse("form main { A " + ending + " }"), "m").Voice.Measures.ToArray();
        Assert.Equal(2, measures.Length);              // A, then the ending's section
        Assert.Equal(expected, measures[1].SectionLabel);
    }

    /// <summary>
    /// The MIDI replay path carries the same arm as the first pass. A one-sided <c>:|</c>
    /// replays the piece from its beginning, and <c>RepeatFromTheBeginning</c> is a SECOND
    /// switch over the same items — an arm added to one and not the other would make the
    /// ending sound on the first pass and vanish on the second.
    /// </summary>
    [Fact]
    public void TheRepeatlessEndingSoundsOnBothPassesOfAOneSidedRepeat()
    {
        var once = MidiPitches("form main { A [1. B] }");
        var twice = MidiPitches("form main { A [1. B] :| }");
        Assert.Equal(once.Concat(once).ToArray(), twice);
    }
}
