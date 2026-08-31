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
/// ★ ALL THREE BRANCHES ARE PORTED. The third is the beat slash, <see cref="BeatTiming"/>,
/// and it is not only for bodies shorter than a measure: LilyPond's else has no length test
/// in it, so a body of three or of eight whole measures reaches the same RepeatSlashEvent,
/// which carries the WHOLE body's length. Measured (2026-08-29, 2.26.0,
/// scratch/p282/wholebody3.ly and wholebody8.ly): the repetition draws ONE slash in its first
/// measure and leaves every later measure of it completely empty.
/// ⚠️ THE GROB DESCRIPTIONS ARE NOT THE RULE. RepeatSlash and DoubleRepeatSlash both call
/// themselves grobs "for repeating patterns shorter than a single measure"
/// (scm/define-grobs.scm), and sessions 282-285 read that as a fourth case and kept a
/// per-measure percent for whole-measure bodies, with LYS2014 warning that the picture could
/// not say what the music was. The iterator has no such case; the description is a summary of
/// the common use, not of the branch. The invented picture and the warning are both retired
/// (session 286) — a reader reported the warning as wrong and was right.
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
    // Identity, not value equality: see ModelIdentity.
    public bool Equals(PercentRepeatItem? other) => ReferenceEquals(this, other);

    /// <inheritdoc/>
    public override int GetHashCode() => ModelIdentity.HashOf(this);

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
