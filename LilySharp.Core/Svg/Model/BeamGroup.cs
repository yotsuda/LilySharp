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

    public BeamGroup(
        ImmutableArray<BeamMember> members,
        int measureIndex,
        int startIndex,
        bool stemUp,
        int growDirection = 0)
    {
        Members = members;
        MeasureIndex = measureIndex;
        StartIndex = startIndex;
        StemUp = stemUp;
        GrowDirection = Math.Clamp(growDirection, -1, 1);
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

    public BeamMember(
        MusicItem item,
        int beamCount,
        int beamCountLeft,
        int beamCountRight,
        int staffPosition,
        int itemIndex,
        bool memberStemUp = true,
        int targetStaffIndex = -1)
    {
        Item = item;
        BeamCount = beamCount;
        BeamCountLeft = beamCountLeft;
        BeamCountRight = beamCountRight;
        StaffPosition = staffPosition;
        ItemIndex = itemIndex;
        MemberStemUp = memberStemUp;
        TargetStaffIndex = targetStaffIndex;
    }
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

    /// <summary>Staff index for multi-staff scores (-1 for single-staff).</summary>
    public int StaffIndex { get; }

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

    public BeamLayout(
        BeamGroup group,
        double leftY,
        double rightY,
        double leftX,
        double rightX,
        ImmutableArray<double> memberXPositions,
        int staffIndex = -1,
        ImmutableArray<int> memberStaffIndices = default)
    {
        Group = group;
        LeftY = leftY;
        RightY = rightY;
        LeftX = leftX;
        RightX = rightX;
        MemberXPositions = memberXPositions;
        StaffIndex = staffIndex;
        MemberStaffIndices = memberStaffIndices.IsDefault ? ImmutableArray<int>.Empty : memberStaffIndices;
    }

    /// <summary>Gets the slope of the beam (rise per unit run).</summary>
    public double Slope => (RightX - LeftX) > 0.001
        ? (RightY - LeftY) / (RightX - LeftX)
        : 0;

    /// <summary>Gets the Y position at a given X position.</summary>
    public double GetYAtX(double x) => LeftY + Slope * (x - LeftX);
}