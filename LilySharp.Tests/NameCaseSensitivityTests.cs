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
using System.Text.RegularExpressions;
using LilySharp.Core.LilyPond;
using LilySharp.Core.Midi;
using LilySharp.Core.MusicXml;
using LilySharp.Core.Rendering;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using LilySharp.Lsp;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Names are CASE-SENSITIVE, one canonical spelling each (owner's decision 2026-09-27):
/// annotation names, and the keys of <c>fonts</c> / <c>layout</c> / <c>paper</c>. A name
/// that differs from a real one only in case is unknown everywhere — nothing drawn, nothing
/// exported — and its diagnostic names the spelling to write, so the fix is a copy.
/// </summary>
[Trait("Category", "Unit")]
public class NameCaseSensitivityTests
{
    // ================================================================================
    // Annotation names — the canonical list
    // ================================================================================

    /// <summary>The '@' completion labels, each cut to its NAME (<c>ottava(bassa)</c> →
    /// <c>ottava</c>): the canonical spellings the editor teaches.</summary>
    private static string[] CompletionNames() =>
        [.. LilySharpLanguageServer.GetArticulationCompletions().Items
            .Select(i => i.Label.Split('(')[0])
            .Distinct(StringComparer.Ordinal)];

    public static TheoryData<string> CompletionNameData()
    {
        var data = new TheoryData<string>();
        foreach (var name in CompletionNames())
            data.Add(name);
        return data;
    }

    [Fact]
    public void EveryCompletionLabel_IsACanonicalName_Exactly()
    {
        var missing = CompletionNames()
            .Where(n => !AnnotationNames.All.Contains(n, StringComparer.Ordinal)).ToList();
        Assert.True(missing.Count == 0,
            "completion labels the tables do not know as written: " + string.Join(", ", missing));
        // The argument rows' dotted names too: ottava(bassa) is the compound name ottava.bassa.
        Assert.Contains("ottava.bassa", AnnotationNames.All);
        Assert.Contains("quindicesima.bassa", AnnotationNames.All);
    }

    /// <summary>
    /// No table holds a name that differs from a completion label only in case — the
    /// shape the lowercase registry keys (<c>hammeron</c>, <c>laissezvibrer</c>) had while
    /// every table matched case-insensitively and the labels were camelCase.
    /// </summary>
    [Fact]
    public void NoTableName_DiffersFromACompletionLabelOnlyInCase()
    {
        var labels = CompletionNames();
        var clashes = AnnotationNames.All
            .SelectMany(n => labels
                .Where(l => string.Equals(n, l, StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(n, l, StringComparison.Ordinal))
                .Select(l => $"{n} ≠ {l}"))
            .ToList();
        Assert.True(clashes.Count == 0, "case-only clashes: " + string.Join(", ", clashes));

        // …and no two canonical names differ only in case, or a hint could name either.
        var twins = AnnotationNames.All
            .GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => string.Join("/", g))
            .ToList();
        Assert.True(twins.Count == 0, "names that differ only in case: " + string.Join(", ", twins));
    }

    private static IReadOnlyList<Diagnostic> Validate(string source)
    {
        var validator = new AnnotationNameValidator();
        validator.Validate(SyntaxTree.Parse(source));
        return validator.Diagnostics;
    }

    [Theory]
    [MemberData(nameof(CompletionNameData))]
    public void EveryCanonicalName_InLowerOrUpperCase_IsUnknown_AndItsWarningNamesTheSpelling(string name)
    {
        foreach (var variant in new[] { name.ToLowerInvariant(), name.ToUpperInvariant() })
        {
            if (variant == name)
                continue;
            var warning = Assert.Single(Validate($"c4@{variant} d |"),
                d => d.Code == DiagnosticCodes.UnknownAnnotation);
            Assert.Contains($"Names are case-sensitive: write '@{name}'.", warning.Message);
        }
    }

    [Theory]
    [InlineData("c4@!UNACORDA d |", "@!unaCorda")]
    [InlineData("c4@!textspan d |", "@!textSpan")]
    [InlineData("c4@Ottava(bassa) d |", "@ottava(bassa)")]
    [InlineData("c4@ottava(BASSA) d |", "@ottava(bassa)")]
    [InlineData("c4@Text(\"dolce\") d |", "@text(\"dolce\")")]
    [InlineData("c4@Notehead(x) d |", "@notehead(x)")]
    public void AWrongCaseTerminatorOrArgumentName_NamesTheSpelling(string source, string canonical)
    {
        var warning = Assert.Single(Validate(source), d => d.Code == DiagnosticCodes.UnknownAnnotation);
        Assert.Contains($"write '{canonical}'", warning.Message);
    }

    // ================================================================================
    // Annotation names — a wrong-case name draws and exports NOTHING
    // ================================================================================

    private static string Book(string music) =>
        "octave absolute\npart m { clef treble }\n"
        + $"section A {{ m {{ {music} }} }}\n"
        + "form main { A }\nscore main { staff m }\n";

    private static string Svg(string music) => Regex.Replace(
        SvgGenerator.Generate(SyntaxTree.Parse(Book(music)), new SvgRenderOptions { EmbedFont = false }),
        " data-pos=\"[^\"]*\"", "");

    private static string Ly(string music) => new LilyPondExporter().Export(SyntaxTree.Parse(Book(music)));

    private static string Xml(string music) =>
        new MusicXmlExporter().Export(SyntaxTree.Parse(Book(music))).ToXml().ToString();

    private static MidiNote[] Midi(string music) =>
        [.. new MidiExporter().Export(SyntaxTree.Parse(Book(music))).Tracks
            .SelectMany(t => t.Notes).Select(n => n with { SourcePos = 0 })];

    /// <summary>
    /// Each row is a canonical annotation and a wrong-case spelling of it. The canonical one
    /// must MOVE the page (else the comparison below proves nothing); the wrong-case one must
    /// leave the page, the LilyPond twin, MusicXML and MIDI exactly as the bare notes are.
    /// </summary>
    [Theory]
    [InlineData("c'4@staccato d' e' f' | c'4 d' e' f' |", "c'4@STACCATO d' e' f' | c'4 d' e' f' |")]
    [InlineData("c'4@fermata d' e' f' | c'4 d' e' f' |", "c'4@Fermata d' e' f' | c'4 d' e' f' |")]
    [InlineData("c'4@laissezVibrer d' e' f' | c'4 d' e' f' |", "c'4@laissezvibrer d' e' f' | c'4 d' e' f' |")]
    [InlineData("c'4@stemUp d' e' f' | c'4 d' e' f' |", "c'4@stemup d' e' f' | c'4 d' e' f' |")]
    [InlineData("c'4@hammerOn d' e' f' | c'4 d' e' f' |", "c'4@hammeron d' e' f' | c'4 d' e' f' |")]
    [InlineData("c'4@glissando d' e' f' | c'4 d' e' f' |", "c'4@Glissando d' e' f' | c'4 d' e' f' |")]
    [InlineData("c'4@startTrillSpan d' e'@stopTrillSpan f' | c'4 d' e' f' |", "c'4@starttrillspan d' e'@stoptrillspan f' | c'4 d' e' f' |")]
    [InlineData("c'4@unaCorda d' e'@!unaCorda f' | c'4 d' e' f' |", "c'4@unacorda d' e'@!unacorda f' | c'4 d' e' f' |")]
    [InlineData("c'4@phrasingSlur d' e'@!phrasingSlur f' | c'4 d' e' f' |", "c'4@phrasingslur d' e'@!phrasingslur f' | c'4 d' e' f' |")]
    [InlineData("c'4 d' e' f' | c'4@mark(\"Z\") d' e' f' |", "c'4 d' e' f' | c'4@Mark(\"Z\") d' e' f' |")]
    [InlineData("c'4@text(\"dolce\") d' e' f' | c'4 d' e' f' |", "c'4@Text(\"dolce\") d' e' f' | c'4 d' e' f' |")]
    [InlineData("c'4@notehead(x) d' e' f' | c'4 d' e' f' |", "c'4@NoteHead(x) d' e' f' | c'4 d' e' f' |")]
    [InlineData("c'4@figuredBass(6) d' e' f' | c'4 d' e' f' |", "c'4@FiguredBass(6) d' e' f' | c'4 d' e' f' |")]
    public void AWrongCaseAnnotation_DrawsAndExportsNothing(string canonical, string wrongCase)
    {
        const string plain = "c'4 d' e' f' | c'4 d' e' f' |";

        Assert.NotEqual(Svg(plain), Svg(canonical));
        Assert.Equal(Svg(plain), Svg(wrongCase));
        Assert.Equal(Ly(plain), Ly(wrongCase));
        Assert.Equal(Xml(plain), Xml(wrongCase));
        Assert.Equal(Midi(plain), Midi(wrongCase));
    }

    [Fact]
    public void AWrongCaseStaccato_IsNotInTheTwinsEither()
    {
        Assert.Contains("\\staccato", Ly("c'4@staccato d' e' f' |"));
        Assert.DoesNotContain("\\staccato", Ly("c'4@STACCATO d' e' f' |"));
        Assert.Contains("<staccato", Xml("c'4@staccato d' e' f' |"));
        Assert.DoesNotContain("<staccato", Xml("c'4@STACCATO d' e' f' |"));
        // MIDI shortens a staccato note, so the plain-vs-wrong-case equality above can see it.
        Assert.NotEqual(Midi("c'4 d' e' f' |"), Midi("c'4@staccato d' e' f' |"));
    }

    // ================================================================================
    // fonts / layout / paper keys
    // ================================================================================

    private static (TextFontPlan Plan, FontPlanReader.Problem[] Problems) Fonts(string block)
    {
        var font = SyntaxTree.Parse(block).GetRoot().DescendantNodes<FontDeclarationSyntax>().First();
        var plan = FontPlanReader.Read(font, out var problems);
        return (plan, [.. problems]);
    }

    [Theory]
    [InlineData("fonts { Tempo bold }", "tempo")]
    [InlineData("fonts { barnumbers \"Georgia\" }", "barNumbers")]
    [InlineData("fonts { SERIF \"Georgia\" }", "serif")]
    [InlineData("fonts { tempo BOLD }", "bold")]
    [InlineData("fonts { tempo Italic }", "italic")]
    public void AWrongCaseFontsKeyOrWord_IsRefused_AndTheRightSpellingIsNamed(string block, string canonical)
    {
        var (_, problems) = Fonts(block);
        var p = Assert.Single(problems, x => x.Code == DiagnosticCodes.UnknownFontRole);
        Assert.Contains($"Keys are case-sensitive: write '{canonical}'.", p.Message);
    }

    [Fact]
    public void AWrongCaseStyleWord_IsNotReadAsTheStyle()
    {
        Assert.Equal(FontStyle.Bold, Fonts("fonts { tempo bold }").Plan.WrittenStyle(TextRole.Tempo));
        Assert.Null(Fonts("fonts { tempo BOLD }").Plan.WrittenStyle(TextRole.Tempo));
        Assert.Null(Fonts("fonts { Tempo bold }").Plan.WrittenStyle(TextRole.Tempo));
    }

    private static (LilySharp.Core.Svg.Layout.LayoutOptions Options, PaperPlanReader.Problem[] Problems)
        Paper(string block)
    {
        var paper = SyntaxTree.Parse(block).GetRoot().DescendantNodes<PaperDeclarationSyntax>().First();
        var options = PaperPlanReader.Read(paper, out var problems);
        return (options, [.. problems]);
    }

    [Theory]
    [InlineData("paper { PaperWidth 150mm }", "paperWidth")]
    [InlineData("paper { raggedright }", "raggedRight")]
    [InlineData("paper { SIZE a5 }", "size")]
    [InlineData("paper { systemSystemSpacing { BasicDistance 12 } }", "basicDistance")]
    public void AWrongCasePaperKey_IsRefused_AndTheRightSpellingIsNamed(string block, string canonical)
    {
        var (options, problems) = Paper(block);
        var p = Assert.Single(problems, x => x.Code == DiagnosticCodes.UnknownPaperKey
                                             && x.Message.Contains("case-sensitive"));
        Assert.Contains($"Keys are case-sensitive: write '{canonical}'.", p.Message);
        Assert.Equal(LilySharp.Core.Svg.Layout.LayoutOptions.Default.PageWidth, options.PageWidth);
    }

    [Fact]
    public void ACanonicalPaperKey_StillBinds()
        => Assert.NotEqual(LilySharp.Core.Svg.Layout.LayoutOptions.Default.PageWidth,
            Paper("paper { paperWidth 150mm }").Options.PageWidth);
}
