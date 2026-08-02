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
using System.Linq;
using LilySharp.Core.Svg;
using Xunit;

namespace LilySharp.Tests.LpFidelity;

/// <summary>
/// The beam quanter and the renderer must stand a stem at the SAME x.
/// </summary>
/// <remarks>
/// <para>
/// A beam is scored against the ink of the grobs it covers, and those grobs' x is measured
/// in the beam's own frame — whose origin is the beam's first STEM, half a stem thickness
/// left of the beam's drawn left edge (lily/beam.cc:631
/// <c>horizontal_[dir] += dir * stem_width / 2</c>). That is a claim about two pieces of
/// code agreeing: <c>ElementCoordinator.BeamStemX</c> on the scoring side and
/// <c>SharedRenderer.DrawBeams</c> on the drawing side. Nothing asserted it, so the day the
/// renderer's attachment moved, every collision would have been measured a notehead width
/// out of frame — silently, because the corpus has no snapshot that meshes stems and the
/// quanter would still have produced A quant for every beam.
/// </para>
/// <para>
/// Both now read <c>LayoutUtilities.StemAttachX</c>. This is the point that says so, in the
/// only terms visible from outside: what got DRAWN. It fails if either side stops using that
/// house, or if the house's value changes without the other side following.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class BeamStemFrameTests
{
    private const string Src = """
        octave absolute
        time 4/4
        key c major

        part m { clef treble }

        section Main { m { g8 g g g g g g g | } }

        form main { ~Main }

        score main "x" { staff m }
        """;

    /// <summary>
    /// A GRACE beam, whose stems attach at another Emmentaler design's head advance
    /// (GraceNoteItem.Font, since 2026-08-02). The quanter is handed that font and the
    /// renderer reads it back through LayoutUtilities.StemX; if either side goes back to
    /// scaling the 20 design's advance, the two frames differ by 0.0056 and this fails.
    /// </summary>
    private const string GraceSrc = """
        octave absolute
        time 4/4
        key c major

        part m { clef treble }

        section Main { m { grace { d'16 e' } f'4 g'2 r4 | } }

        form main { ~Main }

        score main "x" { staff m }
        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ABeamsDrawnEdge_IsHalfAStemThicknessOutsideItsDrawnStem(bool grace)
    {
        var page = RenderedGeometry.Render(grace ? GraceSrc : Src);

        // The stems: vertical strokes at the beam's own thickness. Staff lines and ledger
        // lines are horizontal, so "x1 == x2" is enough to tell them apart.
        double half = EngravingDefaults.StemThickness / 2;
        var stems = page.Lines
            .Where(l => Math.Abs(l.X1 - l.X2) < 1e-9)
            .Select(l => l.X1)
            .OrderBy(x => x)
            .ToList();
        Assert.NotEmpty(stems);

        // The beams: the widest quad of each group. This book beams eighths, one line each.
        var beams = page.Quads
            .Select(q => (Left: Math.Min(q.X0, q.X3), Right: Math.Max(q.X1, q.X2)))
            .OrderBy(q => q.Left)
            .ToList();
        Assert.NotEmpty(beams);

        foreach (var beam in beams)
        {
            Assert.True(
                stems.Any(s => Math.Abs(s - (beam.Left + half)) < 1e-9),
                $"a beam drawn from x={beam.Left:F6} has no stem at {beam.Left + half:F6}. "
                + "The beam's left edge must be half a stem thickness outside its first "
                + "stem, which is the frame the quanter measures covered grobs in "
                + "(ElementCoordinator.CollectBeamCollisions). Stems drawn at: "
                + string.Join(", ", stems.Select(s => s.ToString("F6"))));
            Assert.True(
                stems.Any(s => Math.Abs(s - (beam.Right - half)) < 1e-9),
                $"a beam drawn to x={beam.Right:F6} has no stem at {beam.Right - half:F6}.");
        }
    }
}
