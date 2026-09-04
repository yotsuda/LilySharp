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

using System.Collections.Immutable;

namespace LilySharp.Core.Svg.Model;

/// <summary>
/// Represents a group of notes connected by a beam.
/// Based on Lilypond's beam representation (beam.cc, beaming-pattern.cc).
/// </summary>
public sealed record BeamGroup
{
    /// <summary>The notes in this beam group (NoteItem or ChordItem).</summary>
    public ImmutableArray<BeamMember> Members { get; }

    /// <summary>
    /// The INVISIBLE stems of this beam — one per rest a manual beam runs over, in left-to-
    /// right order. They carry no head, draw no stem and never reach the quanter's stem
    /// scoring (LilyPond gates that on <c>Stem::is_normal_stem</c>,
    /// lily/beam-quanting.cc:299), but they stand in the beam-segment walk: the beams that
    /// survive over the rest are the ones its clamped count lets through, and the leftovers
    /// become beamlets on the visible neighbours.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/beaming-pattern.cc:33-35 — "Sometimes (for example, if the stem
    /// belongs to a rest and stemlets aren't used) the stem will be invisible."
    /// </remarks>
    public ImmutableArray<BeamRestStem> RestStems { get; }

    /// <summary>The measure index containing this beam group.</summary>
    public int MeasureIndex { get; }

    /// <summary>The start index within the measure's items.</summary>
    public int StartIndex { get; }

    /// <summary>Stem direction for the entire beam group (true = up, false = down).</summary>
    public bool StemUp { get; }

    /// <summary>
    /// Feathered beam grow direction: 0=none, 1=right (accel), -1=left (rit).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: beam.cc:1039-1082 grow-direction
    /// LILYPOND-REF: define-grobs.scm Beam.grow-direction
    /// </remarks>
    public int GrowDirection { get; }

    /// <summary>
    /// Index of the voice this beam belongs to (0 = primary). Beams never cross
    /// voices: automatic beaming groups notes within a single voice, so each
    /// group carries its voice so the engraver resolves member X/Y against
    /// <c>score.Voices[VoiceIndex]</c> and the renderer suppresses flags on the
    /// right voice.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/auto-beam-engraver.cc — one Beam per voice.</remarks>
    public int VoiceIndex { get; }

    /// <summary>Creates a beam group from its members and layout parameters.</summary>
    public BeamGroup(
        ImmutableArray<BeamMember> members,
        int measureIndex,
        int startIndex,
        bool stemUp,
        int growDirection = 0,
        int voiceIndex = 0,
        ImmutableArray<BeamRestStem> restStems = default)
    {
        Members = members;
        MeasureIndex = measureIndex;
        StartIndex = startIndex;
        StemUp = stemUp;
        GrowDirection = Math.Clamp(growDirection, -1, 1);
        VoiceIndex = voiceIndex;
        RestStems = restStems.IsDefault ? ImmutableArray<BeamRestStem>.Empty : restStems;
    }

    /// <summary>
    /// Whether this beam group spans multiple staves (cross-staff beam).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: beam.cc:1451-1459 - Beam::is_cross_staff
    /// A beam is cross-staff if any member has a different TargetStaffIndex.
    /// </remarks>
    public bool IsCrossStaff
    {
        get
        {
            if (Members.Length < 2) return false;
            for (int i = 0; i < Members.Length; i++)
            {
                if (Members[i].TargetStaffIndex >= 0)
                    return true;
            }
            return false;
        }
    }

    /// <summary>Gets the number of notes in this beam group.</summary>
    public int Count => Members.Length;

    /// <summary>The same group under other measure numbers: its own
    /// <see cref="MeasureIndex"/> and every member's and rest stem's EXPLICIT one moved
    /// by <paramref name="delta"/> (the <c>-1</c> "same as the group" sentinel stays).
    /// What a per-system memo hands back when it serves a laid-out beam found under
    /// other measure numbers (<c>SystemLayoutCache</c>).</summary>
    internal BeamGroup WithMeasureIndexShifted(int delta)
    {
        var members = ImmutableArray.CreateBuilder<BeamMember>(Members.Length);
        foreach (var m in Members)
            members.Add(m.WithMeasureIndexShifted(delta));
        var rests = RestStems;
        if (!rests.IsEmpty)
        {
            var rb = ImmutableArray.CreateBuilder<BeamRestStem>(rests.Length);
            foreach (var r in rests)
                rb.Add(r.MeasureIndex < 0 ? r : r with { MeasureIndex = r.MeasureIndex + delta });
            rests = rb.MoveToImmutable();
        }
        return new BeamGroup(members.MoveToImmutable(), MeasureIndex + delta, StartIndex,
            StemUp, GrowDirection, VoiceIndex, rests);
    }

