using System.Collections.Immutable;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Layout for a single arpeggio marking.
/// All coordinates are in staff spaces.
/// </summary>
public readonly record struct ArpeggioLayout(
    double X,
    double TopY,
    double BottomY,
    int SourcePosition);

/// <summary>
/// Calculates arpeggio layouts from detected arpeggio items.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/arpeggio.cc, scm/define-grobs.scm:201-224
/// Parameters: padding=0.5, direction=LEFT, protrusion=0.4
/// The arpeggio is a wavy vertical line placed to the left of a chord.
/// </remarks>
public static class ArpeggioEngraver
{
    // LILYPOND-REF: scm/define-grobs.scm:209 (padding . 0.5)
    private const double Padding = 0.5;

    /// <summary>
    /// Calculates layout positions for all arpeggio items.
    /// </summary>
    public static ImmutableArray<ArpeggioLayout> Calculate(
        ImmutableArray<ArpeggioItem> arpeggios,
        ImmutableArray<SystemLayout> systems,
        ImmutableArray<MeasureLayout> measureLayouts,
        double staffHeight)
    {
        if (arpeggios.IsDefaultOrEmpty || arpeggios.Length == 0)
            return ImmutableArray<ArpeggioLayout>.Empty;

        var measureMap = new Dictionary<int, (SystemLayout system, MeasureLayout measure)>();
        foreach (var system in systems)
        {
            foreach (var ml in system.Measures)
            {
                measureMap[ml.MeasureIndex] = (system, ml);
            }
        }

        var layouts = new List<ArpeggioLayout>();

        foreach (var arp in arpeggios)
        {
            if (!measureMap.TryGetValue(arp.MeasureIndex, out var info))
                continue;

            var (system, measure) = info;

            // Get X position of the chord item, then place arpeggio to the left
            // LILYPOND-REF: scm/define-grobs.scm:206 (direction . ,LEFT)
            double itemX = measure.X;
            if (arp.ItemIndex < measure.Items.Length)
                itemX += measure.Items[arp.ItemIndex].X;

            double arpeggioX = itemX - Padding;

            // Calculate Y positions from staff positions
            double staffMiddleY = system.Y + staffHeight / 2;
            double topY = staffMiddleY - arp.MaxStaffPosition / 2.0;
            double bottomY = staffMiddleY - arp.MinStaffPosition / 2.0;

            // Extend slightly beyond the note range for visual clarity
            topY -= 0.3;
            bottomY += 0.3;

            layouts.Add(new ArpeggioLayout(
                X: arpeggioX,
                TopY: topY,
                BottomY: bottomY,
                SourcePosition: arp.SourcePosition));
        }

        return layouts.ToImmutableArray();
    }
}
