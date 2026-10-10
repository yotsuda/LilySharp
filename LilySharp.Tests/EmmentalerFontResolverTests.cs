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

using LilySharp.Core.Rendering;
using LilySharp.Core.Rendering.Pdf;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The PDF font resolver routes families to bundled/embedded faces. These cases are
/// environment-independent (they use a font name no system has, so it never installs
/// or embeds — exercising the fallback-to-bundled-serif routing).
/// </summary>
[Trait("Category", "Unit")]
public class EmmentalerFontResolverTests
{
    private const string BogusFont = "ZzNoSuchFont1234567890";

    /// <summary>
    /// The generic families resolve to the BUNDLED TeX Gyre faces — the same files
    /// <c>TextFontMetrics</c> measures, so the PDF draws the font the layout reserved for.
    /// Was Liberation Serif, which is Times-metric and 9% narrower than what the engine
    /// now spaces.
    /// </summary>
    [Fact]
    public void GenericFamilies_ResolveToTheBundledTexGyreFaces()
    {
        var r = new EmmentalerFontResolver();
        Assert.Equal("Schola#", r.ResolveTypeface("serif", false, false)?.FaceName);
        Assert.Equal("ScholaBold#", r.ResolveTypeface("serif", true, false)?.FaceName);
        Assert.Equal("ScholaItalic#", r.ResolveTypeface("serif", false, true)?.FaceName);
        // Chord symbols are sans; PdfSharpCore has no generic for it either.
        Assert.Equal("Heros#", r.ResolveTypeface("sans", false, false)?.FaceName);
        Assert.Equal("HerosBold#", r.ResolveTypeface("sans-serif", true, false)?.FaceName);
    }

    [Fact]
    public void Emmentaler_ResolvesToMusicFace()
        => Assert.Equal("Emmentaler#", new EmmentalerFontResolver().ResolveTypeface("Emmentaler", false, false)?.FaceName);

    // The document-side answers (DocumentTextFaces.Face): a stand-in of the serif family,
    // of the sans family, and a face that carries its own program.
    private static readonly DocumentTextFaces.Face SerifStandIn = new(Embedded: false, Sans: false);
    private static readonly DocumentTextFaces.Face SansStandIn = new(Embedded: false, Sans: true);

    [Fact]
    public void ConfiguredFont_NotEmbedded_StandsInAsTheBundledSerif()
    {
        // font "X" (no `embedded`) must NOT embed a system font — the document's answer
        // is the bundled serif, so nothing proprietary is embedded without asking.
        var r = new EmmentalerFontResolver();
        var faces = r.LoadTextFonts(new TextFontPlan.Builder().Everything([BogusFont]).Build());
        Assert.Equal(SerifStandIn, faces.Of(BogusFont));
        Assert.False(faces.EmbedsOwnProgram(BogusFont));
    }

    [Fact]
    public void ConfiguredFont_EmbedButNotInstalled_StandsInAsTheBundledSerif()
    {
        // `embedded` on a font this machine doesn't have: nothing to embed, so the answer
        // is still the bundled serif (never a LysEmbed face).
        var r = new EmmentalerFontResolver();
        var faces = r.LoadTextFonts(new TextFontPlan.Builder().Everything([BogusFont]).Embed().Build());
        Assert.Equal(SerifStandIn, faces.Of(BogusFont));
        Assert.Throws<InvalidOperationException>(
            () => r.ResolveTypeface(EmmentalerFontResolver.EmbeddedFamilyRequest(BogusFont), false, false));
    }

    [Fact]
    public void ASansRoleBoundToAnAbsentFace_StandsInWithTheSansTheLayoutMeasured()
    {
        // The stand-in follows the ROLE's family, not a fixed serif: chord symbols are
        // reserved against the bundled Heros, so falling back to Schola would draw a
        // face 9% off the boxes the spacing built. Before `fonts { }` there was one
        // configured face and it always stood in as serif, which was right only because
        // `font "X"` bound every role at once.
        var r = new EmmentalerFontResolver();
        var faces = r.LoadTextFonts(new TextFontPlan.Builder()
            .Role(TextRole.ChordName, [BogusFont])
            .Build());
        Assert.Equal(SansStandIn, faces.Of(BogusFont));
    }

    [Fact]
    public void OneNameBoundToBothFamilies_KeepsTheFirstRolesStandIn()
    {
        // A deliberate fold, and this is its only observer: a document holds ONE
        // stand-in per face NAME, so a name bound to both a serif and a sans role keeps
        // whichever role was declared first (the roles are walked in TextRole order, so
        // LyricText precedes ChordName). Keying faces on (name, family) instead would
        // record the distinction — at the cost of embedding one program twice, to serve a
        // difference that only shows when the face is ABSENT. Change the fold and this
        // case says so out loud.
        var r = new EmmentalerFontResolver();
        var faces = r.LoadTextFonts(new TextFontPlan.Builder()
            .Role(TextRole.LyricText, [BogusFont])
            .Role(TextRole.ChordName, [BogusFont])
            .Build());
        Assert.Equal(SerifStandIn, faces.Of(BogusFont));
    }