    /// <summary>
    /// Whether this beam is a kneed beam (stems change direction within the group).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: beam.cc:1425-1448 is_knee
    /// </remarks>
    public bool IsKnee
    {
        get
        {
            if (Members.Length < 2) return false;
            bool firstUp = Members[0].MemberStemUp;
            for (int i = 1; i < Members.Length; i++)
            {
                if (Members[i].MemberStemUp != firstUp)
                    return true;
            }
            return false;
        }
    }
}

/// <summary>
/// One invisible stem of a beam group — the stem LilyPond puts over a beamed REST.
/// </summary>
/// <param name="ItemIndex">The rest's index in its measure's items.</param>
/// <param name="BeforeMember">The visible member this rest stands immediately LEFT of —
/// the index in <see cref="BeamGroup.Members"/> the segment walk inserts it before.
/// Interior by construction: a manual bracket opens and closes on a note, so
/// <c>1 &lt;= BeforeMember &lt;= Members.Length - 1</c>.</param>
/// <param name="CountLeft">Beams reaching this stem from the left, after the pattern's
/// invisible-stem clamp (lily/beaming-pattern.cc:471-494 unbeam_invisible_stems).
/// ⚠️ One more LilyPond clamp is NOT ported: lily/beam.cc:1260-1262 (Beam::set_beaming)
/// additionally mins an interior invisible stem's count on each side with its OTHER
/// side's. With Lily#'s option space that line cannot fire: the beamify chip needs a
/// stem whose count EXCEEDS a neighbour's or EQUALS it under a fill-assigned direction,
/// and an invisible stem's clamped count is ≤ both neighbours' with either equality
/// case contradicting the very branch that assigned the direction (worked through
/// 2026-08-06) — so CountLeft == CountRight here always. LilyPond's min exists for the
/// options Lily# cannot set (subdivideBeams, strictBeatBeaming); it comes back with
/// them.</param>
/// <param name="CountRight">Beams leaving it to the right, likewise clamped.</param>
/// <param name="NoteValue">The rest's written denominator (16 for r16) — the glyph whose ink
/// CENTER the invisible stem stands on: LilyPond's stem-over-rest X is the rest's own extent
/// centre (lily/stem.cc:1093-1105 Stem::offset_callback, the "rests" branch), and a beamlet
/// next to the rest is length-capped against that x.</param>
/// <param name="MeasureIndex">The rest's measure; <c>-1</c> = the group's own
/// (<see cref="BeamGroup.MeasureIndex"/>), like <see cref="BeamMember.MeasureIndex"/>.</param>
/// <param name="PrePositioned">True for a rest written at a pitch (<c>a4@rest</c>),
/// which the beam does NOT push: LilyPond's callback returns the chained offset
/// untouched the moment it sees a numeric <c>staff-position</c>, before it has looked
/// at the beam at all. Carried on the stem rather than looked up again because this is
/// where the push is decided.
/// ⚠️ The PURE estimate has no such guard in LilyPond — and so none here either. That
/// asymmetry is LilyPond's: spacing may price a pitched rest under a beam a little
/// away from where it prints.
/// LILYPOND-REF: lily/beam.cc:1336-1338 Beam::rest_collision_callback — the guard;
/// LILYPOND-REF: lily/beam.cc:1421-1494 Beam::pure_rest_collision_callback — without it.</param>
public sealed record BeamRestStem(
    int ItemIndex, int BeforeMember, int CountLeft, int CountRight,
    int NoteValue = 4, int MeasureIndex = -1, bool PrePositioned = false);

/// <summary>
/// Represents a single member of a beam group.
/// </summary>
public sealed record BeamMember
{
    /// <summary>The underlying music item (NoteItem or ChordItem).</summary>
    public MusicItem Item { get; }

