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

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// The curved-bow geometry shared by slur and tie scoring. In LilyPond these are
/// single shared functions (<c>lily/misc.cc</c>, <c>lily/bezier-bow.cc</c>) used by
/// both the slur and tie code; the C# port had byte-identical copies in
/// <see cref="SlurScoringProblem"/> and <see cref="TieFormattingProblem"/>, so this
/// reunifies them onto one LilyPond-mirroring source.
/// </summary>
internal static class BezierBow
{
    /// <summary>
    /// peak_around: 1 at x=0, falling to 0 at x=threshold, 0 beyond.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/misc.cc:39-46 peak_around()</remarks>
    public static double PeakAround(double epsilon, double threshold, double x)
    {
        if (x < 0)
            return 1.0;
        return Math.Max(-epsilon * (x - threshold) / ((x + epsilon) * threshold), 0.0);
    }

    /// <summary>
    /// convex_amplifier: 0 at x=0, 1 at x=standardX, growing exponentially beyond.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/misc.cc:51-56 convex_amplifier()</remarks>
    public static double ConvexAmplifier(double standardX, double increaseFactor, double x)
    {
        return (Math.Exp(increaseFactor * x / standardX) - 1.0)
               / (Math.Exp(increaseFactor) - 1.0);
    }

    /// <summary>
    /// Bow arc height from its width using LilyPond's atan formula, bounded by
    /// <paramref name="heightLimit"/> and scaled by <paramref name="ratio"/>.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/bezier-bow.cc:28-38 F0_1() + slur_height()</remarks>
    public static double Height(double heightLimit, double ratio, double width)
    {
        if (heightLimit < 0.001)
            return 0;
        double x = width * ratio / heightLimit;
        return heightLimit * (2.0 / Math.PI) * Math.Atan(Math.PI * x / 2.0);
    }

    /// <summary>
    /// The bow's own MIDPOINT height — how far the drawn curve actually rises at its middle,
    /// which is three quarters of the control-point <see cref="Height"/>.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tie-configuration.cc:80-87 <c>Tie_configuration::height</c> —
    /// <c>slur_shape (l, height_limit, ratio).curve_point (0.5)[Y_AXIS]</c>, i.e. the shape
    /// EVALUATED at its middle and not the control point that shapes it. With
    /// <c>slur_shape</c>'s ends at Y 0 and both middle controls at Y <c>h</c>
    /// (lily/bezier-bow.cc:126-131), a cubic reads <c>(0 + 3h + 3h + 0) / 8 = 0.75 h</c>
    /// there.
    /// <para>
    /// ⚠️ THE DIFFERENCE IS NOT COSMETIC. Every LilyPond test written against
    /// <c>conf-&gt;height ()</c> — the intra-space branch that decides whether a tie is
    /// nudged off a staff line or kept clear of one at its top
    /// (tie-formatting-problem.cc:530-560), the curve-top reading in
    /// <c>score_configuration</c> (:759) — reads THIS, and using the control height instead
    /// makes a bow measure 4/3 of its real rise. A tie 3.6 staff spaces wide rises 0.517 and
    /// its control sits at 0.689: one is under the 0.625 threshold and the other is over it,
    /// so the two answers are different BRANCHES and not merely different numbers.
    /// audit/lp-geometry <c>tie.direction.beam-opposes-stem</c> is the book that says so.
    /// </para>
    /// <para>
    /// ⚠️ NOT A LITERAL PORT, and the debt is the missing器: LilyPond writes
    /// <c>slur_shape (…).curve_point (0.5)</c> because it HAS a Bezier that can be evaluated
    /// (lily/bezier.cc <c>Bezier::curve_point</c>), and this engine has no Bezier type at all
    /// — every bow is four loose tuples carried to the renderer. So the value is written as
    /// the closed form of that same evaluation instead. ⚠️ THE FACTOR IS EXACT, not an
    /// approximation: with <c>slur_shape</c>'s ends at Y 0 and both middle controls at Y
    /// <c>h</c>, the cubic at t=0.5 is <c>(0 + 3h + 3h + 0) / 8</c>. TO MAKE IT LITERAL, give
    /// the layout a Bezier with <c>curve_point</c> and let this call it — the slur scorer
    /// samples its own curve by hand too (<c>SlurScoringProblem.InterpolateSlurY</c>), so
    /// that器 would have two readers, not one.
    /// </para>
    /// </remarks>
    public static double MidpointHeight(double heightLimit, double ratio, double width)
        => 0.75 * Height(heightLimit, ratio, width);

    /// <summary>
    /// Horizontal indent of the control points from the bow ends.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/bezier-bow.cc:109-118 get_slur_indent_height()</remarks>
    public static double Indent(double heightLimit, double width)
    {
        const double maxFraction = 1.0 / 3.1;
        double q = 2 * heightLimit / maxFraction;
        return 2 * heightLimit - q * q * maxFraction / (width + q);
    }
}
