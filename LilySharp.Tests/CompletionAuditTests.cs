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
using System.Linq;
using System.Text.RegularExpressions;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using LilySharp.Lsp;
using Xunit;
// Not a using for LilySharp.Lsp.Protocol: its Diagnostic / DiagnosticSeverity collide with
// Core's, which this file compiles against. The protocol types needed are spelled out.
using CompletionItem = LilySharp.Lsp.Protocol.CompletionItem;
using CompletionItemKind = LilySharp.Lsp.Protocol.CompletionItemKind;
using InsertTextFormat = LilySharp.Lsp.Protocol.InsertTextFormat;

namespace LilySharp.Tests;

/// <summary>
/// The rows the 2026-09-10 completion audit added (session 363 audited the popup against
/// GRAMMAR.md, the lexer and the reader tables; session 364 filled the gaps). Every row is
/// PUT THROUGH THE COMPILER in the position that offers it — the net is the parser and the
/// validators, not this file's idea of the grammar — and the contexts that route to the new
/// lists are pinned by caret.
/// </summary>
/// <remarks>
/// The gaps, each a construct the grammar takes and the popup never named: <c>time none</c>,
/// <c>tempo … shuffle</c>, <c>cue { }</c>, the navigation marks and <c>q</c> and a phrase
/// reference in music, a section's <c>lyrics</c> / <c>chords</c> cells, the top-level
/// <c>transpose</c> / <c>using</c> / <c>drummap</c>, a score header's basename /
/// <c>transpose</c> / <c>pitch</c>, the clef before a <c>staff</c> part and the tuning before
/// a <c>tab</c> part, a bare MIDI-only part, a lyrics body's <c>[N. …]</c>,
/// <c>@feather(accel|rit)</c>, <c>@arpeggio(bracket)</c>, <c>@bend(N)</c>; and two
/// hand-written tables (the key modes, the override targets) that now read the compiler's
/// vocabulary.
/// </remarks>
[Trait("Category", "Unit")]
public class CompletionAuditTests
{
    private static LilySharpLanguageServer.CompletionContext Ctx(string text)
        => LilySharpLanguageServer.GetCompletionContext(text, text.Length);

    /// <summary>Errors from BOTH passes, as CompletionVocabularyTests reads them.</summary>
    private static List<Diagnostic> Errors(string source)
    {
        var tree = SyntaxTree.Parse(source);
        return [.. tree.Diagnostics.Concat(SemanticValidation.Run(tree))
            .Where(d => d.Severity == DiagnosticSeverity.Error)];
    }

    private static void AssertCompiles(string source, string what)
    {
        var errors = Errors(source);
        Assert.True(errors.Count == 0,
            $"{what} is refused: " + string.Join(" | ", errors.Select(d => $"{d.Code} {d.Message}")) + "\n" + source);
    }

    /// <summary>A snippet as the editor leaves it: <c>${1:x}</c> → <c>x</c>, <c>$0</c> →
    /// <paramref name="caret"/> (what the writer types at the caret).</summary>
    private static string Resolved(CompletionItem item, string caret = "")
    {
        string text = item.InsertText ?? item.Label ?? "";
        if (item.InsertTextFormat != InsertTextFormat.Snippet)
            return text;
        text = Regex.Replace(text, @"\$\{\d+:([^}]*)\}", "$1");
        text = text.Replace("$0", caret);
        return Regex.Replace(text, @"\$\{\d+\}|\$\d+", "");
    }

    /// <summary>A whole book: <paramref name="top"/> ahead of a one-part section-major piece,
    /// <paramref name="music"/> as the part's bars, <paramref name="header"/> between the
    /// score's form name and its brace, <paramref name="items"/> as its body.</summary>
    private static string Book(string top = "", string music = "c4 d e f |", string header = "",
        string items = "staff m", string sectionExtra = "")
        => $"{top}\npart m {{ clef treble }}\nsection A {{ {sectionExtra} m {{ {music} }} }}\n"
         + $"form main {{ A }}\nscore main {header} {{ {items} }}\n";

