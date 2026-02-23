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

using Xunit;
using LilySharp.Core.Semantics;
using LilySharp.Core.Semantics.Symbols;
using LilySharp.Core.Syntax;

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
public class SymbolCollectorTests
{
    [Fact]
    public void Collect_EmptySource_ReturnsEmptyTable()
    {
        var source = "";
        var tree = SyntaxTree.Parse(source);
        var collector = new SymbolCollector();

        var result = collector.Collect(tree);

        Assert.True(result.Success);
        Assert.Empty(result.Symbols.Sections);
        Assert.Empty(result.Symbols.Phrases);
        Assert.Empty(result.Symbols.Parts);
        Assert.Empty(result.Symbols.Variables);
        Assert.Null(result.Symbols.Structure);
    }

    [Fact]
    public void Collect_SingleSection_AddsToTable()
    {
        var source = @"
section A {
    c4 d e f |
}";
        var tree = SyntaxTree.Parse(source);
        var collector = new SymbolCollector();

        var result = collector.Collect(tree);

        Assert.True(result.Success);
        Assert.Single(result.Symbols.Sections);
        Assert.True(result.Symbols.Sections.ContainsKey("A"));

        var section = result.Symbols.Sections["A"];
        Assert.Equal("A", section.Name);
        Assert.Equal(SymbolKind.Section, section.Kind);
        Assert.NotEmpty(section.Body);
    }

    [Fact]
    public void Collect_MultipleSections_AddsAllToTable()
    {
        var source = @"
section A {
    c4 d e f |
}

section B {
    g4 a b c' |
}

section Chorus {
    e4 e e e |
}";
        var tree = SyntaxTree.Parse(source);
        var collector = new SymbolCollector();

        var result = collector.Collect(tree);

        Assert.True(result.Success);
        Assert.Equal(3, result.Symbols.Sections.Count);
        Assert.True(result.Symbols.Sections.ContainsKey("A"));
        Assert.True(result.Symbols.Sections.ContainsKey("B"));
        Assert.True(result.Symbols.Sections.ContainsKey("Chorus"));
    }

    [Fact]
    public void Collect_DuplicateSection_ReportsError()
    {
        var source = @"
section A {
    c4 d e f |
}

section A {
    g4 a b c' |
}";
        var tree = SyntaxTree.Parse(source);
        var collector = new SymbolCollector();

        var result = collector.Collect(tree);

        Assert.False(result.Success);
        Assert.Single(result.Diagnostics);
        Assert.Equal(LilySharp.Core.Semantics.DiagnosticSeverity.Error, result.Diagnostics[0].Severity);
        Assert.Contains("Duplicate section", result.Diagnostics[0].Message);
    }

    [Fact]
    public void Collect_PhraseDefinition_AddsToTable()
    {
        var source = @"
phrase melody = {
    c4 d e f |
}";
        var tree = SyntaxTree.Parse(source);
        var collector = new SymbolCollector();

        var result = collector.Collect(tree);

        Assert.True(result.Success);
        Assert.Single(result.Symbols.Phrases);
        Assert.True(result.Symbols.Phrases.ContainsKey("melody"));

        var phrase = result.Symbols.Phrases["melody"];
        Assert.Equal("melody", phrase.Name);
        Assert.Equal(SymbolKind.Phrase, phrase.Kind);
    }

    [Fact]
    public void Collect_VariableDefinition_AddsToTable()
    {
        var source = @"theme = c4 d e f";
        var tree = SyntaxTree.Parse(source);
        var collector = new SymbolCollector();

        var result = collector.Collect(tree);

        Assert.True(result.Success);
        Assert.Single(result.Symbols.Variables);
        Assert.True(result.Symbols.Variables.ContainsKey("theme"));

        var variable = result.Symbols.Variables["theme"];
        Assert.Equal("theme", variable.Name);
        Assert.Equal(SymbolKind.Variable, variable.Kind);
    }

    [Fact]
    public void Collect_Structure_AddsToTable()
    {
        var source = @"
section A { c4 d e f | }

structure {
    A
}";
        var tree = SyntaxTree.Parse(source);
        var collector = new SymbolCollector();

        var result = collector.Collect(tree);

        Assert.True(result.Success);
        Assert.NotNull(result.Symbols.Structure);
        Assert.Equal(SymbolKind.Structure, result.Symbols.Structure.Kind);
    }

    [Fact]
    public void Collect_MultipleStructures_ReportsError()
    {
        var source = @"
section A { c4 d e f | }

structure { A }
structure { A }";
        var tree = SyntaxTree.Parse(source);
        var collector = new SymbolCollector();

        var result = collector.Collect(tree);

        Assert.False(result.Success);
        Assert.Single(result.Diagnostics);
        Assert.Contains("Multiple structure", result.Diagnostics[0].Message);
    }

    [Fact]
    public void Lookup_ExistingSymbol_ReturnsSymbol()
    {
        var source = @"
section A { c4 | }
phrase melody = { d4 | }
theme = e4";
        var tree = SyntaxTree.Parse(source);
        var collector = new SymbolCollector();

        var result = collector.Collect(tree);
        var symbols = result.Symbols;

        Assert.NotNull(symbols.Lookup("A"));
        Assert.IsType<SectionSymbol>(symbols.Lookup("A"));

        Assert.NotNull(symbols.Lookup("melody"));
        Assert.IsType<PhraseSymbol>(symbols.Lookup("melody"));

        Assert.NotNull(symbols.Lookup("theme"));
        Assert.IsType<VariableSymbol>(symbols.Lookup("theme"));
    }

    [Fact]
    public void Lookup_NonExistingSymbol_ReturnsNull()
    {
        var source = @"section A { c4 | }";
        var tree = SyntaxTree.Parse(source);
        var collector = new SymbolCollector();

        var result = collector.Collect(tree);

        Assert.Null(result.Symbols.Lookup("NonExisting"));
    }

    [Fact]
    public void Collect_ComplexSource_CollectsAllSymbols()
    {
        var source = @"
section A { melody { c4 d e f | } }
structure { A }
render score ""test.svg"" { staff treble { melody } }
";
        var tree = SyntaxTree.Parse(source);
        var collector = new SymbolCollector();

        var result = collector.Collect(tree);

        Assert.True(result.Success);
    }

    [Fact]
    public void Collect_PartWithInstrument_CollectsProperties()
    {
        var source = @"
part violin {
    instrument: violin
    clef: treble
}";
        var tree = SyntaxTree.Parse(source);
        var collector = new SymbolCollector();

        var result = collector.Collect(tree);

        Assert.True(result.Success);
        Assert.Single(result.Symbols.Parts);
        Assert.True(result.Symbols.Parts.ContainsKey("violin"));

        var part = result.Symbols.Parts["violin"];
        Assert.Equal("violin", part.GetProperty("instrument"));
        Assert.Equal("treble", part.GetProperty("clef"));
    }

    [Fact]
    public void Collect_PartWithOctave_CollectsOctaveProperty()
    {
        var source = @"
part cello {
    clef: bass
    octave: 3
}";
        var tree = SyntaxTree.Parse(source);
        var collector = new SymbolCollector();

        var result = collector.Collect(tree);

        Assert.True(result.Success);
        var part = result.Symbols.Parts["cello"];
        Assert.Equal("bass", part.GetProperty("clef"));
        Assert.Equal("3", part.GetProperty("octave"));
    }
}
