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
/// <c>clef</c> and <c>octave</c> beside a section's part cells belong to no cell, and are
/// refused there (LYS1035, user decision 2026-08-31).
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ THE SILENT ROWS CARRY THE RULE. The keyword is not what decides — the POSITION is —
/// and the three shapes that engrave a clef correctly are pinned here beside the one that
/// does not, because a predicate written on the keyword alone would break all three.
/// </para>
/// <para>
/// ⚠️ ONE ROW IS A KNOWN HOLE, DELIBERATELY PINNED AS IT STANDS: the part-block option
/// <c>section A { m clef bass { … } }</c> is a second spelling of the same mistake and is
/// still silent. It is asserted silent so the day it closes this test names the line to
/// change, rather than the hole being forgotten (HANDOFF §2 F ⒭).
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class PartSettingInSectionHeaderValidatorTests
{
    private static IReadOnlyList<Diagnostic> Reports(string source)
    {
        var validator = new PartSettingInSectionHeaderValidator();
        validator.Validate(SyntaxTree.Parse(source));
        return validator.Diagnostics
            .Where(d => d.Code == DiagnosticCodes.PartSettingInSectionHeader
                        && d.Severity == DiagnosticSeverity.Error)
            .ToList();
    }

    private const string Score = "form main { ~A }\nscore main { staff m }\n";

    [Theory]
    // beside a part cell — the shape the decision names
    [InlineData("part m { clef treble }\nsection A { clef bass  m { c'4 c c c | } }\n")]
    // `octave` is the same mistake, and worse: it moves no pitch while the LilyPond twin
    // flips the whole part's octave model with it.
    [InlineData("part m { clef treble }\nsection A { octave absolute  m { c'4 c c c | } }\n")]
    // a section whose cells are a track rather than a part has nowhere to put one either
    [InlineData("time 4/4\npart m { clef treble }\n"
        + "section A { clef bass  chords p { C Am | } m { c'4 c c c | } }\n")]
    // ⚠️ A DIRECTIVES-ONLY HEADER standing beside the parts — the shape GRAMMAR.md documents
    // for `key`. It holds no cells either, so a cells-only predicate let it through, and the
    // LSP's convert-layout command then folds it into the section-major section and produces
    // a book this very rule refuses (MEASURED: clean before, LYS1035 after). Refusing it at
    // the source is what keeps the editor from handing the author an uncompilable file.
    [InlineData("part m { clef treble\n  section A { c'4 c c c | }\n}\nsection A { clef bass }\n")]
    public void APartSettingWithNoStreamToJoin_IsRefused(string book)
        => Assert.Single(Reports(book + Score));

    [Theory]
    // part-major: the section's body IS that part's music, so the clef is ordinary music
    [InlineData("part m { clef treble\n  section A { clef bass c'4 c c c | }\n}\n")]
    // a single-part piece writing bare music in a section (GRAMMAR.md allows it): same
    [InlineData("part m { clef treble }\nsection A { clef bass c'4 c c c | }\n")]
    // and inside a cell's music, which is where a mid-piece change goes
    [InlineData("part m { clef treble }\nsection A { m { clef bass c'4 c c c | } }\n")]
    // ⚠️ A KNOWN HOLE, NOT A BLESSING: the part-block OPTION spelling is still silent.
    // When it closes, this row is the one to move up to the theory above.
    [InlineData("part m { clef treble }\nsection A { m clef bass { c'4 c c c | } }\n")]
    public void AClefThatSomethingReads_IsLeftAlone(string book)
        => Assert.Empty(Reports(book + Score));
}
