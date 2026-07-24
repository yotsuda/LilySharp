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
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
public class SymbolReferenceValidatorTests
{
    [Fact]
    public void Validate_UndefinedVariable_ReportsError()
    {
        var source = "$undefined";
        var tree = SyntaxTree.Parse(source);
        
        var validator = new SymbolReferenceValidator();
        validator.Validate(tree);
        
        Assert.Single(validator.Diagnostics);
        Assert.Equal(DiagnosticCodes.UndefinedVariable, validator.Diagnostics[0].Code);
        Assert.Contains("undefined", validator.Diagnostics[0].Message);
    }
    
    [Fact]
    public void Validate_DefinedVariable_NoError()
    {
        var source = @"
phrase melody { c4 d e f | }
$melody
";
        var tree = SyntaxTree.Parse(source);
        
        var validator = new SymbolReferenceValidator();
        validator.Validate(tree);
        
        Assert.Empty(validator.Diagnostics);
    }
    
    [Fact]
    public void Validate_DefinedPhrase_NoError()
    {
        var source = @"
phrase intro { c4 d e f | }
$intro
";
        var tree = SyntaxTree.Parse(source);
        
        var validator = new SymbolReferenceValidator();
        validator.Validate(tree);
        
        Assert.Empty(validator.Diagnostics);
    }
    
    [Fact]
    public void Validate_UndefinedSectionInStructure_ReportsError()
    {
        var source = @"
section Intro { c4 d e f | }
form main {
    Intro
    NonExistent
}
";
        var tree = SyntaxTree.Parse(source);
        
        var validator = new SymbolReferenceValidator();
        validator.Validate(tree);
        
        Assert.Single(validator.Diagnostics);
        Assert.Equal(DiagnosticCodes.UndefinedSection, validator.Diagnostics[0].Code);
        Assert.Contains("NonExistent", validator.Diagnostics[0].Message);
    }
    
    [Fact]
    public void Validate_DefinedSectionInStructure_NoError()
    {
        var source = @"
section Intro { c4 d e f | }
section Verse { g4 a b c | }
form main {
    Intro
    Verse
}
";
        var tree = SyntaxTree.Parse(source);
        
        var validator = new SymbolReferenceValidator();
        validator.Validate(tree);
        
        Assert.Empty(validator.Diagnostics);
    }
    
    [Fact]
    public void Validate_MultipleUndefinedReferences_ReportsAll()
    {
        var source = @"
form main {
    Section1
    Section2
}
$undefined1
$undefined2
";
        var tree = SyntaxTree.Parse(source);

        var validator = new SymbolReferenceValidator();
        validator.Validate(tree);

        Assert.Equal(4, validator.Diagnostics.Count);
    }

    private static IReadOnlyList<Diagnostic> Refs(string source)
    {
        var validator = new SymbolReferenceValidator();
        validator.Validate(SyntaxTree.Parse(source));
        return validator.Diagnostics;
    }

    [Fact]
    public void Validate_StaffNamesUndefinedPart_ReportsError()
    {
        // `staff melody2` names no part — a section-body block nor a header defines it.
        var diags = Refs("section A { melody { c d e f } }\n"
                       + "form main { A }\nscore main { staff melody\n staff melody2 }");
        var undef = diags.Where(d => d.Code == DiagnosticCodes.UndefinedPart).ToList();
        Assert.Single(undef);
        Assert.Contains("melody2", undef[0].Message);
    }

    [Theory]
    // A section-body part block DEFINES the part.
    [InlineData("section A { melody { c d e f } }\nform main { A }\nscore main { staff melody }")]
    // …as does a part header.
    [InlineData("part melody { clef treble section A { c d e f } }\nform main { A }\nscore main { staff melody }")]
    // A clef modifier before the part name is not the part.
    [InlineData("section A { melody { c d e f } }\nform main { A }\nscore main { staff bass melody }")]
    // `tab NAME as numbers | full` — the tab STYLE selector is not a part reference.
    // This reported LYS1007 "Undefined part: 'numbers'" on a valid score, so the
    // committed fixture test/tab-as-numbers.lys would not render through the CLI (the
    // snapshot path never runs this validator, which is why the suite stayed green).
    [InlineData("section A { melody { c d e f } }\nform main { A }\nscore main { tab melody as numbers }")]
    [InlineData("section A { melody { c d e f } }\nform main { A }\nscore main { tab melody as full }")]
    // Tuning override + style selector, and the chord-display selector after it.
    [InlineData("section A { melody { c d e f } }\nchords h { c1 }\nform main { A }\n"
              + "score main { tab bass melody as numbers with chords h as both }")]
    public void Validate_StaffNamesDefinedPart_NoUndefinedPartError(string source)
        => Assert.DoesNotContain(Refs(source), d => d.Code == DiagnosticCodes.UndefinedPart);

    [Fact]
    public void Validate_GrandStaffUndefinedInnerPart_ReportsError()
    {
        var diags = Refs("section A { rh { c1 } }\n"
                       + "form main { A }\nscore main { grandStaff { staff rh staff lh } }");
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.UndefinedPart && d.Message.Contains("lh"));
    }
}
