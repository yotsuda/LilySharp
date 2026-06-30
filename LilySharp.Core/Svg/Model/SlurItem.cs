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
/// Represents a slur connecting multiple notes for phrasing.
/// A slur is a curved line that groups notes together,
/// indicating they should be played legato.
/// </summary>
public sealed record SlurItem
{
    /// <summary>Staff position at start.</summary>
    public int StartStaffPosition { get; }

    /// <summary>Staff position at end.</summary>
    public int EndStaffPosition { get; }

    /// <summary>Direction of the slur curve (up or down).</summary>
    public bool CurveUp { get; }

    /// <summary>Measure index where the slur starts.</summary>
    public int StartMeasureIndex { get; }

    /// <summary>Measure index where the slur ends.</summary>
    public int EndMeasureIndex { get; }

    /// <summary>Index of the start note within its measure.</summary>
    public int StartItemIndex { get; }

    /// <summary>Index of the end note within its measure.</summary>
    public int EndItemIndex { get; }

    public SlurItem(
        int startStaffPosition,
        int endStaffPosition,
        bool curveUp,
        int startMeasureIndex,
        int endMeasureIndex,
        int startItemIndex,
        int endItemIndex)
    {
        StartStaffPosition = startStaffPosition;
        EndStaffPosition = endStaffPosition;
        CurveUp = curveUp;
        StartMeasureIndex = startMeasureIndex;
        EndMeasureIndex = endMeasureIndex;
        StartItemIndex = startItemIndex;
        EndItemIndex = endItemIndex;
    }
}