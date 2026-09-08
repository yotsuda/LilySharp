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
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using LilySharp.Core.LilyPond;
using LilySharp.Core.Rendering;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using LilySharp.Lsp;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The <c>fonts { }</c> entry's SIZE and STYLE attributes (2026-09-08): <c>step</c>,
/// <c>size</c>, <c>bold</c> / <c>italic</c> / <c>regular</c>, and the <c>as FAMILY</c>
/// spelling of the redirect. Owner-approved grammar, HANDOFF §2F F-fonts (session 342).
/// </summary>
/// <remarks>
/// Three claims, three nets: the READER (what an entry means and what it refuses), the
/// PAGE (<see cref="TheReachTable_IsWhatThePageDoes"/> — the one test that holds
/// <see cref="TextRoles.PlanReachOf"/> to the drawn em, in both directions), and the TWIN
/// (a step is LilyPond's font-size and is written as one).
/// </remarks>
[Trait("Category", "Unit")]
public class FontAttributeTests
{
    // ---- helpers -----------------------------------------------------------------

    private static string Svg(string source) =>
        SvgGenerator.Generate(SyntaxTree.Parse(source), new SvgRenderOptions { EmbedFont = false });

    private static Diagnostic[] Check(string source)
    {
        var tree = SyntaxTree.Parse(source);
        return [.. tree.Diagnostics, .. SemanticValidation.Run(tree)];
    }

    /// <summary>The plan the first fonts block of <paramref name="source"/> reads to.</summary>
    private static TextFontPlan Plan(string source)
    {
        var font = SyntaxTree.Parse(source).GetRoot().DescendantNodes<FontDeclarationSyntax>().First();
        return FontPlanReader.Read(font, out _);
    }

    private static double Magstep(double step) => Math.Pow(2.0, step / 6.0);

    /// <summary>
    /// A book that draws one string of every role the plan reaches, each string unique on
    /// the page so its <c>&lt;text&gt;</c> element can be found by content alone: the volta
    /// "2.", the tuplet "5", the bar number "3" (the second system's first bar), the
    /// tempo equation "= 120".
    /// </summary>
    private const string Book = """
        title "Ttl"
        composer "Cmp"
        tempo 120
        time 4/4
        part melody "Vln." { pedal text }
        section A {
          chords prog { D | }
          melody { c'4@mf d@sostenuto e@mark("Q") f@!sostenuto | }
          lyrics words { lyr la la la | }
        }
        section B {
          chords prog { D | }
          melody { tuplet 5/4 { c'16 d e f g } d4 e f | break }
          lyrics words { la la la la | }
        }
        section C {
          chords prog { D | }
          melody { c''4@ottava d e f@!ottava | }
          lyrics words { la la la la | }
        }
        section Z {
          chords prog { D | }
          melody { fine c'4 d e f | }
          lyrics words { la la la la | }
        }
        form main { |: A [1. B] :| [2. C] Z _"rit." }
        score main { chords prog  staff melody  lyrics words }
        """;
    // (The last section is Z, not D: a section outside the repeat is labelled with its
    // name in a boxed mark, and a "D" label would be read as the chord symbol's "D".)

    /// <summary>A second book for the one role the main book cannot hold: a part-combine
    /// label ("a2") needs two parts on one combined staff.</summary>
    private const string CombineBook = """
        time 4/4
        part fl { clef treble }
        part ob { clef treble }
        section A {
          fl { c'4 d e f | g4 a b c' | }
          ob { c'4 d e f | e4 f g a | }
        }
        form main { A }
        score main { combinedStaff { fl ob } }
        """;

    /// <summary>A guitar book for the three tab-family labels the main book has no place
    /// for: a technique letter ("H"), a bend amount ("full") and a fret frame above the
    /// fourth fret ("5fr").</summary>
    private const string GuitarBook = """
        octave absolute
        time 4/4
        part gtr { clef treble }
        section S {
          gtr { c4@hammeron e@bend(full) g@frame(x57565) b | }
        }
        form main { S }
        score main { staff gtr }
        """;

