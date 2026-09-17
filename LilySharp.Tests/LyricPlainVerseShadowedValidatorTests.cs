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
            lyrics w sings melody { section A { [1. one two three four |] [2. aa bb cc dd |] zz zz zz zz | } }
            form main { A A }
            score main { staff melody  lyrics w }
            """));
    }

    [Fact]
    public void AnUncoveredOccurrenceUsesThePlainVerse_NotFlagged()
    {
        // A is sung three times; [1.] and [2.] cover the first two, so the third
        // occurrence falls back to the plain line — it is used, not shadowed.
        Assert.False(PlainShadowed(Melody + """
            lyrics w sings melody { section A { [1. one two three four |] [2. aa bb cc dd |] zz zz zz zz | } }
            form main { A A A }
            score main { staff melody  lyrics w }
            """));
    }

    [Fact]
    public void PlainOnlySection_NoVoltas_NotFlagged()
    {
        // No brackets at all: the plain line repeats under every occurrence as before.
        Assert.False(PlainShadowed(Melody + """
            lyrics w sings melody { section A { do re mi fa | } }
            form main { A A }
            score main { staff melody  lyrics w }
            """));
    }

    // ---- session 399: the validator reads the SHARED collect instead of making its own ----

    private const string TwoParts = """
        time 4/4
        key c major
        part melody { clef treble
          section A { c'4 d' e' f' | }
        }
        part harmony { clef treble
          section A { a4 b c' d' | }
        }
        """;

    [Fact]
    public void AShadowedVerseUnderTheSecondStaff_IsFlagged()
    {
        // The words are the harmony's, placed under the SECOND staff. The validator used
        // to collect the first declared part alone and could not see this line at all; the
        // shared collect is every staff the score draws, so it is reported like the
        // melody's would be.
        Assert.True(PlainShadowed(TwoParts + """
            lyrics w sings harmony { section A { [1. one two three four |] [2. aa bb cc dd |] zz zz zz zz | } }
            form main { A A }
            score main { staff melody  staff harmony  lyrics w }
            """));
    }

    [Fact]
    public void ItReadsTheLentCollect_NotOneOfItsOwn()
    {
        // The poison that proves the validator no longer collects for itself: lend it the
        // collect of a book whose plain verse IS shadowed while the tree it validates has
        // no lyrics at all — the diagnostic follows the lent collect.
        var shadowed = SyntaxTree.Parse(Melody + """
            lyrics w sings melody { section A { [1. one two three four |] [2. aa bb cc dd |] zz zz zz zz | } }
            form main { A A }
            score main { staff melody  lyrics w }
            """);
        var lent = SemanticValidation.TryCollect(shadowed);
        Assert.NotNull(lent);
        Assert.NotEmpty(lent!.LyricShadowedPlainWarnings);

        var wordless = SyntaxTree.Parse(Melody + """
            form main { A A }
            score main { staff melody }
            """);
        var validator = new LyricPlainVerseShadowedValidator();
        validator.ValidateWith(wordless, new System.Lazy<LilySharp.Core.Svg.Collector.MeasureCollector?>(() => lent));
        Assert.Contains(validator.Diagnostics, d => d.Code == DiagnosticCodes.LyricPlainVerseShadowed);

        // And the same validator on its own answers for the wordless tree: nothing.
        Assert.False(PlainShadowed(wordless.Text));
    }
}
