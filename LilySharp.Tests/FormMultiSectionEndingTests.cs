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
using System.Linq;
using System.Xml.Linq;
using LilySharp.Core.Midi;
using LilySharp.Core.MusicXml;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// An ending may hold SEVERAL sections — <c>|: A [1. B C] :| [2. D]</c> — played in order
/// under one bracket (owner's decision, 2026-09-28), and every reader follows: the page (one
/// bracket over both), the MIDI (the pass plays both), MusicXML (one &lt;ending&gt; from the
/// first section's first bar to the last one's last) and the LilyPond twin (one
/// <c>\alternative</c> branch holding both).
/// </summary>
/// <remarks>
/// Also here: the first ending closes a repeat's body (anything but <c>:|</c> after it is the
/// ordinary "Expected" error), an ending needs its <c>]</c> unless a <c>:|</c> follows it at
/// once, and a repeat run must name a section before its first ending or its <c>:|</c> (LYS1041).
/// </remarks>
[Trait("Category", "Unit")]
public sealed class FormMultiSectionEndingTests
{
    // One whole note per section, a different pitch each: A=c' (MIDI 72) B=d' C=e' D=f' E=g'.
    private const string Head = """
        octave absolute
        time 4/4
        part m { clef treble }
        section A { m { c'1 | } }
        section B { m { d'1 | } }
        section C { m { e'1 | } }
        section D { m { f'1 | } }
        section E { m { g'1 | } }

        """;

    private const string Tail = "\nscore main { staff m }\n";

    private static SyntaxTree Parse(string form)
    {
        var tree = SyntaxTree.Parse(Head + form + Tail);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
        return tree;
    }

    private static List<int> MidiPitches(string form) =>
        new MidiExporter().Export(Parse(form)).Tracks.SelectMany(t => t.Notes)
            .OrderBy(n => n.StartTick).Select(n => n.Pitch).ToList();

    [Fact]
    public void AnEndingHoldsItsSectionsInOrder_AndRoundTrips()
    {
        var tree = Parse("form main { |: A [1. B ~C'] :| [2. D \"x\" E] }");
        var endings = tree.GetRoot().DescendantNodes().OfType<FormAlternativeSyntax>().ToList();
        Assert.Equal(2, endings.Count);
        Assert.Equal(new[] { SyntaxKind.SectionReference, SyntaxKind.SilentSectionReference },
            endings[0].Sections.Select(s => s.Kind));
        Assert.Equal(1, SyntaxFacts.NetOctaveMarks(endings[0].Sections[1]));
        Assert.Equal("x", ((SectionReferenceSyntax)endings[1].Sections[0]).DisplayLabel);
        // Every section in an ending is a reference to rename / go to / colour — once each.
        var form = tree.GetRoot().DescendantNodes().OfType<FormDeclarationSyntax>().Single();
        Assert.Equal(new[] { "A", "B", "C", "D", "E" },
            LilySharp.Core.Editing.SectionReferenceFinder.AllSectionNameTokens(form).Select(t => t.Text));
        Assert.Equal(Head + "form main { |: A [1. B ~C'] :| [2. D \"x\" E] }" + Tail,
            tree.GetRoot().ToFullString());
    }

    [Fact]
    public void ThePassPlaysEveryOneOfItsEndingsSections()
    {
        // A |: B [1. C D] :| [2. E]  =  A B C D B E
        Assert.Equal(new[] { 72, 74, 76, 77, 74, 79 },
            MidiPitches("form main { A |: B [1. C D] :| [2. E] }"));
        // The same with the second ending holding two.
        Assert.Equal(new[] { 74, 76, 74, 77, 79 },
            MidiPitches("form main { |: B [1. C] :| [2. D E] }"));
    }

    [Fact]
    public void OneBracketSpansEveryOneOfItsSections()
    {
        var tree = Parse("form main { A |: B [1. C D] :| [2. E] }");
        var spec = RenderSpecParser.FindFirst(tree)!;
        var layout = new LayoutEngine().Layout(new MeasureCollector().CollectMultiStaff(tree, spec));
        var first = Assert.Single(layout.VoltaBracketLayouts, v => v.VoltaText == "1.");
        Assert.Equal((2, 3), (first.StartMeasureIndex, first.EndMeasureIndex));   // C and D
        var second = Assert.Single(layout.VoltaBracketLayouts, v => v.VoltaText == "2.");
        Assert.Equal((4, 4), (second.StartMeasureIndex, second.EndMeasureIndex)); // E
    }

