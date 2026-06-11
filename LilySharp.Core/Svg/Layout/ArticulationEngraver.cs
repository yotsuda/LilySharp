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
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Layout information for an articulation mark.
/// All coordinates are in staff spaces.
/// </summary>
/// <remarks>
/// LILYPOND-REF: define-grobs.scm:2268-2310 Script grob
/// LILYPOND-REF: script-interface.cc positioning logic
/// </remarks>
public readonly record struct ArticulationLayout(
    int MeasureIndex,       // Measure containing this articulation
    int ItemIndex,          // Item index within measure (for X alignment)
    double X,               // Absolute X position (staff spaces from score start)
    double Y,               // Y position (staff spaces from staff top, positive = down)
    string Glyph,           // SMuFL glyph to render
    bool IsAbove,           // Whether placed above the note
    int SourcePosition      // For click-to-source mapping
);

/// <summary>
/// Calculates positions for articulation marks.
/// Implements LilyPond's articulation positioning algorithm.
/// </summary>
/// <remarks>
/// LILYPOND-REF: script-engraver.cc:92-125 Script_engraver::acknowledge_note_head
/// LILYPOND-REF: side-position-interface.cc:92-111 axis_aligned_side_helper
///
/// LilyPond places articulations with:
/// - avoid-slur: around
/// - direction: automatically chosen based on stem direction
/// - padding: 0.2 staff spaces
/// - staff-padding: 0.25 staff spaces
/// </remarks>
public static class ArticulationEngraver
{
    // LILYPOND-REF: define-grobs.scm:2280 padding = 0.2
    private const double Padding = 0.2;

    // LILYPOND-REF: define-grobs.scm:2295 staff-padding = 0.25
    private const double StaffPadding = 0.25;

    // Notehead vertical half-extent (from GlyphMetrics.NoteheadBlack.Top)
    // LILYPOND-REF: the support skyline of a notehead extends ±0.5 staff spaces from center
    private const double NoteheadHalfHeight = 0.5;

    // LILYPOND-REF: stem.cc:93 default stem-length = 3.5
    private const double DefaultStemLength = 3.5;

    // Staff middle line position
    private const double StaffMiddle = 2.0;

    // Staff top and bottom
    private const double StaffTop = 0.0;
    private const double StaffBottom = 4.0;

    /// <summary>
    /// Calculates layout for all articulations in a score.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: side-position-interface.cc:193-400 aligned_side()
    /// Articulations are positioned relative to the note's staff position:
    /// - For notes above middle line: articulations go below (unless overridden)
    /// - For notes below middle line: articulations go above (unless overridden)
    /// - Fermata and ornaments always go above
    /// </remarks>
    public static ImmutableArray<ArticulationLayout> Calculate(
        Score score,
        ImmutableArray<ArticulationItem> articulations,
        ImmutableArray<SystemLayout> systems,
        ImmutableArray<MeasureLayout> measureLayouts)
    {
        if (articulations.IsDefaultOrEmpty)
            return ImmutableArray<ArticulationLayout>.Empty;

        var layouts = ImmutableArray.CreateBuilder<ArticulationLayout>(articulations.Length);

        foreach (var articulation in articulations)
        {
            // Find the measure layout
            if (articulation.MeasureIndex >= measureLayouts.Length)
                continue;

            var measureLayout = measureLayouts[articulation.MeasureIndex];

            // Bounds guard (single-staff layouts only; multi-staff layouts
            // resolve through timing-aligned columns).
            if (measureLayout.Columns.IsDefaultOrEmpty
                && articulation.ItemIndex >= measureLayout.Items.Length)
                continue;

            // Get the music item to determine staff position
            // LILYPOND-REF: script-engraver.cc:92-125 acknowledge_note_head
            var measure = score.Voice.Measures[articulation.MeasureIndex];
            var item = measure.Items[articulation.ItemIndex];

            // Get staff position of the note
            int staffPosition = GetStaffPosition(item);
            bool stemUp = GetStemUp(item, staffPosition);

            // Calculate X position (centered on the note).
            // The item X is the notehead's LEFT edge and articulation glyphs are
            // origin-centred (symmetric BBox), so add the notehead's half-width to
            // land the glyph centre on the notehead centre rather than its left edge.
            // LILYPOND-REF: define-grobs.scm:2289 self-alignment-X = CENTER
            double x = measureLayout.X
                + LayoutUtilities.GetItemXOffset(score.Voice.Measures,
                    articulation.MeasureIndex, articulation.ItemIndex, measureLayout)
                + NoteheadHalfWidth(item);

            // Calculate Y position based on note position and direction
            // LILYPOND-REF: side-position-interface.cc:229-264 skyline calculation
            double y = CalculateYPosition(articulation, staffPosition, stemUp);

            layouts.Add(new ArticulationLayout(
                articulation.MeasureIndex,
                articulation.ItemIndex,
                x,
                y,
                articulation.GetGlyph(),
                articulation.IsAbove,
                articulation.SourcePosition
            ));
        }

        return layouts.ToImmutable();
    }

