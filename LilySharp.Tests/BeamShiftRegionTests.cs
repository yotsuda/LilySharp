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

using LilySharp.Tests.LpFidelity;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The beam quanter's region shift (HANDOFF R9(a), session 572) and the one beam the port
/// exposed as never LilyPond's.
/// </summary>
[Trait("Category", "Unit")]
public class BeamShiftRegionTests
{
    /// <summary>
    /// A KNEE's feasible left point is two-sided — up stems bound it from below, down stems
    /// from above — and a seed outside it goes to its CENTRE (point_in_interval), not onto the
    /// side of the beam's nominal direction.
    /// </summary>
    /// <remarks>
    /// LilyPond 2.26.0 on audit/lpreg/slurdot.ly (input/regression/slur-dot-collision.ly, the
    /// book below exported), Beam.positions dumped: <c>(-3.5 . -5.5)</c>. Lily# drew
    /// (−3.00, −5.19) while it clamped the seed to one side.
    /// LILYPOND-REF: lily/beam-quanting.cc:777-890 shift_region_to_valid, :440-450 point_in_interval.
    /// </remarks>
    [Fact]
    public void AKneeSeedOutsideItsFeasibleInterval_GoesToTheCentre()
    {
        var g = RenderedGeometry.Render("""
            part melody

            section A {
              melody {
                e''16.( e,,32)
              }
            }

            form main { ~A }

            score main { staff melody }
            """);

        Assert.Equal(-3.5, g.BeamPositionAboveStaffMiddle(0, rightEnd: false), 6);
        Assert.Equal(-5.5, g.BeamPositionAboveStaffMiddle(0, rightEnd: true), 6);
    }

    /// <summary>
    /// A part-combined staff beams each ENGRAVING voice on its own: the solo eighth and the
    /// apart eighth after it are never one beam.
    /// </summary>
    /// <remarks>
    /// LilyPond 2.26.0 on part-combine-text.ly score PCV (<c>c8 d8</c> against
    /// <c>r8 g'8</c>): every stem flagged, no Beam grob. Lily# drew one beam over the two, and
    /// the region shift ported beside this made that phantom beam jump over the other part's
    /// g′ — which is how the part-combine ledger point caught it.
    /// LILYPOND-REF: ly/music-functions-init.ly:1643-1651 make-directed-part-combine-music;
    /// LILYPOND-REF: ly/engraver-init.ly:359,396 Voice — \consists Auto_beam_engraver.
    /// </remarks>
    [Fact]
    public void ACombinedStaff_NeverBeamsASoloNoteToTheApartNoteAfterIt()
    {
        var g = RenderedGeometry.Render("""
            octave absolute
            part vone { clef bass }
            part vtwo { clef bass }

            section Intro {
              vone { c8 d8 r4 r2 | }
              vtwo { r8 g'8 r4 r2 | }
            }

            form main { Intro }

            score main "PCV" { combinedStaff { vone vtwo } }
            """);

        Assert.Equal(0, g.BeamGroupCount());
    }
}
