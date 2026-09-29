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
using LilySharp.Core.Rendering;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using LilySharp.Tests.LpFidelity;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A cue beam and a cue column are the CueVoice's own: the beam 0.35 thick at a length-fraction
/// of magstep(−4) standing on the cue heads, and every part of the column's spacing box —
/// head, stem, flag, accidental — out of the cue font (ly/engraver-init.ly CueVoice). LilyPond
/// 2.26.0, Lab sessions/p691 (cuebeam/cb.lys, cuespace/cs.lys and the b-books beside it).
/// Until session 691 a cue beam was the full-size beam (0.48 thick, 1.06 higher), a cue column
/// spaced as a full-size one wherever a floor decided it (beamed cue sixteenths 1.804 apart
/// where LilyPond has 1.315), and a narrow column before a bar line merged its headroom on the
/// spacing increment instead of its own skyline.
/// </summary>
[Trait("Category", "Unit")]
public sealed class CueBeamSpacingTests
{
    private static RecordingDrawingContext Render(string music)
    {
        var tree = SyntaxTree.Parse($$"""
            octave absolute
            time 4/4
            paper { raggedRight }
            part m {
              section A { {{music}} }
            }
            form main { A }
            score main { staff m }
            """);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        var layout = new LayoutEngine(score.Paper).Layout(score);
        using var doc = new RecordingDocumentContext();
        SharedRenderer.RenderTo(score, layout, doc);
        return doc.Page;
    }

    /// <summary>The distinct head columns, left to right (a chord's heads share one).</summary>
    private static double[] Columns(RecordingDrawingContext page) => page.Glyphs
        .Where(g => g.Glyph == EmmentalerGlyphs.NoteheadBlack || g.Glyph == EmmentalerGlyphs.NoteheadHalf)
        .Select(g => System.Math.Round(g.X, 4)).Distinct().OrderBy(x => x).ToArray();

    /// <summary>
    /// LilyPond 2.26.0 (cuebeam/cb.lys, bar 1 `cue { g8 a b c' d' e' f' g' }`): the two beams
    /// are 0.35 thick, and the first one's centre line runs from 1.125 to 2.000 above the middle
    /// line (Lily# drew 0.48 and 2.23 → 3.1 before session 691).
    /// </summary>
    [Fact]
    public void ACueBeam_IsTheCueVoicesBeam()
    {
        var page = Render("cue { g8 a b c' d' e' f' g' } |");
        double middle = Assert.Single(TwinBeamSweep.StavesOf(page)).Middle;
        var beams = page.Quads.OrderBy(q => q.X0).ToArray();
        Assert.Equal(2, beams.Length);
        foreach (var q in beams)
            Assert.Equal(0.35, System.Math.Abs(q.Y3 - q.Y0), 0.0005);
        var first = beams[0];
        Assert.Equal(1.125, middle - (first.Y0 + first.Y3) / 2, 0.005);
        Assert.Equal(2.000, middle - (first.Y1 + first.Y2) / 2, 0.005);
    }

    /// <summary>
    /// A cue beam's members stand their stems where the quanter stood them: the one recipe
    /// (<c>LayoutUtilities.BeamMemberStemX</c>, through <c>BeamLayout.MemberStemX</c>) reads the
    /// cue heads' attachment, as the quanter was handed it — so every reader of the beam's face
    /// at a member (tuplet brackets, rests under the beam) reads it in the quanter's frame.
    /// </summary>
    [Fact]
    public void ACueBeamsMemberStems_AreTheQuantersStems()
    {
        var tree = SyntaxTree.Parse("""
            octave absolute
            time 4/4
            part m {
              section A { cue { g8 a b c' d' e' f' g' } | }
            }
            form main { A }
            score main { staff m }
            """);
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        var layout = new LayoutEngine(score.Paper).Layout(score);
        foreach (var beam in layout.BeamLayouts)
        {
            Assert.True(beam.Group.IsCue);
            Assert.Equal(beam.LeftStemX, beam.MemberStemX(0), 9);
            Assert.Equal(beam.RightStemX, beam.MemberStemX(beam.Group.Members.Length - 1), 9);
        }
    }

    /// <summary>
    /// LilyPond 2.26.0 (cuespace/cs.lys, raggedRight — the natural spacing): the column gaps of
    /// four cue bars — beamed eighths, quarters with a sharp and a flat, beamed sixteenths into
    /// eighths, and chords — to four digits. The eighth → bar line → quarter gap (2.4764) is the
    /// closing spring's own: its minimum is the cue column's skyline before the stem wishes
    /// merge.
    /// </summary>
    [Fact]
    public void CueColumns_SpaceAsLilyPondSpacesThem()
    {
        var page = Render("""
            cue { g8 a b c' d' e' f' g' } |
            cue { e4 fis4 bes4 c'4 } |
            cue { g16 a b c' d' e' f' g' a'8 b' c''4 } |
            cue { <c e g>4 <d fis a>4 e'2 } |
            """);
        var x = Columns(page);
        double[] lp =
        [
            1.6153, 1.6153, 1.6153, 1.6597, 1.6153, 1.6153, 1.6153, 2.4764,
            2.5134, 2.7006, 2.5134, 3.4527,
            1.3153, 1.3153, 1.3153, 1.3153, 1.3153, 1.3153, 1.3153, 1.3153, 1.6153, 1.6153, 3.4605,
            2.5134, 2.8081,
        ];
        Assert.Equal(lp.Length + 1, x.Length);
        for (int i = 0; i < lp.Length; i++)
            Assert.Equal(lp[i], x[i + 1] - x[i], 0.0015);
    }

    /// <summary>
    /// LilyPond 2.26.0 (cuespace/b8.lys): a cue note with an accidental just after a bar line
    /// stands where LilyPond puts it — the bar line → column minimum reads the cue sharp.
    /// Lily# stood it 0.407 further right before session 691.
    /// </summary>
    [Fact]
    public void ACueAccidentalAfterABarLine_IsTheCueAccidental()
    {
        var page = Render("""
            cue { g8 a b c' d' e' f' g' } |
            cue { fis4 a4 g4 a4 } |
            """);
        var x = Columns(page);
        Assert.Equal(12, x.Length);
        Assert.Equal(3.6194, x[8] - x[7], 0.0015);
    }
}
