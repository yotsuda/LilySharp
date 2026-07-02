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
    int SourcePosition,     // For click-to-source mapping
    int SourceIndex = -1,   // F3/B: index into BuildAllMarks() — position-independent
                            //   ref so a reused layout re-derives SourcePosition from
                            //   the live score (see SharedRenderer.ResolveDataPos).
    int SwingSubdivision = 0 // Tempo marks only: note value to swing (0/8/16) for the feel equation.
);

/// <summary>
/// Calculates positions for music marks.
/// Implements LilyPond's mark positioning algorithm with outside-staff-priority stacking.
/// </summary>
/// <remarks>
/// LILYPOND-REF: mark-engraver.cc:46-89 Mark creation
/// LILYPOND-REF: side-position-interface.cc:92-111 axis_aligned_side_helper
/// LILYPOND-REF: axis-group-interface.cc:865-984 skyline_spacing / outside-staff-priority stacking
///
/// LilyPond places marks:
/// - Above staff (direction = UP) for most marks
/// - Below staff (direction = DOWN) for expression marks (rit., accel., etc.)
/// - At beginning of measure for segno/coda
/// - At end of measure for fine/D.S./D.C.
///
/// When multiple marks appear at the same position, they are stacked using
/// outside-staff-priority: lower priority marks are placed closer to the staff,
/// higher priority marks are placed farther away.
/// </remarks>
public static class MusicMarkEngraver
{
    // LILYPOND-REF: define-grobs.scm:3665 padding = 0.5
    private const double Padding = 0.5;

    /// <summary>
    /// Baseline for below-staff marks (pedal text etc.): the system's last
    /// staff bottom + 1.5sp + padding. The pedal BRACKET LINE runs on this
    /// same baseline so "Ped." text, line and the release "*" align in the
    /// classic Ped.____* shape.
    /// </summary>
    public static double BelowMarkBaseline(double systemBottom)
        => systemBottom + 1.5 + Padding;

    // Y offset above staff for marks (when no volta brackets present)
    // A mark and a chord symbol share a column when their centres are within
    // this X window (label box half-width ~3 ss + chord half-width ~1 ss).
    private const double MarkChordXWindow = 4.0;

    // Cap-height ascent of the chord-name text above its baseline
    // (chord font = 4.0 * 0.65 = 2.6 ss; cap height ≈ 0.72 em).
    private const double ChordTextAscent = 1.9;

    // LILYPOND-REF: define-grobs.scm RehearsalMark padding=0.8
    private const double AboveStaffOffset = -2.0;

    // LILYPOND-REF: axis-group-interface.cc:50 default_outside_staff_padding_ = 0.46
    private const double OutsideStaffPadding = 0.46;

    // Extra drop for D.S./D.C. jump instructions so they sit clear of low notes.
    private const double JumpInstructionDrop = 1.5;

    /// <summary>A jump-FROM instruction (D.S./D.C. family) — placed below the staff.</summary>
    private static bool IsJumpInstruction(MusicMarkType type) =>
        type is MusicMarkType.DalSegno or MusicMarkType.DaCapo
             or MusicMarkType.DalSegnoAlFine or MusicMarkType.DalSegnoAlCoda
             or MusicMarkType.DaCapoAlFine or MusicMarkType.DaCapoAlCoda;

    // Gap between stacked marks
    // LILYPOND-REF: axis-group-interface.cc:50 default_outside_staff_padding_ = 0.46
    private const double StackGap = 0.46;

