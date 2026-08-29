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

namespace LilySharp.Core.Semantics;

/// <summary>
/// Which repeat sign a <c>repeat percent</c> body earns, chosen — as LilyPond chooses it —
/// from the body's LENGTH against the measure length, once, for the whole repeat.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/percent-repeat-iterator.cc:75-92 Percent_repeat_iterator::next_element —
///   the test is <c>body_length_.main_part_ == mlen</c> / <c>== mlen * 2</c> against the
///   CONTEXT's measure_length, and everything else falls to its <c>else</c>.
/// </remarks>
internal enum PercentBodyShape
{
    /// <summary>No meter to measure against, or a body of no length: no sign at all.</summary>
    None,

    /// <summary>One measure — the single percent sign, centred in the repeated measure.</summary>
    Single,

    /// <summary>Two measures — the double percent, straddling the bar line between them.</summary>
    Double,

    /// <summary>
    /// Everything else — LilyPond's <c>else</c>, which is ONE branch and not three.
    /// The repetition is a single RepeatSlashEvent carrying the WHOLE body's length, so the
    /// page gets one slash group where the repetition starts and empty measures for the rest
    /// of it, whether the body is a beat, three measures or eight.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/percent-repeat-iterator.cc:86-99 next_element — the two tests are
    ///   <c>== mlen</c> and <c>== mlen * 2</c>, and the else emits RepeatSlashEvent with
    ///   <c>slash-count</c> from calc-repeat-slash-count and <c>length</c> = the body's.
    /// LILYPOND-REF: scm/music-functions.scm:377-389 calc-repeat-slash-count — equal written
    ///   durations give <c>max(log - 2, 1)</c>, so a body of whole notes gives 1; unequal
    ///   durations give 0, which lily/slash-repeat-engraver.cc:57-65 turns into the
    ///   DoubleRepeatSlash grob instead of RepeatSlash.
    /// ⚠️ THIS USED TO BE THREE MEMBERS. Sessions 282-285 read the else as three cases and
    /// ported only the sub-measure one, calling the rest a declared approximation and warning
    /// about it (LYS2014). Reading the iterator settles it: there is no third test, the whole
    /// else is one event, and the "3 or more whole measures" case was never LilyPond's — it
    /// was a subdivision Lily# invented and then documented as a deviation. Measured on
    /// 2.26.0 (scratch/p282/wholebody3.ly, wholebody8.ly): bodies of 3 and of 8 whole measures
    /// both draw ONE slash in the repetition's first measure and leave every later measure of
    /// it blank, which is what this branch now produces.
    /// </remarks>
    RepeatSlash,
}

/// <summary>
/// The one home for "which sign does this body earn". The collector asks in order to emit the
/// sign; nobody else needs to, now that the shape no longer decides whether to warn.
/// </summary>
/// <remarks>
/// ⚠️ THIS USED TO HAVE A SECOND CALLER. MeasureValidator asked the same question in order to
/// warn (LYS2014) about the "three or more whole measures" shape, and the two measured the
/// body's length separately — the collector off MeasureBuilder, the validator by walking the
/// body — with a corpus census as the only thing holding them together. Both the shape and the
/// warning are gone: LilyPond has one else, not three cases, so there is nothing left for a
/// second reader to disagree about.
/// </remarks>
internal static class PercentRepeatShape
{
    /// <summary>Classifies a body by its played length against the measure length.</summary>
    /// <remarks>
    /// LILYPOND-REF: lily/percent-repeat-iterator.cc:86-99 Percent_repeat_iterator::next_element
    ///   — <c>body_length_.main_part_ == mlen</c>, then <c>== mlen * 2</c>, then the else. The
    ///   two equalities are the whole rule; a body's length is never compared any other way.
    /// </remarks>
    public static PercentBodyShape Classify(Fraction bodyLength, Fraction measureLength)
    {
        if (measureLength <= Fraction.Zero || bodyLength <= Fraction.Zero)
            return PercentBodyShape.None;
        if (bodyLength == measureLength)
            return PercentBodyShape.Single;
        if (bodyLength == measureLength + measureLength)
            return PercentBodyShape.Double;
        return PercentBodyShape.RepeatSlash;
    }
}
