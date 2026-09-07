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

    /// <summary>The parameters this engraver will score with.</summary>
    /// <remarks>
    /// The collision SUPPLY needs them too — LilyPond weights a covered stem by
    /// <c>STEM_COLLISION_FACTOR</c> where it collects it (lily/beam-quanting.cc:414), in the
    /// same init_instance_variables that has <c>parameters_</c> to hand. Exposed rather
    /// than let the collector reach for <see cref="BeamQuantParameters.Default"/>, so a
    /// caller that passes its own parameters gets them on both sides.
    /// </remarks>
    public BeamQuantParameters Parameters => _parameters;

    /// <summary>
    /// Calculates the layout for a beam group.
    /// X positions are in staff spaces, Y positions are in staff positions.
    /// </summary>
    public BeamLayout CalculateBeamLayout(
        BeamGroup group,
        IReadOnlyList<double> itemXPositions,
        int staffIndex,
        int systemIndex,
        IReadOnlyList<BeamCollision>? collisions = null)
    {
        if (group.Members.Length < 2)
            throw new ArgumentException("Beam group must have at least 2 members");

        // Get X positions for each member
        var memberXPositions = group.Members
            .Select(m => itemXPositions[m.ItemIndex])
            .ToImmutableArray();

        // …and for each invisible rest stem the beam runs over: the rest glyph's ink
        // CENTRE (LayoutUtilities.RestStemX), which is where LilyPond stands the stem it
        // gives a beamed rest.
        var restXPositions = group.RestStems.IsDefaultOrEmpty
            ? ImmutableArray<double>.Empty
            : group.RestStems
                .Select(r => LayoutUtilities.RestStemX(itemXPositions[r.ItemIndex], r.NoteValue))
                .ToImmutableArray();

        double leftX = memberXPositions[0];
        double rightX = memberXPositions[^1];
        // …unless the beam's END is a rest the writer bracketed: LilyPond reaches it. The
        // frame is the member one (the renderer turns these into stem X), so a bounding rest
        // contributes its own item X, and the stem-X conversion is skipped for it — a rest's
        // stem stands on its ink centre, which LayoutUtilities.RestStemX already answers and
        // restXPositions already holds.
        // LILYPOND-REF: lily/beam.cc:631 — the end is the outer STEM ± stem_width/2, and a
        //   beamed rest's stem is one of them (scratch/p345/beambound.ly: `r8[ c c c]' puts
        //   the beam's left edge at 9.020, the rest's ink centre 9.085 less half a stem).
        for (int r = 0; r < group.RestStems.Length; r++)
        {
            if (!group.RestStems[r].BracketBound) continue;
            if (group.RestStems[r].BeforeMember == 0)
                leftX = Math.Min(leftX, restXPositions[r]);
            else if (group.RestStems[r].BeforeMember == group.Members.Length)
                rightX = Math.Max(rightX, restXPositions[r]);
        }

        // Use BeamScoringProblem to find optimal beam positions
        var problem = new BeamScoringProblem(
            group, itemXPositions, _parameters, collisions,
            restXPositions: restXPositions);
        var (leftY, rightY) = problem.Solve();

        return new BeamLayout(
            group, leftY, rightY, leftX, rightX, memberXPositions, staffIndex, systemIndex,
            restXPositions: restXPositions);
    }
}