    /// <summary>A third book for the stanza number: three verses, no volta — every stanza
    /// number ("1." "2." "3.") reads like a volta ending, so the two cannot share a page.</summary>
    private const string StanzaBook = """
        time 4/4
        section A {
          melody { c'4 d e f | }
          lyrics words { la la la la | }
          lyrics words { lo lo lo lo | }
          lyrics words { lu lu lu lu | }
        }
        form main { A }
        score main { staff melody  lyrics words }
        """;

    /// <summary>The page with its <c>data-pos</c> source offsets masked: a <c>fonts</c> line
    /// prepended to the book shifts every offset, which is not a change of the picture.</summary>
    private static string Mask(string svg) => Regex.Replace(svg, "\\s*data-pos=\"\\d+\"", "");

    /// <summary>The string each role draws in <see cref="Book"/>.</summary>
    private static readonly Dictionary<TextRole, string> Sample = new()
    {
        [TextRole.Title] = "Ttl",
        [TextRole.Composer] = "Cmp",
        [TextRole.Instrument] = "Vln.",
        [TextRole.LyricText] = "lyr",
        [TextRole.Stanza] = "3.",
        [TextRole.ChordName] = "D",
        [TextRole.Tempo] = "= 120",
        [TextRole.Mark] = "Q",
        [TextRole.Pedal] = "Sost. Ped.",
        [TextRole.Navigation] = "Fine",
        [TextRole.Text] = "rit.",
        [TextRole.Dynamics] = "mf",
        [TextRole.PartCombine] = "a2",
        [TextRole.BarNumber] = "3",
        [TextRole.Tuplet] = "5",
        [TextRole.Volta] = "2.",
        [TextRole.Ottava] = "8va",
        [TextRole.TabTechnique] = "H",
        [TextRole.Bend] = "full",
        [TextRole.FretFrame] = "5fr",
    };

    /// <summary>The book a role's sample is read from — <see cref="CombineBook"/> for the
    /// part-combine label, <see cref="StanzaBook"/> for the stanza number,
    /// <see cref="GuitarBook"/> for the tab-family labels, <see cref="Book"/> for everything
    /// else.</summary>
    private static string BookFor(TextRole role) => role switch
    {
        TextRole.PartCombine => CombineBook,
        TextRole.Stanza => StanzaBook,
        TextRole.TabTechnique or TextRole.Bend or TextRole.FretFrame => GuitarBook,
        _ => Book,
    };

    /// <summary>The attribute strings of every <c>&lt;text&gt;</c> whose content is
    /// <paramref name="sample"/>.</summary>
    private static string[] TextElements(string svg, string sample) =>
        [.. Regex.Matches(svg, "<text([^>]*)>" + Regex.Escape(sample) + "</text>")
                 .Select(m => m.Groups[1].Value)];

    private static double FontSizeOf(string attrs) =>
        double.Parse(Regex.Match(attrs, "font-size=\"([0-9.]+)\"").Groups[1].Value, CultureInfo.InvariantCulture);

    // ================================================================================
    // The reader
    // ================================================================================

    [Fact]
    public void AnEntryCarriesAFaceASizeAndAStyleTogether()
    {
        var plan = Plan("fonts { mark \"X\" step +1 bold }");
        Assert.Equal("X", plan.Resolve(TextRole.Mark).FamilyAttribute);
        Assert.Equal(2.0 * Magstep(1), plan.SizeOf(TextRole.Mark, 2.0), 12);
        Assert.Equal(FontStyle.Bold, plan.StyleOf(TextRole.Mark, FontStyle.Italic));
        // ...and says nothing about a role it did not name.
        Assert.Equal(2.0, plan.SizeOf(TextRole.Tempo, 2.0));
        Assert.Equal(FontStyle.Italic, plan.StyleOf(TextRole.Tempo, FontStyle.Italic));
    }

    [Fact]
    public void TheAttributesComeInAnyOrder()
    {
        var a = Plan("fonts { mark \"X\" step +1 bold }");
        var b = Plan("fonts { mark bold step +1 \"X\" }");
        Assert.Equal(a, b);
    }

