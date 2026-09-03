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
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using LilySharp.Lsp;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The editor may not suggest a word the compiler refuses, and may not withhold one it
/// accepts. Every test here puts the suggestion THROUGH THE COMPILER rather than comparing it
/// to a list — a comparison is only ever as good as the list it compares against, and on
/// 2026-08-19 the list guarding <c>removeEmpty</c>'s completions was itself a fourth private
/// copy of the same three words, so it would have stayed green through the very drift it
/// existed to catch.
/// </summary>
/// <remarks>
/// ★ Why this file exists at all: <c>LilySharp.Lsp</c> is a separate assembly and the
/// compiler's vocabularies were <c>internal</c>, so the editor's completion was the ONE reader
/// that could not be held to them. It duly kept private copies of the clef names, the
/// <c>removeEmpty</c> values and the property-name list, and two of the three had gone wrong.
/// <c>LanguageVocabulary</c> publishes them; these tests are what makes using it compulsory.
/// </remarks>
[Trait("Category", "Unit")]
public class CompletionVocabularyTests
{
    /// <summary>A minimal complete document with <paramref name="header"/> as the part header.
    /// `vln` is a plain identifier — single letters are pitch names, not part names.</summary>
    private static string PartHeaderDoc(string header) =>
        $"part vln {{ {header} }}\nsection A {{ vln {{ c4 d e f }} }}\n"
        + "form main { A }\nscore main { staff vln }";

    /// <summary>The same, with <paramref name="directive"/> inside the MUSIC instead.</summary>
    private static string MusicDoc(string directive) =>
        $"part vln {{ clef treble }}\nsection A {{ vln {{ {directive} c4 d e f }} }}\n"
        + "form main { A }\nscore main { staff vln }";

    /// <summary>Errors from BOTH passes: a bad clef in music is a PARSE error, a bad clef in a
    /// header is a SEMANTIC one, and a check that looked at only one would be blind to half of
    /// what it is here to see.</summary>
    private static List<Diagnostic> Errors(string source)
    {
        var tree = SyntaxTree.Parse(source);
        return [.. tree.Diagnostics.Concat(SemanticValidation.Run(tree))
            .Where(d => d.Severity == DiagnosticSeverity.Error)];
    }

    private static string[] LabelsAfter(string text) =>
        [.. LilySharpLanguageServer.GetClefCompletions(
            LilySharpLanguageServer.IsInsidePartBlock(text, text.Length)).Items.Select(i => i.Label)];

    // ================= the general rule =================

    /// <summary>
    /// ★★★ THE net: every value the editor offers for a part property is compiled in a part
    /// header and must produce no error. It reaches every property with a value list at once,
    /// so a property added later is covered without anyone remembering this file.
    /// </summary>
    [Fact]
    public void EveryValueTheEditorOffersInAPartHeader_Compiles()
    {
        var offered = new List<(string Property, string Value)>();

        foreach (string value in LilySharpLanguageServer.GetClefCompletions(inPartHeader: true)
                     .Items.Select(i => i.Label))
            offered.Add(("clef", value));

        foreach (string value in LilySharpLanguageServer.GetRemoveEmptyCompletions()
                     .Items.Select(i => i.Label))
            offered.Add(("removeEmpty", value));

        foreach (string value in LilySharpLanguageServer.GetInstrumentCompletions()
                     .Items.Select(i => i.Label))
            offered.Add(("instrument", value));

        foreach (string value in LilySharpLanguageServer.GetPitchModeCompletions()
                     .Items.Select(i => i.Label))
            offered.Add(("pitch", value));

        var rejected = offered
            .Where(o => Errors(PartHeaderDoc($"{o.Property} {o.Value}")).Count > 0)
            .Select(o => $"{o.Property} {o.Value}")
            .ToList();

        Assert.True(rejected.Count == 0,
            "the editor offers these in a part header and the compiler refuses them: "
            + string.Join(", ", rejected));
    }

    /// <summary>
    /// The other direction, which is the one that was actually wrong: a word the compiler
    /// accepts and the editor never mentions. Six legal clefs were invisible in every part
    /// header in the language because the list offered there was the music position's five.
    /// </summary>
    [Fact]
    public void EveryClefTheCompilerAcceptsInAPartHeader_IsOffered()
    {
        var compiles = LanguageVocabulary.PartClefNames
            .Where(name => Errors(PartHeaderDoc($"clef {name}")).Count == 0)
            .ToList();

        // Guard the guard: if the vocabulary stopped compiling in a header, the assertion
        // below would pass vacuously (RULES §5.4 — the empty-set trap).
        Assert.Equal(LanguageVocabulary.PartClefNames.Count, compiles.Count);

        var offered = LilySharpLanguageServer.GetClefCompletions(inPartHeader: true)
            .Items.Select(i => i.Label).ToList();

        Assert.Equal(
            compiles.OrderBy(n => n, System.StringComparer.Ordinal),
            offered.OrderBy(n => n, System.StringComparer.Ordinal));
    }

    // ================= one production, two positions =================

