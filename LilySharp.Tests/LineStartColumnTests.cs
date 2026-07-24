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

using System.Collections.Generic;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// <c>Paper_column::minimum_distance</c> at a line start, against LilyPond 2.26.0.
/// </summary>
/// <remarks>
/// <para>
/// The expected values are DUMPED from LilyPond by
/// audit/lp-geometry/probes/line-start-mindist.ly (scores SKC / SKD / TKC / TKA), not
/// computed by Lily#. Everything on the Lily# side of each assertion comes from Lily#'s
/// OWN metrics — <see cref="GlyphMetrics"/> and
/// <see cref="BreakAlignSpacing.SolvePrefixColumns"/> — so an agreement means the two
/// engravers place the same ink, not that the test was fed the answer.
/// </para>
/// <para>
/// Nothing consumes <see cref="LineStartColumn"/> in the layout yet: this is step 1 of the
/// merge_springs port (docs/HANDOFF.md section 1), which is deliberately output-invariant.
/// Step 2 floors the line-start spring's fixed distance at <c>0.3 + min_dist</c>
/// (lily/staff-spacing.cc:213) and moves the output.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class LineStartColumnTests
{
    // The staff-local Y frame these tests work in: middle line 0, Y up, so the staff
    // symbol spans positions -4..4 => -2..2 ss. LilyPond's dump is in the system frame
    // with the middle line at -3.800000; every value below is that minus -3.8.
    private const double StaffBottom = -2.0;
    private const double StaffTop = 2.0;

    // A treble clef is drawn anchored on the G line, staff position -2, i.e. 1 ss below
    // the middle line — so its LILC bbox sits 1.0 lower in the staff-local frame.
    // Checked against the dump: ClefG (-2.55 . 4.80) - 1.0 = (-3.55 . 3.80), which is
    // exactly the pure height LilyPond reports for the line-start clef.
    private const double TrebleClefLineOffset = -1.0;

    /// <summary>Boxes for one ordinary notation staff's prefatory column.</summary>
    /// <param name="clefGroupWidth">The Clef break-align GROUP's origin-to-right, which is
    /// the union across the system's staves — pass the treble clef's own width for a
    /// one-staff score, or the TAB clef's when a tab staff widens the group.</param>
    private static List<ColumnBox> Prefatory(
        KeySignature key, int beats, int beatType,
        double neighbourBottom, double neighbourTop,
        double? clefGroupWidth = null)
    {
        double clefInk = GlyphMetrics.LineStartClefWidth(ClefType.Treble);
        double keyInk = SpacingRules.KeySignatureInkWidth(key);
        var columns = BreakAlignSpacing.SolvePrefixColumns(
            clefGroupWidth ?? clefInk, keyInk, includeTimeSignature: true, beats, beatType);

        var boxes = new List<ColumnBox>();
        var clefBox = GlyphMetrics.ClefG;
        boxes.Add(LineStartColumn.PrefatoryBox(
            columns.ClefX, columns.ClefX + clefInk,
            clefBox.Bottom + TrebleClefLineOffset, clefBox.Top + TrebleClefLineOffset,
            // Clef declares no extra-spacing-width, so it takes separation-item.cc:167's
            // default (-0.1 . 0.1).
            -SpacingRules.DefaultExtraSpacingWidth, SpacingRules.DefaultExtraSpacingWidth,
            StaffBottom, StaffTop, neighbourBottom, neighbourTop));

        if (keyInk > 0.0)
            boxes.Add(LineStartColumn.PrefatoryBox(
                columns.KeyX, columns.KeyX + keyInk,
                // The signature's accidentals sit inside the staff; it never binds here
                // (it is LEFT of the meter, whose reach is further right still — the SKD
                // prediction), so its own ink Y only has to be honest, not exact.
                StaffBottom, StaffTop,
                0.0, LineStartColumn.KeySignatureEswRight,
                StaffBottom, StaffTop, neighbourBottom, neighbourTop));

        double timeInk = GlyphMetrics.GetTimeSigWidth(beats, beatType);
        var timeBox = GlyphMetrics.TimeSigCommon;
        boxes.Add(LineStartColumn.PrefatoryBox(
            columns.TimeX, columns.TimeX + timeInk,
            timeBox.Bottom, timeBox.Top,
            0.0, LineStartColumn.TimeSignatureEswRight,
            StaffBottom, StaffTop, neighbourBottom, neighbourTop));
        return boxes;
    }

    /// <summary>
    /// The first note column of one staff: a quarter notehead at
    /// <paramref name="staffPosition"/> plus its stem, both at their ink relative to the
    /// column origin (the notehead's left edge).
    /// </summary>
    private static List<ColumnBox> FirstNote(int staffPosition, string? accidental = null)
    {
        var head = GlyphMetrics.GetNoteheadBBox(4);
        double y = staffPosition / 2.0;
        var boxes = new List<ColumnBox>
        {
            LineStartColumn.FirstNoteBox(0.0, head.Width, y + head.Bottom, y + head.Top),
        };

        if (accidental != null)
        {
            // An Accidental reaches min_dist through Separation_item::conditional_skyline
            // (it is absent from the column's 'elements, paper-column-engraver.cc:259);
            // geometrically that is the same union, so it is a box here.
            var accBox = GlyphMetrics.GetAccidentalBBox(accidental);
            var note = new NoteItem(staffPosition, new Fraction(1, 4), 0, accidental,
                needsLedgerLines: false, sourcePosition: 0);
            var layout = new AccidentalPlacement().CalculateSinglePosition(note);
            Assert.NotNull(layout);
            // XOffset is relative to the notehead's LEFT edge, which IS the column origin.
            double accLeft = layout!.Value.XOffset;
            boxes.Add(LineStartColumn.FirstNoteBox(
                accLeft, accLeft + accBox.Width, y + accBox.Bottom, y + accBox.Top,
                // scm/define-grobs.scm:40 Accidental (extra-spacing-width . (-0.2 . 0.0)).
                -SpacingRules.AccidentalExtraSpacingWidthLeft, 0.0));
        }
        return boxes;
    }

    private static double MinimumDistance(
        KeySignature key, int staffPosition, string? accidental = null,
        double? clefGroupWidth = null)
    {
        var notes = FirstNote(staffPosition, accidental);
        // The neighbours a line-start prefatory grob is stretched to ARE this column
        // (pure-from-neighbor-engraver.cc:110-137 walks the adjacent columns), so the
        // union of these boxes is what PrefatoryY takes.
        double nb = double.PositiveInfinity, nt = double.NegativeInfinity;
        foreach (var b in notes)
        {
            nb = System.Math.Min(nb, b.YBottom);
            nt = System.Math.Max(nt, b.YTop);
        }
        return LineStartColumn.MinimumDistance(
            Prefatory(key, 4, 4, nb, nt, clefGroupWidth), notes);
    }

    /// <summary>
    /// SKC: one notation staff, 4/4, no key, first note c' (one ledger below the staff).
    /// The meter binds: (6.585 ink right + 0.8 esw) - (0.0 - 0.1) = 7.485000.
    /// </summary>
    [Fact]
    public void OneNotationStaff_MatchesLilyPond()
        => Assert.Equal(7.485000, MinimumDistance(KeySignature.CMajor, staffPosition: -6), 6);

    /// <summary>
    /// SKD: the same with \key d \major. The key signature is SHADOWED — it sits LEFT of
    /// the meter, whose own right reach is further right still — so min_dist is the
    /// meter's again, displaced by the key column: 10.135000. Predicted before the dump;
    /// a port that ever took min_dist from the key would be wrong.
    /// </summary>
    [Fact]
    public void KeySignature_IsShadowedByTheMeter()
        => Assert.Equal(10.135000, MinimumDistance(new KeySignature(2), staffPosition: -4), 6);

    /// <summary>
    /// TKA: SKC with a sharp on the first note. The accidental's box reaches
    /// -1.450 - 0.200 = -1.650, so min_dist grows by 1.55 over the plain note's -0.100.
    /// Measured on the notation+tab score, so the meter's reach is TKC's 7.620: 9.270000.
    /// Here the same delta is asserted on the one-staff meter (7.385), which isolates the
    /// accidental from the TAB clef's contribution.
    /// </summary>
    [Fact]
    public void Accidental_ReachesThroughTheConditionalSkyline()
    {
        double plain = MinimumDistance(KeySignature.CMajor, staffPosition: -6);
        double sharp = MinimumDistance(KeySignature.CMajor, staffPosition: -6, "sharp");
        Assert.Equal(1.550000, sharp - plain, 6);
    }

    /// <summary>
    /// TKC: the SAME music with a tab staff under it. The TAB clef is in the Clef
    /// break-align group and is WIDER than the G clef (origin-to-ink-right 2.800 against
    /// 2.565 — <c>clefs.tab</c>'s LILC bbox, which is why it is now generated), so the
    /// group's right edge, the meter column and min_dist all move right by 0.235:
    /// 7.720000, dumped from LilyPond.
    /// <para>
    /// ⚠️ Note the width is the clef's <c>Right</c>, its ORIGIN-to-ink-right, not its ink
    /// width <c>Right - Left</c>. They differ for the TAB clef (2.800 against 2.600)
    /// because LilyPond does NOT shift it onto the column the way it shifts the percussion
    /// clef — its dumped extent is 1.000..3.600 with the grob origin at 0.800.
    /// <see cref="SpacingRules.MaxClefWidth"/> skips tab staves entirely today, which is
    /// this same 0.235 seen from the other side (docs/HANDOFF.md section 2A); fixing it
    /// moves output, so it is step 2's, not this test's.
    /// </para>
    /// </summary>
    [Fact]
    public void TabClef_WidensTheClefGroupAndWithItMinDist()
    {
        double withTab = MinimumDistance(
            KeySignature.CMajor, staffPosition: -6,
            clefGroupWidth: GlyphMetrics.ClefTab.Right);
        Assert.Equal(7.720000, withTab, 6);
        Assert.Equal(0.235000,
            withTab - MinimumDistance(KeySignature.CMajor, staffPosition: -6), 6);
    }

    /// <summary>Where a drawn clef glyph's ink lands, exactly as <c>DrawClef</c> and the
    /// tab renderer compute it: the GROUP's ink-left on the shared column, each clef at
    /// its own stencil offset inside it.</summary>
    private static (double Left, double Right) DrawnInk(
        (double Left, double Right) clef, params (double Left, double Right)[] group)
    {
        double anchor = EngravingDefaults.ClefGlyphXOffset
                        - SpacingRules.ClefGroupExtent(group).Left;
        return (anchor + clef.Left, anchor + clef.Right);
    }

    private static void AssertInk(double left, double right, (double Left, double Right) ink)
    {
        Assert.Equal(left, ink.Left, 6);
        Assert.Equal(right, ink.Right, 6);
    }

    private static double DrawnInkLeft(ClefType clef, params ClefType[] group)
        => DrawnInk(SpacingRules.ClefStencil(clef),
            System.Array.ConvertAll(group, SpacingRules.ClefStencil)).Left;

    /// <summary>
    /// CGP: a percussion clef BESIDE a pitched one. The Clef break-align group's left is
    /// the union — min(0.67, 0) = 0 — so both grobs sit at 0.8 and the percussion clef's
    /// ink stays at 1.470000, which is what LilyPond 2.26.0 dumps. Anchoring each clef's
    /// OWN ink-left on 0.8 (what Lily# did) drags it to 0.800000 instead.
    /// </summary>
    [Fact]
    public void MixedClefs_PlaceEachClefFromTheGROUPsLeftEdge()
    {
        Assert.Equal(0.0, SpacingRules.ClefGroupExtent(new[]
        {
            SpacingRules.ClefStencil(ClefType.Percussion),
            SpacingRules.ClefStencil(ClefType.Treble),
        }).Left, 6);
        Assert.Equal(1.470000,
            DrawnInkLeft(ClefType.Percussion, ClefType.Percussion, ClefType.Treble), 6);
        Assert.Equal(0.800000,
            DrawnInkLeft(ClefType.Treble, ClefType.Percussion, ClefType.Treble), 6);
    }

    /// <summary>
    /// The same rule on a percussion-ONLY system gives the group left 0.67, the grob at
    /// 0.13 and the ink flush on 0.800000 — the placement an earlier session dumped from
    /// LilyPond and read as a per-CLEF rule. Both rules agree here, which is exactly why
    /// the mixed case above went unnoticed; keep this pinned so the group rule is not
    /// "fixed" back into the per-clef one.
    /// </summary>
    [Fact]
    public void PercussionAlone_StillPutsItsInkOnTheColumn()
    {
        Assert.Equal(GlyphMetrics.ClefInkLeft(ClefType.Percussion),
            SpacingRules.ClefGroupExtent(
                new[] { SpacingRules.ClefStencil(ClefType.Percussion) }).Left, 6);
        Assert.Equal(0.800000, DrawnInkLeft(ClefType.Percussion, ClefType.Percussion), 6);
    }

    /// <summary>
    /// The TAB clef is in the group, and both scores LilyPond was dumped on come out
    /// right. Its stencil starts 0.2 right of its origin, so the group's left — and with
    /// it every clef's grob X — depends on whether a notation staff is there too.
    /// </summary>
    /// <remarks>
    /// The reserved column right edge is <c>ClefGlyphXOffset + MaxClefWidth</c>
    /// (BreakAlignSpacing.SolvePrefixColumns) and the drawn ink comes from
    /// <see cref="DrawnInkLeft"/>; asserting BOTH is the point, since booking a width the
    /// renderer does not draw is the failure mode that kept the TAB clef out of the group.
    /// </remarks>
    [Fact]
    public void TabClef_IsInTheGroupAndDrawnWhereItIsBooked()
    {
        var tab = SpacingRules.TabClefStencil;
        var treble = SpacingRules.ClefStencil(ClefType.Treble);
        AssertInk(0.200000, 2.800000, tab);

        // CGT — tab alone: the group is the TAB clef's own (0.2 . 2.8), so the grob sits
        // at 0.6 and the ink runs 0.800000..3.400000. LilyPond's dump exactly.
        AssertInk(0.800000, 3.400000, DrawnInk(tab, tab));

        // TKC — beside a notation staff the group's left is min(0.2, 0) = 0, so the grob
        // sits at 0.8 and the TAB ink runs 1.000000..3.600000, again LilyPond's numbers,
        // while the treble clef stays at 0.800000..3.365000.
        AssertInk(1.000000, 3.600000, DrawnInk(tab, tab, treble));
        AssertInk(0.800000, 3.365000, DrawnInk(treble, tab, treble));

        // …and the width the PREFIX books reaches that same 3.600000, so the reservation
        // and the drawing agree. Booking a width the renderer does not draw is what kept
        // the TAB clef out of the group until its stencil was compared against LilyPond.
        var (l, r) = SpacingRules.ClefGroupExtent(new[] { tab, treble });
        Assert.Equal(3.600000, EngravingDefaults.ClefGlyphXOffset + (r - l), 6);
    }

    /// <summary>
    /// The reserved column width is the GROUP extent, and for every clef set Lily# can
    /// build today that equals the widest clef's ink width — so this rewrite of
    /// <see cref="SpacingRules.MaxClefWidth"/> moves no existing score. (It stops being
    /// true the moment the TAB clef joins the group, which is why that is a separate step.)
    /// </summary>
    [Theory]
    [InlineData(ClefType.Treble)]
    [InlineData(ClefType.Bass)]
    [InlineData(ClefType.Alto)]
    [InlineData(ClefType.Percussion)]
    public void ClefGroupWidth_OfOneClef_IsThatClefsInkWidth(ClefType clef)
    {
        var (left, right) = SpacingRules.ClefGroupExtent(
            new[] { SpacingRules.ClefStencil(clef) });
        Assert.Equal(GlyphMetrics.LineStartClefWidth(clef), right - left, 6);
    }

    /// <summary>
    /// The Y stretch is LOAD-BEARING, not decoration. c' sits below the staff, so with the
    /// prefatory boxes stretched only to the STAFF (positions -4..4) the meter's box would
    /// not face the notehead at all and the stem would bind instead. LilyPond stretches
    /// them to the NEIGHBOURS as well — which at a line start is this very column — so the
    /// notehead is always faced. Removing that stretch must change the answer, or the test
    /// above would pass for the wrong reason.
    /// </summary>
    [Fact]
    public void WithoutTheNeighbourStretch_TheAnswerIsWrong()
    {
        var notes = FirstNote(staffPosition: -6);
        double staffOnly = LineStartColumn.MinimumDistance(
            Prefatory(KeySignature.CMajor, 4, 4, StaffBottom, StaffTop), notes);
        Assert.NotEqual(7.485000, staffOnly, 6);
    }
}
