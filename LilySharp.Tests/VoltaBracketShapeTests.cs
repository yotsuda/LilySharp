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
using LilySharp.Core.MusicXml;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// An ending's RANGE, END SHAPE and LENGTH are three separate settings (owner's design
/// 2026-09-28): <c>]</c> delimits the ending (a <c>:|</c> right after it may stand in for it),
/// <c>]</c> hooks the bracket's right end down and <c>-]</c> leaves it straight, and
/// <c>layout { voltaBracket all|line|N }</c> — or the ending's own
/// <c>[1. B C]@voltaBracket(…)</c>, which wins — says how far the bracket reaches. A bracket
/// cut short always ends straight. Every output follows: the page, the LilyPond twin and
/// MusicXML (<c>stop</c> / <c>discontinue</c>).
/// </summary>
[Trait("Category", "Unit")]
public sealed class VoltaBracketShapeTests
{
    // One whole note a bar; A is one bar, B–E two, F four with a forced system break after
    // its second bar.
    private const string Head = """
        octave absolute
        time 4/4
        part m { clef treble }
        section A { m { c'1 | } }
        section B { m { d'1 | d'1 | } }
        section C { m { e'1 | e'1 | } }
        section D { m { f'1 | f'1 | } }
        section E { m { g'1 | g'1 | } }
        section F { m { a'1 | a'1 | break a'1 | a'1 | } }
        section G { m { b'1 | time 3/4 b'2. | b'2. | } }

        """;

    private static string Source(string form, string layout = "")
        => Head + layout + (layout.Length > 0 ? "\n" : "") + form + "\nscore { staff m }\n";

    private static SyntaxTree Parse(string form, string layout = "")
    {
        var tree = SyntaxTree.Parse(Source(form, layout));
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
        return tree;
    }

    private static List<VoltaBracketLayout> Brackets(string form, string layout = "")
    {
        var tree = Parse(form, layout);
        var spec = RenderSpecParser.FindFirst(tree)!;
        return [.. new LayoutEngine().Layout(new MeasureCollector().CollectMultiStaff(tree, spec))
            .VoltaBracketLayouts.OrderBy(v => v.StartMeasureIndex)];
    }

    // ---- syntax -------------------------------------------------------------------------

    [Fact]
    public void TheEndShapes_AndTheOverride_RoundTrip()
    {
        const string form = "form { |: A [1. B C]@voltaBracket(3) :| [2. D E -]@voltaBracket(line) }";
        var tree = Parse(form);
        Assert.Equal(Source(form), tree.GetRoot().ToFullString());
        var endings = tree.GetRoot().DescendantNodes().OfType<FormAlternativeSyntax>().ToList();
        Assert.Equal(2, endings[0].Sections.Count);
        Assert.Equal(2, endings[1].Sections.Count);
        Assert.True(endings[0].EndsHooked);
        Assert.True(endings[1].EndsOpen);
        Assert.Equal(VoltaBracketLength.FirstBars(3), endings[0].LengthOverride);
        Assert.Equal(VoltaBracketLength.Line, endings[1].LengthOverride);
        Assert.Empty(SemanticValidation.Run(tree).Where(d => d.Code == DiagnosticCodes.UnknownAnnotation));
    }

    /// <summary><c>-]</c> is two tokens that nothing else in a form spells: a section name
    /// cannot hold a '-' and a '-' never lexes into a number — so <c>E-]</c> is the section E
    /// and an open end, and a range ending's own '-' (<c>[1-2.</c>) is unaffected.</summary>
    [Theory]
    [InlineData("form { |: A [1. B] :| [2-3. C E-] }", 2)]
    [InlineData("form { |: A [1. B] :| [2. C -] }", 1)]
    public void MinusBracket_IsAnOpenEnd(string form, int sections)
    {
        var last = Parse(form).GetRoot().DescendantNodes().OfType<FormAlternativeSyntax>().Last();
        Assert.True(last.EndsOpen);
        Assert.Equal(sections, last.Sections.Count);
    }