    /// <summary>
    /// The five names a music block takes are the eleven filtered by the parser's own
    /// predicate — asked of the COMPILER here, so the derivation in
    /// <c>SyntaxFacts.ClefNameVocabulary</c> is checked against behaviour rather than
    /// re-stated.
    /// </summary>
    [Fact]
    public void TheMusicPositionTakesFewerClefsThanTheHeader_AndTheEditorKnowsWhich()
    {
        var compilesInMusic = LanguageVocabulary.PartClefNames
            .Where(name => Errors(MusicDoc($"clef {name}")).Count == 0)
            .ToList();

        // The whole point of the pair: they must DIFFER. Merging them is the mistake.
        Assert.NotEqual(LanguageVocabulary.PartClefNames.Count, compilesInMusic.Count);
        Assert.Equal(5, compilesInMusic.Count);

        Assert.Equal(
            compilesInMusic.OrderBy(n => n, System.StringComparer.Ordinal),
            LanguageVocabulary.ClefNames.OrderBy(n => n, System.StringComparer.Ordinal));

        Assert.Equal(
            compilesInMusic.OrderBy(n => n, System.StringComparer.Ordinal),
            LilySharpLanguageServer.GetClefCompletions(inPartHeader: false)
                .Items.Select(i => i.Label).OrderBy(n => n, System.StringComparer.Ordinal));
    }

    /// <summary>
    /// The caret decides which list, and it decides it correctly for an inner section — a
    /// <c>section</c> INSIDE a part holds music, so the five apply there even though the
    /// enclosing block is a part.
    /// </summary>
    [Fact]
    public void TheCaretPosition_PicksTheList()
    {
        Assert.Equal(11, LabelsAfter("part vln { clef ").Length);
        Assert.Equal(5, LabelsAfter("section A { vln { clef ").Length);
        Assert.Equal(5, LabelsAfter("part vln { section A { clef ").Length);
    }

    // ================= the value lists, read from the compiler =================

    /// <summary>
    /// ⚠️ RULES §7.7 ⑷ — the observer for this session's one fallback. The LSP's prose tables
    /// fall back to NO description for a word they have not been told about, and that ships an
    /// item silently. Falling back beats dropping the word (dropping is what hid six clefs),
    /// but nothing watched it, so the day the compiler grows a word was a quietly blank
    /// tooltip. It is a red now.
    /// </summary>
    [Fact]
    public void EveryOfferedWord_HasADescription()
    {
        var undescribed = new List<string>();

        foreach (bool inHeader in new[] { true, false })
            undescribed.AddRange(LilySharpLanguageServer.GetClefCompletions(inHeader).Items
                .Where(i => i.Detail is null).Select(i => $"clef {i.Label}"));

        undescribed.AddRange(LilySharpLanguageServer.GetRemoveEmptyCompletions().Items
            .Where(i => i.Detail is null).Select(i => $"removeEmpty {i.Label}"));

        undescribed.AddRange(LilySharpLanguageServer.GetPitchModeCompletions().Items
            .Where(i => i.Detail is null).Select(i => $"pitch {i.Label}"));

        undescribed.AddRange(LilySharpLanguageServer.GetPartPropertyCompletions().Items
            .Where(i => i.Detail is null).Select(i => $"property {i.Label}"));

        Assert.True(undescribed.Count == 0,
            "the compiler grew these and the editor offers them with no description: "
            + string.Join(", ", undescribed.Distinct()));
    }

    /// <summary>Rewritten 2026-08-19: this asserted <c>{ "true", "all", "false" }</c>, a
    /// literal fourth copy of the list it was guarding.</summary>
    [Fact]
    public void RemoveEmptyCompletions_AreExactlyTheAcceptedValues()
    {
        Assert.Equal(
            LanguageVocabulary.RemoveEmptyValues.OrderBy(n => n, System.StringComparer.Ordinal),
            LilySharpLanguageServer.GetRemoveEmptyCompletions().Items
                .Select(i => i.Label).OrderBy(n => n, System.StringComparer.Ordinal));
    }

    /// <summary>The two words after <c>pitch</c> come from the compiler, in the compiler's
    /// order (the default first). Written 2026-09-03 with the value context; until then the
    /// top-level item carried its own copy of the pair as a snippet choice.</summary>
    [Fact]
    public void PitchModeCompletions_AreExactlyTheAcceptedValues_InTheCompilersOrder()
    {
        Assert.Equal(
            LanguageVocabulary.PitchModes,
            LilySharpLanguageServer.GetPitchModeCompletions().Items.Select(i => i.Label));
    }

