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
using LilySharp.Core.MusicXml;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The phrasing slur — <c>@phrasingSlur</c> … <c>@!phrasingSlur</c>, LilyPond's <c>\(</c> …
/// <c>\)</c> (user decision 2026-09-22, session 482: the span spelling this language already
/// uses, the grob's name). One curve engine with a slur; two differences, both LilyPond's:
/// the ratio (0.333) and that it scores the slurs inside it.
/// </summary>
[Trait("Category", "Unit")]
public class PhrasingSlurTests
{
    private static string Book(string music) =>
        "octave absolute part m { clef treble } section A { m { " + music
        + " } } form main { A } score main { staff m }";

    private static Score Collect(string music)
    {
        var tree = MusicSource.Parse(music);
        Assert.False(tree.HasErrors);
        return new MeasureCollector().Collect(tree, null);
    }

    private static List<SlurItem> Phrasing(string music) =>
        new SlurDetector().DetectSlurs(Collect(music)).Where(s => s.IsPhrasing).ToList();

    // ---------------------------------------------------------------- pairing

    [Fact]
    public void APhrasingSlurPairsItsTwoAnnotations_AndLeavesTheSlursAlone()
    {
        var all = new SlurDetector().DetectSlurs(
            Collect("c'4@phrasingSlur d'( e') f'@!phrasingSlur |"));
        var phrasing = Assert.Single(all, s => s.IsPhrasing);
        Assert.Equal((0, 3), (phrasing.StartItemIndex, phrasing.EndItemIndex));
        var slur = Assert.Single(all, s => !s.IsPhrasing);
        Assert.Equal((1, 2), (slur.StartItemIndex, slur.EndItemIndex));
        // Laid out AFTER the slurs it scores (SlurDetector's remark).
        Assert.True(all[^1].IsPhrasing);
    }

    /// <summary>
    /// <c>.up</c> / <c>.down</c> is LilyPond's <c>^\(</c> / <c>_\(</c>: the written side wins
    /// over the slur's own rule. Until session 483 the qualifier was accepted and DROPPED.
    /// </summary>
    [Theory]
    [InlineData("c''4@phrasingSlur.down d'' e'' f''@!phrasingSlur |", false)]  // stems down: would go UP
    [InlineData("g4@phrasingSlur.up a b c'@!phrasingSlur |", true)]
    [InlineData("c''4@phrasingSlur d'' e'' f''@!phrasingSlur |", true)]         // the same notes unforced
    public void AWrittenSide_DecidesTheCurve(string music, bool up) =>
        Assert.Equal(up, Assert.Single(Phrasing(music)).CurveUp);

    [Fact]
    public void AWrittenSide_ReachesBothTwins()
    {
        var tree = SyntaxTree.Parse(Book("c''4@phrasingSlur.down d'' e'' f''@!phrasingSlur | g'4@phrasingSlur.up a' b' c''@!phrasingSlur |"));
        string ly = new LilyPondExporter().Export(tree);
        Assert.Contains("c''4_\\(", ly);
        Assert.Contains("g'4^\\(", ly);
        var xml = new MusicXmlExporter().Export(tree).ToXml();
        var placements = xml.Descendants("slur")
            .Where(s => (string)s.Attribute("type") == "start")
            .Select(s => (string)s.Attribute("placement")).ToList();
        Assert.Equal(new[] { "below", "above" }, placements);
    }

