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
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Layout information for an ottava bracket.
/// All coordinates are in staff spaces.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/ottava-bracket.cc Ottava_bracket::print()
/// LILYPOND-REF: scm/define-grobs.scm:2708-2731 OttavaBracket grob defaults
/// </remarks>
public readonly record struct OttavaBracketLayout(
    // Start measure index (for system Y lookup).
    int StartMeasureIndex,
    // Start X position (staff spaces from score start).
    double StartX,
    // End X position.
    double EndX,
    // Y in the Y-up frame: staff-spaces ABOVE the system top, up-positive (frame B).
    // The renderer reflects it to device against the segment's system top
    // (sy + old-Y == sy − YUp).
    double YUp,
    // Display text (e.g., "8va", "8vb", "15ma", "15mb").
    string Text,
    // Whether the bracket is above the staff (true) or below (false).
    bool IsAbove,
    // Edge height for the end hook (in staff spaces).
    double EdgeHeight,
    // Dash period for the dashed line.
    double DashPeriod,
    // Dash fraction for the dashed line.
    double DashFraction,
    // Source position for click-to-source mapping.
    int SourcePosition,
    // F3/B: index of the originating ottava mark in score.MusicMarks,
    // so a reused layout re-derives data-pos from the live score. -1 = unresolved.
    int SourceIndex = -1,
    // The staff this bracket belongs to (0 = the first/only staff). It selects the
    // above-staff stacker's tracker — one per (system, staff), as LilyPond runs
    // Axis_group_interface::skyline_spacing per VerticalAxisGroup — so the bracket clears its OWN
    // staff's ink. (Until 2026-07-30 lower-staff brackets were held out of that pass
    // and fell back to the bare staff-padding floor, drawing through their noteheads:
    // ledger ottava.lower-staff.staff-to-line, −1.727520.)
    int StaffIndex = 0
);

/// <summary>
/// Calculates positions for ottava brackets.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/ottava-bracket.cc Ottava_bracket::print()
/// LILYPOND-REF: scm/define-grobs.scm:2708-2731 OttavaBracket grob defaults
///
/// OttavaBracket parameters from LilyPond:
/// - dash-fraction: 0.3
/// - edge-height: (0 . 0.8) — no hook at start, 0.8 staff spaces hook at end
/// - staff-padding: 2.0
/// - padding: 0.5
/// - shorten-pair: (-0.8 . -0.6)
/// - minimum-length: 0.3
/// - font-series: bold
/// - font-shape: italic
/// </remarks>
internal static class OttavaBracketEngraver
{
    /// <summary>
    /// Dash fraction for the dashed line.
    /// </summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:2710 (dash-fraction . 0.3)</remarks>
    private const double DashFraction = 0.3;

    /// <summary>
    /// Dash period (implicit from LilyPond's default line rendering).
    /// </summary>
    private const double DashPeriod = 2.0;

    /// <summary>
    /// Edge height at the end (right hook).
    /// </summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:2711 (edge-height . (0 . 0.8))</remarks>
    private const double EndEdgeHeight = 0.8;

    /// <summary>
    /// Staff padding — minimum distance from staff. One home: EngravingDefaults'
    /// outside-staff declaration table (the LILYPOND-REF lives beside the entry).
    /// </summary>
    private const double StaffPadding = EngravingDefaults.OttavaBracketStaffPadding;

    /// <summary>
    /// Y-up (above the system top) for 8va/15ma brackets — the staff-padding FLOOR,
    /// measured from the staff's INK edge, not its top line's centre.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/side-position-interface.cc:401-453 aligned_side — "Ensure
    /// 'staff-padding' from my refpoint to the staff" floors the refpoint (the bracket
    /// LINE) at <c>staff_extent[dir] + staff_padding</c>, and the staff's extent is its
    /// ink (2.05 about the middle). Measured six-digit (audit/lp-geometry
    /// ottava.floor.staff-to-line: 4.050000 = 2.05 + 2.0); the bare
    /// <c>StaffPadding</c> here read 4.000000, half a line thickness low.
    /// </remarks>
    private const double AboveStaffYUp =
        StaffPadding + EngravingDefaults.StaffLineThickness / 2.0;

