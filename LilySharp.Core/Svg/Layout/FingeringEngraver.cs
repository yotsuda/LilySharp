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
/// Layout for a single Fingering grob (a finger number digit attached to a note).
/// Coordinates are in staff spaces.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/fingering-engraver.cc — Fingering grob
/// LILYPOND-REF: scm/define-grobs.scm — Fingering (font-size . -5, padding . 0.5)
/// </remarks>
public readonly record struct FingeringLayout(
    // Measure containing the host note.
    int MeasureIndex,
    // Item index of the host note within its measure.
    int ItemIndex,
    // Finger number (typically 1-5).
    int Number,
    // X coordinate of the digit center (staff spaces from score start).
    double X,
    // Y of the digit baseline in the LilyPond-native Y-up frame: staff-spaces
    // above THIS fingering's staff middle line, up-positive (frame B). The
    // renderer reflects it to device against the staff middle it resolves.
    double YUp,
    // True = above the staff, false = below.
    bool IsAbove,
    // Source position for click-to-source mapping.
    int SourcePosition,
    // F3/B: staff index of the host note/chord, so a reused layout re-derives
    // data-pos from the live score (SharedRenderer.ResolveDataPos). -1 = unresolved.
    int StaffIndex = -1,
    // The fingering's script-priority in its note's SCRIPT COLUMN, or int.MinValue
    // when it does not enter the column (chord fingerings — FingeringColumn is a
    // different mechanism). A vertically-oriented fingering is a script like any
    // other: priority 100 (the Fingering grob's declaration) + direction × the
    // head's staff position, sorted into the same per-note walk that stacks
    // staccato under tenuto under a bow (ArticulationEngraver.CalculateWithFingerings).
    // LILYPOND-REF: lily/new-fingering-engraver.cc:334-335 set_property
    //   "script-priority" (finger_prio + d * ft.position_);
    //   scm/define-grobs.scm:1554 Fingering script-priority = 100.
    int ColumnPriority = int.MinValue);

/// <summary>
/// Calculates positions for fingering numbers attached to notes.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/fingering-engraver.cc Fingering_engraver
/// LILYPOND-REF: lily/script-interface.cc — direction calculation (opposite to stem)
/// LILYPOND-REF: scm/define-grobs.scm Fingering
///   font-size = -5 (very small), padding = 0.5, avoid-slur = #'inside, side-axis = Y
///
/// Fingering numbers default to the side opposite the stem; multiple fingerings
/// on a chord stack vertically (FingeringColumn). This implementation handles
/// single fingerings per note and stacks chord fingerings via item-index ordering.
/// </remarks>
internal static class FingeringEngraver
{
    /// <summary>
    /// LILYPOND-REF: scm/define-grobs.scm Fingering — staff-padding default.
    /// Distance from the staff edge to the first fingering digit.
    /// </summary>
    private const double StaffPadding = 0.5;

    /// <summary>
    /// The digits of a fingering as feta-text glyph metrics AT THE FINGERING'S OWN SIZE
    /// (font-size −5 of the 4-ss staff-height base — the same em the figured bass shares,
    /// <see cref="EngravingDefaults.FiguredBassFontSize"/>, both being fetaText at −5):
    /// the mapped glyph run, its ink box relative to the run's LEFT BASELINE origin, and
    /// its advance width. One home for the pen (SharedRenderer.DrawFingerings), the
    /// script-column profile (ArticulationEngraver) and the placement below.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:1547-1568 Fingering (outside-staff-interface
    ///   family) — font-encoding fetaText, font-features cv47 ss01, font-size −5;
    ///   its stencil is text-interface::print of fingering::calc-text (:1559-1560),
    ///   so the drawn ink IS these glyphs at that size.
    /// ⚠️ IT WAS A SERIF DIGIT AT 0.56 EM until 2026-08-08 — roughly HALF LilyPond's ink
    /// (feta numerals are 2.0 design-ss tall → 1.12 at −5), so everything stacked over a
    /// fingering sat a half-space too low. Measured on script-stack-order1: a bow over a
    /// fingering is at −5.33 (LP) = digit top 1.13 + padding 0.20.
    /// </remarks>
    internal static (string Glyphs, GlyphMetrics.BBox Ink, double Width) DigitRun(int number)
    {
        // The ten single digits — every fingering in practice — are answered from a
        // table built once: DigitRun runs per fingering in the island pass, the
        // column flush AND every preview redraw, and each uncached call walks the
        // glyph run three times (pieces, width, ink). A pure function of the number,
        // so the memo is exact.
        if (number is >= 0 and <= 9)
            return SingleDigitRuns[number] ??= BuildDigitRun(number);
        return BuildDigitRun(number);
    }

    private static readonly (string, GlyphMetrics.BBox, double)?[] SingleDigitRuns =
        new (string, GlyphMetrics.BBox, double)?[10];

