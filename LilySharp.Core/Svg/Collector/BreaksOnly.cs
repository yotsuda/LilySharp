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
using System.Linq;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Collector;

/// <summary>
/// <c>paper { breaksOnly }</c> (<see cref="LayoutOptions.BreaksOnly"/>): every bar line the
/// music leaves open to a line or page break is closed, so the only breaks are the written
/// <c>break</c> / <c>pageBreak</c> (their <see cref="BreakPermission.Force"/>) — LilyPond's
/// line- and page-break-permission set to <c>##f</c> on the Score's columns.
/// </summary>
/// <remarks>
/// Applied to the collected score, on every staff, at <c>SvgGenerator.CollectScore</c>'s one
/// exit, so the full compile, the editor's incremental one and every output read the same
/// measures. The piece's LAST bar keeps its permission: the end of the music is a break the
/// breaker must be able to take.
/// </remarks>
internal static class BreaksOnly
{
    /// <summary>The score with its open breaks closed, or the score itself when its paper does
    /// not ask for it.</summary>
    public static MultiStaffScore Apply(MultiStaffScore score)
    {
        if (!score.Paper.BreaksOnly || score.StaffGroups.IsDefaultOrEmpty)
            return score;
        return score with
        {
            StaffGroups = score.StaffGroups.Select(g => g with
            {
                Staves = g.Staves.Select(s => s with
                {
                    Voices = s.Voices.Select(v => v with { Measures = Close(v.Measures) }).ToImmutableArray(),
                }).ToImmutableArray(),
            }).ToImmutableArray(),
        };
    }

    private static ImmutableArray<Measure> Close(ImmutableArray<Measure> measures)
    {
        if (measures.Length < 2)
            return measures;
        var builder = measures.ToBuilder();
        for (int i = 0; i < builder.Count - 1; i++)
        {
            var m = builder[i];
            if (m.LineBreakPermission == BreakPermission.Allow || m.PageBreakPermission == BreakPermission.Allow)
                builder[i] = m with
                {
                    LineBreakPermission = m.LineBreakPermission == BreakPermission.Allow
                        ? BreakPermission.Forbid : m.LineBreakPermission,
                    PageBreakPermission = m.PageBreakPermission == BreakPermission.Allow
                        ? BreakPermission.Forbid : m.PageBreakPermission,
                };
        }
        return builder.MoveToImmutable();
    }
}
