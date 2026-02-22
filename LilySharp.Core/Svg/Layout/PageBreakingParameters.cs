namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Permission for page/line breaks.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/include/constrained-breaking.hh:74-76 break_permission_, page_permission_
/// </remarks>
public enum BreakPermission
{
    /// <summary>Normal break allowed.</summary>
    Allow,
    /// <summary>Break forbidden here.</summary>
    Forbid,
    /// <summary>Break forced here.</summary>
    Force
}

/// <summary>
/// Parameters controlling page breaking optimization.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/page-breaking.cc:256-310 initialization
/// LILYPOND-REF: scm/paper.scm page layout variables
/// </remarks>
public sealed record PageBreakingParameters
{
    public static PageBreakingParameters Default { get; } = new();

    /// <summary>
    /// Don't justify systems vertically (leave space at bottom).
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/page-breaking.cc:280 ragged_</remarks>
    public bool RaggedBottom { get; init; } = false;

    /// <summary>
    /// Don't justify vertically on the last page only.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/page-breaking.cc:281 ragged_last_</remarks>
    public bool RaggedLastBottom { get; init; } = true;

    /// <summary>
    /// Force exactly this many systems per page (0 = auto).
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/page-breaking.cc:291 systems_per_page_</remarks>
    public int SystemsPerPage { get; init; } = 0;

    /// <summary>
    /// Maximum systems per page (0 = no limit).
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/page-breaking.cc:293 max_systems_per_page_</remarks>
    public int MaxSystemsPerPage { get; init; } = 0;

    /// <summary>
    /// Minimum systems per page (0 = no limit).
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/page-breaking.cc:295 min_systems_per_page_</remarks>
    public int MinSystemsPerPage { get; init; } = 0;

    /// <summary>
    /// Penalty for orphan (widow) systems: a single system on the last page.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/page-breaking.cc:297 orphan_penalty_ = 100000</remarks>
    public double OrphanPenalty { get; init; } = 100000;

    /// <summary>
    /// Weight for page spacing penalties relative to line penalties.
    /// Higher values prioritize even page spacing over even line spacing.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/page-breaking.cc:1506 page_spacing_weight = 10</remarks>
    public double PageSpacingWeight { get; init; } = 10;
}
