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
let melody = { c4 d e f | }
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
structure {
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
structure {
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
structure {
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
}
