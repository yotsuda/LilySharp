using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

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
}
