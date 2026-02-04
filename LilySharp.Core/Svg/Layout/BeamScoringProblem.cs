using System.Collections.Immutable;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Solves the beam positioning problem by finding optimal quantized positions.
/// Based on Lilypond's Beam_scoring_problem from beam-quanting.cc
/// </summary>
public sealed class BeamScoringProblem
{
    private readonly BeamGroup _group;
    private readonly IReadOnlyList<double> _itemXPositions;
    private readonly BeamQuantParameters _parameters;

    // Computed values
    private readonly double _xSpan;
    private readonly double _leftX;
    private readonly double _rightX;
    private readonly double[] _stemXPositions;
    private readonly int[] _staffPositions;
    private readonly int _maxBeamCount;
    private readonly IReadOnlyList<BeamCollision> _collisions;

    // Beam constants (in staff positions, converted from EngravingDefaults)
    private static readonly double BeamThickness = EngravingDefaults.ToStaffPositions(EngravingDefaults.BeamThickness);
    private static readonly double BeamTranslation = EngravingDefaults.ToStaffPositions(EngravingDefaults.BeamTranslation);
    private static readonly double IdealStemLength = EngravingDefaults.ToStaffPositions(EngravingDefaults.IdealStemLength);
    private static readonly double MinStemLength = EngravingDefaults.ToStaffPositions(EngravingDefaults.MinStemLength);

    public BeamScoringProblem(
        BeamGroup group,
        IReadOnlyList<double> itemXPositions,
        BeamQuantParameters? parameters = null,
        IReadOnlyList<BeamCollision>? collisions = null)
    {
        _group = group;
        _itemXPositions = itemXPositions;
        _parameters = parameters ?? BeamQuantParameters.Default;
        _collisions = collisions ?? Array.Empty<BeamCollision>();

        // Compute basic values
        var firstMember = group.Members[0];
        var lastMember = group.Members[^1];
        _leftX = itemXPositions[firstMember.ItemIndex];
        _rightX = itemXPositions[lastMember.ItemIndex];
        _xSpan = _rightX - _leftX;

        // Extract stem positions
        _stemXPositions = new double[group.Members.Length];
        _staffPositions = new int[group.Members.Length];
        _maxBeamCount = 0;

        for (int i = 0; i < group.Members.Length; i++)
        {
            var member = group.Members[i];
            _stemXPositions[i] = itemXPositions[member.ItemIndex];
            _staffPositions[i] = member.StaffPosition;
            _maxBeamCount = Math.Max(_maxBeamCount, member.BeamCount);
        }
    }

    /// <summary>
    /// Solves for the optimal beam position.
    /// </summary>
    // LILYPOND-REF: lily/beam-quanting.cc:1017-1030 Beam_scoring_problem::solve()
    public (double leftY, double rightY) Solve()
    {
        // Generate initial position based on note positions
        var (initialLeftY, initialRightY) = CalculateInitialPosition();

        // LILYPOND-REF: lily/beam-quanting.cc:440-443
        // Apply slope damping based on concaveness
        (initialLeftY, initialRightY) = ApplySlopeDamping(initialLeftY, initialRightY);

        // Generate candidate configurations
        var candidates = GenerateQuantCandidates(initialLeftY, initialRightY);

        // Score all candidates
        foreach (var config in candidates)
        {
            ScoreConfiguration(config);
        }

        // Find best configuration
        var best = candidates.MinBy(c => c.Demerits);
        return (best?.LeftY ?? initialLeftY, best?.RightY ?? initialRightY);
    }