    /// <summary>
    /// Calculates layout for all music marks in a score, including section labels.
    /// Section labels from measures are merged with explicit music marks and
    /// stacked using outside-staff-priority when they overlap.
    /// </summary>
    public static ImmutableArray<MusicMarkLayout> Calculate(
        Score? score,
        ImmutableArray<MusicMarkItem> musicMarks,
        ImmutableArray<SystemLayout> systems,
        ImmutableArray<MeasureLayout> measureLayouts,
        ImmutableArray<Measure> measures = default,
        ImmutableArray<VoltaBracketLayout> voltaBrackets = default,
        ImmutableArray<ChordNameLayout> chordNames = default)
    {
        // Merge section labels and tempo marking into the mark list
        var allMarks = BuildAllMarks(musicMarks, measures, score?.Tempo, score?.SwingSubdivision ?? 0);

        if (allMarks.Length == 0)
            return ImmutableArray<MusicMarkLayout>.Empty;

        // Calculate X positions and group marks that need stacking.
        // F3/B: each entry carries its index into allMarks (SourceIndex) so the
        // emitted layout can re-derive its data-pos from the live score later,
        // even though GroupBy/OrderBy below reorders the entries.
        var markEntries = new List<(MusicMarkItem Mark, double X, int SourceIndex)>();
        for (int si = 0; si < allMarks.Length; si++)
        {
            var mark = allMarks[si];
            if (mark.MeasureIndex >= measureLayouts.Length)
                continue;

            var measureLayout = measureLayouts[mark.MeasureIndex];
            double x = CalculateXPosition(mark, measureLayout, systems);
            markEntries.Add((mark, x, si));
        }

        // LILYPOND-REF: axis-group-interface.cc:865-984 skyline_spacing
        // Group by (MeasureIndex, Position) for collision stacking.
        // Marks at the same measure+position are sorted by outside-staff-priority
        // and stacked outward from the staff.

        // Build volta bracket coverage: measure indices that have a volta bracket above
        // LILYPOND-REF: define-grobs.scm:3943 VoltaBracketSpanner outside-staff-priority=600
        var voltaMeasures = new HashSet<int>();
        double voltaTopY = 0;
        if (!voltaBrackets.IsDefaultOrEmpty)
        {
            foreach (var vb in voltaBrackets)
            {
                for (int mi = vb.StartMeasureIndex; mi <= vb.EndMeasureIndex; mi++)
                    voltaMeasures.Add(mi);
                // Track the highest (most negative) volta Y
                if (vb.Y < voltaTopY)
                    voltaTopY = vb.Y;
            }
        }

        // Group by measure + position + ANCHOR TIMING so only marks that share a
        // horizontal column stack vertically. Without the timing, a mid-measure
        // tempo (e.g. \tempo at beat 2) stacked onto the line-start tempo/section
        // label even though they sit at different X, stealing the bottom slot and
        // floating the opening marks far above the staff (grand-staff regression).
        var groups = markEntries
            .GroupBy(e => (e.Mark.MeasureIndex, e.Mark.Position, e.Mark.AnchorTiming))
            .ToList();

        // BELOW-staff marks (pedal text etc.) hang under the LAST staff of
        // the measure's system, not under the top staff — in a grand staff
        // the old top-staff constant dropped "Ped." between the staves,
        // straight into the rh's low ledger notes.
        // LILYPOND-REF: ly/engraver-init.ly — Piano_pedal_engraver lives in
        //   PianoStaff/GrandStaff context: pedal grobs attach below the
        //   whole staff group.
        var measureToSystemBottom = new Dictionary<int, double>();
        foreach (var system in systems)
        {
            double bottom = 4.0;
            if (!system.StaffGroups.IsDefaultOrEmpty)
            {
                foreach (var g in system.StaffGroups)
                    foreach (var st in g.Staves)
                        if (!st.IsHidden)
                            bottom = Math.Max(bottom, st.Y + st.Height);
            }
            foreach (var ml in system.Measures)
                measureToSystemBottom[ml.MeasureIndex] = bottom;
        }

        var layouts = ImmutableArray.CreateBuilder<MusicMarkLayout>();

        foreach (var group in groups)
        {
            // Separate above-staff and below-staff marks
            var aboveMarks = group
                .Where(e => e.Mark.Vertical == MusicMarkVertical.Above)
                .OrderBy(e => GetOutsideStaffPriority(e.Mark.Type))
                .ToList();

            var belowMarks = group
                .Where(e => e.Mark.Vertical == MusicMarkVertical.Below)
                .OrderBy(e => GetOutsideStaffPriority(e.Mark.Type))
                .ToList();

            // Check if any mark in this group overlaps with a volta bracket
            bool hasVoltaOverlap = aboveMarks.Any(e => voltaMeasures.Contains(e.Mark.MeasureIndex));

            // LILYPOND-REF: axis-group-interface.cc:652-681 avoid_outside_staff_collisions
            // Marks with priority > 600 (VoltaBracketSpanner) must be placed above volta.
            // Base Y for above-staff stacking: if volta present, start above volta top.
            double baseAboveY = AboveStaffOffset;
            if (hasVoltaOverlap)
            {
                // Place marks above volta bracket with outside-staff padding
                baseAboveY = voltaTopY - OutsideStaffPadding;
            }

            // Chord symbols are the line CLOSEST to the staff (LP: the marks'
            // outside-staff priority 1500 beats ChordNames), so a mark sharing
            // its column with a chord symbol starts above that symbol's text —
            // otherwise a section label box prints straight over the chord.
            // Only inline top-staff chords have negative Y here; chord ROWS and
            // lower staves don't share the marks' band.
            double markCeiling = double.PositiveInfinity; // constraint on box BOTTOM
            if (!chordNames.IsDefaultOrEmpty && aboveMarks.Count > 0)
            {
                double gx = aboveMarks[0].X;
                foreach (var cn in chordNames)
                {
                    if (cn.Y >= 0 || Math.Abs(cn.X - gx) > MarkChordXWindow)
                        continue;
                    double chordTop = cn.Y - ChordTextAscent;
                    markCeiling = Math.Min(markCeiling, chordTop - OutsideStaffPadding);
                }
            }

            // Stack above-staff marks (lower priority = closer to staff)
            double stackTopY = baseAboveY;
            for (int i = 0; i < aboveMarks.Count; i++)
            {
                var (mark, x, si) = aboveMarks[i];
                double halfExtent = GetMarkHalfExtent(mark.Type);

                double y;
                if (i == 0)
                {
                    y = baseAboveY - Padding;
                    if (!double.IsPositiveInfinity(markCeiling))
                        y = Math.Min(y, markCeiling - halfExtent); // box bottom clears the chord
                    stackTopY = y - halfExtent;
                }
                else
                {
                    // Subsequent marks: stack above previous
                    y = stackTopY - StackGap - halfExtent;
                    stackTopY = y - halfExtent;
                }

                layouts.Add(new MusicMarkLayout(
                    mark.MeasureIndex, x, y, mark.Type, mark.Text,
                    mark.IsSymbol, mark.SourcePosition, si, mark.SwingSubdivision));
            }

            // Stack below-staff marks (lower priority = closer to staff).
            // Base = the system's LAST staff bottom + 1.5 (equals the old
            // 5.5 constant for a single 4sp staff — multi-staff changes only).
            double belowBase = BelowMarkBaseline(4.0) - Padding;
            if (belowMarks.Count > 0
                && measureToSystemBottom.TryGetValue(belowMarks[0].Mark.MeasureIndex, out double sysBottom))
            {
                belowBase = BelowMarkBaseline(sysBottom) - Padding;
            }
            // Pedal CHANGES put the previous release "*" and the next
            // "Ped." in the same group; classic notation writes them SIDE BY
            // SIDE on the one pedal baseline ("* Ped."), never stacked.
            // Releases sharing a group with an on-mark shift left of it.
            bool IsPedalRelease(MusicMarkType t) =>
                t is MusicMarkType.SustainOff or MusicMarkType.SostenutoOff
                  or MusicMarkType.UnaCordaOff;
            bool IsPedal(MusicMarkType t) =>
                t is MusicMarkType.SustainOn or MusicMarkType.SostenutoOn
                  or MusicMarkType.UnaCordaOn || IsPedalRelease(t);
            bool groupHasPedalChange =
                belowMarks.Any(e => IsPedalRelease(e.Mark.Type))
                && belowMarks.Any(e => IsPedal(e.Mark.Type) && !IsPedalRelease(e.Mark.Type));

            double stackBottomY = belowBase;
            bool firstStacked = true;
            for (int i = 0; i < belowMarks.Count; i++)
            {
                var (mark, x, si) = belowMarks[i];
                double halfExtent = GetMarkHalfExtent(mark.Type);

                double y;
                if (IsPedal(mark.Type))
                {
                    // All pedal text shares the pedal baseline.
                    y = belowBase + Padding;
                    if (groupHasPedalChange && IsPedalRelease(mark.Type))
                    {
                        // "*" just left of the new "Ped." — both centered
                        // texts, so clear half of each measured width + gap.
                        double pedHalf = Rendering.SerifTextMetrics.MeasureBold("Ped.", 2.8) / 2;
                        double starHalf = Rendering.SerifTextMetrics.MeasureBold(mark.Text, 2.8) / 2;
                        x -= pedHalf + starHalf + 0.4;
                    }
                }
                else if (firstStacked)
                {
                    y = belowBase + Padding;
                    stackBottomY = y + halfExtent;
                    firstStacked = false;
                }
                else
                {
                    y = stackBottomY + StackGap + halfExtent;
                    stackBottomY = y + halfExtent;
                }

                // Jump-from instructions (D.S./D.C.) hang a little lower than the
                // pedal baseline so they clear low notes under the staff.
                if (IsJumpInstruction(mark.Type))
                {
                    y += JumpInstructionDrop;
                    stackBottomY = Math.Max(stackBottomY, y + halfExtent);
                }

                layouts.Add(new MusicMarkLayout(
                    mark.MeasureIndex, x, y, mark.Type, mark.Text,
                    mark.IsSymbol, mark.SourcePosition, si, mark.SwingSubdivision));
            }
        }

        return layouts.ToImmutable();
    }

