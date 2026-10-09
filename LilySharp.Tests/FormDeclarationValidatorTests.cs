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
        => Assert.Empty(Validate("form { A B }\nscore { staff { melody } }"));

    [Fact]
    public void MultipleNamedForms_NoError()
        => Assert.Empty(Validate(
            "form { A B }\nform excerpt { B }\n"
            + "score { staff { melody } }\nscore excerpt { form excerpt staff { melody } }"));

    [Fact]
    public void ASecondUnnamedForm_IsFlagged()
        => Assert.Contains(Validate("form { A B }\nform { B }\nscore { staff { melody } }"),
            d => d.Code == DiagnosticCodes.UnnamedForm);

    [Fact]
    public void DuplicateFormName_IsFlagged()
        => Assert.Contains(Validate("form x { A }\nform x { B }\nscore { staff { melody } }"),
            d => d.Code == DiagnosticCodes.DuplicateFormName);

    [Fact]
    public void AScoresFormNamingNoForm_IsFlagged()
        => Assert.Contains(Validate("form { A B }\nscore { form verse staff { melody } }"),
            d => d.Code == DiagnosticCodes.UnknownFormReference && d.Message.Contains("Unknown form 'verse'"));

    /// <summary>`form A` with A a section is the likeliest slip: the message writes the fix.</summary>
    [Fact]
    public void AScoresFormNamingASection_SaysToWriteItsOwnForm()
        => Assert.Contains(Validate("form { A B }\nscore { form A staff { melody } }"),
            d => d.Code == DiagnosticCodes.UnknownFormReference && d.Message.Contains("'form { A }'"));

    [Fact]
    public void AScoresOwnForm_IsClean()
        => Assert.Empty(Validate("form { A B }\nscore { form { B A } staff { melody } }"));

    [Fact]
    public void AScoresOwnForm_IsUnnamed()
        => Assert.Contains(Validate("score { form x { A } staff { melody } }"),
            d => d.Code == DiagnosticCodes.UnknownFormReference);

    [Fact]
    public void AScorePlaysOneForm()
        => Assert.Contains(Validate("form x { A }\nscore { form x form { B } staff { melody } }"),
            d => d.Code == DiagnosticCodes.UnknownFormReference && d.Message.Contains("one form"));

    /// <summary>The spelling before 2026-10-09: the score's name picked the form. It is the
    /// score's own name now — said, not played in silence.</summary>
    [Fact]
    public void AScoreNamedLikeAForm_IsWarnedThatTheNameDoesNotPickIt()
    {
        var d = Assert.Single(Validate("form { A }\nform verse { B }\nscore verse { staff { melody } }"));
        Assert.Equal(DiagnosticCodes.UnknownFormReference, d.Code);
        Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
        Assert.Contains("write 'form verse'", d.Message);
    }

    [Fact]
    public void AnUnnamedScoreWithNoForm_PlaysTheDefault_AndIsClean()
        => Assert.Empty(Validate("form { A B }\nscore { staff { melody } }"));
}
