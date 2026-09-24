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

using System.IO;
using System.Linq;
using LilySharp.Core.Editing;
using LilySharp.Core.Syntax;
using LilySharp.Lsp;
using LilySharp.Lsp.Protocol;
using Xunit;

namespace LilySharp.Tests.Lsp;

/// <summary>
/// The section CodeLens: one lens over every <c>section</c> declaration, saying the section's
/// length and who writes it — named while they are few, counted by kind when they are many —
/// or each length with who writes it when they disagree, and how often each form names it; a
/// click lists everything that writes it. A section is a shared span of time, so the same name
/// in three parts is ONE section, and every one of its declarations says so.
/// ⚠️ No "layer" in what the user reads: it is not a word Lily#'s documents use for this, and
/// the owner asked what it meant.
/// </summary>
public class SectionCodeLensTests
{
    private static CodeLens[] LensesOf(string text)
    {
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        var uri = new System.Uri("file:///lens.lys");
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = uri, Text = text, LanguageId = "lilysharp", Version = 1 },
        });
        return server.GetCodeLens(new CodeLensParams { TextDocument = new TextDocumentIdentifier { Uri = uri } }) ?? [];
    }

    [Fact]
    public void AnAgreeingSection_SaysItsLengthWritersAndFormReferences()
    {
        const string text = """
            part flute { section A { c1 | c1 | } section B { c1 | } }
            part oboe { section A { c1 | c1 | } section B { c1 | } }
            chords harmony { section A { C | F | } }
            form main { A B A }
            score main { chords harmony  staff flute  staff oboe }
            """;
        var lenses = LensesOf(text);
        // One lens per section, over its first declaration — A is declared three times and B
        // twice, but the later declarations agree and say nothing.
        Assert.Equal(2, lenses.Length);
        Assert.Equal("Section A · 2 bars · flute, oboe and chords 'harmony' · 2× in form main", lenses[0].Command!.Title);
        Assert.Equal(0, lenses[0].Range.Start.Line);
        Assert.Equal("Section B · 1 bar · flute and oboe · 1× in form main", lenses[1].Command!.Title);
    }

    [Fact]
    public void ALaterDeclaration_SpeaksOnlyWhenItIsShort()
    {
        const string text = """
            section A { partial 2 }
            part melody { section A { c'4 d | e2 f | g2 g | } }
            part bass { section A { c4 d | e2 f | g2 g | } }
            chords prog { section A { C4 D | E2 F | } }
            form main { A }
            score main { chords prog  staff melody  staff bass }
            """;
        var lenses = LensesOf(text);
        Assert.Equal(2, lenses.Length);
        // The header-only declaration comes first, and carries the whole section.
        Assert.Equal(0, lenses[0].Range.Start.Line);
        Assert.StartsWith("Section A · ⚠", lenses[0].Command!.Title);
        // melody and bass agree with the section: no line. The chord row is short.
        Assert.Equal(3, lenses[1].Range.Start.Line);
        Assert.Equal("⚠ Section A · 2 bars here (1 bar shorter) · 3 bars in melody and bass", lenses[1].Command!.Title);
        Assert.Equal("lilysharp.showSectionLayers", lenses[1].Command!.CommandIdentifier);
    }

    [Fact]
    public void ASectionMajorBlock_SpeaksOverItsOwnLine()
    {
        // Every layer is written inside the one declaration, so the declaration's name has no
        // line to point at the short one — the block itself carries it.
        const string text = """
            part melody { clef treble }
            part X { }
            section A {
              partial 2
              melody { c'4 d | e2 f | g2 g | }
              X { | | | }
              chords prog { | | }
            }
            form main { A A }
            score main { staff melody }
            """;
        var lenses = LensesOf(text);
        Assert.Equal(2, lenses.Length);
        Assert.Equal(2, lenses[0].Range.Start.Line);
        Assert.StartsWith("Section A · ⚠", lenses[0].Command!.Title);
        Assert.Equal(6, lenses[1].Range.Start.Line);   // `chords prog`
        Assert.Equal("⚠ Section A · 2 bars here (1 bar shorter) · 3 bars in melody and X", lenses[1].Command!.Title);
        Assert.Equal("lilysharp.showSectionLayers", lenses[1].Command!.CommandIdentifier);

        // A later section-major declaration (a section is open: it may gather parts from
        // several declarations): still over the block, not the declaration's name.
        const string twice = """
            part flute
            part oboe
            part horn
            section A { flute { c1 | c1 | } oboe { c1 | c1 | } }
            section A { horn { c1 | } }
            form main { A }
            score main { staff flute  staff oboe  staff horn }
            """;
        var later = LensesOf(twice);
        Assert.Equal(2, later.Length);
        Assert.Equal(4, later[1].Range.Start.Line);
        Assert.Equal(12, later[1].Range.Start.Character);   // on `horn`, not on `A`
        Assert.Equal("⚠ Section A · 1 bar here (1 bar shorter) · 2 bars in flute and oboe", later[1].Command!.Title);
    }

    [Fact]
    public void TheOddOneOut_IsMeasuredAgainstTheMajority_NotTheLongest()
    {
        // Three parts at 2 bars and one at 3: the one is most likely a bar written twice, so it
        // — not the three — carries the line, though the page lays A out at 3.
        const string text = """
            part flute { section A { c1 | c1 | } }
            part oboe { section A { c1 | c1 | } }
            part horn { section A { c1 | c1 | c1 | } }
            part viola { section A { c1 | c1 | } }
            form main { A }
            score main { staff flute  staff oboe  staff horn  staff viola }
            """;
        var lenses = LensesOf(text);
        Assert.Equal(2, lenses.Length);
        Assert.Equal(0, lenses[0].Range.Start.Line);
        Assert.Equal(2, lenses[1].Range.Start.Line);
        Assert.Equal("⚠ Section A · 3 bars here (1 bar longer) · 2 bars in flute, oboe and viola", lenses[1].Command!.Title);
    }

    [Fact]
    public void OnATie_TheShorterIsTheOddOne()
    {
        const string text = """
            part flute { section A { c1 | c1 | c1 | } }
            part oboe { section A { c1 | c1 | } }
            form main { A }
            score main { staff flute  staff oboe }
            """;
        var lenses = LensesOf(text);
        Assert.Equal(2, lenses.Length);
        Assert.Equal("⚠ Section A · 2 bars here (1 bar shorter) · 3 bars in flute", lenses[1].Command!.Title);

        // The first declaration being the short one needs no second line: its own says it all.
        const string swapped = """
            part oboe { section A { c1 | c1 | } }
            part flute { section A { c1 | c1 | c1 | } }
            form main { A }
            score main { staff flute  staff oboe }
            """;
        Assert.Single(LensesOf(swapped));
    }

    [Fact]
    public void ManyWriters_AreCountedByKind_AndAMismatchNamesTheFewAgainstTheOthers()
    {
        const string agreeing = """
            part oboe { section S { c1 | } }
            part horn { section S { c1 | } }
            part viola { section S { c1 | } }
            chords h { section S { C | } }
            form main { S }
            score main { chords h  staff oboe  staff horn  staff viola }
            """;
        Assert.Equal("Section S · 1 bar · 3 parts, 1 chord row · 1× in form main", LensesOf(agreeing).First().Command!.Title);

        const string mismatched = """
            part flute { section S { c1 | c1 | } }
            part oboe { section S { c1 | } }
            part horn { section S { c1 | } }
            part viola { section S { c1 | } }
            chords h { section S { C | } }
            form main { S }
            score main { chords h  staff flute  staff oboe  staff horn  staff viola }
            """;
        Assert.Equal("Section S · ⚠ 2 bars in flute, 1 bar in the other 4 · 1× in form main",
            LensesOf(mismatched).First().Command!.Title);
    }

    [Fact]
    public void ADisagreeingSection_SaysEachLengthWithWhoWritesIt()
    {
        const string text = """
            part flute { section A { c1 | c1 | c1 | } }
            part oboe { section A { c1 | c1 | } }
            part horn { section A { c1 | c1 | } }
            form main { A }
            score main { staff flute  staff oboe  staff horn }
            """;
        var title = LensesOf(text).First().Command!.Title;
        Assert.Equal("Section A · ⚠ 3 bars in flute, 2 bars in oboe and horn · 1× in form main", title);
    }

    [Fact]
    public void ASectionNoFormNames_SaysSo()
    {
        const string text = """
            part flute { section A { c1 | } section Unused { c1 | } }
            form main { A }
            score main { staff flute }
            """;
        Assert.Contains(LensesOf(text), l => l.Command!.Title == "Section Unused · 1 bar · flute · in no form");
    }

    [Fact]
    public void TheClick_ListsEveryLayer()
    {
        const string text = """
            part flute { section A { c1 | } }
            part oboe { section A { c1 | } }
            lyrics words sings flute { section A { la | } }
            form main { A }
            score main { staff flute  lyrics words  staff oboe }
            """;
        var lens = LensesOf(text).First();
        Assert.Equal("lilysharp.showSectionLayers", lens.Command!.CommandIdentifier);
        var locations = Assert.IsType<Location[]>(lens.Command.Arguments![2]);
        Assert.Equal(3, locations.Length);   // flute, oboe and the lyrics cell
        Assert.Equal(new[] { 0, 1, 2 }, locations.Select(l => l.Range.Start.Line).ToArray());
    }

    [Fact]
    public void TheOverview_ReadsBothLayouts()
    {
        const string sectionMajor = """
            part flute
            part oboe
            section A { flute { c1 | c1 | } oboe { c1 | } lyrics w sings flute { la | lu | } }
            form main { |: A :| }
            score main { staff flute  staff oboe }
            """;
        var a = Assert.Single(SectionOverview.Build(SyntaxTree.Parse(sectionMajor).GetRoot()));
        Assert.Equal(new[] { 2, 1, 2 }, a.Layers.Select(l => l.Bars).ToArray());
        Assert.True(a.IsInconsistent);
        Assert.Equal(2, a.Bars);
        Assert.Equal(("main", 1), Assert.Single(a.FormReferences));   // `|: A :|` is written once
    }
}
