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
/// Layout information for a custom text annotation.
/// All coordinates are in staff spaces.
/// </summary>
/// <remarks>
/// LILYPOND-REF: text-interface.cc Text rendering
/// LILYPOND-REF: define-grobs.scm:3800-3833 TextScript grob
/// </remarks>
public readonly record struct CustomTextLayout(
    int MeasureIndex,       // Measure containing this text
    double X,               // Absolute X position (staff spaces from score start)
    double YUp,             // Y in the LilyPond-native Y-up frame: staff-spaces ABOVE
                            // the staff middle line, up-positive (frame B). The draw
                            // reflects it to device (middle − Y-up).
    string Text,            // Display text
    int SourcePosition,     // For click-to-source mapping
    int SourceIndex = -1,   // F3/B: index into score.CustomTexts (data-pos resolved at render)
    int StaffIndex = -1      // owning staff (-1 = top staff); the draw resolves its middle
);

/// <summary>
/// Calculates positions for custom text annotations.
/// </summary>
/// <remarks>
/// LILYPOND-REF: text-interface.cc:36-89 Text positioning
/// LILYPOND-REF: side-position-interface.cc:92-111 axis_aligned_side_helper
///
/// A form-level <c>_"text"</c> engraves at the section boundary it stands at, as a
/// TextScript ABOVE the staff — the <c>^\markup</c> placement the ledger pair
/// textscript.no-descender.staff-to-baseline measures (2.05 + 0.5): its baseline starts at
/// aligned_side's staff-padding floor here and OutsideStaffStacker.PlaceCustomTexts then
/// clears the staff's accumulated ink at outside-staff-padding, in priority order (450).
/// </remarks>
internal static class CustomTextEngraver
{
    /// <summary>
    /// The baseline aligned_side gives a TextScript over a staff with no support under it,
    /// Y-up from the staff middle: the staff's ink edge (2.0 + half a line) plus
    /// staff-padding 0.5. A TextScript's <c>padding</c> 0.3 is spent against its supports —
    /// the note it hangs on — and this form-level text has none in the model (see the X
    /// bridge in <see cref="Calculate"/>), so the floor IS the answer; the stacker re-applies
    /// the same floor before its collision pass. Until session 567 the seed here was an
    /// invented "5.5 below the staff top less 0.5" (a padding cited to a TimeSignature line)
    /// that the stacker's floor always overrode — dead, and saying "below" (HANDOFF R10⒟).
    /// LILYPOND-REF: lily/side-position-interface.cc:401-453 aligned_side — staff_padding floors total_off at staff_extent[dir] + staff_padding
    /// LILYPOND-REF: scm/define-grobs.scm:3800-3833 TextScript — padding 0.3 against its side-position-interface supports, staff-padding 0.5, outside-staff-priority 450
    /// </summary>
    internal const double AlignedSideBaselineYUp =
        2.0 + EngravingDefaults.StaffLineThickness / 2.0 + EngravingDefaults.TextScriptStaffPadding;

    /// <summary>
    /// The text's em for THIS score: TextScript declares no font-size, so the paper's own
    /// text size (<see cref="EngravingDefaults.TextScriptFontSize"/>) — unless the score's
    /// <c>fonts { }</c> wrote a <c>step</c> or <c>size</c> for <c>text</c>. The draw, the
    /// outside-staff pass and the paging silhouette all read this one call.
    /// </summary>
    internal static double Em(Rendering.ScoreTextMetrics fonts)
        => fonts.Size(Rendering.TextRole.Text, EngravingDefaults.TextScriptFontSize);

    /// <summary>The text's weight and slant: italic (TextScript's font-shape) unless the
    /// score wrote a style for <c>text</c>.</summary>
    internal static Rendering.FontStyle Style(Rendering.ScoreTextMetrics fonts)
        => fonts.Style(Rendering.TextRole.Text, Rendering.FontStyle.Italic);

    /// <summary>
    /// Calculates layout for all custom text items in a score.
    /// </summary>
    public static ImmutableArray<CustomTextLayout> Calculate(
        ImmutableArray<CustomTextItem> customTexts,
        ImmutableArray<MeasureLayout> measureLayouts)
    {
        if (customTexts.IsDefaultOrEmpty)
            return ImmutableArray<CustomTextLayout>.Empty;

        var layouts = ImmutableArray.CreateBuilder<CustomTextLayout>(customTexts.Length);

        for (int ci = 0; ci < customTexts.Length; ci++)
        {
            var customText = customTexts[ci];
            // Find the measure layout
            if (customText.MeasureIndex >= measureLayouts.Length)
                continue;

            var measureLayout = measureLayouts[customText.MeasureIndex];

            // X = the measure's first note column origin, and the text's PEN ORIGIN sits
            // exactly on it (the draw is Start-anchored). LILYPOND-REF:
            // lily/self-alignment-interface.cc:143-175 aligned_on_parent — TextScript
            // declares self-alignment-X #f and parent-alignment-X #f, so NEITHER term
            // applies and the X-offset is zero: the stencil starts at its parent note
            // column's origin. MEASURED (audit/lp-geometry/probes/textscript-ink.ly,
            // NoteHead rows): the script's x-left equals the anchor head's left edge at
            // 21.650925710824165 to 15 digits, for every string (ledger
            // textscript.x.pen-to-notehead-left). The old "measure end - 1.0, centred"
            // was LILYSHARP-OWN and read +8.468502 on that entry.
            //
            // LILYSHARP-OWN, two declared bridges inside that rule (HANDOFF 5.2):
            // (1) The zero is aligned_on_parent EVALUATED, not computed: the inputs
            //     (self/parent-alignment-X) have no surface in Lily#'s model, so the
            //     general formula has nothing to read — the "model addition first"
            //     shape (like staff-grouper/magnification), not a folded live input.
            //     If an alignment override ever enters the grammar, port the formula.
            // (2) WHICH note is the parent: LilyPond's TextScript attaches to a real
            //     note; Lily#'s _"text" is a section-boundary directive with no note in
            //     its model, so "the measure's first column" (Items[0] / Columns[0],
            //     measure start when empty) is this engraver's own bridge — chosen to
            //     mirror the fidelity pair's construction, not read from LP source.
            double x = measureLayout.X
                + (!measureLayout.Items.IsDefaultOrEmpty ? measureLayout.Items[0].X
                    : !measureLayout.Columns.IsDefaultOrEmpty ? measureLayout.Columns[0].X
                    : 0.0);

            // Y: aligned_side's floor above the staff, in the Y-up frame (staff-spaces above
            // the staff middle). No staff offset — the draw resolves the staff middle.
            double yUp = AlignedSideBaselineYUp;

            layouts.Add(new CustomTextLayout(
                customText.MeasureIndex,
                x,
                yUp,
                customText.Text,
                customText.SourcePosition,
                ci
            ));
        }

        return layouts.ToImmutable();
    }
}