using System.Collections.Immutable;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Information about a positioned accidental within a chord.
/// </summary>
public readonly record struct AccidentalLayout(
    /// <summary>Staff position of the note.</summary>
    int StaffPosition,
    /// <summary>The accidental type (sharp, flat, natural, etc.).</summary>
    string Accidental,
    /// <summary>X offset from the note column in staff spaces (negative = left of note).</summary>
    double XOffset
);

/// <summary>
/// LILYPOND-REF: lily/accidental-placement.cc:1-532 Accidental_placement
/// Parameters for accidental placement. All dimensions in staff spaces.
/// </summary>
public sealed record AccidentalPlacementParameters
{
    public static AccidentalPlacementParameters Default { get; } = new();
    
    /// <summary>Padding between accidentals in staff spaces.</summary>
    public double Padding { get; init; } = 0.2;
    
    /// <summary>Padding from note head in staff spaces.</summary>
    public double RightPadding { get; init; } = 0.15;
    
    /// <summary>Minimum distance for staggering in staff positions.</summary>
    public double StaggerThreshold { get; init; } = 6;
}

/// <summary>
/// Calculates positions for accidentals in chords.
/// Based on Lilypond's accidental-placement.cc
/// All coordinates are in staff spaces.
/// 
/// Algorithm:
/// 1. Group accidentals by note name (for octave alignment)
/// 2. Sort by staff position
/// 3. Stack accidentals that are close together (within 6 staff positions)
/// 4. Stagger accidentals to avoid collisions
/// </summary>
public sealed class AccidentalPlacement
{
    private readonly AccidentalPlacementParameters _params;
    
    public AccidentalPlacement(AccidentalPlacementParameters? parameters = null)
    {
        _params = parameters ?? AccidentalPlacementParameters.Default;
    }
    
    /// <summary>
    /// Calculates accidental positions for a chord.
    /// </summary>
    public ImmutableArray<AccidentalLayout> CalculatePositions(IReadOnlyList<ChordNoteInfo> notes)
    {
        // Filter notes with accidentals
        var accidentals = notes
            .Where(n => !string.IsNullOrEmpty(n.Accidental))
            .Select(n => (n.StaffPosition, Accidental: n.Accidental!))
            .OrderByDescending(a => a.StaffPosition) // Top to bottom
            .ToList();
        
        if (accidentals.Count == 0)
            return ImmutableArray<AccidentalLayout>.Empty;
        
        if (accidentals.Count == 1)
        {
            // Single accidental: simple placement
            var (pos, acc) = accidentals[0];
            double width = GetAccidentalWidth(acc);
            return ImmutableArray.Create(new AccidentalLayout(pos, acc, -(width + _params.RightPadding)));
        }
        
        // Multiple accidentals: need to avoid collisions
        return CalculateMultipleAccidentals(accidentals);
    }
    
    /// <summary>
    /// Calculates positions for a single note's accidental.
    /// </summary>
    public AccidentalLayout? CalculateSinglePosition(NoteItem note)
    {
        if (string.IsNullOrEmpty(note.Accidental))
            return null;
        
        double width = GetAccidentalWidth(note.Accidental);
        return new AccidentalLayout(
            note.StaffPosition,
            note.Accidental,
            -(width + _params.RightPadding));
    }
    
    private ImmutableArray<AccidentalLayout> CalculateMultipleAccidentals(
        List<(int StaffPosition, string Accidental)> accidentals)
    {
        var layouts = new List<AccidentalLayout>();
        var columns = new List<List<(int StaffPosition, string Accidental)>>();
        
        // Greedy column assignment:
        // Place each accidental in the rightmost column where it doesn't collide
        foreach (var acc in accidentals)
        {
            bool placed = false;
            
            for (int col = 0; col < columns.Count; col++)
            {
                if (CanPlaceInColumn(acc, columns[col]))
                {
                    columns[col].Add(acc);
                    placed = true;
                    break;
                }
            }
            
            if (!placed)
            {
                // Need new column
                columns.Add(new List<(int, string)> { acc });
            }
        }
        
        // Calculate X offsets for each column
        double currentX = -_params.RightPadding;
        
        for (int col = 0; col < columns.Count; col++)
        {
            // Find widest accidental in this column
            double maxWidth = columns[col].Max(a => GetAccidentalWidth(a.Accidental));
            currentX -= maxWidth;
            
            foreach (var acc in columns[col])
            {
                // Center accidental in column
                double accWidth = GetAccidentalWidth(acc.Accidental);
                double offset = currentX + (maxWidth - accWidth) / 2;
                layouts.Add(new AccidentalLayout(acc.StaffPosition, acc.Accidental, offset));
            }
            
            currentX -= _params.Padding;
        }
        
        return layouts.ToImmutableArray();
    }
    
    /// <summary>
    /// Checks if an accidental can be placed in a column without collision.
    /// Accidentals collide if they are within 6 staff positions of each other.
    /// </summary>
    private bool CanPlaceInColumn(
        (int StaffPosition, string Accidental) acc,
        List<(int StaffPosition, string Accidental)> column)
    {
        foreach (var existing in column)
        {
            int distance = Math.Abs(acc.StaffPosition - existing.StaffPosition);
            if (distance < _params.StaggerThreshold)
                return false;
        }
        return true;
    }
    
    private static double GetAccidentalWidth(string accidental)
    {
        var bbox = accidental switch
        {
            "sharp" => GlyphMetrics.AccidentalSharp,
            "flat" => GlyphMetrics.AccidentalFlat,
            "natural" => GlyphMetrics.AccidentalNatural,
            "double-sharp" or "x" => GlyphMetrics.AccidentalDoubleSharp,
            "double-flat" => GlyphMetrics.AccidentalDoubleFlat,
            _ => GlyphMetrics.AccidentalSharp
        };
        
        // Width is already in staff spaces from GlyphMetrics
        return bbox.Width;
    }
}
