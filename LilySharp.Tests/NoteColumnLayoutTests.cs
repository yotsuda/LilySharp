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
            ImmutableArray.Create(0.0, 10.0));
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
    public void StemSupportDistance_WholeNote_IsTheNominalHalfHead()
    {
        // The articulation model's no-stem fallback is the NOMINAL 0.5, not the glyph ink
        // — a named, verbatim-preserved deviation (see the house's model table). Changing
        // it to ink is an output-moving port that needs its ledger point first.
        var col = NoteColumnLayout.Of(Note(0, Fraction.Whole))!.Value;
        Assert.Equal(0.5, col.StemSupportDistanceDeviceY(), 12);
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

    // ── The trill support model (drawn stem end, ledger-gated) ────────────────────

    [Fact]
    public void SupportEdge_StemSide_IsTheDrawnStemEnd()
    {
        // The trill's support reads the DRAWN stem end — shortened, middle-line
        // pulled, beam-quanted — via the same house the tuplet encompass reads
        // (OutwardTipDeviceY). PORTED 2026-07-30, gated by ledger
        // trill.{shortened-stem,beam-face,stemless-control}.staff-to-line: LilyPond's
        // support edge is the drawn tip (TLS measured 6.5 where the old raw model
        // said 7.5 — that raw pin retired with the port, as its own comment demanded).
        // FULL SHORTEN: forced-up quarter at +8 → 4.0 + (3.5 − 1.0) = 6.5, the TLS
        // number. LILYPOND-REF: lily/stem.cc:519-555 (shorten when dir*hp[dir] >= 0).
        var forcedUp = NoteColumnLayout.Of(
            Note(8, Fraction.Quarter), forcedStemUp: true)!.Value;
        Assert.Equal(6.5, forcedUp.SupportEdgeUp(up: true), 9);

        // A natural-direction stem is NOT shortened: the up stem from −2 still ends
        // at −1.0 + 3.5 — the raw and drawn models agree off the shortening regimes.
        var note = NoteColumnLayout.Of(Note(-2, Fraction.Quarter))!.Value; // natural: up
        Assert.True(note.StemUp);
        Assert.Equal(-1.0 + 3.5, note.SupportEdgeUp(up: true), 12);

        // ONE house, two frames: the stem side IS OutwardTipDeviceY's model, converted
        // from the staff-top device frame — a second stem model here would be the
        // §5.2.1② shape this record exists to prevent.
        Assert.Equal(
            EngravingDefaults.StaffMiddle - note.OutwardTipDeviceY(towardUp: true),
            note.SupportEdgeUp(up: true), 12);
        var chord = NoteColumnLayout.Of(Chord(Fraction.Quarter, -2, 4))!.Value; // natural: down
        Assert.False(chord.StemUp);
        Assert.Equal(
            EngravingDefaults.StaffMiddle - chord.OutwardTipDeviceY(towardUp: false),
            chord.SupportEdgeUp(up: false), 12);
    }

    [Fact]
    public void SupportEdge_HeadSide_IsTheGlyphInk()
    {
        // The no-stem side reads the head's LILC glyph ink (±0.545 — the extent LilyPond
        // itself dumps for the black head), not the nominal half space. UNCHANGED by the
        // drawn-stem port: the TLW control landed 0 exact on this branch and must not move.
        // LILYPOND-REF: lily/grob.cc:85-89 simple_vertical_skylines_from_extents.
        var note = NoteColumnLayout.Of(Note(-2, Fraction.Quarter))!.Value; // stem up
        Assert.InRange(note.SupportEdgeUp(up: false), -1.56, -1.53);

        // Multi-voice forcing is the CALLER's policy and flips which side has the stem
        // — and the forced (unnatural) direction now takes the drawn shortening:
        // head +4, shorten 1/3·(1+4)/2 = 5/6 (the OutwardTipDeviceY arithmetic above).
        var forcedUp = NoteColumnLayout.Of(
            Chord(Fraction.Quarter, -2, 4), forcedStemUp: true)!.Value;
        Assert.Equal(2.0 + 3.5 - 5.0 / 6.0, forcedUp.SupportEdgeUp(up: true), 12);
    }

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
