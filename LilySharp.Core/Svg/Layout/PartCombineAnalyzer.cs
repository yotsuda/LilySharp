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
/// <param name="YUp">Y in the Y-up frame (frame B): staff-spaces ABOVE the system
/// top, up-positive. The renderer reflects it to device against the system top.</param>
/// <param name="MeasureIndex">Measure index</param>
public sealed record PartCombineLayout(
    string Text,
    double X,
    double YUp,
    int MeasureIndex);

/// <summary>
/// Places the part combiner's labels. The ANALYSIS is not here — it belongs to the music
/// and runs in the collect phase (<see cref="Collector.PartCombiner"/>), which is where
/// LilyPond does it too; what is left for layout is where each label goes.
/// </summary>
internal static class PartCombineAnalyzer
{
    /// <summary>
    /// Turns the marks a <c>combinedStaff</c> produced into placed labels.
    /// </summary>
    /// <param name="marks">The marks, each naming the item it belongs to.</param>
    /// <param name="measureLayouts">Measure layouts for X position lookup.</param>
    /// <param name="measures">Measures of the combined staff's first voice, which is the
    /// voice the marks index into.</param>
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
        ImmutableArray<Measure> measures = default)
    {
        if (marks.IsDefaultOrEmpty)
            return ImmutableArray<PartCombineLayout>.Empty;

        var layouts = ImmutableArray.CreateBuilder<PartCombineLayout>();
        // ⚠️ NOT PORTED — the outside-staff placement: a flat 1.5 staff-spaces above the
        // system top stands in for it (LP has the placement, so this is an unported piece
        // and not a Lily#-own quantity — §5.2 audit, session 158). LilyPond puts the
        // label on the outside-staff stacker at priority 475 with padding 0.5 and
        // staff-padding 0.5 (scm/define-grobs.scm:1084-1090), so its height follows the ink
        // underneath — MEASURED on pcombine-lp.ly, the three labels sit at Y-offset 2.583,
        // 3.033, 2.583, i.e. they are NOT all at one height. Nothing observes the difference
        // yet; it disappears when the label joins OutsideStaffStacker, which is where the
        // other 475-and-friends grobs already are.
        const double aboveStaffYUp = 1.5;

        foreach (var mark in marks)
        {
            double x = 0;
            if (mark.MeasureIndex < measureLayouts.Length)
            {
                var ml = measureLayouts[mark.MeasureIndex];
                x = ml.X + LayoutUtilities.GetItemXOffset(
                    measures, mark.MeasureIndex, mark.ItemIndex, ml);
            }

            layouts.Add(new PartCombineLayout(mark.Text, x, aboveStaffYUp, mark.MeasureIndex));
        }

        return layouts.ToImmutable();
    }
}
