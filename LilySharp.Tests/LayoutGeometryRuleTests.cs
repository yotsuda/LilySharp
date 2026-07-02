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
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// LP-mimicry geometry rules pinned at the model/layout level (the rendered
/// counterparts are covered by the SVG snapshots and reviewed through the
/// visual regression harness).
/// </summary>
[Trait("Category", "Unit")]
public class LayoutGeometryRuleTests
{
    private static Score Collect(string music)
    {
        string src = $$"""
            time 4/4
            key c major
            part melody { clef treble }
            section Main { melody { {{music}} } }
            structure { Main }
            score "x" { staff melody }
            """;
        return new MeasureCollector().Collect(SyntaxTree.Parse(src), "melody");
    }

    // --- Slur direction: LILYPOND-REF lily/slur.cc Slur::calc_direction ---

    [Fact]
    public void SlurDirection_FlipsUp_WhenAnyCoveredStemIsDown()
    {
        // c d e are stem-up, b' (above the middle line) is stem-down: LP flips
        // the slur UP as soon as ANY covered stem points down. Deciding by the
        // START note alone curved this slur down, into b''s stem side.
        var slurs = new SlurDetector().DetectSlurs(Collect("c4( d e b') |"));
        var slur = Assert.Single(slurs);
        Assert.True(slur.CurveUp);
    }

    [Fact]
    public void SlurDirection_StaysDown_WhenAllCoveredStemsAreUp()
    {
        // Default DOWN with no down-stems — same result as the old
        // opposite-of-start-stem rule; pinned so the flip stays a flip.
        var slurs = new SlurDetector().DetectSlurs(Collect("c4( d e f) |"));
        var slur = Assert.Single(slurs);
        Assert.False(slur.CurveUp);
    }

    // --- Chord tie stacking: LILYPOND-REF tie-formatting-problem.cc:868-873 ---

    [Fact]
    public void ChordTies_StackInNoteOrder()
    {
        // <c e g>~ <c e g>: three ties, bottom→top. In device Y (down-positive)
        // a HIGHER chord note's tie must sit at a SMALLER Y. The monotonicity
        // penalty used to be inverted (device-Y with the Y-up comparison),
        // biasing the optimizer toward clumped/inverted columns.
        var score = Collect("<c e g>2~ <c e g>2 |");
        var layout = new LayoutEngine().Layout(score);

        var ties = layout.TieLayouts
            .OrderBy(t => t.Tie.StaffPosition) // bottom → top
            .ToArray();
        Assert.Equal(3, ties.Length);
        for (int i = 1; i < ties.Length; i++)
            Assert.True(ties[i].StartY < ties[i - 1].StartY,
                $"tie above staff position {ties[i - 1].Tie.StaffPosition} must sit higher " +
                $"(smaller device Y) than the one below: {ties[i].StartY} vs {ties[i - 1].StartY}");
    }

    // --- Skyline raise: LILYPOND-REF lily/skyline.cc Skyline::raise ---

    [Fact]
    public void SkylineRaise_PreservesSlopes()
    {
        // LP's raise only moves the intercept (y_intercept_ += sky_ * amount);
        // the old implementation rebuilt every building with the FLAT
        // constructor, collapsing a sloped roof onto its intercept. Raise has
        // no production caller yet — pinned so adoption cannot resurrect the
        // corruption silently.
        var sky = VerticalSkyline.FromSlope(0, 0, 10, 5, thickness: 0, VerticalDirection.Up);
        double before0 = sky.Height(0), before10 = sky.Height(10);

        sky.Raise(2);

        Assert.Equal(before10 - before0, sky.Height(10) - sky.Height(0), 6);
        Assert.NotEqual(sky.Height(0), sky.Height(10)); // still sloped
    }

    // --- Tuplet bracket visibility: LILYPOND-REF tuplet-bracket.cc:79-95 ---

    [Fact]
    public void TupletBracket_IsNotHiddenByAnotherVoicesBeam()
    {
        // Voice 1 has an UNBEAMED quarter-note triplet at items 0-2; voice 2
        // has beamed eighths covering the same item range. The if-no-beam
        // check consults the bracket's OWN beam — another voice's beam must
        // not hide this voice's bracket.
        var score = Collect("voice { tuplet 3/2 { c4 d e } r4 r4 | } voice { g8 g g g g g g g | }");
        var layout = new LayoutEngine().Layout(score);

        var bracket = Assert.Single(layout.TupletBracketLayouts);
        Assert.True(bracket.ShowBracket,
            "an unbeamed tuplet's bracket must show even when another voice's beam covers the same items");
    }
}
