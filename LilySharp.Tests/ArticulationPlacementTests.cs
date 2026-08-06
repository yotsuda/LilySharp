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

using System.Linq;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Proposal A: the '.up' / '.down' placement qualifier on '@' annotations
/// (e.g. '@staccato.up') forces an articulation above / below, overriding the
/// automatic (opposite-the-stem) side. It rides the existing '@name(qualifier)'
/// grammar, so the syntax already parsed — only the meaning is new.
/// </summary>
[Trait("Category", "Unit")]
public class ArticulationPlacementTests
{
    private static MultiStaffScore Collect(string src)
    {
        var tree = SyntaxTree.Parse(src);
        Assert.False(tree.HasErrors,
            string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        return SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
    }

    [Fact]
    public void UpDownQualifier_ForcesArticulationSide()
    {
        var score = Collect(
            "part m { clef treble }\n" +
            "section S { m { c'4@staccato.up d'4@staccato.down } }\n" +
            "form main { S }\n" +
            "score main \"o\" { staff m }\n");

        var stac = score.Articulations
            .Where(a => a.Type == ArticulationType.Staccato)
            .OrderBy(a => a.ItemIndex)
            .ToList();

        Assert.Equal(2, stac.Count);
        Assert.True(stac[0].IsAbove, "@staccato.up should be placed above");
        Assert.False(stac[1].IsAbove, "@staccato.down should be placed below");
    }

    [Fact]
    public void QualifierFlipsTheAutomaticSide()
    {
        // The same note: plain '@staccato' takes the automatic side; '.up' / '.down'
        // force the two sides, so at least one of them differs from the automatic one.
        // (This proves the qualifier overrides the default rather than being ignored,
        // without depending on which side the default picks.)
        MultiStaffScore Side(string ann)
        {
            return Collect(
                "part m { clef treble }\n" +
                $"section S {{ m {{ c'4{ann} }} }}\n" +
                "form main { S }\n" +
                "score main \"o\" { staff m }\n");
        }

        bool plain = Side("@staccato").Articulations.Single().IsAbove;
        bool up = Side("@staccato.up").Articulations.Single().IsAbove;
        bool down = Side("@staccato.down").Articulations.Single().IsAbove;

        Assert.True(up);
        Assert.False(down);
        Assert.True(plain == up || plain == down); // plain matches one forced side; the other is a real flip
        Assert.NotEqual(up, down);
    }

    /// <summary>All music-glyph (x, y, char) triples in a rendered SVG.</summary>
    private static List<(double X, double Y, char Glyph)> MusicGlyphs(string svg) =>
        System.Text.RegularExpressions.Regex.Matches(svg,
                "<text class=\"music\" x=\"([-\\d.]+)\" y=\"([-\\d.]+)\"[^>]*>(&#x([0-9A-Fa-f]+);|.)</text>")
            .Select(m => (
                X: double.Parse(m.Groups[1].Value),
                Y: double.Parse(m.Groups[2].Value),
                Glyph: m.Groups[4].Success
                    ? (char)Convert.ToInt32(m.Groups[4].Value, 16)
                    : m.Groups[3].Value[0]))
            .ToList();

    /// <summary>The middle staff line's device Y: the 3rd of the five long horizontals.</summary>
    private static double MiddleLineY(string svg)
    {
        var lineYs = System.Text.RegularExpressions.Regex.Matches(svg,
                "<line x1=\"([-\\d.]+)\" y1=\"([-\\d.]+)\" x2=\"([-\\d.]+)\" y2=\"([-\\d.]+)\"")
            .Where(m => m.Groups[2].Value == m.Groups[4].Value
                && double.Parse(m.Groups[3].Value) - double.Parse(m.Groups[1].Value) > 5)
            .Select(m => double.Parse(m.Groups[2].Value))
            .Distinct().OrderBy(v => v).ToList();
        Assert.Equal(5, lineYs.Count);
        return lineYs[2];
    }

    [Fact]
    public void ForcedUpMarcato_QuantizesIntoTheStaff()
    {
        // The chord-scripts / articulations residual (Δ0.70): a quantize-position
        // script (marcato) snaps its REFPOINT to a staff position and takes NO
        // staff-padding — LilyPond seats a forced-up marcato over c'' at staff
        // POSITION 3, inside the staff, the chevron straddling the top line. Over
        // g'/e'/c' (up stems) the support is the stem tip: 5.4 (past the +5 span
        // gate, unquantized), 5 (rounded 4 = a line, pushed one further), 3.
        // MEASURED against scratch/lpreg/probe-script-y.{ly,svg} — all four exact.
        // LILYPOND-REF: scm/script.scm marcato (quantize-position . #t);
        //   lily/side-position-interface.cc:409-432 quantize_position.
        string svg = LilySharp.Core.Svg.SvgGenerator.Generate(
            SyntaxTree.Parse("time 4/4 c'4@marcato.up g4@marcato.up e4@marcato.up c4@marcato.up"),
            new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false });
        double middle = MiddleLineY(svg);
        var marcatos = MusicGlyphs(svg)
            .Where(g => g.Glyph == '').OrderBy(g => g.X).ToList();
        Assert.Equal(4, marcatos.Count);
        // Device Y-down: origin = middle − (staff-spaces above the middle line).
        Assert.Equal(middle - 1.5, marcatos[0].Y, 2); // c'': position 3, INSIDE
        Assert.Equal(middle - 2.7, marcatos[1].Y, 2); // g': raw 5.4, unquantized
        Assert.Equal(middle - 2.5, marcatos[2].Y, 2); // e': rounded 4 → line → 5
        Assert.Equal(middle - 1.5, marcatos[3].Y, 2); // c': position 3, INSIDE
    }

    [Fact]
    public void Trill_SitsOnTheStaffPaddingRefpointFloor()
    {
        // The articulations-book residual (Δ0.45): the trill glyph's origin IS its
        // ink bottom (font box Bottom 0.000), and the staff-padding floor binds the
        // REFPOINT — LilyPond seats a trill over c'' at exactly staff ink edge +
        // staff-padding = 2.05 + 0.25 = 2.30 above the middle line. The old
        // ornament-fallback box (Bottom −0.5) parked it at 2.75.
        // LILYPOND-REF: lily/side-position-interface.cc:433-453 staff_padding.
        string svg = LilySharp.Core.Svg.SvgGenerator.Generate(
            SyntaxTree.Parse("time 4/4 c'4@trill"),
            new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false });
        double middle = MiddleLineY(svg);
        var trill = Assert.Single(MusicGlyphs(svg).Where(g => g.Glyph == ''));
        Assert.Equal(middle - 2.30, trill.Y, 2);
    }
}