    /// <summary>
    /// Co-places a boundary "To Coda" with the section label it shares a barline
    /// with: lifts the sign to the label's (post-stacking) line and tucks it a
    /// fixed gap to the LEFT of the label, so the two sit side by side instead of
    /// colliding. The sign keeps its own measure — it stays logically at the end
    /// of the previous section (left of the barline), the label stays at the start
    /// of the next; only the outside-staff LINE is shared (the stacker raises the
    /// label, so the sign just adopts that height). Matched by X proximity, which
    /// also means a label on the NEXT system (far X) is never matched. Run AFTER
    /// outside-staff stacking so the label's line is final.
    /// </summary>
    public static ImmutableArray<MusicMarkLayout> CoPlaceToCodaWithLabels(
        ImmutableArray<MusicMarkLayout> marks)
    {
        if (marks.IsDefaultOrEmpty || !marks.Any(m => m.MarkType == MusicMarkType.ToCoda))
            return marks;

        var result = marks.ToBuilder();
        var labelIdx = new List<int>();
        for (int i = 0; i < result.Count; i++)
            if (result[i].MarkType is MusicMarkType.SectionLabel or MusicMarkType.Rehearsal)
                labelIdx.Add(i);
        if (labelIdx.Count == 0)
            return marks;

        // The common rehearsal line: the labels closest to the staff (largest Y),
        // i.e. those NOT raised to clear something. An un-raised label sits at the
        // same staff-relative Y in every system, so this is system-agnostic.
        double commonY = labelIdx.Max(j => result[j].Y);

        for (int i = 0; i < result.Count; i++)
        {
            if (result[i].MarkType != MusicMarkType.ToCoda)
                continue;
            var tc = result[i];
            int bestJ = -1;
            double bestDx = double.MaxValue;
            foreach (int j in labelIdx)
            {
                double dx = result[j].X - tc.X;
                if (dx >= -1.0 && dx < 5.0 && dx < bestDx) { bestDx = dx; bestJ = j; }
            }
            if (bestJ >= 0)
            {
                // The label was raised only to clear this sign. With the sign
                // moving beside it, drop the label back to the common line, then
                // sit the sign just to its left and LOW enough that its baseline
                // meets the label box's bottom edge (the box extends half its
                // height below the shared centre line).
                var lab = result[bestJ] with { Y = commonY };
                result[bestJ] = lab;
                double labelFs = lab.MarkType == MusicMarkType.Rehearsal ? 4.0 * 0.6 : 4.0 * 0.55;
                double boxHalf = (labelFs + 2 * 0.2) / 2; // box height = fs + 2*pad(0.2)
                result[i] = tc with { Y = commonY + boxHalf, X = lab.X - ToCodaLabelGap };
            }
        }
        return result.ToImmutable();
    }

