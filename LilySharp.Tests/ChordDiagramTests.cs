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
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LilySharp.Core.LilyPond;
using LilySharp.Core.Music;
using LilySharp.Core.MusicXml;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using LilySharp.Core.Tablature;
using LilySharp.Lsp;
using LilySharp.Lsp.Protocol;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// <c>@chord</c>'s optional diagram words (owner's decision 2026-09-27; the index counts from
/// 0 since 2026-09-28): <c>@chord(Cm7 2)</c>, <c>@chord(Cm7 2 mute 3 4)</c>,
/// <c>@chord(Cm7 x3x546)</c>, <c>@chord(x32010)</c>, <c>@chord(D mute 5)</c> — read by
/// <see cref="ChordAnnotation"/>, the voicings counted by <see cref="ChordVoicings"/>
/// (<c>ChordVoicingTests</c> pins those numbers). The name draws where every chord symbol
/// draws; the diagram stands under it, between the name and the staff.
/// </summary>
[Trait("Category", "Unit")]
public class ChordDiagramTests
{
    private static ChordAnnotation Words(string argument)
        => ChordAnnotation.Parse(argument.Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static string Resolved(string argument, int[]? tuning = null)
    {
        var r = Words(argument).Resolve(tuning ?? Tunings.Guitar);
        Assert.True(r.HasDiagram, $"{argument}: {r.Problem ?? Words(argument).Problem}");
        return ChordVoicings.Spell(r.Frets);
    }

    private static string Book(string music, string part = "part gt { clef treble }") => $$"""
        octave absolute
        {{part}}
        section A { gt { {{music}} } }
        form main { A }
        score main { staff gt }
        """;

    // ---------------------------------------------------------------- parsing: the forms

    [Fact]
    public void TheNameAlone_AsksForNoDiagram()
    {
        var w = Words("Cm7");
        Assert.Equal("Cm7", w.Symbol);
        Assert.NotNull(w.Structure);
        Assert.False(w.WantsDiagram);
        Assert.Null(w.Problem);
    }

    [Fact]
    public void EveryForm_ReadsAsTheOwnerWroteIt()
    {
        Assert.Equal(12, Words("Cm7 12").Index);
        var muted = Words("Cm7 12 mute 3 4");
        Assert.Equal(12, muted.Index);
        Assert.Equal(new[] { 3, 4 }, muted.Mutes);
        Assert.Equal("x3x546", Words("Cm7 x3x546").Written);
        var alone = Words("x32010");
        Assert.True(alone.NamesFromDiagram);
        Assert.Null(alone.Symbol);
        var slash = Words("C/G 2 mute 1");
        Assert.NotNull(slash.Structure?.BassStep);
        Assert.Equal(2, slash.Index);
        Assert.True(ChordAnnotation.Parse([]).IsBare);
        // `mute` with no index is the index form at 0.
        var openD = Words("D mute 5");
        Assert.True(openD.IsIndexForm);
        Assert.Null(openD.Index);
    }

    [Theory]
    [InlineData("C 0", "x-3-2-0-1-0")]
    [InlineData("Cm7 2", "x35343")]
    [InlineData("Cm7 3", "x3x546")]
    [InlineData("D mute 5", "xx0232")]          // the open D: #0 is x50232
    [InlineData("C 0 mute 4", "x3x010")]        // the owner's example of the overlay
    [InlineData("Cm7 x3x546", "x3x546")]
    [InlineData("x32010", "x32010")]
    [InlineData("C 47", "8-10-10-9-8-8")]
    public void TheDiagram_IsTheVoicingWithItsMutes(string argument, string frets)
        => Assert.Equal(frets.Replace("-", ""), Resolved(argument).Replace("-", ""));

    /// <summary>The BREAKING change: spaces separate words, so <c>C 7</c> is C with voicing 7.</summary>
    [Fact]
    public void ASpacedSymbol_IsNowTheSymbolAndAnIndex()
    {
        var w = Words("C 7");
        Assert.Equal("C", w.Symbol);
        Assert.Equal(7, w.Index);
        Assert.Equal(ChordVoicings.Spell(ChordVoicings.For(Tunings.Guitar, w.Structure!).Bases[7]), Resolved("C 7"));
    }

    /// <summary>An index is one to three digits; a position string is as long as the tuning
    /// has strings (at least four) — so a four-digit word is never an index.</summary>
    [Fact]
    public void IndexAndPositionString_AreToldApartByLength()
    {
        Assert.Equal(123, Words("C 123").Index);
        Assert.Null(Words("C 123").Written);
        Assert.Equal("0232", Words("C 0232").Written);
        Assert.Null(Words("C 0232").Index);
        Assert.Equal("320003", Words("G 320003").Written);
    }

    [Fact]
    public void MuteIsOrderFree()
        => Assert.Equal(Resolved("Cm7 2 mute 3 4"), Resolved("Cm7 2 mute 4 3"));

    /// <summary>The overlay never renumbers: muting a string of #0 leaves #1 where it was, and
    /// the result is not re-validated (muting the root string is the writer's call).</summary>
    [Fact]
    public void TheOverlay_NeverRenumbers_AndIsNotRevalidated()
    {
        Assert.Equal("x35010", Resolved("C 1"));
        Assert.Equal("x35010", Resolved("C 1 mute 6"));   // string 6 is muted already
        // Muting the root string: E G C E — an inversion, drawn as asked.
        Assert.Equal("xx2010", Resolved("C 0 mute 5"));
        var r = Words("C 0 mute 4").Resolve(Tunings.Guitar);
        Assert.Equal(0, r.Index);
        Assert.Equal(57, r.Count);
    }

    // ---------------------------------------------------------------- diagnostics

    private static IReadOnlyList<LilySharp.Core.Syntax.Diagnostic> Warnings(string music, string part = "part gt { clef treble }")
        => SemanticValidation.Run(SyntaxTree.Parse(Book(music, part)))
            .Where(d => d.Code == DiagnosticCodes.ChordDiagramNotDrawn).ToList();

    private static string OneWarning(string music, string part = "part gt { clef treble }")
        => Assert.Single(Warnings(music, part)).Message;

    [Fact]
    public void TheWrittenForms_WarnAboutNothing()
    {
        foreach (var music in new[]
        {
            "c'4@chord(Cm7) c'4@chord(Cm7 2) c'4@chord(Cm7 2 mute 3 4) c'4@chord(Cm7 x3x546) |",
            "c'4@chord(x32010) c'4@chord(C/G 2 mute 1) c'4@chord(D mute 5) <c' e' g'>4@chord |",
        })
            Assert.Empty(Warnings(music));
    }

    [Fact]
    public void AnIndexOutOfRange_GivesTheRange()
        => Assert.Equal("Cm7 has 52 voicings on this tuning (0–51). No diagram is drawn.",
            OneWarning("c'1@chord(Cm7 52) |"));

    [Fact]
    public void AChordWithNoVoicing_SaysToWriteTheDiagramOut()
        => Assert.Contains("write the diagram out",
            OneWarning("c1@chord(C13 0) |", "part gt { clef bass tuning bass }"));

    [Theory]
    [InlineData("c'1@chord(D mute 7) |", "string 7 does not exist")]
    [InlineData("c'1@chord(D mute 5 5) |", "listed twice")]
    [InlineData("c'1@chord(D mute) |", "'mute' needs the strings")]
    [InlineData("c'1@chord(x32010 mute 1) |", "write the x into 'x32010'")]
    [InlineData("c'1@chord(C x32010 mute 1) |", "write the x into 'x32010'")]
    [InlineData("c'1@chord(C 0232) |", "has 4 characters but this part's tuning has 6 strings")]
    [InlineData("c'1@chord(C m7) |", "neither a voicing index")]
    [InlineData("c'1@chord(C 2 foo) |", "after the voicing index")]
    [InlineData("c'1@chord(C 2 mute 3 foo) |", "after the muted strings")]
    [InlineData("c'1@chord(x32010 foo) |", "after a written-out diagram")]
    [InlineData("c'1@chord(x3a010) |", "is not a position string")]
    [InlineData("c'1@chord(x0x00x) |", "name no chord Lily# knows")]
    public void EachProblem_NamesTheFix(string music, string fix)
        => Assert.Contains(fix, OneWarning(music));

    [Fact]
    public void TheIndexForm_OnAReentrantTuning_SaysToWriteItOut()
    {
        const string uke = "part gt { clef treble tuning ukulele }";
        Assert.Contains("re-entrant", OneWarning("c'1@chord(C 0) |", uke));
        Assert.Contains("has 6 characters but this part's tuning has 4 strings",
            OneWarning("c'1@chord(C x32010) |", uke));
        Assert.Empty(Warnings("c'1@chord(C 0003) |", uke));   // the position string works
    }

    [Fact]
    public void AnUnknownSymbol_KeepsChordsOwnMessage()
    {
        var all = SemanticValidation.Run(SyntaxTree.Parse(Book("c'1@chord(C7#9 2) |")));
        Assert.Contains(all, d => d.Code == DiagnosticCodes.UnknownAnnotation && d.Message.Contains("'7#9' is not a chord quality"));
        Assert.DoesNotContain(all, d => d.Code == DiagnosticCodes.ChordDiagramNotDrawn);
    }

    // ---------------------------------------------------------------- the page

    private static ScoreLayout Laid(string book)
    {
        var tree = SyntaxTree.Parse(book);
        return new LayoutEngine().Layout(SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree)));
    }

    [Fact]
    public void ADiagram_IsDrawnForEveryFormThatAsksAndNoOther()
    {
        var lay = Laid(Book("c'4@chord(Cm7 2) c'4@chord(Cm7) c'4@chord(x32010) c'4@chord(Cm7 52) |"));
        Assert.Equal(new[] { "frame:x35343", "frame:x32010" },
            lay.ArticulationLayouts.OrderBy(a => a.X).Select(a => a.Glyph).ToArray());
        // Four names: the out-of-range index still prints its name; x32010 is named C.
        Assert.Equal(new[] { "Cm7", "Cm7", "C", "Cm7" },
            lay.ChordNameLayouts.OrderBy(c => c.X).Select(c => c.ChordText).ToArray());
    }

    [Fact]
    public void AWrittenDiagramsName_IsWhatItsNotesSpell()
    {
        var names = Laid(Book("c'4@chord(x32010) c'4@chord(xx0232) c'4@chord(320003) c'4@chord(x02210) |"))
            .ChordNameLayouts.OrderBy(c => c.X).Select(c => c.ChordText).ToArray();
        Assert.Equal(4, names.Length);
        Assert.Equal("C", names[0]);
        Assert.Equal("D", names[1]);
        Assert.Equal("G", names[2]);
        Assert.StartsWith("a", names[3].ToLowerInvariant());
    }

    /// <summary>The diagram's own elements in an SVG — every line, circle and label that
    /// carries its data-pos except the chord name's sans text — translated so the grid's
    /// top-left string is the origin, as "kind x y …" lines.</summary>
    private static List<string> DiagramElements(string svg, int dataPos)
    {
        var els = Regex.Matches(svg, $"<(line|circle|text)[^>]*data-pos=\"{dataPos}\"[^>]*>(?:[^<]*</text>)?")
            .Select(m => m.Value)
            .Where(e => !e.Contains("sans-serif"))
            .ToList();
        Assert.NotEmpty(els);
        double N(string e, string attr) => double.Parse(
            Regex.Match(e, $" {attr}=\"([-0-9.]+)\"").Groups[1].Value, CultureInfo.InvariantCulture);
        var firstLine = els.First(e => e.StartsWith("<line"));
        double ox = N(firstLine, "x1"), oy = N(firstLine, "y1");
        string Rel(string e, string attr, double o) =>
            Regex.IsMatch(e, $" {attr}=\"") ? $"{attr}={N(e, attr) - o:0.00}" : "";
        return els.Select(e => string.Join(" ",
                e[1..e.IndexOf(' ')],
                Rel(e, "x1", ox), Rel(e, "y1", oy), Rel(e, "x2", ox), Rel(e, "y2", oy),
                Rel(e, "cx", ox), Rel(e, "cy", oy), Rel(e, "x", ox), Rel(e, "y", oy),
                Regex.Match(e, ">([^<]*)</text>").Groups[1].Value))
            .ToList();
    }

    private static int MarkPos(string book, string markText)
        => book.IndexOf(markText, StringComparison.Ordinal);

    [Fact]
    public void ChordCm7Two_DrawsTheGridOfDiagramX35343_WithTheNameAboveIt()
    {
        string a = Book("c'1@chord(Cm7 2) |"), b = Book("c'1@diagram(x35343) |");
        string svgA = SvgGenerator.Generate(SyntaxTree.Parse(a));
        string svgB = SvgGenerator.Generate(SyntaxTree.Parse(b));
        var gridA = DiagramElements(svgA, MarkPos(a, "@chord"));
        var gridB = DiagramElements(svgB, MarkPos(b, "@diagram"));
        Assert.Equal(gridB, gridA);

        // The name stands ABOVE every element of the diagram (device Y grows downward).
        int pos = MarkPos(a, "@chord");
        double nameBaseline = double.Parse(Regex.Match(svgA,
            $"<text x=\"[-0-9.]+\" y=\"([-0-9.]+)\"[^>]*sans-serif[^>]*data-pos=\"{pos}\"").Groups[1].Value,
            CultureInfo.InvariantCulture);
        var ys = Regex.Matches(svgA, $"<(?:line|circle)[^>]*data-pos=\"{pos}\"[^>]*>")
            .SelectMany(m => Regex.Matches(m.Value, " (?:y1|y2|cy)=\"([-0-9.]+)\"")
                .Select(g => double.Parse(g.Groups[1].Value, CultureInfo.InvariantCulture)))
            .ToList();
        Assert.True(nameBaseline < ys.Min() - 0.3,
            $"the name's baseline {nameBaseline} is not above the diagram's top {ys.Min()}");
    }

    [Fact]
    public void NothingCollides_TheNameClearsTheDiagram_AndTheDiagramTheStaff()
    {
        var lay = Laid(Book("c'4@chord(Cm7 2) e''4@chord(C 12) a''4@chord(D 9) c'4@chord(G7 21) |"));
        var frames = lay.ArticulationLayouts.OrderBy(a => a.X).ToList();
        var names = lay.ChordNameLayouts.OrderBy(c => c.X).ToList();
        Assert.Equal(4, frames.Count);
        Assert.Equal(4, names.Count);
        var fonts = LilySharp.Core.Rendering.ScoreTextMetrics.Bundled;
        // The system's staff middle is 2 below its top (one staff): YUp frames differ by that.
        foreach (var (f, n) in frames.Zip(names))
        {
            double frameTopAboveStaffTop = f.YUp + f.Ink.Top - 2.0;
            double nameBottomAboveStaffTop = n.YUp + ChordNameEngraver.SymbolInk(fonts, n).Bottom;
            Assert.True(nameBottomAboveStaffTop >= frameTopAboveStaffTop + 0.5 - 1e-6,
                $"{n.ChordText}: name bottom {nameBottomAboveStaffTop:0.00} over diagram top {frameTopAboveStaffTop:0.00}");
            // …and the diagram's grid clears the staff's top line (TextScript staff-padding).
            Assert.True(f.YUp + f.Ink.Bottom >= 2.05 + 0.3 - 1e-6, $"{f.Glyph}: grid bottom {f.YUp:0.00}");
        }
        // Neighbouring diagrams stand side by side (the spacing @diagram's reuses).
        for (int i = 0; i + 1 < frames.Count; i++)
            Assert.True(frames[i + 1].X + frames[i + 1].Ink.Left >= frames[i].X + frames[i].Ink.Right + 0.4,
                $"diagrams {i}/{i + 1} overlap");
    }

    /// <summary>A high note pushes its diagram up, and the name goes up with it.</summary>
    [Fact]
    public void AHighNote_LiftsTheDiagramAndItsName()
    {
        var low = Laid(Book("c'1@chord(Cm7 2) |"));
        var high = Laid(Book("c'''1@chord(Cm7 2) |"));
        Assert.True(high.ArticulationLayouts[0].YUp > low.ArticulationLayouts[0].YUp + 1);
        Assert.True(high.ChordNameLayouts[0].YUp > low.ChordNameLayouts[0].YUp + 1);
    }

    [Fact]
    public void TheUnchangedForms_DrawAsBefore()
    {
        // @diagram keeps its mover (450) and draws no name.
        var d = Laid(Book("c'1@diagram(x32010) |"));
        Assert.Equal(ArticulationSpacing.TextScriptOutsideStaffPriority, d.ArticulationLayouts.Single().OutsideStaffPriority);
        Assert.Empty(d.ChordNameLayouts);
        // The bare @chord names its notes and draws no diagram.
        var bare = Laid(Book("<c' e' g'>1@chord |"));
        Assert.Equal("C", bare.ChordNameLayouts.Single().ChordText);
        Assert.Empty(bare.ArticulationLayouts);
        // The @chord's diagram declares no priority: it stands under its name.
        var c = Laid(Book("c'1@chord(C 0) |"));
        Assert.Null(c.ArticulationLayouts.Single().OutsideStaffPriority);
    }

    /// <summary>A shape that straddles the 4th fret shifts, LilyPond's fret-range rule —
    /// x35343 starts at fret 3 ("3fr") and draws its 5th-fret dot on row 3.</summary>
    [Fact]
    public void AShapeReachingPastTheFourthFret_Shifts_AsLilyPondDoes()
    {
        Assert.Equal(3, FretFrameGeometry.BaseFret("x35343"));
        Assert.Equal(1, FretFrameGeometry.BaseFret("x32010"));
        Assert.Equal(1, FretFrameGeometry.BaseFret("x02220"));
        Assert.Equal(4, FretFrameGeometry.RowCount("x35343"));
        Assert.Equal(5, FretFrameGeometry.RowCount("x3x557"));   // 3..7: five rows
        Assert.Equal(8, FretFrameGeometry.BaseFret(ChordVoicings.ToFrameSpec(ChordVoicingTests.Shape("8-10-10-9-8-8"))));
        Assert.Equal(10, FretFrameGeometry.FretAt("8aa988", 1));
    }

    // ---------------------------------------------------------------- the part's tuning

    [Fact]
    public void TheVoicing_IsCountedOnThePartsTuning()
    {
        // Drop D: #0 of D rings the low D (000232) — ChordVoicingTests pins the set.
        var lay = Laid(Book("c'1@chord(D 0) |", "part gt { clef treble tuning guitardropd }"));
        Assert.Equal("frame:000232", lay.ArticulationLayouts.Single().Glyph);
        var seven = Laid(Book("c'1@chord(C 0) |", "part gt { clef treble tuning guitar7 }"));
        Assert.Equal("frame:xx32013", seven.ArticulationLayouts.Single().Glyph);
    }

    // ---------------------------------------------------------------- the exporters

    [Fact]
    public void TheTwin_WritesTheNameInChordNames_AndTheDiagramOnTheNote()
    {
        string ly = new LilyPondExporter().Export(SyntaxTree.Parse(Book("c'2@chord(Cm7 2) c'2@chord(C 47) |")));
        Assert.Contains("c2:m7", ly);
        Assert.Contains("^\\markup \\fret-diagram-terse \"x;3;5;3;4;3;\"", ly);
        // Frets 10 and up are written as numbers.
        Assert.Contains("\\fret-diagram-terse \"8;10;10;9;8;8;\"", ly);
    }

    [Fact]
    public void TheTwin_NamesAWrittenDiagramByItsNotes()
    {
        string ly = new LilyPondExporter().Export(SyntaxTree.Parse(Book("c'1@chord(x32010) |")));
        Assert.Contains("\\fret-diagram-terse \"x;3;2;o;1;o;\"", ly);
        Assert.Matches(@"InlineChords = \\chordmode \{\s+c1 \|", ly);
    }

    private static string Xml(string music) =>
        new MusicXmlExporter().Export(SyntaxTree.Parse(Book(music))).ToXml().ToString();

    [Fact]
    public void MusicXml_NestsTheDiagramInTheHarmony()
    {
        string xml = Xml("c'1@chord(Cm7 2) |");
        var harmony = Regex.Match(xml, "<harmony>.*?</harmony>", RegexOptions.Singleline).Value;
        Assert.Contains("<kind>minor-seventh</kind>", harmony);
        Assert.Contains("<frame>", harmony);
        Assert.Contains("<frame-strings>6</frame-strings>", harmony);
        Assert.Contains("<first-fret>3</first-fret>", harmony);
        // x35343: five frame-notes, string 5 at fret 3, string 4 at fret 5.
        Assert.Equal(5, Regex.Matches(harmony, "<frame-note>").Count);
        Assert.Matches(@"<string>4</string>\s*<fret>5</fret>", harmony);
    }

    [Fact]
    public void MusicXml_NamesAWrittenDiagramByItsNotes()
    {
        string xml = Xml("c'1@chord(xx0232) |");
        Assert.Contains("<root-step>D</root-step>", xml);
        Assert.Contains("<frame>", xml);
    }

    // ---------------------------------------------------------------- the editor

    private static string? HoverAt(string doc, string needle)
    {
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        var uri = new Uri("file:///chord-diagram.lys");
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = uri, Text = doc, Version = 1, LanguageId = "lilysharp" },
        });
        int at = doc.IndexOf(needle, StringComparison.Ordinal) + 2;
        int line = doc[..at].Count(ch => ch == '\n');
        int col = at - (doc.LastIndexOf('\n', at - 1) + 1);
        return server.Hover(new TextDocumentPositionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Position = new Position(line, col),
        })?.Contents.Value;
    }

    [Fact]
    public void Hover_ShowsTheVoicingItsIndexAndTheFrets()
    {
        string doc = Book("c'2@chord(Cm7 2) <c' e' g'>2@chord(C 0 mute 4) |");
        string? cm7 = HoverAt(doc, "@chord(Cm7");
        Assert.NotNull(cm7);
        Assert.Contains("`Cm7`", cm7);
        Assert.Contains("`x35343`", cm7);
        Assert.Contains("#2 (0–51)", cm7);
        Assert.Contains("6 x", cm7);
        Assert.Contains("5 3", cm7);
        Assert.Contains("1 3", cm7);
        // On a chord, the mark hovers as its diagram, not as the chord.
        string? c = HoverAt(doc, "@chord(C 0");
        Assert.Contains("`x3x010`", c);
        Assert.Contains("mute 4", c);
    }
}
