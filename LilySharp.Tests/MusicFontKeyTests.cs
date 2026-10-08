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
using LilySharp.Core.Rendering;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using LilySharp.Lsp;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// <c>fonts { music "…" }</c> (docs/smufl-design.md §1, §6 ② ⒜): the music font is a key of the
/// fonts block that takes quoted names only, is resolved where it is written, and is laid over
/// by <c>lysc --set music=NAME</c>.
/// </summary>
public class MusicFontKeyTests
{
    private const string Book = "part m { clef treble }\nsection A { m { c'4 d'4 e'4 f'4 } }\n";

    private static (TextFontPlan Plan, Diagnostic[] Diagnostics) Read(string fontsBlock)
    {
        var tree = SyntaxTree.Parse(fontsBlock + "\n" + Book);
        var font = tree.GetRoot().DescendantNodes<FontDeclarationSyntax>().First();
        var plan = FontPlanReader.Read(font, out _);
        return (plan, [.. tree.Diagnostics, .. SemanticValidation.Run(tree)]);
    }

    [Fact]
    public void TheMusicKey_NamesTheMusicFont_AndIsPartOfThePlansIdentity()
    {
        var (plan, diagnostics) = Read("fonts { music \"Bravura\" }");
        Assert.Equal(["Bravura"], plan.Music);
        Assert.Empty(diagnostics);
        Assert.False(plan.IsDefault);
        Assert.NotEqual(TextFontPlan.Default.Signature, plan.Signature);
        Assert.Contains("music=Bravura", plan.Signature, StringComparison.Ordinal);
        Assert.Empty(TextFontPlan.Default.Music);
    }

    [Fact]
    public void SeveralNames_AreAChain_AndALaterEntryReplacesIt()
    {
        var (plan, diagnostics) = Read("fonts { music \"Petaluma\" \"Bravura\" }");
        Assert.Equal(["Petaluma", "Bravura"], plan.Music);
        Assert.Empty(diagnostics);

        var (replaced, warned) = Read("fonts { music \"Petaluma\"  serif \"Georgia\"  music \"Leland\" }");
        Assert.Equal(["Leland"], replaced.Music);
        var warning = Assert.Single(warned, d => d.Code == DiagnosticCodes.DuplicateFontBinding);
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
    }

