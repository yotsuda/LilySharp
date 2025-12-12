using System.Collections.Immutable;

namespace LilySharp.Core.Svg.Model;

/// <summary>
/// Represents a group of notes connected by a beam.
/// Based on Lilypond's beam representation (beam.cc, beaming-pattern.cc).
/// </summary>
public sealed record BeamGroup
{
    /// <summary>The notes in this beam group (NoteItem or ChordItem).</summary>
    public ImmutableArray<BeamMember> Members { get; }
    
    /// <summary>The measure index containing this beam group.</summary>
    public int MeasureIndex { get; }
    
    /// <summary>The start index within the measure's items.</summary>
    public int StartIndex { get; }
    
    /// <summary>Stem direction for the entire beam group (true = up, false = down).</summary>
    public bool StemUp { get; }
    
    public BeamGroup(
        ImmutableArray<BeamMember> members,
        int measureIndex,
        int startIndex,
        bool stemUp)
    {
        Members = members;
        MeasureIndex = measureIndex;
        StartIndex = startIndex;
        StemUp = stemUp;
    }
    
    /// <summary>Gets the number of notes in this beam group.</summary>
    public int Count => Members.Length;
}

/// <summary>
/// Represents a single member of a beam group.
/// </summary>
public sealed record BeamMember
{
    /// <summary>The underlying music item (NoteItem or ChordItem).</summary>
    public MusicItem Item { get; }
    
    /// <summary>
    /// Number of beam lines at this stem.
    /// 8th=1, 16th=2, 32nd=3, 64th=4, etc.
    /// </summary>
    public int BeamCount { get; }
    
    /// <summary>
    /// Number of beam lines on the left side of this stem.
    /// Used for partial beams (beamlets).
    /// </summary>
    public int BeamCountLeft { get; }
    
    /// <summary>
    /// Number of beam lines on the right side of this stem.
    /// Used for partial beams (beamlets).
    /// </summary>
    public int BeamCountRight { get; }
    
    /// <summary>
    /// Staff position of the note (or lowest note in chord).
    /// Used for beam slope calculation.
    /// </summary>
    public int StaffPosition { get; }
    
    /// <summary>Index of this member in the measure's items.</summary>
    public int ItemIndex { get; }
    
    public BeamMember(
        MusicItem item,
        int beamCount,
        int beamCountLeft,
        int beamCountRight,
        int staffPosition,
        int itemIndex)
    {
        Item = item;
        BeamCount = beamCount;
        BeamCountLeft = beamCountLeft;
        BeamCountRight = beamCountRight;
        StaffPosition = staffPosition;
        ItemIndex = itemIndex;
    }
}

/// <summary>
/// Represents the layout of a beam after position calculation.
/// </summary>
public sealed record BeamLayout
{
    /// <summary>The original beam group.</summary>
    public BeamGroup Group { get; }
    
    /// <summary>Y position of the beam at the first stem (in staff positions from middle line).</summary>
    public double LeftY { get; }
    
    /// <summary>Y position of the beam at the last stem (in staff positions from middle line).</summary>
    public double RightY { get; }
    
    /// <summary>X position of the first stem (in pixels).</summary>
    public double LeftX { get; }
    
    /// <summary>X position of the last stem (in pixels).</summary>
    public double RightX { get; }
    
    public BeamLayout(
        BeamGroup group,
        double leftY,
        double rightY,
        double leftX,
        double rightX)
    {
        Group = group;
        LeftY = leftY;
        RightY = rightY;
        LeftX = leftX;
        RightX = rightX;
    }
    
    /// <summary>Gets the slope of the beam (rise per unit run).</summary>
    public double Slope => (RightX - LeftX) > 0.001 
        ? (RightY - LeftY) / (RightX - LeftX) 
        : 0;
    
    /// <summary>Gets the Y position at a given X position.</summary>
    public double GetYAtX(double x) => LeftY + Slope * (x - LeftX);
}