    // ================= contexts =================

    [Theory]
    // The score header, before its brace — after the form name, a basename, an option's value.
    [InlineData("score main ", "AfterScoreHeader")]
    [InlineData("score main \"out\" ", "AfterScoreHeader")]
    [InlineData("score main transpose d ", "AfterScoreHeader")]
    [InlineData("score main \"out\" pitch concert tr", "AfterScoreHeader")]
    // `transpose |` takes a pitch everywhere the word is a directive.
    [InlineData("score main transpose ", "AfterTransposePitch")]
    [InlineData("transpose ", "AfterTransposePitch")]
    [InlineData("part sax { transpose ", "AfterTransposePitch")]
    // `tab` — parts and tunings; after a tuning, the parts (and, the tuning word being a
    // legal part name, the style selector); after the part, the style selector.
    [InlineData("score main { tab ", "AfterTabRef")]
    [InlineData("score main { tab bass5 ", "AfterTabTuningRef")]
    [InlineData("score main { tab bass ", "AfterTabTuningRef")]
    [InlineData("score main { tab melody ", "AfterTabAttachName")]
    [InlineData("score main { tab bass5 melody ", "AfterTabAttachName")]
    [InlineData("score main { staff m  tab melody ", "AfterTabAttachName")]
    // `staff CLEF |` / `ossia CLEF |` — the part name comes next.
    [InlineData("score main { staff bass ", "AfterStaffClefRef")]
    [InlineData("score main { ossia treble ", "AfterStaffClefRef")]
    [InlineData("score main { grandStaff { staff treble_8 ", "AfterStaffClefRef")]
    // …and a part NAME after `staff` keeps the selector list.
    [InlineData("score main { staff melody ", "AfterStaffAttachName")]
    // The two bare-name groups.
    [InlineData("score main { condensedStaff { ", "BarePartNameList")]
    [InlineData("score main { combinedStaff { fl1 ", "BarePartNameList")]
    // Lyrics bodies, in every spelling: a section-major cell (named, bound, unnamed), a
    // part-major track's inner section, mid-verse.
    [InlineData("section A { lyrics w { ", "LyricsBody")]
    [InlineData("section A { lyrics w sings m { ", "LyricsBody")]
    [InlineData("section A { melody { c } lyrics { ", "LyricsBody")]
    [InlineData("lyrics w { section A { ", "LyricsBody")]
    [InlineData("lyrics w sings m { section A { Twin- kle ", "LyricsBody")]
    // The top-level track itself keeps its section-scaffold list, and a music body its own.
    [InlineData("lyrics w { ", "LyricsBlock")]
    [InlineData("section A { m { c4 d ", "MusicBlock")]
    [InlineData("score main { ", "ScoreBlock")]
    public void TheNewPositions_GetTheirOwnContext(string text, string expected)
        => Assert.Equal(expected, Ctx(text).ToString());

    // ================= the top level =================

    [Fact]
    public void TopLevel_OffersTransposeUsingAndDrummap_AndEachCompiles()
    {
        var items = LilySharpLanguageServer.GetTopLevelCompletions().Items;
        foreach (string label in new[] { "transpose", "using", "drummap" })
        {
            var item = Assert.Single(items, i => i.Label == label);
            Assert.False(string.IsNullOrEmpty(item.Detail), label + " has no help line");
            // `transpose` wants a pitch, `using` a file name; the drummap snippet is whole.
            string caret = label switch { "transpose" => "d", "using" => "other.lys", _ => "" };
            AssertCompiles(Book(top: Resolved(item, caret)), $"top-level `{label}`");
        }
        // `transpose` is a file singleton — dropped once written, as `octave` is.
        var after = LilySharpLanguageServer.GetTopLevelCompletions("transpose d\n").Items.Select(i => i.Label);
        Assert.DoesNotContain("transpose", after);
        Assert.Contains("using", after);
    }

    // ================= time / tempo =================

