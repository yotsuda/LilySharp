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

/// <summary>
/// An unclosed <c>|:</c> in the <c>form</c> is ONE error, at the <c>|:</c>, and the rest of
/// the file still parses.
/// </summary>
/// <remarks>
/// The item loop ran to END OF FILE looking for a <c>:|</c> that was never coming, so
/// <c>form main { ~Body |: A }</c> reported the form's own <c>}</c>, then <c>score</c>,
/// <c>{</c>, <c>staff</c> and <c>}</c> as five things "a form cannot hold", and only then
/// said "Expected RepeatEndBar, found EndOfFile" — five wrong errors before the true one,
/// with the score block declared garbage. Reported 2026-08-31 on
/// scratch/ベースタブLy/Venus.lys.
/// <para>
/// ⚠️ THE AUTHOR'S MISTAKE IS WORTH NAMING, because the pairing crosses the form/music line
/// in ONE DIRECTION ONLY: a <c>|:</c> written in a SECTION may be closed by a <c>:|</c> the
/// form writes (LYS4017 is deferred to score expansion for exactly that reason, and books in
/// the wild are spelled that way), while the form's repeat is a BRACKETED construct that a
/// <c>:|</c> in the music cannot close. Moving only the <c>|:</c> into the form lands here,
/// and the message says which half is missing and which direction does work.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class FormRepeatUnclosedDiagnosticTests
{
    private const string Book = """
        time 4/4
        part m {
          section B { c'4 c c c | }
          section A { d'4 d d d | [1. e'4 e e e | ] :| [2. f'4 f f f | ] }
        }
        form main { ~B |: A }
        score main { staff m }
        """;

    private static Diagnostic[] Errors(string src) => SyntaxTree.Parse(src).Diagnostics
        .Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();

    [Fact]
    public void AnUnclosedFormRepeatIsExactlyOneError()
    {
        var errors = Errors(Book);
        Assert.Single(errors);
        Assert.Equal(DiagnosticCodes.UnpairedRepeat, errors[0].Code);
    }

    [Fact]
    public void ItPointsAtTheOpeningBarlineAndNotAtTheEndOfTheFile()
    {
        var errors = Errors(Book);
        // The `|:` of `form main { ~B |: A }` — not the `}` five tokens later, and not EOF.
        Assert.Equal(Book.IndexOf("|: A"), errors[0].Span.Start);
    }

    [Fact]
    public void TheRestOfTheFileStillParses()
    {
        // The score block is what the runaway used to swallow: with the loop stopped at the
        // form's own brace it is a RenderDeclaration again, not five stray form items.
        var root = SyntaxTree.Parse(Book).GetRoot();
        Assert.Single(root.DescendantNodes().OfType<RenderDeclarationSyntax>());
        Assert.Equal(2, root.DescendantNodes().OfType<SectionDeclarationSyntax>().Count());
    }

    [Fact]
    public void ClosingItInTheFormIsAccepted()
        => Assert.Empty(Errors(Book.Replace("|: A }", "|: A :| }")));

    [Fact]
    public void TheOtherDirectionStillWorks()
        // A `|:` in the section's music closed by a `:|` the form writes — the spelling the
        // message points at, and the one LYS4017 defers to score expansion to allow.
        => Assert.Empty(Errors("""
            time 4/4
            part m {
              section B { c'4 c c c | }
              section A { |: d'4 d d d | }
            }
            form main { ~B A :| }
            score main { staff m }
            """));
}
