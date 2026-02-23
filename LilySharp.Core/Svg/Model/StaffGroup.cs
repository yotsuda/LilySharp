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
/// Type of staff grouping.
/// </summary>
public enum StaffGroupType
{
    /// <summary>Single staff (no grouping).</summary>
    Single,

    /// <summary>Grand staff with brace (piano, harp, organ).</summary>
    GrandStaff,

    /// <summary>Staff group with bracket (orchestral sections).</summary>
    StaffGroup
}

/// <summary>
/// A group of staves rendered together.
/// </summary>
/// <remarks>
/// StaffGroup represents:
/// - A single staff (StaffGroupType.Single)
/// - A grand staff with brace (StaffGroupType.GrandStaff) - piano, harp
/// - A bracketed group (StaffGroupType.StaffGroup) - orchestral sections
///
/// Grand staff characteristics:
/// - Connected by a brace on the left
/// - Barlines extend through all staves
/// - Typically 2 staves (treble + bass), but can have more (organ)
/// </remarks>
public sealed record StaffGroup(
    StaffGroupType Type,
    ImmutableArray<Staff> Staves
)
{
    /// <summary>Number of staves in this group.</summary>
    public int StaffCount => Staves.Length;

    /// <summary>Whether this is a grand staff (brace-connected).</summary>
    public bool IsGrandStaff => Type == StaffGroupType.GrandStaff;

    /// <summary>Whether this is a single staff.</summary>
    public bool IsSingle => Type == StaffGroupType.Single;

    /// <summary>The first (or only) staff.</summary>
    public Staff PrimaryStaff => Staves[0];

    /// <summary>
    /// Creates a single staff group (no brace/bracket).
    /// </summary>
    public static StaffGroup CreateSingle(Staff staff)
        => new(StaffGroupType.Single, ImmutableArray.Create(staff));

    /// <summary>
    /// Creates a grand staff (brace-connected, typically piano).
    /// </summary>
    public static StaffGroup CreateGrandStaff(params Staff[] staves)
    {
        if (staves.Length < 2)
            throw new ArgumentException("Grand staff requires at least 2 staves", nameof(staves));
        return new(StaffGroupType.GrandStaff, [.. staves]);
    }

    /// <summary>
    /// Creates a grand staff from an immutable array.
    /// </summary>
    public static StaffGroup CreateGrandStaff(ImmutableArray<Staff> staves)
    {
        if (staves.Length < 2)
            throw new ArgumentException("Grand staff requires at least 2 staves", nameof(staves));
        return new(StaffGroupType.GrandStaff, staves);
    }
}