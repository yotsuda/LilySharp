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
/// Layout for a single glissando line.
/// All coordinates are in staff spaces.
/// </summary>
public readonly record struct GlissandoLayout(
    double StartX,
    // Y of the line's start/end in the LilyPond-native Y-up frame: staff-spaces
    // ABOVE this glissando's staff middle line, up-positive (frame B). The renderer
    // reflects each back to device (middle − Y-up) against the staff middle
    // it resolves from StaffIndex/MeasureIndex.
    double StartYUp,
    double EndX,
    double EndYUp,
    GlissandoStyle Style,
    int SourcePosition,
    // F3/B: locator of the START note this glissando hangs on, so a reused (cached)
    // layout re-derives its data-pos from the live score (SharedRenderer.ResolveDataPos).
    // -1 = unresolved (direct unit-test construction / no note resolution).
    int StaffIndex = -1,
    int MeasureIndex = -1,
    int ItemIndex = -1,
    // Voice the start note lives in (0 = primary), so the locator resolves against
    // the RIGHT voice on a polyphonic staff — a second voice's glissando must not
    // re-derive its data-pos from the primary voice's note at the same index.
    int VoiceIndex = 0);

/// <summary>
/// Calculates glissando layouts from detected glissando items.
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/scheme-engravers.scm, scm/define-grobs.scm:1692-1719
/// Parameters: style=line, gap=0.5, padding=0.5, zigzag-width=0.75
/// </remarks>
internal static class GlissandoEngraver
{
    // The 0.5 each bound is pulled INWARD ALONG THE LINE — the bound-details
    // padding, the only shortening print applies (the grob's separate `gap` 0.5
    // property is not read by the line-spanner print at all).
    // LILYPOND-REF: scm/define-grobs.scm:1695-1702 Glissando bound-details — (padding . 0.5) on both left and right
    // LILYPOND-REF: lily/line-spanner.cc:559-562 Line_spanner::print — gaps[d] reads the bound's "padding"
    // LILYPOND-REF: lily/line-spanner.cc:599 — span_points[d] += -d * gaps[d] * magstep * dz.direction ()
    private const double Padding = 0.5;

    // The same single-ape accidental column the renderer and the beam quanter place
    // with, so the ink the glissando stops before is the ink drawn.
    private static readonly AccidentalPlacement AccidentalColumn = new();

