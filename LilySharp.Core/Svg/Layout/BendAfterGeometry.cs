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
/// The fall and the doit (<c>@fall</c>, <c>@doit</c>) — LilyPond's BendAfter spanner, printed
/// by bend::print: one stroked cubic that leaves the note head's ink right (or its dots', when
/// the dots sit on the head's own row) by <see cref="Padding"/>, ends <see cref="Padding"/>
/// short of the next column's ink but at least <see cref="MinimumLength"/> on, and drops or
/// rises <see cref="DeltaY"/> staff spaces. The ONE home of the numbers: ArticulationEngraver
/// places the curve, SharedRenderer.Overlays draws it, and LilyPondExporter writes the twin's
/// <c>\bendAfter</c> amount from <see cref="DeltaStep"/>.
/// </summary>
/// <remarks>
/// Until session 566 the curve was Lily#'s own: eight straight segments along a quadratic
/// 1.25 long and 1.7 deep, 0.13 thick, leaving the head by 0.15 — under a LILYPOND-REF that
/// named bend::print without reading it. MEASURED on 2.26.0 (Lab sessions/p566/bend-lp.log):
/// `e\bendAfter #-4 g` draws 13.107 … 13.892 = the e's ink right 12.607 + 0.5 … the g's ink
/// left 14.392 − 0.5, a box 2.0 tall, the path 0.2 thick.
/// </remarks>
internal static class BendAfterGeometry
{
    /// <summary>The gap between each bound's ink and the curve's end at that side.
    /// LILYPOND-REF: scm/output-lib.scm:1357-1380 bend::print — padding (:1357, ly:grob-property's default 0.5, BendAfter declaring none) spent at left-x (:1365) and right-x (:1379) against each bound's generic-bound-extent.</summary>
    public const double Padding = 0.5;

    /// <summary>The least the curve reaches past its start when the next column is closer.
    /// LILYPOND-REF: scm/define-grobs.scm:551-559 BendAfter — minimum-length 0.5 (bend-after-interface).</summary>
    public const double MinimumLength = 0.5;

    /// <summary>The stroke, in line thicknesses.
    /// LILYPOND-REF: scm/define-grobs.scm:551-559 BendAfter — thickness 2.0 (bend-after-interface), times line-thickness in bend::print.</summary>
    public const double Thickness = 2.0 * EngravingDefaults.LineThickness;

    /// <summary>
    /// The interval a fall drops and a doit rises, in staff POSITIONS. LILYSHARP-OWN:
    /// LilyPond's <c>\bendAfter</c> takes the amount from the author; Lily#'s <c>@fall</c> and
    /// <c>@doit</c> take none, so the page prints <c>\bendAfter #-4</c> / <c>#+4</c> — the same
    /// number the exporter writes into the twin, which is why the twin measures the page.
    ///   departs from: ly/music-functions-init.ly:357-361 bendAfter's delta argument (fixed here).
    ///   goes away when: the marks grow an amount.
    ///   observed by: BendAfterGeometryTests (the drop of 2.0) and the twin of test/bend.lys.
    /// </summary>
    public const int DeltaStep = 4;

    /// <summary>The vertical reach in staff spaces.
    /// LILYPOND-REF: scm/output-lib.scm:1347-1352 bend::print — delta-y = 0.5 × delta-position; the dot is read off the note-head-interface bound.</summary>
    public const double DeltaY = 0.5 * DeltaStep;

    /// <summary>The first control point stands this fraction of the reach along, at the
    /// start's height; the second at the far end, <see cref="SecondControlRise"/> of the drop
    /// down. LILYPOND-REF: scm/output-lib.scm:1343-1397 bend::print — rcurveto (dx/3, 0) (dx, 0.66 delta-y) (dx, delta-y) at :1387-1391, off a note-head-interface bound (:1349).</summary>
    public const double FirstControlFraction = 1.0 / 3.0;

    /// <inheritdoc cref="FirstControlFraction"/>
    public const double SecondControlRise = 0.66;
}