    // LILYPOND-REF: lily/beam-quanting.cc:536-687 Beam_scoring_problem::least_squares_positions()
    private (double leftY, double rightY) CalculateInitialPosition()
    {
        // Calculate based on first and last note positions
        int firstPos = _staffPositions[0];
        int lastPos = _staffPositions[^1];

        // Find extremal positions
        int minPos = _staffPositions.Min();
        int maxPos = _staffPositions.Max();

        // Calculate natural slope
        double naturalSlope = 0;
        if (_xSpan > 0.001)
        {
            naturalSlope = (double)(lastPos - firstPos) / _xSpan;
            // Clamp to reasonable slope (0.5 staff spaces per staff space)
            naturalSlope = Math.Clamp(naturalSlope, -0.5, 0.5);
        }

        double leftY, rightY;

        if (_group.StemUp)
        {
            // Beam above notes (higher staff position = more positive)
            leftY = firstPos + IdealStemLength;
            rightY = leftY + naturalSlope * _xSpan;

            // Ensure minimum stem length
            AdjustForMinimumStemLength(ref leftY, ref rightY, stemUp: true);

            // Clamp to staff middle line (position 0) if beam would exceed it
            // This prevents excessively long stems for notes on ledger lines above the staff
            ClampToMiddleLine(ref leftY, ref rightY, stemUp: true);
        }
        else
        {
            // Beam below notes (lower staff position = more negative)
            leftY = firstPos - IdealStemLength;
            rightY = leftY + naturalSlope * _xSpan;

            // Ensure minimum stem length
            AdjustForMinimumStemLength(ref leftY, ref rightY, stemUp: false);

            // Clamp to staff middle line (position 0) if beam would exceed it
            // This prevents excessively long stems for notes on ledger lines below the staff
            ClampToMiddleLine(ref leftY, ref rightY, stemUp: false);
        }

        return (leftY, rightY);
    }

    private void AdjustForMinimumStemLength(ref double leftY, ref double rightY, bool stemUp)
    {
        double slope = _xSpan > 0.001 ? (rightY - leftY) / _xSpan : 0;
        double maxAdjustment = 0;

        for (int i = 0; i < _staffPositions.Length; i++)
        {
            double x = _stemXPositions[i];
            double beamY = leftY + slope * (x - _leftX);
            double noteY = _staffPositions[i];

            // stemUp: beam is above note (beamY > noteY in staff positions)
            // stemDown: beam is below note (beamY < noteY in staff positions)
            double stemLength = stemUp ? beamY - noteY : noteY - beamY;

            if (stemLength < MinStemLength)
            {
                maxAdjustment = Math.Max(maxAdjustment, MinStemLength - stemLength);
            }
        }

        if (maxAdjustment > 0)
        {
            if (stemUp)
            {
                // Move beam further up (increase staff position)
                leftY += maxAdjustment;
                rightY += maxAdjustment;
            }
            else
            {
                // Move beam further down (decrease staff position)
                leftY -= maxAdjustment;
                rightY -= maxAdjustment;
            }
        }
    }

    /// <summary>
    /// Clamps beam position to not exceed the staff middle line (position 0).
    /// For notes on ledger lines, this prevents stems from being excessively long.
    /// </summary>
    private void ClampToMiddleLine(ref double leftY, ref double rightY, bool stemUp)
    {
        const double staffMiddle = 0;  // Middle line of staff is position 0

        if (stemUp)
        {
            // For stem up (beam above notes), clamp beam to not exceed middle line
            // Check if beam endpoints exceed middle line
            double maxBeamY = Math.Max(leftY, rightY);
            if (maxBeamY <= staffMiddle) return;  // Beam doesn't exceed middle line

            // Calculate how much to clamp, but ensure minimum stem length for all notes
            double slope = _xSpan > 0.001 ? (rightY - leftY) / _xSpan : 0;
            double maxAllowedClamp = double.MaxValue;

            for (int i = 0; i < _staffPositions.Length; i++)
            {
                double x = _stemXPositions[i];
                double beamY = leftY + slope * (x - _leftX);
                double noteY = _staffPositions[i];
                double currentStemLength = beamY - noteY;
                double allowedClamp = currentStemLength - MinStemLength;
                if (allowedClamp < maxAllowedClamp)
                {
                    maxAllowedClamp = allowedClamp;
                }
            }

            // Clamp the beam down towards middle line
            double neededClamp = maxBeamY - staffMiddle;
            double actualClamp = Math.Min(neededClamp, Math.Max(0, maxAllowedClamp));

            if (actualClamp > 0)
            {
                leftY -= actualClamp;
                rightY -= actualClamp;
            }
        }
        else
        {
            // For stem down (beam below notes), clamp beam to not exceed middle line
            // Check if beam endpoints exceed middle line
            double minBeamY = Math.Min(leftY, rightY);
            if (minBeamY >= staffMiddle) return;  // Beam doesn't exceed middle line

            // Calculate how much to clamp, but ensure minimum stem length for all notes
            double slope = _xSpan > 0.001 ? (rightY - leftY) / _xSpan : 0;
            double maxAllowedClamp = double.MaxValue;

            for (int i = 0; i < _staffPositions.Length; i++)
            {
                double x = _stemXPositions[i];
                double beamY = leftY + slope * (x - _leftX);
                double noteY = _staffPositions[i];
                double currentStemLength = noteY - beamY;
                double allowedClamp = currentStemLength - MinStemLength;
                if (allowedClamp < maxAllowedClamp)
                {
                    maxAllowedClamp = allowedClamp;
                }
            }

            // Clamp the beam up towards middle line
            double neededClamp = staffMiddle - minBeamY;
            double actualClamp = Math.Min(neededClamp, Math.Max(0, maxAllowedClamp));

            if (actualClamp > 0)
            {
                leftY += actualClamp;
                rightY += actualClamp;
            }
        }
    }

