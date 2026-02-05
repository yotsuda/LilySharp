using System.Linq;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

public class SectionOrientedParserTests
{
    [Fact]
    public void ParseVariableDeclaration_NewStyle()
    {
        var source = "guitar_riff = { c4 d e f }";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors);
    }

    [Fact]
    public void ParseSectionDeclaration_Simple()
    {
        var source = @"
section Intro {
    guitar { c4 d e f }
}";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors);
    }

    [Fact]
    public void ParseSectionDeclaration_WithKeyAndTempo()
    {
        var source = @"
section A {
    key c major
    tempo 120
    guitar { c4 d e f }
    bass { c,4 g, c, g, }
}";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors);
    }

    [Fact]
    public void ParseStructureDeclaration_Simple()
    {
        var source = @"
structure {
    Intro
    A
    B
}";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors);
    }

    [Fact]
    public void ParseStructureDeclaration_WithNavigation()
    {
        var source = @"
structure {
    Intro
    segno
    A
    fine
    B
    ds al fine
}";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors);
    }

    [Fact]
    public void ParseRenderDeclaration_Staff()
    {
        var source = @"
render full ""output.svg"" {
    staff { guitar }
}";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors);
    }

    [Fact]
    public void ParseRenderDeclaration_Tab()
    {
        var source = @"
render guitarTab ""guitar.svg"" {
    staff { guitar }
    tab guitar { guitar }
}";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors);
    }

    [Fact]
    public void ParseRenderDeclaration_Midi()
    {
        var source = @"
render audio ""song.mid"" {
    guitar channel:1 instrument:25
    bass channel:2 instrument:33
}";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors);
    }

    [Fact]
    public void ParseCompleteFile_NewStyle()
    {
        var source = @"
title ""Test Song""
tempo 120
time 4/4
key c major

guitar_riff = { c4 d e f | g a b c' }

section Intro {
    guitar { guitar_riff }
    bass { c,4 g, c, g, }
}

section A {
    guitar { e4 f g a }
    bass { e,4 b, e, b, }
}

structure {
    Intro
    A
}

render full ""test.svg"" {
    staff { guitar }
    staff bass { bass }
}
";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors);
    }

    [Fact]
    public void ParseStructureDeclaration_VoltaBracket_Simple()
    {
        var source = @"
structure {
    |: Verse [1. Bridge] [2. Chorus] :|
}";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors);
    }

    [Fact]
    public void ParseStructureDeclaration_VoltaBracket_SilentSection()
    {
        var source = @"
structure {
    |: Verse [1. Bridge] [2. ~Verse] :|
}";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics));
        
        // Verify the alternative has IsSilent flag
        var structure = tree.GetRoot().DescendantNodes().OfType<StructureDeclarationSyntax>().First();
        var repeatBlock = structure.DescendantNodes().OfType<StructureRepeatBlockSyntax>().First();
        var alternatives = repeatBlock.DescendantNodes().OfType<StructureAlternativeSyntax>().ToList();
        
        Assert.Equal(2, alternatives.Count);
        Assert.False(alternatives[0].IsSilent); // [1. Bridge]
        Assert.True(alternatives[1].IsSilent);  // [2. ~Verse]
        Assert.Equal("Verse", alternatives[1].SectionName.Text);
    }

    [Fact]
    public void ParseStructureDeclaration_VoltaBracket_RangeWithSilent()
    {
        var source = @"
structure {
    |: Verse [1-2. Bridge] [3. ~Coda] :| x3
}";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics));
    }

    [Fact]
    public void ParseStructureDeclaration_MusicMark_Simple()
    {
        var source = @"
structure {
    Intro
    @segno
    Verse
    @fine
}";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics));
        
        // Verify MusicMark nodes are created
        var musicMarks = tree.GetRoot().DescendantNodes().OfType<MusicMarkSyntax>().ToList();
        Assert.Equal(2, musicMarks.Count);
        Assert.Equal("segno", musicMarks[0].MarkName);
        Assert.Equal("fine", musicMarks[1].MarkName);
    }

    [Fact]
    public void ParseStructureDeclaration_MusicMark_Compound()
    {
        var source = @"
structure {
    Intro
    @ds.al.fine
}";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics));
        
        var musicMark = tree.GetRoot().DescendantNodes().OfType<MusicMarkSyntax>().First();
        Assert.Equal("ds.al.fine", musicMark.MarkName);
    }

    [Fact]
    public void ParseStructureDeclaration_CustomText_Simple()
    {
        var source = @"
structure {
    Intro
    _""molto rit.""
    Verse
    _""a tempo""
}";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics));

        // Verify CustomText nodes are created
        var customTexts = tree.GetRoot().DescendantNodes().OfType<CustomTextSyntax>().ToList();
        Assert.Equal(2, customTexts.Count);
        Assert.Equal("molto rit.", customTexts[0].Text);
        Assert.Equal("a tempo", customTexts[1].Text);
    }

    [Fact]
    public void ParseStructureDeclaration_CustomText_WithSpecialChars()
    {
        var source = @"
structure {
    Intro
    _""cresc. poco a poco""
}";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics));

        var customText = tree.GetRoot().DescendantNodes().OfType<CustomTextSyntax>().First();
        Assert.Equal("cresc. poco a poco", customText.Text);
    }
}