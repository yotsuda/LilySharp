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

    /// <summary>
    /// The SAME two books but for the other voice's second beat: one where the down-stem
    /// beam's first note shares its column with an up-stem note, one where it stands alone.
    /// The middle-line <c>bes8.</c> is the note under test in both.
    /// </summary>
    private const string CollidedBeamSrc = """
        time 2/4

        part melody {
          section A { voice { ges' f } { aes' bes8. aes16 } }
        }

        form main { ~A }

        score main { staff melody }
        """;

    private const string LoneBeamSrc = """
        time 2/4

        part melody {
          section A { voice { ges' r } { aes' bes8. aes16 } }
        }

        form main { ~A }

        score main { staff melody }
        """;

    /// <summary>
    /// A BEAMED stem follows its own head's note-collision shift, the way an unbeamed one
    /// already does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// LILYPOND-REF: lily/note-collision.cc:467-468 <c>done[i]-&gt;translate_axis</c> — LilyPond
    /// shifts the whole <c>Note_column</c>, and the stem is IN that column, so no LilyPond
    /// stem can be left behind by its head. Lily# applies the shift at render time
    /// (<c>SharedRenderer.EnumerateStaffItems</c>), which the UNBEAMED stem rides because it
    /// is drawn from that same already-shifted x — but the beamed stem is drawn from
    /// <c>BeamLayout.MemberXPositions</c>, built in <c>ElementCoordinator.LayoutBeams</c>
    /// from the measure layout alone.
    /// </para>
    /// <para>
    /// MEASURED (LilyPond 2.26.0, twin of this book): LilyPond draws the dotted eighth's head
    /// at 24.0881 and its stem at 24.1531 — 0.0650 apart, its attachment, collision or no
    /// collision (the twin of <see cref="LoneBeamSrc"/> puts BOTH at the identical x, so the
    /// down column is the one LilyPond does not move). Lily# drew head 14.77 / stem 15.10 —
    /// 0.33 apart, the stem left standing on the 0.26 the head had moved off, which put it
    /// inside the OTHER voice's notehead.
    /// </para>
    /// <para>
    /// Stated as an identity so it needs no attachment constant and survives a font change:
    /// head-to-stem cannot depend on whether another voice shares the column. The positive
    /// control is the second assertion — the collision must actually MOVE that head between
    /// the two books, or a book with no collision at all would satisfy the first one.
    /// </para>
    /// <para>
    /// ⚠️ WHY NO EXISTING NET SAW THIS, and why the identity is the shape it has to take: a
    /// collided column's two heads sit ONE ATTACHMENT apart, so the stem left behind lands
    /// exactly on the OTHER voice's head. Every check phrased as "each stem stands on SOME
    /// notehead" — a page-geometry sweep, or
    /// <c>SharedRendererBeamTests.MultiStaff_BeamStems_SitUnderColumnAlignedNoteheads</c>,
    /// which asks each MemberXPosition to match SOME notehead X — is satisfied by the wrong
    /// head and reports clean. MEASURED, session 189: a corpus sweep written that way found
    /// 0 orphans in the very output this point was written against. What decides it is
    /// OWNERSHIP, and geometry alone does not carry it; the two books pin ownership by
    /// construction, since it is the same note in both.
    /// </para>
    /// </remarks>
    [Fact]
    public void ACollidedBeamsStem_StandsOnItsOwnShiftedHead_NotOnTheColumnItLeft()
    {
        var (collidedHead, collidedStem, collidedDump) = MiddleLineHeadAndStem(CollidedBeamSrc);
        var (loneHead, loneStem, loneDump) = MiddleLineHeadAndStem(LoneBeamSrc);

        // Positive control FIRST: without a real shift the identity below is vacuous.
        Assert.True(
            Math.Abs(collidedHead - loneHead) > 0.2,
            $"the colliding book drew the middle-line head at x={collidedHead:F6} and the "
            + $"lone book at x={loneHead:F6} — the note-collision shift this point is about "
            + "did not happen, so its identity would hold for an empty reason.\n"
            + collidedDump);

        Assert.Equal(loneStem - loneHead, collidedStem - collidedHead, 9);
    }

    /// <summary>
    /// The one middle-line notehead of these books and the down stem hanging from it, plus the
    /// drawn-geometry dump for a failure message.
    /// </summary>
    private static (double HeadX, double StemX, string Dump) MiddleLineHeadAndStem(string src)
    {
        var page = RenderedGeometry.Render(src);
        double middle = page.StaffRefpoints()[0];

        var head = page.Glyphs
            .Where(g => g.Glyph == EmmentalerGlyphs.NoteheadBlack
                        && Math.Abs(g.Y - middle) < 1e-6)
            .Single();

        // The down stem hanging from it: a vertical stroke of the beam's own thickness whose
        // UPPER end is at the head (the attachment is a fraction of a staff space below the
        // head's centre) and whose lower end is further down the page.
        var stem = page.Lines
            .Where(l => Math.Abs(l.X1 - l.X2) < 1e-9
                        && Math.Abs(l.StrokeWidth - EngravingDefaults.StemThickness) < 1e-9
                        && Math.Abs(Math.Min(l.Y1, l.Y2) - head.Y) < 0.6
                        && Math.Max(l.Y1, l.Y2) > head.Y)
            .Select(l => l.X1)
            .Single();

        return (head.X, stem, "Drawn geometry:\n" + page.Describe());
    }
}