    private static (string Glyphs, GlyphMetrics.BBox Ink, double Width) BuildDigitRun(int number)
    {
        string text = number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var glyphs = new System.Text.StringBuilder(text.Length);
        foreach (var piece in FiguredBassGlyphRun.Pieces(text))
            if (piece.IsGlyph)
                glyphs.Append(piece.Ch);
        double width = FiguredBassGlyphRun.Width(text);
        return (glyphs.ToString(),
            new GlyphMetrics.BBox(0.0, FiguredBassGlyphRun.InkBottom(text),
                width, FiguredBassGlyphRun.InkTop(text)),
            width);
    }

    /// <summary>
    /// Calculates layouts for all fingerings in a single-staff score.
    /// </summary>
    public static ImmutableArray<FingeringLayout> Calculate(
        Score score,
        ImmutableArray<SystemLayout> systems,
        int staffIndex = -1)
    {
        if (score.Voices.IsDefaultOrEmpty)
            return ImmutableArray<FingeringLayout>.Empty;

        var measureMap = LayoutUtilities.BuildMeasureLayoutMap(systems);
        var systemMap = LayoutUtilities.BuildMeasureMap(systems);

        var layouts = ImmutableArray.CreateBuilder<FingeringLayout>();

        var voice = score.Voice;
        for (int mi = 0; mi < voice.Measures.Length; mi++)
        {
            var measure = voice.Measures[mi];
            if (!measureMap.TryGetValue(mi, out var measureLayout))
                continue;
            if (!systemMap.ContainsKey(mi))
                continue;

            for (int ii = 0; ii < measure.Items.Length; ii++)
            {
                var item = measure.Items[ii];
                if (item is NoteItem note && note.Fingering.HasValue)
                {
                    var layout = BuildLayout(
                        note, mi, ii, measureLayout, staffIndex, voice.Measures);
                    if (layout.HasValue)
                        layouts.Add(layout.Value);
                }
                else if (item is ChordItem chord)
                {
                    // LILYPOND-REF: lily/fingering-column.cc — stack chord fingerings.
                    BuildChordFingerings(
                        chord, mi, ii, measureLayout, staffIndex, layouts, voice.Measures);
                }
            }
        }

        return layouts.ToImmutable();
    }

    /// <summary>
    /// Emits one <see cref="FingeringLayout"/> per chord pitch that carries a
    /// finger number, stacked vertically on the appropriate side of the chord.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/fingering-column.cc — FingeringColumn vertical stacking.
    /// Stem-up chords place fingerings on the LEFT side ordered top-to-bottom;
    /// stem-down chords place them on the RIGHT, ordered bottom-to-top. This
    /// implementation uses Y placement that mirrors each pitch's staff position
    /// so each finger sits at its corresponding note (a simpler, equally LP-compatible
    /// arrangement).
    /// </remarks>
    private static void BuildChordFingerings(
        ChordItem chord, int measureIndex, int itemIndex,
        MeasureLayout measureLayout, int staffIndex,
        ImmutableArray<FingeringLayout>.Builder layouts,
        ImmutableArray<Measure> measures)
    {
        if (measureLayout.Columns.IsDefaultOrEmpty
            && itemIndex >= measureLayout.Items.Length)
            return;
        // The head's INK centre — see BuildLayout for the rule and the measurement.
        double centerX = measureLayout.X + LayoutUtilities.GetItemXOffset(
            measures, measureIndex, itemIndex, measureLayout)
            + GlyphMetrics.GetNoteheadBBox(
                chord.BaseDuration.Numerator == 1 ? (int)chord.BaseDuration.Denominator : 1).CenterX;

        bool isAbove = !chord.StemUp;

        foreach (var note in chord.Notes)
        {
            if (!note.Fingering.HasValue)
                continue;

            // Y-up (frame B): align with the note's own staff position — a head at
            // staff-position p sits p/2 spaces above the staff middle. No staff
            // offset here; the renderer resolves the staff middle at draw time.
            double yUp = note.StaffPosition * 0.5;

            layouts.Add(new FingeringLayout(
                MeasureIndex: measureIndex,
                ItemIndex: itemIndex,
                Number: note.Fingering.Value,
                X: centerX,
                YUp: yUp,
                IsAbove: isAbove,
                SourcePosition: chord.SourcePosition,
                StaffIndex: staffIndex));
        }
    }