    [Fact]
    public void TheRedirectIsSpelledWithAs()
    {
        // `chordName as serif` moves the measured family without naming a face — the
        // meaning `chordName serif` used to carry.
        var face = Plan("fonts { chordName as serif }").Resolve(TextRole.ChordName);
        Assert.True(face.IsBundled);
        Assert.Equal(TextFontFamily.Serif, face.Family);
    }

    [Fact]
    public void TheOldBareRedirectIsRefused_AndAnsweredWithTheAsForm()
    {
        // A bare word after a key is the NEXT KEY now, so `chordName serif` opens an empty
        // `serif` entry. The refusal names the spelling that replaced it.
        // Two refusals: the now-empty `chordName` entry, and the `serif` entry it left with no
        // face — the second carries the hint, since that is where the reader's eye lands.
        var all = Check("fonts { chordName serif }\n" + Book)
            .Where(x => x.Code == DiagnosticCodes.FontBindingMissingValue).ToList();
        Assert.Equal(2, all.Count);
        Assert.All(all, x => Assert.Equal(DiagnosticSeverity.Error, x.Severity));
        Assert.Contains(all, x => x.Message.Contains(
            "To point 'chordName' at the serif family write: chordName as serif", StringComparison.Ordinal));
    }

    [Fact]
    public void SizeAndStyleResolveNarrowerFirst_EachOnItsOwn()
    {
        // The group's step and the leaf's style compose: a small bold syllable.
        var plan = Plan("fonts { lyrics step -1  lyricText bold }");
        Assert.Equal(3.0 * Magstep(-1), plan.SizeOf(TextRole.LyricText, 3.0), 12);
        Assert.Equal(FontStyle.Bold, plan.StyleOf(TextRole.LyricText, FontStyle.Regular));
        // The stanza number takes the group's step and keeps the engraving's style.
        Assert.Equal(3.0 * Magstep(-1), plan.SizeOf(TextRole.Stanza, 3.0), 12);
        Assert.Equal(FontStyle.Regular, plan.StyleOf(TextRole.Stanza, FontStyle.Regular));
    }

    [Fact]
    public void ALeafSizeIsNotScaledByItsGroupStep()
    {
        var plan = Plan("fonts { lyrics step -1  lyricText size 3 }");
        Assert.Equal(3.0, plan.SizeOf(TextRole.LyricText, 2.2));
    }

    [Fact]
    public void AnAbsoluteSizeIsAStepForTheGlyphsThatKeepTheTextCompany()
    {
        // A chord symbol's accidental steps with the name; an absolute size has to say by
        // how many steps, which is the log of the ratio.
        var plan = Plan("fonts { chordName size 4.4 }");
        Assert.Equal(6.0, plan.StepOf(TextRole.ChordName, 2.2), 9);
        Assert.Equal(0.0, TextFontPlan.Default.StepOf(TextRole.ChordName, 2.2));
    }

    [Theory]
    [InlineData("bold italic", FontStyle.BoldItalic)]
    [InlineData("italic bold", FontStyle.BoldItalic)]
    [InlineData("bold regular", FontStyle.Regular)]
    [InlineData("regular bold", FontStyle.Bold)]
    [InlineData("regular", FontStyle.Regular)]
    public void StylesCombineAndRegularClears_TheLastWordDeciding(string words, FontStyle expected)
        => Assert.Equal(expected, Plan($"fonts {{ tempo {words} }}").StyleOf(TextRole.Tempo, FontStyle.BoldItalic));

    [Fact]
    public void AStepMayBeFractional()
        => Assert.Equal(2.0 * Magstep(1.5), Plan("fonts { mark step +1.5 }").SizeOf(TextRole.Mark, 2.0), 12);

    [Fact]
    public void TheSizeIsPartOfThePlansIdentity()
    {
        // The incremental collector and the fragment memo compare plans by signature; a
        // keystroke from `step +1` to `step +2` has to read as a change.
        Assert.NotEqual(Plan("fonts { mark step +1 }"), Plan("fonts { mark step +2 }"));
        Assert.NotEqual(Plan("fonts { mark bold }"), Plan("fonts { mark italic }"));
        Assert.Equal(Plan("fonts { mark step +1 }"), Plan("fonts { mark step 1 }"));
    }