    [Fact]
    public void EveryTimeSignatureOffered_Compiles_AtTheTopLevelAndInMusic()
    {
        var labels = LilySharpLanguageServer.GetTimeCompletions().Items.Select(i => i.Label!).ToList();
        Assert.Contains("none", labels);
        foreach (string time in labels)
        {
            AssertCompiles(Book(top: $"time {time}"), $"top-level `time {time}`");
            // Senza misura counts no bars, so the bar is left unclosed for it.
            string music = time == "none" ? "time none c4 d e f g" : $"time {time} c4 d e f |";
            var errors = Errors(Book(music: music))
                .Where(d => d.Code != DiagnosticCodes.MeasureOverflow && d.Code != DiagnosticCodes.MeasureIncomplete)
                .ToList();
            Assert.True(errors.Count == 0, $"in-music `time {time}` is refused: "
                + string.Join(" | ", errors.Select(d => $"{d.Code} {d.Message}")));
        }
    }

    [Fact]
    public void TempoForms_CarryEveryFeelWord_AndEachCompiles()
    {
        var items = LilySharpLanguageServer.GetTempoCompletions().Items;
        foreach (string feel in LanguageVocabulary.TempoFeelWords)
            Assert.Contains(items, i => i.Label!.EndsWith(" " + feel, StringComparison.Ordinal));
        foreach (var item in items)
            AssertCompiles(Book(top: "tempo " + Resolved(item)), $"`tempo {item.Label}`");
    }

    // ================= music =================

    [Fact]
    public void Music_OffersCue_Q_NavigationMarks_AndPhraseReferences_AndEachCompiles()
    {
        var items = LilySharpLanguageServer.GetMusicCompletions("", 0, phraseNames: ["theme"]).Items;
        var labels = items.Select(i => i.Label!).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("cue", labels);
        Assert.Contains("q", labels);
        Assert.Contains("theme", labels);
        foreach (string mark in LanguageVocabulary.NavigationMarks)
            Assert.Contains(mark, labels);

        var cue = items.Single(i => i.Label == "cue");
        AssertCompiles(Book(music: "c4 d " + Resolved(cue, "e4 f") + " g4 a |"), "`cue { }` in music");
        AssertCompiles(Book(music: "cue bass { c4 d } e4 f |"), "`cue bass { }` in music");
        AssertCompiles(Book(music: "<c e g>4 q q q |"), "`q` in music");
        AssertCompiles("phrase theme { c4 d e f | }\n" + Book(music: "theme g4 a b c' |"), "a phrase reference in music");
        // The marks are landmarks at a bar's edge (mid-bar is LYS4003, a warning).
        foreach (string mark in LanguageVocabulary.NavigationMarks)
            AssertCompiles(Book(music: $"c4 d e f | {mark} g4 a b c' |"), $"`{mark}` in music");
        // …and a phrase list absent (the parameterless callers) offers no reference row.
        Assert.DoesNotContain("theme",
            LilySharpLanguageServer.GetMusicCompletions("", 0).Items.Select(i => i.Label));
    }

    [Fact]
    public void NavigationMarks_AreTheCompilersList_InBothPopups()
    {
        const string doc = "part m { section A { c4 d e f | } }\nform main { A }";
        var form = LilySharpLanguageServer.GetFormCompletions(doc).Items.Select(i => i.Label).ToHashSet();
        var drums = LilySharpLanguageServer.GetDrumCompletions().Items.Select(i => i.Label).ToHashSet();
        foreach (string mark in LanguageVocabulary.NavigationMarks)
        {
            Assert.Contains(mark, form);
            Assert.Contains(mark, drums);
            AssertCompiles(Book().Replace("form main { A }", $"form main {{ A {mark} A }}"), $"`{mark}` in a form");
        }
        // The list is the ten the grammar spells (GRAMMAR NavMark) — guard the guard.
        Assert.Equal(10, LanguageVocabulary.NavigationMarks.Count);
    }

    // ================= a section's track cells =================

