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

using Xunit;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using System.Collections.Immutable;

namespace LilySharp.Tests;

/// <summary>
/// Stored-value tests for <see cref="NoteColumnLayout"/> — the single house of "how far a
/// note column reaches" (HANDOFF §5.2.1②, opened by session 34). Written BEFORE the four
/// consumers were rewired onto the house (§5.4: assert the stored values per path first, so
/// a drift during the move diagnoses itself instead of failing N distant measurements).
/// Expected values are LilyPond's own quantities, not echoes of the implementation.
/// </summary>
[Trait("Category", "Unit")]
public class NoteColumnLayoutTests
{
    private static NoteItem Note(int staffPosition, Fraction duration)
        => new(staffPosition, duration, 0, null, false, 0);

    private static ChordItem Chord(Fraction duration, params int[] staffPositions)
        => new(
            staffPositions.Select(p => new ChordNoteInfo(p, null, false)).ToImmutableArray(),
            duration, 0, 0);

    /// <summary>A flat one-beam layout whose quanted centre sits at 3.0 ss above the middle
    /// (half-space position 6), spanning x 0..10 — enough beam model for the face reads.</summary>
    private static BeamLayout FlatBeamAt6(bool stemUp)
    {
        var e1 = Note(0, Fraction.Eighth);
        var e2 = Note(0, Fraction.Eighth);
        var members = ImmutableArray.Create(
            new BeamMember(e1, 1, 0, 1, 0, 0, memberStemUp: stemUp),
            new BeamMember(e2, 1, 1, 0, 0, 1, memberStemUp: stemUp));
        var group = new BeamGroup(members, 0, 0, stemUp);
        return new BeamLayout(group, leftY: 6, rightY: 6, leftX: 0, rightX: 10,
            ImmutableArray.Create(0.0, 10.0), staffIndex: 0, systemIndex: 0);
    }

    // ── HasStem: Stem::is_normal_stem ─────────────────────────────────────────────

    [Fact]
    public void HasStem_ExistsOnlyForHalfNotesAndShorter()
    {
        // LILYPOND-REF: lily/stem.cc Stem::is_normal_stem — duration-log >= 1. A whole
        // note and a breve have NO stem and must not reserve one.
        Assert.False(NoteColumnLayout.Of(Note(0, Fraction.Whole))!.Value.HasStem);
        Assert.False(NoteColumnLayout.Of(Note(0, new Fraction(2, 1)))!.Value.HasStem); // breve
        Assert.True(NoteColumnLayout.Of(Note(0, Fraction.Half))!.Value.HasStem);
        Assert.True(NoteColumnLayout.Of(Note(0, Fraction.Quarter))!.Value.HasStem);
    }

    // ── The articulation support model ────────────────────────────────────────────

    [Fact]
    public void StemSupportDistance_MiddleLineQuarter_IsTheShortenedTenThirds()
    {
        // A quarter ON the middle line (position 0) takes the unnatural-direction
        // shortening — stem.cc:522's `dir * hp[dir] >= 0` INCLUDES position 0 — so the
        // drawn stem is 3.5 − 1/6 = 10/3, not the raw lengths entry 3.5.
        // LILYPOND-REF: lily/stem.cc:519-555 internal_calc_stem_end_position (shortening).
        // MEASURED: ledger staff.staff.tuplet-bracket-shortened-stem — LilyPond reads the
        // middle-line quarter's encompass as 10/3 + bracket padding 1.100, nine-digit.
        var col = NoteColumnLayout.Of(Note(0, Fraction.Quarter))!.Value; // natural: stem down
        Assert.False(col.StemUp);
        Assert.Equal(10.0 / 3.0, col.StemSupportDistanceDeviceY(), 12);
    }

    [Fact]
    public void StemSupportDistance_WholeNote_IsTheHeadsOwnInk()
    {
        // With no stem the support IS the head, and a NoteHead declares no vertical-skylines
        // — so what side-position measures against is the head's LILC extent, the identical
        // read the encompass model above takes. Asked of GlyphMetrics rather than written
        // down, so a font re-extraction moves the rule and not just one of its two houses.
        // MEASURED, the ledger pair that permitted the move: script.staccato-below and
        // script.marcato-below both read -4.700000 against LilyPond's single -4.745000,
        // which is this head's ink bottom less the script's own padding 0.200000.
        var col = NoteColumnLayout.Of(Note(0, Fraction.Whole))!.Value;
        double headInk = GlyphMetrics.GetNoteheadBBox(1).Top;
        Assert.Equal(headInk, col.StemSupportDistanceDeviceY(), 12);
        // ⚠️ POSITIVE CONTROL: this asserts nothing unless the two quantities differ. The
        // nominal half space is what stood here until 2026-08-05, so a revert must fail.
        Assert.NotEqual(EngravingDefaults.NoteheadHalfHeight, headInk, 12);
    }

    [Fact]
    public void StemSupportDistance_Beamed_EndsOnTheQuantedBeamFace()
    {
        // A beamed stem ends at the beam stack's OUTER face, not the unbeamed formula's
        // tip: quanted centre 3.0 + Beam.thickness/2 = 3.24 above the middle, so the
        // support distance from the middle-line head is 3.24.
        // LILYPOND-REF: scm/define-grobs.scm Beam (beam-thickness . 0.48); lily/stem.cc —
        //   a beamed stem ends at the beam it joins (its outer face).
        var beam = FlatBeamAt6(stemUp: true);
        var col = NoteColumnLayout.Of(
            Note(0, Fraction.Eighth), forcedStemUp: true, beam, beamStemX: 0.0)!.Value;
        Assert.Equal(3.0 + 0.48 / 2.0, col.StemSupportDistanceDeviceY(), 12);
    }