    /// <summary>
    /// Number of beam lines at this stem.
    /// 8th=1, 16th=2, 32nd=3, 64th=4, etc.
    /// </summary>
    public int BeamCount { get; }

    /// <summary>
    /// Number of beam lines on the left side of this stem.
    /// Used for partial beams (beamlets).
    /// </summary>
    public int BeamCountLeft { get; }

    /// <summary>
    /// Number of beam lines on the right side of this stem.
    /// Used for partial beams (beamlets).
    /// </summary>
    public int BeamCountRight { get; }

    /// <summary>
    /// Staff position of the note; for a CHORD, the arithmetic mean of its heads
    /// (rounded toward zero) — not a head, and not a quantity LilyPond computes.
    /// </summary>
    /// <remarks>
    /// ⚠️ This is NOT the beam's view of the member: the quanter asks
    /// <see cref="HeadPositionMin"/>/<see cref="HeadPositionMax"/> for the head on the
    /// beam's side, which is what <c>Stem::head_positions (me)[my_dir]</c> and
    /// <c>Stem::chord_start_y</c> both mean (lily/stem.cc:1214, :114-122). The mean used
    /// to flow into the stem-length floor and put a beam over a chord a full staff space
    /// too low. Its ONE remaining reader is the fully-balanced tiebreak in
    /// <c>BeamDetector.DefaultBeamStemUp</c>, where LilyPond sums per-direction far-head
    /// distances instead (lily/beam.cc:913-935) — a divergence that is named but not yet
    /// measured, and the reason this property still exists.
    /// </remarks>
    public int StaffPosition { get; }

    /// <summary>Index of this member in the measure's items.</summary>
    public int ItemIndex { get; }

    /// <summary>
    /// Measure that this beam member lives in. Defaults to <c>-1</c> meaning
    /// "same as the parent <see cref="BeamGroup.MeasureIndex"/>" — the
    /// canonical single-measure beam case. Cross-measure manual beams (via
    /// <c>c8[ ... | ... ]</c>) set this explicitly so the engraver can resolve
    /// each member's X position against the correct MeasureLayout.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/beam.cc — beams may span barlines.</remarks>
    public int MeasureIndex { get; }

    /// <summary>
    /// Per-member stem direction for kneed beams.
    /// For non-kneed beams, matches the group's StemUp.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: beam.cc:894-982 consider_auto_knees
    /// </remarks>
    public bool MemberStemUp { get; }

    /// <summary>
    /// Target staff index for cross-staff notes (-1 = same staff as voice).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: beam.cc:1451-1459 - cross-staff detection via staff symbol comparison
    /// </remarks>
    public int TargetStaffIndex { get; }

    /// <summary>
    /// Lowest notehead staff position (= <see cref="StaffPosition"/> for
    /// single notes; the bottom chord note for chords).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: beam-quanting.cc calc_concaveness — head_positions_[i]
    /// (close/far head per beam direction).
    /// </remarks>
    public int HeadPositionMin { get; }

    /// <summary>
    /// Highest notehead staff position (= <see cref="StaffPosition"/> for
    /// single notes; the top chord note for chords).
    /// </summary>
    public int HeadPositionMax { get; }

    /// <summary>Creates a beam member describing one stem's beaming.</summary>
    public BeamMember(
        MusicItem item,
        int beamCount,
        int beamCountLeft,
        int beamCountRight,
        int staffPosition,
        int itemIndex,
        bool memberStemUp = true,
        int targetStaffIndex = -1,
        int measureIndex = -1,
        int? headPositionMin = null,
        int? headPositionMax = null)
    {
        Item = item;
        BeamCount = beamCount;
        BeamCountLeft = beamCountLeft;
        BeamCountRight = beamCountRight;
        StaffPosition = staffPosition;
        ItemIndex = itemIndex;
        MemberStemUp = memberStemUp;
        TargetStaffIndex = targetStaffIndex;
        MeasureIndex = measureIndex;
        HeadPositionMin = headPositionMin ?? staffPosition;
        HeadPositionMax = headPositionMax ?? staffPosition;
    }

    /// <summary>
    /// Resolves the actual measure index for this member, falling back to the
    /// supplied default when <see cref="MeasureIndex"/> is the sentinel <c>-1</c>.
    /// </summary>
    public int ResolveMeasureIndex(int defaultMeasureIndex)
        => MeasureIndex >= 0 ? MeasureIndex : defaultMeasureIndex;

