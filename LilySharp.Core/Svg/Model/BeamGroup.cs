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
        int voiceIndex = 0)
    {
        Members = members;
        MeasureIndex = measureIndex;
        StartIndex = startIndex;
        StemUp = stemUp;
        GrowDirection = Math.Clamp(growDirection, -1, 1);
        VoiceIndex = voiceIndex;
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
    /// Staff position of the note (or lowest note in chord).
    /// Used for beam slope calculation.
    /// </summary>
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
        ImmutableArray<int> memberStaffIndices = default)
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
    }

    /// <summary>Gets the slope of the beam (rise per unit run).</summary>
    public double Slope => (RightX - LeftX) > 0.001
        ? (RightY - LeftY) / (RightX - LeftX)
        : 0;

    /// <summary>Gets the Y position at a given X position.</summary>
    public double GetYAtX(double x) => LeftY + Slope * (x - LeftX);

    /// <summary>
    /// Staff-space Y (Y-UP from the middle line — frame B) of the beam stack's OUTER edge at
    /// <paramref name="x"/>, on the given stem side. This is the single canonical "where a stem
    /// tip reaches" line: the outermost beam's far face = beam centre ± (thickness/2 +
    /// (beamCount-1)·translation). Slur endpoints, scripts, and tuplet brackets that must clear
    /// a beam all measure to THIS line, so they share this one computation instead of each
    /// re-deriving the beam-thickness math. LeftY/RightY are half-space staff positions, so the
    /// centre is halved to staff spaces here.
    /// LILYPOND-REF: lily/stem.cc — a beamed stem ends at the beam it joins (its outer face).
    /// </summary>
    public double OuterEdgeStaffSpaceAtX(double x, bool stemUp)
    {
        double centerPos = x < LeftX ? LeftY : x > RightX ? RightY : GetYAtX(x); // half-space
        double centerSs = centerPos / 2.0;                                       // → staff-space Y-up
        int beamCount = 1;
        foreach (var m in Group.Members)
            beamCount = System.Math.Max(beamCount, m.BeamCount);
        double halfStack = Svg.EngravingDefaults.BeamThickness / 2.0
            + (beamCount - 1) * Svg.EngravingDefaults.BeamTranslation;           // staff-space
        return centerSs + (stemUp ? halfStack : -halfStack);
    }
}