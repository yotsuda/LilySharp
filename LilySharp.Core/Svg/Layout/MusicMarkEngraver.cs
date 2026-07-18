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
    int SwingSubdivision = 0, // Tempo marks only: note value to swing (0/8/16) for the feel equation.
    string? TempoText = null, // Tempo marks only: bold marking text ("Grave").
    int TempoBeatUnit = 4,    // Tempo marks only: metronome beat unit.
    int TempoDots = 0         // Tempo marks only: dots on the beat unit.
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
internal static class MusicMarkEngraver
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

    // `as both`: the Roman-degree row is drawn this far ABOVE the chord-text
    // baseline (renderer DrawChordNames stackLineHeight), so a two-row chord's
    // real top is that much higher. A mark clearing such a chord must clear the
    // UPPER row, or the tempo / section label overprints the degree line.
    private const double ChordStackLineHeight = 2.2;

    /// <summary>Cap-height ascent of a chord symbol above its baseline Y,
    /// including the stacked Roman-degree row when the chord is drawn `as both`.</summary>
    private static double ChordAscent(ChordNameLayout cn)
        => ChordTextAscent + (cn.AboveLine != null ? ChordStackLineHeight : 0);

    // LILYPOND-REF: define-grobs.scm RehearsalMark padding=0.8
    private const double AboveStaffOffset = -2.0;

    // LILYPOND-REF: axis-group-interface.cc:45 default_outside_staff_padding_ = 0.46
    private const double OutsideStaffPadding = 0.46;

    // Extra drop for D.S./D.C. jump instructions so they sit clear of low notes.
    private const double JumpInstructionDrop = 1.5;

    // A below-staff mark drops this far past the lowest lyric baseline (descent +
    // gap) so "D.C." / "Fine" clear the words rather than overprinting them.
    private const double LyricClearance = 2.0;

    /// <summary>A jump-FROM instruction (D.S./D.C. family) — placed below the staff.</summary>
    private static bool IsJumpInstruction(MusicMarkType type) =>
        type is MusicMarkType.DalSegno or MusicMarkType.DaCapo
             or MusicMarkType.DalSegnoAlFine or MusicMarkType.DalSegnoAlCoda
             or MusicMarkType.DaCapoAlFine or MusicMarkType.DaCapoAlCoda;

    // Gap between a section label box and the tempo mark sharing its line
    private const double TempoLabelGap = 0.8;

    // Gap between stacked marks
    // LILYPOND-REF: axis-group-interface.cc:45 default_outside_staff_padding_ = 0.46
    private const double StackGap = 0.46;

    /// <summary>
    /// Calculates layout for all music marks in a score, including section labels.
    /// Section labels from measures are merged with explicit music marks and
    /// stacked using outside-staff-priority when they overlap.
    /// </summary>
    /// <summary>
    /// Measure indices covered by an above-staff volta bracket, and the highest
    /// (most negative) volta Y across all brackets.
    /// </summary>
    /// <remarks>LILYPOND-REF: define-grobs.scm:4325 VoltaBracketSpanner outside-staff-priority=600</remarks>
    private static (HashSet<int> Measures, double TopY) BuildVoltaCoverage(
        ImmutableArray<VoltaBracketLayout> voltaBrackets)
    {
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
        return (voltaMeasures, voltaTopY);
    }

    public static ImmutableArray<MusicMarkLayout> Calculate(
        Score? score,
        ImmutableArray<MusicMarkItem> musicMarks,
        ImmutableArray<SystemLayout> systems,
        ImmutableArray<MeasureLayout> measureLayouts,
        ImmutableArray<Measure> measures = default,
        ImmutableArray<VoltaBracketLayout> voltaBrackets = default,
        ImmutableArray<ChordNameLayout> chordNames = default,
        ImmutableArray<LyricLayout> lyrics = default)
    {
        // Merge section labels and tempo marking into the mark list
        var allMarks = BuildAllMarks(musicMarks, measures, score?.Tempo, score?.SwingSubdivision ?? 0,
            score?.TempoText, score?.TempoBeatUnit ?? 4, score?.TempoDots ?? 0);

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

        // Build volta bracket coverage: measure indices that have a volta bracket above.
        var (voltaMeasures, voltaTopY) = BuildVoltaCoverage(voltaBrackets);

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
        // X coordinates are SYSTEM-LOCAL: any ink comparison (chords, lyrics)
        // must be restricted to the mark's own system, or bar-1 marks dodge
        // phantom chords from every later line.
        var measureToSystemIdx = new Dictionary<int, int>();
        for (int si2 = 0; si2 < systems.Length; si2++)
            foreach (var ml2 in systems[si2].Measures)
                measureToSystemIdx[ml2.MeasureIndex] = si2;
        bool SameSystem(int measureA, int measureB)
            => measureToSystemIdx.TryGetValue(measureA, out int a)
            && measureToSystemIdx.TryGetValue(measureB, out int b)
            && a == b;

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

        // Lowest lyric baseline per system — a below-staff mark (D.C./D.S./Fine)
        // must drop past the lyrics, which occupy the same band under the staff, or
        // it overprints them.
        var systemLyricBottom = new Dictionary<int, double>();
        if (!lyrics.IsDefaultOrEmpty)
            foreach (var ly in lyrics)
                if (measureToSystemIdx.TryGetValue(ly.Item.MeasureIndex, out int lySys)
                    && (!systemLyricBottom.TryGetValue(lySys, out double cur) || ly.Y > cur))
                    systemLyricBottom[lySys] = ly.Y;

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
            // outside-staff priority 1500 beats ChordNames), so a mark whose
            // INK overlaps a chord symbol starts above that symbol's text —
            // otherwise a section label box (or a wide tempo marking like
            // "Brightly (♩ = 108)") prints straight over the chord. The old
            // centre-distance window missed wide texts whose ink reaches a
            // chord several spaces away; LilyPond's outside-staff stacking is
            // extent-based, so compare real horizontal spans.
            // LILYPOND-REF: lily/axis-group-interface.cc:865-984 skyline_spacing.
            // Only inline top-staff chords have negative Y here; chord ROWS and
            // lower staves don't share the marks' band.
            double markCeiling = double.PositiveInfinity; // constraint on box BOTTOM
            if (!chordNames.IsDefaultOrEmpty && aboveMarks.Count > 0)
            {
                foreach (var e in aboveMarks)
                {
                    var (mx0, mx1) = MarkXExtent(e.Mark, e.X);
                    foreach (var cn in chordNames)
                    {
                        if (cn.Y >= 0 || !SameSystem(cn.MeasureIndex, e.Mark.MeasureIndex))
                            continue;
                        double chHalf = Rendering.SansTextMetrics.MeasureBold(cn.ChordText, 2.6) / 2 + 0.3;
                        if (mx1 < cn.X - chHalf || mx0 > cn.X + chHalf)
                            continue; // no horizontal ink overlap
                        double chordTop = cn.Y - ChordAscent(cn);
                        markCeiling = Math.Min(markCeiling, chordTop - OutsideStaffPadding);
                    }
                }
            }

            // A boxed label and the tempo mark at the SAME anchor start out
            // SIDE BY SIDE (label box, tempo just right of it) so the initial
            // stack is ONE level high; CoPlaceTempoWithLabels re-aligns the
            // pair after the outside-staff stacker has run.
            double stackTopY = baseAboveY;
            bool sideBySide = aboveMarks.Count == 2
                && aboveMarks[0].Mark.Type == MusicMarkType.Tempo
                && (aboveMarks[1].Mark.Type is MusicMarkType.SectionLabel or MusicMarkType.Rehearsal);
            if (sideBySide)
            {
                var (tempoMark, _, tempoSi) = aboveMarks[0];
                var (labelMark, labelX, labelSi) = aboveMarks[1];
                var (lx0, lx1) = MarkXExtent(labelMark, labelX);
                double tempoX = lx1 + TempoLabelGap;
                var (tx0, tx1) = MarkXExtent(tempoMark, tempoX);

                double ceiling = double.PositiveInfinity;
                if (!chordNames.IsDefaultOrEmpty)
                {
                    foreach (var cn in chordNames)
                    {
                        if (cn.Y >= 0 || !SameSystem(cn.MeasureIndex, labelMark.MeasureIndex))
                            continue;
                        double chHalf = Rendering.SansTextMetrics.MeasureBold(cn.ChordText, 2.6) / 2 + 0.3;
                        bool overLabel = !(lx1 < cn.X - chHalf || lx0 > cn.X + chHalf);
                        bool overTempo = !(tx1 < cn.X - chHalf || tx0 > cn.X + chHalf);
                        if (overLabel || overTempo)
                            ceiling = Math.Min(ceiling, cn.Y - ChordAscent(cn) - OutsideStaffPadding);
                    }
                }

                double halfLabel = GetMarkHalfExtent(labelMark.Type);
                double pairY = baseAboveY - Padding;
                if (!double.IsPositiveInfinity(ceiling))
                    pairY = Math.Min(pairY, ceiling - halfLabel);

                layouts.Add(new MusicMarkLayout(
                    labelMark.MeasureIndex, labelX, pairY, labelMark.Type, labelMark.Text,
                    labelMark.IsSymbol, labelMark.SourcePosition, labelSi));
                layouts.Add(new MusicMarkLayout(
                    tempoMark.MeasureIndex, tempoX, pairY + 0.9, tempoMark.Type, tempoMark.Text,
                    tempoMark.IsSymbol, tempoMark.SourcePosition, tempoSi, tempoMark.SwingSubdivision,
                    tempoMark.TempoText, tempoMark.TempoBeatUnit, tempoMark.TempoDots));
                stackTopY = pairY - halfLabel;
                aboveMarks.Clear();
            }

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
                    mark.IsSymbol, mark.SourcePosition, si, mark.SwingSubdivision,
                    mark.TempoText, mark.TempoBeatUnit, mark.TempoDots));
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
            // A jump/other below-staff mark must drop past the lyric line it shares
            // the band with (LyricClearance). Pedal text is EXEMPT — classic notation
            // keeps "Ped."/"*" on its staff-relative baseline, never pushed under the
            // words — so the floor lifts only the non-pedal stacking base, NOT
            // `belowBase`, which the pedal branch below reads directly.
            double stackBase = belowBase;
            if (belowMarks.Count > 0
                && measureToSystemIdx.TryGetValue(belowMarks[0].Mark.MeasureIndex, out int belowSys)
                && systemLyricBottom.TryGetValue(belowSys, out double lyricY))
            {
                stackBase = Math.Max(belowBase, lyricY + LyricClearance);
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
                    y = stackBase + Padding;
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

                // Below-staff text must clear the LYRIC lines: lyrics hang under
                // the staff before any below-mark, and "D.S. al Coda" printed
                // straight through the words. Drop the mark under the deepest
                // syllable whose ink overlaps it horizontally (0.9 = descent of
                // the 3.2 ss lyric face, as in the spacing extents).
                if (!lyrics.IsDefaultOrEmpty && !IsPedal(mark.Type))
                {
                    var (mx0, mx1) = MarkXExtent(mark, x);
                    foreach (var ly in lyrics)
                    {
                        if (!SameSystem(ly.Item.MeasureIndex, mark.MeasureIndex))
                            continue;
                        double lyHalf = ly.Width / 2 + 0.3;
                        if (mx1 < ly.X - lyHalf || mx0 > ly.X + lyHalf)
                            continue;
                        double lyricBottom = ly.Y + 0.9;
                        if (y - halfExtent < lyricBottom + OutsideStaffPadding)
                            y = lyricBottom + OutsideStaffPadding + halfExtent;
                    }
                    stackBottomY = Math.Max(stackBottomY, y + halfExtent);
                }

                layouts.Add(new MusicMarkLayout(
                    mark.MeasureIndex, x, y, mark.Type, mark.Text,
                    mark.IsSymbol, mark.SourcePosition, si, mark.SwingSubdivision,
                    mark.TempoText, mark.TempoBeatUnit, mark.TempoDots));
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
    /// <summary>
    /// After stacking, a tempo mark that shares its anchor with a boxed
    /// section/rehearsal label moves to the label's RIGHT on one shared line
    /// ("[Chorus] ♩ = 132" — the standard chart layout) instead of occupying
    /// a second line above it. The shared line is the higher of the two
    /// stacked positions, so every collision constraint the stacker resolved
    /// (chord symbols under either ink) stays respected.
    /// </summary>
    public static ImmutableArray<MusicMarkLayout> CoPlaceTempoWithLabels(
        ImmutableArray<MusicMarkLayout> marks,
        ImmutableArray<ChordNameLayout> chordNames = default,
        ImmutableArray<SystemLayout> systems = default)
    {
        if (marks.IsDefaultOrEmpty || !marks.Any(m => m.MarkType == MusicMarkType.Tempo))
            return marks;

        // X is system-local: only chords on the pair's OWN system constrain it.
        var measureToSystemIdx = new Dictionary<int, int>();
        if (!systems.IsDefaultOrEmpty)
            for (int si = 0; si < systems.Length; si++)
                foreach (var ml in systems[si].Measures)
                    measureToSystemIdx[ml.MeasureIndex] = si;

        var result = marks.ToBuilder();
        // Pairs already re-aligned in this pass: a later pair whose span runs
        // under an earlier one (adjacent one-bar sections with long tempo
        // texts) must stack ABOVE it — each pair alone only solves against
        // the chord symbols.
        var placedPairs = new List<(int Sys, double X0, double X1, double LineY)>();
        for (int i = 0; i < result.Count; i++)
        {
            if (result[i].MarkType != MusicMarkType.Tempo)
                continue;
            var tempo = result[i];
            int bestJ = -1;
            double bestDx = double.MaxValue;
            for (int j = 0; j < result.Count; j++)
            {
                if (result[j].MarkType is not (MusicMarkType.SectionLabel or MusicMarkType.Rehearsal))
                    continue;
                if (result[j].MeasureIndex != tempo.MeasureIndex)
                    continue;
                double dx = Math.Abs(result[j].X - tempo.X);
                if (dx < 8.0 && dx < bestDx) { bestDx = dx; bestJ = j; }
            }
            if (bestJ < 0)
            {
                // UNPAIRED tempo: it still shares the line with the pairs —
                // "♩=120" (paired with its label) and a following "♩=90"
                // overprinted on the grand-staff fixture. Same band model:
                // its "line" is the label-center height it would pair at.
                const double drop = 0.9; // = section-label fs/2 − pad
                double uw = 2.3 + Rendering.SerifTextMetrics.MeasureBold("= " + tempo.Text, 1.8);
                if (tempo.TempoText != null)
                    uw += Rendering.SerifTextMetrics.MeasureBold(tempo.TempoText, 2.2) + 1.5;
                if (tempo.SwingSubdivision != 0)
                    uw += 5.0;
                double uLine = tempo.Y - drop;
                int uSys = measureToSystemIdx.TryGetValue(tempo.MeasureIndex, out int us) ? us : -1;
                foreach (var placed in placedPairs)
                {
                    if (placed.Sys != uSys)
                        continue;
                    if (tempo.X + uw < placed.X0 - 1.0 || tempo.X > placed.X1 + 1.0)
                        continue;
                    if (Math.Abs(uLine - placed.LineY) < 3.0)
                        uLine = Math.Min(uLine, placed.LineY - 3.6);
                }
                placedPairs.Add((uSys, tempo.X, tempo.X + uw, uLine));
                result[i] = tempo with { Y = uLine + drop };
                continue;
            }

            var lab = result[bestJ];
            double labelFs = lab.MarkType == MusicMarkType.Rehearsal ? 4.0 * 0.6 : 4.0 * 0.55;
            const double pad = 0.2;
            double halfW = (Rendering.SerifTextMetrics.MeasureBold(lab.Text, labelFs) + 2 * pad) / 2;
            double baselineDrop = labelFs / 2 - pad;
            double boxHalf = labelFs / 2 + pad;

            // Start from the LOWER of the two stacked lines (closer to the
            // staff) and solve the SHIFTED pair against the chord symbols
            // directly — inheriting the higher line would drag the pair up by
            // whatever stacking step the other mark once needed.
            double tempoX = lab.X + halfW + TempoLabelGap;
            double tempoW = 2.3 + Rendering.SerifTextMetrics.MeasureBold("= " + tempo.Text, 1.8);
            if (tempo.TempoText != null)
                tempoW += Rendering.SerifTextMetrics.MeasureBold(tempo.TempoText, 2.2) + 1.5;
            if (tempo.SwingSubdivision != 0)
                tempoW += 5.0;

            double lineY = Math.Max(lab.Y, tempo.Y - baselineDrop);
            if (!chordNames.IsDefaultOrEmpty)
            {
                foreach (var cn in chordNames)
                {
                    if (cn.Y >= 0)
                        continue;
                    if (measureToSystemIdx.TryGetValue(cn.MeasureIndex, out int cs)
                        && measureToSystemIdx.TryGetValue(tempo.MeasureIndex, out int ts2)
                        && cs != ts2)
                        continue;
                    double chHalf = Rendering.SansTextMetrics.MeasureBold(cn.ChordText, 2.6) / 2 + 0.3;
                    double chordTop = cn.Y - ChordAscent(cn) - OutsideStaffPadding;
                    bool overTempo = !(tempoX + tempoW < cn.X - chHalf || tempoX > cn.X + chHalf);
                    bool overLabel = !(lab.X + halfW < cn.X - chHalf || lab.X - halfW > cn.X + chHalf);
                    // Tempo ink: stem to ~2.1 above the baseline, digits ~0.5 below.
                    if (overTempo)
                        lineY = Math.Min(lineY, chordTop - 0.5 - baselineDrop);
                    if (overLabel)
                        lineY = Math.Min(lineY, chordTop - boxHalf);
                }
            }

            double pairX0 = lab.X - halfW;
            double pairX1 = tempoX + tempoW;
            int tempoSys = measureToSystemIdx.TryGetValue(tempo.MeasureIndex, out int tsys) ? tsys : -1;
            foreach (var placed in placedPairs)
            {
                if (placed.Sys != tempoSys)
                    continue;
                if (pairX1 < placed.X0 - 1.0 || pairX0 > placed.X1 + 1.0)
                    continue;
                // Band of a pair ≈ ±1.7ss around its line; step a full band
                // plus padding above the one it would run into — but only
                // when the bands actually meet (an X-overlapping mark two
                // levels up leaves this line free).
                if (Math.Abs(lineY - placed.LineY) < 3.0)
                    lineY = Math.Min(lineY, placed.LineY - 3.6);
            }
            placedPairs.Add((tempoSys, pairX0, pairX1, lineY));

            result[bestJ] = lab with { Y = lineY };
            result[i] = tempo with
            {
                X = tempoX,
                Y = lineY + baselineDrop,
            };
        }
        return result.ToImmutable();
    }

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
        int swingSubdivision = 0,
        string? tempoText = null,
        int tempoBeatUnit = 4,
        int tempoDots = 0)
    {
        var allMarks = MergeSectionLabels(musicMarks, measures);
        return MergeTempoMark(allMarks, tempo, swingSubdivision, tempoText, tempoBeatUnit, tempoDots);
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

        // Collect a rehearsal-box mark for every measure that carries a section
        // label. Label VISIBILITY is the author's call, not the engraver's: a
        // section shows its name by being referenced by name (`form main { Body }`)
        // and hides it with the silent reference (`~Body`). So every non-null
        // SectionLabel engraves — no auto-suppression of "single distinct section"
        // or "repeated section" boxes. That heuristic fought the author's explicit
        // `~` control and silently hid deliberately named single sections.
        var sectionLabels = new List<MusicMarkItem>();
        for (int i = 0; i < measures.Length; i++)
        {
            var measure = measures[i];
            if (measure.SectionLabel == null)
                continue;
            // Prefer the `section X` declaration offset so a click jumps there;
            // fall back to the measure's music start when it wasn't threaded.
            int pos = measure.SectionLabelPosition > 0
                ? measure.SectionLabelPosition
                : measure.SourceStart;
            sectionLabels.Add(new MusicMarkItem(
                MusicMarkType.SectionLabel, measure.SectionLabel, i, pos));
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
    /// LILYPOND-REF: scm/define-grobs.scm:2346 MetronomeMark outside-staff-priority = 1300
    /// </remarks>
    private static ImmutableArray<MusicMarkItem> MergeTempoMark(
        ImmutableArray<MusicMarkItem> marks, int? tempo, int swingSubdivision = 0,
        string? tempoText = null, int tempoBeatUnit = 4, int tempoDots = 0)
    {
        // A textual marking without a BPM ("tempo \"Grave\"") still prints.
        if (tempo == null && tempoText == null)
            return marks;

        var tempoMark = new MusicMarkItem(
            MusicMarkType.Tempo, tempo?.ToString() ?? "", 0, 0)
        {
            SwingSubdivision = swingSubdivision,
            TempoText = tempoText,
            TempoBeatUnit = tempoBeatUnit,
            TempoDots = tempoDots,
        };

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
        // LILYPOND-REF: scm/define-grobs.scm:2346 MetronomeMark outside-staff-priority = 1300
        MusicMarkType.Tempo => 1300,
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
    /// <summary>
    /// The mark's approximate horizontal ink span. Tempo (left-anchored)
    /// extends right from its X; End-positioned jump text extends left;
    /// boxed labels and symbols are centred. Widths use the same faces the
    /// renderer draws with.
    /// </summary>
    private static (double x0, double x1) MarkXExtent(MusicMarkItem mark, double x)
    {
        switch (mark.Type)
        {
            case MusicMarkType.Tempo:
            {
                double w = 2.3 + Rendering.SerifTextMetrics.MeasureBold("= " + mark.Text, 1.8);
                if (mark.TempoText != null)
                    w += Rendering.SerifTextMetrics.MeasureBold(mark.TempoText, 2.2) + 1.5;
                if (mark.SwingSubdivision != 0)
                    w += 5.0;
                return (x, x + w);
            }
            case MusicMarkType.Rehearsal:
            case MusicMarkType.SectionLabel:
            {
                double fs = mark.Type == MusicMarkType.Rehearsal ? 2.4 : 2.2;
                double half = Rendering.SerifTextMetrics.MeasureBold(mark.Text, fs) / 2 + 0.2;
                return (x - half, x + half);
            }
            case MusicMarkType.Segno:
            case MusicMarkType.Coda:
                return (x - 1.2, x + 1.2);
            default:
            {
                double w = Rendering.SerifTextMetrics.MeasureBold(mark.Text, 2.2);
                return mark.Position == MusicMarkPosition.End
                    ? (x - w, x)
                    : (x - w / 2, x + w / 2);
            }
        }
    }

    private static double GetMarkHalfExtent(MusicMarkType type) => type switch
    {
        // LILYPOND-REF: define-grobs.scm:2335 MetronomeMark — notehead + stem + text
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
