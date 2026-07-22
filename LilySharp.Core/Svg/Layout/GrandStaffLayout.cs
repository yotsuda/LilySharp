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
/// <remarks>
/// LILYPOND-REF: lily/align-interface.cc:274 — <c>where += stacking_dir * dy</c> with
/// <c>stacking_dir = DOWN = -1</c>: LilyPond's stacking accumulator walks NEGATIVE and
/// stores the result as-is, so a staff's within-system offset is Y-UP throughout.
/// LILYPOND-REF: lily/page-layout-problem.cc:915-917 — "this is relative to the system:
/// negative numbers are down".
/// </remarks>
public sealed record StaffLayout(
    int StaffIndex,
    ClefType Clef,
    // Y-UP offset from the system top: 0 for the first staff, NEGATIVE for every
    // staff below it. The staff's BOTTOM is Y - Height (Height is a length, so it
    // stays positive). Computations that are deliberately device-framed reflect at
    // their edge via LayoutUtilities.StaffOffsetInSystemDown.
    double Y,
    double Height,      // Staff height (typically 4 * staffSpace), a LENGTH — always positive
    TuningType? Tuning = null,  // Tuning for tablature staves
    string? InstrumentName = null,  // Display name for this staff
    // Whether this staff is an ossia (rendered at reduced size).
    bool IsOssia = false,
    // Whether this staff is hidden due to hara-kiri (empty staff removal).
    // LILYPOND-REF: lily/hara-kiri-group-spanner.cc — consider_suicide()
    // When true, the staff and its contents should not be rendered.
    bool IsHidden = false
);

/// <summary>
/// Type of system-start delimiter for staff groups.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/system-start-delimiter.cc — print() dispatches on style property
/// LILYPOND-REF: scm/define-grobs.scm — SystemStartBrace, SystemStartBracket, SystemStartBar, SystemStartSquare
/// </remarks>
public enum SystemStartDelimiterType
{
    /// <summary>No delimiter.</summary>
    None,

    /// <summary>Curly brace (PianoStaff, GrandStaff). Style: 'brace.</summary>
    Brace,

    /// <summary>Thick angular bracket with serifs (StaffGroup, ChoirStaff). Style: 'bracket.</summary>
    Bracket,

    /// <summary>Thin L-shaped bracket without serifs (SystemStartSquare). Style: 'line-bracket.</summary>
    LineBracket,

    /// <summary>Simple vertical bar line. Style: 'bar-line.</summary>
    BarLine
}

/// <summary>
/// Layout information for a multi-staff group with a system-start delimiter.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/system-start-delimiter.cc — brace/bracket/bar-line/line-bracket rendering
/// LILYPOND-REF: scm/define-grobs.scm:3352-3355 staff-staff-spacing
///
/// IMPLEMENTED — bracket/brace collapse-height check (system-start-delimiter.cc:127-129)
/// IMPLEMENTED — bracket, line-bracket, and bar-line delimiter types
/// </remarks>
public sealed record GrandStaffLayout(
    ImmutableArray<StaffLayout> Staves,
    double BraceX,      // X position of the delimiter
    // Both ends share StaffLayout.Y's Y-UP frame, so BraceTop is the GREATER value.
    double BraceTop,    // Top Y of the delimiter (Y-up)
    double BraceBottom, // Bottom Y of the delimiter (Y-up, below the top ⇒ smaller)
    SystemStartDelimiterType DelimiterType = SystemStartDelimiterType.Brace
)
{
    /// <summary>Number of staves in this grand staff.</summary>
    public int StaffCount => Staves.Length;

    /// <summary>Total height from top of first staff to bottom of last staff.</summary>
    public double TotalHeight => BraceTop - BraceBottom;

    /// <summary>The upper staff (typically treble).</summary>
    public StaffLayout UpperStaff => Staves[0];

    /// <summary>The lower staff (typically bass).</summary>
    public StaffLayout LowerStaff => Staves[^1];
}

/// <summary>
/// Layout information for a staff group within a system.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/axis-group-interface.cc — VerticalAxisGroup
/// LILYPOND-REF: lily/staff-grouper-interface.cc — staff-staff-spacing, staffgroup-staff-spacing
/// </remarks>
public sealed record StaffGroupLayout(
    StaffGroupType Type,
    ImmutableArray<StaffLayout> Staves,
    double Y,           // Y-up position of the group's top (same frame as StaffLayout.Y)
    double Height,      // Total height of the group, a LENGTH — always positive
    GrandStaffLayout? GrandStaffLayout  // Multi-staff delimiter layout (brace, bracket, etc.)
)
{
    /// <summary>Whether this is a grand staff.</summary>
    public bool IsGrandStaff => Type == StaffGroupType.GrandStaff;

    /// <summary>Whether this group has a delimiter.</summary>
    public bool HasDelimiter => GrandStaffLayout != null;

    /// <summary>
    /// Creates a single staff layout.
    /// </summary>
    public static StaffGroupLayout CreateSingle(StaffLayout staff, double y, double height)
        => new(StaffGroupType.Single, ImmutableArray.Create(staff), y, height, null);

    /// <summary>
    /// Creates a grand staff layout (brace delimiter).
    /// </summary>
    public static StaffGroupLayout CreateGrandStaff(
        ImmutableArray<StaffLayout> staves,
        double y,
        double height,
        GrandStaffLayout grandStaffLayout)
        => new(StaffGroupType.GrandStaff, staves, y, height, grandStaffLayout);

    /// <summary>
    /// Creates a bracket group layout (StaffGroup with bracket delimiter).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: ly/engraver-init.ly — StaffGroup uses SystemStartBracket
    /// </remarks>
    public static StaffGroupLayout CreateBracketGroup(
        StaffGroupType type,
        ImmutableArray<StaffLayout> staves,
        double y,
        double height,
        GrandStaffLayout delimiterLayout)
        => new(type, staves, y, height, delimiterLayout);
}