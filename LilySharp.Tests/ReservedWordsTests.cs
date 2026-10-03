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
using System.Linq;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Guards the reserved-word documentation (docs/SYNTAX_REFERENCE.md "Reserved Words" and
/// docs/GRAMMAR_FOR_LLM.md) against the parser, probed behaviourally through
/// <c>phrase NAME { … }</c> and <c>part NAME { … }</c> so it tracks real parser behaviour.
/// </summary>
/// <remarks>
/// Since 2026-10-03 (owner's decision) the two positions differ: a PART may be named any
/// keyword outside <see cref="SyntaxFacts.PartNameReservedVocabulary"/>, while a PHRASE —
/// referenced bare in a music stream — may be named only for a word the stream reads back
/// as a reference: an identifier, a clef word or a dynamic word. The reserved table in the
/// documents is the lexer's, and <c>DocKeywordListTests</c> holds it to the lexer; these
/// tests hold the two NAME rules to the parser.
/// </remarks>
[Trait("Category", "Unit")]
public class ReservedWordsTests
{
    // The documented reserved words (mirror of the SYNTAX_REFERENCE.md table), minus the
    // four clef names and the seven dynamic words, which are usable as phrase names (see
    // ClefNames_AreUsableAsNames / DynamicWords_AreUsableAsPhraseNames).
    public static readonly string[] Reserved =
    {
        "section", "form", "using", "tab", "ossia", "transpose", "octave",
        "instrument",
        "score", "part", "staff", "grandStaff", "voice", "phrase", "repeat",
        "break", "partial",
        "title", "composer", "tempo", "time", "key", "clef",
        "major", "minor", "dorian", "phrygian", "lydian", "mixolydian", "aeolian", "locrian",
        "tuplet", "grace", "acciaccatura", "appoggiatura", "lyrics", "chords",
        "tuning",
        "override", "revert", "once",
        "segno", "fine", "coda", "dc", "ds", "al", "to",
        "f",   // the pitch F — a bare `f` is a note, so a phrase cannot be named it
    };

    public static readonly string[] Dynamics = { "ppp", "pp", "p", "mp", "mf", "ff", "fff" };

    [Theory]
    [MemberData(nameof(ReservedData))]
    public void ReservedWord_IsNotUsableAsAPhraseName(string word)
    {
        // A keyword used where a phrase name is expected must be rejected — as a syntax
        // error for a structural word, as LYS1030 for a word a part may be named but a bare
        // reference could never reach.
        var tree = SyntaxTree.Parse($"phrase {word} {{ c4 }}");
        Assert.True(tree.HasErrors, $"'{word}' is documented as reserved but parses as a phrase name.");
    }

    public static IEnumerable<object[]> ReservedData()
    {
        foreach (var w in Reserved) yield return new object[] { w };
    }

    [Theory]
    [MemberData(nameof(ReservedData))]
    public void ReservedWord_NamesAPart_UnlessItIsStructural(string word)
    {
        // The part-name rule: any bare word outside the structural list. The list is the
        // parser's own (SyntaxFacts.PartNameReservedVocabulary), so this says in both
        // directions what the documents say in prose.
        var tree = SyntaxTree.Parse($"part {word} {{ clef treble }}");
        bool structural = SyntaxFacts.PartNameReservedVocabulary.Contains(word);
        Assert.Equal(structural, tree.HasErrors);
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

    [Fact]
    public void DynamicWords_AreUsableAsPhraseNames()
    {
        // A bare `p` in a music stream was a stray item; since 2026-10-03 it plays the phrase.
        foreach (string word in Dynamics)
        {
            var tree = SyntaxTree.Parse($"phrase {word} {{ c4 }}");
            Assert.False(tree.HasErrors, $"'{word}' should be usable as a phrase name.");
        }
        // The lexer keeps `f` as the pitch, which is why it is in Reserved above and not here.
        Assert.Equal(7, Dynamics.Length);
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
