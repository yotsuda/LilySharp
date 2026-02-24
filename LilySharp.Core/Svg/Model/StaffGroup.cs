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
/// <remarks>
/// LILYPOND-REF: ly/engraver-init.ly — context definitions for each staff group type
/// </remarks>
public enum StaffGroupType
{
    /// <summary>Single staff (no grouping).</summary>
    Single,

    /// <summary>Grand staff with brace (piano, harp, organ).</summary>
    GrandStaff,

    /// <summary>Staff group with bracket (orchestral sections). Barlines span all staves.</summary>
    StaffGroup,

    /// <summary>Choir staff with bracket (vocal sections). Barlines are separate per staff.</summary>
    ChoirStaff
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

    /// <summary>Whether this is a choir staff (bracket-connected, separate barlines).</summary>
    public bool IsChoirStaff => Type == StaffGroupType.ChoirStaff;

    /// <summary>Whether this is a staff group (bracket-connected, spanning barlines).</summary>
    public bool IsBracketGroup => Type == StaffGroupType.StaffGroup;

    /// <summary>Whether this is a single staff.</summary>
    public bool IsSingle => Type == StaffGroupType.Single;

    /// <summary>Whether this group has a delimiter (brace/bracket) at the left.</summary>
    public bool HasDelimiter => Type is StaffGroupType.GrandStaff
        or StaffGroupType.StaffGroup or StaffGroupType.ChoirStaff;

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

    /// <summary>
    /// Creates a choir staff (bracket-connected, separate barlines).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: ly/engraver-init.ly — ChoirStaff uses SystemStartBracket
    /// with disconnected barlines (each staff has its own barlines).
    /// </remarks>
    public static StaffGroup CreateChoirStaff(params Staff[] staves)
    {
        if (staves.Length < 2)
            throw new ArgumentException("Choir staff requires at least 2 staves", nameof(staves));
        return new(StaffGroupType.ChoirStaff, [.. staves]);
    }

    /// <summary>
    /// Creates a bracket group (orchestral sections with spanning barlines).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: ly/engraver-init.ly — StaffGroup uses SystemStartBracket
    /// with connected barlines (barlines span all staves in the group).
    /// </remarks>
    public static StaffGroup CreateBracketGroup(params Staff[] staves)
    {
        if (staves.Length < 2)
            throw new ArgumentException("Bracket group requires at least 2 staves", nameof(staves));
        return new(StaffGroupType.StaffGroup, [.. staves]);
    }
}