    [Fact]
    public void MusicXml_OneEndingFromTheFirstSectionToTheLast()
    {
        var doc = new MusicXmlExporter().Export(Parse("form main { A |: B [1. C D] :| [2. E] }")).ToXml();
        var measures = doc.Descendants("measure").ToList();
        Assert.Equal(5, measures.Count);   // A B C D E
        List<(int Measure, string Type, string Number)> Endings() =>
            measures.SelectMany((m, i) => m.Descendants("ending").Select(e =>
                (i, e.Attribute("type")!.Value, e.Attribute("number")!.Value))).ToList();
        Assert.Equal(new[]
        {
            (2, "start", "1"), (3, "stop", "1"),          // [1. C D]: start on C, stop on D
            (4, "start", "2"), (4, "stop", "2"),          // [2. E] — `]` hooks
        }, Endings());
        bool Backward(XElement m) => m.Descendants("repeat").Any(r => r.Attribute("direction")?.Value == "backward");
        Assert.True(Backward(measures[3]));    // the :| after D, not after C
        Assert.False(Backward(measures[2]));
    }

    [Fact]
    public void TheTwin_OneAlternativeHoldsBothSections()
    {
        string ly = new LilySharp.Core.LilyPond.LilyPondExporter().Export(Parse("form main { A |: B [1. C D] :| [2. E] }"));
        int alt = ly.IndexOf("\\alternative", System.StringComparison.Ordinal);
        Assert.True(alt > 0, ly);
        string tail = ly[alt..];
        // C and D in the first branch, E in the second:
        //   \alternative { { \mark … "C" … \mark … "D" … } { \mark … "E" … } }
        int c = tail.IndexOf("\\box \"C\"", System.StringComparison.Ordinal);
        int d = tail.IndexOf("\\box \"D\"", System.StringComparison.Ordinal);
        int e = tail.IndexOf("\\box \"E\"", System.StringComparison.Ordinal);
        Assert.True(c > 0 && d > c && e > d, tail);
        Assert.DoesNotContain("}", tail[c..d]);                      // one branch
        Assert.Matches(@"^[^{}]*\}\s*\{[^{}]*$", tail[d..e]);          // then the next one
        Assert.DoesNotContain("\\box \"C\"", ly[..alt]);             // not in the body
    }

    [Fact]
    public void ASlurRunsFromOneSectionOfAnEndingIntoTheNext()
    {
        const string src = """
            part vn {
              section B { c''1 | }
              section C { c''2 d''( || }
              section D { e''2) f'' || }
              section E { g''1 | }
            }
            form main { |: B [1. C D] :| [2. E] }
            score main { staff vn }
            """;
        var tree = SyntaxTree.Parse(src);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
        Assert.DoesNotContain(SemanticValidation.Run(tree),
            d => d.Code is DiagnosticCodes.SpanAcrossSectionBoundary or DiagnosticCodes.UnpairedSlur);
    }

    /// <summary>A tie at the end of an ending's first section is carried to its NEXT section —
    /// the one played right after it on the same pass — and nowhere else.</summary>
    [Fact]
    public void ATieRunsFromOneSectionOfAnEndingIntoTheNext()
    {
        const string src = """
            part vn {
              section B { c''1 | }
              section C { e''1~ || }
              section D { e''1 | }
              section E { g''1 | }
            }
            form main { |: B [1. C D] :| [2. E] }
            score main { staff vn }
            """;
        var tree = SyntaxTree.Parse(src);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
        Assert.DoesNotContain(SemanticValidation.Run(tree),
            d => d.Code is DiagnosticCodes.TieTargetMismatch or DiagnosticCodes.SpanAcrossSectionBoundary);
        var tie = Assert.Single(new TieDetector().DetectTies(new MeasureCollector().Collect(tree, "vn")));
        Assert.Equal((1, 2), (tie.StartMeasureIndex, tie.EndMeasureIndex));
    }

    // ---- where an ending without its ']' stops ------------------------------------------

