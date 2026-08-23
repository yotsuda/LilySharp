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
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using LilySharp.Lsp;
using Xunit;

namespace LilySharp.Tests.Lsp;

/// <summary>
/// The three DECLARED NAMES — a part's, a section's, a phrase's — coloured from the tree,
/// and the one name that is deliberately left plain.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ Two of these three colours were pinned in <c>package.json</c> from the beginning and
/// never once appeared: the server's legend declared nine token types and <c>section</c> and
/// <c>phrase</c> were not among them, so those rules named types nothing emitted. A pinned
/// colour and an unwired legend are indistinguishable from the file that pins them, which is
/// why <see cref="EveryPinnedColour_NamesATokenTypeTheServerEmits"/> exists: it is the only
/// check here that could have caught the state this file was written to fix.
/// </para>
/// <para>
/// ★ The section names inside a <c>form { }</c> are the reason this belongs to the semantic
/// tokens rather than the grammar. A regex sees that <c>form main { A }</c> writes an A; it
/// cannot see whether a <c>section A { … }</c> exists to give it meaning. The user's call
/// (2026-08-23) was that an unresolvable name gets NO colour — so the editor never asserts a
/// name means something at the same moment LYS1005 squiggles it as undefined.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class DeclaredNameTokenTests
{
    private const int Part = 9;
    private const int Section = 10;
    private const int Phrase = 11;

    /// <summary>The spans carrying one token type, as the text they cover.</summary>
    private static string[] Coloured(string source, int tokenType) =>
        [.. LilySharpLanguageServer
            .CollectSemanticTokens(SyntaxTree.Parse(source).GetRoot(), source)
            .Where(t => t.TokenType == tokenType)
            .Select(t => Span(source, t))];

    private static string Span(string source, LilySharpLanguageServer.SemanticToken token)
    {
        int line = 0, offset = 0;
        while (line < token.Line)
        {
            offset = source.IndexOf('\n', offset) + 1;
            line++;
        }
        return source.Substring(offset + token.Character, token.Length);
    }

    private const string PartMajor = """
        part melody {
          clef treble
          section A { c'4 d e f | g2 g | }
          section B { c'4 d e f | g2 g | }
        }

        phrase turn { c'8 d c b }

        form main { A B }

        score main { staff melody }
        """;

    [Fact]
    public void APartName_IsColouredWhereverItIsWritten()
    {
        // The reported gap: `part` was a keyword and `melody` beside it was plain text in
        // every theme, because nothing — grammar or server — named it at all. The score's
        // `staff melody` is the same name, and colouring one and not the other is the
        // half-coloured shape this repo keeps closing.
        Assert.Equal(["melody", "melody"], Coloured(PartMajor, Part));
    }

    [Theory]
    // Every spelling a score has for naming a part. ⚠️ Enumerated rather than sampled: all
    // six reach the colour through PartReferenceFinder's one switch, and a seventh render
    // form that reached the diagnostics and not this would leave a legal name plain.
    //
    // ⚠️ Each of these was put through `lysc check` before it was written down. Three drafts
    // did not survive it: `midi main { … }` is not a spelling at all (the MIDI-only render is
    // a BARE part name inside a score), and a condensed or combined staff naming ONE part is
    // refused — "one part on one staff is what 'staff melody' already does". A fixture
    // written from the node's doc comment would have pinned the wrong language, which is
    // RULES §5.0's "measure the premise before you write it down".
    [InlineData("score main { staff melody }")]
    [InlineData("score main { staff melody \"Flute\" }")]
    [InlineData("score main { ossia melody }")]
    [InlineData("score main { tab melody }")]
    [InlineData("score main { grandStaff { staff melody staff other } }")]
    [InlineData("score main { condensedStaff { melody other } }")]
    [InlineData("score main { combinedStaff { melody other } }")]
    // A bare part name renders that part to MIDI only — MidiPartRenderSyntax.
    [InlineData("score main { staff other  melody }")]
    public void EverySpellingOfAScoresPartReference_IsColoured(string render)
    {
        var source = $"part melody {{ section A {{ c'4 }} }}\n"
                   + $"part other {{ section A {{ e'4 }} }}\nform main {{ A }}\n{render}\n";

        Assert.Contains("melody", Coloured(source, Part));
        Assert.Equal(2, Coloured(source, Part).Count(n => n == "melody"));
    }

    /// <summary>A book naming every strand a score can place, with one of each spelled
    /// wrong. Everything blue is a name that resolves; everything plain is one the
    /// diagnostics are about to underline.</summary>
    private const string EveryTrack = """
        part melody { section A { c'4 d e f } }
        lyrics verse sings melody { section A { la la la la } }
        chords prog { section A { C | } }
        form main { A }
        score main {
          staff melody
          lyrics verse
          chords prog
          lyrics noSuchVerse
          chords noSuchProg
        }
        """;

    [Fact]
    public void ATrackName_TakesThePartColour()
    {
        // A track is a part-shaped thing — a named strand a score places as a row — so
        // `lyrics verse` and `chords prog` are blue like `part melody` (the user's call,
        // 2026-08-23). ⚠️ Their namespaces stay separate: a chord track is not a part, and
        // folding the sets would make `staff prog` legal, which is the empty staff LYS1007
        // exists to catch.
        Assert.Equal(
            ["melody", "verse", "melody", "prog", "melody", "verse", "prog"],
            Coloured(EveryTrack, Part));
    }

    [Fact]
    public void ARowNamingATrackThatDoesNotExist_LeavesItPlain()
    {
        Assert.DoesNotContain("noSuchVerse", Coloured(EveryTrack, Part));
        Assert.DoesNotContain("noSuchProg", Coloured(EveryTrack, Part));
    }

    [Theory]
    // ⚠️ A `sings` target resolves against a WIDER set than a score's part references: a
    // part OR a named VOICE, which is what LYS6011 checks. Resolving it against the parts
    // alone would leave plain a name the diagnostics accept — the quiet half of a
    // disagreement, and the one no "is it ever wrongly coloured?" test can see.
    //
    // ⚠️ The voice book was written twice from memory and refused twice by `lysc check`
    // before it was written down: `p` is RESERVED (it is the dynamic mark, so no part may
    // be called that), and `voice` opens the span ONCE — the other voices are further
    // blocks, `voice hi { … } lo { … }`, not a second keyword.
    [InlineData("part melody { section A { c'4 c c c } }", "melody", true)]
    [InlineData("part melody { section A { voice hi { c'4 c c c } lo { e4 e e e } } }", "hi", true)]
    [InlineData("part melody { section A { c'4 c c c } }", "nobody", false)]
    public void ASingsTarget_IsColouredExactlyWhenItResolves(string part, string target, bool coloured)
    {
        var book = $"{part}\nlyrics v sings {target} {{ section A {{ la la la la }} }}\n"
                 + "form main { A }\nscore main { staff melody  lyrics v }\n";

        Assert.Equal(coloured, Coloured(book, Part).Contains(target));
    }

    [Fact]
    public void AScoreNamingAPartThatDoesNotExist_LeavesItPlain()
    {
        // The same rule as a form's section, for the same reason — LYS1007 is about to
        // underline it.
        const string source = """
            part melody { section A { c'4 } }
            form main { A }
            score main { staff melody  staff nope }
            """;

        Assert.Equal(["melody", "melody"], Coloured(source, Part));
    }

    [Fact]
    public void ASectionBodyPartBlock_DeclaresAPartAndIsColoured()
    {
        // Section-major: `melody { … }` inside a section carries the part's music, so a
        // staff may render `melody` with no `part melody { … }` header anywhere. It is a
        // declaration, so it resolves the score's reference — and is coloured itself.
        const string source = """
            section A { melody { c'4 } }
            form main { A }
            score main { staff melody }
            """;

        Assert.Equal(["melody", "melody"], Coloured(source, Part));
    }

    [Fact]
    public void APhraseName_IsColoured()
    {
        Assert.Equal(["turn"], Coloured(PartMajor, Phrase));
    }

    [Fact]
    public void ASectionName_IsColouredWhereverItIsWritten()
    {
        // Both declarations AND both resolved references in the form, in document order.
        // ⚠️ The declarations here sit inside a `part { }` — part-major — which is where the
        // TextMate grammar had been failing to colour them at all.
        Assert.Equal(["A", "B", "A", "B"], Coloured(PartMajor, Section));
    }

    [Fact]
    public void AFormNamingASectionThatDoesNotExist_LeavesItPlain()
    {
        // ⚠️ The whole point of colouring this from the tree. `Nope` parses perfectly and a
        // regex would paint it; the server knows there is no such section, and LYS1005 is
        // about to underline it.
        const string source = """
            part melody { section A { c'4 } }
            form main { A Nope }
            score main { staff melody }
            """;

        Assert.Equal(["A", "A"], Coloured(source, Section));
    }

    [Fact]
    public void TheSilentSpelling_AnswersTheSameWayAsThePlainOne()
    {
        // `~A` is the same reference with its rehearsal label hidden. It is the spelling that
        // has drifted before — `form main { ~Nope }` passed `lysc check` clean until the
        // validator learned it — so both halves are pinned here, and both go through the one
        // predicate in SectionSymbols.
        const string resolves = """
            part melody { section A { c'4 } }
            form main { ~A }
            score main { staff melody }
            """;
        const string doesNot = """
            part melody { section A { c'4 } }
            form main { ~Nope }
            score main { staff melody }
            """;

        Assert.Equal(["A", "A"], Coloured(resolves, Section));
        Assert.Equal(["A"], Coloured(doesNot, Section));
    }

    [Fact]
    public void WhatIsColouredAndWhatIsDiagnosed_NeverDisagree()
    {
        // ★ The invariant the two callers of SectionSymbols exist to hold: a name the
        // diagnostics call undefined must never come out painted, and a name they accept
        // must never come out plain. Checked over one book holding every spelling at once,
        // so a future spelling that reaches only one of the two shows up here.
        const string source = """
            part melody { section A { c'4 } }
            section B { melody { c'4 } }
            form main { A B ~A Nope ~AlsoNope }
            score main { staff melody  staff noSuchPart }
            """;

        var tree = SyntaxTree.Parse(source);
        // ⚠️ The diagnostic underlines the whole reference NODE, which carries its trailing
        // trivia (`Nope `) and, for the silent spelling, its `~`. Trimmed to the bare name
        // here: this test is about which names disagree, not about span shape — that is
        // DiagnosticSpanTrimTests' subject.
        var undefinedParts = SemanticValidation.Run(tree)
            .Where(d => d.Code == DiagnosticCodes.UndefinedPart)
            .Select(d => source.Substring(d.Span.Start, d.Span.Length).Trim())
            .ToHashSet(StringComparer.Ordinal);
        Assert.Empty(Coloured(source, Part).ToHashSet(StringComparer.Ordinal).Intersect(undefinedParts));

        var undefined = SemanticValidation.Run(tree)
            .Where(d => d.Code == DiagnosticCodes.UndefinedSection)
            .Select(d => source.Substring(d.Span.Start, d.Span.Length).Trim().TrimStart('~'))
            .ToHashSet(StringComparer.Ordinal);
        var painted = Coloured(source, Section).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(["AlsoNope", "Nope"], undefined.OrderBy(n => n, StringComparer.Ordinal).ToArray());
        Assert.Empty(painted.Intersect(undefined));
        Assert.Equal(["A", "B"], painted.OrderBy(n => n, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void EveryPinnedColour_NamesATokenTypeTheServerEmits()
    {
        // ⚠️ THE check this file was written for. `package.json` pins colours by token-type
        // NAME and the legend declares those names; nothing links the two, and when they
        // disagreed the symptom was not an error anywhere — it was a colour that silently
        // came from the theme instead. Both directions, because both go wrong: a pinned rule
        // with no type is dead, and a custom type with no rule takes whatever the theme has.
        var pinned = PinnedTokenColours();
        var legend = LegendTokenTypes();

        var deadRules = pinned.Keys.Where(name => !legend.Contains(name)).ToArray();
        Assert.True(deadRules.Length == 0,
            "package.json pins a colour for a token type the server never declares, so the "
            + "rule can never fire: " + string.Join(", ", deadRules));

        // Only the CUSTOM types — the six standard LSP ones are meant to follow the theme.
        string[] standard = ["keyword", "variable", "number", "string", "comment", "operator"];
        var unpinned = legend.Where(t => !standard.Contains(t) && !pinned.ContainsKey(t)).ToArray();
        Assert.True(unpinned.Length == 0,
            "the server emits a custom token type package.json pins no colour for, so its "
            + "colour is whatever the loaded theme happens to give it: "
            + string.Join(", ", unpinned));
    }

    [Fact]
    public void NoTwoTokenTypes_ArePinnedToOneColour()
    {
        // ⚠️ The pinned `phrase` was #DCDCAA, which the articulation token already owned — a
        // phrase name and `@staccato` would have been the same colour had the rule ever
        // fired. Distinctness is the reason to pin at all; without it the theme would do.
        var pinned = PinnedTokenColours();
        var collisions = pinned
            .GroupBy(p => p.Value, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} = {string.Join(" + ", g.Select(p => p.Key))}")
            .ToArray();

        Assert.True(collisions.Length == 0,
            "two token types are pinned to one colour: " + string.Join("; ", collisions));
    }

    private static Dictionary<string, string> PinnedTokenColours()
    {
        var manifest = RepoFile("editors/vscode/package.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(manifest));
        var rules = doc.RootElement
            .GetProperty("contributes")
            .GetProperty("configurationDefaults")
            .GetProperty("editor.semanticTokenColorCustomizations")
            .GetProperty("[*]")
            .GetProperty("rules");

        return rules.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!,
                                                    StringComparer.Ordinal);
    }

    /// <summary>The legend as the server actually sends it, read out of the source rather
    /// than restated — a copy of the list here would agree with itself forever.</summary>
    private static string[] LegendTokenTypes()
    {
        var source = File.ReadAllText(RepoFile("LilySharp.Lsp/LilySharpLanguageServer.cs"));
        int start = source.IndexOf("TokenTypes = new[]", StringComparison.Ordinal);
        Assert.True(start >= 0, "the legend is no longer written as `TokenTypes = new[]`.");
        int open = source.IndexOf('{', start);
        int close = source.IndexOf("},", open, StringComparison.Ordinal);

        var names = new List<string>();
        foreach (var line in source[(open + 1)..close].Split('\n'))
        {
            var entry = line.Split("//")[0].Trim().TrimEnd(',').Trim();
            if (entry.Length == 0) continue;
            // Either a literal ("pitch") or SemanticTokenTypes.Keyword, whose wire name is
            // the member with a lower-case initial.
            if (entry.StartsWith('"'))
                names.Add(entry.Trim('"'));
            else
            {
                var member = entry[(entry.LastIndexOf('.') + 1)..];
                names.Add(char.ToLowerInvariant(member[0]) + member[1..]);
            }
        }
        return [.. names];
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
