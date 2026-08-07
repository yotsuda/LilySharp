// Lily# - Music notation compiler
// Copyright (C) 2025-2026 Yoshifumi Tsuda
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System.Collections.Immutable;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Layout information for a single music item within a measure.
/// All coordinates are in staff spaces.
/// </summary>
public readonly record struct ItemLayout(
    int ItemIndex,
    double X,       // X offset from measure start (staff spaces)
    double Width    // Allocated width (staff spaces)
);

/// <summary>
/// Layout information for a timing column within a measure.
/// A column represents a vertical slice of time where items may be placed.
/// All coordinates are in staff spaces.
/// </summary>
public readonly record struct ColumnLayout(
    Fraction Timing,  // Start time of this column (from measure start)
    double X,         // X offset from measure start (staff spaces)
    double Width      // Allocated width (staff spaces)
);

/// <summary>
/// Layout information for a single measure.
/// All coordinates are in staff spaces.
/// </summary>
internal sealed record MeasureLayout
{
    public int MeasureIndex { get; }
    public double X { get; }                              // X position of measure start (staff spaces)
    public double Width { get; }                          // Total measure width (staff spaces)
    public ImmutableArray<ItemLayout> Items { get; }      // Layout of items within the measure (for single-staff)
    public ImmutableArray<ColumnLayout> Columns { get; }  // Timing-based columns (for multi-staff)

    /// <summary>
    /// Creates a MeasureLayout with item-based positioning (for single-staff scores).
    /// </summary>
    public MeasureLayout(int measureIndex, double x, double width, ImmutableArray<ItemLayout> items)
    {
        MeasureIndex = measureIndex;
        X = x;
        Width = width;
        Items = items;
        Columns = ImmutableArray<ColumnLayout>.Empty;
    }

    /// <summary>
    /// Creates a MeasureLayout with both item-based and column-based positioning (for multi-staff scores).
    /// </summary>
    public MeasureLayout(int measureIndex, double x, double width, ImmutableArray<ItemLayout> items, ImmutableArray<ColumnLayout> columns)
    {
        MeasureIndex = measureIndex;
        X = x;
        Width = width;
        Items = items;
        Columns = columns;
    }

    /// <summary>
    /// Gets the X coordinate for a given timing within this measure.
    /// Uses column information if available, otherwise interpolates.
    /// </summary>
    public double GetXForTiming(Fraction timing)
    {
        if (Columns.IsDefaultOrEmpty || Columns.Length == 0)
        {
            // No column info - return start of measure as fallback
            return 0;
        }

        // Find the column with exact or closest timing
        for (int i = 0; i < Columns.Length; i++)
        {
            if (Columns[i].Timing == timing)
                return Columns[i].X;

            if (Columns[i].Timing > timing)
            {
                // Timing is between previous column and this one - interpolate
                if (i == 0)
                    return Columns[0].X;

                var prev = Columns[i - 1];
                var curr = Columns[i];
                var ratio = (timing - prev.Timing).ToDouble() / (curr.Timing - prev.Timing).ToDouble();
                return prev.X + ratio * (curr.X - prev.X);
            }
        }

        // Timing is after all columns - return last column position
        return Columns[^1].X;
    }
}

/// <summary>
/// One of the springs LilyPond puts BETWEEN two spaceable staves of a system.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/page-layout-problem.cc:651-720 — <c>append_system</c> pushes these
/// into the SAME chain as the system-to-system springs, so a page solves the distance
/// between two staves of a system and the distance between two systems at one force.
///
/// <paramref name="MinimumDistance"/> is the refpoint-to-refpoint distance
/// <c>Align_interface</c> already decided, i.e. LilyPond's
/// <c>minimum_offsets_with_min_dist[i] - minimum_offsets_with_min_dist[last]</c>, and it
/// reaches the spring through <c>ensure_min_distance</c> (:699-704) rather than as the
/// distance itself.
///
/// ⚠️ IT IS <c>max(skyline + padding, minimum-distance)</c> AND basic-distance IS NOT IN IT.
/// That is the point of the floor: basic-distance is the spring's IDEAL, and folding it in
/// here would make the spring incompressible (the defect
/// <c>page.compressed.staff-staff-inside</c> caught in 2026-07-26). The floor is therefore
/// often BELOW the spec's basic-distance. At force 0 a spring is <c>max(floor, ideal)</c>,
/// which is exactly the distance the staves were laid out at — asserted for the case the
/// two could disagree by <c>StaffLayoutFrameTests.UnequalStavesInOneGroup_ArePlacedCentreToCentre</c>.
/// </remarks>
/// <param name="UpperStaffIndex">Global index of the upper staff of the pair.</param>
/// <param name="LowerStaffIndex">Global index of the lower staff of the pair.</param>
internal readonly record struct StaffSpring(
    int UpperStaffIndex,
    int LowerStaffIndex,
    VerticalSpacingSpec Spec,
    double MinimumDistance);

