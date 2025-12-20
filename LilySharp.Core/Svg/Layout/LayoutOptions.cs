namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Options for score layout.
/// All dimensions are in staff spaces.
/// </summary>
/// <remarks>
/// Staff space is the distance between two adjacent staff lines.
/// This is the standard unit in LilyPond and music engraving.
/// </remarks>
public sealed record LayoutOptions
{
    // === Page Dimensions (in staff spaces) ===

    /// <summary>Page width in staff spaces.</summary>
    public double PageWidth { get; init; } = 80;

    /// <summary>Left margin in staff spaces.</summary>
    public double MarginLeft { get; init; } = 2;

    /// <summary>Right margin in staff spaces.</summary>
    public double MarginRight { get; init; } = 2;

    /// <summary>Top margin in staff spaces.</summary>
    /// <remarks>LILYPOND-REF: scm/paper.scm:49 top-margin</remarks>
    public double MarginTop { get; init; } = 5;

    /// <summary>Bottom margin in staff spaces.</summary>
    /// <remarks>LILYPOND-REF: scm/paper.scm:22 bottom-margin</remarks>
    public double MarginBottom { get; init; } = 5;

    /// <summary>
    /// Page height in staff spaces.
    /// Set to 0 or negative for automatic (single page, content-driven).
    /// </summary>
    /// <remarks>LILYPOND-REF: scm/paper.scm:41 paper-height</remarks>
    public double PageHeight { get; init; } = 0;

    // === Staff Dimensions (in staff spaces) ===

    /// <summary>
    /// Staff height in staff spaces (always 4 for standard 5-line staff).
    /// </summary>
    public double StaffHeight { get; init; } = 4;

    /// <summary>Vertical spacing between systems in staff spaces.</summary>
    public double SystemSpacing { get; init; } = 8;

    /// <summary>
    /// LILYPOND-REF: lily/page-layout-problem.cc:477-478
    /// Padding between header (title) bottom and first system's topmost element.
    /// </summary>
    public double TopSystemPadding { get; init; } = 1;

    /// <summary>Spacing between staves in a grand staff (in staff spaces).</summary>
    public double GrandStaffSpacing { get; init; } = 3;

    /// <summary>Spacing between staff groups (in staff spaces).</summary>
    public double StaffGroupSpacing { get; init; } = 5;

    // === Spacing Parameters (in staff spaces) ===

    /// <summary>Horizontal padding for collision detection in staff spaces.</summary>
    public double CollisionXPadding { get; init; } = 2;

    /// <summary>Maximum stretch per measure during justification in staff spaces.</summary>
    public double MaxStretchPerMeasure { get; init; } = 5;

    // === Layout Algorithm Options ===

    /// <summary>
    /// If true, lines are not justified (stretched to fill width).
    /// Measures are placed at their ideal width, left-aligned.
    /// </summary>
    public bool RaggedRight { get; init; } = false;

    /// <summary>
    /// If true, uses Knuth-Plass optimal line breaking algorithm.
    /// Otherwise uses greedy first-fit algorithm.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/constrained-breaking.cc</remarks>
    public bool UseOptimalLineBreaking { get; init; } = true;

    /// <summary>
    /// Tolerance for line stretch/compression in optimal breaking.
    /// Lines with ratio outside 1/tolerance to tolerance*2 are rejected.
    /// </summary>
    public double LineBreakingTolerance { get; init; } = 1.1;

    /// <summary>
    /// If true, uses optimal page breaking algorithm.
    /// Otherwise all systems are placed on a single page.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/page-spacing.cc</remarks>
    public bool UseOptimalPageBreaking { get; init; } = false;

    // === Computed Properties ===

    /// <summary>Available width for music content in staff spaces.</summary>
    public double ContentWidth => PageWidth - MarginLeft - MarginRight;

    /// <summary>Default options for standard layout.</summary>
    public static LayoutOptions Default { get; } = new();
}
