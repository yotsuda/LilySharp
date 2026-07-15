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
using LilySharp.Lsp;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// After <c>section </c> inside a part-major <c>part { }</c> body the editor offers the
/// document's section names this part does NOT yet declare, so a part can be filled in
/// with the sections it is still missing.
/// </summary>
[Trait("Category", "Unit")]
public class SectionNameCompletionTests
{
    private static string[] Missing(string text)
        => LilySharpLanguageServer.GetMissingSectionNameCompletions(text, text.Length)
            .Items.Select(i => i.Label).ToArray();

    [Fact]
    public void AfterSection_InPartBlock_IsItsOwnContext()
    {
        var text = "part bass { section ";
        Assert.Equal(LilySharpLanguageServer.CompletionContext.AfterSection,
            LilySharpLanguageServer.GetCompletionContext(text, text.Length));
    }

    [Fact]
    public void OffersOnlyKnownSectionsNotYetInThisPart()
    {
        // melody declares A and B; bass already has A, so only B is still missing here.
        var text =
            "part melody { section A { c } section B { d } }\n" +
            "part bass { section A { e } section ";
        Assert.Equal(new[] { "B" }, Missing(text));
    }

    [Fact]
    public void WhenEverySectionIsPresent_OffersNothing()
    {
        var text =
            "part melody { section A { c } }\n" +
            "part bass { section A { e } section ";
        Assert.Empty(Missing(text));
    }

    [Fact]
    public void PartialNameBeingTyped_IsNotCountedAsAlreadyDeclared()
    {
        // `section B|` (no brace yet) must keep offering B; the editor filters to it.
        var text =
            "part melody { section A { c } section B { d } }\n" +
            "part bass { section A { e } section B";
        Assert.Contains("B", Missing(text));
    }

    [Fact]
    public void EditingAnExistingDeclarationInPlace_StillOffersThatName()
    {
        // Cursor sits inside `section B| {` — B is the declaration being edited, so it is
        // NOT filtered out of its own list.
        const string text = "part melody { section A { c } section B { d } }\n" +
                            "part bass { section B { f } }";
        int offset = text.LastIndexOf("section B", System.StringComparison.Ordinal) + "section B".Length;
        var labels = LilySharpLanguageServer.GetMissingSectionNameCompletions(text, offset)
            .Items.Select(i => i.Label).ToArray();
        Assert.Contains("B", labels);
    }

    [Fact]
    public void AfterSection_InLyricsBlock_IsItsOwnContext()
    {
        var text = "lyrics words { section ";
        Assert.Equal(LilySharpLanguageServer.CompletionContext.AfterSection,
            LilySharpLanguageServer.GetCompletionContext(text, text.Length));
    }

    [Fact]
    public void LyricsBlock_OffersKnownSectionsNotYetInThisTrack()
    {
        // The lyrics track is filled in the same way: melody has A and B, the lyrics
        // track already has A, so only B is offered.
        var text =
            "part melody { section A { c } section B { d } }\n" +
            "lyrics words { section A { la la } section ";
        Assert.Equal(new[] { "B" }, Missing(text));
    }

    [Fact]
    public void CompletingASectionName_OpensTheBodyWithCaretInside()
    {
        var text =
            "part melody { section A { c } section B { d } }\n" +
            "part bass { section A { e } section ";
        var b = LilySharpLanguageServer.GetMissingSectionNameCompletions(text, text.Length)
            .Items.Single(i => i.Label == "B");
        Assert.Equal("B {\n\t$0\n}", b.InsertText);
        Assert.Equal(InsertTextFormat.Snippet, b.InsertTextFormat);
    }

    [Fact]
    public void AfterSection_AtTopLevel_IsItsOwnContext()
    {
        // Section-major top-level `section |` is a declaration site too — it fills in
        // from the form's references.
        var text = "section ";
        Assert.Equal(LilySharpLanguageServer.CompletionContext.AfterSection,
            LilySharpLanguageServer.GetCompletionContext(text, text.Length));
    }

