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
    /// <remarks>LILYPOND-REF: lily/misc.cc:48-55 peak_around()</remarks>
    public static double PeakAround(double epsilon, double threshold, double x)
    {
        if (x < 0)
            return 1.0;
        return Math.Max(-epsilon * (x - threshold) / ((x + epsilon) * threshold), 0.0);
    }

    /// <summary>
    /// convex_amplifier: 0 at x=0, 1 at x=standardX, growing exponentially beyond.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/misc.cc:60-65 convex_amplifier()</remarks>
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
