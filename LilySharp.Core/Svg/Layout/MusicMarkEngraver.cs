using System.Collections.Immutable;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Layout information for a music mark (segno, coda, fine, D.S., D.C., etc.).
/// All coordinates are in staff spaces.
/// </summary>
/// <remarks>
/// LILYPOND-REF: mark-engraver.cc:36-89 Mark_engraver class
/// LILYPOND-REF: define-grobs.scm:3650-3710 RehearsalMark, SegnoMark, CodaMark grobs
/// </remarks>
public readonly record struct MusicMarkLayout(
    int MeasureIndex,       // Measure containing this mark
    double X,               // Absolute X position (staff spaces from score start)
    double Y,               // Y position (staff spaces from staff top, positive = down)
    MusicMarkType MarkType, // Type of mark
    string Text,            // Display text or glyph
    bool IsSymbol,          // True if should use symbol glyph, false for text
    int SourcePosition      // For click-to-source mapping
);

/// <summary>
/// Calculates positions for music marks.
/// Implements LilyPond's mark positioning algorithm.
/// </summary>
/// <remarks>
/// LILYPOND-REF: mark-engraver.cc:46-89 Mark creation
/// LILYPOND-REF: side-position-interface.cc:92-111 axis_aligned_side_helper
///
/// LilyPond places marks:
/// - Above staff (direction = UP) for most marks
/// - Below staff (direction = DOWN) for expression marks (rit., accel., etc.)
/// - At beginning of measure for segno/coda
/// - At end of measure for fine/D.S./D.C.
/// </remarks>
public static class MusicMarkEngraver
{
    // LILYPOND-REF: define-grobs.scm:3660 direction = UP
    private const int DirectionUp = -1;  // UP = -1 (negative Y = up in our coordinate system)
    private const int DirectionDown = 1; // DOWN = 1

    // LILYPOND-REF: define-grobs.scm:3665 padding = 0.5
    private const double Padding = 0.5;

    // LILYPOND-REF: define-grobs.scm:3670 outside-staff-priority = 1500 (high, above dynamics)
    private const double StaffTop = 0.0;

    // Y offset above staff for marks
    private const double AboveStaffOffset = -2.0;

    // Y offset below staff for expression marks
    private const double BelowStaffOffset = 5.5;

    /// <summary>
    /// Calculates layout for all music marks in a score.
    /// </summary>
    public static ImmutableArray<MusicMarkLayout> Calculate(
        Score score,
        ImmutableArray<MusicMarkItem> musicMarks,
        ImmutableArray<SystemLayout> systems,
        ImmutableArray<MeasureLayout> measureLayouts)
    {
        if (musicMarks.IsDefaultOrEmpty)
            return ImmutableArray<MusicMarkLayout>.Empty;

        var layouts = ImmutableArray.CreateBuilder<MusicMarkLayout>(musicMarks.Length);

        foreach (var mark in musicMarks)
        {
            // Find the measure layout
            if (mark.MeasureIndex >= measureLayouts.Length)
                continue;

            var measureLayout = measureLayouts[mark.MeasureIndex];

            // Calculate X position based on mark type
            // LILYPOND-REF: mark-engraver.cc:75-80 break-align-symbol
            double x = CalculateXPosition(mark, measureLayout);

            // Calculate Y position based on mark type
            double y = CalculateYPosition(mark);

            layouts.Add(new MusicMarkLayout(
                mark.MeasureIndex,
                x,
                y,
                mark.Type,
                mark.Text,
                mark.IsSymbol,
                mark.SourcePosition
            ));
        }

        return layouts.ToImmutable();
    }

    /// <summary>
    /// Calculates X position for a mark.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: mark-engraver.cc:75-80 break-align-symbol
    /// - Beginning marks (segno, coda): align to start of measure
    /// - End marks (fine, D.S., D.C.): align to end of measure
    /// </remarks>
    private static double CalculateXPosition(MusicMarkItem mark, MeasureLayout measureLayout)
    {
        return mark.Position switch
        {
            MusicMarkPosition.Beginning => measureLayout.X + 0.5, // Small offset from barline
            MusicMarkPosition.End => measureLayout.X + measureLayout.Width - 0.5, // Before end barline
            _ => measureLayout.X + measureLayout.Width / 2 // Center (fallback)
        };
    }

    /// <summary>
    /// Calculates Y position for a mark.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: define-grobs.scm:3660-3675 direction and padding
    /// - Above staff for navigation marks (segno, coda, fine, D.S., D.C.)
    /// - Below staff for expression marks (rit., accel., cresc., dim.)
    /// </remarks>
    private static double CalculateYPosition(MusicMarkItem mark)
    {
        return mark.Vertical switch
        {
            MusicMarkVertical.Above => AboveStaffOffset - Padding,
            MusicMarkVertical.Below => BelowStaffOffset + Padding,
            _ => AboveStaffOffset - Padding
        };
    }
}