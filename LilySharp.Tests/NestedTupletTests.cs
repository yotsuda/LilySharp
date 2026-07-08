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

using System.Collections.Immutable;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
public class NestedTupletTests
{
    // --- TupletBracketItem.NestingDepth ---

    [Fact]
    public void TupletBracketItem_NestingDepth_DefaultZero()
    {
        var item = new TupletBracketItem(3, 2, 0, 2, 0, 0);
        Assert.Equal(0, item.NestingDepth);
    }

    [Fact]
    public void TupletBracketItem_NestingDepth_ExplicitValue()
    {
        var item = new TupletBracketItem(3, 2, 0, 2, 0, 0, NestingDepth: 1);
        Assert.Equal(1, item.NestingDepth);
    }

    [Fact]
    public void TupletBody_TieAndSlurMarkersAreDetected()
    {
        // Regression: ties/slurs/beams written inside a tuplet body were dropped
        // because ProcessTuplet created notes with no one-node lookahead.
        var source = @"
time 4/4
part m { clef treble }
section A { m { tuplet 3/2 { c'8( d'~ e') } r4 c'2 | } }
structure { A }
score x { staff m }
";
        var tree = SyntaxTree.Parse(source);
        var spec = RenderSpecParser.FindFirst(tree);
        Assert.NotNull(spec);
        var score = new MeasureCollector().CollectMultiStaff(tree, spec!);
        var notes = score.StaffGroups[0].Staves[0].Voices[0]
            .Measures[0].Items.OfType<NoteItem>().ToList();
        Assert.True(notes[0].HasSlurStart);  // c'(
        Assert.True(notes[1].HasTieStart);   // d'~
        Assert.True(notes[2].HasSlurEnd);    // e')
    }

    // --- Parser: nested tuplet syntax ---

    [Fact]
    public void Parser_NestedTuplet_ParsesWithoutError()
    {
        var source = "tuplet 3/2 { c8 tuplet 3/2 { d8 e f } g }";
        var tree = SyntaxTree.Parse(source);

        Assert.False(tree.HasErrors);
        var tuplets = tree.GetNodes<TupletExpressionSyntax>().ToList();
        Assert.Equal(2, tuplets.Count);
    }

    [Fact]
    public void Parser_NestedTuplet_OuterHasCorrectRatio()
    {
        var source = "tuplet 3/2 { c8 tuplet 5/4 { d8 e f g a } b }";
        var tree = SyntaxTree.Parse(source);

        var tuplets = tree.GetNodes<TupletExpressionSyntax>().ToList();
        // Outer tuplet: 3/2
        Assert.Equal(3, tuplets[0].TupletRatio);
        Assert.Equal(2, tuplets[0].BaseDivision);
    }

    // --- MeasureCollector: nested tuplet bracket detection ---

    [Fact]
    public void Collector_NestedTuplet_CreatesTwoBrackets()
    {
        var source = "tuplet 3/2 { c8 tuplet 3/2 { d8 e f } g }";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        Assert.Equal(2, score.TupletBrackets.Length);
    }

    [Fact]
    public void Collector_NestedTuplet_OuterDepthZero()
    {
        var source = "tuplet 3/2 { c8 tuplet 3/2 { d8 e f } g }";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        // Outer bracket should have depth 0
        var outerBracket = score.TupletBrackets
            .First(t => t.NestingDepth == 0);
        Assert.Equal(3, outerBracket.Numerator);
        Assert.Equal(2, outerBracket.Denominator);
    }

    [Fact]
    public void Collector_NestedTuplet_InnerDepthOne()
    {
        var source = "tuplet 3/2 { c8 tuplet 3/2 { d8 e f } g }";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        // Inner bracket should have depth 1
        var innerBracket = score.TupletBrackets
            .First(t => t.NestingDepth == 1);
        Assert.Equal(3, innerBracket.Numerator);
        Assert.Equal(2, innerBracket.Denominator);
    }

    [Fact]
    public void Collector_NestedTuplet_AllNotesCollected()
    {
        // Outer: c8 + inner(d8 e f) + g = 5 notes total
        var source = "tuplet 3/2 { c8 tuplet 3/2 { d8 e f } g }";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        var measure = score.Voice.Measures[0];
        // All 5 notes should be collected as items
        Assert.Equal(5, measure.Items.Length);
    }

    [Fact]
    public void Collector_NestedTuplet_InnerBracketCoversInnerNotes()
    {
        // Items: c(0), d(1), e(2), f(3), g(4)
        // Inner bracket should cover d, e, f (indices 1-3)
        var source = "tuplet 3/2 { c8 tuplet 3/2 { d8 e f } g }";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        var innerBracket = score.TupletBrackets
            .First(t => t.NestingDepth == 1);
        Assert.Equal(1, innerBracket.StartNoteIndex);
        Assert.Equal(3, innerBracket.EndNoteIndex);
    }

    [Fact]
    public void Collector_NestedTuplet_OuterBracketCoversAllNotes()
    {
        // Items: c(0), d(1), e(2), f(3), g(4)
        // Outer bracket should cover all (indices 0-4)
        var source = "tuplet 3/2 { c8 tuplet 3/2 { d8 e f } g }";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        var outerBracket = score.TupletBrackets
            .First(t => t.NestingDepth == 0);
        Assert.Equal(0, outerBracket.StartNoteIndex);
        Assert.Equal(4, outerBracket.EndNoteIndex);
    }

    // --- Simple tuplet still works (regression) ---

    [Fact]
    public void Collector_SimpleTuplet_StillWorks()
    {
        var source = "tuplet 3/2 { c8 d e }";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        Assert.Single(score.TupletBrackets);
        var bracket = score.TupletBrackets[0];
        Assert.Equal(0, bracket.NestingDepth);
        Assert.Equal(3, bracket.Numerator);
        Assert.Equal(0, bracket.StartNoteIndex);
        Assert.Equal(2, bracket.EndNoteIndex);
    }

    // --- Different nested ratios ---

    [Fact]
    public void Collector_NestedTuplet_DifferentRatios()
    {
        // Outer 3/2 with inner 5/4
        var source = "tuplet 3/2 { c8 tuplet 5/4 { d8 e f g a } b }";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        Assert.Equal(2, score.TupletBrackets.Length);

        var outer = score.TupletBrackets.First(t => t.NestingDepth == 0);
        Assert.Equal(3, outer.Numerator);
        Assert.Equal(2, outer.Denominator);

        var inner = score.TupletBrackets.First(t => t.NestingDepth == 1);
        Assert.Equal(5, inner.Numerator);
        Assert.Equal(4, inner.Denominator);
    }

    // --- No double-counting of notes ---

    [Fact]
    public void Collector_NestedTuplet_NoteCountCorrect()
    {
        // tuplet 3/2 { c d e } should have exactly 3 notes, 1 bracket
        // Adding more context: c4 before and after
        var source = "c4 tuplet 3/2 { d8 e f } c4";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        var measure = score.Voice.Measures[0];
        // c4 + (d8 e f) + c4 = 5 items
        Assert.Equal(5, measure.Items.Length);
        Assert.Single(score.TupletBrackets);
    }

    // --- Triple nesting (depth 2) ---

    [Fact]
    public void Collector_TripleNestedTuplet_ThreeBrackets()
    {
        var source = "tuplet 3/2 { c8 tuplet 3/2 { d8 tuplet 3/2 { e8 f g } a } b }";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        Assert.Equal(3, score.TupletBrackets.Length);

        var depths = score.TupletBrackets.Select(t => t.NestingDepth).OrderBy(d => d).ToList();
        Assert.Equal(0, depths[0]);
        Assert.Equal(1, depths[1]);
        Assert.Equal(2, depths[2]);
    }

    [Fact]
    public void Collector_TripleNestedTuplet_AllNotesCollected()
    {
        // c(0), d(1), e(2), f(3), g(4), a(5), b(6) = 7 notes
        var source = "tuplet 3/2 { c8 tuplet 3/2 { d8 tuplet 3/2 { e8 f g } a } b }";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        var measure = score.Voice.Measures[0];
        Assert.Equal(7, measure.Items.Length);
    }

    // --- Nested tuplet with rest ---

    [Fact]
    public void Collector_NestedTuplet_WithRest()
    {
        var source = "tuplet 3/2 { c8 tuplet 3/2 { d8 r e } f }";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        Assert.Equal(2, score.TupletBrackets.Length);
        var measure = score.Voice.Measures[0];
        // c + d + r + e + f = 5 items
        Assert.Equal(5, measure.Items.Length);
        Assert.IsType<RestItem>(measure.Items[2]);
    }

    // --- Bracket-visibility and slope tests ---
    // LILYPOND-REF: scm/define-grobs.scm bracket-visibility = if-no-beam
    // LILYPOND-REF: lily/tuplet-bracket.cc:200-350 slope

    private static ImmutableArray<MeasureLayout> CreateTestMeasureLayouts(int itemCount)
    {
        var items = ImmutableArray.CreateBuilder<ItemLayout>(itemCount);
        for (int i = 0; i < itemCount; i++)
            items.Add(new ItemLayout(i, 2.0 + i * 3.0, 1.0));
        return ImmutableArray.Create(new MeasureLayout(0, 0, 30, items.ToImmutable()));
    }

    [Fact]
    public void Calculate_NoBeams_ShowsBracket()
    {
        var tuplet = new TupletBracketItem(3, 2, 0, 2, 0, 0);
        var measures = CreateTestMeasureLayouts(3);
        var systems = ImmutableArray.Create(new SystemLayout(0, 10.0, 100.0, 5.0, measures));

        var result = TupletBracketEngraver.Calculate(
            ImmutableArray.Create(tuplet), systems, measures,
            ImmutableArray<Measure>.Empty, ImmutableArray<BeamGroup>.Empty);

        Assert.Single(result);
        Assert.True(result[0].ShowBracket, "Bracket should be visible when no beams present");
    }

    [Fact]
    public void Calculate_AllBeamed_HidesBracket()
    {
        var tuplet = new TupletBracketItem(3, 2, 0, 2, 0, 0);
        var measures = CreateTestMeasureLayouts(3);
        var systems = ImmutableArray.Create(new SystemLayout(0, 10.0, 100.0, 5.0, measures));

        // Create a beam group that covers the entire tuplet range
        var dummyNote = new NoteItem(0, Fraction.Eighth, 0, null, false, 0);
        var members = ImmutableArray.Create(
            new BeamMember(dummyNote, 1, 1, 1, 0, 0),
            new BeamMember(dummyNote, 1, 1, 1, 0, 1),
            new BeamMember(dummyNote, 1, 1, 1, 0, 2));
        var beam = new BeamGroup(members, 0, 0, true);

        var result = TupletBracketEngraver.Calculate(
            ImmutableArray.Create(tuplet), systems, measures,
            ImmutableArray<Measure>.Empty, ImmutableArray.Create(beam));

        Assert.Single(result);
        Assert.False(result[0].ShowBracket, "Bracket should be hidden when all notes are beamed");
    }

    [Fact]
    public void Calculate_PartiallyBeamed_ShowsBracket()
    {
        var tuplet = new TupletBracketItem(3, 2, 0, 2, 0, 0);
        var measures = CreateTestMeasureLayouts(3);
        var systems = ImmutableArray.Create(new SystemLayout(0, 10.0, 100.0, 5.0, measures));

        // Beam covers only first 2 of 3 notes
        var dummyNote = new NoteItem(0, Fraction.Eighth, 0, null, false, 0);
        var members = ImmutableArray.Create(
            new BeamMember(dummyNote, 1, 1, 1, 0, 0),
            new BeamMember(dummyNote, 1, 1, 1, 0, 1));
        var beam = new BeamGroup(members, 0, 0, true);

        var result = TupletBracketEngraver.Calculate(
            ImmutableArray.Create(tuplet), systems, measures,
            ImmutableArray<Measure>.Empty, ImmutableArray.Create(beam));

        Assert.Single(result);
        Assert.True(result[0].ShowBracket, "Bracket should be visible when only partially beamed");
    }

    [Fact]
    public void Calculate_SlopedBracket_DifferentStartEndY()
    {
        var tuplet = new TupletBracketItem(3, 2, 0, 2, 0, 0);
        var measures = CreateTestMeasureLayouts(3);
        var systems = ImmutableArray.Create(new SystemLayout(0, 10.0, 100.0, 5.0, measures));

        // Create notes with ascending pitch (different staff positions)
        var noteItems = ImmutableArray.Create<MusicItem>(
            new NoteItem(-2, Fraction.Eighth, 0, null, false, 0),  // low
            new NoteItem(0, Fraction.Eighth, 0, null, false, 0),   // middle
            new NoteItem(2, Fraction.Eighth, 0, null, false, 0));  // high
        var musicMeasures = ImmutableArray.Create(new Measure(noteItems, BarlineType.None, BarlineType.Single, null, 0, 0));

        var result = TupletBracketEngraver.Calculate(
            ImmutableArray.Create(tuplet), systems, measures, musicMeasures);

        Assert.Single(result);
        // Ascending notes should create a sloped bracket
        // (StartY and EndY may differ due to pitch contour)
        Assert.True(result[0].StartX < result[0].EndX);
    }
}