    /// <summary>The kinds offered after <c>repeat</c> are the compiler's, in its order, each
    /// carries a description, and each snippet — the kind, its default count and the braced
    /// body, resolved as the editor leaves it — compiles as a whole in a section's music.
    /// The body is filled per kind because a tremolo's count multiplies a SHORT body
    /// (<c>repeat tremolo 4 { g16 }</c> is one beat) while the other two repeat a bar.</summary>
    [Fact]
    public void RepeatKindCompletions_AreTheCompilersKinds_AndEachSnippetCompiles()
    {
        var items = LilySharpLanguageServer.GetRepeatKindCompletions().Items;
        Assert.Equal(LanguageVocabulary.RepeatKinds, items.Select(i => i.Label));
        Assert.All(items, i => Assert.NotNull(i.Detail));

        var body = new Dictionary<string, (string Body, string Tail)>
        {
            ["unfold"] = ("g4 a", ""),            // 2 × a half bar
            ["percent"] = ("g4 a b c'", ""),       // 2 × a bar
            ["tremolo"] = ("g16", "a4 b c'"),      // 4 strokes = one beat, then the rest of the bar
        };
        var rejected = new List<string>();
        foreach (var item in items)
        {
            var (b, tail) = body[item.Label!];
            string resolved = System.Text.RegularExpressions.Regex.Replace(item.InsertText!, @"\$\{\d+:([^}]*)\}", "$1")
                .Replace("$0", b);
            string doc = $"part vln {{ clef treble }}\nsection A {{ vln {{ repeat {resolved} {tail} }} }}\n"
                + "form main { A }\nscore main { staff vln }";
            var errors = Errors(doc);
            if (errors.Count > 0)
                rejected.Add($"{item.Label}: {errors[0].Message}");
        }
        Assert.True(rejected.Count == 0,
            "the editor offers these after `repeat` and the compiler refuses them: " + string.Join("; ", rejected));
    }

    /// <summary>
    /// Every property the language has is offered — <c>transposition</c>, <c>lines</c> and
    /// <c>pedal</c> were absent until 2026-08-19, so the editor denied that three of the nine
    /// existed. <c>key</c> is the one exclusion and it is deliberate: the parser reads it as a
    /// key SIGNATURE, so inserting it as a property pair would produce something legal that is
    /// not what the suggestion looked like.
    /// </summary>
    [Fact]
    public void EveryPartPropertyTheLanguageHas_IsOffered()
    {
        var offered = LilySharpLanguageServer.GetPartPropertyCompletions()
            .Items.Select(i => i.Label).ToHashSet();

        var missing = LanguageVocabulary.PartPropertiesTakingAValuePair
            .Where(name => !offered.Contains(name)).ToList();

        Assert.True(missing.Count == 0,
            "the language has these part properties and the editor does not offer them: "
            + string.Join(", ", missing));

        Assert.DoesNotContain("key", offered);
        Assert.Contains("section", offered); // not a property; the other thing a part body holds
    }

    /// <summary>
    /// ★★ The rule a part-property description obeys: ANYTHING IN PARENTHESES OR BACKTICKS IS
    /// SOMETHING THE WRITER MAY TYPE. Every such word is compiled here. Running prose is not
    /// examined and is not meant to be — which is how <c>octave</c> can say IN WORDS that
    /// <c>absolute</c> and <c>relative</c> belong to the top-level directive without offering
    /// them.
    /// </summary>
    /// <remarks>
    /// This is the check the old description would have failed: it read "(absolute | relative
    /// | N)" — inside the parentheses — for a day AFTER a part header began refusing both
    /// words. The rule is deliberately about PUNCTUATION rather than about meaning, because a
    /// check that has to understand prose is a check nobody can obey.
    /// </remarks>
    [Fact]
    public void NoPartPropertyDescription_OffersAValueTheCompilerRefuses()
    {
        var bad = new List<string>();
        int examined = 0;

        foreach (var item in LilySharpLanguageServer.GetPartPropertyCompletions().Items)
        {
            // `section` is not a property — it is the OTHER thing a part body holds, and
            // `part vln { section … }` is not the shape this test compiles.
            if (item.Detail is null || item.Label == "section") continue;

            var words = new List<string>();
            foreach (System.Text.RegularExpressions.Match m in
                     System.Text.RegularExpressions.Regex.Matches(item.Detail, @"\(([^)]*)\)"))
                words.AddRange(m.Groups[1].Value
                    .Split(['/', '|'], System.StringSplitOptions.TrimEntries)
                    .Where(w => w.Length > 0));
            foreach (System.Text.RegularExpressions.Match m in
                     System.Text.RegularExpressions.Regex.Matches(item.Detail, "`([^`]+)`"))
                words.Add(m.Groups[1].Value);

            foreach (string w in words)
            {
                // A backticked example may be a whole `name value` pair ("`octave 3`").
                string header = w.StartsWith(item.Label + " ", System.StringComparison.Ordinal)
                    ? w
                    : $"{item.Label} {w}";
                examined++;
                if (Errors(PartHeaderDoc(header)).Count > 0)
                    bad.Add($"{item.Label} → '{w}'");
            }
        }

        Assert.True(bad.Count == 0,
            "these descriptions offer a word the compiler refuses: " + string.Join("; ", bad));

        // Guard the guard (RULES §5.4): a stricter word filter, or descriptions that stopped
        // using parentheses, would let this pass by examining nothing at all.
        Assert.True(examined >= 15, $"only {examined} words were examined — the check went blind");
    }
}