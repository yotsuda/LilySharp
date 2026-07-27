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
    /// <c>nonstaff-nonstaff-spacing</c>'s minimum-distance — the floor under the step from
    /// one lyric line to the next.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: ly/engraver-init.ly:653-656 — the Lyrics context sets
    /// <c>nonstaff-nonstaff-spacing = ((basic-distance . 0) (minimum-distance . 2.8)
    /// (padding . 0.2) (stretchability . 0))</c>, and page-layout-problem.cc:1315-1332 is
    /// the branch of <c>get_spacing_spec</c> that hands it to a spring between two loose
    /// lines.
    /// ⚠️ A ZERO basic-distance with a minimum is the opposite shape from
    /// <see cref="RelatedStaffPadding"/>'s spec: there is no ideal to fall back on, so the
    /// realized step IS <c>max(minimum, ink + padding)</c> and nothing else. The spring is
    /// rigid in stretch because the spec DECLARES <c>(stretchability . 0)</c> at :657.
    /// ⚠️ CORRECTED 2026-07-26: this said <c>set_default_strength</c> derived the strength
    /// from the ideal. It does run (page-layout-problem.cc:1354, unconditionally), but a
    /// declared stretchability overrides it immediately after (:1356-1357), so the 0 here is
    /// LilyPond's own number and not a derived one. The two agree only because the ideal is
    /// also 0 — the same coincidence that hid a literal in <c>MarkupMarkup</c>. MEASURED (audit/lp-geometry, <c>lyrics.verse-step</c>): 2.800000 on a
    /// page whose loose chain is compressed hard enough to pull the first line off its own
    /// basic-distance.
    /// </remarks>
    public const double NonStaffNonStaffMinimum = 2.8;

    /// <summary>
    /// <c>nonstaff-nonstaff-spacing</c>'s padding — the gap left between one lyric line's
    /// descenders and the next line's ascenders when the ink beats the minimum.
    /// </summary>
    /// <remarks>LILYPOND-REF: ly/engraver-init.ly:656.</remarks>
    public const double NonStaffNonStaffPadding = 0.2;

    /// <summary>
    /// <c>nonstaff-unrelatedstaff-spacing</c>'s padding — the gap left between the LAST
    /// line of a lyric block and the up-skyline of the staff on the far side of it, the
    /// side its <c>staff-affinity</c> does NOT point at.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: ly/engraver-init.ly:658 — the Lyrics context overrides exactly this
    /// one member, <c>nonstaff-unrelatedstaff-spacing.padding = #1.5</c>, over the
    /// VerticalAxisGroup default's 0.5 (scm/define-grobs.scm:4240).
    /// LILYPOND-REF: lily/page-layout-problem.cc:1299-1312 — the branch of
    /// <c>get_spacing_spec</c> that reaches it: <c>before</c> is the loose line, its
    /// affinity is UP, and <c>after</c> is spaceable.
    /// <para>
    /// ⚠️ THE PADDING IS ALL THE SPEC DECLARES. No basic-distance, no minimum-distance and
    /// no stretchability, so every other member of the resulting spring comes from the
    /// caller's own <c>Spring spring (1.0, 0.0)</c> (:1035) and from the LARGE_STRETCH the
    /// branch adds — see <see cref="LooseLineSpacer.NonStaffUnrelatedStaff"/>, which is
    /// where that spring is spelled.
    /// </para>
    /// </remarks>
    public const double UnrelatedStaffPadding = 1.5;

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
