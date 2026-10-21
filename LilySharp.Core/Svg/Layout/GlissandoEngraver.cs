// Lily# - Music notation compiler
// Copyright (C) 2025-2026 Yoshifumi Tsuda
//
// Parts of this file are ported from LilyPond, the GNU music typesetter.
// The C# is a modified translation of the following, not a copy of it:
//   lily/line-spanner.cc
//     Copyright (C) 2000--2026 Jan Nieuwenhuizen <janneke@gnu.org>
//   scm/define-grobs.scm
//     Copyright (C) 1998--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>;
//     Jan Nieuwenhuizen <janneke@gnu.org>
//   lily/spanner.cc
//     Copyright (C) 1996--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>
//   scm/scheme-engravers.scm
//     Copyright (C) 2012--2026 David Nalesnik <david.nalesnik@gmail.com>;
//     Thomas Morley <thomasmorley65@gmail.com>; Dan Eble <nine.fierce.ballads@gmail.com>;
//     Jonas Hahnfeld <hahnjo@hahnjo.de>; Jean Abou Samra <jean@abou-samra.fr>
// LilyPond is free software under the GNU General Public License version 3 or
// later; its notices are kept here as that licence requires. The full list is in
// LILYPOND-ATTRIBUTION.md. Lily# is an independent project, not affiliated with
// or endorsed by the LilyPond project.
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
    int VoiceIndex = 0,
    // The first measure of THIS piece's system — where the piece is drawn (its page and its
    // staff middle). -1 = the same as MeasureIndex (an unbroken glissando). MeasureIndex
    // stays the start note's locator for click-to-source; until 2026-10-20 it placed every
    // piece too, so a glissando broken across a line drew its second piece against the
    // FIRST system (OMR request 2026-10-20, item 2).
    int SystemMeasureIndex = -1)
{
    /// <summary>The measure that names the system this piece is drawn on.</summary>
    public int PlacedMeasureIndex => SystemMeasureIndex >= 0 ? SystemMeasureIndex : MeasureIndex;
}

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
        ImmutableArray<Measure> measures = default,
        ImmutableArray<GraceNoteItem> graceNotes = default,
        Voice? voice = null,
        int voiceIndex = 0,
        VoiceCollisionTable? voiceShifts = null)
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
            // A bound on a GRACE note (session 920, HANDOFF §2 U8 ⒟) is the grace HEAD: its
            // column's X read off the run, its ink and its accidentals out of the grace's own
            // fonts. GetItemXOffset answers no grace column, and until session 920 a glissando
            // INTO a grace note (`c4@glissando grace { g16 } a4`) ran over the grace head to the
            // main note's column. MEASURED (2.26.0, audit/lp-geometry/probes/grace-glissando.ly).
            if (GraceBound(gliss.StartMeasureIndex, gliss.StartItemIndex, startMeasure) is { } gs)
                realStartX = gs.HeadLeftX + GraceHeadInkEdge(gs.Column, gliss.StartStaffPosition, right: true);
            if (GraceBound(gliss.EndMeasureIndex, gliss.EndItemIndex, endMeasure) is { } ge)
                realEndX = ge.HeadLeftX + (GraceAccidentalInkLeft(ge.Column)
                    ?? GraceHeadInkEdge(ge.Column, gliss.EndStaffPosition, right: false));

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

            // The broken pieces' X first: a piece's share of the slope is its share of the
            // whole length.
            // LILYPOND-REF: lily/spanner.cc:124-137 — bounds reattached to system edges.
            var pieces = new List<(SpannerBreakSegment Segment, SystemLayout System, double StartX, double EndX)>();
            foreach (var (segment, system) in SpannerBreakSubstitution.BrokenPieces(
                gliss.StartMeasureIndex, gliss.EndMeasureIndex, systems, measureToSystemIdx))
            {
                var (sx, ex) = SpannerBreakSubstitution.ReattachSpanX(segment, system, realStartX, realEndX);
                // A piece broken on its LEFT starts past the line-start column — the clef,
                // key and meter — not at the system's left edge: the left bound-details'
                // attach-dir RIGHT read on the NonMusicalPaperColumn the piece is bound to.
                // LILYPOND-REF: lily/line-spanner.cc:128-135 calc_bound_info — an unfinished
                //   bound reads the `left` details (plus any `left-broken`, which Glissando
                //   has none of); :171-175 — a paper column's generic_bound_extent at attach.
                if (!segment.IsFirst && staffIndex >= 0)
                    sx = system.Measures[0].X + system.LineStartRightOf(staffIndex);
                pieces.Add((segment, system, sx, ex));
            }
            double totalLength = 0;
            foreach (var p in pieces)
                totalLength += Math.Max(0, p.EndX - p.StartX);

            double before = 0;
            foreach (var (segment, system, pieceStartX, pieceEndX) in pieces)
            {
                double segStartX = pieceStartX, segEndX = pieceEndX;

                // ONE CONTINUOUS SLOPE over every broken piece, as if the systems stood in a
                // row: each piece covers the share of the whole rise its share of the whole
                // length is. (An unbroken glissando is the one piece covering all of it.)
                // LILYPOND-REF: lily/line-spanner.cc:247-406 calc_bound_info — a broken
                //   bound's y is the FAR end's note ("the choice of Solomon": the staves'
                //   middles aligned), so every piece spans the whole rise …
                // LILYPOND-REF: lily/spanner.cc:496-539 calc_normalized_endpoints — … and
                //   lily/line-spanner.cc:582-587 print cuts it to the piece's normalized
                //   share of the summed piece lengths.
                // Until 2026-10-20 the pieces were LILYSHARP-OWN horizontals (the first at
                // the start pitch, the next at the target's), which on paper read like a tie
                // or nothing at all (OMR request 2026-10-20, item 2).
                double pieceLength = Math.Max(0, pieceEndX - pieceStartX);
                double from = totalLength > 0 ? before / totalLength : 0;
                double to = totalLength > 0 ? (before + pieceLength) / totalLength : 1;
                before += pieceLength;
                double segStartY = startStaffY + from * (endStaffY - startStaffY);
                double segEndY = startStaffY + to * (endStaffY - startStaffY);

                // A later piece that spans no musical time — from the line start to a target
                // note standing on the system's first moment — is not drawn. Its length still
                // counted in the slope's shares above, as the piece still exists there.
                // LILYPOND-REF: scm/define-grobs.scm:1694 Glissando after-line-breaking
                //   ly:spanner::kill-zero-spanned-time (lily/spanner.cc) — measured on
                //   LilyPond 2.26.0 (Lab sessions/p915/lp/gliss.ly): the target on the next
                //   system's first note gets no second piece.
                if (!segment.IsFirst && segment.StartMeasureIndex == gliss.EndMeasureIndex
                    && !measures.IsDefaultOrEmpty && gliss.EndMeasureIndex < measures.Length
                    && LayoutUtilities.ItemOnset(measures[gliss.EndMeasureIndex], gliss.EndItemIndex) == Semantics.Fraction.Zero)
                    continue;

                // The padding: each bound pulled 0.5 inward ALONG the line (so its X
                // share shrinks as the line steepens) — at a broken edge too, which reads
                // the same `left`/`right` details (Glissando has no `*-broken` ones). A
                // line too short to pay both paddings is not drawn at all.
                // LILYPOND-REF: lily/line-spanner.cc:128-135 calc_bound_info — the broken bound's details
                // LILYPOND-REF: lily/line-spanner.cc:599 span_points[d] += -d * gaps[d] * magstep * dz.direction()
                // LILYPOND-REF: lily/line-spanner.cc:591-594 Line_spanner::print — gaps beyond dz.length () print nothing
                double dx = segEndX - segStartX;
                double dy = segEndY - segStartY;
                double length = Math.Sqrt(dx * dx + dy * dy);
                if (length <= 2 * Padding)
                    continue;
                double gapRatio = Padding / length;
                segStartX += dx * gapRatio;
                segStartY += dy * gapRatio;
                segEndX -= dx * gapRatio;
                segEndY -= dy * gapRatio;

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
                    // broken segments (it drives click-to-source); the piece's OWN system
                    // is named by SystemMeasureIndex, which the draw places it by.
                    MeasureIndex: gliss.StartMeasureIndex,
                    ItemIndex: gliss.StartItemIndex,
                    VoiceIndex: gliss.VoiceIndex,
                    SystemMeasureIndex: segment.StartMeasureIndex != gliss.StartMeasureIndex
                        ? segment.StartMeasureIndex : -1));
            }
        }

        return layouts.ToImmutableArray();

        (GraceColumnInfo Column, double HeadLeftX)? GraceBound(int measureIndex, int itemIndex, MeasureLayout ml)
            => voice is null || graceNotes.IsDefaultOrEmpty
                ? null
                : ElementCoordinator.GraceColumnAt(voice, graceNotes, Math.Max(staffIndex, 0), voiceIndex,
                    measureIndex, itemIndex, ml, voiceShifts ?? SpacingRules.VoiceCollisionShiftsOf(ImmutableArray.Create(voice)));
    }

    /// <summary>A grace bound head's ink edge in its column's frame — the head at
    /// <paramref name="staffPosition"/> (a chord's member), out of the grace's head font.</summary>
    private static double GraceHeadInkEdge(GraceColumnInfo column, int staffPosition, bool right)
    {
        var offsets = GraceColumnHeads.HeadOffsets(column);
        double offset = 0;
        if (!offsets.IsDefaultOrEmpty)
            for (int i = 0; i < column.Heads.Length && i < offsets.Length; i++)
                if (column.Heads[i].StaffPosition == staffPosition)
                {
                    offset = offsets[i];
                    break;
                }
        return offset + (right
            ? GlyphMetrics.GetNoteheadBBox(column.Font, GlyphMetrics.NoteValueOf(column.BaseDuration)).Right
            : 0);
    }

    /// <summary>The LEFT ink edge of a grace target column's accidentals in its frame, out
    /// of the grace's accidental font, or null when it prints none.</summary>
    private static double? GraceAccidentalInkLeft(GraceColumnInfo column)
    {
        double? best = null;
        foreach (var x in GraceColumnHeads.AccidentalOffsets(column))
            if (x is { } v && (best is not { } b || v < b))
                best = v;
        return best;
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
            // The head's ink is asked of the head's own FONT, not of the twenty's box times a
            // scale — the same design LeftmostAccidentalInkX below hands the accidentals.
            case NoteItem n:
            {
                var font = n.IsCue ? EngravingDefaults.CueFont : MusicFont.Current.DesignAt(0);
                int noteValue = GlyphMetrics.NoteValueOf(n.BaseDuration);
                return right ? GlyphMetrics.GetNoteheadBBox(font, noteValue).Right : 0;
            }
            case ChordItem c when c.Notes.Length > 0:
            {
                var font = c.IsCue ? EngravingDefaults.CueFont : MusicFont.Current.DesignAt(0);
                int noteValue = GlyphMetrics.NoteValueOf(c.BaseDuration);
                int i = memberIndex >= 0 && memberIndex < c.Notes.Length ? memberIndex : 0;
                double[] offsets = ChordHeadPositioning.CalculateOffsets(
                    c.Notes, c.StemUp, noteValue, font);
                return offsets[i]
                    + (right ? GlyphMetrics.GetNoteheadBBox(font, noteValue).Right : 0);
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
                var cueFont = c.IsCue ? EngravingDefaults.CueFont : null;
                int noteValue = GlyphMetrics.NoteValueOf(c.BaseDuration);
                // ONE FONT for the heads the stacking clears and for the reversal that moves
                // them — see ChordHeadPositioning.
                double[] offsets = ChordHeadPositioning.CalculateOffsets(
                    c.Notes, c.StemUp, noteValue, cueFont);
                foreach (var al in AccidentalColumn.CalculatePositions(
                    c.Notes, offsets, cueFont, cueFont, AccidentalStem.Of(c, c.StemUp, cueFont)))
                    if (best is not { } b || al.XOffset < b)
                        best = al.XOffset;
                return best;
            }
            default:
                return null;
        }
    }
}