    // LILYPOND-REF: lily/beam-quanting.cc:891-953 Beam_scoring_problem::generate_quants()
    private List<BeamConfiguration> GenerateQuantCandidates(double initialLeftY, double initialRightY)
    {
        var candidates = new List<BeamConfiguration>();

        // Generate candidates in a region around the initial position
        // Quantize to half staff spaces (0.5 units)
        double quantStep = 0.5;
        double regionSize = _parameters.RegionSize;

        for (double leftOffset = -regionSize; leftOffset <= regionSize; leftOffset += quantStep)
        {
            for (double rightOffset = -regionSize; rightOffset <= regionSize; rightOffset += quantStep)
            {
                var config = new BeamConfiguration(
                    initialLeftY + leftOffset,
                    initialRightY + rightOffset);
                candidates.Add(config);
            }
        }

        return candidates;
    }

    // LILYPOND-REF: lily/beam-quanting.cc:955-988 Beam_scoring_problem::one_scorer()
    private void ScoreConfiguration(BeamConfiguration config)
    {
        // Apply all scorers in order
        ScoreOriginalDistance(config);
        ScoreSlopeIdeal(config);
        ScoreSlopeMusical(config);
        ScoreSlopeDirection(config);
        ScoreHorizontalInter(config);
        ScoreForbiddenQuants(config);
        ScoreStemLengths(config);
        ScoreCollisions(config);
    }

    private void ScoreOriginalDistance(BeamConfiguration config)
    {
        // Penalty for deviating from ideal position
        // This is handled implicitly by generating candidates around the initial position
        config.NextScorerTodo = (int)BeamScorer.SlopeIdeal;
    }

    private void ScoreSlopeIdeal(BeamConfiguration config)
    {
        // Penalty for non-ideal slope
        double slope = config.GetSlope(_xSpan);

        // Ideal slope based on note positions
        int firstPos = _staffPositions[0];
        int lastPos = _staffPositions[^1];
        double idealSlope = _xSpan > 0.001 ? (double)(lastPos - firstPos) / _xSpan : 0;
        idealSlope = Math.Clamp(idealSlope, -0.5, 0.5);

        double slopeDiff = Math.Abs(slope - idealSlope);
        double demerit = slopeDiff * _parameters.IdealSlopeFactor;

        if (demerit > _parameters.BeamEps)
            config.AddDemerit(demerit, "slope_ideal");

        config.NextScorerTodo = (int)BeamScorer.SlopeMusical;
    }

    private void ScoreSlopeMusical(BeamConfiguration config)
    {
        // Penalty when slope direction doesn't match musical direction
        double slope = config.GetSlope(_xSpan);

        int firstPos = _staffPositions[0];
        int lastPos = _staffPositions[^1];
        int musicalDirection = Math.Sign(lastPos - firstPos);
        int slopeDirection = Math.Sign(slope);

        if (musicalDirection != 0 && slopeDirection != 0 && musicalDirection != slopeDirection)
        {
            config.AddDemerit(_parameters.MusicalDirectionFactor, "slope_musical");
        }

        config.NextScorerTodo = (int)BeamScorer.SlopeDirection;
    }

    private void ScoreSlopeDirection(BeamConfiguration config)
    {
        // Penalty for slope opposing stem direction
        double slope = config.GetSlope(_xSpan);

        // For stem up, we generally want flat or downward slope
        // For stem down, we generally want flat or upward slope
        if (_group.StemUp && slope > _parameters.RoundToZeroSlope)
        {
            double demerit = slope * _parameters.DampingDirectionPenalty;
            config.AddDemerit(demerit, "slope_dir");
        }
        else if (!_group.StemUp && slope < -_parameters.RoundToZeroSlope)
        {
            double demerit = -slope * _parameters.DampingDirectionPenalty;
            config.AddDemerit(demerit, "slope_dir");
        }

        config.NextScorerTodo = (int)BeamScorer.HorizontalInter;
    }

