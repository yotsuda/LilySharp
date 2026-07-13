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
/// A section's plain (unbracketed) verse only fills an occurrence NO <c>[N. …]</c> verse
/// covers. When every written-out occurrence already has a numbered verse, the plain line
/// never renders — it is silently shadowed, so LYS4004 flags it. If any occurrence is left
/// for the plain line to fill, it is genuinely used and nothing is reported.
/// </summary>
[Trait("Category", "Unit")]
public class LyricPlainVerseShadowedValidatorTests
{
    private static bool PlainShadowed(string source)
    {
        var validator = new LyricPlainVerseShadowedValidator();
        validator.Validate(SyntaxTree.Parse(source));
        return validator.Diagnostics.Any(d => d.Code == DiagnosticCodes.LyricPlainVerseShadowed);
    }

    private const string Melody = """
        time 4/4
        key c major
        part melody { clef treble
          section A { c'4 d' e' f' | }
        }
        """;

    [Fact]
    public void EveryOccurrenceCoveredByAVolta_PlainVerseIsFlagged()
    {
        // A is sung twice; [1.] and [2.] cover both occurrences, so the plain
        // "zz zz zz zz" line can never render.
        Assert.True(PlainShadowed(Melody + """
            lyrics { section A { [1. one two three four |] [2. aa bb cc dd |] zz zz zz zz | } }
            form main { A A }
            score main { staff melody }
            """));
    }

    [Fact]
    public void AnUncoveredOccurrenceUsesThePlainVerse_NotFlagged()
    {
        // A is sung three times; [1.] and [2.] cover the first two, so the third
        // occurrence falls back to the plain line — it is used, not shadowed.
        Assert.False(PlainShadowed(Melody + """
            lyrics { section A { [1. one two three four |] [2. aa bb cc dd |] zz zz zz zz | } }
            form main { A A A }
            score main { staff melody }
            """));
    }

    [Fact]
    public void PlainOnlySection_NoVoltas_NotFlagged()
    {
        // No brackets at all: the plain line repeats under every occurrence as before.
        Assert.False(PlainShadowed(Melody + """
            lyrics { section A { do re mi fa | } }
            form main { A A }
            score main { staff melody }
            """));
    }
}
