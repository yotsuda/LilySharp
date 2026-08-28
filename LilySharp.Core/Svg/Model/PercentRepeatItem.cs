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

namespace LilySharp.Core.Svg.Model;

/// <summary>
/// A percent repeat sign marking a measure as a repetition of the previous measure.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/percent-repeat-engraver.cc - PercentRepeat grob
/// LILYPOND-REF: lily/percent-repeat-interface.cc - visual rendering (slash + dots)
/// LILYPOND-REF: scm/define-grobs.scm:2520-2539 - PercentRepeat properties
///
/// A single percent sign (%) repeats the previous measure.
/// A double percent sign (%%) repeats the previous 2 measures.
///
/// In LilySharp, percent repeats are generated from:
/// - `repeat percent N { body }` syntax (automatic)
/// - Body is unfolded N times, iterations 2+ marked as percent repeats
/// </remarks>
/// <remarks>
/// ⚠️ WHICH SIGN A REPETITION GETS IS THE BODY'S LENGTH, and LilyPond decides it once, on
/// the SECOND iteration, for the whole repeat:
/// LILYPOND-REF: lily/percent-repeat-iterator.cc:75-92 Percent_repeat_iterator::next_element —
///   body == one measure → PercentEvent, body == two measures → DoublePercentEvent, anything
///   else → RepeatSlashEvent. One event per repetition, so `\repeat percent 4` of a two-bar
///   body reports THREE DoublePercentEvents, not six percents.
/// ⚠️ The third branch (a body that is neither one nor two measures) is NOT PORTED: Lily#
/// still marks every measure of such a repetition with a single percent where LilyPond would
/// draw beat slashes. Measured 2026-08-28 only as "Lily# does something else here", not
/// against a LilyPond picture.
/// </remarks>
public sealed record PercentRepeatItem(
    // Measure index where the percent sign appears. For a DOUBLE sign this is the SECOND
    // measure of the pair, because LilyPond's item is made at that measure's downbeat and
    // break-aligns to the bar line there — the sign straddles the bar between the two.
    // LILYPOND-REF: lily/double-percent-repeat-engraver.cc:56-64 process_music — the item is
    //   made when now_mom() reaches start_mom_ = the event's moment plus measure_length.
    int MeasureIndex,
    // Source position for click-to-source mapping.
    int SourcePosition,
    // The staff the repeat was written on — the sign prints there,
    // like LilyPond's Percent_repeat_engraver living in that Voice.
    int StaffIndex = 0,
    // A DOUBLE percent (two slashes on the bar line) standing for a TWO-measure body,
    // rather than the single sign that stands for a one-measure body.
    // LILYPOND-REF: scm/define-grobs.scm — the DoublePercentRepeat entry (:1290-1309):
    //   break-align-symbol = staff-bar, non-musical = #t, stencil = double-percent. The
    //   address carries no range because the grob name is one word and a ranged citation
    //   with no compound name counts as unnamed (HANDOFF §5.2.1⑦).
    bool IsDouble = false
)
{
    /// <summary>
    /// The FIRST measure this sign replaces — <see cref="MeasureIndex"/> itself for a single
    /// sign, and the measure before it for a double, whose anchor is the second of the pair.
    /// Walk <c>FirstCoveredMeasure .. MeasureIndex</c> to visit every measure covered.
    /// </summary>
    /// <remarks>
    /// ⚠️ ONE HOME, because THREE passes ask it and a pass that disagreed would print the
    /// repeated music underneath the sign: the notation notes and the tab fret digits
    /// (<c>SharedRenderer</c>'s two <c>percentCovered</c> sets) and the multi-measure rest
    /// symbol (<c>MultiMeasureRestEngraver</c>). Lily#'s unfold KEEPS the repeated music so
    /// it still plays; only the visual passes skip it, so each of them needs this answer.
    /// LilyPond never faces the question — its percent iterator reports an event instead of
    /// the body, so there is nothing to hide (lily/percent-repeat-iterator.cc:75-92
    /// Percent_repeat_iterator::next_element).
    /// </remarks>
    public int FirstCoveredMeasure => IsDouble ? MeasureIndex - 1 : MeasureIndex;
}
