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

    [Fact]
    public void ConfiguredFont_NotEmbedded_ResolvesToBundledSerif()
    {
        // font "X" (no `embedded`) must NOT embed a system font — it maps to the
        // bundled serif so nothing proprietary is embedded without asking.
        var r = new EmmentalerFontResolver();
        r.SetTextFonts(new TextFontPlan.Builder().Everything([BogusFont]).Build());
        Assert.Equal("Schola#", r.ResolveTypeface(BogusFont, false, false)?.FaceName);
    }

    [Fact]
    public void ConfiguredFont_EmbedButNotInstalled_FallsBackToSerif()
    {
        // `embedded` on a font this machine doesn't have: nothing to embed, so it
        // still resolves to the bundled serif (never a LysEmbed face).
        var r = new EmmentalerFontResolver();
        r.SetTextFonts(new TextFontPlan.Builder().Everything([BogusFont]).Embed().Build());
        Assert.Equal("Schola#", r.ResolveTypeface(BogusFont, false, false)?.FaceName);
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
        r.SetTextFonts(new TextFontPlan.Builder()
            .Role(TextRole.ChordName, [BogusFont])
            .Build());
        Assert.Equal("Heros#", r.ResolveTypeface(BogusFont, false, false)?.FaceName);
    }

    [Fact]
    public void OneNameBoundToBothFamilies_KeepsTheFirstRolesStandIn()
    {
        // A deliberate fold, and this is its only observer: the resolver holds ONE
        // stand-in per face NAME, so a name bound to both a serif and a sans role keeps
        // whichever role was declared first (the roles are walked in TextRole order, so
        // LyricText precedes ChordName). Keying faces on (name, family) instead would
        // record the distinction — at the cost of embedding one program twice, to serve a
        // difference that only shows when the face is ABSENT. Change the fold and this
        // case says so out loud.
        var r = new EmmentalerFontResolver();
        r.SetTextFonts(new TextFontPlan.Builder()
            .Role(TextRole.LyricText, [BogusFont])
            .Role(TextRole.ChordName, [BogusFont])
            .Build());
        Assert.Equal("Schola#", r.ResolveTypeface(BogusFont, false, false)?.FaceName);
    }

    [Fact]
    public void TwoRolesBoundToTwoAbsentFaces_EachResolvesToItsOwn()
    {
        // One document, several configured faces — the shape `fonts { }` introduced.
        // A single-face resolver answered the FIRST name for every one of them.
        const string Other = "NoSuchFontFace-Second";
        var r = new EmmentalerFontResolver();
        r.SetTextFonts(new TextFontPlan.Builder()
            .Role(TextRole.LyricText, [BogusFont])
            .Role(TextRole.ChordName, [Other])
            .Build());
        Assert.Equal("Schola#", r.ResolveTypeface(BogusFont, false, false)?.FaceName);
        Assert.Equal("Heros#", r.ResolveTypeface(Other, false, false)?.FaceName);
    }

    /// <summary>
    /// Two documents configuring this resolver at once must not corrupt its face map.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE RESOLVER IS A PROCESS GLOBAL — PdfSharpCore's
    /// <c>GlobalFontSettings.FontResolver</c>, installed once by
    /// <c>PdfDocumentContext.EnsureFontResolver</c> — and EVERY <c>PdfDocumentContext</c>
    /// constructor calls <see cref="EmmentalerFontResolver.SetTextFonts"/> on it. So this is
    /// not a hypothetical: it is what two threads rendering PDFs do.
    /// <para>
    /// FOUND 2026-09-01 (session 317) as an intermittent red in
    /// <c>BackendKerningTests.PdfPlacesTextWhereTheLayoutReservedItForANamedFace</c>:
    /// <c>System.InvalidOperationException : Operations that change non-concurrent
    /// collections must have exclusive access</c>, thrown from <c>Dictionary.TryInsert</c>
    /// under <c>SetTextFonts</c>, which used to <c>Clear()</c> and refill a plain
    /// <c>Dictionary</c> in place. Green on the next run — a DIFFERENT intermittent from the
    /// HarfBuzz shaping crash closed in the same session, and both were read as one flake of
    /// session 315's parallelisation.
    /// </para>
    /// <para>
    /// ⚠️ A STRESS NET, NOT A PROOF: it cannot show the absence of a race, only catch a
    /// mutable map back in that field, which it does essentially always — the poison
    /// (restoring the in-place Clear-and-refill) throws within the first few iterations.
    /// What it does prove for the repair is the reachable half: no reader ever sees a
    /// half-built map, because the map a reader can reach is never written to.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ConfiguringFromTwoDocumentsAtOnce_NeitherCorruptsNorTearsTheFaceMap()
    {
        const string Other = "NoSuchFontFace-Second";
        var r = new EmmentalerFontResolver();

        // Neither plan asks to embed, so no plan touches the machine's fonts: the case is
        // about the map, and stays the same on every platform.
        var plans = new[]
        {
            new TextFontPlan.Builder().Role(TextRole.LyricText, [BogusFont]).Build(),
            new TextFontPlan.Builder().Role(TextRole.LyricText, [Other]).Build(),
            new TextFontPlan.Builder()
                .Role(TextRole.LyricText, [BogusFont]).Role(TextRole.ChordName, [Other]).Build(),
        };

        var failures = new System.Collections.Concurrent.ConcurrentBag<Exception>();
        var workers = new List<Task>();
        for (int t = 0; t < 8; t++)
        {
            int seed = t;
            workers.Add(Task.Run(() =>
            {
                try
                {
                    for (int i = 0; i < 500; i++)
                    {
                        if ((seed + i) % 2 == 0)
                            r.SetTextFonts(plans[(seed + i) % plans.Length]);
                        else
                        {
                            // A reader must always get one of the bundled stand-ins or the
                            // generic answer — never a torn read, and never an exception.
                            var name = r.ResolveTypeface(BogusFont, false, false)?.FaceName;
                            Assert.True(name is null or "Schola#" or "Heros#",
                                $"torn read: {name}");
                            r.EmbedsOwnProgram(Other);
                        }
                    }
                }
                catch (Exception ex)
                {
                    failures.Add(ex);
                }
            }));
        }
        await Task.WhenAll(workers);

        Assert.True(failures.IsEmpty,
            "SetTextFonts must publish a finished map rather than mutate the shared one: "
            + string.Join("\n", failures.Select(e => e.ToString())));
    }
}
