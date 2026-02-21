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
    TuningType? Tuning = null  // Tuning for tablature staves
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