    [Fact]
    public void AMinusApartFromItsBracket_IsTheOrdinaryExpectedError()
    {
        var tree = SyntaxTree.Parse(Source("form { |: A [1. B] :| [2. C - ] }"));
        var error = Assert.Single(tree.Diagnostics, d => d.Code == DiagnosticCodes.ExpectedToken);
        Assert.Equal("Expected 'CloseBracket', found 'Minus'", error.Message);
    }

    // ---- end shape on the page -----------------------------------------------------------

    [Theory]
    [InlineData("form { |: A [1. B] :| [2. C] }", true)]
    [InlineData("form { |: A [1. B] :| [2. C -] }", false)]
    [InlineData("form { |: A [1. B -] :| [2. C] }", true)]   // the 2. is asked; still hooked
    public void TheLastEndingsHook_FollowsItsBracket(string form, bool hook)
    {
        var second = Assert.Single(Brackets(form), v => v.VoltaText == "2.");
        Assert.Equal(hook, second.IsClosed);
    }

    [Fact]
    public void AnOpenFirstEnding_IsStraightBeforeItsRepeatBar()
    {
        var first = Assert.Single(Brackets("form { |: A [1. B -] :| [2. C] }"), v => v.VoltaText == "1.");
        Assert.False(first.IsClosed);
        // …and the ending its ':|' closes hooks, like `]`.
        var closedByBar = Assert.Single(Brackets("form { |: A [1. B :| [2. C] }"), v => v.VoltaText == "1.");
        Assert.True(closedByBar.IsClosed);
    }

    // ---- length on the page --------------------------------------------------------------

    [Theory]
    // [1. B C] covers bars 1..4 (A is bar 0).
    [InlineData("", "", 1, 4, true)]                                    // all (the default)
    [InlineData("layout { voltaBracket all }", "", 1, 4, true)]
    [InlineData("layout { voltaBracket 2 }", "", 1, 2, false)]          // cut: straight
    [InlineData("layout { voltaBracket 4 }", "", 1, 4, true)]           // N = the ending: not cut
    [InlineData("layout { voltaBracket 9 }", "", 1, 4, true)]           // N longer: all of it
    [InlineData("", "@voltaBracket(3)", 1, 3, false)]
    [InlineData("layout { voltaBracket 1 }", "@voltaBracket(all)", 1, 4, true)]  // the ending wins
    [InlineData("layout { voltaBracket all }", "@voltaBracket(1)", 1, 1, false)]
    public void NBars_CoverTheEndingsFirstNBars(string layout, string annotation, int start, int end, bool hook)
    {
        string form = $"form {{ |: A [1. B C]{annotation} :| [2. D] }}";
        var first = Assert.Single(Brackets(form, layout), v => v.VoltaText == "1.");
        Assert.Equal((start, end, hook), (first.StartMeasureIndex, first.EndMeasureIndex, first.IsClosed));
    }

    [Fact]
    public void Line_DrawsOnlyTheFirstSystemsPiece_Straight()
    {
        // [2. F] covers bars 3..6 with a system break after bar 4.
        const string form = "form { |: A [1. B] :| [2. F] }";
        var all = Brackets(form).Where(v => v.StartMeasureIndex >= 3).ToList();
        Assert.Equal(new[] { (3, 4, false), (5, 6, true) },
            all.Select(v => (v.StartMeasureIndex, v.EndMeasureIndex, v.IsClosed)));

        var line = Brackets(form, "layout { voltaBracket line }").Where(v => v.StartMeasureIndex >= 3).ToList();
        Assert.Equal(new[] { (3, 4, false) }, line.Select(v => (v.StartMeasureIndex, v.EndMeasureIndex, v.IsClosed)));

        // The ending's own `@voltaBracket(all)` beats the layout's `line`.
        var over = Brackets("form { |: A [1. B] :| [2. F]@voltaBracket(all) }", "layout { voltaBracket line }")
            .Where(v => v.StartMeasureIndex >= 3).ToList();
        Assert.Equal(2, over.Count);
    }