    /// <summary>
    /// Y-up (below the system top, so negative) for 8vb/15mb brackets:
    /// StaffHeight (4) + the same ink-edge floor below the staff.
    /// </summary>
    /// <remarks>
    /// The same single claim as <see cref="AboveStaffYUp"/> — aligned_side reads
    /// <c>staff_extent[DOWN]</c>, which is the ink edge too. ⚠️ No ledger point measures
    /// the below-staff regime; this moves with the above side because splitting one
    /// claim's two halves is how HANDOFF 5.0's cap/baseline trap fires.
    /// </remarks>
    private const double BelowStaffYUp =
        -(4.0 + StaffPadding + EngravingDefaults.StaffLineThickness / 2.0);

    /// <summary>
    /// Left shorten (extends bracket slightly left).
    /// </summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:2717 (shorten-pair . (-0.8 . -0.6))</remarks>
    private const double LeftShorten = -0.8;

    /// <summary>
    /// Right shorten (extends bracket slightly right).
    /// </summary>
    private const double RightShorten = -0.6;

    /// <summary>
    /// Calculates layout for all ottava brackets.
    /// </summary>
    public static ImmutableArray<OttavaBracketLayout> Calculate(
        ImmutableArray<OttavaBracketItem> ottavaBrackets,
        ImmutableArray<SystemLayout> systems,
        ImmutableArray<MeasureLayout> measureLayouts,
        Func<int, int, double>? staffYAt = null)
    {
        if (ottavaBrackets.IsDefaultOrEmpty)
            return ImmutableArray<OttavaBracketLayout>.Empty;

        // LILYPOND-REF: lily/ottava-bracket.cc — brackets split at system breaks.
        var measureToSystemIdx = SpannerBreakSubstitution.BuildMeasureToSystemMap(systems);
        var layouts = ImmutableArray.CreateBuilder<OttavaBracketLayout>();

        foreach (var bracket in ottavaBrackets)
        {
            if (bracket.StartMeasureIndex >= measureLayouts.Length)
                continue;

            int endMeasureIdx = Math.Min(bracket.EndMeasureIndex, measureLayouts.Length - 1);

            bool isAbove = bracket.Type == OttavaType.Ottava8va ||
                           bracket.Type == OttavaType.Quindicesima15ma;
            // Offset to this bracket's OWN staff on a grand staff, so an 8va over
            // the lower staff sits above THAT staff, not the top one. Single-staff
            // (offset 0) is unchanged. Mirrors HairpinEngraver/TrillSpannerEngraver.
            double staffOffset = staffYAt?.Invoke(bracket.StartMeasureIndex, bracket.StaffIndex) ?? 0;
            // Y-up from the system top; staffOffset is a within-system downward offset,
            // so it SUBTRACTS.
            double yUp = (isAbove ? AboveStaffYUp : BelowStaffYUp) - staffOffset;

            string text = bracket.Type switch
            {
                OttavaType.Ottava8va => "8va",
                OttavaType.Ottava8vb => "8vb",
                OttavaType.Quindicesima15ma => "15ma",
                OttavaType.Quindicesima15mb => "15mb",
                _ => "8va"
            };

            foreach (var (segment, _) in SpannerBreakSubstitution.BrokenPieces(
                bracket.StartMeasureIndex, endMeasureIdx, systems, measureToSystemIdx))
            {
                if (segment.StartMeasureIndex >= measureLayouts.Length ||
                    segment.EndMeasureIndex >= measureLayouts.Length)
                    continue;

                var segStartMeasure = measureLayouts[segment.StartMeasureIndex];
                var segEndMeasure = measureLayouts[segment.EndMeasureIndex];

                double startX = segStartMeasure.X + LeftShorten;
                double endX = segEndMeasure.X + segEndMeasure.Width + RightShorten;

                // First segment shows the bare text ("8va"); continuation pieces use "(8va)".
                // Last segment carries the hook; non-last ends are open.
                string segText = segment.IsFirst ? text : $"({text})";
                double segEdgeHeight = segment.IsLast ? EndEdgeHeight : 0;

                layouts.Add(new OttavaBracketLayout(
                    StartMeasureIndex: segment.StartMeasureIndex,
                    StartX: startX,
                    EndX: endX,
                    YUp: yUp,
                    Text: segText,
                    IsAbove: isAbove,
                    EdgeHeight: segEdgeHeight,
                    DashPeriod: DashPeriod,
                    DashFraction: DashFraction,
                    SourcePosition: bracket.SourcePosition,
                    SourceIndex: bracket.SourceIndex,
                    StaffIndex: bracket.StaffIndex
                ));
            }
        }

        return layouts.ToImmutable();
    }

