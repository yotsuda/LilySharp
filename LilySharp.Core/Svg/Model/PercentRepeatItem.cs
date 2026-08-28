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

using LilySharp.Core.Semantics;

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
/// ★ The third branch is ported FOR BODIES SHORTER THAN A MEASURE only — the beat slash,
/// <see cref="BeatTiming"/>. For bodies of three or more whole measures it is deliberately
/// NOT PORTED HERE, and they keep the per-measure percent. That split is LilyPond's own:
/// both slash
/// grobs describe themselves as being "for repeating patterns shorter than a single measure"
/// (scm/define-grobs.scm, the RepeatSlash and DoubleRepeatSlash <c>description</c> fields),
/// and a whole-measure body walks out of the shape they were designed for.
/// ⚠️ WHAT LILYPOND ACTUALLY DRAWS THERE WAS MEASURED (2026-08-29, 2.26.0,
/// scratch/p282/wholebody*.ly): <c>\repeat percent 2 { c'1 d'1 e'1 f'1 }</c> engraves the
/// four written measures, then ONE bare slash in the fifth and THREE COMPLETELY EMPTY
/// MEASURES after it — the slash event has no measure-wise extent, so the repetition's
/// remaining bars receive nothing at all. Copying that would replace four percent signs with
/// three blank bars in 24 books of the corpus; the count is in HANDOFF §1.
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
    bool IsDouble = false,
    // The moment WITHIN <see cref="MeasureIndex"/> at which a BEAT slash stands, or null for
    // the two measure-wide signs (which centre on the measure, or on the bar line it opens).
    // A beat slash is what a body SHORTER THAN A MEASURE gets: LilyPond's iterator reports a
    // RepeatSlashEvent for it instead of the body, so the repetition occupies its own
    // duration with one grob and no notes.
    // LILYPOND-REF: lily/percent-repeat-iterator.cc:75-92 Percent_repeat_iterator::next_element.
    Fraction? BeatTiming = null,
    // The item slot the beat slash's spacer occupies in its measure, so the layout can read
    // the same X the notehead pass reads for a single-staff score (where per-item slots, not
    // timing columns, carry the positions). −1 for the measure-wide signs.
    int BeatItemIndex = -1,
    // LilyPond's `slash-count` property, and it selects the GROB as well as the picture:
    // 0 → DoubleRepeatSlash (two slashes AND dots, slope 1.0, slash-negative-kern 1.6),
    // N ≥ 1 → RepeatSlash (N slashes, NO dots, slope 1.7, slash-negative-kern 0.85).
    // Zero means the body's durations VARY, which is the only thing the two grobs are there
    // to tell apart.
    // LILYPOND-REF: scm/music-functions.scm:378-390 calc-repeat-slash-count — all durations
    //   equal → max (duration-log − 2) 1, else 0;
    // LILYPOND-REF: lily/slash-repeat-engraver.cc:56-66 process_music — count 0 makes a
    //   DoubleRepeatSlash and anything else a RepeatSlash;
    // LILYPOND-REF: lily/percent-repeat-interface.cc:107-121 beat_slash — count 0 draws
    //   x_percent (me, 2) and anything else brew_slash (me, count).
    // Meaningless unless <see cref="BeatTiming"/> is set.
    int SlashCount = 0
)
{
    /// <summary>
    /// True iff this sign is a BEAT slash — a body shorter than a measure — rather than one
    /// of the two measure-wide percent signs.
    /// </summary>
    public bool IsBeatSlash => BeatTiming is not null;

    /// <summary>
    /// The FIRST measure this sign replaces — <see cref="MeasureIndex"/> itself for a single
    /// sign, and the measure before it for a double, whose anchor is the second of the pair.
    /// Walk <c>FirstCoveredMeasure .. MeasureIndex</c> to visit every measure covered.
    /// </summary>
    /// <remarks>
    /// ⚠️ ONE HOME, because FOUR passes ask it and a pass that disagreed would print the
    /// repeated music underneath the sign: the notation notes and the tab fret digits
    /// (<c>SharedRenderer</c>'s two <c>percentCovered</c> sets), the multi-measure rest
    /// symbol (<c>MultiMeasureRestEngraver</c>), and the beams (<c>SharedRenderer.Beams</c>).
    /// ⚠️ THE FOURTH ONE WAS NOT ASKING, and the count said three for as long as that was
    /// true: <c>DrawBeams</c> built its own set from <c>MeasureIndex</c> alone, so a double
    /// percent's FIRST measure kept its beams and stems over hidden noteheads. Fixed and
    /// measured 2026-08-29 (scratch/p282/dbl.lys). Lily#'s unfold KEEPS the repeated music so
    /// it still plays; only the visual passes skip it, so each of them needs this answer.
    /// LilyPond never faces the question — its percent iterator reports an event instead of
    /// the body, so there is nothing to hide (lily/percent-repeat-iterator.cc:75-92
    /// Percent_repeat_iterator::next_element).
    /// <para>
    /// ⚠️ A BEAT SLASH COVERS NO MEASURE, and the range is empty by construction rather than
    /// by a flag each caller has to remember to test: a beat slash is the ONE case where
    /// Lily# does what LilyPond does and never emits the repeated body at all, so there is
    /// nothing left underneath to hide. Reporting <c>MeasureIndex</c> here instead would hide
    /// the whole measure the slash stands in — including the written beat the slash repeats.
    /// </para>
    /// </remarks>
    public int FirstCoveredMeasure =>
        IsBeatSlash ? MeasureIndex + 1
        : IsDouble ? MeasureIndex - 1
        : MeasureIndex;
}
