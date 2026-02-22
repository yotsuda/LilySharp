namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Parameters for spacing between staves within a system.
/// Controls both intra-group (staff-staff) and inter-group (staffgroup-staff) spacing.
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/define-grobs.scm:3040-3054 StaffGrouper
/// LILYPOND-REF: lily/staff-grouper-interface.cc
/// </remarks>
public sealed record StaffSpacingParameters
{
    public static StaffSpacingParameters Default { get; } = new();

    /// <summary>
    /// Spacing between consecutive staves within the same group (e.g., piano grand staff).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:3042-3045
    /// (staff-staff-spacing . ((basic-distance . 9)
    ///                         (minimum-distance . 7)
    ///                         (padding . 1)
    ///                         (stretchability . 5)))
    /// </remarks>
    public VerticalSpacingSpec StaffStaff { get; init; } = new()
    {
        BasicDistance = 9,
        MinimumDistance = 7,
        Padding = 1,
        Stretchability = 5
    };

    /// <summary>
    /// Spacing between the last staff of one group and the first staff of the next.
    /// Larger gap to visually separate groups.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:3046-3049
    /// (staffgroup-staff-spacing . ((basic-distance . 10.5)
    ///                              (minimum-distance . 8)
    ///                              (padding . 1)
    ///                              (stretchability . 9)))
    /// </remarks>
    public VerticalSpacingSpec StaffGroupStaff { get; init; } = new()
    {
        BasicDistance = 10.5,
        MinimumDistance = 8,
        Padding = 1,
        Stretchability = 9
    };
}