/// <summary>
/// Layout information for a single system (staff line).
/// All coordinates are in staff spaces.
/// </summary>
internal sealed record SystemLayout(
    int SystemIndex,
    double Y,                              // Y position of system top (staff spaces from page top)
    double Width,                          // System width (staff spaces) - staff lines extend to this
    double PrefixWidth,                    // Width of clef + key + time (staff spaces)
    ImmutableArray<MeasureLayout> Measures, // Measures in this system
    ImmutableArray<StaffGroupLayout> StaffGroups = default,  // Staff groups (optional, for multi-staff)
    double Indent = 0,                     // LILYPOND-REF: ly/paper-defaults-init.ly — indent for instrument names
    // The springs between this system's spaceable staves, in top-to-bottom order. Empty on
    // a one-staff system. LILYPOND-REF: lily/page-layout-problem.cc:651-720.
    ImmutableArray<StaffSpring> StaffSprings = default
)
{
    /// <summary>Whether this system has multiple staff groups.</summary>
    public bool HasMultipleStaffGroups => !StaffGroups.IsDefaultOrEmpty && StaffGroups.Length > 1;

    /// <summary>Whether this system has a grand staff.</summary>
    public bool HasGrandStaff => !StaffGroups.IsDefaultOrEmpty &&
        StaffGroups.Any(g => g.Type == StaffGroupType.GrandStaff);

    /// <summary>Total height of all staff groups (staff spaces).</summary>
    public double TotalStaffHeight => StaffGroups.IsDefaultOrEmpty ? 0 : StaffGroups.Sum(g => g.Height);
}

/// <summary>
/// Layout information for a single page.
/// All coordinates are in staff spaces.
/// </summary>
internal sealed record PageLayout(
    int PageIndex,
    double Width,                          // Page width (staff spaces)
    double Height,                         // Page height (staff spaces)
    double HeaderHeight,                   // Space for title/composer (staff spaces)
    ImmutableArray<SystemLayout> Systems   // Systems on this page
);

/// <summary>
/// Key for voice-specific layout offsets in multi-voice scores.
/// </summary>
public readonly record struct VoiceItemKey(int MeasureIndex, int VoiceId, int ItemIndex);

/// <summary>
/// Key for a rest's vertical shift — beam collision, or collision with another voice.
/// </summary>
/// <remarks>
/// ⚠️ THE VOICE AXIS IS LOAD-BEARING SINCE 2026-08-04 and was not there before. The beam
/// shift only ever fired on a staff's primary voice, so <c>(measure, item)</c> could stand
/// in for it; the rest/note collision is the opposite case by construction — the rest is in
/// one voice and the notes it avoids are in another — and item indices are per voice, so
/// two voices' rests at the same slot would have shared a single entry.
/// </remarks>
public readonly record struct RestShiftKey(int MeasureIndex, int VoiceIndex, int ItemIndex);