    [Fact]
    public void TwoRolesBoundToTwoAbsentFaces_EachResolvesToItsOwn()
    {
        // One document, several configured faces — the shape `fonts { }` introduced.
        // A single-face resolver answered the FIRST name for every one of them.
        const string Other = "NoSuchFontFace-Second";
        var r = new EmmentalerFontResolver();
        var faces = r.LoadTextFonts(new TextFontPlan.Builder()
            .Role(TextRole.LyricText, [BogusFont])
            .Role(TextRole.ChordName, [Other])
            .Build());
        Assert.Equal(SerifStandIn, faces.Of(BogusFont));
        Assert.Equal(SansStandIn, faces.Of(Other));
    }

    [Fact]
    public void AFaceTheDirectiveNeverNamed_HasNoAnswer()
    {
        // The brace and the music faces are asked for under their own names; a document
        // says nothing about them, and the page must be able to tell that apart from a
        // stand-in. ⚠️ THE BRACE IS THE ONE THAT BITES: TextFontPlan.Resolve names
        // "Emmentaler-Brace" for TextRole.SystemBrace on EVERY plan, so a loader that walks
        // the roles without skipping it records the brace as a serif stand-in — and the page
        // then asks for "serif" and draws no brace at all. The p885 sweep caught exactly
        // that (46 grand-staff PDFs without their Emmentaler-Brace font) before this line did.
        var r = new EmmentalerFontResolver();
        Assert.Null(r.LoadTextFonts(TextFontPlan.Default).Of(BogusFont));
        Assert.Null(r.LoadTextFonts(TextFontPlan.Default).Of(TextFontPlan.BraceFaceName));
        Assert.Null(r.LoadTextFonts(new TextFontPlan.Builder().Everything([BogusFont]).Embed().Build())
            .Of(TextFontPlan.BraceFaceName));
    }

    /// <summary>
    /// A music font's companion face (Petaluma Script) is in the bundle under the OFL, so
    /// its own program is on the page whether or not <c>embedded</c> was written — the one
    /// deterministic embedded face, because it needs nothing installed.
    /// </summary>
    [Fact]
    public void ACompanionFace_CarriesItsOwnProgram_UnderTheEmbedRequest()
    {
        const string Companion = "Petaluma Script";
        var r = new EmmentalerFontResolver();
        var faces = r.LoadTextFonts(new TextFontPlan.Builder().Role(TextRole.ChordName, [Companion]).Build());
        Assert.True(faces.EmbedsOwnProgram(Companion));
        Assert.Equal(new DocumentTextFaces.Face(Embedded: true, Sans: true), faces.Of(Companion));

        // The request the page makes, and the program behind the face it is answered with.
        string request = EmmentalerFontResolver.EmbeddedFamilyRequest(Companion);
        Assert.Equal("LysEmbed:Petaluma Script", request);
        string faceName = Assert.IsType<string>(r.ResolveTypeface(request, false, false)?.FaceName);
        Assert.Equal("LysEmbed:Petaluma Script#", faceName);
        Assert.NotEmpty(r.GetFont(faceName));
        // Every weight and slant is the one face; PdfSharpCore synthesises emphasis.
        Assert.Equal(faceName, r.ResolveTypeface(request, true, true)?.FaceName);
    }

    /// <summary>
    /// Two documents loaded from the ONE process-wide resolver keep their own answers: the
    /// answer is in the object each document holds, not in the resolver, so loading the
    /// second cannot change the first.
    /// </summary>
    /// <remarks>
    /// This is what replaced the map the resolver used to keep per document. The old field
    /// had two failure modes: concurrently, a torn map (session 317: <c>Operations that
    /// change non-concurrent collections must have exclusive access</c>, out of
    /// <c>Dictionary.TryInsert</c> under <c>SetTextFonts</c>); and sequentially, the LAST
    /// document's answer standing for every face name while PdfSharpCore's cache froze the
    /// FIRST document's — measured in session 885 (the resolver's field remark). With no
    /// shared answer there is nothing to tear and nothing to freeze; the page-level half is
    /// <c>PdfFontIsolationTests</c>.
    /// </remarks>
    [Fact]
    public void TwoDocumentsLoadedFromOneResolver_KeepTheirOwnAnswers()
    {
        var r = new EmmentalerFontResolver();
        var first = r.LoadTextFonts(new TextFontPlan.Builder().Role(TextRole.LyricText, [BogusFont]).Build());
        var second = r.LoadTextFonts(new TextFontPlan.Builder().Role(TextRole.ChordName, [BogusFont]).Build());
        Assert.Equal(SerifStandIn, first.Of(BogusFont));
        Assert.Equal(SansStandIn, second.Of(BogusFont));
        // The resolver itself no longer has an opinion about the name.
        Assert.Null(r.ResolveTypeface(BogusFont, false, false));
    }
}