    private void ScoreHorizontalInter(BeamConfiguration config)
    {
        // Penalty for beam crossing staff lines at awkward positions
        // Simplified: penalize if beam Y is very close to a staff line

        double leftY = config.LeftY;
        double rightY = config.RightY;

        // Check both ends
        foreach (var y in new[] { leftY, rightY })
        {
            // Staff lines are at even positions (0, 2, 4, 6, 8)
            // Penalize if beam is within 0.1 of a staff line (inter-line)
            double distToLine = Math.Abs(y - Math.Round(y / 2) * 2);
            if (distToLine < 0.1)
            {
                config.AddDemerit(_parameters.HorizontalInterQuantPenalty * (0.1 - distToLine), "horiz_inter");
            }
        }

        config.NextScorerTodo = (int)BeamScorer.Forbidden;
    }

    private void ScoreForbiddenQuants(BeamConfiguration config)
    {
        // Penalize secondary beams on staff lines
        // For 16th notes and shorter, the secondary beam shouldn't sit exactly on a line

        if (_maxBeamCount < 2)
        {
            config.NextScorerTodo = (int)BeamScorer.StemLengths;
            return;
        }

        double slope = config.GetSlope(_xSpan);

        for (int i = 0; i < _stemXPositions.Length; i++)
        {
            var member = _group.Members[i];
            if (member.BeamCount < 2)
                continue;

            double x = _stemXPositions[i];
            double beamY = config.GetYAt(x, _leftX, _xSpan);

            // Check secondary beam positions
            for (int level = 1; level < member.BeamCount; level++)
            {
                double secondaryY = _group.StemUp
                    ? beamY + level * BeamTranslation
                    : beamY - level * BeamTranslation;

                // Check if on staff line (even positions 0, 2, 4, 6, 8)
                double distToLine = Math.Abs(secondaryY - Math.Round(secondaryY / 2) * 2);
                if (distToLine < BeamThickness / 2)
                {
                    config.AddDemerit(_parameters.SecondaryBeamDemerit, "forbidden");
                }
            }
        }

        config.NextScorerTodo = (int)BeamScorer.StemLengths;
    }

    private void ScoreStemLengths(BeamConfiguration config)
    {
        // Penalize stems that are too short or too long
        double slope = config.GetSlope(_xSpan);

        for (int i = 0; i < _stemXPositions.Length; i++)
        {
            double x = _stemXPositions[i];
            double beamY = config.GetYAt(x, _leftX, _xSpan);
            double noteY = _staffPositions[i];

            double stemLength = _group.StemUp ? beamY - noteY : noteY - beamY;

            // Hard penalty for stems that are too short
            if (stemLength < MinStemLength)
            {
                double shortage = MinStemLength - stemLength;
                config.AddDemerit(shortage * _parameters.StemLengthLimitPenalty, "stem_short");
            }

            // Soft penalty for deviation from ideal
            double lengthDiff = Math.Abs(stemLength - IdealStemLength);
            double demerit = lengthDiff * _parameters.StemLengthDemeritFactor;
            if (demerit > _parameters.BeamEps)
            {
                config.AddDemerit(demerit, "stem_len");
            }
        }

        config.NextScorerTodo = (int)BeamScorer.Collisions;
    }