    /// <summary>
    /// Gets the staff position of a music item.
    /// </summary>
    private static int GetStaffPosition(MusicItem item) => item switch
    {
        NoteItem note => note.StaffPosition,
        ChordItem chord => chord.Notes.Length > 0
            ? (chord.Notes.Max(n => n.StaffPosition) + chord.Notes.Min(n => n.StaffPosition)) / 2
            : 4,
        _ => 0 // Default to middle line (StaffPosition 0 = B4 in treble clef)
    };

    /// <summary>
    /// Half the notehead's advance width, i.e. the offset from the notehead's left
    /// edge (the item X) to its horizontal centre. Picks the head glyph by note
    /// value (whole / half / black) so the script centres on the actual head.
    /// </summary>
    private static double NoteheadHalfWidth(MusicItem item)
    {
        int noteValue = item switch
        {
            NoteItem n => n.BaseDuration.Numerator == 1 ? n.BaseDuration.Denominator : 1,
            ChordItem c => c.BaseDuration.Numerator == 1 ? c.BaseDuration.Denominator : 1,
            _ => 4
        };
        double advance = noteValue switch
        {
            1 => GlyphMetrics.NoteheadWholeAdvance,
            2 => GlyphMetrics.NoteheadHalfAdvance,
            _ => GlyphMetrics.NoteheadBlackAdvance
        };
        return advance / 2.0;
    }

    /// <summary>
    /// Determines stem direction from the item.
    /// </summary>
    private static bool GetStemUp(MusicItem item, int staffPosition) => item switch
    {
        NoteItem note => note.StemUp,
        ChordItem chord => chord.StemUp,
        _ => staffPosition < 0 // Default: stem up for notes below middle line
    };

    /// <summary>
    /// Gets the glyph bounding box for an articulation type.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: mf/feta-scripts.mf set_char_box() for each script glyph
    /// </remarks>
    private static GlyphMetrics.BBox GetGlyphBBox(ArticulationType type) => type switch
    {
        ArticulationType.Staccato => GlyphMetrics.ArticStaccato,
        ArticulationType.Accent => GlyphMetrics.ArticAccent,
        ArticulationType.Tenuto => GlyphMetrics.ArticTenuto,
        ArticulationType.Marcato => GlyphMetrics.ArticMarcatoAbove, // direction handled separately
        _ => new GlyphMetrics.BBox(-0.5, -0.5, 0.5, 0.5) // fallback for fermata, ornaments
    };

    /// <summary>
    /// Gets the full vertical extent (total height) of an articulation glyph.
    /// Used for non-quantized articulations in the extent+padding positioning model.
    /// </summary>
    private static double GetArticulationExtent(ArticulationType type)
    {
        var bbox = GetGlyphBBox(type);
        return type switch
        {
            // Fermata and ornaments: use larger values since glyph BBox not defined
            ArticulationType.Fermata => 1.5,
            ArticulationType.Trill or ArticulationType.Mordent or ArticulationType.Prall
                or ArticulationType.Turn or ArticulationType.InvertedTurn
                or ArticulationType.PrallTriller => 1.0,
            _ => bbox.Height
        };
    }

    /// <summary>
    /// Gets the vertical extent of the glyph in the direction toward the note
    /// (the "near side" extent used in skyline distance calculation).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: side-position-interface.cc:229-264 my_dim skyline is the articulation's
    /// extent in the -dir direction (toward the support/note).
    ///
    /// For symmetric glyphs (staccato, tenuto): near extent = half height.
    /// For asymmetric glyphs (marcato): near extent = 0 (tip points toward note).
    /// </remarks>
    private static double GetNearExtent(ArticulationType type, bool isAbove)
    {
        var bbox = GetGlyphBBox(type);
        // "Near extent" = how far the glyph extends toward the note from its reference point.
        // For above placement: the glyph's bottom extent (positive = extends downward toward note)
        // For below placement: the glyph's top extent (positive = extends upward toward note)
        return isAbove ? -bbox.Bottom : bbox.Top;
    }

