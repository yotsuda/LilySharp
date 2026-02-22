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
    double XOffset,
    /// <summary>Whether this is a courtesy (cautionary) accidental.</summary>
    bool IsCourtesy = false
);

/// <summary>
/// Parameters for accidental placement. All dimensions in staff spaces.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/accidental-placement.cc:393-439 position_apes
/// LILYPOND-REF: scm/define-grobs.scm:84 AccidentalPlacement
/// </remarks>
public sealed record AccidentalPlacementParameters
{
    public static AccidentalPlacementParameters Default { get; } = new();

    /// <summary>Padding between accidental columns in staff spaces.</summary>
    /// <remarks>LILYPOND-REF: accidental-placement.cc:398,505 (hardcoded 0.2)</remarks>
    public double Padding { get; init; } = 0.2;

    /// <summary>Extra padding from note head in staff spaces.</summary>
    /// <remarks>LILYPOND-REF: define-grobs.scm:84 AccidentalPlacement.right-padding</remarks>
    public double RightPadding { get; init; } = 0.15;

    /// <summary>Y-axis padding for overlap detection in staff spaces.</summary>
    /// <remarks>LILYPOND-REF: accidental-placement.cc:413 horizon_padding</remarks>
    public double HorizonPadding { get; init; } = 0.1;
}

/// <summary>
/// Calculates accidental positions for chords following LilyPond's algorithm.
/// Uses bounding-box collision detection with glyph Y-extents (equivalent to
/// skylines for rectangular shapes).
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/accidental-placement.cc
///
/// Algorithm:
/// 1. Build entries with glyph Y-extents at each note's staff position
/// 2. Sort by alteration priority (naturals rightmost, flats leftmost)
///    LILYPOND-REF: accidental-placement.cc:164-184 acc_less
/// 3. Position right-to-left: each accidental placed as close to notes
///    as possible without Y-overlapping previously placed accidentals
///    LILYPOND-REF: accidental-placement.cc:393-439 position_apes
/// </remarks>
public sealed class AccidentalPlacement
{
    private readonly AccidentalPlacementParameters _params;

    public AccidentalPlacement(AccidentalPlacementParameters? parameters = null)
    {
        _params = parameters ?? AccidentalPlacementParameters.Default;
    }

    /// <summary>Internal entry for positioning calculations.</summary>
    private readonly record struct PlacementEntry(
        int StaffPosition,
        string Accidental,
        double YBottom,     // Lower bound in staff spaces
        double YTop,        // Upper bound in staff spaces
        double Width,       // Glyph width in staff spaces
        int Priority        // Sorting priority: lower = rightmost
    );

    /// <summary>Tracks a placed accidental for collision detection.</summary>
    private readonly record struct PlacedBox(
        double LeftEdge,    // X position of left edge
        double YBottom,
        double YTop
    );

    /// <summary>
    /// Calculates accidental positions for a chord.
    /// </summary>
    public ImmutableArray<AccidentalLayout> CalculatePositions(IReadOnlyList<ChordNoteInfo> notes)
    {
        var accidentals = notes
            .Where(n => !string.IsNullOrEmpty(n.Accidental))
            .ToList();

        if (accidentals.Count == 0)
            return ImmutableArray<AccidentalLayout>.Empty;

        if (accidentals.Count == 1)
        {
            var n = accidentals[0];
            double width = GetAccidentalWidth(n.Accidental!);
            return ImmutableArray.Create(new AccidentalLayout(
                n.StaffPosition, n.Accidental!, -(width + _params.RightPadding)));
        }

        return CalculateMultipleAccidentals(accidentals);
    }

    /// <summary>
    /// Calculates position for a single note's accidental.
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
        List<ChordNoteInfo> accidentals)
    {
        // Build entries with glyph Y-extents
        var entries = new List<PlacementEntry>(accidentals.Count);
        foreach (var n in accidentals)
        {
            var bbox = GetAccidentalBBox(n.Accidental!);
            // Staff position is in half-spaces; convert to staff spaces
            double yCenterSS = n.StaffPosition / 2.0;
            // BBox: Bottom is negative (below baseline), Top is positive (above)
            double yBottom = yCenterSS + bbox.Bottom;
            double yTop = yCenterSS + bbox.Top;
            int priority = GetAlterationPriority(n.Accidental!);
            entries.Add(new PlacementEntry(
                n.StaffPosition, n.Accidental!, yBottom, yTop, bbox.Width, priority));
        }

        // Sort by alteration priority: naturals rightmost, flats leftmost
        // LILYPOND-REF: accidental-placement.cc:164-184 acc_less
        entries.Sort((a, b) => a.Priority.CompareTo(b.Priority));

        // Position right-to-left with collision avoidance
        // LILYPOND-REF: accidental-placement.cc:393-439 position_apes
        var placed = new List<PlacedBox>();
        var layouts = new List<AccidentalLayout>(entries.Count);

        foreach (var entry in entries)
        {
            // Start with rightmost possible position
            double xRight = -_params.RightPadding;

            // Check collision with each placed accidental
            foreach (var box in placed)
            {
                // Check Y overlap with horizon padding
                // LILYPOND-REF: accidental-placement.cc:413
                if (entry.YBottom - _params.HorizonPadding < box.YTop &&
                    box.YBottom - _params.HorizonPadding < entry.YTop)
                {
                    // Y extents overlap: must be further left
                    double maxRight = box.LeftEdge - _params.Padding;
                    if (maxRight < xRight)
                        xRight = maxRight;
                }
            }

            double xLeft = xRight - entry.Width;
            placed.Add(new PlacedBox(xLeft, entry.YBottom, entry.YTop));
            layouts.Add(new AccidentalLayout(entry.StaffPosition, entry.Accidental, xLeft));
        }

        return layouts.ToImmutableArray();
    }

    /// <summary>
    /// Gets alteration sorting priority.
    /// Lower values are placed first (rightmost, closest to notes).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: accidental-placement.cc:164-184 acc_less
    /// Naturals are safest (rightmost). Sharps next, then flats (leftmost).
    /// </remarks>
    private static int GetAlterationPriority(string accidental) => accidental switch
    {
        "natural" => 0,
        "sharp" => 1,
        "doubleSharp" => 2,
        "flat" => 3,
        "doubleFlat" => 4,
        _ => 2
    };

    private static double GetAccidentalWidth(string accidental) =>
        GetAccidentalBBox(accidental).Width;

    private static GlyphMetrics.BBox GetAccidentalBBox(string accidental) => accidental switch
    {
        "sharp" => GlyphMetrics.AccidentalSharp,
        "flat" => GlyphMetrics.AccidentalFlat,
        "natural" => GlyphMetrics.AccidentalNatural,
        "doubleSharp" => GlyphMetrics.AccidentalDoubleSharp,
        "doubleFlat" => GlyphMetrics.AccidentalDoubleFlat,
        _ => GlyphMetrics.AccidentalSharp
    };
}
