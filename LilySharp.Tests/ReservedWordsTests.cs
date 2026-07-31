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

using System.Collections.Generic;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Guards the reserved-word documentation (docs/SYNTAX_REFERENCE.md "Reserved Words" and
/// docs/GRAMMAR_FOR_LLM.md) against the parser. A reserved word must NOT be usable as a
/// bare name; the four clef-name words and ordinary annotation names MUST be. Probed
/// behaviourally through 'phrase NAME { ... }' so it tracks real parser behaviour.
/// </summary>
[Trait("Category", "Unit")]
public class ReservedWordsTests
{
    // The documented reserved words (mirror of the SYNTAX_REFERENCE.md table), minus the
    // four clef names which are intentionally allowed as names (see ClefNamesAreNames).
    public static readonly string[] Reserved =
    {
        "section", "form", "using", "tab", "ossia", "transpose", "octave",
        "instrument",
        "score", "part", "staff", "grandStaff", "voice", "phrase", "repeat", "volta",
        "alternative", "break", "partial",
        "title", "composer", "tempo", "time", "key", "clef",
        "major", "minor", "dorian", "phrygian", "lydian", "mixolydian", "aeolian", "locrian",
        "tuplet", "grace", "acciaccatura", "appoggiatura", "lyrics", "chords",
        "tuning",
        "override", "revert", "once", "with",
        "segno", "fine", "coda", "dc", "ds", "al", "to",
        "ppp", "pp", "p", "mp", "mf", "f", "ff", "fff",
    };

    [Theory]
    [MemberData(nameof(ReservedData))]
    public void ReservedWord_IsNotUsableAsAName(string word)
    {
        // A keyword used where a phrase name is expected must be rejected.
        var tree = SyntaxTree.Parse($"phrase {word} {{ c4 }}");
        Assert.True(tree.HasErrors, $"'{word}' is documented as reserved but parses as a name.");
    }

    public static IEnumerable<object[]> ReservedData()
    {
        foreach (var w in Reserved) yield return new object[] { w };
    }

    [Theory]
    [InlineData("treble")]
    [InlineData("bass")]
    [InlineData("alto")]
    [InlineData("tenor")]
    public void ClefNames_AreUsableAsNames(string word)
    {
        // Clef-name words are the documented exception: valid as part/section/phrase names.
        var tree = SyntaxTree.Parse($"phrase {word} {{ c4 }}");
        Assert.False(tree.HasErrors,
            $"'{word}' should be usable as a name (documented clef-name exception).");
    }

    [Theory]
    [InlineData("staccato")]  // articulation name — resolved from @text, not reserved
    [InlineData("tr")]
    [InlineData("cresc")]
    [InlineData("melody")]    // ordinary user name
    [InlineData("myTheme")]
    public void NonReservedWord_IsUsableAsAName(string word)
    {
        var tree = SyntaxTree.Parse($"phrase {word} {{ c4 }}");
        Assert.False(tree.HasErrors, $"'{word}' is not reserved and should parse as a name.");
    }
}
