using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Calculates beam positions and slopes.
/// Based on Lilypond's beam.cc and beam-quanting.cc.
/// </summary>
public sealed class BeamEngraver
{
    private readonly BeamQuantParameters _parameters;
    
    public BeamEngraver(BeamQuantParameters? parameters = null)
    {
        _parameters = parameters ?? BeamQuantParameters.Default;
    }
    
    /// <summary>
    /// Calculates the layout for a beam group.
    /// </summary>
    public BeamLayout CalculateBeamLayout(
        BeamGroup group,
        IReadOnlyList<double> itemXPositions,
        double staffSpaceSize)
    {
        if (group.Members.Length < 2)
            throw new ArgumentException("Beam group must have at least 2 members");
        
        // Get X positions for the beam
        var firstMember = group.Members[0];
        var lastMember = group.Members[^1];
        double leftX = itemXPositions[firstMember.ItemIndex];
        double rightX = itemXPositions[lastMember.ItemIndex];
        
        // Use BeamScoringProblem to find optimal beam positions
        var problem = new BeamScoringProblem(group, itemXPositions, staffSpaceSize, _parameters);
        var (leftY, rightY) = problem.Solve();
        
        return new BeamLayout(group, leftY, rightY, leftX, rightX);
    }
}