    [Fact]
    public void SectionMajorSection_OffersLyricsAndChordsCells_AndEachCompiles()
    {
        var text = "part melody { }\npart bass { }\nsection A { ";
        var items = LilySharpLanguageServer.GetSectionBlockCompletions(text, text.Length).Items;
        var lyrics = items.Single(i => i.Label == "lyrics");
        var chords = items.Single(i => i.Label == "chords");
        // The lyrics scaffold binds to the first declared part.
        Assert.Contains("sings melody", Resolved(lyrics), StringComparison.Ordinal);

        string book = $$"""
            part melody { clef treble }
            part bass { clef bass }
            section A { {{Resolved(lyrics, "Twin- kle twin- kle |")}}
              {{Resolved(chords, "C G7 |")}}
              melody { c4 c g g | }
              bass { c2 g | }
            }
            form main { A }
            score main { staff melody  lyrics words  chords prog  staff bass }
            """;
        AssertCompiles(book, "the section's lyrics and chords cells");
    }

    // ================= the score =================

    [Fact]
    public void ScoreHeader_OffersBasenameTransposeAndPitch_AndEachCompiles()
    {
        var items = LilySharpLanguageServer.GetScoreHeaderCompletions().Items;
        Assert.Equal(new[] { "{ }", "\"\"", "transpose", "pitch" }, items.Select(i => i.Label).ToArray());
        AssertCompiles(Book(header: Resolved(items.Single(i => i.Label == "\"\""), "out")), "a score basename");
        AssertCompiles(Book(header: Resolved(items.Single(i => i.Label == "transpose"), "d")), "a score's transpose");
        AssertCompiles(Book(header: Resolved(items.Single(i => i.Label == "pitch"), "concert")), "a score's pitch");
        // The braces item opens the body where the render items are offered.
        Assert.Equal("editor.action.triggerSuggest", items.Single(i => i.Label == "{ }").Command?.CommandIdentifier);
        Assert.Empty(LilySharpLanguageServer.GetTransposePitchCompletions().Items);
    }

    [Fact]
    public void StaffRef_OffersEveryMusicClefBeforeThePart_AndEachCompiles()
    {
        var text = Book();
        var items = LilySharpLanguageServer.GetStaffRefCompletions(text).Items;
        Assert.Equal("m", items[0].Label);                       // the parts first
        var clefs = items.Where(i => i.Kind == CompletionItemKind.EnumMember).Select(i => i.Label!).ToList();
        Assert.Equal(LanguageVocabulary.ClefNames.OrderBy(c => c, StringComparer.Ordinal),
            clefs.OrderBy(c => c, StringComparer.Ordinal));
        foreach (string clef in clefs)
        {
            AssertCompiles(Book(items: $"staff {clef} m"), $"`staff {clef} m`");
            AssertCompiles(Book(items: $"staff m  ossia {clef} m"), $"`ossia {clef} m`");
        }
        // After the clef: the parts, then the selectors (the clef word may be the part).
        var after = LilySharpLanguageServer.GetStaffClefRefCompletions(text).Items.Select(i => i.Label).ToList();
        Assert.Equal("m", after[0]);
        Assert.Contains("as lines", after);
    }

    [Fact]
    public void TabRef_OffersEveryTuningBeforeThePart_AndEachCompiles()
    {
        var text = Book();
        var items = LilySharpLanguageServer.GetTabRefCompletions(text).Items;
        Assert.Equal("m", items[0].Label);
        var tunings = items.Where(i => i.Kind == CompletionItemKind.EnumMember).Select(i => i.Label!).ToList();
        Assert.Equal(LanguageVocabulary.TuningNames.OrderBy(t => t, StringComparer.Ordinal),
            tunings.OrderBy(t => t, StringComparer.Ordinal));
        foreach (string tuning in tunings)
            AssertCompiles(Book(items: $"staff m  tab {tuning} m"), $"`tab {tuning} m`");
    }

