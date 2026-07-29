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
/// Custom text is placed at the end of measures, typically below the staff
/// for expression indications like "molto rit.", "a tempo", etc.
/// </remarks>
internal static class CustomTextEngraver
{
    // LILYPOND-REF: define-grobs.scm:3925 padding = 0.5
    private const double Padding = 0.5;

    // Below-staff custom-text baseline, Y-up from the staff middle: 5.5 below the
    // staff top is 3.5 below the middle (the staff top sits 2 above the middle).
    private const double BelowStaffBaselineYUp = 2.0 - 5.5;

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

            // Y position below the staff, in the Y-up frame (staff-spaces above the
            // top-staff middle). No staff offset — the draw resolves the (top) staff
            // middle.
            double yUp = BelowStaffBaselineYUp - Padding;

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