    /// <summary>The same member with an EXPLICIT measure number moved by
    /// <paramref name="delta"/>; the <c>-1</c> sentinel stays (it follows the group).
    /// See <see cref="BeamGroup.WithMeasureIndexShifted"/>.</summary>
    internal BeamMember WithMeasureIndexShifted(int delta)
        => MeasureIndex < 0
            ? this
            : new BeamMember(Item, BeamCount, BeamCountLeft, BeamCountRight, StaffPosition,
                ItemIndex, MemberStemUp, TargetStaffIndex, MeasureIndex + delta,
                HeadPositionMin, HeadPositionMax);
}

/// <summary>
/// Represents the layout of a beam after position calculation.
/// </summary>
public sealed record BeamLayout
{
    /// <summary>The original beam group.</summary>
    public BeamGroup Group { get; }

    /// <summary>Y position of the beam at the first stem (in staff positions from middle line).</summary>
    public double LeftY { get; }

    /// <summary>Y position of the beam at the last stem (in staff positions from middle line).</summary>
    public double RightY { get; }

    /// <summary>X position of the first stem (in staff spaces).</summary>
    public double LeftX { get; }

    /// <summary>X position of the last stem (in staff spaces).</summary>
    public double RightX { get; }

    /// <summary>X positions for each member (in staff spaces).</summary>
    public ImmutableArray<double> MemberXPositions { get; }

    /// <summary>
    /// X positions for each of <see cref="BeamGroup.RestStems"/> (in staff spaces), parallel
    /// to that array — the rest's COLUMN x, with no notehead attachment offset: an invisible
    /// stem has no head to attach beside (LilyPond's <c>Stem::offset_callback</c> moves a
    /// stem only relative to its heads). Empty when the group runs over no rests.
    /// </summary>
    public ImmutableArray<double> RestXPositions { get; }

    /// <summary>The staff this beam is on.</summary>
    /// <remarks>
    /// ⚠️ TWO PRODUCERS SPELL THIS DIFFERENTLY, and that is documented rather than fixed:
    /// <c>LayoutEngine.LayoutAllSpanners</c> stamps the staff's GLOBAL index, while
    /// <c>MultiStaffLayouter.StaffBeamLayouts</c> lays the staff out on a trivial one-staff
    /// score and stamps 0 (its consumer re-stamps — see that method's remarks). Only the
    /// former's beams are ever SELECTED by staff; the latter's are geometry for one
    /// already-chosen staff.
    /// </remarks>
    public int StaffIndex { get; }

    /// <summary>The system this beam was laid out in — the X positions are in ITS frame.</summary>
    /// <remarks>
    /// ⚠️ CARRIED, NOT RECOVERED, and that is the whole point of the field. LilyPond never
    /// asks this question: a Beam grob hangs off one System's VerticalAxisGroup, so "which
    /// system is this beam in" is answered by its parentage and a score-wide beam list does
    /// not exist to be mis-filtered. Lily# holds a flat per-score array, and for one session
    /// its per-staff consumer selected on the staff alone and read another system's beam ink
    /// (fixed in 50533a8d by recovering the system from the group's measure index — this
    /// field replaces that recovery with the attribution itself).
    /// </remarks>
    public int SystemIndex { get; }

    /// <summary>Whether this beam is a cross-staff beam.</summary>
    public bool IsCrossStaff => Group.IsCrossStaff;

    /// <summary>
    /// Per-member staff indices for cross-staff beams.
    /// Each element is the actual staff index for that beam member.
    /// Empty for non-cross-staff beams.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: beam.cc:1451-1459 - staff symbol comparison per stem
    /// For cross-staff beams, each member may be on a different staff.
    /// The beam line is computed in system-global coordinates.
    /// </remarks>
    public ImmutableArray<int> MemberStaffIndices { get; }

