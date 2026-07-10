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

[Trait("Category", "Unit")]
public class FormDeclarationValidatorTests
{
    private static string Wrap(string formsAndScore) => $@"
title ""t""
time 4/4
part melody
section A {{ melody {{ c'4 d e f | }} }}
section B {{ melody {{ g'4 a b c | }} }}
{formsAndScore}
";

    private static IReadOnlyList<Diagnostic> Validate(string formsAndScore)
    {
        var validator = new FormDeclarationValidator();
        validator.Validate(SyntaxTree.Parse(Wrap(formsAndScore)));
        return validator.Diagnostics;
    }

    [Fact]
    public void NamedFormWithMatchingScore_NoError()
        => Assert.Empty(Validate("form main { A B }\nscore main { staff { melody } }"));

    [Fact]
    public void MultipleNamedForms_NoError()
        => Assert.Empty(Validate(
            "form main { A B }\nform excerpt { B }\n"
            + "score main { staff { melody } }\nscore excerpt { staff { melody } }"));

    [Fact]
    public void UnnamedForm_IsFlagged()
        => Assert.Contains(Validate("form { A B }\nscore main { staff { melody } }"),
            d => d.Code == DiagnosticCodes.UnnamedForm);

    [Fact]
    public void DuplicateFormName_IsFlagged()
        => Assert.Contains(Validate("form main { A }\nform main { B }\nscore main { staff { melody } }"),
            d => d.Code == DiagnosticCodes.DuplicateFormName);

    [Fact]
    public void UnknownFormReference_IsFlagged()
        => Assert.Contains(Validate("form main { A B }\nscore verse { staff { melody } }"),
            d => d.Code == DiagnosticCodes.UnknownFormReference);

    [Fact]
    public void ScoreWithoutFormName_IsFlagged()
        => Assert.Contains(Validate("form main { A B }\nscore { staff { melody } }"),
            d => d.Code == DiagnosticCodes.UnknownFormReference);
}
