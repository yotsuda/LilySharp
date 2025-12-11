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
    private readonly double _staffSpaceSize;
    private readonly BeamQuantParameters _parameters;
    
    // Computed values
    private readonly double _xSpan;
    private readonly double _leftX;
    private readonly double _rightX;
    private readonly double[] _stemXPositions;
    private readonly int[] _staffPositions;
    private readonly int _maxBeamCount;
    
    // Beam constants
    private const double BeamThickness = 0.48; // staff spaces
    private const double BeamTranslation = 0.58; // distance between beams
    private const double IdealStemLength = 3.5;
    private const double MinStemLength = 2.5;
    
    public BeamScoringProblem(
        BeamGroup group,
        IReadOnlyList<double> itemXPositions,
        double staffSpaceSize,
        BeamQuantParameters? parameters = null)
    {
        _group = group;
        _itemXPositions = itemXPositions;
        _staffSpaceSize = staffSpaceSize;
        _parameters = parameters ?? BeamQuantParameters.Default;
        
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
    public (double leftY, double rightY) Solve()
    {
        // Generate initial position based on note positions
        var (initialLeftY, initialRightY) = CalculateInitialPosition();
        
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
            // Beam above notes (smaller Y in staff coordinates)
            leftY = firstPos - IdealStemLength;
            rightY = leftY + naturalSlope * _xSpan;
            
            // Ensure minimum stem length
            AdjustForMinimumStemLength(ref leftY, ref rightY, stemUp: true);
        }
        else
        {
            // Beam below notes (larger Y in staff coordinates)
            leftY = firstPos + IdealStemLength;
            rightY = leftY + naturalSlope * _xSpan;
            
            // Ensure minimum stem length
            AdjustForMinimumStemLength(ref leftY, ref rightY, stemUp: false);
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
            
            double stemLength = stemUp ? noteY - beamY : beamY - noteY;
            
            if (stemLength < MinStemLength)
            {
                maxAdjustment = Math.Max(maxAdjustment, MinStemLength - stemLength);
            }
        }
        
        if (maxAdjustment > 0)
        {
            if (stemUp)
            {
                leftY -= maxAdjustment;
                rightY -= maxAdjustment;
            }
            else
            {
                leftY += maxAdjustment;
                rightY += maxAdjustment;
            }
        }
    }
    
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
            
            double stemLength = _group.StemUp ? noteY - beamY : beamY - noteY;
            
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
}