    /// <summary>The one ending that may omit its <c>]</c>: the one a <c>:|</c> follows at once.
    /// The <c>:|</c> delimits it, and its bracket hooks (owner's design 2026-09-28).</summary>
    [Fact]
    public void AnUnclosedFirstEnding_RunsToItsRepeatBar_AndHooks()
    {
        var tree = Parse("form main { |: A [1. B C :| [2. D] }");
        var first = tree.GetRoot().DescendantNodes().OfType<FormAlternativeSyntax>().First();
        Assert.Equal(2, first.Sections.Count);
        Assert.True(first.EndsHooked);
        Assert.False(first.EndsOpen);
    }

    /// <summary>An unclosed LAST ending — no <c>]</c> and no <c>:|</c> after it — is the
    /// ordinary "Expected" error, whatever follows it (it used to hold its first section and
    /// let the rest play after the repeat; retired 2026-09-28).</summary>
    [Theory]
    [InlineData("form main { |: A [1. B] :| [2. C D E }", "}")]
    [InlineData("form main { |: A [1. B] :| [2. C D |: E :| }", "|:")]
    [InlineData("form main { A [1. B C break }", "break")]
    public void AnUnclosedLastEnding_IsTheOrdinaryExpectedError(string form, string at)
    {
        string src = Head + form + Tail;
        var tree = SyntaxTree.Parse(src);
        var error = Assert.Single(tree.Diagnostics, d => d.Code == DiagnosticCodes.ExpectedToken);
        Assert.StartsWith("Expected 'CloseBracket', found", error.Message);
        Assert.Equal(at, src.Substring(error.Span.Start, error.Span.Length).Trim());
        // The ending holds every section written before the error.
        var last = tree.GetRoot().DescendantNodes().OfType<FormAlternativeSyntax>().Last();
        Assert.True(last.Sections.Count >= 2);
    }

    [Fact]
    public void ALoneEndingOfTwoSections_WarnsWithBothNames()
    {
        var tree = SyntaxTree.Parse(Head + "form main { A [1. B C] }" + Tail);
        var warning = Assert.Single(SemanticValidation.Run(tree),
            d => d.Code == DiagnosticCodes.VoltaEndingWithoutRepeat);
        Assert.Equal("No repeat opens this ending, so '1.' prints nothing and 'B C' is engraved as "
            + "ordinary section references. Open a repeat ('|: … [1. B C] :| …'), or remove the "
            + "brackets and write 'B C' on its own.", warning.Message);
        Assert.Equal("[1. B C]", (Head + "form main { A [1. B C] }" + Tail).Substring(warning.Span.Start, warning.Span.Length));
        Assert.Equal(new[] { 72, 74, 76 }, MidiPitches("form main { A [1. B C] }"));
    }

    // ---- a repeat run must name a section (LYS1041) --------------------------------------

    [Theory]
    [InlineData("form main { |: [1. B] :| [2. C] }", "|:", true)]
    [InlineData("form main { A |: :| [2. C] }", "|:", true)]
    [InlineData("form main { A |: :| }", "|:", false)]
    [InlineData("form main { A |: B :|: :| }", ":|:", false)]
    [InlineData("form main { |: A :|: [1. B] :| [2. C] }", ":|:", true)]
    [InlineData("form main { A |: break :| }", "|:", false)]
    public void ARepeatRunWithNoSection_IsAnError(string form, string underlined, bool withEnding)
    {
        string src = Head + form + Tail;
        var tree = SyntaxTree.Parse(src);
        var error = Assert.Single(tree.Diagnostics, d => d.Code == DiagnosticCodes.EmptyRepeatBody);
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Equal(underlined, src.Substring(error.Span.Start, error.Span.Length));
        Assert.StartsWith(withEnding
            ? "A repeat with endings needs music before its first ending — the part every pass plays"
            : "This repeat has nothing to repeat", error.Message);
    }

    [Theory]
    [InlineData("form main { |: A :| }")]
    [InlineData("form main { |: ~A [1. B] :| [2. C] }")]
    [InlineData("form main { |: A :|: B :| }")]
    [InlineData("form main { |: A [1. B C] :| [2. D] }")]
    public void ARepeatRunThatNamesASection_IsNot(string form)
    {
        var tree = SyntaxTree.Parse(Head + form + Tail);
        Assert.DoesNotContain(tree.Diagnostics, d => d.Code == DiagnosticCodes.EmptyRepeatBody);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
    }
}
