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

/// <summary>
/// A lyrics line with MORE syllables than its notes drops the trailing syllables
/// silently (the bug this guards). The validator warns on that overflow; a line
/// that exactly matches, or is shorter (melisma / instrumental tail), stays quiet.
/// </summary>
[Trait("Category", "Unit")]
public class LyricSyllableValidatorTests
{
    private static IReadOnlyList<Diagnostic> Validate(string source)
    {
        var tree = SyntaxTree.Parse(source);
        var validator = new LyricSyllableValidator();
        validator.Validate(tree);
        return validator.Diagnostics;
    }

    private static Diagnostic? Overflow(IReadOnlyList<Diagnostic> diags) =>
        diags.FirstOrDefault(d => d.Code == DiagnosticCodes.LyricSyllableOverflow);

    [Fact]
    public void MoreSyllablesThanNotes_Warns()
    {
        // 4 notes, 5 syllables — "five" runs off the end and is dropped.
        var diags = Validate("time 4/4\n{ c4 d e f }\nlyrics { one two three four five }\n");
        var warning = Overflow(diags);
        Assert.NotNull(warning);
        Assert.Equal(DiagnosticSeverity.Warning, warning!.Severity);
        // Names the exact dropped word and its bar within the lyric line.
        Assert.Contains("lyric syllable 'five' (bar 1 of its lyrics line)", warning.Message);
        Assert.Contains("it will not be shown", warning.Message);
    }

    [Fact]
    public void TwoExtraSyllables_NamesFirstAndCountsTheRest()
    {
        var diags = Validate("time 4/4\n{ c4 d e f }\nlyrics { a b c d e f }\n");
        var warning = Overflow(diags);
        Assert.NotNull(warning);
        Assert.Contains("lyric syllable 'e'", warning!.Message);
        Assert.Contains("it and the 1 after it will not be shown", warning.Message);
    }

    [Fact]
    public void ExactMatch_NoWarning()
    {
        var diags = Validate("time 4/4\n{ c4 d e f }\nlyrics { one two three four }\n");
        Assert.Null(Overflow(diags));
    }

    [Fact]
    public void FewerSyllablesThanNotes_NoWarning()
    {
        // A short lyric line is normal (melisma / instrumental tail), never overflow.
        var diags = Validate("time 4/4\n{ c4 d e f }\nlyrics { one two }\n");
        Assert.Null(Overflow(diags));
    }

    [Fact]
    public void NoLyrics_NoWarning()
    {
        var diags = Validate("time 4/4\n{ c4 d e f }\n");
        Assert.Null(Overflow(diags));
    }

    [Fact]
    public void WarningAnchorsAtTheFirstDroppedSyllable()
    {
        var source = "time 4/4\n{ c4 d e f }\nlyrics { one two three four five }\n";
        var warning = Overflow(Validate(source));
        Assert.NotNull(warning);
        // Span points at the first word that vanished, so the editor squiggle
        // lands exactly where the miscount starts.
        Assert.Equal("five", source.Substring(warning!.Span.Start, warning.Span.Length));
    }
}
