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
/// Layout result for a percent repeat symbol.
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/define-grobs.scm:2788-2807 - PercentRepeat grob
/// </remarks>
public readonly record struct PercentRepeatLayout(
    int MeasureIndex,
    double X,                // X center of the percent symbol (staff spaces)
    double YUp,              // Y-up (frame B): staff-spaces above the staff middle (0 = middle)
    double Width,            // Measure width for proportional sizing
    int SourcePosition,
    int SourceIndex = -1,    // F3/B: index into score.PercentRepeats (data-pos resolved at render)
    int StaffIndex = -1,      // owning staff, so the draw can resolve its staff middle
    bool IsDouble = false,    // two slashes on the bar line: a TWO-measure body's sign
    // A beat slash — a body shorter than a measure. ⚠️ ITS X IS THE LEFT EDGE of the group,
    // not the centre, because LilyPond's beat_slash callback (unlike double_percent) never
    // calls align_to (X_AXIS, CENTER): the stencil hangs off the rhythmic column it belongs
    // to. Measured on 2.26.0 (scratch/p282/slashprobe-lpsvg.svg): the two-slash group of
    // `\repeat percent 2 { c16 d e f }` has its leftmost slash origin at 27.1376, and the
    // beat's column — the four sixteenths sit 2.5042 apart from 17.1208 — is 27.1376.
    // LILYPOND-REF: lily/percent-repeat-interface.cc:107-121 beat_slash vs :96-101
    //   double_percent — only the latter re-aligns.
    bool IsBeatSlash = false,
    // LilyPond's slash-count, carried through: 0 draws the dotted DoubleRepeatSlash, N ≥ 1
    // draws N plain slashes. Meaningless unless IsBeatSlash.
    int SlashCount = 0
);

/// <summary>
/// Calculates layout positions for percent repeat symbols.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/percent-repeat-interface.cc - x_percent() rendering
/// LILYPOND-REF: scm/define-grobs.scm:2788-2807 - self-alignment-X = CENTER
///
/// The percent symbol is centered horizontally and vertically within the measure.
/// </remarks>
internal static class PercentRepeatEngraver
{
    /// <summary>
    /// Calculates percent repeat layouts from collected items.
    /// </summary>
    public static ImmutableArray<PercentRepeatLayout> Calculate(
        ImmutableArray<PercentRepeatItem> percentRepeats,
        ImmutableArray<SystemLayout> systems,
        ImmutableArray<MeasureLayout> measureLayouts)
    {
        if (percentRepeats.IsDefaultOrEmpty || systems.IsDefaultOrEmpty || measureLayouts.IsDefaultOrEmpty)
            return ImmutableArray<PercentRepeatLayout>.Empty;

        // The sign belongs to the staff the repeat was WRITTEN on (StaffIndex); a
        // cello percent must not print its ％ over the flute. Its own staff middle
        // is resolved at draw time.
        // LILYPOND-REF: lily/percent-repeat-engraver.cc — the engraver lives
        // in the Voice context of its own staff.
        var results = ImmutableArray.CreateBuilder<PercentRepeatLayout>(percentRepeats.Length);

        for (int i = 0; i < percentRepeats.Length; i++)
        {
            var item = percentRepeats[i];
            if (item.MeasureIndex >= measureLayouts.Length)
                continue;

            var ml = measureLayouts[item.MeasureIndex];

            // Center of the measure — or, for the DOUBLE sign, the BAR LINE this measure
            // opens on, because that bar is the one between the two measures the sign
            // stands for and LilyPond break-aligns the item to it.
            // LILYPOND-REF: scm/define-grobs.scm — the DoublePercentRepeat entry (:1290-1292):
            //   break-align-symbol = staff-bar. Range-less on purpose: one-word grob name.
            // LILYPOND-REF: lily/percent-repeat-interface.cc:96-101 double_percent — the
            //   stencil is align_to'd CENTER on X, so the bar line is its middle.
            // A BEAT slash stands at its own moment inside the measure, so it reads the same
            // X the notehead pass reads: the timing columns when a multi-staff score has
            // filled them, and the per-item slots otherwise. Matching that pass exactly is
            // what keeps the slash on the beat rather than near it — the two grids do not
            // agree, which is why SharedRenderer.Noteheads picks between them the same way.
            double x;
            if (item.IsBeatSlash)
            {
                x = !ml.Columns.IsDefaultOrEmpty && ml.Columns.Length > 0
                    ? ml.X + ml.GetXForTiming(item.BeatTiming!.Value)
                    : item.BeatItemIndex >= 0 && item.BeatItemIndex < ml.Items.Length
                        ? ml.X + ml.Items[item.BeatItemIndex].X
                        : ml.X;
            }
            else
            {
                x = item.IsDouble ? ml.X : ml.X + ml.Width / 2;
            }

            // Y-up (frame B): the percent sign is centred on the OWN staff's middle
            // line = 0 staff-spaces above the middle. The staff (and thus its device
            // middle) is resolved at draw time from StaffIndex.
            results.Add(new PercentRepeatLayout(
                item.MeasureIndex, x, 0.0, ml.Width, item.SourcePosition, i,
                StaffIndex: item.StaffIndex, IsDouble: item.IsDouble,
                IsBeatSlash: item.IsBeatSlash, SlashCount: item.SlashCount));
        }

        return results.ToImmutable();
    }
}