    /// <summary>
    /// On a combinedStaff the two parts' notes merge into one column where they agree in
    /// rhythm — and the phrasing slur of either part survives the merge, as LilyPond's
    /// \partCombine keeps both events (it keys a phrasing slur as 'tie, which the note clears
    /// at once, so the slur never blocks the 'chords decision). Until session 483 the merge
    /// rebuilt part one's note as a chord and the marks were lost with it.
    /// </summary>
    [Theory]
    [InlineData("c''4@phrasingSlur d'' e'' f''@!phrasingSlur", "a'4 b' c'' d''")]
    [InlineData("c''4 d'' e'' f''", "a'4@phrasingSlur b' c'' d''@!phrasingSlur")]
    public void ACombinedStaff_KeepsEitherPartsPhrasingSlur(string one, string two)
    {
        string source = "octave absolute part fl1 { clef treble } part fl2 { clef treble } "
            + "section A { fl1 { " + one + " | } fl2 { " + two + " | } } form main { A } "
            + "score main { combinedStaff { fl1 fl2 } }";
        int at = source.IndexOf("@phrasingSlur", StringComparison.Ordinal);
        string svg = SvgGenerator.Generate(SyntaxTree.Parse(source),
            new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false });
        Assert.Matches(@"d=""M [^""]* C [^""]*""[^>]*data-pos=""" + at + @"""", svg);
    }

    [Fact]
    public void OneNote_ClosesBeforeItOpens()
    {
        // LILYPOND-REF: lily/slur-engraver.cc:295-324 Slur_engraver::process_music — stop events first.
        var p = Phrasing("c'4@phrasingSlur d'@!phrasingSlur@phrasingSlur e' f'@!phrasingSlur |");
        Assert.Equal(2, p.Count);
        Assert.Equal((0, 1), (p[0].StartItemIndex, p[0].EndItemIndex));
        Assert.Equal((1, 3), (p[1].StartItemIndex, p[1].EndItemIndex));
    }

    [Fact]
    public void ASecondStartWhileOneIsOpen_IsIgnored_NotNested()
    {
        // "already have phrasing slur" — the open one keeps the span.
        var p = Assert.Single(Phrasing("c'4@phrasingSlur d'@phrasingSlur e' f'@!phrasingSlur |"));
        Assert.Equal((0, 3), (p.StartItemIndex, p.EndItemIndex));
    }

    // ---------------------------------------------------------------- LYS4018

    private static List<Diagnostic> Reports(string music)
    {
        var validator = new SpanPairingValidator();
        validator.Validate(SyntaxTree.Parse(Book(music)));
        return validator.Diagnostics.Where(d => d.Code == DiagnosticCodes.UnpairedSpan).ToList();
    }

    [Fact]
    public void APairedPhrasingSlur_IsNotReported() =>
        Assert.Empty(Reports("c'4@phrasingSlur d' e' f'@!phrasingSlur |"));

    [Fact]
    public void AnUnclosedPhrasingSlur_IsAnError()
    {
        var d = Assert.Single(Reports("c'4@phrasingSlur d' e' f' |"));
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
        Assert.Contains("phrasing slur is never closed", d.Message);
    }

    [Fact]
    public void AStopWithNothingOpen_AndASecondStart_AreWarnings()
    {
        var stop = Assert.Single(Reports("c'4 d' e' f'@!phrasingSlur |"));
        Assert.Equal(DiagnosticSeverity.Warning, stop.Severity);
        Assert.Contains("closes nothing", stop.Message);
        var twice = Assert.Single(Reports("c'4@phrasingSlur d'@phrasingSlur e' f'@!phrasingSlur |"));
        Assert.Contains("already open", twice.Message);
    }

    // ---------------------------------------------------------------- the curve

    /// <summary>
    /// The three phrasing slurs of Lab sessions/p482/ps2.lys against LilyPond 2.26.0's own
    /// (the twin <c>ps2.ly</c>, SVG backend): each curve's control points relative to its
    /// start, in staff spaces, y down. Both engines spaced the bar alike, so the WIDTH is
    /// compared too.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE INNER SLURS ARE WHAT THIS PINS. With the phrasing slur's enclosed slurs
    /// withheld (the poison of session 482) all three curves move — the second one's first
    /// control point from −3.13 to −1.92, into the slurs it should clear — while LilyPond's
    /// shape is matched to the SVG's two decimals with them.
    /// </remarks>
    [Theory]
    [InlineData(0, 11.113, 1.538, -2.806, 8.886, -3.798, -1.500)]  // shares both bounds with its slur
    [InlineData(1, 17.779, 2.025, -3.132, 15.109, -4.604, -2.000)] // two slurs, one sharing the end
    [InlineData(2, 17.529, 2.343, -6.918, 15.187, -6.918, 0.000)]  // a slur climbing to a''
    public void TheCurveIsLilyPonds(int which, double width,
        double c1x, double c1y, double c2x, double c2y, double endY)
    {
        const string music =
            "c'4@phrasingSlur( d' e' f')@!phrasingSlur | "
            + "e'8@phrasingSlur g'( c'' g') e' g' c''( a')@!phrasingSlur | "
            + "c'8@phrasingSlur d' e'( a'' g'') e' d' c'@!phrasingSlur |";
        string source = Book(music);
        int at = -1;
        for (int i = 0; i <= which; i++)
            at = source.IndexOf("@phrasingSlur", at + 1, StringComparison.Ordinal);

        string svg = SvgGenerator.Generate(SyntaxTree.Parse(source),
            new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false });
        var m = Regex.Match(svg,
            @"d=""M ([\d.-]+),([\d.-]+) C ([\d.-]+),([\d.-]+) ([\d.-]+),([\d.-]+) ([\d.-]+),([\d.-]+) C[^""]*""[^>]*data-pos=""" + at + @"""");
        Assert.True(m.Success, "no curve cites the @ at " + at);
        double[] v = Enumerable.Range(1, 8)
            .Select(g => double.Parse(m.Groups[g].Value, CultureInfo.InvariantCulture)).ToArray();
        const double tol = 0.011; // the SVG's two decimals
        Assert.Equal(width, v[6] - v[0], tol);
        Assert.Equal(c1x, v[2] - v[0], tol);
        Assert.Equal(c1y, v[3] - v[1], tol);
        Assert.Equal(c2x, v[4] - v[0], tol);
        Assert.Equal(c2y, v[5] - v[1], tol);
        Assert.Equal(endY, v[7] - v[1], tol);
    }

    [Fact]
    public void TheCurveCitesBothOfItsAnnotations()
    {
        string source = Book("c'4@phrasingSlur( d' e' f')@!phrasingSlur |");
        int open = source.IndexOf("@phrasingSlur", StringComparison.Ordinal);
        int close = source.IndexOf("@!phrasingSlur", StringComparison.Ordinal);
        string svg = SvgGenerator.Generate(SyntaxTree.Parse(source),
            new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false });
        Assert.Contains($"data-pos=\"{open}\"", svg);
        Assert.Contains(close.ToString(CultureInfo.InvariantCulture), svg);
    }

    /// <summary>
    /// Typing the phrasing slur into a book the editor has already drawn gives the pages a
    /// fresh render gives — the flags are CONTENT (MeasureContentKey) and the `@` positions
    /// ride the tail shift (CollectTailShifter), so no cached measure may answer for the old
    /// text.
    /// </summary>
    [Theory]
    [InlineData("c'4 d'( e') f' | g'( a') b' c'' |", "c'4@phrasingSlur d'( e') f' | g'( a') b' c''@!phrasingSlur |")]
    [InlineData("c'4@phrasingSlur d'( e') f' | g'( a') b' c''@!phrasingSlur |", "c'4 d'( e') f' | g'( a') b' c'' |")]
    [InlineData("c'4@phrasingSlur d'( e') f'@!phrasingSlur | g'( a') b' c'' |", "c'4@phrasingSlur d'( e') f' | g'( a') b' c''@!phrasingSlur |")]
    public void AnEditThatAddsOrMovesAPhrasingSlur_DrawsWhatAFreshRenderDraws(string before, string after)
    {
        var options = new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false };
        var compiler = new IncrementalCompiler(SyntaxTree.Parse(Book(before)), options);
        compiler.RenderIncrementalPages(SyntaxTree.Parse(Book(before)), System.Threading.CancellationToken.None);
        var edited = compiler.RenderIncrementalPages(SyntaxTree.Parse(Book(after)), System.Threading.CancellationToken.None);
        var fresh = new IncrementalCompiler(SyntaxTree.Parse(Book(after)), options)
            .RenderIncrementalPages(SyntaxTree.Parse(Book(after)), System.Threading.CancellationToken.None);
        Assert.Equal(fresh.Pages.Length, edited.Pages.Length);
        for (int p = 0; p < fresh.Pages.Length; p++)
            Assert.Equal(fresh.Pages[p], edited.Pages[p]);
    }

    // ---------------------------------------------------------------- the twins

    [Fact]
    public void TheLilyPondTwinWritesTheParenthesesAfterTheirNotes()
    {
        string ly = new LilyPondExporter().Export(
            SyntaxTree.Parse(Book("c'4@phrasingSlur( d' e' f')@!phrasingSlur |")));
        // `\)` is a POST-event: written before f' it would end the phrasing slur on e'.
        Assert.Contains("c'4\\( (", ly);
        Assert.Contains("f')\\)", ly);
    }

    /// <summary>
    /// A book's MusicXML read back is the same phrasing slur, side included: the importer
    /// reads any <c>&lt;slur&gt;</c> numbered other than 1 as one (a voice cannot nest two
    /// slurs), and until session 483 it folded it into the ordinary slur and lost it.
    /// </summary>
    [Fact]
    public void MusicXmlRoundTrips_ThePhrasingSlurAndItsSide()
    {
        string xml = new MusicXmlExporter().Export(SyntaxTree.Parse(Book(
            "c''4@phrasingSlur.down( d'' e'' f'')@!phrasingSlur | g'4@phrasingSlur a' b' c''@!phrasingSlur |")))
            .ToXml().ToString();
        var report = new LilySharp.Core.MusicXmlImport.ImportReport();
        string lys = LilySharp.Core.MusicXmlImport.LysWriter.Write(
            LilySharp.Core.MusicXmlImport.MusicXmlReader.Read(xml, report), report);
        Assert.Contains("@phrasingSlur.down(", lys);
        Assert.Equal(2, Regex.Matches(lys, "@!phrasingSlur").Count);
        Assert.Equal(2, Regex.Matches(lys, @"@phrasingSlur\b").Count);
        // …and the slur under it is still a slur.
        Assert.Contains(")", lys);
    }

    [Fact]
    public void MusicXmlWritesASlurNumberedApartFromTheSlurs()
    {
        var xml = new MusicXmlExporter().Export(
            SyntaxTree.Parse(Book("c'4@phrasingSlur( d' e' f')@!phrasingSlur |"))).ToXml();
        var slurs = xml.Descendants("slur")
            .Select(s => (string)s.Attribute("type") + (string)s.Attribute("number")).ToList();
        Assert.Equal(new[] { "start1", "start2", "stop1", "stop2" }, slurs.OrderBy(s => s));
    }
}