    /// <summary>
    /// Calculates layout positions for all glissando items, splitting cross-system
    /// glissandos into broken pieces (one per system).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spanner.cc:36-144 — Spanner::do_break_processing
    /// LILYPOND-REF: scm/scheme-engravers.scm — Glissando_engraver
    /// </remarks>
    public static ImmutableArray<GlissandoLayout> Calculate(
        ImmutableArray<GlissandoItem> glissandos,
        ImmutableArray<SystemLayout> systems,
        int staffIndex = -1,
        ImmutableArray<Measure> measures = default)
    {
        if (glissandos.IsDefaultOrEmpty || glissandos.Length == 0)
            return ImmutableArray<GlissandoLayout>.Empty;

        var measureMap = LayoutUtilities.BuildMeasureMap(systems);
        var measureToSystemIdx = SpannerBreakSubstitution.BuildMeasureToSystemMap(systems);
        var layouts = new List<GlissandoLayout>();

        foreach (var gliss in glissandos)
        {
            if (!measureMap.TryGetValue(gliss.StartMeasureIndex, out var startInfo))
                continue;
            if (!measureMap.TryGetValue(gliss.EndMeasureIndex, out var endInfo))
                continue;

            var (_, startMeasure) = startInfo;
            var (_, endMeasure) = endInfo;

            // The X ANCHORS are the bound heads' own ink edges, not the columns:
            // left = the start head's RIGHT ink edge (attach-dir RIGHT), right = the
            // target head's LEFT ink edge (attach-dir LEFT) — or, when the target
            // column prints accidentals, the ACCIDENTAL ink's left edge, which is
            // the whole of "glissandi stop before hitting accidentals". Until
            // 2026-08-07 both anchors were the bare column X ± a flat 0.5, so a
            // line missed the head by a head-width on the left and ran into
            // accidentals on the right (regression glissando-accidental.ly).
            // LILYPOND-REF: scm/define-grobs.scm:1695-1702 Glissando bound-details — right attach-dir LEFT + end-on-accidental, left attach-dir RIGHT + start-at-dot
            // LILYPOND-REF: lily/line-spanner.cc:171-175 calc_bound_info — x = robust_relative_extent (bound_grob).linear_combination (attach)
            // LILYPOND-REF: lily/line-spanner.cc:177-202 calc_bound_info — end-on-accidental rereads the x from the column's AccidentalPlacement extent
            var startItem = ItemAt(measures, gliss.StartMeasureIndex, gliss.StartItemIndex);
            var endItem = ItemAt(measures, gliss.EndMeasureIndex, gliss.EndItemIndex);
            double realStartX = startMeasure.X + LayoutUtilities.GetItemXOffset(
                measures, gliss.StartMeasureIndex, gliss.StartItemIndex, startMeasure)
                + HeadInkEdge(startItem, gliss.StartMemberIndex, right: true);
            double realEndX = endMeasure.X + LayoutUtilities.GetItemXOffset(
                measures, gliss.EndMeasureIndex, gliss.EndItemIndex, endMeasure)
                + (LeftmostAccidentalInkX(endItem)
                   ?? HeadInkEdge(endItem, gliss.EndMemberIndex, right: false));

            foreach (var (segment, system) in SpannerBreakSubstitution.BrokenPieces(
                gliss.StartMeasureIndex, gliss.EndMeasureIndex, systems, measureToSystemIdx))
            {
                // Native Y-up (staff-spaces above this glissando's staff middle):
                // a staff position p sits p/2 spaces above the middle line. The former
                // ResolveStaffMiddleY round-trip (PositionToDevice, then ToUp at store)
                // cancelled exactly, so no staff-middle resolution is needed here.
                // LILYPOND-REF: lily/line-spanner.cc:416-421 calc_bound_info — y = the bound head's extent (Y_AXIS).center ()
                // ⚠️ ...which equals the staff position only because a DEFAULT head's ink
                //   is symmetric about it (s2 spans ±0.545). A styled head's is not —
                //   s2triangle spans −0.7828..0.6566, so LilyPond's anchor sits 0.0631
                //   BELOW the position. Unspelled here (no book glisses a shape head);
                //   ticketed 2026-08-07, session 109 audit.
                double startStaffY = gliss.StartStaffPosition * 0.5;
                double endStaffY = gliss.EndStaffPosition * 0.5;

                // Resolve segment-local X bounds against the system's measure layouts.
                // LILYPOND-REF: lily/spanner.cc:124-137 — bounds reattached to system edges.
                var (segStartX, segEndX) = SpannerBreakSubstitution.ReattachSpanX(
                    segment, system, realStartX, realEndX);

                // ⚠️ LILYSHARP-OWN (disclosed 2026-08-07, session 109 audit): Y at the
                //   broken edge "freezes" at the destination pitch — a visual cue that
                //   the slide continues on the adjacent system.
                //   departs from: lily/line-spanner.cc:247-406 calc_bound_info — LilyPond
                //     gives every broken piece ONE continuous slope, aligning the middles
                //     of each pair of staves ("the choice of Solomon") so the pieces would
                //     read as one line if the systems stood in a row.
                //   observed by: NOTHING — no corpus book renders a glissando ACROSS a
                //     break (glissando-chord-linebreak.ly breaks BETWEEN glissandi and is
                //     exact); when one does, its twin measures this branch.
                double segStartY = segment.IsFirst ? startStaffY : endStaffY;
                double segEndY = segment.IsLast ? endStaffY : startStaffY;
                if (segment.IsMiddle)
                {
                    double mid = (startStaffY + endStaffY) / 2.0;
                    segStartY = mid;
                    segEndY = mid;
                }

                // The padding: each bound pulled 0.5 inward ALONG the line (so its X
                // share shrinks as the line steepens), applied only at real-note
                // bounds (not at system-edge cuts). A line too short to pay both
                // paddings is not drawn at all.
                // LILYPOND-REF: lily/line-spanner.cc:599 span_points[d] += -d * gaps[d] * magstep * dz.direction()
                // LILYPOND-REF: lily/line-spanner.cc:591-594 Line_spanner::print — gaps beyond dz.length () print nothing
                double dx = segEndX - segStartX;
                double dy = segEndY - segStartY;
                double length = Math.Sqrt(dx * dx + dy * dy);
                double paddings = (segment.IsFirst ? Padding : 0) + (segment.IsLast ? Padding : 0);
                if (length <= paddings)
                    continue;
                double gapRatio = Padding / length;
                if (segment.IsFirst)
                {
                    segStartX += dx * gapRatio;
                    segStartY += dy * gapRatio;
                }
                if (segment.IsLast)
                {
                    segEndX -= dx * gapRatio;
                    segEndY -= dy * gapRatio;
                }

                // The segment/gap geometry above ran directly in the native Y-up frame
                // (line math is frame-invariant — dy just carries its Y-up sign), so
                // the Y-up store is a direct assignment; no reflection.
                layouts.Add(new GlissandoLayout(
                    StartX: segStartX,
                    StartYUp: segStartY,
                    EndX: segEndX,
                    EndYUp: segEndY,
                    Style: gliss.Style,
                    SourcePosition: gliss.SourcePosition,
                    StaffIndex: staffIndex,
                    // MeasureIndex is the START-note data-pos locator, shared by ALL
                    // broken segments. The draw resolves the staff middle from it, so a
                    // glissando broken across a SYSTEM would resolve later segments
                    // against the first system's staff middle. No such case is in
                    // coverage today; a proper fix needs a separate per-segment system
                    // reference (MeasureIndex can't move — it drives click-to-source).
                    MeasureIndex: gliss.StartMeasureIndex,
                    ItemIndex: gliss.StartItemIndex,
                    VoiceIndex: gliss.VoiceIndex));
            }
        }

        return layouts.ToImmutableArray();
    }

