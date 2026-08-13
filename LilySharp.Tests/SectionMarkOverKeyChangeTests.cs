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

using System.Text.RegularExpressions;
using LilySharp.Core.Svg;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A section label above a mid-measure key change clears the signature's ink: the
/// change's sharps/naturals are inside-staff grobs (no outside-staff-priority), so
/// the outside-staff pass that places the boxed label must find them in the staff
/// profile it clears.
/// </summary>
/// <remarks>
/// Reported 2026-08-13 (scratch/ベースタブLy/Untitled-3.lys): the B label printed
/// straight ON the a-major sharps, and the A2 label on the cancellation naturals —
/// the key-change glyphs were in NO skyline (the §2A "participant missing from the
/// seed" family: accidental, rest, tie, slur, beam, script, dots… now the key
/// signature). LilyPond lifts the mark over them: on the twin, the B mark rides
/// 0.93 ss higher than the A mark, which has only the staff under it.
/// LILYPOND-REF: lily/axis-group-interface.cc:914-935 skyline_spacing —
///   KeySignature/KeyCancellation carry no outside-staff-priority, so they are in
///   the inside-staff profile the movers clear.
/// </remarks>
[Trait("Category", "Unit")]
public class SectionMarkOverKeyChangeTests
{
    private static string Svg(string form) => SvgGenerator.Generate(
        SyntaxTree.Parse($$"""
            part melody {
              clef treble
              section A { c4 c g' g | a a g2 | f4 f e e | d d c2 | }
              section B { key a major g'4 g f f | e e d2 | }
            }
            form main { {{form}} }
            score main { staff melody }
            """),
        new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false });

    /// <summary>The boxed label's rect (x, y, w, h) whose following bold text is
    /// <paramref name="label"/>.</summary>
    private static (double X, double Y, double W, double H) MarkBox(string svg, string label)
    {
        var m = Regex.Match(svg,
            "<rect x=\"([\\d.-]+)\" y=\"([\\d.-]+)\" width=\"([\\d.]+)\" height=\"([\\d.]+)\"" +
            "[^>]*stroke=\"#000000\"[^>]*/>\\s*" +
            "<text [^>]*font-weight=\"bold\"[^>]*>" + Regex.Escape(label) + "</text>");
        Assert.True(m.Success, $"boxed label '{label}' not found");
        return (double.Parse(m.Groups[1].Value), double.Parse(m.Groups[2].Value),
                double.Parse(m.Groups[3].Value), double.Parse(m.Groups[4].Value));
    }

    /// <summary>Baseline positions of every drawn <paramref name="glyph"/>.</summary>
    private static List<(double X, double Y)> MusicGlyphs(string svg, char glyph) =>
        Regex.Matches(svg,
                "<text class=\"music\" x=\"([\\d.-]+)\" y=\"([\\d.-]+)\"[^>]*>" + glyph + "</text>")
            .Select(m => (double.Parse(m.Groups[1].Value), double.Parse(m.Groups[2].Value)))
            .ToList();

    /// <summary>Asserts the label's box bottom sits above every ink top of the
    /// <paramref name="glyph"/>s horizontally under it.</summary>
    private static void AssertClears(string svg, string label, char glyph, string glyphKind)
    {
        var box = MarkBox(svg, label);
        var under = MusicGlyphs(svg, glyph)
            .Where(g => g.X < box.X + box.W + 1.0 && g.X + 1.2 > box.X)
            .ToList();
        Assert.NotEmpty(under);
        double glyphTopHalf = LilySharp.Core.Svg.Layout.GlyphMetrics
            .GetAccidentalSkylineBBox(glyphKind).Top;
        double highestInkTop = double.MaxValue; // device Y-down: smaller = higher
        foreach (var g in under)
        {
            double inkTop = g.Y - glyphTopHalf; // device Y-down: ink top is ABOVE the baseline
            highestInkTop = Math.Min(highestInkTop, inkTop);
            Assert.True(box.Y + box.H <= inkTop + 1e-6,
                $"label '{label}' bottom {box.Y + box.H:F2} overlaps {glyphKind} ink top {inkTop:F2}");
        }
        // …and it must STAND on that ink, not float above it: bottom = binding ink top −
        // outside-staff padding (0.46), with slack for a taller neighbour glyph just
        // outside the box. The first key-change seed cleared thin air 1.2 ss above the
        // real sharps (an invented "(8 − position)" frame flip — LP twin B sat 2.04
        // above the staff, Lily# 3.21), and the clears-assert alone stayed green.
        Assert.True(box.Y + box.H >= highestInkTop - 1.5,
            $"label '{label}' bottom {box.Y + box.H:F2} floats above {glyphKind} ink top " +
            $"{highestInkTop:F2} by more than padding + slack");
    }

    [Fact]
    public void SectionLabelClearsTheNewSignaturesSharps() =>
        // The reported B: an a-major change opening the section, three sharps, the
        // boxed label straight on them before the seed existed.
        AssertClears(Svg("A B"), "B", LilySharp.Core.Svg.EmmentalerGlyphs.AccidentalSharp,
            "sharp");

    [Fact]
    public void SectionLabelClearsTheCancellationNaturals() =>
        // The reported A2: the reprise cancels a major back to c major — naturals
        // under the label.
        AssertClears(Svg("A B A \"A2\""), "A2",
            LilySharp.Core.Svg.EmmentalerGlyphs.AccidentalNatural, "natural");
}
