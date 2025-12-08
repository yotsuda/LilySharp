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

    // ========== Structure Tests ==========

    [Fact]
    public void ParseScoreDeclaration()
    {
        var tree = SyntaxTree.Parse(@"score ""My Song"" {
    part {
        relative c' { c d e f }
    }
}");
        var score = tree.Root.GetSlot(0) as ScoreDeclarationGreen;
        
        Assert.NotNull(score);
        Assert.Equal(SyntaxKind.ScoreDeclaration, score.Kind);
    }

    [Fact]
    public void ParsePartDeclaration()
    {
        var tree = SyntaxTree.Parse(@"part Violin ""First Violin"" {
    relative c' { c d e f }
}");
        var part = tree.Root.GetSlot(0) as PartDeclarationGreen;
        
        Assert.NotNull(part);
        Assert.Equal(SyntaxKind.PartDeclaration, part.Kind);
    }

    [Fact]
    public void ParseStaffDeclaration()
    {
        var tree = SyntaxTree.Parse(@"part Piano {
    staff RH {
        relative c'' { c d e f }
    }
    staff LH {
        relative c { c d e f }
    }
}");
        var part = tree.Root.GetSlot(0) as PartDeclarationGreen;
        Assert.NotNull(part);
    }

    [Fact]
    public void ParseMetadata()
    {
        var tree = SyntaxTree.Parse(@"title ""Happy Birthday""
composer ""Traditional""
tempo 120
time 3/4
key g major
");
        // Should have 5 metadata declarations + EOF
        Assert.Equal(6, tree.Root.SlotCount);
        
        var title = tree.Root.GetSlot(0) as MetadataDeclarationGreen;
        Assert.NotNull(title);
        Assert.Equal(SyntaxKind.MetadataDeclaration, title.Kind);
    }

    [Fact]
    public void ParsePropertyAssignment()
    {
        var tree = SyntaxTree.Parse(@"part {
    clef: treble
    relative c' { c d e f }
}");
        var part = tree.Root.GetSlot(0) as PartDeclarationGreen;
        Assert.NotNull(part);
    }

    [Fact]
    public void ParseVariableDeclaration()
    {
        var tree = SyntaxTree.Parse(@"let theme = relative c' { c d e f }

part {
    use theme
}");
        var varDecl = tree.Root.GetSlot(0) as VariableDeclarationGreen;
        Assert.NotNull(varDecl);
        Assert.Equal(SyntaxKind.VariableDeclaration, varDecl.Kind);
    }

    [Fact]
    public void ParseVariableReference()
    {
        var tree = SyntaxTree.Parse(@"let theme = { c d e f }
use theme");
        
        var varRef = tree.Root.GetSlot(1) as VariableReferenceGreen;
        Assert.NotNull(varRef);
        Assert.Equal(SyntaxKind.VariableReference, varRef.Kind);
    }

    [Fact]
    public void ParseCompleteScore()
    {
        var source = @"title ""Fur Elise""
composer ""Beethoven""
tempo 76
time 3/8
key a minor

score {
    part Piano {
        staff RH {
            clef: treble
            relative c'' { e8 dis e dis e b d c | a4. }
        }
        staff LH {
            clef: bass
            relative c { r4. a8 e' a | c4. }
        }
    }
}";
        var tree = SyntaxTree.Parse(source);
        
        // Round-trip should preserve text
        var reconstructed = tree.ToFullString();
        Assert.Equal(source, reconstructed);
    }

    [Fact]
    public void ParseScoreWithProperties()
    {
        var tree = SyntaxTree.Parse(@"score {
    tempo: 120
    time: 4/4
    key: c major
    
    part {
        relative c' { c d e f }
    }
}");
        var score = tree.Root.GetSlot(0) as ScoreDeclarationGreen;
        Assert.NotNull(score);
    }

    [Fact]
    public void ParseNoteWithArticulation()
    {
        var tree = SyntaxTree.Parse("{ c4@staccato }");
        Assert.False(tree.HasErrors);
        
        var root = tree.GetRoot();
        Assert.NotNull(root);
    }

    [Fact]
    public void ParseNoteWithDynamic()
    {
        var tree = SyntaxTree.Parse(@"{ c4\p }");
        Assert.False(tree.HasErrors);
    }

    [Fact]
    public void ParseNoteWithMultipleArticulations()
    {
        var tree = SyntaxTree.Parse(@"{ c4@staccato@accent\f }");
        Assert.False(tree.HasErrors);
    }

    [Fact]
    public void ParseDynamicSequence()
    {
        var tree = SyntaxTree.Parse(@"{ c4\p d\cresc e\f }");
        Assert.False(tree.HasErrors);
    }

    // ========== Repeat Tests ==========

    [Fact]
    public void ParseRepeatVolta()
    {
        var tree = SyntaxTree.Parse("repeat volta 2 { c4 d e f }");
        Assert.False(tree.HasErrors);
        
        var repeat = tree.Root.GetSlot(0) as RepeatExpressionGreen;
        Assert.NotNull(repeat);
        Assert.Equal(SyntaxKind.RepeatExpression, repeat.Kind);
    }

    [Fact]
    public void ParseRepeatWithAlternative()
    {
        var tree = SyntaxTree.Parse(@"repeat volta 2 { c4 d e f } alternative { { g2 } { a2 } }");
        Assert.False(tree.HasErrors);
        
        var repeat = tree.Root.GetSlot(0) as RepeatExpressionGreen;
        Assert.NotNull(repeat);
        
        // Should have alternative clause
        var alternative = repeat.GetSlot(4) as AlternativeClauseGreen;
        Assert.NotNull(alternative);
    }

    [Fact]
    public void ParseRepeatRoundTrip()
    {
        var source = "repeat volta 2 { c4 d e f | g2 g | }";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors);
        Assert.Equal(source, tree.ToFullString());
    }

    [Fact]
    public void ParseRepeatWithAlternativeRoundTrip()
    {
        var source = "repeat volta 2 { c4 d e f } alternative { { g2 } { a2 } }";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors);
        Assert.Equal(source, tree.ToFullString());
    }

    // ========== Parallel Tests ==========

    [Fact]
    public void ParseParallelExpression()
    {
        var tree = SyntaxTree.Parse(@"<< { c2 d } \\ { e2 f } >>");
        Assert.False(tree.HasErrors);
        
        var parallel = tree.Root.GetSlot(0) as ParallelExpressionGreen;
        Assert.NotNull(parallel);
        Assert.Equal(SyntaxKind.ParallelExpression, parallel.Kind);
    }

    [Fact]
    public void ParseParallelWithRelative()
    {
        var tree = SyntaxTree.Parse(@"<< relative c'' { c2 d } \\ relative c' { e2 f } >>");
        Assert.False(tree.HasErrors);
    }

    [Fact]
    public void ParseParallelRoundTrip()
    {
        var source = @"<< { c2 d } \\ { e2 f } >>";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors);
        Assert.Equal(source, tree.ToFullString());
    }

    [Fact]
    public void ParseParallelThreeVoices()
    {
        var tree = SyntaxTree.Parse(@"<< { c2 } \\ { e2 } \\ { g2 } >>");
        Assert.False(tree.HasErrors);
    }

    [Fact]
    public void ParseNestedRepeatInPart()
    {
        var tree = SyntaxTree.Parse(@"part {
    repeat volta 2 {
        c4 d e f |
    }
    alternative {
        { g2 g | }
        { a2 a | }
    }
}");
        Assert.False(tree.HasErrors);
    }

    [Fact]
    public void ParseParallelInStaff()
    {
        var tree = SyntaxTree.Parse(@"part {
    staff {
        << relative c'' { c2 d } \\ relative c' { e2 f } >>
    }
}");
        Assert.False(tree.HasErrors);
    }

    // ========== Key Signature ==========

    [Fact]
    public void ParseKeySignatureMajor()
    {
        var source = "key c major";
        var tree = SyntaxTree.Parse(source);
        
        Assert.False(tree.HasErrors);
        var keySig = tree.GetNodes<KeySignatureSyntax>().First();
        Assert.Equal("c", keySig.Pitch.PitchName);
        Assert.True(keySig.IsMajor);
    }

    [Fact]
    public void ParseKeySignatureMinor()
    {
        var source = "key a minor";
        var tree = SyntaxTree.Parse(source);
        
        Assert.False(tree.HasErrors);
        var keySig = tree.GetNodes<KeySignatureSyntax>().First();
        Assert.Equal("a", keySig.Pitch.PitchName);
        Assert.False(keySig.IsMajor);
    }

    [Fact]
    public void ParseKeySignatureWithAccidental()
    {
        var source = "key bes major";
        var tree = SyntaxTree.Parse(source);
        
        Assert.False(tree.HasErrors);
        var keySig = tree.GetNodes<KeySignatureSyntax>().First();
        Assert.Equal("bes", keySig.Pitch.PitchName);
    }

    // ========== Clef ==========

    [Fact]
    public void ParseClefTreble()
    {
        var source = "clef treble";
        var tree = SyntaxTree.Parse(source);
        
        Assert.False(tree.HasErrors);
        var clef = tree.GetNodes<ClefDeclarationSyntax>().First();
        Assert.Equal(SyntaxKind.TrebleKeyword, clef.ClefName.Kind);
    }

    [Fact]
    public void ParseClefBass()
    {
        var source = "clef bass";
        var tree = SyntaxTree.Parse(source);
        
        Assert.False(tree.HasErrors);
        var clef = tree.GetNodes<ClefDeclarationSyntax>().First();
        Assert.Equal(SyntaxKind.BassKeyword, clef.ClefName.Kind);
    }

    // ========== Tuplet ==========

    [Fact]
    public void ParseTupletTriplet()
    {
        var source = "tuplet 3/2 { c d e }";
        var tree = SyntaxTree.Parse(source);
        
        Assert.False(tree.HasErrors);
        var tuplet = tree.GetNodes<TupletExpressionSyntax>().First();
        Assert.Equal(3, tuplet.TupletRatio);
        Assert.Equal(2, tuplet.BaseDivision);
        Assert.Equal(3, tuplet.Body.Items.Count());
    }

    [Fact]
    public void ParseTupletQuintuplet()
    {
        var source = "tuplet 5/4 { c d e f g }";
        var tree = SyntaxTree.Parse(source);
        
        Assert.False(tree.HasErrors);
        var tuplet = tree.GetNodes<TupletExpressionSyntax>().First();
        Assert.Equal(5, tuplet.TupletRatio);
        Assert.Equal(4, tuplet.BaseDivision);
    }

    [Fact]
    public void ParseKeyClefTupletCombined()
    {
        var source = @"
clef treble
key g major
tuplet 3/2 { c d e }
c4 d e f
";
        var tree = SyntaxTree.Parse(source);
        
        Assert.False(tree.HasErrors);
        Assert.Single(tree.GetNodes<ClefDeclarationSyntax>());
        Assert.Single(tree.GetNodes<KeySignatureSyntax>());
        Assert.Single(tree.GetNodes<TupletExpressionSyntax>());
    }

    [Fact]
    public void RoundTripKeySignature()
    {
        var source = "key fis minor";
        var tree = SyntaxTree.Parse(source);
        Assert.Equal(source, tree.ToFullString());
    }

    [Fact]
    public void RoundTripTuplet()
    {
        var source = "tuplet 3/2 { c d e }";
        var tree = SyntaxTree.Parse(source);
        Assert.Equal(source, tree.ToFullString());
    }
}