    [Fact]
    public void ScoreBody_OffersTheDeclaredPartsAsMidiOnlyItems_AndOneCompiles()
    {
        string text = Book() + "part click { clef percussion }\n";
        var items = LilySharpLanguageServer.WithMidiOnlyParts(
            LilySharpLanguageServer.GetScoreBlockCompletions(), text).Items;
        var bare = items.Where(i => i.Kind == CompletionItemKind.Reference).Select(i => i.Label).ToArray();
        Assert.Equal(new[] { "m", "click" }, bare);
        // The bare item sorts after every render keyword.
        Assert.True(string.CompareOrdinal(items.Single(i => i.Label == "click").SortText,
            items.Single(i => i.Label == "staff").SortText) > 0);
        AssertCompiles(Book(items: "staff m  click") + "part click { clef percussion }\n"
                       + "section A { click { r4 r r r | } }\n", "a bare MIDI-only part");
    }

    // ================= a lyrics body =================

    [Fact]
    public void LyricsBody_OffersTheVerseHeaders_AndEachCompiles()
    {
        var items = LilySharpLanguageServer.GetLyricVoltaCompletions().Items;
        Assert.Equal(new[] { "[1. ]", "[2. ]", "[1-2. ]", "[~1. ]" }, items.Select(i => i.Label).ToArray());
        foreach (var item in items)
        {
            string verse = Resolved(item, "la la la la |");
            AssertCompiles(Book(sectionExtra: $"lyrics w sings m {{ {verse} }}", items: "staff m  lyrics w"),
                $"`{item.Label}` in a section-major lyrics cell");
            AssertCompiles($"part m {{ section A {{ c4 d e f | }} }}\nlyrics w sings m {{ section A {{ {verse} }} }}\n"
                           + "form main { A }\nscore main { staff m  lyrics w }", $"`{item.Label}` in a part-major track");
        }
        // No pitch letters in a lyrics body.
        Assert.DoesNotContain(items, i => i.Label is "c" or "d");
    }

    // ================= the two tables that now read the compiler =================

    [Fact]
    public void KeyModes_AreTheCompilersNine_InItsOrder_AndEachCompiles()
    {
        var offered = LilySharpLanguageServer.GetKeyModeCompletions().Items.Select(i => i.Label).ToList();
        Assert.Equal(LanguageVocabulary.KeyModes, offered);
        Assert.Equal(9, offered.Count);
        foreach (string mode in offered)
            AssertCompiles(Book(top: $"key c {mode}"), $"`key c {mode}`");
        // The vocabulary IS what the parser tests: every word lexes to a kind the parser's
        // predicate accepts, and the predicate accepts no kind the list has no word for.
        Assert.All(LanguageVocabulary.KeyModes,
            m => Assert.True(SyntaxFacts.IsKeyModeKeyword(LilySharp.Core.Parser.Lexer.GetKeywordKind(m)), m));
        Assert.Equal(9, Enum.GetValues<SyntaxKind>().Count(SyntaxFacts.IsKeyModeKeyword));
        // A wrong-case word is still refused, naming the nine.
        var wrong = Errors(Book(top: "key c Major"));
        Assert.Contains(wrong, d => d.Message.Contains("locrian", StringComparison.Ordinal));
    }

    [Fact]
    public void OverrideTargets_AreTheCompilersSupportedPairs_AndEachCompiles()
    {
        var offered = LilySharpLanguageServer.GetOverrideCompletions().Items.Select(i => i.Label!).ToList();
        Assert.Equal(LanguageVocabulary.GrobOverrideSpellings.OrderBy(s => s, StringComparer.Ordinal),
            offered.OrderBy(s => s, StringComparer.Ordinal));
        Assert.NotEmpty(offered);
        foreach (var item in LilySharpLanguageServer.GetOverrideCompletions().Items)
        {
            // The value is the property's kind; a property this test cannot value is a
            // property the editor grew a row for that nobody described — say so.
            string value = item.Label!.EndsWith(".color", StringComparison.Ordinal) ? "red"
                : item.Label.EndsWith(".transparent", StringComparison.Ordinal) ? "true"
                : throw new InvalidOperationException($"no value known for '{item.Label}'");
            Assert.False(string.IsNullOrEmpty(item.Detail), item.Label + " has no help line");
            Assert.NotNull(item.Command);   // colour and true/false enumerate
            AssertCompiles(Book(top: $"override {item.Label} = {value}"), $"`override {item.Label}`");
            AssertCompiles(Book(music: $"c4 d revert {item.Label} e f |"), $"`revert {item.Label}`");
        }
    }

