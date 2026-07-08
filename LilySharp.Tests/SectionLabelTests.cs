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

using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Per-occurrence section display labels in structure
/// (<c>structure { First Second First "First (reprise)" }</c>) and Unicode
/// section/part/phrase identifiers.
/// </summary>
[Trait("Category", "Unit")]
public class SectionLabelTests
{
    private static string?[] SectionLabels(string source)
    {
        var score = new MeasureCollector().Collect(SyntaxTree.Parse(source));
        return score.Voice.Measures.Select(m => m.SectionLabel).ToArray();
    }

    private const string ReuseSource = """
        part melody
        phrase pa { c4 d e f | }
        section First { melody { $pa } }
        section Second { melody { $pa } }
        structure { First Second First "First (reprise)" }
        score "x" { staff melody }
        """;

    [Fact]
    public void OccurrenceLabel_OverridesSectionNameForThatOccurrence()
    {
        var labels = SectionLabels(ReuseSource);

        Assert.Equal(3, labels.Length);
        Assert.Equal("First", labels[0]);
        Assert.Equal("Second", labels[1]);
        Assert.Equal("First (reprise)", labels[2]);
    }

    [Fact]
    public void EmptyLabel_SuppressesTheMark()
    {
        var labels = SectionLabels(ReuseSource.Replace("\"First (reprise)\"", "\"\""));

        Assert.Equal("First", labels[0]);
        Assert.Null(labels[2]);
    }

    [Fact]
    public void UnicodeIdentifiers_ParseAndLabel()
    {
        var labels = SectionLabels("""
            part メロディ
            phrase 動機 { c4 d e f | }
            section イントロ { メロディ { $動機 } }
            structure { イントロ イントロ "イントロ(再現)" }
            score "x" { staff メロディ }
            """);

        Assert.Equal("イントロ", labels[0]);
        Assert.Equal("イントロ(再現)", labels[1]);
    }

    [Fact]
    public void SilentReference_RendersMusicWithoutLabel()
    {
        // ~B renders B's music but shows no label (regression: it used to drop the
        // whole section). A keeps its label; B's slot is present but null.
        var labels = SectionLabels("""
            part melody
            section A { melody { c4 d e f | } }
            section B { melody { g4 a b c | } }
            structure { A ~B }
            score "x" { staff melody }
            """);

        Assert.Equal(new string?[] { "A", null }, labels);
    }

    [Fact]
    public void NoStructure_UsesSectionDeclarationOrder()
    {
        // With no `structure { }`, sections play in the order they were declared
        // (source order). 'Zebra' is declared before 'Alpha', so an alphabetical
        // or hash-bucket order would fail this — only source order passes.
        var labels = SectionLabels("""
            part melody
            section Zebra { melody { c4 d e f | } }
            section Alpha { melody { g4 a b c | } }
            score "x" { staff melody }
            """);

        Assert.Equal(new[] { "Zebra", "Alpha" }, labels);
    }

    [Fact]
    public void UnicodeIdentifiers_NoDiagnostics()
    {
        var tree = SyntaxTree.Parse("""
            part メロディ
            phrase 動機 { c4 d e f | }
            section イントロ { メロディ { $動機 } }
            structure { イントロ }
            score "x" { staff メロディ }
            """);
        Assert.Empty(tree.Diagnostics);
    }

    // --- Section rehearsal box is suppressed with only one distinct section ---
    // (nothing to navigate between). This is a LAYOUT-stage decision, so these
    // assert on the emitted SectionLabel MARKS, not the measures' SectionLabel
    // property (which stays set for reuse hashing).

    private static int SectionLabelMarkCount(string source)
    {
        var score = new MeasureCollector().Collect(SyntaxTree.Parse(source));
        var marks = MusicMarkEngraver.BuildAllMarks(
            score.MusicMarks, score.Voice.Measures, score.Tempo,
            score.SwingSubdivision, score.TempoText, score.TempoBeatUnit, score.TempoDots);
        return marks.Count(m => m.Type == MusicMarkType.SectionLabel);
    }

    [Fact]
    public void SingleSection_EmitsNoSectionLabelBox()
    {
        Assert.Equal(0, SectionLabelMarkCount("""
            section Main { melody { c4 d e f | g1 } }
            structure { Main }
            score "x" { staff melody }
            """));
    }

    [Fact]
    public void OneSectionRepeated_EmitsNoBox()
    {
        // `structure { A A }` is one distinct section repeated — nothing to jump to.
        Assert.Equal(0, SectionLabelMarkCount("""
            section A { melody { c4 d e f | } }
            structure { A A }
            score "x" { staff melody }
            """));
    }

    [Fact]
    public void TwoDistinctSections_KeepBothBoxes()
    {
        Assert.Equal(2, SectionLabelMarkCount("""
            section Intro { melody { c4 d e f | } }
            section Verse { melody { g4 f e d | } }
            structure { Intro Verse }
            score "x" { staff melody }
            """));
    }

    [Fact]
    public void ExplicitDisplayLabel_CountsAsDistinctAndKeepsBoxes()
    {
        // `A "A2"` is an explicit user label; alongside a plain `A` that is two
        // distinct labels, so the boxes stay (the user asked to tell them apart).
        Assert.Equal(2, SectionLabelMarkCount("""
            section A { melody { c4 d e f | } }
            structure { A A "A2" }
            score "x" { staff melody }
            """));
    }
}