    /// <summary>The item a glissando bound resolves against, or null off the map.</summary>
    private static MusicItem? ItemAt(ImmutableArray<Measure> measures, int measureIdx, int itemIdx)
        => !measures.IsDefaultOrEmpty
           && measureIdx >= 0 && measureIdx < measures.Length
           && itemIdx >= 0 && itemIdx < measures[measureIdx].Items.Length
            ? measures[measureIdx].Items[itemIdx]
            : null;

    /// <summary>
    /// The bound head's ink edge in its column's frame: the head glyph's bbox edge
    /// (every head's ink starts at 0), shifted by the member's collision offset for a
    /// chord head and scaled for a cue. Null item (off the map) anchors at the column.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/line-spanner.cc:171-175 calc_bound_info — the bound is the
    ///   HEAD grob, so the anchor is that glyph's own extent edge.
    /// ⚠️ start-at-dot (a dotted start head anchors at its DOT's right edge,
    ///   define-grobs.scm:1701) is not spelled yet — no book measures it; ticketed
    ///   2026-08-07 session 109.
    /// </remarks>
    private static double HeadInkEdge(MusicItem? item, int memberIndex, bool right)
    {
        switch (item)
        {
            case NoteItem n:
            {
                double scale = n.IsCue ? EngravingDefaults.CueScale : 1.0;
                int noteValue = GlyphMetrics.NoteValueOf(n.BaseDuration);
                return right ? GlyphMetrics.GetNoteheadBBox(noteValue).Right * scale : 0;
            }
            case ChordItem c when c.Notes.Length > 0:
            {
                double scale = c.IsCue ? EngravingDefaults.CueScale : 1.0;
                int noteValue = GlyphMetrics.NoteValueOf(c.BaseDuration);
                int i = memberIndex >= 0 && memberIndex < c.Notes.Length ? memberIndex : 0;
                double[] offsets = ChordHeadPositioning.CalculateOffsets(
                    c.Notes, c.StemUp, noteValue, scale);
                return offsets[i]
                    + (right ? GlyphMetrics.GetNoteheadBBox(noteValue).Right * scale : 0);
            }
            default:
                return 0;
        }
    }

    /// <summary>
    /// The LEFT ink edge of the target column's printed accidentals in its column
    /// frame, or null when the column prints none — the x a glissando's right bound
    /// anchors at so the line stops before the accidental.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/line-spanner.cc:177-202 calc_bound_info — end-on-accidental
    ///   rereads x from the note column's AccidentalPlacement extent; the leftmost ink
    ///   of ALL the column's accidentals is that extent's LEFT edge.
    /// The offsets are the SAME single-ape placement the renderer draws with
    /// (SharedRenderer.DrawNote / the chord branch), packed X first — so the ink
    /// stopped before is the ink drawn. The packed X is measured from the STAFF
    /// column; the multi-voice collision shift is not undone here (no book measures a
    /// multi-voice chord glissando).
    /// </remarks>
    private static double? LeftmostAccidentalInkX(MusicItem? item)
    {
        switch (item)
        {
            case NoteItem { Accidental: not null } n:
            {
                if (n.AccidentalX is { } packed)
                    return packed;
                var cueFont = n.IsCue ? EngravingDefaults.CueFont : null;
                return AccidentalColumn.CalculateSinglePosition(n, cueFont, cueFont)?.XOffset;
            }
            case ChordItem c when c.Notes.Length > 0:
            {
                double? best = null;
                if (c.HasPackedAccidentals)
                {
                    foreach (var n in c.Notes)
                        if (n.Accidental is not null && n.AccidentalX is { } ax
                            && (best is not { } b || ax < b))
                            best = ax;
                    return best;
                }
                double scale = c.IsCue ? EngravingDefaults.CueScale : 1.0;
                int noteValue = GlyphMetrics.NoteValueOf(c.BaseDuration);
                double[] offsets = ChordHeadPositioning.CalculateOffsets(
                    c.Notes, c.StemUp, noteValue, scale);
                var cueFont = c.IsCue ? EngravingDefaults.CueFont : null;
                foreach (var al in AccidentalColumn.CalculatePositions(
                    c.Notes, offsets, cueFont, cueFont))
                    if (best is not { } b || al.XOffset < b)
                        best = al.XOffset;
                return best;
            }
            default:
                return null;
        }
    }
}
