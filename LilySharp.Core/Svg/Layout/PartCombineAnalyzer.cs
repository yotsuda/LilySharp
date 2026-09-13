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
/// Layout for a part combination text annotation ("a2", "Solo", "Solo II").
/// </summary>
/// <param name="Text">Display text</param>
/// <param name="X">X position in staff spaces</param>
/// <param name="YUp">The BASELINE in the Y-up frame (frame B): staff-spaces ABOVE the
/// system top, up-positive. The renderer reflects it to device against the system top.
/// Zero until <see cref="OutsideStaffStacker"/> places the label.</param>
/// <param name="MeasureIndex">Measure index</param>
/// <param name="StaffIndex">The global index of the combined staff the label belongs to —
/// its Y-parent, whose tracker the outside-staff pass places it against.</param>
public sealed record PartCombineLayout(
    string Text,
    double X,
    double YUp,
    int MeasureIndex,
    int StaffIndex);

/// <summary>
/// Places the part combiner's labels. The ANALYSIS is not here — it belongs to the music
/// and runs in the collect phase (<see cref="Collector.PartCombiner"/>), which is where
/// LilyPond does it too; what is left for layout is where each label goes.
/// </summary>
internal static class PartCombineAnalyzer
{
    /// <summary>The label's em for THIS score: <see cref="EngravingDefaults.CombineTextFontSize"/>
    /// unless the score's <c>fonts { }</c> wrote a <c>step</c> or <c>size</c> for
    /// <c>partCombine</c> (or <c>marks</c>). The draw, the outside-staff pass and the page's
    /// extents all read this one call.</summary>
    internal static double LabelEm(Rendering.ScoreTextMetrics fonts)
        => fonts.Size(Rendering.TextRole.PartCombine, EngravingDefaults.CombineTextFontSize);

    /// <summary>The label's weight and slant: bold (CombineTextScript's font-series) unless
    /// the score wrote a style.</summary>
    internal static Rendering.FontStyle LabelStyle(Rendering.ScoreTextMetrics fonts)
        => fonts.Style(Rendering.TextRole.PartCombine, Rendering.FontStyle.Bold);

    /// <summary>
    /// Turns the marks a <c>combinedStaff</c> produced into placed labels.
    /// </summary>
    /// <param name="marks">The marks, each naming the item it belongs to.</param>
    /// <param name="measureLayouts">Measure layouts for X position lookup.</param>
    /// <param name="measures">Measures of the combined staff's first voice, which is the
    /// voice the marks index into.</param>
    /// <param name="staffIndex">The combined staff's global index.</param>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:1086-1092 CombineTextScript, side-position-interface:
    /// its <c>parent-alignment-X</c> and <c>self-alignment-X</c> are both <c>#f</c>, so
    /// <c>ly:self-alignment-interface::aligned-on-x-parent</c> contributes no offset and the
    /// label sits at its X parent's reference point. That parent is the note head the
    /// engraver acknowledged —
    /// LILYPOND-REF: lily/part-combine-engraver.cc:102-112 acknowledge_note_head —
    /// which is why the X here is the ITEM's, not the measure's.
    /// MEASURED (audit/lpreg/pcombine-lp.ly, dumped): each label's X equals its moment's
    /// first note head's X to the printed digit.
    /// </remarks>
    public static ImmutableArray<PartCombineLayout> Calculate(
        ImmutableArray<Collector.PartCombineMark> marks,
        ImmutableArray<MeasureLayout> measureLayouts,
        ImmutableArray<Measure> measures = default,
        int staffIndex = 0)
    {
        if (marks.IsDefaultOrEmpty)
            return ImmutableArray<PartCombineLayout>.Empty;

        var layouts = ImmutableArray.CreateBuilder<PartCombineLayout>();
        // The HEIGHT is not decided here: the label is an outside-staff grob (priority 475)
        // and OutsideStaffStacker places it against its own staff's ink, before the marks
        // (1500) that stack over it. Until session 378 this stood at a flat 1.5 above the
        // SYSTEM top, outside the pass — so on a combined staff under a chord row it drew
        // over the chord band, and a section label placed without seeing it drew through it
        // (user report, scratch/ベースタブLy/bench.lys).
        foreach (var mark in marks)
        {
            double x = 0;
            if (mark.MeasureIndex < measureLayouts.Length)
            {
                var ml = measureLayouts[mark.MeasureIndex];
                x = ml.X + LayoutUtilities.GetItemXOffset(
                    measures, mark.MeasureIndex, mark.ItemIndex, ml);
            }

            layouts.Add(new PartCombineLayout(mark.Text, x, 0, mark.MeasureIndex, staffIndex));
        }

        return layouts.ToImmutable();
    }

