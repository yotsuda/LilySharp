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
using Xunit;
using LilySharp.Core.Rendering;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;

namespace LilySharp.Tests;

/// <summary>
/// Named <c>fonts NAME { }</c> / <c>paper NAME { }</c> blocks, referenced per score —
/// with an optional override block on the reference.
/// </summary>
/// <remarks>
/// The semantic claims, each pinned here because each was a design decision
/// (2026-08-23, user-approved): a reference REPLACES the file's unnamed default (no
/// hidden three-layer chain); a reference's override block reads as if its entries were
/// written at the end of the named block — so the last same-key entry wins WITHOUT a
/// duplicate warning, and the narrower-spelling rule keeps winning WHICHEVER block a
/// binding came from (a score's group override does not beat the house block's role
/// binding — the deliberate surprise, pinned by name below).
/// </remarks>
[Trait("Category", "Unit")]
public class NamedBlockReferenceTests
{
    // A two-score book: `main` references the named blocks, `parts` references nothing
    // and keeps the file's unnamed defaults.
    private const string Music = """
        section Main {
          melody { c'4 d e f | g a b c' | }
        }
        form main { Main }
        """;

    private static MultiStaffScore Collect(string source, string scoreName)
    {
        var tree = SyntaxTree.Parse(source);
        var render = tree.GetRoot().DescendantNodes().OfType<RenderDeclarationSyntax>()
            .First(r => r.FormNameText == scoreName || scoreName == "");
        return SvgGenerator.CollectScore(tree, RenderSpecParser.Parse(render));
    }

    private static Diagnostic[] Check(string source)
    {
        var tree = SyntaxTree.Parse(source);
        return [.. tree.Diagnostics, .. SemanticValidation.Run(tree)];
    }

    // ================================================================================
    // Resolution: the reference reaches the score, and only that score
    // ================================================================================

    [Fact]
    public void AReferenceResolvesForItsScoreAlone_AndReplacesTheDefault()
    {
        // The file default sets a topMargin the named block does not: if `main` kept
        // any of it, the reference would be a hidden three-layer chain — the design
        // this test pins is REPLACE (resolved = defaults + named block + override).
        string src = "paper { paperWidth 100  topMargin 20 }\n"
            + "paper wide { paperWidth 250mm }\n" + Music
            + "score main { paper wide  staff melody }\n"
            + "score parts { staff melody }\n";

        var main = Collect(src, "main");
        Assert.Equal(142.26378, main.Paper.PageWidth); // 250 * 72.27 / 127, the defaults' rounding
        Assert.Equal(LayoutOptions.Default.MarginTop, main.Paper.MarginTop); // NOT the default block's 20

        var parts = Collect(src, "parts");
        Assert.Equal(100, parts.Paper.PageWidth); // the unnamed default, untouched
        Assert.Equal(20, parts.Paper.MarginTop);
    }

    [Fact]
    public void APaperOverrideBlock_LaysOverTheNamedBlock()
    {
        string src = "paper wide { paperWidth 250mm  systemSystemSpacing { basicDistance 20 } }\n"
            + Music
            + "score main { paper wide { topMargin 12mm  systemSystemSpacing { padding 2 } }  staff melody }\n";

        var p = Collect(src, "main").Paper;
        Assert.Equal(142.26378, p.PageWidth);                            // from the named block
        Assert.Equal(6.828661, p.MarginTop);                              // 12mm, from the override
        Assert.Equal(20, p.VerticalSpacing.SystemSystem.BasicDistance);   // named block's line survives
        Assert.Equal(2, p.VerticalSpacing.SystemSystem.Padding);          // the override's line
        Assert.Equal(8, p.VerticalSpacing.SystemSystem.MinimumDistance);  // neither wrote it: default
    }

    [Fact]
    public void AFontsOverrideBlock_ReadsAsOneMergedBlock()
    {
        string src = "fonts house { serif \"Georgia\"  lyricText \"Charis SIL\" }\n"
            + Music
            + "score main { fonts house { lyricText \"Noto Serif CJK JP\" }  staff melody }\n";

        var fonts = Collect(src, "main").Fonts;
        var expected = new TextFontPlan.Builder()
            .Family(TextFontFamily.Serif, ["Georgia"])
            .Role(TextRole.LyricText, ["Noto Serif CJK JP"])
            .Build();
        Assert.Equal(expected, fonts);
        // …and no duplicate-key warning for the cross-block lyricText repeat:
        // overriding a key is the override block's purpose.
        Assert.DoesNotContain(Check(src), d => d.Code == DiagnosticCodes.DuplicateFontBinding);
    }

    [Fact]
    public void TheNarrowerSpellingWins_WhicheverBlockItCameFrom()
    {
        // ⚠️ THE DELIBERATE SURPRISE, pinned by name (design decision 2026-08-23):
        // the house block binds the ROLE lyricText, the score overrides the GROUP
        // lyrics — and the role still wins, because the resolution rule is ONE rule
        // (the narrower spelling wins, source not consulted). A house style's
        // deliberate role bindings survive a score swapping the broad base; to
        // override a role, write the same or a narrower key.
        string src = "fonts house { lyricText \"Charis SIL\" }\n"
            + Music
            + "score main { fonts house { lyrics \"Verdana\" }  staff melody }\n";

        var resolved = Collect(src, "main").Fonts.Resolve(TextRole.LyricText);
        Assert.Equal(["Charis SIL"], resolved.Names);
        // The control beside it: a role the house does NOT bind follows the group.
        var stanza = Collect(src, "main").Fonts.Resolve(TextRole.Stanza);
        Assert.Equal(["Verdana"], stanza.Names);
    }