    // Centre-to-centre gap between a boundary "To Coda" and the section label it
    // shares the rehearsal line with, so the sign sits clear to the label's left.
    private const double ToCodaLabelGap = 4.0;

    /// <summary>
    /// Builds the full, ordered mark list (explicit marks + section labels +
    /// initial tempo) that <see cref="Calculate"/> lays out. Public and pure so
    /// the renderer can reconstruct the SAME list from the live score and resolve
    /// each layout's data-pos by its <c>SourceIndex</c> (F3/B whole-layout reuse).
    /// The merge order is content-invariant, so the index is stable across edits
    /// that don't change content.
    /// </summary>
    public static ImmutableArray<MusicMarkItem> BuildAllMarks(
        ImmutableArray<MusicMarkItem> musicMarks,
        ImmutableArray<Measure> measures,
        int? tempo,
        int swingSubdivision = 0)
    {
        var allMarks = MergeSectionLabels(musicMarks, measures);
        return MergeTempoMark(allMarks, tempo, swingSubdivision);
    }

    /// <summary>
    /// Merges section labels from measures into the music marks list.
    /// Section labels become MusicMarkType.SectionLabel entries.
    /// </summary>
    private static ImmutableArray<MusicMarkItem> MergeSectionLabels(
        ImmutableArray<MusicMarkItem> musicMarks,
        ImmutableArray<Measure> measures)
    {
        if (measures.IsDefaultOrEmpty)
            return musicMarks.IsDefaultOrEmpty ? ImmutableArray<MusicMarkItem>.Empty : musicMarks;

        // Collect section labels from measures
        var sectionLabels = new List<MusicMarkItem>();
        for (int i = 0; i < measures.Length; i++)
        {
            var measure = measures[i];
            if (measure.SectionLabel != null)
            {
                // Prefer the `section X` declaration offset so a click jumps there;
                // fall back to the measure's music start when it wasn't threaded.
                int pos = measure.SectionLabelPosition > 0
                    ? measure.SectionLabelPosition
                    : measure.SourceStart;
                sectionLabels.Add(new MusicMarkItem(
                    MusicMarkType.SectionLabel, measure.SectionLabel, i, pos));
            }
        }

        if (sectionLabels.Count == 0)
            return musicMarks.IsDefaultOrEmpty ? ImmutableArray<MusicMarkItem>.Empty : musicMarks;

        // Merge: existing marks + section labels
        var builder = ImmutableArray.CreateBuilder<MusicMarkItem>();
        if (!musicMarks.IsDefaultOrEmpty)
            builder.AddRange(musicMarks);
        builder.AddRange(sectionLabels);
        return builder.ToImmutable();
    }