    [Fact]
    public void Line_OnAnEndingThatFitsItsSystem_KeepsItsEndShape()
    {
        var hooked = Assert.Single(Brackets("form { |: A [1. B] :| [2. C] }", "layout { voltaBracket line }"),
            v => v.VoltaText == "2.");
        Assert.True(hooked.IsClosed);
        var open = Assert.Single(Brackets("form { |: A [1. B] :| [2. C -] }", "layout { voltaBracket line }"),
            v => v.VoltaText == "2.");
        Assert.False(open.IsClosed);
    }

    [Fact]
    public void NBars_AcrossASystemBreak_ContinueOnTheNextSystem()
    {
        // [2. F] = bars 3..6, break after 4: three bars are two pieces, cut, so straight.
        var pieces = Brackets("form { |: A [1. B] :| [2. F]@voltaBracket(3) }")
            .Where(v => v.StartMeasureIndex >= 3).ToList();
        Assert.Equal(new[] { (3, 4, false), (5, 5, false) },
            pieces.Select(v => (v.StartMeasureIndex, v.EndMeasureIndex, v.IsClosed)));
    }

    /// <summary>A piece a line break cuts runs to the SYSTEM's end — past the last bar line and
    /// over an end-of-line courtesy meter — not to the bar line. MEASURED, LilyPond 2.26.0 (Lab
    /// sessions/p692/abcvolta, the twins of both sources below): the "1." piece's line centre
    /// ends at 102.35 in both, half its 0.16 thickness inside the 102.43 the staff spans to.
    /// Until session 692 Lily# ended it 0.3 before the bar line — 3.17 short where ABC.lys bar
    /// 36's 2/4 ending is followed by a courtesy 4/4.</summary>
    [Theory]
    [InlineData("c'2 c'2 | break c'1 | c'1", false)]
    [InlineData("time 2/4 c'2 | break time 4/4 c'1 | c'1", true)]
    public void APieceTheBreakCuts_RunsToTheSystemsEnd(string ending, bool courtesyMeter)
    {
        string src = $$"""
            time 4/4
            part bass { clef bass }
            section A { bass { c'1 | c'1 | } }
            section E1 { bass { {{ending}} | } }
            section E2 { bass { e'1 | } }
            form { |: A [1. ~E1] :| [2. ~E2] }
            score { staff bass }
            """;
        var tree = SyntaxTree.Parse(src);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
        var score = new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!);
        var layout = new LayoutEngine().Layout(score);
        var first = Assert.Single(layout.VoltaBracketLayouts, v => v.VoltaText == "1.");
        var system = layout.Systems[0];
        Assert.Equal(first.EndMeasureIndex, system.Measures[^1].MeasureIndex);

