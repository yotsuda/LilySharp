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
/// Helpers for combining break permissions across the line/page/page-turn hierarchy.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/constrained-breaking.cc:378-386 — min_permission
/// LILYPOND-REF: lily/constrained-breaking.cc:530-535 — chained application
/// </remarks>
public static class BreakPermissionExtensions
{
    /// <summary>
    /// LP's <c>min_permission</c>: combines two permissions, where the result reflects
    /// LP's asymmetric "the more restrictive line permission constrains the broader one"
    /// rule.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/constrained-breaking.cc:378-386
    /// Truth table (perm1 = outer e.g. line perm, perm2 = inner e.g. page perm):
    /// <code>
    ///   force, force   → force
    ///   force, allow   → allow
    ///   force, forbid  → forbid
    ///   allow, force   → forbid (asymmetric: cannot upgrade allow → force here)
    ///   allow, allow   → allow
    ///   allow, forbid  → forbid
    ///   forbid, *      → forbid
    /// </code>
    /// </remarks>
    public static BreakPermission MinPermission(BreakPermission perm1, BreakPermission perm2)
    {
        // LILYPOND-REF: lily/constrained-breaking.cc:380-381
        if (perm1 == BreakPermission.Force)
            return perm2;

        // LILYPOND-REF: lily/constrained-breaking.cc:382-384
        if (perm1 == BreakPermission.Allow && perm2 != BreakPermission.Force)
            return perm2;

        // LILYPOND-REF: lily/constrained-breaking.cc:385 — fallthrough returns SCM_EOL = forbid.
        return BreakPermission.Forbid;
    }
}

/// <summary>
/// Parameters controlling page breaking optimization.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/page-breaking.cc:256-310 initialization
/// LILYPOND-REF: scm/paper.scm page layout variables
/// </remarks>
internal sealed record PageBreakingParameters
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
    /// <remarks>LILYPOND-REF: ly/paper-defaults-init.ly:56 ragged-last-bottom = ##t
    /// ("best for shorter scores") — the LAST page keeps natural spacing.</remarks>
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

    /// <summary>
    /// Whether to use tight spacing (emergency compression when pages overflow).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/include/constrained-breaking.hh tight_spacing_
    /// When enabled, spacing between systems is reduced to fit more content
    /// on each page, preventing overflow at the cost of tighter layout.
    /// </remarks>
    public bool TightSpacing { get; init; } = false;

    /// <summary>
    /// Compression factor for tight spacing mode (0..1, where 1 = no compression).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc — tight spacing multiplier
    /// Applied to basic-distance and padding when TightSpacing is active.
    /// </remarks>
    public double TightSpacingFactor { get; init; } = 0.7;
}
