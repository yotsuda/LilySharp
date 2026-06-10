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
/// Solves the beam positioning problem by finding optimal quantized positions.
/// Faithful port of LilyPond's Beam_scoring_problem.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/beam-quanting.cc (full file)
/// LILYPOND-REF: lily/include/beam-scoring-problem.hh
///
/// The algorithm:
/// 1. Calculate initial (unquanted) position via least-squares fit
/// 2. Apply slope damping based on concaveness
/// 3. Shift region to valid range (avoid large collisions)
/// 4. Generate quantized candidates at straddle/sit/inter/hang positions
/// 5. Score candidates using priority queue (lazy evaluation)
/// 6. Return best candidate
/// </remarks>
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

    // LILYPOND-REF: lily/beam-quanting.cc:236 beam_thickness_, line_thickness_
    // All calculations are in staff-space units (not staff positions)
    private readonly double _beamThickness;
    private readonly double _lineThickness;
    private readonly double _beamTranslation;

    // Stem info — per-stem ideal/shortest now come from StemCalculator.CalculateBeamedStemInfo
    // (LilyPond calc_stem_info); only the flat minimum floor remains for EnsureMinimumStemLength.
    private readonly double _minStemLength;

    // Direction
    private readonly int _beamDir; // +1 for stem up, -1 for stem down

    // LILYPOND-REF: beam-quanting.cc:338 is_knee_
    private readonly bool _isKnee;

    // Per-member stem directions (needed for kneed beams)
    private readonly int[] _memberBeamDirs;

    // Per-member beam count (1 = eighth, 2 = sixteenth, ...) — drives per-stem
    // ideal/shortest stem length, matching LilyPond's Stem_info.
    private readonly int[] _memberBeamCounts;

    // Edge (first/last member) beam counts and stem directions.
    // LILYPOND-REF: beam-quanting.cc edge_beam_counts_, edge_dirs_
    private readonly int[] _edgeBeamCounts; // [0]=left, [1]=right
    private readonly int[] _edgeDirs;       // [0]=left, [1]=right

    // Staff radius (half staff height in half-spaces = 2.0 for 5-line staff)
    private const double StaffRadius = 2.0;

    // Musical dy (least-squares slope * xSpan, used by scorers)
    private double _musicalDy;

    // Unquanted Y positions (modified by damping and shift)
    private double _unquantedLeftY;
    private double _unquantedRightY;

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

        // Extract stem positions (in staff positions)
        _stemXPositions = new double[group.Members.Length];
        _staffPositions = new int[group.Members.Length];
        _memberBeamCounts = new int[group.Members.Length];
        _maxBeamCount = 0;

        for (int i = 0; i < group.Members.Length; i++)
        {
            var member = group.Members[i];
            _stemXPositions[i] = itemXPositions[member.ItemIndex] - _leftX; // relative to left
            _staffPositions[i] = member.StaffPosition;
            _memberBeamCounts[i] = Math.Max(1, member.BeamCount);
            _maxBeamCount = Math.Max(_maxBeamCount, member.BeamCount);
        }

        _beamDir = group.StemUp ? 1 : -1;

        // LILYPOND-REF: beam-quanting.cc:338 is_knee_
        _isKnee = group.IsKnee;

        // Per-member beam directions for kneed beams
        _memberBeamDirs = new int[group.Members.Length];
        for (int i = 0; i < group.Members.Length; i++)
        {
            _memberBeamDirs[i] = group.Members[i].MemberStemUp ? 1 : -1;
        }

        // LILYPOND-REF: beam-quanting.cc edge_beam_counts_ / edge_dirs_ —
        // forbidden-quant checks run per beam END with that end's own beam
        // count and stem direction (they differ for e.g. c16 d8 and knees).
        _edgeBeamCounts = new[] { _memberBeamCounts[0], _memberBeamCounts[^1] };
        _edgeDirs = _isKnee
            ? new[] { _memberBeamDirs[0], _memberBeamDirs[^1] }
            : new[] { _beamDir, _beamDir };

        // LILYPOND-REF: lily/beam-quanting.cc:236-238
        // Calculations are in staff-space units
        _beamThickness = EngravingDefaults.BeamThickness;  // 0.48 staff spaces
        _lineThickness = EngravingDefaults.StaffLineThickness;  // 0.13 staff spaces
        _beamTranslation = EngravingDefaults.BeamTranslation;
        _minStemLength = EngravingDefaults.MinStemLength;      // 2.5 staff spaces
    }

    /// <summary>
    /// Solves for the optimal beam position.
    /// Returns Y positions in staff positions (half staff-spaces).
    /// </summary>
    // LILYPOND-REF: lily/beam-quanting.cc:1022-1114 Beam_scoring_problem::solve()
    public (double leftY, double rightY) Solve()
    {
        // Phase 1: Calculate initial position via least-squares
        var (initialLeftY, initialRightY) = CalculateInitialPosition();
        _unquantedLeftY = initialLeftY;
        _unquantedRightY = initialRightY;
        _musicalDy = _unquantedRightY - _unquantedLeftY;

        // Phase 2: Apply slope damping based on concaveness
        // LILYPOND-REF: lily/beam-quanting.cc:748-779
        ApplySlopeDamping();

        // Phase 3: Shift region to valid (avoid large collision objects)
        // LILYPOND-REF: lily/beam-quanting.cc:781-894
        ShiftRegionToValid();

        // Phase 4: Generate quantized candidates
        // LILYPOND-REF: lily/beam-quanting.cc:896-958
        var candidates = GenerateQuantCandidates();

        if (candidates.Count == 0)
            return (_unquantedLeftY, _unquantedRightY);

        // Phase 5: Score using priority queue (lazy evaluation)
        // LILYPOND-REF: lily/beam-quanting.cc:1050-1083
        var queue = new PriorityQueue<BeamConfiguration, double>();
        foreach (var config in candidates)
            queue.Enqueue(config, config.Demerits);

        BeamConfiguration best;
        while (true)
        {
            best = queue.Dequeue();
            if (best.IsDone)
                break;

            OneScorer(best);
            queue.Enqueue(best, best.Demerits);
        }

        return (best.LeftY, best.RightY);
    }

    // ========================================
    // Initial Position (Least-Squares)
    // ========================================

    /// <summary>
    /// Calculates initial beam position based on stem ideal lengths.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/beam-quanting.cc:543-608 least_squares_positions()
    ///
    /// In staff-position units (half staff-spaces).
    /// For stem up, ideal Y = staffPos + idealStemLength * 2 (converted to staff positions).
    /// For stem down, ideal Y = staffPos - idealStemLength * 2.
    /// Then least-squares fit through all ideal endpoints.
    /// </remarks>
    private (double leftY, double rightY) CalculateInitialPosition()
    {
        if (_staffPositions.Length < 1)
            return (0, 0);

        double minStemLenPos = _minStemLength * 2;

        // Least-squares: find best fit line through ideal positions
        // LILYPOND-REF: lily/beam-quanting.cc:588-603
        // For kneed beams, use per-member stem direction so the ideal positions
        // naturally cluster in the gap between the two pitch groups.
        // The per-stem ideal beam Y comes from LilyPond's Stem_info (calc_stem_info),
        // NOT a flat 3.5-space length: it shortens stems near the staff and extends
        // far-from-staff stems toward the centre line, so the seed already matches
        // what ScoreStemLengths optimises against.
        // LILYPOND-REF: lily/stem.cc:1024-1155 calc_stem_info.
        var ideals = new List<(double x, double y)>();
        for (int i = 0; i < _staffPositions.Length; i++)
        {
            int dir = _isKnee ? _memberBeamDirs[i] : _beamDir;
            var info = StemCalculator.CalculateBeamedStemInfo(
                _staffPositions[i], dir > 0, _memberBeamCounts[i],
                _beamThickness, _beamTranslation);
            double idealY = info.IdealY * 2.0; // staff-space → staff-position frame
            ideals.Add((_stemXPositions[i], idealY));
        }

        double slope, intercept;
        if (ideals.Count == 1 || _xSpan < 0.001)
        {
            slope = 0;
            intercept = ideals[0].y;
        }
        else
        {
            // Least-squares linear regression
            MinimiseLeastSquares(ideals, out slope, out intercept);
        }

        double leftY = intercept;
        double rightY = intercept + slope * _xSpan;

        // LILYPOND-REF: lily/beam-quanting.cc:470-489 set_minimum_dy()
        // Ensure dy is not smaller than the smallest quant step
        double dy = rightY - leftY;
        if (Math.Abs(dy) > 0.001)
        {
            double sit = (_beamThickness - _lineThickness) / 2 * 2; // In staff positions
            double inter = 1.0; // 0.5 staff spaces = 1 staff position
            double hang = (1.0 - (_beamThickness - _lineThickness) / 2) * 2;
            double minDy = Math.Min(Math.Min(sit, inter), hang);
            dy = Math.Sign(dy) * Math.Max(Math.Abs(dy), minDy);

            double center = (leftY + rightY) / 2;
            leftY = center - dy / 2;
            rightY = center + dy / 2;
        }

        _musicalDy = rightY - leftY;

        // Ensure minimum stem length for all notes
        EnsureMinimumStemLength(ref leftY, ref rightY, minStemLenPos);

        return (leftY, rightY);
    }

    /// <summary>
    /// Least-squares linear regression.
    /// </summary>
    // LILYPOND-REF: lily/least-squares.cc minimise_least_squares()
    private static void MinimiseLeastSquares(
        List<(double x, double y)> points, out double slope, out double intercept)
    {
        int n = points.Count;
        double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;
        for (int i = 0; i < n; i++)
        {
            sumX += points[i].x;
            sumY += points[i].y;
            sumXY += points[i].x * points[i].y;
            sumX2 += points[i].x * points[i].x;
        }

        double denom = n * sumX2 - sumX * sumX;
        if (Math.Abs(denom) < 1e-10)
        {
            slope = 0;
            intercept = sumY / n;
        }
        else
        {
            slope = (n * sumXY - sumX * sumY) / denom;
            intercept = (sumY - slope * sumX) / n;
        }
    }

    private void EnsureMinimumStemLength(ref double leftY, ref double rightY, double minStemLenPos)
    {
        // For kneed beams, skip the uniform shift — per-member directions
        // mean there's no single shift direction that helps all stems.
        // The quanting scorer handles stem length penalties instead.
        if (_isKnee)
            return;

        double slope = _xSpan > 0.001 ? (rightY - leftY) / _xSpan : 0;
        double maxShortage = 0;

        for (int i = 0; i < _staffPositions.Length; i++)
        {
            double beamY = leftY + slope * _stemXPositions[i];
            double stemLength = _beamDir * (beamY - _staffPositions[i]);

            if (stemLength < minStemLenPos)
                maxShortage = Math.Max(maxShortage, minStemLenPos - stemLength);
        }

        if (maxShortage > 0)
        {
            leftY += _beamDir * maxShortage;
            rightY += _beamDir * maxShortage;
        }
    }

    // ========================================
    // Slope Damping
    // ========================================

    /// <summary>
    /// Applies slope damping based on concaveness.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/beam-quanting.cc:748-779 slope_damping()
    /// </remarks>
    private void ApplySlopeDamping()
    {
        if (_staffPositions.Length <= 1)
            return;

        double damping = _parameters.Damping;
        double concaveness = CalculateConcaveness();

        if (concaveness >= 10000 || damping >= 10000)
        {
            // Make beam horizontal
            // LILYPOND-REF: lily/beam-quanting.cc:757-762
            _unquantedRightY = _unquantedLeftY;
            _musicalDy = 0;
            return;
        }

        if (damping > 0 && (damping + concaveness) > 0)
        {
            double dy = _unquantedRightY - _unquantedLeftY;
            double slope = (_xSpan > 0.001) ? dy / _xSpan : 0;

            // LILYPOND-REF: lily/beam-quanting.cc:770
            slope = 0.6 * Math.Tanh(slope) / (damping + concaveness);

            double dampedDy = slope * _xSpan;

            // set_minimum_dy: don't let damping flatten the beam below the smallest
            // quant step (sit/inter/hang). LILYPOND-REF: lily/beam-quanting.cc:771
            //   (set_minimum_dy) + lily/beam-quanting.cc:470-489.
            if (Math.Abs(dampedDy) > 0.001)
            {
                double sit = (_beamThickness - _lineThickness) / 2 * 2;
                double inter = 1.0;
                double hang = (1.0 - (_beamThickness - _lineThickness) / 2) * 2;
                double minDy = Math.Min(Math.Min(sit, inter), hang);
                dampedDy = Math.Sign(dampedDy) * Math.Max(Math.Abs(dampedDy), minDy);
            }

            // LILYPOND-REF: lily/beam-quanting.cc:776-777
            _unquantedLeftY += (dy - dampedDy) / 2;
            _unquantedRightY -= (dy - dampedDy) / 2;
        }
    }

    /// <summary>
    /// Calculates beam concaveness.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/beam-quanting.cc:694-746 calc_concaveness()
    /// LILYPOND-REF: lily/beam-quanting.cc:624-669 is_concave_single_notes()
    /// LILYPOND-REF: lily/beam-quanting.cc:671-692 calc_positions_concaveness()
    /// </remarks>
    private double CalculateConcaveness()
    {
        // LILYPOND-REF: lily/beam-quanting.cc:700-702 — knees and cross-staff
        // beams are exempt from concaveness flattening.
        if (_isKnee || _group.IsCrossStaff)
            return 0;

        if (_staffPositions.Length <= 2)
            return 0;

        // LILYPOND-REF: lily/beam-quanting.cc:733-737
        // Check if notes form a concave pattern
        if (IsConcaveSingleNotes(_staffPositions, _beamDir))
            return 10000;

        // LILYPOND-REF: lily/beam-quanting.cc:740-743
        return CalcPositionsConcaveness(_staffPositions, _beamDir);
    }

    /// <summary>
    /// Determines whether notes form a concave pattern (bowl shape).
    /// </summary>
    // LILYPOND-REF: lily/beam-quanting.cc:624-669
    private static bool IsConcaveSingleNotes(int[] positions, int beamDir)
    {
        int first = positions[0];
        int last = positions[^1];
        int coveringUp = Math.Max(first, last);
        int coveringDown = Math.Min(first, last);

        bool above = false;
        bool below = false;

        // Check if interior notes go above and below the covering interval
        for (int i = 1; i < positions.Length - 1; i++)
        {
            above = above || (positions[i] > coveringUp);
            below = below || (positions[i] < coveringDown);
        }

        // Both above and below = concave
        if (above && below)
            return true;

        // Check for direction reversal near extremes
        int dy = last - first;
        int closest = Math.Max(beamDir * last, beamDir * first);
        for (int i = 2; i < positions.Length - 1; i++)
        {
            int innerDy = positions[i] - positions[i - 1];
            if (Math.Sign(innerDy) != Math.Sign(dy)
                && (beamDir * positions[i] >= closest
                    || beamDir * positions[i - 1] >= closest))
                return true;
        }

        // Check if all interior notes are closer to beam than endpoints
        bool allCloser = true;
        for (int i = 1; i < positions.Length - 1; i++)
        {
            if (beamDir * positions[i] <= closest)
            {
                allCloser = false;
                break;
            }
        }

        return allCloser;
    }

    /// <summary>
    /// Calculates numerical concaveness for a set of positions.
    /// </summary>
    // LILYPOND-REF: lily/beam-quanting.cc:671-692
    private static double CalcPositionsConcaveness(int[] positions, int beamDir)
    {
        double dy = positions[^1] - positions[0];
        double slope = dy / (positions.Length - 1);
        double concaveness = 0.0;

        for (int i = 1; i < positions.Length - 1; i++)
        {
            double lineY = slope * i + positions[0];
            concaveness += Math.Max(beamDir * (positions[i] - lineY), 0.0);
        }

        concaveness /= positions.Length;

        if (Math.Abs(dy) > 0.001)
            concaveness /= Math.Abs(dy);

        return concaveness;
    }

    // ========================================
    // Shift Region to Valid
    // ========================================

    /// <summary>
    /// Shifts the beam position to avoid large collision objects.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/beam-quanting.cc:781-894 shift_region_to_valid()
    ///
    /// Ensures all stems can reach minimum length, and avoids
    /// overlapping with large objects like key/time signatures.
    /// </remarks>
    private void ShiftRegionToValid()
    {
        if (_staffPositions.Length == 0)
            return;

        double beamDy = _unquantedRightY - _unquantedLeftY;
        double slope = _xSpan > 0.001 ? beamDy / _xSpan : 0;

        // Calculate feasible left point based on stem length constraints
        // LILYPOND-REF: lily/beam-quanting.cc:794-812
        double feasibleMin = double.NegativeInfinity;
        double feasibleMax = double.PositiveInfinity;

        for (int i = 0; i < _staffPositions.Length; i++)
        {
            // The minimum beam Y at this stem comes from the per-stem shortest_y of
            // calc_stem_info — NOT a flat 2.5-space length. The flat constant over-
            // constrained the tip note (shortest stem), pushing the whole beam up.
            // LILYPOND-REF: lily/beam-quanting.cc:794-805 (stem_infos_[i].shortest_y_).
            int dir = _isKnee ? _memberBeamDirs[i] : _beamDir;
            var info = StemCalculator.CalculateBeamedStemInfo(
                _staffPositions[i], dir > 0, _memberBeamCounts[i],
                _beamThickness, _beamTranslation);
            double minBeamY = info.ShortestY * 2.0; // staff-space → staff-position frame
            // Convert to left Y: leftY = beamAtStem - slope * stemX
            double leftYForMin = minBeamY - slope * _stemXPositions[i];

            if (dir > 0) // stem up: beam must be above minimum
                feasibleMin = Math.Max(feasibleMin, leftYForMin);
            else // stem down: beam must be below minimum
                feasibleMax = Math.Min(feasibleMax, leftYForMin);
        }

        // Clamp to feasible region
        double beamLeftY = _unquantedLeftY;
        if (_beamDir > 0 && beamLeftY < feasibleMin)
            beamLeftY = feasibleMin;
        else if (_beamDir < 0 && beamLeftY > feasibleMax)
            beamLeftY = feasibleMax;

        _unquantedLeftY = beamLeftY;
        _unquantedRightY = beamLeftY + beamDy;
    }

    // ========================================
    // Quant Generation
    // ========================================

    /// <summary>
    /// Generates quantized beam position candidates.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/beam-quanting.cc:896-958 generate_quants()
    ///
    /// LilyPond uses 4 base quant positions per half-space:
    /// - straddle (0.0): beam center on staff line
    /// - sit: beam edge just touches staff line from above
    /// - inter (0.5): beam center between staff lines
    /// - hang: beam edge just touches staff line from below
    ///
    /// These ensure beams relate properly to staff lines.
    /// </remarks>
    private List<BeamConfiguration> GenerateQuantCandidates()
    {
        var regionSize = (int)_parameters.RegionSize;

        // LILYPOND-REF: lily/beam-quanting.cc:901-906
        // Knees and collisions are harder, try more possibilities
        if (_isKnee)
            regionSize += 2;
        if (_collisions.Count > 0)
            regionSize += 2;

        // LILYPOND-REF: lily/beam-quanting.cc:908-912
        // The 4 base quant positions (in staff spaces, relative to staff line)
        double straddle = 0.0;
        double sit = (_beamThickness - _lineThickness) / 2;
        double inter = 0.5;
        double hang = 1.0 - (_beamThickness - _lineThickness) / 2;
        double[] baseQuants = { straddle, sit, inter, hang };

        // LILYPOND-REF: lily/beam-quanting.cc:911-918 — with more than 4 beams
        // the outer beam (used for quanting) never meets the staff lines, but
        // pins the inner beams awkwardly; shift the quant grid to compensate.
        double gridShift = 0.0;
        if (!_isKnee && _maxBeamCount > 4)
            gridShift = (_maxBeamCount - 4) * (1.0 - _beamTranslation);

        // Convert unquanted_y from staff positions to staff spaces for quanting
        double unquantedLeftSS = _unquantedLeftY / 2.0;
        double unquantedRightSS = _unquantedRightY / 2.0;

        // LILYPOND-REF: lily/beam-quanting.cc:927-932
        var unshiftedQuants = new List<double>();
        for (int i = -regionSize; i < regionSize; i++)
        {
            foreach (double bq in baseQuants)
                unshiftedQuants.Add(i + bq);
        }

        // LILYPOND-REF: lily/beam-quanting.cc:934-957
        var candidates = new List<BeamConfiguration>();
        for (int i = 0; i < unshiftedQuants.Count; i++)
        {
            for (int j = 0; j < unshiftedQuants.Count; j++)
            {
                // LILYPOND-REF: lily/beam-quanting.cc:933-938 — apply the grid
                // shift only when the quant lies outside the 5-line staff.
                double corrLeft = 0.0, corrRight = 0.0;
                if (gridShift != 0.0)
                {
                    if ((unquantedLeftSS + unshiftedQuants[i]) * _edgeDirs[0] > 2.5)
                        corrLeft = gridShift * _edgeDirs[0];
                    if ((unquantedRightSS + unshiftedQuants[j]) * _edgeDirs[1] > 2.5)
                        corrRight = gridShift * _edgeDirs[1];
                }

                // New config: truncate to integer + add quant offset
                // LILYPOND-REF: lily/beam-quanting.cc:161
                double leftYSS = (int)unquantedLeftSS + unshiftedQuants[i] - corrLeft;
                double rightYSS = (int)unquantedRightSS + unshiftedQuants[j] - corrRight;

                // Convert back to staff positions
                double leftY = leftYSS * 2.0;
                double rightY = rightYSS * 2.0;

                // LILYPOND-REF: lily/beam-quanting.cc:166-167
                // Initial demerit based on distance from ideal (closer = better)
                double startScore = Math.Abs(unshiftedQuants[j]) + Math.Abs(unshiftedQuants[i]);
                var config = new BeamConfiguration(leftY, rightY)
                {
                    Demerits = startScore / 1000.0,
                    NextScorerTodo = (int)BeamScorer.SlopeIdeal
                };

                candidates.Add(config);
            }
        }

        return candidates;
    }

    // ========================================
    // Scoring
    // ========================================

    /// <summary>
    /// Applies the next scorer to a configuration.
    /// </summary>
    // LILYPOND-REF: lily/beam-quanting.cc:960-993 one_scorer()
    private void OneScorer(BeamConfiguration config)
    {
        switch ((BeamScorer)config.NextScorerTodo)
        {
            case BeamScorer.SlopeIdeal:
                ScoreSlopeIdeal(config);
                break;
            case BeamScorer.SlopeDirection:
                ScoreSlopeDirection(config);
                break;
            case BeamScorer.SlopeMusical:
                ScoreSlopeMusical(config);
                break;
            case BeamScorer.HorizontalInter:
                ScoreHorizontalInter(config);
                break;
            case BeamScorer.Forbidden:
                ScoreForbiddenQuants(config);
                break;
            case BeamScorer.StemLengths:
                ScoreStemLengths(config);
                break;
            case BeamScorer.Collisions:
                ScoreCollisions(config);
                break;
        }
        config.NextScorerTodo++;
    }

    /// <summary>
    /// Penalizes deviation from ideal slope.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/beam-quanting.cc:1217-1235 score_slope_ideal()
    ///
    /// Uses shrink_extra_weight: |x| * (x &lt; 0 ? 1.5 : 1.0)
    /// to penalize being too flat more than too steep.
    /// </remarks>
    private void ScoreSlopeIdeal(BeamConfiguration config)
    {
        double dy = config.RightY - config.LeftY;
        double dampedDy = _unquantedRightY - _unquantedLeftY;

        double slopePenalty = _parameters.IdealSlopeFactor;

        // LILYPOND-REF: lily/beam-quanting.cc:1224-1226 — cross-staff beams tend
        // to use extreme slopes to get short stems; penalise the slope 10×.
        if (_group.IsCrossStaff)
            slopePenalty *= 10;

        // LILYPOND-REF: lily/beam-quanting.cc:1228-1230
        double diff = Math.Abs(dampedDy) - Math.Abs(dy);
        double dem = ShrinkExtraWeight(diff, 1.5) * slopePenalty;

        config.AddDemerit(dem, "Si");
    }

    /// <summary>
    /// Penalizes slope direction opposing damped direction.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/beam-quanting.cc:1177-1203 score_slope_direction()
    /// </remarks>
    private void ScoreSlopeDirection(BeamConfiguration config)
    {
        double dy = config.RightY - config.LeftY;
        double dampedDy = _unquantedRightY - _unquantedLeftY;
        double dem = 0.0;

        // LILYPOND-REF: lily/beam-quanting.cc:1189-1200
        if (Math.Sign(dampedDy) != Math.Sign(dy))
        {
            if (Math.Abs(dy) < 0.001) // dy == 0 (horizontal candidate)
            {
                if (Math.Abs(dampedDy / Math.Max(_xSpan, 0.001)) > _parameters.RoundToZeroSlope)
                    dem += _parameters.DampingDirectionPenalty;
                else
                    dem += _parameters.HintDirectionPenalty;
            }
            else
            {
                dem += _parameters.DampingDirectionPenalty;
            }
        }

        config.AddDemerit(dem, "Sd");
    }

    /// <summary>
    /// Penalizes slope exceeding musical direction.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/beam-quanting.cc:1206-1213 score_slope_musical()
    /// </remarks>
    private void ScoreSlopeMusical(BeamConfiguration config)
    {
        double dy = config.RightY - config.LeftY;

        // LILYPOND-REF: lily/beam-quanting.cc:1210-1211
        double dem = _parameters.MusicalDirectionFactor
                     * Math.Max(0.0, Math.Abs(dy) - Math.Abs(_musicalDy));

        config.AddDemerit(dem, "Sm");
    }

    /// <summary>
    /// Penalizes horizontal beams landing between staff lines.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/beam-quanting.cc:1247-1257 score_horizontal_inter_quants()
    ///
    /// Only applies to horizontal beams (dy == 0) within the staff.
    /// Penalizes beams positioned exactly between two staff lines.
    /// </remarks>
    private void ScoreHorizontalInter(BeamConfiguration config)
    {
        double dy = config.RightY - config.LeftY;

        // LILYPOND-REF: lily/beam-quanting.cc:1250-1251
        // Only penalize horizontal beams within staff
        if (Math.Abs(dy) < 0.001 && Math.Abs(config.LeftY) < StaffRadius * 2)
        {
            // config.LeftY is in staff positions. Staff lines at even positions.
            // Check if beam is at half-integer position (between lines) = 0.5 staff spaces
            double yInStaffSpaces = config.LeftY / 2.0;
            double yShifted = yInStaffSpaces - 0.5;
            double rounded = Math.Round(yShifted);
            if (Math.Abs(rounded - yShifted) < 0.01)
            {
                config.AddDemerit(_parameters.HorizontalInterQuantPenalty, "H");
            }
        }
    }

    /// <summary>
    /// Penalizes beams where secondary beams (16th+) sit on staff lines.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/beam-quanting.cc:1264-1369 score_forbidden_quants()
    ///
    /// For each beam end, checks whether the gap between adjacent beams
    /// contains a staff line. If so, adds a demerit.
    /// </remarks>
    private void ScoreForbiddenQuants(BeamConfiguration config)
    {
        // LILYPOND-REF: lily/beam-quanting.cc:1266-1268 — divided by the larger
        // EDGE beam count (not the global maximum over all members).
        double extraDemerit = _parameters.SecondaryBeamDemerit
            / Math.Max(Math.Max(_edgeBeamCounts[0], _edgeBeamCounts[1]), 1);

        double dem = 0.0;
        double eps = _parameters.BeamEps;

        // Check each beam end with that end's own beam count and direction.
        // LILYPOND-REF: lily/beam-quanting.cc:1273-1325
        for (int e = 0; e < 2; e++)
        {
            double endYSS = (e == 0 ? config.LeftY : config.RightY) / 2.0; // staff positions → spaces
            int stemDir = _edgeDirs[e];

            for (int j = 1; j <= _edgeBeamCounts[e]; j++)
            {
                // LILYPOND-REF: lily/beam-quanting.cc:1280-1294
                double fudgeFactor = 2.2;
                double gap1 = endYSS
                    - stemDir * ((j - 1) * _beamTranslation + _beamThickness / 2
                                  - _lineThickness / fudgeFactor);
                double gap2 = endYSS
                    - stemDir * (j * _beamTranslation - _beamThickness / 2
                                  + _lineThickness / fudgeFactor);

                double gapMin = Math.Min(gap1, gap2);
                double gapMax = Math.Max(gap1, gap2);
                double gapLength = gapMax - gapMin;

                // LILYPOND-REF: lily/beam-quanting.cc:1300-1322
                // Check if any staff line falls within the gap
                for (double k = -StaffRadius; k <= StaffRadius + eps; k += 1.0)
                {
                    if (k >= gapMin && k <= gapMax)
                    {
                        double dist = Math.Min(Math.Abs(gapMax - k), Math.Abs(gapMin - k));
                        double fixedDemerit = 0.39;
                        dem += extraDemerit
                               * (fixedDemerit + (1 - fixedDemerit) * (dist / Math.Max(gapLength, eps)) * 2);
                    }
                }
            }
        }

        config.AddDemerit(dem, "Fl");

        // LILYPOND-REF: lily/beam-quanting.cc:1327-1366
        // Additional forbidden quants for 2+ beam counts
        dem = 0.0;
        if (Math.Max(_edgeBeamCounts[0], _edgeBeamCounts[1]) >= 2)
        {
            double straddle = 0.0;
            double sit = (_beamThickness - _lineThickness) / 2;
            double hang = 1.0 - (_beamThickness - _lineThickness) / 2;
            double dy = config.RightY - config.LeftY;

            for (int e = 0; e < 2; e++)
            {
                double endYSS = (e == 0 ? config.LeftY : config.RightY) / 2.0;
                int edgeDir = _edgeDirs[e];
                double frac = endYSS - Math.Floor(endYSS); // my_modf

                if (_edgeBeamCounts[e] >= 2
                    && Math.Abs(endYSS - edgeDir * _beamTranslation) < StaffRadius + 0.5)
                {
                    if (edgeDir > 0 && dy <= eps && Math.Abs(frac - sit) < eps)
                        dem += extraDemerit;
                    if (edgeDir < 0 && dy >= eps && Math.Abs(frac - hang) < eps)
                        dem += extraDemerit;
                }

                // LILYPOND-REF: lily/beam-quanting.cc:1352-1365 — the straddle
                // check is also gated by edge direction and slope sign.
                if (_edgeBeamCounts[e] >= 3
                    && Math.Abs(endYSS - 2 * edgeDir * _beamTranslation) < StaffRadius + 0.5)
                {
                    if (edgeDir > 0 && dy <= eps && Math.Abs(frac - straddle) < eps)
                        dem += extraDemerit;
                    if (edgeDir < 0 && dy >= eps && Math.Abs(frac - straddle) < eps)
                        dem += extraDemerit;
                }
            }
        }

        config.AddDemerit(dem, "Fs");
    }

    /// <summary>
    /// Penalizes stems that are too short or deviate from ideal length.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/beam-quanting.cc:1117-1174 score_stem_lengths()
    ///
    /// Uses asymmetric penalty (shrink_extra_weight) and divides by
    /// stem count for scale-free measurement.
    /// </remarks>
    private void ScoreStemLengths(BeamConfiguration config)
    {
        double limitPenalty = _parameters.StemLengthLimitPenalty;
        double lengthPen = _parameters.StemLengthDemeritFactor;

        // LILYPOND-REF: lily/beam-quanting.cc:1120-1121
        double[] score = { 0, 0 }; // [DOWN=0, UP=1]
        int[] count = { 0, 0 };

        for (int i = 0; i < _stemXPositions.Length; i++)
        {
            double x = _stemXPositions[i];
            // LILYPOND-REF: lily/beam-quanting.cc:1130-1133
            double beamY = _xSpan > 0.001
                ? config.RightY * x / _xSpan + config.LeftY * (_xSpan - x) / _xSpan
                : (config.RightY + config.LeftY) / 2;

            double currentY = beamY;  // beam Y at this stem

            // For kneed beams, use per-member stem direction
            int memberDir = _isKnee ? _memberBeamDirs[i] : _beamDir;
            int d = memberDir > 0 ? 1 : 0; // index into score array

            // Per-stem ideal/shortest beam Y, varying with beam count (16th/32nd
            // stems are longer) — LilyPond's Stem_info, not a flat constant.
            // CalculateBeamedStemInfo returns staff-SPACE Y; ×2 converts to the
            // staff-position (half-space) frame the quanter uses.
            // LILYPOND-REF: lily/stem.cc:1137 calc_stem_info;
            //               lily/beam-quanting.cc score_stem_lengths.
            var info = StemCalculator.CalculateBeamedStemInfo(
                _staffPositions[i], memberDir > 0, _memberBeamCounts[i],
                _beamThickness, _beamTranslation);
            double idealY = info.IdealY * 2.0;
            double shortestY = info.ShortestY * 2.0;

            // LILYPOND-REF: lily/beam-quanting.cc:1139-1140
            // Penalty for stems shorter than minimum
            double shortage = memberDir * (shortestY - currentY);
            score[d] += limitPenalty * Math.Max(0.0, shortage);

            // LILYPOND-REF: lily/beam-quanting.cc:1142-1143
            // Penalty for deviation from ideal
            double idealDiff = memberDir * (currentY - idealY);
            double idealScore = ShrinkExtraWeight(idealDiff, 1.5);

            // LILYPOND-REF: lily/beam-quanting.cc:1145-1149
            // Power function for knees: makes scoring strictly convex so that
            // symmetric knee beams have a unique optimum in the gap center.
            if (_isKnee)
                idealScore = Math.Pow(idealScore, 1.1);

            score[d] += lengthPen * idealScore;

            count[d]++;
        }

        // LILYPOND-REF: lily/beam-quanting.cc:1152-1155
        // Divide by number of stems, to make the measure scale-free.
        for (int d = 0; d < 2; d++)
            score[d] /= Math.Max(count[d], 1);

        // LILYPOND-REF: lily/beam-quanting.cc:1157-1169 — symmetric 2-stem knees
        // can tie; nudge the quanting toward the slope direction so the first
        // stem reads as the longer one.
        if (_isKnee && count[0] == count[1] && count[0] == 1)
        {
            int d = Math.Sign(_unquantedRightY - _unquantedLeftY);
            if (d != 0)
            {
                int idx = d > 0 ? 1 : 0;
                if (score[idx] < 1.0)
                    score[idx] += 0.01;
            }
        }

        config.AddDemerit(score[0] + score[1], "L");
    }

    /// <summary>
    /// Penalizes beam collisions with other objects.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/beam-quanting.cc:1370-1401 score_collisions()
    ///
    /// Uses cubic falloff: penalty * ((padding - dist) / padding)^3
    /// </remarks>
    private void ScoreCollisions(BeamConfiguration config)
    {
        if (_collisions.Count == 0)
            return;

        double demerits = 0.0;

        foreach (var collision in _collisions)
        {
            if (collision.X < 0 || collision.X > _xSpan)
                continue;

            // LILYPOND-REF: lily/beam-quanting.cc:1380
            double centerBeamY = config.GetYAt(collision.X + _leftX, _leftX, _xSpan);

            // Convert collision Y to staff positions if needed
            double beamYMin = centerBeamY - _beamThickness;  // Approximate beam extent
            double beamYMax = centerBeamY + _beamThickness;

            double dist;
            bool intersects = beamYMax >= collision.MinY && beamYMin <= collision.MaxY;

            if (intersects)
            {
                dist = 0.0;
            }
            else
            {
                dist = Math.Min(
                    Math.Abs(beamYMin - collision.MaxY),
                    Math.Abs(beamYMax - collision.MinY));
            }

            // LILYPOND-REF: lily/beam-quanting.cc:1390-1394
            double padding = _parameters.CollisionPadding * 2; // Convert to staff positions
            double scaleFree = Math.Max(padding - dist, 0.0) / Math.Max(padding, 0.001);
            double collisionDemerit = collision.BasePenalty
                                     * Math.Pow(scaleFree, 3)
                                     * _parameters.CollisionPenalty;

            if (collisionDemerit > 0)
                demerits += collisionDemerit;
        }

        if (demerits > _parameters.BeamEps)
            config.AddDemerit(demerits, "C");
    }

    // ========================================
    // Utility
    // ========================================

    /// <summary>
    /// Asymmetric weight function: |x| if x >= 0, |x| * fac if x &lt; 0.
    /// </summary>
    // LILYPOND-REF: lily/beam-quanting.cc:128-132 shrink_extra_weight()
    private static double ShrinkExtraWeight(double x, double fac)
    {
        return Math.Abs(x) * (x < 0 ? fac : 1.0);
    }
}
