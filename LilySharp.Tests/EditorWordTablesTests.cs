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

using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The editor's own tables of block words, held to the language (session 1295, review F7).
/// The extension's editor-free halves (<c>editors/vscode/src/*Core.ts</c>) decide a keystroke
/// without the server, so each spells by hand the words that open the blocks it cares about —
/// and nothing held those spellings to the lexer or to <see cref="SyntaxFacts"/>. When this
/// test was written they named five words Lily# does not have (LilyPond's <c>header</c>,
/// <c>midi</c>, <c>markup</c>, <c>alternative</c>, <c>tremolo</c>: a part named <c>midi</c>
/// lost its smart typing, one named <c>tremolo</c> was not seen as a part), and the list of
/// words that are not a part's cell differed from the parser's
/// (<see cref="SyntaxFacts.PartNameReservedVocabulary"/>) in both directions.
/// </summary>
/// <remarks>
/// The TypeScript is READ, not run: a table is the string literals between the brackets of
/// <c>const NAME = new Set([ … ])</c> or <c>const NAME = [ … ]</c>. A table rewritten into
/// another shape fails <see cref="Table"/> by name rather than passing on nothing.
/// </remarks>
public class EditorWordTablesTests
{
    /// <summary>A word that opens a block is a word of the language: the lexer gives it a
    /// keyword's kind. (A word it reads as an identifier is a NAME — a part's, a section's —
    /// and a table that lists it takes that part for something else.)</summary>
    [Theory]
    [InlineData("chatPointerCore.ts", "NOT_A_PART")]
    [InlineData("chatPointerCore.ts", "DIRECTIVES")]
    [InlineData("smartTypingCore.ts", "WORD_BLOCKS")]
    [InlineData("smartTypingCore.ts", "NOT_BARS")]
    [InlineData("smartTypingCore.ts", "SETTINGS_BLOCKS")]
    [InlineData("staffGroupWrapCore.ts", "STAFF_GROUPS")]
    public void EveryWordOfATable_IsAKeywordOfTheLanguage(string file, string name)
    {
        var notKeywords = Table(file, name)
            .Where(word => LilySharp.Core.Parser.Lexer.GetKeywordKind(word) == SyntaxKind.Identifier)
            .ToArray();
        Assert.True(notKeywords.Length == 0,
            $"{file}: {name} lists {string.Join(", ", notKeywords)} — not keywords of Lily#, so a part "
            + "or a section may carry the name, and the table takes it for a block of another kind.");
    }

    /// <summary>In a section a <c>WORD {</c> is a part's cell unless the word cannot name a
    /// part — the parser's rule, <see cref="SyntaxFacts.PartNameReservedVocabulary"/>. The
    /// chat pointer's table is that list: a word it lacks is reported as a part (<c>strokes</c>,
    /// <c>key</c>), a word it has beyond it hides a part of that name (<c>tuplet</c>). Until
    /// session 1295 it was 17 words of its own (<c>MUSIC_BLOCKS</c>), 37 of the list missing and
    /// four not in it.</summary>
    [Fact]
    public void TheChatPointersNotAPartWords_AreTheWordsThatCannotNameAPart()
    {
        var table = Table("chatPointerCore.ts", "NOT_A_PART");
        var reserved = SyntaxFacts.PartNameReservedVocabulary;
        var missing = reserved.Except(table, StringComparer.Ordinal).ToArray();
        var extra = table.Except(reserved, StringComparer.Ordinal).ToArray();
        Assert.True(missing.Length == 0 && extra.Length == 0,
            "chatPointerCore.ts NOT_A_PART is not SyntaxFacts.PartNameReservedVocabulary — "
            + $"missing: {string.Join(", ", missing)}; not in the language's list: {string.Join(", ", extra)}");
        Assert.Equal(reserved, table);      // (and in its order, so the two read side by side)
    }

    /// <summary>The words the chat pointer reads past on its way to a cell's name
    /// (<c>key d major  vn1 {</c> is vn1's cell): every one cannot name a part, and every
    /// setting a section takes (<see cref="SyntaxFacts.SectionSettingVocabulary"/>) is one —
    /// a setting missing here makes the cell after it a block of no part.</summary>
    [Fact]
    public void TheChatPointersDirectives_HoldEverySectionSetting_AndNoWordAPartCanBeNamed()
    {
        var directives = Table("chatPointerCore.ts", "DIRECTIVES");
        Assert.Empty(SyntaxFacts.SectionSettingVocabulary.Except(directives, StringComparer.Ordinal));
        Assert.Empty(directives.Except(SyntaxFacts.PartNameReservedVocabulary, StringComparer.Ordinal));
    }

    /// <summary>The groups "Wrap in a Staff Group" offers are the score items that take rows
    /// in braces — asked of the PARSER, one keyword of
    /// <see cref="SyntaxFacts.ScoreItemKeywordVocabulary"/> at a time (the parser dispatches on
    /// kinds and has no list to read): a word is a group when <c>WORD { staff a  staff b }</c>
    /// or <c>WORD { a b }</c> parses clean into a group's node. In the vocabulary's order,
    /// which is the order the list shows.</summary>
    [Fact]
    public void TheStaffGroupsOffered_AreTheGroupsTheParserTakes_InTheVocabularysOrder()
    {
        var groups = SyntaxFacts.ScoreItemKeywordVocabulary.Where(IsGroup).ToArray();
        Assert.NotEmpty(groups);    // (else the probe below parses nothing as a group, and the test compares nothing)
        Assert.Equal(groups, Table("staffGroupWrapCore.ts", "STAFF_GROUPS"));

        static bool IsGroup(string word) =>
            ParsesAsGroup($"{word} {{ staff a  staff b }}") || ParsesAsGroup($"{word} {{ a b }}");

        static bool ParsesAsGroup(string item)
        {
            var tree = SyntaxTree.Parse($$"""
                part a { clef treble }
                part b { clef bass }
                section A { a { c1 | } b { c1 | } }
                form { A }
                score { {{item}} }
                """);
            return !tree.HasErrors && tree.GetRoot().DescendantNodes().Any(n =>
                n is GrandStaffRenderSyntax or CondensedStaffRenderSyntax or CombinedStaffRenderSyntax);
        }
    }

    /// <summary>The string literals of the table <paramref name="name"/> in an editor source.</summary>
    private static string[] Table(string file, string name)
    {
        var ts = File.ReadAllText(RepoFile($"editors/vscode/src/{file}"));
        var m = Regex.Match(ts, @"\bconst\s+" + Regex.Escape(name) + @"\s*=\s*(?:new Set\(\s*)?\[(?<body>[^\]]*)\]");
        Assert.True(m.Success, $"{file} no longer writes `const {name} = [ … ]` or `new Set([ … ])` — this test reads that shape.");
        var words = Regex.Matches(m.Groups["body"].Value, @"'([^']+)'").Select(w => w.Groups[1].Value).ToArray();
        Assert.NotEmpty(words);
        return words;
    }

    private static string RepoFile(string relative)
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new FileNotFoundException($"Cannot find {relative} walking up from {AppContext.BaseDirectory}");
    }
}
