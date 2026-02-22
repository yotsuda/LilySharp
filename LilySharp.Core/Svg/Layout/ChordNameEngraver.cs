using System.Collections.Immutable;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Layout result for a single chord name.
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/define-grobs.scm - ChordName grob properties
/// </remarks>
public readonly record struct ChordNameLayout(
    int MeasureIndex,
    double X,                // X position (staff spaces from page left)
    double Y,                // Y position (staff spaces from page top, above staff)
    string ChordText,        // Display text (e.g., "Cm7", "B♭7")
    int SourcePosition
);

/// <summary>
/// Calculates layout positions for chord name symbols.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/chord-name.cc - ChordName::after_line_breaking
/// LILYPOND-REF: scm/define-grobs.scm - ChordName: font-family=sans, font-size=1.5
/// LILYPOND-REF: ly/engraver-init.ly:571-592 - ChordNames context
///
/// Chord names are positioned above the staff with padding.
/// In LilyPond, ChordNames is a separate context above the staff.
/// </remarks>
public static class ChordNameEngraver
{
    /// <summary>Padding above the top of the staff (staff spaces).</summary>
    /// <remarks>
    /// LILYPOND-REF: ly/engraver-init.ly:588 - nonstaff-relatedstaff-spacing.padding = 0.5
    /// </remarks>
    private const double StaffPadding = 2.0;

    /// <summary>
    /// Calculates chord name layouts from collected items.
    /// </summary>
    public static ImmutableArray<ChordNameLayout> Calculate(
        ImmutableArray<ChordNameItem> chordNames,
        ImmutableArray<SystemLayout> systems,
        ImmutableArray<MeasureLayout> measureLayouts)
    {
        if (chordNames.IsDefaultOrEmpty || systems.IsDefaultOrEmpty || measureLayouts.IsDefaultOrEmpty)
            return ImmutableArray<ChordNameLayout>.Empty;

        var results = ImmutableArray.CreateBuilder<ChordNameLayout>(chordNames.Length);

        foreach (var chord in chordNames)
        {
            if (chord.MeasureIndex >= measureLayouts.Length)
                continue;

            var ml = measureLayouts[chord.MeasureIndex];

            // Find item X position
            double itemX = 0;
            if (chord.ItemIndex < ml.Items.Length)
                itemX = ml.Items[chord.ItemIndex].X;

            double x = ml.X + itemX;

            // Y position: above the staff (negative = upward)
            double y = -StaffPadding;

            results.Add(new ChordNameLayout(
                chord.MeasureIndex, x, y, chord.ChordText, chord.SourcePosition));
        }

        return results.ToImmutable();
    }
}
