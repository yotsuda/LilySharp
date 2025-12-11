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
    private const double MinStemLength = 2.5; // minimum stem length in staff spaces
    private const double IdealStemLength = 3.5; // ideal stem length
    private const double MaxSlope = 0.5; // maximum beam slope (staff spaces per staff space)
    
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
        
        // Calculate ideal beam positions
        var (leftY, rightY) = CalculateBeamPositions(group, leftX, rightX, staffSpaceSize);
        
        // Calculate stem end Y positions for each member
        var stemEndYs = CalculateStemEndYs(group, leftX, leftY, rightX, rightY, itemXPositions);
        
        return new BeamLayout(group, leftY, rightY, leftX, rightX, stemEndYs);
    }
    
    private (double leftY, double rightY) CalculateBeamPositions(
        BeamGroup group,
        double leftX,
        double rightX,
        double staffSpaceSize)
    {
        // Get extremal positions
        int firstPos = group.Members[0].StaffPosition;
        int lastPos = group.Members[^1].StaffPosition;
        
        // Find min and max positions in the group
        int minPos = int.MaxValue;
        int maxPos = int.MinValue;
        foreach (var member in group.Members)
        {
            minPos = Math.Min(minPos, member.StaffPosition);
            maxPos = Math.Max(maxPos, member.StaffPosition);
        }
        
        // Calculate natural slope based on first and last note
        double naturalSlope = 0;
        double span = rightX - leftX;
        if (span > 0.001)
        {
            naturalSlope = (double)(lastPos - firstPos) / span;
        }
        
        // Clamp slope
        naturalSlope = Math.Clamp(naturalSlope, -MaxSlope, MaxSlope);
        
        // Calculate beam Y positions
        double leftY, rightY;
        
        if (group.StemUp)
        {
            // Stems up: beam above notes
            // Y increases downward in staff coordinates, so "above" means smaller Y
            double stemEnd = minPos - IdealStemLength;
            leftY = firstPos - IdealStemLength - naturalSlope * span / 2;
            rightY = leftY + naturalSlope * span;
            
            // Ensure minimum stem length for all notes
            EnsureMinimumStemLength(group, ref leftY, ref rightY, leftX, rightX, stemUp: true);
        }
        else
        {
            // Stems down: beam below notes
            double stemEnd = maxPos + IdealStemLength;
            leftY = firstPos + IdealStemLength - naturalSlope * span / 2;
            rightY = leftY + naturalSlope * span;
            
            // Ensure minimum stem length for all notes
            EnsureMinimumStemLength(group, ref leftY, ref rightY, leftX, rightX, stemUp: false);
        }
        
        // Quantize to staff positions (optional - Lilypond does this for alignment)
        // For now, skip quantization for simplicity
        
        return (leftY, rightY);
    }
    
    private void EnsureMinimumStemLength(
        BeamGroup group,
        ref double leftY,
        ref double rightY,
        double leftX,
        double rightX,
        bool stemUp)
    {
        double slope = (rightX - leftX) > 0.001 ? (rightY - leftY) / (rightX - leftX) : 0;
        double adjustment = 0;
        
        // For each member, check if stem is long enough
        // We need itemXPositions here, but for initial calculation, 
        // we use linear interpolation based on member index
        for (int i = 0; i < group.Members.Length; i++)
        {
            var member = group.Members[i];
            double t = group.Members.Length > 1 ? (double)i / (group.Members.Length - 1) : 0;
            double beamY = leftY + slope * (rightX - leftX) * t;
            
            double stemLength = stemUp 
                ? member.StaffPosition - beamY 
                : beamY - member.StaffPosition;
            
            if (stemLength < MinStemLength)
            {
                double needed = MinStemLength - stemLength;
                adjustment = Math.Max(adjustment, needed);
            }
        }
        
        // Apply adjustment
        if (adjustment > 0)
        {
            if (stemUp)
            {
                leftY -= adjustment;
                rightY -= adjustment;
            }
            else
            {
                leftY += adjustment;
                rightY += adjustment;
            }
        }
    }
    
    private ImmutableArray<double> CalculateStemEndYs(
        BeamGroup group,
        double leftX,
        double leftY,
        double rightX,
        double rightY,
        IReadOnlyList<double> itemXPositions)
    {
        double slope = (rightX - leftX) > 0.001 ? (rightY - leftY) / (rightX - leftX) : 0;
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