    [Fact]
    public void ANoDirectiveBookReadsItsDefaultsBackExactly()
    {
        // Every reader of an em goes through Size(); a role with no size in the plan must
        // get the engraving's default ITSELF, not a computed copy — the 922-book sweep
        // that landed this relies on it.
        foreach (var role in TextRoles.All)
        {
            Assert.Equal(2.2, TextFontPlan.Default.SizeOf(role, 2.2));
            Assert.Equal(FontStyle.Bold, TextFontPlan.Default.StyleOf(role, FontStyle.Bold));
        }
    }

    // ================================================================================
    // Diagnostics
    // ================================================================================

    [Theory]
    [InlineData("fonts { serif step +1 }")]
    [InlineData("fonts { sans bold }")]
    [InlineData("fonts { step +1 }")]
    [InlineData("fonts { bold mark \"X\" }")]
    public void AnAttributeWhereNoRoleReadsIt_IsRefused(string block)
        => Assert.Contains(Check(block + "\n" + Book),
            x => x.Code == DiagnosticCodes.FontAttributeMisplaced && x.Severity == DiagnosticSeverity.Error);

    [Theory]
    [InlineData("fonts { mark step }")]
    [InlineData("fonts { mark step bold }")]
    [InlineData("fonts { mark step +13 }")]
    [InlineData("fonts { mark step -12.5 }")]
    [InlineData("fonts { mark size 30 }")]
    [InlineData("fonts { mark size 0.2 }")]
    public void AStepOrSizeWithoutANumberOrOutOfRange_IsRefused(string block)
        => Assert.Contains(Check(block + "\n" + Book),
            x => x.Code == DiagnosticCodes.FontSizeOutOfRange && x.Severity == DiagnosticSeverity.Error);

    [Fact]
    public void BothSizesInOneEntry_IsRefusedAndNeitherBinds()
    {
        const string block = "fonts { mark step +1 size 3 }";
        Assert.Single(Check(block + "\n" + Book), x => x.Code == DiagnosticCodes.FontSizeAndStepBothGiven);
        Assert.Equal(2.0, Plan(block).SizeOf(TextRole.Mark, 2.0));
    }

    [Theory]
    [InlineData("fonts { mark 3 }")]
    [InlineData("fonts { mark as }")]
    [InlineData("fonts { mark as Georgia }")]
    [InlineData("fonts { mark }")]
    public void AMalformedAttribute_IsRefused(string block)
        => Assert.Contains(Check(block + "\n" + Book),
            x => x.Code == DiagnosticCodes.FontBindingMissingValue && x.Severity == DiagnosticSeverity.Error);

    [Fact]
    public void ARepeatedAttributeInOneEntry_WarnsAndTheLastWins()
    {
        const string block = "fonts { mark step +1 step +2 }";
        var d = Assert.Single(Check(block + "\n" + Book), x => x.Code == DiagnosticCodes.DuplicateFontBinding);
        Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
        Assert.Equal(2.0 * Magstep(2), Plan(block).SizeOf(TextRole.Mark, 2.0), 12);
    }

    [Fact]
    public void AWellFormedEntry_IsSilent()
        => Assert.Empty(Check($"fonts {{ mark \"{TextFontMetrics.SerifFamily}\" step +1 bold  lyrics step -1  chordName as serif  tempo italic }}\n" + Book)
            .Where(x => x.Code.StartsWith("LYS80", StringComparison.Ordinal)));

    [Theory]
    [InlineData("fonts { fingering step +1 }")]
    [InlineData("fonts { tabFret bold }")]
    [InlineData("fonts { notation bold }")]      // no leaf of the group follows
    public void AnAttributeThePageWouldIgnore_Warns(string block)
    {
        var d = Assert.Single(Check(block + "\n" + Book), x => x.Code == DiagnosticCodes.FontAttributeNotFollowed);
        Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
    }

    [Theory]
    [InlineData("fonts { numbers step +1 }")]    // barNumber, tuplet, volta follow
    [InlineData("fonts { marks italic }")]
    [InlineData("fonts { barNumber step +1 }")]
    public void AnAttributeSomeRoleReads_DoesNotWarn(string block)
        => Assert.DoesNotContain(Check(block + "\n" + Book), x => x.Code == DiagnosticCodes.FontAttributeNotFollowed);