    [Fact]
    public void EmbeddedOnEitherSide_Embeds()
    {
        string src = "fonts house { serif \"Georgia\"  sans \"Georgia\" }\n" + Music
            + "score main { fonts house { embedded }  staff melody }\n";
        Assert.True(Collect(src, "main").Fonts.Embed);
    }

    [Fact]
    public void AnUnknownName_BindsNothing_AndKeepsTheDefault()
    {
        string src = "paper { paperWidth 100 }\n" + Music
            + "score main { paper wide  staff melody }\n";
        // Refused all the way through: the error names the missing declaration…
        Assert.Contains(Check(src), d => d.Code == DiagnosticCodes.UnknownPaperBlockName);
        // …and the score keeps the file default rather than half a guess.
        Assert.Equal(100, Collect(src, "main").Paper.PageWidth);
    }

    // ================================================================================
    // The name layer's diagnostics
    // ================================================================================

    [Fact]
    public void TheNameLayer_IsValidatedBothWays()
    {
        // A duplicate declaration name is an error; an unreferenced declaration warns.
        string src = "fonts a { serif \"Georgia\" }\nfonts a { serif \"Verdana\" }\n"
            + "paper b { paperWidth 100 }\n" + Music
            + "score main { fonts a  staff melody }\n";
        var d = Check(src);
        Assert.Contains(d, x => x.Code == DiagnosticCodes.DuplicateFontsBlockName);
        Assert.Contains(d, x => x.Code == DiagnosticCodes.UnreferencedNamedPaper
                             && x.Severity == DiagnosticSeverity.Warning);
        // The referenced fonts block is NOT flagged as unreferenced.
        Assert.DoesNotContain(d, x => x.Code == DiagnosticCodes.UnreferencedNamedFonts);
    }

    [Fact]
    public void TwoNamedBlocks_AreNotTheDuplicateGlobalSetting()
    {
        // Named declarations coexist; the singleton rule is the UNNAMED default's.
        string src = "fonts a { serif \"Georgia\" }\nfonts b { serif \"Verdana\" }\n" + Music
            + "score main { fonts a  staff melody }\nscore parts { fonts b  staff melody }\n";
        Assert.DoesNotContain(Check(src),
            d => d.Code == DiagnosticCodes.DuplicateGlobalSetting);
    }

    [Fact]
    public void TwoReferencesInOneScore_WarnAndTheLastWins()
    {
        string src = "paper a { paperWidth 100 }\npaper b { paperWidth 90 }\n" + Music
            + "score main { paper a  paper b  staff melody }\n";
        Assert.Contains(Check(src), d => d.Code == DiagnosticCodes.DuplicatePaperReference
                                      && d.Severity == DiagnosticSeverity.Warning);
        Assert.Equal(90, Collect(src, "main").Paper.PageWidth);
    }

    [Theory]
    [InlineData("fonts house\n", "LYS8012")]  // a top-level named declaration needs a block
    [InlineData("paper wide\n", "LYS9010")]
    public void ANamedDeclarationWithoutABlock_IsRefused(string decl, string code)
    {
        Assert.Contains(SyntaxTree.Parse(decl + Music + "score main { staff melody }\n").Diagnostics,
            d => d.Code == code);
    }

    [Theory]
    [InlineData("fonts { serif \"Georgia\" }", "LYS8013")]  // a score's item is a reference
    [InlineData("paper { paperWidth 100 }", "LYS9011")]
    public void AnUnnamedBlockInsideAScore_IsRefused_AndBindsNothing(string item, string code)
    {
        string src = Music + "score main { " + item + "  staff melody }\n";
        Assert.Contains(SyntaxTree.Parse(src).Diagnostics, d => d.Code == code);
        // Refused all the way through: the score keeps the built-in defaults.
        Assert.Equal(LayoutOptions.Default, Collect(src, "main").Paper);
        Assert.True(Collect(src, "main").Fonts.IsDefault);
    }

    // ================================================================================
    // Round trip, and the picture
    // ================================================================================

    [Fact]
    public void NamedDeclarationsAndReferences_RoundTrip()
    {
        string src = "fonts house { serif \"Georgia\" }\n"
            + "paper wide { paperWidth 250mm  systemSystemSpacing { basicDistance 20 } }\n"
            + Music
            + "score main { paper wide { topMargin 12mm }  fonts house  staff melody }\n";
        var root = SyntaxTree.Parse(src).GetRoot();
        Assert.Equal(src, root.ToFullString());
        foreach (var n in root.DescendantNodes())
            Assert.Equal(n.ToFullString(), src.Substring(n.Position, n.FullWidth));
        Assert.Empty(Check(src).Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void TwoScores_GetTwoDifferentPages()
    {
        // The whole point of naming: one file, a wide conductor page and a default
        // part page. The claim is made on the rendered widths.
        string src = "paper wide { paperWidth 250mm }\n" + Music
            + "score main { paper wide  staff melody }\n"
            + "score parts { staff melody }\n";
        var tree = SyntaxTree.Parse(src);
        var all = SvgGenerator.GenerateAll(tree, new SvgRenderOptions { EmbedFont = false });
        Assert.Equal(2, all.Count);
        Assert.NotEqual(SvgWidth(all[0].Svg), SvgWidth(all[1].Svg));
    }

    private static string SvgWidth(string svg)
    {
        var m = System.Text.RegularExpressions.Regex.Match(svg, "<svg[^>]*width=\"([^\"]+)\"");
        Assert.True(m.Success, "no svg width attribute");
        return m.Groups[1].Value;
    }
}