    private static double CalculateYPosition(ArticulationItem articulation, int staffPosition, bool stemUp)
    {
        // LILYPOND-REF: define-grobs.scm:1365 fermata: direction = UP
        // LILYPOND-REF: define-grobs.scm:2175 TrillSpanner: direction = UP
        bool forceAbove = articulation.Type == ArticulationType.Fermata || articulation.IsOrnament;
        bool isAbove = forceAbove || articulation.IsAbove;

        // Convert staff position to Y coordinate (staff spaces from top).
        // StaffPosition: 0 = middle line, positive = up, negative = down.
        // Canonical formula used by note rendering: Y = StaffMiddle - StaffPosition * 0.5
        // LILYPOND-REF: staff-symbol-referencer.cc:76-89 staff_symbol_referencer::get_position
        double noteY = StaffMiddle - staffPosition * 0.5;

        // Use quantize-position for staccato, marcato, tenuto
        // LILYPOND-REF: scm/script.scm staccato/marcato/tenuto: (quantize-position . #t)
        if (ShouldQuantize(articulation.Type))
        {
            return QuantizedYPosition(noteY, isAbove, stemUp, articulation.Type);
        }

        // Non-quantized path: fermata, ornaments, accent, portato
        // LILYPOND-REF: side-position-interface.cc:360-378 total_off calculation
        // LILYPOND-REF: side-position-interface.cc:426-445 staff-padding clamp
        //
        // include_staff = true (staff-padding exists AND quantize-position = false)
        // The staff is included in the support skyline, then staff-padding is applied.

        double glyphNearExtent = GetNearExtent(articulation.Type, isAbove);
        double supportExtent = isAbove
            ? (stemUp ? DefaultStemLength : NoteheadHalfHeight)
            : (!stemUp ? DefaultStemLength : NoteheadHalfHeight);

        // dist = skyline distance; total_off = dist + padding
        double totalOff = supportExtent + glyphNearExtent + Padding;
        double targetY = isAbove ? noteY - totalOff : noteY + totalOff;

        if (isAbove)
        {
            // LILYPOND-REF: side-position-interface.cc:426-445 staff-padding clamp
            // Ensure the glyph's bottom edge clears the staff top by staff-padding
            double glyphBottom = targetY + glyphNearExtent; // glyph's edge toward staff
            double staffEdge = StaffTop - StaffPadding;
            if (glyphBottom > staffEdge)
                targetY = staffEdge - glyphNearExtent;
            return targetY;
        }
        else
        {
            // Ensure the glyph's top edge clears the staff bottom by staff-padding
            double glyphTop = targetY - glyphNearExtent; // glyph's edge toward staff
            double staffEdge = StaffBottom + StaffPadding;
            if (glyphTop < staffEdge)
                targetY = staffEdge + glyphNearExtent;
            return targetY;
        }
    }

    /// <summary>
    /// Returns true for articulation types that use LilyPond's quantize-position algorithm.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/script.scm — these scripts have (quantize-position . #t)
    /// </remarks>
    private static bool ShouldQuantize(ArticulationType type) => type switch
    {
        ArticulationType.Staccato => true,
        ArticulationType.Marcato => true,
        ArticulationType.Tenuto => true,
        _ => false
    };

