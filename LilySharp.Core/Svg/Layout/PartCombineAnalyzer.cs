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
/// <param name="AlignedBaselineUp">The baseline <c>aligned_side</c> gives the label BEFORE the
/// outside-staff pass, in staff-spaces above the staff's MIDDLE line
/// (<see cref="PartCombineAnalyzer.AlignedSideBaselineUp"/>).</param>
public sealed record PartCombineLayout(
    string Text,
    double X,
    double YUp,
    int MeasureIndex,
    int StaffIndex,
    double AlignedBaselineUp);

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
    /// <param name="fonts">The score's text metrics: the label's advance and ink decide its
    /// aligned_side height (<see cref="AlignedSideBaselineUp"/>).</param>
    /// <param name="marks">The marks, each naming the item it belongs to.</param>
    /// <param name="measureLayouts">Measure layouts for X position lookup.</param>
    /// <param name="voices">The combined staff's voices. The marks index into the FIRST, and
    /// its column is the label's support.</param>
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
        Rendering.ScoreTextMetrics fonts,
        ImmutableArray<Collector.PartCombineMark> marks,
        ImmutableArray<MeasureLayout> measureLayouts,
        ImmutableArray<Voice> voices,
        IReadOnlyDictionary<(int Staff, int Voice, int Measure, int Item),
            (BeamLayout Beam, double StemX, bool StemUp)>? beamMembers,
        int staffIndex = 0)
    {
        var measures = voices.IsDefaultOrEmpty ? default : voices[0].Measures;
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

            layouts.Add(new PartCombineLayout(mark.Text, x, 0, mark.MeasureIndex, staffIndex,
                AlignedSideBaselineUp(fonts, voices, mark, x, beamMembers, staffIndex)));
        }

        return layouts.ToImmutable();
    }

    /// <summary>
    /// The label's baseline after <c>aligned_side</c> and BEFORE the outside-staff pass, in
    /// staff-spaces above the staff's MIDDLE line: its extent box kept <c>padding</c> 0.5 over
    /// the heads and stems of its moment, floored by the staff extent, then the staff-padding
    /// floor on the refpoint.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/side-position-interface.cc:188-455 aligned_side — :323-330 the staff
    ///   extent as the support's minimum, :354-358 the distance to the grob's own facing skyline,
    ///   :370 + padding, :433-453 the staff-padding floor on the refpoint.
    /// LILYPOND-REF: lily/part-combine-engraver.cc:102-119 acknowledge_note_head — the heads and
    ///   (acknowledge_stem) the stems of the moment are the supports; DynamicEngraver's
    ///   ColumnSupportSkylines builds exactly that pair (head extent box, the real stem, the
    ///   staff floor), so it is read here rather than spelt a second time.
    /// LILYPOND-REF: lily/grob.cc:81-85 Grob::simple_vertical_skylines_from_extents_proc — the facing skyline is
    ///   the extent box, since CombineTextScript declares no vertical-skylines
    ///   (scm/define-grobs.scm:1077-1105): the box's flat bottom is the string's ink bottom over
    ///   the whole advance, so the distance is the supports' highest point under the advance.
    /// A BEAMED stem enters at its drawn length, ending on the quanted beam face (the beam map
    ///   the dynamics read). MEASURED (scratch/p384/a2/E-beam, C3 eighths under one beam): LilyPond's
    ///   dumped Stem support reaches 3.05 over the staff middle, the label 1.583010 over the top
    ///   line; taken at the unbeamed 3.0 the label stood 0.033 low.
    /// The supports are ONE voice's column, and that is LilyPond's own model rather than a
    ///   narrowing: Part_combine_engraver is consisted in the Voice context, so it acknowledges only
    ///   the heads and stems of the voice the text is made in.
    /// LILYPOND-REF: ly/engraver-init.ly:406 Part_combine_engraver — inside \name Voice (:359).
    ///   The voice read is the right one because a label is only ever made for solo1 / solo2 /
    ///   unisono, whose notes PartCombiner routes to the Solo or Shared voice — always slot 0, the
    ///   voice the marks index into (slot 1 holds only voice Two, an apart passage, which prints no
    ///   text). Another voice's ink reaches the label through the outside-staff pass at 0.46.
    ///   Watched by ledger part-combine.text.own-voice-support (a voice-Two head inside the "Solo"
    ///   label's advance: all voices unioned would read 0.04 higher).
    /// </remarks>
    internal static double AlignedSideBaselineUp(Rendering.ScoreTextMetrics fonts,
        ImmutableArray<Voice> voices, Collector.PartCombineMark mark, double x,
        IReadOnlyDictionary<(int Staff, int Voice, int Measure, int Item),
            (BeamLayout Beam, double StemX, bool StemUp)>? beamMembers, int staffIndex)
    {
        double em = LabelEm(fonts);
        var style = LabelStyle(fonts);
        double advance = fonts.Advance(mark.Text, em, Rendering.TextRole.PartCombine, style);
        var (bottom, _) = fonts.Ink(mark.Text, em, Rendering.TextRole.PartCombine, style);
        var support = DynamicEngraver.ColumnSupportSkylines(
            voices, 0, mark.MeasureIndex, mark.ItemIndex, x,
            beamMembers is null ? null
                : vi => beamMembers.TryGetValue((staffIndex, vi, mark.MeasureIndex, mark.ItemIndex),
                    out var b) ? b : null);
        // :354-358 + :370 — the box bottom (baseline + bottom) stands padding over the supports.
        double totalOff = support.Up.MaxProtrusionInRange(x, x + advance) - bottom
            + CombineTextPadding;
        // :433-453 — the refpoint floor.
        return Math.Max(totalOff, DynamicEngraver.StaffExtent + CombineTextStaffPadding);
    }

    /// <summary>
    /// The label's skyline pair as LilyPond gives it: the extent BOX — [x, x + advance] ×
    /// [baseline + ink bottom, baseline + ink top] — in the caller's Y-up frame.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/grob.cc:81-85 Grob::simple_vertical_skylines_from_extents_proc — the default a grob
    /// that declares no vertical-skylines gets (CombineTextScript, scm/define-grobs.scm:1077-1105).</remarks>
    internal static (VerticalSkyline Up, VerticalSkyline Down) LabelBox(
        Rendering.ScoreTextMetrics fonts, string text, double em, Rendering.FontStyle style,
        double x, double baseline)
    {
        double advance = fonts.Advance(text, em, Rendering.TextRole.PartCombine, style);
        var (bottom, top) = fonts.Ink(text, em, Rendering.TextRole.PartCombine, style);
        return (VerticalSkyline.FromBox(x, x + advance, baseline + bottom, baseline + top,
                    VerticalDirection.Up),
                VerticalSkyline.FromBox(x, x + advance, baseline + bottom, baseline + top,
                    VerticalDirection.Down));
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
    /// The two terms are <c>OutsideStaffStacker.PlacePartCombineTexts</c>'s — the
    /// <see cref="AlignedSideBaselineUp"/> start and the extent box over the accumulated profile
    /// at outside-staff-padding — spelt here because this pass runs before the systems exist.
    /// The profile is the same box the stacker places (<see cref="LabelBox"/>).
    /// </remarks>
    internal static VerticalSkyline InkAboveStaff(
        Rendering.ScoreTextMetrics fonts,
        ImmutableArray<Collector.PartCombineMark> marks,
        ImmutableArray<Voice> voices,
        ImmutableArray<BeamLayout> beams,
        ImmutableArray<MeasureLayout> systemMeasureLayouts,
        VerticalSkyline accumulatedUp)
    {
        var ink = new VerticalSkyline(VerticalDirection.Up);
        if (marks.IsDefaultOrEmpty || systemMeasureLayouts.IsDefaultOrEmpty || voices.IsDefaultOrEmpty)
            return ink;
        var measures = voices[0].Measures;
        double em = LabelEm(fonts);
        var style = LabelStyle(fonts);
        // The staff's own beams, laid out on its trivial one-staff system: stamped staff 0
        // (MultiStaffLayouter.StaffBeamLayouts), so that is the key the members are read at.
        var beamMembers = DynamicEngraver.BuildBeamMembers(beams);
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
            double baseline = Math.Max(
                AlignedSideBaselineUp(fonts, voices, mark, x0, beamMembers, staffIndex: 0),
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

    /// <summary>CombineTextScript's padding — what aligned_side keeps between the label's box and
    /// its supports.</summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:1077-1094 CombineTextScript — (padding . 0.5), beside its outside-staff-priority 475.</remarks>
    internal const double CombineTextPadding = 0.5;
}
