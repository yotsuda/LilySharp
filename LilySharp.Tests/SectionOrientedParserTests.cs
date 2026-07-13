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
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
public class SectionOrientedParserTests
{
    [Fact]
    public void ParsePhraseDeclaration_Simple()
    {
        var source = "phrase guitar_riff { c4 d e f }";
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
form main {
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
form main {
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
score main ""output"" {
    staff guitar
}";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors);
    }

    [Fact]
    public void ParseRenderDeclaration_Tab()
    {
        var source = @"
score main ""guitar"" {
    staff guitar
    tab guitar guitar
}";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors);
    }

    [Fact]
    public void ParseRenderDeclaration_Midi()
    {
        var source = @"
score main ""song"" {
    guitar octave 1 instrument 25
    bass octave 2 instrument 33
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

phrase guitar_riff { c4 d e f | g a b c' }

section Intro {
    guitar { $guitar_riff }
    bass { c,4 g, c, g, }
}

section A {
    guitar { e4 f g a }
    bass { e,4 b, e, b, }
}

form main {
    Intro
    A
}

score main ""test"" {
    staff guitar
    staff bass bass
}
";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors);
    }

    [Fact]
    public void ParseStructureDeclaration_VoltaBracket_Simple()
    {
        var source = @"
form main {
    |: Verse [1. Bridge] :| [2. Chorus]
}";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors);
    }

    [Fact]
    public void ParseStructureDeclaration_VoltaBracket_SilentSection()
    {
        var source = @"
form main {
    |: Verse [1. Bridge] :| [2. ~Verse]
}";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics));
        
        // Verify the alternative has IsSilent flag
        var structure = tree.GetRoot().DescendantNodes().OfType<FormDeclarationSyntax>().First();
        var repeatBlock = structure.DescendantNodes().OfType<FormRepeatBlockSyntax>().First();
        var alternatives = repeatBlock.DescendantNodes().OfType<FormAlternativeSyntax>().ToList();
        
        Assert.Equal(2, alternatives.Count);
        Assert.False(alternatives[0].IsSilent); // [1. Bridge]
        Assert.True(alternatives[1].IsSilent);  // [2. ~Verse]
        Assert.Equal("Verse", alternatives[1].SectionName.Text);
    }

    [Fact]
    public void ParseStructureDeclaration_VoltaBracket_RangeWithSilent()
    {
        var source = @"
form main {
    |: Verse [1-2. Bridge] :| [3. ~Coda] x3
}";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics));
    }

    [Fact]
    public void ParseStructureDeclaration_NavigationMark_Bare()
    {
        // Navigation marks are BARE (a standalone landmark, not a note modifier).
        var source = @"
form main {
    Intro
    segno
    Verse
    fine
}";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics));

        var navs = tree.GetRoot().DescendantNodes().OfType<NavigationMarkSyntax>().ToList();
        Assert.Equal(2, navs.Count);
    }

    [Fact]
    public void ParseStructureDeclaration_NavigationMark_WithAt_IsRejected()
    {
        // '@' modifies a note; a navigation mark with '@' (@segno) is a syntax error.
        var source = "form main { Intro @segno Verse @fine }";
        var tree = SyntaxTree.Parse(source);
        var errors = tree.Diagnostics
            .Where(d => d.Code == LilySharp.Core.Syntax.DiagnosticCodes.NavigationMarkIsBare)
            .ToList();
        Assert.Equal(2, errors.Count);
        // The squiggle lands on the mark name itself, not the token after it.
        Assert.Contains(errors, d => source.Substring(d.Span.Start, d.Span.Length).Trim() == "segno");
        Assert.Contains(errors, d => source.Substring(d.Span.Start, d.Span.Length).Trim() == "fine");
    }

    [Fact]
    public void ParseStructureDeclaration_NavigationMark_CompoundBare()
    {
        var source = @"
form main {
    Intro
    ds al fine
}";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics));

        var nav = tree.GetRoot().DescendantNodes().OfType<NavigationMarkSyntax>().First();
        Assert.NotNull(nav);
    }

    [Fact]
    public void ParseStructureDeclaration_CustomText_Simple()
    {
        var source = @"
form main {
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
form main {
    Intro
    _""cresc. poco a poco""
}";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics));

        var customText = tree.GetRoot().DescendantNodes().OfType<CustomTextSyntax>().First();
        Assert.Equal("cresc. poco a poco", customText.Text);
    }
}
