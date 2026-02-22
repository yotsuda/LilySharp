namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Parameters for note-to-note spacing optical corrections.
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/define-grobs.scm:2428-2442 NoteSpacing
/// LILYPOND-REF: lily/note-spacing.cc:119-199 stem_dir_correction
/// </remarks>
public sealed record NoteSpacingParameters
{
    public static NoteSpacingParameters Default { get; } = new();

    /// <summary>
    /// Correction factor for knee spacing (stem direction changes at beam).
    /// LILYPOND-REF: define-grobs.scm:2428 (knee-spacing-correction . 1.0)
    /// </summary>
    public double KneeSpacingCorrection { get; init; } = 1.0;

    /// <summary>
    /// Correction factor for notes with same stem direction.
    /// Tightens spacing when stems face the same way.
    /// LILYPOND-REF: define-grobs.scm:2429 (same-direction-correction . 0.25)
    /// </summary>
    public double SameDirectionCorrection { get; init; } = 0.25;

    /// <summary>
    /// Correction factor for notes with opposite stem directions.
    /// Increases spacing when stems face opposite ways to avoid collisions.
    /// LILYPOND-REF: define-grobs.scm:2431 (stem-spacing-correction . 0.5)
    /// </summary>
    public double StemSpacingCorrection { get; init; } = 0.5;

    /// <summary>
    /// Whether to measure distance to the barline (or next note).
    /// LILYPOND-REF: define-grobs.scm:2430 (space-to-barline . #t)
    /// </summary>
    public bool SpaceToBarline { get; init; } = true;
}
