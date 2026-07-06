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
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Calculates beam positions and slopes.
/// All calculations are in staff spaces/positions.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/beam.cc:1-1554 Beam class
/// LILYPOND-REF: lily/beam-quanting.cc:1-1397 Beam_scoring_problem class
/// </remarks>
internal sealed class BeamEngraver
{
    private readonly BeamQuantParameters _parameters;

    public BeamEngraver(BeamQuantParameters? parameters = null)
    {
        _parameters = parameters ?? BeamQuantParameters.Default;
    }

    /// <summary>
    /// Calculates the layout for a beam group.
    /// X positions are in staff spaces, Y positions are in staff positions.
    /// </summary>
    public BeamLayout CalculateBeamLayout(
        BeamGroup group,
        IReadOnlyList<double> itemXPositions,
        IReadOnlyList<BeamCollision>? collisions = null,
        int staffIndex = -1)
    {
        if (group.Members.Length < 2)
            throw new ArgumentException("Beam group must have at least 2 members");

        // Get X positions for each member
        var memberXPositions = group.Members
            .Select(m => itemXPositions[m.ItemIndex])
            .ToImmutableArray();

        double leftX = memberXPositions[0];
        double rightX = memberXPositions[^1];

        // Use BeamScoringProblem to find optimal beam positions
        var problem = new BeamScoringProblem(
            group, itemXPositions, _parameters, collisions);
        var (leftY, rightY) = problem.Solve();

        return new BeamLayout(group, leftY, rightY, leftX, rightX, memberXPositions, staffIndex);
    }
}