    /// <summary>
    /// Adds a tempo marking to the mark list if the score has a tempo.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: define-grobs.scm:1835 MetronomeMark outside-staff-priority = 1000
    /// </remarks>
    private static ImmutableArray<MusicMarkItem> MergeTempoMark(
        ImmutableArray<MusicMarkItem> marks, int? tempo, int swingSubdivision = 0)
    {
        if (tempo == null)
            return marks;

        var tempoMark = new MusicMarkItem(
            MusicMarkType.Tempo, tempo.Value.ToString(), 0, 0) { SwingSubdivision = swingSubdivision };

        var builder = ImmutableArray.CreateBuilder<MusicMarkItem>();
        if (!marks.IsDefaultOrEmpty)
        {
            // The INITIAL tempo is drawn from Score.Tempo (the mark added below).
            // A top-level or part-header `tempo` ALSO gets injected into the music
            // stream as a metronome mark at the opening moment (MeasureCollector),
            // so without this filter the same starting tempo prints two or three
            // times — and the stream copies, anchored to a note column rather than
            // the line start, float at the wrong height. Drop those redundant
            // opening-moment stream tempos (MeasureIndex 0, zero elapsed time,
            // AnchorItemIndex >= 0 = stream-sourced). A genuine mid-piece change
            // (AnchorTiming numerator != 0, or a later measure) is kept; an
            // in-music tempo with no Score.Tempo never reaches this branch.
            foreach (var m in marks)
            {
                bool redundantOpeningTempo = m.Type == MusicMarkType.Tempo
                    && m.MeasureIndex == 0
                    && m.AnchorItemIndex >= 0
                    && m.AnchorTiming.Numerator == 0;
                if (!redundantOpeningTempo)
                    builder.Add(m);
            }
        }
        builder.Add(tempoMark);
        return builder.ToImmutable();
    }

