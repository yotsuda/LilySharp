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
using LilySharp.Core.LilyPond;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using LilySharp.Lsp;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The octave a tab is played in (owner's decision 2026-10-28): <c>tab bass5 melody as 8vb</c>
/// frets a guitar line an octave lower on a five-string bass — 8va / 8vb / 15ma / 15mb, the words
/// of a part's <c>transposition</c> — without moving what the part sounds or its staff prints.
/// The tab never chooses an octave by itself; a tab in another tuning that leaves many notes off
/// the strings is told the word that puts them all on (LYS5004).
/// </summary>
[Trait("Category", "Unit")]
public class TabOctaveTests
{
    // A guitar line sounding E4 to C5: on a five-string bass (B0 .. G2 + 24 = G4) the top three
    // are off the strings, and an octave lower every one is on.
    private const string Book = """
        octave absolute
        part melody { instrument guitar }
        section A { melody { e'4 g' a' b' | c''1 | } }
        form { A }
        score { staff melody  tab bass5 melody }
        """;

    private static IReadOnlyList<Diagnostic> Validate(string src) => SemanticValidation.Run(SyntaxTree.Parse(src));

    private static TabRenderSyntax Tab(string src) => SyntaxTree.Parse(src).GetRoot().DescendantNodes<TabRenderSyntax>().Single();

    [Theory]
    [InlineData("score { tab bass5 melody as 8vb numbers }", "8vb", "numbers")]
    [InlineData("score { tab bass5 melody as numbers 15mb }", "15mb", "numbers")]
    [InlineData("score { tab ukulele melody as 8va }", "8va", null)]
    public void TheSelectors_TakeAStyleAndAnOctave_InEitherOrder(string score, string octave, string? style)
    {
        var tab = Tab("part melody\n" + score);
        Assert.Equal(octave, tab.OctaveToken?.Text);
        Assert.Equal(style, tab.DisplayModeToken?.Text);
    }

    [Fact]
    public void ManyNotesOffTheStrings_AreToldTheOctave()
    {
        var advice = Assert.Single(Validate(Book), d => d.Code == DiagnosticCodes.TabOctaveAdvice);
        Assert.Contains("write 'as 8vb'", advice.Message);
        Assert.Contains(Validate(Book), d => d.Code == DiagnosticCodes.TabOutOfRange);
    }

    [Fact]
    public void AsEightVb_PutsThemOn()
    {
        var diagnostics = Validate(Book.Replace("tab bass5 melody", "tab bass5 melody as 8vb"));
        Assert.DoesNotContain(diagnostics, d => d.Code is DiagnosticCodes.TabOutOfRange or DiagnosticCodes.TabOctaveAdvice);
    }

    [Fact]
    public void OneStrayNote_IsItsOwnWarning_AndNoAdvice()
    {
        // A bass line on a four-string bass with one low E♭ (below its E1): the note is warned and
        // left off the tab, and no octave is proposed — one note must not move the whole tab.
        const string book = """
            octave absolute
            part line { instrument guitar }
            section A { line { e,4 g, a, ees,, | e,1 | } }
            form { A }
            score { tab bass line }
            """;
        var diagnostics = Validate(book);
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCodes.TabOutOfRange);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCodes.TabOctaveAdvice);
    }

    [Fact]
    public void TheTwin_FretsTheTabsTuningAndOctave()
    {
        string ly = new LilyPondExporter().Export(SyntaxTree.Parse(Book.Replace("tab bass5 melody", "tab bass5 melody as 8vb")));
        Assert.Contains("\\transpose c c, ", ly);
        Assert.Contains("stringTunings = #" + LilySharp.Core.Tablature.Tunings.LilyPondName(TuningType.Bass5), ly);
    }

    [Fact]
    public void ASecondOctave_IsRefused()
        => Assert.Contains(Validate(Book.Replace("tab bass5 melody", "tab bass5 melody as 8vb 8va")),
            d => d.Code == DiagnosticCodes.UnknownTabRenderWord && d.Message.Contains("octave"));

    [Theory]
    [InlineData("tab bass5 melody", "tab bass5 melody as 8vb")]
    [InlineData("tab bass5 melody as numbers", "tab bass5 melody as 8vb numbers")]
    [InlineData("tab bass5 melody as 8va", "tab bass5 melody as 8vb")]
    public void TheQuickFix_WritesTheWord(string item, string expected)
    {
        string text = "part melody\nscore { " + item + " }";
        var (start, end, insert) = Tab(text).OctaveEdit("8vb");
        Assert.Equal("part melody\nscore { " + expected + " }", text[..start] + insert + text[end..]);
    }

    [Fact]
    public void AfterOneSelector_TheOthersAreOffered()
    {
        string text = "part melody\nscore { tab bass5 melody as 8vb ";
        Assert.Equal("AfterTabSelectorChain", LilySharpLanguageServer.GetCompletionContext(text, text.Length).ToString());
        var labels = LilySharpLanguageServer.GetTabDisplayModeCompletions().Items.Select(i => i.Label).ToList();
        Assert.Equal(["numbers", "full", "8vb", "15mb", "8va", "15ma"], labels);
    }
}