    // ================= the annotation arguments =================

    [Theory]
    [InlineData("feather", "accel")]
    [InlineData("feather", "rit")]
    [InlineData("bend", "3")]
    public void TheWiderArgumentRows_AreOffered(string family, string argument)
        => Assert.Contains(LilySharpLanguageServer.GetAnnotationArgumentCompletions(family)!.Items,
            i => i.Label == argument);

    [Fact]
    public void ArpeggioBracket_IsOffered_AndCompiles()
    {
        Assert.Contains(LilySharpLanguageServer.GetArticulationCompletions().Items, i => i.Label == "arpeggio(bracket)");
        AssertCompiles(Book(music: "<c e g>4@arpeggio(bracket) d e f |"), "`@arpeggio(bracket)`");
    }

    // ================= session 365: the audit's pick-up box =================
    // (the rows session 364 named and left: the tab row's `as` continuation, the printed
    // ottava spellings, and the last three hand-written tables — the score items, the section
    // directives, the paper `size` key and the tonic signatures — now read from the compiler.)

    [Fact]
    public void TabRow_OffersTheStyleSelectorAfterThePart_AndEachCompiles()
    {
        var text = Book();
        var items = LilySharpLanguageServer.GetTabAttachNameCompletions().Items;
        // The selector rows first, one per compiler style, then the score's continuations.
        var selectors = items.TakeWhile(i => i.Label!.StartsWith("as ", StringComparison.Ordinal)).Select(i => i.Label!).ToList();
        Assert.Equal(LanguageVocabulary.TabStyles.Select(s => "as " + s), selectors);
        Assert.Contains(items, i => i.Label == "staff");
        foreach (string selector in selectors)
        {
            AssertCompiles(Book(items: $"staff m  tab m {selector}"), $"`tab m {selector}`");
            AssertCompiles(Book(items: $"staff m  tab bass5 m {selector}"), $"`tab bass5 m {selector}`");
        }
        // After a tuning: the parts, then the same selectors (the tuning word may be the part).
        var afterTuning = LilySharpLanguageServer.GetTabTuningRefCompletions(text).Items.Select(i => i.Label).ToList();
        Assert.Equal("m", afterTuning[0]);
        foreach (string selector in selectors)
            Assert.Contains(selector, afterTuning);
        AssertCompiles(Book(items: "staff m  tab bass") + "part bass { instrument bass }\nsection A { bass { r1 | } }\n",
            "`tab bass` naming a part called bass");
    }

    [Theory]
    [InlineData("8va")]
    [InlineData("8vb")]
    [InlineData("15ma")]
    [InlineData("15mb")]
    public void ThePrintedOttavaSpellings_AreOffered_AndEachCompiles(string spelling)
    {
        var item = Assert.Single(LilySharpLanguageServer.GetArticulationCompletions().Items, i => i.Label == spelling);
        Assert.False(string.IsNullOrEmpty(item.Detail), spelling + " has no help line");
        AssertCompiles(Book(music: $"c'4@{spelling} d' e' f'@!{spelling} |"), $"`@{spelling} … @!{spelling}`");
    }

    [Fact]
    public void ScoreBody_ReadsTheCompilersItemList()
    {
        var items = LilySharpLanguageServer.GetScoreBlockCompletions().Items;
        Assert.Equal(LanguageVocabulary.ScoreItemKeywords, items.Select(i => i.Label));
        Assert.True(items.Length >= 15, $"only {items.Length} score items");
        foreach (var item in items)
        {
            Assert.False(string.IsNullOrEmpty(item.Detail), item.Label + " has no help line");
            Assert.False(string.IsNullOrEmpty(item.InsertText), item.Label + " inserts nothing");
        }
        // The rows that take a part (or a name) re-open the popup; a braced group does not.
        foreach (string partTaking in new[] { "staff", "tab", "ossia", "chords", "lyrics" })
            Assert.NotNull(items.Single(i => i.Label == partTaking).Command);
        foreach (string braced in new[] { "grandStaff", "staffGroup", "choirStaff", "condensedStaff", "combinedStaff" })
            Assert.Null(items.Single(i => i.Label == braced).Command);
        // (DocKeywordListTests holds the vocabulary itself to ParseRenderItem.)
    }

