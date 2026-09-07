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
/// <param name="BracketBound">True when this rest is the beam's own END — the writer put the
/// <c>[</c> or <c>]</c> on it. A rest that merely drifts outside the visible stems (a
/// degenerate group whose edge note was not beamable) has nothing to hang from and is
/// dropped; one the writer bracketed IS the bound, and LilyPond beams its stem and reaches
/// it (scratch/p345/beambound.ly: <c>r8[ c c c]</c> puts the beam's left edge half a stem
/// thickness past the rest's ink centre).</param>
public sealed record BeamRestStem(
    int ItemIndex, int BeforeMember, int CountLeft, int CountRight,
    int NoteValue = 4, int MeasureIndex = -1, bool PrePositioned = false,
    bool BracketBound = false);

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

    /// <summary>Y of the quanted primary beam line AT THE FIRST MEMBER'S STEM
    /// (<see cref="LeftStemX"/>), in staff positions from the middle line.</summary>
    public double LeftY { get; }

    /// <summary>Y of the quanted primary beam line AT THE LAST MEMBER'S STEM
    /// (<see cref="RightStemX"/>), in staff positions from the middle line.</summary>
    public double RightY { get; }

    /// <summary>The first member's COLUMN anchor x (staff spaces) — or, when the writer
    /// bracketed a rest before it, that rest's invisible stem x, further left.</summary>
    /// <remarks>
    /// ⚠️ A MIXED FRAME, kept for the two readers that want an "outer x" of the beam
    /// without its stem geometry (the skyline band's x-extent, the hidden-bracket tuplet
    /// number's span). It is NOT the x <see cref="LeftY"/> is answered at — that is
    /// <see cref="LeftStemX"/>. Until 2026-09-07 <see cref="OuterEdgeStaffSpaceAtX"/>
    /// interpolated between THESE and the two Y, i.e. in the column-anchor frame; see that
    /// method's remarks for what that cost.
    /// </remarks>
    public double LeftX { get; }

    /// <summary>The last member's COLUMN anchor x (staff spaces) — or a bracketed rest's
    /// invisible stem x after it. See <see cref="LeftX"/>.</summary>
    public double RightX { get; }

    /// <summary>
    /// The x the FIRST MEMBER'S STEM is drawn at (staff spaces): the frame <see cref="LeftY"/>
    /// is answered in (<c>BeamScoringProblem.AtOuterStems</c>), and the left end of the line
    /// <see cref="OuterEdgeStaffSpaceAtX"/> interpolates. <c>MemberXPositions[0]</c> plus the
    /// head's stem attachment — 1.2392 for an up stem on a black head, 0.065 for a down one
    /// (<c>LayoutUtilities.BeamMemberStemX</c>).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/beam-quanting.cc:313-315 init_instance_variables — the stems' own
    ///   x fill <c>stem_xpositions_</c>, and <c>positions</c> (the two Y) are given over
    ///   <c>x_span_</c> from them; LilyPond has no column-anchor frame for a beam at all. A
    ///   bracketed rest at this end widens the beam past this stem (lily/beam.cc:631
    ///   calc_beam_segments) without moving it.
    /// </remarks>
    public double LeftStemX { get; }

    /// <summary>The x the LAST MEMBER'S STEM is drawn at — the frame of
    /// <see cref="RightY"/>. See <see cref="LeftStemX"/>.</summary>
    public double RightStemX { get; }

    /// <summary>X positions for each member (in staff spaces) — the COLUMN anchors, not the
    /// stems; <see cref="MemberStemX"/> turns one into its stem's x.</summary>
    public ImmutableArray<double> MemberXPositions { get; }

    /// <summary>
    /// X positions for each of <see cref="BeamGroup.RestStems"/> (in staff spaces), parallel
    /// to that array — where the rest's INVISIBLE STEM stands: the rest glyph's ink centre
    /// (<c>LayoutUtilities.RestStemX</c>), with no notehead attachment offset, since an
    /// invisible stem has no head to attach beside (LilyPond's <c>Stem::offset_callback</c>
    /// "rests" branch centres it on the rest's extent). Empty when the group runs over no
    /// rests.
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
        double leftStemX,
        double rightStemX,
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
        LeftStemX = leftStemX;
        RightStemX = rightStemX;
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
            LeftStemX, RightStemX,
            MemberXPositions, StaffIndex, SystemIndex, MemberStaffIndices, RestXPositions);

    /// <summary>The same laid-out beam attributed to another system — the stamp
    /// <see cref="SystemIndex"/> moved by <paramref name="delta"/>, nothing else (the X
    /// positions are in the system's own frame, which is the same frame under either
    /// number). The system-count twin of <see cref="WithMeasureIndicesShifted"/>.</summary>
    internal BeamLayout WithSystemIndexShifted(int delta)
        => new(Group, LeftY, RightY, LeftX, RightX, LeftStemX, RightStemX,
            MemberXPositions, StaffIndex, SystemIndex + delta, MemberStaffIndices, RestXPositions);

    /// <summary>
    /// The x member <paramref name="memberIndex"/>'s stem is drawn at — the only x a reader
    /// of the beam face may ask for a member's own tip (<c>NoteColumnLayout.BeamStemX</c>).
    /// <c>MemberXPositions[i]</c> plus the head's stem attachment, per member head shape and
    /// direction (<c>LayoutUtilities.BeamMemberStemX</c>, the renderer's recipe).
    /// </summary>
    public double MemberStemX(int memberIndex)
        => Layout.LayoutUtilities.BeamMemberStemX(Group.Members[memberIndex], MemberXPositions[memberIndex]);

    /// <summary>Slope of the beam line: staff POSITIONS per staff space of x, over the
    /// outer member stems (the frame of <see cref="LeftY"/>/<see cref="RightY"/>).</summary>
    public double Slope => (RightStemX - LeftStemX) > 0.001
        ? (RightY - LeftY) / (RightStemX - LeftStemX)
        : 0;

    /// <summary>The primary beam line's Y (staff positions) at <paramref name="x"/> — a REAL
    /// x (a stem's, a rest's ink centre), interpolated from the outer member stems.</summary>
    public double GetYAtX(double x) => LeftY + Slope * (x - LeftStemX);

    /// <summary>The beam's drawn left end: the leftmost stem it carries — the first member's,
    /// or a rest the writer bracketed before it — less half a stem thickness.
    /// LILYPOND-REF: lily/beam.cc:631 calc_beam_segments — <c>horizontal_[dir] += dir * stem_width / 2</c>.</summary>
    public double DrawnLeftX
    {
        get
        {
            double x = LeftStemX;
            for (int r = 0; r < Group.RestStems.Length && r < RestXPositions.Length; r++)
                if (Group.RestStems[r].BracketBound && Group.RestStems[r].BeforeMember == 0)
                    x = System.Math.Min(x, RestXPositions[r]);
            return x - Svg.EngravingDefaults.StemThickness / 2.0;
        }
    }

    /// <summary>The beam's drawn right end — the twin of <see cref="DrawnLeftX"/>.</summary>
    public double DrawnRightX
    {
        get
        {
            double x = RightStemX;
            for (int r = 0; r < Group.RestStems.Length && r < RestXPositions.Length; r++)
                if (Group.RestStems[r].BracketBound && Group.RestStems[r].BeforeMember == Group.Members.Length)
                    x = System.Math.Max(x, RestXPositions[r]);
            return x + Svg.EngravingDefaults.StemThickness / 2.0;
        }
    }

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
    /// <remarks>
    /// <para>
    /// ⚠️ <paramref name="x"/> IS A REAL X — a drawn stem's (<see cref="MemberStemX"/>), a
    /// beamed rest's ink centre (<see cref="RestXPositions"/>) — and the line is interpolated
    /// between the outer MEMBER STEMS (<see cref="LeftStemX"/>/<see cref="RightStemX"/>),
    /// which is where the two Y were answered. Beyond the drawn ends
    /// (<see cref="DrawnLeftX"/>/<see cref="DrawnRightX"/>) it clamps: there is no beam there.
    /// </para>
    /// <para>
    /// UNTIL 2026-09-07 THIS INTERPOLATED BETWEEN THE COLUMN ANCHORS <see cref="LeftX"/>/
    /// <see cref="RightX"/> — the two Y attributed to x's one stem attachment LEFT of where
    /// they were true. That frame is shifted from the stems' by one attach at both ends, so
    /// a reader that handed in a member's ANCHOR got that member's stem tip exactly (the
    /// shift cancelled), while every reader that handed in a real x — the slur's stem
    /// attachment (ElementCoordinator.TryGetBeamedStemTipDeviceY), the tuplet's bounding
    /// rest stem, the rest-collision shift — read the line one attach further right: off by
    /// slope × attach, 1.2392 for an up stem. The tuplet engraver then "corrected" its note
    /// tips by slope × attach for a shift they had never suffered, and the follow-beam
    /// bracket landed slope × half a stem too deep with its dy exact — MEASURED by ledger
    /// <c>staff.staff.tuplet-bracket-follow-beam</c> (+0.007811 = 0.119904 × 0.065) and
    /// <c>-rest</c> (+0.006569 = 0.1011 × 0.065). One frame, every reader in it, closed both.
    /// </para>
    /// </remarks>
    public double OuterEdgeStaffSpaceAtX(double x, bool stemUp)
    {
        double lo = DrawnLeftX, hi = DrawnRightX;
        double centerPos = GetYAtX(x < lo ? lo : x > hi ? hi : x);              // half-space
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