    /// <summary>
    /// Gets the outside-staff-priority for a mark type.
    /// Lower values are placed closer to the staff.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: define-grobs.scm
    /// - SectionLabel: outside-staff-priority = 1450
    /// - RehearsalMark: outside-staff-priority = 1500
    /// - SegnoMark/CodaMark: outside-staff-priority = 1500
    /// </remarks>
    private static int GetOutsideStaffPriority(MusicMarkType type) => type switch
    {
        // LILYPOND-REF: define-grobs.scm:1835 MetronomeMark outside-staff-priority = 1000
        MusicMarkType.Tempo => 1000,
        MusicMarkType.SectionLabel => 1450,
        MusicMarkType.Rehearsal => 1500,
        MusicMarkType.Segno => 1500,
        MusicMarkType.Coda => 1500,
        _ => 1500
    };

    /// <summary>
    /// Gets the approximate half-height of a mark's visual extent in staff spaces.
    /// Used for collision avoidance stacking between marks.
    /// </summary>
    /// <remarks>
    /// These values match the rendering sizes in SvgRenderer:
    /// - Boxed marks (Rehearsal/SectionLabel): (fontSize + boxPadding*2) / 2
    ///   where boxPadding = 0.2 (LILYPOND-REF: define-markup-commands.scm)
    /// - Symbol marks (Segno/Coda): symbol glyph height / 2
    /// - Text marks (D.S./Fine/etc.): fontSize / 2
    /// </remarks>
    private static double GetMarkHalfExtent(MusicMarkType type) => type switch
    {
        // LILYPOND-REF: define-grobs.scm:1835 MetronomeMark — notehead + stem + text
        MusicMarkType.Tempo => 1.8,           // notehead height + stem extends ~1.4 above
        MusicMarkType.Rehearsal => 1.4,       // (FontSize*0.6 + 0.2*2) / 2 = (2.4+0.4)/2
        MusicMarkType.SectionLabel => 1.3,    // (FontSize*0.55 + 0.2*2) / 2 = (2.2+0.4)/2
        MusicMarkType.Segno or MusicMarkType.Coda => 2.0,
        _ => 1.0
    };