    /// <summary>Creates a computed beam layout for the given beam group.</summary>
    /// <remarks>
    /// ⚠️ <paramref name="staffIndex"/> AND <paramref name="systemIndex"/> HAVE NO DEFAULTS,
    /// deliberately: a beam that does not know where it is cannot be selected, and a
    /// selection that silently matches nothing is the shape of the defect 50533a8d fixed
    /// (the profile read no beams at all on one path and phantom ones on another). Both
    /// producers know both answers at the point they build the grob, which is LilyPond's
    /// shape — a grob is created inside its parent.
    /// </remarks>
    public BeamLayout(
        BeamGroup group,
        double leftY,
        double rightY,
        double leftX,
        double rightX,
        ImmutableArray<double> memberXPositions,
        int staffIndex,
        int systemIndex,
        ImmutableArray<int> memberStaffIndices = default,
        ImmutableArray<double> restXPositions = default)
    {
        Group = group;
        LeftY = leftY;
        RightY = rightY;
        LeftX = leftX;
        RightX = rightX;
        MemberXPositions = memberXPositions;
        StaffIndex = staffIndex;
        SystemIndex = systemIndex;
        MemberStaffIndices = memberStaffIndices.IsDefault ? ImmutableArray<int>.Empty : memberStaffIndices;
        RestXPositions = restXPositions.IsDefault ? ImmutableArray<double>.Empty : restXPositions;
    }

    /// <summary>The same laid-out beam under other measure numbers — the group re-stamped
    /// (<see cref="BeamGroup.WithMeasureIndexShifted"/>), the geometry, the staff and the
    /// system carried as they are. What a per-system memo hands back when it serves a
    /// beam found under other measure numbers (<c>SystemLayoutCache</c>).</summary>
    internal BeamLayout WithMeasureIndicesShifted(int delta)
        => new(Group.WithMeasureIndexShifted(delta), LeftY, RightY, LeftX, RightX,
            MemberXPositions, StaffIndex, SystemIndex, MemberStaffIndices, RestXPositions);

    /// <summary>Gets the slope of the beam (rise per unit run).</summary>
    public double Slope => (RightX - LeftX) > 0.001
        ? (RightY - LeftY) / (RightX - LeftX)
        : 0;

    /// <summary>Gets the Y position at a given X position.</summary>
    public double GetYAtX(double x) => LeftY + Slope * (x - LeftX);

    /// <summary>
    /// Staff-space Y (Y-UP from the middle line — frame B) of the beam stack's edge at
    /// <paramref name="x"/>, on the given side. The quanted LeftY/RightY name the PRIMARY
    /// (rank 0) beam line — the one FURTHEST from the noteheads — and secondary beams stack
    /// from it TOWARD the heads (SharedRenderer.Beams rank walk, LP beam.cc print). So the
    /// STEM-side face is just centre ± thickness/2 wherever a stem tip reaches, while the
    /// HEAD-side face adds the stack: centre ∓ (thickness/2 + (beamCount−1)·translation).
    /// Slur endpoints, scripts, and tuplet brackets that must clear a beam all measure to
    /// this one computation. LeftY/RightY are half-space staff positions, so the centre is
    /// halved to staff spaces here.
    /// Until 2026-08-09 BOTH sides carried the stack term, which pushed every stem-side
    /// consumer one translation too far on a multi-line beam — the 16th-triplet score of
    /// tuplet-number-alignment.ly pinned it (LP numbers sit at the same Y for the 8th and
    /// 16th scores; the 16th number sat 0.81 low here).
    /// LILYPOND-REF: lily/stem.cc — a beamed stem ends at the beam it joins (the primary
    ///   line; LP's drawn stem rect stops at that line's centre, measured tupnumb-lp);
    /// LILYPOND-REF: lily/beam.cc:129-145 get_beam_translation (count-aware from 4 beams).
    /// </summary>
    public double OuterEdgeStaffSpaceAtX(double x, bool stemUp)
    {
        double centerPos = x < LeftX ? LeftY : x > RightX ? RightY : GetYAtX(x); // half-space
        double centerSs = centerPos / 2.0;                                       // → staff-space Y-up
        int beamCount = 1;
        foreach (var m in Group.Members)
            beamCount = System.Math.Max(beamCount, m.BeamCount);
        bool stemSide = stemUp == Group.StemUp;
        double halfStack = Svg.EngravingDefaults.BeamThickness / 2.0
            + (stemSide
                ? 0.0
                : (beamCount - 1) * Svg.EngravingDefaults.BeamTranslationOf(
                    Svg.EngravingDefaults.BeamThickness, 1.0, beamCount));       // staff-space
        return centerSs + (stemUp ? halfStack : -halfStack);
    }
}