    // ── The column extent (tuplet-bracket encompass model) ────────────────────────

    [Fact]
    public void OutwardTip_MiddleLineQuarter_ReachesTheDrawnShortenedStem()
    {
        // Down-bracket over the middle-line quarter (natural down stem): the encompass
        // point is the DRAWN stem end, 10/3 below the middle = device 2 + 10/3.
        // MEASURED: ledger staff.staff.tuplet-bracket-shortened-stem (LP nine-digit).
        var col = NoteColumnLayout.Of(Note(0, Fraction.Quarter))!.Value;
        Assert.Equal(2.0 + 10.0 / 3.0, col.OutwardTipDeviceY(towardUp: false), 12);
    }

    [Fact]
    public void OutwardTip_StemlessAndAwayStems_ContributeOnlyTheHeadInk()
    {
        // LILYPOND-REF: lily/tuplet-bracket.cc:554-561 calc_position_and_height — a column
        // with no stem on the bracket's side reaches only its head's ink (≈ half a staff
        // space), never a phantom 3.5 stem.
        var whole = NoteColumnLayout.Of(Note(0, Fraction.Whole))!.Value;
        double wholeTip = whole.OutwardTipDeviceY(towardUp: true);
        Assert.InRange(2.0 - wholeTip, 0.2, 1.0); // head ink above the centre, not a stem

        var awayStem = NoteColumnLayout.Of(Note(0, Fraction.Quarter))!.Value; // stem down
        double awayTip = awayStem.OutwardTipDeviceY(towardUp: true); // up-bracket side
        Assert.InRange(2.0 - awayTip, 0.2, 1.0);
    }

    [Fact]
    public void OutwardTip_Beamed_EndsOnTheQuantedBeamFace()
    {
        // Same beam face as the articulation read: device 2.0 − 3.24 = −1.24. The two
        // consumers must agree on the face because it is ONE read in the house.
        var beam = FlatBeamAt6(stemUp: true);
        var col = NoteColumnLayout.Of(
            Note(0, Fraction.Eighth), forcedStemUp: true, beam, beamStemX: 0.0)!.Value;
        Assert.Equal(2.0 - (3.0 + 0.48 / 2.0), col.OutwardTipDeviceY(towardUp: true), 12);
    }

    [Fact]
    public void OutwardTip_Chord_AnchorsOnTheReachSideHead()
    {
        // A chord's encompass anchors on the head NEAREST the bracket (up-bracket → top
        // head), and the stem end is computed from that head — so the tip clears the top
        // head's stem, not the bottom head's.
        // LILYPOND-REF: lily/stem.cc:506-557 — hp[dir] is the tip-side head.
        var col = NoteColumnLayout.Of(
            Chord(Fraction.Quarter, -2, 4), forcedStemUp: true)!.Value;
        Assert.Equal(4, col.HeadPositionToward(true));
        // Stem-up quarter from head +4: shortened by 1/3·(1+4)/2 = 5/6 → length 8/3;
        // tip = head 2.0ss + 8/3 above the middle = device 2 − (2 + 8/3).
        Assert.Equal(2.0 - (2.0 + (3.5 - 5.0 / 6.0)), col.OutwardTipDeviceY(towardUp: true), 9);
    }

    // ── The trill support model ───────────────────────────────────────────────────
    //
    // SupportEdge_StemSide_IsTheDrawnStemEnd and SupportEdge_HeadSide_IsTheGlyphInk are
    // GONE (2026-07-30, session 39) with the read they pinned: SupportEdgeUp was the
    // SCALAR support edge, and ledger trill.x.{glyph,wave}-zone measured that LilyPond's
    // aligned_side is POINTWISE for the trill as well (the same column reads 8.000000
    // under the glyph and imposes nothing under the wave — no scalar answers both), so
    // the trill now builds DynamicEngraver.ColumnSupportSkylines like the dynamics. The
    // claims those two tests carried did not evaporate: the drawn-stem model is pinned by
    // OutwardTip_* above (the same house the support read converted), and the head's LILC
    // ink by DynamicSupportPointwiseTests on the pointwise side.

    // ── The skyline seed's length model ───────────────────────────────────────────

    [Fact]
    public void RendererStemLength_TakesTheShortening_ButNotTheClamps()
    {
        // The seed's length rule: details.lengths less the unnatural-direction shortening
        // (middle line INCLUDED), without CalculateStemEndY's middle-line extension and
        // minimum floor — those bind only inside the staff, which the staff-symbol seed
        // covers. LILYPOND-REF: lily/stem.cc:506-557 internal_calc_stem_end_position.
        Assert.Equal(10.0 / 3.0,
            NoteColumnLayout.RendererStemLength(stemUp: false, noteValue: 4, headPosition: 0), 12);
        // A natural-direction stem (up stem, head below the middle) is NOT shortened.
        Assert.Equal(3.5,
            NoteColumnLayout.RendererStemLength(stemUp: true, noteValue: 4, headPosition: -6), 12);
    }

    // ── Non-column items stay with the consumers ──────────────────────────────────

    [Fact]
    public void Of_ReturnsNullForNonColumns()
    {
        // A rest has no head or stem: its support/skyline contributions (the ±1.0 support
        // edge, the RestHeight box) are consumer models, not column reach.
        Assert.Null(NoteColumnLayout.Of(new RestItem(Fraction.Quarter, 0, 0)));
    }
}