    [Fact]
    public void TheNotFollowedWarning_IsAboutTheTable()
    {
        // The validator and the page read ONE table (TextRoles.PlanReachOf); this pins the
        // validator's half to it so the two cannot drift.
        foreach (var role in TextRoles.All.Where(r => r != TextRole.SystemBrace))
        {
            bool warned = Check($"fonts {{ {TextRoles.Spelling(role)} step +1 }}\n" + Book)
                .Any(x => x.Code == DiagnosticCodes.FontAttributeNotFollowed);
            bool follows = (TextRoles.PlanReachOf(role) & PlanReach.Size) != 0;
            Assert.True(warned == !follows, $"{TextRoles.Spelling(role)}: warned={warned} follows={follows}");
        }
    }

    // ================================================================================
    // The page — the reach table held to the drawn em, in both directions
    // ================================================================================

    [Fact]
    public void TheBookDrawsEverySampleOnce()
    {
        // The theory below reads each role by its sample string; a sample that is absent or
        // ambiguous would make the theory pass for the wrong reason (RULES §5.4).
        foreach (var (role, sample) in Sample)
        {
            var found = TextElements(Svg(BookFor(role)), sample);
            Assert.True(found.Length >= 1, $"{TextRoles.Spelling(role)}: '{sample}' is not on the page");
        }
    }

    public static IEnumerable<object[]> EveryRole()
        => TextRoles.All.Where(r => r != TextRole.SystemBrace).Select(r => new object[] { r });

    [Theory]
    [MemberData(nameof(EveryRole))]
    public void TheReachTable_IsWhatThePageDoes(TextRole role)
    {
        // `step +6` is a doubling — the one factor every backend prints exactly.
        string book = BookFor(role);
        string control = Svg(book);
        string stepped = Svg($"fonts {{ {TextRoles.Spelling(role)} step +6 }}\n" + book);
        bool follows = (TextRoles.PlanReachOf(role) & PlanReach.Size) != 0;
        if (!follows)
        {
            // Not in the table: the page must not move AT ALL. A role that quietly did
            // would belong in the table (and its validator warning would be a lie).
            Assert.Equal(Mask(control), Mask(stepped));
            return;
        }
        Assert.True(Sample.TryGetValue(role, out var sample),
            $"{TextRoles.Spelling(role)} follows the plan but the book has no sample for it");
        var before = TextElements(control, sample).Select(FontSizeOf).ToArray();
        var after = TextElements(stepped, sample).Select(FontSizeOf).ToArray();
        Assert.NotEmpty(before);
        Assert.Equal(before.Length, after.Length);
        for (int i = 0; i < before.Length; i++)
            Assert.Equal(before[i] * 2.0, after[i], 1);   // the SVG prints two decimals
        Assert.NotEqual(Mask(control), Mask(stepped));
    }

    [Theory]
    [MemberData(nameof(EveryRole))]
    public void TheStyleReach_IsWhatThePageDoes(TextRole role)
    {
        string book = BookFor(role);
        string control = Svg(book);
        string styled = Svg($"fonts {{ {TextRoles.Spelling(role)} bold italic }}\n" + book);
        string plain = Svg($"fonts {{ {TextRoles.Spelling(role)} regular }}\n" + book);
        bool follows = (TextRoles.PlanReachOf(role) & PlanReach.Style) != 0;
        if (!follows)
        {
            Assert.Equal(Mask(control), Mask(styled));
            Assert.Equal(Mask(control), Mask(plain));
            return;
        }
        string sample = Sample[role];
        foreach (var attrs in TextElements(styled, sample))
        {
            Assert.Contains("font-weight=\"bold\"", attrs, StringComparison.Ordinal);
            Assert.Contains("font-style=\"italic\"", attrs, StringComparison.Ordinal);
        }
        foreach (var attrs in TextElements(plain, sample))
        {
            Assert.DoesNotContain("font-weight", attrs, StringComparison.Ordinal);
            Assert.DoesNotContain("font-style", attrs, StringComparison.Ordinal);
        }
        Assert.NotEmpty(TextElements(styled, sample));
    }

