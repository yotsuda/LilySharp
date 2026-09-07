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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using LilySharp.Tests.LpFidelity;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A MANUAL BEAM MAY OPEN AND CLOSE ON A REST, and the beam then reaches that rest.
/// </summary>
/// <remarks>
/// <para>
/// LilyPond gives every NoteColumn a stem — a rest's included — and beams it like any other,
/// so <c>r8[ c c c]</c> is ordinary notation there. MEASURED 2026-09-07
/// (scratch/p345/beamrest.ly): the Beam's <c>stems</c> come back as
/// (STEMLESS note note note). Lily# refused the bracket (LYS4016 "a manual beam ']' has no
/// '[' open"), discarded the group and beamed the notes automatically instead.
/// </para>
/// <para>
/// LP 2.26.0 ORACLE (scratch/p345/beambound.ly, three books): the invisible stem stands on
/// the REST'S INK CENTRE and the beam reaches half a stem thickness (0.065) past it —
/// <c>r8[ c c c]</c> rest ink 8.585..9.585 (centre 9.085), beam x 9.020..17.0976;
/// <c>c8[ c c r]</c> rest ink 16.0976..17.0976 (centre 16.5976), beam x 9.7592..16.6626.
/// LILYPOND-REF: lily/beam.cc:631 <c>Beam::calc_beam_segments</c> —
///   <c>horizontal_[d] += d * stem_width / 2</c>;
/// LILYPOND-REF: lily/stem.cc:1093-1105 <c>Stem::offset_callback</c> — the "rests" branch
///   puts a rest's stem on the rest's own extent centre.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class BeamOnRestBoundTests
{
    private const string OpensOnRest = """
        octave absolute
        part m { section A { r8[ c' c' c'] | } }
        form main { A }
        score main { staff m }
        """;

    private const string ClosesOnRest = """
        octave absolute
        part m { section A { c'8[ c' c' r8] | } }
        form main { A }
        score main { staff m }
        """;

    // THE CONTROL: the same four columns with a note in the rest's place. Its beam must NOT
    // reach 0.065 past a rest's ink centre, because there is no rest — it is what says the
    // readings below are the rest's doing and not the spacing's.
    private const string NoRest = """
        octave absolute
        part m { section A { c'8[ c' c' c'] | } }
        form main { A }
        score main { staff m }
        """;

    /// <summary>The bracket is accepted: no diagnostic, and the group is the writer's.</summary>
    [Fact]
    public void ABracketMayOpenOnARest_WithoutComplaint()
    {
        var validator = new BeamPairingValidator();
        validator.Validate(SyntaxTree.Parse(OpensOnRest));
        Assert.Empty(validator.Diagnostics
            .Where(d => d.Code == DiagnosticCodes.UnpairedBeam));
    }

    /// <summary>
    /// …and the beam REACHES the rest: its drawn left edge is half a stem thickness past the
    /// rest's ink centre, which is LilyPond's own arithmetic.
    /// </summary>
    [Fact]
    public void TheBeamReachesTheRestItOpensOn_LpExact()
    {
        var (left, _, restCentre) = BeamSpanAndRest(OpensOnRest, EmmentalerGlyphs.Rest8th);
        Assert.Equal(restCentre - EngravingDefaults.StemThickness / 2, left, precision: 6);
    }

    [Fact]
    public void TheBeamReachesTheRestItClosesOn_LpExact()
    {
        var (_, right, restCentre) = BeamSpanAndRest(ClosesOnRest, EmmentalerGlyphs.Rest8th);
        Assert.Equal(restCentre + EngravingDefaults.StemThickness / 2, right, precision: 6);
    }

    /// <summary>
    /// THE CONTROL. With a note where the rest was, the beam ends on that note's stem — so
    /// the two readings above are about the rest and not about where this book's columns sit.
    /// </summary>
    [Fact]
    public void WithNoRest_TheBeamEndsOnTheOuterStems()
    {
        var g = RenderedGeometry.Render(NoRest);
        Assert.Empty(g.Glyphs.Where(q => q.Glyph == EmmentalerGlyphs.Rest8th));

        var (left, right) = BeamSpan(g);
        var stems = g.Lines
            .Where(l => Math.Abs(l.X1 - l.X2) < 1e-9
                        && Math.Abs(l.StrokeWidth - EngravingDefaults.StemThickness) < 1e-9)
            .Select(l => l.X1).OrderBy(x => x).ToList();
        Assert.Equal(4, stems.Count);
        double half = EngravingDefaults.StemThickness / 2;
        Assert.Equal(stems[0] - half, left, precision: 6);
        Assert.Equal(stems[^1] + half, right, precision: 6);
    }

    private static (double Left, double Right, double RestCentre) BeamSpanAndRest(
        string source, char restGlyph)
    {
        var g = RenderedGeometry.Render(source);
        var rest = Assert.Single(g.Glyphs.Where(q => q.Glyph == restGlyph));
        var box = GlyphMetrics.GetRestBBox(8);
        var (left, right) = BeamSpan(g);
        return (left, right, rest.X + (box.Left + box.Right) / 2);
    }

    /// <summary>The drawn beam's X extent — the beam is the only filled quad on these books.</summary>
    private static (double Left, double Right) BeamSpan(RenderedGeometry g)
    {
        var quad = Assert.Single(g.Quads);
        double[] xs = { quad.X0, quad.X1, quad.X2, quad.X3 };
        return (xs.Min(), xs.Max());
    }
}