    [Theory]
    [InlineData("Emmentaler")]
    [InlineData("emmentaler")]
    [InlineData("bravura")]
    [InlineData("LELAND")]
    public void ABundledName_IsFoundWithoutRegardToCase(string name)
    {
        var (plan, diagnostics) = Read($"fonts {{ music \"{name}\" }}");
        Assert.Equal([name], plan.Music);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void ANameFoundNowhere_Warns_NamingEveryPlaceLooked()
    {
        var (plan, diagnostics) = Read("fonts { music \"NoSuchFont\" }");
        // Kept in the chain: the glyph-level fallback skips it; the page still comes out.
        Assert.Equal(["NoSuchFont"], plan.Music);
        var warning = Assert.Single(diagnostics, d => d.Code == DiagnosticCodes.MusicFontNotFound);
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Contains("Emmentaler is used", warning.Message, StringComparison.Ordinal);
        Assert.Contains("nosuchfont_metadata.json", warning.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Bravura", warning.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("music \"Bravura\" step +1", "step")]
    [InlineData("music \"Bravura\" size 3", "size")]
    [InlineData("music \"Bravura\" as serif", "as")]
    [InlineData("music \"Bravura\" bold", "bold")]
    public void AnAttributeOnMusic_IsRefused(string entry, string word)
    {
        var (_, diagnostics) = Read($"fonts {{ {entry} }}");
        var error = Assert.Single(diagnostics, d => d.Code == DiagnosticCodes.FontAttributeMisplaced);
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Contains($"'{word}'", error.Message, StringComparison.Ordinal);
        Assert.Contains("quoted font names only", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MusicWithoutAName_IsRefused_NamingTheBundledFonts()
    {
        var (plan, diagnostics) = Read("fonts { music  serif \"Georgia\" }");
        Assert.Empty(plan.Music);
        var error = Assert.Single(diagnostics, d => d.Code == DiagnosticCodes.FontBindingMissingValue);
        Assert.Contains("Bravura", error.Message, StringComparison.Ordinal);
        Assert.Contains("Emmentaler", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheKey_IsInTheVocabulary_TheEditorAndTheColouringRead()
    {
        Assert.Contains(TextRoles.MusicKey, TextRoles.AllKeySpellings());
        Assert.True(TextRoles.IsMusicKey("music"));
        Assert.False(TextRoles.IsMusicKey("Music"));
        Assert.Equal("music", TextRoles.CaseOnlyMatch("Music"));
        // Not a text role, group or family: the reader takes it on its own branch.
        Assert.False(TextRoles.TryParseKey("music", out _, out _, out _));
    }

    [Fact]
    public void TheEditor_OffersTheKey_AndTheBundledFontsInsideItsString()
    {
        var key = Assert.Single(LilySharpLanguageServer.GetFontBlockCompletions().Items, i => i.Label == "music");
        Assert.Equal("music \"$0\"", key.InsertText);

        var values = LilySharpLanguageServer.GetFontRoleValueCompletions("music").Items;
        Assert.Equal(["\"…\""], values.Select(v => v.Label));

        var names = LilySharpLanguageServer.GetFontNameCompletions("music").Items.Select(i => i.Label).ToList();
        Assert.Equal("Emmentaler", names[0]);
        Assert.Contains("Bravura", names);
        Assert.Contains("Petaluma", names);
        Assert.Contains("Leland", names);
        Assert.DoesNotContain(TextFontMetrics.SerifFamily, names);
    }

    [Fact]
    public void TheSetting_LaysANameOverTheFile()
    {
        var settings = PaperOverrides.Parse(["music=bravura"], out var error);
        Assert.Null(error);
        Assert.NotNull(settings);
        Assert.Equal("Bravura", settings!.Music);
        // Over the file's chain, and over a file that wrote none.
        var (plan, _) = Read("fonts { music \"Petaluma\" \"Leland\" }");
        Assert.Equal(["Bravura"], settings.ApplyFonts(plan).Music);
        Assert.Equal(["Bravura"], settings.ApplyFonts(TextFontPlan.Default).Music);
        // A setting names the music font alone: the faces the file bound stay as they were.
        var (faces, _) = Read("fonts { serif \"Georgia\" }");
        var laid = settings.ApplyFonts(faces);
        Assert.Equal(["Bravura"], laid.Music);
        Assert.Equal(faces.Resolve(TextRole.Title), laid.Resolve(TextRole.Title));
        Assert.Equal(["Emmentaler"], PaperOverrides.Parse(["music=emmentaler"], out _)!.ApplyFonts(TextFontPlan.Default).Music);
    }

    [Theory]
    [InlineData("music=NoSuchFont", "No music font named 'NoSuchFont'")]
    [InlineData("music=", "needs a font name")]
    public void TheSetting_RefusesANameFoundNowhere(string setting, string message)
    {
        Assert.Null(PaperOverrides.Parse([setting], out var error));
        Assert.NotNull(error);
        Assert.Contains(message, error!, StringComparison.Ordinal);
    }

    [Fact]
    public void TheTwin_StaysInEmmentaler_AndSaysSo()
    {
        var exporter = new LilySharp.Core.LilyPond.LilyPondExporter();
        exporter.Export(SyntaxTree.Parse("fonts { music \"Bravura\" }\n" + Book));
        Assert.Contains(exporter.Warnings,
            w => w.Contains("music \"Bravura\"", StringComparison.Ordinal) && w.Contains("Emmentaler", StringComparison.Ordinal));
    }
}
