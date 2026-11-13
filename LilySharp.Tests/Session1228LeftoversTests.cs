// This file is part of Lily#.
// Copyright (C) 2025-2026 Yoshifumi Tsuda
//
// Lily# is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// Lily# is distributed in the hope that it will be useful,
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
using LilySharp.Core.MusicXml;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Four defects session 1227 found and left (2026-11-14, Lab sessions/p1227/leftovers.md): a
/// phrase named like an English flat could not be called, the LilyPond twin wrote dynamics
/// LilyPond does not have, a scoop on a rest and on a grace note, and quarter tones in
/// <c>check --pitches</c> and <c>import</c>.
/// </summary>
public class Session1228LeftoversTests
{
    private static List<Diagnostic> Said(string book)
    {
        var tree = SyntaxTree.Parse(book);
        return tree.Diagnostics.Concat(SemanticValidation.Run(tree)).ToList();
    }

    private static string Book(string declarations, string music)
        => "time 4/4\noctave absolute\n" + declarations + "\npart mel { }\nsection Main { mel { " + music + " } }\n"
            + "form { Main }\nscore { staff mel }\n";

    // ========== 1. A phrase named like an English flat ==========

    /// <summary>
    /// <c>eb</c>, <c>bb</c> and <c>cB</c> are no pitches of this grammar (the flats are
    /// <c>ees</c>, <c>bes</c>), so a phrase declared under one of them is called by it.
    /// </summary>
    [Theory]
    [InlineData("eb")]
    [InlineData("bb")]
    [InlineData("cB")]
    [InlineData("gflat")]
    public void APhraseNamedLikeAnEnglishFlat_IsCalledByItsName(string name)
    {
        string book = Book($"phrase {name} {{ g'4 a' b' c'' | }}", $"c'1 | {name} | {name}' |");
        Assert.DoesNotContain(Said(book), d => d.Severity == DiagnosticSeverity.Error);

        var tree = SyntaxTree.Parse(book);
        Assert.Equal(2, tree.GetNodes<VariableReferenceSyntax>().Count(r => r.Name.Text == name));
        // The phrase's four notes are on the page beside the part's own one.
        Assert.Equal(5, ResolvedPitches.ForFile(tree)!.Count);

        // The control: with no such phrase the word is a mistyped pitch, said once.
        var slip = Said(Book("", $"c'1 | {name} |")).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        var one = Assert.Single(slip);
        Assert.Equal(DiagnosticCodes.BareReferenceRequiresDollar, one.Code);
        Assert.Contains("did you mean the pitch '" + name[..1] + "es'", one.Message);
    }

    // ========== 2. The twin's dynamics LilyPond has no command for ==========

    private static string Twin(string music) => new LilyPondExporter().Export(SyntaxTree.Parse(Book("", music)));

    /// <summary>
    /// LilyPond 2.26 declares no <c>\sffz</c> and no <c>\rf</c> (ly/dynamic-scripts-init.ly), so
    /// the twin builds them as LilyPond builds its own; every other level keeps its command.
    /// </summary>
    [Fact]
    public void TheTwin_WritesNoDynamicCommandLilyPondLacks()
    {
        Assert.Contains("c'4-#(make-dynamic-script \"sffz\")", Twin("c'4@sffz d' e' f' |"));
        Assert.Contains("c'4-#(make-dynamic-script \"rf\")", Twin("c'4@rf d' e' f' |"));
        Assert.Contains("c'4^#(make-dynamic-script \"sffz\")", Twin("c'4@sffz.up d' e' f' |"));
        Assert.Contains("c'4_#(make-dynamic-script \"rf\")", Twin("c'4@rf.down d' e' f' |"));
        // The control: a level LilyPond has is its command.
        Assert.Contains("c'4\\sfz", Twin("c'4@sfz d' e' f' |"));

        // Every level the language reads, so that a new one is asked the same question.
        // `niente` is no level of LilyPond's and is written as text (EmitNiente).
        foreach (string level in SyntaxFacts.DynamicTextLevels.Keys.Where(k => k != "niente"))
        {
            string twin = Twin($"c'4@{level} d' e' f' |");
            bool command = twin.Contains($"c'4\\{level} ", StringComparison.Ordinal);
            bool built = twin.Contains($"c'4-#(make-dynamic-script \"{level}\")", StringComparison.Ordinal);
            Assert.True(command != built, level);
            Assert.True(built == level is "sffz" or "rf", level);
        }
    }