    /// <summary>
    /// Calculates X position for a mark.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: mark-engraver.cc:75-80 break-align-symbol
    /// - Beginning marks (segno, coda): align to start of measure
    /// - End marks (fine, D.S., D.C.): align to end of measure
    /// </remarks>
    /// <summary>
    /// X anchor for a mark. Marks break-align: mid-line they anchor on the
    /// measure's start barline; at a line start (no visible barline) the
    /// anchor falls back to the key signature / clef — i.e. the start of the
    /// system's prefix — NOT the first note.
    /// Boxed labels (Rehearsal / SectionLabel) align their LEFT edge on the
    /// anchor; the returned X is the box CENTER (the renderer draws
    /// middle-anchored), so half the box width is added.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm SectionLabel —
    ///   (self-alignment-X . LEFT),
    ///   X-offset self-alignment-interface::self-aligned-on-breakable.
    /// LILYPOND-REF: scm/define-grobs.scm RehearsalMark —
    ///   break-align-symbols (staff-bar key-signature clef).
    /// </remarks>
    private static double CalculateXPosition(
        MusicMarkItem mark, MeasureLayout measureLayout,
        ImmutableArray<SystemLayout> systems)
    {
        if (mark.Position == MusicMarkPosition.End)
            return measureLayout.X + measureLayout.Width - 0.5; // Before end barline

        // A mid-measure tempo change attaches to the musical column of the note
        // that follows it (LilyPond's MetronomeMark moment), not the measure's
        // break-align prefix. Index 0 (first note) stays a measure-start tempo
        // and falls through to the break-align logic below.
        // LILYPOND-REF: metronome-engraver.cc — mark attached at its moment.
        if (mark.Type == MusicMarkType.Tempo && mark.AnchorItemIndex > 0)
        {
            // Resolve the note column the mark sits over. On a grand staff the
            // staves share timing columns, but each voice indexes its OWN notes,
            // so the authoring voice's item index would pick the wrong staff's
            // note (independent rhythms). Prefer the shared timing columns there;
            // fall back to the item index on a single staff (no columns).
            //
            // LilyPond aligns the metronome notehead with the following note's
            // head (its " = NNN" text then trails to the right of that note).
            // The timing column X already lands on the drawn note glyph; the
            // single-staff item X is the slot reference, ~0.7 ss right of the
            // glyph, so back that path off to match.
            // LILYPOND-REF verified: \tempo 4 = N mid-measure puts the mark's
            // notehead at the same X as the note that follows it.
            if (!measureLayout.Columns.IsDefaultOrEmpty)
                return measureLayout.X + measureLayout.GetXForTiming(mark.AnchorTiming);
            if (mark.AnchorItemIndex < measureLayout.Items.Length)
                return measureLayout.X + measureLayout.Items[mark.AnchorItemIndex].X - 0.70;
        }

        // Pedal marks ("Ped." / "*") attach to the note they are written on
        // (like the metronome mark), so they anchor at that note's column rather
        // than the measure/line start. LILYPOND-REF: piano-pedal-engraver.cc.
        if (mark.AnchorItemIndex >= 0 &&
            mark.Type is MusicMarkType.SustainOn or MusicMarkType.SustainOff
                or MusicMarkType.SostenutoOn or MusicMarkType.SostenutoOff
                or MusicMarkType.UnaCordaOn or MusicMarkType.UnaCordaOff)
        {
            if (!measureLayout.Columns.IsDefaultOrEmpty)
                return measureLayout.X + measureLayout.GetXForTiming(mark.AnchorTiming);
            if (mark.AnchorItemIndex < measureLayout.Items.Length)
                return measureLayout.X + measureLayout.Items[mark.AnchorItemIndex].X;
        }

        if (mark.Position != MusicMarkPosition.Beginning)
            return measureLayout.X + measureLayout.Width / 2; // Center (fallback)

        // Break-align anchor: at a line start the barline is invisible, so
        // the anchor falls back to the start of the prefix (clef/key).
        double anchor = measureLayout.X;
        foreach (var system in systems)
        {
            if (!system.Measures.IsDefaultOrEmpty
                && system.Measures[0].MeasureIndex == measureLayout.MeasureIndex)
            {
                anchor = system.Indent + 0.3;
                break;
            }
        }

        if (mark.Type is MusicMarkType.Rehearsal or MusicMarkType.SectionLabel)
        {
            // LEFT edge on the anchor: returned X is the box center.
            double fs = mark.Type == MusicMarkType.Rehearsal ? 4.0 * 0.6 : 4.0 * 0.55;
            double boxWidth = Rendering.SerifTextMetrics.MeasureBold(mark.Text, fs) + 0.4;
            return anchor + boxWidth / 2;
        }

        // Segno/Coda glyphs have a symmetric bbox (origin = horizontal centre), so
        // returning the barline anchor centres the sign on the barline.
        if (mark.Type is MusicMarkType.Segno or MusicMarkType.Coda)
            return anchor;

        return anchor + 0.5;
    }
}
