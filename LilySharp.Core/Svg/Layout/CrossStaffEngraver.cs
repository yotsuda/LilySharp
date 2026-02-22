using System.Collections.Immutable;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Layout information for a cross-staff note that needs to be rendered
/// on a different staff than its voice.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/beam.cc:1451-1459 - cross-staff detection
/// LILYPOND-REF: lily/stem.cc:1168-1179 - cross-staff stem handling
/// </remarks>
public readonly record struct CrossStaffLayout(
    /// <summary>Measure index containing the cross-staff note.</summary>
    int MeasureIndex,

    /// <summary>Item index within the measure.</summary>
    int ItemIndex,

    /// <summary>Source staff index (where the voice belongs).</summary>
    int SourceStaffIndex,

    /// <summary>Target staff index (where the note should render).</summary>
    int TargetStaffIndex
);

/// <summary>
/// Resolves cross-staff note assignments for grand staff rendering.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/beam.cc:1451-1459 - Beam::is_cross_staff
/// LILYPOND-REF: lily/stem.cc:1168-1179 - Stem::is_cross_staff
/// LILYPOND-REF: scm/music-functions.scm:2372-2458 - Span_stem_engraver
///
/// In a grand staff context, @cross annotation moves a note to the "other" staff:
/// - Staff 0 (treble) notes with @cross → rendered on staff 1 (bass)
/// - Staff 1 (bass) notes with @cross → rendered on staff 0 (treble)
/// </remarks>
public static class CrossStaffEngraver
{
    /// <summary>
    /// Resolves cross-staff items into layout information with actual staff indices.
    /// </summary>
    /// <param name="crossStaffItems">Raw cross-staff annotations from collector.</param>
    /// <param name="voiceStaffIndex">The staff index this voice is assigned to.</param>
    /// <param name="staffCount">Total number of staves in the staff group.</param>
    /// <returns>Resolved cross-staff layout items with source and target staff indices.</returns>
    public static ImmutableArray<CrossStaffLayout> Calculate(
        ImmutableArray<CrossStaffItem> crossStaffItems,
        int voiceStaffIndex,
        int staffCount)
    {
        if (crossStaffItems.IsDefaultOrEmpty || staffCount < 2)
            return ImmutableArray<CrossStaffLayout>.Empty;

        var builder = ImmutableArray.CreateBuilder<CrossStaffLayout>(crossStaffItems.Length);

        foreach (var item in crossStaffItems)
        {
            // In a 2-staff grand staff, @cross flips to the other staff
            // In N-staff groups, @cross moves to the next staff (wrapping)
            int targetStaff = staffCount == 2
                ? (voiceStaffIndex == 0 ? 1 : 0)
                : (voiceStaffIndex + 1) % staffCount;

            builder.Add(new CrossStaffLayout(
                item.MeasureIndex,
                item.ItemIndex,
                voiceStaffIndex,
                targetStaff));
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Builds a lookup set for quick cross-staff detection during beam processing.
    /// </summary>
    public static HashSet<(int MeasureIndex, int ItemIndex)> BuildCrossStaffLookup(
        ImmutableArray<CrossStaffLayout> layouts)
    {
        var set = new HashSet<(int, int)>(layouts.Length);
        foreach (var layout in layouts)
        {
            set.Add((layout.MeasureIndex, layout.ItemIndex));
        }
        return set;
    }

    /// <summary>
    /// Gets the target staff index for a specific note, or -1 if not cross-staff.
    /// </summary>
    public static int GetTargetStaffIndex(
        ImmutableArray<CrossStaffLayout> layouts,
        int measureIndex,
        int itemIndex)
    {
        foreach (var layout in layouts)
        {
            if (layout.MeasureIndex == measureIndex && layout.ItemIndex == itemIndex)
                return layout.TargetStaffIndex;
        }
        return -1;
    }
}