    /// <summary>
    /// Detects ottava bracket spans from music marks.
    /// An ottava starts at an 8va/8vb/15ma/15mb mark and ends at
    /// loco or the next ottava mark.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/ottava-engraver.cc process_music() and stop_translation_timestep()
    /// </remarks>
    public static ImmutableArray<OttavaBracketItem> DetectOttavaBrackets(
        ImmutableArray<MusicMarkItem> musicMarks)
    {
        var brackets = ImmutableArray.CreateBuilder<OttavaBracketItem>();

        // F3/B: keep each mark's ORIGINAL index in musicMarks (== score.MusicMarks) so the
        // bracket can re-derive its data-pos from the live score on reuse.
        var ottavaMarks = musicMarks
            .Select((m, i) => (Mark: m, Index: i))
            .Where(x => x.Mark.Type == MusicMarkType.OttavaUp ||
                        x.Mark.Type == MusicMarkType.OttavaDown ||
                        x.Mark.Type == MusicMarkType.QuindicesUp ||
                        x.Mark.Type == MusicMarkType.QuindicesDown ||
                        x.Mark.Type == MusicMarkType.Loco)
            .OrderBy(x => x.Mark.MeasureIndex)
            .ToList();

        if (ottavaMarks.Count == 0)
            return ImmutableArray<OttavaBracketItem>.Empty;

        // Walk through marks: each non-loco mark starts a bracket,
        // terminated by the next ottava/loco mark
        for (int i = 0; i < ottavaMarks.Count; i++)
        {
            var (mark, srcIndex) = ottavaMarks[i];

            // Skip loco marks (they only terminate, don't start)
            if (mark.Type == MusicMarkType.Loco)
                continue;

            OttavaType type = mark.Type switch
            {
                MusicMarkType.OttavaUp => OttavaType.Ottava8va,
                MusicMarkType.OttavaDown => OttavaType.Ottava8vb,
                MusicMarkType.QuindicesUp => OttavaType.Quindicesima15ma,
                MusicMarkType.QuindicesDown => OttavaType.Quindicesima15mb,
                _ => OttavaType.Ottava8va
            };

            // Find the end: next ottava/loco mark ON THE SAME STAFF. On a grand
            // staff each staff runs its own ottava, so a loco under the lower
            // staff must not terminate an 8va over the upper staff.
            // LILYPOND-REF: lily/ottava-engraver.cc — per-staff Ottava_spanner_engraver;
            //   bracket ends just before the terminating mark, so use measure - 1.
            MusicMarkItem? terminator = null;
            for (int j = i + 1; j < ottavaMarks.Count; j++)
                if (ottavaMarks[j].Mark.StaffIndex == mark.StaffIndex)
                {
                    terminator = ottavaMarks[j].Mark;
                    break;
                }

            int endMeasure;
            if (terminator != null)
            {
                // Bracket covers up to the measure before the terminator
                endMeasure = terminator.MeasureIndex - 1;
                if (endMeasure < mark.MeasureIndex)
                    endMeasure = mark.MeasureIndex; // at minimum, cover the start measure
            }
            else
            {
                // No end found — extend to one measure after the start
                endMeasure = mark.MeasureIndex + 1;
            }

            if (endMeasure >= mark.MeasureIndex)
            {
                brackets.Add(new OttavaBracketItem(
                    Type: type,
                    StartMeasureIndex: mark.MeasureIndex,
                    EndMeasureIndex: endMeasure,
                    SourcePosition: mark.SourcePosition,
                    SourceIndex: srcIndex,
                    StaffIndex: mark.StaffIndex
                ));
            }
        }

        return brackets.ToImmutable();
    }
}
