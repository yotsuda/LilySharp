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

using System;
using System.Collections.Generic;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Shared "lower a below-staff line so its text clears the notes" spacing, used by
/// the lyric and figured-bass engravers (both stack text under the staff). Mirrors
/// LilyPond's Align_interface per-pair spacing: each line is placed at
///   realized = max(basic-distance, staffDownSkyline.distance(lineUpSkyline) + padding)
/// so the fixed basic-distance wins for ordinary music and only notes poking far
/// below push the line down, keeping the LilyPond padding gap.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/align-interface.cc:222-275 — Align_interface minimum translations.
/// LILYPOND-REF: lily/page-layout-problem.cc:625-629 — read-spacing-spec padding.
/// </remarks>
internal static class SkylineDrop
{
    /// <summary>relatedstaff-spacing padding (ly/engraver-init.ly:651): the gap left
    /// between the line's up-skyline and the staff down-skyline when the skyline
    /// distance beats the basic-distance.</summary>
    public const double RelatedStaffPadding = 0.5;

    /// <summary>skyline-horizontal-padding for the line/staff skyline distance.</summary>
    public const double HorizonPadding = 0.1;

    /// <summary>
    /// Per-system downward Y-shift so each system's UP-skyline clears the staff's
    /// DOWN-skyline: <c>drop = max(0, max(basicY, distance + RelatedStaffPadding) - basicY)</c>.
    /// Only systems that need a shift appear in the result. <paramref name="basicY"/>
    /// gives the line's basic-distance floor for a system (a scalar for lyrics, the
    /// per-system minimum for figured bass).
    /// </summary>
    public static Dictionary<int, double> Compute(
        IReadOnlyDictionary<int, VerticalSkyline> upBySystem,
        Func<int, double> basicY,
        IReadOnlyList<(VerticalSkyline up, VerticalSkyline down)> systemSkylines)
    {
        var drops = new Dictionary<int, double>();
        foreach (var (s, up) in upBySystem)
        {
            if (s >= systemSkylines.Count) continue;
            var down = systemSkylines[s].down;
            if (down.IsEmpty || up.IsEmpty) continue;
            double dist = down.Distance(up, HorizonPadding);
            if (double.IsInfinity(dist) || double.IsNaN(dist)) continue;
            double basic = basicY(s);
            double drop = Math.Max(basic, dist + RelatedStaffPadding) - basic;
            if (drop > 1e-6) drops[s] = drop;
        }
        return drops;
    }
}