    [Fact]
    public void SectionHeader_ReadsTheCompilersDirectiveList_AndEachCompiles()
    {
        // A part-major book: the top-level section is a HEADER and offers the directives alone.
        const string partMajor = "part m { clef treble section A { c4 d e f | } }\nsection A { ";
        var offered = LilySharpLanguageServer.GetSectionBlockCompletions(partMajor, partMajor.Length).Items
            .Select(i => i.Label).ToList();
        Assert.Equal(LanguageVocabulary.SectionSettings, offered);

        // …and the parser's own message for a stray item in a section names the same words.
        var stray = SyntaxTree.Parse("section A { \"oops\" m { c4 d e f | } }").Diagnostics
            .Single(d => d.Code == DiagnosticCodes.StrayItemToken);
        foreach (string directive in LanguageVocabulary.SectionSettings)
            Assert.Contains(directive, stray.Message, StringComparison.Ordinal);

        var value = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["partial"] = "4", ["key"] = "g major", ["time"] = "4/4", ["tempo"] = "120",
            ["override"] = "NoteHead.color = red",
        };
        foreach (string directive in LanguageVocabulary.SectionSettings)
        {
            string line = $"{directive} {value[directive]}";
            // A pickup shortens the bar, so the bar-length warnings are not the question here.
            static bool NotABarLength(Diagnostic d)
                => d.Code != DiagnosticCodes.MeasureOverflow && d.Code != DiagnosticCodes.MeasureIncomplete;
            var header = Errors($"part m {{ clef treble section A {{ c4 d e f | }} }}\nsection A {{ {line} }}\n"
                                + "form main { A }\nscore main { staff m }").Where(NotABarLength).ToList();
            Assert.True(header.Count == 0, $"`{line}` in a part-major section header is refused: "
                + string.Join(" | ", header.Select(d => $"{d.Code} {d.Message}")));
            var beside = Errors(Book(sectionExtra: line)).Where(NotABarLength).ToList();
            Assert.True(beside.Count == 0, $"`{line}` beside a section-major part cell is refused: "
                + string.Join(" | ", beside.Select(d => $"{d.Code} {d.Message}")));
        }
    }

    [Fact]
    public void PaperSizeRow_IsTheReadersKey()
    {
        var first = LilySharpLanguageServer.GetPaperBlockCompletions().Items[0];
        Assert.Equal(LanguageVocabulary.PaperSizeKey, first.Label);
        Assert.Equal("size", LanguageVocabulary.PaperSizeKey);
        AssertCompiles(Book(top: "paper { size jisb5 }"), "`paper { size jisb5 }`");
    }

    [Fact]
    public void KeyTonics_DescribeTheCompilersSignature_AndEachCompiles()
    {
        var items = LilySharpLanguageServer.GetKeyTonicCompletions().Items;
        Assert.Equal(15, items.Length);
        // The counts are KeySpelling's, pinned on the three the circle's ends and centre give.
        Assert.Contains("0 ♯/♭", items.Single(i => i.Label == "c").Detail);
        Assert.Contains("7 ♯", items.Single(i => i.Label == "cis").Detail);
        Assert.Contains("6 ♯", items.Single(i => i.Label == "fis").Detail);
        Assert.Contains("7 ♭", items.Single(i => i.Label == "ces").Detail);
        Assert.Contains("1 ♭", items.Single(i => i.Label == "f").Detail);
        foreach (var item in items)
            AssertCompiles(Book(top: $"key {item.Label} major"), $"`key {item.Label} major`");
    }
}
