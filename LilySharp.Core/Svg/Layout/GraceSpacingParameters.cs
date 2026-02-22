namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Parameters for grace note spacing.
/// Grace notes use tighter spacing than regular notes.
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/define-grobs.scm:1585-1598 GraceSpacing
/// LILYPOND-REF: lily/spacing-basic.cc:140-155 grace note spring creation
/// </remarks>
public sealed record GraceSpacingParameters
{
    public static GraceSpacingParameters Default { get; } = new();

    /// <summary>
    /// Spacing increment for grace notes (notehead width proxy).
    /// Smaller than regular notes (0.8 vs 1.2) for tighter spacing.
    /// LILYPOND-REF: define-grobs.scm:1592 (spacing-increment . 0.8)
    /// </summary>
    public double SpacingIncrement { get; init; } = 0.8;

    /// <summary>
    /// Duration space factor for the shortest grace note.
    /// LILYPOND-REF: define-grobs.scm:1591 (shortest-duration-space . 1.6)
    /// </summary>
    public double ShortestDurationSpace { get; init; } = 1.6;

    /// <summary>
    /// Base shortest duration for grace note spacing calculation.
    /// Grace notes are typically eighth notes (1/8).
    /// </summary>
    public double BaseShortestDuration { get; init; } = 0.125;
}
