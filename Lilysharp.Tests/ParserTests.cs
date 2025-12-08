using Xunit;
using Lilysharp.Core.Syntax;
using Lilysharp.Core.Syntax.InternalSyntax;

namespace Lilysharp.Tests;

public class ParserTests
{
    [Fact]
    public void ParseSingleNote()
    {
        var tree = SyntaxTree.Parse("c");
        Assert.NotNull(tree.Root);
        Assert.Equal(2, tree.Root.SlotCount); // note + EOF
    }

    [Fact]
    public void ParseNoteWithDuration()
    {
        var tree = SyntaxTree.Parse("c4");
        var note = tree.Root.GetSlot(0) as NoteGreen;
        
        Assert.NotNull(note);
        Assert.Equal(SyntaxKind.Note, note.Kind);
    }

    [Fact]
    public void ParseNoteWithOctave()
    {
        var tree = SyntaxTree.Parse("c''");
        var note = tree.Root.GetSlot(0) as NoteGreen;
        
        Assert.NotNull(note);
        var pitch = note.GetSlot(0) as PitchGreen;
        Assert.NotNull(pitch);
        // pitch token + 2 apostrophes
        Assert.Equal(3, pitch.SlotCount);
    }

    [Fact]
    public void ParseNoteSequence()
    {
        var tree = SyntaxTree.Parse("c4 d e f");
        
        // 4 notes + EOF
        Assert.Equal(5, tree.Root.SlotCount);
        
        for (int i = 0; i < 4; i++)
        {
            var note = tree.Root.GetSlot(i);
            Assert.Equal(SyntaxKind.Note, note?.Kind);
        }
    }

    [Fact]
    public void ParseRest()
    {
        var tree = SyntaxTree.Parse("r4");
        var rest = tree.Root.GetSlot(0) as RestGreen;
        
        Assert.NotNull(rest);
        Assert.Equal(SyntaxKind.Rest, rest.Kind);
    }

    [Fact]
    public void ParseChord()
    {
        var tree = SyntaxTree.Parse("<c e g>4");
        var chord = tree.Root.GetSlot(0) as ChordGreen;
        
        Assert.NotNull(chord);
        Assert.Equal(SyntaxKind.Chord, chord.Kind);
    }

    [Fact]
    public void ParseBarline()
    {
        var tree = SyntaxTree.Parse("c d | e f");
        
        // c, d, |, e, f, EOF
        Assert.Equal(6, tree.Root.SlotCount);
        
        var barline = tree.Root.GetSlot(2) as BarlineGreen;
        Assert.NotNull(barline);
        Assert.Equal(SyntaxKind.Barline, barline.Kind);
    }

    [Fact]
    public void ParseTie()
    {
        var tree = SyntaxTree.Parse("c4~ c4");
        
        // note, tie, note, EOF
        Assert.Equal(4, tree.Root.SlotCount);
        
        var tie = tree.Root.GetSlot(1) as TieGreen;
        Assert.NotNull(tie);
        Assert.Equal(SyntaxKind.Tie, tie.Kind);
    }

    [Fact]
    public void ParseSlur()
    {
        var tree = SyntaxTree.Parse("c( d e f)");
        
        // c, (, d, e, f, ), EOF = 7 items
        Assert.Equal(7, tree.Root.SlotCount);
        
        var slurOpen = tree.Root.GetSlot(1) as SlurGreen;
        Assert.NotNull(slurOpen);
        Assert.Equal(SyntaxKind.Slur, slurOpen.Kind);
    }

    [Fact]
    public void ParseMusicBlock()
    {
        var tree = SyntaxTree.Parse("{ c d e f }");
        var block = tree.Root.GetSlot(0) as MusicBlockGreen;
        
        Assert.NotNull(block);
        Assert.Equal(SyntaxKind.MusicBlock, block.Kind);
    }

    [Fact]
    public void ParseRelativeExpression()
    {
        var tree = SyntaxTree.Parse("relative c' { c d e f }");
        var relative = tree.Root.GetSlot(0) as RelativeExpressionGreen;
        
        Assert.NotNull(relative);
        Assert.Equal(SyntaxKind.RelativeExpression, relative.Kind);
    }

    [Fact]
    public void ParseComplexPhrase()
    {
        var tree = SyntaxTree.Parse("relative c' { c4 d e f | g2 g | }");
        var relative = tree.Root.GetSlot(0) as RelativeExpressionGreen;
        
        Assert.NotNull(relative);
        
        // Check it round-trips
        var reconstructed = tree.ToFullString();
        Assert.Equal("relative c' { c4 d e f | g2 g | }", reconstructed);
    }

    [Fact]
    public void RoundTrip_PreservesText()
    {
        var original = "relative c'' { c4 d8. e16 f4 | <c e g>2. r4 | }";
        var tree = SyntaxTree.Parse(original);
        var reconstructed = tree.ToFullString();
        
        Assert.Equal(original, reconstructed);
    }

    [Fact]
    public void ParseDottedDuration()
    {
        var tree = SyntaxTree.Parse("c4. d8..");
        
        var note1 = tree.Root.GetSlot(0) as NoteGreen;
        var note2 = tree.Root.GetSlot(1) as NoteGreen;
        
        Assert.NotNull(note1);
        Assert.NotNull(note2);
        
        var dur1 = note1.GetSlot(1) as DurationGreen;
        var dur2 = note2.GetSlot(1) as DurationGreen;
        
        Assert.NotNull(dur1);
        Assert.NotNull(dur2);
        Assert.Equal(2, dur1.SlotCount); // number + 1 dot
        Assert.Equal(3, dur2.SlotCount); // number + 2 dots
    }

    [Fact]
    public void ParseAccidentals()
    {
        var tree = SyntaxTree.Parse("cis des fisis geses");
        
        Assert.Equal(5, tree.Root.SlotCount); // 4 notes + EOF
        
        // All should be Note nodes
        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(SyntaxKind.Note, tree.Root.GetSlot(i)?.Kind);
        }
    }

    [Fact]
    public void ParseWithWhitespaceAndComments()
    {
        var source = @"
// A simple melody
c4 d e f  /* first measure */
| g2 g |  // second measure
";
        var tree = SyntaxTree.Parse(source);
        
        // Should parse successfully and round-trip
        var reconstructed = tree.ToFullString();
        Assert.Equal(source, reconstructed);
    }
}