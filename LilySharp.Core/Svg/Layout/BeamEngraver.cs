using System.Collections.Immutable;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Calculates beam positions and slopes.
/// Based on Lilypond's beam.cc and beam-quanting.cc.
/// </summary>
public sealed class BeamEngraver
{
    // Constants matching Lilypond's defaults
    private const double BeamThickness = 0.48; // staff spaces
    private const double BeamTranslation = 0.58; // distance between multiple beams
    
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
        
        // Calculate stem end Y positions for each member
        var stemEndYs = CalculateStemEndYs(group, leftX, leftY, rightX, rightY, itemXPositions);
        
        return new BeamLayout(group, leftY, rightY, leftX, rightX, stemEndYs);
    }
    
    private ImmutableArray<double> CalculateStemEndYs(
        BeamGroup group,
        double leftX,
        double leftY,
        double rightX,
        double rightY,
        IReadOnlyList<double> itemXPositions)
    {
        double xSpan = rightX - leftX;
        double slope = xSpan > 0.001 ? (rightY - leftY) / xSpan : 0;
        var stemEndYs = new double[group.Members.Length];
        
        for (int i = 0; i < group.Members.Length; i++)
        {
            var member = group.Members[i];
            double memberX = itemXPositions[member.ItemIndex];
            double beamY = leftY + slope * (memberX - leftX);
            
            // Stem ends at the beam (adjusted for beam thickness and multiple beams)
            // For multiple beams, the stem connects to the outermost beam
            int numBeams = member.BeamCount;
            double beamOffset = (numBeams - 1) * BeamTranslation;
            
            if (group.StemUp)
            {
                // Stem goes up, beam is above, offset goes up (negative Y)
                stemEndYs[i] = beamY - beamOffset;
            }
            else
            {
                // Stem goes down, beam is below, offset goes down (positive Y)
                stemEndYs[i] = beamY + beamOffset;
            }
        }
        
        return stemEndYs.ToImmutableArray();
    }
    
    /// <summary>
    /// Gets the beam thickness in staff spaces.
    /// </summary>
    public double GetBeamThickness() => BeamThickness;
    
    /// <summary>
    /// Gets the translation between multiple beams in staff spaces.
    /// </summary>
    public double GetBeamTranslation() => BeamTranslation;
}