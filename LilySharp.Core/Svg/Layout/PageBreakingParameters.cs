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
/// LILYPOND-REF: lily/include/constrained-breaking.hh:73-74 break_permission_, page_permission_
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
    /// <remarks>LILYPOND-REF: lily/page-breaking.cc:260 ragged_</remarks>
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
    /// <remarks>LILYPOND-REF: lily/page-breaking.cc:263 systems_per_page_</remarks>
    public int SystemsPerPage { get; init; } = 0;

    /// <summary>
    /// Maximum systems per page (0 = no limit).
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/page-breaking.cc:265 max_systems_per_page_</remarks>
    public int MaxSystemsPerPage { get; init; } = 0;

    /// <summary>
    /// Minimum systems per page (0 = no limit).
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/page-breaking.cc:267 min_systems_per_page_</remarks>
    public int MinSystemsPerPage { get; init; } = 0;

    /// <summary>
    /// Penalty for splitting a multi-line markup PARAGRAPH across a page boundary — a
    /// widow or orphan line of a title/text block. NOT "a lone system on the last page".
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-breaking.cc:269-270 — the value, read from
    /// <c>\paper orphan-penalty</c> with default 100000.
    /// LILYPOND-REF: lily/page-spacing.cc:375-383 — where it is APPLIED, gated on
    /// <c>last_markup_line_</c> / <c>first_markup_line_</c>. Those come from a markup
    /// line's Prob (constrained-breaking.cc:633-636); a music system's Line_details leaves
    /// both false (constrained-breaking.hh:115-116), so music never triggers it.
    ///
    /// ⚠️ CURRENTLY UNREACHABLE, on purpose. Lily#'s breaker has no markup-paragraph model
    /// (<see cref="SystemDetails.IsTitle"/> marks a title line, but there is no
    /// first/last-line-of-paragraph notion), so nothing can satisfy the real condition. It
    /// is kept rather than deleted because it is a genuine LilyPond paper variable and the
    /// value is right; what is missing is multi-line markup in the page breaker.
    ///
    /// ⚠️ It was applied to an INVENTED condition until 2026-07-22 — a lone system on the
    /// last page — where at 100000 it swamped the force-squared demerits (~0.001) and
    /// decided page breaks on its own. The old remark cited :269 for a rule that lives at
    /// page-spacing.cc:375; a LILYPOND-REF next to a formula is not evidence that the
    /// formula matches it.
    /// </remarks>
    public double OrphanPenalty { get; init; } = 100000;

    /// <summary>
    /// Weight for page spacing penalties relative to line penalties.
    /// Higher values prioritize even page spacing over even line spacing.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/page-breaking.cc:1360-1362 page_spacing_weight = 10</remarks>
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
