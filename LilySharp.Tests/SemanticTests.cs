using Xunit;
using LilySharp.Core.Syntax;
using LilySharp.Core.Semantics;

namespace LilySharp.Tests;

public class SemanticTests
{
    // ========== Fraction Tests ==========

    [Fact]
    public void Fraction_Reduces()
    {
        var f = new Fraction(2, 4);
        Assert.Equal(1, f.Numerator);
        Assert.Equal(2, f.Denominator);
    }

    [Fact]
    public void Fraction_Addition()
    {
        var a = new Fraction(1, 4);
        var b = new Fraction(1, 4);
        var result = a + b;
        Assert.Equal(new Fraction(1, 2), result);
    }

    [Fact]
    public void Fraction_QuarterNotes()
    {
        var quarter = Fraction.FromNoteValue(4);
        Assert.Equal(new Fraction(1, 4), quarter);

        var fourQuarters = quarter + quarter + quarter + quarter;
        Assert.Equal(Fraction.Whole, fourQuarters);
    }

    [Fact]
    public void Fraction_DottedQuarter()
    {
        var quarter = Fraction.Quarter;
        var dotted = quarter.Dotted(1);

        // Dotted quarter = 1/4 + 1/8 = 3/8
        Assert.Equal(new Fraction(3, 8), dotted);
    }

    [Fact]
    public void Fraction_DoubleDotted()
    {
        var quarter = Fraction.Quarter;
        var doubleDotted = quarter.Dotted(2);

        // Double-dotted quarter = 1/4 + 1/8 + 1/16 = 7/16
        Assert.Equal(new Fraction(7, 16), doubleDotted);
    }

    [Fact]
    public void Fraction_Comparison()
    {
        Assert.True(Fraction.Quarter < Fraction.Half);
        Assert.True(Fraction.Half > Fraction.Quarter);
        Assert.True(Fraction.Quarter == new Fraction(1, 4));
    }

    // ========== Duration Calculator Tests ==========

    [Fact]
    public void DurationCalculator_SimpleNote()
    {
        var tree = SyntaxTree.Parse("c4");
        var root = tree.GetRoot();
        var note = root.DescendantNodes<NoteSyntax>().First();

        var duration = DurationCalculator.GetDuration(note, Fraction.Quarter);
        Assert.Equal(Fraction.Quarter, duration);
    }

    [Fact]
    public void DurationCalculator_HalfNote()
    {
        var tree = SyntaxTree.Parse("c2");
        var root = tree.GetRoot();
        var note = root.DescendantNodes<NoteSyntax>().First();

        var duration = DurationCalculator.GetDuration(note, Fraction.Quarter);
        Assert.Equal(Fraction.Half, duration);
    }

    [Fact]
    public void DurationCalculator_DottedNote()
    {
        var tree = SyntaxTree.Parse("c4.");
        var root = tree.GetRoot();
        var note = root.DescendantNodes<NoteSyntax>().First();

        var duration = DurationCalculator.GetDuration(note, Fraction.Quarter);
        Assert.Equal(new Fraction(3, 8), duration);
    }

    [Fact]
    public void DurationCalculator_InheritedDuration()
    {
        var tree = SyntaxTree.Parse("c4 d e f");
        var root = tree.GetRoot();
        var notes = root.DescendantNodes<NoteSyntax>().ToList();

        // First note has explicit duration
        Assert.Equal(Fraction.Quarter, DurationCalculator.GetDuration(notes[0], Fraction.Whole));

        // Subsequent notes inherit (no Duration node)
        Assert.Null(notes[1].Duration);
        Assert.Null(notes[2].Duration);
        Assert.Null(notes[3].Duration);
    }

    // ========== Measure Validator Tests ==========

    [Fact]
    public void MeasureValidator_CompleteMeasure()
    {
        var tree = SyntaxTree.Parse("{ c4 d e f | }");
        var validator = new MeasureValidator();
        validator.Validate(tree);

        // 4 quarter notes = 1 whole = 4/4 time - no warnings
        Assert.Empty(validator.Diagnostics);
    }

    [Fact]
    public void MeasureValidator_IncompleteMeasure()
    {
        var tree = SyntaxTree.Parse("{ c4 d e | }");
        var validator = new MeasureValidator();
        validator.Validate(tree);

        // 3 quarter notes < 4/4 time
        Assert.Single(validator.Diagnostics);
        Assert.Contains("less than", validator.Diagnostics[0].Message);
    }

    [Fact]
    public void MeasureValidator_OverfullMeasure()
    {
        var tree = SyntaxTree.Parse("{ c4 d e f g | }");
        var validator = new MeasureValidator();
        validator.Validate(tree);

        // 5 quarter notes > 4/4 time
        Assert.Single(validator.Diagnostics);
        Assert.Contains("exceeds", validator.Diagnostics[0].Message);
    }

    [Fact]
    public void MeasureValidator_ThreeFourTime()
    {
        var tree = SyntaxTree.Parse("{ c4 d e | }");
        var validator = new MeasureValidator();
        validator.SetTimeSignature(3, 4);
        validator.Validate(tree);

        // 3 quarter notes = 3/4 time - no warnings
        Assert.Empty(validator.Diagnostics);
    }

    [Fact]
    public void MeasureValidator_MultipleMeasures()
    {
        var tree = SyntaxTree.Parse("{ c4 d e f | g2 g | }");
        var validator = new MeasureValidator();
        validator.Validate(tree);

        // Both measures are complete in 4/4
        Assert.Empty(validator.Diagnostics);
    }

    [Fact]
    public void MeasureValidator_MixedDurations()
    {
        var tree = SyntaxTree.Parse("{ c2 d4 e | }");
        var validator = new MeasureValidator();
        validator.Validate(tree);

        // c2 (1/2) + d4 (1/4) + e (1/4) = 1 whole = 4/4
        Assert.Empty(validator.Diagnostics);
    }

    [Fact]
    public void MeasureValidator_DottedNotes()
    {
        var tree = SyntaxTree.Parse("{ c4. d8 e4 f | }");
        var validator = new MeasureValidator();
        validator.Validate(tree);

        // c4. (3/8) + d8 (1/8) + e4 (1/4) + f (1/4) = 3/8 + 1/8 + 1/4 + 1/4 = 1
        Assert.Empty(validator.Diagnostics);
    }

    [Fact]
    public void MeasureValidator_WithRests()
    {
        var tree = SyntaxTree.Parse("{ c4 r d r | }");
        var validator = new MeasureValidator();
        validator.Validate(tree);

        // c4 + r4 + d4 + r4 = 4 quarters = 4/4
        Assert.Empty(validator.Diagnostics);
    }
}