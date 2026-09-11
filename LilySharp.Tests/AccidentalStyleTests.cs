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
using LilySharp.Core.LilyPond;
using LilySharp.Core.Parser;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// <c>layout { accidentals … }</c> — LilyPond's <c>\accidentalStyle</c> table for the
/// styles whose context is the staff (user decision 2026-09-11).
/// </summary>
/// <remarks>
/// <para>
/// The claims are made on the ACCIDENTAL each note carries, read off the collected model,
/// because that is what the style decides; the glyph's placement is not this switch's
/// business. The books are deliberately tiny and in C major, so every printed accidental
/// is one the style asked for.
/// </para>
/// <para>
/// ⚠️ The first net is BYTE IDENTITY for <c>default</c>: it is the style Lily# has always
/// drawn, so writing it must change nothing at all — 599 tracked books and 249 snapshots
/// depend on that being true rather than nearly true.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class AccidentalStyleTests
{
    private static readonly SvgRenderOptions Opt = new() { EmbedFont = false };

    /// <summary>A one-part book in C major whose music the caller writes.</summary>
    private static string Book(string music, string style = "")
        => (style.Length > 0 ? $"layout {{ accidentals {style} }}\n" : "")
        + "octave absolute\ntime 4/4\nkey c major\n"
        + "part m { clef treble }\n"
        + "section A { m { " + music + " } }\n"
        + "form main { A }\nscore main { staff m }\n";

    /// <summary>Every NOTE of the book, in order, as (accidental glyph, parenthesised) —
    /// rests are not notes and are not counted.</summary>
    private static (string? Acc, bool Courtesy)[] Notes(string music, string style = "")
    {
        var tree = SyntaxTree.Parse(Book(music, style));
        Assert.False(tree.HasErrors, string.Join(" | ", tree.Diagnostics.Select(d => d.Message)));
        Assert.Empty(SemanticValidation.Run(tree).Where(d => d.Severity == DiagnosticSeverity.Error));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        var found = new List<(string?, bool)>();
        foreach (var measure in score.StaffGroups[0].Staves[0].Voices[0].Measures)
            foreach (var item in measure.Items)
                switch (item)
                {
                    case NoteItem n: found.Add((n.Accidental, n.IsCourtesy)); break;
                    case ChordItem c:
                        foreach (var m in c.Notes) found.Add((m.Accidental, m.IsCourtesy));
                        break;
                }
        return [.. found];
    }

    private static string?[] Glyphs(string music, string style = "")
        => [.. Notes(music, style).Select(n => n.Acc)];

    // ================================================================================
    // The words
    // ================================================================================

    [Fact]
    public void TheWords_AreTheCompilers_TheDefaultFirst()
    {
        Assert.Equal(new[] { "default", "modern", "modernCautionary", "forget", "noReset" },
            AccidentalStyles.Words);
        Assert.Equal(AccidentalStyles.Words, LanguageVocabulary.AccidentalStyleWords);
        // The plan's default IS the default style, so a book that states it states the default.
        Assert.Same(AccidentalStyles.Default, LayoutPlan.Default.AccidentalStyle);
        // …and none of the words is reserved: a part may be called any of them.
        foreach (string word in AccidentalStyles.Words)
            Assert.Equal(SyntaxKind.Identifier, new Lexer(word).ScanAllTokens().First().Kind);
    }

    [Fact]
    public void EachWordReadsIntoThePlan_AndAnUnknownOneIsRefusedAtTheWord()
    {
        foreach (var style in AccidentalStyles.All)
        {
            var tree = SyntaxTree.Parse(Book("c4 d e f |", style.Word));
            Assert.Empty(SemanticValidation.Run(tree).Where(d => d.Severity == DiagnosticSeverity.Error));
            Assert.Same(style, LayoutPlanReader.Resolve(tree.GetRoot(), null).AccidentalStyle);
        }

        string bad = Book("c4 d e f |", "romantic");
        var d = Assert.Single(SemanticValidation.Run(SyntaxTree.Parse(bad)),
            x => x.Severity == DiagnosticSeverity.Error);
        Assert.Equal(DiagnosticCodes.LayoutEntryBadValue, d.Code);
        Assert.Contains("romantic", d.Message, StringComparison.Ordinal);
        Assert.Contains("modernCautionary", d.Message, StringComparison.Ordinal);
        int at = bad.IndexOf("romantic", StringComparison.Ordinal);
        Assert.True(d.Span.Start <= at && at < d.Span.Start + d.Span.Length,
            "the error should stand on the word");
    }

    // ================================================================================
    // default — the style Lily# always had
    // ================================================================================

    [Fact]
    public void WritingTheDefault_IsTheSamePageAsWritingNothing()
    {
        const string music = "cis4 c d dis | e eis f fis |";
        string bare = SvgGenerator.Generate(SyntaxTree.Parse(Book(music)), Opt);
        string stated = SvgGenerator.Generate(SyntaxTree.Parse(Book(music, "default")), Opt);
        Assert.Equal(MaskDataPos(bare), MaskDataPos(stated));
    }

    [Fact]
    public void Default_RemembersToTheBarLine_InItsOwnOctaveOnly()
    {
        // Bar 1: cis prints, the second cis does not (remembered), c prints the cancelling
        // natural, the fourth note needs its sharp again. Bar 2: the SAME cis prints once
        // more — the bar line forgot it.
        Assert.Equal(new string?[] { "sharp", null, "natural", "sharp", "sharp" },
            Glyphs("cis4 cis c cis | cis4 r r r |"));

        // Another OCTAVE is a different memory under this style: cis' does not cancel cis''.
        Assert.Equal(new string?[] { "sharp", "sharp", null },
            Glyphs("cis'4 cis''4 cis'' r |"));
    }

    [Fact]
    public void Default_PrependsTheRestoreNatural_SteppingDownInsideOneSign()
    {
        // 𝄪 → ♯ in the same measure: LilyPond's extraNatural prints the natural first.
        // LILYPOND-REF: scm/music-functions.scm check-pitch-against-signature — its
        // need-restore at :1746-1752.
        Assert.Equal(new string?[] { "doubleSharp", "naturalSharp" }, Glyphs("cisis4 cis r2 |"));
    }

    // ================================================================================
    // modern — Kurt Stone's
    // ================================================================================

    [Fact]
    public void Modern_CancelsInTheNextMeasure()
    {
        // same-octave 1: the sharp is still remembered one bar line later, so the plain c
        // of bar 2 needs a cancelling natural. Under `default` it needs nothing.
        Assert.Equal(new string?[] { "sharp", "natural" }, Glyphs("cis4 r2. | c4 r2. |", "modern"));
        Assert.Equal(new string?[] { "sharp", null }, Glyphs("cis4 r2. | c4 r2. |"));
    }

    [Fact]
    public void Modern_CancelsInOtherOctaves()
    {
        // any-octave 0: cis' makes the c'' of the same bar carry a natural.
        Assert.Equal(new string?[] { "sharp", "natural" }, Glyphs("cis'4 c''4 r2 |", "modern"));
        // …which the default style does not do.
        Assert.Equal(new string?[] { "sharp", null }, Glyphs("cis'4 c''4 r2 |"));
    }

    [Fact]
    public void Modern_DropsTheRestoreNatural()
    {
        // extraNatural is #f for this style, so 𝄪 → ♯ prints a plain sharp.
        // LILYPOND-REF: scm/music-functions.scm accidental-styles — (modern #f …) at :1920-1924.
        Assert.Equal(new string?[] { "doubleSharp", "sharp" }, Glyphs("cisis4 cis r2 |", "modern"));
    }

    // ================================================================================
    // modernCautionary, forget, noReset
    // ================================================================================

    [Fact]
    public void ModernCautionary_ParenthesisesTheOnesModernAdds_AndNotTheOthers()
    {
        // The next-measure cancellation is an autoCautionary here, so it is parenthesised;
        // the ordinary same-octave sharp of bar 1 is not.
        var notes = Notes("cis4 r2. | c4 r2. |", "modernCautionary");
        Assert.Equal(2, notes.Length);
        Assert.Equal(("sharp", false), notes[0]);
        Assert.Equal(("natural", true), notes[1]);
        // The same book under `modern` prints the same glyphs, none of them parenthesised.
        Assert.All(Notes("cis4 r2. | c4 r2. |", "modern"), n => Assert.False(n.Courtesy));
    }

    [Fact]
    public void Forget_PrintsEveryAlteredNoteAgain()
    {
        // laziness -1: nothing is remembered, so the second cis prints too — and a plain c
        // after it prints nothing, because the key signature is all that is ever consulted.
        Assert.Equal(new string?[] { "sharp", "sharp", null }, Glyphs("cis4 cis c r |", "forget"));
    }

    [Fact]
    public void NoReset_KeepsAnAlterationAcrossBarLines()
    {
        // laziness #t: the sharp holds into bar 2 (the second cis prints nothing), and the
        // plain c of bar 3 still needs the cancelling natural.
        Assert.Equal(new string?[] { "sharp", null, "natural" },
            Glyphs("cis4 r2. | cis4 r2. | c4 r2. |", "noReset"));
        // …where the default style has forgotten it twice over.
        Assert.Equal(new string?[] { "sharp", "sharp", null },
            Glyphs("cis4 r2. | cis4 r2. | c4 r2. |"));
    }

    // ================================================================================
    // The readers
    // ================================================================================

    [Fact]
    public void TheTwin_WritesLilyPondsOwnWord_AndNothingForTheDefault()
    {
        foreach (var style in AccidentalStyles.All)
        {
            string ly = new LilyPondExporter().Export(SyntaxTree.Parse(Book("c4 d e f |", style.Word)));
            if (style == AccidentalStyles.Default)
                Assert.DoesNotContain("\\accidentalStyle", ly, StringComparison.Ordinal);
            else
                Assert.Contains("\\accidentalStyle " + style.LilyPondName, ly, StringComparison.Ordinal);
        }
        // A book that writes no style is the twin it always was.
        Assert.Equal(
            new LilyPondExporter().Export(SyntaxTree.Parse(Book("c4 d e f |"))),
            new LilyPondExporter().Export(SyntaxTree.Parse(Book("c4 d e f |", "default"))));
    }

    [Fact]
    public void AStyleEdit_RendersIdenticalToFullRecompile_BothWays()
    {
        // The style lives outside every per-measure reuse key, so the session sheds its
        // caches when it changes — the paper guard's shape (PaperEditIncrementalTests).
        // ⚠️ It is also the one layout switch a LONG memory makes the resume gate refuse
        // (MeasureCollector.WalkCarriesNothing asks whether the accidental memory is
        // empty), so this covers both directions of that too.
        string plain = Book("cis4 r2. | c4 r2. |");
        string modern = Book("cis4 r2. | c4 r2. |", "modern");
        var compiler = new IncrementalCompiler(SyntaxTree.Parse(plain), Opt);
        compiler.Render();
        foreach (var (text, label) in new[] { (modern, "to modern"), (plain, "back to default") })
        {
            string incremental = compiler.RenderIncremental(SyntaxTree.Parse(text));
            string full = SvgGenerator.Generate(SyntaxTree.Parse(text), Opt);
            Assert.True(full == incremental, $"{label}: incremental != full");
        }
    }

    [Fact]
    public void TheStyleTable_SaysWhichStylesForgetAtTheBarLine()
    {
        // The representation choice the collector leans on: a style whose every laziness is
        // 0 or -1 may have its memory CLEARED at the bar line, which is what keeps the
        // resume gate satisfiable for the default book.
        Assert.True(AccidentalStyles.Default.ForgetsAtBar);
        Assert.True(AccidentalStyles.Forget.ForgetsAtBar);
        Assert.False(AccidentalStyles.Modern.ForgetsAtBar);
        Assert.False(AccidentalStyles.ModernCautionary.ForgetsAtBar);
        Assert.False(AccidentalStyles.NoReset.ForgetsAtBar);
    }

    private static string MaskDataPos(string svg)
        => System.Text.RegularExpressions.Regex.Replace(svg, @"data-pos=""\d+""", "data-pos=\"\"");
}
