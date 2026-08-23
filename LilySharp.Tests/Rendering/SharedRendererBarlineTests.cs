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
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests.Rendering;

/// <summary>
/// The live renderer (SharedRenderer, via SvgGenerator) must draw barline TYPES,
/// not a thin line for every measure: repeats need dots and a thick stroke, and a
/// double barline draws two thin strokes. Previously SharedRenderer drew only a
/// single thin rect per measure. (The last bar of a piece is auto-finalized, so
/// these tests vary a MID-piece barline to isolate the type.)
/// </summary>
public sealed class SharedRendererBarlineTests
{
    private static string Render(string body)
    {
        var source = $$"""
            key C major
            time 4/4

            section Demo {
                line { {{body}} }
            }

            form main { Demo }
            score main "out" { staff line }
            """;
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        return SvgGenerator.Generate(tree, new SvgRenderOptions { EmbedFont = false });
    }

    [Fact]
    public void DoubleBarline_AddsAnExtraThinStroke()
    {
        // Two measures divided by a single vs a double barline mid-piece. The
        // double draws two adjacent thin strokes, so it has one more thin rect.
        var single = Render("| c'4 d e f | g a b c'' |");
        var dbl    = Render("| c'4 d e f || g a b c'' |");

        string thin = $"width=\"{EngravingDefaults.ThinBarlineThickness:F2}\""; // width="0.16"
        Assert.True(CountOccurrences(dbl, thin) == CountOccurrences(single, thin) + 1,
            $"double barline should add exactly one thin stroke " +
            $"(single={CountOccurrences(single, thin)}, double={CountOccurrences(dbl, thin)})");
    }

    [Fact]
    public void Repeat_DrawsDotsAndThickStroke()
    {
        var plain = Render("| c'4 d e f | g a b c'' |");
        var repeat = Render("|: c'4 d e f :| g a b c'' |");

        Assert.DoesNotContain("<circle", plain);
        Assert.Contains("<circle", repeat);   // repeat dots (start + end)
        // The repeat-start/end glyphs include a thick stroke.
        string thick = $"width=\"{EngravingDefaults.ThickBarlineThickness:F2}\"";
        Assert.True(CountOccurrences(repeat, thick) > CountOccurrences(plain, thick),
            "repeat barlines should add thick strokes");
    }

    /// <summary>
    /// The repeat dots straddle the CENTRE of the band their barline spans, whatever that
    /// band's height is — LilyPond translates them to <c>center ± dist/2</c> and never refers
    /// to a top edge (scm/bar-line.scm:360-368). Measured on 2.26.0: the dot pair's centre
    /// sits exactly on the staff centre, one staff space apart.
    /// </summary>
    /// <remarks>
    /// This is the invariant, not a ledger point, because the member that MOVED has no
    /// LilyPond counterpart to measure against: the band is Lily#'s lead-sheet row, which
    /// already diverges from LilyPond's bare row by decision (it prints a meter — HANDOFF §3,
    /// session 226). What LilyPond settles is the RULE, and the rule is what this pins.
    ///
    /// Until session 240 the pair was stored as two constants measured down from the band's
    /// TOP (1.5 / 2.5) — the five-line staff's own half-height folded into the numbers. Every
    /// staff is 4.0 tall, so the whole suite, 222 snapshots and 572 tracked books agreed; a
    /// lyrics row grows by one LyricVerseSpacing per extra verse, and on a two-verse row the
    /// dots sat 1.6 ss above the centre of the barline drawn around them (user report).
    /// </remarks>
    [Theory]
    // A plain five-line staff: the band is 4.0 and the old constants were already right here.
    [InlineData("""
        time 4/4
        part m { clef treble section A { c4 d e f | } section B { g4 a b c' | } }
        form main { A |: B :| }
        score main { staff m }
        """)]
    // A staffless lyrics row with TWO verses: the band is taller than a staff, which is the
    // only shape that can tell "centred" apart from "1.5 below the top".
    [InlineData("""
        time 4/4
        part m { clef treble section A { c4 d e f | } section B { g4 a b c' | } }
        lyrics v {
          section A { one two three four | }
          section B { [~1. five six sev- en |] [~2. eight nine ten e- le- ven |] }
        }
        form main { A |: B :| }
        score main { lyrics v }
        """)]
    public void RepeatDots_StraddleTheCentreOfTheBandTheirBarlineSpans(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        var svg = SvgGenerator.Generate(tree, new SvgRenderOptions { EmbedFont = false });

        var dots = System.Text.RegularExpressions.Regex.Matches(svg,
                "<circle cx=\"([0-9.]+)\" cy=\"([0-9.]+)\" r=\"([0-9.]+)\"")
            .Select(m => (X: double.Parse(m.Groups[1].Value), Y: double.Parse(m.Groups[2].Value)))
            .OrderBy(d => d.X).ThenBy(d => d.Y).ToList();
        Assert.True(dots.Count >= 2, $"expected repeat dots, found {dots.Count}");

        // The barline the first dot pair belongs to: a narrow, tall rect whose vertical span
        // contains both dots. (The page background is wide, note ink is short.)
        var bands = System.Text.RegularExpressions.Regex.Matches(svg,
                "<rect[^>]*x=\"([0-9.-]+)\"[^>]*y=\"([0-9.-]+)\"[^>]*width=\"([0-9.-]+)\"[^>]*height=\"([0-9.-]+)\"")
            .Select(m => (Y: double.Parse(m.Groups[2].Value),
                          W: double.Parse(m.Groups[3].Value),
                          H: double.Parse(m.Groups[4].Value)))
            .Where(r => r.W < 1 && r.H > 2).ToList();

        var (d1, d2) = (dots[0], dots[1]);
        var band = bands.FirstOrDefault(b => b.Y <= d1.Y && b.Y + b.H >= d2.Y);
        Assert.True(band.H > 0, "no barline band contains the dot pair");

        Assert.Equal(band.Y + band.H / 2, (d1.Y + d2.Y) / 2, 3);
        Assert.Equal(2 * EngravingDefaults.RepeatDotHalfSpan, d2.Y - d1.Y, 3);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0, i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0)
        {
            count++;
            i += needle.Length;
        }
        return count;
    }
}
