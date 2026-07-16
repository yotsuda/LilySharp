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
/// Resolves the horizontal spring force for one system line. Extracted from the
/// byte-identical ragged-solve branch that was duplicated across the layout passes
/// (<see cref="MultiStaffLayouter"/> and the single-staff measure pass); the only
/// per-site difference was the condition that selects ragged mode, which is now the
/// <c>ragged</c> argument.
/// </summary>
internal static class SystemForceSolver
{
    /// <summary>
    /// In ragged mode, a line shorter than the target keeps its natural spacing
    /// (force 0) while a too-long line compresses; non-ragged justifies to fill
    /// the target width.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/simple-spacer.cc:175-205 Simple_spacer::solve()</remarks>
    internal static double ResolveForce(SpringSolver solver, double springTargetWidth, bool ragged)
    {
        if (ragged)
        {
            double naturalLength = solver.IdealTotalLength;
            if (naturalLength < springTargetWidth)
            {
                // Line is shorter than available - use natural spacing (force = 0)
                return 0;
            }
            // Line is too long - solve normally (may need compression)
            var (solvedForce, _) = solver.Solve(springTargetWidth, ragged: true);
            return solvedForce;
        }
        // Non-ragged: justify to fill available width
        var (justifyForce, _) = solver.Solve(springTargetWidth, ragged: false);
        return justifyForce;
    }
}