    [Fact]
    public void AGroupStepReachesItsRolesOnThePage()
    {
        string control = Svg(Book);
        string stepped = Svg("fonts { numbers step +6 }\n" + Book);
        foreach (var role in new[] { TextRole.BarNumber, TextRole.Tuplet, TextRole.Volta })
        {
            var before = TextElements(control, Sample[role]).Select(FontSizeOf).ToArray();
            var after = TextElements(stepped, Sample[role]).Select(FontSizeOf).ToArray();
            Assert.NotEmpty(before);
            for (int i = 0; i < before.Length; i++)
                Assert.Equal(before[i] * 2.0, after[i], 1);
        }
    }

    [Fact]
    public void TheReservationFollowsTheSize_NotOnlyTheInk()
    {
        // A doubled lyric em widens the columns the syllables stand in: the last bar line
        // of the first system moves right. The draw alone could not move a bar line.
        static double LastBarLineX(string svg)
        {
            // Bar lines are vertical <line> elements with x1 == x2; the rightmost one on the
            // first system is the widest x among them in the upper half of the page.
            var xs = Regex.Matches(svg, "<line x1=\"([0-9.]+)\" y1=\"([0-9.]+)\" x2=\"([0-9.]+)\" y2=\"([0-9.]+)\"")
                .Select(m => (X1: double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                              X2: double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture),
                              Y1: double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                              Y2: double.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture)))
                .Where(l => Math.Abs(l.X1 - l.X2) < 1e-6 && Math.Abs(l.Y2 - l.Y1) > 3.0)
                .Select(l => l.X1);
            return xs.Max();
        }
        const string wideLyrics = """
            section A { melody { c'4 d e f | } lyrics words { supercalifragilistic la la la | } }
            form main { A }
            score main { staff melody  lyrics words }
            """;
        double plain = LastBarLineX(Svg(wideLyrics));
        double big = LastBarLineX(Svg("fonts { lyricText step +6 }\n" + wideLyrics));
        Assert.True(big > plain + 1.0, $"bar line {plain} -> {big}: the reservation did not follow the doubled syllable");
    }

    // ================================================================================
    // The twin
    // ================================================================================

    [Fact]
    public void AStepIsWrittenAsTheGrobsFontSize_AndAStyleAsSeriesAndShape()
    {
        var exporter = new LilyPondExporter();
        string ly = exporter.Export(SyntaxTree.Parse(
            "fonts { mark step +1 bold  lyrics step -1  text italic  tempo step -2.5 }\n" + Book));
        Assert.Contains("\\override RehearsalMark.font-size = #1\n", ly, StringComparison.Ordinal);
        Assert.Contains("\\override RehearsalMark.font-series = #'bold\n", ly, StringComparison.Ordinal);
        // A written style REPLACES the engraving's, so the shape is said too.
        Assert.Contains("\\override RehearsalMark.font-shape = #'upright\n", ly, StringComparison.Ordinal);
        Assert.Contains("\\override SectionLabel.font-size = #1\n", ly, StringComparison.Ordinal);
        // The group reaches both of its leaves.
        Assert.Contains("\\override LyricText.font-size = #-1\n", ly, StringComparison.Ordinal);
        Assert.Contains("\\override StanzaNumber.font-size = #-1\n", ly, StringComparison.Ordinal);
        Assert.Contains("\\override TextScript.font-shape = #'italic\n", ly, StringComparison.Ordinal);
        Assert.Contains("\\override TextScript.font-series = #'medium\n", ly, StringComparison.Ordinal);
        Assert.Contains("\\override MetronomeMark.font-size = #-2.5\n", ly, StringComparison.Ordinal);
        // ...all inside the \Score context of the \layout block, beside the setting it already carried.
        Assert.Contains("\\Score\n      printInitialRepeatBar = ##t\n", ly, StringComparison.Ordinal);
        Assert.DoesNotContain("size", exporter.Warnings.FirstOrDefault(w => w.Contains("not exported", StringComparison.Ordinal)) ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public void ABookWithoutAttributes_WritesTheLayoutItAlwaysWrote()
    {
        string ly = new LilyPondExporter().Export(SyntaxTree.Parse(Book));
        // (15\mm: the book names its part, so the first system is indented.)
        Assert.Contains("\\layout { indent = 15\\mm \\context { \\Score printInitialRepeatBar = ##t } }", ly, StringComparison.Ordinal);
        Assert.DoesNotContain("font-size", ly, StringComparison.Ordinal);
        Assert.Contains("title = \"Ttl\"", ly, StringComparison.Ordinal);
    }

    [Fact]
    public void TheHeaderAndNavigationRolesAreSpelledInTheirMarkups()
    {
        string ly = new LilyPondExporter().Export(SyntaxTree.Parse(
            "fonts { title step +2  composer italic  navigation bold }\n" + Book));
        Assert.Contains("title = \\markup { \\fontsize #2 \"Ttl\" }", ly, StringComparison.Ordinal);
        Assert.Contains("composer = \\markup { \\normal-text \\italic \"Cmp\" }", ly, StringComparison.Ordinal);
        Assert.Contains("\\mark \\markup { \\normal-text \\bold \"Fine\" }", ly, StringComparison.Ordinal);
        // Not as a RehearsalMark override, which the boxed labels share.
        Assert.DoesNotContain("RehearsalMark.font", ly, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAbsoluteSizeHasNoTwinSpelling_AndSaysSo()
    {
        var exporter = new LilyPondExporter();
        string ly = exporter.Export(SyntaxTree.Parse("fonts { mark size 3 }\n" + Book));
        Assert.DoesNotContain("font-size", ly, StringComparison.Ordinal);
        Assert.Contains(exporter.Warnings, w => w.Contains("mark size 3", StringComparison.Ordinal)
                                                && w.Contains("step", StringComparison.Ordinal));
    }

    // ================================================================================
    // The editor
    // ================================================================================

    private static LilySharpLanguageServer.CompletionContext Ctx(string text)
        => LilySharpLanguageServer.GetCompletionContext(text, text.Length);

    [Theory]
    [InlineData("fonts { mark \"X\" ", "FontEntryOpen")]
    [InlineData("fonts { mark bold ", "FontEntryOpen")]
    [InlineData("fonts { mark step +1 ", "FontEntryOpen")]
    [InlineData("fonts { mark step ", "AfterFontNumber")]
    [InlineData("fonts { mark size ", "AfterFontNumber")]
    [InlineData("fonts { chordName as ", "AfterFontAs")]
    [InlineData("fonts { serif \"Georgia\" ", "FontBlock")]
    [InlineData("fonts { mark ", "AfterFontRoleKey")]
    public void EveryCaretInsideAnEntry_KnowsWhatMayFollow(string text, string expected)
        // The context enum is internal to the server; its name is the claim.
        => Assert.Equal(expected, Ctx(text).ToString());

    [Fact]
    public void AfterARoleKey_TheAttributesAreOffered_AndTheBareFamilyIsNot()
    {
        var labels = LilySharpLanguageServer.GetFontRoleValueCompletions("mark").Items.Select(i => i.Label).ToArray();
        foreach (string word in TextRoles.AttributeWords)
            Assert.Contains(word, labels);
        // The bare family would complete the spelling the reader refuses.
        Assert.DoesNotContain("serif", labels);
        Assert.DoesNotContain("sans", labels);
        // A generic family takes faces alone.
        Assert.Single(LilySharpLanguageServer.GetFontRoleValueCompletions("serif").Items);
    }

    [Fact]
    public void InsideAnOpenEntry_BothAttributesAndKeysAreOffered()
    {
        var labels = LilySharpLanguageServer.GetFontEntryContinuationCompletions().Items.Select(i => i.Label).ToArray();
        Assert.Contains("step", labels);
        Assert.Contains("lyricText", labels);
        Assert.Contains("embedded", labels);
    }

    [Fact]
    public void AfterAs_TheTwoFamiliesAreOffered()
        => Assert.Equal(["serif", "sans"],
            LilySharpLanguageServer.GetFontAsCompletions().Items.Select(i => i.Label).ToArray());
}
