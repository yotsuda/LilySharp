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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using LilySharp.Core.Parser;
using LilySharp.Core.Rendering;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The editor's TextMate grammar against the language it claims to colour: every word a
/// writer can actually type has a colour, and no rule in the grammar claims a word is WRONG.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ Colour reaches a Lily# file by two paths and, until 2026-08-18, nothing compared them.
/// <c>editors/vscode/syntaxes/lilysharp.tmLanguage.json</c> is a regular expression per line
/// with no context; <c>LilySharpLanguageServer.SemanticTokens</c> reads the real tree. VS Code
/// LAYERS the second over the first, so a word the grammar misses is uncoloured unless the
/// server also names its kind — and 23 writable reserved words were missed by both.
/// </para>
/// <para>
/// ★ The other half is the decision this file ratchets: <b>"this is wrong" is not the
/// grammar's to say.</b> An <c>invalid.illegal.removed-keyword</c> rule painted every
/// <c>volta</c> and <c>alternative</c> red wherever it stood. Measured 2026-08-18:
/// <c>fonts { volta "TeX Gyre Schola" }</c> is ACCEPTED (volta is a text role) while
/// <c>repeat volta 2 { … }</c> is refused with LYS0006 — and one line of regex holds no
/// information that separates the two. Wrongness belongs to the diagnostics, which know the
/// position; <see cref="TheGrammarCallsNothingInvalid"/> is that decision, written down.
/// </para>
/// <para>
/// ⚠️ And a reserved word is not the whole vocabulary. The keys of a <c>fonts { }</c> block —
/// <c>serif</c>, <c>marks</c>, <c>barNumber</c> — are the language's words only INSIDE that
/// block and name a part anywhere else, so no check over reserved words can reach them. They
/// were plain while <c>tempo</c> beside them was coloured, for the unrelated reason that
/// <c>tempo</c> is reserved. <see cref="EveryFontsBlockKey_IsColoured"/> holds that vocabulary
/// to <c>TextRoles.AllKeySpellings</c> instead.
/// </para>
/// <para>
/// ⚠️ What this net CANNOT see, said plainly so the next reader does not over-trust it:
/// rule PRECEDENCE. It asks whether some reachable pattern matches the bare word end to end,
/// not whether that pattern is the one that WINS at a given spot — the grammar includes
/// <c>#comments</c> and <c>#strings</c> first, so a keyword inside either is correctly left
/// alone, and this file cannot say so. Nor can the GRAMMAR colour a word whose meaning
/// depends on the node above it: <c>swing</c> and <c>shuffle</c> are the language's words only
/// in a tempo's value run, and they belong to the semantic tokens, which have the tree —
/// <c>TempoFeelWordTokenTests</c> is their net, and three regexes were tried and discarded
/// before that was accepted. (REACH it does see, and at two levels since 2026-08-19:
/// <see cref="Reachable"/> for the patterns a word could match, and
/// <see cref="EveryRuleInTheGrammar_IsReachable"/> for a whole rule going dark — the second
/// added after one did.) Nor does it know that a <c>lyrics { }</c> body
/// deliberately colours nothing. One question, asked of every word; that question is what the
/// 23 and the 26 escaped.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class EditorColouringTests
{
    private static string GrammarPath =>
        Path.Combine(CollectResumeTests.FindRepoRoot(),
            "editors", "vscode", "syntaxes", "lilysharp.tmLanguage.json");

    /// <summary>
    /// Words that are reserved so they CANNOT be taken as a name, and that no position
    /// accepts — painting them as keywords would advertise a spelling nobody may type.
    /// </summary>
    /// <remarks>
    /// ⚠️ "in the lexer" does not mean "writable", and reading it that way is how a first
    /// draft of this net published the level marks as a defect. Both entries are measured,
    /// not recalled — see <see cref="TheCheckCanFail"/>.
    /// <list type="bullet">
    /// <item>The level marks are written <c>@p</c>; the bare word is reserved only so that no
    /// part may be named <c>p</c>. The grammar colours them in the <c>@</c> form already.</item>
    /// <item><c>alternative</c> is reachable only inside <c>repeat volta</c>, which is removed,
    /// so every occurrence already sits under LYS0006. (<c>volta</c> is NOT here: the fonts
    /// block gives it a live spelling.)</item>
    /// </list>
    /// </remarks>
    private static readonly string[] ReservedOnlyToRefuse =
        ["p", "pp", "ppp", "mp", "mf", "ff", "fff", "alternative"];

    /// <summary>
    /// Every <c>match</c>/<c>begin</c>/<c>end</c> regex the editor can actually reach, found
    /// by following the root pattern list through its <c>#include</c>s at any depth.
    /// </summary>
    /// <remarks>
    /// ⚠️ Reading the whole JSON instead would count repository entries NOTHING INCLUDES, and
    /// a rule nobody includes colours nothing. That is not hypothetical: it is the one way a
    /// green here could be a lie — write the rule, forget the include, and every word it names
    /// still reports as coloured. <see cref="TheCheckCanFail"/> pins it by dropping an include.
    /// ★ The rule NAMES come back too, for the mirror question — see
    /// <see cref="EveryRuleInTheGrammar_IsReachable"/>. Dropping a rule out of the walk stops it
    /// from making words look coloured; it does not say anything when the rule that went dark is
    /// one no word list asks about, and that is the second half of the same fact.
    /// </remarks>
    private static (string[] Patterns, string[] Rules) Reachable()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(GrammarPath));
        var repository = doc.RootElement.GetProperty("repository");
        var found = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        Walk(doc.RootElement.GetProperty("patterns"), repository, found, seen);
        return ([.. found], [.. seen.Select(s => s[1..]).OrderBy(s => s, StringComparer.Ordinal)]);

        static void Walk(JsonElement e, JsonElement repository, List<string> into, HashSet<string> seen)
        {
            if (e.ValueKind == JsonValueKind.Array)
            {
                foreach (var x in e.EnumerateArray()) Walk(x, repository, into, seen);
                return;
            }
            if (e.ValueKind != JsonValueKind.Object) return;

            if (e.TryGetProperty("include", out var include) && include.ValueKind == JsonValueKind.String)
            {
                string target = include.GetString()!;
                if (target.StartsWith('#') && seen.Add(target)
                    && repository.TryGetProperty(target[1..], out var rule))
                {
                    Walk(rule, repository, into, seen);
                }
                return;
            }

            foreach (var p in e.EnumerateObject())
            {
                if (p.Value.ValueKind == JsonValueKind.String && p.Name is "match" or "begin" or "end")
                {
                    into.Add(p.Value.GetString()!);
                }
                else if (p.Name is "patterns" or "captures" or "beginCaptures" or "endCaptures")
                {
                    Walk(p.Value, repository, into, seen);
                }
            }
        }
    }

    private static string[] Patterns() => Reachable().Patterns;

    /// <summary>
    /// Every <c>patterns</c> array in the file — the root's and each rule's — as the sequence of
    /// its entries, an <c>#include</c> target where the entry is one and <c>null</c> where the
    /// rule is written inline.
    /// </summary>
    private static List<string?[]> PatternLists()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(GrammarPath));
        var lists = new List<string?[]>();
        Walk(doc.RootElement, lists);
        return lists;

        static void Walk(JsonElement e, List<string?[]> into)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var p in e.EnumerateObject())
                    {
                        if (p.Name == "patterns" && p.Value.ValueKind == JsonValueKind.Array)
                        {
                            into.Add([.. p.Value.EnumerateArray().Select(x =>
                                x.ValueKind == JsonValueKind.Object
                                && x.TryGetProperty("include", out var i)
                                && i.ValueKind == JsonValueKind.String
                                    ? i.GetString()
                                    : null)]);
                        }
                        Walk(p.Value, into);
                    }
                    break;
                case JsonValueKind.Array:
                    foreach (var x in e.EnumerateArray()) Walk(x, into);
                    break;
            }
        }
    }

    /// <summary>Every rule the repository DEFINES, whether or not anything includes it.</summary>
    private static string[] RepositoryRules()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(GrammarPath));
        return [.. doc.RootElement.GetProperty("repository").EnumerateObject()
            .Select(p => p.Name).OrderBy(s => s, StringComparer.Ordinal)];
    }

    /// <summary>Every scope NAME the grammar assigns to text.</summary>
    private static string[] ScopeNames()
    {
        var found = new List<string>();
        using var doc = JsonDocument.Parse(File.ReadAllText(GrammarPath));
        Walk(doc.RootElement, found);
        return [.. found];

        static void Walk(JsonElement e, List<string> into)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var p in e.EnumerateObject())
                    {
                        // The grammar's own top-level "name" is the language label ("Lily#"),
                        // not a scope; only dotted names are scopes assigned to text.
                        if (p.Value.ValueKind == JsonValueKind.String && p.Name == "name"
                            && p.Value.GetString()!.Contains('.'))
                        {
                            into.Add(p.Value.GetString()!);
                        }
                        else
                        {
                            Walk(p.Value, into);
                        }
                    }
                    break;
                case JsonValueKind.Array:
                    foreach (var x in e.EnumerateArray()) Walk(x, into);
                    break;
            }
        }
    }

    /// <summary>The reserved spellings, taken from GRAMMAR.md's <c>Keyword</c> production —
    /// the one list <c>DocKeywordListTests</c> already holds to the lexer in both
    /// directions — and then asked of the lexer one at a time.</summary>
    private static string[] ReservedSpellings()
    {
        string block = Regex.Match(
            File.ReadAllText(Path.Combine(CollectResumeTests.FindRepoRoot(), "docs", "GRAMMAR.md")),
            @"^Keyword = (.*?);", RegexOptions.Singleline | RegexOptions.Multiline).Groups[1].Value;
        Assert.False(string.IsNullOrWhiteSpace(block),
            "the Keyword production did not extract — the document's shape moved, and an "
            + "empty list would make every assertion below vacuously true");

        return Regex.Matches(block, "'([^']+)'")
            .Select(m => m.Groups[1].Value)
            .Where(w => KindOf(w) != SyntaxKind.Identifier)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(w => w, StringComparer.Ordinal)
            .ToArray();
    }

    private static SyntaxKind KindOf(string word) =>
        new Lexer(word).ScanAllTokens().First().Kind;

    /// <summary>
    /// Does this pattern colour the bare word — a match that starts at 0 and runs to the end?
    /// </summary>
    /// <remarks>
    /// ⚠️ "Does the pattern match somewhere in the word" answers wrongly, and the first
    /// measurement did: the lyric-connector rule <c>--|__|~|_</c> matches the underscore in
    /// <c>bass_8</c>, so a substring test reported both octave clefs as coloured while the
    /// editor left them plain. Covering the WHOLE word is the question — see
    /// <see cref="TheCheckCanFail"/>, which pins that pair apart.
    /// </remarks>
    private static bool Covers(string pattern, string word)
    {
        var m = Regex.Match(word, pattern);
        return m.Success && m.Index == 0 && m.Length == word.Length;
    }

    private static bool IsColoured(string word) => Patterns().Any(p => Covers(p, word));

    /// <summary>
    /// The words the grammar calls keywords ON THEIR OWN — the literals of every
    /// <c>\b( … )\b</c> alternation whose scope is a <c>keyword.</c> one.
    /// </summary>
    /// <remarks>
    /// ⚠️ Deliberately narrow, in three ways, each of which would otherwise make it lie:
    /// <list type="bullet">
    /// <item>Only the BARE-WORD shape. The <c>@</c>-prefixed rules (<c>@sfz</c>,
    /// <c>@staccato</c>) also carry a <c>keyword.</c> scope, and those names are resolved from
    /// a registry at run time and are NOT reserved — reading them here would report the whole
    /// articulation list as wrong.</item>
    /// <item>Only whole-match <c>name</c> scopes. A rule that scopes by <c>captures</c> puts
    /// its value word in a group of its own, which is exactly how a non-reserved value word is
    /// SUPPOSED to be coloured (<c>octave relative</c>).</item>
    /// <item>★ Only rules reachable WITHOUT entering a context. The claim this direction
    /// makes — a word painted as the language's own must be the language's own — holds for a
    /// rule that fires anywhere in a file, and a begin/end block is precisely the licence to
    /// paint a word that is a keyword only THERE: inside <c>fonts { }</c>, <c>barNumber</c> is
    /// the language's word and outside it a part may be called that. So the walk stops at any
    /// object carrying a <c>begin</c>.</item>
    /// </list>
    /// </remarks>
    private static string[] WordsPaintedAsKeywords()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(GrammarPath));
        var repository = doc.RootElement.GetProperty("repository");
        var found = new List<string>();
        Walk(doc.RootElement.GetProperty("patterns"), repository, found, []);
        return [.. found.Distinct(StringComparer.Ordinal).OrderBy(w => w, StringComparer.Ordinal)];

        static void Walk(JsonElement e, JsonElement repository, List<string> into, HashSet<string> seen)
        {
            if (e.ValueKind == JsonValueKind.Array)
            {
                foreach (var x in e.EnumerateArray()) Walk(x, repository, into, seen);
                return;
            }
            if (e.ValueKind != JsonValueKind.Object) return;

            // A begin/end rule is a CONTEXT; everything under it is scoped to that context.
            if (e.TryGetProperty("begin", out _)) return;

            if (e.TryGetProperty("include", out var include) && include.ValueKind == JsonValueKind.String)
            {
                string target = include.GetString()!;
                if (target.StartsWith('#') && seen.Add(target)
                    && repository.TryGetProperty(target[1..], out var rule))
                {
                    Walk(rule, repository, into, seen);
                }
                return;
            }

            if (e.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String
                && name.GetString()!.StartsWith("keyword.", StringComparison.Ordinal)
                && e.TryGetProperty("match", out var match) && match.ValueKind == JsonValueKind.String)
            {
                var alternation = Regex.Match(match.GetString()!, @"^\\b\(([^)]*)\)\\b$");
                if (alternation.Success)
                {
                    into.AddRange(alternation.Groups[1].Value.Split('|'));
                }
            }

            if (e.TryGetProperty("patterns", out var nested))
            {
                Walk(nested, repository, into, seen);
            }
        }
    }

    /// <summary>The key spellings the <c>fonts-block</c> context claims, from its own
    /// alternations — the reverse of <see cref="EveryFontsBlockKey_IsColoured"/>.</summary>
    private static string[] FontsBlockWordsInTheGrammar()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(GrammarPath));
        var block = doc.RootElement.GetProperty("repository").GetProperty("fonts-block");
        var found = new List<string>();
        foreach (var rule in block.GetProperty("patterns").EnumerateArray())
        {
            if (!rule.TryGetProperty("match", out var match)) continue;
            foreach (Match group in Regex.Matches(match.GetString()!, @"\(([A-Za-z][A-Za-z0-9|-]*)\)"))
            {
                found.AddRange(group.Groups[1].Value.Split('|'));
            }
        }
        Assert.NotEmpty(found);
        return [.. found.Distinct(StringComparer.Ordinal).OrderBy(w => w, StringComparer.Ordinal)];
    }

    /// <summary>
    /// The key/value alternations the part header's context claims, expanded to one pair per
    /// combination — the reverse of <see cref="EveryPartHeaderWord_IsColoured"/>, and the
    /// direction that caught <c>channel</c> in the reserved-word vocabulary.
    /// </summary>
    private static (string Key, string Value)[] PartHeaderPairsInTheGrammar()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(GrammarPath));
        var repository = doc.RootElement.GetProperty("repository");
        var found = new List<(string, string)>();
        foreach (string rule in new[] { "part-body", "tab-tuning" })
        {
            foreach (var r in Rules(repository.GetProperty(rule)))
            {
                if (!r.TryGetProperty("match", out var m)) continue;
                var pair = Regex.Match(m.GetString()!, @"^\\b\(([^)]*)\)\\s\+\(([^)]*)\)\\b$");
                if (!pair.Success) continue;
                foreach (string k in pair.Groups[1].Value.Split('|'))
                    foreach (string v in pair.Groups[2].Value.Split('|'))
                        found.Add((Regex.Unescape(k), Regex.Unescape(v)));
            }
        }
        Assert.NotEmpty(found);
        return [.. found];
    }

    /// <summary>The BARE key spellings the part header's context paints as the language's own —
    /// the words that would be a defect if a writer could not also use them as a part name, and
    /// are legitimate only because a begin/end rule knows where it stands.</summary>
    private static string[] PartHeaderBareKeysInTheGrammar()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(GrammarPath));
        var found = new List<string>();
        foreach (var r in Rules(doc.RootElement.GetProperty("repository").GetProperty("part-body")))
        {
            if (!r.TryGetProperty("name", out var name)
                || !name.GetString()!.StartsWith("keyword.", StringComparison.Ordinal)
                || !r.TryGetProperty("match", out var m)) continue;
            var alternation = Regex.Match(m.GetString()!, @"^\\b\(([^)]*)\)\\b$");
            if (alternation.Success) found.AddRange(alternation.Groups[1].Value.Split('|'));
        }
        Assert.NotEmpty(found);
        return [.. found.Distinct(StringComparer.Ordinal).OrderBy(w => w, StringComparer.Ordinal)];
    }

    private static IEnumerable<JsonElement> Rules(JsonElement block) =>
        block.TryGetProperty("patterns", out var patterns)
            ? patterns.EnumerateArray()
            : [];

    [Fact]
    public void EveryPatternInTheGrammar_IsAReadableRegex()
    {
        // A pattern this runner cannot compile would drop out of every count below and make
        // the file look better covered than it is. Oniguruma has spellings .NET lacks; if one
        // ever arrives, it must arrive as a red test and not as a quietly smaller number.
        var unreadable = Patterns()
            .Where(p => { try { _ = new Regex(p); return false; } catch (ArgumentException) { return true; } })
            .ToArray();

        Assert.True(unreadable.Length == 0,
            "these grammar patterns did not compile, so this file cannot say whether the "
            + "words they cover are coloured: " + string.Join(" | ", unreadable));
    }

    [Fact]
    public void EveryRuleInTheGrammar_IsReachable()
    {
        // ★ The mirror of what Patterns() was taught on 2026-08-18. That walk stopped counting
        // a rule nothing includes, which keeps a DEAD rule from making words look coloured — and
        // it is silent when the rule that goes dark is one no word list here asks about.
        //
        // Which is what happened in the very next commit. `8f7620c6` meant to ADD #fonts-block to
        // the root pattern list and REPLACED #comments with it, at all three sites where the two
        // could be confused (the root, #lyrics-content, and the new block's own patterns — which
        // came out including ITSELF). #comments became the only unreachable entry in the file, so
        // every comment in every .lys went plain, and the suite stayed green for a whole session
        // because a comment is not a word and nothing above counts one.
        //
        // The rule is therefore about RULES, not words: an entry nothing reaches colours nothing,
        // so include it or delete it. No word list can be complete enough to imply this one.
        string[] dead = RepositoryRules().Except(Reachable().Rules, StringComparer.Ordinal).ToArray();

        Assert.True(dead.Length == 0,
            $"the grammar defines these {dead.Length} rules and the root pattern list reaches none "
            + "of them, so everything they would colour is plain in the editor: "
            + string.Join(" ", dead));
    }

    [Fact]
    public void EveryListThatProtectsQuotedText_ProtectsCommentedTextFirst()
    {
        // The reachability check above is necessary and NOT sufficient, which the poison for it
        // showed at once: drop <c>#comments</c> from the root list alone and the rule stays
        // reachable through #lyrics-content, so nothing goes red — while every comment outside a
        // lyrics block goes plain. Reach is not position, and for these two rules position IS
        // the behaviour: they have to be read BEFORE anything that could claim their text, or a
        // keyword inside a comment gets painted as a keyword.
        //
        // So the invariant is about each list: wherever the grammar protects quoted text it
        // protects commented text too, and the two lead in that order. It holds at all three
        // sites today (the root, #lyrics-content, #fonts-block) and it is exactly what
        // `8f7620c6` broke at all three at once, by REPLACING #comments with #fonts-block
        // instead of adding it — which also left #fonts-block including itself.
        string[] broken = [.. PatternLists()
            .Where(l => l.Contains("#strings") || l.Contains("#comments"))
            .Where(l => l.Length < 2 || l[0] != "#comments" || l[1] != "#strings")
            .Select(l => "[" + string.Join(" ", l.Select(x => x ?? "<inline>")) + "]")];

        Assert.True(broken.Length == 0,
            $"these {broken.Length} pattern lists read a comment or a string LATE or not at all, "
            + "so text one of them owns can be claimed by an earlier rule first: "
            + string.Join(" ", broken));
    }

    [Fact]
    public void BothCommentForms_AreColoured()
    {
        // The two checks above are both about the SHAPE of the includes, and both stay green if
        // the `comments` entry is deleted outright — a rule that does not exist is neither dead
        // nor late. So this asks the question directly, of the one vocabulary in this file that
        // is not a vocabulary, and of both forms, which are two separate rules.
        //
        // ⚠️ Equality, not Contains: the pattern covering a line comment has to BE the comment
        // rule and not another that happens to reach across it. A green assembled out of, say,
        // the lyric connectors would say "coloured" while the editor painted the line's tail
        // with the wrong scope entirely.
        string[] cover = [.. Patterns().Where(p => Covers(p, "// a line comment"))];
        Assert.Equal(new[] { "//.*$" }, cover);

        Assert.Contains(Patterns(), p => p == @"/\*");
        Assert.Contains(Patterns(), p => p == @"\*/");
    }

    [Fact]
    public void EveryWritableReservedWord_IsColoured()
    {
        string[] reserved = ReservedSpellings();
        Assert.True(reserved.Length > 70, $"only {reserved.Length} reserved spellings extracted");

        string[] plain = reserved
            .Where(w => !ReservedOnlyToRefuse.Contains(w, StringComparer.Ordinal))
            .Where(w => !IsColoured(w))
            .ToArray();

        Assert.True(plain.Length == 0,
            $"the editor leaves these {plain.Length} reserved words with no colour at all, and "
            + "a writer can type every one of them: " + string.Join(" ", plain));
    }

    [Fact]
    public void EveryWordPaintedAsAKeyword_IsReserved()
    {
        // The mirror of the check above, and it catches the mirror defect: a word coloured as
        // a keyword that a writer may use as a NAME. Measured 2026-08-18 — `part relative { }`,
        // `part unfold { }` and four more all compiled, so six words were advertising
        // themselves as the language's when they were the writer's. Five are the value of a
        // directive and moved to #directive-value, which colours them WITH the word they
        // belong to; the sixth, `channel`, is in no production, no lexer arm and no source file
        // in this repo — a spelling the language never had.
        string[] painted = WordsPaintedAsKeywords();
        Assert.True(painted.Length > 30, $"only {painted.Length} bare keyword words extracted");

        string[] free = painted.Where(w => KindOf(w) == SyntaxKind.Identifier).ToArray();
        Assert.True(free.Length == 0,
            "the grammar paints these as keywords and the lexer hands them back as plain "
            + "names, so a part called any of them is coloured as though it were the "
            + "language's word: " + string.Join(" ", free));
    }

    [Fact]
    public void EveryFontsBlockKey_IsColoured()
    {
        // ⚠️ The check above cannot reach these: a fonts key is NOT a reserved word. `serif`,
        // `barNumber` and `marks` name a part perfectly well, and are the language's words only
        // between `fonts {` and its `}`. So they were plain — while `tempo` and `title`, which
        // happen to be reserved for unrelated reasons, were coloured in the same block. The
        // user saw exactly that after deploying the extension, and this is the net for it.
        string[] keys = [.. TextRoles.AllKeySpellings(), "sans-serif"];
        Assert.True(keys.Length > 25, $"only {keys.Length} key spellings came back");

        string[] plain = keys.Where(k => !IsColoured(k)).ToArray();
        Assert.True(plain.Length == 0,
            "a fonts block binds these and the editor leaves them plain: " + string.Join(" ", plain));
    }

    [Fact]
    public void EveryPaperBlockKey_IsColoured()
    {
        // The fonts-block fact again, in the paper vocabulary: none of these words is
        // reserved (`part indent { … }` compiles), so the reserved-word check above cannot
        // reach them and only the #paper-block context may paint them.
        string[] keys = [.. LanguageVocabulary.PaperScalarKeys, "raggedRight", "size",
            .. LanguageVocabulary.PaperSpacingKeys, .. LanguageVocabulary.PaperSpacingSubKeys];
        Assert.True(keys.Length > 25, $"only {keys.Length} key spellings came back");

        string[] plain = keys.Where(k => !IsColoured(k)).ToArray();
        Assert.True(plain.Length == 0,
            "a paper block takes these and the editor leaves them plain: " + string.Join(" ", plain));

        // The reverse direction, the one `channel` failed for the part header: a word the
        // block colours must be one the reader knows, or the grammar's list drifts into
        // words nobody may write.
        foreach (string key in PaperBlockWordsInTheGrammar())
            Assert.Contains(key, keys, StringComparer.Ordinal);
    }

    /// <summary>The key spellings the <c>paper-block</c> context claims, from its own
    /// alternations — <c>match</c> AND <c>begin</c>, because the spacing keys open a
    /// nested block and live in a <c>begin</c>. The outer <c>paper</c> begin is not
    /// walked (the word is reserved and checked by the keyword direction).</summary>
    private static string[] PaperBlockWordsInTheGrammar()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(GrammarPath));
        var block = doc.RootElement.GetProperty("repository").GetProperty("paper-block");
        var found = new List<string>();
        Walk(block.GetProperty("patterns"), found);
        Assert.NotEmpty(found);
        return [.. found.Distinct(StringComparer.Ordinal).OrderBy(w => w, StringComparer.Ordinal)];

        static void Walk(JsonElement e, List<string> into)
        {
            if (e.ValueKind == JsonValueKind.Array)
            {
                foreach (var x in e.EnumerateArray()) Walk(x, into);
                return;
            }
            if (e.ValueKind != JsonValueKind.Object) return;
            foreach (string prop in new[] { "match", "begin" })
                if (e.TryGetProperty(prop, out var rx) && rx.ValueKind == JsonValueKind.String)
                    foreach (Match group in Regex.Matches(rx.GetString()!, @"\(([A-Za-z][A-Za-z0-9|-]*)\)"))
                        into.AddRange(group.Groups[1].Value.Split('|'));
            if (e.TryGetProperty("patterns", out var nested))
                Walk(nested, into);
        }
    }

    [Fact]
    public void EveryPartHeaderWord_IsColoured()
    {
        // ★ The same fact as the fonts block, in five more vocabularies, and reported by the user
        // in exactly the same shape: `pedal text` coloured only `text` (a fonts key), `clef
        // treble` was coloured and `clef treble^8` was not, and six of the seven tuning names were
        // plain — `bass` being coloured because it is a CLEF. Every word that already had a colour
        // had it for a reason that has nothing to do with the header it stands in.
        //
        // ⚠️ A value is asked about WITH its key, because that is how it is coloured: the value
        // words are not reserved (see TheCheckCanFail) so a bare alternation would paint a
        // writer's own part name, and the two-word rule is what #directive-value already does.
        var plain = new List<string>();

        foreach (string name in SymbolCaseValidator.PropertyNameVocabulary)
            if (!IsColoured(name)) plain.Add(name);
        foreach (string v in SymbolCaseValidator.ClefValueVocabulary)
            if (!IsColoured($"clef {v}")) plain.Add($"clef {v}");
        foreach (string v in SymbolCaseValidator.PedalValueVocabulary)
            if (!IsColoured($"pedal {v}")) plain.Add($"pedal {v}");
        foreach (string v in SymbolCaseValidator.RemoveEmptyValueVocabulary)
            if (!IsColoured($"removeEmpty {v}")) plain.Add($"removeEmpty {v}");
        // ★ A sixth vocabulary the ticket that opened this work had not counted: the property
        // name `transposition` was listed as plain, and its four markers were plain beside it.
        // Colouring the key and not the value is the reported defect with the halves swapped.
        foreach (string v in InstrumentDefaults.TranspositionMarkers)
            if (!IsColoured($"transposition {v}")) plain.Add($"transposition {v}");
        foreach (string v in LanguageVocabulary.PitchModes)
            if (!IsColoured($"pitch {v}")) plain.Add($"pitch {v}");
        foreach (string v in SymbolCaseValidator.TuningValueVocabulary)
        {
            // ⚠️ BOTH positions. A tuning name is also the value of `tab NAME` in a score
            // (measured 2026-08-19: all seven accepted there), and colouring it in the header
            // while leaving it plain in the score is the reported defect moved, not fixed.
            if (!IsColoured($"tuning {v}")) plain.Add($"tuning {v}");
            if (!IsColoured($"tab {v}")) plain.Add($"tab {v}");
        }

        Assert.True(plain.Count == 0,
            $"a part header binds these {plain.Count} and the editor leaves them plain: "
            + string.Join(" | ", plain));
    }

    [Fact]
    public void EveryWordThePartHeaderColours_IsInTheLanguagesVocabulary()
    {
        // The reverse. EveryWordPaintedAsAKeyword_IsReserved deliberately stops at a begin/end
        // rule — a context is the licence to paint a word that is the language's only THERE — so
        // nothing inside #part-body is covered by it, and without this the list could drift into
        // spellings nobody may write. That is not hypothetical: `channel` was such a word, in no
        // production, no lexer arm and no source file, and it was this direction that found it.
        var vocabulary = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.Ordinal)
        {
            ["clef"] = SymbolCaseValidator.ClefValueVocabulary,
            ["tuning"] = SymbolCaseValidator.TuningValueVocabulary,
            ["tab"] = SymbolCaseValidator.TuningValueVocabulary,
            ["pedal"] = SymbolCaseValidator.PedalValueVocabulary,
            ["removeEmpty"] = SymbolCaseValidator.RemoveEmptyValueVocabulary,
            ["transposition"] = InstrumentDefaults.TranspositionMarkers,
            ["pitch"] = LanguageVocabulary.PitchModes,
        };

        string[] stray = [.. PartHeaderPairsInTheGrammar()
            .Where(p => !vocabulary.TryGetValue(p.Key, out var known)
                        || !known.Contains(p.Value, StringComparer.Ordinal))
            .Select(p => $"{p.Key} {p.Value}")];
        Assert.True(stray.Length == 0,
            "the part header context colours these and the language does not know them: "
            + string.Join(" | ", stray));

        string[] strayKeys = [.. PartHeaderBareKeysInTheGrammar()
            .Where(k => !SymbolCaseValidator.PropertyNameVocabulary.Contains(k, StringComparer.Ordinal))];
        Assert.True(strayKeys.Length == 0,
            "the part header context paints these as its keys and no part property is spelled "
            + "that way: " + string.Join(" ", strayKeys));
    }

    /// <summary>The regex of one <c>match</c> rule, read out of the shipped grammar rather
    /// than copied — a test that carries its own copy of the pattern tests the copy.</summary>
    private static string MatchPatternOf(string rule, int index = 0)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(GrammarPath));
        return doc.RootElement.GetProperty("repository").GetProperty(rule)
            .GetProperty("patterns")[index].GetProperty("match").GetString()!;
    }

    [Theory]
    // The duration a writer types after a bare pitch letter — the ordinary case, and the one
    // that was never coloured: `\b` between `d` and `2` is false, so only a pitch carrying an
    // octave mark ever handed the rule a word boundary to stand on.
    [InlineData("d2", "2")]
    [InlineData("e4", "4")]
    [InlineData("g16", "16")]
    [InlineData("b64", "64")]
    // …and with one, which always worked. Both spellings, one rule, same answer.
    [InlineData("c'4", "4")]
    // Dots belong to the duration. The old trailing `\b` sat AFTER them, and `.` followed by
    // a space is not a boundary, so a dotted duration was coloured up to its dot and no
    // further — `c'2.` lit the 2 and left the dot plain.
    [InlineData("c'2.", "2.")]
    [InlineData("d4..", "4..")]
    public void TheDurationPattern_ColoursTheWholeDuration(string source, string expected)
    {
        var match = Regex.Match(source, MatchPatternOf("durations"));

        Assert.True(match.Success, $"no duration found in `{source}`");
        Assert.Equal(expected, match.Value);
    }

    [Theory]
    // ⚠️ The other half, and the reason the guards are lookarounds rather than nothing at all.
    // Each of these contains a digit run that IS a duration spelling, and none of them is a
    // duration: the clef's octave transposition, the transposition markers, a five-string
    // tuning, and a tempo whose first digit is the whole note.
    [InlineData("clef treble_8")]
    [InlineData("transposition 8va")]
    [InlineData("transposition 15ma")]
    [InlineData("tuning bass5")]
    [InlineData("tempo 100")]
    public void TheDurationPattern_LeavesTheseAlone(string source)
    {
        var match = Regex.Match(source, MatchPatternOf("durations"));

        Assert.False(match.Success,
            $"`{source}` is not a duration, but the rule painted `{match.Value}` as one.");
    }

    [Fact]
    public void ASectionInsideAPart_IsColouredLikeOneAtTheTopLevel()
    {
        // ⚠️ Reported 2026-08-23: in `part melody { section A { … } }` the name `A` was plain.
        // #part-body is a begin/end block carrying the part HEADER's vocabulary, and a section
        // is not in it, so `section` fell through to the generic keyword rule and its name to
        // nothing. ⚠️ The half that did not show: #part-body ends at `\}` and nothing inside
        // it balanced a brace, so the part body ENDED at that first section's closing brace —
        // which is why the SECOND section in the same part looked right, having already fallen
        // back to the top level. A fix that only added the name would have left that.
        //
        // This is structural, not a tokenizer run: the suite has no TextMate engine. What it
        // can say is that the part body reaches the rule, that the rule says the same two
        // things the top-level one says, and that it balances braces.
        using var doc = JsonDocument.Parse(File.ReadAllText(GrammarPath));
        var repository = doc.RootElement.GetProperty("repository");

        string?[] partBody = [.. repository.GetProperty("part-body").GetProperty("patterns")
            .EnumerateArray()
            .Where(p => p.TryGetProperty("include", out _))
            .Select(p => p.GetProperty("include").GetString())];
        Assert.Contains("#part-inner-section", partBody);

        var atTopLevel = repository.GetProperty("section-declaration")
            .GetProperty("patterns")[0].GetProperty("captures");
        var insideAPart = repository.GetProperty("part-inner-section").GetProperty("beginCaptures");
        foreach (var group in new[] { "1", "2" })
            Assert.Equal(
                atTopLevel.GetProperty(group).GetProperty("name").GetString(),
                insideAPart.GetProperty(group).GetProperty("name").GetString());

        string?[] insideASection = [.. repository.GetProperty("part-inner-section")
            .GetProperty("patterns").EnumerateArray()
            .Select(p => p.GetProperty("include").GetString())];
        Assert.True(insideASection.FirstOrDefault() == "#nested-braces",
            "a section inside a part must claim its nested `{ … }` before $self does, or "
            + "`repeat 2 { … }` ends the section the way the section was ending the part.");
    }

    [Fact]
    public void TheGrammarCallsNothingInvalid()
    {
        // ★ The decision (user, 2026-08-18): a per-line regex may say what a word IS and may
        // not say that it is WRONG. It cannot hold the position, so it cannot tell the dead
        // spelling from the live one — and the word the removed rule painted has both.
        // Wrongness travels as a diagnostic, which is anchored to a span.
        //
        // ⚠️ This is also what keeps the two colourers from contradicting each other. The
        // grammar now only ever says "keyword"; the semantic tokens only ever say "keyword";
        // a disagreement of the shape "one paints an error where the other paints a word" is
        // not expressible while this holds.
        string[] invalid = ScopeNames()
            .Where(n => n.StartsWith("invalid.", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.True(invalid.Length == 0,
            "the grammar claims these scopes, and a line of regex cannot know whether the word "
            + "it matched is wrong WHERE IT STANDS — report it as a diagnostic instead: "
            + string.Join(", ", invalid));
    }

    [Fact]
    public void TheCheckCanFail()
    {
        // ★ Every claim this file rests on, measured rather than recalled (RULES §5.4): the
        // net was written after the grammar was corrected, so it is green for free, and green
        // for free says nothing.

        // ⑴ The reason `invalid` is banned: ONE word, two positions, opposite answers.
        Assert.False(SyntaxTree.Parse(
                "fonts { volta \"TeX Gyre Schola\" }\npart m { clef treble }\n"
                + "section A { m { c'4 } }\nform main { A }\nscore main { staff m }").HasErrors,
            "a fonts block binds the volta role — this is the live spelling of the word");
        Assert.Contains(
            SyntaxTree.Parse("part m { clef treble }\nsection A { m { repeat volta 2 { c'4 } } }")
                .Diagnostics,
            d => d.Code == DiagnosticCodes.RepeatVoltaRemoved);

        // ⑵ The exception list is measured, not assumed: the level marks really are refused
        //    where a dynamic would stand, so painting them would advertise a spelling nobody
        //    may type — while the '@' form they ARE written in is coloured.
        Assert.True(SyntaxTree.Parse(
                "part m { clef treble }\nsection A { m { c'4 p d'4 } }").HasErrors,
            "a bare level mark is not a dynamic — it is written @p");
        Assert.True(IsColoured("@p"), "the spelling that IS writable has to keep its colour");

        // ⑶ Whole-word coverage is not substring coverage — the pair that fooled the first
        //    measurement, kept here because the difference is invisible until it bites.
        const string LyricConnectors = "--|__|~|_";
        Assert.Contains(LyricConnectors, Patterns());
        Assert.Matches(LyricConnectors, "bass_8");                 // it matches the underscore
        Assert.False(Covers(LyricConnectors, "bass_8"));           // …and colours none of the word

        // ⑷ The six words the reverse check was written for really are the writer's to use —
        //    the claim that made them defects rather than a matter of taste.
        foreach (string free in new[] { "absolute", "channel", "percent", "relative", "tremolo", "unfold" })
        {
            Assert.Equal(SyntaxKind.Identifier, KindOf(free));
            Assert.DoesNotContain(free, WordsPaintedAsKeywords(), StringComparer.Ordinal);
        }
        //    …and the five that ARE a directive's value keep their colour, from the two-word
        //    rule that also names the directive — which is the whole reason they could leave
        //    the bare alternation without going dark.
        Assert.True(IsColoured("octave relative"));
        Assert.True(IsColoured("repeat unfold"));

        // ⑸ The fonts block, both directions. A key the parser does NOT know must not be in
        //    the grammar's list either — that direction is what keeps the list from drifting
        //    into words nobody may write, and it is the direction `channel` failed above.
        foreach (string key in FontsBlockWordsInTheGrammar())
        {
            Assert.True(
                TextRoles.TryParseKey(key, out _, out _, out _),
                $"the fonts block colours `{key}` and the parser does not know it as a key");
        }
        //    ⚠️ And the hyphen trap is real, not theoretical: `sans` alone would match the head
        //    of `sans-serif` and leave the tail plain, because \b succeeds before a hyphen.
        Assert.True(Covers(@"\b(sans-serif|serif|sans)\b", "sans-serif"));
        Assert.False(Covers(@"\b(serif|sans|sans-serif)\b", "sans-serif"));
        //    …and the rule only counts because something INCLUDES it. A repository entry with
        //    no include colours nothing, so the walk starts at the root list and follows them.
        Assert.Contains(Patterns(), p => p.Contains("barNumber", StringComparison.Ordinal));
        Assert.DoesNotContain(Patterns(), p => p.Contains("zzz-unincluded", StringComparison.Ordinal));

        // ⑹ Both directions of the main check bite, on words that were actually wrong.
        Assert.Contains("staffGroup", ReservedSpellings(), StringComparer.Ordinal);
        Assert.Contains("volta", ReservedSpellings(), StringComparer.Ordinal);
        Assert.Contains("staffGroup", WordsPaintedAsKeywords(), StringComparer.Ordinal);
        Assert.False(IsColoured("zzznotaword"));
        Assert.NotEmpty(Patterns());

        // ⑺ The part header — the three measurements the shape of it rests on.
        //    First: these words are the WRITER's. A bare alternation outside a context would
        //    paint a part name as the language's own, which is the `channel` defect again.
        //    Fifteen of the twenty-one words this context colours are free identifiers;
        //    only four clef names (soprano mezzosoprano baritone percussion) are reserved,
        //    and those four were already coloured, for that reason and not for this one.
        foreach (string free in new[]
                 {
                     "transposition", "removeEmpty", "pedal",
                     "bracket", "text", "mixed",
                     "standard", "guitar", "bass5", "bass6", "ukulele", "uke",
                     "true", "all", "false",
                 })
        {
            Assert.False(SyntaxTree.Parse($"part {free} {{ clef treble }}\nsection A {{ {free} {{ c'1 }} }}\nform main {{ A }}\nscore main {{ staff {free} }}").HasErrors,
                $"`part {free}` has to compile, or a context is not what makes it legal to "
                + "colour the word");
        }

        //    Second: the vocabulary is the HEADER's, not the language's everywhere. A part
        //    body takes eleven clef names; `clef` in music takes five and refuses the other six
        //    (measured 2026-08-19, both directions). That is why the eleven are coloured inside a
        //    begin/end and not by a rule that fires anywhere — and it is the defect GRAMMAR.md's
        //    `ClefName` carries, one production standing in two positions.
        Assert.False(SyntaxTree.Parse("part m { clef percussion }\nsection A { m { c'1 } }\nform main { A }\nscore main { staff m }").HasErrors);
        Assert.True(SyntaxTree.Parse("part m { clef treble }\nsection A { m { c'2 clef percussion d'2 } }\nform main { A }\nscore main { staff m }").HasErrors,
            "mid-music `clef` takes the five of ClefName — if this stops being true the two "
            + "vocabularies have merged and a context is no longer the honest device");

        //    Third: a tuning name really has a second position, which is the whole reason
        //    #tab-tuning exists. Colour it in the header only and the report simply moves.
        Assert.False(SyntaxTree.Parse("part m { clef treble }\nsection A { m { c'1 } }\nform main { A }\nscore main { tab guitar m }").HasErrors);

    }
}