    /// <summary>
    /// Calculates Y position using LilyPond's quantize-position algorithm.
    /// Follows the aligned_side() flow from side-position-interface.cc:
    ///   1. Calculate skyline distance (support extent + glyph extent)
    ///   2. Add padding to get total_off
    ///   3. Convert to LP staff position and apply quantize-position
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: side-position-interface.cc:193-448 aligned_side() full flow
    /// LILYPOND-REF: side-position-interface.cc:360-378 total_off = dir * dist + dir * ss * padding
    /// LILYPOND-REF: side-position-interface.cc:402-425 quantize-position
    /// LILYPOND-REF: misc.cc directed_round() — ceil for UP, floor for DOWN
    ///
    /// LP staff positions for 5-line staff:
    ///   Lines: -4 (bottom), -2, 0 (middle), 2, 4 (top)
    ///   Spaces: -5, -3, -1, 1, 3, 5
    ///
    /// Conversion: lpPos = (StaffMiddle - Y) * 2;  Y = StaffMiddle - lpPos / 2
    /// </remarks>
    private static double QuantizedYPosition(double noteY, bool isAbove, bool stemUp, ArticulationType type)
    {
        // ── Stage 4-5 (aligned_side): Calculate total_off ──
        //
        // LILYPOND-REF: side-position-interface.cc:266-328 build support skylines
        // The support skyline for a Script grob is the notehead (+ stem if same direction).
        // Stems pointing AWAY from the articulation are skipped:
        //   LILYPOND-REF: side-position-interface.cc:279-284
        //   if (dir == -get_grob_direction(e)) continue;
        //
        // For staccato (side-relative-direction = DOWN):
        //   stem UP → staccato dir=DOWN → stem dir=UP → dir != -stem_dir → stem SKIPPED
        //   stem DOWN → staccato dir=UP → stem dir=DOWN → dir != -stem_dir → stem SKIPPED
        // In both normal cases, only the notehead is in the support.
        // Stem is only included when direction is forced (e.g., fermata above with stem up).

        double supportExtent; // Support (notehead/stem) extent in the direction of placement
        if (isAbove)
        {
            // For above: support's UP extent (top of notehead, or stem tip if stem goes up)
            // Stem is included only when stem direction matches placement direction
            supportExtent = stemUp ? (noteY - (noteY - DefaultStemLength)) : NoteheadHalfHeight;
            // ↑ if stemUp AND isAbove: stem IS in support (forced above case), use stem length
            // ↑ if !stemUp AND isAbove: stem skipped, just notehead top = 0.5
        }
        else
        {
            // For below: support's DOWN extent
            supportExtent = !stemUp ? (noteY + DefaultStemLength - noteY) : NoteheadHalfHeight;
            // ↑ if !stemUp AND !isAbove: stem IS in support (forced below case), use stem length
            // ↑ if stemUp AND !isAbove: stem skipped, just notehead bottom = 0.5
        }

        // LILYPOND-REF: side-position-interface.cc:229-264 my_dim skyline (-dir direction)
        // The glyph's "near extent" = how far it extends toward the note from its reference point
        double glyphNearExtent = GetNearExtent(type, isAbove);

        // LILYPOND-REF: side-position-interface.cc:360-365
        // dist = dim.distance(my_dim, horizon_padding)
        // For simple bounding boxes: dist = supportExtent + glyphNearExtent
        double dist = supportExtent + glyphNearExtent;

        // LILYPOND-REF: side-position-interface.cc:366-370
        // total_off = dir * dist + dir * ss * padding
        // (ss = staff_space = 1.0 in our coordinate system)
        double totalOff = dist + Padding;

        // Convert total_off to target Y position
        double targetY = isAbove ? noteY - totalOff : noteY + totalOff;

        // ── Stage 7 (aligned_side): Apply quantize-position ──
        //
        // LILYPOND-REF: side-position-interface.cc:402-425
        // Note: include_staff = false when quantize-position = true (line 222-226)
        // So staff-padding is NOT applied before quantization.

        // Convert to LP staff position
        // LP: 0 = middle line (our Y=2.0), positive = up, negative = down
        double lpPosition = (StaffMiddle - targetY) * 2.0;

        // Directed round (away from the note)
        // LILYPOND-REF: misc.cc directed_round(): ceil for UP, floor for DOWN
        double rounded = isAbove ? Math.Ceiling(lpPosition) : Math.Floor(lpPosition);

        // Check if quantization applies
        // LILYPOND-REF: side-position-interface.cc:414-424
        // Staff line span for 5-line staff: [-4, 4], widened by 1: [-5, 5]
        const double StaffSpanMin = -5.0;
        const double StaffSpanMax = 5.0;
        bool inStaffSpan = lpPosition >= StaffSpanMin && lpPosition <= StaffSpanMax;
        // LILYPOND-REF: side-position-interface.cc:418
        // has_interface<Note_head>(head) && dir * position < 0
        // Articulation is between note and staff center (ledger line note case)
        bool betweenNoteAndStaff = isAbove ? lpPosition < 0 : lpPosition > 0;

        if (inStaffSpan || betweenNoteAndStaff)
        {
            // LILYPOND-REF: side-position-interface.cc:420
            // total_off += (rounded - position) * 0.5 * ss;
            // Equivalent: snap targetY to the rounded LP position
            targetY = StaffMiddle - rounded / 2.0;

            // LILYPOND-REF: side-position-interface.cc:421-422
            // if (Staff_symbol_referencer::on_line(me, int(rounded)))
            //     total_off += dir * 0.5 * ss;
            // Even LP positions within staff lines [−4, 4] are on lines
            int roundedInt = (int)rounded;
            if (roundedInt >= -4 && roundedInt <= 4 && roundedInt % 2 == 0)
            {
                targetY += isAbove ? -0.5 : 0.5;
            }
        }

        return targetY;
    }
}