    private static FingeringLayout? BuildLayout(
        NoteItem note, int measureIndex, int itemIndex,
        MeasureLayout measureLayout, int staffIndex,
        ImmutableArray<Measure> measures)
    {
        if (measureLayout.Columns.IsDefaultOrEmpty
            && itemIndex >= measureLayout.Items.Length)
            return null;

        // Centered on the notehead glyph (self-alignment-X = CENTER), via the
        // Items/Columns-aware resolver. The centre is the head's own INK centre: what
        // aligned_on_parent centres on is the PARENT's stencil extent, and a NoteHead's
        // extent is its ink — 1.9620 on the whole head, 1.3042 on the black one, dumped
        // out of LilyPond in audit/lp-geometry/probes/dynamic-support.ly's books, against
        // advances of 1.960 and 1.304.
        // LILYPOND-REF: lily/self-alignment-interface.cc:147 aligned_on_parent — he = him->extent (him, a), the parent's own stencil extent
        // ⚠️ THIS READ THE ADVANCE UNTIL 2026-08-05 (session 95). It is the same defect
        // DynamicEngraver.AnchorCentreOffset carried, found there because that island had
        // points; NOTHING in the ledger observes a fingering, so this site is corrected on
        // the rule and the font table rather than on a residual. If a fingering point is
        // ever raised, it is 0.001 on a whole head that it should find already paid.
        double centerX = measureLayout.X + LayoutUtilities.GetItemXOffset(
            measures, measureIndex, itemIndex, measureLayout)
            + GlyphMetrics.GetNoteheadBBox(
                note.BaseDuration.Numerator == 1 ? (int)note.BaseDuration.Denominator : 1).CenterX;

        // Direction: a melodic (single-voice) fingering defaults ABOVE the staff,
        // NOT opposite the stem. LilyPond's New_fingering_engraver buckets each
        // fingering by fingeringOrientations and tries them in order; the default
        // is '(up down), so a lone fingering takes the first orientation = up —
        // independent of stem direction. (Verified against LilyPond 2.24.4: a
        // stem-UP bass note still gets its fingering above, i.e. on the same side
        // as the stem.)
        // LILYPOND-REF: scm/define-context-properties.scm:411 fingeringOrientations;
        //   ly/engraver-init.ly:907 fingeringOrientations = #'(up down) (Score-level
        //   default, propagates to Voice);
        //   lily/new-fingering-engraver.cc:182,210,360 position_scripts (up/down buckets).
        bool isAbove = true;

        // Y baseline in Y-up (frame B): place the digit just outside the staff on
        // the appropriate side, but also clear of the NOTEHEAD. A fingering on a
        // note above the top line (or below the bottom) must sit outside the
        // notehead, else it overprints it. Take whichever is farther OUT — the
        // staff edge or the note — which in Y-up is the max above / min below.
        // This mirrors LilyPond's side-position (the notehead is a support) while
        // honouring the Fingering staff-padding. No staff offset: the renderer
        // resolves the staff middle at draw time.
        // LILYPOND-REF: lily/side-position-interface.cc; scm/define-grobs.scm Fingering.
        // ⚠️ BOTH OF THESE HAVE ONE HOME, and both used to be spelled again right here — a
        // literal 2.0 and a literal 0.5 beside a LILYPOND-REF, which is the shape HANDOFF
        // 5.2.1② names. The staff half-span is EngravingDefaults.StaffMiddle (the same "half
        // of a five-line staff" frame) and the nominal notehead half-height is
        // EngravingDefaults.NoteheadHalfHeight, whose own remark records that it is 0.045
        // short of the glyph. Reading them from there is output-identical and means a future
        // correction reaches this call site too, instead of leaving it behind.
        // The staff support is the StaffSymbol's INK — its outermost line's top edge
        // (2.0 + half the line thickness = 2.05), the same 2.05 every other
        // side-positioned grob pays before its padding (bar numbers, text scripts:
        // ledger textscript.no-descender.staff-to-baseline = 2.05 + 0.5). Measured on
        // script-stack-order1: an isolated fingering over a staff-clearing head sits
        // at 2.55 in LilyPond, not 2.50.
        const double StaffInk = EngravingDefaults.StaffMiddle
            + EngravingDefaults.StaffLineThickness / 2.0;
        const double NoteheadHalfHeight = EngravingDefaults.NoteheadHalfHeight;
        double digitTop = DigitRun(note.Fingering!.Value).Ink.Top;
        double noteUp = note.StaffPosition * 0.5;
        double yUp = isAbove
            ? System.Math.Max(StaffInk + StaffPadding,
                noteUp + NoteheadHalfHeight + StaffPadding)
            : System.Math.Min(-StaffInk - StaffPadding - digitTop,
                noteUp - NoteheadHalfHeight - StaffPadding - digitTop);

        return new FingeringLayout(
            MeasureIndex: measureIndex,
            ItemIndex: itemIndex,
            Number: note.Fingering!.Value,
            X: centerX,
            YUp: yUp,
            IsAbove: isAbove,
            SourcePosition: note.SourcePosition,
            StaffIndex: staffIndex,
            // Vertical fingerings are scripts of the note's column: priority 100 +
            // direction × head position (direction here is UP, the default bucket).
            // LILYPOND-REF: lily/new-fingering-engraver.cc:334-335 set_property
            // ⚠️ THE STEM IS NOT A SUPPORT HERE. LilyPond adds the stem and its flag
            // to the fingering's supports (new-fingering-engraver.cc:186-190), but
            // Fingering gates them with add-stem-support = only-if-beamed
            // (scm/define-grobs.scm:1543), so an UNBEAMED note — every note of
            // script-stack-order1, where this port was measured — reads identically.
            // A fingering over a BEAMED note sits too low until that gate is ported;
            // no fixture or corpus book reaches the pairing yet.
            ColumnPriority: 100 + note.StaffPosition);
    }
}