    [Fact]
    public void SectionMajor_OffersFormReferencedSectionsNotYetDeclared()
    {
        // The form names A and B; only A is written, so `section ` at the top level
        // offers B — the section the piece expects but that has not been declared.
        var text =
            "section A { melody { c d } }\n" +
            "form main { A B A }\n" +
            "section ";
        Assert.Equal(new[] { "B" }, Missing(text));
    }

    [Fact]
    public void SectionMajor_WithoutAForm_OffersNothing()
    {
        // No form means no known-but-unwritten section, so nothing is suggested.
        var text =
            "section A { melody { c d } }\n" +
            "section ";
        Assert.Empty(Missing(text));
    }

    [Fact]
    public void SectionMajor_DoesNotReofferAnAlreadyDeclaredSection()
    {
        // A is declared and the form references it; it must not be offered again.
        var text =
            "section A { melody { c d } }\n" +
            "section B { melody { e f } }\n" +
            "form main { A B }\n" +
            "section ";
        Assert.Empty(Missing(text));
    }

    [Fact]
    public void SectionInsideAMusicBody_DoesNotTriggerTheContext()
    {
        // `section` is not a declaration site inside a section's music body.
        var text = "section A { melody { c d section ";
        Assert.NotEqual(LilySharpLanguageServer.CompletionContext.AfterSection,
            LilySharpLanguageServer.GetCompletionContext(text, text.Length));
    }

    [Fact]
    public void AfterSection_InUnnamedLyricsBlock_IsItsOwnContext()
    {
        // The lyrics track name is optional — `lyrics { … }`. The keyword then lands in
        // the frame's Name, not its Prefix, and the context must still fire.
        var text = "lyrics { section ";
        Assert.Equal(LilySharpLanguageServer.CompletionContext.AfterSection,
            LilySharpLanguageServer.GetCompletionContext(text, text.Length));
    }

    [Fact]
    public void UnnamedLyricsBlock_OffersKnownSectionsNotYetInThisTrack()
    {
        var text =
            "part melody { section A { c } section B { d } }\n" +
            "lyrics { section A { la la } section ";
        Assert.Equal(new[] { "B" }, Missing(text));
    }

    [Fact]
    public void WhenABraceAlreadyFollows_InsertsJustTheNameNoBody()
    {
        // A body already exists (`section ▮{ e }`), so completing B inserts only the
        // name, not a second `{ }`.
        var text = "part melody { section A { c } section B { d } }\n" +
                   "part bass { section { e } }";
        int offset = text.LastIndexOf("section ", System.StringComparison.Ordinal) + "section ".Length;
        var b = LilySharpLanguageServer.GetMissingSectionNameCompletions(text, offset)
            .Items.Single(i => i.Label == "B");
        Assert.Equal("B", b.InsertText);
        Assert.NotEqual(InsertTextFormat.Snippet, b.InsertTextFormat);
    }

    [Fact]
    public void WhenABraceFollowsAPartialName_StillInsertsJustTheName()
    {
        // `section B▮ { … }` — editing the name of an already-braced section.
        var text = "part melody { section A { c } section B { d } }\n" +
                   "part bass { section B { e } }";
        int offset = text.LastIndexOf("section B", System.StringComparison.Ordinal) + "section B".Length;
        var b = LilySharpLanguageServer.GetMissingSectionNameCompletions(text, offset)
            .Items.Single(i => i.Label == "B");
        Assert.Equal("B", b.InsertText);
    }

    [Fact]
    public void WithNoFollowingBrace_TheContainerCloseIsNotMistakenForABody()
    {
        // The next non-whitespace char is the part's own `}`, not a section body, so the
        // snippet still opens the braces.
        var text = "part melody { section A { c } section B { d } }\n" +
                   "part bass { section A { e } section }";
        int offset = text.LastIndexOf("section ", System.StringComparison.Ordinal) + "section ".Length;
        var b = LilySharpLanguageServer.GetMissingSectionNameCompletions(text, offset)
            .Items.Single(i => i.Label == "B");
        Assert.Equal("B {\n\t$0\n}", b.InsertText);
        Assert.Equal(InsertTextFormat.Snippet, b.InsertTextFormat);
    }

