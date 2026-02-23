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

namespace LilySharp.Core.Svg.Model;

/// <summary>
/// Represents a tie connecting two notes of the same pitch.
/// A tie is a curved line that connects two notes, indicating
/// that the second note is a continuation of the first.
/// </summary>
public sealed record TieItem
{
    /// <summary>The starting note of the tie.</summary>
    public NoteItem StartNote { get; }

    /// <summary>The ending note of the tie.</summary>
    public NoteItem EndNote { get; }

    /// <summary>Staff position of the tie (same as notes).</summary>
    public int StaffPosition { get; }

    /// <summary>Direction of the tie curve (up or down).</summary>
    public bool CurveUp { get; }

    /// <summary>Measure index where the tie starts.</summary>
    public int StartMeasureIndex { get; }

    /// <summary>Measure index where the tie ends.</summary>
    public int EndMeasureIndex { get; }

    /// <summary>Index of the start note within its measure.</summary>
    public int StartItemIndex { get; }

    /// <summary>Index of the end note within its measure.</summary>
    public int EndItemIndex { get; }

    public TieItem(
        NoteItem startNote,
        NoteItem endNote,
        int staffPosition,
        bool curveUp,
        int startMeasureIndex,
        int endMeasureIndex,
        int startItemIndex,
        int endItemIndex)
    {
        StartNote = startNote;
        EndNote = endNote;
        StaffPosition = staffPosition;
        CurveUp = curveUp;
        StartMeasureIndex = startMeasureIndex;
        EndMeasureIndex = endMeasureIndex;
        StartItemIndex = startItemIndex;
        EndItemIndex = endItemIndex;
    }
}