    /// <summary>
    /// THIS STAFF'S labels AS INK ABOVE THE STAFF, in the per-staff skyline's frame (origin =
    /// the staff's MIDDLE line, up-positive) — so that a line standing above the staff (a
    /// chord row) makes room for them.
    /// </summary>
    /// <remarks>
    /// The same move <c>TextSpannerEngraver.InkAboveStaff</c> makes for the rit. spanner, for
    /// the same reason: LilyPond leaves a placed outside-staff grob IN its VerticalAxisGroup's
    /// skyline, and that profile is what the loose lines above are distributed against.
    /// LILYPOND-REF: lily/axis-group-interface.cc:860-985 skyline_spacing;
    ///   lily/page-layout-problem.cc:936-939 distribute_loose_lines.
    /// MEASURED (LilyPond 2.26.0, scratch/p378/a2/va-label-combined.ly): over a combined staff
    /// the ChordNames line stands above the "a2", 3.65 over the staff top against the label's
    /// 1.53. Without this the row was spaced for the notes alone and its first symbol printed
    /// over the label once the label stood where LilyPond puts it.
    /// The two terms are <c>OutsideStaffStacker.PlacePartCombineTexts</c>'s — the staff-padding
    /// floor and outside-staff-padding over the accumulated profile — spelt here because this
    /// pass runs before the systems exist. ⚠️ The profile is the label's ink BOX over its
    /// advance, where the stacker places its outline: the harmless direction (a box is never
    /// lower than the outline under it).
    /// </remarks>
    internal static VerticalSkyline InkAboveStaff(
        Rendering.ScoreTextMetrics fonts,
        ImmutableArray<Collector.PartCombineMark> marks,
        ImmutableArray<Measure> measures,
        ImmutableArray<MeasureLayout> systemMeasureLayouts,
        VerticalSkyline accumulatedUp)
    {
        var ink = new VerticalSkyline(VerticalDirection.Up);
        if (marks.IsDefaultOrEmpty || systemMeasureLayouts.IsDefaultOrEmpty)
            return ink;
        double em = LabelEm(fonts);
        var style = LabelStyle(fonts);
        double floor = EngravingDefaults.StaffMiddle + EngravingDefaults.StaffLineThickness / 2.0
            + CombineTextStaffPadding;
        foreach (var mark in marks)
        {
            MeasureLayout? ml = null;
            foreach (var m in systemMeasureLayouts)
                if (m.MeasureIndex == mark.MeasureIndex) { ml = m; break; }
            if (ml is null)
                continue;   // another system's label
            double x0 = ml.X + LayoutUtilities.GetItemXOffset(
                measures, mark.MeasureIndex, mark.ItemIndex, ml);
            double x1 = x0 + fonts.Advance(mark.Text, em, Rendering.TextRole.PartCombine, style);
            var (bottom, top) = fonts.Ink(mark.Text, em, Rendering.TextRole.PartCombine, style);
            double baseline = Math.Max(floor,
                accumulatedUp.MaxProtrusionInRange(x0, x1)
                    + OutsideStaffStacker.OutsideStaffPadding - bottom);
            ink.Merge(VerticalSkyline.FromBox(
                x0, x1, baseline + bottom, baseline + top, VerticalDirection.Up));
        }
        return ink;
    }

    /// <summary>CombineTextScript's staff-padding.</summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:1077-1094 CombineTextScript — beside its outside-staff-priority 475.</remarks>
    internal const double CombineTextStaffPadding = 0.5;
}