    [Fact]
    public void SectionPartProperty_RetriggersToOfferTheMissingSections()
    {
        var section = LilySharpLanguageServer.GetPartPropertyCompletions().Items
            .Single(i => i.Label == "section");
        Assert.Equal("section $0", section.InsertText);
        Assert.Equal("editor.action.triggerSuggest", section.Command?.CommandIdentifier);
    }

    // ----- directly inside a top-level lyrics { } track (before typing `section`) -----

    [Fact]
    public void DirectlyInsideTopLevelLyricsBlock_IsLyricsBlockContext_NotMusic()
    {
        foreach (var text in new[] { "lyrics { ", "lyrics words { " })
            Assert.Equal(LilySharpLanguageServer.CompletionContext.LyricsBlock,
                LilySharpLanguageServer.GetCompletionContext(text, text.Length));
    }

    [Fact]
    public void DirectlyInsideTopLevelLyricsBlock_OffersSectionScaffoldsWithTheSectionKeyword()
    {
        var text =
            "part melody { section A { c } section B { d } }\n" +
            "lyrics { ";
        var items = LilySharpLanguageServer.GetLyricsSectionCompletions(text, text.Length).Items;
        // The label reads `section A` (matching what is inserted), not a bare `A`.
        Assert.Equal(new[] { "section A", "section B" }, items.Select(i => i.Label).ToArray());
        var a = items.Single(i => i.Label == "section A");
        Assert.Equal("section A {\n\t$0\n}", a.InsertText);
        Assert.Equal(InsertTextFormat.Snippet, a.InsertTextFormat);
    }

    [Fact]
    public void DirectlyInsideTopLevelLyricsBlock_DropsSectionsAlreadyInThisTrack()
    {
        var text =
            "part melody { section A { c } section B { d } }\n" +
            "lyrics { section A { la } ";   // A already present → only B remains
        var labels = LilySharpLanguageServer.GetLyricsSectionCompletions(text, text.Length)
            .Items.Select(i => i.Label).ToArray();
        Assert.Equal(new[] { "section B" }, labels);
    }

    [Fact]
    public void NoteBoundSectionCellLyrics_IsNotLyricsBlockContext()
    {
        // `section A { melody {} lyrics { ` is a note-bound cell (its body is syllables),
        // NOT a top-level track — it must not get the section-scaffold list.
        var text = "section A { melody { c } lyrics { ";
        Assert.NotEqual(LilySharpLanguageServer.CompletionContext.LyricsBlock,
            LilySharpLanguageServer.GetCompletionContext(text, text.Length));
    }

    [Fact]
    public void InsideAnInnerSectionOfALyricsTrack_IsNotLyricsBlockContext()
    {
        // `lyrics { section A { ` — the innermost open block is the section body
        // (syllables), not the lyrics-track level.
        var text = "lyrics { section A { ";
        Assert.NotEqual(LilySharpLanguageServer.CompletionContext.LyricsBlock,
            LilySharpLanguageServer.GetCompletionContext(text, text.Length));
    }

    // ----- directly inside a part { } body: properties + section scaffolds -----

    [Fact]
    public void DirectlyInsidePartBlock_OffersSectionScaffoldsAlongsideProperties()
    {
        var text = "part melody { section A { c } section B { d } }\npart bass { ";
        var items = LilySharpLanguageServer.GetPartBlockCompletions(text, text.Length).Items;
        // The part properties are still there…
        Assert.Contains(items, i => i.Label == "clef");
        // …plus one-step `section A` / `section B` scaffolds (label carries the keyword).
        var scaffolds = items.Where(i => i.Label!.StartsWith("section ", System.StringComparison.Ordinal))
            .Select(i => i.Label).ToArray();
        Assert.Equal(new[] { "section A", "section B" }, scaffolds);
        var a = items.Single(i => i.Label == "section A");
        Assert.Equal("section A {\n\t$0\n}", a.InsertText);
        Assert.Equal(InsertTextFormat.Snippet, a.InsertTextFormat);
    }
}