    private void ScoreCollisions(BeamConfiguration config)
    {
        // Penalty for beam colliding with other objects (rests, notes outside beam group)
        // Based on Lilypond's score_collisions from beam-quanting.cc

        if (_collisions.Count == 0)
        {
            config.NextScorerTodo = (int)BeamScorer.NumScorers;
            return;
        }

        double demerits = 0.0;

        foreach (var collision in _collisions)
        {
            // Skip collisions outside beam X range
            if (collision.X < _leftX || collision.X > _rightX)
                continue;

            // Get beam Y at collision X position
            double beamCenterY = config.GetYAt(collision.X, _leftX, _xSpan);

            // Beam Y range (center ± half thickness, including all beam levels)
            double beamHalfHeight = BeamThickness / 2 + (_maxBeamCount - 1) * BeamTranslation;
            double beamMinY, beamMaxY;
            if (_group.StemUp)
            {
                // Beam above notes: extends upward from center
                beamMinY = beamCenterY - BeamThickness / 2;
                beamMaxY = beamCenterY + beamHalfHeight;
            }
            else
            {
                // Beam below notes: extends downward from center
                beamMinY = beamCenterY - beamHalfHeight;
                beamMaxY = beamCenterY + BeamThickness / 2;
            }

            // Calculate distance between beam and collision object
            double dist;
            bool intersects = beamMaxY >= collision.MinY && beamMinY <= collision.MaxY;

            if (intersects)
            {
                dist = 0.0;
            }
            else
            {
                // Distance to nearest edge
                dist = Math.Min(
                    Math.Abs(beamMinY - collision.MaxY),
                    Math.Abs(beamMaxY - collision.MinY));
            }

            // Calculate penalty using cubic falloff
            double padding = _parameters.CollisionPadding;
            if (dist < padding)
            {
                double scaleFree = (padding - dist) / padding;
                double collisionDemerit = collision.BasePenalty
                    * Math.Pow(scaleFree, 3)
                    * _parameters.CollisionPenalty;
                demerits += collisionDemerit;
            }
        }

        if (demerits > _parameters.BeamEps)
        {
            config.AddDemerit(demerits, "collision");
        }

        config.NextScorerTodo = (int)BeamScorer.NumScorers;
    }

    // ========================================
    // Slope Damping
    // ========================================

    /// <summary>
    /// Applies slope damping based on concaveness.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/beam-quanting.cc:747-785 Beam_scoring_problem::slope_damping()
    ///
    /// If the beam has high concaveness (notes form a "bowl" shape), the slope
    /// is reduced to make the beam more horizontal. This improves readability.
    /// </remarks>
    private (double leftY, double rightY) ApplySlopeDamping(double leftY, double rightY)
    {
        if (_staffPositions.Length <= 1)
            return (leftY, rightY);

        double concaveness = CalculateConcaveness();
        double damping = _parameters.Damping;

        // If concaveness is very high, make the beam horizontal
        if (concaveness >= 10000 || damping >= 10000)
        {
            double avgY = (leftY + rightY) / 2;
            return (avgY, avgY);
        }

        // Apply damping if either damping or concaveness is non-zero
        if (damping > 0 && (damping + concaveness) > 0)
        {
            double dy = rightY - leftY;
            double slope = (_xSpan > 0.001) ? dy / _xSpan : 0;

            // Dampen slope using tanh for smooth falloff
            // LILYPOND-REF: lily/beam-quanting.cc:770
            slope = 0.6 * Math.Tanh(slope) / (damping + concaveness);

            double dampedDy = slope * _xSpan;
            double center = (leftY + rightY) / 2;

            leftY = center - dampedDy / 2;
            rightY = center + dampedDy / 2;
        }

        return (leftY, rightY);
    }

    /// <summary>
    /// Calculates the concaveness of the note pattern under the beam.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/beam-quanting.cc:695-746 Beam_scoring_problem::calc_concaveness()
    /// LILYPOND-REF: lily/beam-quanting.cc:671-692 calc_positions_concaveness()
    ///
    /// Concaveness measures how much the notes deviate from a straight line
    /// in the "wrong" direction (creating a bowl shape). High concaveness
    /// indicates the beam should be made more horizontal.
    /// </remarks>
    private double CalculateConcaveness()
    {
        if (_staffPositions.Length <= 2)
            return 0;

        // Calculate concaveness for the note positions
        var positions = _staffPositions.ToList();
        int beamDir = _group.StemUp ? 1 : -1;

        return CalculatePositionsConcaveness(positions, beamDir);
    }

    /// <summary>
    /// Calculates concaveness for a list of positions.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/beam-quanting.cc:671-692 calc_positions_concaveness()
    ///
    /// For each interior note, measures how far it deviates from the line
    /// connecting the first and last notes, in the direction of the beam.
    /// </remarks>
    private static double CalculatePositionsConcaveness(List<int> positions, int beamDir)
    {
        if (positions.Count <= 2)
            return 0;

        double dy = positions[^1] - positions[0];
        double slope = dy / (positions.Count - 1);
        double concaveness = 0;

        for (int i = 1; i < positions.Count - 1; i++)
        {
            double lineY = slope * i + positions[0];
            // Deviation in beam direction counts toward concaveness
            concaveness += Math.Max(beamDir * (positions[i] - lineY), 0);
        }

        concaveness /= positions.Count;

        // Normalize by the total change
        if (Math.Abs(dy) > 0.001)
            concaveness /= Math.Abs(dy);

        return concaveness;
    }
}