        var (barRight, notationRight, _) = LilySharp.Core.Rendering.SharedRenderer.StaffRightEdges(score, system);
        Assert.Equal(courtesyMeter, notationRight > barRight + 1.0);
        Assert.Equal(notationRight - VoltaBracketEngraver.LineThickness / 2.0, first.EndX, 9);
        Assert.Equal(102.35, first.EndX, 0.01);
    }

    /// <summary>Every piece's line, start and end, where LilyPond draws its centre: the port of
    /// <c>ly:volta-bracket::calc-shorten-pair</c> and the print's bounds
    /// (<c>VoltaBracketEngraver.PieceEnds</c>). MEASURED, LilyPond 2.26.0, on each source's
    /// <c>lysc ly</c> twin (Lab sessions/p692/ends, the VoltaBracket stencil's extent in the
    /// system less the 0.08 round cap): a hook on a mid-line "|" and on the ":|." the second
    /// ending opens after, the closing hook on a ":|.", pieces cut by a break with and without
    /// a courtesy meter, after a clef, a key, a meter at the line start, a second ending
    /// opening a line, and a piece after a break with no bar line of the ending on its line
    /// (the ad-hoc −1 of the port). Until session 692 every end was a bare 0.3 inside the
    /// measure edges.</summary>
    [Theory]
    [InlineData("", "c1 | c1 |", "c2 c2 | break c1 | c1 |", "e1 |", "",
        new[] { 67.980, 102.350, 3.593, 68.303, 69.233, 102.350 })]
    // E2's `time 4/4` restates the meter E1 left: since session 737 it draws nothing, and the
    // twin omits it (MeasureBuilder.SectionHead). LilyPond re-measured on that twin (Lab
    // sessions/p737/volta/nore-p): the restated meter's 0.80 is gone from E1's end and E2's start
    // (it read 68.504 / 69.434 with the \time drawn).
    [InlineData("", "c1 | c1 |", "time 2/4 c2 | break time 4/4 c1 | c1 |", "time 4/4 e1 |", "",
        new[] { 75.685, 102.350, 6.813, 69.304, 70.234, 102.350 })]
    [InlineData("key aes major", "c1 | c1 |", "c2 c2 | break c1 | c1 |", "e1 |", "",
        new[] { 69.712, 102.350, 8.093, 69.918, 70.848, 102.350 })]
    [InlineData("", "c1 | c1 |", "c1 | c1 | break", "e1 | e1 |", "",
        new[] { 58.407, 102.350, 3.593, 102.350 })]
    [InlineData("", "c1 | c1 |", "c1 |", "e1 |", " ~A",
        new[] { 33.699, 42.569, 43.499, 51.619 })]
    [InlineData("", "c1 | c1 |", "c1 | c1 | break", "e1 | e1 |", " ~A",
        new[] { 58.407, 102.350, 3.593, 52.219 })]
    [InlineData("key aes major", "c1 | c1 |", "c2 c2 | c2 c2 | break c1 | c1 |", "e1 |", "",
        new[] { 56.362, 102.350, 8.093, 69.918, 70.848, 102.350 })]
    [InlineData("", "c1 | c1 |", "c2 c2 | c2 c2 | c2 c2 | break c1 |", "e1 |", "",
        new[] { 46.276, 102.350, 4.403, 50.883, 51.813, 102.350 })]
    public void EveryPiece_EndsWhereLilyPondsDoes(string key, string a, string e1, string e2,
        string tail, double[] lilyPond)
    {
        string src = $$"""
            time 4/4
            {{key}}
            octave absolute
            part bass { clef bass }
            section A { bass { {{a}} } }
            section E1 { bass { {{e1}} } }
            section E2 { bass { {{e2}} } }
            form { |: A [1. ~E1] :| [2. ~E2]{{tail}} }
            score { staff bass }
            """;
        var tree = SyntaxTree.Parse(src);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
        var layout = new LayoutEngine().Layout(
            new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!));
        var ends = layout.VoltaBracketLayouts
            .OrderBy(v => v.SourceIndex).ThenBy(v => v.StartMeasureIndex)
            .SelectMany(v => new[] { v.StartX, v.EndX }).ToArray();
        Assert.Equal(lilyPond.Length, ends.Length);
        for (int i = 0; i < ends.Length; i++)
            Assert.True(System.Math.Abs(ends[i] - lilyPond[i]) < 0.006,
                $"end {i}: Lily# {ends[i]:F4}, LilyPond {lilyPond[i]:F3}");
    }

    /// <summary>ONE bracket per ending however many staves the score draws — LilyPond's
    /// Volta_engraver lives in the Score context. Until session 692 every staff's walk of the
    /// form added its own copy, stacked on the same place; and the copies broke
    /// <c>LaterEndingPredecessors</c>' pairing, so the 3rd and 4th endings of a four-ending
    /// repeat paired with the ending before them instead of the first: a staff + tab score
    /// printed a bar number at a line opening with those endings, mid-bar, where LilyPond
    /// prints none (Disco Inferno "both": 36 and 54; its LilyPond twin, Lab sessions/p692/disco,
    /// numbers 3 7 11 15 23 32 41 45 49 59 63).</summary>
    [Theory]
    [InlineData("staff m")]
    [InlineData("staff m\n  tab m")]
    [InlineData("grandStaff { staff m\n  staff m }")]
    public void EveryEnding_HasOneBracket_HoweverManyStaves(string items)
    {
        string src = $$"""
            octave absolute
            time 4/4
            part m { clef bass }
            section A { m { c1 | c2 | break } }
            section E1 { m { c2 | c1 | break } }
            section E2 { m { d2 | d1 | break } }
            section E3 { m { e2 | e1 | break } }
            section E4 { m { f2 | f1 | break } }
            form { |: A [1. ~E1] :| [2. ~E2] :| [3. ~E3] :| [4. ~E4] }
            score {
              {{items}}
            }
            """;
        var tree = SyntaxTree.Parse(src);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
        var score = new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!);
        Assert.Equal(new[] { "1.", "2.", "3.", "4." },
            score.VoltaBrackets.Select(v => v.VoltaText).ToArray());

        // Every ending opens a line mid-bar (the body's last bar is a half), so no line that an
        // ending opens carries a bar number — the same on one staff and on several.
        var layout = new LayoutEngine().Layout(score);
        var numbered = layout.BarNumberLayouts.Select(b => b.MeasureIndex).ToHashSet();
        foreach (var v in score.VoltaBrackets)
            Assert.DoesNotContain(v.StartMeasureIndex, numbered);
    }

    /// <summary>A cut bracket still pairs with the next ending of its repeat: the collector
    /// pairs a repeat's endings by where the ENDING ends, not where its ink does
    /// (<c>VoltaBracketItem.EndingLastMeasureIndex</c>). A tie from the body into the second
    /// ending is the reader of that pairing.</summary>
    [Fact]
    public void ACutBracket_KeepsItsEndingsPlace()
    {
        var tree = Parse("form { |: A [1. B C]@voltaBracket(1) :| [2. D] }");
        var score = new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!);
        var first = Assert.Single(score.VoltaBrackets, v => v.VoltaText == "1.");
        Assert.Equal((1, 1, 4), (first.StartMeasureIndex, first.EndMeasureIndex, first.EndingLastMeasureIndex));
    }

    // ---- values and their diagnostics ----------------------------------------------------

    [Theory]
    [InlineData("layout { voltaBracket Line }", "'Line' is not a value of 'voltaBracket'. Values are case-sensitive: write 'line'.")]
    [InlineData("layout { voltaBracket 0 }", "'0' is not a value of 'voltaBracket'. 'voltaBracket' takes all, line or a whole number of bars (at least 1).")]
    [InlineData("layout { voltaBracket sometimes }", "'sometimes' is not a value of 'voltaBracket'. 'voltaBracket' takes all, line or a whole number of bars (at least 1).")]
    [InlineData("layout { voltaBracket }", "'voltaBracket' takes all, line or a whole number of bars (at least 1) — e.g. 'voltaBracket line' or 'voltaBracket 2'.")]
    [InlineData("layout { voltaBracket 2 3 }", "'voltaBracket' takes all, line or a whole number of bars (at least 1) — one value; '3' is extra.")]
    [InlineData("layout { VoltaBracket 2 }", "'VoltaBracket' is not a layout key. Keys are case-sensitive: write 'voltaBracket'.")]
    public void ABadLayoutValue_IsTheOrdinaryLayoutError(string layout, string message)
    {
        var tree = SyntaxTree.Parse(Source("form { |: A [1. B] :| [2. C] }", layout));
        var d = Assert.Single(SemanticValidation.Run(tree), x => x.Code is DiagnosticCodes.LayoutEntryBadValue
            or DiagnosticCodes.UnknownLayoutKey);
        Assert.Equal(message, d.Message);
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
    }

    [Theory]
    [InlineData("[2. C]@voltaBracket(Line)", "Unknown annotation '@voltaBracket(Line)' - it is ignored. Values are case-sensitive: write '@voltaBracket(line)'.")]
    [InlineData("[2. C]@voltaBracket(0)", "Unknown annotation '@voltaBracket(0)' - it is ignored. '@voltaBracket' takes all, line or a whole number of bars (at least 1): @voltaBracket(line), @voltaBracket(2).")]
    [InlineData("[2. C]@voltaBracket", "Unknown annotation '@voltaBracket' - it is ignored. '@voltaBracket' takes all, line or a whole number of bars (at least 1): @voltaBracket(line), @voltaBracket(2).")]
    [InlineData("[2. C]@VoltaBracket(2)", "Unknown annotation '@VoltaBracket(2)' on an ending - it is ignored. Names are case-sensitive: write '@voltaBracket(2)'.")]
    [InlineData("[2. C]@accent", "Unknown annotation '@accent' on an ending - it is ignored. An ending takes '@voltaBracket(…)': [1. B C]@voltaBracket(2).")]
    public void ABadEndingAnnotation_IsTheOrdinaryAnnotationWarning(string ending, string message)
    {
        var tree = SyntaxTree.Parse(Source($"form {{ |: A [1. B] :| {ending} }}"));
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
        var d = Assert.Single(SemanticValidation.Run(tree), x => x.Code == DiagnosticCodes.UnknownAnnotation);
        Assert.Equal(message, d.Message);
        Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
        // …and the bracket keeps the layout's length (C = bars 3..4) and its hook.
        var second = Brackets($"form {{ |: A [1. B] :| {ending} }}").Single(v => v.VoltaText == "2.");
        Assert.Equal((3, 4, true), (second.StartMeasureIndex, second.EndMeasureIndex, second.IsClosed));
    }

    [Fact]
    public void VoltaBracketOnANote_IsIgnoredWithWhereItBelongs()
    {
        var tree = SyntaxTree.Parse(Head.Replace("c'1 |", "c'1@voltaBracket(2) |")
            + "form { |: A [1. B] :| [2. C] }\nscore { staff m }\n");
        var d = Assert.Single(SemanticValidation.Run(tree), x => x.Code == DiagnosticCodes.UnknownAnnotation);
        Assert.Equal("'@voltaBracket(2)' is ignored: '@voltaBracket' belongs on a form ending, glued to its ']' "
            + "- [1. B C]@voltaBracket(line).", d.Message);
    }

    // ---- MusicXML ------------------------------------------------------------------------

    private static List<(int Measure, string Type, string Number)> XmlEndings(string form, string layout = "")
    {
        var doc = new MusicXmlExporter().Export(Parse(form, layout)).ToXml();
        return [.. doc.Descendants("measure").SelectMany((m, i) => m.Descendants("ending").Select(e =>
            (i, e.Attribute("type")!.Value, e.Attribute("number")!.Value)))];
    }

    [Fact]
    public void MusicXml_StopForAHook_DiscontinueForAnOpenEnd()
    {
        // A=0, B=1..2, C=3..4
        Assert.Equal(new[] { (1, "start", "1"), (2, "stop", "1"), (3, "start", "2"), (4, "stop", "2") },
            XmlEndings("form { |: A [1. B] :| [2. C] }"));
        Assert.Equal(new[] { (1, "start", "1"), (2, "discontinue", "1"), (3, "start", "2"), (4, "discontinue", "2") },
            XmlEndings("form { |: A [1. B -] :| [2. C -] }"));
    }

    [Fact]
    public void MusicXml_ACutBracket_StopsAtTheCut_Discontinued()
    {
        // [1. B C] = 1..4 cut to 2 bars; [2. D] = 5..6 not cut by 5.
        Assert.Equal(new[] { (1, "start", "1"), (2, "discontinue", "1"), (5, "start", "2"), (6, "stop", "2") },
            XmlEndings("form { |: A [1. B C] :| [2. D]@voltaBracket(5) }", "layout { voltaBracket 2 }"));
        // The repeat bar stays where the ending ends.
        var doc = new MusicXmlExporter().Export(Parse("form { |: A [1. B C]@voltaBracket(1) :| [2. D] }")).ToXml();
        var measures = doc.Descendants("measure").ToList();
        Assert.Contains(measures[4].Descendants("repeat"), r => r.Attribute("direction")?.Value == "backward");
    }

    // ---- the editor ----------------------------------------------------------------------

    [Theory]
    [InlineData("layout { voltaBracket ", "AfterLayoutVoltaBracket")]
    [InlineData("form { |: A [1. B]@", "AfterEndingAt")]
    [InlineData("form { |: A [1. B]@vol", "AfterEndingAt")]
    [InlineData("form { |: A [1. B]@voltaBracket(", "InVoltaBracketAnnotation")]
    [InlineData("form { |: A [1. B -]@voltaBracket(li", "InVoltaBracketAnnotation")]
    public void Completion_KnowsTheKeyAndTheAnnotation(string text, string context)
    {
        Assert.Equal(context, LilySharp.Lsp.LilySharpLanguageServer.GetCompletionContext(text, text.Length).ToString());
    }

    [Fact]
    public void Completion_OffersTheValues_AndTheOneEndingAnnotation()
    {
        var values = LilySharp.Lsp.LilySharpLanguageServer.GetVoltaBracketCompletions().Items.Select(i => i.Label).ToList();
        Assert.Equal(new[] { "all", "line", "N" }, values);
        var names = LilySharp.Lsp.LilySharpLanguageServer.GetEndingAnnotationCompletions().Items;
        Assert.Equal("voltaBracket(${1:line})", Assert.Single(names).InsertText);
    }

    [Theory]
    [InlineData("layout { voltaBracket line }\n", "layout { volta", "**Layout** `voltaBracket`")]
    [InlineData("", "[1. B]@volta", "**Ending bracket length** `@voltaBracket`")]
    [InlineData("", "[1. B", "**Ending end** `]`")]
    [InlineData("", "[2. C ", "**Ending end** `-]`")]
    public void Hover_ExplainsTheKeyTheAnnotationAndTheEnd(string layout, string before, string head)
    {
        string src = Source("form { |: A [1. B]@voltaBracket(2) :| [2. C -] }", layout);
        var node = SyntaxTree.Parse(src).FindNode(src.IndexOf(before, System.StringComparison.Ordinal) + before.Length)!;
        var hover = LilySharp.Lsp.LanguageReference.Hover(node);
        Assert.NotNull(hover);
        Assert.StartsWith(head, hover);
    }

    // ---- the LilyPond twin ---------------------------------------------------------------

    private static string Twin(string form, string layout = "")
        => new LilySharp.Core.LilyPond.LilyPondExporter().Export(Parse(form, layout));

    private static string AlternativeBranch(string ly, int index)
    {
        int alt = ly.IndexOf("\\alternative", System.StringComparison.Ordinal);
        Assert.True(alt > 0, ly);
        // Each branch opens on a line of its own that is a lone '{'.
        var lines = ly[alt..].Split('\n');
        var opens = Enumerable.Range(1, lines.Length - 1).Where(i => lines[i].Trim() == "{").ToList();
        Assert.True(opens.Count > index, ly[alt..]);
        int to = index + 1 < opens.Count ? opens[index + 1] : lines.Length;
        return string.Join("\n", lines[opens[index]..to]);
    }

    private const string OpenEnd = "\\once \\override Score.VoltaBracket.edge-height = #'(2.0 . 0.0)";
    private const string ForceHook = "(ly:grob-set-property! grob 'edge-height '(2.0 . 2.0))";

    [Fact]
    public void Twin_AHookedFirstEnding_IsLilyPondsDefault_AHookedLastOneIsForced()
    {
        string ly = Twin("form { |: A [1. B] :| [2. C] }");
        Assert.DoesNotContain("VoltaBracket", AlternativeBranch(ly, 0));
        Assert.Contains(ForceHook, AlternativeBranch(ly, 1));
        Assert.DoesNotContain(OpenEnd, ly);
    }

    [Fact]
    public void Twin_AnOpenEnd_ZeroesTheRightEdge()
    {
        string ly = Twin("form { |: A [1. B -] :| [2. C -] }");
        Assert.Contains(OpenEnd, AlternativeBranch(ly, 0));
        Assert.Contains(OpenEnd, AlternativeBranch(ly, 1));
        Assert.DoesNotContain(ForceHook, ly);
    }

    [Fact]
    public void Twin_NBars_SetMusicalLength_AndOpenTheEnd()
    {
        string ly = Twin("form { |: A [1. B C]@voltaBracket(3) :| [2. D]@voltaBracket(5) }");
        string first = AlternativeBranch(ly, 0);
        Assert.Contains("\\once \\override Score.VoltaBracket.musical-length = #(ly:make-moment 3/1)", first);
        Assert.Contains(OpenEnd, first);
        // Five bars of a two-bar ending: not cut — the ordinary hooked last ending.
        string second = AlternativeBranch(ly, 1);
        Assert.DoesNotContain("musical-length", second);
        Assert.Contains(ForceHook, second);
    }

    /// <summary>N bars are the bars' OWN lengths, not N of the ending's opening meter: G's
    /// first two bars are a whole note and a 3/4 bar, so the twin's bracket ends 7/4 after its
    /// start — the bar the page ends it on (<c>VoltaBracketLength.LastBar</c> counts bars).
    /// Until 2026-09-29 (第663 ⒀) the twin wrote 2 × 4/4 and ran a quarter into bar 3. An
    /// ending of two sections sums across them (A's whole note, then G's first two bars).</summary>
    [Theory]
    [InlineData("form { |: A [1. B] :| [2. G]@voltaBracket(2) }", "7/4")]
    [InlineData("form { |: A [1. B] :| [2. A G]@voltaBracket(3) }", "11/4")]
    [InlineData("form { |: A [1. B] :| [2. G]@voltaBracket(1) }", "1/1")]
    public void Twin_NBars_AreTheBarsOwnLengths(string form, string moment)
    {
        string branch = AlternativeBranch(Twin(form), 1);
        Assert.Contains($"\\once \\override Score.VoltaBracket.musical-length = #(ly:make-moment {moment})", branch);
        Assert.Contains(OpenEnd, branch);
        // …and the page cuts the same bracket at its second bar (G starts at bar 3 of the
        // printed A B G: bars 3 and 4), straight-ended.
        if (form.Contains("[2. G]@voltaBracket(2)"))
        {
            var second = Assert.Single(Brackets(form), v => v.VoltaText == "2.");
            Assert.Equal((3, 4, false), (second.StartMeasureIndex, second.EndMeasureIndex, second.IsClosed));
        }
    }

    [Fact]
    public void Twin_Line_KillsEveryPieceButTheFirst()
    {
        string ly = Twin("form { |: A [1. B] :| [2. F -] }", "layout { voltaBracket line }");
        string second = AlternativeBranch(ly, 1);
        Assert.Contains("(ly:grob-suicide! grob)", second);
        Assert.Contains(OpenEnd, second);
        Assert.Contains("(ly:grob-suicide! grob)", AlternativeBranch(ly, 0));
    }
}