/// <summary>
/// Complete layout information for a score.
/// All coordinates are in staff spaces unless otherwise noted.
/// </summary>
internal sealed record ScoreLayout(
    ImmutableArray<PageLayout> Pages,
    ImmutableArray<SystemLayout> AllSystems,
    ImmutableArray<BeamLayout> BeamLayouts,
    ImmutableArray<TieLayout> TieLayouts,
    ImmutableArray<SlurLayout> SlurLayouts,
    ImmutableArray<DynamicLayout> DynamicLayouts,
    ImmutableArray<ArticulationLayout> ArticulationLayouts,
    ImmutableArray<GraceNoteLayout> GraceNoteLayouts,
    ImmutableArray<LyricLayout> LyricLayouts,
    ImmutableArray<LyricHyphenLayout> LyricHyphenLayouts,
    ImmutableArray<MusicMarkLayout> MusicMarkLayouts,
    ImmutableArray<CustomTextLayout> CustomTextLayouts,
    ImmutableArray<VoltaBracketLayout> VoltaBracketLayouts,
    ImmutableArray<TupletBracketLayout> TupletBracketLayouts,
    ImmutableArray<HairpinLayout> HairpinLayouts,
    ImmutableArray<TextSpannerLayout> TextSpannerLayouts,
    ImmutableArray<OttavaBracketLayout> OttavaBracketLayouts,
    ImmutableArray<GlissandoLayout> GlissandoLayouts,
    ImmutableArray<ArpeggioLayout> ArpeggioLayouts,
    ImmutableArray<PedalBracketLayout> PedalBracketLayouts,
    ImmutableArray<FiguredBassLayout> FiguredBassLayouts,
    ImmutableArray<ChordNameLayout> ChordNameLayouts,
    ImmutableArray<PercentRepeatLayout> PercentRepeatLayouts,
    ImmutableArray<CrossStaffLayout> CrossStaffLayouts,
    ImmutableArray<PartCombineLayout> PartCombineLayouts,
    ImmutableArray<TrillSpannerLayout> TrillSpannerLayouts,
    ImmutableArray<FingeringLayout> FingeringLayouts,
    ImmutableArray<TieVariantLayout> TieVariantLayouts,
    ImmutableArray<MultiMeasureRestLayout> MultiMeasureRestLayouts,
    ImmutableArray<LedgerLineSpan> LedgerLineSpans,
    ImmutableArray<BarNumberLayout> BarNumberLayouts,
    ImmutableArray<StanzaNumberLayout> StanzaNumberLayouts,
    ImmutableDictionary<VoiceItemKey, double> VoiceOffsets,
    ImmutableHashSet<VoiceItemKey> HeadWipeEntries,
    ImmutableDictionary<VoiceItemKey, DotAdjustment> DotAdjustments,
    ImmutableDictionary<RestShiftKey, double> RestShifts
)
{
    /// <summary>
    /// Grob property resolver for user \override / \revert.
    /// LILYPOND-REF: lily/grob-property.cc — property resolution chain
    /// </summary>
    public GrobPropertyResolver GrobPropertyResolver { get; init; } = GrobPropertyResolver.Empty;

    /// <summary>
    /// Layout options used to compute this layout. Renderers consult these for
    /// page margins, indents, and other paper-level parameters.
    /// </summary>
    public LayoutOptions Options { get; init; } = LayoutOptions.Default;

    /// <summary>Total number of pages.</summary>
    public int PageCount => Pages.Length;

    /// <summary>Total number of systems across all pages.</summary>
    public int SystemCount => AllSystems.Length;

    /// <summary>Width of the first page (staff spaces).</summary>
    public double Width => Pages.Length > 0 ? Pages[0].Width : 0;

    /// <summary>Height of the first page (staff spaces).</summary>
    public double Height => Pages.Length > 0 ? Pages[0].Height : 0;

    /// <summary>Header height of the first page (staff spaces).</summary>
    public double HeaderHeight => Pages.Length > 0 ? Pages[0].HeaderHeight : 0;

    /// <summary>All systems from all pages (pre-computed for performance).</summary>
    public ImmutableArray<SystemLayout> Systems => AllSystems;

    /// <summary>
    /// Gets the X offset for a specific voice item due to collision handling.
    /// Returns 0 if no offset is needed. Value is in staff spaces.
    /// </summary>
    public double GetVoiceOffset(int measureIndex, int voiceId, int itemIndex)
    {
        var key = new VoiceItemKey(measureIndex, voiceId, itemIndex);
        return VoiceOffsets.TryGetValue(key, out var offset) ? offset : 0;
    }

    /// <summary>
    /// The dot-column adjustments a two-voice collision imposes on this voice item
    /// (preferred dot direction and/or a minimum dot-column X); <c>default</c> when none.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/note-collision.cc:352-397 check_meshing_chords — the dot
    /// side supports and the dot direction rule, computed in
    /// NoteCollision.CalculateVoiceOffsets.
    /// </remarks>
    public DotAdjustment GetDotAdjustment(int measureIndex, int voiceId, int itemIndex)
    {
        var key = new VoiceItemKey(measureIndex, voiceId, itemIndex);
        return DotAdjustments.TryGetValue(key, out var dot) ? dot : default;
    }

    /// <summary>
    /// Gets the Y shift for a rest — from its beam, or from another voice's notes.
    /// Returns 0 if no shift is needed. Value is in staff positions (half spaces).
    /// </summary>
    public double GetRestShift(int measureIndex, int voiceIndex, int itemIndex)
    {
        var key = new RestShiftKey(measureIndex, voiceIndex, itemIndex);
        return RestShifts.TryGetValue(key, out var shift) ? shift : 0;
    }

    /// <summary>
    /// Checks if a notehead should be hidden due to head wipe (merge collision).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/note-collision.cc:254-318
    /// Head wipe hides the down-stem notehead when two voices merge at the same pitch.
    /// </remarks>
    public bool IsHeadWiped(int measureIndex, int voiceId, int itemIndex)
    {
        var key = new VoiceItemKey(measureIndex, voiceId, itemIndex);
        return HeadWipeEntries.Contains(key);
    }
}