    // ========== 3. A scoop on a rest, and on a grace note ==========

    /// <summary>The gestures' strokes: every round-capped line that carries a source position.</summary>
    private static List<(int Pos, double X1, double X2)> GestureStrokes(string svg) => Regex.Matches(svg,
            "<line x1=\"([-\\d.]+)\" y1=\"[-\\d.]+\" x2=\"([-\\d.]+)\" y2=\"[-\\d.]+\" stroke=\"#000000\" "
            + "stroke-width=\"[\\d.]+\" stroke-linecap=\"round\" data-pos=\"(\\d+)\"/>")
        .Select(m => (int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture), Number(m.Groups[1]), Number(m.Groups[2])))
        .ToList();

    private static double Number(Group g) => double.Parse(g.Value, CultureInfo.InvariantCulture);

    /// <summary>How many drawn elements carry the source position of the book's first '@' mark
    /// — a gesture's curve, arrow and label are keyed to it.</summary>
    private static int InkOfTheMark(string svg, string book)
        => Regex.Matches(svg, "data-pos=\"" + book.IndexOf('@') + "\"").Count;

    /// <summary>Where the music glyph written at <paramref name="written"/> stands.</summary>
    private static double GlyphX(string svg, string book, string written)
    {
        int pos = book.IndexOf(written, StringComparison.Ordinal);
        Assert.True(pos >= 0, written);
        var m = Regex.Match(svg, "<text class=\"music\"(?: style=\"[^\"]*\")? x=\"([-\\d.]+)\" y=\"[-\\d.]+\" font-size=\"[\\d.]+\" data-pos=\"" + pos + "\">");
        Assert.True(m.Success, written);
        return Number(m.Groups[1]);
    }

    /// <summary>
    /// A gesture that bends a pitch needs a note: on a rest it is one error (LYS4079), and no
    /// curve is drawn, nothing is written to MusicXML or to the LilyPond twin.
    /// </summary>
    [Theory]
    [InlineData("r4@scoop c'' c'' c'' |", "scoop")]
    [InlineData("r4@plop c'' c'' c'' |", "plop")]
    [InlineData("r4@fall c'' c'' c'' |", "fall")]
    [InlineData("r4@doit c'' c'' c'' |", "doit")]
    [InlineData("r4@bend(full) c'' c'' c'' |", "bend")]
    [InlineData("s4@scoop c'' c'' c'' |", "scoop")]
    [InlineData("R1@fall | c''1 |", "fall")]
    public void AGestureOnARest_IsRefusedAndDrawnNowhere(string music, string name)
    {
        string book = Book("", music);
        var error = Assert.Single(Said(book), d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal(DiagnosticCodes.GestureNotOnANote, error.Code);
        Assert.Contains($"'@{name}'", error.Message);

        Assert.Equal(0, InkOfTheMark(LiveRender.SvgFromRenderSpec(book), book));
        var tree = SyntaxTree.Parse(book);
        var notes = new MusicXmlExporter().Export(tree).Parts.Single().Measures.SelectMany(m => m.Notes).ToList();
        Assert.All(notes, n => { Assert.Empty(n.Articulations); Assert.Empty(n.Technicals); });
        Assert.DoesNotContain("bendAfter", new LilyPondExporter().Export(tree));
    }

    /// <summary>The control: on a note each of them is taken, and drawn.</summary>
    [Theory]
    [InlineData("c''4@scoop c'' c'' c'' |")]
    [InlineData("c''4@plop c'' c'' c'' |")]
    [InlineData("c''4@fall c'' c'' c'' |")]
    [InlineData("c''4@doit c'' c'' c'' |")]
    [InlineData("c''4@bend(full) c'' c'' c'' |")]
    public void AGestureOnANote_IsDrawn(string music)
    {
        string book = Book("", music);
        Assert.DoesNotContain(Said(book), d => d.Code == DiagnosticCodes.GestureNotOnANote);
        Assert.NotEqual(0, InkOfTheMark(LiveRender.SvgFromRenderSpec(book), book));
    }

    /// <summary>
    /// A grace note's scoop or plop is drawn at the grace head, and the run keeps the curve's
    /// room: it stood on the head before the run (or on the time signature) and could not be seen.
    /// </summary>
    [Theory]
    [InlineData("scoop")]
    [InlineData("plop")]
    public void AGraceNotesApproachCurve_ClearsTheNoteBeforeTheRun(string gesture)
    {
        string plain = Book("", "c''4 grace { d''16 } e''4 f'' g'' |");
        string marked = Book("", $"c''4 grace {{ d''16@{gesture} }} e''4 f'' g'' |");
        string plainSvg = LiveRender.SvgFromRenderSpec(plain), markedSvg = LiveRender.SvgFromRenderSpec(marked);
        Assert.DoesNotContain(Said(marked), d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning);

        var curve = GestureStrokes(markedSvg);
        Assert.Equal(8, curve.Count);
        double before = GlyphX(markedSvg, marked, "c''4 grace"), grace = GlyphX(markedSvg, marked, "d''16");
        // The curve ends 1.55 left of the grace head (OwnGestureGeometryTests)…
        Assert.InRange(curve[^1].X2, grace - 1.56, grace - 1.54);
        // …and starts right of the quarter before it: its head is 1.3 wide.
        Assert.True(curve[0].X1 > before + 1.3, $"the curve starts at {curve[0].X1}, the head before it stands at {before}");
        // The control: the same run with no mark stands nearer by about the curve's reach.
        double plainGap = GlyphX(plainSvg, plain, "d''16") - GlyphX(plainSvg, plain, "c''4 grace");
        Assert.True(grace - before > plainGap + 1.5, $"{grace - before} against {plainGap}");
    }

    /// <summary>The curve clears the time signature too, on the first note of the piece.</summary>
    [Fact]
    public void AGraceNotesScoop_OnTheFirstNote_ClearsTheTimeSignature()
    {
        string plain = Book("", "grace { d''16 } e''4 f'' g'' a'' |");
        string marked = Book("", "grace { d''16@scoop } e''4 f'' g'' a'' |");
        double plainX = GlyphX(LiveRender.SvgFromRenderSpec(plain), plain, "d''16");
        double markedX = GlyphX(LiveRender.SvgFromRenderSpec(marked), marked, "d''16");
        Assert.True(markedX > plainX + 1.5, $"{markedX} against {plainX}");
    }

    /// <summary>MusicXML writes a grace note's gesture as it writes a full-size note's.</summary>
    [Theory]
    [InlineData("grace { d''16@scoop } e''4 f'' g'' a'' |", "scoop")]
    [InlineData("grace { d''16@plop } e''4 f'' g'' a'' |", "plop")]
    [InlineData("grace { d''16@fall } e''4 f'' g'' a'' |", "falloff")]
    [InlineData("grace { d''16@doit } e''4 f'' g'' a'' |", "doit")]
    [InlineData("grace { <d'' f''>16@scoop } e''4 f'' g'' a'' |", "scoop")]
    public void MusicXml_WritesAGraceNotesGesture(string music, string element)
    {
        var notes = new MusicXmlExporter().Export(SyntaxTree.Parse(Book("", music)))
            .Parts.Single().Measures.SelectMany(m => m.Notes).ToList();
        var grace = notes.First(n => n.IsGrace);
        Assert.Equal(element, Assert.Single(grace.Articulations));
        // The control: nothing else of the bar carries it.
        Assert.All(notes.Where(n => !ReferenceEquals(n, grace)), n => Assert.Empty(n.Articulations));
    }
}
