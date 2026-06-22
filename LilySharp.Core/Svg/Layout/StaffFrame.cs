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
/// The single conversion between LilyPond's native vertical frame and Lily#'s
/// device coordinates.
/// </summary>
/// <remarks>
/// LilyPond reasons about vertical geometry in a <b>Y-up</b> frame measured in
/// staff-spaces from the staff middle line (staff position 0), and flips to a
/// device Y-down frame only at stencil/output time. Lily#'s shared layout and
/// rendering space is device Y-down (origin top-left, Y growing downward).
///
/// To port LilyPond layout code sign-for-sign, engravers should compute in the
/// Y-up frame (so a formula reads identically to its <c>lily/*.cc</c> source)
/// and convert at the single hand-off point with <see cref="ToDevice"/>. This
/// is the one chokepoint that stands in for LilyPond's stencil-time flip;
/// keeping it here (rather than inlining <c>staffMiddleY - y</c> at every call
/// site) means the convention is defined exactly once.
///
/// The reflection is an involution about <c>staffMiddleY</c>: <see cref="ToUp"/>
/// and <see cref="ToDevice"/> are the same map, named for the direction of
/// travel so call sites document which frame they are entering.
/// </remarks>
public static class StaffFrame
{
    /// <summary>
    /// Converts a device Y coordinate (Y-down) into the LilyPond Y-up frame:
    /// staff-spaces above <paramref name="staffMiddleY"/>.
    /// </summary>
    public static double ToUp(double deviceY, double staffMiddleY)
        => staffMiddleY - deviceY;

    /// <summary>
    /// Reflects a Y-up coordinate (staff-spaces above the middle line) back to
    /// device coordinates (Y-down). This is the stand-in for LilyPond's
    /// stencil-time flip.
    /// </summary>
    public static double ToDevice(double up, double staffMiddleY)
        => staffMiddleY - up;
}
