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
using Xunit;
using LilySharp.Core.Rendering;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;

namespace LilySharp.Tests;

/// <summary>
/// The <c>font</c> directive: what each kind of text is drawn in.
/// </summary>
/// <remarks>
/// The block-form cases assert the RULE rather than the spelling of any one page — which
/// face a role resolves to, in what order the bindings win, and which roles the broad
/// binding is allowed to reach. The end-to-end ones read the emitted
/// <c>font-family</c> attributes because that is where the answer becomes observable to
/// a reader.
/// <para>
/// ⚠️ The RESERVATION is not asserted here, and that is not an oversight: the layout
/// measures the bundled family whatever face a score names, so a bound face has no
/// number to be checked against. HANDOFF §2F holds that gap, with its measurement.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class FontDirectiveTests
{
    [Fact]
    public void FontDirective_WithEmbedded_ParsesNameAndEmbeddedFlag()
    {
        var tree = SyntaxTree.Parse("font \"meiryo\" embedded");

        var fonts = tree.GetRoot().DescendantNodes().OfType<FontDeclarationSyntax>().ToList();
        Assert.Single(fonts);
        Assert.Equal("meiryo", fonts[0].FontName);
        Assert.True(fonts[0].Embedded);
        Assert.False(tree.HasErrors);
    }

    [Fact]
    public void FontDirective_WithoutEmbedded_ParsesNameAndNoEmbeddedFlag()
    {
        var tree = SyntaxTree.Parse("font \"Noto Serif CJK JP\"");

        var fonts = tree.GetRoot().DescendantNodes().OfType<FontDeclarationSyntax>().ToList();
        Assert.Single(fonts);
        Assert.Equal("Noto Serif CJK JP", fonts[0].FontName);
        Assert.False(fonts[0].Embedded);
        Assert.False(tree.HasErrors);
    }

    [Fact]
    public void FontDirective_InFullDocumentHeader_ParsesClean()
    {
        var source =
            "font \"meiryo\" embedded\n" +
            "time 4/4\n" +
            "part m { clef treble section A { c4 d e f | } }\n" +
            "form main { A }\n" +
            "score main { staff m }";
        var tree = SyntaxTree.Parse(source);

        var fonts = tree.GetRoot().DescendantNodes().OfType<FontDeclarationSyntax>().ToList();
        Assert.Single(fonts);
        Assert.Equal("meiryo", fonts[0].FontName);
        Assert.True(fonts[0].Embedded);
        Assert.False(tree.HasErrors);
    }

    // ================================================================================
    // The block form: font { KEY VALUE… }
    // ================================================================================

    private static string Svg(string source) =>
        SvgGenerator.Generate(SyntaxTree.Parse(source), new SvgRenderOptions { EmbedFont = false });

    /// <summary>Every <c>font-family</c> attribute on a non-music text element.</summary>
    /// <remarks>
    /// A role resolving to the BUNDLED serif emits no attribute at all (the document root
    /// already names it), so an empty result means "everything took the default" rather
    /// than "nothing was drawn" — which is why the cases below also assert that the page
    /// holds the text they are about.
    /// </remarks>
    private static string[] Families(string svg) =>
        [.. Regex.Matches(svg, "<text(?![^>]*class=\"music\")[^>]*font-family=\"([^\"]*)\"")
                 .Select(m => m.Groups[1].Value)];

    // A book carrying one of everything the cases below name.
    private const string Book = """
        title "T"
        composer "C"
        time 4/4
        section Main {
          chords prog { c2 a:m | }
          melody { c'4 d e f | }
          lyrics words { la la la la | }
        }
        form main { Main }
        score main { chords prog  staff melody  lyrics words }
        """;

    // ---- the resolution rule ------------------------------------------------------

    [Fact]
    public void ALeafBindingBeatsItsGroup_WhicheverOrderTheyAreWritten()
    {
        // The whole reason groups can sit beside leaves: the narrower spelling wins, and
        // it wins by RULE and not by source order. Under a "last one wins" reading the
        // first of these two would give the group the tempo.
        var groupFirst = new TextFontPlan.Builder()
            .Group(TextRoleGroup.Marks, ["G"]).Role(TextRole.Tempo, ["L"]).Build();
        var leafFirst = new TextFontPlan.Builder()
            .Role(TextRole.Tempo, ["L"]).Group(TextRoleGroup.Marks, ["G"]).Build();

        Assert.Equal("L", groupFirst.Resolve(TextRole.Tempo).FamilyAttribute);
        Assert.Equal("L", leafFirst.Resolve(TextRole.Tempo).FamilyAttribute);
        // ...and the group still reaches its other members.
        Assert.Equal("G", groupFirst.Resolve(TextRole.Dynamics).FamilyAttribute);
        Assert.Equal("G", leafFirst.Resolve(TextRole.Dynamics).FamilyAttribute);
    }

    [Fact]
    public void AGroupBindingBeatsTheGenericFamily()
    {
        var plan = new TextFontPlan.Builder()
            .Family(TextFontFamily.Serif, ["F"])
            .Group(TextRoleGroup.Lyrics, ["G"])
            .Build();
        Assert.Equal("G", plan.Resolve(TextRole.LyricText).FamilyAttribute);
        Assert.Equal("G", plan.Resolve(TextRole.Stanza).FamilyAttribute);
        // A serif role in no bound group still takes the family.
        Assert.Equal("F", plan.Resolve(TextRole.Title).FamilyAttribute);
    }

    [Fact]
    public void ARedirectMovesTheMeasuredFamilyWithoutNamingAFace()
    {
        // `chordName serif` is the one way a score can say "reserve this against the
        // OTHER bundled face". It must move Family — what the layout measures — and must
        // not invent a face name.
        var plan = new TextFontPlan.Builder().Role(TextRole.ChordName, TextFontFamily.Serif).Build();
        var face = plan.Resolve(TextRole.ChordName);
        Assert.True(face.IsBundled);
        Assert.Equal(TextFontFamily.Serif, face.Family);
        // Untouched, that role is the engine's one sans — LilyPond's default for it too.
        Assert.Equal(TextFontFamily.Sans, TextFontPlan.Default.Resolve(TextRole.ChordName).Family);
    }

    [Fact]
    public void ARedirectThenFollowsWhateverThatFamilyWasBoundTo()
    {
        // The two layers compose: point a role at serif, bind serif to a face, and the
        // role DRAWS that face while still MEASURING the bundled serif.
        var plan = new TextFontPlan.Builder()
            .Family(TextFontFamily.Serif, ["Georgia"])
            .Role(TextRole.ChordName, TextFontFamily.Serif)
            .Build();
        var face = plan.Resolve(TextRole.ChordName);
        Assert.Equal("Georgia", face.FamilyAttribute);
        Assert.Equal(TextFontFamily.Serif, face.Family);
    }

    [Fact]
    public void TheSystemBraceIsNeverBound()
    {
        // It is in the enum so that no draw site has to pass a family beside its role,
        // not so that a score can ask for the brace in Georgia.
        var plan = new TextFontPlan.Builder().Everything(["Georgia"])
            .Group(TextRoleGroup.Notation, ["Georgia"]).Build();
        Assert.Equal(TextFontPlan.BraceFaceName, plan.Resolve(TextRole.SystemBrace).FamilyAttribute);
    }

    [Fact]
    public void TwoDifferentlyOrderedButEquallyBindingDirectivesAreOnePlan()
    {
        // Equality is what the incremental collector compares a re-read header against,
        // and what the SVG fragment memo declines on; a reference comparison would call
        // every keystroke a change.
        var a = new TextFontPlan.Builder()
            .Family(TextFontFamily.Serif, ["A"]).Role(TextRole.Title, ["B"]).Build();
        var b = new TextFontPlan.Builder()
            .Role(TextRole.Title, ["B"]).Family(TextFontFamily.Serif, ["A"]).Build();
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, new TextFontPlan.Builder()
            .Family(TextFontFamily.Serif, ["A"]).Role(TextRole.Title, ["C"]).Build());
    }

    // ---- notation is outside the broad binding ------------------------------------

    [Fact]
    public void TheBroadBindingDoesNotReachNotationDrawnAsText()
    {
        // DECIDED 2026-08-18: `font "NAME"` says what the score's PROSE is set in, and a
        // tab fret number is not prose. Before this, `font "Comic Sans"` reached all three.
        var plan = new TextFontPlan.Builder().Everything(["Georgia"]).Build();
        foreach (var role in new[] { TextRole.TabFret, TextRole.ClefOctave, TextRole.Meter })
            Assert.True(plan.Resolve(role).IsBundled,
                $"{TextRoles.Spelling(role)} followed the broad binding");
        // ...while ordinary text does follow it.
        Assert.Equal("Georgia", plan.Resolve(TextRole.Title).FamilyAttribute);
    }

    [Fact]
    public void NamingNotationReachesThemOutLoud()
    {
        var group = new TextFontPlan.Builder().Group(TextRoleGroup.Notation, ["Georgia"]).Build();
        Assert.Equal("Georgia", group.Resolve(TextRole.TabFret).FamilyAttribute);
        Assert.Equal("Georgia", group.Resolve(TextRole.ClefOctave).FamilyAttribute);
        // A leaf on its own reaches exactly that one.
        var leaf = new TextFontPlan.Builder().Role(TextRole.TabFret, ["Georgia"]).Build();
        Assert.Equal("Georgia", leaf.Resolve(TextRole.TabFret).FamilyAttribute);
        Assert.True(leaf.Resolve(TextRole.ClefOctave).IsBundled);
    }

    /// <summary>A guitar book: staff plus tab, so the page really holds fret digits.</summary>
    private const string TabBook = """
        title "T"
        part g { clef treble_8 tuning guitar }
        section A { c'4 d e f | }
        score main { staff g  tab g }
        form main { A }
        """;

    [Fact]
    public void APlainFontDirectiveLeavesTabFretNumbersInTheBundledFace()
    {
        string svg = Svg("font \"Georgia\"\n" + TabBook);
        // There really are fret digits on this page to have been left alone.
        Assert.Contains(">8</text>", svg, StringComparison.Ordinal);
        // The bundled face emits no attribute, so every attribute present is the title's.
        var families = Families(svg);
        Assert.NotEmpty(families);
        Assert.All(families, f => Assert.Equal("Georgia", f));
        // Nailed down: the digit's own element carries no family.
        Assert.Matches("<text[^>]*>8</text>", Regex.Replace(svg, "\\s+", " "));
        Assert.DoesNotMatch("<text[^>]*font-family=\"Georgia\"[^>]*>8</text>",
            Regex.Replace(svg, "\\s+", " "));
    }

    [Fact]
    public void NamingNotationPutsTheFretDigitsInThatFace()
    {
        string svg = Regex.Replace(
            Svg("font { serif \"Georgia\"  notation \"Georgia\" }\n" + TabBook), "\\s+", " ");
        Assert.Matches("<text[^>]*font-family=\"Georgia\"[^>]*>8</text>", svg);
    }

    // ---- end to end ---------------------------------------------------------------

    [Fact]
    public void EachRoleReachesThePageInTheFaceItWasBoundTo()
    {
        string svg = Svg("""
            font {
              serif  "Georgia"
              lyricText "Charis SIL"
              title  "Cormorant"
            }
            """ + "\n" + Book);
        Assert.Contains("font-family=\"Cormorant\"", svg, StringComparison.Ordinal);
        Assert.Contains("font-family=\"Charis SIL\"", svg, StringComparison.Ordinal);
        // The composer took the generic family, having no binding of its own.
        Assert.Contains("font-family=\"Georgia\"", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void AChainReachesThePageAsAFallbackList()
    {
        // What a chain is FOR: a Latin face for the words and a CJK face for the
        // syllables it has no glyph for, walked per glyph by the viewer.
        string svg = Svg("font { lyricText \"Charis SIL\" \"Noto Serif CJK JP\" }\n" + Book);
        Assert.Contains("font-family=\"Charis SIL, Noto Serif CJK JP\"", svg,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheOneLinerStillMeansEveryGenericFamily()
    {
        // `font "NAME"` is not deprecated and its meaning is unchanged (bar notation):
        // BOTH bundled families, so chord symbols move with the prose.
        var families = Families(Svg("font \"Georgia\"\n" + Book));
        Assert.NotEmpty(families);
        Assert.All(families, f => Assert.Equal("Georgia", f));
    }

    // ---- diagnostics ---------------------------------------------------------------

    private static Diagnostic[] Check(string source)
    {
        var tree = SyntaxTree.Parse(source);
        return [.. tree.Diagnostics, .. SemanticValidation.Run(tree)];
    }

    [Fact]
    public void AnUnknownKeyIsRefused()
    {
        // Refused rather than ignored: a binding that reaches nothing looks exactly like
        // one that works — the page simply comes out in the bundled face.
        var err = Assert.Single(Check("font { lyrix \"Charis SIL\" }\n" + Book),
            x => x.Code == DiagnosticCodes.UnknownFontRole);
        // The message names the whole vocabulary, so the fix is one read.
        Assert.Contains("lyricText", err.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AKeyBoundTwiceInOneBlockIsAWarningAndTheLastOneWins()
    {
        Assert.Single(Check("font { lyricText \"A\"  lyricText \"B\" }\n" + Book),
            x => x.Code == DiagnosticCodes.DuplicateFontBinding);
        string svg = Svg("font { lyricText \"A\"  lyricText \"B\" }\n" + Book);
        Assert.Contains("font-family=\"B\"", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("font-family=\"A\"", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void AKeyWithNoFaceIsRefused()
        => Assert.Single(Check("font { lyricText }\n" + Book),
            x => x.Code == DiagnosticCodes.FontBindingMissingValue);

    [Fact]
    public void MonoIsNotAKey_BecauseNoRoleWouldReadIt()
        // Better to be told the word does not exist than to write a binding that reaches
        // nothing: this engine draws no monospace text.
        => Assert.Single(Check("font { mono \"Consolas\" }\n" + Book),
            x => x.Code == DiagnosticCodes.UnknownFontRole);

    [Fact]
    public void ARefusedEntryDoesNotTakeTheRestOfTheBlockWithIt()
    {
        // One bad key must not cost the score the bindings it spelled correctly.
        string svg = Svg("font { lyrix \"X\"  title \"Cormorant\" }\n" + Book);
        Assert.Contains("font-family=\"Cormorant\"", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRoleVocabularyReservesNoWords()
    {
        // A key is read inside `font { }` only, against the role vocabulary — never by the
        // lexer — so adding twenty-four role names must not cost a score twenty-four
        // identifiers. `serif`, `header` and `chordName` are ordinary names everywhere else.
        var d = Check("""
            font { serif "Georgia" }
            part serif { clef treble }
            part header { clef bass }
            phrase chordName { c'4 d e f | }
            section A { serif { $chordName } header { c1 | } }
            score main { staff serif  staff header }
            form main { A }
            """);
        Assert.Empty(d.Where(x => x.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void EveryFaceAnEmbeddedBlockNamesIsLicenceChecked()
    {
        // The embed check read FontName — the FIRST name — which cleared a block whose
        // SECOND face was the restricted one.
        var notFound = Check(
            "font { title \"ZzNoSuchFontA\"  lyricText \"ZzNoSuchFontB\" embedded }\n" + Book)
            .Where(x => x.Code == DiagnosticCodes.FontNotFound).ToList();
        Assert.Equal(2, notFound.Count);
    }
}
