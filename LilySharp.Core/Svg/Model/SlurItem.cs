using System.Collections.Immutable;

namespace LilySharp.Core.Svg.Model;

/// <summary>
/// Represents a slur connecting multiple notes for phrasing.
/// A slur is a curved line that groups notes together,
/// indicating they should be played legato.
/// </summary>
public sealed record SlurItem
{
    /// <summary>The starting note of the slur.</summary>
    public NoteItem StartNote { get; }

    /// <summary>The ending note of the slur.</summary>
    public NoteItem EndNote { get; }

    /// <summary>Staff position at start.</summary>
    public int StartStaffPosition { get; }

    /// <summary>Staff position at end.</summary>
    public int EndStaffPosition { get; }

    /// <summary>Direction of the slur curve (up or down).</summary>
    public bool CurveUp { get; }

    /// <summary>Measure index where the slur starts.</summary>
    public int StartMeasureIndex { get; }

    /// <summary>Measure index where the slur ends.</summary>
    public int EndMeasureIndex { get; }

    /// <summary>Index of the start note within its measure.</summary>
    public int StartItemIndex { get; }

    /// <summary>Index of the end note within its measure.</summary>
    public int EndItemIndex { get; }

    public SlurItem(
        NoteItem startNote,
        NoteItem endNote,
        int startStaffPosition,
        int endStaffPosition,
        bool curveUp,
        int startMeasureIndex,
        int endMeasureIndex,
        int startItemIndex,
        int endItemIndex)
    {
        StartNote = startNote;
        EndNote = endNote;
        StartStaffPosition = startStaffPosition;
        EndStaffPosition = endStaffPosition;
        CurveUp = curveUp;
        StartMeasureIndex = startMeasureIndex;
        EndMeasureIndex = endMeasureIndex;
        StartItemIndex = startItemIndex;
        EndItemIndex = endItemIndex;
    }
}