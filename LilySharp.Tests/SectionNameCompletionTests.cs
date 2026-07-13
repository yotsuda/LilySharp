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
    public void SectionNamesOutsideAPart_DoNotTriggerTheContext()
    {
        // Section-major top-level `section |` declares a NEW section; it is not the
        // part-major fill-in, so the after-section context does not fire there.
        var text = "section ";
        Assert.NotEqual(LilySharpLanguageServer.CompletionContext.AfterSection,
            LilySharpLanguageServer.GetCompletionContext(text, text.Length));
    }

    [Fact]
    public void SectionPartProperty_RetriggersToOfferTheMissingSections()
    {
        var section = LilySharpLanguageServer.GetPartPropertyCompletions().Items
            .Single(i => i.Label == "section");
        Assert.Equal("section $0", section.InsertText);
        Assert.Equal("editor.action.triggerSuggest", section.Command?.CommandIdentifier);
    }
}
