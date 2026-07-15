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
/// A <c>partial</c> (pickup) shortens the opening bar for every part of a section at once, so
/// in a structured file it must be a section directive — not a top-level global, a part header,
/// or a per-voice directive. Bare music (no sections) is exempt: a leading partial is just that
/// note stream's pickup.
/// </summary>
[Trait("Category", "Unit")]
public class PartialScopeValidatorTests
{
    private static int ErrCount(string src)
        => SemanticValidation.Run(SyntaxTree.Parse(src))
            .Count(d => d.Code == DiagnosticCodes.PartialOutsideSection);

    // --- Allowed: a section directive (parent is the section node) ---

    [Fact]
    public void SectionMajorHeaderPartial_Ok()
        => Assert.Equal(0, ErrCount(
            "time 4/4\nsection A { partial 4  melody { g4 | c' d' e' f' | } }\nform main { A }\nscore main { staff melody }"));

    [Fact]
    public void StandalonePartMajorHeaderPartial_Ok()
        => Assert.Equal(0, ErrCount(
            "part melody { section A { g4 | c' d' e' f' | } }\nsection A { partial 4 }\nform main { A }\nscore main { staff melody }"));

    [Fact]
    public void SingleVoiceSectionBodyPartial_Ok()
        // A TOP-LEVEL single-voice section: the section itself IS the lone voice, so its
        // partial is the section's pickup. (A partial in a part-MAJOR cell is different — it
        // belongs to only one part; see PartialInPartMajorCell_Errors.)
        => Assert.Equal(0, ErrCount(
            "time 4/4\nsection A { partial 4  g4 | c' d' e' f' | }\nform main { A }\nscore main { staff melody }"));

    // --- Rejected: not a section directive ---

    [Fact]
    public void PartialInPartMajorCell_Errors()
        // A section nested in a `part {}` is one part's cell; a section-wide pickup does not
        // belong there — write it in a standalone `section A { partial N }` header instead.
        => Assert.Equal(1, ErrCount(
            "part melody { section A { partial 4  g4 | c' d' e' f' | } }\nform main { A }\nscore main { staff melody }"));

    [Fact]
    public void TopLevelPartial_InStructuredFile_Errors()
        => Assert.Equal(1, ErrCount(
            "partial 4\nsection A { melody { c4 d e f | } }\nform main { A }\nscore main { staff melody }"));

    [Fact]
    public void PartialInsideAPartBlock_Errors()
        // The old pre-model form: partial nested in a part block applies to only that one part.
        => Assert.Equal(1, ErrCount(
            "section A { melody { partial 4  c4 d e f | } }\nform main { A }\nscore main { staff melody }"));

    // --- Exempt: bare music has no sections ---

    [Fact]
    public void BareMusicPartial_Ok()
        => Assert.Equal(0, ErrCount("time 4/4 partial 4 g4 | c4 d e f |"));
}
