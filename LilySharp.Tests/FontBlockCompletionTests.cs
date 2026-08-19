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
using System.Linq;
using System.Text.RegularExpressions;
using LilySharp.Core.Rendering;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using LilySharp.Lsp;
using LilySharp.Lsp.Protocol;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Completing a <c>fonts { … }</c> body: role keys, then the installed faces.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ Until 2026-08-18 the block had NO completion context of its own. Its <c>{</c> is a
/// brace like any other, so every caret inside one fell through to the MusicBlock case and
/// the popup offered PITCHES AND ARTICULATIONS where a role key belongs. Measured across all
/// twelve carets a writer reaches while filling one in, from <c>fonts {</c> to
/// <c>fonts { serif "Georgia"  sans "|</c> — every one of them reported MusicBlock.
/// </para>
/// <para>
/// ⚠️ The one-liner meanwhile had TWO dedicated contexts, which is most of why the block
/// read as the harder form to write. That asymmetry was an editor gap, not a language fact,
/// and the syntax question could not be judged fairly until it was closed.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class FontBlockCompletionTests
{
    private static LilySharpLanguageServer.CompletionContext Ctx(string text)
        => LilySharpLanguageServer.GetCompletionContext(text, text.Length);

    [Theory]
    // Every caret a writer reaches while filling in a block. Not one of them may be music.
    [InlineData("fonts {")]
    [InlineData("fonts { ")]
    [InlineData("fonts { ser")]
    [InlineData("fonts { serif \"Georgia\" ")]
    [InlineData("fonts {\n  serif \"Georgia\"\n  ")]
    public void InsideTheBlock_OffersRoleKeys_NotMusic(string text)
        => Assert.Equal(LilySharpLanguageServer.CompletionContext.FontBlock, Ctx(text));

    [Theory]
    [InlineData("fonts { serif ")]
    [InlineData("fonts { lyricText ")]
    [InlineData("fonts { chordName ")]
    [InlineData("fonts { notation ")]
    public void AfterARoleKey_OffersTheValue(string text)
        => Assert.Equal(LilySharpLanguageServer.CompletionContext.AfterFontRoleKey, Ctx(text));

    [Theory]
    // The face list has to be reachable from INSIDE the block, not only from the one-liner:
    // the keyword owning the string here is the role key, so the one-liner's lookup — which
    // asks for the word before the opening quote — could never have found it.
    [InlineData("fonts { serif \"")]
    [InlineData("fonts { serif \"Geo")]
    [InlineData("fonts { serif \"Georgia\"  sans \"")]
    [InlineData("fonts { lyricText \"Charis SIL\" \"Noto Ser")]
    public void InsideABlockString_OffersTheInstalledFaces(string text)
        => Assert.Equal(LilySharpLanguageServer.CompletionContext.AfterFontName, Ctx(text));

    [Fact]
    public void TheKeyListIsTheReadersVocabulary_NotACopyOfIt()
    {
        // ★ The keys come from TextRoles.AllKeySpellings(), the one home FontPlanReader
        // validates against. Asserting the two agree is what keeps a key added there from
        // being invisible in the editor — the failure mode the score-item lists had.
        var labels = LilySharpLanguageServer.GetFontBlockCompletions()
            .Items.Select(i => i.Label).ToArray();

        foreach (string key in TextRoles.AllKeySpellings())
            Assert.Contains(key, labels);

        // …and the block takes `embedded` too, which is an entry but not a key.
        Assert.Contains("embedded", labels);
    }

    [Fact]
    public void EveryKeyLandsTheCaretInTheFaceList()
    {
        // A key that inserted only its own spelling would leave the writer to type the
        // quotes and re-trigger by hand — the motion the one-liner never demanded.
        foreach (var item in LilySharpLanguageServer.GetFontBlockCompletions().Items
                     .Where(i => i.Label != "embedded"))
        {
            Assert.Equal("\"$0\"", item.InsertText?[^4..]);
            Assert.NotNull(item.Command);
        }
    }

    [Fact]
    public void TheBlockSnippetIsPreFilledWithTheFacesAlreadyInUse()
    {
        // ★ The defaults are the faces the document is ALREADY in, so accepting the
        // completion and changing nothing does not move the page. Measured 2026-08-18: a
        // book with these two bindings and the same book with no `font` at all have
        // IDENTICAL geometry, differing only in carrying the font-family attribute
        // explicitly. Swapping the families, or binding serif alone, does move it.
        //
        // ⚠️ An earlier draft wrote `${1:face}` into BOTH bindings so one face could be
        // typed once. `face` is not a face, and the two families have different defaults —
        // a mirror cannot carry two.
        foreach (string insert in new[]
                 {
                     LilySharpLanguageServer.GetFontDeclarationCompletions()
                         .Items.Single(i => i.Label == "{ … }").InsertText!,
                     // The keyword item shares the one home; if it ever stops, this fails.
                     LilySharpLanguageServer.GetTopLevelCompletions()
                         .Items.Single(i => i.Label == "fonts").InsertText!,
                 })
        {
            Assert.Contains("${1:" + TextFontMetrics.SerifFamily + "}", insert, StringComparison.Ordinal);
            Assert.Contains("${2:" + TextFontMetrics.SansFamily + "}", insert, StringComparison.Ordinal);
            // Two independent fields: editing the prose face must not drag the sans one.
            Assert.DoesNotContain("${1:face}", insert, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheSnippetDefaultsAreTheEnginesOwnFaceNames_NotACopy()
    {
        // ⚠️ The bundled family names are ONE quantity. Typed a second time in the editor,
        // the popup goes on offering a face the engine stopped bundling — and nothing would
        // be red. Assert the editor's text is built from the engine's constants.
        string insert = LilySharpLanguageServer.GetFontDeclarationCompletions()
            .Items.Single(i => i.Label == "{ … }").InsertText!;

        Assert.Contains(TextFontMetrics.SerifFamily, insert, StringComparison.Ordinal);
        Assert.Contains(TextFontMetrics.SansFamily, insert, StringComparison.Ordinal);
        // …and they really are two different names, or the assertion above is vacuous.
        Assert.NotEqual(TextFontMetrics.SerifFamily, TextFontMetrics.SansFamily);
    }

    /// <summary>An LSP snippet as the editor leaves it when the writer accepts and changes
    /// nothing: each placeholder becomes its own default.</summary>
    private static string Resolved(string snippet) =>
        Regex.Replace(Regex.Replace(snippet, @"\$\{\d+:([^}]*)\}", "$1"), @"\$\{\d+\}|\$\d+", "");

    /// <summary>Every coordinate and extent on the page, in document order.</summary>
    /// <remarks>
    /// ⚠️ The character class needs the look-behind. Without it <c>y="</c> matches inside
    /// <c>font-famil<b>y="</b>TeX Gyre Schola"</c>, and the face NAME is read as a
    /// coordinate — which is how the first measurement of this claim reported that the
    /// geometry moved when it does not (RULES §5.3: a instrument that answers a different
    /// question than the one asked).
    /// </remarks>
    private static string Geometry(string svg) =>
        string.Join("|", Regex.Matches(
                Regex.Replace(svg, @"\s*data-pos=""\d+""", ""),
                @"(?<![-A-Za-z])(?:x|y|x1|y1|x2|y2|width|height|d)=""([^""]+)""")
            .Select(m => m.Groups[1].Value));

    /// <summary>A book carrying both text families: chord symbols are the ONE role whose
    /// default is sans, so without them a `sans` binding cannot be observed at all.</summary>
    /// <remarks>
    /// ⚠️ The chord part is <c>prog</c> and not <c>p</c>. It WAS <c>p</c> when this book was
    /// written, and <c>p</c> is the piano dynamic — a reserved word, which RULES §5.5 names
    /// outright ("part 名に予約語を避ける — `p` は dynamic"). The book had been failing to
    /// parse ever since, silently: the tests that used it only rendered and compared, and a
    /// page still comes out of a tree that carries errors. The first test to READ its
    /// diagnostics found it.
    /// </remarks>
    private const string Book = """

        part m { clef treble }

        section A {
          m { c4 d e f | g2 g | }
          lyrics w sings m { la la la la | la la | }
          chords prog { c1 | g1 | }
        }

        form main { A }

        score main "out" {
          title "A Title"
          chords prog
          staff m
          lyrics w
        }
        """;

    private static string Svg(string source) =>
        SvgGenerator.Generate(SyntaxTree.Parse(source), new SvgRenderOptions { EmbedFont = false });

    [Fact]
    public void AcceptingTheSnippetUnchanged_DoesNotMoveThePage()
    {
        // ★ The point of pre-filling with the faces already in use: a writer who completes
        // `font`, sees the block and decides to change only ONE role must not discover that
        // merely inserting it reflowed the score.
        string inserted = "fonts " + Resolved(
            LilySharpLanguageServer.GetFontDeclarationCompletions()
                .Items.Single(i => i.Label == "{ … }").InsertText!);

        Assert.Equal(Geometry(Svg(Book)), Geometry(Svg(inserted + "\n" + Book)));

        // ⚠️ This equality cannot be poisoned by writing a WRONG default: a face nobody has
        // falls back to the bundled metrics, so `serif "face"` engraves the same geometry as
        // no directive at all. Measured 2026-08-18 — the poison run that put `face` back
        // failed the two assertions below this one and left the equality green. What keeps
        // the equality honest is therefore the controls, not a poison: they use faces that
        // DO resolve, and they must differ.
        // Controls, or "identical" says nothing: a real binding DOES move the page.
        Assert.NotEqual(Geometry(Svg(Book)), Geometry(Svg(
            $"fonts {{ serif \"{TextFontMetrics.SansFamily}\"  sans \"{TextFontMetrics.SerifFamily}\" }}\n" + Book)));
        Assert.NotEqual(Geometry(Svg(Book)), Geometry(Svg(
            $"fonts {{ serif \"{TextFontMetrics.SansFamily}\" }}\n" + Book)));

        // …and the resolver is not vacuous — it really fills the placeholders.
        Assert.Contains(TextFontMetrics.SerifFamily, inserted, StringComparison.Ordinal);
        Assert.DoesNotContain("${", inserted, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryFaceTheSnippetPreFillsIsAlsoOffered()
    {
        // ★ ONE QUESTION, THREE READERS: "is this face available?" The metrics path answers
        // it correctly (the bundle is consulted before the machine), the missing-face warning
        // answered it wrongly until f7e18024, and the face LIST answered it wrongly until
        // now — Skia enumerates INSTALLED families, and a bundled face is shipped, not
        // installed. So the popup offered every face except the two the completion itself
        // pre-fills, and the two that are present on every machine by construction.
        //
        // Tying the list to the snippet is what makes the three agree: a face good enough to
        // be typed in for the writer has to be good enough to be offered to them.
        string snippet = LilySharpLanguageServer.GetFontDeclarationCompletions()
            .Items.Single(i => i.Label == "{ … }").InsertText!;
        var offered = LilySharpLanguageServer.GetFontNameCompletions()
            .Items.Select(i => i.Label).ToArray();

        foreach (string face in new[] { TextFontMetrics.SerifFamily, TextFontMetrics.SansFamily })
        {
            Assert.Contains(face, snippet, StringComparison.Ordinal);
            Assert.Contains(face, offered);
        }

        // …and they lead the list: a bundled face is the one choice that cannot make the
        // page depend on the machine.
        var bundled = LilySharpLanguageServer.GetFontNameCompletions().Items
            .Where(i => i.Label == TextFontMetrics.SerifFamily || i.Label == TextFontMetrics.SansFamily);
        Assert.All(bundled, i => Assert.StartsWith("!", i.SortText!, StringComparison.Ordinal));
    }

    [Fact]
    public void ANameTheEngineShipsIsNeverReportedMissing_AndTheOfferedOnesAreClean()
    {
        // The other half of the same agreement: everything the editor offers must survive
        // the validator. An offered face that warns is the editor sending the writer into a
        // diagnostic — which is exactly what the bundled pre-fill did until f7e18024.
        //
        // ⚠️ Only the BUNDLED entries are checked here, not all ~190: the installed ones are
        // this machine's, so asserting over them would make the test's meaning depend on
        // which fonts happen to be present (RULES §5.5).
        foreach (string face in new[] { TextFontMetrics.SerifFamily, TextFontMetrics.SansFamily })
        {
            var tree = SyntaxTree.Parse($"fonts {{ serif \"{face}\" }}\n" + Book);
            var all = tree.Diagnostics.Concat(SemanticValidation.Run(tree));
            Assert.DoesNotContain(all, d => d.Code == DiagnosticCodes.FontNotFound);
        }
    }

    [Fact]
    public void AGenericFamilyBindingOffersOnlyThatShape()
    {
        // `serif "…"` and `sans "…"` are asking WHAT SHAPE the letters are, so the list is
        // narrowed to it — read from each font's own OS/2 classification, not guessed from
        // its name.
        //
        // ⚠️ Asserted on the BUNDLED faces, which ship, rather than on this machine's fonts:
        // a test that names Georgia or Arial means something different on every box
        // (RULES §5.5). What the machine's fonts do is covered by ShapeFromOs2's own tests,
        // which need no installed font at all.
        var serif = LilySharpLanguageServer.GetFontNameCompletions("serif")
            .Items.Select(i => i.Label).ToArray();
        var sans = LilySharpLanguageServer.GetFontNameCompletions("sans")
            .Items.Select(i => i.Label).ToArray();

        Assert.Contains(TextFontMetrics.SerifFamily, serif);
        Assert.DoesNotContain(TextFontMetrics.SansFamily, serif);
        Assert.Contains(TextFontMetrics.SansFamily, sans);
        Assert.DoesNotContain(TextFontMetrics.SerifFamily, sans);
    }

    [Fact]
    public void AROLEBindingKeepsTheWholeList()
    {
        // A role or a group may legitimately name ANY face — a script face for a title is a
        // real choice — so only the two generic-family keys narrow. Nothing else may.
        var whole = LilySharpLanguageServer.GetFontNameCompletions()
            .Items.Select(i => i.Label).ToArray();

        foreach (string key in new[] { "lyricText", "title", "marks", "notation", "" })
        {
            var offered = LilySharpLanguageServer.GetFontNameCompletions(key)
                .Items.Select(i => i.Label).ToArray();
            Assert.Equal(whole, offered);
        }
        // …and both bundled faces are in the whole list, which is what "whole" has to mean.
        Assert.Contains(TextFontMetrics.SerifFamily, whole);
        Assert.Contains(TextFontMetrics.SansFamily, whole);
    }

    [Fact]
    public void AFaceThatClassifiesAsNothing_IsKeptAndSaidSo()
    {
        // ⚠️ A font is free to fill in neither OS/2 field, and 16 of this machine's 232
        // families do exactly that — including SimSun, a CJK SERIF, and all of Sitka.
        // Dropping them would make a real and wanted face unreachable from the binding that
        // wants it. They are kept, in a tail, and told the truth about themselves.
        //
        // The claim is conditional on this machine having such a font, so it is written that
        // way rather than asserted flat: if there are none, there is nothing to keep.
        var serif = LilySharpLanguageServer.GetFontNameCompletions("serif").Items;
        var unknown = serif.Where(i => i.Detail?.Contains("unclassified") == true).ToArray();

        foreach (var i in unknown)
        {
            Assert.Equal(FontEmbedInfo.FaceShape.Unknown, FontEmbedInfo.ShapeOf(i.Label!));
            // Last: a face that cannot say what it is must not outrank one that can.
            Assert.StartsWith("9", i.SortText!, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void EveryValueOfferedAfterAKey_IsAcceptedAfterThatKey()
    {
        // ★ THE AGREEMENT, one level below the last one: it is not enough that every KEY the
        // popup offers is real — every VALUE it offers must fit THE KEY IT WAS OFFERED FOR.
        //
        // ⚠️ The value list was flat until 2026-08-18 and offered `serif` / `sans` after
        // every key, so at `fonts { serif |` the popup proposed exactly the two words the
        // reader was about to refuse (LYS8006, "a generic family takes quoted face names,
        // not another family"). The reader's own message says the offer must not be made
        // there; the editor made it anyway, because the value list did not know which key it
        // was answering for.
        //
        // Written as a PRODUCT — every key crossed with every value offered for that key —
        // so neither list can grow past the other unnoticed.
        var refused = new System.Collections.Generic.List<string>();
        int pairs = 0;

        foreach (string key in TextRoles.AllKeySpellings())
        {
            foreach (var item in LilySharpLanguageServer.GetFontRoleValueCompletions(key).Items)
            {
                // The quoted item is a snippet for a face name; stand a real one in it.
                string value = item.Label == "\"…\""
                    ? $"\"{TextFontMetrics.SerifFamily}\""
                    : item.Label!;
                pairs++;

                var tree = SyntaxTree.Parse($"fonts {{ {key} {value} }}\n" + Book);
                var error = tree.Diagnostics.Concat(SemanticValidation.Run(tree))
                    .FirstOrDefault(d => d.Severity == LilySharp.Core.Syntax.DiagnosticSeverity.Error);
                if (error is not null)
                    refused.Add($"fonts {{ {key} {value} }} -> {error.Code}: {error.Message}");
            }
        }

        Assert.True(pairs > 60, $"only {pairs} key/value pairs checked");
        Assert.True(refused.Count == 0,
            "the popup offers values the reader refuses at that key:\n  "
            + string.Join("\n  ", refused));
    }

    [Fact]
    public void AGenericFamilyOffersOnlyAQuotedFace()
    {
        // The narrowing, stated directly: a family takes a NAME and not another family.
        foreach (string family in new[] { "serif", "sans" })
        {
            var labels = LilySharpLanguageServer.GetFontRoleValueCompletions(family)
                .Items.Select(i => i.Label).ToArray();
            Assert.Equal(["\"…\""], labels);
        }
        // …while a role or a group keeps the redirect, which IS accepted there.
        foreach (string key in new[] { "chordName", "lyrics", "title" })
        {
            var labels = LilySharpLanguageServer.GetFontRoleValueCompletions(key)
                .Items.Select(i => i.Label).ToArray();
            Assert.Contains("serif", labels);
            Assert.Contains("sans", labels);
        }
    }

    [Fact]
    public void TheCheckCanFail()
    {
        // ★ The contexts above are today's behaviour written down, so they were green the
        // moment they were built (RULES §5.4). What bites: a brace body with no context of
        // its own IS music, and that is exactly what a font block used to report.
        Assert.Equal(LilySharpLanguageServer.CompletionContext.MusicBlock,
            Ctx("part m { section A { m {  "));
        // …the caret right after the keyword has its own context, distinct from inside…
        Assert.Equal(LilySharpLanguageServer.CompletionContext.AfterFontKeyword, Ctx("fonts "));
        // …and the face list is reachable ONLY from inside a block. A bare string after
        // `font` is the removed one-liner, and the editor no longer completes into it.
        Assert.Equal(LilySharpLanguageServer.CompletionContext.AfterFontName,
            Ctx("fonts { serif \"Geo"));
        Assert.NotEqual(LilySharpLanguageServer.CompletionContext.AfterFontName,
            Ctx("font \"Geo"));
    }
}
