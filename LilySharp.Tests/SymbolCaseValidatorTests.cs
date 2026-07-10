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
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Header symbols are case-sensitive: a wrong-case or unknown property name, or a
/// wrong-case/unknown clef / instrument-preset / tuning value, is an error rather
/// than a silent fallback to a default. Free-text (quoted) values are not symbols.
/// </summary>
[Trait("Category", "Unit")]
public class SymbolCaseValidatorTests
{
    // Wrap a part header body in a minimal complete document. `vln` is a plain
    // identifier part name (p / pp / mf … are reserved dynamics, not names).
    private static string Doc(string header) =>
        $"part vln {{ {header} }}\nsection A {{ vln {{ c4 d e f }} }}\nform main {{ A }}\nscore \"s\" {{ staff vln }}";

    private static bool HasSymbolError(string header) =>
        SemanticValidation.Run(SyntaxTree.Parse(Doc(header)))
            .Any(d => d.Code == DiagnosticCodes.UnknownSymbolCase);

    [Fact]
    public void CanonicalLowercaseSymbols_AreClean()
    {
        Assert.False(HasSymbolError("clef treble  instrument violin"));
    }

    [Fact]
    public void WrongCaseClefValue_IsError()
    {
        Assert.True(HasSymbolError("clef Treble"));
    }

    [Fact]
    public void WrongCaseInstrumentPreset_IsError()
    {
        Assert.True(HasSymbolError("instrument Violin"));
    }

    [Fact]
    public void CapitalizedPropertyName_IsError()
    {
        Assert.True(HasSymbolError("Clef treble"));
    }

    [Fact]
    public void WrongCaseTuningValue_IsError()
    {
        Assert.True(HasSymbolError("tuning Guitar"));
    }

    [Fact]
    public void QuotedInstrumentLabel_IsNotASymbol_NoError()
    {
        // A quoted "…" name is free text, not a preset symbol — no case rule applies.
        Assert.False(HasSymbolError("instrument \"1st Violin\""));
    }

    [Fact]
    public void PresetPlusQuotedLabel_ChecksOnlyThePreset()
    {
        Assert.False(HasSymbolError("instrument cello \"Cello I\""));   // known preset + label
        Assert.True(HasSymbolError("instrument Cello \"Cello I\""));    // wrong-case preset
    }
}
