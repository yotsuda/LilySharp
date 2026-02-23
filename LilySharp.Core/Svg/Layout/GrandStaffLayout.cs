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
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Layout information for a single staff within a system.
/// </summary>
public sealed record StaffLayout(
    int StaffIndex,
    ClefType Clef,
    double Y,           // Y position relative to system top
    double Height,      // Staff height (typically 4 * staffSpace)
    TuningType? Tuning = null,  // Tuning for tablature staves
    string? InstrumentName = null,  // Display name for this staff
    /// <summary>Whether this staff is an ossia (rendered at reduced size).</summary>
    bool IsOssia = false
);

/// <summary>
/// Layout information for a grand staff (brace-connected staves).
/// </summary>
public sealed record GrandStaffLayout(
    ImmutableArray<StaffLayout> Staves,
    double BraceX,      // X position of the brace
    double BraceTop,    // Top Y of the brace
    double BraceBottom  // Bottom Y of the brace
)
{
    /// <summary>Number of staves in this grand staff.</summary>
    public int StaffCount => Staves.Length;

    /// <summary>Total height from top of first staff to bottom of last staff.</summary>
    public double TotalHeight => BraceBottom - BraceTop;

    /// <summary>The upper staff (typically treble).</summary>
    public StaffLayout UpperStaff => Staves[0];

    /// <summary>The lower staff (typically bass).</summary>
    public StaffLayout LowerStaff => Staves[^1];
}

/// <summary>
/// Layout information for a staff group within a system.
/// </summary>
public sealed record StaffGroupLayout(
    StaffGroupType Type,
    ImmutableArray<StaffLayout> Staves,
    double Y,           // Y position of the group
    double Height,      // Total height of the group
    GrandStaffLayout? GrandStaffLayout  // Only for GrandStaff type
)
{
    /// <summary>Whether this is a grand staff.</summary>
    public bool IsGrandStaff => Type == StaffGroupType.GrandStaff;

    /// <summary>
    /// Creates a single staff layout.
    /// </summary>
    public static StaffGroupLayout CreateSingle(StaffLayout staff, double y, double height)
        => new(StaffGroupType.Single, ImmutableArray.Create(staff), y, height, null);

    /// <summary>
    /// Creates a grand staff layout.
    /// </summary>
    public static StaffGroupLayout CreateGrandStaff(
        ImmutableArray<StaffLayout> staves,
        double y,
        double height,
        GrandStaffLayout grandStaffLayout)
        => new(StaffGroupType.GrandStaff, staves, y, height, grandStaffLayout);
}