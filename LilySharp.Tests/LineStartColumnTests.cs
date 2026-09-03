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
using LilySharp.Core.Rendering;

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
/// <see cref="MultiStaffLayouter"/> consumes this: the line-start spring's FIXED distance is
/// floored at <c>0.3 + min_dist</c> and its minimum IS <c>min_dist</c>
/// (lily/staff-spacing.cc:210-220, <see cref="LineStartColumn.SpringWithMinimumDistanceFloor"/>).
/// That cannot move a force-0 layout, and measured, it does not move one ledger point that is
/// read ragged; what it moves is compressed lines, where
/// <c>compressed.line-start.time-to-first-note</c> reads it. The break GATE still does not
/// see the floor — docs/HANDOFF.md section 1 carries what that costs.
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
            clefGroupWidth ?? clefInk, keyInk, includeTimeSignature: true, beats.ToString(), beatType.ToString());

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

    // ===================== THE SPRING =====================
    //
    // staff-spacing.cc:161-200 turns the extremal grob's space-alist entry and its own INK
    // extent into (fixed, ideal, stretchability), and :210-220 turns those plus min_dist
    // into the spring. Everything below is COLUMN-relative, the frame LilyPond's dump and
    // BreakAlignSpacing.SpaceAlistDistances both speak, and each assertion subtracts the
    // prefix right where it wants the prefix-relative quantity the measure chain carries.

    /// <summary>
    /// A system whose rows are ALL chord / lyric rows engraves no clef, so the Clef
    /// break-align group is EMPTY and the prefix books nothing at all — not the treble G it
    /// used to fall back to, and not even the 0.8 LeftEdge→Clef gap, since with no clef
    /// there is no such gap (break-alignment-interface.cc:145-146,155-156 skips an empty
    /// group). Booking 6.585 of ink nobody draws is what drove a lead sheet's first column
    /// ~8 ss right of LilyPond's.
    /// </summary>
    [Fact]
    public void StafflessSystem_BooksNoPrefatoryColumnAtAll()
    {
        var empty = SpacingRules.ClefGroupExtent(System.Array.Empty<(double, double)>());
        Assert.Equal(0.0, empty.Left, 6);
        Assert.Equal(0.0, empty.Right, 6);

        var cols = BreakAlignSpacing.SolvePrefixColumns(
            clefWidth: 0.0, keyInkWidth: 0.0, includeTimeSignature: false);
        Assert.Equal(0.0, cols.Right, 6);
        Assert.Equal(0.0, cols.ClefX, 6);
        Assert.False(cols.HasKey);
        Assert.False(cols.HasTime);
    }

    /// <summary>
    /// With no <c>Staff_spacing</c> wish in the left column LilyPond falls to
    /// <c>standard_breakable_column_spacing</c>, which for a <c>dt == 0</c> pair is
    /// <c>ideal = min_dist + 0.5</c> with the default spring strengths. MEASURED
    /// (audit/lp-geometry/probes/staffless-system.ly, scores CO / CO3 / COK): with nothing
    /// prefatory engraved <c>min_dist</c> is 0 and the first chord name of a staff-less
    /// system lands on 0.500000 — identical to 15 digits under 4/4, under 3/4 and under a
    /// 4-sharp key, because a ChordNames context engraves no meter and no signature.
    /// </summary>
    [Fact]
    public void StafflessLineStart_IsStandardBreakableColumnSpacing()
    {
        var spring = LineStartColumn.StandardBreakableColumnSpacing(0.0);
        Assert.Equal(0.500000, spring.IdealDistance, 6);
        Assert.Equal(0.000000, spring.MinDistance, 6);
        // Spring (dist, min_dist) defaults: stretch = ideal, compress = ideal - min.
        Assert.Equal(0.500000, spring.InverseStretchStrength, 6);
        Assert.Equal(0.500000, spring.InverseCompressStrength, 6);

        // The min_dist term is carried, and clamped at 0 like spacing-basic.cc:44.
        Assert.Equal(2.500000, LineStartColumn.StandardBreakableColumnSpacing(2.0).IdealDistance, 6);
        Assert.Equal(0.500000, LineStartColumn.StandardBreakableColumnSpacing(-1.0).IdealDistance, 6);
        Assert.Equal(0.000000, LineStartColumn.StandardBreakableColumnSpacing(-1.0).MinDistance, 6);
    }

    /// <summary>
    /// The keep-inside-line rod: a column whose ink reaches LEFT of it must stand at least
    /// that far from the line-start column, and the rod carries NO padding and no spring term
    /// (lily/simple-spacer.cc:559 <c>add_rod (0, i, -keep_inside_line_[LEFT])</c>). MEASURED
    /// (same probe, CL / CLX / CLL): a syllable reaching 2.312540 left of its column puts the
    /// column on 2.312539, not on that plus the spring's 0.5; remove the reach (CLL) and the
    /// column drops back to the bare 0.500000. The rod lands as a BLOCKING FORCE — the
    /// spring's ideal stays 0.5 and its LENGTH is held at the rod distance at every force
    /// at or below that blocking force, which is what the measured 2.312539 is
    /// (lily/simple-spacer.cc:89-127 add_rod → lily/spring.cc:183-195 set_blocking_force).
    /// </summary>
    [Fact]
    public void KeepInsideLineRod_MovesTheFirstColumnByTheOverhangAlone()
    {
        var chain = System.Collections.Immutable.ImmutableArray.Create(
            LineStartColumn.StandardBreakableColumnSpacing(0.0),
            new Spring(4.0, 2.0, 4.0));

        // No overhang: the line start keeps standard_breakable_column_spacing's 0.5.
        Assert.Equal(0.500000, chain[0].IdealDistance, 6);

        var rodded = SpringSolver.ApplyRods(
            chain, new[] { (Left: 0, Right: 1, Distance: 2.312540) });
        // The spring holds the rod distance at the solved (natural-or-compressed) force…
        Assert.Equal(2.312540, rodded[0].Length(0), 6);
        // …by blocking force, not by an inflated ideal.
        Assert.Equal(0.500000, rodded[0].IdealDistance, 6);
        // The rod does not touch the springs it does not span.
        Assert.Equal(4.000000, rodded[1].IdealDistance, 6);
        Assert.Equal(4.000000, rodded[1].Length(0), 6);
    }

    /// <summary>
    /// The rods are built for EVERY column, not just the first — so the fact that
    /// generalising them moved no output is "already satisfied", not "never generated".
    /// This pins the input side: a chord row hands back one reach PER COLUMN, each the
    /// WHOLE width of the symbol standing there, because the symbol's ink starts at its
    /// column (scm/define-grobs.scm:837-855 — no X-offset, no self-alignment-interface).
    /// </summary>
    [Fact]
    public void KeepInsideLineOverhangs_AreMeasuredForEveryColumnNotJustTheFirst()
    {
        var timings = new List<Fraction>
        {
            Fraction.Zero, new Fraction(1, 2),
        };
        var chords = System.Collections.Immutable.ImmutableArray.Create(
            new ChordNameItem("C", 0, 0, 0, useTiming: true,
                timing: Fraction.Zero, isChordRow: true),
            new ChordNameItem("Bbmaj7", 0, 1, 0, useTiming: true,
                timing: new Fraction(1, 2), isChordRow: true));

        var reach = SpacingRules.ChordInkRightReachPerColumn(ScoreTextMetrics.Bundled, 
            timings, measureIndex: 0, chords, includeAttached: false);

        Assert.Equal(2, reach.Length);
        // ALL of what the renderer draws — the one width home the engraver, the spacing
        // rules and the renderer share (regular series at LilyPond's ChordName em).
        Assert.Equal(ChordNameEngraver.SymbolInkWidth(ScoreTextMetrics.Bundled, "C"), reach[0], 6);
        Assert.Equal(ChordNameEngraver.SymbolInkWidth(ScoreTextMetrics.Bundled, "Bbmaj7"), reach[1], 6);
        // The SECOND column's reach is real and larger — the wide symbol is not on the
        // first column, so a first-column-only rod would never have seen it.
        Assert.True(reach[1] > reach[0]);

        // A symbol in a DIFFERENT measure contributes nothing to this one.
        Assert.Equal(
            new double[] { 0.0, 0.0 },
            SpacingRules.ChordInkRightReachPerColumn(ScoreTextMetrics.Bundled, 
                timings, measureIndex: 1, chords, includeAttached: false));
    }

    /// <summary>
    /// The MUSICAL half of <c>keep_inside_line_</c>, which the rod was missing for one
    /// commit: a column reference point coincides with the note head's LEFT edge, so a plain
    /// head reaches its full width RIGHT of the column and NOTHING left, while a head
    /// carrying an accidental reaches left by the accidental's ink. Both are the bare extent
    /// — <c>extra-spacing-width</c> is read by Separation_item and is not part of a grob's
    /// X-extent, which is what <c>col-&gt;extent (col, X_AXIS)</c> takes.
    /// </summary>
    /// <remarks>
    /// This exists so "the rods moved nothing" stays distinguishable from "the rods were
    /// never generated": the numbers below are the rod distances, and they are not zero.
    /// </remarks>
    [Fact]
    public void KeepInsideLineOverhangs_IncludeTheMusicalInkNotJustTheCentredText()
    {
        var plain = ParseMeasure("c4 d e f |");
        var withAccidental = ParseMeasure("cis4 d e f |");
        var timings = new List<Fraction>
        {
            Fraction.Zero, new Fraction(1, 4), new Fraction(1, 2), new Fraction(3, 4),
        };

        var (plainLeft, plainRight) = SpacingRules.MusicalInkOverhangsPerColumn(
            new[] { plain }, timings);
        var (accLeft, _) = SpacingRules.MusicalInkOverhangsPerColumn(
            new[] { withAccidental }, timings);

        // A plain head hangs nothing left of its column…
        Assert.Equal(0.0, plainLeft[0], 6);
        // …but reaches its whole ink width to the right, on EVERY column — so the right-hand
        // rod is a live constraint everywhere, not an empty list.
        Assert.All(plainRight, r => Assert.True(r > 1.0, $"expected a notehead width, got {r}"));

        // The accidental is what reaches LEFT, and only on the column that carries it.
        Assert.True(accLeft[0] > 1.0,
            $"an accidental should reach past its column; got {accLeft[0]}");
        Assert.Equal(0.0, accLeft[1], 6);
    }

    /// <summary>The first measure of a one-part score, for the overhang tests.</summary>
    private static Measure ParseMeasure(string music)
    {
        var tree = LilySharp.Core.Syntax.SyntaxTree.Parse($$"""
            octave absolute
            time 4/4
            key c major

            part melody

            section Main {
              melody { {{music}} }
            }

            form main { ~Main }

            score main "M" {
              staff melody
            }
            """);
        var spec = LilySharp.Core.Svg.Collector.RenderSpecParser.FindFirst(tree)!;
        var score = LilySharp.Core.Svg.SvgGenerator.CollectScore(tree, spec);
        return score.PrimaryContentStaff.PrimaryVoice.Measures[0];
    }

    /// <summary>The prefix ink right edge — LilyPond's <c>last_ext[RIGHT]</c>.</summary>
    private static double PrefixRight(KeySignature key, bool hasTime)
        => BreakAlignSpacing.SolvePrefixColumns(
            GlyphMetrics.LineStartClefWidth(ClefType.Treble),
            SpacingRules.KeySignatureInkWidth(key), hasTime, "4", "4").Right;

    /// <summary>
    /// SKC's spring, end to end. LilyPond's own numbers for this line start:
    /// <c>fixed</c> 7.585 lifted to <c>0.3 + 7.485 = 7.785</c>, <c>ideal</c> 8.585 (the
    /// natural first-head X probe JN measures on a justified line, so the lift must NOT
    /// move it), <c>min_distance</c> 7.485 — the SKYLINE distance, not the fixed one.
    /// </summary>
    [Fact]
    public void MeteredLineStart_SpringIsLilyPonds()
    {
        double prefixRight = PrefixRight(KeySignature.CMajor, hasTime: true);
        Assert.Equal(6.585000, prefixRight, 6);

        double minDist = MinimumDistance(KeySignature.CMajor, staffPosition: -6);

        // The extremal grob is the meter, whose ink ends at the prefix right; its
        // (first-note . (semi-shrink-space . 2.0)) gives fixed = ink + 1.0, ideal = + 2.0,
        // and no stretchability at all.
        var (fixed_, ideal, stretchability) = BreakAlignSpacing.SpaceAlistDistances(
            BreakAlignSpacing.GetSpacing(
                BreakAlignSymbol.TimeSignature, BreakAlignSymbol.FirstNote),
            prefixRight - GlyphMetrics.GetTimeSigWidth(4, 4), prefixRight);
        Assert.Equal(7.585000, fixed_, 6);
        Assert.Equal(8.585000, ideal, 6);
        Assert.Equal(0.0, stretchability, 6);

        var spring = LineStartColumn.SpringWithMinimumDistanceFloor(
            ideal, fixed_, stretchability, minDist);

        // Directly against the dump.
        Assert.Equal(8.585000, spring.IdealDistance, 6);
        Assert.Equal(7.485000, spring.MinDistance, 6);
        // fixed = ideal - inverse_compress_strength, by :219 — lifted from 7.585 to
        // 0.3 + 7.485.
        Assert.Equal(7.785000, spring.IdealDistance - spring.InverseCompressStrength, 6);
        Assert.Equal(0.0, spring.InverseStretchStrength, 6);
        // What the floor bought: the ideal is untouched (the space-alist's own 8.585, which
        // probe JN reads on a justified line) while the compressibility drops from 1.0 to
        // 0.8. That is the whole observable effect — force 0 does not move, compressed
        // lines do.
        Assert.Equal(0.800000, spring.InverseCompressStrength, 6);
    }

    /// <summary>
    /// A CONTINUATION line start, whose prefix is the clef alone: the floor does NOT bind
    /// there (<c>0.3 + 3.565</c> against a fixed of 5.8), and the spring comes out with
    /// compress strength 0 — RIGID, unable to shrink at any force. That is what LilyPond's
    /// <c>minimum-fixed-space</c> means, and it is why a continuation system's first head
    /// sits on 5.800000 whether the line is justified or not (probe JN, systems 2 and 3).
    /// </summary>
    [Fact]
    public void ClefOnlyLineStart_SpringIsRigidAndTheFloorDoesNotBind()
    {
        double clefInk = GlyphMetrics.LineStartClefWidth(ClefType.Treble);
        double prefixRight = PrefixRight(KeySignature.CMajor, hasTime: false);
        Assert.Equal(3.365000, prefixRight, 6);

        // The prefatory column is the clef alone; the note column is a plain head.
        var notes = FirstNote(staffPosition: -6);
        double nb = double.PositiveInfinity, nt = double.NegativeInfinity;
        foreach (var b in notes)
        {
            nb = System.Math.Min(nb, b.YBottom);
            nt = System.Math.Max(nt, b.YTop);
        }
        var clefBox = GlyphMetrics.ClefG;
        var prefatory = new List<ColumnBox>
        {
            LineStartColumn.PrefatoryBox(
                EngravingDefaults.ClefGlyphXOffset,
                EngravingDefaults.ClefGlyphXOffset + clefInk,
                clefBox.Bottom + TrebleClefLineOffset, clefBox.Top + TrebleClefLineOffset,
                -SpacingRules.DefaultExtraSpacingWidth, SpacingRules.DefaultExtraSpacingWidth,
                StaffBottom, StaffTop, nb, nt),
        };
        double minDist = LineStartColumn.MinimumDistance(prefatory, notes);
        Assert.Equal(3.565000, minDist, 6);   // clef ink right 3.365 + 0.1 - (0 - 0.1)

        // minimum-fixed-space 5.0 off the clef's own INK LEFT (0.8), absorbing its 2.565
        // width: fixed = ideal = 0.8 + max (2.565, 5.0).
        var (fixed_, ideal, stretchability) = BreakAlignSpacing.SpaceAlistDistances(
            BreakAlignSpacing.GetSpacing(BreakAlignSymbol.Clef, BreakAlignSymbol.FirstNote),
            EngravingDefaults.ClefGlyphXOffset,
            EngravingDefaults.ClefGlyphXOffset + clefInk);
        Assert.Equal(5.800000, fixed_, 6);
        Assert.Equal(5.800000, ideal, 6);
        Assert.Equal(0.0, stretchability, 6);   // is_stretchable, but ideal == fixed

        var spring = LineStartColumn.SpringWithMinimumDistanceFloor(
            ideal, fixed_, stretchability, minDist);

        Assert.Equal(5.800000, spring.IdealDistance, 6);
        Assert.Equal(3.565000, spring.MinDistance, 6);
        Assert.Equal(0.0, spring.InverseStretchStrength, 6);
        Assert.Equal(0.0, spring.InverseCompressStrength, 6);
    }

    /// <summary>
    /// A line that OPENS WITH A REPEAT: the <c>.|:</c> is the LAST column of the line-start
    /// break-align group — after the meter (scm/define-grobs.scm:668-683, begin-of-line
    /// order) — and the first-note wish is BarLine's own
    /// <c>(first-note . (semi-shrink-space . 1.3))</c> off the bar's ink, not the meter's 2.0.
    /// LilyPond 2.26.0's dump of audit/lp-geometry/probes/initial-repeat-bar.ly score IR:
    /// TIME 4.885 (ink 1.7), BAR 7.585 (ink 1.84), HEAD 10.725 — i.e. meter right 6.585 +
    /// TimeSignature's (staff-bar . (extra-space . 1.0)), then 1.84, then 1.3.
    /// </summary>
    /// <remarks>
    /// Until session 328 the bar was not in the column at all: the wish was the meter's 2.0
    /// and the measure frame inserted the bar's 1.84 after it, which put the opener 0.15 too
    /// far right and the first head 0.30 too close to it (the owner's report on Lambada's
    /// section C: "|: と最初の音符の x 距離が近すぎる").
    /// </remarks>
    [Fact]
    public void OpeningRepeat_BarColumnAndSpringAreLilyPonds()
    {
        double barInk = EngravingDefaults.BarlineDrawnWidth(BarlineType.RepeatStart);
        Assert.Equal(1.840000, barInk, 6);

        var columns = BreakAlignSpacing.SolvePrefixColumns(
            GlyphMetrics.LineStartClefWidth(ClefType.Treble), 0.0, includeTimeSignature: true,
            "4", "4", staffBarWidth: barInk);
        // The prefix proper still ends on the meter; the bar is its own column after it.
        Assert.Equal(6.585000, columns.Right, 6);
        Assert.True(columns.HasBar);
        Assert.Equal(7.585000, columns.BarX, 6);
        Assert.Equal(1.000000, columns.BarGap, 6);

        // The bar's box (ink ± the default 0.1 esw) is what min_dist now reaches:
        // 9.425 + 0.1 - (0 - 0.1) = 9.625.
        // min_dist reads the head's LEFT reach (-0.1) and the stretched Y bands, so the
        // probe's whole note and this quarter head give the same number.
        var notes = FirstNote(staffPosition: -6);
        var prefatory = Prefatory(KeySignature.CMajor, 4, 4, notes[0].YBottom, notes[0].YTop);
        // The bar line spans the staff and, like every prefatory grob, is stretched to its
        // neighbour — the head below the staff — so its box faces the note column.
        prefatory.Add(LineStartColumn.PrefatoryBox(
            columns.BarX, columns.BarX + barInk, StaffBottom, StaffTop,
            -SpacingRules.DefaultExtraSpacingWidth, SpacingRules.DefaultExtraSpacingWidth,
            StaffBottom, StaffTop, notes[0].YBottom, notes[0].YTop));
        double minDist = LineStartColumn.MinimumDistance(prefatory, notes);
        Assert.Equal(9.625000, minDist, 6);

        // semi-shrink-space 1.3 off the bar's ink: fixed = 9.425 + 0.65, ideal = + 1.3,
        // and no stretch.
        var (fixed_, ideal, stretchability) = BreakAlignSpacing.SpaceAlistDistances(
            BreakAlignSpacing.GetSpacing(BreakAlignSymbol.StaffBar, BreakAlignSymbol.FirstNote),
            columns.BarX, columns.BarX + barInk);
        Assert.Equal(10.075000, fixed_, 6);
        Assert.Equal(10.725000, ideal, 6);
        Assert.Equal(0.0, stretchability, 6);

        var spring = LineStartColumn.SpringWithMinimumDistanceFloor(
            ideal, fixed_, stretchability, minDist);
        // The floor (0.3 + 9.625 = 9.925) does not bind against fixed 10.075, so the head
        // lands on LilyPond's 10.725 and the spring keeps its 0.65 of compressibility.
        Assert.Equal(10.725000, spring.IdealDistance, 6);
        Assert.Equal(9.625000, spring.MinDistance, 6);
        Assert.Equal(0.650000, spring.InverseCompressStrength, 6);
    }

    /// <summary>
    /// A CONTINUATION line opening with a repeat — the owner's case (Lambada's section C):
    /// the prefix is the clef alone, so the bar is spaced off the CLEF by Clef.space-alist's
    /// <c>(staff-bar . (extra-space . 0.7))</c> (scm/define-grobs.scm:916), and with a key
    /// signature off the KEY by KeySignature's <c>(staff-bar . (extra-space . 1.1))</c>
    /// (:1991). Three different gaps for three different last grobs — which is why the
    /// pen reads the column and not one number.
    /// </summary>
    [Fact]
    public void OpeningRepeat_OnAContinuationLine_IsSpacedOffTheClefOrTheKey()
    {
        double clefInk = GlyphMetrics.LineStartClefWidth(ClefType.Treble);
        double barInk = EngravingDefaults.BarlineDrawnWidth(BarlineType.RepeatStart);

        var clefOnly = BreakAlignSpacing.SolvePrefixColumns(
            clefInk, 0.0, includeTimeSignature: false, staffBarWidth: barInk);
        Assert.Equal(3.365000, clefOnly.Right, 6);
        Assert.Equal(0.700000, clefOnly.BarGap, 6);
        Assert.Equal(4.065000, clefOnly.BarX, 6);

        double keyInk = SpacingRules.KeySignatureInkWidth(new KeySignature(2));
        var withKey = BreakAlignSpacing.SolvePrefixColumns(
            clefInk, keyInk, includeTimeSignature: false, staffBarWidth: barInk);
        Assert.Equal(withKey.KeyX + keyInk, withKey.Right, 6);
        Assert.Equal(1.100000, withKey.BarGap, 6);

        // …and the prefix proper is what it was without the bar: the bar is priced through
        // the line-start spring, not booked as prefix width (see PrefixColumns).
        Assert.Equal(
            BreakAlignSpacing.SolvePrefixColumns(clefInk, keyInk, includeTimeSignature: false).Right,
            withKey.Right, 9);
    }

    /// <summary>
    /// With nothing prefatory engraved at all (a rows-only continuation line) the opener
    /// is the FIRST present grob and sits on the left edge — LeftEdge's
    /// <c>(staff-bar . (extra-space . 0.0))</c> (scm/define-grobs.scm:2094) — not on the
    /// clef's 0.8.
    /// </summary>
    [Fact]
    public void OpeningRepeat_WithNoPrefix_SitsOnTheLeftEdge()
    {
        var columns = BreakAlignSpacing.SolvePrefixColumns(
            clefWidth: 0.0, keyInkWidth: 0.0, includeTimeSignature: false,
            staffBarWidth: EngravingDefaults.BarlineDrawnWidth(BarlineType.RepeatStart));
        Assert.Equal(0.0, columns.Right, 6);
        Assert.Equal(0.0, columns.BarX, 6);
        Assert.Equal(0.0, columns.BarGap, 6);
        Assert.True(columns.HasBar);
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
